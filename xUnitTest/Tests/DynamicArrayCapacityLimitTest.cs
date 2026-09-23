// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Xunit;

namespace XunitTest;

public class DynamicArrayCapacityLimitTest
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(16)]
    [InlineData(2147483647)]
    public void OptionalGrowthOvershootDoesNotRejectRepresentableMinimum(long stride)
    {
        var maximum = long.MaxValue / stride;
        var capacity = (maximum / 2) + 1;
        var minimum = capacity + 1;
        var ir = RuntimeIr();
        // A test allocator records the request and supplies an address without reserving exabytes.
        // The synthetic handle is empty, so the runtime must not read the backing allocation.
        ir = ReplaceDefinition(ir, "__kimi_alloc", "define internal ptr @__kimi_alloc(i64 %size, ptr %location, i64 %location_length) {\nentry:\n  store i64 %size, ptr @probe_size, align 8\n  ret ptr @probe_memory\n}");
        var body = $$"""
            {{Handle(0, capacity)}}
              call void @__kimi_array_grow(ptr %handle, i64 {{stride}}, i64 {{minimum}}, ptr @probe_location, i64 5)
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
        WriteProbe("Overshoot" + stride, ir, body, 0, string.Empty);
    }

    [Theory]
    [InlineData("Append", "call void @__kimi_array_append_i8(ptr %handle, i8 1, ptr @probe_location, i64 5)")]
    [InlineData("Insert", "call void @__kimi_array_insert_i8(ptr %handle, i64 0, i8 1, ptr @probe_location, i64 5)")]
    public void IncreasedLengthIsCheckedBeforeBufferAccess(string name, string call)
        => WriteProbe(name, RuntimeIr(), Handle(long.MaxValue, long.MaxValue) + "\n  " + call + "\n  call void @__kimi_exit(i32 93)\n  unreachable", 1, "probe: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");

    [Fact]
    public void AnUnrepresentableMinimumStillAborts()
        => WriteProbe("RequiredSize", RuntimeIr(), Handle(0, 0) + "\n  call void @__kimi_array_grow(ptr %handle, i64 16, i64 576460752303423488, ptr @probe_location, i64 5)\n  call void @__kimi_exit(i32 93)\n  unreachable", 1, "probe: abort KIMI_E_ALLOC_SIZE: Allocation size exceeds limit\n");

    [Fact]
    public void InvalidInsertionPositionPrecedesTheAdditionLimit()
        => WriteProbe("BoundsFirst", RuntimeIr(), Handle(long.MaxValue, long.MaxValue) + "\n  call void @__kimi_array_insert_i8(ptr %handle, i64 -1, i8 1, ptr @probe_location, i64 5)\n  call void @__kimi_exit(i32 93)\n  unreachable", 1, "probe: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    internal static string RuntimeIr()
    {
        var compilation = MinimalEmissionTest.Analyze("var values: Array<u8> = []\nvalues@uniq.append(1)\nvalues@uniq.insert(0, 2)");
        using var writer = new StringWriter();
        Assert.True(compilation.Emission.WriteIr(writer, out var error), error);
        return writer.ToString();
    }

    internal static string ReplaceDefinition(string ir, string name, string replacement)
    {
        var pattern = @"(?ms)^define[^\n]*@" + Regex.Escape(name) + @"\([^\n]*\n.*?^\}";
        Assert.Single(Regex.Matches(ir, pattern).Cast<Match>());
        return Regex.Replace(ir, pattern, _ => replacement);
    }

    private static string Handle(long length, long capacity)
        => $$"""
              %handle = alloca { ptr, i64, i64 }, align 8
              store ptr null, ptr %handle, align 8
              %length_ptr = getelementptr i8, ptr %handle, i64 8
              store i64 {{length}}, ptr %length_ptr, align 8
              %capacity_ptr = getelementptr i8, ptr %handle, i64 16
              store i64 {{capacity}}, ptr %capacity_ptr, align 8
            """;

    private static void WriteProbe(string name, string ir, string body, int exit, string stderr)
    {
        ir = ReplaceDefinition(ir, "__kimi_start", "define void @__kimi_start() noreturn #0 {\nentry:\n" + body + "\n}");
        ir += "\n@probe_location = private constant [5 x i8] c\"probe\"\n@probe_memory = private global i8 0\n@probe_size = private global i64 0\n";
        ScalarEmissionTest.WriteFixture("DynamicArrayCapacity" + name, ir, string.Empty, exit, stderr);
    }
}
