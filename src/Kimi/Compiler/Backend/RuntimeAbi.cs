// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the runtime ABI of the windows-x64-v1 profile (SPEC 21.3.1, 22.1-22.7; DECISIONS R5-M15): the representations of Unit,
// string values and string handles, the string release kinds, the zero-sized buffer address, and the FunctionAbi of each
// runtime routine that generated code defines or calls, with the routines whose template definitions are spliced from them, and the
// text of its constants' markers in a runtime template, which LlvmModuleText's one scan writes (the zero-sized buffer address at
// `{{zero_sized_buffer}}`, Abort's os_error without an OS error at `{{no_os_error}}`, the release kinds at `{{static_release_kind}}`
// and `{{heap_release_kind}}`, and WindowsProfile's unmapped bound at `{{unmapped_bound}}`).
// Input: none; the entry symbol and the unmapped bound come from WindowsProfile; a marker's name and the expanded text being written.
// Output: the records, read by code generation and by LlvmModuleText's runtime splice; a constant marker's text appended.
// Invariants: each runtime routine has exactly one FunctionAbi, shared by its definition and every call (identity by reference);
// RuntimeDefinitions lists each routine whose `{{name}}` marker the runtime template holds exactly once; a template spells a
// marker's constant only through its marker, and a writer through the constant.
// Must not take BoundType, syntax or MIR types, or hold a per-module record.

using System.Globalization;
using System.Text;

namespace Kimi.Compiler.Backend;

/// <summary>The runtime representations, constants and routine signatures shared by generated code and the runtime template.</summary>
internal static class RuntimeAbi
{
    /// <summary>The Static string releaseKind: compiler constant backing that is never freed (the string representation, IMPL 21.1.3).</summary>
    internal const int StaticReleaseKind = 0;

    /// <summary>The Heap string releaseKind: bytes of a process heap block, which the string's destruction frees. A buffer's string
    /// takes its kind as `zext i1 %heap to i8` (the formatting runtime's __kimi_buffer_into_string and __kimi_format_take_string), so
    /// Heap is 1 and Static 0 (Kernel32ImportsTest.TheRuntimeTemplatesUnmarkedFactsHold).</summary>
    internal const int HeapReleaseKind = 1;

    /// <summary>The buffer address of an Array with zero-sized elements (SPEC 4.5): nonnull and page-aligned, so aligned for every
    /// element, and below WindowsProfile.UnmappedBound, so it is never allocated or released (__kimi_free skips it).</summary>
    internal const int ZeroSizedBuffer = 4096;

    /// <summary>The runtime Abort's os_error for a failure without an operating system error, which the runtime does not print (it prints
    /// only an os_error of 0 or more).</summary>
    internal const long NoOsError = -2;

    internal static readonly ValueLowering Unit = new(new("void", 0, 1, 0, ReadOnlyMemory<int>.Empty), "void", null);

    // { data, length, releaseKind }: size/stride 24, alignment 8, offsets 0/8/16 (implementation-defined, IMPL 21.1.3). A lasting pair with the
    // library's Storage.StringRepr, since hand-written runtime IR needs these words; LayoutTest and KimiLibraryShapeTest pin the two.
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
    internal static readonly FunctionAbi ArrayFree = new("__kimi_array_free", Unit.ComputationType, ArrayHandleParameters);

    // IMPL 21.4.3: a heap block of a byte size, 16-aligned; an allocation failure Aborts at the location (an erased environment, SPEC 7.7.4).
    internal static readonly FunctionAbi Allocate =
        new("__kimi_alloc", "ptr", [new("i64", "size", AbiParameterKind.Value, 0), Location, LocationLength]);

    internal static readonly FunctionAbi RawAllocate =
        new("__kimi_raw_allocate", "ptr", [new("i64", "count", AbiParameterKind.Value, 0), new("i64", "stride"), Location, LocationLength]);

    internal static readonly FunctionAbi StorageRelease =
        new("__kimi_free", Unit.ComputationType, [new("ptr", "storage", AbiParameterKind.Value, 0), Location, LocationLength]);

    // SPEC 22.2: the private nullable Alloc and byte copy behind Kimi.Storage, defined only in modules whose bodies call them
    // (LlvmModuleText.TryAllocate and LlvmModuleText.TransferBytes, written as a pair; DECISIONS R5-M92, R5-M145).
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
    internal static readonly FunctionAbi[] RuntimeDefinitions = [Exit, DestroyString, WriteLine, Abort, AbortMessage, ArrayFree];

    // The constants a runtime template spells through a marker, by the marker's name (Expand).
    private static readonly (string Name, long Value)[] Constants =
    [
        ("zero_sized_buffer", ZeroSizedBuffer), ("no_os_error", NoOsError), ("static_release_kind", StaticReleaseKind),
        ("heap_release_kind", HeapReleaseKind), ("unmapped_bound", WindowsProfile.UnmappedBound),
    ];

    /// <summary>Gets the hidden diagnostic location parameter (SPEC 22.5.1); a property, so the field initializers above may read it.</summary>
    internal static AbiParameter Location => new("ptr", "location", AbiParameterKind.Location);

    /// <summary>Gets the byte length parameter of the hidden diagnostic location.</summary>
    internal static AbiParameter LocationLength => new("i64", "location_length", AbiParameterKind.LocationLength);

    // Appends the text of a runtime template's ABI constant marker, the constant's number; false for any other marker.
    internal static bool Expand(ReadOnlySpan<char> marker, StringBuilder runtime)
    {
        foreach (var (name, value) in Constants)
        {
            if (marker.SequenceEqual(name))
            {
                runtime.Append(value.ToString(CultureInfo.InvariantCulture));
                return true;
            }
        }

        return false;
    }
}
