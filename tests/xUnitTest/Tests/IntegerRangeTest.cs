// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.6.3–4.6.4: two integer boundaries construct the iterable Range&lt;T&gt;; construction checks no order,
/// iteration Aborts on a reversed range, and resolution and slicing check boundaries against a length.</summary>
public class IntegerRangeTest
{
    private const string Iteration =
        "var sum: i32 = 0\nfor var x in -3..3\n    sum += x\n    x += 100\nrequire sum == -3 else => $abort(\"half-open\")\n" +
        "var count: i32 = 0\nfor x in 1..=3 => count += x\nrequire count == 6 else => $abort(\"inclusive\")\n" +
        "var last: u8 = 0\nfor b in 250@u8..=255 => last = b\nrequire last == 255 else => $abort(\"maximum\")\n" +
        "var steps = 0\nfor _ in 5..5 => steps += 1\nrequire steps == 0 else => $abort(\"empty\")\nfor _ in 7..=7 => steps += 1\nrequire steps == 1 else => $abort(\"single\")\n" +
        "let saved = 0@i64..4\nvar total: i64 = 0\nfor v in saved => total += v\nfor v in saved => total += v\nrequire total == 12 else => $abort(\"reuse\")\n" +
        "let n = 10\nlet d = 0..n@ref\nvar seen = 0\nfor v in d => seen = v\nrequire seen == 9 else => $abort(\"scalar read\")\n" +
        "let e = [0..3, 5..8@i64]\nrequire e[1].start == 5 and e[1].end == 8 and e[0].end == 3 and not e[0].isInclusive else => $abort(\"array\")\n" +
        "let reversed = 3..1\nrequire reversed.start == 3 and reversed.end == 1 else => $abort(\"reversed value\")\n" +
        "require (0..3) != (0..=2) and (0..3) == (0..3) and saved == (0..4) else => $abort(\"equality\")\n" +
        "Console.writeLine(\"ok\")";

    private const string Slicing =
        "let values: [5 of i32] = [10, 20, 30, 40, 50]\nlet a: i32 = 1\nlet b: i32 = 3\n" +
        "let s = values[a..b]\nrequire s.length == 2 and s[0] == 20 else => $abort(\"direct\")\n" +
        "let t = values[a..]\nrequire t.length == 4 and t[0] == 20 else => $abort(\"from\")\n" +
        "let u = values[a..^1]\nrequire u.length == 3 and u[2] == 40 else => $abort(\"index range\")\n" +
        "let w = values[^b..]\nrequire w.length == 3 and w[0] == 30 else => $abort(\"from end\")\n" +
        "let r: Range<isize> = 1..4\nlet x = values[r]\nrequire x.length == 3 and x[0] == 20 else => $abort(\"saved\")\n" +
        "let inclusive = values[1..=3]\nrequire inclusive.length == 3 and inclusive[2] == 40 else => $abort(\"inclusive\")\n" +
        "let big: u64 = 2\nlet y = values[big..4]\nrequire y.length == 2 and y[1] == 40 else => $abort(\"unsigned\")\n" +
        "let resolved = (1..3).resolve(values.length)\nrequire resolved.start == 1 and resolved.end == 3 else => $abort(\"resolve\")\n" +
        "match (3..1).tryResolve(5)\n    .Some(_) => $abort(\"reversed\")\n    .None => Console.writeLine(\"Reversed refused.\")\n" +
        "match (-1..2).tryResolve(5)\n    .Some(_) => $abort(\"negative\")\n    .None => Console.writeLine(\"Negative refused.\")\n" +
        "match (0..=4).tryResolve(4)\n    .Some(_) => $abort(\"inclusive end\")\n    .None => Console.writeLine(\"Inclusive end refused.\")\n" +
        "match values[..].trySlice(-3..3)\n    .Some(_) => $abort(\"try negative\")\n    .None => Console.writeLine(\"Try negative refused.\")\n" +
        "match values[..].trySlice(3..1)\n    .Some(_) => $abort(\"try reversed\")\n    .None => Console.writeLine(\"Try reversed refused.\")\n" +
        "match values[..].trySlice(2..=4)\n    .Some(let found) => require found.length == 3 and found[0] == 30 else => $abort(\"try inclusive\")\n    .None => $abort(\"try none\")\n" +
        "Console.writeLine(\"ok\")";

    private const string Generic =
        "func sum<T>(values: Range<T>) -> T\n    T is PrimitiveInteger\n    var total: T = 0\n    for value in values\n        total += value\n    return total\n" +
        "func tryWindow<T, I>(values: Slice<T> ! start: I, end: I) -> Option<Slice<T> during values.source>\n    I is PrimitiveInteger\n    return values.trySlice(start..end)\n" +
        "let data: [4 of i32] = [1, 2, 3, 4]\n" +
        "require sum(1..5@u8) == 10 and sum(0..1000) == 499500 and sum(-2..=2@i64) == 0 else => $abort(\"sum\")\n" +
        "match tryWindow(data[..], start: 1@u16, end: 3)\n    .Some(let w) => require w.length == 2 and w[0] == 2 else => $abort(\"window\")\n    .None => $abort(\"window none\")\n" +
        "match tryWindow(data[..], start: -1, end: 2)\n    .Some(_) => $abort(\"window negative\")\n    .None => Console.writeLine(\"Window refused.\")\n" +
        "Console.writeLine(\"ok\")";

