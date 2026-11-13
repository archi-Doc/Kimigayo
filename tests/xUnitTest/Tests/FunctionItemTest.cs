// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class FunctionItemTest
{
    [Theory]
    [InlineData("Local", "let f = inc\nlet g = f\nrequire f(40) + g(0) == 42 else => $abort(\"copy\")")]
    [InlineData("Tuple", "let pair = (inc, 40)\nrequire pair.0(pair.1) == 41 else => $abort(\"tuple\")")]
    [InlineData("Array", "let items = [inc, inc]\nrequire items[1](41) == 42 else => $abort(\"array\")")]
    [InlineData("Borrow", "let f = inc\nlet r = f@ref\nrequire r(41) == 42 else => $abort(\"borrow\")")]
    [InlineData("Erase", "let f = inc\nlet erased: (i32) -> i32 = f\nrequire erased(40) + f(0) == 42 else => $abort(\"erase\")")]
    [InlineData("EraseArgument", "func apply(f: (i32) -> i32) -> i32 => f(41)\nlet f = inc\nrequire apply(f) == 42 else => $abort(\"argument\")")]
    [InlineData("Capture", "let f = inc\nlet closure = func [f] (v: i32) -> i32 => f(v)\nlet erased: (i32) -> i32 = closure\nrequire erased(41) == 42 and closure(40) == 41 else => $abort(\"capture\")")]
    [InlineData("Option", "let item = inc\nlet option = Option.Some(item)\nmatch option\n    .Some(let f) => require f(41) == 42 else => $abort(\"option\")\n    .None => $abort(\"none\")")]
    [InlineData("Iteration", "let items = [inc, inc]\nvar total = 0\nfor f in items@move\n    total += f(20)\nrequire total == 42 else => $abort(\"iteration\")")]
    [InlineData("Default", "func answer(value: i32 = 41) -> i32 => value + 1\nlet f = answer\nrequire f(41) == 42 else => $abort(\"explicit input\")")]
    [InlineData("SharedInput", "func read(value: ref/i32) -> i32 => value + 1\nlet f = read\nlet value = 41\nrequire f(value@ref) == 42 else => $abort(\"input\")")]
    [InlineData("Aggregate", "func sum(value: (i32, i32)) -> (i32, i32) => (value.0 + 1, value.1)\nlet f = sum\nlet result = f((40, 2))\nrequire result.0 == 41 and result.1 == 2 else => $abort(\"aggregate\")")]
    [InlineData("Qualified", "group G\n    public func answer(value: i32) -> i32 => value + 1\nlet f = G.answer\nrequire f(41) == 42 else => $abort(\"qualified\")")]
    public void StoredItemsKeepTheirDeclarationAndCopySemantics(string name, string body)
    {
        var source = "func inc(value: i32) -> i32 => value + 1\n" + body;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        ScalarEmissionTest.EmitFixture("FunctionItem" + name, source, string.Empty);
    }

    [Fact]
    public void DifferentDeclarationsHaveDifferentItemTypes()
    {
        var c = MinimalEmissionTest.Analyze("func first() => ()\nfunc second() => ()\nlet a = first\nlet b = second\nlet c = first");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var variables = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().ToArray();
        Assert.NotSame(variables[0].SymbolOf()!.Type, variables[1].SymbolOf()!.Type);
        Assert.Same(variables[0].SymbolOf()!.Type, variables[2].SymbolOf()!.Type);
    }

    [Theory]
    [InlineData("let x: i32 = inc")]
    [InlineData("func g() -> i32 => inc")]
    [InlineData("let b = inc == inc")]
    [InlineData("let f = inc\nlet r = f(value: 1)")]
    [InlineData("let f = inc\nlet r = f()")]
    [InlineData("let f = inc\nlet r = f<i32>(1)")]
    [InlineData("let f = inc\nlet bad: (bool) -> i32 = f")]
    [InlineData("let f = inc\nlet bad: (i32) -> i32 = f@ref")]
    public void ItemsDoNotInventConversionsLabelsDefaultsOrTypeParameters(string body)
    {
        var c = MinimalEmissionTest.Analyze("func inc(value: i32 = 41) -> i32 => value + 1\n" + body);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Fact]
    public void CliAndLspExplainAnItemTypeMismatch()
    {
        const string Source = "func inc(value: i32) -> i32 => value + 1\nlet number: i32 = inc";
        var path = Path.GetFullPath("item-mismatch.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), error.Code);
        Assert.Equal("inc", Source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal(["actual", "expected"], error.Reason!.Select(static x => x.Name));
        Assert.Equal(["function item inc", "i32"], error.Reason!.Select(static x => x.Value));
        Assert.Equal("expected i32, found function item inc", error.Label);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("let number: i32 = inc\n  |                   ^^^", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Code, sent.Code);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains(error.Message, sent.Message, StringComparison.Ordinal);
        }
    }

    // SPEC 15.3.4, 15.6.5, 10.7, 7.7.4: the value of a function nested in a block, whose signature names the enclosing function's fixed
    // Origin, is compared with a required Function Type under the enclosing function's clauses: at a local, a result, an assignment and
    // an argument, after an open evidence region too (PLAN G827).
    [Theory]
    [InlineData("func h(anchor0: ref/u8 during a, other: ref/u8 during b) -> i32\n    origin b outlives a\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        let f: (ref/u8 during b) -> i32 = pk\n        return f(other)\n    return 0")]
    [InlineData("func fallback(x: ref/u8 during q) -> i32 => 0\nfunc h(anchor0: ref/u8 during a, other: ref/u8 during b) -> (ref/u8 during b) -> i32\n    origin b outlives a\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        return pk\n    return fallback")]
    [InlineData("func h(anchor0: ref/u8 during a, mid: ref/u8 during c, other: ref/u8 during b) -> i32\n    origin b outlives c\n    origin c outlives a\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        let f: (ref/u8 during b) -> i32 = pk\n        return f(other)\n    return 0")]
    [InlineData("func h(anchor0: ref/u8 during a, mid: ref/u8 during c, other: ref/u8 during b) -> i32\n    origin b outlives a\n    origin a outlives c\n    if true\n        func pk(x: ref/u8 during a) -> ref/u8 during a => x\n        let f: (ref/u8 during b) -> ref/u8 during c = pk\n        let r = f(other)\n        return 0\n    return 0")]
    [InlineData("func apply(g: (ref/u8 during q) -> i32, r: ref/u8 during q) -> i32 => 0\nfunc h(anchor0: ref/u8 during a, other: ref/u8 during b) -> i32\n    origin b outlives a\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        return apply(pk, other)\n    return 0")]
    [InlineData("func h<T>(x: ref/T during a, o: ref/T during b) -> i32\n    origin b outlives a\n    if true\n        func pk(y: ref/T during a) -> i32 => 0\n        let f: (ref/T during b) -> i32 = pk\n        return f(o)\n    return 0")]
    [InlineData("func h(anchor0: ref/u8 during a, other: ref/u8 during b) -> i32\n    origin b outlives a\n    if true\n        func pk<U>(y: ref/U during a) -> i32 => 0\n        let f: (ref/u8 during b) -> i32 = pk\n        return f(other)\n    return 0")]
    [InlineData("func fb(x: ref/u8 during q) -> i32 => 0\nfunc h(anchor0: ref/u8 during a, other: ref/u8 during b) -> i32\n    origin b outlives a\n    var f: (ref/u8 during b) -> i32 = fb\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        f = pk\n    return f(other)")]
    [InlineData("func both<T>(g: (T) -> i32, k: (T) -> i32, v: T) -> i32 => 0\nfunc h(anchor0: ref/u8 during a, other: ref/u8 during b) -> i32\n    origin b outlives a\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        return both(func (x: ref/u8) => 0, pk, other)\n    return 0")]
    [InlineData("func both<T>(v: T, g: (T) -> i32, k: (T) -> i32) -> i32 => 0\nfunc h(anchor0: ref/u8 during a, other: ref/u8 during b) -> i32\n    origin b outlives a\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        return both(other, func (x: ref/u8) => 0, pk)\n    return 0")]
    public void NestedItemsAreErasedUnderTheEnclosingFunctionsClauses(string body)
        => Assert.Equal(string.Empty, Records("public func main() => ()\n" + body));

    // The invalid twins: without the clause the parameter or the result needs, the record names that relation.
    [Theory]
    [InlineData("func h(anchor0: ref/u8 during a, other: ref/u8 during b) -> i32\n    origin a outlives b\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        let f: (ref/u8 during b) -> i32 = pk\n        return f(other)\n    return 0", "UnprovenOriginContract_Kd pk: the 1st parameter requires b outlives a, which is not proven")]
    [InlineData("func h(anchor0: ref/u8 during a, mid: ref/u8 during c, other: ref/u8 during b) -> i32\n    origin b outlives a\n    if true\n        func pk(x: ref/u8 during a) -> ref/u8 during a => x\n        let f: (ref/u8 during b) -> ref/u8 during c = pk\n        let r = f(other)\n        return 0\n    return 0", "UnprovenOriginContract_Kd pk: the result requires a outlives c, which is not proven")]
    [InlineData("func h<T>(x: ref/T during a, o: ref/T during b) -> i32\n    if true\n        func pk(y: ref/T during a) -> i32 => 0\n        let f: (ref/T during b) -> i32 = pk\n        return f(o)\n    return 0", "UnprovenOriginContract_Kd pk: the 1st parameter requires b outlives a, which is not proven")]
    [InlineData("func apply(g: (ref/u8 during q) -> i32, r: ref/u8 during q) -> i32 => 0\nfunc h(anchor0: ref/u8 during a, other: ref/u8 during b) -> i32\n    if true\n        func pk(x: ref/u8 during a) -> i32 => 0\n        return apply(pk, other)\n    return 0", "NoApplicableOverload_Kd apply(pk, other): none of 1 candidates applies")]
    public void NestedItemsAreNotErasedWithoutTheEnclosingFunctionsClauses(string body, string record)
    {
        var source = "public func main() => ()\n" + body;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(record, $"{error.Code} {source.Substring(error.Span!.Value.Start, error.Span.Value.Length)}: {error.Label}");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmItemBindingAnalysisAndEmissionAllocateNothing()
    {
        const string Source = "func inc(value: i32) -> i32 => value + 1\nlet f = inc\nlet g: (i32) -> i32 = f\nrequire f(1) == g(1) else => $abort(\"item\")";
        var c = MinimalEmissionTest.Analyze(Source);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }

    private static string Records(string source)
        => string.Join("; ", DiagnosticCorpus.Check(source).Diagnostics.Select(x => $"{x.Code} {(x.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty)}"));
}
