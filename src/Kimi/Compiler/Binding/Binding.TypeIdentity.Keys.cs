// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

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

    // R7-C2c2: the key of `type` in a view's classes: its class's Representative (c2-4: a constructed member's too, so every member's
    // premise rows are the class's, SPEC 8.2.3 "a Contract fact about a Type is evidence for an identical one, also where that Type is
    // constructed"), else the Type over its components' keys (congruence), whose class is asked again. A lent row meeting the member's
    // closed world is contradictory evidence (ProveConformance's polarity join, SPEC 8.3.2), never a Proven over a Refuted. A
    // key names the class to the premise supply only; no reader sees it as a Type. Each
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

    // R7-C2c2 c2-4 (design R1.1): the subject an atom judge takes: the Type's identity class's Member in a view with an identity
    // premise, else the Type, with the table's Completion (Complete without an identity premise; a re-entered build, which answers no
    // table, Provisional). A Member whose operand contexts are unresolved (a concrete prerequisite still pending) stands for nothing
    // yet: the Type itself is judged, Unknown as before (UnresolvedCapabilityProofBindingTest's pending `Target`). ContractTypeCore's
    // (E) arm meets the Completion (W4); the judges (ProveConformance, RequestCapability, JudgeConstraintAtom's other arms) read it
    // unthreaded, as PremiseRefines' (ContractSolver's header), until R7-C2e2 carries Completion through the judgments' answers.
    private BoundType MemberOf(BoundType type, BindingScope scope, out Completion completion)
    {
        completion = Completion.Complete;
        if (!HasTypeIdentity(scope))
        {
            return type;
        }

        return this.ClassesOf(scope, out completion)?.Member(type) is { } member && !this.OperandContexts(member).Unresolved ? member : type;
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
        private readonly Dictionary<int, (BoundType? Least, BoundType? Member)> scanned = new(); // per class root: Representative's and Member's answers
        private readonly Dictionary<int, (int Start, int Count)> projections = new(); // ProjectionMembers' range per class root (G881)
        private readonly List<BoundType> projectionMembers = new();

        // PLAN G881: the projection members of a Type's class in Order, appended to `into`, where Members lists the class (ContractSolver's
        // row (5) walks them). Each class is scanned and sorted once per table, so a link of a parameter-only class costs a lookup.
        internal void ProjectionMembers(BoundType type, List<BoundType> into)
        {
            var root = this.ListedRoot(type);
            if (root < 0)
            {
                return;
            }

            if (!this.projections.TryGetValue(root, out var range))
            {
                var start = this.projectionMembers.Count;
                for (var m = this.head[root]; m >= 0; m = this.next[m])
                {
                    binding.IdentityMateWork++;
                    if (this.types[m].Kind == BoundTypeKind.AssociatedProjection)
                    {
                        this.projectionMembers.Add(this.types[m]);
                    }
                }

                range = (start, this.projectionMembers.Count - start);
                CollectionsMarshal.AsSpan(this.projectionMembers).Slice(range.Start, range.Count).Sort(static (x, y) => Order(x, y));
                this.projections[root] = range;
            }

            for (var i = range.Start; i < range.Start + range.Count; i++)
            {
                into.Add(this.projectionMembers[i]);
            }
        }

        // R7-C2c2: the table's key of a Type ClassKey formed from it, or null. Keys are a function of the closed table (no class joins
        // after Close), so each Type is keyed once per table; they go with the table at its next Open.
        internal BoundType? KeyOf(BoundType type) => this.keys.GetValueOrDefault(type);

        internal void SetKey(BoundType type, BoundType key) => this.keys[type] = key;

        // R7-C2c2: the key of a class in state One whose meets are complete, its least parameter or projection by Order (shallowest
        // first), for any node of it, constructed ones included (c2-4); null for a Type that is no node, a class with no such member, or
        // an erroneous one (an erroneous class keys nothing). The table's finality is not read: it decides only reuse (ContractSolver's
        // kept rows).
        internal BoundType? Representative(BoundType type) => this.Scanned(type).Least;

        // R7-C2c2 c2-4 (SPEC 8.2.3, 8.3.2; design R1.1, R1.2): the class's constructed member whose closed world decides an atom about
        // any member, a closed one (no parameter) first, else the least by Order (the more determined spelling; never premise order);
        // null as Representative. The members of a class in state One are identical, so any one decides. Its readers: MemberOf (the
        // atom judges' subjects) and Refuted.
        internal BoundType? Member(BoundType type) => this.Scanned(type).Member;

        // The one class-validity rule and member scan behind both answers: a node's class (any node, constructed ones included) in
        // state One whose meets are complete, scanned once per table (its members are fixed after Close), whatever the number asked.
        // Its Kind list names the members that may key a class (R7-M25: a Type parameter, the original `s/T` among them, or an
        // associated projection), not SPEC 8.2.3's abstract Type, which the meets test by IsAbstractType.
        private (BoundType? Least, BoundType? Member) Scanned(BoundType type)
        {
            if (!this.node.TryGetValue(type, out var n))
            {
                return default;
            }

            var root = this.Find(n);
            if (this.state[root] != One || this.incompleteClass[root])
            {
                return default;
            }

            if (!this.scanned.TryGetValue(root, out var answer))
            {
                BoundType? best = null;
                BoundType? complete = null;
                BoundType? open = null;
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
                    else
                    {
                        open = open is null || Order(member, open) < 0 ? member : open;
                    }
                }

                this.scanned[root] = answer = (best, complete ?? open);
            }

            return answer;
        }
    }
}
