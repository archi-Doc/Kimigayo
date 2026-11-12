// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's call reservations (A3 5's err(activate) and err(destroy), with E1, E5, E11 and E13 (1b), (3); SPEC 15.6.7): a two-phase Loan in its
// reservation holder t activates where a Call takes t as an input (an argument, or a Value callee's operand) or the writeback writes through
// it (R4-M37), checked on the activation's check set (MirBorrows.CheckSets): the holders live after ℓ, the values of the operation's operands
// but t's and E, closed through the holders their direct Loans name and seeded by those σ_in(t)'s direct Loans name (E5), t left out, so the
// inputs of one call are checked against each other, at the reservation's Place and at those of the parent Loans σ_in(t) carries unique
// (SPEC 15.6.3, 15.6.7: overlap is judged through the Loans the argument carries, so a Loan on what a reference it was Reborrowed through
// names is met at the activation, its own record, SPEC 23.1.2), each parent root's candidates scanned once for all of the root's parents,
// whose Places one prefix trie holds (Parents), so an activation's work is its check set's and its parents' steps, not parents × candidates.
// A call that destroys its target in place (`replace`) writes the target and, where its old value's destruction may observe, uses the Places
// past it exclusively and deeply, on the same set. A destroying call whose target is not destroyed in place (the `clear`s) needs no such row:
// its activation is already an exclusive deep use (E3: no shared cut).
internal sealed partial class MirBorrows
{
    private readonly List<(int Local, int Loan, DiagnosticCode Code)> reserving = []; // The reservations the location activates.
    private readonly Dictionary<(int Node, MirProjectionKind Kind, int Arg), int> throughChildren = []; // The parents' trie: a node's child by step key.
    private readonly Dictionary<int, int> throughRoots = []; // By root key: the trie node the root's parents' steps lie under.
    private bool[] throughEnds = []; // By trie node: whether a parent's steps end there.
    private int[] throughFirst = []; // By trie node: its first child (None: none), the list an Index step of a Loan walks.
    private int[] throughNext = []; // By trie node: its next sibling (None: none).
    private MirProjectionKind[] throughKinds = []; // By trie node: the kind of the step it is reached by (a root node's unused).
    private int throughCount;

    // The reservations a location activates, those σ_in(t) carries in t: at a Call's input holders, its argument operands' and a Value
    // callee's operand's (the receiver an Exclusive call takes, SPEC 7.7.3), and at the writeback's write through a reservation.
    private void Reserving(ulong[] state, MirLocation at)
    {
        this.reserving.Clear();
        ref readonly var data = ref this.body.Blocks[at.Block];
        if (at.Index < data.Count)
        {
            ref readonly var statement = ref this.body.Statements[data.Start + at.Index];
            if (statement.Kind == MirStatementKind.Assign && this.ThroughRoot(statement.Place))
            {
                this.Reserve(state, statement.Place.Root, DiagnosticCode.PlacementActivationConflict_Kd);
            }

            return;
        }

        var call = data.Terminator;
        for (var i = call.Callee.Kind == MirCalleeKind.Value ? -1 : 0; call.Kind == MirTerminatorKind.Call && i < call.Count; i++)
        {
            if ((i < 0 ? call.Operand : this.body.Operands[call.Start + i]) is { Kind: not MirOperandKind.Const, Place: { Projection: 0, Root: >= 0 } input })
            {
                this.Reserve(state, input.Root, DiagnosticCode.CallActivationConflict_Kd);
            }
        }
    }

    // A reservation holder's state, read once per call since the call moves it: an input that holds no reservation is not read.
    private void Reserve(ulong[] state, int local, DiagnosticCode code)
    {
        var slot = this.slots[local];
        if (slot < 0 || !this.reserves[slot])
        {
            return;
        }

        var held = state[slot];
        this.Work += this.Count(held);
        foreach (var element in this.ElementsOf(held))
        {
            if (element >= this.loanBase && this.loans[element - this.loanBase] is { Mode: MirRefKind.TwoPhaseUniq } loan && loan.Holder == slot)
            {
                this.reserving.Add((local, element - this.loanBase, code));
            }
        }
    }

