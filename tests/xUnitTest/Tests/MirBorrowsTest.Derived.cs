// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>A5 on values derived from Binding's errors (SPEC 23.1.2; docs/dev/DECISIONS.md R4-M109): an input Binding rejected after
/// selection is its consumer's derived value, which carries no Loan, so A5 reports no conflict derived from it, while the independent
/// records beside it stay.</summary>
public sealed partial class MirBorrowsTest
{
    // R4's m1: st's kept row stores x's borrow past c, a by-argument holder (P13), beside a Bad input, then y written while r lives.
    private const string Kept = RetentionTest.Bad + "struct Ph<T>\n    public var n: i32 = 0\n    public init() => ()\n" +
        "func st(c: uniq/Ph<uniq/(ref/i32 during a) during e>, v: ref/i32 during a, b: uniq/Bad) => ()\nfunc g(b: uniq/Bad)\n    var x = 1\n" +
        "    var y = 2\n    let r = y@ref\n    var c = Ph<uniq/(ref/i32)>.init()\n    st(c@uniq, x@ref, b)\n    y = 3\n    let k = r@follow\n";

    // SPEC 23.1.2 (DECISIONS R4-M109): an argument Binding rejects after selection (an Origin relation it cannot prove) is an Opaque of the
    // reference the call takes, so a result kept past the statement carries no Loan on a temporary: no ComparisonLoanConflict_Kd beside
    // Binding's record, also where the rejected argument keeps a call that moves twice (A1's MovedPlace_Kd stays), while the true conflict of
    // a literal's temporary borrowed for static stays (fail-before: a conflict at each rejected argument).
    [Fact]
    public void ARejectedInputFormsNoLoan()
    {
        var c = MirBuildTest.Compile(Prelude + """
            struct Tally
                public var n: i32 = 0
                public init() => ()
            func keepStatic(x: ref/i32 during static) -> ref/i32 during static => x
            func chosen(c: ref/Tally, a: string, b: string) -> ref/Tally during c => c
            func field(c: ref/Tally)
                let r = keepStatic(c.n)
                inspect(r)
            func kept(c: ref/Tally, s: string)
                let r = keepStatic(chosen(c, s@move, s@move).n)
                inspect(r)
            func literal()
                let r = keepStatic(5)
                inspect(r)
            """);
        Assert.Equal(2, c.Binding.Issues.Count(static x => x.Code == DiagnosticCode.UnprovenOriginRelation_Kd));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        new Stratum(c.Types).Run(program);
        Assert.Equal(["kept: s MovedPlace_Kd", "literal: 5"], Problems(c, program, [.. LoanCodes, DiagnosticCode.MovedPlace_Kd]));
    }

    // SPEC 3.6, 23.1.2 (DECISIONS R4-M125): a local whose declaration Binding rejected (a failed Origin clause, a mismatched initializer)
    // is one storage, as its valid twin's (§ replaced by the valid fragment) is, so A5 and A1 report what the twin reports and no more:
    // no conflict derived from the name (a Borrow of it, of a Field, a method form, an exclusive one), a second move, a write or update
    // while it is borrowed (also by a capture) at the write, and a use before an initialization the declaration does not give
    // (fail-before: a conflict at the name, whose every use was a temp of its own statement, and none of the twin's records).
    [Theory]
    [InlineData("func f(a: ref/i32) -> ref/i32 during a\n    let h0 = S.init(a)\n    let h: S{x} = h0@move\n        origin x.a outlives §\n    let p = h@ref\n    let q = p.r@follow@ref\n    return q\n", "zz", "a", "")]
    [InlineData("func f(a: ref/i32) -> i32\n    let h0 = S.init(a)\n    let h: S{x} = h0@move\n        origin x.a outlives §\n    let p = h.n@ref\n    let k = p@follow\n    return k\n", "zz", "a", "")]
    [InlineData("func f(a: ref/i32) -> i32\n    let h0 = S.init(a)\n    let h: S{x} = h0@move\n        origin x.a outlives §\n    let p = h@ref\n    let k = p.r@follow\n    return k\n", "zz", "a", "")]
    [InlineData("func f(n: uniq/i32) -> i32\n    var e: U{x} = U.init(p: n)\n        origin x.a outlives §\n    var p = e@uniq\n    let k = p.p@follow\n    return k\n", "zz", "n", "")]
    [InlineData("func f() -> i32\n    var h: i32 = §\n    let p = h@ref\n    let k = p@follow\n    return k\n", "\"s\"", "5", "")]
    [InlineData("func f(a: ref/i32) -> i32\n    let h0 = S.init(a)\n    let h: S{x} = h0@move\n        origin x.a outlives §\n    let m = h@move\n    let n = h@move\n    return 0\n", "zz", "a", "f: h MovedPlace_Kd")]
    [InlineData("func f() -> i32\n    var h: i32 = §\n    let p = h@ref\n    h = 3\n    let k = p@follow\n    return k\n", "\"s\"", "5", "f: h = 3")]
    [InlineData("func f() -> i32\n    var h: i32 = §\n    let p = h@ref\n    h += 3\n    let k = p@follow\n    return k\n", "\"s\"", "5", "f: h += 3 PlacementActivationConflict_Kd")]
    [InlineData("func f() -> i32\n    var h: i32 = §\n    let g = func [h@ref] () => h@follow\n    h = 4\n    return g()\n", "\"s\"", "5", "f: h = 4")]
    [InlineData("func f(a: ref/i32) -> i32\n    var h: S{x}\n        origin x.a == §\n    let p = h@ref\n    let k = p.r@follow\n    return k\n", "zz", "a", "f: h@ref UninitializedPlace_Kd")]
    [InlineData("func f(c: bool) -> i32\n    defer\n        var h: i32 = §\n        let p = h@ref\n        let k = p@follow\n    if c\n        return 1\n    return 2\n", "\"s\"", "5", "")]
    public void ARejectedDeclarationsLocalIsOneStorage(string source, string failed, string valid, string expected)
    {
        foreach (var fragment in (string[])[failed, valid])
        {
            var c = MirBuildTest.Compile(Prelude + source.Replace("§", fragment, StringComparison.Ordinal));
            Assert.Equal(fragment == failed, c.Binding.Issues.Any());
            var program = c.BuildMir();
            Assert.Empty(program.Findings);
            new Stratum(c.Types).Run(program);
            Assert.Equal(
                expected.Split('|', StringSplitOptions.RemoveEmptyEntries),
                Problems(c, program, [.. LoanCodes, DiagnosticCode.MovedPlace_Kd, DiagnosticCode.UninitializedPlace_Kd]));
        }
    }

