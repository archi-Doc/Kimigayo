// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 7.3 and 10.5-10.6: forms a function-group reference under a required Function Type.
// Existing candidate inference and comparison select the reference and publish its binding or failure. Per-candidate slot facts
// keep their existing reset/read lifetime; Function Type structure and shared contract comparison stay in Binding.FunctionTypes.
public sealed partial class Binding
{
    // SPEC 10.6: the last reference slot that only per-call Origins of S would satisfy, as (Type parameter, parameters of S as bits,
    // whether S's own positions bind every slot, so that a wrapper's call infers them), and why the last candidate's slots did not bind
    // from S, with the slot when one is known; BindFunctionReference clears both before it checks a candidate and reads them for a single one.
    private (int Slot, ulong Parameters)? perCallReferenceSlot;

    private (int Slot, ReferenceSlotFailure Failure) referenceSlotFailure;

    // Ordinary members of Origin-bearing containers remain limited; constrained requirements use RequirementItems.
    // An instance member is referenced unbound through its Type (SPEC 7.3).
    private static bool UnsupportedReference(BindingSymbol candidate, bool unbound, BoundType? declaringType)
        => (candidate.ReceiverIndex >= 0 && !unbound) || candidate.Scope.Owner is ContractKoto || candidate.Scope.Owner.BoundSymbol?.Schema is { Origins.Count: > 0 } ||
            (candidate.Scope.Owner.BoundSymbol?.Schema is { GenericSlots.Count: > 0 } && !ContainerBound(candidate, declaringType));

