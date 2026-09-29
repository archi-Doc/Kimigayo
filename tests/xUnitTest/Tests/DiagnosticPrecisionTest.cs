// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class DiagnosticPrecisionTest
{
    public static TheoryData<string> MutationNames => [.. DiagnosticCorpus.Mutations.Select(static x => x.Name)];

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
    [MemberData(nameof(MutationNames))]
    public void MilestoneFaultsHaveSpecificDiagnosticsWithoutDependentCascades(string name)
    {
        var mutation = DiagnosticCorpus.Mutation(name);
        var c = MinimalEmissionTest.Analyze(DiagnosticCorpus.Apply(mutation));
        var codes = c.Binding.Issues.Select(x => x.Code.ToString()).Concat(c.Ownership.Issues.Select(x => x.Code.ToString())).ToArray();
        var detail = string.Join("\n", c.Binding.Issues.Select(x => x.Code + ": " + x.Node)) + "\n" + string.Join("\n", c.Ownership.Issues);
        Assert.True(codes.Length > 0 && codes.All(mutation.Expected.Contains), detail);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
