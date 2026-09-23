// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DynamicArrayBorrowTest
{
    private const string Task = "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit => Console.writeLine(\"drop\")\n";

    [Theory]
    [InlineData("Owner", "let item = values[0]@ref\nrequire item.id == 42 else => $abort(\"value\")")]
    [InlineData("SharedHandle", "let handle = values@ref\nlet item = handle[0]@ref\nrequire item.id == 42 else => $abort(\"value\")")]
    [InlineData("ExclusiveHandle", "let handle = values@uniq\nlet item = handle[0]@ref\nrequire item.id == 42 else => $abort(\"value\")")]
    [InlineData("Slice", "let view = values[..]\nlet item = view[0]@ref\nrequire item.id == 42 else => $abort(\"value\")")]
    public void BorrowsTheElementWithoutAcquiringOwnership(string name, string access)
        => ScalarEmissionTest.EmitFixture("DynamicArrayBorrow" + name, Task + "var values: Array<Task> = [Task.init(42)]\n" + access + "\nvalues@uniq.clear()\nConsole.writeLine(\"done\")", "drop\ndone\n");

    [Fact]
    public void BorrowsCopyStorageAndReadsItsReferent()
        => ScalarEmissionTest.EmitFixture("DynamicArrayBorrowScalar", "var values: Array<i32> = [42]\nlet item = values[0]@ref\nrequire item == 42 else => $abort(\"value\")\nvalues[0] = 7", string.Empty);

    [Fact]
    public void AnImmediateHandleBorrowCanBeIndexedAndSliced()
        => ScalarEmissionTest.EmitFixture("DynamicArrayBorrowImmediateHandle", "var values: Array<i32> = [20, 22]\nrequire (values@ref)[0] + (values@uniq)[1] == 42 else => $abort(\"value\")\nrequire (values@ref)[..].length == 2 else => $abort(\"view\")", string.Empty);

    [Fact]
    public void ATemporaryReceiverLivesThroughTheCall()
        => ScalarEmissionTest.EmitFixture("DynamicArrayBorrowTemporary", Task + "func make() -> Array<Task> => [Task.init(42)]\nfunc inspect(item: ref/Task) => require item.id == 42 else => $abort(\"value\")\ninspect(make()[0]@ref)\nConsole.writeLine(\"done\")", "drop\ndone\n");

    [Fact]
    public void ReturnedBorrowKeepsTheCallerStorageOrigin()
        => ScalarEmissionTest.EmitFixture("DynamicArrayBorrowReturned", Task + "func first(values: ref/Array<Task>) -> ref{values}/Task => values[0]@ref\nvar values: Array<Task> = [Task.init(42)]\nlet item = first(values@ref)\nrequire item.id == 42 else => $abort(\"value\")\nvalues@uniq.clear()", "drop\n");

    [Theory]
    [InlineData("Negative", "-1")]
    [InlineData("End", "1")]
    [InlineData("Maximum", "9223372036854775807")]
    public void BoundsAreCheckedBeforeFormingTheReference(string name, string index)
        => ScalarEmissionTest.EmitFixture("DynamicArrayBorrowBounds" + name, "let values: Array<i32> = [42]\nlet item = values[" + index + "]@ref", string.Empty, 1, "Hello.kimi:2:12: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    [Theory]
    [InlineData("func make() -> Array<i32> => [1]\nlet item = make()[0]@ref\nlet n: i32 = item")]
    [InlineData("var values: Array<i32> = [1]\nlet item = values[(index: do\n    values@uniq.clear()\n    exit to index: 0\n)]@ref\nlet n: i32 = item")]
    [InlineData("var values: Array<i32> = [1]\nlet handle = values@uniq\nlet item = handle[0]@ref\nhandle@uniq.clear()\nlet n: i32 = item")]
    public void RejectsExpiredAndConflictingDependencies(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Bodies.SelectMany(x => x.Issues), x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("values@uniq.clear()")]
    [InlineData("values@uniq.reserve(0)")]
    [InlineData("values[0] = Task.init(7)")]
    [InlineData("let moved = values@move")]
    public void RejectsInvalidationWhileAnElementBorrowIsLive(string mutation)
    {
        var c = MinimalEmissionTest.Analyze(Task + "var values: Array<Task> = [Task.init(42)]\nlet item = values[0]@ref\n" + mutation + "\nlet id = item.id");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Bodies.SelectMany(x => x.Issues), x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }
}
