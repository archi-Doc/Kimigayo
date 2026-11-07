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

    // R8-A1a: append's body reaches the same limit from source, its buffer null: the checked sum Aborts before any buffer access, at the call.
    [Fact]
    public void AppendChecksTheIncreasedLengthBeforeBufferAccess()
    {
        const string Source = """
            #Layout("C")
            struct Words
                public var data: raw/u8
                public var length: isize
                public var capacity: isize

            var values: Array<u8> = []
            let largest: i64 = 9223372036854775807
            let words = values@raw@raw/Words
            unsafe
                (*words).length = largest@isize
                (*words).capacity = largest@isize
            values.append(1)
            """;
        ScalarEmissionTest.EmitFixture("DynamicArrayCapacityAppend", Source, string.Empty, 1, "Hello.kimi:13:1: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");
    }

    // R8-A1a, SPEC 4.9.5, 22.5.2: `reserve`'s Abort paths through `growArray`, each with its code at the call: a negative request, a sum past
    // `isize` (also over zero-sized elements), a byte size past the limit (the unrepresentable minimum among them) and a failed allocation.
    [Theory]
    [InlineData("Negative", "var values: Array<i32> = []\nvalues.reserve(additional: -1)", 2, "KIMI_E_ARG_RANGE: Argument out of range")]
    [InlineData("Sum", "var values: Array<i32> = [1]\nlet count: i64 = 9223372036854775807\nvalues.reserve(additional: count@isize)", 3, "KIMI_E_INT_OVERFLOW: Integer overflow")]
    [InlineData("ZeroSizedSum", "var values: Array<()> = []\nvalues.append(())\nlet count: i64 = 9223372036854775807\nvalues.reserve(additional: count@isize)", 4, "KIMI_E_INT_OVERFLOW: Integer overflow")]
    [InlineData("Size", "var values: Array<i64> = []\nlet count: i64 = 2305843009213693952\nvalues.reserve(additional: count@isize)", 3, "KIMI_E_ALLOC_SIZE: Allocation size exceeds limit")]
    [InlineData("UnrepresentableMinimum", "var values: Array<(i64, i64)> = []\nlet count: i64 = 576460752303423488\nvalues.reserve(additional: count@isize)", 3, "KIMI_E_ALLOC_SIZE: Allocation size exceeds limit")]
    [InlineData("Allocation", "var values: Array<u8> = []\nlet count: i64 = 4611686018427387904\nvalues.reserve(additional: count@isize)", 3, "KIMI_E_ALLOC: Failed to allocate memory")]
    public void ReserveAbortsAtItsCall(string name, string source, int line, string abort)
        => ScalarEmissionTest.EmitFixture("DynamicArrayCapacityReserve" + name, source, string.Empty, 1, $"Hello.kimi:{line}:1: abort {abort}\n");

    // R8-A1a: `growArray`'s optional doubling cannot reject a representable minimum, from source: the capacity is past half the maximum, a test
    // allocator takes the clamped request without reserving exabytes, and the program restores an empty handle before its destruction.
    [Theory]
    [InlineData("u8", 1)]
    [InlineData("[3 of u8]", 3)]
    [InlineData("(i64, i64)", 16)]
    [InlineData("[2147483647 of u8]", 2147483647)]
    public void ReserveOvershootDoesNotRejectARepresentableMinimum(string element, long stride)
    {
        var maximum = long.MaxValue / stride;
        var capacity = (maximum / 2) + 1;
        var source = $$"""
            #Layout("C")
            struct Words
                public var data: raw/u8
                public var length: isize
                public var capacity: isize

            var values: Array<{{element}}> = []
            let words = values@raw@raw/Words
            let capacity: i64 = {{capacity}}
            let minimum: i64 = {{capacity + 1}}
            let maximum: i64 = {{maximum}}
            unsafe => (*words).capacity = capacity@isize
            values.reserve(additional: minimum@isize)
            unsafe
                let grown = (*words).capacity@i64
                require grown >= minimum and grown <= maximum else => $abort("capacity")
                (*words).data = null
                (*words).capacity = 0
            """;
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze(source));
        ir = ReplaceDefinition(ir, "__kimi_alloc", $"define internal ptr @__kimi_alloc(i64 %size, ptr %location, i64 %location_length) {{\nentry:\n  %right = icmp eq i64 %size, {maximum * stride}\n  br i1 %right, label %done, label %wrong\nwrong:\n  call void @__kimi_exit(i32 93)\n  unreachable\ndone:\n  ret ptr @probe_memory\n}}");
        ScalarEmissionTest.WriteFixture("DynamicArrayCapacityReserveOvershoot" + stride, ir + "\n@probe_memory = private global i8 0\n", string.Empty);
    }

    [Theory]
    [InlineData("Insert", "__kimi_array_insert_", "(ptr %handle, i64 0, i8 1, ptr @probe_location, i64 5)")]
    public void IncreasedLengthIsCheckedBeforeBufferAccess(string name, string helper, string arguments)
    {
        var ir = RuntimeIr();
        WriteProbe(name, ir, Handle(long.MaxValue, long.MaxValue) + "\n  call void @" + Helper(ir, helper) + arguments + "\n  call void @__kimi_exit(i32 93)\n  unreachable", 1, "probe: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");
    }

    [Fact]
    public void AnUnrepresentableMinimumStillAborts()
        => WriteProbe("RequiredSize", RuntimeIr(), Handle(0, 0) + "\n  call void @__kimi_array_grow(ptr %handle, i64 16, i64 576460752303423488, ptr @probe_location, i64 5)\n  call void @__kimi_exit(i32 93)\n  unreachable", 1, "probe: abort KIMI_E_ALLOC_SIZE: Allocation size exceeds limit\n");

    [Fact]
    public void InvalidInsertionPositionPrecedesTheAdditionLimit()
    {
        var ir = RuntimeIr();
        WriteProbe("BoundsFirst", ir, Handle(long.MaxValue, long.MaxValue) + "\n  call void @" + Helper(ir, "__kimi_array_insert_") + "(ptr %handle, i64 -1, i8 1, ptr @probe_location, i64 5)\n  call void @__kimi_exit(i32 93)\n  unreachable", 1, "probe: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");
    }

    [Fact]
    public void CleanupFreeClearDoesNotWalkTheElements()
    {
        var ir = RuntimeIr();
        WriteProbe("ConstantClear", ir, Handle(long.MaxValue, long.MaxValue) + "\n  call void @" + Helper(ir, "__kimi_array_clear_") + "(ptr %handle, ptr @probe_location, i64 5)\n  %length = load i64, ptr %length_ptr, align 8\n  %empty = icmp eq i64 %length, 0\n  br i1 %empty, label %passed, label %failed\npassed:\n  call void @__kimi_exit(i32 0)\n  unreachable\nfailed:\n  call void @__kimi_exit(i32 93)\n  unreachable", 0, string.Empty);
    }

    internal static string RuntimeIr()
    {
        var compilation = MinimalEmissionTest.Analyze("var values: Array<u8> = []\nvalues@uniq.append(1)\nvalues@uniq.insert(0, 2)\nvalues@uniq.clear()");
        var ir = CompilationTestHelper.WriteIr(compilation);
        return ir;
    }

    // The one Array helper of a kind the module defines, by its kind's prefix: its name is its generator's (the old one names it by its
    // element, MIR code generation by its item; DECISIONS R5-M121), so the probe calls it whichever wrote the module.
    internal static string Helper(string ir, string prefix)
        => Assert.Single(Regex.Matches(ir, @"(?m)^define internal void @(" + Regex.Escape(prefix) + @"[A-Za-z0-9_]+)\(").Cast<Match>()).Groups[1].Value;

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