    // Each reservation activated (A3 5's err(activate)) as an exclusive borrow of its Place, the destroying one as a write of its target
    // (shallow, and by E1 a destruction of the reservations it prefixes), each meeting only Loans shared in the holder found (R4-M68), whose
    // call then destroys the old value there (Destroyed, every mode).
    private void Activations(ulong[] state, MirLocation at)
    {
        foreach (var (local, reservation, code) in this.reserving)
        {
            var (slot, loan) = (this.slots[local], this.loans[reservation]);
            var (taking, kind) = (Activation(slot), loan.Destroying ? AccessKind.ActivateInPlace : AccessKind.Activate);
            if (this.Activated(state[slot], slot, reservation, kind, taking, this.Source(at).Scope) is var c and not None)
            {
                this.Report(this.Code(this.candidates[c], kind, code), c, taking, at, reservation, null);
            }

            if (loan.Destroying)
            {
                this.Destroyed(reservation, cases.TypeOf(this.lent[local]), taking, at);
            }
        }

        this.Work += this.reserving.Count;
    }

    // The first Loan of an activation's check set its reservation conflicts with (Conflict), None for none: at the reservation's Place, then
    // at the Places of the Loans its holder carries unique, its parent Loans (ActivateThrough; the holder's reservations are shared there,
    // each its own activation; a shared Loan conflicts with none the activation meets), each parent root's candidates scanned once against
    // the parents' trie (Parents, Through), in the order of the roots' first parents.
    private int Activated(ulong held, int slot, int reservation, AccessKind kind, int taking, int scope)
    {
        var loan = this.loans[reservation];
        var c = this.Conflict(loan.Root, this.loanSteps.AsSpan(loan.Start, loan.Count), kind, taking, scope, reservation);
        if (c != None || !this.Parents(held, slot))
        {
            return c;
        }

        this.Work += this.Count(held);
        foreach (var element in this.ElementsOf(held))
        {
            var index = element - this.loanBase;
            if (index >= 0 && index < this.loanCount && UniqueIn(this.loans[index], slot) && this.throughRoots.ContainsKey(this.loans[index].Root))
            {
                c = this.Conflict(this.loans[index].Root, [], AccessKind.ActivateThrough, taking, scope, reservation);
                this.throughRoots.Remove(this.loans[index].Root);
                if (c != None)
                {
                    break;
                }
            }
        }

        return c;
    }

    // The parents' prefix trie of an activation: the steps of each Loan its holder carries unique, under the node of the Loan's root, and
    // whether there is one. A node's children are by StepKey (MirBorrows.Checks), each step inserted once, so the trie is linear in the
    // parents' steps; a parent's last node is an end. The index ambiguity is the walk's (Below).
    private bool Parents(ulong held, int slot)
    {
        this.throughChildren.Clear();
        this.throughRoots.Clear();
        this.throughCount = 0;
        this.Work += this.Count(held);
        foreach (var element in this.ElementsOf(held))
        {
            var index = element - this.loanBase;
            if (index >= 0 && index < this.loanCount && UniqueIn(this.loans[index], slot))
            {
                var parent = this.loans[index];
                if (!this.throughRoots.TryGetValue(parent.Root, out var node))
                {
                    this.throughRoots[parent.Root] = node = this.Node(MirProjectionKind.Deref);
                }

                this.Insert(node, this.loanSteps.AsSpan(parent.Start, parent.Count));
            }
        }

        return this.throughCount > 0;
    }

    // A parent's steps under a trie node, each a child by its key, each a step of Work; the last node an end.
    private void Insert(int node, ReadOnlySpan<MirStep> steps)
    {
        this.Work += steps.Length + 1;
        foreach (var step in steps)
        {
            node = this.Child(node, StepKey(step));
        }

        this.throughEnds[node] = true;
    }

    // A trie node's child by a step key, made and linked into the node's child list where there is none.
    private int Child(int node, (MirProjectionKind Kind, int Arg) key)
    {
        if (!this.throughChildren.TryGetValue((node, key.Kind, key.Arg), out var child))
        {
            this.throughChildren[(node, key.Kind, key.Arg)] = child = this.Node(key.Kind);
            (this.throughNext[child], this.throughFirst[node]) = (this.throughFirst[node], child);
        }

        return child;
    }

