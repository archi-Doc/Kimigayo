// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 17.4.3, 23.3.6.9: a discarded Result statement offers `_ = try ` where the enclosing function's failure return target takes
// its error Type and `_ = ` always; a discarded try success value offers `_ = `; neither carries a condition, and neither is
// offered where the expression is not a direct item of an indented body, where the catalog's Advice describes every repair.
public class DiscardRepairTest
{
    private const string Source = "func source() -> Result<i32, i32> => .Ok(1)\n";

    [Theory]
    [InlineData("Result<(), i32>", "source()", "DiscardedResult_Kd", "Repair.PropagateFailure:_ = try ,Repair.ExplicitDiscard:_ = ", "Handle the Result with match")]
    [InlineData("Result<(), string>", "source()", "DiscardedResult_Kd", "Repair.ExplicitDiscard:_ = ", "Handle the Result with match")]
    [InlineData("()", "source()", "DiscardedResult_Kd", "Repair.ExplicitDiscard:_ = ", "Handle the Result with match")]
    [InlineData("Result<(), i32>", "try source()", "UnusedTrySuccess_Kd", "Repair.ExplicitDiscard:_ = ", "Use the extracted value")]
    public void ADiscardedStatementOffersItsRepairs(string result, string statement, string code, string repairs, string advice)
    {
        var tail = result == "()" ? string.Empty : "\n    return .Ok(())";
        var source = Source + $"func run() -> {result}\n    {statement}{tail}\npublic func main() => ()\n";
        var warning = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(code, warning.Code);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains(advice, warning.Advice);
        Assert.Equal(repairs.Split(','), warning.Repairs!.Select(static x => $"{x.Kind}:{Assert.Single(x.Edits).Text}"));
        foreach (var repair in warning.Repairs!)
        {
            Assert.Empty(repair.Verified);
            Assert.Empty(repair.Required);
            Assert.Equal(source.LastIndexOf(statement, StringComparison.Ordinal), Assert.Single(repair.Edits).Span.Start);
            Assert.Empty(DiagnosticCorpus.Check(UnnecessaryUnsafeBlockTest.Apply(source, repair.Edits)).Diagnostics);
        }
    }

    // A single-item body and a parenthesized expression are not direct items of an indented body: no candidate, and the Advice
    // of the catalog describes the repairs.
    [Theory]
    [InlineData("if true => source()")]
    [InlineData("(source())")]
    public void AnExpressionOutsideAStatementPositionOffersNothing(string statement)
    {
        var source = Source + $"func run() -> Result<(), i32>\n    {statement}\n    return .Ok(())\npublic func main() => ()\n";
        var warning = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics, static x => x.Code == nameof(DiagnosticCode.DiscardedResult_Kd));
        Assert.Null(warning.Repairs);
        Assert.Contains("_ = try", warning.Advice);
    }
}
