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
    // R4's m1: st's kept row stores x's borrow into c, whose Type argument gives Loan facts only (R4-M147), beside a Bad input, then y
    // written while r lives.
    private const string Kept = RetentionTest.Bad + "struct Ph<T>\n    public var n: i32 = 0\n    public init() => ()\n" +
        "func st(c: uniq/Ph<uniq/(ref/i32 during a) during e>, v: ref/i32 during a, b: uniq/Bad) => ()\nfunc g(b: uniq/Bad)\n    var x = 1\n" +
        "    var y = 2\n    let r = y@ref\n    var c = Ph<uniq/(ref/i32)>.init()\n    st(c@uniq, x@ref, b)\n    y = 3\n    let k = r@follow\n";

    // R4's G244 probes: v lent before a form, then written while the borrow lives; S, a struct a Case payload moves.
    private const string Lent = "    var v = 1\n    let r = v@ref\n";
    private const string Written = "    v = 2\n    let k = r@follow\n";
    private const string Moved = "struct S\n    public var n: i32\n    public init(n: i32) => self.n = n\n";

    // PLAN G1022's writes: a holder whose update has a custom set and a standard get over S.Output (HS<bool> is ill-formed: bool is not
    // Addable<bool>), and f's locals a write's right side reads, moves or borrows.
    private const string Writes = "func take(x: string) -> i32 => 5\nfunc at(x: string) -> isize => 0\nfunc k(x: i32) -> i32 => x\nfunc use(x: string) => ()\nstruct HS<S>\n" +
        "    S is Addable<S>\n    public var x: S.Output\n        set(value: S.Output) -> ()\n            storage = value@move\n" +
        "    public init(x: S.Output) => self.x = x@move\nfunc f(c: bool, h: uniq/HS<HOLDER>)\n    var t = \"t\"\n    var s = \"s\"\n" +
        "    var u = \"u\"\n    var y = 1\n    var m = 0\n    var xs: [2 of i32] = [0, 0]\n    var p = Pair.init()\n";

    // The codes PLAN G1022's write rows observe: A5's Loan conflicts and A1's MovedPlace_Kd and UninitializedPlace_Kd.
    private static DiagnosticCode[] WriteCodes => [.. LoanCodes, DiagnosticCode.MovedPlace_Kd, DiagnosticCode.UninitializedPlace_Kd];

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

    // SPEC 3.6, 23.1.2 (DECISIONS R4-M125): a local whose declaration Binding rejected (a failed Origin clause, a mismatched initializer,
    // a duplicate binding, each declaration of which owns its symbol) is one storage, as its valid twin's (§ replaced by the valid fragment)
    // is, so A5 and A1 report what the twin reports and no more:
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
    [InlineData("func f() -> i32\n    var §: i32\n    var h: i32\n    let p = h@ref\n    let k = p@follow\n    return k\n", "h", "g", "f: h@ref UninitializedPlace_Kd")]
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

    // SPEC 23.1.2, 3.6 (PLAN G247; DECISIONS R4-M149): an Opaque temp is no storage, so a Borrow kept past its statement that would lend
    // a Place in one (MirBuildTest.AnOpaqueTempLendsNothing; MirBuildTest.RejectedRoots: each use of `w` and `h` fails its Type's
    // constraint) carries no Loan, and A5 reports no conflict derived from the failed root, while the records independent of it stay: a
    // move in a runtime index, a range boundary, a key, a method's argument, an assigned value and an update's right operand (A1), and an
    // unrelated local's conflict (fail-before: a ComparisonLoanConflict_Kd at the root where a Borrow of it is kept). A conflict between two
    // uses Binding rejected goes with them (the twin's write while borrowed; R4-M149's lost class), and so does the temporary's conflict
    // of a group Binding rejects whole (q20; R4-M149's grouping rule), while a literal's Borrow keeps its temporary's. Each row: the
    // failed and the valid records.
    [Theory]
    [InlineData("let p = h@ref\n    inspect(p)", "", "")]
    [InlineData("see(w.arr[take(t@move)]@ref)\n    use(t)", "f: use(t) MovedPlace_Kd", "f: use(t) MovedPlace_Kd")]
    [InlineData("let s = w.xs[0..take(t@move)]\n    let k = s.length\n    use(t)", "f: use(t) MovedPlace_Kd", "f: use(t) MovedPlace_Kd")]
    [InlineData("let x = w.d[take(t@move)]\n    use(t)", "f: use(t) MovedPlace_Kd", "f: use(t) MovedPlace_Kd")]
    [InlineData("w.inner.put(take(t@move))\n    use(t)", "f: use(t) MovedPlace_Kd", "f: use(t) MovedPlace_Kd")]
    [InlineData("w.v = take(t@move)\n    use(t)", "f: use(t) MovedPlace_Kd", "f: use(t) MovedPlace_Kd")]
    [InlineData("w.v += take(t@move)\n    use(t)", "f: use(t) MovedPlace_Kd", "f: use(t) MovedPlace_Kd")]
    [InlineData("let p = w.v@ref\n    var y = 1\n    let q = y@ref\n    y = 2\n    see(q)\n    see(p)", "f: y = 2", "f: y = 2")]
    [InlineData("let p = w.v@uniq\n    w.v = 4\n    p@follow = 3", "", "f: w.v = 4")]
    [InlineData("let r = w.inner.pick()\n    w.inner.n = 2\n    see(r)", "", "f: w.inner.n = 2")]
    [InlineData("let p = (w@move).v@ref\n    see(p)", "", "f: (w@move)")]
    [InlineData("let p = 1@ref\n    let k = p@follow", "f: 1", "f: 1")]
    public void AnOpaqueTempKeepsTheIndependentRecords(string body, string failed, string valid)
    {
        foreach (var (key, expected) in new[] { ("S2", failed), ("i32", valid) })
        {
            var c = MirBuildTest.Compile(Prelude + MirBuildTest.RejectedRoots(key) + "    " + body + "\n");
            Assert.Equal(key == "S2", c.Binding.Issues.Any(static x => x.Code == DiagnosticCode.UnsatisfiedConstraint_Kd));
            var program = c.BuildMir();
            Assert.Empty(program.Findings);
            new Stratum(c.Types).Run(program);
            Assert.Equal(expected.Split(" | ", StringSplitOptions.RemoveEmptyEntries), Problems(c, program, [.. LoanCodes, DiagnosticCode.MovedPlace_Kd]));
        }
    }

    // SPEC 13.7.1 step 1, 13.7.2 step 1, 23.1.2 (PLAN G1022): a write whose target Binding rejected (an unresolved name, its index or
    // referent, a missing member, an update of a Property over an ill-formed holder Type) is derived from that failure after its right
    // side, which is evaluated first, so the right side's own failures stay: a second Move (A1) and a read under a live exclusive Loan (A5)
    // (fail-before: neither), also where locating a Place right side evaluates its index. The write's securing of the right side at the
    // destination Type (a Place's Copy or Reborrow) rests on the failure and adds nothing (before the rule, a Reborrow's InternalInvariant_Kd),
    // nor does a right side that reads only the failed target, and the twin whose target resolves is accepted with none; no row has a
    // construction record (Unsupported_Kd, InternalInvariant_Kd). Each row: the holder's Type argument, the body, the problems, and whether
    // Binding accepts the program.
    [Theory]
    [InlineData("i32", "missing = take(t@move)\n    use(t@move)", "f: t MovedPlace_Kd", false)]
    [InlineData("i32", "m = take(t@move)", "", true)]
    [InlineData("i32", "missing = k(missing)", "", false)]
    [InlineData("i32", "let r = y@uniq\n    missing = k(y)\n    r@follow = 2", "f: k(y)", false)]
    [InlineData("i32", "let r = y@uniq\n    r@follow = 2\n    m = k(y)", "", true)]
    [InlineData("i32", "missing += take(t@move)\n    use(t@move)", "f: t MovedPlace_Kd", false)]
    [InlineData("i32", "m += take(t@move)", "", true)]
    [InlineData("i32", "p.nofield = take(t@move)\n    use(t@move)", "f: t MovedPlace_Kd", false)]
    [InlineData("i32", "p.a = take(t@move)", "", true)]
    [InlineData("i32", "p.nofield = k(p.nofield)", "", false)]
    [InlineData("i32", "missing[0] = take(t@move)\n    use(t@move)", "f: t MovedPlace_Kd", false)]
    [InlineData("i32", "xs[0] = take(t@move)", "", true)]
    [InlineData("i32", "missing@follow = take(t@move)\n    use(t@move)", "f: t MovedPlace_Kd", false)]
    [InlineData("i32", "let q = m@uniq\n    q@follow = take(t@move)", "", true)]
    [InlineData("i32", "missing = s@move\n    use(s@move)", "f: s MovedPlace_Kd", false)]
    [InlineData("i32", "u = s@move", "", true)]
    [InlineData("i32", "missing = if c => take(t@move) else => 2\n    use(t@move)", "f: t MovedPlace_Kd", false)]
    [InlineData("i32", "m = if c => take(t@move) else => 2", "", true)]
    [InlineData("bool", "h.x += take(t@move)\n    use(t@move)", "f: t MovedPlace_Kd", false)]
    [InlineData("i32", "h.x += take(t@move)", "", true)]
    [InlineData("bool", "h.x += h.x", "", false)]
    [InlineData("i32", "let q = m@uniq\n    missing = q", "", false)]
    [InlineData("i32", "let r = y@uniq\n    missing = y\n    r@follow = 2", "", false)]
    [InlineData("i32", "missing = xs[at(t@move)]\n    use(t@move)", "f: t MovedPlace_Kd", false)]
    public void AWriteWhoseTargetBindingRejectedBuildsItsRightSide(string holder, string body, string expected, bool accepted)
    {
        var c = MirBuildTest.Compile(Prelude + Writes.Replace("HOLDER", holder, StringComparison.Ordinal) + "    " + body + "\n");
        Assert.Equal(accepted, c.Binding.Issues.Count == 0);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        new Stratum(c.Types).Run(program);
        Assert.DoesNotContain(program.Problems.All.ToArray(), static x => x.IsRecord);
        Assert.Equal(expected.Split(" | ", StringSplitOptions.RemoveEmptyEntries), Problems(c, program, WriteCodes));
    }

    // PLAN G1022's exposure (SPEC 23.1.1): valid programs Binding rejects only through its Unsupported_Kd limits reach the derived write
    // too (tests/milestones/Milestone41.kimi's `self.selfWeak = selfWeak@move`, Milestone43.kimi's `owner.level = 30`), and keep the
    // records they had before it: Binding's limits, no construction record, and (w2) the false UninitializedPlace_Kd the derived write's
    // site gives the construction receiver `self` while `v` is uninitialized, a root of the target the site names as read (a PLAN open
    // issue of SiteRoots, D-799's follow-up; not G1022's). Each row: the program, Binding's codes, the problems.
    [Theory]
    [InlineData("struct Node\n    public let w: Weak<rc/Node>\n    public let v: i32\n    public init(w: Weak<rc/Node>, v: i32)\n        self.w = w@move\n        self.v = v\n", "Unsupported_Kd Unsupported_Kd", "init: self.w = w@move UninitializedPlace_Kd")]
    [InlineData("open struct Device\n    public var level: i32\n        set(value: i32) -> ()\n            storage = value\n    protected init(level: i32)\n        self.level = level\nstruct Lamp : Device\n    public init(level: i32) : base(level) => ()\nfunc g()\n    var owner = Kimi.Intrinsics.makeObj<Lamp>(Lamp.init(40))\n    owner.level = 30\n", "Unsupported_Kd", "")]
    public void AWriteRejectedOnlyThroughALimitKeepsItsRecords(string source, string binding, string expected)
    {
        var c = MirBuildTest.Compile(Prelude + source);
        Assert.Equal(binding, string.Join(" ", c.Binding.Issues.Select(static x => x.Code)));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        new Stratum(c.Types).Run(program);
        Assert.DoesNotContain(program.Problems.All.ToArray(), static x => x.IsRecord);
        Assert.Equal(expected.Split(" | ", StringSplitOptions.RemoveEmptyEntries), Problems(c, program, WriteCodes));
    }

    // SPEC 23.1.2 (G240; DECISIONS BU-36): a call over storage whose part Binding did not bind keeps the rows that hold whatever the part
    // is, so A5 reports its repaired program's Loan conflicts beside it and no Unsupported_Kd at the call (fail-before: Unsupported_Kd at
    // keep2, stash and choose; i-field's at D.init), with no builder record or validator finding. A conflict the check finds over holder
    // states stays, also one whose value reaches its holder only through rows that hold whatever the failed part is (Selection: `result`
    // receives choose's result, which the contract's relation ties to pair and so to first's Loan, and the value-flow rule H makes it a
    // holder whatever its failed payload's Type; the check reads holder states, never which input failed: AGENTS Diagnostics, a record
    // kept under DECISIONS Q13). An independent conflict stays (m1: y written while r lives, also in its repair). A genuine limit stays its record at the call, the depth limit (the
    // owner's 「G272: (1)」, R4-M153), beside the failed part (a Field of height 66, a growing Field) and in its repair, and so do X and T,
    // which reach T's Box^64 Field.
    [Theory]
    [InlineData(RetentionTest.Keep2, "Missing", "i32", "g: v = 2")]
    [InlineData(RetentionTest.Stash, "Missing", "i32", "g: x = 3")]
    [InlineData(RetentionTest.Pick, "missing", "s", "g: x = 2")]
    [InlineData(RetentionTest.Handle, "Missing", "i32", "")]
    [InlineData(RetentionTest.Selection + "    first.value = 5\n    inspect(result@ref)\n", "missing", "source", "run: first.value = 5")]
    [InlineData(Kept, "Missing", "i32", "g: y = 3")]
    [InlineData(RetentionTest.Fielded, "C{x}\n        origin x.s == zz", "C{x}\n        origin x.s == s", "")]
    [InlineData(RetentionTest.Fielded, "Missing", "C{x}\n        origin x.s == s", "")]
    [InlineData(RetentionTest.Tall, "Missing", "i32", "g: f(b, null) OwnershipDepthLimit_Kd")]
    [InlineData(RetentionTest.Growing, "Missing", "i32", "h: f(g, null) OwnershipDepthLimit_Kd")]
    [InlineData(RetentionTest.Beyond + "func g(t: uniq/T, x: uniq/X)\n    fx(x, null)\n    ft(t, null)\n", "Missing", "i32", "g: fx(x, null) OwnershipDepthLimit_Kd | g: ft(t, null) OwnershipDepthLimit_Kd")]
    public void ACallOverRejectedStorageKeepsItsRows(string source, string failed, string repaired, string expected)
    {
        Assert.Equal(expected, CallRecords(source.Replace("MISSING", failed, StringComparison.Ordinal), complete: false));
        Assert.Equal(expected, CallRecords(source.Replace("MISSING", repaired, StringComparison.Ordinal), complete: true));
    }

    // SPEC 6.5.1, 23.1.2 (G242, G245's enum half; DECISIONS BU-37): an enum whose Case A Binding did not bind publishes its other Cases, so
    // MIR built over them (a payload-free and a payload construction, a borrowed and a moved payload, inside an Option, as a default and as
    // a Field initializer, a generic instance's, a move out of a payload, a call's result, matches over B) has no validator finding and no
    // record of construction, and A5 and A1 report the valid twin's records (fail-before: an Arity or Projection finding, InternalInvariant_Kd,
    // nothing analyzed). A conflict that rests on the failed Case is its dependent and goes (Sel's Loan of v is
    // held only through the failed Found, its repair's conflict).
    [Theory]
    [InlineData("enum E\n    A(MISSING)\n    B\nfunc g()\n" + Lent + "    let e: E = .B\n" + Written, "Missing", "i32", "g: v = 2")]
    [InlineData("enum E\n    A(MISSING)\n    B(i32)\nfunc g()\n" + Lent + "    let e: E = .B(1)\n" + Written, "Missing", "i32", "g: v = 2")]
    [InlineData("enum E {s}\n    A(MISSING)\n    B(ref/i32 during s)\nfunc g()\n    var v = 1\n    let e: E = .B(v@ref)\n    v = 2\n    inspect(e@ref)\n", "Missing", "i32", "g: v = 2")]
    [InlineData("enum E\n    A(MISSING)\n    B\nfunc g()\n" + Lent + "    let o: Option<E> = .Some(.B)\n" + Written, "Missing", "i32", "g: v = 2")]
    [InlineData("enum E\n    A(MISSING)\n    B\nfunc f(k: E = .B) => ()\nfunc g()\n" + Lent + "    f()\n" + Written, "Missing", "i32", "g: v = 2")]
    [InlineData("enum E\n    A(MISSING)\n    B\nstruct H\n    public var e: E = .B\n    public init() => ()\nfunc g()\n" + Lent + "    let h = H.init()\n" + Written, "Missing", "i32", "g: v = 2")]
    [InlineData("enum E<T>\n    A(MISSING)\n    B(T)\nfunc g()\n" + Lent + "    let e: E<i32> = .B(1)\n    match e\n        .B(let y) => ()\n        _ => ()\n" + Written, "Missing", "i32", "g: v = 2")]
    [InlineData("enum E\n    A(MISSING)\n    B(i32, string)\nfunc take(s: string) => ()\nfunc g()\n" + Lent + "    let e: E = .B(1, \"a\")\n    match e@move\n        .B(_, let s) => take(s@move)\n        _ => ()\n" + Written, "Missing", "i32", "g: v = 2")]
    [InlineData(Moved + "enum E\n    A(MISSING)\n    B(S)\nfunc g()\n    let s = S.init(1)\n    let e: E = .B(s@move)\n    let t = s@move\n", "Missing", "i32", "g: s MovedPlace_Kd")]
    [InlineData("enum Sel {s}\n    Found(ref/i32 during MISSING)\n    None\nfunc pick(x: ref/i32) -> Sel during x => .None\nfunc g()\n    var v = 1\n    let s = pick(v@ref)\n    v = 2\n    inspect(s@ref)\n", "missing", "s", "", "g: v = 2")]
    [InlineData("enum Pick {s}\n    First(ref/i32 during MISSING)\n    Second(ref/i32 during s)\n    public func choose(self: ref/Self, useFirst: bool) -> i32 => 0\nfunc g(useFirst: bool) -> i32\n    var x = 1\n    let pair: Pick = .Second(x@ref)\n    return pair.choose(useFirst)\n", "missing", "s", "")]
    [InlineData("enum E\n    A(MISSING)\n    B(i32)\nfunc g(e: E) -> i32 => match e\n    .B(let y) => y\n    _ => 0\n", "Missing", "i32", "")]
    [InlineData("enum E\n    A(MISSING)\n    B\nfunc g(e: E) -> i32 => match e\n    .B => 1\n    _ => 0\n", "Missing", "i32", "")]
    [InlineData("enum E {s}\n    A(ref/i32 during MISSING)\n    B(i32)\nfunc g(e: E) -> i32 => match e\n    .B(let y) => y\n    _ => 0\n", "missing", "s", "")]
    public void AnEnumsOtherCasesBuildBesideItsFailedCase(string source, string failed, string repaired, string expected, string? twin = null)
    {
        Assert.Equal(expected, CallRecords(source.Replace("MISSING", failed, StringComparison.Ordinal), complete: false));
        Assert.Equal(twin ?? expected, CallRecords(source.Replace("MISSING", repaired, StringComparison.Ordinal), complete: true));
    }

    // SPEC 6.5.2, 14.8.1 (G242; DECISIONS BU-37): a form over the Case whose payload Binding did not bind stays Binding's record, a
    // construction's TypeMismatch_Kd and a pattern's InvalidPattern_Kd, and the MIR never projects that Case: no validator finding, and A5
    // reports the twin's conflict only where it does not rest on the failed construction (`.A(v@ref)`'s Loan is held only through it).
    [Theory]
    [InlineData("enum E\n    A(MISSING)\n    B\nfunc g()\n" + Lent + "    let e: E = .A(1)\n" + Written, "Missing", "i32", "TypeMismatch_Kd .A(1)", "g: v = 2", "g: v = 2")]
    [InlineData("enum E {s}\n    A(ref/i32 during MISSING)\n    B\nfunc g()\n    var v = 1\n    let e: E = .A(v@ref)\n    v = 2\n    inspect(e@ref)\n", "missing", "s", "TypeMismatch_Kd .A(v@ref)", "", "g: v = 2")]
    [InlineData("enum E\n    A(MISSING)\n    B(i32)\nfunc g(e: E) -> i32 => match e\n    .A(let x) => 1\n    .B(let y) => y\n", "Missing", "i32", "InvalidPattern_Kd let x", "", "")]
    [InlineData("enum E {s}\n    A(ref/i32 during MISSING)\n    B(i32)\nfunc g(e: E) -> i32 => match e\n    .A(let r) => r@follow\n    .B(let y) => y\n", "missing", "s", "InvalidPattern_Kd let r", "", "")]
    public void AFormOverAFailedCaseStaysBindingsRecord(string source, string failed, string repaired, string record, string expected, string twin)
    {
        var c = MirBuildTest.Compile("func inspect<T>(value: ref/T) => ()\n" + source.Replace("MISSING", failed, StringComparison.Ordinal));
        Assert.Contains(record, c.Binding.Issues.Select(static x => $"{x.Code} {x.Node.ToString().Split('\n')[0]}"));
        Assert.Equal(expected, CallRecords(source.Replace("MISSING", failed, StringComparison.Ordinal), complete: false));
        Assert.Equal(twin, CallRecords(source.Replace("MISSING", repaired, StringComparison.Ordinal), complete: true));
    }

    // A5's Loan conflicts, A1's MovedPlace_Kd and limit records (a located Unsupported_Kd, the depth limit) over a program (with an
    // `inspect` of its own), which Binding completes or rejects, whose build has no validator finding (InternalInvariant_Kd at check) and
    // no record of construction.
    private static string CallRecords(string source, bool complete)
    {
        var c = MirBuildTest.Compile("func inspect<T>(value: ref/T) => ()\n" + source);
        Assert.Equal(complete, c.Binding.Result.IsComplete);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records(MirRecordClass.Construction));
        new Stratum(c.Types).Run(program);
        return string.Join(" | ", Problems(c, program, [.. LoanCodes, DiagnosticCode.MovedPlace_Kd, DiagnosticCode.Unsupported_Kd, DiagnosticCode.OwnershipDepthLimit_Kd]));
    }
}