    private BoundType? BindFunctionReference(Koto use, BindingSymbol symbol, BoundType required, BindingScope scope, bool erase = true)
    {
        if (this.ReferenceRequirements(use) is { } requirements)
        {
            return this.BindRequirementItem(use, requirements, scope, required, erase);
        }

        if (this.BoundMethodReference(use, symbol))
        {
            return this.Fail(use, BindingFailure.BoundMethodValue);
        }

        var unbound = this.UnboundMemberReference(use);
        var declaring = this.ReferenceDeclaringType(use);
        var count = 0;
        for (var candidate = ReferenceCandidate(symbol); candidate is not null; candidate = ReferenceCandidate(candidate.Next))
        {
            count++;
        }

        var evaluated = this.candidateScratch.Rent(count);
        var stride = required.Components[0].Components.Count + 1;
        var operations = this.argumentOperationScratch.Rent(count * stride);
        BindingSymbol selected;
        (int Slot, ulong Parameters)? perCall = null;
        (int Slot, ReferenceSlotFailure Failure) slotFailure = default;
        try
        {
            var index = 0;
            var applicable = 0;
            ReferenceConstraintFailure? pending = null;
            var pendingProof = false;
            Koto? invalid = null;
            var error = false;
            this.failedCause = null;
            for (var candidate = ReferenceCandidate(symbol); candidate is not null; candidate = ReferenceCandidate(candidate.Next), index++)
            {
                this.BindHeader(candidate);
                if (candidate.Declaration is not FunctionKoto function || UnsupportedReference(candidate, unbound, declaring))
                {
                    return this.Fail(use, BindingFailure.Unsupported);
                }

                this.perCallReferenceSlot = null;
                this.referenceSlotFailure = default;
                ReferenceConstraintFailure? constraint = null;
                var state = this.Accessible(candidate, scope)
                    ? this.FunctionReferenceApplicability(use, candidate, function, required, scope, out constraint, operations.AsSpan(index * stride, stride - 1))
                    : CandidateApplicability.Inapplicable;
                if (count == 1)
                {
                    perCall = this.perCallReferenceSlot;
                    slotFailure = this.referenceSlotFailure;
                }

                evaluated[index] = new(candidate, state, null, 0, ConstraintFailure: constraint);
                pendingProof |= state == CandidateApplicability.Pending;
                if (state == CandidateApplicability.Pending)
                {
                    pending ??= constraint;
                }

                if (state == CandidateApplicability.Error)
                {
                    error = true;
                    invalid ??= this.InvalidDeclarationContextCause(function) ?? this.FailedSignaturePart(function);
                }

                if (state == CandidateApplicability.Applicable)
                {
                    applicable++;
                }
            }

            if (error)
            {
                return (invalid ?? this.TakeFailedCause().Use) is { } cause ? this.CompleteDependent(use, cause)
                    : this.Fail(use, BindingFailure.InvalidConstraint);
            }

            if (pendingProof)
            {
                return pending is { } fact ? this.FailExplained(ref this.referenceConstraints, use, BindingFailure.UnprovenConstraint, fact) : this.Fail(use, BindingFailure.UnprovenConstraint);
            }

            var winner = this.SelectBest(evaluated.AsSpan(0, count), operations, stride);
            if (winner < 0)
            {
                if (count == 1 && evaluated[0].ConstraintFailure is { } failure)
                {
                    return this.FailExplained(ref this.referenceConstraints, use, BindingFailure.UnsatisfiedConstraint, failure);
                }

                if (perCall is { } slot)
                {
                    // SPEC 10.6, 15.3.6: the one candidate's slot is left unsolved by a per-call Origin of S.
                    return this.FailExplained(ref this.perCallSlots, use, BindingFailure.MissingOrigin, new PerCallSlotFact((FunctionKoto)symbol.Declaration, slot.Slot, slot.Parameters, required));
                }

                if (slotFailure.Failure != ReferenceSlotFailure.None && symbol.Declaration is FunctionKoto { GenericArguments.Count: > 0 } generic)
                {
                    // SPEC 10.5: the Type-mismatch Note names why the one generic candidate's slots did not bind from S.
                    (this.referenceSlotFacts ??= new(ReferenceEqualityComparer.Instance))[use] = new(generic, slotFailure.Slot, slotFailure.Failure, KotoHelper.UnwrapParentheses(use) is GenericsKoto);
                }

                var candidateItem = this.types.Intern(BoundTypeKind.FunctionItem, symbol, SemanticsKind.Owner, []);
                if (count == 1 && this.FunctionItemSignature(candidateItem) is { } actual)
                {
                    // SPEC 10.7, 15.6.1: when the one candidate's signature matches and only its Origin contract fails, the record names
                    // that contract, with the candidate's own inputs instantiated: a member, else a condition of the candidate.
                    var contract = ComparisonFailure.Shape;
                    return ReferenceTypes.StorageMatches(required, actual) &&
                        !this.ItemContractFits(candidateItem, actual, required, use, out contract) && contract.IsCondition
                        ? this.FailExplained(ref this.originContracts, use, BindingFailure.OriginContract, this.ContractFact(use, contract, use))
                        : this.FailMismatch(use, use, actual, required, contract);
                }

                var rejected = new RejectedCandidate[applicable == 0 ? count : applicable];
                var next = 0;
                for (var i = 0; i < count; i++)
                {
                    if (applicable == 0 || evaluated[i].State == CandidateApplicability.Applicable)
                    {
                        var candidate = evaluated[i].Symbol!;
                        var item = this.types.Intern(BoundTypeKind.FunctionItem, candidate, SemanticsKind.Owner, []);
                        rejected[next++] = new((FunctionKoto)candidate.Declaration, this.FunctionItemSignature(item), required, CallableSignature: true, ReferenceSignature: true, ConstraintFailure: evaluated[i].ConstraintFailure);
                    }
                }

                (this.rejectedCandidates ??= new(ReferenceEqualityComparer.Instance))[use] = rejected;
                return this.Fail(use, applicable == 0 ? BindingFailure.NoApplicableCandidate : BindingFailure.Ambiguous);
            }

            selected = evaluated[winner].Symbol!;
        }
        finally
        {
            this.argumentOperationScratch.Return(operations, clearArray: true);
            this.candidateScratch.Return(evaluated, clearArray: true);
        }

        var target = (FunctionKoto)selected.Declaration;
        if ((target.Modifier & ModifierKind.Unsafe) != 0)
        {
            return this.Fail(use, BindingFailure.UnsafeFunctionValue);
        }

        if (target.GenericArguments.Count != 0)
        {
            // The selected generic Item keeps its bound arguments; at a common Function Type it is then erased (SPEC 7.7.4).
            var arguments = this.typeScratch.Rent(target.GenericArguments.Count);
            var lengths = this.lengthScratch.Rent(target.GenericArguments.Count);
            try
            {
                if (!this.FunctionReferenceFits(use, selected, target, required, scope, boundArguments: arguments, boundLengths: lengths))
                {
                    return this.Fail(use, BindingFailure.Internal); // The selected candidate was applicable.
                }

                if (KotoHelper.UnwrapParentheses(use) is GenericsKoto written)
                {
                    for (var i = 0; i < target.GenericArguments.Count && i < written.TypeArguments.Count; i++)
                    {
                        if (written.TypeArguments[i].BoundType is { } type && arguments[i] is { } bound && SameExceptOmittedOrigins(type, bound, 0))
                        {
                            this.SolveOmittedOrigins(type, bound, use, 0);
                        }
                    }
                }

                var item = this.CompleteFunctionItem(use, selected, arguments.AsSpan(0, target.GenericArguments.Count), this.ReferenceContainer(use, selected), lengths.AsSpan(0, target.GenericArguments.Count));
                if (!erase)
                {
                    return item;
                }

                if (!this.ErasesToFunction(use, item, required))
                {
                    return this.FailMismatch(use, use, item, required, default);
                }

                use.ErasedFunctionType = required;
                return required;
            }
            finally
            {
                this.typeScratch.Return(arguments, clearArray: true);
                this.lengthScratch.Return(lengths, clearArray: true);
            }
        }

        // The reference is its Function Item; at a common Function Type that Item is erased (SPEC 7.7.4), whatever syntax names it.
        var concrete = this.CompleteFunctionItem(use, selected, default, this.ReferenceContainer(use, selected));
        if (!erase)
        {
            return concrete;
        }

        if (!this.ErasesToFunction(use, concrete, required))
        {
            return this.FailMismatch(use, use, concrete, required, default);
        }

        use.ErasedFunctionType = required;
        return required;
    }

