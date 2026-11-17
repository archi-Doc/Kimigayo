// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.6.2–4.6.3: two boundaries of one integer Type construct an iterable Range&lt;T, T&gt; or ClosedRange&lt;T, T&gt;;
/// construction checks nothing, iteration Aborts on a reversed range, and resolution and slicing check boundaries against a
/// length, with one check for a range applied directly.</summary>
public class IntegerRangeTest
{
    private const string Iteration =
        "var sum: i32 = 0\nfor var x in -3..3\n    sum += x\n    x += 100\nrequire sum == -3 else => $abort(\"half-open\")\n" +
        "var count: i32 = 0\nfor x in 1..=3 => count += x\nrequire count == 6 else => $abort(\"closed\")\n" +
        "var last: u8 = 0\nfor b in 250@u8..=255 => last = b\nrequire last == 255 else => $abort(\"maximum\")\n" +
        "var steps = 0\nfor _ in 5..5 => steps += 1\nrequire steps == 0 else => $abort(\"empty\")\nfor _ in 7..=7 => steps += 1\nrequire steps == 1 else => $abort(\"single\")\n" +
        "let saved = 0@i64..4\nvar total: i64 = 0\nfor v in saved => total += v\nfor v in saved => total += v\nrequire total == 12 else => $abort(\"reuse\")\n" +
        "let n = 10\nlet d = 0..n@ref\nvar seen = 0\nfor v in d => seen = v\nrequire seen == 9 else => $abort(\"value read\")\n" +
        "let e = [0..3, 5..8@i64]\nrequire e[1].start == 5 and e[1].end == 8 and e[0].end == 3 else => $abort(\"array\")\n" +
        "let reversed = 3..1\nrequire reversed.start == 3 and reversed.end == 1 else => $abort(\"reversed value\")\n" +
        "require (0..3) != (0..4) and (0..3) == (0..3) and saved == (0..4) and (0..=2) == (0..=2) else => $abort(\"equality\")\n" +
        "let mixed = 0@i32..2@i64\nrequire mixed.start == 0 and mixed.end == 2 else => $abort(\"mixed\")\n" +
        "Console.writeLine(\"ok\")";

    private const string Slicing =
        "let values: [5 of i32] = [10, 20, 30, 40, 50]\nlet a: i32 = 1\nlet b: i32 = 3\n" +
        "let s = values[a..b]\nrequire s.length == 2 and s[0] == 20 else => $abort(\"direct\")\n" +
        "let t = values[a..]\nrequire t.length == 4 and t[0] == 20 else => $abort(\"from\")\n" +
        "let u = values[a..^1]\nrequire u.length == 3 and u[2] == 40 else => $abort(\"from end\")\n" +
        "let w = values[^b..]\nrequire w.length == 3 and w[0] == 30 else => $abort(\"from end start\")\n" +
        "let r: Range<isize, isize> = 1..4\nlet x = values[r]\nrequire x.length == 3 and x[0] == 20 else => $abort(\"saved\")\n" +
        "let closed = values[1..=3]\nrequire closed.length == 3 and closed[2] == 40 else => $abort(\"closed\")\n" +
        "let lastTwo = values[^2..=^1]\nrequire lastTwo.length == 2 and lastTwo[0] == 40 else => $abort(\"closed from end\")\n" +
        "let big: u64 = 2\nlet y = values[big..4]\nrequire y.length == 2 and y[1] == 40 else => $abort(\"unsigned\")\n" +
        "let z = values[1@u8..^(1@i64)]\nrequire z.length == 3 else => $abort(\"mixed boundaries\")\n" +
        "let resolved = (1..3).resolve(values.length)\nrequire resolved.start == 1 and resolved.end == 3 else => $abort(\"resolve\")\n" +
        "match (3..1).tryResolve(5)\n    .Some(_) => $abort(\"reversed\")\n    .None => Console.writeLine(\"Reversed refused.\")\n" +
        "match (-1..2).tryResolve(5)\n    .Some(_) => $abort(\"negative\")\n    .None => Console.writeLine(\"Negative refused.\")\n" +
        "match (0..=4).tryResolve(4)\n    .Some(_) => $abort(\"closed end\")\n    .None => Console.writeLine(\"Closed end refused.\")\n" +
        "match values[..].trySlice(-3..3)\n    .Some(_) => $abort(\"try negative\")\n    .None => Console.writeLine(\"Try negative refused.\")\n" +
        "match values[..].trySlice(3..1)\n    .Some(_) => $abort(\"try reversed\")\n    .None => Console.writeLine(\"Try reversed refused.\")\n" +
        "match values[..].trySlice(2..=4)\n    .Some(let found) => require found.length == 3 and found[0] == 30 else => $abort(\"try closed\")\n    .None => $abort(\"try none\")\n" +
        "Console.writeLine(\"ok\")";

