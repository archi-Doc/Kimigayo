// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.InteropServices;

namespace Kimi.Compiler.Backend;

// Placement (IMPL 21.1.1, 21.1.2): a struct, Tuple, Closure or Function Item places its parts, a fixed array repeats its element and
// an enum places an i32 tag before the union of its Case payloads. The walk (DECISIONS R5-M37) places a Type with a budget of the
// levels left in the 64-level window: a Type within its budget gets one complete entry, its own answer at every budget; a deeper one
// gets one exhausted entry per budget, holding its first CLayout in the window, so every answer is a function of the Type alone and
// placement stays linear over shared parts. Each representation has one record within a program version.
internal sealed partial class Layout
{
    // IMPL 21.3.2: the generation limit of a size, offset or element count, which the LLVM text writes as a signed 32-bit number.
    private const long MaximumSize = int.MaxValue;

    // The complete entries by placed Type.
    private readonly Dictionary<BoundType, Entry> memo = new(ReferenceEqualityComparer.Instance);

    // The exhausted entries by Type and budget: at most MaximumHeight per Type, only for Types past the bound.
    private readonly Dictionary<Window, Entry> exhausted = new();

    // The records by key: the storage text, then size, alignment and Field offsets.
    private readonly Dictionary<string, ValueLowering> records = new(StringComparer.Ordinal);

    private readonly Dictionary<string, ValueLowering>.AlternateLookup<ReadOnlySpan<char>> recordsByKey;

    private readonly Scratch scratch = new();

    internal Layout() => this.recordsByKey = this.records.GetAlternateLookup<ReadOnlySpan<char>>();

    private static Result Exceeded => new(Outcome.SizeLimit, null, null);

    /// <summary>Forgets the entries and records of the previous program version (Compilation.InvalidateSourceAnalysis); a pass
    /// without an edit keeps every Type's stored Types, IsCLayout and InlineCycle (R4-M51's premise), so warm passes keep them.</summary>
    internal void Clear()
    {
        this.memo.Clear();
        this.exhausted.Clear();
        this.records.Clear();
    }

    private static long Align(long size, int alignment) => (size + alignment - 1) & -(long)alignment;

    // A storage declaration, fixed array or enum Binding rejected is a leaf one level high.
    private static Entry Rejected(BoundType type) => new(new(Outcome.Rejected, null, type), 1);

    // A Type placed with `budget` levels left. A row is 0 high; a complete entry serves every budget not below its height; at budget
    // 0 a placed Type lies outside the window; an exhausted entry is `budget` + 1 high.
    private Entry Walk(BoundType type, int budget)
    {
        if (type.Kind is not (BoundTypeKind.Tuple or BoundTypeKind.Closure or BoundTypeKind.FunctionItem or BoundTypeKind.FixedArray) &&
            !AdtDef.IsStruct(type) && !AdtDef.IsEnum(type))
        {
            return new(new(Outcome.Ok, Row(type), null), 0);
        }

        if (this.memo.TryGetValue(type, out var entry) && entry.Height <= budget)
        {
            return entry;
        }

        if (budget == 0)
        {
            return new(new(Outcome.DepthLimit, null, null), 1);
        }

        var window = new Window(type, budget);
        if (this.exhausted.TryGetValue(window, out entry))
        {
            return entry;
        }

        entry = type.Kind switch
        {
            _ when type.Symbol?.Adt is { InlineCycle: true } or { CLayoutRejected: true } => Rejected(type),
            BoundTypeKind.FixedArray => this.PlaceArray(type, budget - 1),
            _ when AdtDef.IsEnum(type) => this.PlaceEnum(type, budget - 1),
            _ => this.Place(type, budget - 1),
        };
        if (entry.Height > budget)
        {
            this.exhausted.Add(window, entry);
        }
        else
        {
            this.memo.Add(type, entry);
        }

        return entry;
    }

