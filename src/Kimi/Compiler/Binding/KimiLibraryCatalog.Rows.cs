// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// SPEC 4.9.6, 12.4.3: the user-code effects a library operation publishes beside its input accesses, allocation and Abort: the requirement
// of Contract Runs on the Type below parameter On, and a destruction of the Type below parameter Destroys (-1: none). InPlace: the
// destruction destroys that target in place (SPEC 15.6.3, 15.7: `replace`), which ends the children based in it (design A3 4.1).
internal readonly record struct PublishedRow(KimiDeclarationId? Runs = null, sbyte On = -1, sbyte Destroys = -1, bool InPlace = false);

// SPEC 5.6, 5.7 (D14): an Origin relation a library operation publishes beyond its signature: its result Origin lies within the Origin
// that the argument of parameter Within states before its fit to that parameter (Raw.slice's storage).
internal readonly record struct WithinRow(KimiDeclarationId Id, sbyte Within);

// The rows a library operation publishes beyond its signature, by its catalog ID (the recognition entries,
// KimiLibraryCatalog.Definitions.cs, name the operations and carry their PublishedRow).
internal static partial class KimiLibraryCatalog
{
    // The operations' Origin relations beyond their signatures (WithinRow), apart from their recognition entries.
    private static readonly WithinRow[] WithinRows = [new(KimiDeclarationId.RawSlice, 0)];

    // SPEC 4.9.6, 12.4.3: the user-code effects an operation publishes, a bodiless constructor its linked implementation's; null for none.
    // The row, dependency rules, allocation, Abort and input accesses form its published summary; callers and bound verification use
    // that summary, never the private body, which is verified against it.
    internal static PublishedRow? Row(BindingSymbol? symbol)
        => symbol?.LibraryDeclaration is { } id && Index(ImplementationOf(id) ?? id) is >= 0 and var index ? Definitions[index].Row : null;

    // SPEC 5.6, 5.7 (D14): the parameter whose argument's raw Origin, as stated before its fit, the operation's result Origin lies
    // within (WithinRows); -1 for none.
    internal static int Within(BindingSymbol? symbol)
    {
        foreach (var row in WithinRows)
        {
            if (symbol?.LibraryDeclaration == row.Id)
            {
                return row.Within;
            }
        }

        return -1;
    }
}
