// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the legacy runtime helpers of the generated LLVM text until Kimigayo library code replaces them at R8 (plan PD2, P1a-P2b;
// DECISIONS R5-M16): the per-element helper records of the Array, Dictionary and formatting runtime text, the element descriptor
// of a stored collection value, the stored-value text kernels the helpers write through, the Array capacity routines and helpers
// (LegacyRuntimeHelpers.Arrays.cs), a Dictionary entry's layout and the Dictionary helpers (LegacyRuntimeHelpers.Dictionaries.cs),
// the formatting runtime with its routines' ABIs and the wrappers over user formatting witnesses (LegacyRuntimeHelpers.Formatting.cs),
// the storage-boundary and capacity kernels written inline in their caller's body with their library record Field addressing
// (LegacyRuntimeHelpers.Storage.cs) and the string-comparison routines, spliced from their signatures (StringEqual, StringCompare,
// the ones every call reads). MIR code generation writes the helpers through this static component (promoted, never copied).
// Input: helper records built from FunctionAbi, ValueLowering, StoredValue (drop routine name, text flag), the Option payload
// offset and remainder Field offsets; the compiled ABIs of the DictionaryStorage functions a Dictionary
// helper calls; the formatting wrappers' ABIs and the compiled functions they call; a built-in formattable Type, whose
// formatting kind and bound it gives (FormatKind, FormatBound), and a Text.writer destination, whose writer kind it gives (WriterKind);
// an owning interpolation's literal bytes and values' bounds, summed by its estimate (FormatEstimate); the runtime templates
// (.ll.in); for
// an inline storage kernel, StorageKernelInput (the spellings of the address it reads and of its result, its number, its
// result record's Field offsets, the number naming its temporaries); a writer, LLVM SSA names and sizes.
// Output: LLVM text written to the writer; per helper kind one row, readable
// by kind before any representation exists, which declares the kind's library callees and type-driven needs for the instance
// collector (X4). The rows land in P1b-P2b: each Array kind's row (ArrayRow; P1b) holds its name prefix, ABI shape, prologue,
// writer and HelperNeeds (the Array runtime for the kinds that grow a buffer or copy or move elements, every kind but Clear,
// Drop, BorrowStorage and OwnStorage; the element's drop glue for Clear and Drop), and no Array row declares a library callee,
// since none calls one; each Dictionary kind's row (DictionaryRow; P1c) holds its ABI shape, writer, HelperNeeds (the Array
// runtime for Place, which copies its key and value; the element's drop glue for Drop) and its callees as DictionaryCallee values:
// the generic function its helpers call over the entry Types (clearEntries for Drop) and the one the module resolves (append for
// Place). DictionaryCallee, the key of KimiLibrary's DictionaryStorage view (F2c), names each function by the symbol Binding declares
// for it; Construction (initialize) is the callee a Dictionary's construction runs.
// Each formatting compiler function's row
// (FormattingRow; P1d) holds its routine's ABI, whose capacity Context names by its logical index the parameter receiving the fixed array
// it measures, and HelperNeeds (the formatting runtime) and declares no library callee, since the formatting runtime calls none; the
// formatting witness WriterWrite, TextToString and TextTryFormat run is the need their published library rows state (Formats, SPEC
// 4.9.6), which the collector reads there; the kind Context of a built-in formattable Type is the runtime's own numbering (FormatKind),
// and so is Text.writer's of its destination (WriterKind); a bounded Type's maximum is the runtime's bound at its kind (FormatBound),
// which an owning interpolation's estimate sums for its buffer, hints and stack placement (FormatEstimate, FormatStackLimit; R3-10).
// Each inline storage kernel's row
// (StorageRow; P2a) holds its writer over StorageKernelInput and HelperNeeds, which are none: its text is loads, stores and
// address arithmetic in the caller's body, so it declares no callee. The capacity
// kernel (P2b) is a plain writer without a row: their calls are intrinsics, whose rows are IntrinsicRows'. The one map from a compiler
// function of the Body disposition to how MIR code generation writes its call (Row, S2-S4 X39; DECISIONS R5-M116) names each kind's
// writer: writeLine's runtime routine, the whole-value updates, the fixed-array storage kernel, the Array operations and storage kinds in
// their function's instance (Instance: the Array helper of its kind, or a capacity routine, through its call's LegacyRow item; DECISIONS
// R5-M121), setArrayLength's length, placeEntry in its function's instance (Place: the Dictionary Place helper, which calls the item's
// declared callee, append), the other Dictionary storage kinds as the P2a kernels over their call's entry layout (DECISIONS R5-M124) and
// the Text, buffer, window and writer kinds and WriteLineUtf8 as their formatting routines (Formatting; DECISIONS R5-M128).
// Invariants: a new helper kind adds a row; a kind without a row fails, a Body-disposition compiler function's included; a helper's
// runtime needs come from its row; warm writes allocate nothing; whether a value travels as a scalar is decided once, by
// StoredValue.TravelsAsScalar.
// Must not hold module state, take syntax or MIR types, or a BoundType but a built-in formattable Type or a writer's
// destination for its kind or bound (FormatKind, FormatBound, WriterKind), or use LINQ.

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

    /// <summary>The formatting runtime: the UTF-8 buffer, writer, formatting and floating-point routines and the memory
    /// declarations (SPEC 22.7, 22.9, 22.10).</summary>
    FormattingRuntime = 8,
}

