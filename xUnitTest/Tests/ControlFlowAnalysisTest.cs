// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class ControlFlowAnalysisTest
{
    [Theory]
    [InlineData("<<", "u8")]
    [InlineData(">>", "u8")]
    [InlineData("&", "i64")]
    [InlineData("|", "i64")]
    [InlineData("^", "i64")]
    public void IntegerResultsComeFromLeftWithoutConstrainingShiftCounts(string op, string countType)
    {
        var analysis = Analyze($"func f(x: i64, n: {countType}) -> i64 => x {op} n");
        Assert.Empty(analysis.Issues);
        var expression = Assert.Single(analysis.Nodes, x => x.Key is BinaryKoto);
        Assert.Equal("i64", expression.Value.ExpressionType?.Name);
        var right = ((BinaryKoto)expression.Key).Right;
        Assert.Equal(countType, analysis.Nodes[right].ExpressionType?.Name);
    }

    [Theory]
    [InlineData("<<=")]
    [InlineData(">>=")]
    public void CompoundShiftsKeepIndependentCountAndUnitResult(string op)
    {
        var analysis = Analyze($"func f(n: u8) -> ()\n    var x: i64 = 1\n    x {op} n");
        Assert.Empty(analysis.Issues);
        var expression = Assert.Single(analysis.Nodes, x => x.Key is BinaryKoto);
        Assert.Equal(ControlFlowType.Unit, expression.Value.ExpressionType);
        Assert.Equal("u8", analysis.Nodes[((BinaryKoto)expression.Key).Right].ExpressionType?.Name);
    }

    [Theory]
    [InlineData("bool", false)]
    [InlineData("i32", true)]
    [InlineData("Never", false)]
    [InlineData(null, false)]
    public void TestsBoundValueOfGroupedCondition(string? returnType, bool hasError)
    {
        var compilation = Compilation.CreateForTest();
        var tree = compilation.Kotonoha;
        tree.CreateCodeContext().Parse(tree.RootKoto, "var i3 = if (Func()) => 1 else => 0");
        Assert.Empty(tree.DiagnosticCollection.GetArray());
        var analysis = compilation.AnalyzeControlFlow(new ConditionCallTypes(returnType));
        Assert.Equal(hasError, analysis.Issues.Count > 0);
        var condition = analysis.Nodes.Single(x => x.Key is ParenthesizedKoto);
        Assert.Equal(returnType, condition.Value.ExpressionType?.Name);
        if (returnType is null)
        {
            Assert.Contains(condition.Key, analysis.PendingBinding);
        }
    }

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
        Assert.NotEmpty(tree.DiagnosticCollection.GetArray());
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
    [InlineData("outer: loop\n    loop\n        exit to outer")]
    [InlineData("func f(flag: bool) => match flag\n    true => 1\n    false => 2")]
    [InlineData("func f() -> i32\n    if false\n        return -1\n    return 1")]
    [InlineData("func f() -> i32\n    work: do\n        exit to work\n    return 1")]
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
    [InlineData("let x = if true => 1", "incompatible")]
    [InlineData("let x: i32 = if true => ()", "incompatible")]
    [InlineData("let x: i32 = if true\n    1", "incompatible")]
    [InlineData("let x = if true\n    1\nelse => 2", "incompatible")]
    [InlineData("if true\n    yield 1", "discarded selection")]
    [InlineData("func f() -> i32\n    if false\n        return \"text\"\n    return 1", "incompatible")]
    [InlineData("func f()\n    if false\n        return \"text\"\n    return 1", "incompatible")]
    [InlineData("loop\n    if false\n        exit\n    exit 1", "incompatible")]
    [InlineData("let x: i32 = loop\n    if false\n        exit \"text\"", "incompatible")]
    [InlineData("let x = if false => \"text\"\nelse => 1", "incompatible")]
    [InlineData("for x in values\n    if false\n        exit 1", "incompatible")]
    [InlineData("loop\n    yield 1", "No valid target")]
    [InlineData("let x = match true\n    true => 1", "exhaustive")]
    [InlineData("func f() -> i32\n    return loop\n        if false\n            exit \"text\"", "incompatible")]
    [InlineData("func f() -> i32\n    return 1\n    1 + \"text\"", "incompatible")]
    [InlineData("outer: loop\n    outer: loop\n        continue", "overlaps")]
    [InlineData("loop\n    func f()\n        exit", "No valid target")]
    [InlineData("outer: while (exit to outer)\n    ()", "No valid target")]
    [InlineData("func f() -> i32\n    return 1\n    -\"text\"", "numeric")]
    [InlineData("func f() -> i32\n    return 1\n    true + false", "numeric")]
    [InlineData("let x: Never = loop\n    if false\n        exit 1", "incompatible")]
    [InlineData("func f() -> i8\n    if false\n        return 128\n    return 1", "incompatible")]
    [InlineData("func f() -> i32\n    1", "cannot fall through")]
    [InlineData("func f()\n    require true else => ()\n    ()", "require")]
    [InlineData("func f(ready: bool)\n    require ready else\n        loop\n            exit\n    ()", "require")]
    [InlineData("func f(ready: bool) -> i32\n    require ready else => return 0\n    ()", "cannot fall through")]
    public void RejectsInvalidControlFlow(string source, string diagnostic)
    {
        Assert.Contains(Analyze(source).Issues, x => x.Message.Contains(diagnostic, StringComparison.Ordinal));
    }

    [Fact]
    public void SeparatesNeverFromAnAbsentOrExpectedTargetResultType()
    {
        var analysis = Analyze("let a = loop\n    continue\nlet x: i32 = loop\n    continue");
        Assert.Empty(analysis.Issues);
        var loops = analysis.Nodes.Where(x => x.Key is LoopKoto).Select(x => x.Value).ToArray();
        Assert.Equal(2, loops.Length);
        Assert.All(loops, x => Assert.Equal(ControlFlowType.Never, x.ExpressionType));
        Assert.Null(loops[0].TargetResultType);
        Assert.Equal(new ControlFlowType("i32"), loops[1].TargetResultType);
    }

    [Fact]
    public void NamedFunctionRetainsDefaultUnitDespiteDivergence()
    {
        var analysis = Analyze("func f()\n    if false\n        return\n    loop\n        continue");
        var info = analysis.Nodes.Single(x => x.Key is FunctionKoto { Name: "f" }).Value;
        Assert.Empty(analysis.Issues);
        Assert.Equal(ControlFlowType.Unit, info.FunctionResultType);
        Assert.Equal(ControlFlowType.Unit, info.TargetResultType);
    }

    [Fact]
    public void DoesNotInferNeverFromAMissingRequiredResult()
    {
        var analysis = Analyze("let x = if true\n    1\nelse\n    yield 2");
        Assert.Contains(analysis.Issues, x => x.Message.Contains("incompatible", StringComparison.Ordinal));
        Assert.NotEqual(ControlFlowType.Never, analysis.Nodes.Single(x => x.Key is IfKoto).Value.ExpressionType);
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
        Assert.False(selections[1].Value.IsResultRequiring);
        Assert.All(analysis.Targets.Where(x => x.Key is YieldKoto), x => Assert.Same(selections[1].Key, x.Value));
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
        Assert.Contains(analysis.PendingBinding, x => x is CodeBlockKoto);
    }

    [Fact]
    public void UsesBoundNeverCallsAndContextualFunctionResultTypes()
    {
        var compilation = Compilation.CreateForTest();
        compilation.Kotonoha.CreateCodeContext().Parse(
            compilation.Kotonoha.RootKoto,
            "func f() -> i32\n    if false\n        return \"text\"\n    abort()");
        var analysis = compilation.AnalyzeControlFlow(new BoundTestTypes());
        Assert.Contains(analysis.Issues, x => x.Node is StringLiteralKoto && x.Message.Contains("i32", StringComparison.Ordinal));
        Assert.DoesNotContain(analysis.Issues, x => x.Message.Contains("type ()", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolvesHeadersOutsideTheirOwnBoundary()
    {
        var analysis = Analyze("let result = outer: loop\n    while (exit to outer: 1)\n        continue");
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
        var selection = Assert.IsType<IfKoto>(analysis.Nodes.Single(x => x.Key is IfKoto && x.Value.IsResultRequiring).Key);
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
            Assert.False(await compilation.Project.Check());
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

    private sealed class ConditionCallTypes(string? returnType) : ControlFlowTypeSystem
    {
        private readonly SyntaxControlFlowTypes fallback = new();

        public override ControlFlowType? GetExpressionType(Koto expression)
            => expression is InvocationKoto
                ? returnType is null ? null : new(returnType)
                : this.fallback.GetExpressionType(expression);

        public override ControlFlowType? GetDeclaredType(Koto? syntax) => this.fallback.GetDeclaredType(syntax);

        public override bool? IsCompatible(ControlFlowResultSource source, ControlFlowType target) => this.fallback.IsCompatible(source, target);

        public override bool? IsExhaustive(MatchKoto match) => this.fallback.IsExhaustive(match);
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
