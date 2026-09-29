// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    // Exactly the given stored fields, in order, each with its bound declared Type.
    private static bool BoundFields(DeclarationContainerKoto declaration, BoundType first, BoundType second, BoundType? third = null)
    {
        var count = third is null ? 2 : 3;
        for (var i = 0; i < count; i++)
        {
            var expected = i == 0 ? first : i == 1 ? second : third;
            if (StoredField(declaration, i) is not { BoundSymbol.Type: { } type } field || !ReferenceEquals(field.TypeKoto?.BoundType, type) || !ReferenceEquals(type, expected))
            {
                return false;
            }
        }

        return StoredField(declaration, count) is null;
    }

    // Computed Properties are members, not storage.
    private static VariableKoto? StoredField(DeclarationContainerKoto declaration, int ordinal)
    {
        for (var i = 0; i < declaration.Members.Count; i++)
        {
            if (declaration.Members[i] is VariableKoto field && field is not PropertyKoto { DeclarationKind: not (PropertyDeclarationKind.Let or PropertyDeclarationKind.Var) } && ordinal-- == 0)
            {
                return field;
            }
        }

        return null;
    }

    // Option<T> is Some(T) then None; Result<T, E> is Ok(T) then Err(E).
    private static bool ValidBoundCases(BindingSymbol symbol, bool option)
    {
        if (symbol.Declaration is not EnumKoto declaration || declaration.GenericParameterNodes.Count != (option ? 1 : 2))
        {
            return false;
        }

        for (var i = 0; i < 2; i++)
        {
            if (declaration.Members[(option ? 1 : 0) + i] is not SyntaxFormKoto { BoundSymbol.EnumCase: { } bound } form ||
                !ReferenceEquals(bound.Symbol.Declaration, form) || !ReferenceEquals(bound.Owner, symbol) || bound.Ordinal != i)
            {
                return false;
            }

            var payload = bound.Payload;
            var expected = option && i == 1 ? null : declaration.GenericParameterNodes[option ? 0 : i].BoundType;
            if (payload.Length != (expected is null ? 0 : 1) ||
                (expected is not null && (expected.Kind != BoundTypeKind.Parameter || !ReferenceEquals(payload[0].BoundType, expected))))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 4.6.3 and 11.2: lowering constructs and matches these Cases and fields directly (Option/Result results and
    // the ResolvedRange key of a slice selection), so their bound payload and field Types are fixed.
    private bool ValidBoundRecordLayout(BindingSymbol symbol, KimiDeclarationId id)
    {
        if (id is KimiDeclarationId.Option or KimiDeclarationId.Result)
        {
            return ValidBoundCases(symbol, id == KimiDeclarationId.Option);
        }

        if (symbol.Declaration is not StructKoto declaration)
        {
            return id != KimiDeclarationId.ResolvedRange;
        }

        return id != KimiDeclarationId.ResolvedRange || BoundFields(declaration, BoundType.ISize, BoundType.ISize);
    }
}
