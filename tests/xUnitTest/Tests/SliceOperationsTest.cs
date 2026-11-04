// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.8.2: the Slice operations tryGet, trySlice, splitAt and trySplitAt are written in Kimigayo; the try-prefixed
/// forms convert only their own bounds failure to None, and every result retains the backing source.</summary>
public class SliceOperationsTest
{
    private const string Operations =
        "let values: [4 of i32] = [10, 20, 30, 40]\nlet s = values[1..]\n" +
        "match s.tryGet(10)\n    .Some(_) => $abort(\"beyond\")\n    .None => Console.writeLine(\"None for 10.\")\n" +
        "match s.tryGet(^1)\n    .Some(let last)\n        require last == 40 else => $abort(\"last\")\n    .None => $abort(\"none\")\n" +
        "match s.tryGet(^0)\n    .Some(_) => $abort(\"end boundary\")\n    .None => Console.writeLine(\"End boundary is not an element.\")\n" +
        "let parts = s.splitAt(1)\nrequire parts.0.length == 1 and parts.0[0] == 20 and parts.1.length == 2 and parts.1[0] == 30 else => $abort(\"split\")\n" +
        "let ends = s.splitAt(^0)\nrequire ends.0.length == 3 and ends.1.isEmpty else => $abort(\"split at end\")\n" +
        "match s.trySplitAt(5)\n    .Some(_) => $abort(\"beyond split\")\n    .None => Console.writeLine(\"None for split 5.\")\n" +
        "match s.trySplitAt(0@u8)\n    .Some(let pair)\n        require pair.0.isEmpty and pair.1.length == 3 else => $abort(\"split 0\")\n    .None => $abort(\"split none\")\n" +
        "match s.trySlice(1..)\n    .Some(let tail)\n        require tail.length == 2 and tail[0] == 30 else => $abort(\"tail\")\n    .None => $abort(\"tail none\")\n" +
        "match s.trySlice(2..=5)\n    .Some(_) => $abort(\"beyond slice\")\n    .None => Console.writeLine(\"None for 2..=5.\")\n" +
        "let r = (1..3).resolve(3)\nmatch s.trySlice(r)\n    .Some(let inner)\n        require inner.length == 2 and inner[1] == 40 else => $abort(\"resolved\")\n    .None => $abort(\"resolved none\")\n" +
        "match s.trySlice((2..5).resolve(5))\n    .Some(_) => $abort(\"beyond resolved\")\n    .None => Console.writeLine(\"None for resolved.\")\n" +
        "let text: [2 of string] = [\"First text.\", \"Last text.\"]\nlet words = text[..]\n" +
        "match words.tryGet(1)\n    .Some(let word) => Console.writeLine(word)\n    .None => $abort(\"word\")\n" +
        "func tryTail<T>(items: Slice<T>) -> Option<Slice<T> during items.source>\n    return items.trySlice(1..)\n" +
        "match tryTail(words)\n    .Some(let rest) => Console.writeLine(rest[0])\n    .None => $abort(\"generic tail\")\n" +
        "match tryTail(words[1..])\n    .Some(let rest)\n        require rest.isEmpty else => $abort(\"tail of one\")\n    .None => $abort(\"tail of one none\")\n" +
        "match tryTail(words[2..])\n    .Some(_) => $abort(\"empty tail\")\n    .None => Console.writeLine(\"None for an empty tail.\")\n" +
        "Console.writeLine(\"ok\")";

    // SPEC 4.8.3, 14.6.2: every mode enumerates the shared elements through the Slice's own entries; a range selection is
    // an owned temporary Subject, and a borrowed Slice selects its entry at the referent.
    private const string Entries =
        "var values: [4 of string] = [\"a\", \"b\", \"c\", \"d\"]\nvar s = values[1..]\nvar n = 0\n" +
        "for v in s => n += 1\nfor v in s@uniq => n += 1\nfor v in s@move => n += 1\nfor v in values[..2] => n += 1\n" +
        "func count(items: ref/Slice<string>) -> i32\n    var k = 0\n    for v in items => k += 1\n    return k\n" +
        "func countUniq(items: uniq/Slice<string>) -> i32\n    var k = 0\n    for v in items => k += 1\n    return k\n" +
        "func generic<C>(items: ref/C) -> i32\n    C is Iterable\n    var k = 0\n    for v in items => k += 1\n    return k\n" +
        "var t = values[..]\nn += count(t@ref) + countUniq(t@uniq) + generic(t@ref)\n" +
        "var it = t.iterate()\nmatch it.next()\n    .Some(let first) => Console.writeLine(first)\n    .None => $abort(\"first\")\n" +
        "var owning = values[3..].intoIterator()\nmatch owning.next()\n    .Some(let last) => Console.writeLine(last)\n    .None => $abort(\"last\")\n" +
        "require n == 23 else => $abort(\"count\")\nConsole.writeLine(values[0])";

    // SPEC 15.6.4 (R4-M170): Entries is valid, but since G264 (S) S2 A5 errs twice at generic's `for` (the lent item's composed erased
    // destruction stores next()'s Loan into the iterator, a self-row; ledger d3c9ea47f1c4), and generation refuses it; the old analysis
    // accepted and ran it to "a\nd\na\n" (fixture SliceEntries). H5's (e) list, row "S2's self-row family" (the owner's O-9, 「(a) 加えて
    // 着地」): PLAN G416 restores the acceptance and re-points this test back to its run.
    [Fact]
    public void EntriesEnumerateEveryMode()
    {
        var c = MinimalEmissionTest.Analyze(Entries);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.False(c.Accepts);
        var loop = MirTargetSide.At("    C is Iterable\n    var k = 0\n    for v in items => k += 1", "for v in items => k += 1");
        MirTargetSide.Check(c, (DiagnosticCode.ComparisonLoanConflict_Kd, loop), (DiagnosticCode.ComparisonLoanConflict_Kd, loop));
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Fact]
    public void Executes()
        => ScalarEmissionTest.EmitFixture("SliceOperations", Operations, "None for 10.\nEnd boundary is not an element.\nNone for split 5.\nNone for 2..=5.\nNone for resolved.\nLast text.\nLast text.\nNone for an empty tail.\nok\n");
}
