// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    internal static bool CallableSignatureFits(BoundType actual, BoundType expected) => FitsType(actual, expected);

    private static bool PerCallSignature(BoundType signature)
        => !signature.Components[1].CarriesOrigin && PerCallInputs(signature);

    private static bool PerCallInputs(BoundType signature)
    {
        var inputs = signature.Components[0];
        for (var i = 0; i < inputs.Components.Count; i++)
        {
            var input = inputs.Components[i];
            if (input.CarriesOrigin && !(input is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1, OriginArguments.Count: 0, Origin.Kind: OriginKind.Input } &&
                input.Origin.Slot == i && !input.Components[0].CarriesOrigin))
            {
                return false;
            }
        }

        return true;
    }

    private bool FixedCaptureSignature(BoundType signature, Koto receiver)
    {
        if (!PerCallInputs(signature))
        {
            return false;
        }

        var result = signature.Components[1];
        var type = receiver.BoundType!;
        type = type.Kind == BoundTypeKind.Semantics ? type.Components[0] : type;
        return type.Kind == BoundTypeKind.Closure && type.Symbol?.Declaration is FunctionKoto { BoundClosure: { } closure } &&
            this.ProveCopy(result, receiver) == ConstraintProof.Proven && FixedType(result);

        // A concrete Copy result may retain shared external Origins already carried by Copy captures. No per-call
        // substitution or receiver-storage Loan is needed; erased, exclusive and receiver-dependent results stay closed.
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

            var found = false;
            for (var i = 0; i < closure.Captures.Count; i++)
            {
                var captured = closure.Captures[i].Environment.Type!;
                if (ContainsOrigin(captured, origin))
                {
                    // Equal lifetime names do not prove which capture supplied the result. A Non-Copy carrier may
                    // require a receiver-dependent reborrow even when another capture carries the same Origin.
                    if (this.ProveCopy(captured, receiver) != ConstraintProof.Proven)
                    {
                        return false;
                    }

                    found = true;
                }
            }

            return found;
        }
    }

    private BoundType? BindFunctionReference(Koto use, BindingSymbol symbol, BoundType required, BindingScope scope, bool erase = true)
    {
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
                if (candidate.Declaration is not FunctionKoto function ||
                    function.GenericArguments.Count != 0 || function.TypeConstraints.Count != 0 || candidate.ReceiverIndex >= 0 ||
                    candidate.Scope.Owner.BoundSymbol?.Schema is { GenericSlots.Count: > 0 } or { Origins.Count: > 0 })
                {
                    return this.Fail(use, BindingFailure.Unsupported, true);
                }

                var fits = this.Accessible(candidate, scope) && this.FunctionReferenceFits(use, candidate, function, required, operations.AsSpan(index * stride, stride - 1));
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
                    return this.FailMismatch(use, use, actual, required);
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

        if ((((FunctionKoto)selected.Declaration).Modifier & ModifierKind.Unsafe) != 0)
        {
            return this.Fail(use, BindingFailure.UnsafeFunctionValue);
        }

        if (!erase)
        {
            return this.CompleteFunctionItem(use, selected);
        }

        use.BoundSymbol = selected;
        if (use is MemberAccessKoto member)
        {
            member.Right.BoundSymbol = selected;
        }

        return Complete(use, required);
    }

    // Whether one function of a group converts to a common Function Type. A form BindFunctionReference rejects with its own
    // diagnostic (a generic, receiver or unsafe function) counts as fitting, so that diagnostic is published after selection.
    private bool FunctionGroupFits(Koto use, BindingSymbol symbol, BoundType required, BindingScope scope)
    {
        for (var candidate = symbol; candidate is not null; candidate = candidate.Next)
        {
            this.BindHeader(candidate);
            if (candidate.Declaration is not FunctionKoto function || function.GenericArguments.Count != 0 || function.TypeConstraints.Count != 0 ||
                candidate.ReceiverIndex >= 0 || candidate.Scope.Owner.BoundSymbol?.Schema is { GenericSlots.Count: > 0 } or { Origins.Count: > 0 })
            {
                return true;
            }

            if (this.Accessible(candidate, scope) && this.FunctionReferenceFits(use, candidate, function, required))
            {
                return true;
            }
        }

        return false;
    }

    private bool FunctionReferenceFits(Koto use, BindingSymbol symbol, FunctionKoto function, BoundType required, Span<BoundArgumentOperation> operations = default)
    {
        var parameters = required.Components[0];
        if (parameters.Components.Count != function.Parameters.Count)
        {
            return false;
        }

        var origins = this.originScratch.Rent(function.Origins.Count);
        var inputCount = InputOriginCount(function);
        var inputs = this.originScratch.Rent(inputCount);
        Array.Clear(origins, 0, function.Origins.Count);
        Array.Clear(inputs, 0, inputCount);
        try
        {
            var inference = this.BeginOriginInference(use, function);
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (function.Parameters[i].Type.BoundType is not { } parameter)
                {
                    return false;
                }

                // Only the implementation's per-call binders are inferred; the
                // required signature's quantifiers and fixed Origins remain rigid.
                this.MatchInputOrigins(parameter, parameters.Components[i], function, origins, inputs);
                this.CollectOriginInference(parameter, parameters.Components[i], inference);
            }

            if (symbol.Type is { } produced)
            {
                this.CollectOriginInference(produced, required.Components[1], inference, result: true);
            }

            if (!this.SolveOriginInference(inference, origins, inputs, use))
            {
                return false;
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                var parameter = Substitute(function.Parameters[i].Type.BoundType!);
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
                symbol.Type is not { } result || HasUnsubstitutedOrigin(result = Substitute(result), function) || !this.FitsTypeAt(result, required.Components[1], use))
            {
                return false;
            }

            return true;
        }
        finally
        {
            this.originScratch.Return(origins, clearArray: true);
            this.originScratch.Return(inputs, clearArray: true);
        }

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
