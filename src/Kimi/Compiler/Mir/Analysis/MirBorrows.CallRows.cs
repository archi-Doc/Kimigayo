// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// A5's Call contract records (A3 5, Calls; E14; SPEC 7.2.3, 15.8.2; DECISIONS R4-M51, R4-M63, R4-M93, R4-M148, R4-M154): the retention a
// Call applies, by callee kind, apart from its transfer (MirBorrows.Calls).
// Input: a Call terminator; for a callee's contract Binding's retention (TyCtxt.Retains over the callee's declaration and Subst, an
// intrinsic's library declaration included, or a Value callee's declared signature) and the intrinsic table's retained operand beyond a
// declaration (MirDescriptors); a default template's call (MirCalleeKind.Fn, Arg > 0) needs neither.
// Output: Plan, in reused scratch: the result's kinds by operand position (resultKinds) and whether it is untied, the contract's
// retention whose Writes the Call stores through (planned), its slots' operand positions (slotPositions) and the outputs the call writes only
// where their receiver refers to (its referent stores, CallRetention.ReferentStores, by position); Retaining, the CallRetention of a
// callee's contract; Broken, a Value call whose declared signature does not number its operands; and PD13's view hold (held, R4-M148),
// which MirBorrows.Reports applies.
// - A callee's contract: its records, slots to positions (Position: a Closure callee's input 0, its call receiver, at the callee operand,
//   position call.Count, where TyCtxt.HasReceiverInput says the contract has it; every other slot as MirCallee.Position maps it), and an
//   intrinsic's Value for the operand the builder adds beyond its declaration. The records and the referent stores map through the one
//   Position.
// - PD13 (SPEC 7.7.3; R4-M126 (iii), R4-M148): a Call whose acquired receiver, the Semantics of its callee operand's case Type, is
//   Exclusive while the callee's case Type admits a Shared one (TyCtxt.ClosureReceiverIn) holds the view: each rejection of it is recorded
//   as the located Unsupported_Kd at its subject (MirBorrows.Reports). Plan sets the fact in the forward solve, which plans every
//   reachable Call before the checks replay records any problem; Run resets it per view. Removal: per-case callee acquisition (PLAN G13).
// - G136 (R4-M148): a Value call whose declared signature's inputs are not its operands is a broken contract: Plan maps no record of it and
//   the checks record InternalInvariant_Kd at the call (MirBorrows.Locations).
// - P9 (SPEC 7.2.3, R4-M93): a default template's call keeps each input's Content in its untied result: its inputs are the prologue's
//   shared borrows of the slots, and each input's Content, its slot's value, reaches the result; no store (the omitting call's own
//   records apply the default's raw stores, SPEC 7.2.3, 5.2.1 Retained dependencies; R4-M116 PQ-5).
// Invariants: a CallRetention.Limit, a genuine limit (Binding's storage bounds or a keeper cycle), retains nothing the stratum follows;
// another terminator, an intrinsic without a declaration and a default template's call have no CallRetention.
// Must not read a state or apply a record (MirBorrows.Calls), derive retention (TyCtxt.Retains) or record a problem: the checks record the
// Limit as a located Unsupported_Kd at the call (R4-M51, MirBorrows.Locations).
internal sealed partial class MirBorrows
{
    private RetentionKind[] resultKinds = []; // A Call's result kinds by operand position (Plan).
    private int[] slotPositions = []; // A Call's contract slots' operand positions (Plan).
    private int[] referentStores = []; // A Call's referent stores by operand position (Plan; CallRetention.ReferentStores).
    private int referentCount;
    private CallRetention? planned; // A Call's contract retention, whose Writes it stores through (Plan); null for none.
    private bool held; // PD13's view hold (R4-M126 (iii), R4-M148): the view has a Call acquiring more than its case admits (Plan).

    // A contract slot's operand position: with the call receiver input (receiver, call.Count), slot 0 is the callee operand and slot i the
    // argument i - 1 (R4-M148); without it (receiver -1), as MirCallee.Position maps a function's slots.
    private static int Position(in MirCallee callee, int receiver, int slot)
        => receiver < 0 ? MirCallee.Position(callee.Symbol, slot) : slot == 0 ? receiver : slot - 1;

