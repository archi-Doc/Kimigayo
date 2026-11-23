// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the runtime ABI of the executable target profiles (SPEC 21.3.1, 22.1-22.7; DECISIONS R5-M15): the representations of Unit,
// string values and string handles, the string release kinds, the zero-sized buffer address, and the FunctionAbi of each
// runtime routine that generated code defines or calls, the platform layer's routines (PlatformRoutines, which the common runtime and
// the allocator call and every layer template defines; the Tatara plan's decision 22) and a layer's own (PlatformStart, Clock; B7b2a,
// N6), one RuntimeLayer record per platform layer (windows-x64's over kernel32, linux-x64's over system calls: its template's resource
// name, which a target row's Layer column names; its OS-error prefix; its definition list, the routines whose template definitions are
// spliced from them; its start form, the start routine, the module asm defining the entry and the layer's routines called before the
// startup root; whether test builds run on it), and
// the text of its constants' markers in a runtime template, which LlvmModuleText's one scan writes: the target-free ones (the
// zero-sized buffer address at `{{zero_sized_buffer}}`, Abort's os_error without an OS error at `{{no_os_error}}`, the release kinds at
// `{{static_release_kind}}` and `{{heap_release_kind}}`) and a row's (RowConstants: the row's unmapped bound at `{{unmapped_bound}}`,
// and its layer's OS-error prefix at `{{os_prefix}}` with its length at `{{os_prefix_length}}`), which only the row's runtime expansion
// is given.
// Input: a target row (its Layer and UnmappedBound); the Windows and Linux rows' entry symbols; a marker's name, the row's constants and
// the expanded text being written.
// Output: the records, read by code generation and by LlvmModuleText's frame and runtime splice; a row's layer and constants; a
// constant marker's text appended.
// Invariants: each runtime routine has exactly one FunctionAbi, shared by its definition and every call (identity by reference);
// layers' template names distinct; a layer's definition list lists each routine whose `{{name}}` marker its runtime templates (the
// layer, the common part and the allocator) hold exactly once; a template spells a marker's constant only through its marker, and a
// writer through the constant.
// Must not take BoundType, syntax or MIR types, or hold a per-module record.

using System.Globalization;
using System.Text;

namespace Kimi.Compiler.Backend;

/// <summary>The runtime representations, constants and routine signatures shared by generated code and the runtime template.</summary>
internal static class RuntimeAbi
{
    /// <summary>The Static string releaseKind: compiler constant backing that is never freed (the string representation, IMPL 21.1.3).</summary>
    internal const int StaticReleaseKind = 0;

    /// <summary>The Heap string releaseKind: bytes of a heap block (Kimi's allocator's), which the string's destruction frees. A buffer's string
    /// takes its kind as the library's HeapBuffer.intoString writes it, 1 for an allocation and 0 for none, so Heap is 1 and Static 0
    /// (Kernel32ImportsTest.TheRuntimeTemplatesUnmarkedFactsHold).</summary>
    internal const int HeapReleaseKind = 1;

    /// <summary>The buffer address of an Array with zero-sized elements (SPEC 4.5): nonnull and page-aligned, so aligned for every
    /// element, and below every row's TargetProfile.UnmappedBound, so it is never allocated or released (__kimi_free skips it).</summary>
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
    internal static readonly FunctionAbi Start = new(TargetProfile.Windows.EntrySymbol, Unit.ComputationType, []) { NoReturn = true };
    internal static readonly FunctionAbi Exit = new("__kimi_exit", Unit.ComputationType, [new("i32", "code")]) { NoReturn = true };

    internal static readonly FunctionAbi Abort = new(
        "__kimi_abort",
        Unit.ComputationType,
        [new("i32", "reason"), Location, LocationLength, new("i64", "os_error")]) { NoReturn = true };

