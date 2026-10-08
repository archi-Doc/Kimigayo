// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Verification;
using Xunit;

namespace XunitTest;

// PLAN G74 U4: every Binding obligation records the check that discharges it and receives one verdict per analysis of a Binding,
// which ownership reporting and the emission check both read.
public class OriginObligationVerdictTest
{
    public static TheoryData<string, string?> Families => OriginProofScalingTest.Families;

    // SPEC 15.6.1: each obligation is judged once per analysis; reading the verdicts again judges nothing, and a rebind judges each
    // of its obligations once more.
    [Theory]
    [MemberData(nameof(Families))]
    public void EachObligationIsJudgedOncePerAnalysis(string family, string? code)
    {
        var (c, metrics) = Analyze(OriginProofWorkloads.Create(family, 2));
        var count = c.Binding.Result.IsComplete ? c.Binding.Obligations.Count : 0; // Ownership analyzes only a complete Binding.
        Assert.Equal(count, metrics.Verdicts);
        Assert.Equal(code is null, c.Ownership.SupportsOriginObligations());
        c.Ownership.ReportDiagnostics();
        Assert.Equal(count, metrics.Verdicts);

        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        Assert.Equal(2 * count, metrics.Verdicts);
    }

    // SPEC 8.1.2, 15.6.4: the well-formedness of a direct borrowed struct input belongs to each call's borrow formation, recorded
    // where the input's Type is bound; Origins nested deeper in the input are judged.
    [Fact]
    public void ADirectBorrowedInputBelongsToItsCalls()
    {
        var (c, _) = Analyze(OriginProofWorkloads.Create("valid", 2));
        var calls = c.Binding.Obligations.Where(static x => x.Discharge == OriginDischarge.CallBorrows && x.Shorter?.Binder is FunctionKoto { Name: "g" }).ToArray();
        Assert.Equal(2, calls.Length); // The Holder inputs h1 and h2.
        Assert.All(calls, static x => Assert.Equal(OriginKind.Input, x.Shorter!.Kind));
        Assert.True(c.Ownership.SupportsOriginObligations());
    }

    // PLAN G74 U4: verdicts read after their inputs changed are an internal invariant failure, never judged again; a rebind
    // invalidates them, so nothing is supported until the analysis runs again.
    [Fact]
    public void VerdictsReadAfterTheirInputsChangedFail()
    {
        var (c, _) = Analyze(OriginProofWorkloads.Create("valid", 1));
        Assert.True(c.Ownership.SupportsOriginObligations());
        var version = typeof(Binding).GetField("originStateVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        version.SetValue(c.Binding, (int)version.GetValue(c.Binding)! + 1);
        Assert.Throws<InvalidOperationException>(() => c.Ownership.SupportsOriginObligations());

        c.Bind();
        Assert.False(c.Ownership.SupportsOriginObligations());
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        Assert.True(c.Ownership.SupportsOriginObligations());
    }

    private static (Compilation Compilation, OriginProofMetrics Metrics) Analyze(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("verdict.kimi", source));
        var metrics = new OriginProofMetrics();
        c.Binding.OriginProofMetrics = metrics;
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        return (c, metrics);
    }
}
