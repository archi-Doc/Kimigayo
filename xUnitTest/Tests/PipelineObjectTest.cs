// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class PipelineObjectTest
{
    private const string Counter = "struct Counter\n    var value: i32 = 0\n    public func add(self: uniq/Self, n: i32) => self.value = self.value + n\n    public func read(self: ref/Self) -> i32 => self.value\n    deinit => Console.writeLine(\"drop\")\n";

    [Theory]
    [InlineData("Exchange", "var owner = Kimi.Intrinsics.makeObj(Counter.init())\nowner.add(7)\ndo\n    let old = Kimi.Intrinsics.exchange(owner@uniq/Counter, with: Counter.init())\n    require old.read() == 7 and owner.read() == 0 else => $abort(\"exchange\")\n    owner.add(old.read())\nrequire owner.read() == 7 else => $abort(\"owner\")\nConsole.writeLine(\"done\")", "drop\ndone\ndrop\n")]
    [InlineData("BorrowCapture", "var owner = Kimi.Intrinsics.makeObj(Counter.init())\ndo\n    let target = owner@uniq/Counter\n    var visit = func [target@move] () => target.add(3)\n    visit@uniq()\n    visit@uniq()\nrequire owner.read() == 6 else => $abort(\"capture\")", "drop\n")]
    [InlineData("ConsumeCapture", "func finish(value: obj/Counter) => require value.read() == 0 else => $abort(\"consume\")\nlet owner = Kimi.Intrinsics.makeObj(Counter.init())\nlet complete = func [owner@move] () => finish(owner@move)\ncomplete@move()\nConsole.writeLine(\"done\")", "drop\ndone\n")]
    [InlineData("UnusedCapture", "let owner = Kimi.Intrinsics.makeObj(Counter.init())\nlet keep = func [owner@move] () => ()\nkeep()\nkeep()\nConsole.writeLine(\"done\")", "done\ndrop\n")]
    [InlineData("TwoOwners", "let first = Kimi.Intrinsics.makeObj(Counter.init())\nlet second = Kimi.Intrinsics.makeObj(Counter.init())\nrequire first.read() == 0 and second.read() == 0 else => $abort(\"owners\")", "drop\ndrop\n")]
    public void EmitsObjectLifetime(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("PipelineObject" + name, Counter + source, stdout);

    [Fact]
    public void ZeroSizedPayloadStillDestroys()
        => ScalarEmissionTest.EmitFixture("PipelineObjectZero", "struct Empty\n    deinit => Console.writeLine(\"empty\")\nlet value = Kimi.Intrinsics.makeObj(Empty.init())", "empty\n");

    [Fact]
    public void DistinctTypesWithIdenticalLayoutsHaveDistinctMetadata()
    {
        var ir = ScalarEmissionTest.EmitFixture("PipelineObjectTypes", "struct First\n    public let n: i32 = 1\nstruct Second\n    public let n: i32 = 2\nlet first = Kimi.Intrinsics.makeObj(First.init())\nlet second = Kimi.Intrinsics.makeObj(Second.init())", string.Empty);
        Assert.Contains("@__kimi_object_metadata0", ir);
        Assert.Contains("@__kimi_object_metadata1", ir);
        Assert.Contains("@__kimi_object_type_key0", ir);
        Assert.Contains("@__kimi_object_type_key1", ir);
    }

    [Theory]
    [InlineData("func take(value: obj/Counter) => ()\nlet owner = Kimi.Intrinsics.makeObj(Counter.init())\ntake(owner@move)\ntake(owner@move)")]
    [InlineData("func take(value: obj/Counter) => ()\nlet owner = Kimi.Intrinsics.makeObj(Counter.init())\ntake(owner)")]
    [InlineData("let owner = Kimi.Intrinsics.makeObj(Counter.init())\nlet target = owner@uniq/Counter")]
    [InlineData("var owner = Kimi.Intrinsics.makeObj(Counter.init())\nlet target = owner@uniq/i32")]
    [InlineData("let value: i32 = 1\nlet owner = Kimi.Intrinsics.makeObj(value@ref)")]
    [InlineData("var owner = Kimi.Intrinsics.makeObj(Counter.init())\nlet target = owner@uniq/Counter\nvar visit = func [target@move] () => target.add(1)\nowner.read()\nvisit@uniq()")]
    [InlineData("var owner = Kimi.Intrinsics.makeObj(Counter.init())\nlet target = owner@uniq/Counter\nlet visit = func [target@move] () => target.add(1)\nvisit@uniq()")]
    [InlineData("var owner = Kimi.Intrinsics.makeObj(Counter.init())\nlet target = owner@uniq/Counter\nvar visit = func [target] () => target.add(1)\nvisit@uniq()")]
    [InlineData("var owner = Kimi.Intrinsics.makeObj(Counter.init())\nlet target = owner@uniq/Counter\nvar visit = func [target@move] () => target.add(1)\nvisit()")]
    [InlineData("func take(value: obj/Counter) => ()\nlet owner = Kimi.Intrinsics.makeObj(Counter.init())\nlet complete = func [owner@move] () => take(owner@move)\ncomplete@move()\ncomplete@move()")]
    [InlineData("func take(value: obj/Counter) => ()\nlet owner = Kimi.Intrinsics.makeObj(Counter.init())\nlet complete = func [owner@move] () => take(owner@move)\ncomplete()")]
    [InlineData("let owner = Kimi.Intrinsics.makeObj(Counter.init())\nlet complete = func [owner@move] () => ()\nowner.read()")]
    [InlineData("let owner = Kimi.Intrinsics.makeObj(Counter.init())\nlet complete = func [owner] () => ()\ncomplete()")]
    public void RejectsInvalidAcquisition(string source)
    {
        var c = MinimalEmissionTest.Analyze(Counter + source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }
}