    private const string Generic =
        "func sum<T>(values: Range<T, T>) -> T\n    T is PrimitiveInteger\n    var total: T = 0\n    for value in values\n        total += value\n    return total\n" +
        "func tryWindow<T, I>(values: Slice<T> ! start: I, end: I) -> Option<Slice<T> during values.source>\n    I is PrimitiveInteger\n    return values.trySlice(start..end)\n" +
        "let data: [4 of i32] = [1, 2, 3, 4]\n" +
        "require sum(1..5@u8) == 10 and sum(0..1000) == 499500 and sum(-2..3@i64) == 0 else => $abort(\"sum\")\n" +
        "match tryWindow(data[..], start: 1@u16, end: 3)\n    .Some(let w) => require w.length == 2 and w[0] == 2 else => $abort(\"window\")\n    .None => $abort(\"window none\")\n" +
        "match tryWindow(data[..], start: -1, end: 2)\n    .Some(_) => $abort(\"window negative\")\n    .None => Console.writeLine(\"Window refused.\")\n" +
        "Console.writeLine(\"ok\")";

    // SPEC 4.6.4: a ResolvedRange's entries return RangeIterator<isize> over its half-open interval, also through a
    // generic Iterable and an owning adapter.
    private const string Resolved =
        "func count<S>(items: S) -> i32\n    S is Iterable\n    var n: i32 = 0\n    for _ in items\n        n += 1\n    return n\n" +
        "let values: [5 of i32] = [1, 2, 3, 4, 5]\nlet r = (1..4).resolve(values.length)\n" +
        "var sum: isize = 0\nfor i in Kimi.Iteration.owning(r.intoIterator()) => sum += i\nrequire sum == 6 else => $abort(\"owning\")\n" +
        "var shared = r.iterate()\nmatch shared.next()\n    .Some(let first) => require first == 1 else => $abort(\"first\")\n    .None => $abort(\"empty\")\n" +
        "require count(values.indices) == 5 and count(r) == 3 and count((2..2).resolve(5)) == 0 and count(0..4) == 4 and count(0..=4) == 5 else => $abort(\"generic\")\n" +
        "Console.writeLine(\"ok\")";

    // SPEC 4.6.4: a closed range yields its end last and never computes past it, also at the maximum of its Type; the
    // iterator iterates a half-open state unless the end is that maximum (measured design, former P41 U6; current P30).
    private const string ClosedEnds =
        "func check<T>(first: T, last: T, count: i32) -> bool\n    T is PrimitiveInteger\n    var n: i32 = 0\n    var seen: T = first\n    for v in first..=last\n        seen = v\n        n += 1\n    return n == count and seen == last\n" +
        "require check(120@i8, 127@i8, 8) and check(-128@i8, -126@i8, 3) and check(250@u8, 255@u8, 6) else => $abort(\"8\")\n" +
        "require check(65534@u16, 65535@u16, 2) and check(32767@i16, 32767@i16, 1) else => $abort(\"16\")\n" +
        "require check(2147483646, 2147483647, 2) and check(4294967295@u32, 4294967295@u32, 1) else => $abort(\"32\")\n" +
        "require check(9223372036854775806@i64, 9223372036854775807@i64, 2) and check(18446744073709551614@u64, 18446744073709551615@u64, 2) else => $abort(\"64\")\n" +
        "require check(9223372036854775807@isize, 9223372036854775807@isize, 1) and check(18446744073709551615@usize, 18446744073709551615@usize, 1) else => $abort(\"size\")\n" +
        "require check(170141183460469231731687303715884105727@i128, 170141183460469231731687303715884105727@i128, 1) and check(340282366920938463463374607431768211455@u128, 340282366920938463463374607431768211455@u128, 1) else => $abort(\"128\")\n" +
        "require check(0@u8, 0@u8, 1) and check(-1@i64, 1@i64, 3) and check(126@i8, 126@i8, 1) and check(254@u8, 254@u8, 1) else => $abort(\"near\")\n" +
        "var it = (3..=5).iterate()\nvar total = 0\nloop\n    match it.next()\n        .Some(let v) => total += v\n        .None => exit\nmatch it.next()\n    .Some(_) => $abort(\"exhausted\")\n    .None => ()\nrequire total == 12 else => $abort(\"explicit\")\n" +
        "Console.writeLine(\"ok\")";

