// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Parsing;

public enum SemanticsKind : byte
{
    Owner,
    Borrow,
    Stack,
    OwnerRef,
    BorrowRef,
    Rc,
    Arc,
    Unsafe,
    Parameter = byte.MaxValue,
}

public static class SemanticsKindHelper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsValue(this SemanticsKind kind)
        => kind <= SemanticsKind.Stack;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsReference(this SemanticsKind kind)
        => kind >= SemanticsKind.OwnerRef && kind <= SemanticsKind.Unsafe;

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
