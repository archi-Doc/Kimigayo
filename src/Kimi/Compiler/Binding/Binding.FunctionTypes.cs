// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 10.6: the last reference slot that only per-call Origins of S would satisfy, as (Type parameter, parameters of S as bits,
    // whether S's own positions bind every slot, so that a wrapper's call infers them), and why the last candidate's slots did not bind
    // from S, with the slot when one is known; BindFunctionReference clears both before it checks a candidate and reads them for a single one.
    private (int Slot, ulong Parameters, bool Inferable)? perCallReferenceSlot;

    private (int Slot, ReferenceSlotFailure Failure) referenceSlotFailure;

    // SPEC 10.7, 15.3.7: `own` is the declaration whose own per-call inputs the implementation's signature names, if any.
    internal static bool CallableSignatureFits(BoundType actual, BoundType expected, Koto? own) => FitsTypeCore(actual, expected, null, null, own: own);

    // The binder of a Function Type's own per-call inputs: its syntax, which quantifies an outer input Origin at that input's slot.
    internal static Koto? FunctionTypeBinder(BoundType signature)
    {
        var inputs = signature.Components[0];
        for (var i = 0; i < inputs.Components.Count; i++)
        {
            if (inputs.Components[i].Origin is { Kind: OriginKind.Input, Occurrence: null, Binder: FunctionTypeKoto binder } origin && origin.Slot == i)
            {
                return binder;
            }
        }

        return null;
    }

    // The declaration whose own per-call inputs the signature of a Function Item or closure names (SPEC 8.6).
    internal static Koto? SignatureOwner(BoundType callee)
    {
        var owner = callee is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } borrowed ? borrowed.Components[0] : callee;
        return owner is { Kind: BoundTypeKind.FunctionItem or BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto declaration } ? declaration : null;
    }

    // SPEC 15.6.4: fresh per-call inputs and a result whose Origins, if any, are all static, so a call substitutes nothing.
    private static bool PerCallSignature(BoundType signature, Koto? own, bool any = false)
        => (!signature.Components[1].CarriesOrigin || StaticOnly(signature.Components[1])) && PerCallInputs(signature, own, any);

    private static bool StaticOnly(BoundType type)
    {
        if (!StaticOrigin(type.Origin))
        {
            return false;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (!StaticOrigin(type.OriginArguments[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (!StaticOnly(type.Components[i]))
            {
                return false;
            }
        }

        return true;

        static bool StaticOrigin(BoundOrigin? origin)
        {
            if (origin is null || origin.Kind == OriginKind.Static)
            {
                return true;
            }

            if (origin.Kind != OriginKind.Intersection)
            {
                return false;
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (!StaticOrigin(origin.Operands[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    // SPEC 15.6.4: the binder of a signature whose inputs are fresh per-call borrows and whose result depends on those inputs
    // or on static storage alone; a call substitutes the arguments' Origins into that result.
    private static Koto? InputDependentBinder(BoundType signature, Koto? own, bool any = false)
    {
        if (!PerCallInputs(signature, own, any))
        {
            return null;
        }

        var inputs = signature.Components[0];
        Koto? binder = null;
        for (var i = 0; i < inputs.Components.Count; i++)
        {
            if (inputs.Components[i].Origin is { Kind: OriginKind.Input } origin)
            {
                if (binder is not null && !ReferenceEquals(binder, origin.Binder))
                {
                    return null;
                }

                binder = origin.Binder;
            }
        }

        return binder is not null && OverInputs(signature.Components[1], binder, inputs.Components.Count) ? binder : null;

        static bool OverInputs(BoundType part, Koto binder, int count)
        {
            if (!OverInput(part.Origin, binder, count))
            {
                return false;
            }

            for (var i = 0; i < part.OriginArguments.Count; i++)
            {
                if (!OverInput(part.OriginArguments[i], binder, count))
                {
                    return false;
                }
            }

            for (var i = 0; i < part.Components.Count; i++)
            {
                if (!OverInputs(part.Components[i], binder, count))
                {
                    return false;
                }
            }

            return true;
        }

        static bool OverInput(BoundOrigin? origin, Koto binder, int count)
        {
            if (origin is null || origin.Kind == OriginKind.Static)
            {
                return true;
            }

            if (origin.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < origin.Operands.Count; i++)
                {
                    if (!OverInput(origin.Operands[i], binder, count))
                    {
                        return false;
                    }
                }

                return true;
            }

            return origin.Kind == OriginKind.Input && ReferenceEquals(origin.Binder, binder) && origin.Slot < count;
        }
    }

    // SPEC 8.6, 15.6.4: every Origin-bearing input is a direct borrow over its own per-call input, an Origin that the signature's
    // own binder quantifies: a Function Type's own syntax or the declaration `own`. Any other Origin is fixed, even at its own slot.
    // An expected signature (`any`) may take a fixed Origin for a per-call one, which only asks more of the implementation.
    private static bool PerCallInputs(BoundType signature, Koto? own, bool any = false)
    {
        var inputs = signature.Components[0];
        for (var i = 0; i < inputs.Components.Count; i++)
        {
            var input = inputs.Components[i];
            if (input.CarriesOrigin && !(input is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1, OriginArguments.Count: 0, Origin.Kind: OriginKind.Input } &&
                input.Origin.Slot == i && (any || input.Origin.Binder is FunctionTypeKoto || (own is not null && ReferenceEquals(input.Origin.Binder, own))) &&
                !input.Components[0].CarriesOrigin))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 10.5: the input Origins a slot binding holds. `perCall` collects the parameters of S whose per-call Origin, the outer Origin
    // of a direct input that S quantifies, the binding holds, alone or in a meet; `foreign` is any other input Origin, except one that a
    // Function Type inside the binding quantifies itself (an inner per-call Origin never escapes its Function Type) and an input Origin
    // of a function enclosing the reference, which is fixed in its body (SPEC 15.6.5), so a bound argument may hold it.
    private static void InputOrigins(BoundType type, BoundType parameters, Koto? binder, Koto use, bool nested, ref ulong perCall, ref bool foreign, int depth)
    {
        if (depth > 64)
        {
            foreign = true;
            return;
        }

        Origin(type.Origin, parameters, binder, use, nested, ref perCall, ref foreign, 0);
        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            Origin(type.OriginArguments[i], parameters, binder, use, nested, ref perCall, ref foreign, 0);
        }

        nested |= type.Kind == BoundTypeKind.Function;
        for (var i = 0; i < type.Components.Count; i++)
        {
            InputOrigins(type.Components[i], parameters, binder, use, nested, ref perCall, ref foreign, depth + 1);
        }

        static void Origin(BoundOrigin? origin, BoundType parameters, Koto? binder, Koto use, bool nested, ref ulong perCall, ref bool foreign, int depth)
        {
            if (origin is null)
            {
                return;
            }

            if (origin.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < origin.Operands.Count && depth < 64; i++)
                {
                    Origin(origin.Operands[i], parameters, binder, use, nested, ref perCall, ref foreign, depth + 1);
                }

                return;
            }

            if (origin.Kind != OriginKind.Input)
            {
                return;
            }

            if (binder is not null && ReferenceEquals(origin.Binder, binder) && origin.Occurrence is null && (uint)origin.Slot < (uint)Math.Min(parameters.Components.Count, 64) &&
                ReferenceEquals(parameters.Components[origin.Slot].Origin, origin))
            {
                perCall |= 1UL << origin.Slot;
            }
            else if (!(nested && origin.Binder is FunctionTypeKoto && !ReferenceEquals(origin.Binder, binder)) && !(origin.Binder is FunctionKoto owner && IsWithin(use, owner)))
            {
                foreign = true;
            }
        }
    }

    // SPEC 15.4.4: whether a written Type argument is the binding except in Origins it omits, which local inference would solve.
    private static bool SameExceptOmittedOrigins(BoundType written, BoundType binding, int depth)
    {
        if (ReferenceEquals(written, binding))
        {
            return true;
        }

        if (depth > 32 || written.Kind != binding.Kind || !ReferenceEquals(written.Symbol, binding.Symbol) || written.Semantics != binding.Semantics ||
            written.Length != binding.Length || !ReferenceEquals(written.LengthExpression, binding.LengthExpression) || !ReferenceEquals(written.ClosureContext, binding.ClosureContext) || !written.LengthArguments.AsSpan().SequenceEqual(binding.LengthArguments) ||
            written.Components.Count != binding.Components.Count || written.OriginArguments.Count != binding.OriginArguments.Count || !Omitted(written.Origin, binding.Origin))
        {
            return false;
        }

        for (var i = 0; i < written.OriginArguments.Count; i++)
        {
            if (!Omitted(written.OriginArguments[i], binding.OriginArguments[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < written.Components.Count; i++)
        {
            if (!SameExceptOmittedOrigins(written.Components[i], binding.Components[i], depth + 1))
            {
                return false;
            }
        }

        return true;

        static bool Omitted(BoundOrigin? written, BoundOrigin? binding) => ReferenceEquals(written, binding) || written?.Kind == OriginKind.Inference;
    }

    // Members of Origin-bearing containers, requirements referenced through a constrained Type (SPEC 10.5, `T.compare`),
    // generic functions that the compiler implements are not yet referenced; an instance
    // member is referenced unbound, through its Type (SPEC 7.3), and a generic container is named with its Type arguments.
    private static bool UnsupportedReference(BindingSymbol candidate, FunctionKoto function, bool unbound, BoundType? declaringType)
    {
        if ((candidate.ReceiverIndex >= 0 && !unbound) || candidate.Scope.Owner is ContractKoto || candidate.Scope.Owner.BoundSymbol?.Schema is { Origins.Count: > 0 } ||
            (candidate.Scope.Owner.BoundSymbol?.Schema is { GenericSlots.Count: > 0 } && !ContainerBound(candidate, declaringType)))
        {
            return true;
        }

        if (function.GenericArguments.Count != 0 && (candidate.Intrinsic != IntrinsicKind.None || candidate.CompilerFunction != CompilerFunctionKind.None))
        {
            return true;
        }

        for (var i = 0; i < function.GenericArguments.Count; i++)
        {
            if (function.GenericArguments[i] is not (GenericParameterKoto or LengthParameterKoto))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 15.8.2, 15.6.4, 7.6.3: a closure call's result over its environment depends on the call receiver, the closure Place the call
    // lends. In a Shared or Exclusive call, a borrow of an environment binding becomes a borrow of the receiver (its Place Origin), and an
    // external Origin reached through a Non-Copy capture, such as a Reborrow of a captured exclusive reference, or kept by a Non-Copy
    // result, also stays within the receiver; a Copy of a captured shared reference keeps its external Origin. A Consuming call consumes
    // the environment: borrows of it were Refuted at the closure (SPEC 15.8.2), and a reference it moves out keeps its own Origin.
    // Null when some Origin cannot be expressed; the caller keeps that call a located limit.
    private BoundType? ReceiverDependentResult(BoundType result, Koto receiver, SemanticsKind kind, Koto use)
    {
        var type = receiver.BoundType!;
        type = type.Kind == BoundTypeKind.Semantics ? type.Components[0] : type;
        if (type is not { Kind: BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto { BoundClosure: { } closure } function })
        {
            return null;
        }

        // A call on the closure literal itself borrows that literal's temporary, which its own Place Origin names.
        var place = kind == SemanticsKind.Owner ? null
            : ReferenceEquals(KotoHelper.UnwrapParentheses(receiver), function) ? this.OriginAtom(function, OriginKind.Projection, ClosureValueSlot)
            : this.PlaceOrigin(receiver);
        var context = new ReceiverContext(function, closure, place, this.ProveCopy(result, receiver) == ConstraintProof.Proven, use);
        var mapped = this.ReceiverType(result, context, out var failed);
        return failed ? null : mapped;
    }

    private BoundType ReceiverType(BoundType type, in ReceiverContext context, out bool failed)
    {
        failed = false;
        if (!type.CarriesOrigin)
        {
            return type;
        }

        var origin = type.Origin is { } outer ? this.ReceiverOrigin(outer, context, ref failed) : null;
        var components = this.RentTypes(type.Components.Count);
        var origins = this.originScratch.Rent(type.OriginArguments.Count);
        try
        {
            var changed = !ReferenceEquals(origin, type.Origin);
            for (var i = 0; i < type.Components.Count && !failed; i++)
            {
                components[i] = type.Kind == BoundTypeKind.Function ? type.Components[i] : this.ReceiverType(type.Components[i], context, out failed);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            for (var i = 0; i < type.OriginArguments.Count && !failed; i++)
            {
                origins[i] = this.ReceiverOrigin(type.OriginArguments[i], context, ref failed);
                changed |= !ReferenceEquals(origins[i], type.OriginArguments[i]);
            }

            return failed || !changed ? type
                : this.InternType(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, type.Components.Count), type.Length, origin, origins.AsSpan(0, type.OriginArguments.Count), type.LengthExpression, type.ClosureContext, type.LengthArguments);
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
    }

    private BoundOrigin ReceiverOrigin(BoundOrigin origin, in ReceiverContext context, ref bool failed)
    {
        if (origin.Kind == OriginKind.Static)
        {
            return origin;
        }

        if (origin.Kind == OriginKind.Intersection && origin.Operands.Count != 0)
        {
            var meet = this.ReceiverOrigin(origin.Operands[0], context, ref failed);
            for (var i = 1; i < origin.Operands.Count; i++)
            {
                meet = this.Meet(meet, this.ReceiverOrigin(origin.Operands[i], context, ref failed));
            }

            return meet;
        }

        if (ReferenceEquals(origin.Binder, context.Function))
        {
            if (origin.Kind == OriginKind.Input)
            {
                return origin; // A per-call input, substituted by the call's argument afterwards.
            }

            failed |= context.Receiver is null || origin.Kind != OriginKind.Projection || origin.Slot > EnvironmentSlot(0);
            return context.Receiver ?? origin;
        }

        var carried = false;
        var lent = !context.Copy;
        for (var i = 0; i < context.Closure.Captures.Count; i++)
        {
            var captured = context.Closure.Captures[i].Environment;
            if (captured.Type is { } carrier && ContainsOrigin(carrier, origin))
            {
                carried = true;
                lent |= captured.CaptureAcquisition is not CaptureAcquisition.Copy || this.ProveCopy(carrier, context.Use) != ConstraintProof.Proven;
            }
        }

        if (!carried)
        {
            failed |= !FixedInBodyOrigin(origin, context.Use);
            return origin;
        }

        return lent && context.Receiver is { } receiver ? this.Meet(origin, receiver) : origin;
    }

    // SPEC 15.6.4, 15.8.2: a value call whose result names none of the callee's per-call inputs keeps that result as written when
    // it is independent of the call: each Origin is static, carried only by Copy captures of a Copy result (a Copy of a captured
    // shared reference keeps its external Origin), or, carried by no capture, a fixed Origin of a function enclosing the call. A Non-Copy
    // carrier would need a receiver-dependent reborrow (G65), and equal lifetime names never prove which capture supplied the result.
    private bool ResultFixedForCall(BoundType result, Koto receiver, Koto use)
    {
        var type = receiver.BoundType!;
        type = type.Kind == BoundTypeKind.Semantics ? type.Components[0] : type;
        if (type.Kind != BoundTypeKind.Closure)
        {
            return FixedInBody(result, use);
        }

        if (type.Symbol?.Declaration is not FunctionKoto { BoundClosure: { } closure })
        {
            return false;
        }

        var copy = this.ProveCopy(result, receiver) == ConstraintProof.Proven;
        return FixedType(result);

        bool FixedType(BoundType part)
        {
            if (!FixedOrigin(part.Origin))
            {
                return false;
            }

            for (var i = 0; i < part.OriginArguments.Count; i++)
            {
                if (!FixedOrigin(part.OriginArguments[i]))
                {
                    return false;
                }
            }

            for (var i = 0; i < part.Components.Count; i++)
            {
                if (!FixedType(part.Components[i]))
                {
                    return false;
                }
            }

            return true;
        }

        bool FixedOrigin(BoundOrigin? origin)
        {
            if (origin is null || origin.Kind == OriginKind.Static)
            {
                return true;
            }

            if (origin.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < origin.Operands.Count; i++)
                {
                    if (!FixedOrigin(origin.Operands[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            var carried = false;
            for (var i = 0; i < closure.Captures.Count; i++)
            {
                var captured = closure.Captures[i].Environment.Type!;
                if (ContainsOrigin(captured, origin))
                {
                    if (this.ProveCopy(captured, receiver) != ConstraintProof.Proven)
                    {
                        return false;
                    }

                    carried = true;
                }
            }

            return carried ? copy : FixedInBodyOrigin(origin, use);
        }
    }

    private BoundType? BindFunctionReference(Koto use, BindingSymbol symbol, BoundType required, BindingScope scope, bool erase = true)
    {
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
        (int Slot, ulong Parameters, bool Inferable)? perCall = null;
        (int Slot, ReferenceSlotFailure Failure) slotFailure = default;
        try
        {
            var index = 0;
            var applicable = 0;
            for (var candidate = ReferenceCandidate(symbol); candidate is not null; candidate = ReferenceCandidate(candidate.Next), index++)
            {
                this.BindHeader(candidate);
                if (candidate.Declaration is not FunctionKoto function || UnsupportedReference(candidate, function, unbound, declaring))
                {
                    return this.Fail(use, BindingFailure.Unsupported);
                }

                this.perCallReferenceSlot = null;
                this.referenceSlotFailure = default;
                var fits = this.Accessible(candidate, scope) && this.FunctionReferenceFits(use, candidate, function, required, scope, operations.AsSpan(index * stride, stride - 1));
                if (count == 1)
                {
                    perCall = this.perCallReferenceSlot;
                    slotFailure = this.referenceSlotFailure;
                }

                evaluated[index] = new(candidate, fits ? CandidateApplicability.Applicable : CandidateApplicability.Inapplicable, null, 0);
                if (fits)
                {
                    applicable++;
                }
            }

            var winner = SelectBest(evaluated.AsSpan(0, count), operations, stride);
            if (winner < 0)
            {
                if (perCall is { } slot)
                {
                    // SPEC 10.6, 15.3.6: the one candidate's slot is left unsolved by a per-call Origin of S. The advised wrapper exists
                    // only when its call infers every slot from S's positions, and it passes each by-value parameter of S that is not
                    // proven Copy with @move, since a bare Place never moves (SPEC 3.5, 10.1).
                    var moves = slot.Inferable ? this.MovedReferenceInputs(required, use) : null;
                    return this.FailExplained(ref this.perCallSlots, use, BindingFailure.MissingOrigin, new PerCallSlotFact((FunctionKoto)symbol.Declaration, slot.Slot, slot.Parameters, required, moves));
                }

                if (slotFailure.Failure != ReferenceSlotFailure.None && symbol.Declaration is FunctionKoto { GenericArguments.Count: > 0 } generic)
                {
                    // SPEC 10.5: the Type-mismatch Note names why the one generic candidate's slots did not bind from S.
                    (this.referenceSlotFacts ??= new(ReferenceEqualityComparer.Instance))[use] = new(generic, slotFailure.Slot, slotFailure.Failure, KotoHelper.UnwrapParentheses(use) is GenericsKoto);
                }

                var candidateItem = this.InternType(BoundTypeKind.FunctionItem, symbol, SemanticsKind.Owner, []);
                if (count == 1 && this.FunctionItemSignature(candidateItem) is { } actual)
                {
                    // SPEC 10.7, 15.6.1: when the one candidate's signature matches and only its Origin contract fails, the record names
                    // that contract, with the candidate's own inputs instantiated: a member, else a condition of the candidate.
                    return ReferenceTypes.StorageMatches(required, actual) &&
                        this.ItemContractFailure(candidateItem, actual, required, use, use) is { } contract
                        ? this.FailExplained(ref this.originContracts, use, BindingFailure.OriginContract, contract)
                        : this.FailMismatch(use, use, actual, required);
                }

                var rejected = new RejectedCandidate[applicable == 0 ? count : applicable];
                var next = 0;
                for (var i = 0; i < count; i++)
                {
                    if (applicable == 0 || evaluated[i].State == CandidateApplicability.Applicable)
                    {
                        var candidate = evaluated[i].Symbol;
                        var item = this.InternType(BoundTypeKind.FunctionItem, candidate, SemanticsKind.Owner, []);
                        rejected[next++] = new((FunctionKoto)candidate.Declaration, this.FunctionItemSignature(item), required, CallableSignature: true, ReferenceSignature: true);
                    }
                }

                (this.rejectedCandidates ??= new(ReferenceEqualityComparer.Instance))[use] = rejected;
                return this.Fail(use, applicable == 0 ? BindingFailure.NoApplicableCandidate : BindingFailure.Ambiguous);
            }

            selected = evaluated[winner].Symbol;
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
            // The selected generic Item keeps its bound arguments; at a common Function Type it is then erased (SPEC 7.6.4).
            var arguments = this.typeScratch.Rent(target.GenericArguments.Count);
            var lengths = this.lengthScratch.Rent(target.GenericArguments.Count);
            try
            {
                if (!this.FunctionReferenceFits(use, selected, target, required, scope, boundArguments: arguments, boundLengths: lengths))
                {
                    return this.Fail(use, BindingFailure.Unsupported, true);
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
                    return this.FailMismatch(use, use, item, required);
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

        // The reference is its Function Item; at a common Function Type that Item is erased (SPEC 7.6.4), whatever syntax names it.
        var concrete = this.CompleteFunctionItem(use, selected, default, this.ReferenceContainer(use, selected));
        if (!erase)
        {
            return concrete;
        }

        if (!this.ErasesToFunction(use, concrete, required))
        {
            return this.FailMismatch(use, use, concrete, required);
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
            if (candidate.Declaration is not FunctionKoto function || UnsupportedReference(candidate, function, unbound, declaring))
            {
                return true;
            }

            if (this.Accessible(candidate, scope) && this.FunctionReferenceFits(use, candidate, function, required, scope))
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
    private bool BindReferenceArguments(Koto use, BindingSymbol symbol, FunctionKoto function, BoundType required, BoundType?[] arguments, BindingScope scope, GenericsKoto? explicitReference, BoundType? container, BoundLength?[] lengths)
    {
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
                if (ReferenceSlotsComplete(function, arguments, lengths) && this.ReferenceArgumentProof(function, arguments, scope, container, lengths) != ConstraintProof.Proven)
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

        var proof = this.ReferenceArgumentProof(function, arguments, scope, container, lengths);
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

                if (!(FitsType(written, bound) && FitsType(bound, written)) && !(commonOrigins && this.CommonOriginType(written, bound) is not null))
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
        }
    }

    // SPEC 3.5, 10.1: the parameters of S, as bits, that an anonymous function calling the reference passes with @move: a by-value
    // parameter not proven Copy, since a bare Place never moves; null when S has more parameters than the bits hold.
    private ulong? MovedReferenceInputs(BoundType required, Koto use)
    {
        var inputs = required.Components[0].Components;
        if (inputs.Count > 64)
        {
            return null;
        }

        var moves = 0UL;
        for (var i = 0; i < inputs.Count; i++)
        {
            if (inputs[i].Semantics is SemanticsKind.Owner or SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc && this.ProveCopy(inputs[i], use) != ConstraintProof.Proven)
            {
                moves |= 1UL << i;
            }
        }

        return moves;
    }

    // SPEC 10.6, 15.3.6: the slot that only per-call Origins of S would satisfy. The slots are bound as a call of the reference would bind
    // them: from the parameters of S, meeting the Origins that two parameters give one slot, and a slot that no parameter binds from the
    // result of S. Some slot then holds per-call Origins of S and no other input Origin, a written Type argument differs from that binding
    // only in Origins it omits, the Constraints are Proven and the substituted signature fits S. A fixed Origin cannot satisfy that slot,
    // since a per-call Origin never becomes part of a bound argument, while an anonymous function that calls the reference binds it; that
    // call infers the slots only when S's positions bind each of them, not a written Type argument alone (`Inferable`).
    private (int Slot, ulong Parameters, bool Inferable)? PerCallReferenceSlot(Koto use, BindingSymbol symbol, FunctionKoto function, BoundType required, BindingScope scope, GenericsKoto? explicitReference, BoundType? container)
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

            (int Slot, ulong Parameters, bool Inferable)? slot = null;
            var inferable = true;
            var parameters = required.Components[0];
            for (var i = 0; i < count; i++)
            {
                if (function.GenericArguments[i] is LengthParameterKoto)
                {
                    var writtenLength = explicitReference is null ? null : this.BindLength(explicitReference.TypeArguments[i], scope);
                    inferable &= lengths[i] is not null || resultLengths[i] is not null;
                    if ((lengths[i] ??= resultLengths[i] ?? writtenLength) is not { } argumentLength ||
                        (explicitReference is not null && !ReferenceEquals(writtenLength, argumentLength)))
                    {
                        return null;
                    }

                    continue;
                }

                var written = explicitReference?.TypeArguments[i].BoundType;
                inferable &= arguments[i] is not null || fromResult[i] is not null;
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
                    slot = (i, perCall, false);
                }
            }

            return slot is { } found && this.ReferenceArgumentProof(function, arguments, scope, container, lengths) == ConstraintProof.Proven &&
                this.FunctionReferenceFits(use, symbol, function, required, scope, boundArguments: arguments, presolved: true, boundLengths: lengths) ? (found.Slot, found.Parameters, inferable) : null;
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
    private ConstraintProof ReferenceArgumentProof(FunctionKoto function, BoundType?[] arguments, BindingScope scope, BoundType? container, BoundLength?[]? lengths = null)
    {
        var count = function.GenericArguments.Count;
        var proof = this.CheckConstraints(function.TypeConstraints, function, arguments.AsSpan(0, count), scope, declaringType: container, lengths: lengths.AsSpan(0, lengths is null ? 0 : count));
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

        return proof;

        ConstraintProof Formed(BoundType? written)
            => written is not null && this.MemberType(written, container) is { } member &&
                this.SubstituteType(member, function, arguments.AsSpan(0, count), lengths.AsSpan(0, count)) is { } bound && this.ProveTypeLengths(bound, scope.Function)
                ? ConstraintProof.Proven : ConstraintProof.Refuted;
    }

    // With `presolved`, `boundArguments` already holds the slots and only the substituted signature is checked against S.
    private bool FunctionReferenceFits(Koto use, BindingSymbol symbol, FunctionKoto function, BoundType required, BindingScope scope, Span<BoundArgumentOperation> operations = default, BoundType?[]? boundArguments = null, bool presolved = false, BoundLength?[]? boundLengths = null)
    {
        var parameters = required.Components[0];
        var explicitReference = KotoHelper.UnwrapParentheses(use) as GenericsKoto;
        if (parameters.Components.Count != function.Parameters.Count ||
            (explicitReference is not null && explicitReference.TypeArguments.Count != function.GenericArguments.Count))
        {
            return false;
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
            if (arguments is not null && !presolved && !this.BindReferenceArguments(use, symbol, function, required, arguments, scope, explicitReference, container, lengths!))
            {
                return false;
            }

            return this.ContractFits(use, symbol, function, required, container, null, arguments, operations, origins, inputs, lengths: lengths);
        }
        finally
        {
            this.originScratch.Return(origins, clearArray: true);
            this.originScratch.Return(inputs, clearArray: true);
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

    // SPEC 10.7, 15.3.7: the implementation's per-call Origins are solved against the required signature, whose quantifiers and fixed
    // Origins stay rigid, under its declared relations and result premises; then the required inputs fit the implementation's, its
    // result fits the required one, and its conditions are proven for the solution. The implementation's Types are its members within
    // `container` with `arguments` bound, or those of the Function Item `item`.
    private bool ContractFits(Koto use, BindingSymbol symbol, FunctionKoto function, BoundType required, BoundType? container, BoundType? item, BoundType?[]? arguments, Span<BoundArgumentOperation> operations, BoundOrigin[] origins, BoundOrigin[] inputs, bool explain = false, BoundLength?[]? lengths = null)
    {
        var parameters = required.Components[0];
        var generic = function.GenericArguments.Count;
        var inputCount = InputOriginCount(function);
        var inference = this.BeginOriginInference(use, function);
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (function.Parameters[i].Type.BoundType is not { } written || Bound(written) is not { } parameter)
            {
                return false;
            }

            // Only the implementation's per-call binders are inferred; the
            // required signature's quantifiers and fixed Origins remain rigid.
            this.MatchInputOrigins(parameter, parameters.Components[i], function, origins, inputs);
            this.CollectOriginInference(parameter, parameters.Components[i], inference);
        }

        if (symbol.Type is { } writtenResult && Bound(writtenResult) is { } produced)
        {
            this.CollectOriginInference(produced, required.Components[1], inference, result: true);
        }

        if (!this.SolveCallOriginInference(function, inference, origins, inputs, use, null, select: explain))
        {
            return false;
        }

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var parameter = Substitute(Bound(function.Parameters[i].Type.BoundType!)!);
            if (HasUnsubstitutedOrigin(parameter, function) || !this.FitsTypeAt(parameters.Components[i], parameter, use))
            {
                return false;
            }

            if (!operations.IsEmpty)
            {
                // Compare the same substituted contract that established applicability. In particular,
                // independently named per-call Origins are fixed to the required signature before ranking.
                // References insert no adaptations and use no defaults; results never rank candidates.
                operations[i] = new(null, null, parameter, ArgumentOperationKind.Value, ArgumentAdaptation.Exact);
            }
        }

        return this.CheckCallOriginRelations(function, origins, inputs, use, null) &&
            symbol.Type is { } result && Bound(result) is { } boundResult &&
            !HasUnsubstitutedOrigin(result = Substitute(boundResult), function) && this.FitsTypeAt(result, required.Components[1], use) &&
            this.ProvesResultPremises(function, result, use);

        BoundType? Bound(BoundType type)
            => item is not null ? this.ItemType(type, item, function)
            : this.MemberType(type, container) is not { } member ? null : arguments is null ? member : this.SubstituteType(member, function, arguments.AsSpan(0, generic), lengths.AsSpan(0, lengths is null ? 0 : generic));

        BoundType Substitute(BoundType type)
            => this.SubstituteStoredOrigins(type, function, origins.AsSpan(0, function.Origins.Count), inputs.AsSpan(0, inputCount));
    }

    private BoundType? BindFunctionType(FunctionTypeKoto function, BindingScope scope, TypeBindingContext context)
    {
        var tuple = function.Parameters as TupleTypeKoto;
        var count = tuple?.ElementNodes.Count ?? 1;
        var scratch = this.RentTypes(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var syntax = tuple is null ? function.Parameters : tuple.ElementNodes[i];
                if (this.BindType(syntax, scope, new(TypePosition.Parameter, function, i, true)) is not { } input)
                {
                    return null;
                }

                scratch[i] = input;
            }

            var parameters = count == 0 ? BoundType.Unit : this.InternType(BoundTypeKind.Tuple, null, SemanticsKind.Owner, scratch.AsSpan(0, count));
            if (tuple is not null)
            {
                Complete(tuple, parameters);
            }

            var result = this.BindType(function.ReturnType, scope, new(TypePosition.Result, function));
            return result is null ? null : this.InternType(BoundTypeKind.Function, null, SemanticsKind.Owner, [parameters, result]);
        }
        finally
        {
            this.typeScratch.Return(scratch, clearArray: true);
        }
    }
}
