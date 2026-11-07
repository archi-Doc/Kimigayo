// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class DynamicArrayIterationTest
{
    private const string Task = "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop\n        match self.id\n            1 => Console.writeLine(\"drop 1\")\n            2 => Console.writeLine(\"drop 2\")\n            _ => Console.writeLine(\"drop 3\")\n";

    [Theory]
    [InlineData("values", "iterate")]
    [InlineData("values@ref", "iterate")]
    [InlineData("values@uniq", "iterateUniq")]
    [InlineData("values@move", "intoIterator")]
    public void SelectsTheLibraryIterationEntry(string subject, string entry)
    {
        var c = MinimalEmissionTest.Analyze("var values: Array<i32> = [1, 2]\nfor value in " + subject + " => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var loop = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ForKoto>());
        Assert.NotNull(loop.IterationOf().Entry);
        Assert.Equal(entry, Assert.IsType<IdentifierNameKoto>(Assert.IsType<MemberAccessKoto>(loop.IterationOf().Entry!.Method).Right).IdentifierName);
    }

    [Theory]
    [InlineData("Scalar", "let values: Array<i32> = [10, 20, 12]\nvar total = 0\nfor value in values@move => total += value\nrequire total == 42 else => $abort(\"sum\")", "")]
    [InlineData("Empty", "let values: Array<i32> = []\nfor value in values@move => $abort(\"empty\")\nConsole.writeLine(\"done\")", "done\n")]
    [InlineData("Temporary", "func make() -> Array<string> => [\"first\", \"second\"]\nfor value in make() => Console.writeLine(value)", "first\nsecond\n")]
    [InlineData("Continue", "let values: Array<i32> = [1, 2, 3]\nvar total = 0\nfor value in values@move\n    if value == 2 => continue\n    total += value\nrequire total == 4 else => $abort(\"continue\")", "")]
    [InlineData("Boolean", "let values: Array<bool> = [true, false, true]\nvar count = 0\nfor value in values@move\n    if value => count += 1\nrequire count == 2 else => $abort(\"bool storage\")", "")]
    [InlineData("Nested", "let values: Array<i32> = [1, 2]\nvar count = 0\nfor value in values@move\n    let inner: Array<i32> = [10, 20]\n    for item in inner@move => count += value + item\nrequire count == 66 else => $abort(\"nested\")", "")]
    [InlineData("Reinitialize", "var values: Array<i32> = [1, 2]\nfor value in values@move => ()\nvalues = [42]\nrequire values[0] == 42 else => $abort(\"reinitialize\")", "")]
    [InlineData("StringReturn", "func first(values: Array<string>) -> string\n    for value in values@move => return value@move\n    $abort(\"empty\")\nlet values: Array<string> = [\"first\", \"second\"]\nConsole.writeLine(first(values@move))", "first\n")]
    [InlineData("Tuple", "let values: Array<(i32, i32)> = [(20, 22)]\nfor (first, second) in values@move\n    require first + second == 42 else => $abort(\"tuple\")", "")]
    [InlineData("OwnedTuple", "let values: Array<(string, string)> = [(\"first\", \"second\")]\nfor (first, second) in values@move\n    Console.writeLine(first)\n    Console.writeLine(second)", "first\nsecond\n")]
    public void ConsumesElementsInIndexOrder(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("DynamicArrayIteration" + name, source, stdout);

    [Theory]
    [InlineData("Complete", "for task in tasks@move\n    Console.writeLine(\"body\")\nConsole.writeLine(\"done\")", "body\ndrop 1\nbody\ndrop 2\nbody\ndrop 3\ndone\n")]
    [InlineData("Exit", "for task in tasks@move\n    require task.id == 1 else => $abort(\"order\")\n    Console.writeLine(\"body\")\n    exit\nConsole.writeLine(\"done\")", "body\ndrop 1\ndrop 3\ndrop 2\ndone\n")]
    [InlineData("Unnamed", "for _ in tasks@move\n    Console.writeLine(\"body\")\n    exit\nConsole.writeLine(\"done\")", "body\ndrop 1\ndrop 3\ndrop 2\ndone\n")]
    [InlineData("Continue", "for task in tasks@move\n    Console.writeLine(\"body\")\n    continue\nConsole.writeLine(\"done\")", "body\ndrop 1\nbody\ndrop 2\nbody\ndrop 3\ndone\n")]
    public void DestroysYieldedAndRemainingElementsOnce(string name, string loop, string stdout)
        => ScalarEmissionTest.EmitFixture("DynamicArrayIteration" + name + "Tasks", Task + "let tasks: Array<Task> = [Task.init(1), Task.init(2), Task.init(3)]\n" + loop, stdout);

    // SPEC 4.9.7, 22.2.2 (R8-A1d): an owning iterator moved between holders and partly consumed destroys what remains in decreasing index order,
    // Array and fixed array alike, an inner Array with it; a zero-sized element runs each destructor.
    [Theory]
    [InlineData("Moved", "var noisy: Array<Noisy> = [Noisy.init(1), Noisy.init(2), Noisy.init(3), Noisy.init(4)]\nvar it = noisy@move.intoIterator()\nmatch it.next()\n    .Some(let first) => Console.writeLine(\"took \\(first.id)\")\n    .None => ()\nlet moved = it@move\nConsole.writeLine(\"moved\")\nvar again = moved@move\nmatch again.next()\n    .Some(let second) => Console.writeLine(\"took \\(second.id)\")\n    .None => ()\nConsole.writeLine(\"end\")\n", "took 1\ndrop 1\nmoved\ntook 2\ndrop 2\nend\ndrop 4\ndrop 3\n")]
    [InlineData("Fixed", "var noisy: [3 of Noisy] = [Noisy.init(7), Noisy.init(8), Noisy.init(9)]\nvar it = noisy@move.intoIterator()\nmatch it.next()\n    .Some(let first) => Console.writeLine(\"took \\(first.id)\")\n    .None => ()\nConsole.writeLine(\"end\")\n", "took 7\ndrop 7\nend\ndrop 9\ndrop 8\n")]
    [InlineData("Nested", "var outer: Array<Array<Noisy>> = [[Noisy.init(11), Noisy.init(12)], [Noisy.init(13)]]\nvar it = outer@move.intoIterator()\nmatch it.next()\n    .Some(let row) => Console.writeLine(\"row \\(row.length)\")\n    .None => ()\nConsole.writeLine(\"end\")\n", "row 2\ndrop 12\ndrop 11\nend\ndrop 13\n")]
    public void AnOwningIteratorDestroysWhatRemainsInDecreasingOrder(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("DynamicArrayIterationRemaining" + name, "struct Noisy\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop => Console.writeLine(\"drop \\(self.id)\")\n" + source, stdout);

    // SPEC 4.8.4, 4.8.5 (R8-A1d): each view over a zero-sized element and over an empty provider counts its elements and writes nothing.
    [Fact]
    public void ViewsCountZeroSizedAndEmptyElements()
    {
        const string Units = "var units: Array<()> = [(), ()]\nvar seen: isize = 0\nfor _ in units@uniq => seen += 1\nfor _ in units => seen += 1\nvar view = units.sliceUniq()\nConsole.writeLine(\"\\(seen) \\(view.length)\")\n";
        const string Empties = "var none: [0 of i32] = []\nvar nothing = none.sliceUniq()\nvar empty: Array<i32> = []\nvar still = empty.sliceUniq()\nConsole.writeLine(\"\\(nothing.length) \\(still.length)\")\n";
        ScalarEmissionTest.EmitFixture("DynamicArrayIterationZeroSized", Units + Empties, "4 2\n0 0\n");
    }

    [Fact]
    public void ReturnSecuresTheYieldBeforeDestroyingTheIterator()
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayIterationReturn",
            Task + "func first(tasks: Array<Task>) -> Task\n    for task in tasks@move => return task@move\n    $abort(\"empty\")\nlet tasks: Array<Task> = [Task.init(1), Task.init(2), Task.init(3)]\nlet taken = first(tasks@move)\nrequire taken.id == 1 else => $abort(\"first\")\nConsole.writeLine(\"done\")",
            "drop 3\ndrop 2\ndone\ndrop 1\n");

    [Theory]
    [InlineData("Conditional", "if true => tasks@move else => tasks@move")]
    [InlineData("Scope", "label selection: do\n    exit to selection tasks@move\n")]
    [InlineData("Match", "match true\n    true => tasks@move\n    false => tasks@move\n")]
    [InlineData("Loop", "label selection: loop\n    exit to selection tasks@move\n")]
    public void OwnsTheResultHandleAcrossControlFlow(string name, string expression)
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayIterationResult" + name,
            Task + "let tasks: Array<Task> = [Task.init(1), Task.init(2), Task.init(3)]\nfor task in (" + expression + ")\n    require task.id == 1 else => $abort(\"first\")\n    exit\nConsole.writeLine(\"done\")",
            "drop 1\ndrop 3\ndrop 2\ndone\n");

    [Theory]
    [InlineData("let values: Array<i32> = [1]\nfor value in values@move => ()\nlet n = values.length")]
    [InlineData("var values: Array<i32> = [1]\nlet borrow = values@ref\nfor value in values@move => ()\nlet n = borrow.length")]
    [InlineData("let values: Array<i32> = [1]\nfor value in values@move => value = 2")]
    [InlineData("let values: Array<i32> = [1]\nfor value in (if true => values@move else => values@move) => ()\nlet n = values.length")]
    public void RejectsInvalidConsumption(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(c.Generate(output));
        Assert.Empty(output.ToString());
        Assert.False(c.Binding.Result.IsComplete && c.Analysis.IsVerified);
    }
}
