// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// Declaration schemas (SPEC 8.1, 15.3.1): a declaration's generic slots and Origin parameters, its inherited environment included,
// reused while the ordered header is unchanged.
public sealed partial class Binding
{
    // SPEC 8.1: length slots belong only to functions; a nominal header with one is invalid, but its recovery Type is kept.
    private static LengthParameterKoto? LengthSlot(DeclarationContainerKoto container)
    {
        for (var i = 0; i < container.GenericParameterNodes.Count; i++)
        {
            if (container.GenericParameterNodes[i] is LengthParameterKoto slot)
            {
                return slot;
            }
        }

        return null;
    }

    private static GenericSlotKind SlotKind(TypeKoto syntax, BindingSymbol symbol)
        => syntax is LengthParameterKoto ? GenericSlotKind.Length : symbol.Pair is not null ? GenericSlotKind.Pair : GenericSlotKind.Type;

    private static SourceSpan OriginSpan(Koto owner, IReadOnlyList<string> names, int index)
        => names is OriginNameList locations && index < locations.Spans.Count ? locations.Spans[index] : owner.Span;

    private void BindSchemas()
    {
        for (var n = 0; n < this.nodes.Count; n++)
        {
            var node = this.nodes[n];
            IReadOnlyList<TypeKoto> parameters;
            IReadOnlyList<string> origins;
            if (node is FunctionKoto { IsGenerated: false, IsAnonymous: false } function)
            {
                // Specialization arguments are concrete Types, not generic declaration slots.
                parameters = function.IsSpecialization ? [] : function.GenericArguments;
                origins = function.Origins;
            }
            else if (node is PropertyAccessorKoto accessor)
            {
                parameters = [];
                origins = accessor.Origins;
            }
            else if (node is DeclarationContainerKoto { IsRoot: false } container)
            {
                if (container.Parent is not (GroupKoto or StructKoto) &&
                    container.Parent is not CodeBlockKoto { DeclarationContext: Lexing.TokenKind.Group or Lexing.TokenKind.Struct })
                {
                    this.Fail(node, BindingFailure.InvalidTypeFormation);
                }

                if (container.HasIncompatibleBindingHeader)
                {
                    this.Fail(node, BindingFailure.Duplicate);
                }

                parameters = container.GenericParameterNodes;
                if (LengthSlot(container) is { } slot)
                {
                    this.AddPrerequisite(node, slot); // The invalid header is kept for recovery; a slot the parser reported explains the failure.
                    this.Fail(node, BindingFailure.InvalidTypeFormation);
                }

                origins = container.OriginNames;
            }
            else
            {
                continue;
            }

            var symbol = node.BoundSymbol!;
            var scope = this.scopes[node];
            origins = this.DiscoverOriginNames(node, origins, scope);
            var inherited = node is DeclarationContainerKoto && node.Parent is DeclarationContainerKoto parent ? parent.BoundSymbol?.Schema : null;
            var genericCount = parameters.Count + (inherited?.GenericSlots.Count ?? 0);
            var originCount = origins.Count + (inherited?.Origins.Count ?? 0);
            var schema = symbol.Schema;
            if (!this.SchemaMatches(schema, node, parameters, origins, inherited))
            {
                var slots = genericCount == 0 ? Array.Empty<GenericSlot>() : new GenericSlot[genericCount];
                for (var i = 0; i < parameters.Count; i++)
                {
                    var parameter = this.symbols[parameters[i]];
                    slots[i] = new(SlotKind(parameters[i], parameter), parameter, parameter.Pair);
                }

                for (var i = 0; inherited is not null && i < inherited.GenericSlots.Count; i++)
                {
                    var outer = inherited.GenericSlots[i];
                    slots[parameters.Count + i] = new(outer.Kind, outer.Symbol, outer.Semantics);
                }

                var bindings = originCount == 0 ? Array.Empty<OriginParameter>() : new OriginParameter[originCount];
                for (var i = 0; i < origins.Count; i++)
                {
                    bindings[i] = new(origins[i], i, this.OriginAtom(node, OriginKind.Parameter, i, origins[i]), OriginSpan(node, origins, i));
                }

                for (var i = 0; inherited is not null && i < inherited.Origins.Count; i++)
                {
                    var outer = inherited.Origins[i];
                    bindings[origins.Count + i] = new(outer.Name, outer.Slot, outer.Origin, outer.Span);
                }

                symbol.Schema = schema = new(slots, bindings);
            }

            for (var i = 0; i < schema!.GenericSlots.Count; i++)
            {
                schema.GenericSlots[i].OriginVariance = OriginVariance.Unused;
                if (i >= parameters.Count)
                {
                    continue;
                }

                schema.GenericSlots[i].Symbol.Slot = i;
                if (schema.GenericSlots[i].Semantics is { } semantics)
                {
                    semantics.Slot = i;
                }
            }

            for (var i = 0; i < schema.Origins.Count; i++)
            {
                var origin = schema.Origins[i];
                origin.Variance = OriginVariance.Unused;
                origin.ResetRequirement();
                if (i < origins.Count && scope.Parent is { } enclosing && FindAbstractOrigin(origin.Name, enclosing) is not null)
                {
                    this.Fail(node, BindingFailure.Duplicate);
                }

                scope.Origins ??= new(StringComparer.Ordinal);
                if (!scope.Origins.TryAdd(origin.Name, origin.Origin))
                {
                    this.Fail(node, BindingFailure.Duplicate);
                }
            }
        }

        static BoundOrigin? FindAbstractOrigin(string name, BindingScope scope)
        {
            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Origins?.TryGetValue(name, out var origin) == true)
                {
                    return origin;
                }
            }

            return null;
        }
    }

    // Schema reuse follows the complete ordered header, including its inherited environment. Counting slots alone
    // misses same-sized edits. The unchanged path compares existing storage without allocating a replacement header.
    private bool SchemaMatches(DeclarationSchema? schema, Koto owner, IReadOnlyList<TypeKoto> parameters, IReadOnlyList<string> origins, DeclarationSchema? inherited)
    {
        if (schema is null || schema.GenericSlots.Count != parameters.Count + (inherited?.GenericSlots.Count ?? 0) ||
            schema.Origins.Count != origins.Count + (inherited?.Origins.Count ?? 0))
        {
            return false;
        }

        for (var i = 0; i < schema.GenericSlots.Count; i++)
        {
            var slot = schema.GenericSlots[i];
            var symbol = i < parameters.Count ? this.symbols[parameters[i]] : inherited!.GenericSlots[i - parameters.Count].Symbol;
            var kind = i < parameters.Count ? SlotKind(parameters[i], symbol) : inherited!.GenericSlots[i - parameters.Count].Kind;
            if (!ReferenceEquals(slot.Symbol, symbol) || !ReferenceEquals(slot.Semantics, symbol.Pair) || slot.Kind != kind)
            {
                return false;
            }
        }

        for (var i = 0; i < schema.Origins.Count; i++)
        {
            var slot = schema.Origins[i];
            if (i < origins.Count)
            {
                if (slot.Name != origins[i] || slot.Span != OriginSpan(owner, origins, i))
                {
                    return false;
                }
            }
            else
            {
                var original = inherited!.Origins[i - origins.Count];
                if (!ReferenceEquals(slot.Origin, original.Origin) || slot.Name != original.Name || slot.Span != original.Span)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
