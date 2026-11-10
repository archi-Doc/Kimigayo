// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Step 5's obligation (SPEC 10.1 step 5, 8.1.3; DECISIONS BU-136, PLAN G572 U2a), a part of constraint validation.
// - Responsibility: a candidate's Constraints and premises over its bindings, one conjunction in one order (StepFiveConjunction) over
//   one value, CandidateBindings, the candidate's substitution; and the selected call's obligation (CheckCallTypeConstraints): that
//   conjunction judged again in the Final loop, then the selected call's own Types.
// - Inputs: the trial's solution (SlotSolution.Prove) with the Contract reference its candidate is tried under (the
//   activeRequirementContract CallSelection sets), or the selected call's plan (CallPlan's columns, RequirementContract among them);
//   the scope whose premises apply.
// - Outputs: the proof, combined as P < U < L < R < E; the selected call's obligation keeps its verdict's facts (the Unknown fact, the
//   clause) for its record (RequireClauses).
// - Invariants: the trial and the plan supply the same value and judge one conjunction, under the value's Contract reference (SPEC
//   8.6.4), the enclosing one restored after it; InfersOwner holds only while a constructor's trial infers its owner's slots (10.8.1);
//   the declared signature stays the trial's part until C2 (b) (PLAN G375).
// - Prohibitions: no record and no failure is written here; no second copy of the conjunction (the Final check as its own copy, BU-136);
//   CandidateBindings holds no verdict, and no judgment but step 5's conjunction reads it.
public sealed partial class Binding
{
    private ConstraintProof CheckCallTypeConstraints(CallPlan call, BindingScope scope)
    {
        // A linked constructor is judged as the public declaration it selected (SPEC 22.1).
        var target = this.Library.PresentedTarget(call.Target);
        if (this.InvalidDeclarationContext(target.Declaration))
        {
            return ConstraintProof.Error;
        }

        // SPEC 10.1: the selected candidate's obligation is its trial's conjunction (StepFiveConjunction), as one proof that keeps its
        // facts for the record, then the selected call's own Types.
        this.BeginProof();
        ConstraintProof proof;
        if (target.Declaration is FunctionKoto function)
        {
            // The declared signature stays the trial's part until R7-C2 (b) (PLAN G375): read here it would fault on library declarations.
            var bindings = new CandidateBindings(
                function,
                target,
                SubstitutionOf(function, call),
                call.ConformingType,
                call.DeclaringType,
                call.DeclaringType,
                call.RequirementContract,
                default,
                infersOwner: false);
            proof = this.StepFiveConjunction(bindings, scope, false, null);
        }
        else
        {
            proof = call.DeclaringType is { } declaringType ? this.CheckTypeConstraints(declaringType, scope) : ConstraintProof.Proven;
            proof = CombineProof(proof, this.ProveMemberConditions(target, call.DeclaringType, scope), true);
        }

        // The slots past the callee's own (a constructor's owner slots) keep the Final check's formation read.
        var first = target.Declaration is FunctionKoto callee ? System.Math.Min(call.TypeArguments.Length, CallOwnSlots(callee).Count) : 0;
        for (var i = first; i < call.TypeArguments.Length; i++)
        {
            if (call.TypeArguments[i] is { } argument)
            {
                proof = CombineProof(proof, this.CheckTypeConstraints(argument, scope), true);
            }
        }

        if (UnresolvedTypeDeclarationContext(target.Declaration))
        {
            proof = CombineProof(proof, ConstraintProof.Unknown, true);
        }

        proof = CombineProof(proof, this.CheckTypeConstraints(call.ReturnType, scope), true);
        if (call.ConformingType is { } conformingType)
        {
            proof = CombineProof(proof, this.CheckTypeConstraints(conformingType, scope), true);
        }

        proof = CombineProof(proof, this.CheckOperationTypeConstraints(call.ReceiverOperation, scope), true);
        foreach (var operation in call.ArgumentOperations)
        {
            proof = CombineProof(proof, this.CheckOperationTypeConstraints(operation, scope), true);
        }

        var fact = this.ProofCause(proof) is null ? this.ProofFact(proof) : null;
        var clause = this.ProofClause(proof);
        proof = this.EndProof(proof);
        this.LeaveUnknown(fact, proof);
        this.LeaveClause(clause, proof);
        return proof;
    }

