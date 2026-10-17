// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns instance identity (DECISIONS R5-M21; R5 package F 4.11, K 4.1, X8): whether a definition's body is checked once and generated per
// closed substitution (Instantiates), the null rule of instance keys.
// Input: a definition's BindingSymbol (a named or anonymous function, an accessor's executing function, a property) once the pass has bound
// its schemas. Output: a bool per definition, kept across passes with no edit and discarded at an edit (Binding.Invalidate).
// Invariants: true exactly when the definition or a declaration enclosing it on its scope chain has a Type, Semantics or length slot, even an
// unused one (SPEC 6.1.3), and no specialization lies between them. A specialization and the declarations nested in it count as closed, the
// re-pointed legacy readers' behavior (DECISIONS R5-M21, until K1), though SPEC 8.13.1 closes only its arguments: one in a generic container
// stays generic in the container's slots (an open SPEC 6.1.3 gap). Origin slots never count; instance identity excludes them (SPEC 8.13.3).
// Must not: decide by syntax what a declaration's schema publishes, or write any state other than its memo.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class TyCtxt
{
    // Per definition; its scope chain and the schemas on it change only at an edit.
    private readonly Dictionary<BindingSymbol, bool> instantiates = new();

    // SPEC 6.1.3, 8.15.1: whether a body keyed by the definition is generated per closed substitution of its own and its enclosing
    // declarations' slots rather than once.
    internal bool Instantiates(BindingSymbol definition)
    {
        if (!this.instantiates.TryGetValue(definition, out var result))
        {
            result = HasSlot(definition);
            this.instantiates.Add(definition, result);
        }

        return result;
    }

    // R4-M51: discarded with the source analysis at an edit.
    internal void ResetInstances() => this.instantiates.Clear();

    // The definition, then each declaration its scope chain passes, up to the first with a slot or the first specialization.
    private static bool HasSlot(BindingSymbol definition)
    {
        BindingSymbol? declaration = definition;
        for (var scope = definition.Scope; ; scope = scope.Parent)
        {
            if (declaration?.Declaration is FunctionKoto { IsSpecialization: true })
            {
                return false;
            }

            if (declaration?.Schema is { GenericSlots.Count: > 0 })
            {
                return true;
            }

            if (scope is null)
            {
                return false;
            }

            // A scope's owner contributes the slots of the declaration it is, never those of a symbol it names (a Pattern's Type).
            declaration = scope.Owner.BoundSymbol is { } owner && ReferenceEquals(owner.Declaration, scope.Owner) ? owner : null;
        }
    }
}
