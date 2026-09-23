// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DynamicArraySliceTest
{
    [Theory]
    [InlineData("Whole", "var values: Array<i32> = [10, 20, 12]\nlet view = values[..]\nrequire view.length == 3 and view[0] + view[1] + view[2] == 42 else => $abort(\"view\")\nvalues@uniq.clear()\nrequire values.length == 0 else => $abort(\"loan ended\")")]
    [InlineData("Partial", "let values: Array<i32> = [10, 20, 22, 30]\nlet view = values[1..3]\nlet again = view[..]\nrequire again.length == 2 and again[0] + again[1] == 42 else => $abort(\"partial\")")]
    [InlineData("SharedIteration", "var values: Array<i32> = [10, 20, 12]\nvar total = 0\nfor value in values => total += value\nrequire total == 42 and values.length == 3 else => $abort(\"shared\")\nvalues@uniq.clear()")]
    [InlineData("Borrowed", "func sum(values: ref/Array<i32>) -> i32\n    let view = values[..]\n    return view[0] + view[1]\nlet values: Array<i32> = [20, 22]\nrequire sum(values@ref) == 42 else => $abort(\"borrowed\")")]
    [InlineData("Exclusive", "var values: Array<i32> = [20, 22]\nlet handle = values@uniq\nlet view = handle[..]\nrequire view[0] + view[1] == 42 else => $abort(\"exclusive\")\nhandle@uniq.clear()")]
    [InlineData("Temporary", "func make() -> Array<i32> => [20, 22]\nrequire make()[..][1] == 22 else => $abort(\"temporary\")")]
    [InlineData("UnusedTemporary", "func make() -> Array<i32> => [20, 22]\nlet unused = make()[..]\nConsole.writeLine(\"\")")]
    [InlineData("Empty", "let values: Array<i32> = []\nlet view = values[..]\nrequire view.length == 0 and view.isEmpty else => $abort(\"empty\")\nfor value in values => $abort(\"iteration\")")]
    public void SharesStorageAndKeepsTheOwner(string name, string source)
        => ScalarEmissionTest.EmitFixture("DynamicArraySlice" + name, source, name == "UnusedTemporary" ? "\n" : string.Empty);

    [Fact]
    public void SharedIterationDoesNotDestroyOrMoveNonCopyElements()
        => ScalarEmissionTest.EmitFixture(
            "DynamicArraySliceTasks",
            "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit => Console.writeLine(\"drop\")\nlet values: Array<Task> = [Task.init(20), Task.init(22)]\nvar sum = 0\nfor task in values => sum += task.id\nrequire sum == 42 and values.length == 2 else => $abort(\"shared tasks\")\nConsole.writeLine(\"done\")",
            "done\ndrop\ndrop\n");

    [Theory]
    [InlineData("var values: Array<i32> = [1]\nlet view = values[..]\nvalues@uniq.clear()\nlet n = view[0]")]
    [InlineData("var values: Array<i32> = [1]\nlet view = values[0..0]\nvalues@uniq.reserve(0)\nlet n = view.length")]
    [InlineData("var values: Array<i32> = [1]\nlet view = values[..]\nvalues[0] = 2\nlet n = view[0]")]
    [InlineData("var values: Array<i32> = [1]\nlet view = values[..]\nlet moved = values@move\nlet n = view[0]")]
    [InlineData("var values: Array<i32> = [1]\nlet handle = values@uniq\nlet view = handle[..]\nhandle@uniq.clear()\nlet n = view[0]")]
    [InlineData("var values: Array<i32> = [1]\nlet view = values[..]\nlet derived = view[0..0]\nvalues@uniq.clear()\nlet n = derived.length")]
    [InlineData("var values: Array<i32> = [1]\nfor value in values\n    values@uniq.clear()\n    let n = value")]
    [InlineData("func make() -> Array<i32> => [1]\nlet view = make()[..]\nlet n = view[0]")]
    [InlineData("var values: Array<i32> = [1]\nlet view = values[(index: do\n    values@uniq.clear()\n    exit to index: 0\n)..]\nlet n = view.length")]
    public void RejectsInvalidationAndExpiredBackingStorage(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }
}
