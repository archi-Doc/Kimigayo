// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the target table (IMPL 21.4, 21.5, 21.6, SPEC 22; Tatara r3 §4; DECISIONS R5-M174): one row per executable target profile,
// windows-x64 (Windows), linux-x64 (Linux, the Tatara plan's B7b2a: static, without a C library, its layer over system calls) and
// none-x64 (None, the Tatara plan's B9: freestanding, its program supplying its platform), which Binding's native resolution, the
// Backend's module frame and runtime ABI, target selection (Project, Solution, LSP), project configuration and the native build read.
// None is staged until its link exists (Staged; removal B9g): Find and the reads over every row (AllRows: Rows, then Staged) reach it,
// selection (Host, Project.ArtifactTarget) and the readers of the selectable rows (Rows) do not.
// Input: this process's operating system and architecture (HostName, read once). Output: the selectable rows (Rows), the staged rows
// (Staged), the row of a triple (Find), the host's row (Host: the row whose executables this host runs, or null), and each row's
// columns: the triple, the profile name, data layout, CPU, features and code generation settings, the module frame's extra function
// attributes (FunctionAttributes), the entry and a COFF row's subsystem, float marker and load configuration symbol (null on a row
// without them), the platform layer (Layer: its template's resource name), the reserved supplies (Supplies: each one's name, kind and
// symbol set; Supply(name)), the library kinds the row links (Kinds, IMPL 20.4; the Tatara plan's B7b5), the supply a program's C
// library starts through (CRuntime, null for none; the Tatara plan's B7b2b), the convention by which the row's imports extend their
// arguments (ArgumentExtension, IMPL 21.2's name, null for none), the extension of a program library's input (InputExtension, IMPL
// 20.4), the executable format, whose link writer the native build selects (Format, IMPL 20.3.2: "COFF" or "ELF"; the Tatara plan's
// B7b7), the host that runs the row's executables (ExecutionHost, as HostName spells one, null for none; B7b8), whether the program
// supplies the platform layer's routines (ProgramPlatform, SPEC 22.4.5; the Tatara plan's B9), the runtime's import declarations,
// and the bound below which the system maps no address; table-wide, the C memory functions every runtime defines (MemorySymbols), the
// C entry's symbol (MainSymbol), the reserved external names of every target (ReservedSymbols, IMPL 21.2) and the reads over every
// row (B7b5): a reserved supply of a name (ReservedSupply; its name is reserved under every target key, IMPL 20.4), whether a supply
// of that name provides a symbol (Provides), and a symbol's runtime declaration with its providing supply (TryGetRuntimeDeclaration).
// Invariants: constants and arrays only, but HostName, this process's, read once; triples distinct; no two rows run on one host
// (ExecutionHost); each selectable row's Layer names exactly one of the Backend's RuntimeLayer records (Core names no Backend type, so
// CoreModelTest pins it; the staged row's record is B9d1's); each row's supplies distinct, in Ordinal name order (publication
// interleaves a program's supplies by name), with disjoint symbol sets (an external symbol has one providing library, IMPL 21.2), each
// one's kind among its row's Kinds and one kind for every row's supply of its name (a reserved supply's kind is fixed, IMPL 20.4); a
// row's CRuntime names one of its supplies; every row declaring a runtime symbol declares it alike (one physical signature, provided
// by a supply of one name), so any row's declaration is every row's; ReservedSymbols are every row's float marker and backend
// supply's symbols, MemorySymbols and MainSymbol, pairwise disjoint parts, built once; the native build checks the embedded backend
// record against the backend supply and keys its material by the supplies' names (ToolchainIdentity).
// Must not read a file, a resource or the toolchain (the native build's material is ToolchainIdentity's), nor stand for the checked
// triple (Compilation.TargetTriple and IrTarget: any triple a check selects).

