// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// The shared acceptance tests of PLAN G264 U0′ (WORKFLOW Q11; written from the problem statement before either candidate): the catalog
// facts SPEC 4.9.6's rows for Array `truncate` and `removeAll` become.
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
}
