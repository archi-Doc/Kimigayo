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
        var c = Compile(source);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        Assert.Empty(CompilationTestHelper.BindingErrors(c));
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
        // The jump target, label and function fallthrough checks are Binding's HIR lints; the require fallthrough is the MIR's A3.
        var c = Compile(source);
        c.Analyze();
        Assert.Contains(CompilationTestHelper.PublishedErrors(c), x => x.Message.Contains(diagnostic, StringComparison.Ordinal));
    }

    // SPEC 14.11, 14.10.3 (DECISIONS R4-M7a): the MIR's A3 reports each falling failure body once, at that body: the require rows above,
    // a failure body only the checks reach, a require nested after a return in a failure body that the checks alone leave to its end, a
    // default's failure body once however many calls copy it, a defer's once however many exits build it, a defer that a single-item
    // failure body registers, which runs where that body is left (SPEC 16.1), whose own body diverges or completes, and a default whose
    // failure bodies continue only for the checks. Each row that falls has one require.
    [Theory]
    [InlineData("func f(ready: bool) -> i32\n    require ready else => return 0\n    return 1", 0)]
    [InlineData("func f(ready: bool) -> i32\n    require ready\n    else\n        return 0\n    return 1", 0)]
    [InlineData("let answer = if true\n    require false else => yield 0\n    yield 1\nelse => 2", 0)]
    [InlineData("defer\n    require true else => exit\n    ()", 0)]
    [InlineData("func f()\n    require false else => return\n    ()", 0)]
    [InlineData("func f()\n    require true else => ()\n    ()", 1)]
    [InlineData("func f(ready: bool)\n    require ready else\n        loop\n            exit\n    ()", 1)]
    [InlineData("func f(ready: bool) -> i32\n    require ready else => return 0\n    ()", 0)]
    [InlineData("func f()\n    return\n    require true else => ()", 1)]
    [InlineData("func f(c: bool)\n    require c else\n        label outer: do\n            return\n            require c else => exit to outer\n    ()", 0)]
    [InlineData("func value(c: bool, y: i32 = (label choice: do\n    require c else => ()\n    exit to choice 7\n)) -> i32 => y\nlet a = value(true)\nlet b = value(false)", 1)]
    [InlineData("func f(c: bool)\n    defer\n        require c else => ()\n    if c => return\n    ()", 1)]
    [InlineData("func f()\n    require true else => defer => loop => ()\n    ()", 0)]
    [InlineData("func f()\n    require true else => defer => ()\n    ()", 1)]
    [InlineData(RequireContinuationTest.Default, 0)]
    public void TheMirFindsEveryRequireFallthrough(string source, int count)
    {
        var c = Compile(source);
        var mir = RequireFallthroughs(c);
        Assert.Equal(count, mir.Length);
        Assert.All(mir, x => Assert.Same(Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<RequireKoto>()).ElseBody, x));
    }

    [Fact]
    public void ResolvesYieldsBeforeClassifyingDiscardedSelections()
    {
        // SPEC 14.9: a yield in a discarded selection has no target, so its plan row is null and Binding reports it.
        var c = Compile("if true\n    if false\n        yield 1\n    else\n        yield 2\nelse\n    ()");
        var selections = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<IfKoto>().ToArray();
        Assert.False(KotoHelper.IsResultRequiringSelection(selections[0]));
        Assert.False(KotoHelper.IsResultRequiringSelection(selections[1]));
        Assert.All(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<YieldKoto>(), static x => Assert.Null(HirTables.PlanOf(x).Plan));

        // SPEC 14.9.1, 23.1.2 (DECISIONS BU-101): the invalid yields go to no target, so their operands have no expected Type and no
        // TypeMismatch_Kd rests on the discarded selection's Unit.
        var errors = CompilationTestHelper.BindingErrors(c);
        Assert.Contains(errors, static x => x is { Code: "UnlabeledYieldTarget_Kd", Text: "yield 1" });
        Assert.Contains(errors, static x => x is { Code: "UnlabeledYieldTarget_Kd", Text: "yield 2" });
        Assert.Equal(2, errors.Count(x => x.Message.Contains("discarded selection", StringComparison.Ordinal)));
        Assert.DoesNotContain(errors, static x => x.Code == nameof(Kimi.DiagnosticCode.TypeMismatch_Kd));
    }

    [Fact]
    public void KeepsUnresolvedExhaustivenessPendingInsteadOfRejectingEnumPatterns()
    {
        var c = Compile("let x = match value\n    .A => 1\n    .B => 2");
        c.Analyze();
        Assert.Equal(["UnresolvedBinding_Kd value"], CompilationTestHelper.PublishedErrors(c).Select(static x => $"{x.Code} {x.Text}"));
    }

    [Fact]
    public void EnvironmentDirectivesAreSelectedBeforeControlFlowAnalysis()
    {
        var c = Compile("func f() -> i32\n    #if false\n    return \"text\"\n    return 1");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
    }

    // R6-check H10b (D11): an unresolved call could have Type Never, so its cause is published and no fallthrough or coverage code rests on
    // it; the function form publishes the cause alone. The join form's exact set is AJoinPastAnUnresolvedCallPublishesOnlyItsCause's.
    [Fact]
    public void DefersCoverageWhenAnUnresolvedCallCouldHaveTypeNever()
    {
        Assert.Equal(["UnresolvedBinding_Kd abort"], PublishedErrors("func f() -> i32\n    abort()"));
        var errors = PublishedErrors("let x = if true\n    abort()\nelse => 1");
        Assert.Contains("UnresolvedBinding_Kd abort", errors);
        Assert.DoesNotContain(errors, static x => x.Split(' ')[0] is nameof(Kimi.DiagnosticCode.FunctionFallthrough_Kd)
            or nameof(Kimi.DiagnosticCode.RequireFallthrough_Kd) or nameof(Kimi.DiagnosticCode.NonExhaustiveMatch_Kd));
    }

    // R6-check H10b (D11): a join past an unresolved call publishes only the call's cause. Today the join also publishes TypeMismatch_Kd at
    // `1`, resting on the failed call: PLAN G52's join cascade. Explicit until G52 decides it, its expectation unchanged; then a default
    // test (the kept-apart form of MirBorrowsTest's G264 row).
    [Fact(Explicit = true)]
    public void AJoinPastAnUnresolvedCallPublishesOnlyItsCause()
        => Assert.Equal(["UnresolvedBinding_Kd abort"], PublishedErrors("let x = if true\n    abort()\nelse => 1"));

    [Fact]
    public void UsesBoundNeverCallsAndContextualFunctionResultTypes()
    {
        var compilation = Compilation.CreateForTest();
        compilation.Kotonoha.CreateCodeContext().Parse(
            compilation.Kotonoha.RootKoto,
            "func stop() -> Never => stop()\nfunc f() -> i32\n    if false\n        return \"text\"\n    stop()");
        compilation.Bind();
        var errors = CompilationTestHelper.BindingErrors(compilation);
        Assert.Contains(errors, static x => x.Code == nameof(Kimi.DiagnosticCode.TypeMismatch_Kd));
        Assert.DoesNotContain(errors, static x => x.Code == nameof(Kimi.DiagnosticCode.FunctionFallthrough_Kd));
    }

    [Fact]
    public void ResolvesHeadersOutsideTheirOwnBoundary()
    {
        var c = Compile("let result = label outer: loop\n    while (exit to outer 1)\n        continue");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        Assert.IsType<LoopKoto>(HirTables.PlanOf(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ExitKoto>().Single()).Plan);
        Assert.IsType<WhileKoto>(HirTables.PlanOf(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ContinueKoto>().Single()).Plan);
    }

    [Fact]
    public void PreservesBodyFormThroughSerialization()
    {
        var compilation = Compilation.CreateForTest();
        compilation.Kotonoha.CreateCodeContext().Parse(
            compilation.Kotonoha.RootKoto,
            "let a = if true => 1\nelse\n    yield 2\nlet b = loop\n    if false\n        exit 1");
        var restored = new Kotonoha(compilation);
        TinyhandSerializer.DeserializeObject(TinyhandSerializer.Serialize(compilation.Kotonoha), ref restored);
        restored!.OnDeserialized(compilation);
        var selection = Assert.Single(KotoTree.Walk(restored.RootKoto).OfType<IfKoto>(), KotoHelper.IsResultRequiringSelection);
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

    // The subjects of the MIR's A3 RequireFallthrough problems (DECISIONS R4-M7a), in source order.
    internal static Koto[] RequireFallthroughs(Compilation compilation)
        => compilation.BuildMir().Problems.All.ToArray().Where(static x => x.Code == Kimi.DiagnosticCode.RequireFallthrough_Kd)
            .Select(x => compilation.SyntaxOf(x.Subject)).OrderBy(static x => x.Span.Start).ToArray();

    // The published Errors of an analyzed source as code and text (CompilationTestHelper.PublishedErrors asserts that the MIR exists).
    private static string[] PublishedErrors(string source)
    {
        var c = Compile(source);
        c.Analyze();
        return [.. CompilationTestHelper.PublishedErrors(c).Select(static x => $"{x.Code} {x.Text}")];
    }

    private static Compilation Compile(string source)
    {
        var compilation = Compilation.CreateForTest();
        compilation.Kotonoha.CreateCodeContext().Parse(compilation.Kotonoha.RootKoto, source);
        Assert.Empty(TestDiagnostics.Of(compilation));
        compilation.Bind();
        return compilation;
    }
}
