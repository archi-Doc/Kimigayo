// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the target table (IMPL 21.4, 21.5, SPEC 22; Tatara r3 §4; DECISIONS R5-M174): one row per executable target profile, windows-x64
// (Windows) and linux-x64 (Linux, the Tatara plan's B7b2a: static, without a C library, its layer over system calls), which Binding's
// native resolution, the Backend's module frame and runtime ABI, target selection (Project, Solution, LSP), project configuration and
// the native build read.
// Input: none. Output: the rows (Rows), the row of a triple (Find), and each row's columns: the triple, the profile name, data layout,
// CPU, features and code generation settings, the module frame's extra function attributes (FunctionAttributes), the entry and a COFF
// row's subsystem and float marker (null on a row without them), the platform layer (Layer: its template's resource name), the
// reserved supplies (Supplies: each one's name, kind and symbol set; Supply(name)), the supply a program's C library starts through
// (CRuntime, null for none; the Tatara plan's B7b2b), the convention by which the row's imports extend their arguments (ArgumentExtension,
// IMPL 21.2's name, null for none) and the reserved external names (ReservedSymbols, IMPL 21.2), the runtime's import declarations, and
// the bound below which the system maps no address; table-wide, the C memory functions every runtime defines (MemorySymbols) and the C
// entry's symbol (MainSymbol).
// Invariants: constants and arrays only; triples distinct; each row's Layer names exactly one of the Backend's RuntimeLayer records
// (Core names no Backend type, so CoreModelTest pins it); each row's supplies distinct, in Ordinal name order (publication interleaves
// a program's supplies by name), with disjoint symbol sets (an external symbol has one providing library, IMPL 21.2); a row's CRuntime
// names one of its supplies; a row's reserved external names are its float marker and its backend supply's symbols; the native build
// checks the embedded backend record against them and keys its material by the supplies' names (ToolchainIdentity).
// Must not read a file, a resource or the toolchain (the native build's material is ToolchainIdentity's), nor stand for the checked
// triple (Compilation.TargetTriple and IrTarget: any triple a check selects).

namespace Kimi.Compiler;

/// <summary>A row of the target table: one executable target profile (SPEC 22). This is a partial language implementation.</summary>
internal sealed class TargetProfile
{
    /// <summary>The C entry's one spelling (IMPL 21.5.2; the Tatara plan's B7b2b): the function a C runtime's startup calls after
    /// initializing the C library, which a module starting through its row's CRuntime defines; a reserved external name (IMPL 21.2).</summary>
    internal const string MainSymbol = "main";

    /// <summary>The C memory functions every runtime defines in its module's IR, whatever the row (IMPL 21.4.7; the Tatara plan's
    /// decision 24): the names the Backend's memory part (MemoryRuntime.ll.in) defines, in Ordinal order. Static initializers run in
    /// text order: this before the Windows row's values.</summary>
    internal static readonly string[] MemorySymbols = ["memcmp", "memcpy", "memmove", "memset"];

    // The Windows row's values that two of its columns share, stated once. Static initializers run in text order: these before Windows,
    // Windows before Rows (a reversed pair reads null).
    private const string WindowsFloatMarker = "_fltused";

    // The kernel32 declarations the generated runtimes emit, with their import-ABI physical signatures: the ordinary runtime of SPEC
    // 21.4.7, the two test-runtime APIs, which one source can reach in either build, then the platform layer's pages (the Tatara plan's
    // decision 23, B7r2). A null signature marks a declaration no expressible LibraryImport can share: the ExitProcess declaration is
    // noreturn, an ABI attribute no import carries (SPEC 21.2).
    private static readonly (string Symbol, string? Signature)[] WindowsRuntimeDeclarations =
    [
        ("GetStdHandle", "p4"),
        ("WriteFile", "4pp4pp"),
        ("GetLastError", "4"),
        ("ExitProcess", null),
        ("GetEnvironmentVariableA", "4pp4"),
        ("SetHandleInformation", "4p44"),
        ("VirtualAlloc", "pp844"),
        ("VirtualFree", "4p84"),
    ];

    // The backend supply's helper symbols in catalog order (IMPL 21.4.7), which its supply and the reserved external names share: the
    // archive still holds members for the memory functions, which no link pulls since every module defines them.
    private static readonly string[] WindowsBackendSymbols = ["__chkstk", .. MemorySymbols];

    /// <summary>Gets the windows-x64 row, the pinned, module-local Windows execution profile (IMPL 21.4). Its reserved supplies (IMPL
    /// 20.4), in Ordinal name order: kernel32, the generated import library, whose symbol set is the runtime declarations' symbols, then
    /// VirtualProtect and the Windows counter APIs; and kimi_backend, the backend archive, a static library of its helper
    /// catalog (IMPL 21.4.7).</summary>
    internal static TargetProfile Windows { get; } = new()
    {
        Target = "x86_64-pc-windows-msvc",
        Name = "windows-x64-v1",
        DataLayout = "e-m:w-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128",
        Cpu = "x86-64",
        Features = "+sse2",
        RelocationModel = "pic",
        CodeModel = "small",
        UnwindTables = "async",
        FunctionAttributes = [],
        EntrySymbol = "__kimi_start",
        Subsystem = "console",
        FloatMarker = WindowsFloatMarker,
        Layer = "WindowsPlatform.ll.in",
        UnmappedBound = 65536,
        RuntimeDeclarations = WindowsRuntimeDeclarations,
        Supplies =
        [
            new(
                "kernel32",
                "import",
                [
                    .. WindowsRuntimeDeclarations.Select(static x => x.Symbol),
                    "VirtualProtect", "QueryPerformanceCounter", "QueryPerformanceFrequency",
                ]),
            new("kimi_backend", "static", WindowsBackendSymbols),
        ],
        CRuntime = null,
        ArgumentExtension = null,
        ReservedSymbols = [WindowsFloatMarker, .. WindowsBackendSymbols],
    };