    // SPEC 23.1.2 (G240; DECISIONS BU-36): a call over storage whose part Binding did not bind keeps the rows that hold whatever the part
    // is, so A5 reports its repaired program's Loan conflicts beside it and no Unsupported_Kd at the call (fail-before: Unsupported_Kd at
    // keep2, stash and choose; i-field's at D.init), with no builder record or validator finding. A conflict that rests on the failed
    // part is its dependent and goes (q1: `result` holds `first` only through the failed payload, its repair's conflict). A held view
    // stays held (m1: the kept store row holds it, its repair's P13 hold). A genuine limit stays the Unsupported_Kd at the call beside the
    // failed part (a Field of height 66, a growing Field) and in its repair, and so do X and T, which reach T's Box^64 Field.
    [Theory]
    [InlineData(RetentionTest.Keep2, "Missing", "i32", "g: v = 2")]
    [InlineData(RetentionTest.Stash, "Missing", "i32", "g: x = 3")]
    [InlineData(RetentionTest.Pick, "missing", "s", "g: x = 2")]
    [InlineData(RetentionTest.Handle, "Missing", "i32", "")]
    [InlineData(RetentionTest.Selection + "    first.value = 5\n    inspect(result@ref)\n", "missing", "source", "", "run: first.value = 5")]
    [InlineData(Kept, "Missing", "i32", "g: y = 3 Unsupported_Kd | g: var x = 1 Unsupported_Kd")]
    [InlineData(RetentionTest.Fielded, "C{x}\n        origin x.s == zz", "C{x}\n        origin x.s == s", "")]
    [InlineData(RetentionTest.Fielded, "Missing", "C{x}\n        origin x.s == s", "")]
    [InlineData(RetentionTest.Tall, "Missing", "i32", "g: f(b, null) Unsupported_Kd")]
    [InlineData(RetentionTest.Growing, "Missing", "i32", "h: f(g, null) Unsupported_Kd")]
    [InlineData(RetentionTest.Beyond + "func g(t: uniq/T, x: uniq/X)\n    fx(x, null)\n    ft(t, null)\n", "Missing", "i32", "g: fx(x, null) Unsupported_Kd | g: ft(t, null) Unsupported_Kd")]
    public void ACallOverRejectedStorageKeepsItsRows(string source, string failed, string repaired, string expected, string? twin = null)
    {
        Assert.Equal(expected, CallRecords(source.Replace("MISSING", failed, StringComparison.Ordinal), complete: false));
        Assert.Equal(twin ?? expected, CallRecords(source.Replace("MISSING", repaired, StringComparison.Ordinal), complete: true));
    }

    // A5's Loan conflicts and located Unsupported_Kd over a program (with an `inspect` of its own), which Binding completes or rejects,
    // whose build has no validator finding (InternalInvariant_Kd under the mir pipeline) and no record of construction.
    private static string CallRecords(string source, bool complete)
    {
        var c = MirBuildTest.Compile("func inspect<T>(value: ref/T) => ()\n" + source);
        Assert.Equal(complete, c.Binding.Result.IsComplete);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records);
        new Stratum(c.Types).Run(program);
        return string.Join(" | ", Problems(c, program, [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }
}
