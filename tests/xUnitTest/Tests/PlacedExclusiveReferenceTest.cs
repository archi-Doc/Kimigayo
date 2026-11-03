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

    // The record is the MIR's (H5): the Move of `b` while its Reborrow child `c` is live, one ComparisonLoanConflict_Kd at that `b`.
    [Theory]
    [InlineData("    let items = (c@move, b@move)\n    items.1@follow = 42\n    items.0@follow = 7")]
    [InlineData("    let items = (b@move, c@move)\n    items.0@follow = 42\n    items.1@follow = 7")]
    [InlineData("    var entries = [1: c@move, 2: b@move]\n    entries[2]@follow = 42\n    entries[1]@follow = 7")]
    [InlineData("    var items = [c@move, b@move]\n    items[1]@follow = 42\n    items[0]@follow = 7")]
    [InlineData("    let items = (b@move, 1)\n    c@follow = 7")]
    public void AParentPlacedWhileItsReborrowChildIsLiveIsRejected(string body)
    {
        var c = MinimalEmissionTest.Analyze(Run + body);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var problem = Assert.Single(c.Problems);
        Assert.Equal(DiagnosticCode.ComparisonLoanConflict_Kd, problem.Code);
        Assert.Equal(((Run + body).IndexOf("b@move", StringComparison.Ordinal), "b"), (c.SyntaxOf(problem.Subject).Span.Start, c.SyntaxOf(problem.Subject).ToString()));
    }

    [Theory]
    [InlineData("    let items = (c@move, 1)\n    items.0@follow = 7")]
    [InlineData("    c@follow = 7\n    let items = (b@move, 1)\n    items.0@follow = 42")]
    [InlineData("    var entries = [1: c@move]\n    entries[1]@follow = 7")]
    public void APlacementWithoutALiveChildIsAccepted(string body)
    {
        var c = MinimalEmissionTest.Analyze(Run + body);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    // The MIR's record (H5) is at the placed `b` and relates the Reborrow that created `c`'s Loan and the literal's next use, the key `2`,
    // with no Note: the placement Note ("acquired exclusively where the literal places it") is a fact A6 publishes later.
    [Fact]
    public void TheConflictNamesThePlacement()
    {
        const string Source = Run + "    var entries = [1: c@move, 2: b@move]\n    entries[2]@follow = 42\n    entries[1]@follow = 7\npublic func main()\n    var value = 0\n    run(value@uniq)\n";
        var record = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), Source.IndexOf("b@move", StringComparison.Ordinal), 1), (record.Code, record.Span!.Value.Start, record.Span.Value.Length));
        Assert.Null(record.Note);
        Assert.Equal(
            [("borrow", Source.IndexOf("b@follow@uniq", StringComparison.Ordinal), 13, "borrow that created the loan"),
                ("loan", Source.IndexOf("2: b@move", StringComparison.Ordinal), 1, "later use of the value retaining the conflicting loan")],
            record.Related!.Select(static x => (x.Role, x.Span!.Value.Start, x.Span.Value.Length, x.Label)));
    }
}
