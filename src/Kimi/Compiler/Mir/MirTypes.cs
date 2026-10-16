// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the Type facts needed by MIR construction and analyses.
// Input: published Types, their stored shapes and Binding's Copy and Sealed proofs. Output: the destruction walk and the drop, observation,
// Loan-holding, raw, exclusivity, agreement, borrow-kind and pointer-cast facts.
// Inline storage is traversed under each fact's documented bounds and Type rules; what destroying a value does is the one Destruction walk.
// Must not inspect source syntax or substitute new Binding decisions.

#pragma warning disable SA1402 // The walk's visitor belongs to the Type facts.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

/// <summary>What the destruction walk of a Type reports (<see cref="MirTypes.Destruction"/>).</summary>
internal interface IMirDestruction
{
    /// <summary>A part of the destroyed value whose destruction runs or releases something.</summary>
    /// <param name="type">The part's Type.</param>
    /// <param name="depth">The part's depth in the walk, from which a walk of what the part owns goes on (a Sealed handle's payload).</param>
    /// <returns>Whether the walk stops.</returns>
    bool Visit(BoundType type, int depth);

    /// <summary>A part past the walk's depth bound, whose destruction the walk does not follow.</summary>
    /// <returns>Whether the walk stops.</returns>
    bool Deep();
}

/// <summary>The Type facts MIR decides by, each one walk of a Type's inline storage: its Tuple, fixed-array and Closure components,
/// a struct's Fields and base and an enum's Case payloads (plan 5.5).</summary>
internal static class MirTypes
{
    /// <summary>Gets whether a value of a Type may own what its destruction releases (SPEC 16.3), the one rule by which the builder drops
    /// an old value: whether its <see cref="Destruction"/> reports a part.</summary>
    /// <param name="type">The Type.</param>
    /// <returns>Whether drop elaboration has to consider the value.</returns>
    internal static bool MayNeedDrop(BoundType? type)
    {
        var any = default(AnyPart);
        return Destruction(type, ref any);
    }

    /// <summary>Gets whether destroying a value of a Type may observe the Origins and Loans it holds (SPEC 15.6.6, 16.3.3): its
    /// <see cref="Destruction"/> reaches a struct that declares a drop, which observes all it reaches; a Function value or an abstract part
    /// not proven Copy, whose destruction is unknown; or an owning handle whose payload is not proven Sealed, or is and observes, since any
    /// rc or arc release may be final. A payload is walked once per query, below its handle: a recursive Type ends, exactly where it
    /// repeats and at the walk's bound where it grows.</summary>
    /// <param name="type">The Type.</param>
    /// <param name="types">The Type context, whose Copy and Sealed proofs Binding publishes.</param>
    /// <param name="definition">The definition whose premises prove Copy and Sealed.</param>
    /// <param name="seen">Scratch for the payloads walked, cleared first.</param>
    /// <param name="deep">Whether the walk reached a part past its depth bound, which this implementation does not follow (SPEC 8.15):
    /// the caller's located Unsupported_Kd.</param>
    /// <returns>Whether the destruction may observe them; true when deep.</returns>
    internal static bool Observes(BoundType? type, TyCtxt types, BindingSymbol? definition, HashSet<BoundType> seen, out bool deep)
    {
        seen.Clear();
        var observer = new Observer(types, definition, seen);
        var observes = Destruction(type, ref observer);
        deep = observer.Beyond;
        return observes;
    }

    /// <summary>Walks what destroying a value of a Type does (SPEC 16.3), the one walk of its inline storage that drop elaboration and the
    /// effect summaries share. It reports each part that runs or releases something: an abstract Type (its instance decides), an owning
    /// handle, a Function value (its environment), a string, an Array or a Dictionary, a struct that declares a drop (whose destructor body
    /// alone destroys its parts, R4-M6b), and as Deep a part past depth 32; it walks on into the components of a Tuple, fixed array,
    /// Closure, Array or Dictionary, a struct's Fields and base and an enum's Case payloads. References, raw pointers, Slices, scalars and
    /// an InlineStorage, whose owner destroys its contents (<see cref="AdtDef.IsInlineStorage"/>), release nothing.</summary>
    /// <typeparam name="TVisitor">The visitor's struct type.</typeparam>
    /// <param name="type">The Type.</param>
    /// <param name="visitor">The visitor.</param>
    /// <param name="depth">The walk's depth.</param>
    /// <returns>Whether the visitor stopped the walk.</returns>
    internal static bool Destruction<TVisitor>(BoundType? type, ref TVisitor visitor, int depth = 0)
        where TVisitor : struct, IMirDestruction
    {
        if (type is null)
        {
            return false;
        }

        if (depth > 32)
        {
            return visitor.Deep();
        }

        if (type.IsAbstract || type.Kind == BoundTypeKind.Function)
        {
            return visitor.Visit(type, depth);
        }

        if (type.Kind == BoundTypeKind.Semantics)
        {
            return type.Semantics is SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc && visitor.Visit(type, depth);
        }

        var sequence = type.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary; // A managed sequence releases its storage and its elements.
        var destructor = AdtDef.Destructor(type) is not null;
        if ((sequence || ReferenceEquals(type, BoundType.String) || destructor) && visitor.Visit(type, depth))
        {
            return true;
        }

        if (destructor || AdtDef.IsInlineStorage(type))
        {
            return false;
        }

        var parts = sequence || type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Closure
            ? type.Components : AdtDef.CaseTypes(type) ?? [];
        for (var i = 0; i < parts.Count; i++)
        {
            if (Destruction(parts[i], ref visitor, depth + 1))
            {
                return true;
            }
        }

        for (var i = 0; AdtDef.IsStruct(type) && i < AdtDef.Count(type); i++)
        {
            if (Destruction(AdtDef.FieldType(type, i), ref visitor, depth + 1))
            {
                return true;
            }
        }

        return AdtDef.Base(type) is { } parent && Destruction(parent, ref visitor, depth + 1);
    }

