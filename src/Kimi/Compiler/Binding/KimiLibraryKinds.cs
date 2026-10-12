// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns compiler/library identities and the disposition of each compiler operation.
// Input: CompilerFunctionKind. Output: a Kimigayo implementation, intrinsic or shim disposition.
// Each operation has one disposition; temporary intrinsics keep their recorded removal stage.
// Must not create a second intrinsic allowlist or hand-written library path.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Compiler-owned identity vocabulary.

public enum IntrinsicKind : byte
{
    None,
    Copy,
    Owned,
    Callable,
    Sealed,
    ObjectPayload,
    PrimitiveInteger,
}

/// <summary>Identifies a compiler-provided language function implementation.</summary>
public enum CompilerFunctionKind : byte
{
    None,
    WriteLine,
    Abort,
    Replace,
    Exchange,
    Swap,
    MakeObj,
    MakeRc,
    MakeArc,
    Clone,
    TestTempDirectory,
    ArrayReserve,
    ArrayAppend,
    ArrayInsert,
    ArrayPop,
    ArrayRemove,
    ArrayClear,
    ArrayShrinkToFit,
    ArraySwap,
    TextFixed,
    TextHeap,
    TextWriter,
    TextUtf8,
    TextValidateUtf8,
    TextToString,
    TextTryFormat,
    TextRelease,
    FixedBufferBytes,
    FixedBufferText,
    FixedBufferValidate,
    FixedBufferClear,
    FixedBufferReserve,
    FixedBufferIntoText,
    HeapBufferBytes,
    HeapBufferText,
    HeapBufferValidate,
    HeapBufferClear,
    HeapBufferReserve,
    HeapBufferIntoString,
    WindowPush,
    WindowAppend,
    WindowLimit,
    WindowCommit,
    WriterWrite,
    WriterStatus,
    WriteLineUtf8,
    BuiltinFormat,
    BuiltinEquals,
    BuiltinCompare,
    BuiltinArithmetic,
    StorageBorrowShared,
    StorageBorrowExclusive,
    StorageOwn,
    StorageBorrowDictionary,
    StorageBorrowDictionaryExclusive,
    StorageOwnDictionary,
    StorageKeyAt,
    StorageValueAt,
    StorageBorrowFixedShared,
    StorageBorrowFixedExclusive,
    StorageOwnFixed,
    StorageDictionaryLayout,
    StorageMissingDictionaryKey,
    StorageArgumentOutOfRange,
    StoragePlaceDictionaryEntry,
    StorageCountOverflow,
    StorageAllocationSizeExceeded,
    StorageTryAllocateBytes,
    StorageTransferBytes,
    StorageIndexBounds,
    StorageBorrowUniqSlice,
    StorageSetArrayLength,
    RawAllocate,
    RawRelease,
    RawInitialize,
    RawSlice,
}

/// <summary>A compiler intrinsic (plan 5.8): an operation Kimigayo cannot express, which MIR calls as an <c>Intrinsic</c> callee and the
/// backend generates. <see cref="IntrinsicKind"/> is another concept, the compiler-known Contracts.</summary>
internal enum IntrinsicId : byte
{
    None,
    RawAllocate, // Memory (SPEC 5.6, 22.1.2.5).
    RawRelease,
    RawInitialize,
    RawSlice,
    TryAllocateBytes,
    MoveBytes, // A byte copy whose ranges may overlap.
    Stride, // Type facts (SPEC 22.1.2.5, IMPL 21.2.2); destroying the values of a cleanup-free T generates nothing.
    DestroyValues,
    ReinterpretLoan, // shareLoan and lendLoan (SPEC 22.1.2.5).
    Abort, // $abort (SPEC 17.3).
    Fail, // A fixed SPEC 22.5.4 code.
    WriteStdout, // SPEC 22.4, 22.5.
    FormatFloat, // Formatting (utf8-formatting 1): Ryu, the erased entry Text.writer makes, the erased reserve, i128 until Kimigayo formats it.
    ErasedWriter,
    ErasedReserve,
    FormatInteger128,
    MakeObj, // Handles (IMPL 21.2.3, SPEC 13.6); the Weak operations come with P35.
    MakeRc,
    MakeArc,
    Clone,
    TypeTest,
    TestObserve, // Tests (SPEC 22.6.1, testing profile).
    TestMessage,
    TestRequireAbort,
    TestRecordValue,
    TestPhaseEnter,
    TestPhaseLeave,
    TestTempDirectory,
    StaticEnsure, // SPEC 22.2.3.
    [Transitional("R8", Reason = "R8 replaces this temporary intrinsic with Kimigayo representation and library code.")]
    Capacity, // Temporary (owner, 2026-10-11): an Array's or a Dictionary's capacity (SPEC 4.7.4) until R8 makes it a Field read.
    [Transitional("R8", Reason = "R8 replaces this temporary intrinsic with Kimigayo representation and library code.")]
    FormatBuffer, // Temporary (owner, 2026-10-11) until R8 writes the internal adapter in Kimigayo: an owning interpolation's private buffer of
    [Transitional("R8", Reason = "R8 replaces this temporary intrinsic with Kimigayo representation and library code.")]
    FormatHint, // the capacity its instance estimates, the hint before each write and the adapter's end into the string (utf8-formatting 5.1, 5.2).
    [Transitional("R8", Reason = "R8 replaces this temporary intrinsic with Kimigayo representation and library code.")]
    FormatFinish,
}

