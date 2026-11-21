// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the windows-x64 target profile (IMPL 21.4, SPEC 22): the one executable target profile that Binding's native resolution, the
// Backend's module frame and runtime ABI, target selection (Project, Solution, LSP) and the native build read (DECISIONS R5-M174).
// Input: none. Output: the triple, the profile name, data layout, CPU, features and code generation settings, the entry and subsystem,
// the reserved runtime name, the reserved supplies (Supplies: each one's name, kind and symbol set; Supply(name)) and the reserved
// external names (ReservedSymbols, IMPL 21.2), the runtime's kernel32 declarations, and the bound below which the system maps no address.
// Invariants: constants and arrays only; the reserved supplies distinct, in Ordinal name order (publication interleaves a program's
// supplies by name), with disjoint symbol sets (an external symbol has one providing library, IMPL 21.2); the reserved external names
// are the float marker and the backend supply's symbols; the native build checks the embedded backend record against them and keys
// its material by the supplies' names (ToolchainIdentity).
// Must not read a file, a resource or the toolchain (its identity is ToolchainIdentity's), nor stand for the checked triple
// (Compilation.TargetTriple and IrTarget: any triple a check selects).

namespace Kimi.Compiler;

/// <summary>The pinned, module-local Windows execution profile. This is a partial language implementation.</summary>
public static class WindowsProfile
{
    public const string Target = "x86_64-pc-windows-msvc";
    public const string Name = "windows-x64-v1";
    public const string DataLayout = "e-m:w-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128";

    // Code generation settings shared by function attributes, the link manifest and llc (SPEC 21.3.1).
    internal const string Cpu = "x86-64";
    internal const string Features = "+sse2";
    internal const string RelocationModel = "pic";
    internal const string CodeModel = "small";
    internal const string UnwindTables = "async";

    // Application entry (SPEC 22.4.3) and reserved runtime name (IMPL 21.2) identities.
    internal const string EntrySymbol = "__kimi_start";
    internal const string Subsystem = "console";
    internal const string FloatMarker = "_fltused";

    /// <summary>The bound below which Windows maps no address (the null-pointer partition, 64 KiB): no heap block lies below it, so
    /// the runtime's release skips such an address.</summary>
    internal const int UnmappedBound = 65536;

    /// <summary>Gets the kernel32 declarations the generated runtimes emit, with their import-ABI physical
    /// signatures: the ordinary runtime of SPEC 21.4.7 and the two test-runtime APIs, which one source can
    /// reach in either build. A null signature marks a declaration no expressible LibraryImport can share:
    /// the ExitProcess declaration is noreturn, an ABI attribute no import carries (SPEC 21.2).</summary>
    internal static readonly (string Symbol, string? Signature)[] RuntimeDeclarations =
    [
        ("GetProcessHeap", "p"),
        ("HeapAlloc", "pp48"),
        ("HeapFree", "4p4p"),
        ("GetStdHandle", "p4"),
        ("WriteFile", "4pp4pp"),
        ("GetLastError", "4"),
        ("ExitProcess", null),
        ("GetEnvironmentVariableA", "4pp4"),
        ("SetHandleInformation", "4p44"),
    ];

    // The backend supply's helper symbols in catalog order (IMPL 21.4.7), which its supply and the reserved external names share.
    private static readonly string[] BackendSymbols = ["__chkstk", "memcmp", "memcpy", "memmove", "memset"];

    /// <summary>Gets the reserved supplies (IMPL 20.4) in Ordinal name order: kernel32, the generated import library, whose symbol set
    /// is the runtime declarations' symbols, then the VirtualAlloc test family and the Windows counter APIs; and kimi_backend, the
    /// backend archive, a static library of its helper catalog (IMPL 21.4.7). Static initializers run in text order, so the fields
    /// above are set first.</summary>
    internal static TargetSupply[] Supplies { get; } =
    [
        new(
            "kernel32",
            "import",
            [
                .. RuntimeDeclarations.Select(static x => x.Symbol),
                "VirtualAlloc", "VirtualProtect", "VirtualFree", "QueryPerformanceCounter", "QueryPerformanceFrequency",
            ]),
        new("kimi_backend", "static", BackendSymbols),
    ];

    /// <summary>Gets the profile's reserved external names (IMPL 21.2): the float marker and the backend supply's symbols.</summary>
    internal static string[] ReservedSymbols { get; } = [FloatMarker, .. BackendSymbols];

    /// <summary>Gets the reserved supply of a library name.</summary>
    /// <param name="name">The library name.</param>
    /// <returns>The reserved supply that <paramref name="name"/> names, or null when it names none.</returns>
    internal static TargetSupply? Supply(string? name)
    {
        foreach (var supply in Supplies)
        {
            if (supply.Name == name)
            {
                return supply;
            }
        }

        return null;
    }
}

/// <summary>A reserved supply of the target profile (IMPL 20.4): the library the compiler supplies, its kind and the symbols it
/// provides.</summary>
/// <param name="Name">The reserved library name.</param>
/// <param name="Kind">The library's kind, "import" or "static" (IMPL 20.4).</param>
/// <param name="Symbols">The external symbols the library provides.</param>
internal sealed record TargetSupply(string Name, string Kind, string[] Symbols);
