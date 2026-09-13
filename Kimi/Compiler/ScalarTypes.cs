// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>The scalar value-plan subset shared by ownership and emission, independent of storage layout.</summary>
internal static class ScalarTypes
{
    internal static bool Supports(BoundType? type) => ReferenceEquals(type, BoundType.Boolean) || Width(type) != 0;

    internal static int Width(BoundType? type, int pointerWidth = 64) => type?.Kind != BoundTypeKind.Primitive ? 0 : type.Name switch
    {
        "i8" or "u8" => 8,
        "i16" or "u16" => 16,
        "i32" or "u32" => 32,
        "i64" or "u64" => 64,
        "isize" or "usize" => pointerWidth,
        _ => 0,
    };

    internal static bool Signed(BoundType type) => type.Name[0] == 'i';

    // Canonical signed extension of the N-bit payload, also for unsigned language Types.
    internal static long Normalize(long bits, int width) => (bits << (64 - width)) >> (64 - width);

    internal static bool TryLiteral(BoundType? type, UInt128 magnitude, bool negative, int pointerWidth, out long bits)
    {
        bits = 0;
        var width = Width(type, pointerWidth);
        if (width == 0 || width > 64)
        {
            return false;
        }

        var signed = Signed(type!);
        var limit = signed ? ((UInt128)1 << (width - 1)) - (negative ? 0U : 1U) : ((UInt128)1 << width) - 1;
        if (magnitude > limit || (negative && !signed && magnitude != 0))
        {
            return false;
        }

        bits = Normalize(negative ? unchecked((long)(0UL - (ulong)magnitude)) : unchecked((long)(ulong)magnitude), width);
        return true;
    }
}
