// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Numerics;
using System.Text;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.6.2–4.6.4: resolution and application agree with an overflow-free mathematical model. Every integer
/// Type and FromEnd, Start and End resolve against lengths 0, 3 and isize.MaxValue; every range shape written at a
/// selection, saved, or passed to trySlice selects the model's interval; and invalid shapes Abort in one check.</summary>
public class PositionModelTest
{
    // Every range shape over every length 0-4 and boundary -1..L+1: written at the selection, saved, and passed to
    // trySlice, each selects the model's interval, and trySlice is None exactly when the model has none.
    private const string Shapes =
        "func model(s: isize, e: isize, closed: bool, length: isize) -> (bool, isize, isize)\n" +
        "    if s < 0 or s > length => return (false, 0, 0)\n" +
        "    if closed\n        if e < 0 or e >= length or s > e => return (false, 0, 0)\n        return (true, s, e + 1)\n" +
        "    if e < 0 or e > length or s > e => return (false, 0, 0)\n    return (true, s, e)\n" +
        "func same(view: Slice<i32>, m: (bool, isize, isize)) -> bool\n    if view.length != m.2 - m.1 => return false\n    if view.length == 0 => return true\n    return view[0] == m.1@i32 * 10\n" +
        "func tried(result: Option<Slice<i32>>, m: (bool, isize, isize)) -> bool\n    match result\n        .Some(let slice)\n            return m.0 and same(slice@follow, m)\n        .None\n            return not m.0\n" +
        "func check(ok: bool, code: i32)\n    if not ok\n        Console.writeLine(\"case \\(code)\")\n        $abort(\"model\")\n" +
        "var checks = 0\nvar length: isize = 0\nwhile length <= 4\n" +
        "    var values: Array<i32> = []\n    var n: isize = 0\n    while n < length\n        values.append(n@i32 * 10)\n        n += 1\n" +
        "    let view = values[..]\n    var a: isize = -1\n    while a <= length + 1\n        var b: isize = -1\n        while b <= length + 1\n" +
        "            var m = model(a, b, false, length)\n            if m.0\n                let saved = a..b\n                check(same(values[a..b], m) and same(values[saved], m), 1)\n" +
        "            check(tried(view.trySlice(a..b), m), 2)\n" +
        "            m = model(a, b, true, length)\n            if m.0\n                let saved = a..=b\n                check(same(values[a..=b], m) and same(values[saved], m), 3)\n" +
        "            check(tried(view.trySlice(a..=b), m), 4)\n" +
        "            if a >= 0 and b >= 0\n                m = model(length - a, length - b, false, length)\n                if m.0\n                    let saved = ^a..^b\n                    check(same(values[^a..^b], m) and same(values[saved], m), 5)\n" +
        "                check(tried(view.trySlice(^a..^b), m), 6)\n" +
        "            if b >= 0\n                m = model(a, length - b, true, length)\n                if m.0\n                    let saved = a..=^b\n                    check(same(values[a..=^b], m) and same(values[saved], m), 7)\n" +
        "                check(tried(view.trySlice(a..=^b), m), 8)\n" +
        "            checks += 1\n            b += 1\n" +
        "        var m = model(a, length, false, length)\n        if m.0\n            check(same(values[a..], m), 9)\n        check(tried(view.trySlice(a..), m), 10)\n" +
        "        m = model(0, a, false, length)\n        if m.0\n            check(same(values[..a], m), 11)\n        check(tried(view.trySlice(..a), m), 12)\n" +
        "        m = model(0, a, true, length)\n        if m.0\n            check(same(values[..=a], m), 13)\n        check(tried(view.trySlice(..=a), m), 14)\n" +
        "        let element = model(a, a, true, length)\n        if element.0\n            check(values[a] == a@i32 * 10 and view[a] == a@i32 * 10, 15)\n" +
        "        match view.tryGet(a)\n            .Some(let found)\n                check(element.0 and found == a@i32 * 10, 16)\n            .None\n                check(not element.0, 17)\n" +
        "        a += 1\n    check(same(values[..], (true, 0, length)), 18)\n    length += 1\n" +
        "Console.writeLine(\"\\(checks) pairs\")";

    // SPEC 4.6.8: constant positions and ranges on a fixed array, resolved at compile time.
    private const string Folding =
        "let values: [4 of i32] = [1, 2, 3, 4]\nlet a = values[^1]\nlet s = values[1..^1]\nlet c = values[..=2]\nlet t = values[^2..]\nlet u = values[1@u8..3@i64]\n" +
        "require a == 4 and s.length == 2 and c.length == 3 and t.length == 2 and u.length == 2 else => $abort(\"fold\")\nConsole.writeLine(\"ok\")";

    private static readonly (string Name, BigInteger Min, BigInteger Max)[] Integers =
    [
        ("i8", sbyte.MinValue, sbyte.MaxValue), ("u8", 0, byte.MaxValue), ("i16", short.MinValue, short.MaxValue), ("u16", 0, ushort.MaxValue),
        ("i32", int.MinValue, int.MaxValue), ("u32", 0, uint.MaxValue), ("i64", long.MinValue, long.MaxValue), ("u64", 0, ulong.MaxValue),
        ("i128", (BigInteger)Int128.MinValue, (BigInteger)Int128.MaxValue), ("u128", 0, (BigInteger)UInt128.MaxValue),
        ("isize", long.MinValue, long.MaxValue), ("usize", 0, ulong.MaxValue),
    ];

    private static readonly BigInteger[] Lengths = [0, 3, long.MaxValue];

