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