/// <summary>How MIR code generation writes a call of a compiler function of the Body disposition (S2-S4 X39; DECISIONS R5-M116).</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal enum LegacyWriter : byte
{
    /// <summary>The row's runtime routine through the one call writer.</summary>
    Routine,

    /// <summary>SPEC 22.7, 22.9, 22.10.1: the row's routine in the formatting runtime through the one call writer, its capacity Context the
    /// length of the fixed array its operand refers to and its kind Context its witness target's Self's (FunctionCodegen.Formatting;
    /// DECISIONS R5-M128).</summary>
    Formatting,

    /// <summary>SPEC 15.7: the target's old value destroyed in place, then the new one put (FunctionCodegen.Updates).</summary>
    Replace,

    /// <summary>SPEC 15.7: the target's old value put at the result, then the new one at the target.</summary>
    Exchange,

    /// <summary>SPEC 15.7: the two targets' values exchanged.</summary>
    Swap,

    /// <summary>SPEC 22.2: a fixed array's remainder through the BorrowFixed kernel (FunctionCodegen.Storage).</summary>
    FixedStorage,

    /// <summary>SPEC 4.9.3, 4.9.5, 22.2: the row's routine in its function's instance, its call's LegacyRow item's (ModuleCodegen.Row): the
    /// Array helper of the row's <see cref="LegacyRow.Helper"/> kind, or the row's capacity routine given the element's stride (DECISIONS
    /// R5-M121).</summary>
    Instance,

    /// <summary>SPEC 22.2: setArrayLength, the length stored in the Array handle's length word (FunctionCodegen.Arrays).</summary>
    Length,

    /// <summary>SPEC 4.9.6, 22.2: placeEntry, P1c's Place helper in its function's instance, its call's LegacyRow item's (ModuleCodegen.Row),
    /// which calls the item's declared callee, append (DECISIONS R5-M124).</summary>
    Place,

    // The Dictionary storage kernels (SPEC 22.2; DECISIONS R5-M124), each P2a's kernel over its call's entry layout with its own number
    // (FunctionCodegen.Storage's DictionaryKernel); they stay contiguous, so LegacyRow.DictionaryKernel names them by range.

    /// <summary>dictionaryStorage: the {address, entry stride} Tuple through the DictionaryLayout kernel.</summary>
    DictionaryLayout,

    /// <summary>borrowStorage and borrowStorageUniq over a Dictionary: the remainder through the BorrowDictionary kernel, with the stride.</summary>
    DictionaryBorrow,

    /// <summary>ownStorage of a Dictionary: the owning remainder through the OwnDictionary kernel, with the stride.</summary>
    DictionaryOwn,

    /// <summary>keyAt: a slot's key address through the EntryAddress kernel, with the key's offset.</summary>
    KeyAt,

    /// <summary>valueAt: a slot's value address through the EntryAddress kernel, with the value's offset.</summary>
    ValueAt,
}

