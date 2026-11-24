// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class PatternCoverageCompletionTest
{
    [Fact]
    public void LateInvalidTypeSuppressesMissingCoverageCascade()
    {
        var c = MinimalEmissionTest.Analyze(Source("internal"));
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.Access);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.NonExhaustiveMatch);
        Assert.False(c.Bind().IsComplete);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.NonExhaustiveMatch);
        var restored = CompilationTestHelper.Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.DoesNotContain(restored.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.NonExhaustiveMatch);
    }

    // SPEC 9.4.4, 14.8.4, 23.1.2 (PLAN G1321): the late refutation of `E<Source>` (public E's requirement exposes Hidden) is at a nested
    // payload position under a proven root `F`, and the late recheck judges only the root, so the coverage stays the Patterns': `.D`'s
    // absence reads nothing of E, an independent NonExhaustiveMatch_Kd (before: the per-position recheck set the coverage Invalid).
    [Fact]
    public void ALateNestedRefutationKeepsTheMissingCoverage()
    {
        var c = MinimalEmissionTest.Analyze("contract Hidden\npublic struct Source\n    Self is Hidden\npublic enum E<T>\n    T is Hidden\n    A(i32)\n    B\nenum F\n    C(E<Source>, i32)\n    D\nfunc f(v: F) -> i32\n    return match v\n        .C(.A(let n), _) => n");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.Access);
        Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.NonExhaustiveMatch);
    }

    [Fact]
    public void ValidTypeStillRequiresMissingCaseCoverage()
    {
        var c = MinimalEmissionTest.Analyze(Source("public"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.NonExhaustiveMatch);
        Assert.False(c.Binding.CheckBound().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.NonExhaustiveMatch);
        var restored = CompilationTestHelper.Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Contains(restored.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.NonExhaustiveMatch);
    }

    [Fact]
    public void AddingMissingCaseRestoresValidMatch()
    {
        var c = MinimalEmissionTest.Analyze(Source("public") + "\n        .B => ()");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Bind().IsComplete);
    }

    [Fact]
    public void EditingCoverageReplacesTheCaseFactAndRetiresTheFailure()
    {
        const string Source = "func f(x: Option<i32>) => match x\n    .None => ()";
        var c = MinimalEmissionTest.Analyze(Source);
        var original = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<MatchKoto>());
        c.Binding.ReportDiagnostics();
        Assert.Equal("0", Assert.Single(c.Diagnostics.Finalize().Diagnostics, x => x.Code == "NonExhaustiveMatch_Kd").Reason![1].Value);
        foreach (var pattern in new[] { ".Some(_)", "_" })
        {
            var donor = MinimalEmissionTest.Analyze(Source.Replace(".None", pattern, StringComparison.Ordinal));
            var replacement = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<MatchKoto>()).Arms[0].Pattern;
            ParseTestHelper.Detach(replacement, new CodeBlockKoto(original.CodeContext));
            Assert.True(KotoHelper.Replace(original, original.Arms[0].Pattern, replacement));
            Assert.Equal(pattern == "_", c.Bind().IsComplete);
            c.Binding.ReportDiagnostics();
            var records = c.Diagnostics.Finalize().Diagnostics.Where(x => x.Code == "NonExhaustiveMatch_Kd");
            if (pattern == "_")
            {
                Assert.Empty(records);
            }
            else
            {
                var record = Assert.Single(records);
                Assert.Equal(new DiagnosticValue("case", DiagnosticValueKind.Number, "1"), record.Reason![1]);
                Assert.Equal("The Case at declaration index 1 has no arm.", record.Note);
            }
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmCoverageFailureAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source("public"));
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Nonexhaustive match was accepted.");
            }
        }));
    }

    private static string Source(string access)
        => access + " contract Hidden\npublic struct Value\n    Self is Hidden\npublic enum E<T>\n    T is Hidden\n    A\n    B\ngroup Consumer\n    func inspect(value: E<Value>) => match value@move\n        .A => ()";
}
