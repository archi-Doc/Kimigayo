// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DynamicArrayCostTest
{
    [Fact]
    public void EmptyConstructionAndEmptyOperationsAllocateNothing()
        => WriteCostFixture("Empty", "var values: Array<isize> = []\nvalues@uniq.reserve(0)\nvalues@uniq.clear()\nvalues@uniq.shrinkToFit()\nmatch values@uniq.pop()\n    .None => ()\n    .Some(_) => $abort(\"empty\")\nfor value in values@move => $abort(\"iteration\")", 0, 0);

    [Fact]
    public void CapacityPreservingMutationAndChurnNeedOnlyTheInitialAllocation()
        => WriteCostFixture(
            "Churn",
            "var values: Array<isize> = []\nvalues@uniq.reserve(4)\nvar i: isize = 0\nwhile i < 2048\n    values@uniq.append(i)\n    values@uniq.append(i + 1)\n    values@uniq.insert(1, i + 2)\n    values@uniq.insert(^0, i + 3)\n    values[0] = 42\n    require values[0] == 42 and values.length == 4 else => $abort(\"replacement\")\n    values@uniq.reserve(0)\n    let removed = values@uniq.remove(1)\n    require removed == i + 2 else => $abort(\"remove\")\n    match values@uniq.pop()\n        .Some(let last) => require last == i + 3 else => $abort(\"pop\")\n        .None => $abort(\"empty\")\n    values@uniq.clear()\n    values@uniq.reserve(4)\n    i += 1\nrequire values.length == 0 and values.capacity >= 4 else => $abort(\"capacity\")",
            1,
            0);

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
        WriteCostFixture("Growth" + count + (reserve ? "Reserve" : "Append"), source, 1 + (int)Math.Ceiling(Math.Log2(count)), 16L * count, exactAllocations: false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShrinkSuccessAndFailurePreserveLiveValues(bool fail)
    {
        var source = "var values: Array<isize> = []\nvalues@uniq.reserve(8)\nvalues@uniq.append(20)\nvalues@uniq.append(22)\nlet previous = values.capacity\nvalues@uniq.shrinkToFit()\nrequire values.length == 2 and values[0] == 20 and values[1] == 22 else => $abort(\"values\")\nrequire values.capacity >= 2 and values.capacity <= previous else => $abort(\"capacity\")\n" +
            (fail ? "require values.capacity == previous else => $abort(\"failed shrink changed capacity\")\n" : string.Empty);
        WriteCostFixture(fail ? "ShrinkFailure" : "ShrinkSuccess", source, 2, 0, failAllocation: fail ? 2 : 0);
    }

    [Theory]
    [InlineData("Binding")]
    [InlineData("Ownership")]
    [InlineData("Emission")]
    [InlineData("Validation")]
    [InlineData("Pipeline")]
    public void WarmArrayAnalysisAndEmissionAllocateNothing(string stage)
    {
        var c = MinimalEmissionTest.Analyze("var values: Array<i32> = [1, 2]\nvalues@uniq.insert(^0, 3)\nvalues[0] = 4\nlet last = values@uniq.remove(^1)\nfor value in values@move => require value > 0 else => $abort(\"value\")");
        for (var i = 0; i < 32; i++)
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (stage == "Pipeline")
            {
                if (!c.Bind().IsComplete)
                {
                    throw new InvalidOperationException("Array warm Binding failed.");
                }

                c.Binding.CheckStartup(OutputKind.Application);
                if (!c.Ownership.Analyze().IsVerified || !c.Emission.WriteIr(TextWriter.Null, out _))
                {
                    throw new InvalidOperationException("Array warm compilation failed.");
                }

                return;
            }

            var valid = stage switch
            {
                "Binding" => c.Bind().IsComplete,
                "Ownership" => c.Ownership.Analyze().IsVerified,
                "Validation" => c.Emission.Validate(out _),
                _ => c.Emission.WriteIr(TextWriter.Null, out _),
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
        Assert.True(c.Emission.WriteIr(initial, out var error), error);
        Assert.Contains("@__kimi_array_remove_string", initial.ToString());
        var replacement = MinimalEmissionTest.Analyze("var values: Array<i32> = [20, 22]\nrequire values[0] + values[1] == 42 else => $abort(\"values\")\nvalues@uniq.clear()");
        var bytes = Tinyhand.TinyhandSerializer.Serialize(replacement.Kotonoha);
        var tree = c.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref tree);
        Assert.Same(c.Kotonoha, tree);
        tree!.OnDeserialized(c);
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified);
        using var output = new StringWriter();
        Assert.True(c.Emission.WriteIr(output, out error), error);
        var ir = output.ToString();
        Assert.DoesNotContain("@__kimi_array_remove_string", ir);
        Assert.DoesNotContain("@__kimi_array_place_string", ir);
        ScalarEmissionTest.WriteFixture("DynamicArrayCostReload", ir, string.Empty);
    }

    private static void WriteCostFixture(string name, string source, int allocations, long transferredBytes, int failAllocation = 0, bool exactAllocations = true)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.True(c.Emission.WriteIr(writer, out var error), MinimalEmissionTest.Describe(c, error));
        var ir = writer.ToString();
        Assert.Contains("call ptr @HeapAlloc(", ir);
        ir = ir.Replace("call ptr @HeapAlloc(", "call ptr @probe_allocate(", StringComparison.Ordinal);
        // Count only Array growth transfers; user payload construction and stable insert/remove are separate costs.
        const string CopyAnchor = "  %used = mul i64 %length, %stride\n";
        Assert.Equal(2, ir.Split(CopyAnchor, StringSplitOptions.None).Length);
        ir = ir.Replace(CopyAnchor, CopyAnchor + "  %previous_bytes = load i64, ptr @probe_transferred, align 8\n  %total_bytes = add i64 %previous_bytes, %used\n  store i64 %total_bytes, ptr @probe_transferred, align 8\n", StringComparison.Ordinal);
        var start = $$"""
            define void @__kimi_start() noreturn #0 {
            entry:
              call void @__kimi_entry_body()
              %allocations = load i64, ptr @probe_allocations, align 8
              %allocation_ok = icmp {{(exactAllocations ? "eq" : "ule")}} i64 %allocations, {{allocations}}
              %transferred = load i64, ptr @probe_transferred, align 8
              %transfer_ok = icmp ule i64 %transferred, {{transferredBytes}}
              %valid = and i1 %allocation_ok, %transfer_ok
              br i1 %valid, label %passed, label %failed
            passed:
              call void @__kimi_exit(i32 0)
              unreachable
            failed:
              call void @__kimi_exit(i32 93)
              unreachable
            }
            """;
        ir = DynamicArrayCapacityLimitTest.ReplaceDefinition(ir, "__kimi_start", start);
        ir += $$"""

            @probe_allocations = private global i64 0
            @probe_transferred = private global i64 0
            define internal ptr @probe_allocate(ptr %heap, i32 %flags, i64 %size) {
            entry:
              %previous = load i64, ptr @probe_allocations, align 8
              %count = add i64 %previous, 1
              store i64 %count, ptr @probe_allocations, align 8
              %fail = icmp eq i64 %count, {{failAllocation}}
              br i1 %fail, label %failed, label %allocate
            failed:
              ret ptr null
            allocate:
              %memory = call ptr @HeapAlloc(ptr %heap, i32 %flags, i64 %size)
              ret ptr %memory
            }

            """;
        ScalarEmissionTest.WriteFixture("DynamicArrayCost" + name, ir, string.Empty);
    }
}
