// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<Koto, BoundLength> inferredArrayLengths = new();
    private Dictionary<Koto, ArrayInferenceFailure>? arrayInferenceFailures;

    private enum ArrayInferenceProblem : byte
    {
        Element,
        Length,
        LengthConflict,
        FixedArrayRequired,
    }

    private readonly record struct ArrayInferenceFailure(ArrayInferenceProblem Problem, Koto Related, BoundLength? Expected = null, BoundLength? Actual = null, BoundType? Type = null);

    // A negative length exists only in an initializer expectation, never in a completed Type. There is no inference
    // variable or inverse length solver: literal counts and complete result Types supply each written length hole.
    private static bool HasArrayLengthHole(BoundType type)
        => type.Kind == BoundTypeKind.FixedArray && (type.Length < 0 || HasArrayLengthHole(type.Components[0]));

    private static bool IsArrayHole(Koto syntax) => syntax is TypeSemanticsKoto { Type: null, Identifier: "_" };

    private static Koto ArrayShapeSyntax(Koto syntax)
    {
        while (true)
        {
            if (syntax is ParenthesizedTypeKoto parentheses)
            {
                syntax = parentheses.Type;
            }
            else if (syntax is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Owner, SemanticsParameter: null, Type: { } inner })
            {
                syntax = inner;
            }
            else
            {
                return syntax;
            }
        }
    }

    private BoundType? BindArrayFill(ArrayLiteralKoto fill, BindingScope scope, BoundType? expected)
    {
        var length = fill.FillCount = this.BindLength(fill.FillLength!, scope);
        var element = this.BindNode(fill.Elements[0], scope, expected?.Kind == BoundTypeKind.FixedArray ? expected.Components[0] : null);
        if (length is null || element is null)
        {
            // A failed element or length explains the fill (SPEC 23.3.6.4); a length syntax that binds but is no length keeps
            // the formation failure.
            var failed = element is null && fill.Elements[0].BindingState != BindingState.Resolved ? fill.Elements[0]
                : length is null && fill.FillLength!.BindingState != BindingState.Resolved ? fill.FillLength : null;
            return failed is not null ? this.CompleteDependent(fill, failed) : this.Fail(fill, BindingFailure.InvalidTypeFormation);
        }

        if (this.ProveCopy(element, fill) != ConstraintProof.Proven)
        {
            this.FailConstraint(fill);
            return null;
        }

        var type = this.InternType(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [element], length.IsConstant ? length.Value : 0, lengthExpression: length.IsConstant ? null : length);
        return Complete(fill, type);
    }

    // As for Tuple literals, retain the acquired element Type until the enclosing
    // declaration has inferred omitted Origins. Replacing it with the expectation
    // here would erase the very evidence needed by InferLocalOrigins.
    private BoundType? BindContextualArrayLiteral(ArrayLiteralKoto literal, BindingScope scope, BoundType expected)
    {
        BoundType? element = null;
        var complete = true;
        for (var i = 0; i < literal.Elements.Count; i++)
        {
            var actual = this.BindNode(literal.Elements[i], scope, expected.Components[0]);
            if (actual is null)
            {
                complete = false;
            }
            else if (!ReferenceEquals(actual, BoundType.Never))
            {
                var common = element is null ? actual : this.CommonOriginType(element, actual);
                if (common is null)
                {
                    this.Fail(literal.Elements[i], BindingFailure.TypeMismatch);
                    complete = false;
                }
                else
                {
                    element = common;
                }
            }
        }

        if (expected.Kind == BoundTypeKind.FixedArray && (expected.LengthExpression is not null || (expected.Length >= 0 && expected.Length != literal.Elements.Count)))
        {
            return this.Fail(literal, BindingFailure.TypeMismatch);
        }

        return Complete(literal, complete ? this.InternType(expected.Kind, expected.Symbol, expected.Semantics, [element ?? expected.Components[0]], expected.Kind == BoundTypeKind.FixedArray ? literal.Elements.Count : expected.Length) : null);
    }

    private void InferArrayAnnotation(Koto syntax, Koto initializer, BindingScope scope)
    {
        var hole = ArrayShapeSyntax(syntax);
        if (hole is not FixedArrayTypeKoto)
        {
            return;
        }

        var lengthHole = false;
        while (hole is FixedArrayTypeKoto array)
        {
            lengthHole |= IsArrayHole(array.Length);
            hole = ArrayShapeSyntax(array.ElementType);
        }

        if (!lengthHole && !IsArrayHole(hole))
        {
            return;
        }

        BoundType? established = null;
        BoundType? literalDefault = null;
        var gathered = this.ArrayElementEvidence(syntax, initializer, scope, ref established, ref literalDefault, out var failed);
        if (IsArrayHole(hole))
        {
            if (gathered && (established ?? literalDefault) is { } element)
            {
                Complete(hole, element);
            }
            else
            {
                if (failed is not null)
                {
                    this.CompleteDependent(hole, failed);
                }
                else
                {
                    this.FailExplained(ref this.arrayInferenceFailures, hole, BindingFailure.ArrayAnnotationInference, new(ArrayInferenceProblem.Element, initializer));
                }
            }
        }

        for (var shape = ArrayShapeSyntax(syntax); shape is FixedArrayTypeKoto array; shape = ArrayShapeSyntax(array.ElementType))
        {
            if (IsArrayHole(array.Length) && !this.inferredArrayLengths.ContainsKey(array.Length))
            {
                if (failed is not null)
                {
                    this.CompleteDependent(array.Length, failed);
                }
                else
                {
                    this.FailExplained(ref this.arrayInferenceFailures, array.Length, BindingFailure.ArrayAnnotationInference, new(ArrayInferenceProblem.Length, initializer));
                }
            }
        }
    }

    // Only a known leaf supplies an expectation. `_` alone does not demand another fixed dimension or select an element
    // Type for an otherwise unconstrained generic call. Ordinary candidate inference receives the written structure.
    private BoundType? ArrayAnnotationExpectation(Koto shape, BindingScope scope)
    {
        shape = ArrayShapeSyntax(shape);
        if (shape is not FixedArrayTypeKoto array)
        {
            return IsArrayHole(shape) ? null : this.BindType(shape, scope);
        }

        if (this.ArrayAnnotationExpectation(array.ElementType, scope) is not { } element)
        {
            return null;
        }

        var length = IsArrayHole(array.Length) ? this.inferredArrayLengths.GetValueOrDefault(array.Length) : this.BindLength(array.Length, scope);
        return this.InternType(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [element], length is null ? -1 : length.IsConstant ? length.Value : 0, lengthExpression: length is { IsConstant: false } ? length : null);
    }

    private bool InferArrayLength(FixedArrayTypeKoto array, BoundLength length, Koto source)
    {
        if (!IsArrayHole(array.Length))
        {
            return true;
        }

        if (length.IsConstant && length.Value < 0)
        {
            return true; // An empty contextual literal with an unknown nested dimension supplies no length evidence.
        }

        if (this.inferredArrayLengths.TryGetValue(array.Length, out var previous) && !ReferenceEquals(previous, length))
        {
            this.FailExplained(ref this.arrayInferenceFailures, source, BindingFailure.ArrayAnnotationInference, new(ArrayInferenceProblem.LengthConflict, array.Length, previous, length));
            return false;
        }

        this.inferredArrayLengths[array.Length] = length;
        Complete(array.Length, BoundType.ISize);
        return true;
    }

    private BoundType CompleteArrayExpectation(BoundType expected, BoundType actual)
    {
        if (!HasArrayLengthHole(expected) || actual.Kind != BoundTypeKind.FixedArray)
        {
            return expected;
        }

        var element = this.CompleteArrayExpectation(expected.Components[0], actual.Components[0]);
        return this.InternType(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [element], expected.Length < 0 ? actual.Length : expected.Length, lengthExpression: expected.Length < 0 ? actual.LengthExpression : expected.LengthExpression);
    }

    // SPEC 4.3: without a fixed-array expectation an independent literal constructs an Array whose element Type is the
    // one complete Type its elements establish; unfitted numeric literals follow ordinary inference. Call arguments keep
    // candidate-local fitting and never take this default.
    private BoundType? BindIndependentArrayLiteral(ArrayLiteralKoto literal, BindingScope scope)
    {
        BoundType? established = null;
        BoundType? literalDefault = null;
        for (var i = 0; i < literal.Elements.Count; i++)
        {
            if (!this.CollectLiteralElementEvidence(literal.Elements[i], scope, ref established, ref literalDefault))
            {
                return Complete(literal, null);
            }
        }

        if ((established ?? literalDefault) is not { } element)
        {
            return this.Fail(literal, BindingFailure.MissingType, true);
        }

        for (var i = 0; i < literal.Elements.Count; i++)
        {
            this.RequireType(literal.Elements[i], scope, element);
        }

        return Complete(literal, this.InternType(BoundTypeKind.Array, this.Library.DynamicArray, SemanticsKind.Owner, [element]));
    }

    // Arrays and Dictionary keys/values use the same bounded evidence rule: complete Types establish the expectation;
    // unfitted literals supply only a default, and Never contributes no value Type. No temporary element list is needed.
    private bool CollectLiteralElementEvidence(Koto source, BindingScope scope, ref BoundType? established, ref BoundType? literalDefault)
    {
        var unwrapped = KotoHelper.UnwrapParentheses(source);
        if (IsUnfittedLiteral(unwrapped))
        {
            literalDefault ??= this.LiteralDefault(unwrapped);
            return true;
        }

        var actual = this.BindNode(source, scope);
        if (actual is null)
        {
            return false;
        }

        if (ReferenceEquals(actual, BoundType.Never))
        {
            return true;
        }

        var common = established is null ? actual : this.CommonOriginType(established, actual);
        if (common is null)
        {
            this.FailMismatch(source, source, actual, established!);
            return false;
        }

        established = common;
        return true;
    }

    private bool ArrayElementEvidence(Koto shape, Koto source, BindingScope scope, ref BoundType? established, ref BoundType? literalDefault, out Koto? failed)
    {
        failed = null;
        shape = ArrayShapeSyntax(shape);
        source = KotoHelper.UnwrapParentheses(source);
        if (shape is FixedArrayTypeKoto array && source is ArrayLiteralKoto literal)
        {
            var length = literal.FillLength is { } fill ? this.BindLength(fill, scope) : this.InternLength(KotoKind.NumberLiteral, literal.Elements.Count);
            if (length is null || !this.InferArrayLength(array, length, source))
            {
                failed = length is null ? literal.FillLength : source;
                return false;
            }

            for (var i = 0; i < literal.Elements.Count; i++)
            {
                if (!this.ArrayElementEvidence(array.ElementType, literal.Elements[i], scope, ref established, ref literalDefault, out failed))
                {
                    return false;
                }
            }

            return true;
        }

        if (shape is not FixedArrayTypeKoto && !IsArrayHole(shape))
        {
            return true; // Ordinary contextual Binding will fit known leaves after all lengths are established.
        }

        if (IsArrayHole(shape) && IsUnfittedLiteral(source))
        {
            literalDefault ??= this.LiteralDefault(source);
            return true;
        }

        var actual = this.BindNode(source, scope, this.ArrayAnnotationExpectation(shape, scope));
        if (actual is null)
        {
            failed = source;
            return false;
        }

        if (ReferenceEquals(actual, BoundType.Never))
        {
            return true;
        }

        // A typed initializer contributes its complete element Type. Dimensions
        // are checked by ordinary fitting after the annotation is complete.
        while (shape is FixedArrayTypeKoto nested)
        {
            if (actual.Kind != BoundTypeKind.FixedArray)
            {
                this.FailExplained(ref this.arrayInferenceFailures, source, BindingFailure.ArrayAnnotationInference, new(ArrayInferenceProblem.FixedArrayRequired, nested, Type: actual));
                failed = source;
                return false;
            }

            if (!this.InferArrayLength(nested, actual.LengthExpression ?? this.InternLength(KotoKind.NumberLiteral, actual.Length), source))
            {
                failed = source;
                return false;
            }

            actual = actual.Components[0];
            shape = ArrayShapeSyntax(nested.ElementType);
        }

        if (!IsArrayHole(shape))
        {
            return true;
        }

        var common = established is null ? actual : this.CommonOriginType(established, actual);
        if (common is null)
        {
            this.FailMismatch(source, source, actual, established!);
            failed = source;
            return false;
        }

        established = common;
        return true;
    }
}
