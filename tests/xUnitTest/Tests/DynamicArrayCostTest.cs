// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DynamicArrayCostTest
{
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(1024)]
    public void SliceCopiesAllocateAtMostOnceWithoutGrowthTransfers(int count)
        => WriteCostFixture("SliceCopy" + count, "let values: [" + count + " of i32] = [" + count + " of 7]\nlet copy = values.slice().toArray()\nrequire copy.length == " + count + " else => $abort(\"length\")", count == 0 ? 0 : 1, 0);

    // SPEC 4.9.5: repeating construction reserves once and places every Copy within that capacity; zero-sized elements allocate nothing.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(1024)]
    public void RepeatingConstructionAllocatesAtMostOnceWithoutGrowthTransfers(int count)
        => WriteCostFixture("Repeating" + count, "let values = Array<i32>.init(repeating: 7, count: " + count + ")\nrequire values.length == " + count + " else => $abort(\"length\")", count == 0 ? 0 : 1, 0);

    // SPEC 4.9.5, 21.4.3: the one reservation that fails Aborts at the construction, which the implementation's caller location names.
    [Fact]
    public void RepeatingAllocationFailureReportsTheConstruction()
    {
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze("let values = Array<i32>.init(repeating: 1, count: 2)"));
        Assert.Contains("call ptr @HeapAlloc(", ir, StringComparison.Ordinal);
        ir = ir.Replace("call ptr @HeapAlloc(", "call ptr @fail_allocate(", StringComparison.Ordinal);
        ir += "\ndefine internal ptr @fail_allocate(ptr %heap, i32 %flags, i64 %size) { ret ptr null }\n";
        ScalarEmissionTest.WriteFixture("DynamicArrayCostRepeatingFailure", ir, string.Empty, 1, "Hello.kimi:1:14: abort KIMI_E_ALLOC: Failed to allocate memory\n");
    }

    // The implementation is prepared only for a program that constructs one; its own loop places the Copies within the one
    // reservation (DynamicArrayCostRepeating* counts it).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RepeatingConstructionCompilesOnlyItsImplementationLoop(bool used)
    {
        var source = used ? "let values = Array<i32>.init(repeating: 1, count: 3)\nrequire values[2] == 1 else => $abort(\"value\")"
            : "let values = Array<i32>.init(capacity: 3)\nrequire values.length == 0 else => $abort(\"value\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Validate(out var failure), MinimalEmissionTest.Describe(c, failure));
        var (sel, generated) = FunctionCodegenTest.GeneratedDefinitions(source);
        Assert.Equal(used, generated.Contains(sel.Library.GetSymbol(KimiDeclarationId.ArrayRepeatingImplementation)!.Declaration));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ZeroSizedRepeatingConstructionAllocatesNothing()
        => WriteCostFixture("RepeatingZeroSized", "let values = Array<()>.init(repeating: (), count: 1024)\nrequire values.length == 1024 else => $abort(\"length\")", 0, 0);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AppendingCopiesRetainsPreallocatedStorage()
        => WriteCostFixture("AppendCopiesReserved", "let values: [3 of i32] = [1, 2, 3]\nvar copied = Array<i32>.init(capacity: 32)\nvar i = 0\nwhile i < 8\n    copied.appendCopies(values.slice())\n    i += 1\nrequire copied.length == 24 and copied[23] == 3 else => $abort(\"copies\")", 1, 0);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ZeroSizedSliceCopiesAllocateNothing()
        => WriteCostFixture("SliceCopyZeroSized", "let values: [1024 of ()] = [1024 of ()]\nlet copied = values.slice().toArray()\nrequire copied.length == 1024 else => $abort(\"length\")", 0, 0);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ExclusiveViewsAndChildrenAllocateNoElementStorage()
        => WriteCostFixture("UniqViews", "var values: [4 of i32] = [1, 2, 3, 4]\nvar i = 0\nwhile i < 128\n    var view = values.sliceUniq()\n    var child = view.sliceUniq(1..3)\n    child[0] = i\n    require child.slice()[0] == i else => $abort(\"view\")\n    i += 1", 0, null);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void BorrowedElementUpdatesReuseTheBuffer()
        => WriteCostFixture("BorrowedWrite", "func edit(values: uniq/Array<isize>)\n    var i: isize = 0\n    while i < 1024\n        values[0] = i\n        values[1] += 1\n        i += 1\nvar values: Array<isize> = [0, 0]\nedit(values@uniq)\nrequire values[0] == 1023 and values[1] == 1024 else => $abort(\"write\")", 1, 0);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void BorrowedFixedArrayUpdatesAllocateNoStorage()
        => WriteCostFixture("BorrowedFixedWrite", "func edit(values: uniq/[2 of isize])\n    var i: isize = 0\n    while i < 1024\n        values[0] = i\n        values[1] += 1\n        i += 1\nvar values: [2 of isize] = [0, 0]\nedit(values@uniq)\nrequire values[0] == 1023 and values[1] == 1024 else => $abort(\"write\")", 0, null);

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Array<i32>", "Array", 1)]
    [InlineData("[2 of i32]", "Fixed", 0)]
    public void DisjointPairsAllocateNoAdditionalStorage(string type, string name, int allocations)
        => WriteCostFixture("DisjointPair" + name, DisjointArrayPairTest.RepeatedUpdates(type), allocations, allocations == 0 ? null : 0);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void SortingRetainsStorageWithoutFurtherAllocation()
        => WriteCostFixture("Sort", "var values: Array<isize> = [7, 3, 5, 1, 6, 2, 4, 3]\nvalues.sort()\nvalues.reverse()\nvalues.sort()\nrequire values[0] == 1 and values[7] == 7 and values.length == 8 else => $abort(\"sort\")\nvar empty: Array<isize> = []\nempty.sort()", 1, 0);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void EmptyConstructionAndEmptyOperationsAllocateNothing()
        => WriteCostFixture("Empty", "var values: Array<isize> = []\nvalues@uniq.reserve(0)\nvalues@uniq.clear()\nvalues@uniq.shrinkToFit()\nmatch values@uniq.pop()\n    .None => ()\n    .Some(_) => $abort(\"empty\")\nfor value in values@move => $abort(\"iteration\")", 0, 0);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ZeroSizedElementsNeverAllocate()
        => WriteCostFixture("ZeroSized", "var units: Array<()> = [(), ()]\nvar i: isize = 0\nwhile i < 1024\n    units@uniq.append(())\n    units@uniq.insert(0, ())\n    units@uniq.remove(1)\n    i += 1\nunits@uniq.reserve(4096)\nunits@uniq.shrinkToFit()\nrequire units.length == 1026 else => $abort(\"length\")\nfor unit in units@move => ()", 0, 0);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void CapacityPreservingMutationAndChurnNeedOnlyTheInitialAllocation()
        => WriteCostFixture(
            "Churn",
            "var values: Array<isize> = []\nvalues@uniq.reserve(4)\nvar i: isize = 0\nwhile i < 2048\n    values@uniq.append(i)\n    values@uniq.append(i + 1)\n    values@uniq.insert(1, i + 2)\n    values@uniq.insert(^0, i + 3)\n    values[0] = 42\n    require values[0] == 42 and values.length == 4 else => $abort(\"replacement\")\n    values@uniq.reserve(0)\n    let removed = values@uniq.remove(1)\n    require removed == i + 2 else => $abort(\"remove\")\n    match values@uniq.pop()\n        .Some(let last) => require last == i + 3 else => $abort(\"pop\")\n        .None => $abort(\"empty\")\n    values@uniq.clear()\n    values@uniq.reserve(4)\n    i += 1\nrequire values.length == 0 and values.capacity >= 4 else => $abort(\"capacity\")",
            1,
            0);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RepeatedSharedViewsAndIterationAllocateNoStorage()
        => WriteCostFixture("SharedViews", "let values: Array<isize> = [10, 20, 22, 30]\nvar i = 0\nwhile i < 1024\n    let view = values[1..3]\n    let copied = view[..]\n    let item = copied[0]@ref\n    require item == 20 else => $abort(\"borrow\")\n    var sum: isize = 0\n    for value in copied => sum += value\n    require sum == 42 else => $abort(\"iteration\")\n    i += 1", 1, 0);

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(17, false)]
    [InlineData(1000, false)]
    [InlineData(1024, false)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(17, true)]
    [InlineData(1000, true)]
    [InlineData(1024, true)]
    public void GrowthTransfersStayLinearWithAndWithoutRepeatedReserve(int count, bool reserve)
    {
        var source = "var values: Array<isize> = []\nvar i: isize = 0\nwhile i < " + count + "\n" +
            (reserve ? "    values@uniq.reserve(1)\n" : string.Empty) +
            "    values@uniq.append(i)\n    i += 1\nrequire values.length == " + count + " else => $abort(\"length\")\nvar total: isize = 0\nfor value in values@move => total += value\nrequire total == " + ((long)count * (count - 1) / 2) + " else => $abort(\"values\")";
        WriteCostFixture("Growth" + count + (reserve ? "Reserve" : "Append"), source, new(1, 1 + (int)Math.Ceiling(Math.Log2(count))), new(0, 16L * count));
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShrinkSuccessAndFailurePreserveLiveValues(bool fail)
    {
        var source = "var values: Array<isize> = []\nvalues@uniq.reserve(8)\nvalues@uniq.append(20)\nvalues@uniq.append(22)\nlet previous = values.capacity\nvalues@uniq.shrinkToFit()\nrequire values.length == 2 and values[0] == 20 and values[1] == 22 else => $abort(\"values\")\nrequire values.capacity >= 2 and values.capacity <= previous else => $abort(\"capacity\")\n" +
            (fail ? "require values.capacity == previous else => $abort(\"failed shrink changed capacity\")\n" : string.Empty);
        // A failed shrink keeps, and releases only, the first storage.
        NativeAllocationAudit.WriteFixture(
            "DynamicArrayCost" + (fail ? "ShrinkFailure" : "ShrinkSuccess"),
            source,
            2,
            fail ? 1 : 2,
            NativeAllocationAudit.Bound.Any,
            failAllocation: fail ? 2 : 0,
            transferred: 0);
    }

    // The storage boundary helpers of the Kimigayo iterators are keyed by layout ids, so warm emission reuses them without
    // building their names again.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("var it = values.iterate()\nloop\n    match it.next()\n        .Some(let v) => total += v\n        .None => exit")]
    [InlineData("var it = values.iterateUniq()\nloop\n    match it.next()\n        .Some(let v) => v@follow += 1\n        .None => exit")]
    [InlineData("var it = (values@move).intoIterator()\nloop\n    match it.next()\n        .Some(let v) => total += v\n        .None => exit")]
    public void WarmStorageIteratorEmissionAllocatesNothing(string loop)
    {
        var c = MinimalEmissionTest.Analyze("var values: Array<i32> = [1, 2]\nvar total: i32 = 0\n" + loop);
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Analyze().IsVerified);
        Assert.True(c.WriteIr(TextWriter.Null, out var error), error);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.WriteIr(TextWriter.Null, out _)));
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Binding")]
    [InlineData("Ownership")]
    [InlineData("Emission")]
    [InlineData("Validation")]
    [InlineData("Pipeline")]
    public void WarmArrayAnalysisAndEmissionAllocateNothing(string stage)
    {
        var c = MinimalEmissionTest.Analyze("struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop => ()\nlet tasks: Array<Task> = [Task.init(42)]\nlet task = tasks[0]@ref\nrequire task.id == 42 else => $abort(\"task\")\nlet taskView = tasks[..]\nfor item in taskView => require item.id == 42 else => $abort(\"shared\")\nvar values: Array<i32> = [1, 2]\nvalues@uniq.insert(^0, 3)\nvalues[0] = 4\nlet last = values@uniq.remove(^1)\nlet view = values[0..1]\nlet item = view[0]@ref\nrequire item == 4 else => $abort(\"view\")\nfor value in values@move => require value > 0 else => $abort(\"value\")\nlet filled = Array<Task>.init(capacity: 0)\nlet copies = Array<i32>.init(repeating: 5, count: 3)\nrequire copies[2] == 5 and filled.length == 0 else => $abort(\"repeating\")");
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Analyze().IsVerified);
        Assert.True(c.WriteIr(TextWriter.Null, out var error), error);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (stage == "Pipeline")
            {
                if (!c.Bind().IsComplete)
                {
                    throw new InvalidOperationException("Array warm Binding failed.");
                }

                c.Binding.CheckStartup(OutputKind.Application);
                if (!c.Analyze().IsVerified || !c.WriteIr(TextWriter.Null, out _))
                {
                    throw new InvalidOperationException("Array warm compilation failed.");
                }

                return;
            }

            var valid = stage switch
            {
                "Binding" => c.Bind().IsComplete,
                "Ownership" => c.Analyze().IsVerified,
                "Validation" => c.Validate(out _),
                _ => c.WriteIr(TextWriter.Null, out _),
            };
            if (!valid)
            {
                throw new InvalidOperationException("Array warm compilation failed.");
            }
        }));
    }

    [Fact]
    public void ReloadEmitsOnlyTheCurrentlyUsedCachedHelpers()
    {
        var c = MinimalEmissionTest.Analyze("var values: Array<string> = [\"before\"]\nlet first = values@uniq.remove(0)\nConsole.writeLine(first)");
        using var initial = new StringWriter();
        Assert.True(c.WriteIr(initial, out var error), error);
        Assert.Contains("@__kimi_array_remove_", initial.ToString()); // A generator names its helper (DECISIONS R5-M121).
        var replacement = MinimalEmissionTest.Analyze("var values: Array<i32> = [20, 22]\nrequire values[0] + values[1] == 42 else => $abort(\"values\")\nvalues@uniq.clear()");
        var bytes = Tinyhand.TinyhandSerializer.Serialize(replacement.Kotonoha);
        var tree = c.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref tree);
        Assert.Same(c.Kotonoha, tree);
        tree!.OnDeserialized(c);
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Analyze().IsVerified);
        using var output = new StringWriter();
        Assert.True(c.WriteIr(output, out error), error);
        var ir = output.ToString();
        Assert.DoesNotContain("@__kimi_array_remove_", ir);

        ScalarEmissionTest.WriteFixture("DynamicArrayCostReload", ir, string.Empty);
    }

    // Audits the allocations within their bound, each released by exit, and under transferred the bytes Array growth copies;
    // fixed storage has no growth routine to count. Exact capacities are implementation-defined (SPEC 4.9.5): no byte claim.
    private static void WriteCostFixture(string name, string source, NativeAllocationAudit.Bound allocations, NativeAllocationAudit.Bound? transferred)
        => NativeAllocationAudit.WriteFixture(
            "DynamicArrayCost" + name,
            source,
            allocations,
            allocations,
            NativeAllocationAudit.Bound.Any,
            transferred: transferred);
}
