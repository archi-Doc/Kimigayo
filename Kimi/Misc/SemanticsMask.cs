// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a set of concrete type semantics used by constraints.
/// </summary>
[Flags]
public enum SemanticsMask : byte
{
    None = 0,

    Owner = 1 << 0,
    Borrow = 1 << 1,
    Stack = 1 << 2,
    OwnerRef = 1 << 3,
    BorrowRef = 1 << 4,
    Rc = 1 << 5,
    Arc = 1 << 6,
    Unsafe = 1 << 7,

    Value = Owner | Borrow | Stack,

    Reference = OwnerRef | BorrowRef | Rc | Arc | Unsafe,

    Owning = Owner | OwnerRef | Rc | Arc | Unsafe,

    All = Value | Reference,
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
            6 when text.SequenceEqual(Constants.OwningKeyword) => SemanticsMask.Owning,
            9 when text.SequenceEqual(Constants.ReferenceKeyword) => SemanticsMask.Reference,
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
            SemanticsMask.Borrow => Constants.BorrowKeyword,
            SemanticsMask.Stack => Constants.StackKeyword,
            SemanticsMask.OwnerRef => Constants.OwnerRefKeyword,
            SemanticsMask.BorrowRef => Constants.BorrowRefKeyword,
            SemanticsMask.Rc => Constants.RcKeyword,
            SemanticsMask.Arc => Constants.ArcKeyword,
            SemanticsMask.Unsafe => Constants.UnsafeKeyword,
            SemanticsMask.Value => Constants.ValueKeyword,
            SemanticsMask.Reference => Constants.ReferenceKeyword,
            SemanticsMask.Owning => Constants.OwningKeyword,
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
            SemanticsKind.Borrow => SemanticsMask.Borrow,
            SemanticsKind.Stack => SemanticsMask.Stack,
            SemanticsKind.OwnerRef => SemanticsMask.OwnerRef,
            SemanticsKind.BorrowRef => SemanticsMask.BorrowRef,
            SemanticsKind.Rc => SemanticsMask.Rc,
            SemanticsKind.Arc => SemanticsMask.Arc,
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
