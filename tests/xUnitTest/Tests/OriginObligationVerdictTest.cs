// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Verification;
using Xunit;

namespace XunitTest;

// PLAN G74 U4: every Binding obligation records the check that discharges it and receives one verdict per analysis of a Binding;
// an unchecked obligation is an ownership record, so the analysis result that emission reads is unverified.
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
        Assert.Equal(code is null, c.Ownership.Result.IsVerified);
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
        Assert.True(c.Ownership.Result.IsVerified);
    }

    // PLAN G74 U4: a rebind invalidates the verdicts, so nothing is verified until the analysis runs again.
    [Fact]
    public void ARebindInvalidatesTheVerdicts()
    {
        var (c, _) = Analyze(OriginProofWorkloads.Create("valid", 1));
        Assert.True(c.Ownership.Result.IsVerified);
        c.Bind();
        Assert.False(c.Ownership.Result.IsVerified);
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        Assert.True(c.Ownership.Result.IsVerified);
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
