// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.6.1, 4.6.4: built-in element selection accepts isize or Index keys and range selection accepts Range
/// or ResolvedRange keys, including inclusive and from-end range syntax; keys resolve against the current length.</summary>
public class KeyedIndexingTest
{
    private const string IndexKeys =
        "let values: [4 of i32] = [10, 20, 30, 40]\nlet last: Index = ^1\n" +
        "require values[last] == 40 and values[Index.init(1)] == 20 and values[^4] == 10 else => $abort(\"fixed\")\n" +
        "let view = values[..]\nrequire view[last] == 40 and view[^2] == 30 and view[Index.init(0)] == 10 else => $abort(\"slice\")\n" +
        "let dynamic: Array<i32> = [1, 2, 3]\nrequire dynamic[^1] == 3 and dynamic[Index.init(1)] == 2 else => $abort(\"array\")\n" +
        "func at(items: ref/[4 of i32], position: Index) -> i32 => items[position]\nrequire at(values, ^2) == 30 else => $abort(\"through reference\")\n" +
        "let text: [2 of string] = [\"First text.\", \"Last text.\"]\nlet words = text[..]\nConsole.writeLine(words[last])\nConsole.writeLine(words[^2])\n" +
        "let borrowed = words[Index.init(0)]@ref\nConsole.writeLine(borrowed)\nConsole.writeLine(\"ok\")";

    private const string RangeKeys =
        "let values: [5 of i32] = [1, 2, 3, 4, 5]\nlet saved: Range = 1..^1\nlet middle = values[saved]\n" +
        "require middle.length == 3 and middle[0] == 2 and middle[^1] == 4 else => $abort(\"saved\")\n" +
        "let inclusive = values[1..=2]\nrequire inclusive.length == 2 and inclusive[0] == 2 and inclusive[1] == 3 else => $abort(\"inclusive\")\n" +
        "let tail = values[^2..]\nrequire tail.length == 2 and tail[0] == 4 else => $abort(\"from-end start\")\n" +
        "let head = values[..^1]\nrequire head.length == 4 and head[3] == 4 else => $abort(\"from-end end\")\n" +
        "let resolved = saved.resolve(values.length)\nlet again = values[resolved]\nrequire again.length == 3 and again[1] == 3 else => $abort(\"resolved\")\n" +
        "let empty = values[^0..]\nrequire empty.isEmpty else => $abort(\"empty\")\n" +
        "let whole: Range = ..\nrequire values[whole].length == 5 and middle[whole].length == 3 else => $abort(\"whole\")\n" +
        "let nested = middle[1..]\nrequire nested.length == 2 and nested[0] == 3 else => $abort(\"nested\")\n" +
        "let direct = ResolvedRange.init(start: 1, end: 3)\nlet applied = middle[direct]\nrequire applied.length == 2 and applied[0] == 3 else => $abort(\"direct\")\n" +
        "Console.writeLine(\"ok\")";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "IndexKeys", IndexKeys, "Last text.\nFirst text.\nFirst text.\nok\n" },
        { "RangeKeys", RangeKeys, "ok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("KeyedIndexing" + name, source, stdout);

    [Theory]
    [InlineData("let values: [3 of i32] = [1, 2, 3]\nlet i: i32 = 1\nlet v = values[i]", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("func make() -> [3 of i32] => [1, 2, 3]\nlet last: Index = ^1\nlet v = make()[last]", DiagnosticCode.UnsupportedBinding_Kd)]
    public void RejectsAtBinding(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }
}
