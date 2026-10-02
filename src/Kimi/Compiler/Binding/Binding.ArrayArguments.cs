// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static bool IsAggregateArgument(Koto source)
        => KotoHelper.UnwrapParentheses(source) is ArrayLiteralKoto or TupleLiteralKoto { Elements.Count: > 0 } || IsMixedRange(source);

    // SPEC 4.6.3.1: a range with a literal-only and a typed written boundary. Like a tuple element, each literal-only boundary
    // takes the S or E of the candidate's parameter, while a typed boundary keeps its independent Type.
    private static bool IsMixedRange(Koto source)
        => KotoHelper.UnwrapParentheses(source) is RangeKoto range && !IsLiteralOnlyRange(range) &&
            ((range.Start is { } start && IsLiteralOnlyPosition(start)) || (range.End is { } end && IsLiteralOnlyPosition(end)));

    private bool InferAggregateCall(BoundType pattern, Koto source, FunctionKoto function, BoundType?[] types, BoundLength?[] lengths, BoundOrigin[] origins, BoundOrigin[] inputs, bool fitLiterals)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (source is RangeKoto mixed && IsMixedRange(mixed) && pattern.Kind != BoundTypeKind.Parameter)
        {
            if (pattern is not { Kind: BoundTypeKind.Constructed, Components.Count: 2 } || !ReferenceEquals(pattern.Symbol, mixed.IsInclusive ? this.Library.ClosedRange : this.Library.Range))
            {
                return true; // Probing reports a shape mismatch once the target is fixed.
            }

            // The typed boundary first; after all other evidence, a literal-only boundary whose S or E is still unknown takes the
            // other boundary's integer Type, else i32 (SPEC 4.6.3.1 rules 2 and 3).
            var startLiteral = mixed.Start is { } first && IsLiteralOnlyPosition(first);
            var endLiteral = mixed.End is { } last && IsLiteralOnlyPosition(last);
            if ((mixed.Start is not null && !startLiteral && !this.InferAggregateCall(pattern.Components[0], mixed.Start, function, types, lengths, origins, inputs, fitLiterals)) ||
                (mixed.End is not null && !endLiteral && !this.InferAggregateCall(pattern.Components[1], mixed.End, function, types, lengths, origins, inputs, fitLiterals)))
            {
                return false;
            }

            return (!startLiteral || this.InferLiteralBoundary(pattern.Components[0], mixed.Start!, this.IndependentRangeBoundary(mixed.End, this.EndType), function, types, lengths, fitLiterals)) &&
                (!endLiteral || this.InferLiteralBoundary(pattern.Components[1], mixed.End!, this.IndependentRangeBoundary(mixed.Start, this.StartType), function, types, lengths, fitLiterals));
        }

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
            // A typed range boundary contributes the read Type it supplies (SPEC 3.5.3).
            actual = KotoHelper.UnwrapParentheses(source.Parent!) is RangeKoto && this.ReadTypeReferent(actual, source) is { } read ? read : actual;
            this.MatchInputOrigins(pattern, actual, function, origins, inputs);
            pattern = this.SubstituteStoredOrigins(pattern, function, origins.AsSpan(0, function.Origins.Count), inputs.AsSpan(0, Math.Min(inputs.Length, InputOriginCount(function))));
            return this.Infer(pattern, actual, function, types, true, lengths);
        }

        if (!fitLiterals || this.SubstituteType(pattern, function, types, lengths) is not null)
        {
            return true;
        }

        return this.LiteralDefault(source) is not { } literalDefault || this.Infer(pattern, literalDefault, function, types, lengths: lengths);
    }

    // Obtain independent tuple/fill Types without committing candidate-local numeric defaults.
    private BoundType? IndependentAggregateType(Koto source, bool fitLiterals)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (source.BoundType is { } actual)
        {
            return actual;
        }

        if (source is RangeKoto range && IsMixedRange(range))
        {
            // SPEC 4.6.3.1: without an expected range, a literal-only boundary takes the integer Type of the typed one.
            var start = this.IndependentRangeBoundary(range.Start, this.StartType);
            var end = this.IndependentRangeBoundary(range.End, this.EndType);
            start ??= this.OtherBoundaryType(range.Start!, end);
            end ??= this.OtherBoundaryType(range.End!, start);
            return this.InternType(BoundTypeKind.Constructed, range.IsInclusive ? this.Library.ClosedRange : this.Library.Range, SemanticsKind.Owner, [start, end]);
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

        return fitLiterals ? this.LiteralDefault(source) : null;
    }

    private bool InferLiteralBoundary(BoundType pattern, Koto boundary, BoundType? other, FunctionKoto function, BoundType?[] types, BoundLength?[] lengths, bool fitLiterals)
        => !fitLiterals || this.SubstituteType(pattern, function, types, lengths) is not null ||
            this.Infer(pattern, this.OtherBoundaryType(boundary, other), function, types, lengths: lengths);

    // The independent Type of a written range boundary: Start or End when omitted, the read Type of a typed one, or null for a
    // literal-only one.
    private BoundType? IndependentRangeBoundary(Koto? boundary, BoundType omitted)
    {
        if (boundary is null)
        {
            return omitted;
        }

        var syntax = KotoHelper.UnwrapParentheses(boundary);
        return IsLiteralOnlyPosition(syntax) || syntax.BoundType is not { } actual ? null : this.ReadTypeReferent(actual, syntax) ?? actual;
    }

    // A literal-only boundary beside a typed one: that boundary's integer Type, or its T for FromEnd<T>, else i32 (SPEC 4.6.3.1).
    private BoundType OtherBoundaryType(Koto boundary, BoundType? other)
    {
        var integer = other is { IsInteger: true } ? other : this.FromEndOffsetType(other) ?? BoundType.I32;
        return KotoHelper.UnwrapParentheses(boundary) is FromEndIndexKoto ? this.FromEndType(integer) : integer;
    }

    private bool PrepareAggregateArgument(Koto source, BindingScope scope)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (source is RangeKoto range && IsMixedRange(range))
        {
            // A typed boundary is bound on its own now; a literal-only one waits for the candidate (SPEC 4.6.3.1).
            return (range.Start is null || this.PrepareAggregateArgument(range.Start, scope)) & (range.End is null || this.PrepareAggregateArgument(range.End, scope));
        }

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

        if (source is RangeKoto range && IsMixedRange(range))
        {
            // SPEC 4.6.3.1: the candidate's range of this shape supplies each literal-only boundary's S or E; a typed boundary
            // must supply exactly its Type argument, through a value read.
            if (expected is not { Kind: BoundTypeKind.Constructed, Semantics: SemanticsKind.Owner, Components.Count: 2 } ||
                !ReferenceEquals(expected.Symbol, range.IsInclusive ? this.Library.ClosedRange : this.Library.Range))
            {
                return CandidateApplicability.Inapplicable;
            }

            var start = this.ProbeRangeBoundary(range.Start, expected.Components[0], this.StartType, scope);
            return start != CandidateApplicability.Applicable ? start : this.ProbeRangeBoundary(range.End, expected.Components[1], this.EndType, scope);
        }

        if (NeedsEnumContext(source))
        {
            return this.ProbeContextualEnum(source, expected, scope);
        }

        if (IsUnfittedLiteral(source))
        {
            return this.FitsInputLiteral(source, expected, scope) ? CandidateApplicability.Applicable : CandidateApplicability.Inapplicable;
        }

        return source.BoundType is not { } actual ? CandidateApplicability.Pending :
            this.FitsTypeAt(actual, expected, source) ? CandidateApplicability.Applicable : CandidateApplicability.Inapplicable;
    }

    private CandidateApplicability ProbeRangeBoundary(Koto? boundary, BoundType expected, BoundType omitted, BindingScope scope)
    {
        if (boundary is null)
        {
            return ReferenceEquals(expected, omitted) ? CandidateApplicability.Applicable : CandidateApplicability.Inapplicable;
        }

        if (IsUnfittedLiteral(boundary))
        {
            return this.FitsInputLiteral(boundary, expected, scope) ? CandidateApplicability.Applicable : CandidateApplicability.Inapplicable;
        }

        var syntax = KotoHelper.UnwrapParentheses(boundary);
        return syntax.BoundType is not { } actual ? CandidateApplicability.Pending :
            ReferenceEquals(this.ReadTypeReferent(actual, syntax) ?? actual, expected) ? CandidateApplicability.Applicable : CandidateApplicability.Inapplicable;
    }
}
