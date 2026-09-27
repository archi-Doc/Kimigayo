// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class AssociatedLendingStepTest
{
    private const string Declarations = "contract Lending\n    associate Item(step) is ref/i32 during step\n    func next(self: uniq/Self during step) -> Self.Item(step)\nstruct Counter\n    Self is Lending\n    var value: i32 = 0\n    public init() => ()\n    public func next(self: uniq/Self during step) -> ref/i32 during step\n        self.value += 1\n        return self.value@ref\nfunc advance<T>(iterator: uniq/T during step) -> T.(Lending).Item(step)\n    T is Lending\n    return iterator.next()\n";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LendingStepReleasesAfterLastItemUse(bool generic)
    {
        var next = generic ? "advance(iterator@uniq)" : "iterator.next()";
        var source = Declarations + "var iterator = Counter.init()\nlet first = " + next + "\nrequire first == 1 else => $abort(\"first\")\nlet second = " + next + "\nrequire second == 2 else => $abort(\"second\")\nConsole.writeLine(\"lending\")";
        ScalarEmissionTest.EmitFixture("AssociatedOriginLending" + (generic ? "Generic" : "Concrete"), source, "lending\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveItemBlocksNextStep(bool generic)
    {
        var next = generic ? "advance(iterator@uniq)" : "iterator.next()";
        var source = Declarations + "var iterator = Counter.init()\nlet first = " + next + "\nlet second = " + next + "\nlet observed: i32 = first\nlet another: i32 = second";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.NotEmpty(c.Ownership.Issues);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }
}