    /// <summary>Gets whether a value of a Type is a reference whose Loans an EndBorrow ends: a ref, uniq, objref or objuniq, or a pair layer.</summary>
    /// <param name="type">The Type.</param>
    /// <returns>Whether the value is a reference.</returns>
    internal static bool IsReference(BoundType? type)
        => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq } ||
            (TyCtxt.Layer(type, out _, out var binder) is var _ && binder is not null);

    /// <summary>Gets whether a Type is an exclusive reference, <c>uniq</c> or <c>objuniq</c>: a borrow of its mode is exclusive, and a bare
    /// acquisition of a Place storing it Reborrows it in its own Semantics where another Type's Copies (SPEC 3.5, 8.14).</summary>
    /// <param name="type">The Type.</param>
    /// <returns>Whether it is an exclusive reference.</returns>
    internal static bool IsExclusive(BoundType? type) => type?.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq;

    /// <summary>Gets the kind of the borrow that forms a reference Type (SPEC 13.5.6, 5.4), the mode of every Ref: an address for a raw
    /// pointer, a shared borrow for a shared reference, else an exclusive one, two-phase for a call's input: reserved at the borrow and
    /// activated at the Call (SPEC 15.6.7).</summary>
    /// <param name="reference">The reference Type.</param>
    /// <param name="input">Whether the borrow is a call's input.</param>
    /// <returns>The borrow's kind.</returns>
    internal static MirRefKind RefKind(BoundType reference, bool input)
    {
        if (reference.Semantics == SemanticsKind.Raw)
        {
            return MirRefKind.Raw;
        }

        if (!IsExclusive(reference))
        {
            return MirRefKind.Shared;
        }

        return input ? MirRefKind.TwoPhaseUniq : MirRefKind.Uniq;
    }

    /// <summary>Gets the Cast of a raw pointer conversion from a source Type (SPEC 5.4): Pointer from a raw pointer, else IntToPointer, an
    /// integer's, an environment effect (SPEC 8.11.2).</summary>
    /// <param name="source">The converted value's Type.</param>
    /// <returns>The Cast's kind.</returns>
    internal static MirCastKind PointerCast(BoundType source) => source.Semantics == SemanticsKind.Raw ? MirCastKind.Pointer : MirCastKind.IntToPointer;

    /// <summary>Gets whether two Types agree, the one Type agreement of MIR: equal but for the Origins and Origin arguments of either Type
    /// and its components, which an assignment or a call relates instead of equating (SPEC 15.3) and the ownership analysis checks (R4).
    /// Interned Types that carry no Origin agree only when identical; an unknown Type (null) agrees with any.</summary>
    /// <param name="left">A Type.</param>
    /// <param name="right">The other Type.</param>
    /// <returns>Whether they agree.</returns>
    internal static bool Agree(BoundType? left, BoundType? right)
    {
        if (ReferenceEquals(left, right) || left is null || right is null)
        {
            return true;
        }

        if ((!left.CarriesOrigin && !right.CarriesOrigin) || left.Kind != right.Kind || left.Semantics != right.Semantics ||
            !ReferenceEquals(left.Symbol, right.Symbol))
        {
            return false;
        }

        if (left.Length != right.Length || !ReferenceEquals(left.LengthExpression, right.LengthExpression))
        {
            return false;
        }

        if (!ReferenceEquals(left.ClosureContext, right.ClosureContext) || left.Components.Count != right.Components.Count ||
            !left.LengthArguments.AsSpan().SequenceEqual(right.LengthArguments))
        {
            return false;
        }

        for (var i = 0; i < left.Components.Count; i++)
        {
            if (!Agree(left.Components[i], right.Components[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Gets whether a value of a Type holds a raw pointer, an rc handle or a Weak of one at any depth, so that reading it from an
    /// immutable static obtains authority from the environment (SPEC 8.11.2); a referent reached through the value counts too.</summary>
    /// <param name="type">The Type.</param>
    /// <param name="depth">The walk's depth; past 32 the answer is yes.</param>
    /// <returns>Whether the value holds raw authority.</returns>
    internal static bool HoldsRaw(BoundType? type, int depth = 0)
        => type is not null && (depth > 32 || type.Semantics is SemanticsKind.Raw or SemanticsKind.Rc || Any(type.Components, depth) || Stored(type, depth));

    /// <summary>Gets whether a value of a Type may hold a Loan (SPEC 15.6), so that A5 tracks it: a part of it is a borrow or pair layer, a
    /// Slice, a Function or Closure value, or an abstract Type, whose instance may store a borrow its Origins do not show. Its parts are its
    /// own storage, a struct's Fields and base and an enum's payloads, and its components: elements, an owning handle's payload, and a
    /// Type argument, which its storage may hold behind a raw pointer (a library collection, its iterators) or as a Loan or a Weak (SPEC
    /// 3.2.2, 15.3.5). A raw pointer holds none (SPEC 5.2.2: its borrow ends at once), nor a function item. Each Type is walked once per
    /// query, so a repeating recursive Type ends exactly; past depth 32, where one grows (SPEC 15.3.5), the answer is yes.</summary>
    /// <param name="type">The Type.</param>
    /// <param name="seen">Scratch for the Types walked, cleared first.</param>
    /// <returns>Whether the value may hold a Loan.</returns>
    internal static bool HoldsLoans(BoundType? type, HashSet<BoundType> seen)
    {
        seen.Clear();
        return Holds(type, seen, 0);
    }

    private static bool Any(IReadOnlyList<BoundType> types, int depth)
    {
        for (var i = 0; i < types.Count; i++)
        {
            if (HoldsRaw(types[i], depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    // A struct's Fields and base, an enum's payload Tuples.
    private static bool Stored(BoundType type, int depth)
    {
        for (var i = 0; AdtDef.IsStruct(type) && i < AdtDef.Count(type); i++)
        {
            if (HoldsRaw(AdtDef.FieldType(type, i), depth + 1))
            {
                return true;
            }
        }

        return (AdtDef.Base(type) is { } parent && HoldsRaw(parent, depth + 1)) || (AdtDef.CaseTypes(type) is { } cases && Any(cases, depth));
    }

    // HoldsLoans' walk: a Type seen before adds nothing.
    private static bool Holds(BoundType? type, HashSet<BoundType> seen, int depth)
    {
        if (type is null || type.Semantics == SemanticsKind.Raw || type.Kind == BoundTypeKind.FunctionItem || !seen.Add(type))
        {
            return false;
        }

        if (depth > 32 || type.IsAbstract || type.Kind is BoundTypeKind.Slice or BoundTypeKind.Function or BoundTypeKind.Closure || IsReference(type))
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (Holds(type.Components[i], seen, depth + 1))
            {
                return true;
            }
        }

        for (var i = 0; AdtDef.IsStruct(type) && i < AdtDef.Count(type); i++)
        {
            if (Holds(AdtDef.FieldType(type, i), seen, depth + 1))
            {
                return true;
            }
        }

        foreach (var payload in AdtDef.CaseTypes(type) ?? [])
        {
            if (Holds(payload, seen, depth + 1))
            {
                return true;
            }
        }

        return Holds(AdtDef.Base(type), seen, depth + 1);
    }

    // MayNeedDrop's visitor: any reported part.
    private struct AnyPart : IMirDestruction
    {
        public readonly bool Visit(BoundType type, int depth) => true;

        public readonly bool Deep() => true;
    }

    // Observes' visitor: a reported part that may observe, and past the walk's depth bound one it does not follow. A string's or a
    // sequence's own release observes nothing; its elements are parts of their own.
    private struct Observer(TyCtxt types, BindingSymbol? definition, HashSet<BoundType> seen) : IMirDestruction
    {
        internal bool Beyond { get; private set; }

        public bool Visit(BoundType type, int depth)
        {
            if (type.IsAbstract)
            {
                return !types.Copy(definition, type);
            }

            if (type.Kind == BoundTypeKind.Function || AdtDef.Destructor(type) is not null)
            {
                return true;
            }

            var payload = type.Kind == BoundTypeKind.Semantics ? type.Components[0] : null;
            return payload is not null && (!types.Sealed(definition, payload) || (seen.Add(payload) && Destruction(payload, ref this, depth + 1)));
        }

        public bool Deep()
        {
            this.Beyond = true;
            return true;
        }
    }
}
