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

    private static BindingSymbol? SelectTypeCandidate(TypeCandidates candidates, Koto use)
    {
        if (candidates.Ambiguous)
        {
            Fail(use, BindingFailure.Ambiguous, true);
            return null;
        }

        // Retain a mismatching declaration for the existing formation/arity diagnostic.
        // A populated nearer stage must never fall through to an outer or default stage.
        return candidates.Match ?? candidates.First;
    }

    // A stack-local summary fixes the lookup stage before checking Type arity.
    private struct TypeCandidates
    {
        internal BindingSymbol? First;
        internal BindingSymbol? Match;
        internal bool Ambiguous;
    }

    private void AddTypeCandidates(ref TypeCandidates result, BindingSymbol? head, BindingScope scope, bool core, int arity)
    {
        for (var candidate = head; candidate is not null; candidate = candidate.Next)
        {
            if ((core && candidate.Kind == BindingSymbolKind.Container) || !this.Accessible(candidate, scope))
            {
                continue;
            }

            result.First ??= candidate;
            var count = candidate.Declaration is DeclarationContainerKoto container ? container.GenericParameterNodes.Count : 0;
            if (count != arity)
            {
                continue;
            }

            if (result.Match is not null && !ReferenceEquals(result.Match, candidate))
            {
                result.Ambiguous = true;
            }

            result.Match = candidate;
        }
    }

    private BindingSymbol? SelectTypeCandidate(BindingSymbol? head, BindingScope scope, Koto use, bool core, int arity = 0)
    {
        var candidates = default(TypeCandidates);
        this.AddTypeCandidates(ref candidates, head, scope, core, arity);
        return SelectTypeCandidate(candidates, use);
    }
}
