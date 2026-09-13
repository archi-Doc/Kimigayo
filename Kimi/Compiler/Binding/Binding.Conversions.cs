// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private BindingScope? conversionEvidenceScope;

    private bool ConversionCanComplete(Koto source, BindingScope scope)
    {
        var structural = this.resultStructure ??= new(this.ResultNeverEvidence);
        structural.Clear();
        this.conversionEvidenceScope = scope;
        try
        {
            return structural.CanComplete(source);
        }
        finally
        {
            this.conversionEvidenceScope = null;
        }
    }

    private bool ResultNeverEvidence(Koto source)
    {
        if (source.BoundType is { } known)
        {
            return ReferenceEquals(known, BoundType.Never);
        }

        // Probe only ordinary name/signature evidence, without binding operands
        // or reentering conversion inference during the structural traversal.
        if (this.conversionEvidenceScope is not { } scope || source is not (InvocationKoto or IdentifierNameKoto))
        {
            return false;
        }

        for (var parent = source.Parent; parent is not null; parent = parent.Parent)
        {
            if (this.scopes.TryGetValue(parent, out var enclosing) ||
                (parent.Parent is CodeBlockKoto { Parent: FunctionKoto { IsGenerated: true } } &&
                parent.CodeContext.SourceDocument is { } document && this.scopes.TryGetValue(document, out enclosing)))
            {
                scope = enclosing;
                break;
            }
        }

        return ReferenceEquals(this.ResultEvidence(source, scope), BoundType.Never);
    }

    private BoundType? BindConversion(ConversionKoto conversion, BindingScope scope)
    {
        var syntax = conversion.Right;
        while (true)
        {
            if (syntax is ParenthesizedTypeKoto parentheses)
            {
                syntax = parentheses.Type;
            }
            else if (syntax is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner })
            {
                syntax = inner;
            }
            else
            {
                break;
            }
        }

        if (syntax is TypeSemanticsKoto { Type: null } shorthand && CompilerHelper.TryParse(shorthand.Identifier, out _))
        {
            this.BindNode(conversion.Left, scope);
            Fail(conversion.Right, BindingFailure.Unsupported, true);
            return Fail(conversion, BindingFailure.Unsupported, true);
        }

        var target = this.BindType(conversion.Right, scope);

        // Explicit Semantics adaptations have separate acquisition rules, even when
        // owner normalization happens to produce the same primitive Type.
        var plain = syntax is not TypeSemanticsKoto { Type: not null, IsTransparentWrapper: false };
        var operand = KotoHelper.UnwrapParentheses(conversion.Left);
        var literal = operand is NumberLiteralKoto { IsInteger: true } or
            PrefixMinusKoto { Operand: NumberLiteralKoto { IsInteger: true } } or
            PrefixPlusKoto { Operand: NumberLiteralKoto { IsInteger: true } };
        if (plain && target is { IsFloatingPoint: true } && literal)
        {
            // Exact integer-to-float literal fitting must not first impose i32's
            // range. Leave it pending until floating conversion is implemented.
            Fail(conversion.Left, BindingFailure.Unsupported, true);
            return Fail(conversion, BindingFailure.Unsupported, true);
        }

        var fit = plain && target is { IsInteger: true } && literal;
        var source = this.BindNode(conversion.Left, scope, fit ? target : null);
        if (source is null || target is null)
        {
            return Complete(conversion, null);
        }

        if (ReferenceEquals(source, BoundType.Never))
        {
            conversion.ConversionBinding = ConversionBinding.Abrupt;
            return Complete(conversion, BoundType.Never);
        }

        if (!plain)
        {
            return Fail(conversion, BindingFailure.Unsupported, true);
        }

        if (source.IsNumeric && target.IsNumeric)
        {
            if (ScalarTypes.Width(source, this.compilation.PointerWidth) == 0 || ScalarTypes.Width(target, this.compilation.PointerWidth) == 0)
            {
                return Fail(conversion, BindingFailure.Unsupported, true);
            }

            conversion.ConversionBinding = fit ? ConversionBinding.Literal : ConversionBinding.Integer;
            return Complete(conversion, target);
        }

        // Identity acquisition and nonnumeric ownership adaptations are legitimate
        // language operations, but are outside this integer execution slice.
        var unsupported = ReferenceEquals(source, target) || (!source.IsNumeric && !target.IsNumeric) ||
            source.Kind == BoundTypeKind.Semantics || target.Kind == BoundTypeKind.Semantics;
        return Fail(conversion, unsupported ? BindingFailure.Unsupported : BindingFailure.TypeMismatch, unsupported);
    }
}
