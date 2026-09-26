// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Linq;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 13.4, 4.6.9: a string comparison reads each operand in place. An operand selected by a dynamic key, below
/// an element or through a reference is shared-borrowed, like a string argument or a receiver below a shared element, and a
/// Copy element below a shared element is read as its root.</summary>
public class ElementComparisonTest
{
    private const string Task =
        "struct Task\n    public let id: i32\n    public let name: string\n    public init(id: i32, name: string)\n        self.id = id\n        self.name = name@move\n";

    private const string ElementsSource =
        "var names: Array<string> = [\"a\", \"b\"]\nlet i: isize = 1\nrequire names[0] == \"a\" and \"b\" == names[i] and names[..][1] == \"b\" else => $abort(\"array\")\n" +
        "let view = names[..]\nrequire view[1] != \"a\" and view[0] < view[1] else => $abort(\"slice\")\n" +
        "func first(v: ref/Array<string>) -> bool => v[0] == \"a\"\nfunc second(r: ref/[2 of string]) -> bool => r[1] == \"y\"\n" +
        "let fixed: [2 of string] = [\"x\", \"y\"]\nrequire first(names) and second(fixed) else => $abort(\"reference\")\nConsole.writeLine(\"ok\")";

    private const string FieldsSource =
        Task + "let tasks: Array<Task> = [Task.init(1, \"one\"), Task.init(2, \"two\")]\nfor t in tasks\n    if t.name == \"two\" => Console.writeLine(\"found\")\n" +
        "let view = tasks[..]\nrequire view[0].name == \"one\" and tasks[1].name == \"two\" else => $abort(\"field\")\n" +
        "let pairs: Array<(i32, string)> = [(1, \"x\"), (2, \"y\")]\nlet items = pairs[..]\nrequire items[1].1 == \"y\" and pairs[0].1 == \"x\" else => $abort(\"tuple\")\n" +
        "let words: Array<[2 of string]> = [[\"a\", \"b\"]]\nlet rows = words[..]\nrequire rows[0][1] == \"b\" and words[0][0] == \"a\" else => $abort(\"nested\")\nConsole.writeLine(\"ok\")";

    private const string NestedCopySource =
        "let grid: [2 of [2 of i32]] = [[1, 2], [3, 4]]\nlet rows = grid[..]\nrequire rows[1][0] == 3 else => $abort(\"slice\")\n" +
        "let dynamic: Array<[2 of i32]> = [[5, 6]]\nrequire dynamic[0][1] == 6 else => $abort(\"array\")\n" +
        "func pick(r: ref/[2 of [2 of i32]]) -> i32 => r[1][1]\nrequire pick(grid) == 4 else => $abort(\"reference\")\nConsole.writeLine(\"ok\")";

    private const string BelowSharedElementsSource =
        "let words: Array<[2 of string]> = [[\"a\", \"b\"]]\nlet rows = words[..]\nConsole.writeLine(rows[0][1])\nlet kept = rows[0][0]@ref\nConsole.writeLine(kept)\n" +
        "let pairs: Array<[2 of (i32, string)]> = [[(1, \"x\"), (2, \"y\")]]\nlet grid = pairs[..]\nrequire grid[0][1].0 == 2 else => $abort(\"leaf\")\nConsole.writeLine(grid[0][0].1)\n" +
        "let items: Array<([2 of i32], string)> = [([1, 2], \"a\")]\nlet v = items[..]\nrequire v[0].0[1] == 2 else => $abort(\"part\")";

    private const string DictionarySource =
        "var d: Dictionary<i32, string> = [:]\n_ = d.tryInsert(1, \"one\")\nrequire d[1] == \"one\" and \"one\" == d[1] else => $abort(\"dictionary\")\nConsole.writeLine(d[1])";

    [Theory]
    [InlineData("Elements", ElementsSource, "ok\n")]
    [InlineData("Dictionary", DictionarySource, "one\n")]
    [InlineData("BelowSharedElements", BelowSharedElementsSource, "b\na\nx\n")]
    [InlineData("Fields", FieldsSource, "found\nok\n")]
    [InlineData("NestedCopy", NestedCopySource, "ok\n")]
    public void OperandsAreReadInPlace(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("ElementComparison" + name, source, stdout);

    [Theory]
    [InlineData("let names: Array<string> = [\"a\"]\nlet same = names[0] == \"a\"", "names[0]")]
    [InlineData("let names: Array<string> = [\"a\"]\nlet view = names[..]\nlet same = \"a\" == view[0]", "view[0]")]
    [InlineData(Task + "func named(t: ref/Task) -> bool => t.name == \"a\"", "t.name")]
    public void AnOperandWithoutAnOwnedPathIsSharedBorrowed(string source, string operand)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var node = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<BinaryKoto>().Single(x => x.ToString() == operand);
        Assert.True(c.Binding.TryGetAdaptation(node, out var adaptation));
        Assert.Equal(ExpectedAdaptationKind.SharedBorrow, adaptation.Kind);
        Assert.True(ReferenceTypes.IsString(adaptation.Type));
        Assert.Equal(BoundType.String, node.BoundType);
    }

    [Fact]
    public void AnOwnedStaticPathIsInspectedWithoutAnAdaptation()
    {
        var c = MinimalEmissionTest.Analyze("let pair: (i32, string) = (1, \"a\")\nlet same = pair.1 == \"a\"");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var node = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<MemberAccessKoto>().Single();
        Assert.False(c.Binding.TryGetAdaptation(node, out _));
        Assert.True(c.Ownership.Result.IsVerified);
    }
}
