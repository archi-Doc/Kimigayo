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
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("FunctionItem" + name, source, string.Empty);
    }

    [Fact]
    public void DifferentDeclarationsHaveDifferentItemTypes()
    {
        var c = MinimalEmissionTest.Analyze("func first() => ()\nfunc second() => ()\nlet a = first\nlet b = second\nlet c = first");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
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
        Assert.False(c.Emission.Validate(out _));
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

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmItemBindingAnalysisAndEmissionAllocateNothing()
    {
        const string Source = "func inc(value: i32) -> i32 => value + 1\nlet f = inc\nlet g: (i32) -> i32 = f\nrequire f(1) == g(1) else => $abort(\"item\")";
        var c = MinimalEmissionTest.Analyze(Source);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
