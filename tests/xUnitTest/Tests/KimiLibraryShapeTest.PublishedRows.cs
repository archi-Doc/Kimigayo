// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// The shared acceptance tests of PLAN G264 U0′ (WORKFLOW Q11; written from the problem statement before either candidate): the catalog
// facts SPEC 4.9.6's rows for Array `truncate` and `removeAll` become; the catalog's no-store rows (C-189); and its out-of-line rows (G779).
public partial class KimiLibraryShapeTest
{
    // SPEC 4.9.6 (PLAN G264 U0′): the catalog recognizes Array `truncate` and `removeAll` and carries their rows, each destroying the Type
    // below parameter 0, its receiver, not in place, as `clear`'s does (the comparison; `PublishedRowsAreFixed` lists every row).
    // Fail-before: `truncate`'s entry has no row and `removeAll` has no entry.
    [Theory]
    [InlineData("truncate")]
    [InlineData("removeAll")]
    [InlineData("clear")]
    public void ArrayTruncateAndRemoveAllCarryTheirRows(string name)
    {
        var library = Library.Value.Library;
        var entry = Assert.Single(KimiLibraryCatalog.Entries.ToArray(), x => library.GetSymbol(x.Id) is { } symbol && symbol.Name == name && ReferenceEquals(symbol.Declaration.Parent, library.DynamicArray.Declaration));
        Assert.True(entry.Row.HasValue, $"{name}: no row");
        Assert.Equal((0, false), ((int)entry.Row.Value.Destroys, entry.Row.Value.InPlace));
    }

    // SPEC 5.2 Placement, 5.6, 15.7 (C-189): each parameter slot an operation contract stores no value through (KimiLibraryCatalog.NoStore,
    // read by TyCtxt.NoStore) is a parameter of a contract whose calls are checked against its identity, named here: a bodiless contract,
    // or a 15.7 declaration (`replace`, `exchange`: "A call is checked against this section's rules, which belong to the declarations'
    // identities, never against a body"), so a reordered, renamed or removed parameter, a body given to another contract or a changed row
    // fails here first. Fail-before: KimiLibraryCatalog.NoStore does not exist; before D-933 `replace` and `exchange` had no row.
    [Fact]
    public void NoStoreInputsAreIdentityContractsNamedParameters()
    {
        var c = Library.Value;
        var inputs = new List<string>();
        foreach (var entry in KimiLibraryCatalog.Entries)
        {
            var symbol = c.Library.GetSymbol(entry.Id);
            for (var i = 0; i < 8; i++)
            {
                if (KimiLibraryCatalog.NoStore(symbol, i))
                {
                    var function = Assert.IsType<FunctionKoto>(symbol!.Declaration);
                    Assert.False(HasBody(function) && entry.Id is not (KimiDeclarationId.Replace or KimiDeclarationId.Exchange), Line(function));
                    Assert.InRange(i, 0, function.Parameters.Count - 1);
                    inputs.Add($"{entry.Id} {function.Parameters[i].InternalName}");
                }
            }
        }

        Assert.Equal(["Replace value", "Exchange value", "StorageTransferBytes source", "RawRelease storage", "RawInitialize value", "RawSlice storage"], inputs);
    }

    // PLAN G779 (O-195 (a)): the functions code generation keeps out of line (KimiLibraryCatalog.KeepsOutOfLine) are a closed set, each
    // admitted by a measured O2 reason in its commit: today only the writer's reservation, a library body. A new row changes this pin.
    [Fact]
    public void OnlyTheWritersReservationIsKeptOutOfLine()
    {
        var c = Library.Value;
        var kept = new List<string>();
        foreach (var entry in KimiLibraryCatalog.Entries)
        {
            var symbol = c.Library.GetSymbol(entry.Id);
            if (KimiLibraryCatalog.KeepsOutOfLine(symbol))
            {
                Assert.True(HasBody(Assert.IsType<FunctionKoto>(symbol!.Declaration)), entry.Id.ToString());
                kept.Add(entry.Id.ToString());
            }
        }

        Assert.Equal(["WriterReserve"], kept);
    }
}
