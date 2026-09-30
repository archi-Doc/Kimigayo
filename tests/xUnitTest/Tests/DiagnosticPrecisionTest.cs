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

    // DIAGNOSTICS.md rule 3: an implementation limit is Unsupported where it is met; a check that rests on a failed Type
    // (expected Type, candidate signature, generic Name) is derived and published only through that failure.
    [Theory]
    [InlineData("func set(a: uniq/[3 of i32])\n    a[0] = 5\npublic func main() => ()", "UnsupportedBinding_Kd", "a[0] = 5")]
    [InlineData("func set(a: uniq/Array<i32>)\n    a[0] += 5\npublic func main() => ()", "UnsupportedBinding_Kd", "a[0] += 5")]
    [InlineData("struct S\n    public let v: [2 of i32]\n    public init() => self.v = [1, 2]\nfunc set(s: uniq/S)\n    s.v[0] = 5\npublic func main() => ()", "InvalidAssignment_Kd", "s.v[0] = 5")]
    [InlineData("func f(w: Weak<i32>) -> i32 => 0\npublic func main() => ()", "UnsupportedBinding_Kd", "Weak")]
    [InlineData("public func main()\n    let r = Kimi.Intrinsics.makeRc(1)", "UnsupportedBinding_Kd", "Kimi.Intrinsics.makeRc")]
    [InlineData("public func main()\n    let r = Kimi.Intrinsics.nothing(1)", "UnresolvedBinding_Kd", "Kimi.Intrinsics.nothing")]
    [InlineData("func f(w: Missing<i32>) -> i32 => 0\npublic func main() => ()", "UnresolvedBinding_Kd", "Missing")]
    [InlineData("public func main()\n    let w: Option<Missing> = .None", "UnresolvedBinding_Kd", "Missing")]
    [InlineData("public func main()\n    var w: Missing = .None\n    w = .Some(1)", "UnresolvedBinding_Kd", "Missing")]
    [InlineData("func make() -> Missing => .None\npublic func main()\n    let v = make()", "UnresolvedBinding_Kd", "Missing")]
    [InlineData("func make() -> Missing\n    return .None\npublic func main()\n    let v = make()", "UnresolvedBinding_Kd", "Missing")]
    public void LimitsAndDependentChecksPublishOneSpecificError(string source, string code, string text)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(code, error.Code);
        Assert.Equal(text, error.Text);
    }

    // A recovered chained comparison is not type-checked, a control-flow check that repeats Binding's is derived, and a
    // structural control-flow check stays independent of a Binding failure at the same place.
    [Theory]
    [InlineData("public func main()\n    let ok = 1 < 2 < 3\n    let wrong: i32 = true\n    missing()", "ChainedComparison_Kd,TypeMismatch_Kd,UnresolvedBinding_Kd")]
    [InlineData("func f() -> i32\n    let wrong: i32 = true\npublic func main() => ()", "FunctionFallthrough_Kd,TypeMismatch_Kd")]
    [InlineData("public func main() -> Missing\n    return ()", "UnresolvedBinding_Kd")]
    public void EachProblemPublishesOneErrorAcrossPhases(string source, string codes)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        c.Binding.ReportStartupDiagnostics();
        c.Ownership.ControlFlow!.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        var errors = TestDiagnostics.Of(c).Where(static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error).Select(static x => x.Code).Order(StringComparer.Ordinal);
        Assert.Equal(codes.Split(','), errors);
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
