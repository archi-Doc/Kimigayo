// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

public class DynamicArrayElementTest
{
    [Theory]
    [InlineData("Owned", "var values: Array<i32> = [10, 20, 12]\nvar total = 0\nfor i in values.indices => total += values[i]\nrequire total == 42 and values.length == 3 else => $abort(\"read\")\nvalues@uniq.append(7)\nrequire values[3] == 7 else => $abort(\"append\")")]
    [InlineData("Shared", "func at(values: ref/Array<i32>, i: isize) -> i32 => values[i]\nlet values: Array<i32> = [10, 42]\nrequire at(values@ref, 1) == 42 else => $abort(\"read\")")]
    [InlineData("Exclusive", "func at(values: uniq/Array<i32>) -> i32 => values[0] + values[1]\nvar values: Array<i32> = [20, 22]\nrequire at(values@uniq) == 42 else => $abort(\"read\")")]
    [InlineData("Temporary", "func make() -> Array<bool> => [false, true]\nrequire make()[1] else => $abort(\"read\")")]
    public void ReadsCopyElements(string name, string source)
        => ScalarEmissionTest.EmitFixture("DynamicArrayRead" + name, source, string.Empty);

    [Theory]
    [InlineData("Copy", "func sum(values: Array<i32>) -> i32\n    var total = 0\n    for i in values.indices => total += values[i]\n    return total\nrequire sum([10, 20, 12]) == 42 and sum([]) == 0 else => $abort(\"sum\")\nConsole.writeLine(\"done\")", "done\n")]
    [InlineData("NonCopy", "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit => Console.writeLine(\"drop\")\nfunc last(values: Array<Task>) -> i32 => values[1].id\nrequire last([Task.init(1), Task.init(42)]) == 42 else => $abort(\"last\")\nConsole.writeLine(\"done\")", "drop\ndrop\ndone\n")]
    [InlineData("Shared", "func count<T>(values: ref/Array<T>) -> isize => values.length\nrequire count([1, 2, 3]) == 3 and count([\"a\"]) == 1 else => $abort(\"count\")\nConsole.writeLine(\"done\")", "done\n")]
    [InlineData("Generic", "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit => Console.writeLine(\"drop\")\nfunc count<T>(values: Array<T>) -> isize => values.length\nlet tasks: Array<Task> = [Task.init(1), Task.init(2)]\nrequire count(tasks@move) == 2 and count([1, 2, 3]) == 3 and count([\"a\"]) == 1 else => $abort(\"count\")\nConsole.writeLine(\"done\")", "drop\ndrop\ndone\n")]
    [InlineData("GenericResult", "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit => Console.writeLine(\"drop\")\nfunc pair<T>(first: T, second: T) -> Array<T> => [first@move, second@move]\nlet tasks = pair(Task.init(1), Task.init(2))\nrequire tasks.length == 2 and tasks[1].id == 2 and pair(3, 4)[0] == 3 else => $abort(\"pair\")\nConsole.writeLine(\"done\")", "done\ndrop\ndrop\n")]
    public void ACallArgumentLiteralConstructsTheParameterArray(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("DynamicArrayArgumentLiteral" + name, source, stdout);

    [Theory]
    [InlineData("Negative", "-1")]
    [InlineData("Length", "1")]
    [InlineData("Maximum", "9223372036854775807")]
    public void BoundsAbort(string name, string index)
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayReadBounds" + name,
            "let values: Array<i32> = [42]\nlet n = values[" + index + "]",
            string.Empty,
            1,
            "Hello.kimi:2:9: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    [Theory]
    [InlineData("var values: Array<i32> = [42]\nlet n = values[(work: do\n    values@uniq.clear()\n    exit to work: 0)]")]
    [InlineData("var values: Array<i32> = [42]\nlet borrow = values@ref\nvalues@uniq.clear()\nlet n = borrow[0]")]
    [InlineData("let values: Array<i32> = [42]\nlet moved = values@move\nlet n = values[0]")]
    [InlineData("let values: Array<string> = [\"owned\"]\nlet taken = values[0]@move")]
    [InlineData("let values: Array<i32> = [42]\nlet taken = values[0]@move")]
    [InlineData("let values: Array<i32> = [42]\nlet i: i32 = 0\nlet n = values[i]")]
    [InlineData("var values: Array<i32> = [42]\nlet borrow = values@uniq\nlet n = borrow[(work: do\n    borrow@uniq.clear()\n    exit to work: 0)]")]
    public void RejectsInvalidAccess(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }
}
