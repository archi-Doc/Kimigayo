// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns compiler/library identities, the disposition of each compiler operation and the intrinsic Contract table.
// Input: CompilerFunctionKind; IntrinsicKind. Output: a Kimigayo implementation, intrinsic or shim disposition; an intrinsic Contract's row.
// Each operation has one disposition and each intrinsic Contract one row; temporary intrinsics keep their recorded removal stage.
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

// The intrinsic Contract table (SPEC 8.7.1; DECISIONS R4-M89, package 2b-D §3.4): how Binding decides each compiler-intrinsic Contract.
// Input: an IntrinsicKind. Output: its row, which ProveConstraint, a Contract's Self clause, a struct's or enum's Self clause (plain
// or conditional) and conformance verification read.
// Invariants: one row per IntrinsicKind, None (a declared Contract) included; a new intrinsic adds a row, never a case at a reader.
// Must not: judge a capability (the capability engine's) or decide a conformance's members (contract matching's).

/// <summary>A row of the intrinsic Contract table.</summary>
internal readonly record struct IntrinsicContract
{
    /// <summary>Gets a value indicating whether the capability engine judges <c>T is K</c> (SPEC 8.7.2–8.7.4, 15.2.3).</summary>
    internal bool Judged { get; init; }

    /// <summary>Gets the failure of <c>Self is K</c> in a struct or enum declaration; None when it declares a conformance (SPEC 8.7.1).</summary>
    internal BindingFailure Declaration { get; init; }

    /// <summary>Gets a value indicating whether a Contract's <c>Self is K</c> is a requirement and a premise (SPEC 8.7.1, 8.7.3).</summary>
    internal bool SelfRequirement { get; init; }

    /// <summary>Gets a value indicating whether a conformance to K, declared or an ancestor's, is decided by the judgment, not by matching.</summary>
    internal bool ConformanceJudged { get; init; }

    internal static IntrinsicContract Of(IntrinsicKind kind) => kind switch
    {
        IntrinsicKind.None => default,
        IntrinsicKind.Copy => new() { Judged = true, SelfRequirement = true, ConformanceJudged = true }, // Derived Copy (SPEC 3.5.2).
        IntrinsicKind.Owned => new() { Judged = true, SelfRequirement = true, ConformanceJudged = true }, // A checked assertion.
        IntrinsicKind.Callable => new() { Declaration = BindingFailure.InvalidConstraint }, // Bound as a Callable Constraint, never a Contract one.
        IntrinsicKind.Sealed => new() { Judged = true, Declaration = BindingFailure.InvalidConstraint, ConformanceJudged = true },
        IntrinsicKind.ObjectPayload => new() { Judged = true, Declaration = BindingFailure.InvalidSelfClause, SelfRequirement = true }, // Never granted.
        IntrinsicKind.PrimitiveInteger => new() { Judged = true, Declaration = BindingFailure.ClosedContractConformance }, // Twelve integer Types (8.7.4).
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
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
    RawAllocate, // Memory (SPEC 5.6, 22.2).
    RawRelease,
    RawInitialize,
    RawSlice,
    TryAllocateBytes,
    MoveBytes, // A byte copy whose ranges do not overlap (transferBytes' contract; DECISIONS R5-M18).
    Stride, // Type facts (SPEC 22.2, IMPL 21.1.3); destroying the values of a cleanup-free T generates nothing.
    DestroyValues,
    ReinterpretLoan, // shareLoan and lendLoan (SPEC 22.2).
    Abort, // $abort (SPEC 17.4).
    Fail, // A fixed SPEC 22.5.2 code.
    WriteStdout, // SPEC 22.1, 22.5.
    FormatFloat, // Formatting (SPEC 22.9.1): Ryu, the erased entry Text.writer makes, the erased reserve, i128 until Kimigayo formats it.
    ErasedWriter,
    ErasedReserve,
    FormatInteger128,
    MakeObj, // Handles (IMPL 21.1.3, SPEC 13.6); the Weak operations come with P41.
    MakeRc,
    MakeArc,
    Clone,
    TypeTest,
    TestObserve, // Tests (SPEC 20.6.1, testing profile).
    TestMessage,
    TestRequireAbort,
    TestRecordValue,
    TestTempDirectory,
    StaticEnsure, // SPEC 22.4.3.
    [Transitional("R8", Reason = "R8 replaces this temporary intrinsic with Kimigayo representation and library code.")]
    Capacity, // Temporary (owner, 2026-10-11): an Array's or a Dictionary's capacity (SPEC 4.9.5) until R8 makes it a Field read.
    [Transitional("R8", Reason = "R8 replaces this temporary intrinsic with Kimigayo representation and library code.")]
    FormatBuffer, // Temporary (owner, 2026-10-11) until R8 writes the internal adapter in Kimigayo: the owning interpolation's private buffer of
    // the capacity its instance estimates.
    [Transitional("R8", Reason = "R8 replaces this temporary intrinsic with Kimigayo representation and library code.")]
    FormatSegment, // One step per write position, the hint of the later literal bytes and values, then the literal segment's text ("" at a
    // value's; R5-M133).
    [Transitional("R8", Reason = "R8 replaces this temporary intrinsic with Kimigayo representation and library code.")]
    FormatFinish, // The adapter's end into the string (SPEC 22.10.5, 22.10.3).
}

/// <summary>How an effect summary classifies a compiler function (SPEC 8.11.2), the effect column of the intrinsic table.</summary>
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
    Intrinsic, // An IntrinsicId; Fail carries its fixed SPEC 22.5.2 code.
    Shim, // A built-in witness (formatting, equality, comparison, arithmetic), whose body the backend generates for its instance.
}

/// <summary>The intrinsic table (plan 5.8): the one disposition of each <see cref="CompilerFunctionKind"/>.</summary>
/// <param name="Kind">How the call is served.</param>
/// <param name="Id">The intrinsic of an Intrinsic disposition.</param>
/// <param name="Code">The SPEC 22.5.2 code of <see cref="IntrinsicId.Fail"/>.</param>
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

    // SPEC 8.11.2: the effect class of each compiler function. The Storage and Raw families act only on their inputs and the allocator
    // (SPEC 5.6, 22.2), so they are classified as families; a storage kind that obtained environment authority would have to be split
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
    Length, // A sequence's length (SPEC 4.7).
    IsEmpty,
    Indices, // The ResolvedRange from 0 to the length (SPEC 4.6.2).
    Capacity, // An Array's or a Dictionary's capacity (SPEC 4.9.5).
}
