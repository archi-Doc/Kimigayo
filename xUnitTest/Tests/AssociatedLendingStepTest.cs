// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContinueAndExitReleaseStepLoans(bool generic)
    {
        var next = generic ? "advance(iterator@uniq)" : "iterator.next()";
        var source = Declarations + "var iterator = Counter.init()\nvar count: i32 = 0\nvar total: i32 = 0\nwhile count < 5\n    let item = " + next + "\n    count += 1\n    if item < 3 => continue\n    total += item\n    if item == 4 => exit\nrequire count == 4 else => $abort(\"exit count\")\nrequire total == 7 else => $abort(\"continue total\")\nlet after = " + next + "\nrequire after == 5 else => $abort(\"after exit\")\nConsole.writeLine(\"transfers\")";
        ScalarEmissionTest.EmitFixture("AssociatedOriginLendingTransfers" + (generic ? "Generic" : "Concrete"), source, "transfers\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LendingItemCannotEscapeItsLocalIterator(bool generic)
    {
        var next = generic ? "advance(iterator@uniq)" : "iterator.next()";
        var source = Declarations + "func escape() -> ref/i32 during static\n    var iterator = Counter.init()\n    return " + next + "\nlet item = escape()\nlet observed: i32 = item";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.True(c.Binding.Issues.Count != 0 || c.Ownership.Issues.Count != 0, MinimalEmissionTest.Describe(c, null));
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void RepeatedLendingStepsReuseLoansAndAllocateNothing()
    {
        const string source = Declarations + "var iterator = Counter.init()\nvar count: i32 = 0\nvar total: i32 = 0\nwhile count < 100\n    let item = advance(iterator@uniq)\n    total += item\n    count += 1\nrequire total == 5050 else => $abort(\"lending total\")";
        var c = MinimalEmissionTest.Analyze(source);
        for (var i = 0; i < 32; i++)
        {
            Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Ownership.Analyze().IsVerified || !c.Emission.WriteIr(TextWriter.Null, out _))
            {
                throw new InvalidOperationException("Lending compilation failed.");
            }
        }));
        NativeAllocationAudit.WriteFixture("AssociatedOriginLendingCost", source, 0, 0, 0);
    }
}