    /// <summary>Gets the linux-x64 row, the static Linux execution profile (IMPL 21.5; the Tatara plan's decision 21): its platform
    /// layer calls the kernel directly, so it has no runtime declarations, float marker or subsystem, and a module links the C library
    /// only when it requires a library, starting through it (CRuntime: c, a static library providing the C functions the layer stands
    /// for); its imports extend 8- and 16-bit integers by the System V x86-64 convention (IMPL 21.2).</summary>
    internal static TargetProfile Linux { get; } = new()
    {
        Target = "x86_64-unknown-linux-musl",
        Name = "linux-x64-v1",
        DataLayout = "e-m:e-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128",
        Cpu = "x86-64",
        Features = "+sse2",
        RelocationModel = "static",
        CodeModel = "small",
        UnwindTables = "async",
        FunctionAttributes = ["\"probe-stack\"=\"inline-asm\""],
        EntrySymbol = "__kimi_start",
        Subsystem = null,
        FloatMarker = null,
        Layer = "LinuxPlatform.ll.in",
        UnmappedBound = 65536,
        RuntimeDeclarations = [],
        Supplies = [new("c", "static", ["write", "_exit", "exit", "mmap", "munmap", "clock_gettime"])],
        CRuntime = "c",
        ArgumentExtension = "SystemV",
        ReservedSymbols = [],
    };

    /// <summary>Gets the table's rows: one per executable target profile (SPEC 22), in the order the table states them.</summary>
    internal static TargetProfile[] Rows { get; } = [Windows, Linux];

    /// <summary>Gets the target triple, the row's key (the manifest's target).</summary>
    internal required string Target { get; init; }

    /// <summary>Gets the profile name the IR header and the manifest record.</summary>
    internal required string Name { get; init; }

    /// <summary>Gets the LLVM data layout.</summary>
    internal required string DataLayout { get; init; }

    // Code generation settings shared by function attributes, the link manifest and llc (SPEC 21.3.1).
    internal required string Cpu { get; init; }

    internal required string Features { get; init; }

    internal required string RelocationModel { get; init; }

    internal required string CodeModel { get; init; }

    internal required string UnwindTables { get; init; }

    /// <summary>Gets the LLVM function attributes the module frame adds to the profile attributes of every generated definition (#0),
    /// each attribute's text; Windows has none, Linux probes the stack inline (no __chkstk without a backend archive).</summary>
    internal required string[] FunctionAttributes { get; init; }

    // Application entry (SPEC 22.4.3) and reserved runtime name (IMPL 21.2) identities; a COFF row's subsystem and float marker, null on
    // a row without them (every reader takes null as none).
    internal required string EntrySymbol { get; init; }

    internal required string? Subsystem { get; init; }

    internal required string? FloatMarker { get; init; }

    /// <summary>Gets the resource name of the row's platform layer template, which names the row's RuntimeLayer record in the Backend's
    /// RuntimeAbi (the layer's OS-error prefix and runtime definitions).</summary>
    internal required string Layer { get; init; }

    /// <summary>Gets the bound below which the system maps no address (Windows: the null-pointer partition, 64 KiB): no heap block lies
    /// below it, so the runtime's release skips such an address.</summary>
    internal required int UnmappedBound { get; init; }

    /// <summary>Gets the import declarations the generated runtimes emit, with their import-ABI physical signatures (null: no
    /// expressible LibraryImport can share the declaration).</summary>
    internal required (string Symbol, string? Signature)[] RuntimeDeclarations { get; init; }

    /// <summary>Gets the reserved supplies (IMPL 20.4) in Ordinal name order, with disjoint symbol sets.</summary>
    internal required TargetSupply[] Supplies { get; init; }

    /// <summary>Gets the name of the reserved supply through which a module that requires any library starts (IMPL 21.5.2: the C
    /// runtime, linked and started first), one of <see cref="Supplies"/>, or null: the row links none.</summary>
    internal required string? CRuntime { get; init; }

    /// <summary>Gets the name of the convention by which the row's imports extend their 8- and 16-bit integer arguments and results
    /// (IMPL 21.2: "SystemV", by signedness), or null: they pass at their own width.</summary>
    internal required string? ArgumentExtension { get; init; }

    /// <summary>Gets the row's reserved external names (IMPL 21.2): the float marker and the backend supply's symbols.</summary>
    internal required string[] ReservedSymbols { get; init; }

    /// <summary>Gets the row of a target triple.</summary>
    /// <param name="triple">The target triple.</param>
    /// <returns>The row whose <see cref="Target"/> is <paramref name="triple"/>, or null when the table has none.</returns>
    internal static TargetProfile? Find(string triple)
    {
        foreach (var row in Rows)
        {
            if (row.Target == triple)
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>Gets the reserved supply of a library name.</summary>
    /// <param name="name">The library name.</param>
    /// <returns>The reserved supply that <paramref name="name"/> names, or null when it names none.</returns>
    internal TargetSupply? Supply(string? name)
    {
        foreach (var supply in this.Supplies)
        {
            if (supply.Name == name)
            {
                return supply;
            }
        }

        return null;
    }
}

/// <summary>A reserved supply of a target profile (IMPL 20.4): the library the compiler supplies, its kind and the symbols it
/// provides.</summary>
/// <param name="Name">The reserved library name.</param>
/// <param name="Kind">The library's kind, "import" or "static" (IMPL 20.4).</param>
/// <param name="Symbols">The external symbols the library provides.</param>
internal sealed record TargetSupply(string Name, string Kind, string[] Symbols);
