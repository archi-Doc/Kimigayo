// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

// A5's meet law at a shorter end (SPEC 15.3.6; U3-design.md's twelfth amendment) and the nested-body limit, on the view's rows: each
// operand has one bound here, so each in-edge takes one choice and its record is that edge's.
public sealed partial class MirOriginsTest
{
    // The caller's parameters of the meet-law family (U3-design.md's `gen-meets.py`): three free Origins, static, and a forcing reference
    // to each.
    internal const string MeetParams = "r: ref/i32, q: ref/i32, e: ref/i32, s: ref/i32 during static, ur: uniq/(ref/i32 during r), uq: uniq/(ref/i32 during q), ue: uniq/(ref/i32 during e), us: uniq/(ref/i32 during static), ur2: uniq/(ref/i32 during r), uq2: uniq/(ref/i32 during q), ue2: uniq/(ref/i32 during e)";

    // `x: (a and d)` with two call regions bounded by distinct ends r1 and q1 (C-49's B1).
    internal const string DistinctMk = "func mk(p: Option<ref/(ref/i32 during a) during r1>, w: Option<ref/(ref/i32 during d) during q1>, x: ref/i32 during (a and d), y: uniq/(ref/i32 during r1), z: uniq/(ref/i32 during q1)) -> i32 => 0\n";

    // `x: (a and d)` with a call region `a` bounded by r1 (a forcing reference) and `d` taken from a parameter: one literal Fixed operand.
    private const string LiteralMk = "func mk(p: Option<ref/(ref/i32 during a) during r1>, y: uniq/(ref/i32 during r1), w: ref/i32 during d, x: ref/i32 during (a and d)) -> i32 => 0\n";

    // A callee whose Closure argument's input and result lie during the call region `a`, which is Fixed in the Closure's body.
    private const string RegionMk = "func mk(p: Option<ref/(ref/i32 during a) during b>, y: ref/i32 during b, f: (ref/i32 during a) -> ref/i32 during a) -> i32 => 0\n";

