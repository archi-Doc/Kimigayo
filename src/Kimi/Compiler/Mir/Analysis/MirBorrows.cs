// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns A5's Loan stratum (SPEC 15.6–15.8; design A3 3, 5 and 6 with its 12 errata; DECISIONS R4-M57) per body and Semantics case view.
// Input: a validated body, its dataflow (MirDataflow, begun) and move paths (MirMoveData, built), the selected view (MirCases), A2's
// verdicts (MirProgram.Drops), and Binding's call retention (TyCtxt.Retains) and published rows (TyCtxt.Published). Output: each Loan
// conflict at its access or activation, coded by A3 5's A6 table (CallReservationConflict_Kd for a Loan in its reservation holder,
// CallActivationConflict_Kd or PlacementActivationConflict_Kd for an activation at a Call or at the writeback write, else
// ComparisonLoanConflict_Kd, also for a value that keeps a guard candidate's guard-local Origin past its EndBorrow), with the Loan's
// anchor, its witness's next use or the store that carries it and an activation's reservation as facts, and Unsupported_Kd at a destruction
// or a call it does not classify (a destruction walk past its bound, CallRetention.Limit), through MirReport; and Work, the steps it took
// (A3 6.2).
// Invariants: a holder is a root local whose case Type may hold a Loan (MirTypes.HoldsLoans), or E, the storage the body does not own; its
// state is a MirLoanSets node of Loans and markers, which never conflict and no ending removes (Φ_E, τ_s, and a guard candidate's γ, E7). A
// Loan exists per non-raw Ref site and view whose Place passes no shared borrow Deref (E3), named by its whole Place; access is syntactic,
// by Place prefix. A check reads holders, never Loan ids: the holders live after the access but those it overwrites, E, the values it
// stores into a live holder (transit), the holders their direct Loans name (the closure) and those its own Loans name (E5); an activation
// the holders live into it but its reservation holder, whose Loans seed the closure. A two-phase Loan is shared only in its reservation
// holder, where a write or destruction of a prefix of its Place reaches it (E1). Destroying a Move Path in place ends every Loan whose path
// it exactly prefixes (E8), after the checks and the transfer: the Loans are ordered by the Move Path of their Places' longest inline
// prefixes (MirMoveData's pre-order), so those of a path's subtree are one run of ids. A holder a direct Loan names keeps its state at
// every head.
// Must not read syntax or HIR, judge Origins, re-derive retention or Move Paths, intern projections, or copy the body per view.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

/// <summary>A5's Loan stratum (design A3, R4 shadow: the old path still decides): <see cref="Run"/> checks a body under the selected
/// view.</summary>
/// <param name="types">The Type context, whose retention and published rows calls read.</param>
/// <param name="flow">The dataflow, begun with the body.</param>
/// <param name="moves">The move paths, built for the body.</param>
/// <param name="cases">The Semantics case views, a view of the body selected.</param>
internal sealed partial class MirBorrows(TyCtxt types, MirDataflow flow, MirMoveData moves, MirCases cases)
{
    private const int Escaped = 0; // E's slot, and the element of the marker Φ_E.

