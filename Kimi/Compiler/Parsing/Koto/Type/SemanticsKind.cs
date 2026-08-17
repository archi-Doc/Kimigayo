// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Defines the ownership and reference semantics of a type.
/// </summary>
/// <remarks>
/// Value and reference kinds occupy contiguous ranges for fast classification.
/// </remarks>
public enum SemanticsKind : byte
{
    /// <summary>
    /// An owned value. This is the default form used by <c>T</c>.
    /// </summary>
    Owner,

    /// <summary>
    /// A borrowed value expressed as <c>borrow/T</c>.
    /// </summary>
    Borrow,

    /// <summary>
    /// A stack-bound value expressed as <c>stack/T</c>.
    /// </summary>
    Stack,

    /// <summary>
    /// An owning reference expressed as <c>ownerref/T</c>.
    /// </summary>
    OwnerRef,

    /// <summary>
    /// A borrowed reference expressed as <c>/T</c> or <c>borrowref/T</c>.
    /// </summary>
    BorrowRef,

    /// <summary>
    /// A reference-counted reference expressed as <c>rc/T</c>.
    /// </summary>
    Rc,

    /// <summary>
    /// An atomically reference-counted reference expressed as <c>arc/T</c>.
    /// </summary>
    Arc,

    /// <summary>
    /// An unsafe reference expressed as <c>unsafe/T</c>.
    /// </summary>
    Unsafe,

    /// <summary>
    /// A generic semantics parameter expressed as <c>s/T</c>.
    /// </summary>
    Parameter = byte.MaxValue,
}

/// <summary>
/// Provides allocation-free operations for <see cref="SemanticsKind"/>.
/// </summary>
public static class SemanticsKindHelper
{
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
