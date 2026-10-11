// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private enum TypeLookupRole : byte
    {
        Any,
        Type,
        Semantics,
    }

    private static bool HasTypeLookupRole(BindingSymbol candidate, TypeLookupRole role)
        => role == TypeLookupRole.Any || (role == TypeLookupRole.Semantics
            ? candidate.Kind == BindingSymbolKind.SemanticsParameter
            : (candidate.Kind is BindingSymbolKind.Type or BindingSymbolKind.TypeParameter or BindingSymbolKind.SemanticsTarget or BindingSymbolKind.AssociatedType) && candidate.Declaration is not ContractKoto);

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

    private BindingSymbol? SelectTypeCandidate(TypeCandidates candidates, Koto use)
    {
        if (candidates.Ambiguous)
        {
            this.Fail(use, BindingFailure.Ambiguous, true);
            return null;
        }

        // Retain a mismatching declaration for the existing formation/arity diagnostic.
        // A populated nearer stage must never fall through to an outer or default stage.
        if (candidates.Environment is { } environment)
        {
            this.importedContainerEnvironments[use] = environment;
        }

        return candidates.Match ?? candidates.First;
    }

    // A stack-local summary fixes the lookup stage before checking Type arity.
    private struct TypeCandidates
    {
        internal BindingSymbol? First;
        internal BindingSymbol? Match;
        internal bool Ambiguous;
        internal bool NonGeneric;
        internal BoundType? Environment;
    }

    private void AddTypeCandidates(ref TypeCandidates result, BindingSymbol? head, BindingScope scope, bool core, int arity, BoundType? environment = null, TypeLookupRole role = TypeLookupRole.Any)
    {
        for (var candidate = head; candidate is not null; candidate = candidate.Next)
        {
            if ((core && candidate.Kind == BindingSymbolKind.Container) || !HasTypeLookupRole(candidate, role) || !this.Accessible(candidate, scope))
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

            if (result.Match is not null && (!ReferenceEquals(result.Match, candidate) || !ReferenceEquals(result.Environment, environment)))
            {
                result.Ambiguous = true;
            }

            result.Match = candidate;
            result.Environment = environment;
        }
    }

    private BindingSymbol? SelectTypeCandidate(BindingSymbol? head, BindingScope scope, Koto use, bool core, int arity = 0)
    {
        var candidates = default(TypeCandidates);
        this.AddTypeCandidates(ref candidates, head, scope, core, arity);
        return this.SelectTypeCandidate(candidates, use);
    }
}
