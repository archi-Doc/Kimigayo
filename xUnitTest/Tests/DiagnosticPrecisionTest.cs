// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class DiagnosticPrecisionTest
{
    [Fact]
    public void IndependentErrorsRemainVisibleAfterAnUnknownIterationSubject()
    {
        const string source = """
            public func main()
                var sum: i32 = 0
                for value in missing
                    sum = sum + value
                let wrong: i32 = true
                absent()
            """;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Issues.Count == 3, string.Join("\n", c.Binding.Issues));
        Assert.Equal(2, c.Binding.Issues.Count(x => x.Code == Kimi.DiagnosticCode.UnresolvedBinding_Kd));
        Assert.Single(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.TypeMismatch_Kd);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData(4, "var counter =", "let counter =", "InvalidAssignment_Kd")]
    [InlineData(8, "public group Storage", "group Storage", "InaccessibleBinding_Kd")]
    [InlineData(9, "func [target] (value: i32)", "func [target] (value: bool)", "NoApplicableOverload_Kd")]
    [InlineData(9, "find<5, i32>", "find<4, i32>", "NoApplicableOverload_Kd")]
    [InlineData(11, "result = result + forward<T>(values[index]@ref/T)", "let copied: T = values[index]\n            result = result + forward<T>(values[index]@ref/T)", "TransferRequired_Kd")]
    [InlineData(12, "func [offset] ()", "func [] ()", "InvalidCaptureBinding_Kd")]
    [InlineData(14, "accumulator@uniq/Pipeline.Accumulator", "accumulator@uniq/Pipeline.Job", "InvalidAssignment_Kd")]
    public void MilestoneFaultsHaveSpecificDiagnosticsWithoutDependentCascades(int number, string before, string after, string expected)
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, $"../../../../milestones/Milestone{number}.kimi"));
        Assert.Contains(before, source);
        var c = MinimalEmissionTest.Analyze(source.Replace(before, after, StringComparison.Ordinal));
        var codes = c.Binding.Issues.Select(x => x.Code.ToString()).Concat(c.Ownership.Issues.Select(x => x.Failure + "_Kd")).ToArray();
        var detail = string.Join("\n", c.Binding.Issues.Select(x => x.Code + ": " + x.Node)) + "\n" + string.Join("\n", c.Ownership.Issues);
        Assert.True(codes.Length > 0 && codes.All(x => x == expected), detail);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
