// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.3, 4.5 and 4.6.1: the compiler-managed Array Type binds; its operations are not generated yet (PLAN P29).</summary>
public class ArrayBindingTest
{
    private const string Task = "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit => ()\n";

    private static readonly string TaskOperations = Task.Replace("deinit => ()", "deinit\n        match self.id\n            1 => Console.writeLine(\"Task 1 destroyed.\")\n            2 => Console.writeLine(\"Task 2 destroyed.\")\n            3 => Console.writeLine(\"Task 3 destroyed.\")\n            _ => Console.writeLine(\"Task 4 destroyed.\")", StringComparison.Ordinal) +
        "var tasks: Array<Task> = []\ntasks@uniq.reserve(additional: 2)\ntasks@uniq.append(Task.init(1))\ntasks@uniq.append(Task.init(3))\ntasks@uniq.insert(1, Task.init(2))\ntasks@uniq.append(Task.init(4))\nrequire tasks.length == 4 and tasks.capacity >= 4 else => $abort(\"growth\")\nlet last = tasks@uniq.remove(3)\nrequire last.id == 4 and tasks.length == 3 else => $abort(\"remove\")\nConsole.writeLine(\"Removed the last task.\")\nmatch tasks@uniq.pop()\n    .Some(let popped)\n        require popped.id == 3 else => $abort(\"pop\")\n        Console.writeLine(\"Popped a task.\")\n    .None => $abort(\"empty\")\ntasks@uniq.shrinkToFit()\nrequire tasks.capacity == 2 else => $abort(\"shrink\")\nConsole.writeLine(\"Clearing.\")\ntasks@uniq.clear()\nrequire tasks.length == 0 and tasks.capacity == 2 else => $abort(\"clear\")\ntasks@uniq.append(Task.init(2))\nConsole.writeLine(\"Done.\")";

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
        var free = ir.IndexOf("define internal void @__kimi_array_free", StringComparison.Ordinal);
        Assert.DoesNotContain("@HeapAlloc(", ir[free..ir.IndexOf("\n}\n", free, StringComparison.Ordinal)]);
    }

    // SPEC 4.7.2, 4.7.4: the mutation operations are catalog compiler functions of the Array struct with an exclusive receiver.
    [Theory]
    [InlineData("values@uniq.reserve(additional: 4)")]
    [InlineData("values@uniq.append(1)")]
    [InlineData("values@uniq.insert(0, 2)")]
    [InlineData("let removed: i32 = values@uniq.remove(0)")]
    [InlineData("match values@uniq.pop()\n    .Some(let last) => require last == 1 else => $abort(\"pop\")\n    .None => $abort(\"empty\")")]
    [InlineData("values@uniq.clear()")]
    [InlineData("values@uniq.shrinkToFit()")]
    public void MutationOperationsBindThroughAnExclusiveReceiver(string statement)
    {
        var c = MinimalEmissionTest.Analyze("var values: Array<i32> = []\n" + statement);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        using var writer = new StringWriter();
        Assert.True(c.Emission.WriteIr(writer, out var error), error);
    }

    // SPEC 4.7.2, 4.7.4: growth, insertion, removal, pop, shrinking and clearing over scalar elements.
    [Fact]
    public void MutationOperationsRunNativelyOverScalars()
        => ScalarEmissionTest.EmitFixture(
            "ArrayOperationsI32",
            "var values: Array<i32> = []\nvalues@uniq.reserve(additional: 4)\nrequire values.length == 0 and values.capacity >= 4 else => $abort(\"reserve\")\nvalues@uniq.append(1)\nvalues@uniq.insert(0, 2)\nvalues@uniq.append(3)\nlet removed: i32 = values@uniq.remove(0)\nmatch values@uniq.pop()\n    .Some(let last) => require last == 3 else => $abort(\"pop\")\n    .None => $abort(\"empty\")\nvalues@uniq.shrinkToFit()\nrequire removed == 2 and values.length == 1 and values.capacity == 1 else => $abort(\"ops\")\nvalues@uniq.clear()\nmatch values@uniq.pop()\n    .Some(_) => $abort(\"cleared\")\n    .None => ()\nvar flags: Array<bool> = []\nflags@uniq.append(true)\nflags@uniq.append(false)\nrequire flags@uniq.remove(0) and flags.length == 1 else => $abort(\"bool\")\nif flags@uniq.remove(0) => $abort(\"bool\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    // SPEC 4.7.6: removed and popped elements transfer out; clear and destruction destroy the remaining elements in reverse index order.
    [Fact]
    public void MutationOperationsDestroyElementsInReverseOrder()
        => ScalarEmissionTest.EmitFixture(
            "ArrayOperationsTasks",
            TaskOperations,
            "Removed the last task.\nPopped a task.\nTask 3 destroyed.\nClearing.\nTask 2 destroyed.\nTask 1 destroyed.\nDone.\nTask 4 destroyed.\nTask 2 destroyed.\n");

    [Fact]
    public void MutationOperationsMoveStringElements()
        => ScalarEmissionTest.EmitFixture(
            "ArrayOperationsStrings",
            "var names: Array<string> = []\nnames@uniq.append(\"alpha\")\nnames@uniq.append(\"gamma\")\nnames@uniq.insert(1, \"beta\")\nlet second = names@uniq.remove(1)\nConsole.writeLine(second)\nmatch names@uniq.pop()\n    .Some(let last) => Console.writeLine(last)\n    .None => $abort(\"empty\")\nrequire names.length == 1 else => $abort(\"length\")\nnames@uniq.clear()\nnames@uniq.append(\"delta\")\nConsole.writeLine(\"ok\")",
            "beta\ngamma\nok\n");

    // SPEC 4.7.1, 4.7.4: invalid positions and a negative reserve amount Abort before the collection changes.
    [Theory]
    [InlineData("InsertBounds", "var values: Array<i32> = []\nvalues@uniq.append(1)\nConsole.writeLine(\"before\")\nvalues@uniq.insert(2, 5)\nConsole.writeLine(\"after\")", "before\n", "Hello.kimi:4:1: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n")]
    [InlineData("RemoveEmpty", "var values: Array<i32> = []\nlet removed = values@uniq.remove(0)\nConsole.writeLine(\"after\")", "", "Hello.kimi:2:15: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n")]
    [InlineData("NegativeReserve", "var values: Array<i32> = []\nlet n: isize = -1\nvalues@uniq.reserve(n)\nConsole.writeLine(\"after\")", "", "Hello.kimi:3:1: abort KIMI_E_ARGUMENT: Invalid argument value\n")]
    public void MutationOperationsAbortOnInvalidPositionsAndAmounts(string name, string source, string stdout, string stderr)
        => ScalarEmissionTest.EmitFixture("ArrayOperations" + name, source, stdout, 1, stderr);

    [Fact]
    public void MutationOperationsAreValidatedCatalogDeclarations()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        foreach (var id in new[] { KimiDeclarationId.ArrayReserve, KimiDeclarationId.ArrayAppend, KimiDeclarationId.ArrayInsert, KimiDeclarationId.ArrayPop, KimiDeclarationId.ArrayRemove, KimiDeclarationId.ArrayClear, KimiDeclarationId.ArrayShrinkToFit })
        {
            Assert.Equal(KimiDeclarationState.Validated, c.Library.GetDeclarationState(id));
            var symbol = c.Library.GetSymbol(id)!;
            Assert.NotEqual(CompilerFunctionKind.None, symbol.CompilerFunction);
            Assert.Same(c.Library.DynamicArray.Declaration, symbol.Declaration.Parent);
        }
    }

    [Theory]
    [InlineData("values.append(1)", DiagnosticCode.ExclusiveBorrowRequired_Kd)]
    [InlineData("values@ref.append(1)", DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData("values@uniq.append(true)", DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData("values@uniq.reserve(4, 5)", DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData("let index: i32 = 0\nvalues@uniq.insert(index, 1)", DiagnosticCode.NoApplicableOverload_Kd)]
    public void MutationOperationsRejectWrongReceiversAndArguments(string statement, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze("var values: Array<i32> = []\n" + statement);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
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
