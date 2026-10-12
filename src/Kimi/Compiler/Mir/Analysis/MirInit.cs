// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns initialization and moves (A1; SPEC 15.1, 14.10.3; DECISIONS R4-M21) per body and Semantics case view.
// Input: a validated body, its move paths (MirMoveData), its dataflow (MirDataflow) and the selected view (MirCases). Output: the
// problems UninitializedPlace_Kd, MovedPlace_Kd, ReassignedLet_Kd and StaticMovePathRequired_Kd through MirReport, and the converged states
// left in MirDataflow for A2.
// Three lanes per path, joined by union: MaybeUninit, MaybeMoved, EverInit. A definition initializes a path's subtree, a Move or a Drop
// uninitializes it, StorageLive and StorageDead start a fresh binding lifetime; a use needs its whole subtree initialized on every incoming
// path, its own bit being the construction fact. Every root is tracked (A2 reads temporaries' states), but the state rules report only the
// Places of Var, Arg and Return roots: a compiler temporary is never a Place of the program. A whole use of the return local is a delivery,
// checked only where the runtime edges reach it; a Return checks nothing. A Move without a Move Path is reported whatever its root. A body
// no view decides is analyzed once, for every view.
// Must not judge Take eligibility (Binding's), apply a derived-problem rule (MirReport's) or copy the body per view.

namespace Kimi.Compiler.Mir;

/// <summary>Initialization and moves (A1, SPEC 15.1): <see cref="Run"/> analyzes a body under the selected view.</summary>
/// <param name="flow">The dataflow, begun with the body.</param>
/// <param name="moves">The move paths, built for the body.</param>
/// <param name="cases">The Semantics case views, a view of the body selected.</param>
internal sealed class MirInit(MirDataflow flow, MirMoveData moves, MirCases cases)
{
    private const int Lanes = 3;
    private const int Uninit = 0; // The lanes, each set and cleared by its bit 1 << lane: MaybeUninit,
    private const int Moved = 1; // MaybeMoved,
    private const int Ever = 2; // EverInit.

    /// <summary>Analyzes a body under the selected view; view 0 of a body no view decides analyzes it for every view.</summary>
    /// <param name="body">The body.</param>
    /// <param name="view">The selected view.</param>
    /// <param name="report">The problems of the program version.</param>
    internal void Run(MirBody body, long view, MirReport report)
    {
        if (view > 0 && !moves.ViewDependent)
        {
            return;
        }

        var views = moves.ViewDependent ? 1UL << (int)view : ulong.MaxValue >> (64 - (int)MirCases.Count(body));
        var state = flow.Prepare(Lanes * ((moves.FlowCount + 63) >> 6), Lanes * ((moves.Paths.Length + 63) >> 6));
        if (state is null)
        {
            report.Add(new(DiagnosticCode.OwnershipStorageLimit_Kd, body.Source, views)); // R4-M15a: the body is not analyzed.
            return;
        }

        var transfer = new Transfer(body, moves, cases, flow, state) { Report = report, Views = views };
        flow.Forward(ref transfer);
        transfer.Checks = true;
        flow.Replay(ref transfer);
    }

    // The lanes of A1 over the move paths: the entry, each block's statements and terminator, and in Replay the checks.
    private struct Transfer(MirBody body, MirMoveData moves, MirCases cases, MirDataflow flow, ulong[] state) : IMirFlow, IMirUses
    {
        private SourceRef subject;
        private bool runtime; // Whether the runtime edges reach the block applied.

        internal required MirReport Report { get; init; }

        internal ulong Views { get; init; }

        internal bool Checks { get; set; }

        // Every path uninitialized; the arguments initialized, and a constructor's object storage, whose Fields are not (SPEC 6.2.3).
        public readonly void Entry()
        {
            var paths = moves.Paths;
            MirDataflow.Apply(state, Lanes, 0, paths.Length, set: 1 << Uninit, clear: (1 << Moved) | (1 << Ever));
            for (var local = 1; local < body.Locals.Length && body.Locals[local].Kind == MirLocalKind.Arg; local++)
            {
                var path = moves.Find(new(local), out _);
                MirDataflow.Apply(state, Lanes, path, paths[path].End, set: 1 << Ever, clear: 1 << Uninit);
            }

            if (moves.Constructing)
            {
                var path = moves.Find(new(0), out _);
                MirDataflow.Apply(state, Lanes, path, path + 1, set: 0, clear: 1 << Uninit);
            }
        }

