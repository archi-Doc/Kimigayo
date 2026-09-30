// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class DictionaryCapacityTest
{
    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(2147483647)]
    public void OptionalGrowthRoundingKeepsARepresentableMinimum(long stride)
    {
        var maximum = long.MaxValue / stride;
        var capacity = (maximum / 2) + 1;
        var minimum = capacity + 1;
        var ir = DynamicArrayCapacityLimitTest.ReplaceDefinition(RuntimeIr(), "__kimi_alloc", "define internal ptr @__kimi_alloc(i64 %size, ptr %location, i64 %location_length) {\nentry:\n  store i64 %size, ptr @probe_size, align 8\n  ret ptr @probe_memory\n}");
        var body = $$"""
            {{Handle(0, capacity)}}
              call void @__kimi_dictionary_reserve(ptr %handle, i64 {{stride}}, i64 {{minimum}}, ptr @probe_location, i64 5)
              %actual_capacity = load i64, ptr %capacity_ptr, align 8
              %enough = icmp uge i64 %actual_capacity, {{minimum}}
              %representable = icmp ule i64 %actual_capacity, {{maximum}}
              %valid_capacity = and i1 %enough, %representable
              %requested = load i64, ptr @probe_size, align 8
              %expected_bytes = mul i64 %actual_capacity, {{stride}}
              %valid_bytes = icmp eq i64 %requested, %expected_bytes
              %valid = and i1 %valid_capacity, %valid_bytes
              br i1 %valid, label %passed, label %failed
            passed:
              call void @__kimi_exit(i32 0)
              unreachable
            failed:
              call void @__kimi_exit(i32 93)
              unreachable
            """;
        WriteProbe("Rounding" + stride, ir, body, 0, string.Empty);
    }

    [Fact]
    public void InsertionRejectsLengthOverflowBeforeAccessingStorage()
        => WriteProbe("InsertionLimit", RuntimeIr(), Handle(long.MaxValue, long.MaxValue) + "\n  %slot = call ptr @__kimi_dictionary_append_slot(ptr %handle, i64 24, ptr @probe_location, i64 5)\n  call void @__kimi_exit(i32 93)\n  unreachable", 1, "probe: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");

    [Fact]
    public void AllocationFailureLeavesThePreviousHandleUntouched()
    {
        var ir = RuntimeIr().Replace("call ptr @HeapAlloc(", "call ptr @probe_fail_alloc(", StringComparison.Ordinal);
        const string abort = """
            define internal void @__kimi_abort(i32 %reason, ptr %location, i64 %location_length, i64 %error) noreturn #0 {
            entry:
              %buffer = load ptr, ptr @probe_handle, align 8
              %capacity_ptr = getelementptr i8, ptr @probe_handle, i64 16
              %capacity = load i64, ptr %capacity_ptr, align 8
              %same_buffer = icmp eq ptr %buffer, @probe_memory
              %same_capacity = icmp eq i64 %capacity, 4
              %allocation = icmp eq i32 %reason, 3
              %same = and i1 %same_buffer, %same_capacity
              %valid = and i1 %same, %allocation
              br i1 %valid, label %passed, label %failed
            passed:
              call void @__kimi_exit(i32 0)
              unreachable
            failed:
              call void @__kimi_exit(i32 93)
              unreachable
            }
            """;
        ir = DynamicArrayCapacityLimitTest.ReplaceDefinition(ir, "__kimi_abort", abort);
        ir += "\n@probe_handle = private global { ptr, i64, i64, i64, i64, i64, i64 } { ptr @probe_memory, i64 0, i64 4, i64 0, i64 0, i64 0, i64 0 }\n" +
            "define internal ptr @probe_fail_alloc(ptr %heap, i32 %flags, i64 %bytes) {\nentry:\n  ret ptr null\n}\n";
        WriteProbe("AllocationFailure", ir, "  call void @__kimi_dictionary_reserve(ptr @probe_handle, i64 24, i64 5, ptr @probe_location, i64 5)\n  call void @__kimi_exit(i32 93)\n  unreachable", 0, string.Empty);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void GrowthCopiesTheHighWaterSlotsAndKeepsInsertionAndFreeLinks()
    {
        const string source = """
            var entries = [1: 10, 2: 20, 3: 30, 4: 40]
            _ = entries.remove(2)
            _ = entries.remove(3)
            entries.reserve(5)
            _ = entries.tryInsert(5, 50)
            _ = entries.tryInsert(6, 60)
            _ = entries.tryInsert(7, 70)
            var position = 0
            for (key, value) in entries
                require value == key * 10 else => $abort("pair")
                if position == 0 => require key == 1 else => $abort("first")
                if position == 1 => require key == 4 else => $abort("second")
                if position >= 2 => require key == position + 3 else => $abort("reused")
                position += 1
            require position == 5 else => $abort("length")
            """;
        NativeAllocationAudit.WriteFixture("DictionaryCapacityHoles", source, 2, 2, 288, maxTransferredBytes: 96, minTransferredBytes: 96);
    }

    private static string RuntimeIr()
        => CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze("var entries: Dictionary<i32, i32> = [:]\nentries.reserve(1)"));

    private static string Handle(long length, long capacity) => $$"""
              %handle = alloca { ptr, i64, i64, i64, i64, i64, i64 }, align 8
              store { ptr, i64, i64, i64, i64, i64, i64 } zeroinitializer, ptr %handle, align 8
              %length_ptr = getelementptr i8, ptr %handle, i64 24
              store i64 {{length}}, ptr %length_ptr, align 8
              %capacity_ptr = getelementptr i8, ptr %handle, i64 16
              store i64 {{capacity}}, ptr %capacity_ptr, align 8
        """;

    private static void WriteProbe(string name, string ir, string body, int exit, string stderr)
    {
        ir = DynamicArrayCapacityLimitTest.ReplaceDefinition(ir, "__kimi_start", "define void @__kimi_start() noreturn #0 {\nentry:\n" + body + "\n}");
        ir += "\n@probe_location = private constant [5 x i8] c\"probe\"\n@probe_memory = private global i8 0\n@probe_size = private global i64 0\n";
        ScalarEmissionTest.WriteFixture("DictionaryCapacity" + name, ir, string.Empty, exit, stderr);
    }
}
