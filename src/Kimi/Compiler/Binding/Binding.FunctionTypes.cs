// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    internal static bool CallableSignatureFits(BoundType actual, BoundType expected)
    {
        if (FitsType(actual, expected))
        {
            return true;
        }

        if (!PerCallSignature(actual) || !PerCallSignature(expected) || !FitsType(actual.Components[1], expected.Components[1]))
        {
            return false;
        }

        var a = actual.Components[0];
        var b = expected.Components[0];
        if (a.Components.Count != b.Components.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Components.Count; i++)
        {
            var input = a.Components[i];
            var required = b.Components[i];
            if (!FitsType(required, input) && !(input.Origin is { Kind: OriginKind.Input } && required.Origin is { Kind: OriginKind.Input } &&
                input.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq && required.Semantics == input.Semantics && ReferenceEquals(input.Components[0], required.Components[0])))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PerCallSignature(BoundType signature)
    {
        if (signature.Components[1].CarriesOrigin)
        {
            return false;
        }

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

    private BoundType? BindFunctionReference(Koto use, BindingSymbol symbol, BoundType required, BindingScope scope)
    {
        BindingSymbol? selected = null;
        for (var candidate = symbol; candidate is not null; candidate = candidate.Next)
        {
            this.BindHeader(candidate);
            if (candidate.Declaration is not FunctionKoto function ||
                function.GenericArguments.Count != 0 || function.TypeConstraints.Count != 0 || candidate.ReceiverIndex >= 0 ||
                candidate.Scope.Owner.BoundSymbol?.Schema is { GenericSlots.Count: > 0 } or { Origins.Count: > 0 })
            {
                return Fail(use, BindingFailure.Unsupported, true);
            }

            if (!this.Accessible(candidate, scope) || !this.FunctionReferenceFits(use, candidate, function, required))
            {
                continue;
            }

            if ((function.Modifier & ModifierKind.Unsafe) != 0)
            {
                return Fail(use, BindingFailure.UnsafeFunctionValue);
            }

            if (selected is not null)
            {
                return Fail(use, BindingFailure.Ambiguous, true);
            }

            selected = candidate;
        }

        if (selected is null)
        {
            return Fail(use, BindingFailure.TypeMismatch);
        }

        use.BoundSymbol = selected;
        if (use is MemberAccessKoto member)
        {
            member.Right.BoundSymbol = selected;
        }

        return Complete(use, required);
    }

    private bool FunctionReferenceFits(Koto use, BindingSymbol symbol, FunctionKoto function, BoundType required)
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
