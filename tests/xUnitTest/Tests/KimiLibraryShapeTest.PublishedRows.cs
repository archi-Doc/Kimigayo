// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// The shared acceptance tests of PLAN G264 U0′ (WORKFLOW Q11; written from the problem statement before either candidate): the catalog
// facts SPEC 4.9.6's rows for Array `truncate` and `removeAll` become; and the catalog's no-store rows (C-189).
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
        var entry = Assert.Single(KimiLibraryCatalog.Entries.ToArray(), x => x.Container == KimiLibraryContainer.Array && x.Name == name);
        Assert.True(entry.Row.HasValue, $"{name}: no row");
        Assert.Equal((0, false), ((int)entry.Row.Value.Destroys, entry.Row.Value.InPlace));
    }

    // SPEC 5.2 Placement, 5.6 (C-189): each parameter slot an operation contract stores no value through (KimiLibraryCatalog.NoStore, read
    // by TyCtxt.NoStore) is a parameter of a bodiless contract, named here, so a reordered, renamed or removed parameter, a body given to
    // the contract or a changed row fails here first. Fail-before: KimiLibraryCatalog.NoStore does not exist.
    [Fact]
    public void NoStoreInputsAreBodilessContractsNamedParameters()
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
                    Assert.False(HasBody(function), Line(function));
                    Assert.InRange(i, 0, function.Parameters.Count - 1);
                    inputs.Add($"{entry.Id} {function.Parameters[i].InternalName}");
                }
            }
        }

        Assert.Equal(["StorageTransferBytes source", "RawRelease storage", "RawInitialize value", "RawSlice storage"], inputs);
    }
}