    // Hidden diagnostic context follows the ordinary parameters (SPEC 21.3.1, 22.5.1).
    internal static readonly AbiParameter[] OwnedStringParameters =
        [new(String.ArgumentType!, "text", AbiParameterKind.OwnedSlot, 0), Location, LocationLength];

    // SPEC 22.7, 22.5.1: Console.writeStdout, the WriteStdout intrinsic's routine: the bytes to standard output, KIMI_E_STDOUT at the
    // call's location (a caller-location body's received one).
    internal static readonly FunctionAbi Stdout = new(
        "__kimi_stdout",
        Unit.ComputationType,
        [new("ptr", "data", LogicalIndex: 0), new("i64", "length", LogicalIndex: 1), Location, LocationLength]);

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

    // SPEC 22.9.4, 22.10.1: a user Writer's reservation through an adapter's dispatch word, its window checked (written 0, remaining at
    // least the minimum; tag 1 on a violation): the ErasedReserve intrinsic's routine, defined by the kept ErasedWriterRuntime.ll.in.
    internal static readonly FunctionAbi ErasedReserve = new(
        "__kimi_erased_reserve",
        "void",
        [new("ptr", "ret", AbiParameterKind.ResultSlot), new("ptr", "writer", AbiParameterKind.Value, 0), new("i64", "amount", AbiParameterKind.Value, 1)])
    {
        ResultSlot = true,
    };

    // SPEC 22.10.2: a float's shortest round-trip text into a 24-byte destination, its length returned: the FormatFloat intrinsic's
    // routine (Storage.formatFloat, R8-S2c), defined by the float formatting part (LlvmModuleText.FormatFloat).
    internal static readonly FunctionAbi FormatFloat = new(
        "__kimi_format_float",
        "i64",
        [new("i64", "bits", AbiParameterKind.Value, 0), new("i1", "narrow", AbiParameterKind.Value, 1), new("ptr", "destination", AbiParameterKind.Value, 2)]);

    // SPEC 22.10.2: a u128's ASCII decimal digits at the end of a 40-byte destination, their count returned: the FormatInteger128
    // intrinsic's routine (Storage.formatInteger128, R8-S2b2), defined by its module part (LlvmModuleText.FormatInteger128).
    internal static readonly FunctionAbi FormatInteger128 = new(
        "__kimi_format_integer128",
        "i64",
        [new("i128", "value", AbiParameterKind.Value, 0), new("ptr", "destination", AbiParameterKind.Value, 1)]);

    internal static readonly FunctionAbi AbortMessage = new("__kimi_abort_message", Unit.ComputationType, OwnedStringParameters) { NoReturn = true };

    // The platform layer's write (IMPL 21.4.2): a nonempty checked range to standard output (stream 1) or error (2); -1 when written,
    // else the OS error or NoOsError.
    internal static readonly FunctionAbi Write = new("__kimi_write", "i64", [new("i32", "stream"), new("ptr", "data"), new("i64", "length")]);

    // The layer's pages (IMPL 21.4.3; the Tatara plan's decision 23): a page-aligned run of at least a byte count, readable and writable,
    // at or above the unmapped bound, null on failure; a run's release, by its start and byte count, -1 when released, else the OS error.
    internal static readonly FunctionAbi Pages = new("__kimi_pages", "ptr", [new("i64", "bytes")]);

    internal static readonly FunctionAbi ReleasePages = new("__kimi_release_pages", "i64", [new("ptr", "pages"), new("i64", "bytes")]);

    // Kimi's allocator (IMPL 21.4.3; the Tatara plan's decision 23; HeapRuntime.ll.in, common to every target) over the layer's pages: a
    // 16-aligned block of a byte size at most isize's maximum, null without pages; a block's release, -1 when released, else the OS error.
    internal static readonly FunctionAbi HeapAllocate = new("__kimi_heap_allocate", "ptr", [new("i64", "bytes")]);

    internal static readonly FunctionAbi HeapRelease = new("__kimi_heap_release", "i64", [new("ptr", "block")]);

