// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the Type facts needed by MIR construction and analyses.
// Input: published Types and their stored shapes. Output: drop, raw, exclusivity, agreement, borrow-kind and pointer-cast facts.
// Inline storage is traversed under each fact's documented bounds and Type rules.
// Must not inspect source syntax or substitute new Binding decisions.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

/// <summary>The Type facts MIR decides by, each one walk of a Type's inline storage: its Tuple, fixed-array and Closure components,
/// a struct's Fields and base and an enum's Case payloads (plan 5.5).</summary>
internal static class MirTypes
{
    /// <summary>Gets whether a value of a Type may own what its destruction releases (SPEC 16.3), the one rule by which the builder drops
    /// an old value: an owning handle, a managed sequence or string, a Function value's environment, a destructor, or a Type its
    /// instance decides, anywhere in its inline storage; references, raw pointers, Slices, scalars and an InlineStorage, whose owner
    /// destroys its contents (<see cref="AdtDef.IsInlineStorage"/>), own nothing.</summary>
    /// <param name="type">The Type.</param>
    /// <param name="depth">The walk's depth; past 32 the answer is yes.</param>
    /// <returns>Whether drop elaboration has to consider the value.</returns>
    internal static bool MayNeedDrop(BoundType? type, int depth = 0) => type is not null && (depth > 32 || type.IsAbstract || type.Kind switch
    {
        BoundTypeKind.Array or BoundTypeKind.Dictionary or BoundTypeKind.Function => true,
        BoundTypeKind.Semantics => type.Semantics is SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc,
        BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Closure => Any(type.Components, depth, raw: false),
        _ => ReferenceEquals(type, BoundType.String) || AdtDef.Destructor(type) is not null ||
            (!AdtDef.IsInlineStorage(type) && Stored(type, depth, raw: false)),
    });

    /// <summary>Gets whether a value of a Type is a reference whose Loans an EndBorrow ends: a ref, uniq, objref or objuniq, or a pair layer.</summary>
    /// <param name="type">The Type.</param>
    /// <returns>Whether the value is a reference.</returns>
    internal static bool IsReference(BoundType? type)
        => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq } ||
            (TyCtxt.Layer(type, out _, out var binder) is var _ && binder is not null);

    /// <summary>Gets whether a Type is an exclusive reference, <c>uniq</c> or <c>objuniq</c>: a borrow of its mode is exclusive, and a bare
    /// acquisition of a Place storing it Reborrows it in its own Semantics where another Type's Copies (SPEC 3.5, 8.9).</summary>
    /// <param name="type">The Type.</param>
    /// <returns>Whether it is an exclusive reference.</returns>
    internal static bool IsExclusive(BoundType? type) => type?.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq;

    /// <summary>Gets the kind of the borrow that forms a reference Type (SPEC 13.5.5.2, 5.4), the mode of every Ref: an address for a raw
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
    /// integer's, an environment effect (SPEC 8.4.10.2).</summary>
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

        if (left.Length != right.Length || !ReferenceEquals(left.LengthExpression, right.LengthExpression) || left.ResultMode != right.ResultMode)
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
    /// immutable static obtains authority from the environment (SPEC 8.4.10.2); a referent reached through the value counts too.</summary>
    /// <param name="type">The Type.</param>
    /// <param name="depth">The walk's depth; past 32 the answer is yes.</param>
    /// <returns>Whether the value holds raw authority.</returns>
    internal static bool HoldsRaw(BoundType? type, int depth = 0)
        => type is not null && (depth > 32 || type.Semantics is SemanticsKind.Raw or SemanticsKind.Rc || Any(type.Components, depth, raw: true) ||
            Stored(type, depth, raw: true));

    private static bool Any(IReadOnlyList<BoundType> types, int depth, bool raw)
    {
        for (var i = 0; i < types.Count; i++)
        {
            if (raw ? HoldsRaw(types[i], depth + 1) : MayNeedDrop(types[i], depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    // A struct's Fields and base, an enum's payload Tuples.
    private static bool Stored(BoundType type, int depth, bool raw)
    {
        for (var i = 0; AdtDef.IsStruct(type) && i < AdtDef.Count(type); i++)
        {
            if (raw ? HoldsRaw(AdtDef.FieldType(type, i), depth + 1) : MayNeedDrop(AdtDef.FieldType(type, i), depth + 1))
            {
                return true;
            }
        }

        return (AdtDef.Base(type) is { } parent && (raw ? HoldsRaw(parent, depth + 1) : MayNeedDrop(parent, depth + 1))) ||
            (AdtDef.CaseTypes(type) is { } cases && Any(cases, depth, raw));
    }
}