    // A per-Type function tries each value as an integer position and as a from-end offset against each length and compares
    // with the model: an integer n resolves to n when 0 <= n <= L, and ^a to L - a when 0 <= a <= L.
    [Fact]
    public void PositionsResolveAsTheModel()
    {
        var source = new StringBuilder();
        source.Append("func expect<P>(p: P, length: isize, valid: bool, value: isize) -> bool\n    P is Position\n    match p.tryResolve(length)\n        .Some(let q) => return valid and q == value\n        .None => return not valid\n");
        var checks = 0;
        foreach (var (name, min, max) in Integers)
        {
            var values = new List<BigInteger> { 0, 3, 4, max };
            if (min < 0)
            {
                values.Add(-1);
                values.Add(min);
            }

            foreach (var length in Lengths)
            {
                values.Add(length);
                values.Add(length + 1);
            }

            source.Append(CultureInfo.InvariantCulture, $"func check{name}() -> bool\n    var ok = true\n");
            var index = 0;
            foreach (var value in values.Distinct())
            {
                if (value < min || value > max)
                {
                    continue;
                }

                source.Append(CultureInfo.InvariantCulture, $"    let v{index}: {name} = {value}\n");
                foreach (var length in Lengths)
                {
                    var valid = value >= 0 && value <= length;
                    source.Append(CultureInfo.InvariantCulture, $"    ok = ok and expect(v{index}, {length}@isize, {Bool(valid)}, {(valid ? value : 0)}@isize)\n");
                    source.Append(CultureInfo.InvariantCulture, $"    ok = ok and expect(^v{index}, {length}@isize, {Bool(valid)}, {(valid ? length - value : 0)}@isize)\n");
                    checks += 2;
                }

                index++;
            }

            source.Append("    return ok\n");
        }

        source.Append("let whole = ..\nlet reach = whole.start\nlet limit = whole.end\n");
        source.Append("require expect(reach, 0, true, 0) and expect(reach, 9223372036854775807@isize, true, 0) and expect(reach, -1, false, 0) else => $abort(\"Start\")\n");
        source.Append("require expect(limit, 0, true, 0) and expect(limit, 9223372036854775807@isize, true, 9223372036854775807@isize) and expect(limit, -1, false, 0) and expect(3, -1, false, 0) and expect(^0, -1, false, 0) else => $abort(\"End\")\n");
        source.Append("require ");
        source.Append(string.Join(" and ", Integers.Select(x => $"check{x.Name}()")));
        source.Append(CultureInfo.InvariantCulture, $" else => $abort(\"integers\")\nConsole.writeLine(\"{checks} checks\")");
        ScalarEmissionTest.EmitFixture("PositionModelResolution", source.ToString(), $"{checks} checks\n");
    }

    [Fact]
    public void EveryShapeSelectsTheModelInterval()
        => ScalarEmissionTest.EmitFixture("PositionModelShapes", Shapes, "135 pairs\n");

    // Invalid shapes Abort in the selection's one check, at the selection, for a fixed array and an Array.
    [Theory]
    [InlineData("FixedStartPast", "fixed", "[4..]")]
    [InlineData("FixedEndPast", "fixed", "[..4]")]
    [InlineData("FixedReversed", "fixed", "[2..1]")]
    [InlineData("FixedClosedEnd", "fixed", "[0..=3]")]
    [InlineData("FixedClosedFromEnd", "fixed", "[..=^0]")]
    [InlineData("FixedFromEndPast", "fixed", "[^4..]")]
    [InlineData("FixedNegativeOffset", "fixed", "[..^(-1)]")]
    [InlineData("FixedElementEnd", "fixed", "[3]")]
    [InlineData("FixedElementFromEnd", "fixed", "[^0]")]
    [InlineData("FixedElementFromEndPast", "fixed", "[^4]")]
    [InlineData("ArrayNegativeStart", "array", "[(-1)..]")]
    [InlineData("ArrayClosedReversed", "array", "[2..=0]")]
    [InlineData("ArrayFromEndReversed", "array", "[^1..^2]")]
    [InlineData("ArrayElementNegative", "array", "[-1]")]
    [InlineData("ArrayElementFromEndNegative", "array", "[^(-1)]")]
    [InlineData("ArrayWideElement", "array", "[18446744073709551615@u64]")]
    [InlineData("ArrayWideFromEnd", "array", "[^(18446744073709551615@u64)]")]
    [InlineData("ArrayWideStart", "array", "[(170141183460469231731687303715884105727@i128)..]")]
    public void InvalidShapesAbortAtTheSelection(string name, string receiver, string selection)
        => ScalarEmissionTest.EmitFixture(
            "PositionModelAbort" + name,
            "let fixed: [3 of i32] = [1, 2, 3]\nlet array: Array<i32> = [1, 2, 3]\nConsole.writeLine(\"before\")\nlet bad = " + receiver + selection + "\nConsole.writeLine(\"after\")",
            "before\n",
            1,
            "Hello.kimi:4:11: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    // SPEC 4.6.8: constant positions and ranges that resolve against a fixed array's length are resolved at compile time:
    // no from-end subtraction and no runtime check remain, while a failing constant keeps its check (see the Abort cases).
    [Fact]
    public void ConstantPositionsOnFixedArraysFold()
    {
        var ir = ScalarEmissionTest.EmitFixture("PositionModelFolding", Folding, "ok\n");
        Assert.DoesNotContain("%reversed", ir, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"%invalid[0-9]", ir);
        Assert.DoesNotContain("= sub i64 4, ", ir, StringComparison.Ordinal);
        Assert.DoesNotContain(" = sext i32 ", ir, StringComparison.Ordinal);
        Assert.True(ir.Split("br i1 false,").Length - 1 >= 4, ir);
    }

    private static string Bool(bool value) => value ? "true" : "false";
}
