// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class GenericIterationEmissionTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void WalksAllIndices(int length)
        => ScalarEmissionTest.EmitFixture(
            "GenericIteration" + length,
            "func count<length N, T>(values: ref/[N of T]) -> isize\n    var count: isize = 0\n    for index in values.indices\n        count = count + 1\n    return count\nlet values: [" + length + " of i32] = [" + string.Join(", ", Enumerable.Repeat("7", length)) + "]\nif count<" + length + ", i32>(values@ref) != " + length + "\n    $abort(\"count\")",
            string.Empty);

    // SPEC 14.6.2, 13.5.5, 15.1.6 (DECISIONS BU-14): a parenthesized binding selects the qualifying pair layer of a Tuple item reached
    // through the entry's reference (a false TypeMismatch_Kd before), shared and exclusive, in the owner and uniq instances.
    [Fact]
    public void PairTupleItemsAreSelectedThroughTheirLayer()
    {
        const string Source = "func total<s/T>(items: ref/Array<s/(i32, i32) during a>, marker: s/T) -> i32\n    s is owner or uniq\n    var sum: i32 = 0\n" +
            "    for (x, y) in items\n        sum += x * 10 + y\n    return sum\n" +
            "func bump<s/T>(items: uniq/Array<s/(i32, i32) during a>, marker: s/T)\n    s is owner or uniq\n    for (x, y) in items\n        x@follow += 100\n" +
            "var p = (1, 2)\nvar q = (3, 4)\nlet m: i32 = 0\nvar mm: i32 = 0\nvar owned: Array<(i32, i32)> = [(5, 6), (7, 8)]\nlet a = total(owned@ref, m)\n" +
            "bump(owned@uniq, m)\nlet c = total(owned@ref, m)\nvar refs: Array<uniq/(i32, i32)> = [p@uniq, q@uniq]\nlet b = total(refs@ref, mm@uniq)\n" +
            "bump(refs@uniq, mm@uniq)\nrequire a == 134 and c == 2134 and b == 46 else => $abort(\"total\")\nConsole.writeLine(\"\\(p.0) \\(q.0)\")";
        ScalarEmissionTest.EmitFixture("GenericIterationPairTuple", Source, "101 103\n");
    }

    [Fact]
    public void SharedScalarReadsRetainSnapshots()
        => ScalarEmissionTest.EmitFixture("GenericIterationSnapshot", "func snapshot<T>(x: T) -> isize\n    var n: isize = 1\n    let value = n + n++\n    return value\nrequire snapshot<i32>(7) == 2 else => $abort(\"snapshot\")", string.Empty);

    [Fact]
    public void SharedAdditionChecksOverflow()
        => ScalarEmissionTest.EmitFixture("GenericIterationOverflow", "func increment<T>(x: T, n: isize) -> isize => n + 1\nlet value = increment<i32>(7, 9223372036854775807)", string.Empty, 1, "Hello.kimi:1:47: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");
}
