// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private BindingScope? conversionEvidenceScope;
    private NumberLiteralKoto? floatingIntegerLiteral;

    internal static bool SupportsIdentityAcquisition(BoundType type)
        => type.Semantics == SemanticsKind.Owner && type.Kind is BoundTypeKind.Primitive or BoundTypeKind.Tuple or BoundTypeKind.FixedArray;

    private static Koto ConversionTargetSyntax(ConversionKoto conversion)
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
                return syntax;
            }
        }
    }

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
        var syntax = ConversionTargetSyntax(conversion);
        if (syntax is TypeSemanticsKoto { Type: null } shorthand && CompilerHelper.TryParse(shorthand.Identifier, out var semantics))
        {
            var operandType = this.BindNode(conversion.Left, scope);
            if (operandType is null)
            {
                return Complete(conversion, null);
            }

            if (semantics is SemanticsKind.Ref or SemanticsKind.Uniq &&
                (StructStorage.IsStruct(operandType) || ReferenceTypes.IsStruct(operandType)) &&
                shorthand.OriginName is null && shorthand.OriginExpression is null && shorthand.OriginArguments is null)
            {
                var referent = IsBorrow(operandType.Semantics) ? operandType.Components[0] : operandType;
                var pattern = this.InternType(BoundTypeKind.Semantics, null, semantics, [referent]);
                if (!this.AdaptInput(conversion.Left, pattern, operandType, scope, null, null, out var adapted, out _, out _))
                {
                    return Fail(conversion, BindingFailure.InvalidAssignment);
                }

                Complete(conversion.Right, adapted);
                conversion.ConversionBinding = ConversionBinding.Borrow;
                return Complete(conversion, adapted);
            }

            if (semantics == SemanticsKind.Owner && SupportsIdentityAcquisition(operandType) &&
                shorthand.OriginName is null && shorthand.OriginExpression is null && shorthand.OriginArguments is null)
            {
                for (var targetNode = conversion.Right; ;)
                {
                    Complete(targetNode, operandType);
                    if (ReferenceEquals(targetNode, syntax))
                    {
                        break;
                    }

                    targetNode = targetNode is ParenthesizedTypeKoto parentheses ? parentheses.Type : ((TypeSemanticsKoto)targetNode).Type!;
                }

                conversion.ConversionBinding = ReferenceEquals(operandType, BoundType.Never) ? ConversionBinding.Abrupt : ConversionBinding.Identity;
                return Complete(conversion, operandType);
            }

            Fail(conversion.Right, BindingFailure.Unsupported, true);
            return Fail(conversion, BindingFailure.Unsupported, true);
        }

        var target = this.BindType(conversion.Right, scope);

        // Explicit owner targets use the same normalized numeric/identity operation.
        // Borrow and other ownership adaptations retain their separate rules.
        var plain = syntax is not TypeSemanticsKoto { Type: not null, IsTransparentWrapper: false } explicitSemantics ||
            (explicitSemantics.SemanticsKind == SemanticsKind.Owner && explicitSemantics.SemanticsParameter is null);
        var operand = KotoHelper.UnwrapParentheses(conversion.Left);
        var literal = operand is NumberLiteralKoto { IsInteger: true } or
            PrefixMinusKoto { Operand: NumberLiteralKoto { IsInteger: true } } or
            PrefixPlusKoto { Operand: NumberLiteralKoto { IsInteger: true } };
        var floatingLiteral = operand is NumberLiteralKoto { IsInteger: false } or
            PrefixMinusKoto { Operand: NumberLiteralKoto { IsInteger: false } } or
            PrefixPlusKoto { Operand: NumberLiteralKoto { IsInteger: false } };
        var fit = plain && ((target is { IsInteger: true } && literal) || (target is { IsFloatingPoint: true } && (literal || floatingLiteral)));
        var previousLiteral = this.floatingIntegerLiteral;
        BoundType? source;
        try
        {
            this.floatingIntegerLiteral = fit && literal && target is { IsFloatingPoint: true }
                ? operand as NumberLiteralKoto ?? ((UnaryKoto)operand).Operand as NumberLiteralKoto : null;
            source = this.BindNode(conversion.Left, scope, fit ? target : null);
        }
        finally
        {
            this.floatingIntegerLiteral = previousLiteral;
        }

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
            if (source.IsFloatingPoint && target.IsFloatingPoint)
            {
                conversion.ConversionBinding = fit ? ConversionBinding.Literal : ConversionBinding.Floating;
                return Complete(conversion, target);
            }

            if (ScalarTypes.Width(source, this.compilation.PointerWidth) == 0 || ScalarTypes.Width(target, this.compilation.PointerWidth) == 0)
            {
                var integer = source.IsInteger ? source : target;
                if (ScalarTypes.Width(integer, this.compilation.PointerWidth) is > 0 and <= 64)
                {
                    conversion.ConversionBinding = ConversionBinding.Numeric;
                    return Complete(conversion, target);
                }

                return Fail(conversion, BindingFailure.Unsupported, true);
            }

            conversion.ConversionBinding = fit ? ConversionBinding.Literal : ConversionBinding.Integer;
            return Complete(conversion, target);
        }

        if (ReferenceEquals(source, target) && SupportsIdentityAcquisition(source))
        {
            conversion.ConversionBinding = ConversionBinding.Identity;
            return Complete(conversion, target);
        }

        // Other ownership/borrow adaptations require their own verified paths.
        var unsupported = !SupportsIdentityAcquisition(source) || !SupportsIdentityAcquisition(target);
        return Fail(conversion, unsupported ? BindingFailure.Unsupported : BindingFailure.TypeMismatch, unsupported);
    }
}
