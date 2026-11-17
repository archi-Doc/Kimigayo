// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Verification;
using Xunit;

namespace XunitTest;

// PLAN G74 U4: every Binding obligation records the check that discharges it; an unchecked obligation is an ownership record, so the
// analysis result that emission reads is unverified.
public class OriginObligationVerdictTest
{
    public static TheoryData<string, string?> Families => OriginProofScalingTest.Families;

    // SPEC 15.6.1: check's verdict on each family is Accepts with the family's one record, Binding's or, on a complete Binding, the
    // MIR's (R6-check H5, DECISIONS R5-M185), and a rebind and a new analysis give the same verdict.
    [Theory]
    [MemberData(nameof(Families))]
    public void EachFamilyKeepsItsOneRecordAcrossARebind(string family, string? code)
    {
        var c = Analyze(OriginProofWorkloads.Create(family, 2));
        Assert.Empty(TestDiagnostics.Of(c)); // Nothing is published before the reports, so no syntax record (Analyze checks none).
        DiagnosticCode[] expected = code is null ? [] : [Enum.Parse<DiagnosticCode>(code)];
        Assert.Equal(code is null, c.Accepts);
        Assert.Equal(expected, c.Binding.Issues.Select(static x => x.Code).Concat(c.Problems.Select(static x => x.Code)));
        c.ReportAnalysis();

        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        Assert.Equal(code is null, c.Accepts);
        Assert.Equal(expected, c.Binding.Issues.Select(static x => x.Code).Concat(c.Problems.Select(static x => x.Code)));
    }

    // SPEC 8.1.2, 15.6.4: the well-formedness of a direct borrowed struct input belongs to each call's borrow formation; Origins nested
    // deeper in the input are judged. Binding's declaration judge leaves the signature's rows to the calls (DECISIONS BU-26), so none stays
    // an obligation, and the calls verify.
    [Fact]
    public void ADirectBorrowedInputBelongsToItsCalls()
    {
        var c = Analyze(OriginProofWorkloads.Create("valid", 2));
        Assert.DoesNotContain(c.Binding.Obligations, static x => x.Discharge == OriginDischarge.CallBorrows && x.Shorter?.Binder is FunctionKoto { Name: "g" });
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Analysis.IsVerified);
    }

    // PLAN G74 U4: a rebind invalidates the verdicts, so nothing is verified until the analysis runs again.
    [Fact]
    public void ARebindInvalidatesTheVerdicts()
    {
        var c = Analyze(OriginProofWorkloads.Create("valid", 1));
        Assert.True(c.Analysis.IsVerified);
        c.Bind();
        Assert.False(c.Analysis.IsVerified);
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        Assert.True(c.Analysis.IsVerified);
    }

    // DECISIONS BU-85: every obligation other than an Origin relation with both ends is proven where it forms or discharged when its
    // local binds, so Binding's Final check reports one left over bound parts as InternalInvariant_Kd at its use (a contract; only a
    // tampered list reaches it).
    [Fact]
    public void AnUnjudgedObligationIsAnInternalInvariantAtItsUse()
    {
        var c = Analyze("func f() -> isize\n    let n: isize = 4\n    return n\n");
        Assert.True(c.Binding.Result.IsComplete);
        var use = KotoTree.Walk(c.Kotonoha.RootKoto).Last(static x => x is IdentifierNameKoto && x.ToString() == "n");
        var compound = new BoundLength(LengthOperation.Sub, 0, null, new(LengthOperation.Constant, 1, null, null, null), new(LengthOperation.Constant, 2, null, null, null));
        BindingObligation obligation = new(BindingObligationKind.TypeFormation, use, BindingDeadline.Instantiation, Length: compound);
        typeof(Binding).GetMethod("AddObligation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(c.Binding, [obligation]);
        Assert.False(c.Binding.CheckBound().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InternalInvariant_Kd && ReferenceEquals(x.Node, use));
    }

    // SPEC 15.3.6, 15.4.4 (DECISIONS BU-85, rule W): an Origin a local's Type omits is inferred when the local binds; when its
    // initializer fails, the inference is left resting on that failure and adds no record. A valid local leaves none.
    [Theory]
    [InlineData("func f()\n    var x = 1\n    let p = x@ref\n    let r: ref/i32 = bogus(p)\n", false)]
    [InlineData("func f(c: bool)\n    let x = 1\n    let r: ref/i32 = if c => x@ref else => missing\n", false)]
    [InlineData("func f()\n    let x = 1\n    let r: ref/i32 = x@ref\n", true)]
    public void AnOriginInferenceRestsOnItsLocalsFailure(string source, bool valid)
    {
        var c = Analyze(source);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Code == DiagnosticCode.InternalInvariant_Kd);
        Assert.Equal(!valid, c.Binding.Obligations.Any(x => x.Kind == BindingObligationKind.OriginInference && !c.Binding.IsVerifiedOriginObligation(x)));
    }

    private static Compilation Analyze(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("verdict.kimi", source));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        return c;
    }
}
