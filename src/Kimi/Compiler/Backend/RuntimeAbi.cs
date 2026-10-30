// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the runtime ABI of the windows-x64-v1 profile (SPEC 21.3.1, 22.1-22.7; DECISIONS R5-M15): the representations of Unit,
// string values and string handles, the static string release kind, the zero-sized buffer address, and the FunctionAbi of each
// runtime routine that generated code defines or calls, with the routines whose template definitions are spliced from them.
// Input: none; the entry symbol comes from WindowsProfile.
// Output: the records, read by both generators and by LlvmModuleText's runtime splice.
// Invariants: each runtime routine has exactly one FunctionAbi, shared by its definition and every call (identity by reference);
// RuntimeDefinitions lists each routine whose `{{name}}` marker the runtime template holds exactly once.
// Must not take BoundType, syntax or MIR types, or hold a per-module record.

namespace Kimi.Compiler.Backend;

/// <summary>The runtime representations, constants and routine signatures shared by generated code and the runtime template.</summary>
internal static class RuntimeAbi
{
    /// <summary>The Static string releaseKind: compiler constant backing that is never freed (the string representation, IMPL 21.1.3).</summary>
    internal const int StaticReleaseKind = 0;

    /// <summary>The buffer address of an Array with zero-sized elements (SPEC 4.5): nonnull and page-aligned, so aligned for every
    /// element, and below the 64 KiB that Windows never maps, so it is never allocated or released (__kimi_free skips it).</summary>
    internal const int ZeroSizedBuffer = 4096;

    /// <summary>The runtime Abort's os_error for a failure without an operating system error, which the runtime does not print (it prints
    /// only an os_error of 0 or more).</summary>
    internal const long NoOsError = -2;

    internal static readonly ValueLowering Unit = new(new("void", 0, 1, 0, ReadOnlyMemory<int>.Empty), "void", null);

    // { data, byteLength, releaseKind }: size/stride 24, alignment 8, offsets 0/8/16 (implementation-defined, IMPL 21.1.3).
    internal static readonly ValueLowering String = new(new("%kimi.string", 24, 8, 24, new[] { 0, 8, 16 }), "%kimi.string", "ptr");

    internal static readonly ValueLowering StringReference = new(new("ptr", 8, 8, 8, ReadOnlyMemory<int>.Empty), "ptr", "ptr");

    internal static readonly FunctionAbi Entry = new("__kimi_entry_body", Unit.ComputationType, []);
    internal static readonly FunctionAbi Start = new(WindowsProfile.EntrySymbol, Unit.ComputationType, []) { NoReturn = true };
    internal static readonly FunctionAbi Exit = new("__kimi_exit", Unit.ComputationType, [new("i32", "code")]) { NoReturn = true };

    internal static readonly FunctionAbi Abort = new(
        "__kimi_abort",
        Unit.ComputationType,
        [new("i32", "reason"), Location, LocationLength, new("i64", "os_error")]) { NoReturn = true };

    // Hidden diagnostic context follows the ordinary parameters (SPEC 21.3.1, 22.5.1).
    internal static readonly AbiParameter[] OwnedStringParameters =
        [new(String.ArgumentType!, "text", AbiParameterKind.OwnedSlot, 0), Location, LocationLength];

    // SPEC 22.1-22.5.5: writeLine borrows its string handle and releases nothing.
    internal static readonly FunctionAbi WriteLine = new(
        "__kimi_write_line",
        Unit.ComputationType,
        [new(StringReference.ArgumentType!, "text", AbiParameterKind.SharedReference, 0), Location, LocationLength]);

    internal static readonly FunctionAbi DestroyString = new("__kimi_destroy_string", Unit.ComputationType, OwnedStringParameters);

    internal static readonly FunctionAbi CloneRc = new(
        "__kimi_clone_rc",
        "void",
        [
            new("ptr", "ret", AbiParameterKind.ResultSlot),
            new("ptr", "value", AbiParameterKind.Value, 0),
            new("ptr", "location", AbiParameterKind.Location),
            new("i64", "length", AbiParameterKind.LocationLength),
        ]) { ResultSlot = true };

    internal static readonly FunctionAbi CloneArc = new("__kimi_clone_arc", "void", CloneRc.Parameters) { ResultSlot = true };

    // SPEC 4.9.5: an Array handle is {buffer, length, capacity}; construction zeroes it and destruction releases its buffer.
    internal static readonly AbiParameter[] ArrayHandleParameters = [new("ptr", "handle", AbiParameterKind.OwnedSlot, 0), Location, LocationLength];
    internal static readonly FunctionAbi ArrayInit = new("__kimi_array_init", Unit.ComputationType, ArrayHandleParameters);
    internal static readonly FunctionAbi DictionaryInit = new("__kimi_dictionary_init", Unit.ComputationType, ArrayHandleParameters);
    internal static readonly FunctionAbi ArrayFree = new("__kimi_array_free", Unit.ComputationType, ArrayHandleParameters);

