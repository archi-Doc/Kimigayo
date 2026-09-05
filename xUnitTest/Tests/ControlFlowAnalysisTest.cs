// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class ControlFlowAnalysisTest
{
    [Theory]
    [InlineData("loop\n    if false\n        exit 1")]
    [InlineData("func f()\n    if false\n        return 1\n    loop\n        continue")]
    [InlineData("func f() -> i32\n    if false\n        return 1\n    loop\n        continue")]
    [InlineData("loop\n    exit 10")]
    [InlineData("if true => 1;\nelse => 2;")]
    [InlineData("if true\n    yield 1\nelse\n    yield 2")]
    [InlineData("func f(flag: bool) -> i32\n    let x = if flag\n        return 1\n    else\n        return 2")]
    [InlineData("let x = if true => 1\nelse\n    2")]
    [InlineData("let x = loop\n    exit loop\n        continue")]
    [InlineData("let x = match true\n    true => 1\n    false => 2")]
    [InlineData("outer: loop\n    loop\n        exit from outer")]
    [InlineData("func f(flag: bool) => match flag\n    true => 1\n    false => 2")]
    [InlineData("func f() -> i32\n    if false\n        return -1\n    return 1")]
    [InlineData("func f() -> i32\n    work:\n        exit from work\n    return 1")]
    public void AcceptsValidControlFlow(string source)
    {
        var analysis = Analyze(source);
        Assert.True(analysis.Issues.Count == 0, string.Join("\n", analysis.Issues.Select(x => x.Message)));
    }

    [Theory]
    [InlineData("if true => 1", "final else")]
    [InlineData("if true => ()", "final else")]
    [InlineData("let x = if true\n    1", "final else")]
    [InlineData("let x = if true\n    1\nelse => 2", "must yield")]
    [InlineData("if true\n    yield 1", "final else")]
    [InlineData("func f() -> i32\n    if false\n        return \"text\"\n    return 1", "incompatible")]
    [InlineData("func f()\n    if false\n        return \"text\"\n    return 1", "incompatible")]
    [InlineData("loop\n    if false\n        exit\n    exit 1", "incompatible")]
    [InlineData("let x: i32 = loop\n    if false\n        exit \"text\"", "incompatible")]
    [InlineData("let x = if false => \"text\"\nelse => 1", "incompatible")]
    [InlineData("for x in values\n    if false\n        exit 1", "Only loop")]
    [InlineData("loop\n    yield 1", "No valid target")]
    [InlineData("let x = match true\n    true => 1", "exhaustive")]
    [InlineData("func f() -> i32\n    return loop\n        if false\n            exit \"text\"", "incompatible")]
    [InlineData("func f() -> i32\n    return 1\n    1 + \"text\"", "incompatible")]
    [InlineData("outer: loop\n    outer: loop\n        continue", "overlaps")]
    [InlineData("loop\n    func f()\n        exit", "No valid target")]
    [InlineData("outer: while (exit from outer)\n    ()", "No valid target")]
    [InlineData("func f() -> i32\n    return 1\n    -\"text\"", "numeric")]
    [InlineData("func f() -> i32\n    return 1\n    true + false", "numeric")]
    [InlineData("let x: Never = loop\n    if false\n        exit 1", "incompatible")]
    [InlineData("func f() -> i8\n    if false\n        return 128\n    return 1", "incompatible")]
    [InlineData("func f() -> i32\n    1", "cannot fall through")]
    public void RejectsInvalidControlFlow(string source, string diagnostic)
    {
        Assert.Contains(Analyze(source).Issues, x => x.Message.Contains(diagnostic, StringComparison.Ordinal));
    }

    [Fact]
    public void SeparatesNeverFromAnAbsentOrExpectedTargetResultType()
    {
        var analysis = Analyze("loop\n    if false\n        exit 1\nlet x: i32 = loop\n    if false\n        exit 1");
        Assert.Empty(analysis.Issues);
        var loops = analysis.Nodes.Where(x => x.Key is LoopKoto).Select(x => x.Value).ToArray();
        Assert.Equal(2, loops.Length);
        Assert.All(loops, x => Assert.Equal(ControlFlowType.Never, x.ExpressionType));
        Assert.Null(loops[0].TargetResultType);
        Assert.Equal(new ControlFlowType("i32"), loops[1].TargetResultType);
    }

    [Fact]
    public void InfersNeverForFunctionSignatureWithoutInventingATransferContract()
    {
        var analysis = Analyze("func f()\n    if false\n        return 1\n    loop\n        continue");
        var info = analysis.Nodes.Single(x => x.Key is FunctionKoto { Name: "f" }).Value;
        Assert.Empty(analysis.Issues);
        Assert.Equal(ControlFlowType.Never, info.FunctionResultType);
        Assert.Null(info.TargetResultType);
    }

    [Fact]
    public void DoesNotInferNeverFromAMissingRequiredResult()
    {
        var analysis = Analyze("let x = if true\n    1\nelse\n    yield 2");
        Assert.Contains(analysis.Issues, x => x.Message.Contains("must yield", StringComparison.Ordinal));
        Assert.Null(analysis.Nodes.Single(x => x.Key is IfKoto).Value.ExpressionType);
    }

    [Fact]
    public void PropagatesALaterInferredContractIntoUnreachableNestedResults()
    {
        var analysis = Analyze("loop\n    if false\n        exit loop\n            exit \"text\"\n    exit 1");
        Assert.Contains(analysis.Issues, x => x.Node is StringLiteralKoto && x.Message.Contains("incompatible", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolvesYieldsBeforeClassifyingDiscardedSelections()
    {
        var analysis = Analyze("if true\n    if false\n        yield 1\n    else\n        yield 2\nelse\n    ()");
        var selections = analysis.Nodes.Where(x => x.Key is IfKoto).ToArray();
        Assert.False(selections[0].Value.IsResultRequiring);
        Assert.True(selections[1].Value.IsResultRequiring);
        Assert.All(analysis.Targets.Where(x => x.Key is YieldKoto), x => Assert.Same(selections[1].Key, x.Value));
        Assert.Empty(analysis.Issues);
    }

    [Fact]
    public void KeepsUnresolvedExhaustivenessPendingInsteadOfRejectingEnumPatterns()
    {
        var analysis = Analyze("let x = match value\n    A => 1\n    B => 2");
        Assert.Empty(analysis.Issues);
        Assert.Contains(analysis.PendingBinding, x => x is MatchKoto);
    }

    [Fact]
    public void DefersBodiesWithUnselectedCompileTimeDirectives()
    {
        var analysis = Analyze("func f() -> i32\n    #if later\n    return \"text\"\n    return 1");
        Assert.Empty(analysis.Issues);
        Assert.Contains(analysis.PendingBinding, x => x is FunctionKoto { Name: "f" });
    }

    [Fact]
    public void DefersCoverageWhenAnUnresolvedCallCouldHaveTypeNever()
    {
        var analysis = Analyze("func f() -> i32\n    abort()");
        Assert.Empty(analysis.Issues);
        Assert.Contains(analysis.PendingBinding, x => x is FunctionKoto { Name: "f" });
        analysis = Analyze("let x = if true\n    abort()\nelse => 1");
        Assert.Empty(analysis.Issues);
        Assert.Contains(analysis.PendingBinding, x => x is CodeBlockKoto);
    }

    [Fact]
    public void UsesBoundNeverCallsAndContextualFunctionResultTypes()
    {
        var compilation = Compilation.CreateForTest();
        compilation.Kotonoha.CreateCodeContext().Parse(
            compilation.Kotonoha.RootKoto,
            "func f()\n    if false\n        return \"text\"\n    abort()");
        var analysis = compilation.AnalyzeControlFlow(new BoundTestTypes());
        Assert.Contains(analysis.Issues, x => x.Node is StringLiteralKoto && x.Message.Contains("i32", StringComparison.Ordinal));
        Assert.DoesNotContain(analysis.Issues, x => x.Message.Contains("type ()", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolvesHeadersOutsideTheirOwnBoundary()
    {
        var analysis = Analyze("outer: loop\n    while (exit 1 from outer)\n        continue");
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
            "if true => 1;\nelse\n    yield 2\nloop\n    if false\n        exit 1");
        var restored = new Kotonoha(compilation);
        TinyhandSerializer.DeserializeObject(TinyhandSerializer.Serialize(compilation.Kotonoha), ref restored);
        restored!.OnDeserialized(compilation);
        var analysis = ControlFlowAnalysis.Analyze(restored.RootKoto);
        Assert.Empty(analysis.Issues);
        var selection = Assert.IsType<IfKoto>(analysis.Nodes.Single(x => x.Key is IfKoto && x.Value.IsResultRequiring).Key);
        Assert.True(selection.Branches[0].Body.IsExpressionBody);
        Assert.True(selection.Branches[0].Body.HasTrailingSemicolon);
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
            compilation.Project.AddSource("invalid.kimi", "if true => 1");
            Assert.False(await compilation.Project.Build());
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
        Assert.NotEmpty(tree.DiagnosticCollection.GetArray());
    }

    private static ControlFlowAnalysis Analyze(string source)
    {
        var compilation = Compilation.CreateForTest();
        compilation.Kotonoha.CreateCodeContext().Parse(compilation.Kotonoha.RootKoto, source);
        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
        return compilation.AnalyzeControlFlow();
    }

    private sealed class BoundTestTypes : ControlFlowTypeSystem
    {
        private readonly SyntaxControlFlowTypes fallback = new();

        public override ControlFlowType? GetExpressionType(Koto expression) => expression switch
        {
            InvocationKoto => ControlFlowType.Never,
            IdentifierNameKoto { IdentifierName: "abort" } => new("function"),
            _ => this.fallback.GetExpressionType(expression),
        };

        public override ControlFlowType? GetExpectedResultType(Koto boundary)
            => boundary is FunctionKoto { Name: "f" } ? new("i32") : null;

        public override ControlFlowType? GetDeclaredType(Koto? syntax) => this.fallback.GetDeclaredType(syntax);

        public override bool? IsCompatible(ControlFlowResultSource source, ControlFlowType target) => this.fallback.IsCompatible(source, target);

        public override bool? IsExhaustive(MatchKoto match) => this.fallback.IsExhaustive(match);
    }
}
