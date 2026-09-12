// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>Checks front-end control-flow behavior against the SPEC 2.2 and 14 examples and boundaries.</summary>
public class ControlFlowConformanceTest
{
    [Theory]
    [InlineData("func f() -> i32 => compute()\n    .max()")]
    [InlineData("let v = run(func (i: i32) => i)\n    .count()")]
    [InlineData("let v = if a => b else => c\n    .d()")]
    [InlineData("let v = items.map(func (i: i32) => i)\n    .count()")]
    [InlineData("func f() => items\n    .map(func (i: i32) => i)\n    .sum()")]
    [InlineData("for (k, v) in dict => use(k)\nlet y = 1")]
    [InlineData("consume(\n    match mode\n        .Fast => 1\n        _\n            prepare()\n            yield 2\n)")]
    [InlineData("for item in filtered(\n    source,\n    predicate\n) => process(item)")]
    [InlineData("print(if ready => 1\n    else => 2)")]
    [InlineData("if a => b()\n\nelse => c()")]
    [InlineData("if a\n    b()\n// comment\nelse\n    c()")]
    [InlineData("require ready\nelse => return")]
    [InlineData("func f(a: bool) -> i32 => match a\n    true => 1\n    false => 2")]
    [InlineData("for x in xs => if a => b()")]
    [InlineData("require a else => if b => return")]
    [InlineData("match x\n    .A => if b => 1 else => 2\n    _ => ()")]
    [InlineData("let unsafe = 1\nlet y = unsafe + 1")]
    [InlineData("func unsafe() => ()\nunsafe()")]
    [InlineData("let unsafe = [1]\nunsafe[0] = 2")]
    [InlineData("func f()\n    unsafe\n        work()\n    defer\n        work()")]
    [InlineData("let v = x.block")]
    [InlineData("consume(lbl: do => 1)")]
    [InlineData("let v = if a => (if b => 1 else => 2) else => 3")]
    [InlineData("if (if a => b else => c) => work()")]
    [InlineData("func run() => if ready => work()")]
    [InlineData("outer: for row in rows\n    for cell in row\n        if skipRow(cell) => continue to outer\n        if done(cell) => exit to outer")]
    [InlineData("let r = choice: if enabled\n    for item in items[..]\n        if accepts(item) => yield to choice: score(item)\n    yield -1\nelse => 0")]
    [InlineData("let x = compute()\n    .next()")]
    public void AcceptsSpecifiedLayout(string source)
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.True(c.Kotonoha.DiagnosticCollection.GetArray().Length == 0, source + "\n" + string.Join("\n", c.Kotonoha.DiagnosticCollection.GetArray().Select(x => x.Message)));
    }

    [Theory]
    [InlineData("if lbl: do => true => ()")]
    [InlineData("if x: if a => b else => c => ()")]
    [InlineData("while lbl: do => true => ()")]
    [InlineData("for x in lbl: do => xs => ()")]
    [InlineData("require lbl: do => true else => return")]
    [InlineData("match lbl: do => 1\n    _ => ()")]
    [InlineData("match v\n    .A if lbl: do => true => ()\n    _ => ()")]
    [InlineData("consume(value: lbl: do => 1)")]
    [InlineData("let v = [1, lbl: do => 2]")]
    [InlineData("let v = [k: lbl: do => 1]")]
    [InlineData("let v = consume(do\n    1)")]
    [InlineData("let v = [do\n    1]")]
    [InlineData("exit to outer 1")]
    [InlineData("if a => loop\n    work()")]
    [InlineData("match x\n    #if true\n        _ => ()")]
    [InlineData("return lbl: do => 1")]
    [InlineData("let v = x.do")]
    [InlineData("func do() => ()")]
    [InlineData("require ready\n    else => return")]
    [InlineData("if a => for x in xs => if b => c()")]
    [InlineData("yield to lbl:\n    1")]
    [InlineData("exit to\n    outer")]
    [InlineData("defer\n    // comment\nwork()")]
    [InlineData("unsafe\n    // comment\nwork()")]
    public void RejectsInvalidLayout(string source)
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.NotEmpty(c.Kotonoha.DiagnosticCollection.GetArray());
    }

    [Theory]
    [InlineData("let value: i32 = if true\n    #match\n        #case false\n            yield 1\n        #case _\n            yield 2\nelse => 0")]
    [InlineData("func f() -> i32\n    loop\n        if true => return 1")]
    [InlineData("func a() -> bool => true\nif true => a() else => ()")]
    [InlineData("let unit: () = if true => ()")]
    [InlineData("func f()\n    defer\n        exit ()")]
    [InlineData("let r = if true\n    require true else => yield 0\n    yield 1\nelse => 0")]
    [InlineData("func f()\n    defer\n        require true else => exit\n        ()")]
    [InlineData("let v = choice: if true => yield to choice: 1 else => 2")]
    [InlineData("func big() -> i64 => 1\nlet v = if true => 1 else => big()")]
    [InlineData("func big() -> i64 => 1\nlet v = if true => big() else => 1")]
    [InlineData("let v = if true\n    loop => yield 1\nelse => 2")]
    [InlineData("func f()\n    lbl: match true\n        true\n            yield to lbl\n        false => ()")]
    [InlineData("func f()\n    loop\n        do\n            exit\n        ()")]
    [InlineData("func f()\n    require true else => loop => ()")]
    [InlineData("func f()\n    while true => exit ()")]
    [InlineData("func f(left: i32, right: i32)\n    left == right")]
    [InlineData("func f(flag: bool) -> i32 => match flag\n    true => 1\n    false => 2")]
    [InlineData("func f() -> i32\n    return if true\n        yield 1\n    else => 2")]
    public void AcceptsSpecifiedResults(string source)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, source + "\n" + string.Join("\n", c.Binding.Issues.Select(x => x.Code + ": " + x.Node)));
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.True(flow.Issues.Count == 0, source + "\n" + string.Join("\n", flow.Issues.Select(x => x.Message)));
        Assert.Empty(flow.PendingBinding);
    }

    [Theory]
    [InlineData("let v = if true\n    if true => yield 1\n    yield 2\nelse => 3")]
    [InlineData("func f()\n    if true\n        yield")]
    [InlineData("func f()\n    match true\n        true\n            yield\n        false => ()")]
    [InlineData("func f()\n    lbl: match true\n        true => yield to lbl: 1\n        false => ()")]
    [InlineData("func f()\n    exit to nowhere")]
    [InlineData("func f()\n    lbl: if true => exit to lbl")]
    [InlineData("func f()\n    outer: loop\n        defer => exit to outer")]
    [InlineData("func f()\n    defer => continue")]
    [InlineData("func f() -> i32\n    return 1\n    return \"x\"")]
    [InlineData("let v = if true => 1 else => \"x\"")]
    [InlineData("func f(flag: bool) -> i32\n    flag and (return 1)")]
    [InlineData("func f()\n    require true else => ()")]
    [InlineData("func f()\n    require true else\n        loop => exit")]
    [InlineData("func f()\n    require true else => while true => ()")]
    [InlineData("func f()\n    loop => exit 1")]
    [InlineData("func f()\n    choice: if true\n        yield to choice: 1\n    else => ()")]
    [InlineData("func f()\n    a: loop\n        a: loop => exit to a")]
    [InlineData("func f()\n    a: loop => exit\n    exit to a")]
    [InlineData("func f()\n    a: while (do\n        exit to a\n    ) => ()")]
    [InlineData("func f() -> i32 => if true => 1")]
    [InlineData("func f()\n    while true => exit 1")]
    [InlineData("func f()\n    defer => exit 1")]
    [InlineData("func f()\n    do => exit")]
    [InlineData("func f() => return 1")]
    [InlineData("func f()\n    lbl: do => exit to lbl: 1")]
    [InlineData("func f()\n    lbl: loop\n        exit to lbl: 1")]
    [InlineData("let v = lbl: loop\n    exit to lbl: 1\n    exit to lbl: \"x\"")]
    public void RejectsInvalidResultsAndTargets(string source)
    {
        var c = Parse(source);
        var bound = c.Bind();
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.True(!bound.IsComplete || flow.Issues.Count > 0, source);
    }

    [Theory]
    [InlineData("func f(a: i32)\n    (a, 1)", true)]
    [InlineData("func g() -> i32 => 1\nfunc f(a: i32)\n    (a, g())", false)]
    [InlineData("enum Direction\n    Self is Copy\n    North\n    South\nfunc f()\n    Direction.North", true)]
    [InlineData("enum Kind\n    North\nfunc f()\n    Kind.North", false)]
    public void EffectFreeDiscardCoversTuplesAndCopyCases(string source, bool warns)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, source + "\n" + string.Join("\n", c.Binding.Issues.Select(x => x.Code + ": " + x.Node)));
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.Issues);
        Assert.Equal(warns, flow.Warnings.Any(warning => warning.Message.Contains("effect-free")));
    }

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.True(c.Kotonoha.DiagnosticCollection.GetArray().Length == 0, source + "\n" + string.Join("\n", c.Kotonoha.DiagnosticCollection.GetArray().Select(x => x.Message)));
        return c;
    }
}
