// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class FiniteRegionCompositionTest
{
    private const string Values = "var a = 1\nvar b = 2\nlet root = a@ref\n";
    private const string Identity = "let f = func (value: ref/i32 during root) -> ref/i32 during root => value\n";
    private const string Read = "require view@follow == 2 else => $abort(\"view\")\n";
    private const string Store = "func put<T>(slot: uniq/T, value: T) => slot@follow = value@move\nfunc none() -> Option<ref/i32 during source> => .None\nvar a = none()\nvar b = none()\nvar slot = a@uniq\nslot = b@uniq\nvar n = 2\nput(slot, .Some(n@ref))\n";
    private const string ReadStored = "match b\n    .Some(let value) => require value@follow == 2 else => $abort(\"value\")\n    .None => $abort(\"none\")\n";

    [Theory]
    [InlineData("Annotation", Values + "let view: ref/i32 during root = b@ref\n" + Read)]
    [InlineData("ValueCall", Values + Identity + "let view = f(b@ref)\n" + Read)]
    [InlineData("BeforeBorrow", Values + "b = 2\nlet view: ref/i32 during root = b@ref\n" + Read)]
    [InlineData("AnonymousInput", "let f = func (pair: (ref/i32, i32)) => pair.0\nlet n = 7\nrequire f((n@ref, 1))@follow == 7 else => $abort(\"closure\")")]
    [InlineData("IndirectTargets", Store + ReadStored)]
    public void FiniteAndInferredRegionsShareValueFlow(string name, string source)
        => ScalarEmissionTest.EmitFixture("FiniteRegionComposition" + name, source, string.Empty);

    // The `use` row expects the old rule: `f()`'s result `during x` keeps no Loan live (SPEC 15.6) and the call retains no argument
    // (15.6.4, 15.6.7), so `x@follow = 9` is valid, as the MIR finds; the default pipeline keeps the old rule until R6-check (PLAN G147
    // (7), class (a), X-1's; DECISIONS R4-M136).
    [Theory]
    [Trait("OldRule", "ledger:a5815e667296:4")]
    [InlineData(Values + "let view: ref/i32 during root = b@ref\nb = 3\n" + Read, "b = 3", "b = 3")]
    [InlineData(Values + Identity + "let view = f(b@ref)\nb = 3\n" + Read, "b = 3", "b = 3")]
    [InlineData("var n = 7\nlet f = func (pair: (ref/i32, i32)) => pair.0\nlet view = f((n@ref, 1))\nn = 8\nrequire view@follow == 7 else => $abort(\"closure\")", "n = 8", "n = 8")]
    [InlineData(Store + "n = 3\n" + ReadStored, "n = 3", "n = 3")]
    [InlineData("func use(x: uniq/i32, f: ref/(() -> ref/i32 during x)) -> i32\n    var old = x@follow@ref\n    let view = f()\n    x@follow = 9\n    return view@follow\npublic func main() => ()", "x")]
    public void EveryTransferredBorrowStillProtectsItsSource(string source, string at, string? mir = null)
    {
        var c = MinimalEmissionTest.Analyze(source, "Program.kimi");
        Assert.Contains(c.Problems, x => x.Code == DiagnosticCode.ComparisonLoanConflict_Kd && c.SyntaxOf(x.Subject).ToString() == at);
        Assert.DoesNotContain(c.Problems, static x => x.Code == DiagnosticCode.Unsupported_Kd);
        MirTargetSide.Check(c, mir is null ? [] : [(DiagnosticCode.ComparisonLoanConflict_Kd, new(mir))]);
    }

    [Theory]
    [InlineData(Values + "let view: ref/i32 during root = b@ref\n" + Read)]
    [InlineData(Store + ReadStored)]
    [Trait("Purpose", "Allocation")]
    public void WarmFiniteGraphsAndIndirectStoresAllocateNothing(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var expected = CompilationTestHelper.WriteIr(c);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
        Assert.Equal(expected, CompilationTestHelper.WriteIr(c));
    }
}
