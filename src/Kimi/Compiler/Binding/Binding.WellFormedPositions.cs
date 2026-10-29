// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The positions the well-formedness check reads (SPEC 15.6.1, 15.3.3, 8.1.1, 8.1.2).
// - Responsibility: the one walk of a Type's well-formedness positions: each node's own clauses over its Origin arguments (an
//   established declaration with relations), and below each borrow or Slice layer `B/V during o` every Origin that V stores, each
//   node's stored slot (a pair layer's in its borrow cases) and each Origin argument at any depth, a position that must outlive `o`. A
//   nested Function Type is not entered: its well-formedness is its own (SPEC 15.3.4).
// - Inputs: the Type, beside the pattern it instantiates (the Type itself, or a declared Type before a call's substitution) and a value's
//   own Type at the same positions (`kept`); a consumer (IWellFormedPositions) with the use whose binders give a stored pair slot's case.
// - Outputs: the consumer's visits in one pre-order (a node's clauses, its layer's stored positions, then its parts), and the Type
//   rebuilt with the Origins the consumer replaced (Replace; only a consumer that never stops replaces one).
// - Invariants: the walk follows the pattern beside the Type while their shapes agree, and below a layer a pair layer of the pattern
//   stores its slot whatever its substitution is; `kept` is read only beside the same layer shape; a stored slot's case is its pair
//   binder's condition as the use gives it, which each consumer reads by its own rule (a premise, a requirement).
// - Prohibitions: no consumer restates which positions are read; the walk judges, bounds and records nothing itself.
public sealed partial class Binding
{
    // What a consumer does at the positions of WalkWellFormed; a false return stops the walk.
    private interface IWellFormedPositions
    {
        // The use whose binders give a stored pair slot's case.
        Koto Use { get; }

        // Whether to walk a node and its parts.
        bool Enter(BoundType type);

        // A node's own clauses: `declaration`'s relations over `type`'s Origin arguments, beside `kept`, the value's own Type there.
        bool Clauses(BoundType type, BoundType? kept, OriginDeclaration declaration);

        // One position below a layer (WellFormedPosition).
        bool Stored(in WellFormedPosition position);

        // The Origin the walk leaves at a position of the Type.
        BoundOrigin Replace(BoundOrigin origin);
    }

    private static bool SameLayer(BoundType actual, BoundType result)
        => actual.Kind == result.Kind && actual.Components.Count == result.Components.Count && actual.OriginArguments.Count == result.OriginArguments.Count;

    // The established declaration whose relations are `type`'s own clauses at this occurrence, or null.
    private OriginDeclaration? OwnClauses(BoundType type)
        => type.Symbol is { } symbol && type.OriginArguments.Count != 0 && this.originDeclarations.TryGetValue(symbol.Declaration, out var declaration) &&
            declaration.State == 3 && declaration.Relations.Count != 0 ? declaration : null;

    // The positions of `type` beside `pattern` and `kept`; false when the consumer stops. `type` is rebuilt with the Origins it replaced.
    private bool WalkWellFormed<T>(BoundType pattern, ref BoundType type, BoundType? kept, ref T consumer)
        where T : struct, IWellFormedPositions
    {
        if (!pattern.StoresOrigin || pattern.Kind == BoundTypeKind.Function || pattern.Kind != type.Kind || pattern.Components.Count != type.Components.Count ||
            !consumer.Enter(type))
        {
            return true;
        }

        kept = kept is not null && SameLayer(kept, type) ? kept : null; // Every position is the Type's own when the shapes differ.
        var node = new WalkedNode(this, type);
        try
        {
            if (this.OwnClauses(type) is { } declaration)
            {
                if (!consumer.Clauses(type, kept, declaration))
                {
                    return false;
                }

                for (var i = 0; i < type.OriginArguments.Count; i++)
                {
                    node.SetArgument(i, consumer.Replace(type.OriginArguments[i]));
                }
            }

            if ((IsBorrow(pattern.Semantics) || pattern.Kind == BoundTypeKind.Slice) && pattern.Origin is { } outer && type.Origin is { } substituted &&
                pattern.Components.Count != 0)
            {
                var referent = type.Components[0];
                if (!this.WalkStored(pattern.Components[0], ref referent, kept?.Components[0], outer, substituted, ref consumer))
                {
                    return false;
                }

                node.SetComponent(0, referent);
                node.SetOrigin(pattern.Components[0].StoresOrigin ? consumer.Replace(substituted) : substituted);
            }

            for (var i = 0; i < pattern.Components.Count; i++)
            {
                var part = node.Component(i);
                if (!this.WalkWellFormed(pattern.Components[i], ref part, kept?.Components[i], ref consumer))
                {
                    return false;
                }

                node.SetComponent(i, part);
            }

            type = node.Build();
            return true;
        }
        finally
        {
            node.Release();
        }
    }

