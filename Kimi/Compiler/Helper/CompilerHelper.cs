// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Provides helper functions for compiler.
/// </summary>
public static class CompilerHelper
{
    public const int AccessibilityModifierMask = 15;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ModifierKind ExtractAccessibilityModifiers(this ModifierKind kind)
    {
        return (ModifierKind)((byte)kind & AccessibilityModifierMask);
    }

    /// <summary>Returns whether the kind represents value semantics.</summary>
    /// <param name="kind">The kind to classify.</param>
    /// <returns><see langword="true"/> for value semantics; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsValue(this SemanticsKind kind)
        => kind <= SemanticsKind.Stack;

    /// <summary>Returns whether the kind represents reference semantics.</summary>
    /// <param name="kind">The kind to classify.</param>
    /// <returns><see langword="true"/> for reference semantics; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsReference(this SemanticsKind kind)
        => kind >= SemanticsKind.OwnerRef && kind <= SemanticsKind.Unsafe;

    /// <summary>Parses a built-in semantics name without allocating.</summary>
    /// <param name="text">The semantics name.</param>
    /// <param name="kind">The parsed kind, or <see cref="SemanticsKind.Parameter"/> when the name is not built in.</param>
    /// <returns><see langword="true"/> when <paramref name="text"/> is built in; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> text, out SemanticsKind kind)
    {
        kind = text.Length switch
        {
            2 when text.SequenceEqual("rc") => SemanticsKind.Rc,
            3 when text.SequenceEqual("arc") => SemanticsKind.Arc,
            5 when text.SequenceEqual("owner") => SemanticsKind.Owner,
            5 when text.SequenceEqual("stack") => SemanticsKind.Stack,
            6 when text.SequenceEqual("borrow") => SemanticsKind.Borrow,
            6 when text.SequenceEqual("unsafe") => SemanticsKind.Unsafe,
            8 when text.SequenceEqual("ownerref") => SemanticsKind.OwnerRef,
            9 when text.SequenceEqual("borrowref") => SemanticsKind.BorrowRef,
            _ => SemanticsKind.Parameter,
        };

        return kind != SemanticsKind.Parameter;
    }

    /// <summary>Returns the canonical name of a built-in semantics kind.</summary>
    /// <param name="kind">The semantics kind.</param>
    /// <returns>The canonical name, or an empty string for <see cref="SemanticsKind.Parameter"/>.</returns>
    public static string ToText(this SemanticsKind kind)
        => kind switch
        {
            SemanticsKind.Owner => "owner",
            SemanticsKind.Borrow => "borrow",
            SemanticsKind.Stack => "stack",
            SemanticsKind.OwnerRef => "ownerref",
            SemanticsKind.BorrowRef => "borrowref",
            SemanticsKind.Rc => "rc",
            SemanticsKind.Arc => "arc",
            SemanticsKind.Unsafe => "unsafe",
            _ => string.Empty,
        };
}