    // Whether one function of a group converts to a common Function Type. A form BindFunctionReference rejects with its own
    // diagnostic (such as an unavailable container or requirement reference) counts as fitting, so that diagnostic is
    // published after selection.
    private bool FunctionGroupFits(Koto use, BindingSymbol symbol, BoundType required, BindingScope scope)
    {
        var unbound = this.UnboundMemberReference(use);
        var declaring = this.ReferenceDeclaringType(use);
        for (var candidate = ReferenceCandidate(symbol); candidate is not null; candidate = ReferenceCandidate(candidate.Next))
        {
            this.BindHeader(candidate);
            if (candidate.Declaration is not FunctionKoto function || UnsupportedReference(candidate, unbound, declaring))
            {
                return true;
            }

            if (this.Accessible(candidate, scope) && this.FunctionReferenceApplicability(use, candidate, function, required, scope, out _) != CandidateApplicability.Inapplicable)
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 10.5: a generic candidate's own slots are bound by matching its parameter Types against those of S and its result
    // against the result of S, without adaptations; it applies when every slot is bound and its Constraints are Proven.
    // Origins are left to the per-call solver, and a per-call Origin of S never becomes part of a bound argument. Explicit Type
    // arguments are compared with that binding as normalized Types, so equivalent Function Types with distinct binders agree.
    private bool BindReferenceArguments(Koto use, BindingSymbol symbol, FunctionKoto function, BoundType required, BoundType?[] arguments, BindingScope scope, GenericsKoto? explicitReference, BoundType? container, BoundLength?[] lengths, out ConstraintProof proof)
    {
        proof = ConstraintProof.Proven;
        var count = function.GenericArguments.Count;
        if (!this.InferReferenceSlots(symbol, function, required, arguments, container, false, true, lengths) ||
            (explicitReference is not null && !this.ExplicitReferenceAgrees(explicitReference, arguments, count, false, function, scope, lengths)))
        {
            // SPEC 10.8, 15.3.6: bindings of one slot that differ only in their Origins are an Origin failure, not a structural one;
            // when only per-call Origins of S would satisfy the slot, the reference's record names that slot and those parameters.
            // A Constraint that the met binding does not prove is judged first, as for a binding without a conflict, since neither an Origin
            // of the slot nor a wrapper would repair it.
            if (this.InferReferenceSlots(symbol, function, required, arguments, container, true, true, lengths) &&
                (explicitReference is null || this.ExplicitReferenceAgrees(explicitReference, arguments, count, true, function, scope, lengths)))
            {
                if (ReferenceSlotsComplete(function, arguments, lengths) && (proof = this.ReferenceArgumentProof(function, arguments, scope, container, lengths)) != ConstraintProof.Proven)
                {
                    this.referenceSlotFailure = (-1, ReferenceSlotFailure.Constraint);
                }
                else
                {
                    this.referenceSlotFailure = (count == 1 ? 0 : -1, ReferenceSlotFailure.OriginConflict);
                    this.perCallReferenceSlot = this.PerCallReferenceSlot(use, symbol, function, required, scope, explicitReference, container);
                }
            }
            else
            {
                this.referenceSlotFailure = (-1, ReferenceSlotFailure.Structure);
            }

            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (function.GenericArguments[i] is LengthParameterKoto ? lengths[i] is null : arguments[i] is null)
            {
                this.referenceSlotFailure = (i, ReferenceSlotFailure.Structure);
                return false;
            }
        }

        proof = this.ReferenceArgumentProof(function, arguments, scope, container, lengths);
        var parameters = required.Components[0];
        var binder = FunctionTypeBinder(required);
        for (var i = 0; i < count; i++)
        {
            if (function.GenericArguments[i] is LengthParameterKoto)
            {
                continue;
            }

            var perCall = 0UL;
            var foreign = false;
            InputOrigins(arguments[i]!, parameters, binder, use, false, ref perCall, ref foreign, 0);
            if (perCall != 0 || foreign)
            {
                // SPEC 10.6, 15.3.6: when the binding fits and its Constraints hold, a bound argument would hold an input Origin bound at
                // each call; when only per-call Origins of S would satisfy the slot, the reference's record names that slot and those parameters.
                if (proof != ConstraintProof.Proven)
                {
                    this.referenceSlotFailure = (-1, ReferenceSlotFailure.Constraint);
                    return false;
                }

                this.referenceSlotFailure = (i, ReferenceSlotFailure.InputOrigin);
                this.perCallReferenceSlot = this.PerCallReferenceSlot(use, symbol, function, required, scope, explicitReference, container);
                return false;
            }
        }

        if (proof != ConstraintProof.Proven)
        {
            this.referenceSlotFailure = (-1, ReferenceSlotFailure.Constraint);
            return false;
        }

        return true;
    }

    // SPEC 10.5, 10.8: binds a reference's slots from the parameters of S and, with `result`, from its result; `commonOrigins` meets
    // the Origins that two positions give one slot, as a call's inference would.
    private bool InferReferenceSlots(BindingSymbol symbol, FunctionKoto function, BoundType required, BoundType?[] arguments, BoundType? container, bool commonOrigins, bool result, BoundLength?[]? lengths = null)
    {
        Array.Clear(arguments, 0, function.GenericArguments.Count);
        if (lengths is not null)
        {
            Array.Clear(lengths, 0, function.GenericArguments.Count);
        }

        var parameters = required.Components[0];
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (function.Parameters[i].Type.BoundType is not { } written || this.MemberType(written, container) is not { } parameter ||
                !this.Infer(parameter, parameters.Components[i], function, arguments, inferOrigins: true, lengths: lengths, commonOrigins: commonOrigins, structural: true))
            {
                return false;
            }
        }

        return !result || (symbol.Type is { } writtenResult && this.MemberType(writtenResult, container) is { } bound &&
            this.Infer(bound, required.Components[1], function, arguments, inferOrigins: true, lengths: lengths, commonOrigins: commonOrigins, structural: true));
    }

    // SPEC 10.5: explicit Type arguments agree with the binding from S when they are the same normalized Type: identical, or equivalent
    // in both directions, as two spellings of one Function Type are. With `commonOrigins`, they may differ in covariant Origins only.
    // An agreeing argument replaces the binding, so the Item keeps the written Type; one that differs only in Origins it omits keeps
    // the binding, since local inference solves those Origins from S (SPEC 15.4.4).
    private bool ExplicitReferenceAgrees(GenericsKoto explicitReference, BoundType?[] arguments, int count, bool commonOrigins, FunctionKoto function, BindingScope scope, BoundLength?[] lengths)
    {
        for (var i = 0; i < count; i++)
        {
            var syntax = explicitReference.TypeArguments[i];
            if (function.GenericArguments[i] is LengthParameterKoto)
            {
                if (!this.IsLengthArgument(syntax, scope) || this.BindLength(syntax, scope) is not { } length ||
                    (lengths[i] is { } inferred && !ReferenceEquals(inferred, length)))
                {
                    return false;
                }

                lengths[i] = length;
                continue;
            }

            if (this.IsLengthArgument(syntax, scope))
            {
                return false;
            }

            if (explicitReference.TypeArguments[i].BoundType is not { } written)
            {
                return false;
            }

            if (arguments[i] is { } bound && !ReferenceEquals(bound, written))
            {
                if (SameExceptOmittedOrigins(written, bound, 0))
                {
                    continue;
                }

                if (!(IsSubtype(written, bound) && IsSubtype(bound, written)) && !(commonOrigins && this.CommonOriginType(written, bound, out _) is not null))
                {
                    return false;
                }
            }

            arguments[i] = written;
        }

        return true;
    }

    // SPEC 15.4.4: an Origin that a written Type argument omits is an inference variable of the local whose initializer holds the
    // reference; the selected binding from S solves it, as an initializer's acquisition bounds it. Elsewhere it stays unsolved.
    private void SolveOmittedOrigins(BoundType written, BoundType binding, Koto use, int depth)
    {
        if (ReferenceEquals(written, binding) || depth > 32)
        {
            return;
        }

        this.SolveOmittedOrigin(written.Origin, binding.Origin, use);
        for (var i = 0; i < written.OriginArguments.Count && i < binding.OriginArguments.Count; i++)
        {
            this.SolveOmittedOrigin(written.OriginArguments[i], binding.OriginArguments[i], use);
        }

        for (var i = 0; i < written.Components.Count && i < binding.Components.Count; i++)
        {
            this.SolveOmittedOrigins(written.Components[i], binding.Components[i], use, depth + 1);
        }
    }

    private void SolveOmittedOrigin(BoundOrigin? written, BoundOrigin? binding, Koto use)
    {
        if (written is { Kind: OriginKind.Inference } atom && binding is not null && !ReferenceEquals(atom, binding) && this.OpenInitializerInference(atom, use) is { } pending)
        {
            pending.Replacements[atom] = pending.Replacements.TryGetValue(atom, out var previous) ? this.Meet(previous, binding) : binding;
            this.originStateVersion++;
        }
    }

    // SPEC 10.6, 15.3.6: the slot that only per-call Origins of S would satisfy. The slots are bound as a call of the reference would bind
    // them: from the parameters of S, meeting the Origins that two parameters give one slot, and a slot that no parameter binds from the
    // result of S. Some slot then holds per-call Origins of S and no other input Origin, a written Type argument differs from that binding
    // only in Origins it omits, the Constraints are Proven and the substituted signature fits S. A fixed Origin cannot satisfy that slot,
    // since a per-call Origin never becomes part of a bound argument.
    private (int Slot, ulong Parameters)? PerCallReferenceSlot(Koto use, BindingSymbol symbol, FunctionKoto function, BoundType required, BindingScope scope, GenericsKoto? explicitReference, BoundType? container)
    {
        var count = function.GenericArguments.Count;
        var binder = FunctionTypeBinder(required);
        if (binder is null)
        {
            return null;
        }

        var arguments = this.typeScratch.Rent(count);
        var fromResult = this.typeScratch.Rent(count);
        var lengths = this.lengthScratch.Rent(count);
        var resultLengths = this.lengthScratch.Rent(count);
        try
        {
            if (!this.InferReferenceSlots(symbol, function, required, arguments, container, true, false, lengths))
            {
                return null;
            }

            Array.Clear(fromResult, 0, count);
            Array.Clear(resultLengths, 0, count);
            if (symbol.Type is not { } writtenResult || this.MemberType(writtenResult, container) is not { } result ||
                !this.Infer(result, required.Components[1], function, fromResult, inferOrigins: true, lengths: resultLengths, commonOrigins: true, structural: true))
            {
                return null;
            }

            (int Slot, ulong Parameters)? slot = null;
            var parameters = required.Components[0];
            for (var i = 0; i < count; i++)
            {
                if (function.GenericArguments[i] is LengthParameterKoto)
                {
                    var writtenLength = explicitReference is null ? null : this.BindLength(explicitReference.TypeArguments[i], scope);
                    if ((lengths[i] ??= resultLengths[i] ?? writtenLength) is not { } argumentLength ||
                        (explicitReference is not null && !ReferenceEquals(writtenLength, argumentLength)))
                    {
                        return null;
                    }

                    continue;
                }

                var written = explicitReference?.TypeArguments[i].BoundType;
                if ((arguments[i] ??= fromResult[i] ?? written) is not { } argument ||
                    (explicitReference is not null && (written is null || !SameExceptOmittedOrigins(written, argument, 0))))
                {
                    return null;
                }

                var perCall = 0UL;
                var foreign = false;
                InputOrigins(argument, parameters, binder, use, false, ref perCall, ref foreign, 0);
                if (foreign)
                {
                    return null;
                }

                if (perCall != 0 && slot is null)
                {
                    slot = (i, perCall);
                }
            }

            return slot is { } found && this.ReferenceArgumentProof(function, arguments, scope, container, lengths) == ConstraintProof.Proven &&
                this.FunctionReferenceFits(use, symbol, function, required, scope, boundArguments: arguments, presolved: true, boundLengths: lengths) ? (found.Slot, found.Parameters) : null;
        }
        finally
        {
            this.typeScratch.Return(fromResult, clearArray: true);
            this.typeScratch.Return(arguments, clearArray: true);
            this.lengthScratch.Return(resultLengths, clearArray: true);
            this.lengthScratch.Return(lengths, clearArray: true);
        }
    }

    // A substitution must be a valid complete Type, and the Constraints hold for it, as for a call (SPEC 8.1.3, 10.1 step 5).
    private ConstraintProof ReferenceArgumentProof(FunctionKoto function, BoundType?[] arguments, BindingScope scope, BoundType? container, BoundLength?[]? lengths = null, Koto? failureUse = null)
    {
        if (failureUse is not null)
        {
            this.referenceConstraints?.Remove(failureUse);
        }

        var count = function.GenericArguments.Count;
        var outer = this.BeginProof(); // A reference's applicability is one judgment, whose U fact its record reads (design-r2 Amendment 5).
        var proof = this.CheckConstraints(function.TypeConstraints, function, arguments.AsSpan(0, count), scope, declaringType: container, lengths: lengths.AsSpan(0, lengths is null ? 0 : count), failureUse: failureUse);
        proof = CombineProof(proof, this.ProveMemberConditions(function.BoundSymbol!, container, scope, failureUse), true);
        proof = CombineProof(proof, this.CheckSignatureTypeConstraints(function), true);
        if (container is not null)
        {
            proof = CombineProof(proof, this.CheckTypeConstraints(container, scope), true);
        }

        for (var i = 0; i < count; i++)
        {
            var argumentProof = function.GenericArguments[i] is LengthParameterKoto
                ? lengths?[i] is { } length && this.ProveLength(length, scope.Function) ? ConstraintProof.Proven : ConstraintProof.Refuted
                : this.CheckTypeConstraints(arguments[i]!, scope);
            proof = CombineProof(proof, argumentProof, true);
        }

        // Length expressions in a declaration signature may defer formation until its arguments are fixed. A reference
        // binds them now, just as a direct call does; its mere creation cannot publish a signature with an invalid length.
        if (lengths is not null && ItemTypeArgumentCount(function) != count)
        {
            proof = CombineProof(proof, Formed(function.BoundSymbol?.Type), true);
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                proof = CombineProof(proof, Formed(function.Parameters[i].Type.BoundType), true);
            }
        }

        return this.EndProof(outer, proof);

        ConstraintProof Formed(BoundType? written)
            => written is not null && this.MemberType(written, container) is { } member &&
                this.SubstituteType(member, function, arguments.AsSpan(0, count), lengths.AsSpan(0, count)) is { } bound && this.ProveTypeLengths(bound, scope.Function)
                ? ConstraintProof.Proven : ConstraintProof.Refuted;
    }

