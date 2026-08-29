// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Provides compiler-related helper methods.
/// </summary>
public static class CompilerHelper
{
    /// <summary>
    /// The bit mask for accessibility modifiers.
    /// </summary>
    public const int AccessibilityModifierMask = 15;

    /// <summary>
    /// Extracts the accessibility modifiers from a modifier set.
    /// </summary>
    /// <param name="kind">The modifier set.</param>
    /// <returns>The accessibility modifiers.</returns>
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
        => kind == SemanticsKind.Owner;

    /// <summary>Returns whether the kind represents a value borrow.</summary>
    /// <param name="kind">The kind to classify.</param>
    /// <returns><see langword="true"/> for value-borrow semantics; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsValueBorrow(this SemanticsKind kind)
        => kind is >= SemanticsKind.Ref and <= SemanticsKind.Uniq;

    /// <summary>Returns whether the kind represents an owning object.</summary>
    /// <param name="kind">The kind to classify.</param>
    /// <returns><see langword="true"/> for object semantics; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsObject(this SemanticsKind kind)
        => kind is >= SemanticsKind.Obj and <= SemanticsKind.Arc;

    /// <summary>Returns whether the kind represents an object borrow.</summary>
    /// <param name="kind">The kind to classify.</param>
    /// <returns><see langword="true"/> for object-borrow semantics; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsObjectBorrow(this SemanticsKind kind)
        => kind is >= SemanticsKind.ObjRef and <= SemanticsKind.ObjUniq;

    /// <summary>Returns whether the kind represents reference semantics.</summary>
    /// <param name="kind">The kind to classify.</param>
    /// <returns><see langword="true"/> for reference semantics; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsReference(this SemanticsKind kind)
        => kind is >= SemanticsKind.Ref and <= SemanticsKind.Unsafe;

    /// <summary>Parses a built-in semantics name without allocating.</summary>
    /// <param name="text">The semantics name.</param>
    /// <param name="kind">The parsed kind, or <see cref="SemanticsKind.Parameter"/> when the name is not built in.</param>
    /// <returns><see langword="true"/> when <paramref name="text"/> is built in; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> text, out SemanticsKind kind)
    {
        kind = text.Length switch
        {
            2 when text.SequenceEqual(Constants.RcKeyword) => SemanticsKind.Rc,
            3 when text.SequenceEqual(Constants.RefKeyword) => SemanticsKind.Ref,
            3 when text.SequenceEqual(Constants.ObjKeyword) => SemanticsKind.Obj,
            3 when text.SequenceEqual(Constants.ArcKeyword) => SemanticsKind.Arc,
            4 when text.SequenceEqual(Constants.UniqKeyword) => SemanticsKind.Uniq,
            5 when text.SequenceEqual(Constants.OwnerKeyword) => SemanticsKind.Owner,
            6 when text.SequenceEqual(Constants.ObjRefKeyword) => SemanticsKind.ObjRef,
            6 when text.SequenceEqual(Constants.UnsafeKeyword) => SemanticsKind.Unsafe,
            7 when text.SequenceEqual(Constants.ObjUniqKeyword) => SemanticsKind.ObjUniq,
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
            SemanticsKind.Owner => Constants.OwnerKeyword,
            SemanticsKind.Ref => Constants.RefKeyword,
            SemanticsKind.Uniq => Constants.UniqKeyword,
            SemanticsKind.Obj => Constants.ObjKeyword,
            SemanticsKind.Rc => Constants.RcKeyword,
            SemanticsKind.Arc => Constants.ArcKeyword,
            SemanticsKind.ObjRef => Constants.ObjRefKeyword,
            SemanticsKind.ObjUniq => Constants.ObjUniqKeyword,
            SemanticsKind.Unsafe => Constants.UnsafeKeyword,
            _ => string.Empty,
        };
}
