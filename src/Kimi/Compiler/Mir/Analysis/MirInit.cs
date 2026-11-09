// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns initialization and moves (A1; SPEC 15.1, 14.10.3, 7.2.3; DECISIONS R4-M21, R4-M6a, R4-M69) per body and Semantics case view.
// Input: a validated body, its move paths (MirMoveData), its dataflow (MirDataflow) and the selected view (MirCases). Output: the
// problems UninitializedPlace_Kd, MovedPlace_Kd, ReassignedLet_Kd, StaticMovePathRequired_Kd and DefaultArgumentMove_Kd through MirReport,
// each inside an inline default copy with the copy's call as its Inlined fact, and the arena limit through MirDataflow.StorageLimit; to
// A2 (MirDrops) the code, subject (in the location's scope) and MirReport.Add's index of each problem it records (R4-M102, R4-M132), the
// paths its entry initializes (the arguments'; a constructing _0's own bit has no subtree event) and, in its checks replay, the state
// before each Drop, with whether the runtime edges reach it, and each state event the runtime edges reach.
// Five lanes per path, joined by union: MaybeUninit, MaybeMoved, EverInit, and for A2 MaybeInit and MaybePartial (a non-root path a Move or
// a Drop took out of its parent, cleared with the subtree by any event of an ancestor-or-self). A definition initializes a path's subtree
// but its Never parts and what a temp it takes whole left uninitialized on the path (MirMoveData's NeverParts and Carries, SPEC 14.10.3),
// a Move or a Drop uninitializes it, StorageLive and StorageDead start a fresh binding lifetime; a use needs its whole subtree initialized on
// every incoming path, its own bit being the construction fact; a use through a runtime Index of storage reports on its receiver under
// the selection's subject, its position local's, as the selection's guard, a use where the receiver is located, does (the builder's
// contract, DECISIONS R4-M56), so the two are one problem unless the receiver changed in between. Every root is tracked (A2 reads
// temporaries' states). The state rules report a temp's use as a program Place's but for its own bit: a temp that holds nothing
// holds the Never value, which a consumption takes as it is (SPEC 3.8, 14.10.3), and its parts are what a construction left
// unplaced (R4-M175), a part being a Place in its Temporary Place (SPEC 3.4). A whole temp that a placing location's operand moves is
// transferred, not used (SPEC 3.5.3, 12.4.3): an Assign's Use or aggregate, whose carries give its state to the component, and a
// Places Call, a construction's insertion into storage without component paths (PLAN G442). A whole use of the return local
// is a delivery, checked only where the runtime edges reach it; a Return checks nothing. A Move without a Move Path is reported whatever its
// root: DefaultArgumentMove_Kd for a default template's prepared slot or its owned contents, which the default cannot Move (SPEC 7.2.3),
// else StaticMovePathRequired_Kd. A body no view decides is analyzed once, for every view.
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

    private bool[] held = [];

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

        var all = MirCases.Every(body);
        var state = flow.Prepare(0, Lanes * ((moves.FlowCount + 63) >> 6), Lanes * ((moves.Paths.Length + 63) >> 6), out var words);
        if (state is null)
        {
            var limit = MirDataflow.StorageLimit(report, body, all, MirStorageTable.Initialization, words); // R4-M15a: the driver stops the views.
            drops.Incomplete(DiagnosticCode.OwnershipStorageLimit_Kd, default, limit);
            return false;
        }

        MirDataflow.Grow(ref this.held, moves.MaxCarried);
        var transfer = new Transfer(body, moves, cases, flow, state, drops) { Report = report, Views = moves.ViewDependent ? 1UL << (int)view : all, Held = this.held };
        flow.Forward(ref transfer);
        transfer.Checks = true;
        flow.Replay(ref transfer);
        return true;
    }

    // The lanes of A1 over the move paths: the entry, each block's statements and terminator, and in Replay the checks and A2's input.
    private struct Transfer(MirBody body, MirMoveData moves, MirCases cases, MirDataflow flow, ulong[] state, MirDrops drops) : IMirFlow, IMirUses
    {
        private SourceRef subject;
        private ushort scope; // The location's scope.
        private MirLocation at;
        private bool runtime; // Whether the runtime edges reach the block applied.
        private bool placing; // Whether the location places its operands: an Assign of a Use or an aggregate, or a Places Call.
        private bool placed; // Whether the next Place reported is the whole temp an operand of such a location moves.

        internal required MirReport Report { get; init; }

        internal ulong Views { get; init; }

        internal required bool[] Held { get; init; } // Each carry's source state before an Assign's uses (pooled, MirMoveData.MaxCarried).

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
                (this.subject, this.scope, this.at) = (body.Subject(statement.Source), statement.Source.Scope, new(block, i));
                if (statement.Kind is MirStatementKind.StorageLive or MirStatementKind.StorageDead)
                {
                    var path = moves.Find(statement.Place, out _);
                    MirDataflow.Apply(state, Lanes, path, moves.Paths[path].End, set: 1 << Uninit, clear: Fresh);
                    this.Event(path, set: false);
                }
                else if (statement.Kind != MirStatementKind.EndBorrow)
                {
                    // SPEC 14.10.3 (R4-M175): a construction places nothing into its Never parts, and a temp taken whole gives its
                    // component what this path left of it, read before its uses take it.
                    var carries = statement.Kind == MirStatementKind.Assign ? moves.Carries(data.Start + i) : default;
                    for (var k = 0; k < carries.Length; k += 2)
                    {
                        this.Held[k >> 1] = MirDataflow.Any(state, Lanes, Uninit, carries[k], moves.Paths[carries[k]].End);
                    }

                    this.placing = statement.Kind == MirStatementKind.Assign && statement.Rvalue.Kind is MirRvalueKind.Use or MirRvalueKind.Aggregate;
                    MirUses.ForEach(cases, this.at, ref this);
                    this.placing = false;
                    foreach (var part in statement.Kind == MirStatementKind.Assign ? moves.NeverParts(data.Start + i) : default)
                    {
                        this.Unplaced(part);
                    }

                    for (var k = 0; k < carries.Length; k += 2)
                    {
                        if (this.Held[k >> 1])
                        {
                            this.Unplaced(carries[k + 1]);
                        }
                    }
                }
            }

            if (data.Terminator.Kind != MirTerminatorKind.Return)
            {
                (this.subject, this.scope, this.at) = (body.Subject(data.Terminator.Source), data.Terminator.Source.Scope, new(block, data.Count));
                this.placing = data.Terminator.Places;
                MirUses.ForEach(cases, this.at, ref this);
                this.placing = false;
            }
        }

        // The union join (R4-M34).
        public readonly bool Join(Span<ulong> target, ReadOnlySpan<ulong> incoming) => MirDataflow.Union(target, incoming);

        // A placing location's operand that moves a whole temp transfers it (SPEC 3.5.3, 12.4.3, 14.10.3): it is not checked there; an
        // Assign's carries give its state to the component, checked where that is used. MirUses reports an operand before its Place.
        public void Operand(in MirOperand operand)
            => this.placed = this.placing && operand.Kind != MirOperandKind.Const && operand.Place is { Projection: 0, IsStatic: false } &&
                body.Locals[operand.Place.Root].Kind == MirLocalKind.Temp;

        public readonly void Effect(MirEffect effects, int site)
        {
        }

        // A use of a Place under the view. A whole use of the return local delivers it, which only the runtime edges can (SPEC 14.10.3). A
        // PairLayer a case-dependent access takes is the Place itself in the owner case and a Deref in a reference case (SPEC 13.5.5),
        // so the outermost such reference ends the path; through a Deref or an Index only the prefix's path is used, and a Move there has
        // no Move Path (SPEC 15.1.3, 15.1.5). A2 judges a Drop from the state before it, by its path (-1: none), and whether the runtime
        // edges reach it.
        public void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            var transfer = this.placed;
            this.placed = false;
            var used = (access & MirAccess.CaseDependent) != 0 ? this.Layer(place) : place;
            var how = MirMoveData.NoPath;
            var path = place.IsStatic ? -1 : moves.Find(used, out how);
            how = used == place ? how : MirMoveData.NoPath;
            if ((access & MirAccess.DropUse) != 0 && this.Checks)
            {
                drops.Record(state, this.at.Block, how == MirMoveData.Direct ? path : -1, this.runtime);
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
                    var (code, requirement) = this.Prepared(place) ? (DiagnosticCode.DefaultArgumentMove_Kd, OwnershipRequirement.DefaultArgumentMove)
                        : (DiagnosticCode.StaticMovePathRequired_Kd, OwnershipRequirement.StaticMovePathRequired);
                    this.Add(code, requirement, this.subject);
                }
            }
            else if ((access & MirAccess.Defines) != 0)
            {
                this.Define(path);
            }
            else if ((access & MirAccess.Moves) != 0)
            {
                if (!transfer)
                {
                    this.Check(path, this.subject);
                }

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

        // Whether a Place is a default template's prepared slot or its owned contents (SPEC 7.2.3, R4-M69): rooted at a PreparedInput p_i,
        // through its Deref and then no step into storage the slot does not own: a Deref, a PairLayer the view makes a reference (SPEC
        // 13.5.5) or an Index of a value, a Slice's or a raw pointer's (MirTypes.SelectsByValue).
        private readonly bool Prepared(MirPlace place)
        {
            if (place.IsStatic || body.Locals[place.Root].Role != MirLocalRole.PreparedInput)
            {
                return false;
            }

            var projections = body.Projections;
            for (var id = place.Projection; projections[id].Prefix > 0; id = projections[id].Prefix)
            {
                var borrowed = projections[id].Kind switch
                {
                    MirProjectionKind.Deref => true,
                    MirProjectionKind.PairLayer => cases.Layer(place with { Projection = id }) is not null,
                    MirProjectionKind.Index => MirTypes.SelectsByValue(body.TypeOf(place with { Projection = projections[id].Prefix })),
                    _ => false,
                };
                if (borrowed)
                {
                    return false;
                }
            }

            return true;
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
                this.Add(DiagnosticCode.ReassignedLet_Kd, OwnershipRequirement.ReassignedLet, this.subject);
            }

            MirDataflow.Apply(state, Lanes, path, paths[path].End, set: (1 << Ever) | (1 << Init), clear: (1 << Uninit) | (1 << Moved) | (1 << Partial));
            this.Event(path, set: true);
        }

        // A part its definition left uninitialized (SPEC 14.10.3): a Never part, or a part a carried temp left so on this path.
        private readonly void Unplaced(int path)
            => MirDataflow.Apply(state, Lanes, path, moves.Paths[path].End, set: 1 << Uninit, clear: (1 << Init) | (1 << Partial));

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
            var (start, end) = (this.Checked(path), moves.Paths[path].End);
            if (this.Checks && this.Reported(path) && MirDataflow.Any(state, Lanes, Uninit, start, end))
            {
                this.Uninitialized(MirDataflow.Any(state, Lanes, Moved, start, end), subject);
            }
        }

        // Whether the state rules report on a path: its root is a Place of the program or a temp (PLAN G442), not a drop flag.
        private readonly bool Reported(int path) => body.Locals[moves.Paths[path].Local].Kind is not MirLocalKind.DropFlag;

        // The first path a use of a path checks: a temp's own bit is no construction fact, as a temp that holds nothing holds the Never
        // value, which a consumption takes as it is (SPEC 3.8, 14.10.3); its parts are what a construction left unplaced (R4-M175).
        private readonly int Checked(int path)
            => moves.Paths[path].Parent < 0 && body.Locals[moves.Paths[path].Local].Kind == MirLocalKind.Temp ? path + 1 : path;

        // A use of a Place some incoming path leaves uninitialized: MovedPlace_Kd where it may have been moved, else UninitializedPlace_Kd.
        private readonly void Uninitialized(bool moved, SourceRef subject) => this.Add(
            moved ? DiagnosticCode.MovedPlace_Kd : DiagnosticCode.UninitializedPlace_Kd,
            moved ? OwnershipRequirement.PossiblyMovedUse : OwnershipRequirement.UninitializedUse,
            subject);

        // A problem at the location, with the call of the inline default copy it lies in, whose template's check it is (R4-M69); A2's table
        // records it first, a copy's included (R4-M102), at its subject in the location's scope with its index.
        private readonly void Add(DiagnosticCode code, OwnershipRequirement requirement, SourceRef subject)
        {
            var problem = new MirProblem(code, DiagnosticRequirement.Ownership(requirement), subject, body.Ordinal, this.Views);
            var inlined = body.Inlining(this.scope);
            var index = this.Report.Add(problem, inlined < 0 ? default : [new(ProblemFactKind.Inlined, body.Scopes[inlined].Source)]);
            drops.Incomplete(code, new(subject, this.scope, StatementStart: false), index);
        }
    }
}
