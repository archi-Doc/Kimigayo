// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>Immutable recognition rules, shared across compilations. The entries, indexed by ID, are in KimiLibraryCatalog.Definitions.cs,
/// the rows an operation publishes beyond its signature and the functions code generation keeps out of line in
/// KimiLibraryCatalog.Rows.cs.</summary>
internal static partial class KimiLibraryCatalog
{
    // IMPL 21.3.4: a `#Lang("Name")` names the KimiDeclarationId member of that Name, compared ordinally.
    private static readonly Dictionary<string, KimiDeclarationId> LangItems = Enum.GetValues<KimiDeclarationId>().ToDictionary(static x => x.ToString(), StringComparer.Ordinal);

    internal static ReadOnlySpan<Entry> Entries => Definitions;

    // SPEC 22.5.1: the private caller-location ABI: the original declaration's `#CallerLocation`, and a destructor of the Kimi Kotonoha,
    // which only a destruction runs (SPEC 16.3) and which reports the location its drop glue receives, the Drop's (O-405); a reader
    // passes the original's symbol (TyCtxt.Original), never a specialization's.
    internal static bool RequiresCallerLocation(BindingSymbol? symbol)
        => symbol is { CallerLocation: true } or { Declaration: Parsing.FunctionKoto { IsDestructor: true, Kotonoha.Syntax.IsLibrary: true } };

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

    // An entry's declaration is the one whose `#Lang` names its Id (IMPL 21.3.4), which the library's registration reads; a Form entry's
    // is a bodyless function whose call the MIR builder writes as a MIR form (DispositionKind.Form).
    internal readonly record struct Entry(
        KimiDeclarationId Id,
        IntrinsicKind Intrinsic = IntrinsicKind.None,
        bool ManagedRepresentation = false,
        PublishedRow? Row = null,
        KimiDeclarationId? Implementation = null,
        bool Form = false);
}
