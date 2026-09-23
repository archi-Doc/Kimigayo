// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DynamicArraySharedReadTest
{
    private const string Task = "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit => Console.writeLine(\"drop\")\n";

    [Theory]
    [InlineData("Owner", "let item = values[0]")]
    [InlineData("SharedHandle", "let handle = values@ref\nlet item = handle[0]")]
    [InlineData("ExclusiveHandle", "let handle = values@uniq\nlet item = handle[0]")]
    [InlineData("Slice", "let view = values[..]\nlet item = view[0]")]
    public void AConcreteNonCopyStructReadBorrowsItsSlot(string name, string access)
        => ScalarEmissionTest.EmitFixture("DynamicArraySharedRead" + name, Task + "var values: Array<Task> = [Task.init(42)]\n" + access + "\nrequire item.id == 42 else => $abort(\"value\")\nvalues@uniq.clear()\nConsole.writeLine(\"done\")", "drop\ndone\n");

    [Fact]
    public void ASharedElementCanBePassedAndReturnedAsAReference()
        => ScalarEmissionTest.EmitFixture("DynamicArraySharedReadReturn", Task + "func first(values: ref/Array<Task>) -> ref{values}/Task => values[0]\nfunc inspect(value: ref/Task) => require value.id == 42 else => $abort(\"value\")\nlet values: Array<Task> = [Task.init(42)]\ninspect(first(values@ref))\ninspect(values[0])\nConsole.writeLine(\"done\")", "done\ndrop\n");

    [Fact]
    public void ReceiverAndIndexAreEvaluatedOnce()
        => ScalarEmissionTest.EmitFixture("DynamicArraySharedReadOnce", Task + "func make() -> Array<Task>\n    Console.writeLine(\"receiver\")\n    return [Task.init(42)]\nfunc index() -> isize\n    Console.writeLine(\"index\")\n    return 0\nrequire make()[index()].id == 42 else => $abort(\"value\")\nConsole.writeLine(\"done\")", "receiver\nindex\ndrop\ndone\n");

    [Fact]
    public void AChainedWriteStillTargetsTheStoredField()
        => ScalarEmissionTest.EmitFixture("DynamicArraySharedReadFieldWrite", Task.Replace("public let id", "public var id", StringComparison.Ordinal) + "var values: Array<Task> = [Task.init(41)]\nvalues[0].id += 1\nlet item = values[0]\nrequire item.id == 42 else => $abort(\"value\")", "drop\n");

    [Theory]
    [InlineData("SliceField", "values[..][0].id")]
    [InlineData("SharedField", "(values@ref)[0].id")]
    [InlineData("ExclusiveField", "(values@uniq)[0].id")]
    public void ChainedReadsThroughViewsBorrowTheNonCopyElement(string name, string read)
        => ScalarEmissionTest.EmitFixture("DynamicArraySharedRead" + name, Task + "var values: Array<Task> = [Task.init(42)]\nrequire " + read + " == 42 else => $abort(\"value\")", "drop\n");

    [Theory]
    [InlineData("func make() -> Array<Task> => [Task.init(42)]\nlet item = make()[0]\nlet id = item.id")]
    [InlineData("func inspect(item: ref/Task, ignored: ()) => ()\nvar values: Array<Task> = [Task.init(42)]\ninspect(values[0], values@uniq.clear())")]
    public void RejectsExpiredOrInvalidatedSharedReads(string source)
    {
        var c = MinimalEmissionTest.Analyze(Task + source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Bodies.SelectMany(x => x.Issues), x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("values@uniq.clear()")]
    [InlineData("values[0] = Task.init(7)")]
    [InlineData("let moved = values@move")]
    public void ASharedReadRetainsTheArrayLoan(string mutation)
    {
        var c = MinimalEmissionTest.Analyze(Task + "var values: Array<Task> = [Task.init(42)]\nlet item = values[0]\n" + mutation + "\nlet id = item.id");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Bodies.SelectMany(x => x.Issues), x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("let item = values[0]@move")]
    [InlineData("func take(value: Task) => ()\ntake(values[0])")]
    public void AnElementReadNeverTransfersItsNonCopyPayload(string use)
    {
        var c = MinimalEmissionTest.Analyze(Task + "let values: Array<Task> = [Task.init(42)]\n" + use);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