    // A struct, its complete base first, or a Tuple, Closure or Function Item, its parts placed with `budget` levels left. A C-layout
    // struct with a zero-sized Field is CLayout (SPEC 6.7.1; a stored Never counts as zero-sized, IMPL 21.1.3); a Field whose stored
    // Type Binding did not resolve makes it Rejected.
    private Entry Place(BoundType type, int budget)
    {
        var structure = AdtDef.IsStruct(type);
        var cLayout = structure && type.Symbol!.Adt is { IsCLayout: true };
        var fold = new Fold(type);
        var inherited = structure && AdtDef.Base(type) is { } parent ? fold.Add(this.Walk(parent, budget), false) : null;
        var count = type.Kind switch
        {
            BoundTypeKind.Tuple or BoundTypeKind.Closure => type.Components.Count,
            BoundTypeKind.FunctionItem => 0, // Its Components are its bound generic arguments, not stored Fields.
            _ => AdtDef.Count(type),
        };
        var local = this.scratch;
        var start = local.Parts.Count;
        try
        {
            for (var i = 0; i < count; i++)
            {
                if ((structure ? AdtDef.FieldType(type, i) : type.Components[i]) is not { } part)
                {
                    fold.Reject();
                }
                else if (fold.Add(this.Walk(part, budget), cLayout) is { } value)
                {
                    local.Parts.Add(value);
                }
            }

            return fold.Close(budget, Outcome.Ok) ?? fold.Laid(this.Arrange(CollectionsMarshal.AsSpan(local.Parts)[start..], inherited, cLayout));
        }
        finally
        {
            local.Parts.RemoveRange(start, local.Parts.Count - start);
        }
    }

    // Kimigayo mode groups the parts by descending alignment, each group in written order; C mode (Windows x64 /Zp16: natural
    // alignment, at most 16) places them in written order (IMPL 21.1.2). Either follows the complete base, which keeps its tail
    // padding (X1).
    private Result Arrange(ReadOnlySpan<ValueLowering> parts, ValueLowering? inherited, bool cLayout)
    {
        var alignment = inherited?.Layout.Alignment ?? 1;
        foreach (var part in parts)
        {
            alignment = Math.Max(alignment, part.Layout.Alignment);
        }

        long size = inherited?.Layout.Size ?? 0;
        var local = this.scratch;
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
                    return Exceeded;
                }