    private readonly MirLoanSets sets = new();
    private readonly HashSet<BoundType> seen = new(ReferenceEqualityComparer.Instance);
    private int[] slots = []; // Each local's holder slot, -1 for none; E is slot 0.
    private bool[] named = []; // By local: whether a direct Loan names it, so it is a flow holder.
    private bool[] borrowed = []; // By root key (Key): whether a Loan's Place has that root.
    private MirPlace[] lent = []; // By local: the Place a Ref lent into it, or the local itself.
    private Loan[] loans = [];
    private Step[] loanSteps = []; // The Loans' Places, each a run.
    private Step[] walk = []; // A Place's steps.
    private int[] chain = []; // A Place's projections, last first.
    private int[] loanAt = []; // By location (Location): the Loan its Ref issues, or -1.
    private int[] loanOfOperand = []; // By operand: the Loan its Reborrow issues, or -1.
    private int[] loanStart = []; // By group (Loan.Group): its first Loan, the Loans being in group order.
    private Loan[] ordered = []; // The Loans being put in group order.
    private int[] renumbered = []; // By Loan in issue order: its id in group order.
    private int[] ended = []; // By location: the set of the Loans its destructions in place end (0: none).
    private MirBody body = null!;
    private MirProgram program = null!;
    private long view;
    private ulong views;
    private int holderCount; // The slots, E's included.
    private int flowCount; // The slots a head stores: E and the flow holders.
    private int loanCount;
    private int loanStepCount;
    private int guardBase; // The element of local 0's γ: after Φ_E and each Opaque site's τ_s; a guard candidate H's γ_H is guardBase + H.
    private int loanBase; // The element of Loan 0: after the markers, Φ_E, τ_s and γ.
    private int escapedSet; // The set {Φ_E}.

    /// <summary>The class of a Place's step (A3 3): an inline step, a runtime index into inline storage, an owning handle's payload, a
    /// shared or exclusive borrow Deref (a Slice's index is a shared one, E6), or a raw pointer's referent.</summary>
    private enum StepClass : byte
    {
        Inline,
        Index,
        Own,
        Shared,
        Uniq,
        Raw,
    }

    /// <summary>Gets the work of the runs (A3 6.2): the pre-pass's walks, transfers, both problems' slot joins, the holders of each point
    /// that ends Loans, each check's bit scan, stored values and seeds, set elements scanned, candidates compared and set bytes made.</summary>
    internal long Work { get; private set; }

    /// <summary>Gets the element bytes of the Loan sets the runs made (MirLoanSets.Bytes, A3 6.2's ν), which Work also counts.</summary>
    internal long Bytes { get; private set; }

    /// <summary>Checks a body under the selected view; view 0 of a body no view decides checks it for every view.</summary>
    /// <param name="program">The program, whose drop table it reads and whose problems it adds to.</param>
    /// <param name="body">The body.</param>
    /// <param name="view">The selected view.</param>
    internal void Run(MirProgram program, MirBody body, long view)
    {
        if (view > 0 && !moves.ViewDependent)
        {
            return;
        }

        var all = ulong.MaxValue >> (64 - (int)MirCases.Count(body));
        (this.program, this.body, this.view, this.views) = (program, body, view, moves.ViewDependent ? 1UL << (int)view : all);
        this.sets.Clear();
        this.guardBase = 1 + body.OpaqueSites.Length;
        (this.loanBase, this.escapedSet) = (this.guardBase + body.Locals.Length, this.sets.Add(0, Escaped));
        this.Loans();
        if (this.loanCount == 0)
        {
            return; // A view without a Loan has no conflict (A3 6.1).
        }

        this.Holders();
        this.Endings();
        this.Touches();
        var words = (this.holderCount + 63) >> 6;
        var state = flow.Prepare((this.flowCount + 63) >> 6, this.flowCount, Math.Max(words, this.holderCount));
        if (state is null)
        {
            program.Problems.Add(new(DiagnosticCode.OwnershipStorageLimit_Kd, body.Source, all)); // R4-M15a.
            return;
        }

        var liveness = new Liveness(this, state);
        flow.Backward(ref liveness);
        liveness.Recording = true;
        flow.ReplayBackward(ref liveness);
        var carrying = new Carrying(this, state);
        flow.Forward(ref carrying);
        (carrying.Checks, this.chainHead, this.stamp) = (true, -1, 0);
        Array.Clear(this.loanMarks);
        Array.Clear(this.reached);
        Array.Clear(this.scanned);
        Array.Clear(this.markSeen);
        Array.Clear(this.markVia);
        flow.Replay(ref carrying);
        this.Bytes += this.sets.Bytes;
        this.Work += this.sets.Bytes;
    }

