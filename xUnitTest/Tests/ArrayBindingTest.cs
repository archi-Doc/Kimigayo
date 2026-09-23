// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.3, 4.5 and 4.6.1: the compiler-managed Array Type binds; its operations are not generated yet (PLAN P29).</summary>
public class ArrayBindingTest
{
    private const string Task = "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit => ()\n";

    // SPEC 4.6.1: a borrowed handle shares access for the metadata operation and is read through the reference.
    [Fact]
    public void BorrowedHandlesReadMetadataThroughTheReference()
        => ScalarEmissionTest.EmitFixture(
            "ArrayBorrowedHandle",
            Task + "func describe(tasks: ref/Array<Task>) -> isize => tasks.length\nfunc room(tasks: ref/Array<Task>) -> isize => tasks.capacity\nfunc count(tasks: uniq/Array<Task>) -> isize => tasks.indices.length\nvar tasks: Array<Task> = []\nrequire describe(tasks@ref) == 0 and room(tasks@ref) == 0 and count(tasks@uniq) == 0 and describe(tasks@ref) == 0 else => $abort(\"borrowed\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    // SPEC 4.5: a handle transfers as a value; the callee releases an owned parameter and a result is secured by its caller.
    [Fact]
    public void HandlesTransferAsParametersAndResults()
        => ScalarEmissionTest.EmitFixture(
            "ArrayValues",
            "func make() -> Array<i32> => []\nfunc take(values: Array<i32>) -> isize => values.length\nlet a = make()\nrequire a.length == 0 else => $abort(\"make\")\nlet b: Array<i32> = []\nrequire take(b@move) == 0 and take(make()) == 0 else => $abort(\"take\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    // An aggregate holding an Array would have to release the buffer during its own destruction (PLAN P29).
    [Theory]
    [InlineData("struct Bag\n    var items: Array<i32> = []\nlet bag = Bag.init()")]
    [InlineData("let pair: (Array<i32>, i32) = ([], 1)")]
    public void AggregatesHoldingHandlesAreNotGeneratedYet(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.True(c.Ownership.Result.UnsupportedCount > 0, MinimalEmissionTest.Describe(c, null));
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    // SPEC 4.5: Array is Non-Copy and its Owned classification follows the element Type.
    [Theory]
    [InlineData("i32", "n", true)]
    [InlineData("ref/i32", "n@ref", false)]
    public void OwnedFollowsTheElementType(string element, string argument, bool owned)
    {
        const string Keep = "func keep<T>(value: T) -> isize\n    T is Owned\n    return 1\nlet n: i32 = 1\n";
        var plain = MinimalEmissionTest.Analyze(Keep + "let result = keep<" + element + ">(" + argument + ")");
        var array = MinimalEmissionTest.Analyze(Keep + "let values: Array<" + element + "> = []\nlet result = keep<Array<" + element + ">>(values@move)");
        Assert.Equal(owned, plain.Binding.Result.IsComplete);
        Assert.Equal(owned, array.Binding.Result.IsComplete);
        Assert.Equal(plain.Binding.Issues.Select(x => x.Code).Order(), array.Binding.Issues.Select(x => x.Code).Order());
    }

    [Theory]
    [InlineData("let tasks: Array<Task> = [Task.init(1), Task.init(2)]\nlet room: isize = tasks.capacity\nlet all = tasks.indices")]
    [InlineData("let numbers: Array<i32> = [1, 2, 3]\nlet n: isize = numbers.length")]
    public void ArrayTypesLiteralsAndMetadataBind(string source)
    {
        var c = MinimalEmissionTest.Analyze(Task + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified); // Array storage is not analyzed or generated yet.
        Assert.True(c.Ownership.Result.UnsupportedCount > 0, MinimalEmissionTest.Describe(c, null));
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    // SPEC 4.7.4: the typed empty literal is a zeroed handle that allocates nothing; destruction releases the buffer.
    [Fact]
    public void EmptyHandleLifecycleRunsNatively()
        => ScalarEmissionTest.EmitFixture(
            "ArrayEmptyHandle",
            "var values: Array<i32> = []\nlet empty: Array<bool> = []\nrequire values.length == 0 and values.capacity == 0 and empty.indices.length == 0 else => $abort(\"empty\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    [Fact]
    public void EmptyHandleIsZeroedAndReleasedThroughTheRuntime()
    {
        var c = MinimalEmissionTest.Analyze("var values: Array<i32> = []\nrequire values.length == 0 else => $abort(\"empty\")");
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        using var writer = new StringWriter();
        Assert.True(c.Emission.WriteIr(writer, out var error), error);
        var ir = writer.ToString();
        Assert.Contains("call void @__kimi_array_init(ptr %", ir);
        Assert.Contains("call void @__kimi_array_free(ptr %", ir);
        Assert.DoesNotContain("@HeapAlloc(", ir[ir.IndexOf("define internal void @__kimi_array_free", StringComparison.Ordinal)..]);
    }

    [Theory]
    [InlineData("func take(values: Array<i32>) => ()\nlet values: Array<i32> = []\ntake(values)", DiagnosticCode.TransferRequired_Kd)]
    [InlineData("let values: Array<i32> = []\nlet empty = values.isEmpty", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("let values: Array<i32> = [1, true]", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let values: Array<i32, bool> = []", DiagnosticCode.TypeMismatch_Kd)]
    public void ArrayIsNonCopyAndCheckedLikeOtherSequences(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }
}
