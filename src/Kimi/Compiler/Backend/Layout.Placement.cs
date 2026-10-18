// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Kimi.Compiler.Backend;

// Placement (IMPL 21.1.1, 21.1.2): a struct, Tuple, Closure or Function Item places its parts, a fixed array repeats its element and
// an enum places an i32 tag before the union of its Case payloads. Each representation has one record, shared by every Type and
// thread that places it.
internal static partial class Layout
{
    // IMPL 21.3.2: the generation limit of a size or offset, which the LLVM text writes as a signed 32-bit number.
    private const long MaximumSize = int.MaxValue;

    // The records by key: the storage text, then size, alignment and Field offsets. A thread that places a new representation adds it.
    private static readonly ConcurrentDictionary<string, ValueLowering> Records = new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, ValueLowering>.AlternateLookup<ReadOnlySpan<char>> RecordsByKey =
        Records.GetAlternateLookup<ReadOnlySpan<char>>();

    [ThreadStatic]
    private static Scratch? scratch;

    private static Result Limit => default;

    // A struct, its complete base first, or a Tuple, Closure or Function Item. A C-layout struct with a zero-sized part has no layout
    // (SPEC 6.7.1; a stored Never counts as zero-sized, IMPL 21.1.3); a part without a layout gives the part's result.
    private static Result Place(BoundType type)
    {
        var structure = AdtDef.IsStruct(type);
        var cLayout = structure && type.Symbol!.Adt is { IsCLayout: true };
        ValueLowering? inherited = null;
        if (structure && AdtDef.Base(type) is { } parent)
        {
            var layout = Of(parent);
            if (layout.Value is null)
            {
                return layout;
            }

            inherited = layout.Value;
        }

        var count = type.Kind switch
        {
            BoundTypeKind.Tuple or BoundTypeKind.Closure => type.Components.Count,
            BoundTypeKind.FunctionItem => 0, // Its Components are its bound generic arguments, not stored Fields.
            _ => AdtDef.Count(type),
        };
        var local = scratch ??= new();
        var start = local.Parts.Count;
        try
        {
            for (var i = 0; i < count; i++)
            {
                var part = Of(structure ? AdtDef.FieldType(type, i)! : type.Components[i]);
                if (part.Value is not { } value)
                {
                    return part;
                }

                if (cLayout && value.Layout.Size == 0)
                {
                    return new(null, type);
                }

                local.Parts.Add(value);
            }

            return Arrange(local, CollectionsMarshal.AsSpan(local.Parts)[start..], inherited, cLayout);
        }
        finally
        {
            local.Parts.RemoveRange(start, local.Parts.Count - start);
        }
    }

    // Kimigayo mode groups the parts by descending alignment, each group in written order; C mode (Windows x64 /Zp16: natural
    // alignment, at most 16) places them in written order (IMPL 21.1.2). Either follows the complete base, which keeps its tail
    // padding (X1).
    private static Result Arrange(Scratch local, ReadOnlySpan<ValueLowering> parts, ValueLowering? inherited, bool cLayout)
    {
        var alignment = inherited?.Layout.Alignment ?? 1;
        foreach (var part in parts)
        {
            alignment = Math.Max(alignment, part.Layout.Alignment);
        }

        long size = inherited?.Layout.Size ?? 0;
        var offsets = local.Offsets(parts.Length);
        for (var group = alignment; group != 0; group >>= 1)
        {
            for (var i = 0; i < parts.Length; i++)
            {
                // C mode places every part in the first round.
                var layout = parts[i].Layout;
                if (cLayout ? group != alignment : layout.Alignment != group)
                {
                    continue;
                }

                size = Align(size, layout.Alignment);
                if (size > MaximumSize)
                {
                    return Limit;
                }

                offsets[i] = (int)size;
                size += layout.Size;
            }
        }

        size = Align(size, alignment);
        if (size > MaximumSize)
        {
            return Limit;
        }

        local.Begin();
        if (cLayout)
        {
            // Ordinary LLVM structs derive the same ABI padding; never packed structs.
            for (var i = 0; i < parts.Length; i++)
            {
                local.Write(i == 0 ? "{ " : ", ");
                local.Write(parts[i].Layout.StorageType);
            }

            local.Write(" }");
        }
        else
        {
            local.WriteBytes(alignment, size);
        }

        return new(local.Record((int)size, alignment, offsets), null);
    }