    // A Deref's class by the Semantics of the Type it applies to: a shared or exclusive borrow, an owning handle's payload, or a raw
    // pointer's referent.
    private static StepClass Deref(SemanticsKind semantics) => semantics switch
    {
        SemanticsKind.Ref or SemanticsKind.ObjRef => StepClass.Shared,
        SemanticsKind.Uniq or SemanticsKind.ObjUniq => StepClass.Uniq,
        SemanticsKind.Raw => StepClass.Raw,
        _ => StepClass.Own,
    };

    // Whether a value of a case Type is a borrow, through which a read reaches what it refers to (A3 5's ret): a reference or a pair layer,
    // or a view handle, whose Index is a borrow Deref (E6).
    private static bool Borrows(BoundType? type) => MirTypes.IsReference(type) || MirTypes.View(type) is not null;

    // A location's index: a statement's in the body, a terminator's after every statement.
    private int Location(int block, int index)
    {
        ref readonly var data = ref this.body.Blocks[block];
        return index < data.Count ? data.Start + index : this.body.Statements.Length + block;
    }

    // A root's key: a local, or a static after the locals.
    private int Key(int root) => root >= 0 ? root : this.body.Locals.Length + ~root;

    // Whether a PairAcquire Reborrows under the view.
    private bool Reborrowing(in MirOperand acquire) => cases.Reborrows(acquire);

    // The Loans of the view (A3 4's issued, E3): a non-raw Ref of a Place that passes no shared borrow Deref, by its location, and a
    // PairAcquire the view makes a Reborrow, by its operand; each named by its whole Place, then ordered by group.
    private void Loans()
    {
        var (locals, locations) = (this.body.Locals.Length, this.body.Statements.Length + this.body.Blocks.Length);
        var keys = locals + this.body.Statics.Length;
        MirDataflow.Grow(ref this.loanAt, locations);
        MirDataflow.Grow(ref this.loanOfOperand, this.body.Operands.Length);
        MirDataflow.Grow(ref this.borrowed, keys);
        MirDataflow.Grow(ref this.named, locals);
        MirDataflow.Grow(ref this.lent, locals);
        Array.Fill(this.loanAt, -1, 0, locations);
        Array.Fill(this.loanOfOperand, -1, 0, this.body.Operands.Length);
        Array.Clear(this.borrowed, 0, keys);
        Array.Clear(this.named, 0, locals);
        for (var i = 0; i < locals; i++)
        {
            this.lent[i] = new(i);
        }

        (this.loanCount, this.loanStepCount) = (0, 0);
        for (var block = 0; block < this.body.Blocks.Length; block++)
        {
            ref readonly var data = ref this.body.Blocks[block];
            for (var i = 0; i < data.Count; i++)
            {
                ref readonly var statement = ref this.body.Statements[data.Start + i];
                if (statement.Kind != MirStatementKind.Assign)
                {
                    continue;
                }

                var rvalue = statement.Rvalue.Kind == MirRvalueKind.Adapt ? cases.Adapt(statement.Rvalue, out _) : statement.Rvalue;
                if (rvalue.Kind == MirRvalueKind.Ref && (MirRefKind)rvalue.Op != MirRefKind.Raw)
                {
                    this.loanAt[data.Start + i] = this.Issue(rvalue.Place, reborrow: false, (MirRefKind)rvalue.Op, statement.Place.Root, statement.Source);
                    if (statement.Place is { Projection: 0, Root: >= 0 } into)
                    {
                        this.lent[into.Root] = rvalue.Place;
                    }
                }

                this.Reborrows(statement.Rvalue.Start, statement.Rvalue.Count, statement.Source);
            }

            if (data.Terminator.Kind == MirTerminatorKind.Call)
            {
                this.Reborrows(data.Terminator.Start, data.Terminator.Count, data.Terminator.Source);
            }
        }

        this.Order(moves.Paths.Length + this.body.Statics.Length);
        this.Renumber(this.loanAt.AsSpan(0, locations));
        this.Renumber(this.loanOfOperand.AsSpan(0, this.body.Operands.Length));
        this.Work += locations;
    }

