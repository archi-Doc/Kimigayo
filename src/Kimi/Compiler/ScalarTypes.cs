// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>The scalar value-plan subset shared by ownership and emission, independent of storage layout.</summary>
internal static class ScalarTypes
{
    // The bounded local-loop/checking evaluator accepts scalar/Unit values and references to those values.
    // Default expressions use ordinary body analysis and do not share this representation boundary.
    internal static bool SupportsFlowValue(BoundType? type) => Supports(type) || ReferenceEquals(type, BoundType.Unit) ||
        (type is { Kind: BoundTypeKind.Semantics, Semantics: Parsing.SemanticsKind.Ref or Parsing.SemanticsKind.Uniq, Components.Count: 1 } && SupportsFlowValue(type.Components[0]));

    internal static bool Supports(BoundType? type) => ReferenceEquals(type, BoundType.Boolean) || ReferenceEquals(type, BoundType.Char) || FloatingTypes.Supports(type) || Width(type) != 0;

    // Character storage uses i32, but does not grant integer arithmetic or conversions.
    internal static bool IsCharacterValue(Int128 value) => value >= 0 && value <= 0x10FFFF && !(value >= 0xD800 && value <= 0xDFFF);

    // A wrapping integer Type answers every width and signedness query by its integer argument (SPEC 3.1.1.1).
    internal static int Width(BoundType? type, int pointerWidth = 64) => type?.Kind != BoundTypeKind.Primitive ? 0 : type.Underlying.Name switch
    {
        "i8" or "u8" => 8,
        "i16" or "u16" => 16,
        "i32" or "u32" => 32,
        "i64" or "u64" => 64,
        "i128" or "u128" => 128,
        "isize" or "usize" => pointerWidth,
        _ => 0,
    };

    internal static bool Signed(BoundType type) => type.Underlying.Name[0] == 'i';

    // Canonical signed extension of the N-bit payload, also for unsigned language Types.
    internal static Int128 Normalize(Int128 bits, int width) => width <= 64
        ? (unchecked((long)bits) << (64 - width)) >> (64 - width) : bits;

    internal static bool TryLiteral(BoundType? type, UInt128 magnitude, bool negative, int pointerWidth, out Int128 bits)
    {
        bits = 0;
        var width = Width(type, pointerWidth);
        if (width == 0)
        {
            return false;
        }

        var signed = Signed(type!);
        var limit = signed ? ((UInt128)1 << (width - 1)) - (negative ? 0U : 1U) : width == 128 ? UInt128.MaxValue : ((UInt128)1 << width) - 1;
        if (magnitude > limit || (negative && !signed && magnitude != 0))
        {
            return false;
        }

        bits = Normalize(unchecked((Int128)(negative ? (UInt128)0 - magnitude : magnitude)), width);
        return true;
    }
}
