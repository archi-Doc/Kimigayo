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

    // R7-C2c2: the key of `type` in a view's classes: a parameter's or projection's class's Representative, else the Type over its
    // components' keys (congruence), whose class is asked again. A constructed member (`i32`, a struct, `Option<U>`) keys to itself over
    // its components' keys: its conformance is ResolveConformance's alone (SPEC 8.3.2's closed world), so no parameter's premise rows
    // reach it, and a premise contradicting it stays the premise's error. A key names the class to the premise supply only; no reader
    // sees it as a Type. Each
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

    // The table's part of the key: its least member per class (Representative) and the keys it keeps (cleared at Open with the rest).
    private sealed partial class IdentityClasses
    {
        private readonly Dictionary<BoundType, BoundType> keys = new(ReferenceEqualityComparer.Instance); // ClassKey's answers (R7-C2c2)
        private readonly Dictionary<int, BoundType?> complete = new(); // CompleteMember's answer per class root (N9)
        private readonly Dictionary<int, BoundType?> least = new(); // Representative's answer per class root (R7-C2c2)

        // R7-C2c2: the table's key of a Type ClassKey formed from it, or null. Keys are a function of the closed table (no class joins
        // after Close), so each Type is keyed once per table; they go with the table at its next Open.
        internal BoundType? KeyOf(BoundType type) => this.keys.GetValueOrDefault(type);

        internal void SetKey(BoundType type, BoundType key) => this.keys[type] = key;

        // R7-C2c2: the key of a class in state One whose meets are complete, its least parameter or projection by Order (shallowest
        // first); null for a Type that is no node, a class with no such member, or an erroneous one (an erroneous class keys nothing).
        // The table's finality is not read: it decides only reuse (ContractSolver's kept rows). A constructed Type receives no key (ClassKey).
        internal BoundType? Representative(BoundType type) => this.Scanned(type) is { } root ? this.least[root] : null;

        // N9 (SPEC 8.2.3, 8.3.2): the complete constructed member (no parameter in it) of the same class, least by Order (the members
        // agree, so any one decides a conformance by the closed world); null otherwise. With Representative it is the one fact c2-1's
        // rule rests on: a constructed member's conformance is ResolveConformance's alone. The answer is the table's, so it carries
        // the table's Completion (Provisional while the table is not Final).
        internal BoundType? CompleteMember(BoundType type) => this.Scanned(type) is { } root ? this.complete[root] : null;

        // The one class-validity rule and member scan behind both answers: a parameter's or projection's class in state One whose
        // meets are complete, scanned once per table (its members are fixed after Close), whatever the number of its members asked.
        private int? Scanned(BoundType type)
        {
            if (type.Kind is not (BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection) || !this.node.TryGetValue(type, out var n))
            {
                return null;
            }

            var root = this.Find(n);
            if (this.state[root] != One || this.incompleteClass[root])
            {
                return null;
            }

            if (!this.least.ContainsKey(root))
            {
                BoundType? best = null;
                BoundType? complete = null;
                for (var m = this.head[root]; m >= 0; m = this.next[m])
                {
                    var member = this.types[m];
                    binding.IdentityKeyWork++;
                    if (member.Kind is BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection)
                    {
                        best = best is null || Order(member, best) < 0 ? member : best;
                    }
                    else if (!member.ContainsParameter)
                    {
                        complete = complete is null || Order(member, complete) < 0 ? member : complete;
                    }
                }

                this.least[root] = best;
                this.complete[root] = complete;
            }

            return root;
        }
    }
}
