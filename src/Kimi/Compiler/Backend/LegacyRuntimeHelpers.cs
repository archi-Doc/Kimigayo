// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the legacy runtime helpers of the generated LLVM text until Kimigayo library code replaces them at R8 (plan PD2, P1a-P2b;
// DECISIONS R5-M16): a module's requests for Array, Dictionary, formatting and string-comparison runtime text (the per-element
// helper records, the runtime flags, the library functions the Dictionary helpers call and the formatting wrappers), the element
// descriptor of a stored collection value, the stored-value text kernels those helpers write through and the string-comparison
// routines. Each generator's module holds one instance, and both write the helpers through this component (promoted, never
// copied); the old writer's Array, Dictionary and formatting family writers join it in P1b-P1d.
// Input: helper records built from FunctionAbi, ValueLowering, StoredValue (drop routine name, text flag), the Option payload
// offset and remainder Field offsets; the library ABIs the Dictionary helpers call; a writer, LLVM SSA names and sizes.
// Output: the module's request state, in request order, and LLVM text written to the writer.
// Invariants: each requested helper is written once, in request order; the legacy writer's .ll stays byte-identical while it
// shares this text; warm writes allocate nothing; a Dictionary helper's Related index and library callees resolve before writing.
// Must not take AggregateLayout, EmissionModule, BoundType, syntax or MIR types, branch on the generator, or use LINQ.

#pragma warning disable SA1402 // The helper records and the element descriptor are this component's request vocabulary.

using System.Globalization;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal enum ArrayHelperKind : byte
{
    Append,
    Insert,
    Pop,
    Remove,
    Place,
    Clear,
    Drop,
    Swap,

    // SPEC 22.2: the storage boundary over an Array handle and its remainder records.
    BorrowStorage,
    OwnStorage,

    // PLAN G33: a consumed fixed array moves into the inline storage of its owning remainder.
    OwnFixedStorage,
}

[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal enum DictionaryHelperKind : byte
{
    CheckKey,
    Place,
    Drop,
}

/// <summary>The layout offsets of a storage remainder record's Fields {storage, position, count, capacity}, addressed by name,
/// and the size of its storage Field (SPEC 22.2).</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal readonly record struct RemainderFields(int Storage, int Position, int Count, int Capacity, int StorageSize);

/// <summary>The element descriptor of a stored collection value (SPEC 4.5, 16.3.2, 21.3.1): its representation, the name of the
/// routine that destroys it when it is an aggregate needing destruction (otherwise null), and whether it is a string.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal readonly record struct StoredValue(ValueLowering Value, string? Drop, bool Text)
{
    /// <summary>Gets a value indicating whether the value has no bytes (Unit or a zero-sized aggregate); its helpers take no
    /// value or result pointer, because zero-sized values have no slot.</summary>
    internal bool IsZeroSized => this.Value.Layout.Size == 0;

    /// <summary>Gets a value indicating whether the value travels as a scalar rather than through a slot: its internal-ABI
    /// argument form is its computation representation (SPEC 21.3.1), while strings and aggregates pass by address and Unit
    /// has no argument.</summary>
    internal bool IsScalar => this.Value.ArgumentType == this.Value.ComputationType;

    /// <summary>Gets a value indicating whether destroying the value runs code.</summary>
    internal bool NeedsDestruction => this.Text || this.Drop is not null;
}

/// <summary>A generated Array helper for one element: its ABI, element descriptor, for pop the Option payload offset (the tag is
/// at offset 0), and for the storage boundary its remainder record's Fields.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal sealed record ArrayHelper(ArrayHelperKind Kind, FunctionAbi Abi, StoredValue Element, int? OptionPayload = null, RemainderFields Fields = default);

/// <summary>The physical layout of a Dictionary entry: the key and value offsets and the entry stride.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal readonly record struct DictionaryEntryLayout(long KeyOffset, long ValueOffset, long Stride);

/// <summary>Physical Dictionary entry helper; source calls resolve to the ordinary instantiated ABI. Related indexes
/// <see cref="LegacyRuntimeHelpers.FunctionReferences"/>, or is -1.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal sealed record DictionaryHelper(
    DictionaryHelperKind Kind, FunctionAbi Abi, StoredValue Key, StoredValue Value, DictionaryEntryLayout Entry, int Related);

/// <summary>A module's legacy runtime helper requests, and the text kernels and routines its helpers share.</summary>
[Transitional("R8", Reason = Removal)]
internal sealed class LegacyRuntimeHelpers
{
    internal const string Removal = "R8 replaces the legacy runtime helpers with Kimigayo library code.";

    internal const string StringComparisons = """
        declare i32 @memcmp(ptr, ptr, i64)
        define internal i32 @__kimi_string_equal(ptr %a, i64 %an, ptr %b, i64 %bn) #0 {
        entry:
          %sameLength = icmp eq i64 %an, %bn
          br i1 %sameLength, label %same, label %different
        same:
          %empty = icmp eq i64 %an, 0
          br i1 %empty, label %equal, label %bytes
        bytes:
          %comparison = call i32 @memcmp(ptr %a, ptr %b, i64 %an)
          ret i32 %comparison
        equal:
          ret i32 0
        different:
          ret i32 1
        }
        define internal i32 @__kimi_string_compare(ptr %a, i64 %an, ptr %b, i64 %bn) #0 {
        entry:
          %shorter = icmp ult i64 %an, %bn
          %count = select i1 %shorter, i64 %an, i64 %bn
          %empty = icmp eq i64 %count, 0
          br i1 %empty, label %lengths, label %bytes
        bytes:
          %comparison = call i32 @memcmp(ptr %a, ptr %b, i64 %count)
          %equal = icmp eq i32 %comparison, 0
          br i1 %equal, label %lengths, label %different
        different:
          ret i32 %comparison
        lengths:
          %sameLength = icmp eq i64 %an, %bn
          %direction = select i1 %shorter, i32 -1, i32 1
          %result = select i1 %sameLength, i32 0, i32 %direction
          ret i32 %result
        }
        """ + "\n";

