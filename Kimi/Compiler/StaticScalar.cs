// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Pure immutable scalar initializers whose first access has no observable initialization effect.</summary>
internal static class StaticScalar
{
    internal static bool TryGet(BoundProperty? property, out Int128 value)
    {
        value = 0;
        if (property is not { IsVerified: true, Getter.IsStandard: true, Symbol.Scope.Owner: GroupKoto } ||
            property.Declaration.DeclarationKind != PropertyDeclarationKind.Let || property.Declaration.AttributeChain is not null ||
            property.Declaration.InitializerKoto is not { } source || !ReferenceEquals(source.BoundType, property.Type))
        {
            return false;
        }

        source = KotoHelper.UnwrapParentheses(source);
        if (source is BoolLiteralKoto boolean && ReferenceEquals(property.Type, BoundType.Boolean))
        {
            value = boolean.Value ? 1 : 0;
            return true;
        }

        var number = source is PrefixMinusKoto or PrefixPlusKoto ? ((UnaryKoto)source).Operand as NumberLiteralKoto : source as NumberLiteralKoto;
        return number is { IsInteger: true } && number.TryGetIntegerMagnitude(out var magnitude) &&
            ScalarTypes.TryLiteral(property.Type, magnitude, source is PrefixMinusKoto, 64, out value);
    }
}