    // Routines of a row's own layer, outside PlatformRoutines (the Tatara plan's B7b2a, N6): the process's setup before the program's
    // start (linux-x64: SIGPIPE ignored, so a write to a closed pipe fails with its errno); the monotonic clock in nanoseconds, or -1
    // (C1; the Kimi library's MonotonicClock intrinsic, declared only where `not windows` holds); and the body of a start whose entry the
    // layer's module asm defines (linux-x64's libc-free __kimi_start), internal.
    internal static readonly FunctionAbi PlatformStart = new("__kimi_platform_start", Unit.ComputationType, []);

    internal static readonly FunctionAbi Clock = new("__kimi_clock", "i64", []);

    internal static readonly FunctionAbi StartBody = new("__kimi_start_body", Unit.ComputationType, []) { NoReturn = true };

    /// <summary>Gets the platform layer's routines, which the common runtime and the allocator call and only the layer template defines;
    /// every kernel32 call of the runtime in a program module lies in their definitions, each routine's own calls pinned by
    /// NativeAllocationAudit.AssertOsCallsInLayer (the test runtime's calls and a program's own imports are outside the layer).</summary>
    internal static readonly FunctionAbi[] PlatformRoutines = [Exit, Write, Pages, ReleasePages];

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

    // The routines every layer's runtime defines after the layer's own: the allocator's, then the common part's compiler-facing ones.
    private static readonly FunctionAbi[] CommonDefinitions = [HeapAllocate, HeapRelease, DestroyString, Abort, AbortMessage];

    // linux-x64's libc-free start (IMPL 21.5.3): the module asm defining the entry, which clears the frame pointer, aligns the stack to
    // 16 bytes and calls the start body, never returning; @llvm.used keeps the internal body, whose only caller the asm is.
    private static readonly string LinuxStart =
        "module asm \".text\"\nmodule asm \".globl " + TargetProfile.Linux.EntrySymbol + "\"\nmodule asm \".type " + TargetProfile.Linux.EntrySymbol +
        ", @function\"\nmodule asm \"" + TargetProfile.Linux.EntrySymbol + ":\"\nmodule asm \"  xorl %ebp, %ebp\"\nmodule asm \"  andq $-16, %rsp\"\n" +
        "module asm \"  callq " + StartBody.Name + "\"\nmodule asm \"  ud2\"\n@llvm.used = appending global [1 x ptr] [ptr @" + StartBody.Name +
        "], section \"llvm.metadata\"\n";

    // The target-free constants a runtime template spells through a marker, by the marker's name (Expand), each as its text.
    private static readonly (string Name, string Text)[] Constants =
    [
        ("zero_sized_buffer", Number(ZeroSizedBuffer)), ("no_os_error", Number(NoOsError)), ("static_release_kind", Number(StaticReleaseKind)),
        ("heap_release_kind", Number(HeapReleaseKind)),
    ];

    /// <summary>Gets the platform layers' records, one per layer template, which the target rows' Layer columns name (the Tatara plan's
    /// B7b2a), each runtime the layer, the common part (Runtime.ll.in) and the allocator (HeapRuntime.ll.in): windows-x64's
    /// (WindowsPlatform.ll.in, over kernel32), whose start is the exported entry, with test builds; linux-x64's (LinuxPlatform.ll.in, over
    /// system calls), whose module asm starts the internal start body after the process's setup, without test builds. A property after
    /// the fields it reads, since static initializers run in text order.</summary>
    internal static RuntimeLayer[] Layers { get; } =
    [
        new("WindowsPlatform.ll.in", " (win32=", [.. PlatformRoutines, .. CommonDefinitions], Start, null, [], true),
        new(
            "LinuxPlatform.ll.in", " (errno=", [.. PlatformRoutines, PlatformStart, Clock, .. CommonDefinitions], StartBody, LinuxStart, [PlatformStart], false),
    ];

