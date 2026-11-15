// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// The container IsUnsourced matches an unsourced entry's Name in (temporary with IsUnsourced, removed by G1-3b-2).
internal enum KimiLibraryContainer : byte
{
    Root,
    Intrinsics,
}

/// <summary>Immutable recognition rules, shared across compilations. The entries, indexed by ID, are in KimiLibraryCatalog.Definitions.cs,
/// the rows an operation publishes beyond its signature and the functions code generation keeps out of line in
/// KimiLibraryCatalog.Rows.cs.</summary>
internal static partial class KimiLibraryCatalog
{
    // IMPL 21.3.4: a `#Lang("Name")` names the KimiDeclarationId member of that Name, compared ordinally.
    private static readonly Dictionary<string, KimiDeclarationId> LangItems = Enum.GetValues<KimiDeclarationId>().ToDictionary(static x => x.ToString(), StringComparer.Ordinal);

    internal static ReadOnlySpan<Entry> Entries => Definitions;

    // SPEC 22.5.1: the private caller-location ABI, the original declaration's `#CallerLocation`; a reader passes the original's symbol
    // (TyCtxt.Original), never a specialization's.
    internal static bool RequiresCallerLocation(BindingSymbol? symbol) => symbol is { CallerLocation: true };

    // The number of lang items, the KimiDeclarationId members, whose values are implicit and dense from 0: an array of this length is
    // indexed by (int)id.
    internal static int LangItemCount => LangItems.Count;

    // The lang item a `#Lang` Name names, or null when the Name is no KimiDeclarationId member.
    internal static KimiDeclarationId? LangItem(string name) => LangItems.TryGetValue(name, out var id) ? id : null;

    // SPEC 22.1: a Kimi Type whose representation the compiler manages has exactly the constructors its declaration declares.
    internal static bool ManagesRepresentation(BindingSymbol? symbol)
        => symbol?.LibraryDeclaration is { } id && Index(id) is >= 0 and var index && Definitions[index].ManagedRepresentation;

    // The entry of a cataloged lang item, at its ID; -1 for an internal declaration KimiLibrary reads into a member, which has none.
    internal static int Index(KimiDeclarationId id) => (int)id < Definitions.Length ? (int)id : -1;

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

    // An entry's declaration is the one whose `#Lang` names its Id (IMPL 21.3.4), which the library's registration reads. Name and
    // Container belong only to the entries whose SourceExpected is false: the spelling and container IsUnsourced matches, since such a
    // declaration has no source to carry a `#Lang`. A temporary structure: G1-3b-2 writes the five declarations `#Unsupported` and
    // removes Name, Container, SourceExpected and IsUnsourced.
    internal readonly record struct Entry(
        KimiDeclarationId Id,
        IntrinsicKind Intrinsic = IntrinsicKind.None,
        bool SourceExpected = true,
        string? Name = null,
        KimiLibraryContainer Container = KimiLibraryContainer.Root,
        bool ManagedRepresentation = false,
        PublishedRow? Row = null,
        KimiDeclarationId? Implementation = null);
}
