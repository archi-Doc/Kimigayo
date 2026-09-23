// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static bool IsAggregateArgument(Koto source)
        => KotoHelper.UnwrapParentheses(source) is ArrayLiteralKoto or TupleLiteralKoto { Elements.Count: > 0 };

    private bool InferAggregateCall(BoundType pattern, Koto source, FunctionKoto function, BoundType?[] types, BoundLength?[] lengths, BoundOrigin[] origins, BoundOrigin[] inputs, bool fitLiterals)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (pattern.Kind == BoundTypeKind.Parameter)
        {
            if (this.SubstituteType(pattern, function, types, lengths) is { } fixedType)
            {
                pattern = fixedType;
            }
            else if (this.IndependentAggregateType(source, fitLiterals) is { } inferred)
            {
                return this.Infer(pattern, inferred, function, types, lengths: lengths);
            }
        }

        if (source is TupleLiteralKoto tuple)
        {
            if (pattern.Kind != BoundTypeKind.Tuple || pattern.Components.Count != tuple.Elements.Count)
            {
                return true; // Probing reports a shape mismatch once the target is fixed.
            }

            for (var i = 0; i < tuple.Elements.Count; i++)
            {
                if (!this.InferAggregateCall(pattern.Components[i], tuple.Elements[i], function, types, lengths, origins, inputs, fitLiterals))
                {
                    return false;
                }
            }

            return true;
        }

        if (source is ArrayLiteralKoto array)
        {
            if (pattern.Kind is not (BoundTypeKind.FixedArray or BoundTypeKind.Array))
            {
                return true; // Existing candidate-specific probing diagnoses other shapes.
            }

            if (pattern.Kind == BoundTypeKind.FixedArray && pattern.LengthExpression is { Parameter: { } parameter } && ReferenceEquals(parameter.Scope.Owner, function))
            {
                var length = array.FillLength is null ? this.InternLength(KotoKind.NumberLiteral, array.Elements.Count) : array.FillCount;
                if (length is null)
                {
                    return false;
                }

                if (lengths[parameter.Slot] is { } previous && !ReferenceEquals(previous, length))
                {
                    return false;
                }

                lengths[parameter.Slot] = length;
            }

            for (var i = 0; i < array.Elements.Count; i++)
            {
                if (!this.InferAggregateCall(pattern.Components[0], array.Elements[i], function, types, lengths, origins, inputs, fitLiterals))
                {
                    return false;
                }
            }

            return true;
        }

        if (source.BoundType is { } actual)
        {
            this.MatchInputOrigins(pattern, actual, function, origins, inputs);
            pattern = this.SubstituteStoredOrigins(pattern, function, origins.AsSpan(0, function.Origins.Count), inputs.AsSpan(0, Math.Min(inputs.Length, InputOriginCount(function))));
            return this.Infer(pattern, actual, function, types, true, lengths);
        }

        if (!fitLiterals || this.SubstituteType(pattern, function, types, lengths) is not null)
        {
            return true;
        }

        var literal = source is NumberLiteralKoto number ? number : source is PrefixMinusKoto or PrefixPlusKoto ? ((UnaryKoto)source).Operand as NumberLiteralKoto : null;
        return literal is null || this.Infer(pattern, literal.IsInteger ? BoundType.I32 : BoundType.F64, function, types, lengths: lengths);
    }

    // Obtain independent tuple/fill Types without committing candidate-local numeric defaults.
    private BoundType? IndependentAggregateType(Koto source, bool fitLiterals)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (source.BoundType is { } actual)
        {
            return actual;
        }

        if (source is ArrayLiteralKoto { FillCount: { } length } fill &&
            this.IndependentAggregateType(fill.Elements[0], fitLiterals) is { } elementType)
        {
            return this.InternType(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [elementType], length.IsConstant ? length.Value : 0, lengthExpression: length.IsConstant ? null : length);
        }

        if (source is TupleLiteralKoto tuple)
        {
            var elements = this.RentTypes(tuple.Elements.Count);
            try
            {
                for (var i = 0; i < tuple.Elements.Count; i++)
                {
                    if (this.IndependentAggregateType(tuple.Elements[i], fitLiterals) is not { } element)
                    {
                        return null;
                    }

                    elements[i] = element;
                }

                return this.InternType(BoundTypeKind.Tuple, null, SemanticsKind.Owner, elements.AsSpan(0, tuple.Elements.Count));
            }
            finally
            {
                this.typeScratch.Return(elements, clearArray: true);
            }
        }

        var literal = source is NumberLiteralKoto number ? number : source is PrefixMinusKoto or PrefixPlusKoto ? ((UnaryKoto)source).Operand as NumberLiteralKoto : null;
        return fitLiterals && literal is not null ? DefaultLiteralType(literal, null) : null;
    }

    private bool PrepareAggregateArgument(Koto source, BindingScope scope)
    {
        source = KotoHelper.UnwrapParentheses(source);
        var elements = source switch
        {
            ArrayLiteralKoto array => array.Elements,
            TupleLiteralKoto tuple => tuple.Elements,
            _ => null,
        };
        if (elements is null)
        {
            return NeedsEnumContext(source) ? this.PrepareContextualEnumInputs(source, scope) : IsUnfittedLiteral(source) || this.BindNode(source, scope) is not null;
        }

        var valid = true;
        if (source is ArrayLiteralKoto { FillLength: { } length } fill)
        {
            valid = (fill.FillCount = this.BindLength(length, scope)) is not null;
        }

        for (var i = 0; i < elements.Count; i++)
        {
            valid &= this.PrepareAggregateArgument(elements[i], scope);
        }

        return valid;
    }

    // Read-only candidate probing: bind only the winning literal's aggregate shape.
    // General element expressions already have independent Types and cannot be retried.
    private CandidateApplicability ProbeAggregateArgument(Koto source, BoundType expected, BindingScope scope)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (source is ArrayLiteralKoto array)
        {
            // SPEC 4.3: a call-argument literal fits the candidate's fixed array of exactly its length, or its owned Array.
            if (expected is not { Kind: BoundTypeKind.FixedArray or BoundTypeKind.Array, Semantics: SemanticsKind.Owner } ||
                (array.FillLength is not null && expected.Kind != BoundTypeKind.FixedArray) ||
                (expected.Kind == BoundTypeKind.FixedArray && (array.FillLength is null ? expected.Length != array.Elements.Count :
                    array.FillCount is not { } count || (count.IsConstant ? expected.LengthExpression is not null || expected.Length != count.Value : !ReferenceEquals(expected.LengthExpression, count)))))
            {
                return CandidateApplicability.Inapplicable;
            }

            for (var i = 0; i < array.Elements.Count; i++)
            {
                var result = this.ProbeAggregateArgument(array.Elements[i], expected.Components[0], scope);
                if (result != CandidateApplicability.Applicable)
                {
                    return result;
                }
            }

            return CandidateApplicability.Applicable;
        }

        if (source is TupleLiteralKoto tuple)
        {
            if (expected.Kind != BoundTypeKind.Tuple || expected.Components.Count != tuple.Elements.Count)
            {
                return CandidateApplicability.Inapplicable;
            }

            for (var i = 0; i < tuple.Elements.Count; i++)
            {
                var result = this.ProbeAggregateArgument(tuple.Elements[i], expected.Components[i], scope);
                if (result != CandidateApplicability.Applicable)
                {
                    return result;
                }
            }

            return CandidateApplicability.Applicable;
        }

        if (NeedsEnumContext(source))
        {
            return this.ProbeContextualEnum(source, expected, scope);
        }

        if (IsUnfittedLiteral(source))
        {
            return this.FitsInputLiteral(source, expected) ? CandidateApplicability.Applicable : CandidateApplicability.Inapplicable;
        }

        return source.BoundType is not { } actual ? CandidateApplicability.Pending :
            FitsType(actual, expected) ? CandidateApplicability.Applicable : CandidateApplicability.Inapplicable;
    }
}