    // Orders the Loans by group, stably, so the Loans a Move Path prefixes are one run of ids (Subtree).
    private void Order(int groups)
    {
        MirDataflow.Grow(ref this.loanStart, groups + 1);
        MirDataflow.Grow(ref this.ordered, this.loanCount);
        MirDataflow.Grow(ref this.renumbered, this.loanCount);
        Array.Clear(this.loanStart, 0, groups + 1);
        for (var l = 0; l < this.loanCount; l++)
        {
            this.loanStart[this.loans[l].Group + 1]++;
        }

        for (var g = 1; g <= groups; g++)
        {
            this.loanStart[g] += this.loanStart[g - 1]; // Each group's start.
        }

        for (var l = 0; l < this.loanCount; l++)
        {
            var id = this.loanStart[this.loans[l].Group]++; // Each group's start becomes its end.
            (this.renumbered[l], this.ordered[id]) = (id, this.loans[l]);
        }

        this.loanStart.AsSpan(0, groups).CopyTo(this.loanStart.AsSpan(1));
        this.loanStart[0] = 0;
        (this.loans, this.ordered) = (this.ordered, this.loans);
        this.Work += groups + (2 * this.loanCount);
    }

    // The sites' Loans in group order.
    private void Renumber(Span<int> sites)
    {
        foreach (ref var loan in sites)
        {
            loan = loan >= 0 ? this.renumbered[loan] : -1;
        }

        this.Work += sites.Length;
    }

    // The PairAcquire operands the view makes Reborrows, each a Loan of its Place's referent.
    private void Reborrows(int start, int count, in SourceInfo source)
    {
        for (var o = start; o < start + count; o++)
        {
            if (this.body.Operands[o] is { Kind: MirOperandKind.PairAcquire } operand && cases.Reborrows(operand))
            {
                this.loanOfOperand[o] = this.Issue(operand.Place, reborrow: true, MirRefKind.Uniq, -1, source);
            }
        }
    }

    // A Loan of a Place issued into a local (-1: none), or -1 past a shared borrow Deref (E3).
    private int Issue(MirPlace place, bool reborrow, MirRefKind mode, int into, in SourceInfo source)
    {
        var count = this.Walk(place, reborrow, ref this.walk);
        var direct = true;
        for (var i = 0; i < count; i++)
        {
            if (this.walk[i].Class == StepClass.Shared)
            {
                return -1;
            }

            direct &= this.walk[i].Class is not (StepClass.Uniq or StepClass.Raw);
        }

        MirDataflow.Grow(ref this.loans, this.loanCount + 1);
        MirDataflow.Grow(ref this.loanSteps, this.loanStepCount + count);
        this.walk.AsSpan(0, count).CopyTo(this.loanSteps.AsSpan(this.loanStepCount));
        var (key, root) = (this.Key(place.Root), direct ? place.Root : -1);
        this.loans[this.loanCount] = new()
        {
            Root = key,
            Group = place.IsStatic ? moves.Paths.Length + ~place.Root : moves.Find(place, out _),
            Start = this.loanStepCount,
            Count = count,
            Mode = mode,
            Holder = into,
            Named = root,
            Direct = direct,
            Site = this.body.Subject(source),
        };
        this.loanStepCount += count;
        this.borrowed[key] = true;
        if (root >= 0)
        {
            this.named[root] = true;
        }

        return this.loanCount++;
    }

    // The holders of the view (A3 3): E in slot 0, then the flow holders, whose states cross heads (MirMoveData's flow roots and the roots a
    // direct Loan names, whose states the closure and the Loans' content read anywhere), then the chain-local ones; then each Loan's
    // reservation holder and named holder as slots.
    private void Holders()
    {
        var locals = this.body.Locals;
        MirDataflow.Grow(ref this.slots, locals.Length);
        this.holderCount = 1;
        for (var i = 0; i < locals.Length; i++)
        {
            this.slots[i] = MirTypes.HoldsLoans(cases.Type(locals[i].Type), this.seen) ? -2 : -1;
        }

        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < locals.Length; i++)
            {
                if (this.slots[i] == -2 && (this.named[i] || moves.Find(new(i), out _) < moves.FlowCount) == (pass == 0))
                {
                    this.slots[i] = this.holderCount++;
                }
            }

