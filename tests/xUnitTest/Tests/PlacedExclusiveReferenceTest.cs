// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 15.6.3, 15.6.7 (PLAN G53): an exclusive reference moved into a Tuple, array or Dictionary literal is acquired
// exclusively there, as a call argument is, so a value can never hold an exclusive reference together with its own live
// Reborrow child and authorize access through the parent while the child is live.
public sealed class PlacedExclusiveReferenceTest
{
    private const string Run = "func run(b: uniq/i32)\n    let c = b@follow@uniq\n";

    [Theory]
    [InlineData("    let items = (c@move, b@move)\n    items.1@follow = 42\n    items.0@follow = 7")]
    [InlineData("    let items = (b@move, c@move)\n    items.0@follow = 42\n    items.1@follow = 7")]
    [InlineData("    var entries = [1: c@move, 2: b@move]\n    entries[2]@follow = 42\n    entries[1]@follow = 7")]
    [InlineData("    let items = [c@move, b@move]\n    items[1]@follow = 42\n    items[0]@follow = 7")]
    [InlineData("    let items = (b@move, 1)\n    c@follow = 7")]
    public void AParentPlacedWhileItsReborrowChildIsLiveIsRejected(string body)
    {
        var c = MinimalEmissionTest.Analyze(Run + body);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Code == DiagnosticCode.PlacementActivationConflict_Kd);
    }

    [Theory]
    [InlineData("    let items = (c@move, 1)\n    items.0@follow = 7")]
    [InlineData("    c@follow = 7\n    let items = (b@move, 1)\n    items.0@follow = 42")]
    [InlineData("    var entries = [1: c@move]\n    entries[1]@follow = 7")]
    public void APlacementWithoutALiveChildIsAccepted(string body)
    {
        var c = MinimalEmissionTest.Analyze(Run + body);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void TheConflictNamesThePlacement()
    {
        var result = DiagnosticCorpus.Check(Run + "    var entries = [1: c@move, 2: b@move]\n    entries[2]@follow = 42\n    entries[1]@follow = 7\npublic func main()\n    var value = 0\n    run(value@uniq)\n");
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.PlacementActivationConflict_Kd), record.Code);
        Assert.Contains("is acquired exclusively where the literal places it", record.Note, StringComparison.Ordinal);
    }
}