/// <summary>How an effect summary classifies a compiler function (SPEC 8.4.10.2), the effect column of the intrinsic table.</summary>
internal enum CompilerFunctionEffect : byte
{
    /// <summary>The kind names no compiler function, or one the summary cannot classify.</summary>
    Unclassified,

    /// <summary>The function acts only on its acquired inputs and the allocator, and may Abort.</summary>
    Inputs,

    /// <summary>The function obtains authority from the environment, which confined excludes.</summary>
    Environment,

    /// <summary>The function runs a selected formatting witness, which the summary follows before classifying.</summary>
    Formatting,

    /// <summary>A definition Binding rejected, whose parts are not published (DECISIONS R4-M18): its effects are a dependent of that error.</summary>
    Dependent,
}

/// <summary>How a call of a compiler function is served (plan 5.8).</summary>
internal enum DispositionKind : byte
{
    Body, // A Kimigayo body: the declaration's own, or for a compiler function the body R8 writes, which until then the backend serves from the legacy runtime helpers.
    Intrinsic, // An IntrinsicId; Fail carries its fixed SPEC 22.5.4 code.
    Shim, // A built-in witness (formatting, equality, comparison, arithmetic), whose body the backend generates for its instance.
}

/// <summary>The intrinsic table (plan 5.8): the one disposition of each <see cref="CompilerFunctionKind"/>.</summary>
/// <param name="Kind">How the call is served.</param>
/// <param name="Id">The intrinsic of an Intrinsic disposition.</param>
/// <param name="Code">The SPEC 22.5.4 code of <see cref="IntrinsicId.Fail"/>.</param>
internal readonly record struct IntrinsicDisposition(DispositionKind Kind, IntrinsicId Id = IntrinsicId.None, string? Code = null)
{
    internal static IntrinsicDisposition Of(CompilerFunctionKind kind) => kind switch
    {
        CompilerFunctionKind.None or CompilerFunctionKind.WriteLine or CompilerFunctionKind.WriteLineUtf8 or CompilerFunctionKind.Replace or CompilerFunctionKind.Exchange or
            CompilerFunctionKind.Swap or CompilerFunctionKind.ArrayReserve or CompilerFunctionKind.ArrayAppend or CompilerFunctionKind.ArrayInsert or CompilerFunctionKind.ArrayPop or
            CompilerFunctionKind.ArrayRemove or CompilerFunctionKind.ArrayClear or CompilerFunctionKind.ArrayShrinkToFit or CompilerFunctionKind.ArraySwap or
            CompilerFunctionKind.TextFixed or CompilerFunctionKind.TextHeap or CompilerFunctionKind.TextUtf8 or CompilerFunctionKind.TextValidateUtf8 or CompilerFunctionKind.TextToString or
            CompilerFunctionKind.TextTryFormat or CompilerFunctionKind.TextRelease or CompilerFunctionKind.FixedBufferBytes or CompilerFunctionKind.FixedBufferText or
            CompilerFunctionKind.FixedBufferValidate or CompilerFunctionKind.FixedBufferClear or CompilerFunctionKind.FixedBufferReserve or CompilerFunctionKind.FixedBufferIntoText or
            CompilerFunctionKind.HeapBufferBytes or CompilerFunctionKind.HeapBufferText or CompilerFunctionKind.HeapBufferValidate or CompilerFunctionKind.HeapBufferClear or
            CompilerFunctionKind.HeapBufferReserve or CompilerFunctionKind.HeapBufferIntoString or CompilerFunctionKind.WindowPush or CompilerFunctionKind.WindowAppend or
            CompilerFunctionKind.WindowLimit or CompilerFunctionKind.WindowCommit or CompilerFunctionKind.WriterWrite or CompilerFunctionKind.WriterStatus or
            CompilerFunctionKind.StorageBorrowShared or CompilerFunctionKind.StorageBorrowExclusive or CompilerFunctionKind.StorageOwn or CompilerFunctionKind.StorageBorrowDictionary or
            CompilerFunctionKind.StorageBorrowDictionaryExclusive or CompilerFunctionKind.StorageOwnDictionary or CompilerFunctionKind.StorageKeyAt or CompilerFunctionKind.StorageValueAt or
            CompilerFunctionKind.StorageBorrowFixedShared or CompilerFunctionKind.StorageBorrowFixedExclusive or CompilerFunctionKind.StorageOwnFixed or
            CompilerFunctionKind.StorageDictionaryLayout or CompilerFunctionKind.StoragePlaceDictionaryEntry or CompilerFunctionKind.StorageBorrowUniqSlice or
            CompilerFunctionKind.StorageSetArrayLength => default,
        CompilerFunctionKind.BuiltinFormat or CompilerFunctionKind.BuiltinEquals or CompilerFunctionKind.BuiltinCompare or CompilerFunctionKind.BuiltinArithmetic => new(DispositionKind.Shim),
        CompilerFunctionKind.Abort => Intrinsic(IntrinsicId.Abort),
        CompilerFunctionKind.MakeObj => Intrinsic(IntrinsicId.MakeObj),
        CompilerFunctionKind.MakeRc => Intrinsic(IntrinsicId.MakeRc),
        CompilerFunctionKind.MakeArc => Intrinsic(IntrinsicId.MakeArc),
        CompilerFunctionKind.Clone => Intrinsic(IntrinsicId.Clone),
        CompilerFunctionKind.TestTempDirectory => Intrinsic(IntrinsicId.TestTempDirectory),
        CompilerFunctionKind.TextWriter => Intrinsic(IntrinsicId.ErasedWriter),
        CompilerFunctionKind.StorageTryAllocateBytes => Intrinsic(IntrinsicId.TryAllocateBytes),
        CompilerFunctionKind.StorageTransferBytes => Intrinsic(IntrinsicId.MoveBytes),
        CompilerFunctionKind.RawAllocate => Intrinsic(IntrinsicId.RawAllocate),
        CompilerFunctionKind.RawRelease => Intrinsic(IntrinsicId.RawRelease),
        CompilerFunctionKind.RawInitialize => Intrinsic(IntrinsicId.RawInitialize),
        CompilerFunctionKind.RawSlice => Intrinsic(IntrinsicId.RawSlice),
        CompilerFunctionKind.StorageIndexBounds => Fail("KIMI_E_INDEX_BOUNDS"),
        CompilerFunctionKind.StorageMissingDictionaryKey => Fail("KIMI_E_MISSING_KEY"),
        CompilerFunctionKind.StorageArgumentOutOfRange => Fail("KIMI_E_ARG_RANGE"),
        CompilerFunctionKind.StorageCountOverflow => Fail("KIMI_E_INT_OVERFLOW"),
        CompilerFunctionKind.StorageAllocationSizeExceeded => Fail("KIMI_E_ALLOC_SIZE"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    // SPEC 8.4.10.2: the effect class of each compiler function. The Storage and Raw families act only on their inputs and the allocator
    // (SPEC 5.6, 22.1.2.5), so they are classified as families; a storage kind that obtained environment authority would have to be split
    // out of its family. Console output and the test temporary directory are environment effects. Comparison, formatting and destruction
    // callbacks are summarized separately.
    internal static CompilerFunctionEffect Effect(CompilerFunctionKind kind) => kind switch
    {
        CompilerFunctionKind.WriteLine or CompilerFunctionKind.WriteLineUtf8 or CompilerFunctionKind.TestTempDirectory => CompilerFunctionEffect.Environment,
        CompilerFunctionKind.WriterWrite or CompilerFunctionKind.TextToString or CompilerFunctionKind.TextTryFormat => CompilerFunctionEffect.Formatting,
        >= CompilerFunctionKind.Abort and <= CompilerFunctionKind.Clone or >= CompilerFunctionKind.ArrayReserve and <= CompilerFunctionKind.TextValidateUtf8 =>
            CompilerFunctionEffect.Inputs,
        >= CompilerFunctionKind.TextRelease and <= CompilerFunctionKind.WindowCommit or CompilerFunctionKind.WriterStatus or
            >= CompilerFunctionKind.BuiltinFormat and <= CompilerFunctionKind.BuiltinArithmetic => CompilerFunctionEffect.Inputs,
        _ when KimiLibraryCatalog.IsStorageOperation(kind) || KimiLibraryCatalog.IsRawOperation(kind) => CompilerFunctionEffect.Inputs,
        _ => CompilerFunctionEffect.Unclassified,
    };

    // The built-in form of a published node: a sequence's metadata by its member.
    internal static BuiltinForm FormOf(Koto node)
        => HirTables.Op(node) == HirOp.PrimitiveOp && node is MemberAccessKoto member ? Binding.SequenceMember(member) : BuiltinForm.None;

    private static IntrinsicDisposition Intrinsic(IntrinsicId id) => new(DispositionKind.Intrinsic, id);

    private static IntrinsicDisposition Fail(string code) => new(DispositionKind.Intrinsic, IntrinsicId.Fail, code);
}

/// <summary>A sequence's metadata member, which Binding serves without a call: <see cref="IntrinsicDisposition.FormOf"/> gives a node's.</summary>
internal enum BuiltinForm : byte
{
    None,
    Length, // A sequence's length (SPEC 4.6.1).
    IsEmpty,
    Indices, // The ResolvedRange from 0 to the length (SPEC 4.6.3).
    Capacity, // An Array's or a Dictionary's capacity (SPEC 4.7.4).
}
