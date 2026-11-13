// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Numerics;
using System.Text;
using Xunit;

namespace XunitTest;

public class Utf8FloatTest
{
    private static readonly BigInteger[] PowersOfTen = Enumerable.Range(0, 350).Select(x => BigInteger.Pow(10, x)).ToArray();

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Zero", "0.0", "0")]
    [InlineData("NegativeZero", "-0.0", "-0")]
    [InlineData("Decimal", "1.2345", "1.2345")]
    [InlineData("SmallFixed", "1e-4", "0.0001")]
    [InlineData("SmallScientific", "1e-5", "1e-5")]
    [InlineData("LargeFixed", "1e15", "1000000000000000")]
    [InlineData("LargeScientific", "1e16", "1e16")]
    [InlineData("Negative", "-1.25", "-1.25")]
    [InlineData("Subnormal32", "1e-45@f32", "1e-45")]
    [InlineData("Subnormal64", "5e-324", "5e-324")]
    [InlineData("Largest32", "3.4028234663852886e38@f32", "3.4028235e38")]
    [InlineData("Largest64", "1.7976931348623157e308", "1.7976931348623157e308")]
    [InlineData("OriginalWidth", "0.1@f32", "0.1")]
    [InlineData("PromotedWidth", "(0.1@f32)@f64", "0.10000000149011612")]
    [InlineData("Infinity", "1.0 / 0.0", "Infinity")]
    [InlineData("NegativeInfinity", "-1.0 / 0.0", "-Infinity")]
    [InlineData("NaN", "0.0 / 0.0", "NaN")]
    public void CanonicalNotationFitsItsExactByteCount(string name, string expression, string expected)
    {
        var source = "var bytes = [" + expected.Length + " of 0@u8]\nmatch Text.tryFormat(" + expression + ", bytes@uniq)@move\n    .Ok(let text) => Console.writeLine(text)\n    .Err(_) => $abort(\"exact size\")";
        NativeAllocationAudit.WriteFixture("Utf8Float" + name, source, 0, 0, 0, expected + "\n");
    }

