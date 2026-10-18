// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns initialization and moves (A1; SPEC 15.1, 14.10.3; DECISIONS R4-M21, R4-M6a) per body and Semantics case view.
// Input: a validated body, its move paths (MirMoveData), its dataflow (MirDataflow) and the selected view (MirCases). Output: the
// problems UninitializedPlace_Kd, MovedPlace_Kd, ReassignedLet_Kd and StaticMovePathRequired_Kd through MirReport, and to A2 (MirDrops) the
// paths its entry initializes (the arguments'; a constructing _0's own bit has no subtree event) and, in its checks replay, the state before
// each Drop the runtime edges reach and each state event there.
// Five lanes per path, joined by union: MaybeUninit, MaybeMoved, EverInit, and for A2 MaybeInit and MaybePartial (a non-root path a Move or
// a Drop took out of its parent, cleared with the subtree by any event of an ancestor-or-self). A definition initializes a path's subtree, a
// Move or a Drop uninitializes it, StorageLive and StorageDead start a fresh binding lifetime; a use needs its whole subtree initialized on
// every incoming path, its own bit being the construction fact; a use through a runtime Index of storage reports on its receiver under
// the selection's subject, its position local's, as the selection's guard, a use where the receiver is located, does (the builder's
// contract, DECISIONS R4-M56), so the two are one problem unless the receiver changed in between. Every root is tracked (A2 reads
// temporaries' states), but the state rules
// report only the Places of Var, Arg and Return roots: a compiler temporary is never a Place of the program. A whole use of the return local
// is a delivery, checked only where the runtime edges reach it; a Return checks nothing. A Move without a Move Path is reported whatever its
// root. A body no view decides is analyzed once, for every view.
// Must not judge Take eligibility (Binding's), decide a Drop (A2's), apply a derived-problem rule (MirReport's) or copy the body per view.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

/// <summary>Initialization and moves (A1, SPEC 15.1): <see cref="Run"/> analyzes a body under the selected view.</summary>
/// <param name="flow">The dataflow, begun with the body.</param>
/// <param name="moves">The move paths, built for the body.</param>
/// <param name="cases">The Semantics case views, a view of the body selected.</param>
/// <param name="drops">Drop elaboration (A2), begun with the body, which the checks replay feeds.</param>
internal sealed class MirInit(MirDataflow flow, MirMoveData moves, MirCases cases, MirDrops drops)
{
    /// <summary>The lanes per path, each set and cleared by its bit 1 &lt;&lt; lane.</summary>
    internal const int Lanes = 5;

    /// <summary>MaybeUninit.</summary>
    internal const int Uninit = 0;

    /// <summary>MaybeInit, which A2 reads.</summary>
    internal const int Init = 3;

    /// <summary>MaybePartial, which A2 reads: the path was taken out of its parent, whose own bit stayed.</summary>
    internal const int Partial = 4;

    private const int Moved = 1; // MaybeMoved.
    private const int Ever = 2; // EverInit.
    private const int Fresh = (1 << Moved) | (1 << Ever) | (1 << Init) | (1 << Partial); // Cleared where a binding lifetime starts.

    /// <summary>Analyzes a body under the selected view; view 0 of a body no view decides analyzes it for every view.</summary>
    /// <param name="body">The body.</param>
    /// <param name="view">The selected view.</param>
    /// <param name="report">The problems of the program version.</param>
    /// <returns>False when the body's state arena exceeds the analyses' limit (R4-M15a), so that no view of it is analyzed.</returns>
    internal bool Run(MirBody body, long view, MirReport report)
    {
        if (view > 0 && !moves.ViewDependent)
        {
            return true;
        }

        var all = ulong.MaxValue >> (64 - (int)MirCases.Count(body));
        var state = flow.Prepare(0, Lanes * ((moves.FlowCount + 63) >> 6), Lanes * ((moves.Paths.Length + 63) >> 6));
        if (state is null)
        {
            var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.StorageLimit);
            report.Add(new(DiagnosticCode.OwnershipStorageLimit_Kd, requirement, body.Source, all)); // R4-M15a: the driver stops the body's views.
            return false;
        }

