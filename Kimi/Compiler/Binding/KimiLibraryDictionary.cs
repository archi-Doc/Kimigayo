// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    private bool ValidDictionaryOperation(BindingSymbol symbol, KimiDeclarationId id)
    {
        ref readonly var rule = ref KimiLibraryCatalog.Entries[KimiLibraryCatalog.Index(id)];
        if (symbol.CompilerFunction != rule.Function || symbol.Declaration is not FunctionKoto function || !ReferenceEquals(function.Parent, this.DictionaryScope.Owner) ||
            function.NameBoundaryIndex >= 0 || function.Name != rule.Name || function.Modifier != ModifierKind.Public ||
            function.GenericArguments.Count != 0 || function.Origins.Count != 0 || function.TypeConstraints.Count != 0 ||
            function.Body is not null || function.ExpressionBody is not null || function.AttributeChain is not null ||
            function.IsRequirement || function.IsGenerated || function.IsSpecialization || function.Parameters.Count == 0 ||
            function.Parameters[0] is not { ExternalName: "self", InternalName: "self", DefaultValue: null, AttributeChain: null, Type: TypeSemanticsKoto { SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null } receiver } ||
            receiver.SemanticsKind != (id == KimiDeclarationId.DictionaryTryGet ? SemanticsKind.Ref : SemanticsKind.Uniq) || !BareName(receiver.Type, "Self"))
        {
            return false;
        }

        var inputs = function.Parameters.Count - 1;
        var result = BareType(function.ReturnType) as GenericsKoto;
        return id switch
        {
            KimiDeclarationId.DictionaryReserve => inputs == 1 && Input(1, "additional", "isize") && function.ReturnType is null,
            KimiDeclarationId.DictionaryTryInsert => inputs == 2 && Input(1, "key", "K") && Input(2, "value", "V") &&
                result is { TypeArguments.Count: 2 } && BareName(result.Identifier, "Result") && BareType(result.TypeArguments[0]) is TupleTypeKoto { ElementNodes.Count: 0 } && Pair(result.TypeArguments[1]),
            KimiDeclarationId.DictionaryInsertOrReplace => inputs == 2 && Input(1, "key", "K") && Input(2, "value", "V") &&
                result is { TypeArguments.Count: 1 } && BareName(result.Identifier, "Option") && BareName(result.TypeArguments[0], "V"),
            KimiDeclarationId.DictionaryRemove => inputs == 1 && Input(1, "key", "K", borrow: true) &&
                result is { TypeArguments.Count: 1 } && BareName(result.Identifier, "Option") && Pair(result.TypeArguments[0]),
            KimiDeclarationId.DictionaryTryGet => inputs == 1 && Input(1, "key", "K", borrow: true) &&
                result is { TypeArguments.Count: 1 } && BareName(result.Identifier, "Option") &&
                result.TypeArguments[0] is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Ref, SemanticsParameter: null, OriginArguments: null, OriginExpression: IdentifierNameKoto { IdentifierName: "self" }, Type: { } value } && BareName(value, "V"),
            _ => inputs == 0 && function.ReturnType is null,
        };

        bool Input(int index, string name, string type, bool borrow = false)
        {
            var parameter = function.Parameters[index];
            if (parameter.DefaultValue is not null || parameter.AttributeChain is not null || parameter.InternalName != name || parameter.ExternalName != name)
            {
                return false;
            }

            return borrow ? parameter.Type is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Ref, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null, Type: { } target } && BareName(target, type) : BareName(parameter.Type, type);
        }

        static bool Pair(Koto type) => BareType(type) is TupleTypeKoto { ElementNodes.Count: 2 } tuple && BareName(tuple.ElementNodes[0], "K") && BareName(tuple.ElementNodes[1], "V");
    }
}