    // SPEC 4.6.3.5: a ResolvedRange's entries return RangeIterator<isize> over its half-open interval, also through a
    // generic Iterable and an owning adapter.
    private const string Resolved =
        "func count<S>(items: S) -> i32\n    S is Iterable\n    var n: i32 = 0\n    for _ in items\n        n += 1\n    return n\n" +
        "let values: [5 of i32] = [1, 2, 3, 4, 5]\nlet r = (1..4).resolve(values.length)\n" +
        "var sum: isize = 0\nfor i in Kimi.Iteration.owning(r.intoIterator()) => sum += i\nrequire sum == 6 else => $abort(\"owning\")\n" +
        "var shared = r.iterate()\nmatch shared.next()\n    .Some(let first) => require first == 1 else => $abort(\"first\")\n    .None => $abort(\"empty\")\n" +
        "require count(values.indices) == 5 and count(r) == 3 and count((2..2).resolve(5)) == 0 and count(0..4) == 4 else => $abort(\"generic\")\n" +
        "Console.writeLine(\"ok\")";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "Iteration", Iteration, "ok\n" },
        { "Resolved", Resolved, "ok\n" },
        { "Slicing", Slicing, "Reversed refused.\nNegative refused.\nInclusive end refused.\nTry negative refused.\nTry reversed refused.\nok\n" },
        { "Generic", Generic, "Window refused.\nok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("IntegerRange" + name, source, stdout);

    [Fact]
    public void ReversedIterationAbortsWhenItStarts()
        => ScalarEmissionTest.EmitFixture(
            "IntegerRangeReversed",
            "let r = 3..1\nConsole.writeLine(\"constructed\")\nfor i in r\n    Console.writeLine(\"body\")",
            "constructed\n",
            1,
            LibraryAbort("$abort(\"Reversed range\")") + ": abort KIMI_E_ABORT: Reversed range\n");

    [Fact]
    public void DirectReversedSliceAbortsInSlicing()
        => ScalarEmissionTest.EmitFixture(
            "IntegerRangeReversedSlice",
            "let values: [3 of i32] = [1, 2, 3]\nConsole.writeLine(\"before\")\nlet s = values[2..1]\nConsole.writeLine(\"after\")",
            "before\n",
            1,
            "Hello.kimi:3:9: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    [Fact]
    public void IndexFormationFollowsBoundaryEvaluation()
        => ScalarEmissionTest.EmitFixture(
            "IntegerRangeFormationOrder",
            "func sideEffect() -> isize\n    Console.writeLine(\"end evaluated\")\n    return 2\nlet a = (-1)..sideEffect()\nConsole.writeLine(\"range built\")\nlet b = (-1)..^sideEffect()",
            "end evaluated\nrange built\nend evaluated\n",
            1,
            LibraryAbort("$abort(\"Invalid range start\")") + ": abort KIMI_E_ABORT: Invalid range start\n");

    [Fact]
    public void FromEndFormationChecksItsOwnType()
        => ScalarEmissionTest.EmitFixture(
            "IntegerRangeFromEndWide",
            "let values: [3 of i32] = [1, 2, 3]\nlet n: u64 = 18446744073709551615\nConsole.writeLine(\"before\")\nlet index = ^n\nConsole.writeLine(\"after\")",
            "before\n",
            1,
            "Hello.kimi:4:14: abort KIMI_E_INT_CONVERSION: Integer conversion out of range\n");

    [Theory]
    [InlineData("let e: IndexRange = 1..3")]
    [InlineData("let f = 0@i32..1@i64")]
    [InlineData("for i in ..3 => ()")]
    [InlineData("let inner = 1..^1\nfor i in inner => ()")]
    [InlineData("func accept(range: Range<i32>) -> () => ()\nfunc accept(range: Range<i64>) -> () => ()\naccept(0..10)")]
    [InlineData("let r: Range<f64> = 0..1")]
    [InlineData("let r = Range<i32>.between(1, 2)")]
    [InlineData("let r = IndexRange.between(Index.init(0), ^0)")]
    [InlineData("let i = Index.init(unchecked: 3)")]
    [InlineData("let r = ResolvedRange.init(start: 1, end: 2)")]
    [InlineData("let i = RangeIterator<i32>.unchecked(0, 3)")]
    [InlineData("func zero<T>() -> T\n    T is PrimitiveInteger\n    return 0\nlet r = zero()..10")]
    [InlineData("let values: [2 of i32] = [1, 2]\nlet a: i32 = 0\nlet x = values[a]")]
    [InlineData("let r = (0..3)..5")]
    [InlineData("let r = true..false")]
    public void RejectsWhatTheRangeRulesDoNotAdmit(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void ShadowingDoesNotRedirectRangeSyntax()
        => ScalarEmissionTest.EmitFixture(
            "IntegerRangeShadow",
            "struct Range\n    public let other: i32 = 0\nstruct IndexRange\n    public let other: i32 = 0\nlet values: [3 of i32] = [1, 2, 3]\nvar total = 0\nfor v in 0..3 => total += v\nlet tail = values[1..^0]\nrequire total == 3 and tail.length == 2 else => $abort(\"shadow\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    private static string LibraryAbort(string anchor)
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Kimi/Library/Core.kimi"));
        var offset = source.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(offset >= 0);
        Assert.Equal(offset, source.LastIndexOf(anchor, StringComparison.Ordinal));
        var line = source.AsSpan(0, offset).Count('\n') + 1;
        var column = offset - source.LastIndexOf('\n', offset);
        return FormattableString.Invariant($"compiler://Kimi/{Compilation.CurrentLanguageVersion}/Core.kimi:{line}:{column}");
    }
}
