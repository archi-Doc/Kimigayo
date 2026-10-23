// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the Type facts needed by MIR construction and analyses.
// Input: published Types, their stored shapes and Binding's Copy and Sealed proofs. Output: the destruction walk and the drop, observation,
// Loan-holding with what a call may write past a value (its guard), raw, exclusivity, selection, agreement, borrow-kind and pointer-cast
// facts.
// Inline storage is traversed under each fact's documented bounds and Type rules; what destroying a value does is the one Destruction walk.
// Must not inspect source syntax or substitute new Binding decisions.
// Its concerns: the destruction walk and the facts over it (MirTypes.Destruction.cs), what a value may hold (MirTypes.Holding.cs), and
// here the reference, view, selection, borrow-kind, pointer-cast and agreement facts.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

/// <summary>The Type facts MIR decides by, each one walk of a Type's inline storage: its Tuple, fixed-array and Closure components,
/// a struct's Fields and base and an enum's Case payloads (plan 5.5).</summary>
internal static partial class MirTypes
{
    /// <summary>Gets whether a value of a Type is a reference whose Loans an EndBorrow ends: a ref, uniq, objref or objuniq, or a pair layer.</summary>
    /// <param name="type">The Type.</param>
    /// <returns>Whether the value is a reference.</returns>
    internal static bool IsReference(BoundType? type)
        => (type is { Kind: BoundTypeKind.Semantics } && IsBorrow(type.Semantics)) || (TyCtxt.Layer(type, out _, out var binder) is var _ && binder is not null);

    /// <summary>Gets whether a Semantics is a borrow's: ref, uniq, objref or objuniq (SPEC 15.6.5's borrow cases).</summary>
    /// <param name="semantics">The Semantics.</param>
    /// <returns>Whether it is a borrow's.</returns>
    internal static bool IsBorrow(SemanticsKind semantics) => semantics is SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq;

    /// <summary>Gets the borrow a view handle is (SPEC 4.8.1, 4.8.2): a Slice's elements are its handle's referent, shared, and a UniqSlice's,
    /// exclusive, so a read through the handle reaches its backing as a read through a reference does; null for another Type.</summary>
    /// <param name="type">The Type.</param>
    /// <returns>The view's mode, <c>ref</c> or <c>uniq</c>, or null.</returns>
    internal static SemanticsKind? View(BoundType? type) => type switch
    {
        { Kind: BoundTypeKind.Slice } => SemanticsKind.Ref,
        { Symbol.LibraryDeclaration: KimiDeclarationId.UniqSlice } => SemanticsKind.Uniq,
        _ => null,
    };

    /// <summary>Gets whether a Type is an exclusive reference, <c>uniq</c> or <c>objuniq</c>: a borrow of its mode is exclusive, and a bare
    /// acquisition of a Place storing it Reborrows it in its own Semantics where another Type's Copies (SPEC 3.5, 8.14).</summary>
    /// <param name="type">The Type.</param>
    /// <returns>Whether it is an exclusive reference.</returns>
    internal static bool IsExclusive(BoundType? type) => type?.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq;

    /// <summary>Gets whether a built-in selection reads its receiver's value, a Slice handle or a raw pointer, whose element is its
    /// referent (SPEC 4.7.4, 4.8.1, 5.3); else the receiver is storage, which the selection lends where it locates it (its guard, which
    /// is the receiver's check, DECISIONS R4-M56).</summary>
    /// <param name="type">The receiver's Type.</param>
    /// <returns>Whether the selection reads the receiver's value.</returns>
    internal static bool SelectsByValue(BoundType? type) => type is { Kind: BoundTypeKind.Slice } or { Semantics: SemanticsKind.Raw };

    /// <summary>Gets the kind of the borrow that forms a reference Type (SPEC 13.5.6, 5.4), the mode of every Ref: an address for a raw
    /// pointer, a shared borrow for a shared reference, else an exclusive one, two-phase for a reservation: reserved at the borrow and
    /// activated at the Call that takes it, a call's input on its preparation path (SPEC 15.6.7), or at the write through it, an update's
    /// destination (SPEC 13.7.2).</summary>
    /// <param name="reference">The reference Type.</param>
    /// <param name="input">Whether the borrow is a reservation.</param>
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

        if ((!left.CarriesAnyOrigin && !right.CarriesAnyOrigin) || left.Kind != right.Kind || left.Semantics != right.Semantics ||
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
}