using System.Runtime.InteropServices;

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

    // The backend supply's name, which the Windows row's supply and the reserved external names read (IMPL 21.4.7).
    private const string BackendSupply = "kimi_backend";

    // The Windows row's values that its columns read, stated once. Static initializers run in text order: these before Windows, Windows
    // and Linux before Rows, None before Staged, Rows and Staged before AllRows, AllRows before ReservedSymbols, Rows and HostName before
    // Host (a reversed pair reads null).
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

    // The backend supply's helper symbols in catalog order (IMPL 21.4.7), reserved external names through the supply (ReservedSymbols).
    private static readonly string[] WindowsBackendSymbols = ["__chkstk"];

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
        FloatMarker = "_fltused",
        LoadConfigSymbol = "_load_config_used",
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
            new(BackendSupply, "static", WindowsBackendSymbols),
        ],
        Kinds = ["import", "static"],
        CRuntime = null,
        ArgumentExtension = null,
        InputExtension = ".lib",
        Format = "COFF",
        ExecutionHost = "windows x64",
        ProgramPlatform = false,
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
        LoadConfigSymbol = null,
        Layer = "LinuxPlatform.ll.in",
        UnmappedBound = 65536,
        RuntimeDeclarations = [],
        Supplies = [new("c", "static", ["write", "_exit", "exit", "mmap", "munmap", "clock_gettime"])],
        Kinds = ["static"],
        CRuntime = "c",
        ArgumentExtension = "SystemV",
        InputExtension = ".a",
        Format = "ELF",
        ExecutionHost = null,
        ProgramPlatform = false,
    };

    /// <summary>Gets the table's selectable rows: one per executable target profile (SPEC 22) that selection reads (Host, the artifact
    /// target), in the order the table states them.</summary>
    internal static TargetProfile[] Rows { get; } = [Windows, Linux];

    /// <summary>Gets the none-x64 row, the freestanding x86-64 profile (IMPL 21.6; the Tatara plan's decision 22): kernel code generation
    /// (the kernel code model, static relocation, no red zone, soft float without SSE or x87 state, and no stack probe: SPEC 17.4.3's
    /// freestanding exception, decision 44), its program supplying its platform layer's routines (ProgramPlatform, SPEC 22.4.5), no
    /// runtime declaration, reserved supply, C runtime or host. Staged (<see cref="Staged"/>) until the Tatara plan's B9g.</summary>
    internal static TargetProfile None { get; } = new()
    {
        Target = "x86_64-unknown-none",
        Name = "none-x64-v1",
        DataLayout = "e-m:e-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128",
        Cpu = "x86-64",
        Features = "-mmx,-sse,-sse2,-sse3,-ssse3,-sse4.1,-sse4.2,-avx,-avx2,+soft-float",
        RelocationModel = "static",
        CodeModel = "kernel",
        UnwindTables = "async",
        FunctionAttributes = ["noredzone"],
        EntrySymbol = "__kimi_start",
        Subsystem = null,
        FloatMarker = null,
        LoadConfigSymbol = null,
        Layer = "NonePlatform.ll.in",
        UnmappedBound = 65536,
        RuntimeDeclarations = [],
        Supplies = [],
        Kinds = ["static"],
        CRuntime = null,
        ArgumentExtension = "SystemV",
        InputExtension = ".a",
        Format = "ELF",
        ExecutionHost = null,
        ProgramPlatform = true,
    };

    /// <summary>Gets the staged rows: the table's rows outside <see cref="Rows"/>, which <see cref="Find"/> and the reads over every row
    /// reach and selection does not.</summary>
    [Transitional("B9g", Reason = "The x86_64-unknown-none row outside Rows until its link exists (the Tatara plan's B9b): B9g appends None to Rows and deletes Staged, AllRows and Project.ArtifactTarget's membership test.")]
    internal static TargetProfile[] Staged { get; } = [None];

    /// <summary>Gets every row of the table, <see cref="Rows"/> then <see cref="Staged"/>, which <see cref="Find"/> and the reads over
    /// every row read (<see cref="ReservedSupply"/>, <see cref="Provides"/>, <see cref="TryGetRuntimeDeclaration"/> and
    /// <see cref="ReservedSymbols"/>' build); B9g deletes it with <see cref="Staged"/>.</summary>
    internal static TargetProfile[] AllRows { get; } = [.. Rows, .. Staged];

    /// <summary>Gets the reserved external names of every target (IMPL 21.2; the Tatara plan's B7b5): for each row its float marker and
    /// its backend supply's symbols, then <see cref="MemorySymbols"/>, then <see cref="MainSymbol"/>, each name once, built once.</summary>
    internal static string[] ReservedSymbols { get; } = ReservedNames();

    /// <summary>Gets this process's host, "&lt;os&gt; &lt;arch&gt;": the OS windows, linux, macos or unknown, and the OS architecture lowercased (x64,
    /// arm64, ...), read once.</summary>
    internal static string HostName { get; } =
        (OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "unknown") + " " +
        RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

    /// <summary>Gets the host's row: the row whose <see cref="ExecutionHost"/> is <see cref="HostName"/>, whose executables this process's
    /// host runs, or null when it runs none (IMPL 20.3: run and test require it; an implicit project takes its target).</summary>
    internal static TargetProfile? Host { get; } = Array.Find(Rows, static x => x.ExecutionHost == HostName);

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

    /// <summary>Gets the symbol whose definition a COFF link makes the image's load configuration, which lld-link loads whenever an
    /// input defines it (the dependency step's fixed root); null on a row without one.</summary>
    internal required string? LoadConfigSymbol { get; init; }

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

    /// <summary>Gets the library kinds the row links (IMPL 20.4), in Ordinal order, each supply's among them: a program library of
    /// another kind is Unsupported on the row.</summary>
    internal required string[] Kinds { get; init; }

    /// <summary>Gets the name of the reserved supply through which a module that requires any library starts (IMPL 21.5.2: the C
    /// runtime, linked and started first), one of <see cref="Supplies"/>, or null: the row links none.</summary>
    internal required string? CRuntime { get; init; }

    /// <summary>Gets the name of the convention by which the row's imports extend their 8- and 16-bit integer arguments and results
    /// (IMPL 21.2: "SystemV", by signedness), or null: they pass at their own width.</summary>
    internal required string? ArgumentExtension { get; init; }

    /// <summary>Gets the extension of a program library's input on the row (IMPL 20.4: ".lib" for windows-x64, ".a" for linux-x64),
    /// which publication checks and the native build's staged copy keeps.</summary>
    internal required string InputExtension { get; init; }

    /// <summary>Gets the row's executable format (IMPL 20.3.2), "COFF" or "ELF", which ArtifactPaths maps to its executable's extension
    /// and the native build to its link writer (another value is a broken contract in each); a "COFF" row has a subsystem, a float marker
    /// and a load configuration symbol, no other row has.</summary>
    internal required string Format { get; init; }

    /// <summary>Gets the host whose processes run the row's executables, spelled as <see cref="HostName"/> spells one, or null when no
    /// host Kimi runs on runs them (linux-x64 until the Tatara plan's B8: no Linux host runs Kimi's native build or its launcher).</summary>
    internal required string? ExecutionHost { get; init; }

    /// <summary>Gets whether the program supplies the platform layer's routines through Kimi.Platform (SPEC 22.4.5; the Tatara plan's
    /// decision 29): true on a freestanding row, false on a hosted row, whose layer the runtime defines.</summary>
    internal required bool ProgramPlatform { get; init; }

    /// <summary>Gets the row of a target triple, staged rows included.</summary>
    /// <param name="triple">The target triple.</param>
    /// <returns>The row whose <see cref="Target"/> is <paramref name="triple"/>, or null when the table has none.</returns>
    internal static TargetProfile? Find(string triple)
    {
        foreach (var row in AllRows)
        {
            if (row.Target == triple)
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>Gets a reserved supply of a library name in any row (IMPL 20.4: its name is reserved under every target key).</summary>
    /// <param name="name">The library name.</param>
    /// <returns>A row's reserved supply that <paramref name="name"/> names, or null when no row's names it.</returns>
    internal static TargetSupply? ReservedSupply(string? name)
    {
        foreach (var row in AllRows)
        {
            if (row.Supply(name) is { } supply)
            {
                return supply;
            }
        }

        return null;
    }

    /// <summary>Gets whether any row's reserved supply of a library name provides an external symbol.</summary>
    /// <param name="name">The library name.</param>
    /// <param name="symbol">The external symbol.</param>
    /// <returns>True when a row's supply that <paramref name="name"/> names lists <paramref name="symbol"/>.</returns>
    internal static bool Provides(string name, string symbol)
    {
        foreach (var row in AllRows)
        {
            if (row.Supply(name) is { } supply && Array.IndexOf(supply.Symbols, symbol) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the runtime import declaration of an external symbol in any row (IMPL 21.2, 21.4.7), with the name of that row's
    /// supply providing the symbol; rows declaring one symbol declare it alike, so it is every row's.</summary>
    /// <param name="symbol">The external symbol.</param>
    /// <param name="signature">The declaration's import-ABI physical signature, null when no expressible LibraryImport can share it.</param>
    /// <param name="provider">The name of the row's supply providing <paramref name="symbol"/>, null when none does.</param>
    /// <returns>True when a row's runtime declares <paramref name="symbol"/>.</returns>
    internal static bool TryGetRuntimeDeclaration(string symbol, out string? signature, out string? provider)
    {
        foreach (var row in AllRows)
        {
            foreach (var declaration in row.RuntimeDeclarations)
            {
                if (string.Equals(declaration.Symbol, symbol, StringComparison.Ordinal))
                {
                    (signature, provider) = (declaration.Signature, Provider(row.Supplies, symbol));
                    return true;
                }
            }
        }

        signature = null;
        provider = null;
        return false;

        static string? Provider(TargetSupply[] supplies, string symbol)
        {
            foreach (var supply in supplies)
            {
                if (Array.IndexOf(supply.Symbols, symbol) >= 0)
                {
                    return supply.Name;
                }
            }

            return null;
        }
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

    // ReservedSymbols' one build, after AllRows: each part appended as it is (the parts are disjoint).
    private static string[] ReservedNames()
    {
        List<string> names = [];
        foreach (var row in AllRows)
        {
            if (row.FloatMarker is { } marker)
            {
                names.Add(marker);
            }

            names.AddRange(row.Supply(BackendSupply)?.Symbols ?? []);
        }

        return [.. names, .. MemorySymbols, MainSymbol];
    }
}

/// <summary>A reserved supply of a target profile (IMPL 20.4): the library the compiler supplies, its kind and the symbols it
/// provides.</summary>
/// <param name="Name">The reserved library name.</param>
/// <param name="Kind">The library's kind, "import" or "static" (IMPL 20.4).</param>
/// <param name="Symbols">The external symbols the library provides.</param>
internal sealed record TargetSupply(string Name, string Kind, string[] Symbols);