    /// <summary>Gets the per-element Array helpers requested by lowered bodies (SPEC 4.9.3).</summary>
    internal List<ArrayHelper> ArrayHelpers { get; } = new();

    internal List<DictionaryHelper> DictionaryHelpers { get; } = new();

    /// <summary>Gets the functions the Dictionary helpers call through their Related index.</summary>
    internal List<FunctionAbi> FunctionReferences { get; } = new();

    /// <summary>Gets or sets a value indicating whether a lowered body uses the Array capacity routines (SPEC 4.9.5).</summary>
    internal bool NeedsArrayRuntime { get; set; }

    internal bool NeedsDictionaryRuntime { get; set; }

    /// <summary>Gets or sets a value indicating whether a lowered body uses the private storage byte primitives (SPEC 22.2).</summary>
    internal bool NeedsStorageBytes { get; set; }

    internal FunctionAbi? DictionaryAppendSlot { get; set; }

    internal FunctionAbi? DictionaryRequireAbsent { get; set; }

    internal FunctionAbi? DictionaryReserveStorage { get; set; }

    internal FunctionAbi? DictionaryAppend { get; set; }

    internal FunctionAbi? DictionaryInitialize { get; set; }

    internal FunctionAbi? DictionaryClearLinks { get; set; }

    internal FunctionAbi? DictionaryShrink { get; set; }

    internal bool NeedsFormattingRuntime { get; set; }

    internal List<(FunctionAbi Wrapper, FunctionAbi Implementation)> FormattingWrites { get; } = new();

    internal List<(FunctionAbi Wrapper, FunctionAbi Write, bool Fixed)> FormattingConversions { get; } = new();

    internal bool NeedsStringComparison { get; set; }

    internal static string Reason(int reason) => reason.ToString(CultureInfo.InvariantCulture);

    internal static void WriteStoredCopy(TextWriter output, string destination, string source, long size)
    {
        if (size == 0)
        {
            return;
        }

        output.Write("  call void @llvm.memcpy.p0.p0.i64(ptr ");
        output.Write(destination);
        output.Write(", ptr ");
        output.Write(source);
        output.Write(", i64 ");
        WriteNumber(output, size);
        output.Write(", i1 false)\n");
    }

    internal static void WriteStoredArgument(TextWriter output, ValueLowering value, bool scalar, string source, string destination, string widened)
    {
        if (!scalar)
        {
            WriteStoredCopy(output, destination, source, value.Layout.Size);
            return;
        }

        if (value.ComputationType != value.Layout.StorageType)
        {
            output.Write("  ");
            output.Write(widened);
            output.Write(" = zext ");
            output.Write(value.ComputationType);
            output.Write(' ');
            output.Write(source);
            output.Write(" to ");
            output.Write(value.Layout.StorageType);
            output.Write('\n');
            source = widened;
        }

        output.Write("  store ");
        output.Write(value.Layout.StorageType);
        output.Write(' ');
        output.Write(source);
        output.Write(", ptr ");
        output.Write(destination);
        output.Write(", align ");
        WriteNumber(output, value.Layout.Alignment);
        output.Write('\n');
    }

    internal static void WriteStoredDestruction(TextWriter output, in StoredValue value, string address)
    {
        if (!value.NeedsDestruction)
        {
            return;
        }

        output.Write("  call void @");
        output.Write(value.Text ? RuntimeAbi.DestroyString.Name : value.Drop);
        output.Write("(ptr ");
        output.Write(address);
        output.Write(", ptr %location, i64 %location_length)\n");
    }

    internal void Clear()
    {
        this.ArrayHelpers.Clear();
        this.DictionaryHelpers.Clear();
        this.FunctionReferences.Clear();
        this.NeedsArrayRuntime = false;
        this.NeedsDictionaryRuntime = false;
        this.NeedsStorageBytes = false;
        this.DictionaryAppendSlot = null;
        this.DictionaryRequireAbsent = null;
        this.DictionaryReserveStorage = null;
        this.DictionaryAppend = null;
        this.DictionaryInitialize = null;
        this.DictionaryClearLinks = null;
        this.DictionaryShrink = null;
        this.NeedsFormattingRuntime = false;
        this.FormattingWrites.Clear();
        this.FormattingConversions.Clear();
        this.NeedsStringComparison = false;
    }

    /// <summary>Gets a value indicating whether a Dictionary helper's Related index or library callee is unresolved.</summary>
    /// <returns>True when the requests cannot be written.</returns>
    internal bool HasUnresolvedReference()
    {
        foreach (var helper in this.DictionaryHelpers)
        {
            if (helper.Related < -1 || helper.Related >= this.FunctionReferences.Count ||
                (helper.Kind == DictionaryHelperKind.CheckKey && (helper.Related < 0 || this.DictionaryRequireAbsent is null)) ||
                (helper.Kind == DictionaryHelperKind.Place && this.DictionaryAppend is null))
            {
                return true;
            }
        }

        return false;
    }
}
