// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
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
        var element = expected?.Kind == BoundTypeKind.FixedArray
            ? this.RequireType(fill.Elements[0], scope, expected.Components[0])
            : this.BindNode(fill.Elements[0], scope);
        if (length is null || element is null)
        {
            return Fail(fill, BindingFailure.InvalidTypeFormation);
        }

        if (this.ProveCopy(element, fill) != ConstraintProof.Proven)
        {
            this.FailConstraint(fill);
            return null;
        }

        var type = this.InternType(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [element], length.IsConstant ? length.Value : 0, lengthExpression: length.IsConstant ? null : length);
        return expected is not null && !FitsType(type, expected) ? Fail(fill, BindingFailure.TypeMismatch) : Complete(fill, type);
    }

    private void InferArrayAnnotation(Koto syntax, Koto initializer, BindingScope scope)
    {
        var hole = ArrayShapeSyntax(syntax);
        if (hole is not FixedArrayTypeKoto)
        {
            return;
        }

        while (hole is FixedArrayTypeKoto array)
        {
            hole = ArrayShapeSyntax(array.ElementType);
        }

        if (hole is not TypeSemanticsKoto { Type: null, Identifier: "_" })
        {
            return;
        }

        BoundType? established = null;
        BoundType? literalDefault = null;
        if (this.ArrayElementEvidence(syntax, initializer, scope, ref established, ref literalDefault) && (established ?? literalDefault) is { } element)
        {
            Complete(hole, element);
        }
        else
        {
            Fail(hole, BindingFailure.MissingType, true);
        }
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
            var source = KotoHelper.UnwrapParentheses(literal.Elements[i]);
            if (IsUnfittedLiteral(source))
            {
                var number = source as NumberLiteralKoto ?? (source as UnaryKoto)?.Operand as NumberLiteralKoto;
                if (number is not null)
                {
                    literalDefault ??= DefaultLiteralType(number, null);
                }

                continue;
            }

            var actual = this.BindNode(literal.Elements[i], scope);
            if (actual is null)
            {
                return Complete(literal, null);
            }

            if (ReferenceEquals(actual, BoundType.Never))
            {
                continue;
            }

            if (established is not null && !ReferenceEquals(established, actual))
            {
                return Fail(literal, BindingFailure.TypeMismatch);
            }

            established = actual;
        }

        if ((established ?? literalDefault) is not { } element)
        {
            return Fail(literal, BindingFailure.MissingType, true);
        }

        for (var i = 0; i < literal.Elements.Count; i++)
        {
            this.RequireType(literal.Elements[i], scope, element);
        }

        return Complete(literal, this.InternType(BoundTypeKind.Array, this.Library.DynamicArray, SemanticsKind.Owner, [element]));
    }

    private bool ArrayElementEvidence(Koto shape, Koto source, BindingScope scope, ref BoundType? established, ref BoundType? literalDefault)
    {
        shape = ArrayShapeSyntax(shape);
        source = KotoHelper.UnwrapParentheses(source);
        if (shape is FixedArrayTypeKoto array && source is ArrayLiteralKoto literal)
        {
            for (var i = 0; i < literal.Elements.Count; i++)
            {
                if (!this.ArrayElementEvidence(array.ElementType, literal.Elements[i], scope, ref established, ref literalDefault))
                {
                    return false;
                }
            }

            return true;
        }

        if (shape is TypeSemanticsKoto { Type: null, Identifier: "_" } && IsUnfittedLiteral(source))
        {
            var number = source as NumberLiteralKoto ?? (source as UnaryKoto)?.Operand as NumberLiteralKoto;
            if (number is not null)
            {
                literalDefault ??= DefaultLiteralType(number, null);
            }

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

        // A typed initializer contributes its complete element Type. Dimensions
        // are checked by ordinary fitting after the annotation is complete.
        while (shape is FixedArrayTypeKoto nested)
        {
            if (actual.Kind != BoundTypeKind.FixedArray)
            {
                Fail(source, BindingFailure.TypeMismatch);
                return false;
            }

            actual = actual.Components[0];
            shape = ArrayShapeSyntax(nested.ElementType);
        }

        if (established is not null && !ReferenceEquals(established, actual))
        {
            Fail(source, BindingFailure.TypeMismatch);
            return false;
        }

        established = actual;
        return true;
    }
}