    // With `presolved`, `boundArguments` already holds the slots and only the substituted signature is checked against S.
    private bool FunctionReferenceFits(Koto use, BindingSymbol symbol, FunctionKoto function, BoundType required, BindingScope scope, Span<BoundArgumentOperation> operations = default, BoundType?[]? boundArguments = null, bool presolved = false, BoundLength?[]? boundLengths = null)
        => this.FunctionReferenceApplicability(use, symbol, function, required, scope, out _, operations, boundArguments, presolved, boundLengths) == CandidateApplicability.Applicable;

    private CandidateApplicability FunctionReferenceApplicability(Koto use, BindingSymbol symbol, FunctionKoto function, BoundType required, BindingScope scope, out ReferenceConstraintFailure? failure, Span<BoundArgumentOperation> operations = default, BoundType?[]? boundArguments = null, bool presolved = false, BoundLength?[]? boundLengths = null)
    {
        failure = null;
        var parameters = required.Components[0];
        var explicitReference = KotoHelper.UnwrapParentheses(use) as GenericsKoto;
        if (parameters.Components.Count != function.Parameters.Count ||
            (explicitReference is not null && explicitReference.TypeArguments.Count != function.GenericArguments.Count))
        {
            return CandidateApplicability.Inapplicable;
        }

        var generic = function.GenericArguments.Count;
        var container = this.ReferenceContainer(use, symbol);
        var arguments = generic == 0 ? null : boundArguments ?? this.typeScratch.Rent(generic);
        var lengths = generic == 0 ? null : boundLengths ?? this.lengthScratch.Rent(generic);
        var origins = this.originScratch.Rent(function.Origins.Count);
        var inputCount = InputOriginCount(function);
        var inputs = this.originScratch.Rent(inputCount);
        Array.Clear(origins, 0, function.Origins.Count);
        Array.Clear(inputs, 0, inputCount);
        try
        {
            var proof = ConstraintProof.Proven;
            if (arguments is not null && !presolved && !this.BindReferenceArguments(use, symbol, function, required, arguments, scope, explicitReference, container, lengths!, out proof))
            {
                if (proof == ConstraintProof.Proven)
                {
                    return CandidateApplicability.Inapplicable;
                }
            }
            else if (!this.ContractFits(use, symbol, function, required, container, null, arguments, operations, origins, inputs, lengths: lengths))
            {
                return CandidateApplicability.Inapplicable;
            }

            // A bound enclosing Type can supply conditions even when the function has no own slots.
            // Keep Unknown distinct from Refuted so another reference cannot win by missing proof.
            if (arguments is null && !presolved)
            {
                proof = this.ReferenceArgumentProof(function, [], scope, container);
            }

            if (proof is ConstraintProof.Refuted or ConstraintProof.Unknown)
            {
                failure = this.CandidateConstraintFailure(function, arguments ?? [], lengths ?? [], scope, null, container, proof) is { } fact
                    ? fact with { Declaration = null } : null;
            }

            return proof switch
            {
                ConstraintProof.Proven => CandidateApplicability.Applicable,
                ConstraintProof.Refuted => CandidateApplicability.Inapplicable,
                ConstraintProof.Error => CandidateApplicability.Error,
                _ => CandidateApplicability.Pending,
            };
        }
        finally
        {
            this.originScratch.Return(inputs, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
            if (arguments is not null && !ReferenceEquals(arguments, boundArguments))
            {
                this.typeScratch.Return(arguments, clearArray: true);
            }

            if (lengths is not null && !ReferenceEquals(lengths, boundLengths))
            {
                this.lengthScratch.Return(lengths, clearArray: true);
            }
        }
    }
}