    // SPEC 4.6.4, 14.6.2: a ResolvedRange loop (its Shared entry) and an owning adapter over its intoIterator run the same positions
    // over every interval of a small target.
    private const string ResolvedCursor =
        "var checked = 0\nvar start: isize = 0\nwhile start <= 6\n    var end = start\n    while end <= 6\n" +
        "        let r = (start..end).resolve(6)\n        var cursor: isize = 0\n        var cursorCount = 0\n        for i in r\n            cursor = cursor * 7 + i\n            cursorCount += 1\n" +
        "        var entry: isize = 0\n        var entryCount = 0\n        for i in Kimi.Iteration.owning(r.intoIterator())\n            entry = entry * 7 + i\n            entryCount += 1\n" +
        "        require cursor == entry and cursorCount == entryCount and cursorCount@isize == r.length else => $abort(\"cursor\")\n        checked += 1\n        end += 1\n    start += 1\n" +
        "require checked == 28 else => $abort(\"count\")\nConsole.writeLine(\"ok\")";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "ClosedEnds", ClosedEnds, "ok\n" },
        { "ResolvedCursor", ResolvedCursor, "ok\n" },
        { "Iteration", Iteration, "ok\n" },
        { "Slicing", Slicing, "Reversed refused.\nNegative refused.\nClosed end refused.\nTry negative refused.\nTry reversed refused.\nok\n" },
        { "Generic", Generic, "Window refused.\nok\n" },
    };

    // R8-W3's coverage table (labels from SPEC 14.6.1, 14.6.2, 4.6.2-4.6.4, 15.1.5 and 15.1.6, written before any run), by table id: a
    // ResolvedRange `for` in each Subject mode, place, producer and transfer, and its Range twin (suffix r) on the same Iterable path, with
    // the same output. Not here: v9x (the parser's), v10y and v10v (the parser's ForBinding, PLAN F1), v14 and its twins (PLAN G416,
    // AResolvedRangeThroughAGenericIterableIsRefusedPendingG416) and r7 (resolve's abort code, PLAN F2). b4 and b4r are the Middle's
    // consent rows (C-338): the valid neighbours of the rejected b2x.
    public static TheoryData<string, string, string> CoverageRows => new()
    {
        { "v1", "let values: [5 of i32] = [10, 20, 30, 40, 50]\nvar total: isize = 0\nvar weighted: i32 = 0\nfor i in values.indices\n    total += i\n    weighted += values[i]\nConsole.writeLine(\"\\(total) \\(weighted)\")\n", "10 150\n" }, // valid 14.6.2 ByValue temporary
        { "v2", "let values: [5 of i32] = [1, 2, 3, 4, 5]\nlet r = (1..4).resolve(values.length)\nvar a: isize = 0\nfor i in r => a += i\nfor i in r => a += i * 10\nConsole.writeLine(\"\\(a) \\(r.start) \\(r.end)\")\n", "66 1 4\n" }, // valid 14.6.2 Shared variable, reused
        { "v3", "var r = (0..3).resolve(3)\nvar a: isize = 0\nvar s: isize = 0\nfor i in r@uniq\n    a = a * 10 + i\n    s += r.start\nConsole.writeLine(\"\\(a) \\(s)\")\n", "12 0\n" }, // valid 14.6.2 Exclusive @uniq; body reads the Subject
        { "v3r", "var q = 0..3\nvar a = 0\nvar s = 0\nfor i in q@uniq\n    a = a * 10 + i\n    s += q.start\nConsole.writeLine(\"\\(a) \\(s)\")\n", "12 0\n" }, // valid 14.6.2 Range Exclusive @uniq; body reads the Subject (the Iterable path today)
        { "v4", "let r = (2..5).resolve(5)\nvar a: isize = 0\nfor i in r@copy => a += i\nfor i in r@move => a += i\nConsole.writeLine(\"\\(a)\")\n", "18\n" }, // valid 14.6.2 ByValue @copy and @move
        { "v5", "let r = (1..3).resolve(3)\nlet w = r@ref\nvar a: isize = 0\nfor i in w => a += i\nfor i in r@ref => a += i\nConsole.writeLine(\"\\(a)\")\n", "6\n" }, // valid 14.6.2 Shared ref value and @ref
        { "v5r", "let q = 1..3\nlet w = q@ref\nvar a = 0\nfor i in w => a += i\nfor i in q@ref => a += i\nConsole.writeLine(\"\\(a)\")\n", "6\n" }, // valid 14.6.2 Range: Shared ref value and @ref (the Iterable path today)
        { "v6", "var r = (1..3).resolve(3)\nlet u = r@uniq\nvar a: isize = 0\nfor i in u => a += i\nConsole.writeLine(\"\\(a)\")\n", "3\n" }, // valid 14.6.2 Exclusive uniq value
        { "v6r", "var q = 1..3\nlet u = q@uniq\nvar a = 0\nfor i in u => a += i\nConsole.writeLine(\"\\(a)\")\n", "3\n" }, // valid 14.6.2 Range: Exclusive uniq value (the Iterable path today)
        { "v7", "var n = 0\nfor _ in (2..2).resolve(5) => n += 1\nfor _ in (0..0).resolve(0) => n += 1\nfor _ in (1..4).resolve(4) => n += 1\nConsole.writeLine(\"\\(n)\")\n", "3\n" }, // valid 4.6.4 empty; 14.6.1 `_` slot
        { "v8", "let r = (0..3).resolve(3)\nvar seen: isize = 0\nfor var i in r\n    i += 100\n    seen += i\nConsole.writeLine(\"\\(seen)\")\n", "303\n" }, // valid 14.6.1 var slot
        { "v11", "func firstOver(values: ref/[6 of i32], limit: i32) -> isize\n    for i in values.indices\n        if values[i] > limit => return i\n    return -1\nlet values: [6 of i32] = [3, 9, 1, 7, 12, 5]\nvar sum: isize = 0\nfor i in values.indices\n    if i == 1 => continue\n    if i == 4 => exit\n    sum += i\nvar pairs = 0\nlabel outer: for a in values.indices\n    for b in values.indices\n        if b > a => continue to outer\n        if a == 4 => exit to outer\n        pairs += 1\nConsole.writeLine(\"\\(sum) \\(pairs) \\(firstOver(values@ref, 8)) \\(firstOver(values@ref, 100))\")\n", "5 10 1 -1\n" }, // valid 14.6.2 exit/continue/labelled/return
        { "v13", "struct Holder\n    public let range: ResolvedRange\n    public init(range: ResolvedRange) => self.range = range\nfunc sumOf(h: ref/Holder) -> isize\n    var a: isize = 0\n    for i in h.range => a += i\n    return a\nlet h = Holder.init((1..4).resolve(4))\nvar b: isize = 0\nfor i in h.range => b += i\nConsole.writeLine(\"\\(b) \\(sumOf(h@ref))\")\n", "6 6\n" }, // valid 14.6.2 Field Subject (owned and ref/ paths)
        { "v15", "var r = (0..3).resolve(3)\nvar a: isize = 0\nfor i in r\n    a = a * 10 + i\n    r = (5..6).resolve(6)\nConsole.writeLine(\"\\(a) \\(r.start)\")\n", "12 5\n" }, // valid 4.6.4 Subject reassigned in the body
        { "v15r", "var q = 0..3\nvar a = 0\nfor i in q\n    a = a * 10 + i\n    q = 5..6\nConsole.writeLine(\"\\(a) \\(q.start)\")\n", "12 5\n" }, // valid 4.6.4 Range Subject reassigned in the body (the Iterable path today)
        { "v16", "var values: [4 of i32] = [1, 2, 3, 4]\nfor i in values.indices\n    values[i] = values[i] * 2\nvar list: Array<i32> = [1, 2, 3]\nfor i in list.indices\n    list[i] = list[i] + 1\n    list.append(0)\nConsole.writeLine(\"\\(values[0]) \\(values[3]) \\(list.length) \\(list[0]) \\(list[2])\")\n", "2 8 6 2 4\n" }, // valid 4.6.4 indices loop mutating its sequence
        { "v17", "let n: isize = 9223372036854775807\nlet r = ((n - 2)..n).resolve(n)\nvar count = 0\nvar last: isize = 0\nfor i in r\n    count += 1\n    last = i\nConsole.writeLine(\"\\(count) \\(last == n - 1)\")\n", "2 true\n" }, // valid 4.6.4 interval ending at the isize maximum
        { "v18", "let values: [5 of i32] = [4, 8, 15, 16, 23]\nlet copy = values[1..].toArray()\nvar list: Array<i32> = [1]\nlist.appendCopies(values[..2])\nlet target: i32 = 15\nmatch values[..].firstIndex(of: target)\n    .Some(let at) => Console.writeLine(\"\\(copy.length) \\(copy[0]) \\(list.length) \\(list[2]) \\(at)\")\n    .None => $abort(\"none\")\n", "4 8 3 8 2\n" }, // valid library bodies appendCopies/firstIndex
        { "v19", "let ranges: [2 of ResolvedRange] = [(0..2).resolve(2), (1..3).resolve(3)]\nvar a: isize = 0\nfor r in ranges\n    for i in r => a += i\nConsole.writeLine(\"\\(a)\")\n", "4\n" }, // valid 14.6.2 ref/ResolvedRange items
        { "r1", "var r = (0..3).resolve(3)\nvar a: isize = 0\nfor i in r@uniq\n    a = a * 10 + i\n    r = (5..6).resolve(6)\nConsole.writeLine(\"\\(a) \\(r.start)\")\n", "12 5\n" }, // valid 14.6.2 Exclusive @uniq; body assigns the Subject (4.6.4 no Loan)
        { "r1r", "var q: Range<isize, isize> = 0..3\nvar a: isize = 0\nfor i in q@uniq\n    a = a * 10 + i\n    q = 5..6\nConsole.writeLine(\"\\(a) \\(q.start)\")\n", "12 5\n" }, // valid 14.6.2 Range twin of r1
        { "r1b", "func peek(r: uniq/ResolvedRange) -> isize => r.start\nvar r = (1..3).resolve(3)\nvar a: isize = 0\nfor i in r => a += i + peek(r@uniq)\nConsole.writeLine(\"\\(a)\")\n", "5\n" }, // valid 14.6.2 Shared; body lends the Subject @uniq (4.6.4 no Loan)
        { "r1br", "func peek(q: uniq/Range<isize, isize>) -> isize => q.start\nvar q: Range<isize, isize> = 1..3\nvar a: isize = 0\nfor i in q => a += i + peek(q@uniq)\nConsole.writeLine(\"\\(a)\")\n", "5\n" }, // valid 14.6.2 Range twin of r1b
        { "r2", "var r = (1..3).resolve(3)\nlet u = r@uniq\nvar a: isize = 0\nfor i in u => a += i\nfor i in u => a += i\nConsole.writeLine(\"\\(a) \\(u.start)\")\n", "6 1\n" }, // valid 15.1.6/14.6.2 a uniq/ResolvedRange local iterated twice, read after
        { "r2r", "var q: Range<isize, isize> = 1..3\nlet u = q@uniq\nvar a: isize = 0\nfor i in u => a += i\nfor i in u => a += i\nConsole.writeLine(\"\\(a) \\(u.start)\")\n", "6 1\n" }, // valid Range twin of r2
        { "r3", "func viaRef(r: ref/ResolvedRange) -> isize\n    var a: isize = 0\n    for i in r => a += i\n    for i in r => a += i\n    return a + r.start\nfunc viaUniq(r: uniq/ResolvedRange) -> isize\n    var a: isize = 0\n    for i in r => a += i\n    for i in r => a += i\n    return a + r.end\nvar r = (1..3).resolve(3)\nConsole.writeLine(\"\\(viaRef(r@ref)) \\(viaUniq(r@uniq))\")\n", "7 9\n" }, // valid 14.6.2 ref/ and uniq/ ResolvedRange parameters iterated twice
        { "r3r", "func viaRef(r: ref/Range<isize, isize>) -> isize\n    var a: isize = 0\n    for i in r => a += i\n    for i in r => a += i\n    return a + r.start\nfunc viaUniq(r: uniq/Range<isize, isize>) -> isize\n    var a: isize = 0\n    for i in r => a += i\n    for i in r => a += i\n    return a + r.end\nvar r: Range<isize, isize> = 1..3\nConsole.writeLine(\"\\(viaRef(r@ref)) \\(viaUniq(r@uniq))\")\n", "7 9\n" }, // valid Range twin of r3
        { "r4", "func sumIdx<T>(values: Slice<T>, r: ResolvedRange) -> isize\n    var a: isize = 0\n    for i in values.indices => a += i\n    for i in r => a += i * 10\n    return a\nlet v: [4 of i32] = [1, 2, 3, 4]\nConsole.writeLine(\"\\(sumIdx(v[..], (1..3).resolve(3)))\")\n", "36\n" }, // valid 14.6.2 ResolvedRange loops (indices, a parameter) in a generic body
        { "r4r", "func sumIdx<T>(values: Slice<T>, r: Range<isize, isize>) -> isize\n    var a: isize = 0\n    for i in 0..values.length => a += i\n    for i in r => a += i * 10\n    return a\nlet v: [4 of i32] = [1, 2, 3, 4]\nConsole.writeLine(\"\\(sumIdx(v[..], 1..3))\")\n", "36\n" }, // valid Range twin of r4
        { "r5", "let values: [5 of i32] = [1, 2, 3, 4, 5]\nvar a: isize = 0\nfor i in (1..=3).resolve(5) => a += i\nmatch (1..3).tryResolve(5)\n    .Some(let r)\n        for i in r => a += i * 10\n    .None => $abort(\"none\")\nlet o = (0..2).tryResolve(5)\nmatch o\n    .Some(let r)\n        for i in r => a += i * 100\n    .None => $abort(\"none\")\nlet rr = (0..2).resolve(2)\nfor i in rr.resolve(5) => a += i * 1000\nfor i in values[2..].indices => a += i * 10000\nConsole.writeLine(\"\\(a)\")\n", "31136\n" }, // valid 4.6.2/4.6.3 each producer; 15.1.6 a ref/ResolvedRange match binding
        { "r6", "var arr: [3 of i32] = [1, 2, 3]\nvar us = arr.sliceUniq()\nfor i in us.indices\n    us[i] = us[i] * 2\nConsole.writeLine(\"\\(arr[0]) \\(arr[2])\")\n", "2 6\n" }, // valid 15.1.6 UniqSlice's computed indices (library producer)
        { "r6r", "var arr: [3 of i32] = [1, 2, 3]\nvar us = arr.sliceUniq()\nfor i in 0..us.length\n    us[i] = us[i] * 2\nConsole.writeLine(\"\\(arr[0]) \\(arr[2])\")\n", "2 6\n" }, // valid Range twin of r6
        { "b4", "func f(u: uniq/ResolvedRange) -> isize\n    let s = u@follow@ref\n    let e = s.end\n    var a: isize = 0\n    for i in u => a += i + u.start\n    return a + e\nvar r = (1..3).resolve(3)\nConsole.writeLine(\"\\(f(r@uniq))\")\n", "8\n" }, // valid 15.1.6/15.6 b2x's neighbour: the shared Reborrow dead before the Exclusive loop; the body reads u (4.6.4 no Loan)
        { "b4r", "func f(u: uniq/Range<isize, isize>) -> isize\n    let s = u@follow@ref\n    let e = s.end\n    var a: isize = 0\n    for i in u => a += i + u.start\n    return a + e\nvar r: Range<isize, isize> = 1..3\nConsole.writeLine(\"\\(f(r@uniq))\")\n", "8\n" }, // valid Range twin of b4
    };

    // The invalid rows of the same table and their Range twins: each record's code and position. The Middle's consent rows (C-338; labels
    // from SPEC 15.1.6 and 15.6): a3x, an explicit `r@uniq` Subject while a shared Loan of `r` is live, reports as its Range twin does, at
    // the `for` (on the counted loop: ComparisonLoanConflict_Kd at the `@uniq`); b2x, b5x and b6x, a Subject storing uniq/ResolvedRange
    // (Exclusive: its entry acquires the referent) while a shared Reborrow of the referent stays live across the loop, which the counted
    // loop's referent read accepted.
    public static TheoryData<string, string, string> RejectedCoverageRows => new()
    {
        { "v3x", "let r = (0..3).resolve(3)\nfor i in r@uniq => ()\n", "InvalidAssignment_Kd 2:10" }, // invalid 15.1.5 @uniq of a let Subject
        { "rx3", "let q: Range<isize, isize> = 0..3\nfor i in q@uniq => ()\n", "InvalidAssignment_Kd 2:10" }, // invalid Range twin of v3x (@uniq of a let)
        { "v8x", "let r = (0..3).resolve(3)\nfor i in r\n    i += 1\n", "InvalidAssignment_Kd 3:5" }, // invalid 14.6.1 immutable slot assigned
        { "rx8", "let q: Range<isize, isize> = 0..3\nfor i in q\n    i += 1\n", "InvalidAssignment_Kd 3:5" }, // invalid Range twin of v8x (immutable slot assigned)
        { "v10x", "let r = (0..3).resolve(3)\nfor (a, b) in r => ()\n", "TypeMismatch_Kd 2:1" }, // invalid 14.6.2 pair binding of an isize item
        { "rx10", "let q: Range<isize, isize> = 0..3\nfor (a, b) in q => ()\n", "TypeMismatch_Kd 2:1" }, // invalid Range twin of v10x (pair binding of an isize item)
        { "v12x", "let r = (0..2).resolve(2)\nfor i in r\n    let x: i32 = i\n", "TypeMismatch_Kd 3:18" }, // invalid 14.6.2 isize item into i32
        { "rx12", "let q: Range<isize, isize> = 0..2\nfor i in q\n    let x: i32 = i\n", "TypeMismatch_Kd 3:18" }, // invalid Range twin of v12x (isize item into i32)
        { "rx13", "var r = (0..3).resolve(3)\nlet u = r@uniq\nfor i in u@uniq => ()\n", "InvalidAssignment_Kd 3:10" }, // invalid 15.1.6 @uniq of a let slot storing uniq/ResolvedRange
        { "rx13r", "var q: Range<isize, isize> = 0..3\nlet u = q@uniq\nfor i in u@uniq => ()\n", "InvalidAssignment_Kd 3:10" }, // invalid Range twin of rx13
        { "a3x", "var r = (0..3).resolve(3)\nlet h = r@ref\nvar a: isize = 0\nfor i in r@uniq => a += i\nConsole.writeLine(\"\\(a) \\(h.end)\")\n", "CallActivationConflict_Kd 4:1" }, // invalid 15.6 r@uniq while h's shared Loan is live
        { "a3xr", "var r: Range<isize, isize> = 0..3\nlet h = r@ref\nvar a: isize = 0\nfor i in r@uniq => a += i\nConsole.writeLine(\"\\(a) \\(h.end)\")\n", "CallActivationConflict_Kd 4:1" }, // invalid Range twin of a3x
        { "b2x", "func f(u: uniq/ResolvedRange) -> isize\n    let s = u@follow@ref\n    var a: isize = 0\n    for i in u => a += i\n    return a + s.end\nvar r = (0..3).resolve(3)\nConsole.writeLine(\"\\(f(r@uniq))\")\n", "CallActivationConflict_Kd 4:5" }, // invalid 15.1.6/15.6 a uniq/ResolvedRange parameter iterated while s, a shared Reborrow of its referent, is live
        { "b2xr", "func f(u: uniq/Range<isize, isize>) -> isize\n    let s = u@follow@ref\n    var a: isize = 0\n    for i in u => a += i\n    return a + s.end\nvar r: Range<isize, isize> = 0..3\nConsole.writeLine(\"\\(f(r@uniq))\")\n", "CallActivationConflict_Kd 4:5" }, // invalid Range twin of b2x
        { "b5x", "var r = (0..3).resolve(3)\nlet u = r@uniq\nlet s = u@follow@ref\nvar a: isize = 0\nfor i in u => a += i\nlet e = s.end\n", "CallActivationConflict_Kd 5:1" }, // invalid 15.1.6/15.6 b2x at top level: a let slot storing uniq/ResolvedRange
        { "b5xr", "var r: Range<isize, isize> = 0..3\nlet u = r@uniq\nlet s = u@follow@ref\nvar a: isize = 0\nfor i in u => a += i\nlet e = s.end\n", "CallActivationConflict_Kd 5:1" }, // invalid Range twin of b5x
        { "b6x", "var r = (0..3).resolve(3)\nlet rv = r@uniq\nvar f = func [rv] () -> isize\n    let s = rv@follow@ref\n    var k: isize = 0\n    for i in rv => k += i\n    return k + s.end\nConsole.writeLine(\"\\(f())\")\n", "CallActivationConflict_Kd 6:5" }, // invalid 15.1.6/15.6 b2x in a var Closure capturing the uniq/ResolvedRange (7.7.3)
        { "b6xr", "var r: Range<isize, isize> = 0..3\nlet rv = r@uniq\nvar f = func [rv] () -> isize\n    let s = rv@follow@ref\n    var k: isize = 0\n    for i in rv => k += i\n    return k + s.end\nConsole.writeLine(\"\\(f())\")\n", "CallActivationConflict_Kd 6:5" }, // invalid Range twin of b6x
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("IntegerRange" + name, source, stdout);

    [Theory]
    [MemberData(nameof(CoverageRows))]
    public void CoverageRowsRun(string row, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("IntegerRangeRow_" + row, source, stdout);

    [Theory]
    [MemberData(nameof(RejectedCoverageRows))]
    public void CoverageRowsReject(string row, string source, string records)
    {
        var actual = string.Join("; ", DiagnosticCorpus.Check(source).Diagnostics.Select(x => x.Code + " " + Position(x.Span!.Value.Start)));
        Assert.True(actual == records, row + ": " + actual);

        string Position(int start) => $"{source.AsSpan(0, start).Count('\n') + 1}:{start - source.LastIndexOf('\n', Math.Max(0, start - 1))}";
    }

    // SPEC 15.6.4 (R4-M170): Resolved is valid, but since G264 (S) S2 A5 errs twice at count's `for` (the lent item's composed erased
    // destruction stores next()'s Loan into the iterator, a self-row), and generation refuses it; the old analysis accepted and ran it to
    // "ok\n". H5's (e) list, row "S2's self-row family" (the owner's O-9, 「(a) 加えて着地」): PLAN G416 restores the acceptance and moves
    // the row back to Executes.
    [Fact]
    public void AResolvedRangeThroughAGenericIterableIsRefusedPendingG416()
    {
        var c = MinimalEmissionTest.Analyze(Resolved);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.False(c.Accepts);
        var loop = new SyntaxLocation("for _ in items\n        n += 1");
        MirTargetSide.Check(c, (DiagnosticCode.ComparisonLoanConflict_Kd, loop), (DiagnosticCode.ComparisonLoanConflict_Kd, loop));
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Fact]
    public void ReversedIterationAbortsWhenItStarts()
        => ScalarEmissionTest.EmitFixture(
            "IntegerRangeReversed",
            "let r = 3..1\nConsole.writeLine(\"constructed\")\nfor i in r\n    Console.writeLine(\"body\")",
            "constructed\n",
            1,
            "Hello.kimi:3:1: abort KIMI_E_ARG_RANGE: Argument out of range\n");

    [Fact]
    public void ReversedClosedIterationAbortsWhenItStarts()
        => ScalarEmissionTest.EmitFixture(
            "IntegerRangeClosedReversed",
            "let r = 3..=2\nConsole.writeLine(\"constructed\")\nfor i in r\n    Console.writeLine(\"body\")",
            "constructed\n",
            1,
            "Hello.kimi:3:1: abort KIMI_E_ARG_RANGE: Argument out of range\n");

    [Theory]
    [InlineData("Reversed", "values[2..1]")]
    [InlineData("ClosedReversed", "values[2..=0]")]
    [InlineData("ClosedEnd", "values[1..=^0]")]
    [InlineData("NegativeEnd", "values[0..=(-1)]")]
    [InlineData("LongFromEnd", "values[^4..]")]
    [InlineData("WideStart", "values[18446744073709551615@u64..]")]
    [InlineData("WideEnd", "values[..170141183460469231731687303715884105727@i128]")]
    public void DirectRangeAbortsInOneCheck(string name, string selection)
        => ScalarEmissionTest.EmitFixture(
            "IntegerRangeDirect" + name,
            "let values: [3 of i32] = [1, 2, 3]\nConsole.writeLine(\"before\")\nlet s = " + selection + "\nConsole.writeLine(\"after\")",
            "before\n",
            1,
            "Hello.kimi:3:9: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    // SPEC 4.6.3: construction checks nothing; a negative boundary fails only when a use resolves it.
    [Fact]
    public void ConstructionChecksNothing()
        => ScalarEmissionTest.EmitFixture(
            "IntegerRangeConstruction",
            "func sideEffect() -> isize\n    Console.writeLine(\"end evaluated\")\n    return 2\nlet a = (-1)..sideEffect()\nlet b = ^(-1)..sideEffect()\nConsole.writeLine(\"ranges built\")\nlet values: [3 of i32] = [1, 2, 3]\nlet c = values[(-1)..]",
            "end evaluated\nend evaluated\nranges built\n",
            1,
            "Hello.kimi:8:9: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    // SPEC 4.6.1: a u64 offset beyond isize fails resolution; nothing is truncated or checked at construction.
    [Theory]
    [InlineData("Direct", "let x = values[^n]")]
    [InlineData("Saved", "let x = values[index]")]
    [InlineData("Element", "let x = values[n]")]
    public void WidePositionsFailInResolution(string name, string statement)
        => ScalarEmissionTest.EmitFixture(
            "IntegerRangeWide" + name,
            "let values: [3 of i32] = [1, 2, 3]\nlet n: u64 = 18446744073709551615\nlet index = ^n\nConsole.writeLine(\"constructed\")\n" + statement,
            "constructed\n",
            1,
            "Hello.kimi:5:9: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    [Theory]
    [InlineData("let e: ClosedRange<i32, i32> = 1..3")]
    [InlineData("for i in 0@i32..1@i64 => ()")]
    [InlineData("for i in ..3 => ()")]
    [InlineData("let inner = 1..^1\nfor i in inner => ()")]
    [InlineData("func accept(range: Range<i32, i32>) -> () => ()\nfunc accept(range: Range<i64, i64>) -> () => ()\naccept(0..10)")]
    [InlineData("let r: Range<f64, f64> = 0..1")]
    [InlineData("let r = Range<i32, i32>.init(1, 2)")]
    [InlineData("let r = PositionSyntax.between(1, 2)")]
    [InlineData("let e = FromEnd<i32>.init(3)")]
    [InlineData("let s = Start.init()")]
    [InlineData("let r = ResolvedRange.init(start: 1, end: 2)")]
    [InlineData("let i = RangeIterator<i32>.unchecked(0, 3)")]
    [InlineData("func zero<T>() -> T\n    T is PrimitiveInteger\n    return 0\nlet r = zero()..10")]
    [InlineData("let values: [2 of i32] = [1, 2]\nlet a = 1.5\nlet x = values[a]")]
    [InlineData("let r = (0..3)..5")]
    [InlineData("let r = true..false")]
    [InlineData("let p = ^1 + 1")]
    [InlineData("let x = (0..3) == (0..=2)")]
    [InlineData("let x = 0 == ^0")]
    [InlineData("let r: Range<i64, u8> = 1..^1")]
    [InlineData("let values: [2 of i32] = [1, 2]\nlet x = values[3_000_000_000]")]
    [InlineData("let n: i64 = 1\nlet r: Range<i32, i32> = n..2")]
    public void RejectsWhatTheRangeRulesDoNotAdmit(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    // SPEC 4.6.2: each literal-only boundary takes the S or E of the expected range, else the integer Type of the other
    // boundary, else i32.
    [Theory]
    [InlineData("let r = 1..^1", "Range<i32, FromEnd<i32>>")]
    [InlineData("let r = 2..", "Range<i32, End>")]
    [InlineData("let r = ..=3", "ClosedRange<Start, i32>")]
    [InlineData("let r = ..", "Range<Start, End>")]
    [InlineData("let n: i64 = 2\nlet r = n..^1", "Range<i64, FromEnd<i64>>")]
    [InlineData("let n: u8 = 2\nlet r = ^n..10", "Range<FromEnd<u8>, u8>")]
    [InlineData("let n: i64 = 2\nlet r: Range<i64, u8> = n..10", "Range<i64, u8>")]
    [InlineData("let r: Range<FromEnd<u16>, End> = ^3..", "Range<FromEnd<u16>, End>")]
    [InlineData("let r = ^1", "FromEnd<i32>")]
    public void LiteralBoundariesTakeTheirTypeByRule(string source, string expected)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var r = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<Kimi.Compiler.Parsing.VariableKoto>().Single(x => x.NameKoto.IdentifierName == "r");
        Assert.Equal(expected, Render(r.NameKoto.TypeOf()!));

        static string Render(BoundType type)
            => type.Components.Count == 0 ? type.Symbol?.Name ?? type.Name : (type.Symbol?.Name ?? type.Name) + "<" + string.Join(", ", type.Components.Select(Render)) + ">";
    }

    [Fact]
    public void ShadowingDoesNotRedirectRangeSyntax()
        => ScalarEmissionTest.EmitFixture(
            "IntegerRangeShadow",
            "struct Range\n    public let other: i32 = 0\nstruct End\n    public let other: i32 = 0\nstruct FromEnd\n    public let other: i32 = 0\nlet values: [3 of i32] = [1, 2, 3]\nvar total = 0\nfor v in 0..3 => total += v\nlet tail = values[1..^0]\nlet saved = 1..\nlet last = ^1\nrequire total == 3 and tail.length == 2 and values[saved].length == 2 and values[last] == 3 else => $abort(\"shadow\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    [Fact]
    public void ExampleRunsUnchanged()
        => ScalarEmissionTest.EmitFixture(
            "IntegerRangeExample",
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../docs/examples/Ranges/Ranges.kimi")).Replace("\r\n", "\n", StringComparison.Ordinal),
            "Sum of 1..=4 is 10.\nLast byte is 255.\nMiddle has 3 values.\nTail starts at 40.\nResolved range covers 3 positions.\nWindow 3..9 is out of range.\n");
}
