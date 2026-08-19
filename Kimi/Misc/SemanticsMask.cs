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
