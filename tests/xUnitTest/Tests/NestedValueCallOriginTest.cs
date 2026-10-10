// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class NestedValueCallOriginTest
{
    [Theory]
    [InlineData("let f = pin")]
    [InlineData("let item = pin\n    let f = item@ref")]
    public void ASelectedItemReportsItsFailedClauseAtTheSupplyingArgument(string reference)
    {
        var source = "func pin(p: ref/i32) -> ref/i32 during static\n    origin p outlives static\n    return p\nfunc use()\n    let n = 1\n    " + reference + "\n    _ = f(n@ref)\npublic func main() => ()";
        var check = DiagnosticCorpus.Check(source);
        var error = Assert.Single(check.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), error.Code);
        Assert.Equal("n@ref", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal("declared", error.Reason!.Single(x => x.Name == "source").Value);
        Assert.Contains(error.Related!, x => x.Role == "relation" && source.Substring(x.Span!.Value.Start, x.Span.Value.Length) == "origin p outlives static");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(new(check.Diagnostics, check.Sources), string.Empty);
        Assert.Contains("requires the borrow n@ref outlives static, which is false", console.Text, StringComparison.Ordinal);
        Assert.Contains(" = relation: ", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(check.Sources[error.Source].Path);
        var sent = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, true)[identity]);
        Assert.Equal((error.Code, error.Display!.Range), (sent.Code, sent.Range));
        var clause = error.Related!.Single(x => x.Role == "relation");
        Assert.Contains(sent.RelatedInformation!, x => x.Location.Range == clause.Range);
    }

    [Theory]
    [InlineData("Tuple", "func pick(pair: (ref/i32 during source, i32)) -> ref/i32 during source => pair.0\nlet f = pick\nlet n = 7\nrequire f((n@ref, 1))@follow == 7 else => $abort(\"tuple\")")]
    [InlineData("Slice", "func pick(values: Slice<i32>) -> ref/i32 during values.source => values[0]@ref\nlet f = pick\nlet n: [1 of i32] = [7]\nrequire f(n[..])@follow == 7 else => $abort(\"slice\")")]
    [InlineData("NestedReference", "func pick(slot: ref/(ref/i32 during source)) -> ref/i32 during source => slot@follow\nlet f = pick\nlet n = 7\nlet view = n@ref\nrequire f(view@ref)@follow == 7 else => $abort(\"nested\")")]
    [InlineData("ResultOnly", "func empty() -> Option<ref/i32 during source> => .None\nlet f = empty\nlet n = 7\nvar value = f()\nvalue = .Some(n@ref)\nmatch value\n    .Some(let item) => require item@follow == 7 else => $abort(\"some\")\n    .None => $abort(\"none\")")]
    [InlineData("Place", "func slot(values: ref/[1 of ref/i32 during source]) -> place ref/(ref/i32 during source) => values[0]\nlet f = slot\nlet n = 7\nlet values: [1 of ref/i32] = [n@ref]\nrequire f(values@ref)@follow == 7 else => $abort(\"place\")")]
    [InlineData("DistinctCalls", "func pick(pair: (ref/i32 during source, i32)) -> ref/i32 during source => pair.0\nlet f = pick\nvar a = 1\nvar b = 2\nlet first = f((a@ref, 0))\nlet second = f((b@ref, 0))\nrequire first@follow == 1 else => $abort(\"first\")\na = 3\nrequire second@follow == 2 else => $abort(\"second\")")]
    public void EachCallSolvesEveryInputOrigin(string name, string source)
        => ScalarEmissionTest.EmitFixture("NestedValueCallOrigin" + name, source, string.Empty);

    [Theory]
    [InlineData("func pick(pair: (ref/i32 during source, i32)) -> ref/i32 during source => pair.0\nlet f = pick\nvar n = 7\nlet result = f((n@ref, 1))\nn = 8\nrequire result@follow == 7 else => $abort(\"tuple\")")]
    [InlineData("func pick(slot: ref/(ref/i32 during source)) -> ref/i32 during source => slot@follow\nlet f = pick\nvar n = 7\nlet view = n@ref\nlet result = f(view@ref)\nn = 8\nrequire result@follow == 7 else => $abort(\"nested\")")]
    [InlineData("let f = Text.validateUtf8\nvar n: Array<u8> = [65]\nlet result = f(n.slice(..))\nn[0] = 8\nmatch result@move\n    .Ok(let view) => require view.length == 1 else => $abort(\"validate\")\n    .Err(_) => $abort(\"utf8\")")]
    public void NestedResultsKeepTheirInputLoans(string source)
    {
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Contains(errors, x => x.Code == nameof(DiagnosticCode.ComparisonLoanConflict_Kd) && source.Substring(x.Span!.Value.Start, x.Span.Value.Length) is "n = 8" or "n[0] = 8");
        Assert.DoesNotContain(errors, x => x.Category == DiagnosticCategory.Unsupported);
    }

    private const string EmptyCalls = "func empty() -> Option<ref/i32 during source> => .None\nlet f = empty\nlet first = f()\nlet second = f()";

    [Fact]
    public void ResultOnlyOriginsAreFreshAtEachValueCall()
    {
        var c = MinimalEmissionTest.Analyze(EmptyCalls);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Where(static x => x.ValueCallOf() is not null).ToArray();
        Assert.Equal(2, calls.Length);
        var first = calls[0].TypeOf()!.Components[0].Origin!;
        var second = calls[1].TypeOf()!.Components[0].Origin!;
        Assert.True(first.Open && second.Open);
        Assert.NotSame(first, second);
    }

    [Theory]
    [Trait("Purpose", "Allocation")]
    [InlineData(EmptyCalls)]
    [InlineData("func pick(pair: (ref/i32 during source, i32)) -> ref/i32 during source => pair.0\nlet f = pick\nlet n = 7\nrequire f((n@ref, 1))@follow == 7 else => $abort(\"tuple\")")]
    public void WarmNestedCallsReuseInferenceAndLowering(string source)
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

    [Fact]
    public void ReadingACopyPartDoesNotEndItsStoredLoan()
    {
        const string Source = "var a = 1\nvar value = (a@ref, 7)\nlet saved = value.0\nrequire saved@follow == 1 else => $abort(\"copy\")\na = 2\nrequire value.0@follow == 1 else => $abort(\"stored\")";
        Assert.Contains(DiagnosticCorpus.Check(Source).Diagnostics, x => x.Code == nameof(DiagnosticCode.ComparisonLoanConflict_Kd));
    }
}