    // A fixed array: its elements at consecutive strides, with no Field offsets.
    private static Result PlaceArray(BoundType type)
    {
        if (type.Length < 0 || type.Components.Count != 1)
        {
            throw Outside(type);
        }

        var element = Of(type.Components[0]);
        if (element.Value is not { } value)
        {
            return element;
        }

        // The old pool bounds the count by 2^31 - 1 before laying out (P7c brings the count bound here).
        var stride = value.Layout.Stride;
        if (stride != 0 && type.Length > MaximumSize / stride)
        {
            return Limit;
        }

        var local = scratch ??= new();
        local.Begin();
        local.WriteBytes(value.Layout.Alignment, type.Length * stride);
        return new(local.Record((int)(type.Length * stride), value.Layout.Alignment, []), null);
    }

    // An enum: an i32 tag at offset 0, then the union of its Case payload Tuples, aligned for the most aligned payload (IMPL 21.1.3:
    // implementation-defined). The nonnull Option is laid out so too until PLAN U8 (STATUS).
    private static Result PlaceEnum(BoundType type)
    {
        var alignment = 1;
        long payload = 0;
        foreach (var item in AdtDef.CaseTypes(type) ?? throw Outside(type))
        {
            var part = Of(item);
            if (part.Value is not { } value)
            {
                return part;
            }

            alignment = Math.Max(alignment, value.Layout.Alignment);
            payload = Math.Max(payload, value.Layout.Size);
        }

        var offset = (int)Align(4, alignment);
        var size = Align(offset + Align(payload, alignment), Math.Max(4, alignment));
        if (size > MaximumSize)
        {
            return Limit;
        }

        var local = scratch ??= new();
        local.Begin();
        local.Write("{ i32, ");
        local.WriteBytes(alignment, Align(payload, alignment));
        local.Write(" }");
        return new(local.Record((int)size, Math.Max(4, alignment), [0, offset]), null);
    }

    private static long Align(long size, int alignment) => (size + alignment - 1) & -(long)alignment;

    // The current thread's placement buffers: the parts of the placements in progress, innermost last, and one record's offsets
    // and key, written after its parts are laid out.
    private sealed class Scratch
    {
        private int[] offsets = new int[16];
        private char[] key = new char[128];
        private int length;

        internal List<ValueLowering> Parts { get; } = [];

        internal Span<int> Offsets(int count)
        {
            if (this.offsets.Length < count)
            {
                this.offsets = new int[Math.Max(count, this.offsets.Length * 2)];
            }

            return this.offsets.AsSpan(0, count);
        }

        internal void Begin() => this.length = 0;

        internal void Write(ReadOnlySpan<char> text)
        {
            this.Reserve(text.Length);
            text.CopyTo(this.key.AsSpan(this.length));
            this.length += text.Length;
        }

        internal void Write(long number)
        {
            this.Reserve(20);
            number.TryFormat(this.key.AsSpan(this.length), out var written, default, CultureInfo.InvariantCulture);
            this.length += written;
        }

        // A zero-length integer carrier keeps the natural alignment without making padding a typed source-language value.
        internal void WriteBytes(int alignment, long size)
        {
            this.Write("{ [0 x i");
            this.Write(alignment * 8);
            this.Write("], [");
            this.Write(size);
            this.Write(" x i8] }");
        }

        // Completes the key written as the storage text and returns the representation's one record, created at its first use.
        internal ValueLowering Record(int size, int alignment, ReadOnlySpan<int> fieldOffsets)
        {
            var storage = this.length;
            this.Write("|");
            this.Write(size);
            this.Write("|");
            this.Write(alignment);
            foreach (var offset in fieldOffsets)
            {
                this.Write(",");
                this.Write(offset);
            }

            var key = this.key.AsSpan(0, this.length);
            if (RecordsByKey.TryGetValue(key, out var record))
            {
                return record;
            }

            var text = key.ToString();
            var storageType = text[..storage];
            return Records.GetOrAdd(text, new ValueLowering(new(storageType, size, alignment, size, fieldOffsets.ToArray()), storageType, "ptr"));
        }

        private void Reserve(int count)
        {
            if (this.length + count > this.key.Length)
            {
                Array.Resize(ref this.key, Math.Max(this.length + count, this.key.Length * 2));
            }
        }
    }
}
