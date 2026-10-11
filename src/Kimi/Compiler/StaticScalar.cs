// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Immutable group scalars, with folded reads or a first-access initializer execution view.</summary>
internal static class StaticScalar
{
    internal static bool IsDynamic(BoundProperty? property)
    {
        if (property is not { IsVerified: true, Getter.IsStandard: true, Symbol.Scope.Owner: GroupKoto } ||
            property.Declaration.DeclarationKind != PropertyDeclarationKind.Let || property.Declaration.AttributeChain is not null ||
            property.Declaration.InitializerKoto is null || !ScalarTypes.Supports(property.Type) || TryGet(property, out _))
        {
            return false;
        }

        // A closed per-binding storage key is required before supporting generic static fields.
        for (var owner = property.Symbol.Scope.Owner; owner is not null; owner = owner.Parent)
        {
            if (owner is DeclarationContainerKoto { GenericArguments.Count: > 0 })
            {
                return false;
            }
        }

        return true;
    }

    internal static FunctionKoto Initializer(BoundProperty property)
    {
        var function = property.InitializerFunction ??= new(property);
        function.RefreshStaticInitializer();
        return function;
    }

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

        var number = KotoHelper.SignedNumber(source, out var negative);
        return number is { IsInteger: true } && number.TryGetIntegerMagnitude(out var magnitude) &&
            ScalarTypes.TryLiteral(property.Type, magnitude, negative, 64, out value);
    }
}
