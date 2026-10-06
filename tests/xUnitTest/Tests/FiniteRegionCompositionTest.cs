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

    [Theory]
    [InlineData(Values + "let view: ref/i32 during root = b@ref\nb = 3\n" + Read, "b = 3")]
    [InlineData(Values + Identity + "let view = f(b@ref)\nb = 3\n" + Read, "b = 3")]
    [InlineData("var n = 7\nlet f = func (pair: (ref/i32, i32)) => pair.0\nlet view = f((n@ref, 1))\nn = 8\nrequire view@follow == 7 else => $abort(\"closure\")", "n = 8")]
    [InlineData(Store + "n = 3\n" + ReadStored, "n = 3")]
    [InlineData("func use(x: uniq/i32, f: ref/(() -> ref/i32 during x)) -> i32\n    var old = x@follow@ref\n    let view = f()\n    x@follow = 9\n    return view@follow\npublic func main() => ()", "x")]
    public void EveryTransferredBorrowStillProtectsItsSource(string source, string at)
    {
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Contains(errors, x => x.Code == nameof(DiagnosticCode.ComparisonLoanConflict_Kd) && source.Substring(x.Span!.Value.Start, x.Span.Value.Length) == at);
        Assert.DoesNotContain(errors, x => x.Category == DiagnosticCategory.Unsupported);
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
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(expected, CompilationTestHelper.WriteIr(c));
    }
}
