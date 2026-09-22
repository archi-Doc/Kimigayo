// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class OptionalTryDiscardTest
{
    [Theory]
    [InlineData("let value: i32? = .None")]
    [InlineData("let value: i32?? = .Some(.None)")]
    [InlineData("let value: ((i32) -> i32)? = .None")]
    [InlineData("_ = 3")]
    [InlineData("_ = if true => 1 else => 2")]
    [InlineData("let items: [2 of i32] = [1, 2]\nfor _ in items => ()")]
    public void AcceptsNewForms(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.True(c.Ownership.Result.IsVerified, Describe(c));
    }

    [Theory]
    [InlineData("let _: i32 = 1")]
    [InlineData("func f(_: i32) => ()")]
    [InlineData("let f = func (_: i32) => ()")]
    [InlineData("let x = (_ = 1)")]
    [InlineData("_ += 1")]
    [InlineData("let x = 1@ref?")]
    [InlineData("let x: (i32)? -> i32")]
    public void RejectsSyntaxOutsideDedicatedPositions(string source)
    {
        var parsed = ParseTestHelper.Parse(source);
        Assert.NotEmpty(parsed.DiagnosticCollection.GetArray());
    }

    [Theory]
    [InlineData("Option", "func source() -> i32? => .Some(7)\nfunc run() -> i32? => .Some((try source()) + 1)\nmatch run()\n    .Some(let n) => require n == 8 else => $abort(\"value\")\n    .None => $abort(\"none\")")]
    [InlineData("None", "func source() -> i32? => .None\nfunc run() -> bool?\n    let n = try source()\n    $abort(\"continued\")\nmatch run()\n    .Some(_) => $abort(\"some\")\n    .None => ()")]
    [InlineData("Result", "func source() -> Result<i32, i32> => .Err(9)\nfunc run() -> Result<bool, i32>\n    let n = try source()\n    return .Ok(n == 1)\nmatch run()\n    .Ok(_) => $abort(\"ok\")\n    .Err(let n) => require n == 9 else => $abort(\"error\")")]
    [InlineData("Discard", "func source() -> Result<i32, i32> => .Ok(1)\nfunc run() -> Result<(), i32>\n    _ = try source()\n    return .Ok(())\n_ = run()\n_ = \"destroy\"")]
    public void ExecutesBothPaths(string name, string source)
        {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, Describe(c));
        ScalarEmissionTest.EmitFixture("OptionalTry" + name, source + "\nConsole.writeLine(\"ok\")", "ok\n");
    }

    [Theory]
    [InlineData("func source() -> i32? => .None\nfunc run() -> i32 => try source()")]
    [InlineData("func source() -> i32? => .None\nlet x = try source()")]
    [InlineData("func run() -> i32? => .Some(try 1)")]
    [InlineData("func run() -> i32? => .Some(try .None)")]
    [InlineData("func source() -> Result<(), i32> => .Ok(())\nfunc run() -> Result<(), i32> => try source()")]
    [InlineData("func source() -> Result<i32, string> => .Err(\"bad\")\nfunc run() -> Result<i32, i32> => .Ok(try source())")]
    [InlineData("func source() -> i32? => .Some(1)\nfunc run() -> Result<i32, i32> => .Ok(try source())")]
    [InlineData("func source() -> i32? => .Some(1)\nfunc run() -> i32?\n    defer => _ = try source()\n    return .None")]
    public void RejectsInvalidPropagation(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }

    [Theory]
    [InlineData("try source()", true)]
    [InlineData("_ = try source()", false)]
    [InlineData("source()", true)]
    [InlineData("_ = source()", false)]
    public void WarnsOnlyForImplicitDiscard(string statement, bool warning)
    {
        var c = MinimalEmissionTest.Analyze("func source() -> Result<i32, i32> => .Ok(1)\nfunc run() -> Result<(), i32>\n    " + statement + "\n    return .Ok(())");
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Equal(warning, flow.Warnings.Count != 0);
    }

    [Theory]
    [InlineData("ref{static}/i32?", "Option<ref{static}/i32>")]
    [InlineData("ref{static}/(i32?)", "ref{static}/Option<i32>")]
    [InlineData("i32??", "Option<Option<i32>>")]
    [InlineData("(i32) -> i32?", "(i32) -> Option<i32>")]
    [InlineData("((i32) -> i32)?", "Option<(i32) -> i32>")]
    [InlineData("[2 of i32?]?", "Option<[2 of Option<i32>]>")]
    public void OptionalSpellingHasIdenticalType(string abbreviated, string complete)
    {
        var c = MinimalEmissionTest.Analyze($"func first(value: {abbreviated}) => ()\nfunc second(value: {complete}) => ()");
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        var functions = Descendants(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Where(x => !x.IsGenerated).ToArray();
        Assert.Same(functions[0].Parameters[0].Type.BoundType, functions[1].Parameters[0].Type.BoundType);
    }

    [Theory]
    [InlineData("Nested", "func source() -> i32?? => .Some(.Some(8))\nfunc run() -> i32? => .Some(try try source())\nmatch run()\n    .Some(8) => ()\n    _ => $abort(\"nested\")")]
    [InlineData("SingleLayer", "func source() -> i32?? => .Some(.None)\nfunc run() -> bool?\n    let inner = try source()\n    let isNone = match inner\n        .None => true\n        .Some(_) => false\n    return .Some(isNone)\nmatch run()\n    .Some(true) => ()\n    _ => $abort(\"flattened\")")]
    [InlineData("Borrow", "struct S\n    public var n: i32\n    public init(n: i32) => self.n = n\nfunc wrap(x: ref/S) -> ref{x}/S? => .Some(x)\nfunc run(x: ref/S) -> i32?\n    let r = try wrap(x)\n    return .Some(r.n)\nlet s = S.init(8)\nmatch run(s)\n    .Some(8) => ()\n    _ => $abort(\"borrow\")")]
    [InlineData("Lambda", "func source() -> i32? => .Some(8)\nlet f = func () -> i32? => .Some(try source())\nmatch f()\n    .Some(8) => ()\n    _ => $abort(\"lambda\")")]
    [InlineData("Generic", "func unwrap<T>(x: T?) -> T? => .Some(try x)\nmatch unwrap<i32>(.Some(8))\n    .Some(8) => ()\n    _ => $abort(\"generic\")\n_ = unwrap<string>(.Some(\"owned\"))")]
    public void ExecutesCombinations(string name, string source)
        {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, Describe(c));
        ScalarEmissionTest.EmitFixture("OptionalTry" + name, source + "\nConsole.writeLine(\"ok\")", "ok\n");
    }

    [Theory]
    [InlineData("try source()", "()", 0)]
    [InlineData("source()", "()", 2)]
    [InlineData("(try source())", "i32", 3)]
    [InlineData("if true => try source()", "i32", 3)]
    [InlineData("try source()", "Result<(), i32>", 2)]
    [InlineData("let x = try source()", "i32", 0)]
    [InlineData("_ = do\n        source()", "i32", 1)]
    public void WarningPriorityIsLocalToDiscard(string statement, string payload, int priority)
    {
        var value = payload == "()" ? "()" : payload.StartsWith("Result", StringComparison.Ordinal) ? ".Ok(())" : "1";
        var c = MinimalEmissionTest.Analyze($"func source() -> Result<{payload}, i32> => .Ok({value})\nfunc run() -> Result<(), i32>\n    {statement}\n    return .Ok(())");
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        var warnings = c.AnalyzeControlFlow(c.Binding.TypeSystem).Warnings;
        if (priority == 0)
        {
            Assert.Empty(warnings);
        }
        else
        {
            Assert.Equal(priority, Assert.Single(warnings).Priority);
        }
    }

    [Fact]
    public void ExplicitDiscardDestroysAtStatementEnd()
        => ScalarEmissionTest.EmitFixture("OptionalTryDiscardCleanup", "struct Token\n    deinit => Console.writeLine(\"destroy\")\n_ = Token.init()\nConsole.writeLine(\"after\")", "destroy\nafter\n");

    [Fact]
    public void FailureCleansAcquiredArgumentsBeforeFunctionLocals()
        => EmitChecked("OptionalTryFailureCleanup", "struct Token\n    let id: i32\n    public init(id: i32) => self.id = id\n    deinit\n        if self.id == 1 => Console.writeLine(\"local\")\n        if self.id == 2 => Console.writeLine(\"argument\")\n        if self.id == 3 => Console.writeLine(\"late\")\nfunc fail() -> Result<i32, string> => .Err(\"error\")\nfunc skipped(a: Token, b: i32, c: Token) => $abort(\"called\")\nfunc run() -> Result<(), string>\n    let local = Token.init(1)\n    defer => Console.writeLine(\"defer\")\n    skipped(Token.init(2), try fail(), Token.init(3))\n    return .Ok(())\nmatch run()\n    .Ok(_) => $abort(\"ok\")\n    .Err(let e) => Console.writeLine(e)", "argument\ndefer\nlocal\nerror\n");

    [Theory]
    [InlineData("Partial", "(key, _)", "sum += key", 4)]
    [InlineData("Repeated", "(_, _)", "sum += 1", 2)]
    [InlineData("Whole", "_", "sum += 1", 2)]
    [InlineData("Named", "pair", "sum += pair.0", 4)]
    public void UnnamedIterationPreservesShape(string name, string binding, string body, int total)
        => EmitChecked("OptionalTryFor" + name, $"let pairs: [2 of (i32, i32)] = [(1, 2), (3, 4)]\nvar sum = 0\nfor {binding} in pairs => {body}\nrequire sum == {total} else => $abort(\"iteration\")", string.Empty);

    [Fact]
    public void RebindingRetainsCurrentTryArms()
    {
        var c = MinimalEmissionTest.Analyze("func run(x: i32?) -> i32? => .Some(try x)\n_ = run(.Some(1))");
        for (var i = 0; i < 3; i++)
        {
            Assert.True(c.Bind().IsComplete, Describe(c));
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
            Assert.True(c.Emission.Validate(out var error), error);
        }
    }

    private static void EmitChecked(string name, string source, string expected)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, Describe(c));
        ScalarEmissionTest.EmitFixture(name, source, expected);
    }

    private static IEnumerable<Koto> Descendants(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static string Describe(Compilation c)
        => MinimalEmissionTest.Describe(c, null) + "\n" + string.Join("\n", c.Binding.Issues.Select(x => x.Node.GetType().Name + ":" + x.Node.BindingFailure + ":" + x.Node)) +
            "\n" + string.Join("\n", c.Kotonoha.DiagnosticCollection.GetArray().Select(x => x.Message));
}
