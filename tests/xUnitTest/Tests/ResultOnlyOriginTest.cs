// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 15.3.6: in an executable body, a callee Origin that nothing at the call bounds, such as the result-only `s` of
// `none() -> Option<ref/i32 during s>` at a call whose result initializes an unannotated local, is a local region of the call. It needs
// no annotation, is never replaced by static and never makes the candidate pending; no input supplies it, so it holds no Loans. A use
// that stores a borrow contributes its Loan to the local region.
public class ResultOnlyOriginTest
{
    private const string Main = "public func main() => ()\n";

    private const string None = "func none() -> Option<ref/i32 during s> => .None\n";

    // v7/s4: the call was the fact-less UnprovenConstraint_Kd at the call.
    [Theory]
    [InlineData("func constant() -> ref/i32 during s => $abort(\"never\")\nfunc f() -> i32\n    let k = constant()\n    return k@follow\n")]
    [InlineData("func constant() -> ref/i32 during s => $abort(\"never\")\nfunc f() -> i32\n    let k = constant()\n    let x: i32 = 1\n    let j = constant()\n    return k@follow + j@follow + x\n")]
    [InlineData(None + "func f() -> i32\n    var k = none()\n    k = none()\n    return 1\n")]
    [InlineData("func make<T>() -> Option<ref/T during s> => .None\nfunc f() -> i32\n    let g = make<i32>()\n    return 1\n")]
    [InlineData(None + "func keep(o: Option<ref/i32 during x>) -> Option<ref/i32 during x> => o@move\nfunc f() -> i32\n    let w = keep(none())\n    return 1\n")]
    [InlineData(None + "group G\n    public func make() -> Option<ref/i32 during s> => .None\nfunc f() -> i32\n    let m = G.make()\n    return 1\n")]
    public void AResultOnlyOriginIsALocalRegionOfTheCall(string body)
    {
        var check = DiagnosticCorpus.Check(body + Main);
        Assert.Empty(check.Diagnostics);
    }

    // Each call has its own region, an open Inference atom that is never displayed.
    [Fact]
    public void EachCallHasItsOwnRegion()
    {
        var c = MinimalEmissionTest.Analyze(None + "let a = none()\nlet b = none()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Where(static x => x.CallOf()?.Target.Name == "none").ToArray();
        Assert.Equal(2, calls.Length);
        var first = calls[0].TypeOf()!.Components[0].Origin!;
        var second = calls[1].TypeOf()!.Components[0].Origin!;
        Assert.Equal((OriginKind.Inference, true), (first.Kind, first.Open));
        Assert.NotSame(first, second);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 15.3.6: an expected result fixes the Origin instead, at a return or an outer argument, and a fixed chain is accepted. A clause
    // that bounds the Origin keeps it out of the local region: the call's result is over the bound, judged at its destination.
    [Fact]
    public void AnExpectedResultFixesTheResultOnlyOrigin()
    {
        var program = None + "func keep(o: Option<ref/i32 during x>) -> Option<ref/i32 during x> => o@move\nfunc fixedResult(x: ref/i32) -> Option<ref/i32 during x> => none()\nfunc staticResult() -> Option<ref/i32 during static> => none()\n" +
            "var k = none()\nk = none()\nlet w = keep(none())\nlet a: i32 = 4\nlet f = fixedResult(a@ref)\nlet s = staticResult()\nmatch k\n    .Some(let v) => $abort(\"k\")\n    .None => ()\nmatch w\n    .Some(let v) => $abort(\"w\")\n    .None => ()\nmatch f\n    .Some(let v) => $abort(\"f\")\n    .None => ()\nmatch s\n    .Some(let v) => $abort(\"s\")\n    .None => ()";
        ScalarEmissionTest.EmitFixture("ResultOnlyOriginLocalRegion", program, string.Empty);

        var source = "func bounded(a: ref/i32) -> ref/i32 during s\n    origin a outlives s\n    return a\nfunc caller(p: ref/i32) -> ref/i32 during static => bounded(p)\nfunc local(p: ref/i32) -> i32\n    let k = bounded(p)\n    return k@follow\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "bounded(p)", "requires p outlives static, which is not proven"), (error.Code, Text(source, error.Span), error.Label));
        Assert.Equal("fit", error.Reason![3].Value);
    }

    // SPEC 15.3.6, 15.6.5: a result-only region starts without Loans, then receives the actual stored values.
    [Theory]
    [InlineData("Store", None + "func f() -> i32\n    var k = none()\n    let y: i32 = 3\n    k = .Some(y@ref)\n    return 1\n")]
    [InlineData("IndirectStore", None + "func put<T>(slot: uniq/T, v: T) -> ()\n    slot@follow = v@move\nfunc f() -> i32\n    var q = none()\n    let y: i32 = 3\n    put(q@uniq, .Some(y@ref))\n    return 1\n")]
    [InlineData("EmptyStatic", None + "func needsStatic(o: Option<ref/i32 during static>) -> i32 => 1\nfunc f() -> i32 => needsStatic(none())\n")]
    public void LocalRegionsComposeWithStoresAndEmptyResults(string name, string body)
        => ScalarEmissionTest.EmitFixture("ResultOnlyRegion" + name, body + "public func main() => require f() == 1 else => $abort(\"region\")", string.Empty);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmResultOnlyCallsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(None + "func keep(o: Option<ref/i32 during x>) -> Option<ref/i32 during x> => o@move\nvar k = none()\nk = none()\nlet w = keep(none())\nmatch w\n    .Some(let v) => $abort(\"w\")\n    .None => ()");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
}
