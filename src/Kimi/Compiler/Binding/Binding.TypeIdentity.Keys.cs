// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Binding.TypeIdentity's class keys (the entry file states the contract; R7-C2c2 c2-1): ClassKey, a Type's key in a view's identity
// table, which only ContractSolver's premise supply reads, and Order, the total structural order whose least parameter or projection
// is a class's key (IdentityClasses.Representative). The table keeps each key until its next Open.
public sealed partial class Binding
{
    // R7-C2c2: a total structural order on Types that never reads premise order (ClassKey's least member): projection depth, kind,
    // the symbol (its declaration's span, then its place among every source, CompareNodes, then name and kind), Semantics,
    // components, Origins (CompareOrigins), length, and last the canonical name, which tells apart symbol-less Types (primitives).
    // Allocation-free: enums compare as integers (Enum.CompareTo(object) boxes).
    private static int Order(BoundType x, BoundType y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        var c = Depth(x).CompareTo(Depth(y));
        c = c != 0 ? c : ((int)x.Kind).CompareTo((int)y.Kind);
        c = c != 0 ? c : SymbolOrder(x.Symbol, y.Symbol);
        c = c != 0 ? c : ((int)x.Semantics).CompareTo((int)y.Semantics);
        c = c != 0 ? c : x.Components.Count.CompareTo(y.Components.Count);
        for (var i = 0; c == 0 && i < x.Components.Count; i++)
        {
            c = Order(x.Components[i], y.Components[i]);
        }

        c = c != 0 ? c : x.OriginArguments.Count.CompareTo(y.OriginArguments.Count);
        for (var i = 0; c == 0 && i < x.OriginArguments.Count; i++)
        {
            c = CompareOrigins(x.OriginArguments[i], y.OriginArguments[i]);
        }

        c = c != 0 ? c : OriginOrder(x.Origin, y.Origin); // a raw Origin is no class member's: members are parameters and projections
        c = c != 0 ? c : x.Length.CompareTo(y.Length);
        return c != 0 ? c : string.CompareOrdinal(x.Name, y.Name);

        static int Depth(BoundType type) => type.Kind == BoundTypeKind.AssociatedProjection ? 1 + Depth(type.Components[0]) : 0;
        static int OriginOrder(BoundOrigin? a, BoundOrigin? b) => a is null || b is null ? (b is null).CompareTo(a is null) : CompareOrigins(a, b);
    }

    private static int SymbolOrder(BindingSymbol? a, BindingSymbol? b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        if (a is null || b is null)
        {
            return a is null ? -1 : 1;
        }

        var c = a.Declaration.Span.Start.CompareTo(b.Declaration.Span.Start);
        c = c != 0 ? c : a.Declaration.Span.Length.CompareTo(b.Declaration.Span.Length);
        c = c != 0 || ReferenceEquals(a.Declaration, b.Declaration) ? c : CompareNodes(a.Declaration, b.Declaration); // another source
        c = c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
        return c != 0 ? c : ((int)a.Kind).CompareTo((int)b.Kind);
    }

    // R7-C2c2: the key of `type` in a view's classes: its class's Representative, else the Type over its components' keys
    // (congruence), whose class is asked again. A key names the class to the premise supply only; no reader sees it as a Type. Each
    // Type is keyed once per table (the table keeps it), so a request's keys cost the distinct Types it compares; a warm pass finds
    // the rebuilt Types interned and the table's capacity kept, and allocates nothing.
    private BoundType ClassKey(BoundType type, IdentityClasses classes)
    {
        if (classes.KeyOf(type) is not { } key)
        {
            this.IdentityKeyWork++;
            key = classes.Representative(type) ?? this.CongruentKey(type, classes);
            classes.SetKey(type, key);
        }

        return key;
    }

    // The Type over its components' keys, whose class is asked again; the Type itself when no component's key differs.
    private BoundType CongruentKey(BoundType type, IdentityClasses classes)
    {
        var count = type.Components.Count;
        if (count == 0 || !type.ContainsParameter)
        {
            return type;
        }

        var components = this.RentTypes(count);
        try
        {
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                components[i] = this.ClassKey(type.Components[i], classes);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            if (!changed)
            {
                return type;
            }

            var rebuilt = this.types.Rebuild(type, components.AsSpan(0, count), type.Origin, (BoundOrigin[])type.OriginArguments);
            return classes.Representative(rebuilt) ?? rebuilt;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    // The table's part of the key: its least member (Representative) and the keys it keeps (cleared at Open with the rest).
    private sealed partial class IdentityClasses
    {
        private readonly Dictionary<BoundType, BoundType> keys = new(ReferenceEqualityComparer.Instance); // ClassKey's answers (R7-C2c2)

        // R7-C2c2: the table's key of a Type ClassKey formed from it, or null. Keys are a function of the closed table (no class joins
        // after Close), so each Type is keyed once per table; they go with the table at its next Open.
        internal BoundType? KeyOf(BoundType type) => this.keys.GetValueOrDefault(type);

        internal void SetKey(BoundType type, BoundType key) => this.keys[type] = key;

        // R7-C2c2: the key of a class in state One whose meets are complete, its least parameter or projection by Order (shallowest
        // first); null for a Type that is no node, a class with no such member, or an erroneous one (an erroneous class keys nothing).
        // The table's finality is not read: it decides only reuse (ContractSolver's kept rows).
        internal BoundType? Representative(BoundType type)
        {
            if (!this.node.TryGetValue(type, out var n))
            {
                return null;
            }

            var root = this.Find(n);
            if (this.state[root] != One || this.incompleteClass[root])
            {
                return null;
            }

            BoundType? best = null;
            for (var m = this.head[root]; m >= 0; m = this.next[m])
            {
                var member = this.types[m];
                binding.IdentityKeyWork++;
                if (member.Kind is BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection && (best is null || Order(member, best) < 0))
                {
                    best = member;
                }
            }

            return best;
        }
    }
}