    // SPEC 15.3.6: `u outlives (o1 and ... and on)` is judged as `u outlives (g1 and ... and gn)` for each choice of one bound gi per
    // operand, at the in-edge's site: a literal Fixed operand (q) beside a call region bounded by r; distinct ends (r and q), Refuted for
    // a local's Borrow and Unknown for a third parameter (C-49's mx_bad, mx_fix); a nested meet (the bound of a call region bounded by
    // a meet, the record at p's row); a cycle of two meets through two locals; two in-edges into one meet, each judged; and an operand
    // whose only edge is its axiom edge into its own meet, which is never walked, so it has no bound and a finite source is valid.
    [Theory]
    [InlineData(LiteralMk + "func k(r: ref/i32, q: ref/i32, ur: uniq/(ref/i32 during r)) -> i32\n    let short = 2\n    return mk(.None, ur, q, short@ref)\n", "UnsatisfiedOriginRelation_Kd short@ref")]
    [InlineData(LiteralMk + "func k(r: ref/i32, q: ref/i32, e: ref/i32, ur: uniq/(ref/i32 during r)) -> i32\n    return mk(.None, ur, q, e)\n", "UnprovenOriginRelation_Kd e")]
    [InlineData(LiteralMk + "func k(r: ref/i32, q: ref/i32, ur: uniq/(ref/i32 during r)) -> i32\n    let short = 2\n    return mk(.None, ur, q, r)\n")]
    [InlineData(DistinctMk + "func k(r: ref/i32, q: ref/i32, ur: uniq/(ref/i32 during r), uq: uniq/(ref/i32 during q)) -> i32\n    let short = 2\n    return mk(.None, .None, short@ref, ur, uq)\n", "UnsatisfiedOriginRelation_Kd short@ref")]
    [InlineData(DistinctMk + "func k(r: ref/i32, q: ref/i32, e: ref/i32, ur: uniq/(ref/i32 during r), uq: uniq/(ref/i32 during q)) -> i32\n    return mk(.None, .None, e, ur, uq)\n", "UnprovenOriginRelation_Kd e")]
    [InlineData(DistinctMk + "func k(r: ref/i32, q: ref/i32, ur: uniq/(ref/i32 during r), uq: uniq/(ref/i32 during q)) -> i32\n    let short = 2\n    return mk(.None, .None, r, ur, uq)\n")]
    [InlineData("func mk(p: Option<ref/(ref/i32 during a) during (c and h)>, w1: Option<ref/(ref/i32 during c) during f>, y1: uniq/(ref/i32 during f), w2: Option<ref/(ref/i32 during h) during g>, y2: uniq/(ref/i32 during g), x: ref/i32 during a) -> i32 => 0\nfunc k(" + MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, .None, ur, .None, uq, short@ref)\n", "UnsatisfiedOriginRelation_Kd .None")]
    [InlineData("func mk(p: Option<ref/(ref/i32 during a) during (c and h)>, w1: Option<ref/(ref/i32 during c) during f>, y1: uniq/(ref/i32 during f), w2: Option<ref/(ref/i32 during h) during g>, y2: uniq/(ref/i32 during g), x: ref/i32 during a) -> i32 => 0\nfunc k(" + MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, .None, ur, .None, uq, r)\n")]
    [InlineData("func mka(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), x: ref/i32 during (a and d)) -> i32 => 0\nfunc mkb(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), p2: Option<ref/(ref/i32 during a) during h>, y2: uniq/(ref/i32 during h), w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), x: ref/i32 during (a and d)) -> i32 => 0\nfunc k(" + MeetParams + ") -> i32\n    let short = 2\n    var v1 = r\n    var v2 = q\n    let t1 = mka(.None, v2@uniq, .None, ur, v1)\n    let t2 = mkb(.None, v1@uniq, .None, ue, .None, uq, v2)\n    return t1 + t2\n")]
    [InlineData("func mk(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), x: ref/i32 during (a and d), x2: ref/i32 during (a and d)) -> i32 => 0\nfunc k(" + MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, ur, .None, uq, r, e)\n", "UnprovenOriginRelation_Kd e")]
    [InlineData("func mk(p: Option<ref/(ref/i32 during a) during f>, y: uniq/(ref/i32 during f), w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), x: ref/i32 during (a and d), x2: ref/i32 during (a and d)) -> i32 => 0\nfunc k(" + MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, ur, .None, uq, r, r)\n")]
    [InlineData("func mk(w: Option<ref/(ref/i32 during d) during g>, z: uniq/(ref/i32 during g), x: ref/i32 during (a and d)) -> i32 => 0\nfunc k(" + MeetParams + ") -> i32\n    let short = 2\n    return mk(.None, uq, short@ref)\n")]
    public void AMeetAtAShorterEndOutlivesOneBoundPerOperand(string source, params string[] problems)
        => Assert.Equal(problems, Origins(MirBuildTest.Compile(source + Main)));

    // The nested-body limit (U3-design.md's N4): an enclosing body's call region is Fixed in a Closure's body, where only the enclosing
    // body's rows decide it, so a failing chain with such an end is the located Unsupported_Kd at the chain's site: Refuted at a target
    // region (a literal's Borrow through pick into the Closure's result), Unknown at a target region (a captured parameter returned) and
    // Unknown at a source region (hole9_ok's `a outlives static`, valid, the owner's (i)).
    [Theory]
    [InlineData(RegionMk + "func pick(x: ref/i32 during s, y: ref/i32 during s) -> ref/i32 during s => x\nfunc k(t: ref/i32, u: ref/i32) -> i32\n    return mk(.None, t, func [] (v) => pick(v, 7@ref))\n", "Unsupported_Kd pick(v, 7@ref)")]
    [InlineData(RegionMk + "func k(t: ref/i32, u: ref/i32) -> i32\n    return mk(.None, t, func [u] (v) => u)\n", "Unsupported_Kd u")]
    [InlineData("func mk(p: Option<ref/(ref/i32 during a) during b>, u: Option<ref/(ref/i32 during e) during a>, w: Option<ref/(ref/i32 during d) during b>, y: ref/i32 during b, z: ref/i32 during e, x: ref/i32 during (a and d), g: (ref/i32 during b) -> i32, f: (ref/i32 during a, bool) -> i32) -> i32 => 0\nfunc k(c: bool, s: ref/i32 during static) -> i32\n    let short = 2\n    let sr = short@ref\n    return mk(if c => .None else => .None, if c => .None else => .None, if c => .None else => .None, s, s, s, func [] (t: ref/i32) => 0, func [s] (v, c2) => mk(if c2 => .None else => .None, if c2 => .None else => .None, if c2 => .None else => .None, s, s, v, func [] (t: ref/i32 during static) => 0, func [] (v2, c3) => 0))\n", "Unsupported_Kd v")]
    public void AFailingChainAtAnEnclosingCallRegionIsTheLocatedLimit(string source, string problem)
        => Assert.Equal([problem], Origins(MirBuildTest.Compile(source + Main)));

    // The law's cost (MirOrigins.Meets.cs): Work grows linearly in a close's choices, the product of its operands' bound counts (the
    // product row, C-49 re-check N1: `a` with k bounds and `d` with 8, so 8k choices); in the calls with a meet argument in one body; and
    // in a chain of nested meets whose operands each have two comparable bounds (`origin r outlives q`, C-38's row), where an entered
    // meet's bounds multiply into the outer choices but the canonical meets of r and q stay three.
    [Fact]
    public void TheMeetLawsWorkIsLinear()
    {
        long[] product = [.. new[] { 8, 16, 32 }.Select(static k => MeetWork(Product(k, 8)))];
        long[] calls = [.. new[] { 16, 32, 64 }.Select(static n => MeetWork(DistinctMk + $"func f(r: ref/i32, q: ref/i32, {Forcing(n)}) -> i32\n" +
            string.Concat(Enumerable.Range(0, n).Select(static i => $"    let t{i} = mk(.None, .None, r, ur{i}, uq{i})\n")) + "    return 0\n"))];
        long[] nested = [.. new[] { 16, 32, 64 }.Select(static n => MeetWork(Nested(n)))];
        Assert.True(MirBorrowsTest.Grows(product, 2), $"product {string.Join(", ", product)}");
        Assert.True(MirBorrowsTest.Grows(calls, 2), $"calls {string.Join(", ", calls)}");
        Assert.True(MirBorrowsTest.Grows(nested, 2), $"nested {string.Join(", ", nested)}");
    }

    // +warm: the stratum on meet-law bodies it judged before allocates nothing: a Refuted and an Unknown derived edge (mx_bad, mx_fix), a
    // product of 16 choices and a chain of nested meets (the view interns each canonical meet once; the law's storage is pooled).
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmMeetLawJudgmentAllocatesNothing()
    {
        var c = MirBuildTest.Compile(DistinctMk + "func k1(r: ref/i32, q: ref/i32, ur: uniq/(ref/i32 during r), uq: uniq/(ref/i32 during q)) -> i32\n" +
            "    let short = 2\n    return mk(.None, .None, short@ref, ur, uq)\nfunc k2(r: ref/i32, q: ref/i32, e: ref/i32, ur: uniq/(ref/i32 during r), " +
            "uq: uniq/(ref/i32 during q)) -> i32\n    return mk(.None, .None, e, ur, uq)\n" + Product(4, 4) + Nested(4) + Main);
        var bodies = c.BuildMir().Bodies.ToArray();
        var origins = new MirOrigins(c.Types, new MirCases(c.Types));
        var report = new MirReport();
        void Run()
        {
            report.Clear();
            foreach (var body in bodies)
            {
                origins.Judge(body, MirCases.Count(body), report);
            }
        }

        Run();
        Assert.Equal(2, report.All.Length);
        Assert.Equal(0, AllocationMeasurement.Measure(Run));
    }

    // `x: (a and d)` with a bounded by k forcing references to r1..rk and d by m to q1..qm, given o under `origin o outlives qj`: valid.
    private static string Product(int k, int m)
    {
        var ends = Enumerable.Range(1, k).Select(static i => ("a", $"r{i}")).Concat(Enumerable.Range(1, m).Select(static i => ("d", $"q{i}"))).ToArray();
        var mk = string.Join(", ", ends.Select(static x => $"p{x.Item2}: Option<ref/(ref/i32 during {x.Item1}) during f{x.Item2}>, y{x.Item2}: uniq/(ref/i32 during f{x.Item2})"));
        var parameters = string.Join(", ", ends.Select(static x => $"{x.Item2}: ref/i32, u{x.Item2}: uniq/(ref/i32 during {x.Item2})"));
        var premises = string.Concat(Enumerable.Range(1, m).Select(static i => $"    origin o outlives q{i}\n"));
        return $"func mkp({mk}, x: ref/i32 during (a and d)) -> i32 => 0\nfunc f(o: ref/i32, {parameters}) -> i32\n{premises}" +
            $"    return mkp({string.Join(", ", ends.Select(static x => $".None, u{x.Item2}"))}, o)\n";
    }

    // A chain of n nested meets: a0 bounded by r and q, each ai (i > 0) by (a(i-1) and hi), hi by r and q; x during an, given r: valid.
    private static string Nested(int n)
    {
        var mk = new List<string> { "p0: Option<ref/(ref/i32 during a0) during b0>, y0: uniq/(ref/i32 during b0), w0: Option<ref/(ref/i32 during a0) during c0>, z0: uniq/(ref/i32 during c0)" };
        mk.AddRange(Enumerable.Range(1, n).Select(static i => $"m{i}: Option<ref/(ref/i32 during a{i}) during (a{i - 1} and h{i})>, p{i}: Option<ref/(ref/i32 during h{i}) during b{i}>, " +
            $"y{i}: uniq/(ref/i32 during b{i}), w{i}: Option<ref/(ref/i32 during h{i}) during c{i}>, z{i}: uniq/(ref/i32 during c{i})"));
        var args = new List<string> { ".None, ur0, .None, uq0" };
        args.AddRange(Enumerable.Range(1, n).Select(static i => $".None, .None, ur{i}, .None, uq{i}"));
        return $"func mkn({string.Join(", ", mk)}, x: ref/i32 during a{n}) -> i32 => 0\nfunc g(r: ref/i32, q: ref/i32, {Forcing(n + 1)}) -> i32\n" +
            $"    origin r outlives q\n    return mkn({string.Join(", ", args)}, r)\n";
    }

    // n pairs of forcing references to r and q, one pair per use.
    private static string Forcing(int n) => string.Join(", ", Enumerable.Range(0, n).Select(static i => $"ur{i}: uniq/(ref/i32 during r), uq{i}: uniq/(ref/i32 during q)"));

    // The stratum's work on the body f or g, a valid one.
    private static long MeetWork(string source)
    {
        var name = source.Contains("func g(", StringComparison.Ordinal) ? "g" : "f";
        var (origins, _, c) = Judged(source, name);
        Assert.Empty(Origins(c));
        return origins.Work;
    }
}
