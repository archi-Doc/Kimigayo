// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
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

    // Members of Origin-bearing containers, requirements referenced through a constrained Type (SPEC 10.5, `T.compare`),
    // length-generic functions and generic functions that the compiler implements are not yet referenced; an instance
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
            if (function.GenericArguments[i] is not GenericParameterKoto)
            {
                return true;
            }
        }

        return false;
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
        for (var candidate = symbol; candidate is not null; candidate = candidate.Next)
        {
            count++;
        }

        var evaluated = this.candidateScratch.Rent(count);
        var stride = required.Components[0].Components.Count + 1;
        var operations = this.argumentOperationScratch.Rent(count * stride);
        BindingSymbol selected;
        try
        {
            var index = 0;
            var applicable = 0;
            for (var candidate = symbol; candidate is not null; candidate = candidate.Next, index++)
            {
                this.BindHeader(candidate);
                if (candidate.Declaration is not FunctionKoto function || UnsupportedReference(candidate, function, unbound, declaring))
                {
                    return this.Fail(use, BindingFailure.Unsupported);
                }

                var fits = this.Accessible(candidate, scope) && this.FunctionReferenceFits(use, candidate, function, required, scope, operations.AsSpan(index * stride, stride - 1));
                evaluated[index] = new(candidate, fits ? CandidateApplicability.Applicable : CandidateApplicability.Inapplicable, null, 0);
                if (fits)
                {
                    applicable++;
                }
            }

            var winner = SelectBest(evaluated.AsSpan(0, count), operations, stride);
            if (winner < 0)
            {
                if (count == 1 && this.FunctionItemSignature(this.InternType(BoundTypeKind.FunctionItem, symbol, SemanticsKind.Owner, [])) is { } actual)
                {
                    // SPEC 10.7, 15.6.1: when the one candidate's signature matches and only its Origin contract fails, the record names
                    // that contract, with the candidate's own inputs instantiated.
                    return ReferenceTypes.StorageMatches(required, actual) && this.ConversionContractFailure(actual, required, symbol.Declaration, use, use) is { } contract
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
            try
            {
                if (!this.FunctionReferenceFits(use, selected, target, required, scope, boundArguments: arguments))
                {
                    return this.Fail(use, BindingFailure.Unsupported, true);
                }

                var item = this.CompleteFunctionItem(use, selected, arguments.AsSpan(0, target.GenericArguments.Count), this.ReferenceContainer(use, selected));
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
    // diagnostic (a receiver, generic-container or length-generic function) counts as fitting, so that diagnostic is
    // published after selection.
    private bool FunctionGroupFits(Koto use, BindingSymbol symbol, BoundType required, BindingScope scope)
    {
        var unbound = this.UnboundMemberReference(use);
        var declaring = this.ReferenceDeclaringType(use);
        for (var candidate = symbol; candidate is not null; candidate = candidate.Next)
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
    // Origins are left to the per-call solver, and a per-call Origin of S never becomes part of a bound argument.
    private bool BindReferenceArguments(BindingSymbol symbol, FunctionKoto function, BoundType required, BoundType?[] arguments, BindingScope scope, GenericsKoto? explicitReference, BoundType? container)
    {
        var count = function.GenericArguments.Count;
        Array.Clear(arguments, 0, count);
        if (explicitReference is not null && !this.ExplicitReferenceArguments(explicitReference, arguments))
        {
            return false;
        }

        var parameters = required.Components[0];
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (function.Parameters[i].Type.BoundType is not { } written || this.MemberType(written, container) is not { } parameter ||
                !this.Infer(parameter, parameters.Components[i], function, arguments, inferOrigins: true, structural: true))
            {
                return false;
            }
        }

        if (symbol.Type is not { } writtenResult || this.MemberType(writtenResult, container) is not { } result ||
            !this.Infer(result, required.Components[1], function, arguments, inferOrigins: true, structural: true))
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (arguments[i] is not { } argument || CarriesInputOrigin(argument))
            {
                return false;
            }
        }

        return this.ReferenceArgumentProof(function, arguments, scope, container) == ConstraintProof.Proven;

        static bool CarriesInputOrigin(BoundType type)
        {
            if (type.Origin?.Kind == OriginKind.Input)
            {
                return true;
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                if (type.OriginArguments[i].Kind == OriginKind.Input)
                {
                    return true;
                }
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (CarriesInputOrigin(type.Components[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // A substitution must be a valid complete Type, and the Constraints hold for it, as for a call (SPEC 8.1.3, 10.1 step 5).
    private ConstraintProof ReferenceArgumentProof(FunctionKoto function, BoundType?[] arguments, BindingScope scope, BoundType? container)
    {
        var count = function.GenericArguments.Count;
        var proof = this.CheckConstraints(function.TypeConstraints, function, arguments.AsSpan(0, count), scope, declaringType: container);
        proof = CombineProof(proof, this.CheckSignatureTypeConstraints(function), true);
        if (container is not null)
        {
            proof = CombineProof(proof, this.CheckTypeConstraints(container, scope), true);
        }

        for (var i = 0; i < count; i++)
        {
            proof = CombineProof(proof, this.CheckTypeConstraints(arguments[i]!, scope), true);
        }

        return proof;
    }

    private bool FunctionReferenceFits(Koto use, BindingSymbol symbol, FunctionKoto function, BoundType required, BindingScope scope, Span<BoundArgumentOperation> operations = default, BoundType?[]? boundArguments = null)
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
        var origins = this.originScratch.Rent(function.Origins.Count);
        var inputCount = InputOriginCount(function);
        var inputs = this.originScratch.Rent(inputCount);
        Array.Clear(origins, 0, function.Origins.Count);
        Array.Clear(inputs, 0, inputCount);
        try
        {
            if (arguments is not null && !this.BindReferenceArguments(symbol, function, required, arguments, scope, explicitReference, container))
            {
                return false;
            }

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

            if (!this.SolveOriginInference(inference, origins, inputs, use))
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

            if (!this.CheckCallOriginRelations(function, origins, inputs, use, null) ||
                symbol.Type is not { } result || Bound(result) is not { } boundResult ||
                HasUnsubstitutedOrigin(result = Substitute(boundResult), function) || !this.FitsTypeAt(result, required.Components[1], use))
            {
                return false;
            }

            return true;
        }
        finally
        {
            this.originScratch.Return(origins, clearArray: true);
            this.originScratch.Return(inputs, clearArray: true);
            if (arguments is not null && !ReferenceEquals(arguments, boundArguments))
            {
                this.typeScratch.Return(arguments, clearArray: true);
            }
        }

        BoundType? Bound(BoundType type)
            => this.MemberType(type, container) is not { } member ? null : arguments is null ? member : this.SubstituteType(member, function, arguments.AsSpan(0, generic));

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
