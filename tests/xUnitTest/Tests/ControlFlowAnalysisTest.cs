// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class ControlFlowAnalysisTest
{
    [Theory]
    [InlineData("if (let z = true) => 1 else => 0", false)]
    [InlineData("if ((var z = false)) => 1 else => 0", false)]
    [InlineData("while (var z = true)\n    exit", false)]
    [InlineData("if (var z: bool = 1) => 1 else => 0", true)]
    [InlineData("var ordinary = (var z = true)", false)]
    public void RejectsConditionBindingSyntax(string source, bool hasError)
    {
        _ = hasError;
        var tree = Compilation.CreateForTest().Kotonoha;
        tree.CreateCodeContext().Parse(tree.RootKoto, source);
        Assert.NotEmpty(TestDiagnostics.Of(tree));
    }

    [Theory]
    [InlineData("let result = loop\n    if false\n        exit 1")]
    [InlineData("func f()\n    if false\n        return\n    loop\n        continue")]
    [InlineData("func f() -> i32\n    if false\n        return 1\n    loop\n        continue")]
    [InlineData("let result = loop\n    exit 10")]
    [InlineData("if true => 1\nelse => 2")]
    [InlineData("let result = if true\n    yield 1\nelse\n    yield 2")]
    [InlineData("func f(flag: bool) -> i32\n    let x = if flag\n        return 1\n    else\n        return 2")]
    [InlineData("let x = if true => 1\nelse\n    yield 2")]
    [InlineData("let x = loop\n    exit loop\n        continue")]
    [InlineData("let x = match true\n    true => 1\n    false => 2")]
    [InlineData("label outer: loop\n    loop\n        exit to outer")]
    [InlineData("func f(flag: bool) => match flag\n    true => 1\n    false => 2")]
    [InlineData("func f() -> i32\n    if false\n        return -1\n    return 1")]
    [InlineData("func f() -> i32\n    label work: do\n        exit to work\n    return 1")]
    [InlineData("func f(ready: bool) -> i32\n    require ready else => return 0\n    return 1")]
    [InlineData("func f(ready: bool) -> i32\n    require ready\n    else\n        return 0\n    return 1")]
    [InlineData("let answer = if true\n    require false else => yield 0\n    yield 1\nelse => 2")]
    [InlineData("defer\n    require true else => exit\n    ()")]
    [InlineData("func f()\n    require false else => return\n    ()")]
    public void AcceptsValidControlFlow(string source)
    {
        var analysis = Analyze(source);
        Assert.True(analysis.Issues.Count == 0, string.Join("\n", analysis.Issues.Select(x => x.Message)));
    }

    [Theory]
    [InlineData("if true\n    yield 1", "discarded selection")]
    [InlineData("loop\n    yield 1", "No valid target")]
    [InlineData("label outer: loop\n    label outer: loop\n        continue", "overlaps")]
    [InlineData("loop\n    func f()\n        exit", "No valid target")]
    [InlineData("label outer: while (exit to outer)\n    ()", "No valid target")]
    [InlineData("func f() -> i32\n    1", "cannot fall through")]
    [InlineData("func f()\n    require true else => ()\n    ()", "require")]
    [InlineData("func f(ready: bool)\n    require ready else\n        loop\n            exit\n    ()", "require")]
    [InlineData("func f(ready: bool) -> i32\n    require ready else => return 0\n    ()", "cannot fall through")]
    public void RejectsInvalidControlFlow(string source, string diagnostic)
    {
        Assert.Contains(Analyze(source).Issues, x => x.Message.Contains(diagnostic, StringComparison.Ordinal));
    }

    [Fact]
    public void ResolvesYieldsBeforeClassifyingDiscardedSelections()
    {
        var analysis = Analyze("if true\n    if false\n        yield 1\n    else\n        yield 2\nelse\n    ()");
        var selections = analysis.Nodes.Keys.OfType<IfKoto>().ToArray();
        Assert.False(KotoHelper.IsResultRequiringSelection(selections[0]));
        Assert.False(KotoHelper.IsResultRequiringSelection(selections[1]));
        Assert.All(analysis.Targets.Where(x => x.Key is YieldKoto), x => Assert.Same(selections[1], x.Value));
        Assert.Contains(analysis.Issues, x => x.Message.Contains("discarded selection", StringComparison.Ordinal));
    }

    [Fact]
    public void KeepsUnresolvedExhaustivenessPendingInsteadOfRejectingEnumPatterns()
    {
        var analysis = Analyze("let x = match value\n    .A => 1\n    .B => 2");
        Assert.Empty(analysis.Issues);
        Assert.Contains(analysis.PendingBinding, x => x is MatchKoto);
    }

    [Fact]
    public void EnvironmentDirectivesAreSelectedBeforeControlFlowAnalysis()
    {
        var analysis = Analyze("func f() -> i32\n    #if false\n    return \"text\"\n    return 1");
        Assert.Empty(analysis.Issues);
        Assert.Empty(analysis.PendingBinding);
    }

    [Fact]
    public void DefersCoverageWhenAnUnresolvedCallCouldHaveTypeNever()
    {
        var analysis = Analyze("func f() -> i32\n    abort()");
        Assert.Empty(analysis.Issues);
        Assert.Contains(analysis.PendingBinding, x => x is FunctionKoto { Name: "f" });
        analysis = Analyze("let x = if true\n    abort()\nelse => 1");
        Assert.Empty(analysis.Issues);
        Assert.Contains(analysis.PendingBinding, x => x is IfKoto);
    }

    [Fact]
    public void UsesBoundNeverCallsAndContextualFunctionResultTypes()
    {
        var compilation = Compilation.CreateForTest();
        compilation.Kotonoha.CreateCodeContext().Parse(
            compilation.Kotonoha.RootKoto,
            "func stop() -> Never => stop()\nfunc f() -> i32\n    if false\n        return \"text\"\n    stop()");
        compilation.Bind();
        Assert.Contains(compilation.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.TypeMismatch_Kd);
        Assert.DoesNotContain(compilation.AnalyzeControlFlow().Issues, x => x.Code == Kimi.DiagnosticCode.FunctionFallthrough_Kd);
    }

    [Fact]
    public void ResolvesHeadersOutsideTheirOwnBoundary()
    {
        var analysis = Analyze("let result = label outer: loop\n    while (exit to outer 1)\n        continue");
        Assert.Empty(analysis.Issues);
        var exitTarget = analysis.Targets.Single(x => x.Key is ExitKoto).Value;
        Assert.IsType<LoopKoto>(exitTarget);
        Assert.IsType<WhileKoto>(analysis.Targets.Single(x => x.Key is ContinueKoto).Value);
    }

    [Fact]
    public void PreservesBodyFormAndAnalysisThroughSerialization()
    {
        var compilation = Compilation.CreateForTest();
        compilation.Kotonoha.CreateCodeContext().Parse(
            compilation.Kotonoha.RootKoto,
            "let a = if true => 1\nelse\n    yield 2\nlet b = loop\n    if false\n        exit 1");
        var restored = new Kotonoha(compilation);
        TinyhandSerializer.DeserializeObject(TinyhandSerializer.Serialize(compilation.Kotonoha), ref restored);
        restored!.OnDeserialized(compilation);
        var analysis = ControlFlowAnalysis.Analyze(restored.RootKoto);
        Assert.Empty(analysis.Issues);
        var selection = Assert.Single(analysis.Nodes.Keys.OfType<IfKoto>(), KotoHelper.IsResultRequiringSelection);
        Assert.True(selection.Branches[0].Body.IsExpressionBody);
        Assert.True(selection.Branches[0].Body.HasTrailingExpression);
        Assert.False(selection.ElseBody!.IsExpressionBody);
    }

    [Fact]
    public async Task ProjectBuildReportsControlFlowErrors()
    {
        var directory = Directory.CreateTempSubdirectory("Kimigayo-control-flow-");
        try
        {
            var compilation = Compilation.CreateForTest();
            compilation.Project.Directory = directory.FullName;
            compilation.Project.AddSource("invalid.kimi", "let result = if true => 1");
            Assert.False(await compilation.Project.Check(TestContext.Current.CancellationToken));
        }
        finally
        {
            foreach (var file in directory.GetFiles())
            {
                file.Delete();
            }

            directory.Delete();
        }
    }

    [Theory]
    [InlineData("if true 1 else 2")]
    [InlineData("if true =>\nelse => 2")]
    [InlineData("if true =>\n    yield 1\nelse => 2")]
    public void RejectsMissingArrowOrExpression(string source)
    {
        var tree = Compilation.CreateForTest().Kotonoha;
        tree.CreateCodeContext().Parse(tree.RootKoto, source);
        Assert.NotEmpty(TestDiagnostics.Of(tree));
    }

    private static ControlFlowAnalysis Analyze(string source)
    {
        var compilation = Compilation.CreateForTest();
        compilation.Kotonoha.CreateCodeContext().Parse(compilation.Kotonoha.RootKoto, source);
        Assert.Empty(TestDiagnostics.Of(compilation));
        compilation.Bind();
        return compilation.AnalyzeControlFlow();
    }
}
