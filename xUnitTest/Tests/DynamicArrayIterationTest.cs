// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

public class DynamicArrayIterationTest
{
    private const string Task = "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit\n        match self.id\n            1 => Console.writeLine(\"drop 1\")\n            2 => Console.writeLine(\"drop 2\")\n            _ => Console.writeLine(\"drop 3\")\n";

    [Theory]
    [InlineData("Scalar", "let values: Array<i32> = [10, 20, 12]\nvar total = 0\nfor value in values@move => total += value\nrequire total == 42 else => $abort(\"sum\")", "")]
    [InlineData("Empty", "let values: Array<i32> = []\nfor value in values@move => $abort(\"empty\")\nConsole.writeLine(\"done\")", "done\n")]
    [InlineData("Temporary", "func make() -> Array<string> => [\"first\", \"second\"]\nfor value in make() => Console.writeLine(value)", "first\nsecond\n")]
    [InlineData("Continue", "let values: Array<i32> = [1, 2, 3]\nvar total = 0\nfor value in values@move\n    if value == 2 => continue\n    total += value\nrequire total == 4 else => $abort(\"continue\")", "")]
    [InlineData("Boolean", "let values: Array<bool> = [true, false, true]\nvar count = 0\nfor value in values@move\n    if value => count += 1\nrequire count == 2 else => $abort(\"bool storage\")", "")]
    [InlineData("Nested", "let values: Array<i32> = [1, 2]\nvar count = 0\nfor value in values@move\n    let inner: Array<i32> = [10, 20]\n    for item in inner@move => count += value + item\nrequire count == 66 else => $abort(\"nested\")", "")]
    [InlineData("Reinitialize", "var values: Array<i32> = [1, 2]\nfor value in values@move => ()\nvalues = [42]\nrequire values[0] == 42 else => $abort(\"reinitialize\")", "")]
    [InlineData("StringReturn", "func first(values: Array<string>) -> string\n    for value in values@move => return value@move\n    $abort(\"empty\")\nlet values: Array<string> = [\"first\", \"second\"]\nConsole.writeLine(first(values@move))", "first\n")]
    public void ConsumesElementsInIndexOrder(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("DynamicArrayIteration" + name, source, stdout);

    [Theory]
    [InlineData("Complete", "for task in tasks@move\n    Console.writeLine(\"body\")\nConsole.writeLine(\"done\")", "body\ndrop 1\nbody\ndrop 2\nbody\ndrop 3\ndone\n")]
    [InlineData("Exit", "for task in tasks@move\n    require task.id == 1 else => $abort(\"order\")\n    Console.writeLine(\"body\")\n    exit\nConsole.writeLine(\"done\")", "body\ndrop 1\ndrop 3\ndrop 2\ndone\n")]
    [InlineData("Unnamed", "for _ in tasks@move\n    Console.writeLine(\"body\")\n    exit\nConsole.writeLine(\"done\")", "body\ndrop 1\ndrop 3\ndrop 2\ndone\n")]
    [InlineData("Continue", "for task in tasks@move\n    Console.writeLine(\"body\")\n    continue\nConsole.writeLine(\"done\")", "body\ndrop 1\nbody\ndrop 2\nbody\ndrop 3\ndone\n")]
    public void DestroysYieldedAndRemainingElementsOnce(string name, string loop, string stdout)
        => ScalarEmissionTest.EmitFixture("DynamicArrayIteration" + name + "Tasks", Task + "let tasks: Array<Task> = [Task.init(1), Task.init(2), Task.init(3)]\n" + loop, stdout);

    [Fact]
    public void ReturnSecuresTheYieldBeforeDestroyingTheIterator()
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayIterationReturn",
            Task + "func first(tasks: Array<Task>) -> Task\n    for task in tasks@move => return task@move\n    $abort(\"empty\")\nlet tasks: Array<Task> = [Task.init(1), Task.init(2), Task.init(3)]\nlet taken = first(tasks@move)\nrequire taken.id == 1 else => $abort(\"first\")\nConsole.writeLine(\"done\")",
            "drop 3\ndrop 2\ndone\ndrop 1\n");

    [Theory]
    [InlineData("let values: Array<i32> = [1]\nfor value in values@move => ()\nlet n = values.length")]
    [InlineData("var values: Array<i32> = [1]\nlet borrow = values@ref\nfor value in values@move => ()\nlet n = borrow.length")]
    [InlineData("let values: Array<i32> = [1]\nfor value in values@move => value = 2")]
    [InlineData("let values: Array<(i32, i32)> = [(1, 2)]\nfor (first, second) in values@move => ()")]
    public void RejectsInvalidConsumption(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }
}
