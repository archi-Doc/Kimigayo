// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Reads the compiler's constant integer tables, stored as little-endian bytes (DECISIONS R5-M179): a constant byte span is static
// data in every configuration, while a constant span of wider elements is built by RuntimeHelpers.CreateSpan, which allocates under
// Debug's JIT minopts. A table's owner publishes its bytes (UnicodeIdentifierData, UnicodeNameData, MarkdownUnicodeData) and its one
// reader wraps them in the reader of their width. Input: the bytes, a whole number of elements. Output: the element count and the
// element at an index, little-endian on any host (a plain load on a little-endian one, misaligned loads being supported on every
// .NET platform); an index out of range throws. No allocation; each read is one range check and one load, as an int span's.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kimi.Compiler.Helper;

/// <summary>A constant table of 32-bit integers stored as little-endian bytes.</summary>
/// <param name="bytes">The table's bytes, four per element.</param>
internal readonly ref struct Int32Table(ReadOnlySpan<byte> bytes)
{
    private readonly ReadOnlySpan<byte> bytes = bytes;

    /// <summary>Gets the number of elements.</summary>
    internal int Length => this.bytes.Length / sizeof(int);

    /// <summary>Gets the element at an index.</summary>
    /// <param name="index">The element's index.</param>
    internal int this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var value = MemoryMarshal.Cast<byte, int>(this.bytes)[index];
            return BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value);
        }
    }
}

/// <summary>A constant table of unsigned 64-bit integers stored as little-endian bytes.</summary>
/// <param name="bytes">The table's bytes, eight per element.</param>
internal readonly ref struct UInt64Table(ReadOnlySpan<byte> bytes)
{
    private readonly ReadOnlySpan<byte> bytes = bytes;

    /// <summary>Gets the number of elements.</summary>
    internal int Length => this.bytes.Length / sizeof(ulong);

    /// <summary>Gets the element at an index.</summary>
    /// <param name="index">The element's index.</param>
    internal ulong this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var value = MemoryMarshal.Cast<byte, ulong>(this.bytes)[index];
            return BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value);
        }
    }
}
