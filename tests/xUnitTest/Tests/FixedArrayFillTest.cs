// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

#pragma warning disable SA1117, SA1118 // Multiline language fixtures.

/// <summary>SPEC 4.3: fixed-array fill acquires one Copy value, including at length zero.</summary>
public class FixedArrayFillTest
{
    [Fact]
    public void FillEvaluatesOnceAndSupportsInferenceAndCompositeElements()
        => ScalarEmissionTest.EmitFixture("Utf8FillValues", """
            func value() -> i32
                Console.writeLine("value")
                return 7
            let empty = [0 of value()]
            let values = [5 of value()]
            require empty.length == 0 and values[0] == 7 and values[4] == 7 else => $abort("evaluation")
            let bytes: [64 of u8] = [64 of 255]
            require bytes[0] == 255 and bytes[63] == 255 else => $abort("bytes")
            let pairs = [3 of (4, true)]
            require pairs[2].0 == 4 and pairs[2].1 else => $abort("pair")
            let nested: [2 of [3 of i32]] = [2 of [3 of 9]]
            require nested[1][2] == 9 else => $abort("nested")
            let inferred: [3 of _] = [3 of false]
            require not inferred[1] else => $abort("inference")
            let composite = [(2 + 3) of 8]
            require composite.length == 5 and composite[4] == 8 else => $abort("length")
            Console.writeLine("ok")
            """, "value\nvalue\nok\n");

    [Fact]
    public void FillWorksInGenericBodiesAndCandidateLocalArguments()
        => ScalarEmissionTest.EmitFixture("Utf8FillGeneric", """
            func filled<length N, T>(value: T) -> [N of T]
                T is Copy
                return [N of value]
            func count<length N>(values: [N of u8]) -> isize => values.length
            func identity<T>(value: T) -> T => value@move
            let values = filled<5, i32>(12)
            require values[4] == 12 else => $abort("generic")
            require count([4 of 255]) == 4 else => $abort("argument")
            let flags = identity([2 of true])
            require flags[1] else => $abort("independent")
            Console.writeLine("ok")
            """, "ok\n");

    [Theory]
    [InlineData("let x: Array<i32> = [3 of 1]")]
    [InlineData("let x: [4 of i32] = [3 of 1]")]
    [InlineData("let x = [0 of \"not Copy\"]")]
    [InlineData("let x = [3 of \"not Copy\"]")]
    [InlineData("let x = [(-1) of 0]")]
    [InlineData("var n: isize = 3\nlet x = [n of 0]")]
    [InlineData("let x = [2 + 3 of 0]")]
    [InlineData("let x: [2 of u8] = [2 of 256]")]
    [InlineData("func take(value: Array<i32>) => ()\ntake([3 of 1])")]
    public void InvalidFillsAreRejected(string source)
    {
        var compilation = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(compilation.Emission.WriteIr(output, out _));
    }

    [Fact]
    public void FillCodeSizeDoesNotDependOnTheLength()
    {
        var compilation = MinimalEmissionTest.Analyze("let bytes: [4096 of u8] = [4096 of 0]\nrequire bytes[4095] == 0 else => $abort(\"fill\")");
        using var output = new StringWriter();
        Assert.True(compilation.Emission.WriteIr(output, out var error), error ?? MinimalEmissionTest.Describe(compilation, null));
        var ir = output.ToString();
        Assert.Contains("@__kimi_fill_array", ir, StringComparison.Ordinal);
        Assert.True(ir.Length < 100_000);
    }
}
