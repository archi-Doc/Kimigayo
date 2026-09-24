// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// These regressions read the normative example itself, so a stale copied test cannot mask drift.
public class VerifiedSpecExampleTest
{
    [Fact]
    public void ComparisonInspectionUsesExplicitTransferAndKeepsItsLoan()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "../../../../spec/13-operators-and-assignment.md");
        var text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        const string marker = "<!-- verified-example: comparison-inspection (positive; commented line is a rejection) -->";
        var markerStart = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerStart >= 0);
        Assert.Equal(markerStart, text.LastIndexOf(marker, StringComparison.Ordinal));
        var start = text.IndexOf("```kimi\n", markerStart, StringComparison.Ordinal) + "```kimi\n".Length;
        var end = text.IndexOf("\n```", start, StringComparison.Ordinal);
        var source = text[start..end];
        ScalarEmissionTest.EmitFixture("SpecExampleComparisonInspection", source, "a\n");
        const string rejection = "// let invalid = text == take(text@move)";
        Assert.Contains(rejection, source);
        var c = MinimalEmissionTest.Analyze(source.Replace(rejection, rejection[3..], StringComparison.Ordinal));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
