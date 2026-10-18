// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the legacy runtime helpers of the generated LLVM text until Kimigayo library code replaces them at R8 (plan PD2, P1a-P2b;
// DECISIONS R5-M16): a module's requests for Array, Dictionary, formatting and string-comparison runtime text (the per-element
// helper records, the runtime flags, the source functions the compiler requested, the DictionaryStorage functions and the
// formatting wrappers), the element descriptor of a stored collection value, the stored-value text kernels the helpers write
// through, the Array capacity routines and helpers (LegacyRuntimeHelpers.Arrays.cs), the Dictionary runtime, the storage byte
// primitives and the Dictionary helpers (LegacyRuntimeHelpers.Dictionaries.cs), the formatting runtime with its routines' ABIs
// and the wrappers over user formatting witnesses (LegacyRuntimeHelpers.Formatting.cs), the storage-boundary, Raw.slice and
// capacity kernels written inline in their caller's body with the library record Field addressing they share with the old
// writer (LegacyRuntimeHelpers.Storage.cs) and the string-comparison routines. Each generator's module holds one instance, and both
// write the helpers through this component (promoted, never copied).
// Input: helper records built from FunctionAbi, ValueLowering, StoredValue (drop routine name, text flag), the Option payload
// offset and remainder Field offsets; the compiled ABIs of the DictionaryStorage functions the Dictionary helpers call, keyed
// by DictionaryCallee; the formatting wrappers' ABIs and the compiled functions they call; the runtime templates (.ll.in); for
// an inline storage kernel, StorageKernelInput (the spellings of the address it reads and of its result, its number, its
// result record's Field offsets, the number naming its temporaries); a writer, LLVM SSA names and sizes.
// Output: the module's request state, in request order, and LLVM text written to the writer; per helper kind one row, readable
// by kind before any representation exists, which declares the kind's library callees and type-driven needs for the instance
// collector (X4). The rows land in P1b-P2b: each Array kind's row (ArrayRow; P1b) holds its name prefix, ABI shape, prologue,
// writer and HelperNeeds (the Array runtime for the kinds that grow a buffer or copy or move elements, every kind but Clear,
// Drop, BorrowStorage and OwnStorage; the element's drop glue for Clear and Drop), and no Array row declares a library callee,
// since none calls one; each Dictionary kind's row (DictionaryRow; P1c) holds its ABI shape, writer, HelperNeeds (the Array
// runtime for Place, which copies its key and value; the Dictionary runtime for CheckKey; the element's drop glue for Drop) and
// its callees as DictionaryCallee values: the generic function its helpers call over the entry Types (findEntry for
// CheckKey, clearEntries for Drop) and the one the module resolves (requireAbsent for CheckKey, append for Place).
// DictionaryCallee, the key of KimiLibrary's DictionaryStorage view (F2c), names each function by the symbol Binding declares
// for it; the Dictionary runtime (the duplicate-key adapter and the initialization bridge, which calls initialize) has no
// row, and the old lowering also requests it for a Dictionary's construction. Each formatting compiler function's row
// (FormattingRow; P1d) holds its routine's ABI and HelperNeeds (the formatting runtime) and declares no library callee, since the
// formatting runtime calls none; the formatting witness WriterWrite, TextToString and TextTryFormat run is the need their
// published library rows state (Formats, SPEC 4.9.6), which the collector reads there. Each inline storage kernel's row
// (StorageRow; P2a) holds its writer over StorageKernelInput and HelperNeeds, which are none: its text is loads, stores and
// address arithmetic in the caller's body, so it declares no callee and nothing is requested for it. The Raw.slice and capacity
// kernels (P2b) are plain writers without a row: their calls are intrinsics, whose rows are IntrinsicRows'.
// Invariants: a new helper kind adds a row; a kind without a row fails; a request's runtime flags come from its row; each
// requested helper is written once (the planner requests each formatting wrapper once), in request order, after the runtimes its
// row declares;
// the legacy writer's .ll stays byte-identical while it shares this text; warm writes allocate nothing; a Dictionary helper's
// Related index and the callees its row declares resolve before writing; whether a value travels as a scalar is decided once,
// by StoredValue.TravelsAsScalar.
// Must not take AggregateLayout, EmissionModule, BoundType, syntax or MIR types, branch on the generator, or use LINQ.

#pragma warning disable SA1402 // The helper records and the element descriptor are this component's request vocabulary.

using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

/// <summary>The needs a legacy helper row declares (X4): runtime text its module must hold and items its helpers call.</summary>
[Flags]
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal enum HelperNeeds : byte
{
    None = 0,

    /// <summary>The Array capacity routines and the memory declarations (SPEC 4.9.5): a helper that grows a buffer or copies or
    /// moves elements needs them.</summary>
    ArrayRuntime = 1,

    /// <summary>The element's drop glue: the helper destroys elements, a Dictionary helper through its Entries function, which it
    /// then calls only when an entry needs destruction (SPEC 4.9.7, 16.3.2).</summary>
    ElementGlue = 2,

    /// <summary>The Dictionary runtime: the duplicate-key adapter a literal's key check reports through and the empty-Dictionary
    /// initialization bridge (SPEC 4.3, 4.9.6).</summary>
    DictionaryRuntime = 4,

    /// <summary>The formatting runtime: the UTF-8 buffer, writer, formatting and floating-point routines and the memory
    /// declarations (SPEC 22.7, 22.9, 22.10).</summary>
    FormattingRuntime = 8,
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

    /// <summary>Gets the ABI of every source call the compiler requested for generated code, in request order: the destructors
    /// (SPEC 16.3.2) and the Dictionary storage operations, which a Dictionary helper calls through its Related index.</summary>
    internal List<FunctionAbi> FunctionReferences { get; } = new();

    /// <summary>Gets or sets a value indicating whether a lowered body uses the Array capacity routines (SPEC 4.9.5).</summary>
    internal bool NeedsArrayRuntime { get; set; }

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
        this.dictionaryHelpers.Clear();
        this.FunctionReferences.Clear();
        this.NeedsArrayRuntime = false;
        this.NeedsDictionaryRuntime = false;
        this.NeedsStorageBytes = false;
        Array.Clear(this.dictionaryCallees);
        this.NeedsFormattingRuntime = false;
        this.formattingWrites.Clear();
        this.formattingConversions.Clear();
        this.NeedsStringComparison = false;
    }

    // A requested helper's runtime needs are its kind row's (X4); they join the module's.
    private void JoinNeeds(HelperNeeds needs)
    {
        this.NeedsArrayRuntime |= (needs & HelperNeeds.ArrayRuntime) != 0;
        this.NeedsDictionaryRuntime |= (needs & HelperNeeds.DictionaryRuntime) != 0;
        this.NeedsFormattingRuntime |= (needs & HelperNeeds.FormattingRuntime) != 0;
    }
}
