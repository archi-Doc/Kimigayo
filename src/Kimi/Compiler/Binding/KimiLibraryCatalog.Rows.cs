// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// SPEC 4.9.6, 12.4.3: the user-code effects a library operation publishes beside its input accesses, allocation and Abort: the requirement
// of Contract Runs on the Type below parameter On, a destruction of the Type below parameter Destroys (-1: none), and the calls of the
// by-value Callable input at parameter Calls (-1: none; `removeAll`'s `matching`), which the operation calls through a shared borrow of it,
// the receiver its Callable premise declares (SPEC 8.11.4), and destroys after the last call (SPEC 15.6.4, 16.3.3: a by-value input's
// cleanup is the callee's); callers compose those calls and that destruction through the actual argument. InPlace: the destruction
// destroys that target in place (SPEC 15.6.3, 15.7: `replace`), which ends the children based in it (design A3 4.1).
internal readonly record struct PublishedRow(KimiDeclarationId? Runs = null, sbyte On = -1, sbyte Destroys = -1, bool InPlace = false, sbyte Calls = -1);

// SPEC 5.6, 5.7 (D14): an Origin relation a library operation publishes beyond its signature: its result Origin lies within the Origin
// that the argument of parameter Within states before its fit to that parameter (Raw.slice's storage).
internal readonly record struct WithinRow(KimiDeclarationId Id, sbyte Within);

// SPEC 4.9.6, 15.6.4: the added and replacing inputs of a 4.9 operation, by parameter slot (bit i: slot i), the only arguments other
// than its receiver that its element storage retains at a raw layer without an Origin (the transfer rule, Binding.Retention.Transfers.cs).
internal readonly record struct AddedRow(KimiDeclarationId Id, byte Mask);

// The rows a library operation publishes beyond its signature, by its catalog ID (the recognition entries,
// KimiLibraryCatalog.Definitions.cs, name the operations and carry their PublishedRow).
internal static partial class KimiLibraryCatalog
{
    // The operations' Origin relations beyond their signatures (WithinRow), apart from their recognition entries.
    private static readonly WithinRow[] WithinRows = [new(KimiDeclarationId.RawSlice, 0)];

    // The 4.9 operations' added and replacing inputs (AddedRow); an Array or Dictionary member without a row adds none (KimiLibraryShapeTest
    // pins the rows against the members' declared retentions into their receivers).
    private static readonly AddedRow[] AddedRows =
    [
        new(KimiDeclarationId.ArrayAppend, 0b10), new(KimiDeclarationId.ArrayInsert, 0b100), new(KimiDeclarationId.ArrayInsertPosition, 0b100),
        new(KimiDeclarationId.ArrayAppendAll, 0b10), new(KimiDeclarationId.ArrayAppendCopies, 0b10), new(KimiDeclarationId.DictionaryTryInsert, 0b110),
        new(KimiDeclarationId.DictionaryInsertOrReplace, 0b110),
    ];

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

    // SPEC 4.9.6: whether parameter slot `input` of a 4.9 operation is one of its added or replacing inputs (AddedRows).
    internal static bool Added(BindingSymbol? symbol, int input)
    {
        foreach (var row in AddedRows)
        {
            if (symbol?.LibraryDeclaration == row.Id)
            {
                return (row.Mask & (1 << input)) != 0;
            }
        }

        return false;
    }
}