    // A new trie node reached by a step of a kind: no end, no children.
    private int Node(MirProjectionKind kind)
    {
        var node = this.throughCount++;
        MirDataflow.Grow(ref this.throughEnds, this.throughCount);
        MirDataflow.Grow(ref this.throughFirst, this.throughCount);
        MirDataflow.Grow(ref this.throughNext, this.throughCount);
        MirDataflow.Grow(ref this.throughKinds, this.throughCount);
        (this.throughEnds[node], this.throughFirst[node], this.throughNext[node], this.throughKinds[node]) = (false, None, None, kind);
        return node;
    }

    // hits(A, X, ActivateThrough) for the parents A on a shared Loan X's root (MirBorrows.Checks' Hits, its shallow rows): X's steps walked
    // down the root's trie, a parent ending past X's last borrow Deref hits, as does any parent at or below X's Place.
    private bool Through(int root, in Loan loan)
    {
        var steps = this.loanSteps.AsSpan(loan.Start, loan.Count);
        var deref = -1;
        for (var i = 0; i < steps.Length; i++)
        {
            deref = steps[i].Class is MirStepClass.Shared or MirStepClass.Uniq ? i : deref;
        }

        this.Work += steps.Length;
        return this.Below(this.throughRoots[root], steps, 0, deref);
    }

    // Whether a parent in a trie node's subtree hits a Loan's steps from a depth: an end past the last borrow Deref, or the node reached at
    // the steps' end. Meets at the walk: an Index step of the Loan walks every index child of the node (each a parent step it meets, so a
    // node is visited at most once per Loan), a ConstIndex step the parents' Index key and its own, another step its key alone.
    private bool Below(int node, ReadOnlySpan<MirStep> steps, int depth, int deref)
    {
        this.Work++;
        if (depth == steps.Length || (this.throughEnds[node] && depth > deref))
        {
            return true;
        }

        var step = steps[depth];
        if (step.Kind == MirProjectionKind.Index)
        {
            for (var each = this.throughFirst[node]; each != None; each = this.throughNext[each])
            {
                if (this.throughKinds[each] is MirProjectionKind.Index or MirProjectionKind.ConstIndex && this.Below(each, steps, depth + 1, deref))
                {
                    return true;
                }
            }

            return false;
        }

        var key = StepKey(step);
        return (step.Kind == MirProjectionKind.ConstIndex && this.throughChildren.TryGetValue((node, MirProjectionKind.Index, 0), out var index) &&
            this.Below(index, steps, depth + 1, deref)) ||
            (this.throughChildren.TryGetValue((node, key.Kind, key.Arg), out var child) && this.Below(child, steps, depth + 1, deref));
    }

    // err(destroy) (A3 5, E11): the old value a call destroys in place at its target, where its Type may observe what it holds, uses the
    // Places past those the destroying activation writes exclusively and deeply, on the activation's check set, before the ending: the
    // conflicts relation's ObservePast; past the destruction walk's bound it is unclassified, the depth limit at the call (R4-M153).
    private void Destroyed(int reservation, BoundType target, int taking, MirLocation at)
    {
        var kind = this.Destruction(target);
        if (kind == AccessKind.Unclassified)
        {
            this.Limit(this.Subject(at), MirOutcome.Deep);
            return;
        }

        var (loan, scope) = (this.loans[reservation], this.Source(at).Scope);
        var steps = this.loanSteps.AsSpan(loan.Start, loan.Count);
        if (kind == AccessKind.Observe && this.Conflict(loan.Root, steps, AccessKind.ObservePast, taking, scope, reservation) is var c and not None)
        {
            this.Report(this.Code(this.candidates[c], AccessKind.ObservePast, DiagnosticCode.ComparisonLoanConflict_Kd), c, taking, at, reservation, null);
        }
    }

    // Whether a Place's first step is a Deref of its root: a write through the reference the root holds.
    private bool ThroughRoot(MirPlace place)
    {
        var id = place.Projection;
        while (id > 0 && this.body.Projections[id].Prefix > 0)
        {
            id = this.body.Projections[id].Prefix;
        }

        return id > 0 && this.body.Projections[id].Kind == MirProjectionKind.Deref;
    }
}
