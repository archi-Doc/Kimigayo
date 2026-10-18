// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns instance identity (DECISIONS R5-M21, R5-M34; R5 package F 4.11, K 4.1, X8, X40): whether a definition's body is checked once and
// generated per closed substitution (Instantiates), the null rule of instance keys.
// Input: a definition's BindingSymbol (a named or anonymous function, an accessor's executing function, a property) once the pass has bound
// its schemas. Output: a class per definition (Closed, Generic, GenericThroughSpecialization), kept across passes with no edit and
// discarded at an edit (Binding.Invalidate).
// Invariants: true exactly when the definition or a declaration enclosing it on its scope chain has a Type, Semantics or length slot, even an
// unused one (SPEC 6.1.3). A specialization declares no slot (SPEC 8.13.1), so one in a generic container, and every declaration nested in
// it, is generic in the container's slots, its instance key holding the container's arguments (SPEC 8.13.3). Origin slots never count.
// Temporary (R6-gen; G7 deletes it with the old generator, its one reader): InstantiatesInLegacy, the old generator's view, which counts a
// specialization and the declarations nested in it as closed (R5-M21's former rule, the only one under which it generates them correctly).
// Must not: decide by syntax what a declaration's schema publishes, or write any state other than its memo.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class TyCtxt
{
    // Per definition; its scope chain and the schemas on it change only at an edit.
    private readonly Dictionary<BindingSymbol, InstanceClass> instances = new();

    // Whether the walk from a definition meets a slot, and whether a specialization lies before it.
    private enum InstanceClass : byte
    {
        Closed,
        Generic,
        [Transitional("R6-gen", Reason = "G7 deletes the old generator, the one reader that tells it from Generic.")]
        GenericThroughSpecialization,
    }

    // SPEC 6.1.3, 8.15.1: whether a body keyed by the definition is generated per closed substitution of its own and its enclosing
    // declarations' slots rather than once.
    internal bool Instantiates(BindingSymbol definition) => this.Classify(definition) != InstanceClass.Closed;

    // The old generator's view (GenericStoragePlan.IsGeneric): a specialization and the declarations nested in it count as closed.
    [Transitional("R6-gen", Reason = "G7 deletes the old generator, its one reader (DECISIONS R5-M34).")]
    internal bool InstantiatesInLegacy(BindingSymbol definition) => this.Classify(definition) == InstanceClass.Generic;

    // R4-M51: discarded with the source analysis at an edit.
    internal void ResetInstances() => this.instances.Clear();

    // The definition, then each declaration its scope chain passes, up to the first with a slot.
    private static InstanceClass Walk(BindingSymbol definition)
    {
        var specialized = false;
        BindingSymbol? declaration = definition;
        for (var scope = definition.Scope; ; scope = scope.Parent)
        {
            if (declaration?.Schema is { GenericSlots.Count: > 0 })
            {
                return specialized ? InstanceClass.GenericThroughSpecialization : InstanceClass.Generic;
            }

            if (scope is null)
            {
                return InstanceClass.Closed;
            }

            specialized |= declaration?.Declaration is FunctionKoto { IsSpecialization: true };

            // A scope's owner contributes the slots of the declaration it is, never those of a symbol it names (a Pattern's Type).
            declaration = scope.Owner.BoundSymbol is { } owner && ReferenceEquals(owner.Declaration, scope.Owner) ? owner : null;
        }
    }

    private InstanceClass Classify(BindingSymbol definition)
    {
        if (!this.instances.TryGetValue(definition, out var result))
        {
            result = Walk(definition);
            this.instances.Add(definition, result);
        }

        return result;
    }
}
