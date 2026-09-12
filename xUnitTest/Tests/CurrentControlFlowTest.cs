// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class CurrentControlFlowTest
{
    [Theory]
    [InlineData("let block = 1\nlet to = block\nlet result = do => to")]
    [InlineData("let result = work: do\n    if ready => exit to work: 1\n    exit to work: 2")]
    [InlineData("outer: loop => exit to outer\nfor item in items => use(item)\nwhile ready => tick()")]
    [InlineData("func f() => defer => unsafe => release()")]
    [InlineData("func f()\n    require ready else => return\n    defer\n        finish()")]
    [InlineData("let v = selection: match flag\n    true\n        yield to selection: 1\n    false => 2")]
    [InlineData("let v = choice: if flag\n    for x in xs => yield to choice: x\n    yield\nelse => ()")]
    [InlineData("let v = do => match flag\n    true\n        yield 1\n    false => 2")]
    [InlineData("if (do => true) => ()")]
    [InlineData("let v = ((do\n    1\n))")]
    [InlineData("let v = [do\n    1\n]")]
    [InlineData("func f() => consume(do\n    1\n)")]
    [InlineData("if table[do => 0] => ()")]
    [InlineData("let v = if (do\n    true\n) => 1 else => 2")]
    [InlineData("if table[do\n    0\n] => ()")]
    public void CommonBodiesAndTransfersRoundTrip(string source)
    {
        var c = Parse(source);
        var output = c.Kotonoha.RootKoto.ToString();
        Parse(output);
        var restored = TinyhandSerializer.Deserialize<Kotonoha>(TinyhandSerializer.Serialize(c.Kotonoha))!;
        restored.OnDeserialized(Compilation.CreateForTest());
        Assert.Empty(restored.DiagnosticCollection.GetArray());
    }

    [Theory]
    [InlineData("let do = 1")]
    [InlineData("work:\n    ()")]
    [InlineData("defer: finish()")]
    [InlineData("unsafe:\n    release()")]
    [InlineData("loop\n    exit 1 from outer")]
    [InlineData("loop\n    continue outer")]
    [InlineData("if ready =>\n    work()")]
    [InlineData("func f() =>\n    work()")]
    [InlineData("match flag\n    _ =>\n        work()")]
    [InlineData("defer => unsafe\n    release()")]
    [InlineData("if a => if b => () else => ()")]
    [InlineData("if do => true => ()")]
    [InlineData("if (let flag = true) => ()")]
    [InlineData("let result = unsafe => read()")]
    [InlineData("let result = require ready else => return")]
    [InlineData("loop => exit to target: inner: do => 1")]
    public void RejectsObsoleteAndAmbiguousSyntax(string source)
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.NotEmpty(c.Kotonoha.DiagnosticCollection.GetArray());
    }

    [Theory]
    [InlineData("func f() => 42")]
    [InlineData("func f(flag: bool)\n    if flag => 1 else => \"text\"")]
    [InlineData("func f(flag: bool)\n    match flag\n        true => 1\n        false => \"text\"")]
    [InlineData("let v = do => 1")]
    [InlineData("let v = work: do\n    exit to work: 1")]
    [InlineData("let v = loop => exit 1")]
    [InlineData("let v = loop\n    continue\n    exit 1")]
    [InlineData("func f(flag: bool) -> i32\n    return if flag\n        yield 1\n    else => 2")]
    [InlineData("func f(flag: bool) -> i64\n    let big: i64 = 10\n    return if flag => 1 else => big")]
    [InlineData("let v = choice: if true\n    loop => yield to choice: 1\nelse => 2")]
    [InlineData("let large: i64 = 10\nlet result = if true => 1 else => large + 1")]
    [InlineData("let large: i64 = 10\nlet result = if true => 1 + large else => 1")]
    public void BindsCurrentResultRules(string source)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.Issues);
        Assert.Empty(flow.PendingBinding);
    }

    [Theory]
    [InlineData("loop => exit 1")]
    [InlineData("work: do => exit to work: 1")]
    [InlineData("let v = if true => 1")]
    [InlineData("let v = loop\n    continue\n    exit 1\n    exit \"x\"")]
    [InlineData("func f() => return 1")]
    [InlineData("func f() -> i32\n    while true => ()")]
    [InlineData("func f() -> i32\n    if true => return 1")]
    [InlineData("if true => yield")]
    [InlineData("match true\n    true => ()")]
    public void RejectsInvalidResultContracts(string source)
    {
        var c = Parse(source);
        var bound = c.Bind();
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.True(!bound.IsComplete || flow.Issues.Count > 0, source);
    }

    [Fact]
    public void UnreachableExitAndBlockedCleanupRetainTheirTypes()
    {
        var c = Parse("let a = loop\n    continue\n    exit 1\nlet b = work: do\n    defer => loop => ()\n    exit to work: 2");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.Issues);
        var results = c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FieldKoto>().ToArray();
        Assert.All(results, field => Assert.Equal("i32", field.BoundType?.Name));
        Assert.All(results, field => Assert.Equal("i32", flow.Nodes[field.InitializerKoto!].ExpressionType?.Name));
        Assert.All(results, field => Assert.False(flow.Nodes[field.InitializerKoto!].CanCompleteNormally));
    }

    [Theory]
    [InlineData("func f() -> i32 => unsafe => return 1")]
    [InlineData("func f() -> i32 => unsafe => loop => return 1")]
    [InlineData("func fail() -> Never => fail()\nlet result = while fail() => ()")]
    [InlineData("func f(flag: bool, large: i64) -> i64\n    return if flag\n        yield if flag => 1 else => 2\n    else => large")]
    public void StatementCompletionAndNestedResultsBind(string source)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.Issues);
        Assert.Empty(flow.PendingBinding);
    }

    [Theory]
    [InlineData("func f()\n    defer => writeLine(\"later\")", true)]
    [InlineData("func f() => defer => writeLine(\"later\")", true)]
    [InlineData("func f()\n    var text = \"old\"\n    defer => writeLine(text)\n    text = \"new\"", true)]
    [InlineData("func f()\n    let text: string\n    defer => writeLine(text)\n    text = \"new\"", true)]
    [InlineData("func f()\n    let text = \"old\"\n    defer => writeLine(text)\n    writeLine(text)", false)]
    [InlineData("func f()\n    let text = \"old\"\n    if false => writeLine(text)\n    writeLine(text)", false)]
    [InlineData("func f()\n    let n: i32\n    while true\n        n = 1\n        exit\n    let value = n", false)]
    [InlineData("func f()\n    let n: i32\n    loop\n        n = 1\n        exit\n    let value = n", true)]
    [InlineData("func f() -> string\n    return (work: do\n        let text = \"value\"\n        defer => writeLine(\"cleanup\")\n        exit to work: text\n    )", true)]
    [InlineData("func f()\n    require true else => defer => loop => ()", true)]
    [InlineData("func f()\n    require true else => defer => ()", false)]
    [InlineData("func f() -> i32 => unsafe => return 1", true)]
    [InlineData("func f()\n    defer => exit", true)]
    [InlineData("func f()\n    loop => exit ()", true)]
    [InlineData("func f()\n    choice: if true => yield to choice: ()", true)]
    public void CleanupAndConservativePathsPreserveOwnership(string source, bool valid)
    {
        var c = Parse(source);
        c.Bind();
        Assert.Equal(valid, c.Ownership.Analyze().IsVerified);
    }

    [Theory]
    [InlineData("func f() => 42", "effect-free")]
    [InlineData("func isAdult(age: i32) => age >= 18", "effect-free")]
    [InlineData("func f() => while true => ()", "Use loop")]
    [InlineData("func compute() -> i32 => 1\nlet x = do\n    compute()", "Unit was inferred")]
    [InlineData("func compute() -> i32 => 1\nlet x = do\n    if true => compute() else => compute()", "Unit was inferred")]
    public void ReportsSpecifiedWarnings(string source, string message)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.Issues);
        Assert.Contains(flow.Warnings, warning => warning.Message.Contains(message));
        var count = flow.Warnings.Count;
        Assert.Equal(count, c.AnalyzeControlFlow(c.Binding.TypeSystem).Warnings.Count);
    }

    [Theory]
    [InlineData("func close() -> bool => true\nfunc cleanup() => close()")]
    [InlineData("func f() => ()")]
    [InlineData("func f() => defer => ()")]
    [InlineData("func f()\n    defer => ()")]
    public void DoesNotWarnSolelyForCallsUnitOrFinalDefer(string source)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Empty(c.AnalyzeControlFlow(c.Binding.TypeSystem).Warnings);
    }

    [Theory]
    [InlineData("func f(text: string) -> string\n    return text\n    return text", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f() -> i32\n    let value: i32\n    return 1\n    return value", OwnershipFailure.UninitializedUse)]
    public void UnreachableOwnershipUsesTheStateBeforeTransferCleanup(string source, OwnershipFailure failure)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.False(c.Ownership.Analyze().IsVerified);
        Assert.Contains(c.Ownership.Issues, issue => issue.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, issue => issue.Failure == OwnershipFailure.Unsupported);
    }

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.True(c.Kotonoha.DiagnosticCollection.GetArray().Length == 0, source + "\n" + string.Join("\n", c.Kotonoha.DiagnosticCollection.GetArray().Select(x => x.Message)));
        return c;
    }

    private static string Describe(Compilation c)
        => string.Join("\n", c.Binding.Issues.Select(x => x.Code + ": " + x.Node));
}