    // SPEC 22.10.5: toString's buffer starts empty (Text.heap(0), R8-W2 c3b), so its one allocation is the written bytes (fail-before:
    // the Type's bound, 17 and 24).
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("f32")]
    [InlineData("f64")]
    public void OwningFloatConversionAllocatesItsBytesOnce(string type)
        => NativeAllocationAudit.WriteFixture("Utf8FloatOwn" + type, "var value: " + type + " = 1.25\nConsole.writeLine(Text.toString(value))", 1, 1, 4, "1.25\n");

    [Fact]
    public void DecimalCoreMatchesAnIndependentOracleAcrossBinaryIntervals()
    {
        // IEEE bit patterns exercise every exponent, mantissa interval edges and deterministic random inputs.
        // An exact rational interval search is independent of the native table-based converter. The harness calls the FormatFloat
        // routine directly (R8-S2c): its returned count is the text's length, and no byte before, after or past 24 is written.
        var cases = new List<(ulong Bits, int Kind, string Text)>();
        for (var exponent = 0; exponent <= 2047; exponent++)
        {
            Add64((ulong)exponent << 52);
            Add64(((ulong)exponent << 52) | 1);
            Add64(((ulong)exponent << 52) | 0xfffffffffffffUL);
            if (exponent <= 255)
            {
                Add32((uint)exponent << 23);
                Add32(((uint)exponent << 23) | 1);
                Add32(((uint)exponent << 23) | 0x7fffffU);
            }
        }

        ulong state = 0x9e3779b97f4a7c15UL;
        for (var i = 0; i < 4096; i++)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            Add64(state);
            Add32((uint)state);
        }

        var c = MinimalEmissionTest.Analyze("var bytes = [24 of 0@u8]\n_ = Text.tryFormat(1.0, bytes@uniq)");
        using var writer = new StringWriter();
        Assert.True(c.Generate(writer), MinimalEmissionTest.Describe(c));
        var ir = new StringBuilder(writer.ToString());
        ir.Append("\n@float_cases = private constant [").Append(cases.Count).Append(" x {i64, i32, i32, [24 x i8]}] [\n");
        for (var i = 0; i < cases.Count; i++)
        {
            var (bits, kind, text) = cases[i];
            Assert.InRange(text.Length, 1, kind == 11 ? 17 : 24);
            ir.Append("{i64, i32, i32, [24 x i8]} {i64 ").Append(bits.ToString(CultureInfo.InvariantCulture)).Append(", i32 ").Append(kind).Append(", i32 ").Append(text.Length).Append(", [24 x i8] c\"").Append(text);
            for (var p = text.Length; p < 24; p++)
            {
                ir.Append("\\00");
            }

            ir.Append(i + 1 == cases.Count ? "\"}\n]\n" : "\"},\n");
        }

        ir.Append("""
            declare i32 @memcmp(ptr, ptr, i64)
            @float_passed = private constant [14 x i8] c"oracle passed\0A"

            define internal void @float_audit() #0 {
            entry:
              %bytes = alloca [26 x i8], align 8
              %destination = getelementptr i8, ptr %bytes, i64 1
              %lastp = getelementptr i8, ptr %bytes, i64 25
              br label %loop
            loop:
              %index = phi i64 [0, %entry], [%next, %passed]
              %item = getelementptr {i64, i32, i32, [24 x i8]}, ptr @float_cases, i64 %index
              %kindp = getelementptr i8, ptr %item, i64 8
              %lengthp = getelementptr i8, ptr %item, i64 12
              %expected = getelementptr i8, ptr %item, i64 16
              %bits = load i64, ptr %item
              %kind = load i32, ptr %kindp
              %narrow = icmp eq i32 %kind, 11
              %length32 = load i32, ptr %lengthp
              %length = zext i32 %length32 to i64
              call void @llvm.memset.p0.i64(ptr %bytes, i8 42, i64 26, i1 false)
              %count = call i64 @__kimi_format_float(i64 %bits, i1 %narrow, ptr %destination)
              %same_length = icmp eq i64 %count, %length
              %equal = call i32 @memcmp(ptr %destination, ptr %expected, i64 %length)
              %same_bytes = icmp eq i32 %equal, 0
              %start = load i8, ptr %bytes
              %endp = getelementptr i8, ptr %destination, i64 %length
              %end = load i8, ptr %endp
              %last = load i8, ptr %lastp
              %start_ok = icmp eq i8 %start, 42
              %end_ok = icmp eq i8 %end, 42
              %last_ok = icmp eq i8 %last, 42
              %edges = and i1 %start_ok, %end_ok
              %canaries = and i1 %edges, %last_ok
              %same = and i1 %same_length, %same_bytes
              %all = and i1 %same, %canaries
              br i1 %all, label %passed, label %failed
            passed:
              %next = add i64 %index, 1
              %done = icmp eq i64 %next,
            """).Append(' ').Append(cases.Count).Append("\n").Append("""
              br i1 %done, label %return, label %loop
            failed:
              %within = icmp ule i64 %count, 24
              %shown = select i1 %within, i64 %count, i64 24
              call void @__kimi_stdout(ptr %destination, i64 %shown, ptr null, i64 0)
              call void @__kimi_stdout(ptr @__kimi_lf, i64 1, ptr null, i64 0)
              call void @__kimi_stdout(ptr %expected, i64 %length, ptr null, i64 0)
              call void @__kimi_exit(i32 91)
              unreachable
            return:
              call void @__kimi_stdout(ptr @float_passed, i64 14, ptr null, i64 0)
              ret void
            }

            """);
        // The oracle reports its completion, so a run that skips it, or runs it twice, fails by its output. It runs first at the process
        // entry (IMPL 21.4.2; DECISIONS R5-M145), and exactly one entry is required.
        const string Entry = "define void @__kimi_start() noreturn #0 {\nentry:\n";
        var complete = NativeAllocationAudit.Instrument(ir.ToString(), Entry, Entry + "  call void @float_audit()\n");
        ScalarEmissionTest.WriteFixture("Utf8FloatOracle", complete, "oracle passed\n");

        // The high word of a binary32 case's operand is noise: the routine reads only the low 32 bits.
        void Add32(uint bits)
            => cases.Add((0xA5A5A5A5_00000000UL | bits, 11, ExactDecimal(bits, 23, 8, 127)));

        void Add64(ulong bits)
            => cases.Add((bits, 12, ExactDecimal(bits, 52, 11, 1023)));
    }

    [Fact]
    public void ExactOracleDistinguishesAdjacentBinaryValues()
    {
        // The host formatter used during development maps these distinct values to the same text.
        Assert.Equal("4.104536801298376e-289", ExactDecimal(292733975779082239, 52, 11, 1023));
        Assert.Equal("4.1045368012983762e-289", ExactDecimal(292733975779082240, 52, 11, 1023));
    }

    private static string ExactDecimal(ulong bits, int mantissaBits, int exponentBits, int bias)
    {
        var signBit = 1UL << (mantissaBits + exponentBits);
        var negative = (bits & signBit) != 0;
        var magnitude = bits & (signBit - 1);
        var fractionMask = (1UL << mantissaBits) - 1;
        var exponentMask = (1UL << exponentBits) - 1;
        if ((magnitude >> mantissaBits) == exponentMask)
        {
            return (magnitude & fractionMask) != 0 ? "NaN" : negative ? "-Infinity" : "Infinity";
        }

        if (magnitude == 0)
        {
            return negative ? "-0" : "0";
        }

        // Midpoints to the adjacent binary values define the nearest-even parsing interval.
        // The conceptual successor of the largest finite value is the next power of two.
        var value = Binary(magnitude);
        var previous = Binary(magnitude - 1);
        var next = Binary(magnitude + 1);
        var common = Math.Min(value.Exponent, Math.Min(previous.Exponent, next.Exponent));
        var center = value.Mantissa << (value.Exponent - common);
        var lower = center + (previous.Mantissa << (previous.Exponent - common));
        var upper = center + (next.Mantissa << (next.Exponent - common));
        center *= 2;
        var binaryPower = common - 1;
        var numerator = binaryPower >= 0 ? center << binaryPower : center;
        var denominator = binaryPower >= 0 ? BigInteger.One : BigInteger.One << -binaryPower;
        var order = numerator.ToString(CultureInfo.InvariantCulture).Length - denominator.ToString(CultureInfo.InvariantCulture).Length;
        while (ComparePower(order) < 0)
        {
            order--;
        }

        while (ComparePower(order + 1) >= 0)
        {
            order++;
        }

        var inclusive = (magnitude & 1) == 0;
        for (var count = 1; count <= (mantissaBits == 23 ? 9 : 17); count++)
        {
            var power = order - count + 1;
            var factor = binaryPower >= 0 ? BigInteger.One << binaryPower : BigInteger.One;
            var divisor = binaryPower >= 0 ? BigInteger.One : BigInteger.One << -binaryPower;
            if (power >= 0)
            {
                divisor *= PowersOfTen[power];
            }
            else
            {
                factor *= PowersOfTen[-power];
            }

            var first = BigInteger.DivRem(lower * factor, divisor, out var loRemainder);
            if (loRemainder != 0 || !inclusive)
            {
                first++;
            }

            var last = BigInteger.DivRem(upper * factor, divisor, out var hiRemainder);
            if (hiRemainder == 0 && !inclusive)
            {
                last--;
            }

            if (first > last)
            {
                continue;
            }

            var nearest = BigInteger.DivRem(center * factor, divisor, out var remainder);
            var comparison = (remainder * 2).CompareTo(divisor);
            if (comparison > 0 || (comparison == 0 && !nearest.IsEven))
            {
                nearest++;
            }

            nearest = BigInteger.Max(first, BigInteger.Min(last, nearest));
            return Notation(nearest.ToString(CultureInfo.InvariantCulture), power, negative);
        }

        throw new InvalidOperationException("No shortest-roundtrip decimal in the binary interval.");

        (BigInteger Mantissa, int Exponent) Binary(ulong pattern)
        {
            var exponent = (int)(pattern >> mantissaBits);
            return (new BigInteger((pattern & fractionMask) | (exponent == 0 ? 0 : 1UL << mantissaBits)), Math.Max(1, exponent) - bias - mantissaBits);
        }

        int ComparePower(int power) => power >= 0 ? numerator.CompareTo(denominator * PowersOfTen[power]) : (numerator * PowersOfTen[-power]).CompareTo(denominator);
    }

    private static string Notation(string digits, int power, bool negative)
    {
        while (digits.EndsWith('0'))
        {
            digits = digits[..^1];
            power++;
        }

        var e = digits.Length + power - 1;
        var text = e is >= -4 and < 16 ? e < 0 ? "0." + new string('0', -e - 1) + digits :
            e + 1 >= digits.Length ? digits.PadRight(e + 1, '0') : digits.Insert(e + 1, ".") :
            digits[..1] + (digits.Length == 1 ? string.Empty : "." + digits[1..]) + "e" + e.ToString(CultureInfo.InvariantCulture);
        return negative ? "-" + text : text;
    }
}