    // The positions below a layer whose Origins are `outer` (the pattern's) and `substituted` (the Type's).
    private bool WalkStored<T>(BoundType pattern, ref BoundType type, BoundType? kept, BoundOrigin outer, BoundOrigin substituted, ref T consumer)
        where T : struct, IWellFormedPositions
    {
        // A pair layer of the pattern stores its slot whatever its substitution is; any other layer only beside the same shape.
        var shaped = pattern.Kind == type.Kind && pattern.Components.Count == type.Components.Count;
        if (!pattern.StoresOrigin || pattern.Kind == BoundTypeKind.Function || (!shaped && !TryPairLayer(pattern, out _, out _)))
        {
            return true;
        }

        kept = kept is not null && SameLayer(kept, type) ? kept : null;
        var node = new WalkedNode(this, type);
        try
        {
            // SPEC 8.1.1, 8.1.2, 15.6.5: a pair layer's slot, the implicit `o` of an original `s/T` included, is stored only in its binder's
            // borrow cases. At a use it is the slot its substitution stores: a borrow's Origin, a caller's pair slot in that pair's borrow
            // cases, and none for a value binding, which leaves the relation vacuous.
            if (this.OuterOrigin(pattern) is { } longer && this.StoredSlot(type, consumer.Use, out var condition) is { } value)
            {
                var keptSlot = kept is null ? null : this.OuterOrigin(kept);
                if (!consumer.Stored(new(longer, outer, type, value, keptSlot, substituted, condition)))
                {
                    return false;
                }

                if (ReferenceEquals(value, type.Origin))
                {
                    node.SetOrigin(consumer.Replace(value));
                }
            }

            for (var i = 0; shaped && i < Math.Min(pattern.OriginArguments.Count, type.OriginArguments.Count); i++)
            {
                if (!consumer.Stored(new(pattern.OriginArguments[i], outer, type, type.OriginArguments[i], kept?.OriginArguments[i], substituted, 0)))
                {
                    return false;
                }

                node.SetArgument(i, consumer.Replace(type.OriginArguments[i]));
            }

            for (var i = 0; shaped && i < pattern.Components.Count; i++)
            {
                var part = node.Component(i);
                if (!this.WalkStored(pattern.Components[i], ref part, kept?.Components[i], outer, substituted, ref consumer))
                {
                    return false;
                }

                node.SetComponent(i, part);
            }

            type = node.Build();
            return true;
        }
        finally
        {
            node.Release();
        }
    }

    // One position below a layer: the pattern's Origin `Longer` and the Type's `Value` there outlive the layer's Origins, the pattern's
    // `Outer` and the Type's `Substituted`; `Kept` is the value's own Origin there, `Type` the Type at the position and `Condition` the
    // Semantics case that stores a pair slot (0: every case).
    private readonly record struct WellFormedPosition(
        BoundOrigin Longer, BoundOrigin Outer, BoundType Type, BoundOrigin Value, BoundOrigin? Kept, BoundOrigin Substituted, ulong Condition);

    // A node of the walk with the Origins and parts its consumer replaced: the node itself until one differs, then scratch copies.
    private ref struct WalkedNode(Binding binding, BoundType type)
    {
        private BoundType[]? components;
        private BoundOrigin[]? arguments;
        private BoundOrigin? origin = type.Origin;

        internal readonly BoundType Component(int index) => this.components?[index] ?? type.Components[index];

        internal void SetOrigin(BoundOrigin replaced) => this.origin = replaced;

        internal void SetComponent(int index, BoundType part)
        {
            if (!ReferenceEquals(part, this.Component(index)))
            {
                if (this.components is null)
                {
                    this.components = binding.RentTypes(type.Components.Count);
                    for (var i = 0; i < type.Components.Count; i++)
                    {
                        this.components[i] = type.Components[i];
                    }
                }

                this.components[index] = part;
            }
        }

        internal void SetArgument(int index, BoundOrigin argument)
        {
            if (!ReferenceEquals(argument, this.arguments?[index] ?? type.OriginArguments[index]))
            {
                if (this.arguments is null)
                {
                    this.arguments = binding.originScratch.Rent(type.OriginArguments.Count);
                    for (var i = 0; i < type.OriginArguments.Count; i++)
                    {
                        this.arguments[i] = type.OriginArguments[i];
                    }
                }

                this.arguments[index] = argument;
            }
        }

        // The node with every replacement, interned; the node itself when nothing differs.
        internal readonly BoundType Build()
        {
            if (this.components is null && this.arguments is null && ReferenceEquals(this.origin, type.Origin))
            {
                return type;
            }

            var parts = this.components is null ? (ReadOnlySpan<BoundType>)(BoundType[])type.Components : this.components.AsSpan(0, type.Components.Count);
            var origins = this.arguments is null
                ? (ReadOnlySpan<BoundOrigin>)(BoundOrigin[])type.OriginArguments : this.arguments.AsSpan(0, type.OriginArguments.Count);
            return binding.types.Rebuild(type, parts, this.origin, origins);
        }

        internal void Release()
        {
            if (this.components is not null)
            {
                binding.typeScratch.Return(this.components, clearArray: true);
                this.components = null;
            }

            if (this.arguments is not null)
            {
                binding.originScratch.Return(this.arguments, clearArray: true);
                this.arguments = null;
            }
        }
    }
}
