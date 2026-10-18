// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the legacy runtime helpers of the generated LLVM text until Kimigayo library code replaces them at R8 (plan PD2, P1a-P2b;
// DECISIONS R5-M16): a module's requests for Array, Dictionary, formatting and string-comparison runtime text (the per-element
// helper records, the runtime flags, the source functions the compiler requested and the formatting wrappers), the element
// descriptor of a stored collection value, the stored-value text kernels the helpers write through, the Array capacity routines
// and helpers (LegacyRuntimeHelpers.Arrays.cs) and the string-comparison routines. Each generator's module holds one instance,
// and both write the helpers through this component (promoted, never copied); the old writer's Dictionary and formatting family
// writers join it in P1c-P1d.
// Input: helper records built from FunctionAbi, ValueLowering, StoredValue (drop routine name, text flag), the Option payload
// offset and remainder Field offsets; the library ABIs the Dictionary helpers call; a writer, LLVM SSA names and sizes.
// Output: the module's request state, in request order, and LLVM text written to the writer; per helper kind one row, readable
// by kind before any representation exists, which declares the kind's library callees and type-driven needs for the instance
// collector (X4). The rows land in P1b-P2b: each Array kind's row (ArrayRow; P1b) holds its name prefix, ABI shape, prologue,
// writer and HelperNeeds (the Array runtime for every kind but BorrowStorage, the element's drop glue for Clear and Drop), and no
// Array row declares a library callee, since none calls one; the Dictionary and formatting rows follow in P1c and P1d, the storage
// kernels in P2a-P2b.
// Invariants: a new helper kind adds a row; a kind without a row fails; each requested helper is written once, in request order;
// the legacy writer's .ll stays byte-identical while it shares this text; warm writes allocate nothing; a Dictionary helper's
// Related index and library callees resolve before writing; whether a value travels as a scalar is decided once, by
// StoredValue.TravelsAsScalar.
// Must not take AggregateLayout, EmissionModule, BoundType, syntax or MIR types, branch on the generator, or use LINQ.

#pragma warning disable SA1402 // The helper records and the element descriptor are this component's request vocabulary.

using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal enum DictionaryHelperKind : byte
{
    CheckKey,
    Place,
    Drop,
}

/// <summary>The element descriptor of a stored collection value (SPEC 4.5, 16.3.2, 21.3.1): its representation, the name of the
/// routine that destroys it when it is an aggregate needing destruction (otherwise null), and whether it is a string.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal readonly record struct StoredValue(ValueLowering Value, string? Drop, bool Text)
{
    /// <summary>Gets a value indicating whether the value has no bytes (Unit or a zero-sized aggregate); its helpers take no
    /// value or result pointer, because zero-sized values have no slot.</summary>
    internal bool IsZeroSized => this.Value.Layout.Size == 0;

    /// <summary>Gets a value indicating whether the value travels as a scalar (<see cref="TravelsAsScalar"/>).</summary>
    internal bool IsScalar => TravelsAsScalar(this.Value);

    /// <summary>Gets a value indicating whether destroying the value runs code.</summary>
    internal bool NeedsDestruction => this.Text || this.Drop is not null;

    /// <summary>Gets a value indicating whether a value of a representation travels as a scalar rather than through a slot: its
    /// internal-ABI argument form is its computation representation (SPEC 21.3.1), while strings and aggregates pass by address
    /// and Unit has no argument. The helpers' one scalar decision, which their requesters ask too.</summary>
    /// <param name="value">The value's representation.</param>
    /// <returns>True when the value is passed, returned and stored as a scalar.</returns>
    internal static bool TravelsAsScalar(ValueLowering value) => value.ArgumentType == value.ComputationType;
}

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
internal sealed partial class LegacyRuntimeHelpers
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

    // The per-element Array helpers requested by lowered bodies (SPEC 4.9.3), in first-request order.
    private readonly List<ArrayHelper> arrayHelpers = new();

    internal List<DictionaryHelper> DictionaryHelpers { get; } = new();

    /// <summary>Gets the ABI of every source call the compiler requested for generated code, in request order: the destructors
    /// (SPEC 16.3.2) and the Dictionary storage operations, which a Dictionary helper calls through its Related index.</summary>
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

    // Stores an argument into its slot: a scalar in its storage form, widened through `widened` when that is wider than its
    // computation form; another value by copying its bytes from the slot the argument points to.
    internal static void WriteStoredArgument(TextWriter output, in StoredValue value, string source, string destination, string widened)
    {
        var lowering = value.Value;
        if (!value.IsScalar)
        {
            WriteStoredCopy(output, destination, source, lowering.Layout.Size);
            return;
        }

        if (lowering.ComputationType != lowering.Layout.StorageType)
        {
            output.Write("  ");
            output.Write(widened);
            output.Write(" = zext ");
            output.Write(lowering.ComputationType);
            output.Write(' ');
            output.Write(source);
            output.Write(" to ");
            output.Write(lowering.Layout.StorageType);
            output.Write('\n');
            source = widened;
        }

        output.Write("  store ");
        output.Write(lowering.Layout.StorageType);
        output.Write(' ');
        output.Write(source);
        output.Write(", ptr ");
        output.Write(destination);
        output.Write(", align ");
        WriteNumber(output, lowering.Layout.Alignment);
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
        this.arrayHelpers.Clear();
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
