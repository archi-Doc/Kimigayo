// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.6.3–4.6.4: two boundaries of one integer Type construct an iterable Range&lt;T, T&gt; or ClosedRange&lt;T, T&gt;;
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

    // SPEC 4.6.3.4: a ResolvedRange's entries return RangeIterator<isize> over its half-open interval, also through a
    // generic Iterable and an owning adapter.
    private const string Resolved =
        "func count<S>(items: S) -> i32\n    S is Iterable\n    var n: i32 = 0\n    for _ in items\n        n += 1\n    return n\n" +
        "let values: [5 of i32] = [1, 2, 3, 4, 5]\nlet r = (1..4).resolve(values.length)\n" +
        "var sum: isize = 0\nfor i in Kimi.Iteration.owning(r.intoIterator()) => sum += i\nrequire sum == 6 else => $abort(\"owning\")\n" +
        "var shared = r.iterate()\nmatch shared.next()\n    .Some(let first) => require first == 1 else => $abort(\"first\")\n    .None => $abort(\"empty\")\n" +
        "require count(values.indices) == 5 and count(r) == 3 and count((2..2).resolve(5)) == 0 and count(0..4) == 4 and count(0..=4) == 5 else => $abort(\"generic\")\n" +
        "Console.writeLine(\"ok\")";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "Iteration", Iteration, "ok\n" },
        { "Resolved", Resolved, "ok\n" },
        { "Slicing", Slicing, "Reversed refused.\nNegative refused.\nClosed end refused.\nTry negative refused.\nTry reversed refused.\nok\n" },
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
            LibrarySource.Location("Core.kimi", "$abort(\"Reversed range\")", "internal func starting(start: T, end: T) -> RangeIterator<T>") + ": abort KIMI_E_ABORT: Reversed range\n");

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

    // SPEC 4.6.4: construction checks nothing; a negative boundary fails only when a use resolves it.
    [Fact]
    public void ConstructionChecksNothing()
        => ScalarEmissionTest.EmitFixture(
            "IntegerRangeConstruction",
            "func sideEffect() -> isize\n    Console.writeLine(\"end evaluated\")\n    return 2\nlet a = (-1)..sideEffect()\nlet b = ^(-1)..sideEffect()\nConsole.writeLine(\"ranges built\")\nlet values: [3 of i32] = [1, 2, 3]\nlet c = values[(-1)..]",
            "end evaluated\nend evaluated\nranges built\n",
            1,
            "Hello.kimi:8:9: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    // SPEC 4.6.2: a u64 offset beyond isize fails resolution; nothing is truncated or checked at construction.
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
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 4.6.3.1: each literal-only boundary takes the S or E of the expected range, else the integer Type of the other
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var r = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<Kimi.Compiler.Parsing.VariableKoto>().Single(x => x.NameKoto.IdentifierName == "r");
        Assert.Equal(expected, Render(r.NameKoto.BoundType!));

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
