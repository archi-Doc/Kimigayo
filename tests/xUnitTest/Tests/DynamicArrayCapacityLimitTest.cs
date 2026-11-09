// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Xunit;

namespace XunitTest;

public class DynamicArrayCapacityLimitTest
{
    // R8-A1a: append's body reaches the same limit from source, its buffer null: the checked sum Aborts before any buffer access, at the call.
    [Fact]
    public void AppendChecksTheIncreasedLengthBeforeBufferAccess()
    {
        ScalarEmissionTest.EmitFixture("DynamicArrayCapacityAppend", FullArray("u8", long.MaxValue) + "values.append(1)", string.Empty, 1, "Hello.kimi:13:1: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");
    }

    // R8-A1b, SPEC 4.9.3, 4.9.7: insert's Abort paths from source, each at insertAt's call in its entry and before any buffer access, the buffer
    // null: the sum, an invalid position first over a full Array, and a byte size past the limit through growArray.
    [Theory]
    [InlineData("Insert", "u8", long.MaxValue, "values.insert(0, 1)", "self.insertAt(position, value@move)", "KIMI_E_INT_OVERFLOW: Integer overflow")]
    [InlineData("BoundsFirst", "u8", long.MaxValue, "let position: isize = -1\nvalues.insert(position, 1)", "self.insertAt(-1", "KIMI_E_INDEX_BOUNDS: Index out of bounds")]
    [InlineData("InsertSize", "(i64, i64)", 576460752303423487, "values.insert(5, (1, 2))", "self.insertAt(position, value@move)", "KIMI_E_ALLOC_SIZE: Allocation size exceeds limit")]
    public void InsertAbortsAtItsEntry(string name, string element, long length, string statement, string anchor, string abort)
        => ScalarEmissionTest.EmitFixture("DynamicArrayCapacity" + name, FullArray(element, length) + statement, string.Empty, 1, LibrarySource.Location("Array.kimi", anchor) + $": abort {abort}\n");

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

    // SPEC 4.9.8: `clear` is O(1 + d), so over a cleanup-free element it walks nothing at any optimization level: an Array of the largest
    // length, its buffer null, clears at once at O0 and O2, and a cleanup-free destroyValues is no loop in the generated text.
    [Fact]
    public void CleanupFreeClearDoesNotWalkTheElements()
    {
        ScalarEmissionTest.EmitFixture("DynamicArrayCapacityConstantClear", FullArray("u8", long.MaxValue) + "values.clear()\nrequire values.length == 0 else => $abort(\"length\")\nConsole.writeLine(\"ok\")", "ok\n");
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze("func f(values: uniq/Array<u8>) => values.clear()\nvar values: Array<u8> = []\nf(values@uniq)"));
        Assert.DoesNotContain("destroying", ir, StringComparison.Ordinal);
        var kept = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze("func f(values: uniq/Array<string>) => values.clear()\nvar values: Array<string> = []\nf(values@uniq)"));
        Assert.Contains("destroying", kept, StringComparison.Ordinal);
    }

    // SPEC 4.9.8: `truncate` is O(1 + d) as `clear` is: the same Array keeps its first element at once at O0 and O2. A walk from the end reads
    // the null buffer at the top of the address space; FunctionCodegenTest's TruncateDestroysThroughItsOwnBody pins that no walk is written.
    [Fact]
    public void CleanupFreeTruncateDoesNotWalkTheElements()
        => ScalarEmissionTest.EmitFixture("DynamicArrayCapacityConstantTruncate", FullArray("u8", long.MaxValue) + "values.truncate(1)\nrequire values.length == 1 else => $abort(\"length\")\nConsole.writeLine(\"ok\")", "ok\n");

    internal static string ReplaceDefinition(string ir, string name, string replacement)
    {
        var pattern = @"(?ms)^define[^\n]*@" + Regex.Escape(name) + @"\([^\n]*\n.*?^\}";
        Assert.Single(Regex.Matches(ir, pattern).Cast<Match>());
        return Regex.Replace(ir, pattern, _ => replacement);
    }

    // A program prefix: `values`, an Array whose length and capacity are `length` and whose buffer is null, so a call must fail before any access.
    private static string FullArray(string element, long length)
        => $$"""
            #Layout("C")
            struct Words
                public var data: raw/u8
                public var length: isize
                public var capacity: isize

            var values: Array<{{element}}> = []
            let count: i64 = {{length}}
            let words = values@raw@raw/Words
            unsafe
                (*words).length = count@isize
                (*words).capacity = count@isize

            """;
}