        var transfer = new Transfer(body, moves, cases, flow, state, drops) { Report = report, Views = moves.ViewDependent ? 1UL << (int)view : all };
        flow.Forward(ref transfer);
        transfer.Checks = true;
        flow.Replay(ref transfer);
        return true;
    }

    // The lanes of A1 over the move paths: the entry, each block's statements and terminator, and in Replay the checks and A2's input.
    private struct Transfer(MirBody body, MirMoveData moves, MirCases cases, MirDataflow flow, ulong[] state, MirDrops drops) : IMirFlow, IMirUses
    {
        private SourceRef subject;
        private MirLocation at;
        private bool runtime; // Whether the runtime edges reach the block applied.

        internal required MirReport Report { get; init; }

        internal ulong Views { get; init; }

        internal bool Checks { get; set; }

        // Every path uninitialized; the arguments initialized, which A2 sets the flags of at the entry, and a constructor's object storage,
        // whose Fields are not (SPEC 6.3).
        public readonly void Entry()
        {
            var paths = moves.Paths;
            MirDataflow.Apply(state, Lanes, 0, paths.Length, set: 1 << Uninit, clear: Fresh);
            for (var local = 1; local < body.Locals.Length && body.Locals[local].Kind == MirLocalKind.Arg; local++)
            {
                var path = moves.Find(new(local), out _);
                MirDataflow.Apply(state, Lanes, path, paths[path].End, set: (1 << Ever) | (1 << Init), clear: 1 << Uninit);
                drops.Event(new(0, -1), path, set: true, this.Views);
            }

            if (moves.Constructing)
            {
                var path = moves.Find(new(0), out _);
                MirDataflow.Apply(state, Lanes, path, path + 1, set: 1 << Init, clear: 1 << Uninit);
            }
        }

        // StorageLive and StorageDead start a fresh binding lifetime; EndBorrow ends Loans only; a Return delivers what the exit requests
        // before it checked (SPEC 6.3.5), so it checks nothing.
        public void Block(int block)
        {
            ref readonly var data = ref body.Blocks[block];
            this.runtime = flow.Runtime(block);
            for (var i = 0; i < data.Count; i++)
            {
                ref readonly var statement = ref body.Statements[data.Start + i];
                (this.subject, this.at) = (body.Subject(statement.Source), new(block, i));
                if (statement.Kind is MirStatementKind.StorageLive or MirStatementKind.StorageDead)
                {
                    var path = moves.Find(statement.Place, out _);
                    MirDataflow.Apply(state, Lanes, path, moves.Paths[path].End, set: 1 << Uninit, clear: Fresh);
                    this.Event(path, set: false);
                }
                else if (statement.Kind != MirStatementKind.EndBorrow)
                {
                    MirUses.ForEach(cases, this.at, ref this);
                }
            }

            if (data.Terminator.Kind != MirTerminatorKind.Return)
            {
                (this.subject, this.at) = (body.Subject(data.Terminator.Source), new(block, data.Count));
                MirUses.ForEach(cases, this.at, ref this);
            }
        }

        // The union join (R4-M34).
        public readonly bool Join(Span<ulong> target, ReadOnlySpan<ulong> incoming) => MirDataflow.Union(target, incoming);

        public readonly void Operand(in MirOperand operand)
        {
        }

        public readonly void Effect(MirEffect effects, int site)
        {
        }

        // A use of a Place under the view. A whole use of the return local delivers it, which only the runtime edges can (SPEC 14.10.3). A
        // PairLayer a case-dependent access takes is the Place itself in the owner case and a Deref in a reference case (SPEC 13.5.5),
        // so the outermost such reference ends the path; through a Deref or an Index only the prefix's path is used, and a Move there has
        // no Move Path (SPEC 15.1.3, 15.1.5). A2 judges a Drop the runtime edges reach from the state before it, by its path (-1: none).
        public readonly void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            var used = (access & MirAccess.CaseDependent) != 0 ? this.Layer(place) : place;
            var how = MirMoveData.NoPath;
            var path = place.IsStatic ? -1 : moves.Find(used, out how);
            how = used == place ? how : MirMoveData.NoPath;
            if ((access & MirAccess.DropUse) != 0 && this.Checks && this.runtime)
            {
                drops.Record(state, this.at.Block, how == MirMoveData.Direct ? path : -1);
            }

            if (place.IsStatic || (place == new MirPlace(0) && (access & MirAccess.Defines) == 0 && !this.runtime))
            {
                return;
            }

            if (how != MirMoveData.Direct)
            {
                this.Check(path, this.Selection(place));
                if (this.Checks && how == MirMoveData.NoPath && (access & MirAccess.Moves) != 0)
                {
                    this.Add(DiagnosticCode.StaticMovePathRequired_Kd, OwnershipRequirement.StaticMovePathRequired);
                }
            }
            else if ((access & MirAccess.Defines) != 0)
            {
                this.Define(path);
            }
            else if ((access & MirAccess.Moves) != 0)
            {
                this.Check(path, this.subject);
                this.Take(path, (1 << Uninit) | (1 << Moved));
            }
            else if ((access & MirAccess.DropUse) != 0)
            {
                this.Take(path, 1 << Uninit);
            }
            else
            {
                this.Check(path, this.subject);
            }
        }

        // The subject a use below its path reports under: through a runtime Index of storage, the innermost one's selection, which its
        // position local carries and its guard reports under (R4-M56); else the location's.
        private readonly SourceRef Selection(MirPlace place)
        {
            var projections = body.Projections;
            var subject = this.subject;
            for (var id = place.Projection; id > 0; id = projections[id].Prefix)
            {
                var receiver = place with { Projection = projections[id].Prefix };
                if (projections[id].Kind == MirProjectionKind.Index && !MirTypes.SelectsByValue(body.TypeOf(receiver)))
                {
                    subject = body.Subject(body.Locals[projections[id].Arg].Source);
                }
            }

            return subject;
        }

        // The Place up to its outermost PairLayer step the view makes a reference, or the Place itself.
        private readonly MirPlace Layer(MirPlace place)
        {
            var projections = body.Projections;
            var layer = place;
            for (var id = place.Projection; id > 0; id = projections[id].Prefix)
            {
                if (projections[id].Kind == MirProjectionKind.PairLayer && cases.Layer(place with { Projection = id }) is not null)
                {
                    layer = place with { Projection = projections[id].Prefix };
                }
            }

            return layer;
        }

        // A definition: every strict ancestor constructed (its own bit initialized), a path admitting one placement initialized once per
        // binding lifetime (SPEC 15.1.1, 11.7), then the subtree initialized.
        private readonly void Define(int path)
        {
            var paths = moves.Paths;
            var checks = this.Checks && this.Reported(path);
            for (var above = paths[path].Parent; checks && above >= 0; above = paths[above].Parent)
            {
                if (MirDataflow.Any(state, Lanes, Uninit, above, above + 1))
                {
                    this.Uninitialized(MirDataflow.Any(state, Lanes, Moved, above, above + 1), this.subject);
                    break;
                }
            }

            if (checks && paths[path].Once && MirDataflow.Any(state, Lanes, Ever, path, paths[path].End))
            {
                this.Add(DiagnosticCode.ReassignedLet_Kd, OwnershipRequirement.ReassignedLet);
            }

            MirDataflow.Apply(state, Lanes, path, paths[path].End, set: (1 << Ever) | (1 << Init), clear: (1 << Uninit) | (1 << Moved) | (1 << Partial));
            this.Event(path, set: true);
        }

        // A Move or a Drop takes the subtree out, uninitialized; a part taken out of its parent leaves the parent's own bit, so it is partial.
        private readonly void Take(int path, int set)
        {
            var paths = moves.Paths;
            MirDataflow.Apply(state, Lanes, path, paths[path].End, set, clear: (1 << Init) | (1 << Partial));
            if (paths[path].Parent >= 0)
            {
                MirDataflow.Apply(state, Lanes, path, path + 1, set: 1 << Partial, clear: 0);
            }

            this.Event(path, set: false);
        }

        // A state event the runtime edges reach, which sets or clears the drop flags of the path's subtree (A2).
        private readonly void Event(int path, bool set)
        {
            if (this.Checks && this.runtime)
            {
                drops.Event(this.at, path, set, this.Views);
            }
        }

        // A use needs the whole subtree initialized, the construction fact included (SPEC 15.1.2): Moved where it may have been moved.
        private readonly void Check(int path, SourceRef subject)
        {
            var end = moves.Paths[path].End;
            if (this.Checks && this.Reported(path) && MirDataflow.Any(state, Lanes, Uninit, path, end))
            {
                this.Uninitialized(MirDataflow.Any(state, Lanes, Moved, path, end), subject);
            }
        }

        // Whether the state rules report on a path: its root is a Place of the program, not a compiler temporary.
        private readonly bool Reported(int path) => body.Locals[moves.Paths[path].Local].Kind is MirLocalKind.Var or MirLocalKind.Arg or MirLocalKind.Return;

        private readonly void Add(DiagnosticCode code, OwnershipRequirement requirement)
            => this.Report.Add(new(code, DiagnosticRequirement.Ownership(requirement), this.subject, this.Views));

        // A use of a Place some incoming path leaves uninitialized: MovedPlace_Kd where it may have been moved, else UninitializedPlace_Kd.
        private readonly void Uninitialized(bool moved, SourceRef subject) => this.Report.Add(moved
            ? new(DiagnosticCode.MovedPlace_Kd, DiagnosticRequirement.Ownership(OwnershipRequirement.PossiblyMovedUse), subject, this.Views)
            : new(DiagnosticCode.UninitializedPlace_Kd, DiagnosticRequirement.Ownership(OwnershipRequirement.UninitializedUse), subject, this.Views));
    }
}