        // StorageLive and StorageDead start a fresh binding lifetime; EndBorrow ends Loans only; a Return delivers what the exit requests
        // before it checked (SPEC 6.2.3.5), so it checks nothing.
        public void Block(int block)
        {
            ref readonly var data = ref body.Blocks[block];
            this.runtime = flow.Runtime(block);
            for (var i = 0; i < data.Count; i++)
            {
                ref readonly var statement = ref body.Statements[data.Start + i];
                this.subject = body.Subject(statement.Source);
                if (statement.Kind is MirStatementKind.StorageLive or MirStatementKind.StorageDead)
                {
                    var path = moves.Find(statement.Place, out _);
                    MirDataflow.Apply(state, Lanes, path, moves.Paths[path].End, set: 1 << Uninit, clear: (1 << Moved) | (1 << Ever));
                }
                else if (statement.Kind != MirStatementKind.EndBorrow)
                {
                    MirUses.ForEach(cases, new(block, i), ref this);
                }
            }

            if (data.Terminator.Kind != MirTerminatorKind.Return)
            {
                this.subject = body.Subject(data.Terminator.Source);
                MirUses.ForEach(cases, new(block, data.Count), ref this);
            }
        }

        public readonly void Operand(in MirOperand operand)
        {
        }

        public readonly void Effect(MirEffect effects, int site)
        {
        }

        // A use of a Place under the view. A whole use of the return local delivers it, which only the runtime edges can (SPEC 14.10.3). A
        // PairLayer a case-dependent access takes is the Place itself in the owner case and a Deref in a reference case (SPEC 13.5.5.1),
        // so the outermost such reference ends the path; through a Deref or an Index only the prefix's path is used, and a Move there has
        // no Move Path (SPEC 15.1.3, 15.1.5).
        public readonly void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            if (place.IsStatic || (place == new MirPlace(0) && (access & MirAccess.Defines) == 0 && !this.runtime))
            {
                return;
            }

            var used = (access & MirAccess.CaseDependent) != 0 ? this.Layer(place) : place;
            var path = moves.Find(used, out var how);
            how = used == place ? how : MirMoveData.NoPath;
            var paths = moves.Paths;
            if (how != MirMoveData.Direct)
            {
                this.Check(path);
                if (this.Checks && how == MirMoveData.NoPath && (access & MirAccess.Moves) != 0)
                {
                    this.Add(DiagnosticCode.StaticMovePathRequired_Kd);
                }
            }
            else if ((access & MirAccess.Defines) != 0)
            {
                this.Define(path);
            }
            else if ((access & MirAccess.Moves) != 0)
            {
                this.Check(path);
                MirDataflow.Apply(state, Lanes, path, paths[path].End, set: (1 << Uninit) | (1 << Moved), clear: 0);
            }
            else if ((access & MirAccess.DropUse) != 0)
            {
                MirDataflow.Apply(state, Lanes, path, paths[path].End, set: 1 << Uninit, clear: 0);
            }
            else
            {
                this.Check(path);
            }
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
        // binding lifetime (SPEC 15.1.1, 11.3.1), then the subtree initialized.
        private readonly void Define(int path)
        {
            var paths = moves.Paths;
            var checks = this.Checks && this.Reported(path);
            for (var above = paths[path].Parent; checks && above >= 0; above = paths[above].Parent)
            {
                if (MirDataflow.Any(state, Lanes, Uninit, above, above + 1))
                {
                    this.Add(MirDataflow.Any(state, Lanes, Moved, above, above + 1) ? DiagnosticCode.MovedPlace_Kd : DiagnosticCode.UninitializedPlace_Kd);
                    break;
                }
            }

            if (checks && paths[path].Once && MirDataflow.Any(state, Lanes, Ever, path, paths[path].End))
            {
                this.Add(DiagnosticCode.ReassignedLet_Kd);
            }

            MirDataflow.Apply(state, Lanes, path, paths[path].End, set: 1 << Ever, clear: (1 << Uninit) | (1 << Moved));
        }

        // A use needs the whole subtree initialized, the construction fact included (SPEC 15.1.2): Moved where it may have been moved.
        private readonly void Check(int path)
        {
            var end = moves.Paths[path].End;
            if (this.Checks && this.Reported(path) && MirDataflow.Any(state, Lanes, Uninit, path, end))
            {
                this.Add(MirDataflow.Any(state, Lanes, Moved, path, end) ? DiagnosticCode.MovedPlace_Kd : DiagnosticCode.UninitializedPlace_Kd);
            }
        }

        // Whether the state rules report on a path: its root is a Place of the program, not a compiler temporary.
        private readonly bool Reported(int path) => body.Locals[moves.Paths[path].Local].Kind is MirLocalKind.Var or MirLocalKind.Arg or MirLocalKind.Return;

        private readonly void Add(DiagnosticCode code) => this.Report.Add(new(code, this.subject, this.Views));
    }
}