    /// <summary>Gets the hidden diagnostic location parameter (SPEC 22.5.1); a property, so the field initializers above may read it.</summary>
    internal static AbiParameter Location => new("ptr", "location", AbiParameterKind.Location);

    /// <summary>Gets the byte length parameter of the hidden diagnostic location.</summary>
    internal static AbiParameter LocationLength => new("i64", "location_length", AbiParameterKind.LocationLength);

    /// <summary>Gets the record of a row's platform layer, the one its Layer column names; a row naming none is a broken contract.</summary>
    /// <param name="row">The target row.</param>
    /// <returns>The layer's record.</returns>
    internal static RuntimeLayer Layer(TargetProfile row)
    {
        foreach (var layer in Layers)
        {
            if (layer.Template == row.Layer)
            {
                return layer;
            }
        }

        throw new InvalidOperationException("The target row's Layer must name a runtime layer.");
    }

    /// <summary>Gets a row's runtime constants by marker name, the data of the row's runtime expansion: the row's unmapped bound and its
    /// layer's OS-error prefix with its length.</summary>
    /// <param name="row">The target row.</param>
    /// <returns>Each constant's marker name and text.</returns>
    internal static (string Name, string Text)[] RowConstants(TargetProfile row)
    {
        var prefix = Layer(row).OsErrorPrefix;
        return [("unmapped_bound", Number(row.UnmappedBound)), ("os_prefix", prefix), ("os_prefix_length", Number(prefix.Length))];
    }

    // Appends the text of a runtime template's constant marker, a target-free one or one of the row's constants the expansion is given
    // (none for a target-free template); false for any other marker.
    internal static bool Expand(ReadOnlySpan<char> marker, ReadOnlySpan<(string Name, string Text)> rowConstants, StringBuilder runtime)
        => Append(marker, Constants, runtime) || Append(marker, rowConstants, runtime);

    private static bool Append(ReadOnlySpan<char> marker, ReadOnlySpan<(string Name, string Text)> constants, StringBuilder runtime)
    {
        foreach (var (name, text) in constants)
        {
            if (marker.SequenceEqual(name))
            {
                runtime.Append(text);
                return true;
            }
        }

        return false;
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>A platform layer's runtime record (the Tatara plan's B7b2a): the layer a target row's Layer column names.</summary>
/// <param name="Template">The layer template's resource name, the row's Layer.</param>
/// <param name="OsErrorPrefix">The Abort report's OS-error prefix (IMPL 21.5.4): the layer template defines <c>@__kimi_os_prefix</c> from
/// it and the common runtime writes its length from it, so a layer's prefix of another length changes no common rule (the Tatara plan's
/// B7b revision 2.2). A <c>c"..."</c> literal spells it as is.</param>
/// <param name="Definitions">The routines whose definitions the row's runtime splices at their markers, each exactly once: the layer's
/// routines (every layer's PlatformRoutines, then its own), the allocator's, then the common part's compiler-facing ones.</param>
/// <param name="Start">The routine an Application's start defines (IMPL 21.4.2): it calls <paramref name="StartCalls"/>, the startup
/// root, then exits with 0. Exported when it is the row's entry symbol, internal when <paramref name="StartAssembly"/> defines the entry
/// and calls it.</param>
/// <param name="StartAssembly">The module text that defines the entry and calls <paramref name="Start"/>, written before it; null when
/// <paramref name="Start"/> is the entry.</param>
/// <param name="StartCalls">The layer's routines the start calls before the startup root, each a Unit routine without parameters.</param>
/// <param name="Tests">Whether a test build runs on the layer: the test runtime (Kimi.Testing's TestRuntime.ll) calls kernel32 and its
/// entry defines the exported entry, so only windows-x64's layer has a test variant.</param>
internal sealed record RuntimeLayer(
    string Template, string OsErrorPrefix, FunctionAbi[] Definitions, FunctionAbi Start, string? StartAssembly, FunctionAbi[] StartCalls, bool Tests);