    // IMPL 21.4.3: a heap block of a byte size, 16-aligned; an allocation failure Aborts at the location (an erased environment, SPEC 7.7.4).
    internal static readonly FunctionAbi Allocate =
        new("__kimi_alloc", "ptr", [new("i64", "size", AbiParameterKind.Value, 0), Location, LocationLength]);

    internal static readonly FunctionAbi RawAllocate =
        new("__kimi_raw_allocate", "ptr", [new("i64", "count", AbiParameterKind.Value, 0), new("i64", "stride"), Location, LocationLength]);

    internal static readonly FunctionAbi StorageRelease =
        new("__kimi_free", Unit.ComputationType, [new("ptr", "storage", AbiParameterKind.Value, 0), Location, LocationLength]);

    // SPEC 4.9.5, 4.9.8: capacity routines move element bytes by stride and run no user code; the writer emits them only for
    // modules that use Arrays. A call's handle and count are at their slots, its stride given (R5 plan C8d).
    internal static readonly FunctionAbi ArrayGrow =
        new("__kimi_array_grow", Unit.ComputationType, [new("ptr", "handle"), new("i64", "stride"), new("i64", "minimum"), Location, LocationLength]);

    internal static readonly FunctionAbi ArrayReserve = new(
        "__kimi_array_reserve",
        Unit.ComputationType,
        [new("ptr", "handle", AbiParameterKind.Value, 0), new("i64", "stride"), new("i64", "additional", AbiParameterKind.Value, 1), Location, LocationLength]);

    internal static readonly FunctionAbi ArrayShrink = new(
        "__kimi_array_shrink",
        Unit.ComputationType,
        [new("ptr", "handle", AbiParameterKind.Value, 0), new("i64", "stride"), Location, LocationLength]);

    // SPEC 22.2: the private nullable Alloc and byte copy behind Kimi.Storage, defined only in modules whose bodies call them
    // (LlvmModuleText.TryAllocate and LlvmModuleText.TransferBytes, written as a pair by both generators; DECISIONS R5-M92, R5-M145).
    internal static readonly FunctionAbi TryAllocateBytes = new("__kimi_try_allocate", "ptr", [new("i64", "bytes", AbiParameterKind.Value, 0)]);

    internal static readonly FunctionAbi TransferBytes = new(
        "__kimi_transfer_bytes",
        Unit.ComputationType,
        [new("ptr", "destination", AbiParameterKind.Value, 0), new("ptr", "source", AbiParameterKind.Value, 1), new("i64", "size", AbiParameterKind.Value, 2)]);

    internal static readonly FunctionAbi AbortMessage = new("__kimi_abort_message", Unit.ComputationType, OwnedStringParameters) { NoReturn = true };

    // The test runtime's routines (Kimi.Testing's TestRuntime.ll, SPEC 17.6): a verification's observation, which numbers a failure's issue
    // from its condition and the site's number in the test catalog and sends the values held for it; the issue's message, a borrowed string;
    // a $require's Abort of the case; a compared value held until the observation, by its side, its frame kind and width and its bits.
    internal static readonly FunctionAbi TestTempDirectory =
        new("__kimi_test_temp", "void", [new("ptr", "result", AbiParameterKind.ResultSlot)]) { ResultSlot = true };

    internal static readonly FunctionAbi TestObserve =
        new("__kimi_test_observe", "i64", [new("i1", "condition", AbiParameterKind.Value, 0), new("i32", "site", AbiParameterKind.Value, 1)]);

    internal static readonly FunctionAbi TestMessage =
        new("__kimi_test_message", "void", [new("i64", "issue", AbiParameterKind.Value, 0), new("ptr", "text", AbiParameterKind.Value, 1)]);

    internal static readonly FunctionAbi TestRequireAbort = new(
        "__kimi_test_require_abort",
        "void",
        [new("i64", "issue", AbiParameterKind.Value, 0), new("i32", "site", AbiParameterKind.Value, 1)]) { NoReturn = true };

    internal static readonly FunctionAbi TestValue = new(
        "__kimi_test_value",
        "void",
        [new("i32", "which", AbiParameterKind.Value, 0), new("i16", "kind"), new("i16", "width"), new("i128", "bits")]);

    /// <summary>Gets the compiler-facing runtime definitions expanded into WindowsRuntime.ll.in.</summary>
    internal static readonly FunctionAbi[] RuntimeDefinitions = [Exit, DestroyString, WriteLine, Abort, AbortMessage, ArrayInit, ArrayFree];

    /// <summary>Gets the hidden diagnostic location parameter (SPEC 22.5.1); a property, so the field initializers above may read it.</summary>
    internal static AbiParameter Location => new("ptr", "location", AbiParameterKind.Location);

    /// <summary>Gets the byte length parameter of the hidden diagnostic location.</summary>
    internal static AbiParameter LocationLength => new("i64", "location_length", AbiParameterKind.LocationLength);
}
