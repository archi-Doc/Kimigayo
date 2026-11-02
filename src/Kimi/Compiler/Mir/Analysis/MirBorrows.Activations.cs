// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's call reservations (A3 5's err(activate) and err(destroy), with E1, E5, E11 and E13 (1b), (3); SPEC 15.6.7): a two-phase Loan in its
// reservation holder t activates where a Call takes t as an input (an argument, or a Value callee's operand) or the writeback writes through
// it (R4-M37), checked on the activation's check set (MirBorrows.CheckSets): the holders live after ℓ, the values of the operation's operands
// but t's and E, closed through the holders their direct Loans name and seeded by those σ_in(t)'s direct Loans name (E5), t left out, so the
// inputs of one call are checked against each other. A call that destroys its target in place (`replace`) writes the target and, where its
// old value's destruction may observe, uses the Places past it exclusively and deeply, on the same set. A destroying call whose target is not
// destroyed in place (the `clear`s) needs no such row: its activation is already an exclusive deep use (E3: no shared cut).
internal sealed partial class MirBorrows
{
    private readonly List<(int Local, int Loan, DiagnosticCode Code)> reserving = []; // The reservations the location activates.

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
    private void Activations(MirLocation at)
    {
        foreach (var (local, reservation, code) in this.reserving)
        {
            var loan = this.loans[reservation];
            var steps = this.loanSteps.AsSpan(loan.Start, loan.Count);
            var (taking, kind) = (Activation(this.slots[local]), loan.Destroying ? AccessKind.ActivateInPlace : AccessKind.Activate);
            if (this.Conflict(loan.Root, steps, kind, taking, this.Source(at).Scope, reservation) is var c and not None)
            {
                this.Report(this.Code(this.candidates[c], kind, code), c, taking, at, reservation, null);
            }

            if (loan.Destroying)
            {
                this.Destroyed(reservation, cases.Type(this.body.TypeOf(this.lent[local])!), taking, at);
            }
        }

        this.Work += this.reserving.Count;
    }

    // err(destroy) (A3 5, E11): the old value a call destroys in place at its target, where its Type may observe what it holds, uses the
    // Places past those the destroying activation writes exclusively and deeply, on the activation's check set, before the ending: the
    // conflicts relation's ObservePast; past the destruction walk's bound it is unclassified, the depth limit at the call (R4-M153).
    private void Destroyed(int reservation, BoundType target, int taking, MirLocation at)
    {
        var kind = this.Destruction(target, out _);
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