                offsets[i] = (int)size;
                size += layout.Size;
            }
        }

        size = Align(size, alignment);
        if (size > MaximumSize)
        {
            return Exceeded;
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

        return new(Outcome.Ok, this.Record((int)size, alignment, offsets), null);
    }

    // A fixed array: its elements at consecutive strides, with no Field offsets; more than 2^31 - 1 elements, of any size, is
    // CountLimit. A length Binding did not resolve makes it Rejected.
    private Entry PlaceArray(BoundType type, int budget)
    {
        if (type.Components.Count != 1)
        {
            throw Outside(type);
        }

        if (type.Length < 0)
        {
            return Rejected(type);
        }

        var fold = new Fold(type);
        var element = fold.Add(this.Walk(type.Components[0], budget), false);
        if (fold.Close(budget, type.Length > MaximumSize ? Outcome.CountLimit : Outcome.Ok) is { } decided)
        {
            return decided;
        }

        var size = type.Length * element!.Layout.Stride;
        if (size > MaximumSize)
        {
            return fold.Laid(Exceeded);
        }

        this.scratch.Begin();
        this.scratch.WriteBytes(element.Layout.Alignment, size);
        return fold.Laid(new(Outcome.Ok, this.Record((int)size, element.Layout.Alignment, []), null));
    }

    // An enum: an i32 tag at offset 0, then the union of its Case payload Tuples, aligned for the most aligned payload (IMPL 21.1.3:
    // implementation-defined). The nonnull Option is laid out so too until PLAN U8 (STATUS). Payloads Binding did not substitute make
    // it Rejected.
    private Entry PlaceEnum(BoundType type, int budget)
    {
        if (AdtDef.CaseTypes(type) is not { } payloads)
        {
            return Rejected(type);
        }

        var fold = new Fold(type);
        var alignment = 1;
        long payload = 0;
        foreach (var item in payloads)
        {
            if (fold.Add(this.Walk(item, budget), false) is { } value)
            {
                alignment = Math.Max(alignment, value.Layout.Alignment);
                payload = Math.Max(payload, value.Layout.Size);
            }
        }

        if (fold.Close(budget, Outcome.Ok) is { } decided)
        {
            return decided;
        }

        var offset = (int)Align(4, alignment);
        var size = Align(offset + Align(payload, alignment), Math.Max(4, alignment));
        if (size > MaximumSize)
        {
            return fold.Laid(Exceeded);
        }

        var local = this.scratch;
        local.Begin();
        local.Write("{ i32, ");
        local.WriteBytes(alignment, Align(payload, alignment));
        local.Write(" }");
        return fold.Laid(new(Outcome.Ok, this.Record((int)size, Math.Max(4, alignment), [0, offset]), null));
    }

    // Completes the key written as the storage text and returns the representation's one record, created at its first use.
    private ValueLowering Record(int size, int alignment, ReadOnlySpan<int> fieldOffsets)
    {
        var local = this.scratch;
        var storage = local.Length;
        local.Write("|");
        local.Write(size);
        local.Write("|");
        local.Write(alignment);
        foreach (var offset in fieldOffsets)
        {
            local.Write(",");
            local.Write(offset);
        }

        if (this.recordsByKey.TryGetValue(local.Key, out var record))
        {
            return record;
        }

        var text = local.Key.ToString();
        var storageType = text[..storage];
        record = new(new(storageType, size, alignment, size, fieldOffsets.ToArray()), storageType, "ptr");
        this.records.Add(text, record);
        return record;
    }

    // A placed Type's answer and placement height; an exhausted entry's height is its budget + 1.
    private readonly record struct Entry(Result Result, int Height);

    // An exhausted entry's key: the Type, by reference (BoundType's identity), and its remaining budget.
    private readonly record struct Window(BoundType Type, int Budget);

    // One placement's parts folded in placement order, the owner of R5-M37's precedence: the first CLayout in the window, then
    // DepthLimit when a part reaches past the budget, then the first limit (the parts', then the Type's own count), then the first
    // Rejected, then the Type's own size.
    private struct Fold
    {
        private readonly BoundType type;
        private BoundType? clayout;
        private BoundType? rejected;
        private Outcome limit;
        private int height;

        internal Fold(BoundType type) => this.type = type;

        // Folds a part's entry and returns its record when it is laid out; a zero-sized one makes a C-layout struct CLayout.
        internal ValueLowering? Add(Entry part, bool cField)
        {
            this.height = Math.Max(this.height, part.Height);
            var result = part.Result;
            if (result.Outcome == Outcome.Ok && cField && result.Value!.Layout.Size == 0)
            {
                this.clayout ??= this.type;
            }
            else if (result.Outcome == Outcome.CLayout)
            {
                this.clayout ??= result.Instance;
            }
            else if (result.Outcome == Outcome.Rejected)
            {
                this.rejected ??= result.Instance;
            }
            else if (result.Outcome is Outcome.SizeLimit or Outcome.CountLimit && this.limit == Outcome.Ok)
            {
                this.limit = result.Outcome;
            }

            return result.Value;
        }

        // A stored Type Binding did not resolve: nothing placed, no level.
        internal void Reject() => this.rejected ??= this.type;

        // The decided entry of parts placed with `budget` levels left, or null when every part is laid out and the Type's own size
        // decides.
        internal readonly Entry? Close(int budget, Outcome own)
        {
            if (this.height > budget)
            {
                return new Entry(this.clayout is null ? new(Outcome.DepthLimit, null, null) : new Result(Outcome.CLayout, null, this.clayout), budget + 2);
            }

            if (this.clayout is not null)
            {
                return new Entry(new(Outcome.CLayout, null, this.clayout), this.height + 1);
            }

            var limited = this.limit == Outcome.Ok ? own : this.limit;
            if (limited != Outcome.Ok)
            {
                return new Entry(new(limited, null, null), this.height + 1);
            }

            return this.rejected is null ? null : new Entry(new(Outcome.Rejected, null, this.rejected), this.height + 1);
        }

        // The Type's own size decided its result.
        internal readonly Entry Laid(Result own) => new(own, this.height + 1);
    }

    // The placement buffers: the parts of the placements in progress, innermost last, and one record's offsets and key, written
    // after its parts are laid out.
    private sealed class Scratch
    {
        private int[] offsets = new int[16];
        private char[] key = new char[128];

        internal List<ValueLowering> Parts { get; } = [];

        internal int Length { get; private set; }

        internal ReadOnlySpan<char> Key => this.key.AsSpan(0, this.Length);

        internal Span<int> Offsets(int count)
        {
            if (this.offsets.Length < count)
            {
                this.offsets = new int[Math.Max(count, this.offsets.Length * 2)];
            }

            return this.offsets.AsSpan(0, count);
        }

        internal void Begin() => this.Length = 0;

        internal void Write(ReadOnlySpan<char> text)
        {
            this.Reserve(text.Length);
            text.CopyTo(this.key.AsSpan(this.Length));
            this.Length += text.Length;
        }

        internal void Write(long number)
        {
            this.Reserve(20);
            number.TryFormat(this.key.AsSpan(this.Length), out var written, default, CultureInfo.InvariantCulture);
            this.Length += written;
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

        private void Reserve(int count)
        {
            if (this.Length + count > this.key.Length)
            {
                Array.Resize(ref this.key, Math.Max(this.Length + count, this.key.Length * 2));
            }
        }
    }
}
