// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class SiblingMoveBorrowTest
{
    private const string Pair = "struct Counter\n    public var value: i32 = 1\nstruct Pair\n    public var left: Counter = Counter.init()\n    public var right: Counter = Counter.init()\n";

    [Theory]
    [InlineData("Shared", "var pair = Pair.init()\nlet a = pair.left@ref\nlet moved = pair.right\nrequire a.value + moved.value == 2 else => $abort(\"value\")")]
    [InlineData("Exclusive", "var pair = Pair.init()\nlet a = pair.left@uniq\nlet moved = pair.right\na.value += moved.value\nrequire pair.left.value == 2 else => $abort(\"value\")")]
    [InlineData("MoveFirst", "var pair = Pair.init()\nlet moved = pair.right\nlet a = pair.left@uniq\na.value += moved.value\nrequire pair.left.value == 2 else => $abort(\"value\")")]
    [InlineData("Tuple", "var pair = (Counter.init(), Counter.init())\nlet a = pair.0@uniq\nlet moved = pair.1\na.value += moved.value\nrequire pair.0.value == 2 else => $abort(\"value\")")]
    [InlineData("Nested", "var pair = (Counter.init(), (Counter.init(), Counter.init()))\nlet a = pair.1.0@uniq\nlet moved = pair.1.1\na.value += moved.value\nrequire pair.1.0.value == 2 else => $abort(\"value\")")]
    [InlineData("Reborrow", "var pair = Pair.init()\nlet a = pair.left@uniq\nlet b = a@ref\nlet moved = pair.right\nrequire b.value + moved.value == 2 else => $abort(\"value\")")]
    [InlineData("Conditional", "func check(flag: bool)\n    var pair = Pair.init()\n    let a = pair.left@uniq\n    if flag\n        let moved = pair.right\n    a.value += 1\n    require pair.left.value == 2 else => $abort(\"value\")\ncheck(true)\ncheck(false)")]
    [InlineData("Unreachable", "func check()\n    return\n    var pair = Pair.init()\n    let a = pair.left@ref\n    let moved = pair.right\n    let value = a.value\ncheck()")]
    [InlineData("Repair", "var pair = Pair.init()\nlet moved = pair.right\nlet a = pair.left@uniq\npair.right = Counter.init()\na.value += moved.value\nlet whole = pair\nrequire whole.left.value == 2 and whole.right.value == 1 else => $abort(\"value\")")]
    public void KeepsDisjointBorrowAliveAcrossMove(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(Pair + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, string.Join('\n', c.Ownership.Issues));
        ScalarEmissionTest.EmitFixture("SiblingMoveBorrow" + name, Pair + source, string.Empty);
    }

    [Theory]
    [InlineData("var pair = Pair.init()\nlet a = pair.left@ref\nlet moved = pair.left\nlet value = a.value")]
    [InlineData("var pair = Pair.init()\nlet a = pair.left@uniq\nlet moved = pair\na.value += 1")]
    [InlineData("var pair = Pair.init()\nlet a = pair@ref\nlet moved = pair.right\nlet value = a.left.value")]
    [InlineData("var pair = Pair.init()\nlet moved = pair.left\nlet a = pair.left@ref\nlet value = a.value")]
    [InlineData("var pair = (Counter.init(), (Counter.init(), Counter.init()))\nlet moved = pair.1.1\nlet a = pair.1@ref\nlet value = a.0.value")]
    [InlineData("var pair = Pair.init()\nlet a = pair.left@ref\nlet moved = pair.right\npair = Pair.init()\nlet value = a.value")]
    [InlineData("func check(flag: bool)\n    var pair = Pair.init()\n    if flag\n        let moved = pair.left\n    let a = pair.left@ref\n    let value = a.value\ncheck(false)")]
    public void RejectsMovedOrOverlappingBorrowedStorage(string source)
    {
        var c = MinimalEmissionTest.Analyze(Pair + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure is OwnershipFailure.ComparisonLoanConflict or OwnershipFailure.PossiblyMovedUse);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void RemainingAndMovedValuesAreDestroyedOnce()
    {
        const string Source = "struct Counter\n    public var value: i32 = 1\n    deinit => Console.writeLine(\"drop\")\nvar pair = (Counter.init(), Counter.init())\nlet a = pair.0@uniq\nlet moved = pair.1\na.value += moved.value\nrequire pair.0.value == 2 else => $abort(\"value\")";
        ScalarEmissionTest.EmitFixture("SiblingMoveBorrowCleanup", Source, "drop\ndrop\n");
    }

    [Fact]
    public void EmissionRechecksTheBorrowedSubtree()
    {
        var c = MinimalEmissionTest.Analyze(Pair + "var pair = Pair.init()\nlet moved = pair.right\nlet a = pair.left@ref\nlet value = a.value");
        Assert.True(c.Emission.Validate(out var failure), failure);
        var body = Assert.Single(c.Ownership.Bodies, x => x.Function.IsGenerated);
        var borrow = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Borrow);
        var moved = Assert.Single(body.Operations, x => x.Kind == OwnershipOperationKind.Produce && x.Projection >= 0).Source;
        body.OperationStorage[borrow] = body.Operations[borrow] with { Source = moved };
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }
}
