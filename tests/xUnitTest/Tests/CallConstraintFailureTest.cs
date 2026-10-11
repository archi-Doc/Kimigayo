// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

public class CallConstraintFailureTest
{
    [Theory]
    [InlineData("string", false, false)]
    [InlineData("string", true, false)]
    [InlineData("i32", false, true)]
    [InlineData("i32", true, true)]
    public void CallsKeepTheSubstitutedFailedCondition(string type, bool block, bool valid)
    {
        var member = block ? "    Self is Marker when T is Copy\n        public func read() -> i32 => 1\n"
            : "    public func read() -> i32\n        T is Copy\n        return 1\n";
        var source = "contract Marker\nstruct S<T>\n" + member + $"let result = S<{type}>.read()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        if (!valid)
        {
            var record = Assert.Single(TestDiagnostics.Of(c));
            Assert.Equal("NoApplicableOverload_Kd", record.Code);
            Assert.Contains("Copy", record.Note, StringComparison.Ordinal);
            Assert.Contains("string", record.Note, StringComparison.Ordinal);
            var published = Assert.Single(c.Diagnostics.Finalize().Diagnostics);
            var clause = Assert.Single(published.Related!, x => x.Role == "constraint");
            Assert.Equal("T is Copy", source.Substring(clause.Span!.Value.Start, clause.Span.Value.Length));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnreachedConditionDoesNotExplainAnInputMismatch(bool overload)
    {
        var source = "func read<T>(value: bool) -> i32\n    T is Copy\n    return 1\n" +
            (overload ? "func read<T>(value: i32) -> i32\n    T is Copy\n    return 2\n" : string.Empty) +
            "let result = read<string>(\"wrong\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var record = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("NoApplicableOverload_Kd", record.Code);
        Assert.DoesNotContain("refuted", record.Note ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedConditionsDoNotDisplaceAnApplicableOverload()
    {
        var c = MinimalEmissionTest.Analyze("func read<T>(value: ref/T) -> i32\n    T is Copy\n    return 1\nfunc read(value: ref/string) -> i32 => 2\nlet text = \"text\"\nlet result = read(text@ref)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Empty(c.Binding.Issues);
    }
}