            this.flowCount = pass == 0 ? this.holderCount : this.flowCount;
        }

        for (var l = 0; l < this.loanCount; l++)
        {
            ref var loan = ref this.loans[l];
            (loan.Holder, loan.Named) = (loan.Holder >= 0 ? this.slots[loan.Holder] : -1, loan.Named >= 0 ? this.slots[loan.Named] : -1);
        }

        this.Work += (2 * locals.Length) + this.loanCount;
    }

    // The Loans each location's destructions in place end (E8): a whole write of a Move Path (an Assign's Place, a returning Call's
    // destination), StorageLive and StorageDead of a root, and a call target its published row destroys in place (`replace`), the Place
    // its Ref lent, whose reservation is destroying (A3 4's destroying(site)).
    private void Endings()
    {
        var statements = this.body.Statements;
        MirDataflow.Grow(ref this.ended, statements.Length + this.body.Blocks.Length);
        for (var block = 0; block < this.body.Blocks.Length; block++)
        {
            ref readonly var data = ref this.body.Blocks[block];
            for (var i = 0; i < data.Count; i++)
            {
                var statement = statements[data.Start + i];
                this.ended[data.Start + i] = statement.Kind is MirStatementKind.Assign or MirStatementKind.StorageLive or MirStatementKind.StorageDead
                    ? this.Ending(statement.Place) : 0;
            }

            var terminator = data.Terminator;
            var set = terminator.Kind == MirTerminatorKind.Call && terminator.Target >= 0 ? this.Ending(terminator.Place) : 0;
            if (this.InPlaceTarget(terminator) is var target and >= 0)
            {
                set = this.sets.Union(set, this.Ending(this.lent[target]));
                this.Destroying(target);
            }

            this.ended[statements.Length + block] = set;
        }

        this.Work += statements.Length + this.body.Blocks.Length;
    }

    // The argument local whose target a Call's published row destroys in place (`replace`), or -1.
    private int InPlaceTarget(in MirTerminator call)
    {
        if (call is not { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Fn, Arg: 0, Symbol: { } symbol } } ||
            types.Published(symbol) is not { InPlace: true, Destroys: >= 0 } row)
        {
            return -1;
        }

        return this.body.Operands[call.Start + MirCallee.Position(symbol, row.Destroys)].Place is { Root: >= 0 } target ? target.Root : -1;
    }

    // The two-phase Loans issued into an argument holder whose call destroys its target in place: the Ref reads its target where it is
    // formed, and its activation writes it (A3 5's access and err(activate)).
    private void Destroying(int local)
    {
        var (first, end) = this.Subtree(new(this.lent[local].Root));
        for (var l = first; l < end; l++)
        {
            ref var loan = ref this.loans[l];
            loan.Destroying |= loan.Mode == MirRefKind.TwoPhaseUniq && loan.Holder == this.slots[local];
        }

        this.Work += end - first;
    }

    // The Loans destroying a Move Path in place ends (E8), those whose Places it exactly prefixes: the Loans whose groups lie in its
    // subtree. A Place through a Deref or an index under the view, or a static's, is no Move Path.
    private int Ending(MirPlace destroyed)
    {
        if (destroyed.IsStatic)
        {
            return 0;
        }

        var count = this.Walk(destroyed, reborrow: false, ref this.walk);
        for (var i = 0; i < count; i++)
        {
            if (this.walk[i].Class != StepClass.Inline)
            {
                return 0;
            }
        }

        var (first, end) = this.Subtree(destroyed);
        return this.sets.Range(this.loanBase + first, end - first);
    }

    // The Loans whose groups lie in a Place's: the subtree [K, End) of a local's Move Path K (MirMoveData's pre-order), whose Places K
    // exactly prefixes (E8), or a static's group.
    private (int First, int End) Subtree(MirPlace place)
    {
        if (place.IsStatic)
        {
            var group = moves.Paths.Length + ~place.Root;
            return (this.loanStart[group], this.loanStart[group + 1]);
        }

        var path = moves.Find(place, out _);
        return (this.loanStart[path], this.loanStart[moves.Paths[path].End]);
    }

    // The steps of a Place under the view, root first (A3 3): a Base of depth d is d steps, an owner-case pair layer is none (SPEC 13.5.5),
    // and a Reborrow adds its referent's exclusive Deref; returns their number.
    private int Walk(MirPlace place, bool reborrow, ref Step[] into)
    {
        var projections = this.body.Projections;
        var length = 0;
        for (var id = place.Projection; id > 0; id = projections[id].Prefix)
        {
            MirDataflow.Grow(ref this.chain, length + 1);
            this.chain[length++] = id;
        }

        var count = 0;
        for (var i = length - 1; i >= 0; i--)
        {
            var (id, row) = (this.chain[i], projections[this.chain[i]]);
            var prefix = place with { Projection = row.Prefix };
            var @class = row.Kind switch
            {
                MirProjectionKind.Deref => Deref(cases.Type(this.body.TypeOf(prefix)!).Semantics),
                MirProjectionKind.PairLayer => cases.Layer(place with { Projection = id }) is { } layer ? Deref(layer.Semantics) : (StepClass?)null,
                MirProjectionKind.Index => MirTypes.View(cases.Type(this.body.TypeOf(prefix)!)) is { } view ? Deref(view) : StepClass.Index,
                _ => StepClass.Inline,
            };

            for (var depth = row.Kind == MirProjectionKind.Base ? row.Arg : 1; @class is { } known && depth > 0; depth--)
            {
                MirDataflow.Grow(ref into, count + 2);
                into[count++] = new(row.Kind, row.Kind == MirProjectionKind.Base ? 1 : row.Arg, known);
            }
        }

        MirDataflow.Grow(ref into, count + 1);
        if (reborrow)
        {
            into[count++] = new(MirProjectionKind.Deref, 0, StepClass.Uniq);
        }

        return count;
    }

    /// <summary>A step of a Place: its projection kind and argument (a Base of depth 1) and its class.</summary>
    private readonly record struct Step(MirProjectionKind Kind, int Arg, StepClass Class);

    /// <summary>A Loan (A3 3).</summary>
    private struct Loan
    {
        /// <summary>Gets or sets its Place's root key.</summary>
        internal int Root { get; set; }

        /// <summary>Gets or sets its Place's group: the Move Path of its longest inline prefix (MirMoveData), or for a static's Place the
        /// number of paths and the static's index.</summary>
        internal int Group { get; set; }

        /// <summary>Gets or sets the first of its Place's steps.</summary>
        internal int Start { get; set; }

        /// <summary>Gets or sets the number of its Place's steps.</summary>
        internal int Count { get; set; }

        /// <summary>Gets or sets its mode.</summary>
        internal MirRefKind Mode { get; set; }

        /// <summary>Gets or sets the slot it was issued into, a two-phase Loan's reservation holder (-1: none).</summary>
        internal int Holder { get; set; }

        /// <summary>Gets or sets the slot a direct Loan names (-1: none).</summary>
        internal int Named { get; set; }

        /// <summary>Gets or sets a value indicating whether it is direct: its Place passes no borrow or raw Deref.</summary>
        internal bool Direct { get; set; }

        /// <summary>Gets or sets a value indicating whether it is a destroying reservation: its call destroys its target in place.</summary>
        internal bool Destroying { get; set; }

        /// <summary>Gets or sets its anchor.</summary>
        internal SourceRef Site { get; set; }
    }
}
