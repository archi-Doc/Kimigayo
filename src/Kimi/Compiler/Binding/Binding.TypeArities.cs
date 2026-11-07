// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static bool DistinctTypeArities(DeclarationContainerKoto declaration, BindingSymbol head)
    {
        for (var candidate = head; candidate is not null; candidate = candidate.Next)
        {
            if (candidate.Kind != BindingSymbolKind.Type || candidate.Declaration is not DeclarationContainerKoto other ||
                declaration.TokenKind != other.TokenKind || declaration.GenericParameterNodes.Count == other.GenericParameterNodes.Count)
            {
                return false;
            }
        }

        return true;
    }

    // A stack-local summary fixes the lookup stage before checking Type arity.
    private struct TypeCandidates
    {
        internal BindingSymbol? First;
        internal BindingSymbol? Match;
        internal bool Ambiguous;
        internal bool NonGeneric;
        internal AliasImport Import;
    }

    private void AddTypeCandidates(
        ref TypeCandidates result,
        BindingSymbol? head,
        BindingScope scope,
        TypeLookupRole role,
        int arity,
        AliasImport import = default)
    {
        for (var candidate = head; candidate is not null; candidate = candidate.Next)
        {
            if (!HasTypeLookupRole(candidate, role) || !this.Accessible(candidate, scope))
            {
                continue;
            }

            result.First ??= candidate;
            var count = candidate.Declaration is DeclarationContainerKoto container ? container.GenericParameterNodes.Count : 0;
            // An omitted construction target prefers own arity zero within this stage;
            // otherwise one distinct generic reference must remain. Evidence never reopens lookup.
            if (arity < 0 && count == 0 && !result.NonGeneric)
            {
                result.Match = null;
                result.Ambiguous = false;
                result.NonGeneric = true;
            }

            if (arity < 0 ? result.NonGeneric && count != 0 : count != arity)
            {
                continue;
            }

            var same = ReferenceEquals(result.Match, candidate) && ReferenceEquals(result.Import.Environment, import.Environment);
            if (result.Match is not null && !same)
            {
                result.Ambiguous = true;
            }

            result.Import = same ? result.Import.MergeWitness(import) : import;
            result.Match = candidate;
        }
    }
}
