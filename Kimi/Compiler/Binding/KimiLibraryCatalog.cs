// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal enum KimiLibraryContainer : byte
{
    Root,
    Console,
    Intrinsics,
    Test,
}

/// <summary>Immutable recognition rules, shared across compilations. Stable IDs are independent of catalog and syntax order.</summary>
internal static class KimiLibraryCatalog
{
    private static readonly Entry[] Definitions =
    [
        new(KimiDeclarationId.Copy, "Copy", Intrinsic: IntrinsicKind.Copy),
        new(KimiDeclarationId.Owned, "Owned", Intrinsic: IntrinsicKind.Owned),
        new(KimiDeclarationId.Callable, "Callable", Intrinsic: IntrinsicKind.Callable),
        new(KimiDeclarationId.WriteLine, "writeLine", KimiLibraryContainer.Console, Function: CompilerFunctionKind.WriteLine),
        new(KimiDeclarationId.TestTempDirectory, "tempDirectory", KimiLibraryContainer.Test, Function: CompilerFunctionKind.TestTempDirectory),
        new(KimiDeclarationId.Option, "Option"),
        new(KimiDeclarationId.Result, "Result"),
        new(KimiDeclarationId.Array, "Array", SourceExpected: false),
        new(KimiDeclarationId.Index, "Index", SourceExpected: false),
        new(KimiDeclarationId.Range, "Range", SourceExpected: false),
        new(KimiDeclarationId.ResolvedRange, "ResolvedRange", SourceExpected: false),
        new(KimiDeclarationId.Slice, "Slice"),
        new(KimiDeclarationId.Dictionary, "Dictionary", SourceExpected: false),
        new(KimiDeclarationId.Stringify, "Stringify", SourceExpected: false),
        new(KimiDeclarationId.Equatable, "Equatable", SourceExpected: false),
        new(KimiDeclarationId.Comparable, "Comparable", SourceExpected: false),
        new(KimiDeclarationId.Iterator, "Iterator"),
        new(KimiDeclarationId.Iterable, "Iterable", SourceExpected: false),
        new(KimiDeclarationId.Sealed, "Sealed", Intrinsic: IntrinsicKind.Sealed),
        new(KimiDeclarationId.Replace, "replace", KimiLibraryContainer.Intrinsics, Function: CompilerFunctionKind.Replace),
        new(KimiDeclarationId.Exchange, "exchange", KimiLibraryContainer.Intrinsics, Function: CompilerFunctionKind.Exchange),
        new(KimiDeclarationId.Swap, "swap", KimiLibraryContainer.Intrinsics, Function: CompilerFunctionKind.Swap),
        new(KimiDeclarationId.MakeObj, "makeObj", KimiLibraryContainer.Intrinsics, Function: CompilerFunctionKind.MakeObj),
        new(KimiDeclarationId.MakeRc, "makeRc", KimiLibraryContainer.Intrinsics, SourceExpected: false),
        new(KimiDeclarationId.MakeArc, "makeArc", KimiLibraryContainer.Intrinsics, SourceExpected: false),
        new(KimiDeclarationId.Clone, "clone", KimiLibraryContainer.Intrinsics, SourceExpected: false),
        new(KimiDeclarationId.Downgrade, "downgrade", KimiLibraryContainer.Intrinsics, SourceExpected: false),
        new(KimiDeclarationId.Upgrade, "upgrade", KimiLibraryContainer.Intrinsics, SourceExpected: false),
        new(KimiDeclarationId.MakeRcCyclic, "makeRcCyclic", KimiLibraryContainer.Intrinsics, SourceExpected: false),
        new(KimiDeclarationId.MakeArcCyclic, "makeArcCyclic", KimiLibraryContainer.Intrinsics, SourceExpected: false),
        new(KimiDeclarationId.Weak, "Weak", SourceExpected: false),
    ];

    private static readonly int[] Indices = CreateIndices();

    internal static ReadOnlySpan<Entry> Entries => Definitions;

    internal static int Index(KimiDeclarationId id) => (uint)id < (uint)Indices.Length ? Indices[(int)id] : -1;

    private static int[] CreateIndices()
    {
        var result = new int[Enum.GetValues<KimiDeclarationId>().Length];
        Array.Fill(result, -1);
        for (var i = 0; i < Definitions.Length; i++)
        {
            var id = (int)Definitions[i].Id;
            if (result[id] != -1)
            {
                throw new InvalidOperationException("Duplicate Kimi catalog ID.");
            }

            result[id] = i;
        }

        return result;
    }

    internal readonly record struct Entry(KimiDeclarationId Id, string Name, KimiLibraryContainer Container = KimiLibraryContainer.Root, IntrinsicKind Intrinsic = IntrinsicKind.None, CompilerFunctionKind Function = CompilerFunctionKind.None, bool SourceExpected = true)
    {
        internal bool IsFunction => this.Container != KimiLibraryContainer.Root;
    }
}