    // A Call's plan into the scratch (A3 E14): its callee's contract (Retaining), slots to positions, the result's kinds with an intrinsic's
    // Value for the operand the builder adds beyond its declaration (MirDescriptors); a default template's call P9's (SPEC 7.2.3, R4-M93):
    // its inputs are the prologue's shared borrows of the slots, and each input's Content, its slot's value, reaches the untied result; no
    // store. Whether the result is untied.
    private bool Plan(in MirTerminator call)
    {
        var callee = call.Callee;
        (this.referentCount, this.planned) = (0, null);
        MirDataflow.Grow(ref this.resultKinds, call.Count + 1);
        Array.Clear(this.resultKinds, 0, call.Count + 1);
        if (callee is { Kind: MirCalleeKind.Fn, Arg: > 0 })
        {
            this.resultKinds.AsSpan(0, call.Count).Fill(RetentionKind.Content);
            return true;
        }

        var receiver = this.Receiver(call);
        var retention = !this.Broken(call) && this.Retaining(call) is { IsLimit: false } contract ? contract : null;
        var untied = false;
        if (retention is not null)
        {
            var result = retention.Result;
            MirDataflow.Grow(ref this.slotPositions, result.Kinds.Length);
            for (var slot = 0; slot < result.Kinds.Length; slot++)
            {
                this.slotPositions[slot] = Position(callee, receiver, slot);
                this.resultKinds[this.slotPositions[slot]] |= result.Kinds[slot];
            }

            MirDataflow.Grow(ref this.referentStores, retention.ReferentStores.Length);
            foreach (var slot in retention.ReferentStores)
            {
                this.referentStores[this.referentCount++] = this.slotPositions[slot];
            }

            (this.planned, untied) = (retention, result.Untied);
        }

        if (callee.Kind == MirCalleeKind.Intrinsic && MirDescriptors.Of((IntrinsicId)callee.Arg).Retained is >= 0 and var added)
        {
            this.resultKinds[added] |= RetentionKind.Value;
        }

        return untied;
    }

    // The position of a Call's call receiver input, call.Count, where its callee is a Value callee whose contract has one
    // (TyCtxt.HasReceiverInput, read through one Semantics layer), else -1; there PD13's hold (R4-M126 (iii)): an Exclusive acquisition, a
    // `uniq` callee operand, where the case Type admits a Shared receiver is the only one stronger than its case's (the acquisition is the
    // callee's strongest receiver, and a Consuming one never depends on a case).
    private int Receiver(in MirTerminator call)
    {
        if (call.Callee.Kind != MirCalleeKind.Value || this.TypeOf(call.Operand) is not { } callee || !types.HasReceiverInput(callee))
        {
            return -1;
        }

        this.held |= callee is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Components: [var closure] } &&
            types.ClosureReceiverIn(closure) == SemanticsKind.Ref;
        return call.Count;
    }

    // G136 (R4-M148): whether a Value call's declared signature numbers inputs other than its operands, a broken contract.
    private bool Broken(in MirTerminator call)
        => call is { Kind: MirTerminatorKind.Call, Callee.Kind: MirCalleeKind.Value } && this.body.Consts[call.Callee.Arg].Type.Components[0] is var inputs &&
            (ReferenceEquals(inputs, BoundType.Unit) ? 0 : inputs.Components.Count) != call.Count;

    // The retention a Call's contract gives: TyCtxt.Retains over the callee's declaration (an intrinsic's library declaration, E14) and
    // Subst, or a Value callee's declared signature; none for another terminator, an intrinsic without a declaration or a default template's
    // call. Its Limit, a genuine limit, retains nothing the stratum follows: the checks record a located Unsupported_Kd at the call
    // (R4-M51); a call over storage Binding rejected has its rows (G240).
    private CallRetention? Retaining(in MirTerminator call)
    {
        var callee = call.Callee;
        if (call.Kind != MirTerminatorKind.Call || callee is { Kind: MirCalleeKind.Intrinsic, Symbol: null } or { Kind: MirCalleeKind.Fn, Arg: > 0 })
        {
            return null;
        }

        return callee.Kind == MirCalleeKind.Value
            ? types.Retains(this.TypeOf(call.Operand)!, this.body.Consts[callee.Arg].Type, this.body.Key.Decl)
            : types.Retains(callee.Symbol!, callee.Subst, this.body.Key.Decl);
    }
}
