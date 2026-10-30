// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal enum KimiLibraryContainer : byte
{
    Root,
    Console,
    Intrinsics,
    Test,
    Array,
    Dictionary,
    Text,
    FixedBuffer,
    HeapBuffer,
    WriteWindow,
    Utf8Writer,
    Storage,
    Raw,
    FixedArray,
}

/// <summary>Immutable recognition rules, shared across compilations. Stable IDs are independent of catalog and syntax order. The entries
/// are in KimiLibraryCatalog.Definitions.cs, the rows an operation publishes beyond its signature in KimiLibraryCatalog.Rows.cs.</summary>
internal static partial class KimiLibraryCatalog
{
    internal static ReadOnlySpan<Entry> Entries => Definitions;

    internal static bool IsStorageOperation(CompilerFunctionKind kind) => kind is >= CompilerFunctionKind.StorageBorrowShared and <= CompilerFunctionKind.StorageSetArrayLength;

    internal static bool IsRawOperation(CompilerFunctionKind kind) => kind is >= CompilerFunctionKind.RawAllocate and <= CompilerFunctionKind.RawSlice;

    internal static bool RequiresCallerLocation(BindingSymbol? symbol)
        => symbol?.LibraryDeclaration is { } id && Index(id) is >= 0 and var index && Definitions[index].CallerLocation;

    // SPEC 22.1: a Kimi Type whose representation the compiler manages has exactly the constructors its declaration declares.
    internal static bool ManagesRepresentation(BindingSymbol? symbol)
        => symbol?.LibraryDeclaration is { } id && Index(id) is >= 0 and var index && Definitions[index].ManagedRepresentation;

    internal static int Index(KimiDeclarationId id) => (uint)id < (uint)Indices.Length ? Indices[(int)id] : -1;

    // The internal source function that defines a linked public declaration (SPEC 22.1).
    internal static KimiDeclarationId? ImplementationOf(KimiDeclarationId id) => Index(id) is >= 0 and var index ? Definitions[index].Implementation : null;

    // The public declaration an internal implementation defines, which every presentation names instead.
    internal static KimiDeclarationId? PresentationOf(KimiDeclarationId id) => Index(id) is >= 0 and var index && Presentations[index] >= 0 ? Definitions[Presentations[index]].Id : null;

    /// <summary>Gets a value indicating whether a name is a cataloged declaration that the library does not yet declare in source (PLAN G4).</summary>
    /// <param name="container">The container the name was looked up in.</param>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> when a lookup that misses the name meets an implementation limit, not a missing name.</returns>
    internal static bool IsUnsourced(KimiLibraryContainer container, string name)
    {
        foreach (ref readonly var entry in Entries)
        {
            if (!entry.SourceExpected && entry.Container == container && entry.Name == name)
            {
                return true;
            }
        }

        return false;
    }

    internal readonly record struct Entry(
        KimiDeclarationId Id,
        string Name,
        KimiLibraryContainer Container = KimiLibraryContainer.Root,
        IntrinsicKind Intrinsic = IntrinsicKind.None,
        CompilerFunctionKind Function = CompilerFunctionKind.None,
        bool SourceExpected = true,
        int Overload = -1,
        bool SourceFunction = false,
        bool CallerLocation = false,
        string? Owner = null,
        bool ManagedRepresentation = false,
        PublishedRow? Row = null,
        KimiDeclarationId? Implementation = null)
    {
        internal bool IsFunction => this.SourceFunction || this.Function != CompilerFunctionKind.None || this.Container == KimiLibraryContainer.Intrinsics || this.Implementation is not null;
    }
}
