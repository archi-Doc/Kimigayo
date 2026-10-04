// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class ConsumingCallableCleanupTest
{
    private const string Prefix = "struct Item\n    public var value: i32 = 7\n    public func next(self: uniq/Self) -> i32\n        self.value += 1\n        return self.value\n    drop => Console.writeLine(\"drop\")\n" +
        "func consume(item: Item) -> i32 => item.value\nfunc invoke<F>(action: F) -> i32\n    F is Callable<owner, () -> i32>\n    return action@move()\nvar item = Item.init()\n";

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Shared", "let action = func [item@move] () -> i32 => item.value", 7, 0, 0)]
    [InlineData("Exclusive", "let action = func [var item@move] () -> i32 => item.next()", 8, 0, 0)]
    [InlineData("Consuming", "let action = func [item@move] () -> i32 => consume(item@move)", 7, 0, 0)]
    [InlineData("Common", "let action: () -> i32 = func [item@move] () -> i32 => item.value", 7, 1, 4)]
    public void OwningCallDestroysTheTransferredEnvironmentExactlyOnce(string name, string creation, int result, int allocations, long bytes)
    {
        var source = Prefix + creation + $"\nrequire invoke(action@move) == {result} else => $abort(\"value\")\nConsole.writeLine(\"done\")";
        NativeAllocationAudit.WriteFixture("ConsumingCallable" + name, source, allocations, allocations, bytes, "drop\ndone\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmConsumingAdapterEmissionAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "let action = func [item@move] () -> i32 => item.value\nlet result = invoke(action@move)");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AnAbandonedArgumentKeepsTheReceiversCleanupResponsibility()
    {
        const string Source = Prefix + "func abandon<F>(action: F) -> i32\n    F is Callable<owner, (i32) -> i32>\n    return action@move((return 42))\n" +
            "let action = func [item@move] (x: i32) -> i32 => x + item.value\nrequire abandon(action@move) == 42 else => $abort(\"early return\")\nConsole.writeLine(\"done\")";
        NativeAllocationAudit.WriteFixture("ConsumingCallableAbandoned", Source, 0, 0, 0, "drop\ndone\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ExternalLoansRemainAvailableToTheDestructionObservation()
    {
        const string Source = "struct View {source}\n    public let value: ref/i32 during source\n    public init(value: ref/i32 during source) => self.value = value\n    drop => Console.writeLine(\"drop \\(self.value)\")\n" +
            "func invoke<F>(action: F) -> i32\n    F is Callable<owner, () -> i32>\n    return action@move()\nvar number = 7\nlet view = View.init(number@ref)\nlet action = func [view@move] () -> i32 => view.value\n" +
            "require invoke(action@move) == 7 else => $abort(\"value\")\nnumber = 9\nConsole.writeLine(\"done\")";
        NativeAllocationAudit.WriteFixture("ConsumingCallableExternalLoan", Source, 0, 0, 0, "drop 7\ndone\n");
    }
}
