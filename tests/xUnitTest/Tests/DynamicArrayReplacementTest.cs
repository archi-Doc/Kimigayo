// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

public class DynamicArrayReplacementTest
{
    [Theory]
    [InlineData("Scalar", "var values: Array<i32> = [10, 20]\nlet capacity = values.capacity\nvalues[0] = values[1] + 22\nrequire values[0] == 42 and values[1] == 20 and values.length == 2 and values.capacity == capacity else => $abort(\"replace\")", "")]
    [InlineData("Updates", "var values: Array<i32> = [10, 20]\nlet amount = values[1]\nvalues[0] += amount\nlet old = values[0]++\nlet next = ++values[1]\nrequire old == 30 and values[0] == 31 and next == 21 and values[1] == 21 else => $abort(\"update\")", "")]
    [InlineData("Strings", "var names: Array<string> = [\"first\", \"last\"]\nnames[0] = \"new\"\nlet first = names@uniq.remove(0)\nConsole.writeLine(first)\nnames[0] = \"tail\"\nlet last = names@uniq.remove(0)\nConsole.writeLine(last)", "new\ntail\n")]
    [InlineData("RhsRead", "var values: Array<i32> = [10, 20]\nvalues[0] += values[1]\nvalues[1] += values[1]\nrequire values[0] == 30 and values[1] == 40 else => $abort(\"rhs\")", "")]
    [InlineData("Order", "func value() -> i32\n    Console.writeLine(\"value\")\n    return 42\nfunc index() -> isize\n    Console.writeLine(\"index\")\n    return 0\nvar values: Array<i32> = [1]\nvalues[index()] = value()\nrequire values[0] == 42 else => $abort(\"replace\")", "value\nindex\n")]
    public void ReplacesAndUpdatesElements(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("DynamicArrayReplace" + name, source, stdout);

    [Fact]
    public void DestroysOldElementBeforeInstallingTheNewOne()
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayReplaceDestruction",
            "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit\n        if self.id == 1 => Console.writeLine(\"old\")\n        else => Console.writeLine(\"new\")\nfunc make() -> Task\n    Console.writeLine(\"value\")\n    return Task.init(2)\nfunc index() -> isize\n    Console.writeLine(\"index\")\n    return 0\nvar tasks: Array<Task> = [Task.init(1)]\ntasks[index()] = make()\nrequire tasks[0].id == 2 else => $abort(\"replace\")\nConsole.writeLine(\"done\")",
            "value\nindex\nold\ndone\nnew\n");

    [Theory]
    [InlineData("Negative", "-1")]
    [InlineData("Length", "1")]
    public void BoundsAbortAfterAcquiringTheValue(string name, string index)
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayReplaceBounds" + name,
            "func value() -> i32\n    Console.writeLine(\"value\")\n    return 42\nvar values: Array<i32> = [1]\nvalues[" + index + "] = value()\nConsole.writeLine(\"after\")",
            "value\n",
            1,
            "Hello.kimi:5:1: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    [Theory]
    [InlineData("let values: Array<i32> = [1]\nvalues[0] = 2")]
    [InlineData("var values: Array<i32> = [1]\nlet borrow = values@ref\nvalues[0] = 2\nlet n = borrow[0]")]
    [InlineData("var values: Array<i32> = [1]\nlet moved = values@move\nvalues[0] = 2")]
    [InlineData("var values: Array<i32> = [1]\nlet handle = values@ref\nvalues[0] += 2\nlet n = handle.length")]
    [InlineData("var values: Array<i32> = [1]\nvalues[(work: do\n    values@uniq.clear()\n    exit to work: 0)] = 2")]
    [InlineData("let name = \"owned\"\nvar names: Array<string> = [\"old\"]\nnames[0] = name")]
    public void RejectsInvalidWrites(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }
}
