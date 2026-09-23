// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class BorrowStructEmissionTest
{
    private const string Counter = "struct Counter\n    public var value: i32 = 0\n    deinit => Console.writeLine(\"drop\")\nfunc add(counter: uniq/Counter, amount: i32)\n    counter.value = counter.value + amount\nfunc borrow(counter: ref/Counter) -> ref{counter}/Counter => counter\n";

    [Fact]
    public void Milestone5()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), "milestones", "Milestone5.kimi"));
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        Assert.True(c.Ownership.Result.IsVerified, c.Ownership.Result + "\nObligations: " + string.Join('\n', c.Binding.Obligations) + "\n" + string.Join('\n', c.Ownership.Issues) + "\nFlow: " + string.Join('\n', c.Ownership.ControlFlow!.Issues) + "\nPending: " + string.Join('\n', c.Ownership.ControlFlow.PendingBinding));
        ScalarEmissionTest.EmitFixture("BorrowStructMilestone5", source, "Borrowed sum is 55.\nView destroyed; counter is still 55.\nFinal value is 56.\nCounter destroyed.\nDone.\n");
    }

    [Theory]
    [InlineData("Default", "struct S\n    public var value: i32 = 7\n    deinit => Console.writeLine(\"drop\")\nlet s = S.init()\nif s.value == 7 => Console.writeLine(\"ok\")", "ok\ndrop\n")]
    [InlineData("Empty", "struct S\n    deinit => Console.writeLine(\"drop\")\nlet s = S.init()", "drop\n")]
    [InlineData("Order", "struct S\n    let a: () = ::Kimi.Console.writeLine(\"first\")\n    let b: () = ::Kimi.Console.writeLine(\"second\")\nlet s = S.init()", "first\nsecond\n")]
    public void SynthesizedConstruction(string name, string source, string stdout)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
        ScalarEmissionTest.EmitFixture("BorrowStruct" + name, source, stdout);
    }

    [Theory]
    [InlineData("struct S\n    let n: i32\nlet s = S.init()")]
    [InlineData("struct S\n    let n: i32 = 1\n    init() => ()\nlet s = S.init()")]
    [InlineData("struct S\n    let n: i32 = 1\n    public init(n: i32) => ()\nlet s = S.init()")]
    [InlineData("struct S\n    let n: i32 = true\nlet s = S.init()")]
    public void RejectsInvalidConstruction(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified && !c.Kotonoha.HasSourceErrors);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("add(counter@uniq, 1)")]
    [InlineData("finish(counter@move)")]
    [InlineData("counter.value = 99")]
    [InlineData("counter = Counter.init()")]
    public void DestructorKeepsStoredLoanAlive(string statement)
    {
        var milestone = File.ReadAllText(Path.Combine(FindRoot(), "milestones", "Milestone5.kimi"));
        Assert.Contains("// Mutating or moving counter here", milestone);
        var source = milestone.Replace("// Mutating or moving counter here", statement + "\n        // Mutating or moving counter here");
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("var c = Counter.init()\nlet r = borrow(c@ref)\nadd(c@uniq, 1)\nlet n = r.value")]
    [InlineData("func take(c: Counter) => ()\nvar c = Counter.init()\nlet r = borrow(c@ref)\ntake(c@move)\nlet n = r.value")]
    [InlineData("func take(c: Counter) => ()\nvar c = Counter.init()\ntake(c)")]
    [InlineData("var c = Counter.init()\nadd(c, 1)")]
    [InlineData("func bad() -> ref{static}/Counter\n    let c = Counter.init()\n    return c@ref")]
    [InlineData("func bad(c: ref/Counter)\n    c.value = 9")]
    [InlineData("let c = Counter.init()\nadd(c@uniq, 1)")]
    [InlineData("func both(a: uniq/Counter, b: uniq/Counter) => ()\nvar c = Counter.init()\nboth(c@uniq, c@uniq)")]
    [InlineData("func both(a: ref/Counter, b: uniq/Counter) => ()\nvar c = Counter.init()\nboth(c@ref, c@uniq)")]
    [InlineData("var c = Counter.init()\nlet r = c@ref\nadd(r@uniq, 1)")]
    [InlineData("let r = borrow(Counter.init())\nlet n = r.value")]
    [InlineData("func bad(c: uniq/Counter)\n    let r = borrow(c@ref)\n    c.value = 9\n    let n = r.value\nvar c = Counter.init()\nbad(c@uniq)")]
    [InlineData("var c = Counter.init()\nlet u = c@uniq\nlet r = borrow(u@ref)\nu.value = 9\nlet n = r.value")]
    [InlineData("func bad(c: uniq/Counter)\n    $abort(\"stop\")\n    let r = c@ref\n    c.value = 9\n    let n = r.value")]
    public void RejectsInvalidBorrow(string body)
    {
        var c = MinimalEmissionTest.Analyze(Counter + body);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified && !c.Kotonoha.HasSourceErrors, body);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("Returned", "var c = Counter.init()\nadd(c@uniq, 7)\nlet r = borrow(c@ref)\nif r.value == 7 => Console.writeLine(\"ok\")", "ok\ndrop\n")]
    [InlineData("LastUse", "var c = Counter.init()\nlet r = borrow(c@ref)\nlet n = r.value\nadd(c@uniq, 7)\nif c.value == 7 => Console.writeLine(\"ok\")", "ok\ndrop\n")]
    [InlineData("SharedCopies", "var c = Counter.init()\nlet r = borrow(c@ref)\nlet s = r\nif s.value == r.value => Console.writeLine(\"ok\")", "ok\ndrop\n")]
    [InlineData("Reborrow", "func forward(c: uniq/Counter)\n    add(c, 7)\n    add(c, 1)\nvar c = Counter.init()\nforward(c@uniq)\nif c.value == 8 => Console.writeLine(\"ok\")", "ok\ndrop\n")]
    [InlineData("OwnerArgument", "func inspect(c: Counter)\n    let r = borrow(c@ref)\n    if r.value == 0 => Console.writeLine(\"ok\")\ninspect(Counter.init())", "ok\ndrop\n")]
    [InlineData("ExclusiveLocal", "var c = Counter.init()\nlet u = c@uniq\nadd(u@uniq, 7)\nadd(u@uniq, 1)\nif u.value == 8 => Console.writeLine(\"ok\")", "ok\ndrop\n")]
    public void ExecutesBorrow(string name, string body, string stdout)
        => ScalarEmissionTest.EmitFixture("BorrowStruct" + name, Counter + body, stdout);

    [Fact]
    public void RebindingAndReloadRetainSynthesis()
    {
        var c = MinimalEmissionTest.Analyze("struct S\n    public var n: i32 = 3\nlet s = S.init()");
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        var tree = c.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(c);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(c.Bind().IsComplete);
            Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SPEC.md")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }
}