/// <summary>The legacy row of a compiler function of the Body disposition (X39): its writer, a routine row's runtime routine and an Instance
/// row's Array helper kind.</summary>
/// <param name="Writer">How MIR code generation writes a call.</param>
/// <param name="Routine">The runtime routine of a <see cref="LegacyRow.Fixed"/> row, or an <see cref="LegacyWriter.Instance"/> row's capacity
/// routine.</param>
/// <param name="Helper">The Array helper kind of an <see cref="LegacyWriter.Instance"/> row without a routine.</param>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal readonly record struct LegacyRow(LegacyWriter Writer, FunctionAbi? Routine = null, ArrayHelperKind? Helper = null)
{
    /// <summary>Gets a value indicating whether the row is a whole-value update (SPEC 15.7): the one place that names the update writers.</summary>
    internal bool Updates => this.Writer is LegacyWriter.Replace or LegacyWriter.Exchange or LegacyWriter.Swap;

    /// <summary>Gets a value indicating whether the row's call runs a routine in its function's instance, its LegacyRow item's (K 4.5 row 2;
    /// DECISIONS R5-M121, R5-M124): the one place that names those writers.</summary>
    internal bool InInstance => this.Writer is LegacyWriter.Instance or LegacyWriter.Place;

    /// <summary>Gets a value indicating whether the row's call is its row's own fixed-symbol routine (writeLine's, a formatting routine;
    /// DECISIONS R5-M128): the one place that names those writers.</summary>
    internal bool Fixed => this.Writer is LegacyWriter.Routine or LegacyWriter.Formatting;

    /// <summary>Gets a value indicating whether the row is a Dictionary storage kernel (DECISIONS R5-M124): the one place that names them.</summary>
    internal bool DictionaryKernel => this.Writer is >= LegacyWriter.DictionaryLayout and <= LegacyWriter.ValueAt;

    /// <summary>Gets the row's layout need (LayoutNeeds; DECISIONS R5-M149): a whole-value update's and a Dictionary kernel's call reads
    /// its Type arguments' layouts (T's size; K's and V's entry layout); a row in its function's instance reads them through its LegacyRow
    /// item, a type-driven item the collector lays out; every other row reads none.</summary>
    internal LayoutNeed Need => this.Updates || this.DictionaryKernel ? LayoutNeed.TypeArguments : LayoutNeed.None;

    /// <summary>Gets the runtime needs a row declares (X4; DECISIONS R5-M46, R5-M121, R5-M124, R5-M128): an Array helper kind's row's, the
    /// Array runtime for a capacity routine, which is that runtime's, placeEntry's Place row's, or the formatting runtime for a formatting
    /// routine, the need every formatting row declares (FormattingRow; DECISIONS R5-M30).</summary>
    internal HelperNeeds Needs => this switch
    {
        { Helper: { } kind } => LegacyRuntimeHelpers.ArrayRow(kind).Needs,
        { Writer: LegacyWriter.Instance } => HelperNeeds.ArrayRuntime,
        { Writer: LegacyWriter.Place } => LegacyRuntimeHelpers.DictionaryRow(DictionaryHelperKind.Place).Needs,
        { Writer: LegacyWriter.Formatting } => HelperNeeds.FormattingRuntime,
        _ => HelperNeeds.None,
    };

    /// <summary>Gets the library callee a row in its function's instance declares (X4, F2c; DECISIONS R5-M124): placeEntry's, its Place row's
    /// Storage callee, append; None for every other row, since no Array helper calls one.</summary>
    internal DictionaryCallee Callee
        => this.Writer == LegacyWriter.Place ? LegacyRuntimeHelpers.DictionaryRow(DictionaryHelperKind.Place).Storage : DictionaryCallee.None;
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
internal static partial class LegacyRuntimeHelpers
{
    internal const string Removal = "R8 replaces the legacy runtime helpers with Kimigayo library code.";

    /// <summary>SPEC 13.4: the string equality routine, 0 exactly when the two byte sequences are equal: the one signature its definition
    /// (<see cref="StringComparisons"/>) and every call read.</summary>
    internal static readonly FunctionAbi StringEqual =
        new("__kimi_string_equal", "i32", [new("ptr", "a"), new("i64", "an"), new("ptr", "b"), new("i64", "bn")]);

    /// <summary>SPEC 13.4: the string ordering routine, negative, 0 or positive as the first byte sequence orders before, equal to or after
    /// the second, lexicographically by UTF-8 byte.</summary>
    internal static readonly FunctionAbi StringCompare = new("__kimi_string_compare", "i32", StringEqual.Parameters);

    /// <summary>The string-comparison routines' text, each definition spliced from its signature (LlvmModuleText.ExpandRuntime), which code
    /// generation writes once per module that compares strings.</summary>
    internal static readonly string StringComparisons = ExpandRuntime(
        """
        declare i32 @memcmp(ptr, ptr, i64)
        {{__kimi_string_equal}}
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
        {{__kimi_string_compare}}
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

        """,
        [StringEqual, StringCompare]);

    /// <summary>Gets the legacy row of a compiler function of the Body disposition (S2-S4 X39; DECISIONS R5-M116), the one map from such a
    /// kind to how MIR code generation writes its call, by the row's writer, and the one test of whether a function is one: every site asks
    /// it, a Call, a Value call, the copy slots and the routine rows.</summary>
    /// <param name="kind">The compiler function.</param>
    /// <returns>Its row; null for None and for a kind of another disposition.</returns>
    internal static LegacyRow? Row(CompilerFunctionKind kind) => kind switch
    {
        CompilerFunctionKind.None => null,
        CompilerFunctionKind.WriteLine => new(LegacyWriter.Routine, RuntimeAbi.WriteLine), // SPEC 22.1-22.5.5: it borrows its string.
        CompilerFunctionKind.Replace => new(LegacyWriter.Replace),
        CompilerFunctionKind.Exchange => new(LegacyWriter.Exchange),
        CompilerFunctionKind.Swap => new(LegacyWriter.Swap),
        CompilerFunctionKind.StorageBorrowFixedShared or CompilerFunctionKind.StorageBorrowFixedExclusive => new(LegacyWriter.FixedStorage),

        // SPEC 4.9.3, 4.9.5, 22.2 (DECISIONS R5-M121): the Array operations and storage kinds in their function's instance.
        CompilerFunctionKind.ArrayReserve => new(LegacyWriter.Instance, RuntimeAbi.ArrayReserve),
        CompilerFunctionKind.ArrayShrinkToFit => new(LegacyWriter.Instance, RuntimeAbi.ArrayShrink),
        CompilerFunctionKind.ArrayAppend => Instance(ArrayHelperKind.Append),
        CompilerFunctionKind.ArrayInsert => Instance(ArrayHelperKind.Insert),
        CompilerFunctionKind.ArrayPop => Instance(ArrayHelperKind.Pop),
        CompilerFunctionKind.ArrayRemove => Instance(ArrayHelperKind.Remove),
        CompilerFunctionKind.ArrayClear => Instance(ArrayHelperKind.Clear),
        CompilerFunctionKind.ArraySwap => Instance(ArrayHelperKind.Swap),
        CompilerFunctionKind.StorageBorrowShared or CompilerFunctionKind.StorageBorrowExclusive or CompilerFunctionKind.StorageBorrowUniqSlice =>
            Instance(ArrayHelperKind.BorrowStorage),
        CompilerFunctionKind.StorageOwn => Instance(ArrayHelperKind.OwnStorage),
        CompilerFunctionKind.StorageOwnFixed => Instance(ArrayHelperKind.OwnFixedStorage),
        CompilerFunctionKind.StorageSetArrayLength => new(LegacyWriter.Length),

        // SPEC 4.9.6, 22.2 (DECISIONS R5-M124): the Dictionary storage kinds, placeEntry in its function's instance, the others inline kernels.
        CompilerFunctionKind.StoragePlaceDictionaryEntry => new(LegacyWriter.Place),
        CompilerFunctionKind.StorageDictionaryLayout => new(LegacyWriter.DictionaryLayout),
        CompilerFunctionKind.StorageBorrowDictionary or CompilerFunctionKind.StorageBorrowDictionaryExclusive => new(LegacyWriter.DictionaryBorrow),
        CompilerFunctionKind.StorageOwnDictionary => new(LegacyWriter.DictionaryOwn),
        CompilerFunctionKind.StorageKeyAt => new(LegacyWriter.KeyAt),
        CompilerFunctionKind.StorageValueAt => new(LegacyWriter.ValueAt),

        // SPEC 22.7, 22.9, 22.10.1 (DECISIONS R5-M128): the Text, buffer, window and writer kinds and WriteLineUtf8, their formatting routines
        // (P1d's rows, one text); a Body-disposition kind without a row is a fault.
        _ when IntrinsicDisposition.Of(kind).Kind == DispositionKind.Body =>
            new(LegacyWriter.Formatting, FormattingRow(kind)?.Abi ?? throw new InvalidOperationException($"The compiler function {kind} has no legacy row.")),
        _ => null,
    };

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

    // An Instance row over the Array helper of a kind (DECISIONS R5-M121).
    private static LegacyRow Instance(ArrayHelperKind kind) => new(LegacyWriter.Instance, Helper: kind);
}
