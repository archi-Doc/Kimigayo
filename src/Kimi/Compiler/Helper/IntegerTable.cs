// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Reads the compiler's constant integer tables, stored as little-endian bytes (DECISIONS R5-M179): a constant byte span is static
// data in every configuration, while a constant span of wider elements is built by RuntimeHelpers.CreateSpan, which allocates under
// Debug's JIT minopts. A table's owner publishes its bytes (UnicodeIdentifierData, UnicodeNameData, MarkdownUnicodeData), written by
// its generator through Add-IntegerTable (IntegerTable.ps1 beside this file, the one writer of the form), and its one reader wraps them
// in IntegerTable of their element type. Input: the bytes, a whole number of elements. Output: the element count and the element at an
// index, little-endian on any host (a plain load on a little-endian one, misaligned loads being supported on every .NET platform); an
// index out of range throws. No allocation; each read is one range check and one load, as an integer span's.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kimi.Compiler.Helper;

/// <summary>A constant table of integers stored as little-endian bytes.</summary>
/// <typeparam name="T">The element type, whose size is the element width.</typeparam>
/// <param name="bytes">The table's bytes, a whole number of elements.</param>
internal readonly ref struct IntegerTable<T>(ReadOnlySpan<byte> bytes)
    where T : unmanaged, IBinaryInteger<T>
{
    private readonly ReadOnlySpan<byte> bytes = bytes;

    /// <summary>Gets the number of elements.</summary>
    internal int Length => this.bytes.Length / Unsafe.SizeOf<T>();

    /// <summary>Gets the element at an index.</summary>
    /// <param name="index">The element's index.</param>
    internal T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var value = MemoryMarshal.Cast<byte, T>(this.bytes)[index];
            if (!BitConverter.IsLittleEndian)
            {
                MemoryMarshal.AsBytes(new Span<T>(ref value)).Reverse();
            }

            return value;
        }
    }
}
