// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a set of concrete type semantics used by constraints.
/// </summary>
[Flags]
public enum SemanticsMask : ushort
{
    None = 0,

    Owner = 1 << 0,
    Ref = 1 << 1,
    Uniq = 1 << 2,
    Obj = 1 << 3,
    Rc = 1 << 4,
    Arc = 1 << 5,
    ObjRef = 1 << 6,
    ObjUniq = 1 << 7,
    Unsafe = 1 << 8,

    Value = Owner,

    ValueBorrow = Ref | Uniq,

    Object = Obj | Rc | Arc,

    ObjectBorrow = ObjRef | ObjUniq,

    Borrow = ValueBorrow | ObjectBorrow,

    Owning = Value | Object,

    Reference = ValueBorrow | Object | ObjectBorrow | Unsafe,

    Safe = Value | ValueBorrow | Object | ObjectBorrow,

    All = Safe | Unsafe,
}

/// <summary>
/// Provides conversion and constraint matching helpers for <see cref="SemanticsMask"/>.
/// </summary>
public static class SemanticsMaskHelper
{
    /// <summary>
    /// Parses a name that can be used in a semantics constraint.
    /// </summary>
    /// <param name="text">The semantics constraint name.</param>
    /// <param name="mask">The parsed mask.</param>
    /// <returns><see langword="true"/> when <paramref name="text"/> is a supported name.</returns>
    public static bool TryParse(ReadOnlySpan<char> text, out SemanticsMask mask)
    {
        if (CompilerHelper.TryParse(text, out var kind))
        {
            mask = kind.ToMask();
            return true;
        }

        mask = text.Length switch
        {
            5 when text.SequenceEqual(Constants.ValueKeyword) => SemanticsMask.Value,
            6 when text.SequenceEqual(Constants.BorrowKeyword) => SemanticsMask.Borrow,
            6 when text.SequenceEqual(Constants.ObjectKeyword) => SemanticsMask.Object,
            6 when text.SequenceEqual(Constants.OwningKeyword) => SemanticsMask.Owning,
            9 when text.SequenceEqual(Constants.ReferenceKeyword) => SemanticsMask.Reference,
            11 when text.SequenceEqual(Constants.ValueBorrowKeyword) => SemanticsMask.ValueBorrow,
            12 when text.SequenceEqual(Constants.ObjectBorrowKeyword) => SemanticsMask.ObjectBorrow,
            _ => SemanticsMask.None,
        };

        return mask != SemanticsMask.None;
    }

    /// <summary>
    /// Returns the canonical name of a semantics constraint mask.
    /// </summary>
    /// <param name="mask">The mask to format.</param>
    /// <returns>The canonical name, or an empty string when the mask is not a named constraint.</returns>
    public static string ToText(this SemanticsMask mask)
        => mask switch
        {
            SemanticsMask.Owner => Constants.OwnerKeyword,
            SemanticsMask.Ref => Constants.RefKeyword,
            SemanticsMask.Uniq => Constants.UniqKeyword,
            SemanticsMask.Obj => Constants.ObjKeyword,
            SemanticsMask.Rc => Constants.RcKeyword,
            SemanticsMask.Arc => Constants.ArcKeyword,
            SemanticsMask.ObjRef => Constants.ObjRefKeyword,
            SemanticsMask.ObjUniq => Constants.ObjUniqKeyword,
            SemanticsMask.Unsafe => Constants.UnsafeKeyword,
            SemanticsMask.ValueBorrow => Constants.ValueBorrowKeyword,
            SemanticsMask.Object => Constants.ObjectKeyword,
            SemanticsMask.ObjectBorrow => Constants.ObjectBorrowKeyword,
            SemanticsMask.Borrow => Constants.BorrowKeyword,
            SemanticsMask.Owning => Constants.OwningKeyword,
            SemanticsMask.Reference => Constants.ReferenceKeyword,
            _ => string.Empty,
        };

    /// <summary>
    /// Converts a concrete semantics kind to its corresponding single-bit mask.
    /// </summary>
    /// <param name="kind">The semantics kind.</param>
    /// <returns>
    /// The corresponding mask, or <see cref="SemanticsMask.None"/> when
    /// <paramref name="kind"/> is not a concrete semantics kind.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SemanticsMask ToMask(this SemanticsKind kind)
        => kind switch
        {
            SemanticsKind.Owner => SemanticsMask.Owner,
            SemanticsKind.Ref => SemanticsMask.Ref,
            SemanticsKind.Uniq => SemanticsMask.Uniq,
            SemanticsKind.Obj => SemanticsMask.Obj,
            SemanticsKind.Rc => SemanticsMask.Rc,
            SemanticsKind.Arc => SemanticsMask.Arc,
            SemanticsKind.ObjRef => SemanticsMask.ObjRef,
            SemanticsKind.ObjUniq => SemanticsMask.ObjUniq,
            SemanticsKind.Unsafe => SemanticsMask.Unsafe,
            _ => SemanticsMask.None,
        };

    /// <summary>
    /// Determines whether a mask contains a concrete semantics kind.
    /// </summary>
    /// <param name="mask">The semantics set.</param>
    /// <param name="kind">The semantics kind to find.</param>
    /// <returns><see langword="true"/> when the mask contains the kind.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Contains(this SemanticsMask mask, SemanticsKind kind)
    {
        var kindMask = kind.ToMask();
        return kindMask != SemanticsMask.None && (mask & kindMask) != 0;
    }

    /// <summary>
    /// Determines whether a concrete semantics kind satisfies a mask constraint.
    /// </summary>
    /// <param name="mask">The semantics constraint.</param>
    /// <param name="kind">The concrete semantics kind.</param>
    /// <param name="isNegated">Whether the entire mask constraint is negated.</param>
    /// <returns><see langword="true"/> when the constraint is satisfied.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSatisfiedBy(this SemanticsMask mask, SemanticsKind kind, bool isNegated = false)
    {
        var kindMask = kind.ToMask();
        if (kindMask == SemanticsMask.None)
        {
            return false;
        }

        var matched = (mask & kindMask) != 0;
        return isNegated ? !matched : matched;
    }
}
