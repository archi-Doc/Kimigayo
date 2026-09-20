// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class SharedBitwiseEmissionTest
{
    [Theory]
    [InlineData("Masks", "func bits<T>(value?: ref/T, a?: u8, b?: u8) -> u8 => (a & b) | (a ^ b)\nlet v = true\nrequire bits(v, 170, 85) == 255 else => $abort(\"mask\")")]
    [InlineData("ShiftWidths", "func left<T>(value?: ref/T, n?: u64, count?: u8) -> u64 => n << count\nfunc right<T>(value?: ref/T, n?: i8, count?: i64) -> i8 => n >> count\nfunc logical<T>(value?: ref/T, n?: u8, count?: i16) -> u8 => n >> count\nlet v = true\nrequire left(v, 1, 63) == 9223372036854775808 and right(v, -3, 1) == -2 and logical(v, 128, 7) == 1 else => $abort(\"shift\")")]
    [InlineData("DiscardedBits", "func left<T>(value?: ref/T, n?: i8, count?: i16) -> i8 => n << count\nlet v = true\nrequire left(v, -128, 1) == 0 and left(v, 127, 1) == -2 and left(v, -128, 0) == -128 else => $abort(\"bits\")")]
    [InlineData("Compound", "func bits<T>(value?: ref/T) -> u8\n    var n: u8 = 12\n    n &= 10\n    n |= 128\n    n ^= 2\n    n <<= 1\n    n >>= 2\n    return n\nlet v = true\nrequire bits(v) == 5 else => $abort(\"compound\")")]
    [InlineData("Order", "func bits<T>(value?: ref/T) -> i32\n    var x = 1\n    let result = x++ << x++\n    return result + x\nlet v = true\nrequire bits(v) == 7 else => $abort(\"order\")")]
    [InlineData("Phi", "func choose<T>(value?: ref/T, flag?: bool, n?: i16, count?: i32) -> i16 => if flag => n << count else => n >> count\nlet v = true\nrequire choose(v, true, 3, 2) == 12 and choose(v, false, 12, 2) == 3 else => $abort(\"phi\")")]
    [InlineData("ShortCircuit", "func valid<T>(value?: ref/T, n?: u8, count?: u64) -> bool => count < 8 and (n << count) > 0\nlet v = true\nrequire not valid(v, 1, 256) and valid(v, 1, 7) else => $abort(\"short\")")]
    public void Executes(string name, string source)
        => ScalarEmissionTest.EmitFixture("SharedBitwise" + name, source, string.Empty);

    [Theory]
    [InlineData("Negative", "i8", "i64", "-1", ">>")]
    [InlineData("Width", "u8", "u8", "8", "<<")]
    [InlineData("BeforeNarrowing", "u8", "u64", "256", "<<")]
    [InlineData("UnsignedMaximum", "i64", "u64", "18446744073709551615", ">>")]
    public void InvalidCountsAbortBeforeTheShift(string name, string valueType, string countType, string count, string operation)
    {
        var header = $"func shift<T>(value?: ref/T, n?: {valueType}, count?: {countType}) -> {valueType} => n {operation} count";
        var source = header + $"\nlet v = true\nlet result = shift(v, 1, {count})";
        var column = header.IndexOf($"n {operation} count", StringComparison.Ordinal) + 1;
        ScalarEmissionTest.EmitFixture("SharedBitwise" + name, source, string.Empty, 1, $"Hello.kimi:1:{column}: abort KIMI_E_INT_SHIFT_COUNT: Shift count out of range\n");
    }
}