    // SPEC 10.1 step 5, 8.1.3 (DECISIONS: G572 U2a): a candidate's Constraints and premises over its bindings, one conjunction in
    // one order: the callee's Constraints, its owner Type's formation, each own binding's formation (a length slot bound, a Type slot
    // formed, an unsolved slot only where 10.8 leaves it unbound), the member's conditions. The candidate trial (SlotSolution.Prove) and
    // the selected call's obligation (CheckCallTypeConstraints) judge it over the same value; the declared signature stays the trial's
    // (C2 (b), PLAN G375). `incomplete` and `slotOwner` are the trial's: a waiting argument's open slots (10.5.1, 10.8).
    private ConstraintProof StepFiveConjunction(in CandidateBindings bindings, BindingScope scope, bool incomplete, Koto? slotOwner)
    {
        // SPEC 8.6.4: a requirement's clauses read the Contract's parameters at the reference the call goes through, as its signature does.
        var enclosing = this.activeRequirementContract;
        this.activeRequirementContract = bindings.Reference;
        try
        {
            var function = bindings.Function;
            var open = !bindings.Unbound.IsEmpty;
            var proof = this.CheckConstraints(function.TypeConstraints, bindings.Substitution, scope, bindings.Self, incomplete: incomplete || open, skipUnresolved: open);
            if (bindings.Owner is { } owner)
            {
                proof = CombineProof(proof, this.CheckTypeConstraints(owner, scope), true);
            }
            else if (bindings.InfersOwner && bindings.DeclaringType is not null && slotOwner is StructKoto openOwner)
            {
                // A constructor whose owner Type is not closed yet judges its owner's clauses over the slots inferred so far (10.8.1).
                proof = CombineProof(
                    proof,
                    this.CheckConstraints(
                        openOwner.ConstraintNodes, new CallSubstitution(openOwner, bindings.Slots), scope, incomplete: true, skipUnresolved: !bindings.Unbound.IsEmpty),
                    true);
            }

            var own = CallOwnSlots(function);
            var lengths = bindings.Lengths;
            for (var i = 0; i < own.Count && i < bindings.Slots.Length; i++)
            {
                var slot = own[i] is LengthParameterKoto ? (i < lengths.Length && lengths[i] is not null ? ConstraintProof.Proven : ConstraintProof.Unknown)
                    : bindings.Slots[i] is { } argument ? this.CheckTypeConstraints(argument, scope)
                    : bindings.Unbound.Contains(i) ? ConstraintProof.Proven : ConstraintProof.Unknown;
                proof = CombineProof(proof, slot, true);
            }

            return CombineProof(proof, this.ProveMemberConditions(bindings.Target, bindings.DeclaringType, scope), true);
        }
        finally
        {
            this.activeRequirementContract = enclosing;
        }
    }

    private ConstraintProof CheckOperationTypeConstraints(in BoundArgumentOperation operation, BindingScope scope)
    {
        var proof = operation.SourceType is { } source ? this.CheckTypeConstraints(source, scope) : ConstraintProof.Proven;
        if (operation.ParameterType is { } parameter && !ReferenceEquals(parameter, operation.SourceType))
        {
            proof = CombineProof(proof, this.CheckTypeConstraints(parameter, scope), true);
        }

        return proof;
    }

    // SPEC 10.1 steps 3 to 5 (DECISIONS: G572 U2a): a candidate's bindings as inference leaves them and the selected call publishes
    // them; the one input of step 5's conjunction (StepFiveConjunction).
    // - Inputs: the trial's solution (SlotSolution.Prove) or the selected call's plan (CheckCallTypeConstraints, CallPlan's columns).
    // - Invariants: Substitution is the candidate's one substitution (Binding.CallSubstitution), the value its signature is read under;
    //   Slots (its Types) holds the call's slots, the callee's own first (CallOwnSlots), a constructor's owner slots after them; a null Type
    //   slot is unsolved, and Unbound lists those 10.8 leaves unbound (none in a plan); Lengths is per slot; DeclaringType is the Type the
    //   member is found in, Owner its closed formation (a constructor's ConstructionType), null while its slots are open; Reference is the
    //   bound Contract reference a requirement candidate is called through (SPEC 8.6.4), null for any other candidate: in a trial the
    //   activeRequirementContract CallSelection tries it under, in a plan CallPlan.RequirementContract; InfersOwner holds while a
    //   constructor's trial infers its owner's slots (10.8.1), never in a plan.
    // - Prohibitions: no judgment but step 5's conjunction reads it; it holds no verdict.
    private readonly ref struct CandidateBindings
    {
        internal CandidateBindings(
            FunctionKoto function,
            BindingSymbol target,
            CallSubstitution substitution,
            BoundType? self,
            BoundType? declaringType,
            BoundType? owner,
            BindingSymbol? reference,
            SlotSet unbound,
            bool infersOwner)
        {
            this.Reference = reference;
            this.Function = function;
            this.Target = target;
            this.Substitution = substitution;
            this.Self = self;
            this.DeclaringType = declaringType;
            this.Owner = owner;
            this.Unbound = unbound;
            this.InfersOwner = infersOwner;
        }

        internal FunctionKoto Function { get; }

        internal BindingSymbol Target { get; }

        // The candidate's substitution (Binding.CallSubstitution), which its signature is read under too (CallType).
        internal CallSubstitution Substitution { get; }

        internal ReadOnlySpan<BoundType?> Slots => this.Substitution.Types;

        internal ReadOnlySpan<BoundLength?> Lengths => this.Substitution.Lengths;

        internal BoundType? Self { get; }

        internal BoundType? DeclaringType { get; }

        internal BoundType? Owner { get; }

        internal BindingSymbol? Reference { get; }

        internal SlotSet Unbound { get; }

        internal bool InfersOwner { get; }
    }
}
