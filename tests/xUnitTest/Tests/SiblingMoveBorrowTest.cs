// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class SiblingMoveBorrowTest
{
    private const string Pair = "struct Counter\n    public var value: i32 = 1\nstruct Pair\n    public var left: Counter = Counter.init()\n    public var right: Counter = Counter.init()\n";

    [Theory]
    [InlineData("Shared", "var pair = Pair.init()\nlet a = pair.left@ref\nlet moved = pair.right@move\nrequire a.value + moved.value == 2 else => $abort(\"value\")")]
    [InlineData("Exclusive", "var pair = Pair.init()\nlet a = pair.left@uniq\nlet moved = pair.right@move\na.value += moved.value\nrequire pair.left.value == 2 else => $abort(\"value\")")]
    [InlineData("MoveFirst", "var pair = Pair.init()\nlet moved = pair.right@move\nlet a = pair.left@uniq\na.value += moved.value\nrequire pair.left.value == 2 else => $abort(\"value\")")]
    [InlineData("Tuple", "var pair = (Counter.init(), Counter.init())\nlet a = pair.0@uniq\nlet moved = pair.1@move\na.value += moved.value\nrequire pair.0.value == 2 else => $abort(\"value\")")]
    [InlineData("Nested", "var pair = (Counter.init(), (Counter.init(), Counter.init()))\nlet a = pair.1.0@uniq\nlet moved = pair.1.1@move\na.value += moved.value\nrequire pair.1.0.value == 2 else => $abort(\"value\")")]
    [InlineData("Reborrow", "var pair = Pair.init()\nlet a = pair.left@uniq\nlet b = a@follow@ref\nlet moved = pair.right@move\nrequire b.value + moved.value == 2 else => $abort(\"value\")")]
    [InlineData("Conditional", "func check(flag: bool)\n    var pair = Pair.init()\n    let a = pair.left@uniq\n    if flag\n        let moved = pair.right@move\n    a.value += 1\n    require pair.left.value == 2 else => $abort(\"value\")\ncheck(true)\ncheck(false)")]
    [InlineData("Repair", "var pair = Pair.init()\nlet moved = pair.right@move\nlet a = pair.left@uniq\npair.right = Counter.init()\na.value += moved.value\nlet whole = pair@move\nrequire whole.left.value == 2 and whole.right.value == 1 else => $abort(\"value\")")]
    public void KeepsDisjointBorrowAliveAcrossMove(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(Pair + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Analysis.IsVerified, string.Join('\n', c.Problems));
        ScalarEmissionTest.EmitFixture("SiblingMoveBorrow" + name, Pair + source, string.Empty);
    }

    [Theory]
    [InlineData("var pair = Pair.init()\nlet a = pair.left@ref\nlet moved = pair.left@move\nlet value = a.value")]
    [InlineData("var pair = Pair.init()\nlet a = pair.left@uniq\nlet moved = pair@move\na.value += 1")]
    [InlineData("var pair = Pair.init()\nlet a = pair@ref\nlet moved = pair.right@move\nlet value = a.left.value")]
    [InlineData("var pair = Pair.init()\nlet moved = pair.left@move\nlet a = pair.left@ref\nlet value = a.value")]
    [InlineData("var pair = (Counter.init(), (Counter.init(), Counter.init()))\nlet moved = pair.1.1@move\nlet a = pair.1@ref\nlet value = a.0.value")]
    [InlineData("var pair = Pair.init()\nlet a = pair.left@ref\nlet moved = pair.right@move\npair = Pair.init()\nlet value = a.value")]
    [InlineData("func check(flag: bool)\n    var pair = Pair.init()\n    if flag\n        let moved = pair.left@move\n    let a = pair.left@ref\n    let value = a.value\ncheck(false)")]
    public void RejectsMovedOrOverlappingBorrowedStorage(string source)
    {
        var c = MinimalEmissionTest.Analyze(Pair + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict() || x.Code is DiagnosticCode.MovedPlace_Kd);
        Assert.False(c.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void RemainingAndMovedValuesAreDestroyedOnce()
    {
        const string Source = "struct Counter\n    public var value: i32 = 1\n    drop => Console.writeLine(\"drop\")\nvar pair = (Counter.init(), Counter.init())\nlet a = pair.0@uniq\nlet moved = pair.1@move\na.value += moved.value\nrequire pair.0.value == 2 else => $abort(\"value\")";
        ScalarEmissionTest.EmitFixture("SiblingMoveBorrowCleanup", Source, "drop\ndrop\n");
    }
}
