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
            3 when text.SequenceEqual(Constants.RawKeyword) => SemanticsKind.Raw,
            4 when text.SequenceEqual(Constants.UniqKeyword) => SemanticsKind.Uniq,
            5 when text.SequenceEqual(Constants.OwnerKeyword) => SemanticsKind.Owner,
            6 when text.SequenceEqual(Constants.ObjRefKeyword) => SemanticsKind.ObjRef,
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
            SemanticsKind.Raw => Constants.RawKeyword,
            _ => string.Empty,
        };
}
