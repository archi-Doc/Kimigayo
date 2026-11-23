// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>A5 on values derived from Binding's errors (SPEC 23.1.2; docs/dev/DECISIONS.md R4-M109): an input Binding rejected after
/// selection (a static Place's Borrow, the located limit) is its consumer's derived value, which carries no Loan, so A5 reports no
/// conflict derived from it, while the independent records beside it stay; since R7-O1 b1 a call position's relation rejects no input
/// (its row is A5's).</summary>
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

    // PLAN G1058's struct (DECISIONS Z27's probes): a part to move, s, beside a part to write, n.
    private const string TwoFields = "struct P\n    public var s: string\n    public var n: i32\n    public init(s: string, n: i32)\n" +
        "        self.s = s@move\n        self.n = n\n";

    // DECISIONS Z27's review twins (z27ir): a base struct whose `level` has a custom `set`, beside a part to move, name.
    private const string SetLevel = "open struct Device\n    public var name: string\n    public var level: i32\n        set(value: i32) -> ()\n" +
        "            storage = value\n    protected init(name: string, level: i32)\n        self.name = name@move\n        self.level = level\n";

    // The codes PLAN G1022's write rows observe: A5's Loan conflicts and A1's MovedPlace_Kd and UninitializedPlace_Kd.
    private static DiagnosticCode[] WriteCodes => [.. LoanCodes, DiagnosticCode.MovedPlace_Kd, DiagnosticCode.UninitializedPlace_Kd];

    // SPEC 23.1.2 (DECISIONS R4-M109; superseded for call positions only, ACallPositionsInputIsJudgedAsAValidOne): an argument Binding
    // rejects after selection, here a static Place's Borrow (the located limit until P42, DECISIONS BU-25), is an Opaque of the reference
    // the call takes, so a result kept past the statement carries no Loan on a temporary: no ComparisonLoanConflict_Kd beside Binding's
    // record, also where the rejected argument keeps an index call that moves twice (A1's MovedPlace_Kd stays).
    [Fact]
    public void ARejectedInputFormsNoLoan()
    {
        var c = MirBuildTest.Compile(Prelude + """
            struct Tally
                public var n: i32 = 0
                public init() => ()
            group State
                public var tallies: [2 of Tally] = [Tally.init(), Tally.init()]
            func pick(a: string, b: string) -> isize => 0
            func kept(s: string)
                let r = keep(State.tallies[pick(s@move, s@move)].n)
                inspect(r)
            """);
        var program = AnalyzeUnsupported(c, ["State.tallies[pick(s@move, s@move)].n"]);
        Assert.Equal(["kept: s MovedPlace_Kd"], Problems(c, program, [.. LoanCodes, DiagnosticCode.MovedPlace_Kd]));
    }

    // SPEC 15.6.1, 15.6.5, 23.1.2 (R7-O1 b1, superseding DECISIONS R4-M109 for call positions): an argument whose call-position relation the
    // premises do not prove (`c.n` and `chosen(...).n` outliving static) is no rejected input: the relation is a row the stratum judges, its
    // record A5's, and the argument is built and judged as a valid one, so its Loans are the records a valid argument's are: none derived
    // from borrowing through the shared `c`, A1's MovedPlace_Kd of a call that moves twice, and the true conflict of a literal's temporary
    // borrowed for static.
    [Fact]
    public void ACallPositionsInputIsJudgedAsAValidOne()
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
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Code == DiagnosticCode.UnprovenOriginRelation_Kd);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        new Stratum(c.Types).Run(program);
        Assert.Equal(["field", "kept"], Problems(c, program, [DiagnosticCode.UnprovenOriginRelation_Kd]).Select(static x => x.Split(':')[0]));
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
    // referent, a missing member, an update of a Property over an ill-formed holder Type), also through parentheses, whose Place is the
    // operand's (SPEC 3.4; DECISIONS Z16: the fact is read at the Place the target designates), is derived from that failure after its right
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
    [InlineData("i32", "let r = y@uniq\n    missing = y\n    r@follow = 2", "f: y", false)] // Z17: the located y read at its node (R6).
    [InlineData("i32", "missing = xs[at(t@move)]\n    use(t@move)", "f: t MovedPlace_Kd", false)]
    [InlineData("i32", "(missing) = take(t@move)\n    use(t@move)", "f: t MovedPlace_Kd", false)] // Z16: the Place a parenthesized target designates (SPEC 3.4).
    [InlineData("i32", "((missing)) = take(t@move)\n    use(t@move)", "f: t MovedPlace_Kd", false)]
    [InlineData("i32", "(missing) += take(t@move)\n    use(t@move)", "f: t MovedPlace_Kd", false)]
    [InlineData("i32", "(missing)++", "", false)]
    [InlineData("i32", "(p.nofield) = take(t@move)\n    use(t@move)", "f: t MovedPlace_Kd", false)]
    [InlineData("i32", "var q = missing\n    (q) = take(t@move)\n    use(t@move)", "f: t MovedPlace_Kd", false)]
    [InlineData("i32", "(m) = take(t@move)", "", true)]
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

    // SPEC 12, 13.7.1 step 1, 15.6.2, 23.1.2 (DECISIONS Z17): a rejected form's part its consumer acquires is read at its own node, as its
    // resolved twin's is (the row `missing = y` above: a read under a live exclusive Loan, at `y`), and A5 gets no access of a part used in
    // place, a kept part (every kept child is published in place until Binding publishes its form's use) or a part a kept borrow ends at
    // the site: a failed selection's receiver beside a live Loan on a sibling Field (a valid program's Weak limit, an unresolved member), a
    // kept exclusive borrow whose Loan ends at the site (R4-M70), and a located element or referent read, through a shared reference too,
    // add no Loan conflict beside Binding's records (an element read uninitialized stays A1's, at the element; the Weak limit's construction
    // gives no record since DECISIONS Z27, PLAN G1058 closed: fail-before, G1058's UninitializedPlace_Kd, D-983). A located element through a runtime index reads under a live Loan once, at the element,
    // where its Index guard's shared borrow is (R4-M56), as its resolved twins do (fail-before: a second conflict at the site), and a read
    // through a Deref under a live reborrow conflicts at the read, as its twin's; a located exclusive borrow is its built value (a
    // Temporary), whose Loan ends at its construction.
    [Theory]
    [InlineData(
        "struct Node\n    public let w: Weak<rc/Node>\n    public var v: i32\n    public init(w: Weak<rc/Node>, v: i32)\n        self.w = w@move\n" +
        "        self.v = v\nfunc f(w: Weak<rc/Node>) -> i32\n    var n: Node = Node.init(w@move, 1)\n    let r = n.v@uniq\n    let k = n.w@ref\n" +
        "    r@follow = 2\n    return 0\n",
        "")]
    [InlineData(
        "struct P\n    public var s: string\n    public var n: i32\n    public init(s: string, n: i32)\n        self.s = s@move\n        self.n = n\n" +
        "func f()\n    var p = P.init(\"s\", 1)\n    let r = p.s@uniq\n    let q = p.nofield\n    r@follow = \"t\"\n",
        "")]
    [InlineData("func k(a: uniq/i32, b: i32) -> i32 => 1\nfunc f() -> i32\n    var y = 1\n    let z = k(y@uniq, missing)\n    y = 2\n    return z\n", "")]
    [InlineData("func f()\n    var y: Option<i32> = .Some(1)\n    let z = match y@uniq\n        .Nope => 1\n        _ => 2\n    y = .None\n", "")]
    [InlineData("func f()\n    var a: [2 of i32]\n    missing = a[0]\n", "f: a[0] UninitializedPlace_Kd")]
    [InlineData("func f()\n    var y = 1\n    let r = y@uniq\n    missing = r@follow\n    y = 2\n", "")]
    [InlineData("struct Q\n    public var n: i32\n    public init(n: i32) => self.n = n\nfunc f(p: ref/Q)\n    missing = p.n\n", "")]
    [InlineData("func f(i: isize)\n    var a = [1, 2]\n    let r = a@uniq\n    missing = a[i]\n    r@follow = [3, 4]\n", "f: a[i]")]
    [InlineData("func f(i: isize)\n    var a = [1, 2]\n    var m = 0\n    let r = a@uniq\n    m = a[i]\n    r@follow = [3, 4]\n", "f: a[i]")]
    [InlineData("func f(i: isize)\n    var a = [1, 2]\n    let r = a@uniq\n    let m = a[i]\n    r@follow = [3, 4]\n", "f: a[i]")]
    [InlineData("func f()\n    var y = 1\n    let r = y@uniq\n    let q = r@follow@uniq\n    missing = r@follow\n    q@follow = 3\n", "f: r@follow")]
    [InlineData("func f()\n    var y = 1\n    let r = y@uniq\n    let q = r@follow@uniq\n    let m = r@follow\n    q@follow = 3\n", "f: r@follow")]
    [InlineData("func inspect(v: ref/i32) => ()\nfunc f()\n    var y = 1\n    let r = y@ref\n    missing = y@uniq\n    inspect(r)\n", "f: y@uniq")]
    [InlineData("func f()\n    var y = 1\n    missing = y@uniq\n    y = 2\n", "")]
    public void ARejectedFormsPartIsReadAtItsOwnNode(string source, string expected)
    {
        var c = MirBuildTest.Compile(source);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        new Stratum(c.Types).Run(program);
        Assert.DoesNotContain(program.Problems.All.ToArray(), static x => x.IsRecord);
        Assert.Equal(expected.Split(" | ", StringSplitOptions.RemoveEmptyEntries), Problems(c, program, WriteCodes));
    }

    // DECISIONS Z17c (SPEC 12.2, 15.6.7, 23.1.2): an Error node's kept part is read as the call builder reads an operand, so A5 reports it
    // where its decided twin does: a Copy part the form reads at its node where it or a later kept part builds code, else at the site's
    // node (a-call, r1-two-reads, r1-call-last, r3-kept-op-right at the call or operation); a kept invocation argument's written exclusive
    // borrow a reservation the site activates (r1-uniq-then-shared, c-two-uniq, c-shared-then-uniq: the twin's activation records;
    // r4-kept-temp-move's CallReservationConflict_Kd), never a live Loan (r5-limit-uniq-later and D-1003 (2)'s false conflict gone; the G724
    // rows are valid but the limit), while a tuple's element and a Case payload stay live Loans (c-tuple-later, c-tuple-read, cr-opt-excess:
    // B6). A non-Copy part, a Copy part's own hold and a raw borrow keep their records (r4-kept-live, r4d-copyhold, z-raw-missing); a
    // Subject is read at its node (r3-kept-match); a part at the last that builds only by its path (v[i]) is read at its selection
    // (n27-ridx, R4-M56). Fail-before: base lacks the gained records and keeps the false one. Each row, its decided twin after it: a
    // program and its A5 problems, code and line:column, as the CLI reports them.
    [Theory]
    [InlineData("func k(x: i32) -> i32 => x\nfunc g(n: i32, m: i32) -> i32 => n\nfunc f()\n    var y = 1\n    let r = y@uniq\n    missing(y)\n    r@follow = 2\n", "ComparisonLoanConflict_Kd@6:5")] // a-call
    [InlineData("func k(x: i32) -> i32 => x\nfunc g(n: i32, m: i32) -> i32 => n\nfunc f()\n    var y = 1\n    let r = y@uniq\n    k(y)\n    r@follow = 2\n", "ComparisonLoanConflict_Kd@6:5")] // a-call-dec
    [InlineData("func k(x: i32) -> i32 => x\nfunc g(n: i32, m: i32) -> i32 => n\nfunc kq3(a: uniq/i32, b: ref/i32) -> i32 => 1\nfunc f() -> i32\n    var y = 1\n    var z = 2\n    let r = y@uniq\n    missing(y, z)\n    r@follow = 2\n    return 0\n", "ComparisonLoanConflict_Kd@8:5")] // r1-two-reads
    [InlineData("func k(x: i32) -> i32 => x\nfunc g(n: i32, m: i32) -> i32 => n\nfunc kq3(a: uniq/i32, b: ref/i32) -> i32 => 1\nfunc f() -> i32\n    var y = 1\n    var z = 2\n    let r = y@uniq\n    g(y, z)\n    r@follow = 2\n    return 0\n", "ComparisonLoanConflict_Kd@8:5")] // r1-tw-two-reads
    [InlineData("func k(x: i32) -> i32 => x\nfunc g(n: i32, m: i32) -> i32 => n\nfunc kq3(a: uniq/i32, b: ref/i32) -> i32 => 1\nfunc f() -> i32\n    var y = 1\n    var z = 2\n    let r = y@uniq\n    let w = g(missing, y)\n    r@follow = 2\n    return 0\n", "ComparisonLoanConflict_Kd@8:13")] // r1-call-last
    [InlineData("func k(x: i32) -> i32 => x\nfunc g(n: i32, m: i32) -> i32 => n\nfunc kq3(a: uniq/i32, b: ref/i32) -> i32 => 1\nfunc f() -> i32\n    var y = 1\n    var z = 2\n    let r = y@uniq\n    let w = g(1, y)\n    r@follow = 2\n    return 0\n", "ComparisonLoanConflict_Kd@8:13")] // r1-tw-call-last
    [InlineData("func f()\n    var y = 1\n    let r = y@uniq\n    let z = missing + y\n    r@follow = 2\n", "ComparisonLoanConflict_Kd@4:13")] // r3-kept-op-right
    [InlineData("func f()\n    var y = 1\n    let r = y@uniq\n    let z = 1 + y\n    r@follow = 2\n", "ComparisonLoanConflict_Kd@4:13")] // r3-tw-op-right
    [InlineData("func kq3(a: uniq/i32, b: ref/i32) -> i32 => 1\nfunc f() -> i32\n    var y = 1\n    let q = y@ref\n    return missing(y@uniq, q)\n", "CallActivationConflict_Kd@5:12")] // r1-uniq-then-shared
    [InlineData("func kq3(a: uniq/i32, b: ref/i32) -> i32 => 1\nfunc f() -> i32\n    var y = 1\n    let q = y@ref\n    return kq3(y@uniq, q)\n", "CallActivationConflict_Kd@5:12")] // r1-tw-uniq-then-shared
    [InlineData("func g(x: i32) -> i32 => x\nfunc f()\n    var y = 1\n    let r = y@uniq\n    let z = missing(r, g(y))\n", "ComparisonLoanConflict_Kd@5:24")] // r4-kept-live
    [InlineData("func k(a: uniq/i32, b: i32) -> i32 => b\nfunc g(x: i32) -> i32 => x\nfunc f()\n    var y = 1\n    let r = y@uniq\n    let z = k(r, g(y))\n", "ComparisonLoanConflict_Kd@6:18")] // r4-tw-live
    [InlineData("func g(x: i32) -> i32 => x\nfunc f()\n    var y = 1\n    let z = missing(y@uniq, g(y@move))\n", "CallReservationConflict_Kd@4:31")] // r4-kept-temp-move
    [InlineData("func kk(a: uniq/i32, b: i32) -> i32 => b\nfunc g(x: i32) -> i32 => x\nfunc f()\n    var y = 1\n    let z = kk(y@uniq, g(y@move))\n", "CallReservationConflict_Kd@5:26")] // r4-tw-temp-move
    [InlineData("func g2(a: uniq/i32) -> i32 => 1\nfunc f()\n    var y = 1\n    let q = y@ref\n    let z = missing(q, g2(y@uniq))\n", "CallActivationConflict_Kd@5:24")] // r4d-copyhold
    [InlineData("func kq(a: ref/i32, b: i32) -> i32 => b\nfunc g2(a: uniq/i32) -> i32 => 1\nfunc f()\n    var y = 1\n    let q = y@ref\n    let z = kq(q, g2(y@uniq))\n", "CallActivationConflict_Kd@6:19")] // r4d-tw-copyhold
    [InlineData("contract C<T>\n    associate Item\nstruct P\n    public init() => ()\n    public func make(a: uniq/i32, b: i32) -> i32 => b\n    public func make2(a: ref/i32, b: i32) -> i32 => b\nstruct S\n    Self is C<i32>\n    associate C<i32>.Item is P\nfunc g(x: i32) -> i32 => x\nfunc g2(a: uniq/i32) -> i32 => 1\nfunc f() -> i32\n    var y = 1\n    return S.(C<i32>).Item.make(y@uniq, y)\n", "")] // r5-limit-uniq-read
    [InlineData("contract C<T>\n    associate Item\nstruct P\n    public init() => ()\n    public func make(a: uniq/i32, b: i32) -> i32 => b\n    public func make2(a: ref/i32, b: i32) -> i32 => b\nstruct S\n    Self is C<i32>\n    associate C<i32>.Item is P\nfunc g(x: i32) -> i32 => x\nfunc g2(a: uniq/i32) -> i32 => 1\nfunc f() -> i32\n    var y = 1\n    return P.make(y@uniq, y)\n", "")] // r5-tw-limit-uniq-read
    [InlineData("contract C<T>\n    associate Item\nstruct P\n    public init() => ()\n    public func make(a: uniq/i32, b: i32) -> i32 => b\n    public func make2(a: ref/i32, b: i32) -> i32 => b\nstruct S\n    Self is C<i32>\n    associate C<i32>.Item is P\nfunc g(x: i32) -> i32 => x\nfunc g2(a: uniq/i32) -> i32 => 1\nfunc f() -> i32\n    var y = 1\n    return S.(C<i32>).Item.make(y@uniq, g(y))\n", "")] // r5-limit-uniq-later
    [InlineData("contract C<T>\n    associate Item\nstruct P\n    public init() => ()\n    public func make(a: uniq/i32, b: i32) -> i32 => b\n    public func make2(a: ref/i32, b: i32) -> i32 => b\nstruct S\n    Self is C<i32>\n    associate C<i32>.Item is P\nfunc g(x: i32) -> i32 => x\nfunc g2(a: uniq/i32) -> i32 => 1\nfunc f() -> i32\n    var y = 1\n    return P.make(y@uniq, g(y))\n", "")] // r5-tw-limit-uniq-later
    [InlineData("func g(x: i32) -> i32 => x\nfunc kk2(a: uniq/i32, b: uniq/i32) -> i32 => 1\nfunc f() -> i32\n    var y = 1\n    let t = (y@uniq, g(y), missing)\n    return 0\nfunc h(a: uniq/i32) -> i32 => 1\nfunc kk(a: uniq/i32, b: i32) -> i32 => b\nfunc kk3(a: i32, b: i32) -> i32 => b\nfunc kq2(a: ref/i32, b: uniq/i32) -> i32 => 1\nfunc kd(a: uniq/i32, c: ref/i32, b: i32 = missing) -> i32 => b\n", "ComparisonLoanConflict_Kd@5:22")] // c-tuple-later
    [InlineData("func g(x: i32) -> i32 => x\nfunc kk2(a: uniq/i32, b: uniq/i32) -> i32 => 1\nfunc f() -> i32\n    var y = 1\n    let t = (y@uniq, g(y))\n    return 0\nfunc h(a: uniq/i32) -> i32 => 1\nfunc kk(a: uniq/i32, b: i32) -> i32 => b\nfunc kk3(a: i32, b: i32) -> i32 => b\nfunc kq2(a: ref/i32, b: uniq/i32) -> i32 => 1\nfunc kd(a: uniq/i32, c: ref/i32, b: i32 = missing) -> i32 => b\n", "ComparisonLoanConflict_Kd@5:22")] // c-tw-tuple-later
    [InlineData("func g(x: i32) -> i32 => x\nfunc kk2(a: uniq/i32, b: uniq/i32) -> i32 => 1\nfunc f() -> i32\n    var y = 1\n    let t = (y@uniq, y, missing)\n    return 0\nfunc h(a: uniq/i32) -> i32 => 1\nfunc kk(a: uniq/i32, b: i32) -> i32 => b\nfunc kk3(a: i32, b: i32) -> i32 => b\nfunc kq2(a: ref/i32, b: uniq/i32) -> i32 => 1\nfunc kd(a: uniq/i32, c: ref/i32, b: i32 = missing) -> i32 => b\n", "ComparisonLoanConflict_Kd@5:22")] // c-tuple-read
    [InlineData("func g(x: i32) -> i32 => x\nfunc kk2(a: uniq/i32, b: uniq/i32) -> i32 => 1\nfunc f() -> i32\n    var y = 1\n    let t = (y@uniq, y)\n    return 0\nfunc h(a: uniq/i32) -> i32 => 1\nfunc kk(a: uniq/i32, b: i32) -> i32 => b\nfunc kk3(a: i32, b: i32) -> i32 => b\nfunc kq2(a: ref/i32, b: uniq/i32) -> i32 => 1\nfunc kd(a: uniq/i32, c: ref/i32, b: i32 = missing) -> i32 => b\n", "ComparisonLoanConflict_Kd@5:13")] // c-tw-tuple-read
    [InlineData("func g(x: i32) -> i32 => x\nfunc kk2(a: uniq/i32, b: uniq/i32) -> i32 => 1\nfunc f() -> i32\n    var y = 1\n    return missing(y@uniq, y@uniq)\nfunc h(a: uniq/i32) -> i32 => 1\nfunc kk(a: uniq/i32, b: i32) -> i32 => b\nfunc kk3(a: i32, b: i32) -> i32 => b\nfunc kq2(a: ref/i32, b: uniq/i32) -> i32 => 1\nfunc kd(a: uniq/i32, c: ref/i32, b: i32 = missing) -> i32 => b\n", "CallReservationConflict_Kd@5:12")] // c-two-uniq
    [InlineData("func g(x: i32) -> i32 => x\nfunc kk2(a: uniq/i32, b: uniq/i32) -> i32 => 1\nfunc f() -> i32\n    var y = 1\n    return kk2(y@uniq, y@uniq)\nfunc h(a: uniq/i32) -> i32 => 1\nfunc kk(a: uniq/i32, b: i32) -> i32 => b\nfunc kk3(a: i32, b: i32) -> i32 => b\nfunc kq2(a: ref/i32, b: uniq/i32) -> i32 => 1\nfunc kd(a: uniq/i32, c: ref/i32, b: i32 = missing) -> i32 => b\n", "CallReservationConflict_Kd@5:12")] // c-tw-two-uniq
    [InlineData("func g(x: i32) -> i32 => x\nfunc kk2(a: uniq/i32, b: uniq/i32) -> i32 => 1\nfunc f() -> i32\n    var y = 1\n    let q = y@ref\n    return missing(q, y@uniq)\nfunc h(a: uniq/i32) -> i32 => 1\nfunc kk(a: uniq/i32, b: i32) -> i32 => b\nfunc kk3(a: i32, b: i32) -> i32 => b\nfunc kq2(a: ref/i32, b: uniq/i32) -> i32 => 1\nfunc kd(a: uniq/i32, c: ref/i32, b: i32 = missing) -> i32 => b\n", "CallActivationConflict_Kd@6:12")] // c-shared-then-uniq
    [InlineData("func g(x: i32) -> i32 => x\nfunc kk2(a: uniq/i32, b: uniq/i32) -> i32 => 1\nfunc f() -> i32\n    var y = 1\n    let q = y@ref\n    return kq2(q, y@uniq)\nfunc h(a: uniq/i32) -> i32 => 1\nfunc kk(a: uniq/i32, b: i32) -> i32 => b\nfunc kk3(a: i32, b: i32) -> i32 => b\nfunc kq2(a: ref/i32, b: uniq/i32) -> i32 => 1\nfunc kd(a: uniq/i32, c: ref/i32, b: i32 = missing) -> i32 => b\n", "CallActivationConflict_Kd@6:12")] // c-tw-shared-then-uniq
    [InlineData("func g(x: i32) -> i32 => x\nfunc f()\n    var y = 1\n    let e: Option<uniq/i32> = .Some(y@uniq, g(y))\n", "ComparisonLoanConflict_Kd@4:45")] // cr-opt-excess
    [InlineData("func g(x: i32) -> i32 => x\nfunc f()\n    var y = 1\n    let t = (Option.Some(y@uniq), g(y))\n", "ComparisonLoanConflict_Kd@4:35")] // cr-tw-opt
    [InlineData("func inspect(v: ref/[2 of i32]) => ()\nfunc k(x: i32) => ()\nfunc f()\n    var v: [2 of i32] = [1, 2]\n    var i = 0\n    let r = v@uniq\n    missing(v[i])\n    inspect(r)\n", "ComparisonLoanConflict_Kd@7:13")] // n27-ridx
    [InlineData("func inspect(v: ref/[2 of i32]) => ()\nfunc k(x: i32) => ()\nfunc f()\n    var v: [2 of i32] = [1, 2]\n    var i = 0\n    let r = v@uniq\n    k(v[i])\n    inspect(r)\n", "ComparisonLoanConflict_Kd@7:7 | ComparisonLoanConflict_Kd@7:5")] // n27-tw-ridx
    [InlineData("func f()\n    var y: Option<i32> = .Some(1)\n    let r = y@uniq\n    let z = match y\n        .Nope => 1\n        _ => 2\n    r@follow = .None\n", "ComparisonLoanConflict_Kd@4:19")] // r3-kept-match
    [InlineData("func f()\n    var y: Option<i32> = .Some(1)\n    let r = y@uniq\n    let z = match y\n        .Some(_) => 1\n        _ => 2\n    r@follow = .None\n", "ComparisonLoanConflict_Kd@4:19")] // r3-kept-match-ok
    [InlineData("func f()\n    var h = 1\n    let r = h@uniq\n    let p = h@raw/Missing\n    r@follow = 2\n", "ComparisonLoanConflict_Kd@4:13")] // z-raw-missing
    [InlineData("func f()\n    var h = 1\n    let r = h@uniq\n    let p = h@raw\n    r@follow = 2\n", "ComparisonLoanConflict_Kd@4:13")] // z-raw-ok
    public void AKeptPartIsReadAsTheCallBuilderReadsAnOperand(string source, string expected)
    {
        var c = MirBuildTest.Compile(source);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        new Stratum(c.Types).Run(program);
        Assert.Equal(expected.Split(" | ", StringSplitOptions.RemoveEmptyEntries), program.Problems.All.ToArray()
            .Where(static x => !x.IsRecord && LoanCodes.Contains(x.Code) && x.Subject.Module == 0)
            .Select(x => $"{x.Code}@{At(c.SyntaxOf(x.Subject).Span.Start)}"));

        string At(int offset) => $"{source[..offset].Count(static x => x == '\n') + 1}:{offset - source.LastIndexOf('\n', Math.Max(offset - 1, 0))}";
    }

    // PLAN G1022's exposure (SPEC 23.1.1): valid programs Binding rejects only through its Unsupported_Kd limits reach the derived write
    // too (tests/milestones/Milestone41.kimi's `self.selfWeak = selfWeak@move`, Milestone43.kimi's `owner.level = 30`), and keep the
    // records they had before it: Binding's limits and no construction record; (w2) the construction receiver `self`, the target's kept
    // operand, is an enclosing use whose own bit the constructor sets, so `v` uninitialized gives no record (PLAN G1058's first form,
    // D-983, closed by DECISIONS Z27; fail-before: a false UninitializedPlace_Kd at the write). Each row: the program, Binding's codes,
    // the problems.
    [Theory]
    [InlineData("struct Node\n    public let w: Weak<rc/Node>\n    public let v: i32\n    public init(w: Weak<rc/Node>, v: i32)\n        self.w = w@move\n        self.v = v\n", "Unsupported_Kd Unsupported_Kd", "")]
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

    // PLAN G1058, G1186 (DECISIONS Z27; SPEC 13.7.1 steps 1-2, 15.1.2, 15.1.3, 6.3.4, 23.1.2): a derived write takes its right side at its
    // own node (step 1), then evaluates its target's kept operands (step 2), and its site names the in-place ones as enclosing uses, which
    // A1 checks for construction only, their own bits and their ancestors'. A target over a partly moved root, a constructing receiver or
    // a placed Field gives no record (p03, p04 at 10:5, p19, p22, t05 at 10:9, t06, t08, t16, Milestone 41 at 11:9; fail-before: a false
    // MovedPlace_Kd or UninitializedPlace_Kd at the write); one whose root is moved whole, never constructed or a moved reference keeps it
    // (p07, p08, p09), as a runtime index reads its array whole (t09) and the valid twin has none (p10). Step 2's operands are checked at
    // their own nodes (p11's uninitialized index at 4:7 and t11's second move at 3:16, fail-before: at the write; p12's MovedPlace_Kd at
    // 6:9 and p21's conflict, fail-before: none), after step 1 (t12, u1: the right side is read, or its failed take is a site at its node,
    // before step 2 moves it). A target written through its `set` call takes its receiver whole (SPEC 11.3.1: `uniq/Self`), so Binding
    // publishes the receiver as the call's input, a kept use: v1's inherited custom `set` after a base Field's Move keeps MovedPlace_Kd
    // beside the limit (fail-before: Unsupported_Kd alone), as its decided twin v3 reports it. Each row: the program, then its records,
    // Binding's and the analyses', code@line:col.
    [Theory]
    [InlineData(TwoFields + "func f()\n    var p = P.init(\"s\", 1)\n    let a = p.s@move\n    p.nofield = 1\n", "UnresolvedBinding_Kd@10:5")] // p03
    [InlineData(TwoFields + "func f()\n    var p = P.init(\"s\", 1)\n    let a = p.s@move\n    p.n = 2\n", "")] // p10, valid
    [InlineData("struct R\n    public let a: i32\n    public var b: i32\n    public init(a: i32, b: i32)\n        self.a = a\n        self.b = b\nfunc f() -> i32\n    var x = R.init(1, 2)\n    let t = x.b@move\n    x.a = 3\n    return x.b\n", "InaccessibleBinding_Kd@10:5 | MovedPlace_Kd@11:12")] // p04
    [InlineData(TwoFields + "func f()\n    var p = P.init(\"s\", 1)\n    let a = p@move\n    p.nofield = 1\n", "UnresolvedBinding_Kd@10:5 | MovedPlace_Kd@10:5")] // p07
    [InlineData(TwoFields + "func f()\n    var p: P\n    p.nofield = 1\n", "UnresolvedBinding_Kd@9:5 | UninitializedPlace_Kd@9:5")] // p08
    [InlineData(TwoFields + "func f()\n    var q = P.init(\"s\", 1)\n    let r = q@uniq\n    let r2 = r@move\n    r.nofield = 1\n", "UnresolvedBinding_Kd@11:5 | MovedPlace_Kd@11:5")] // p09
    [InlineData("func f()\n    var a: [2 of i32] = [1, 2]\n    var i: isize\n    a[i].nofield = 1\n", "UnresolvedBinding_Kd@4:5 | UninitializedPlace_Kd@4:7")] // p11
    [InlineData("func at(t: string) -> isize => 0\nfunc use(t: string) => ()\nfunc f()\n    var t = \"a\"\n    missing[at(t@move)] = 1\n    use(t@move)\n", "UnresolvedBinding_Kd@5:5 | MovedPlace_Kd@6:9")] // p12
    [InlineData("struct Q\n    public var m: i32\n    public init(m: i32) => self.m = m\nstruct P\n    public var s: string\n    public var q: Q\n    public init(s: string, q: Q)\n        self.s = s@move\n        self.q = q@move\nfunc f()\n    var p = P.init(\"s\", Q.init(1))\n    let a = p.s@move\n    p.q.nofield = 1\n", "UnresolvedBinding_Kd@13:5")] // p19
    [InlineData("func k(x: i32) -> isize => 0\nfunc f()\n    var y = 1\n    let r = y@uniq\n    missing[k(y)] = 1\n    r@follow = 2\n", "UnresolvedBinding_Kd@5:5 | ComparisonLoanConflict_Kd@5:13")] // p21
    [InlineData(TwoFields + "func f(y: string)\n    var p = P.init(\"s\", 1)\n    let a = p.s@move\n    p.nofield = y\n    let b = y@move\n", "UnresolvedBinding_Kd@10:5 | TransferRequired_Kd@10:17")] // p22
    [InlineData(TwoFields + "func f(c: bool)\n    var p = P.init(\"s\", 1)\n    while c\n        p.nofield = 1\n        let a = p.s@move\n", "UnresolvedBinding_Kd@10:9 | MovedPlace_Kd@11:17")] // t05
    [InlineData(TwoFields + "func g(q: P) -> ()\n    let f = func [var q@move] ()\n        let a = q.s@move\n        q.nofield = 1\n    f@move()\n", "UnresolvedBinding_Kd@10:9")] // t06
    [InlineData(TwoFields + "func f()\n    var a: [2 of P] = [P.init(\"a\", 1), P.init(\"b\", 2)]\n    let x = a[1]@move\n    a[0].nofield = 1\n", "UnresolvedBinding_Kd@10:5")] // t08
    [InlineData(TwoFields + "func f()\n    var a: [2 of P] = [P.init(\"a\", 1), P.init(\"b\", 2)]\n    let i: isize = 0\n    let x = a[1]@move\n    a[i].nofield = 1\n", "UnresolvedBinding_Kd@11:5 | MovedPlace_Kd@11:5")] // t09
    [InlineData("func at(t: string) -> isize => 0\nfunc f(y: string)\n    missing[at(y@move)] = y@move\n", "UnresolvedBinding_Kd@3:5 | MovedPlace_Kd@3:16")] // t11
    [InlineData("func at(t: string) -> isize => 0\nfunc f(y: string)\n    missing[at(y@move)] = y\n", "UnresolvedBinding_Kd@3:5 | TransferRequired_Kd@3:27")] // t12
    [InlineData("struct Q\n    public var m: string\n    public init(m: string) => self.m = m@move\nstruct P\n    public var q: Q\n    public var n: i32\n    public init(n: i32)\n        self.q = Q.init(\"m\")\n        self.q.nofield = 1\n        self.n = n\n", "UnresolvedBinding_Kd@9:9")] // t16
    [InlineData("struct Bad\n    Self is Copy\n    public var s: string = \"\"\n    public init() => ()\nfunc at(b: Bad) -> isize => 0\nfunc f(b: Bad)\n    missing[at(b@move)] = b\n", "UnsatisfiedConstraint_Kd@2:5 | UnresolvedBinding_Kd@7:5")] // u1
    [InlineData(SetLevel + "struct Lamp : Device\n    public init(name: string, level: i32) : base(name@move, level) => ()\nfunc g()\n    var owner = Lamp.init(\"desk\", 40)\n    let n = owner.name@move\n    owner.level = 30\n", "Unsupported_Kd@14:5 | MovedPlace_Kd@14:5")] // z27ir's v1
    [InlineData("struct Device\n    public var name: string\n    public var level: i32\n        set(value: i32) -> ()\n            storage = value\n    public init(name: string, level: i32)\n        self.name = name@move\n        self.level = level\nfunc g()\n    var owner = Device.init(\"desk\", 40)\n    let n = owner.name@move\n    owner.level = 30\n", "MovedPlace_Kd@12:5")] // z27ir's v3, decided
    [InlineData("tests/milestones/Milestone41.kimi", "Unsupported_Kd@8:26 | Unsupported_Kd@10:27 | Unsupported_Kd@15:19 | Unsupported_Kd@17:16 | Unsupported_Kd@18:11 | Unsupported_Kd@26:11 | Unsupported_Kd@31:11 | Unsupported_Kd@36:32 | Unsupported_Kd@37:15 | Unsupported_Kd@41:16 | Unsupported_Kd@42:11")] // Milestone 41, valid but for Binding's Weak limits
    public void ADerivedWritesTargetIsEnclosedNotRead(string source, string expected)
    {
        source = source.StartsWith("tests/", StringComparison.Ordinal)
            ? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../..", source)).Replace("\r\n", "\n", StringComparison.Ordinal) : source;
        var c = MirBuildTest.Compile(source);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        new Stratum(c.Types).Run(program);
        Assert.DoesNotContain(program.Problems.All.ToArray(), static x => x.IsRecord);
        string[] records = [.. c.Binding.Issues.Select(x => $"{x.Code}@{At(x.Node.Span.Start)}"),
            .. program.Problems.All.ToArray().Where(static x => !x.IsRecord && x.Subject.Module == 0).Select(x => $"{x.Code}@{At(c.SyntaxOf(x.Subject).Span.Start)}")];
        Assert.Equal(expected.Split(" | ", StringSplitOptions.RemoveEmptyEntries), records);

        string At(int offset) => $"{source[..offset].Count(static x => x == '\n') + 1}:{offset - source.LastIndexOf('\n', Math.Max(offset - 1, 0))}";
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

    // SPEC 13.3.1, 23.1.2 (PLAN G1073; rule W): a shift Binding rejected (a float's, NonIntegerOperand_Kd) is a temp of no Type, whose
    // conversion inside a loop is stored into an element of an array the body later slices: a read value of no Type encloses no `during
    // self` layer (Encloses), so A5 adds nothing to the shift's record (fail-before: an InvalidOperationException at MirCases.TypeOf from
    // Encloses, the driver's CheckFaulted_Kd; the diagnostic snapshot's Milestone31 float-shift mutation).
    [Fact]
    public void ARejectedShiftsValueEnclosesNoLayer()
    {
        var c = MirBuildTest.Compile(
            "func f() -> i32\n    let half = 1.5@bits<u32>@bits<f32>\n    var floatBytes: [4 of u8] = [0, 0, 0, 0]\n    for i in 0..4\n" +
            "        floatBytes[i] = (half >> (i * 8))@wrap<u8>\n    let s = floatBytes[..]\n    return 0\n");
        Assert.Equal([DiagnosticCode.NonIntegerOperand_Kd], c.Binding.Issues.Select(static x => x.Code));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        new Stratum(c.Types).Run(program);
        Assert.Empty(Problems(c, program, [.. LoanCodes, DiagnosticCode.MovedPlace_Kd]));
    }

    // PLAN G1073 (rule W; MirValidator's Bounded): an Opaque temp of no Type read as the first operand of an operator that takes its Type
    // from its operands, with a Loan live across it so A5's step walk reads the operator, in a body MirValidator accepts: a value of no
    // Type is no raw pointer, so Add and Sub keep no displaced value (Keeps) and A5 reports nothing (fail-before for Add and Sub: an
    // InvalidOperationException at MirCases.TypeOf from Keeps; Mul is no displacement either way).
    [Theory]
    [InlineData("Add")]
    [InlineData("Sub")]
    [InlineData("Mul")]
    public void AnOperandOfNoTypeIsNoRawPointer(string op)
    {
        var c = MirBuildTest.Compile("func w() -> i32 => 1\n");
        var program = c.BuildMir();
        program.Problems.Clear();
        var body = new MirBody();
        body.Reset(Body(program, "w").Key, default);
        body.AddLocal(new(BoundType.I32, MirLocalKind.Return, default));
        var reference = c.Types.Reference(SemanticsKind.Ref, BoundType.I32);
        MirPlace Local(BoundType? type, MirLocalKind kind) => new(body.AddLocal(new(type, kind, default)));
        var (x, r) = (Local(BoundType.I32, MirLocalKind.Var), Local(reference, MirLocalKind.Var));
        var (unknown, value) = (Local(null, MirLocalKind.Temp), Local(BoundType.I32, MirLocalKind.Temp));
        SourceInfo At(int id) => new(new(0, id), 0, true);
        int Constant(long n) => body.AddOperand(MirOperand.Const(body.Const(new(MirConstKind.Integer, BoundType.I32, n))));
        var block = body.AddBlock();
        body.Push(block, MirStatement.Assign(x, MirRvalue.Use(Constant(2)), At(1)));
        body.Push(block, MirStatement.Assign(r, MirRvalue.Ref(MirRefKind.Shared, x, reference), At(2)));
        body.Push(block, MirStatement.Assign(unknown, MirRvalue.Opaque(null, body.AddOpaque(new(0, 3), [], 0, 0)), At(3)));
        var operands = body.AddOperand(MirOperand.Move(unknown));
        _ = Constant(1);
        body.Push(block, MirStatement.Assign(value, MirRvalue.Binary(Enum.Parse<MirBinaryOp>(op), operands, proven: true), At(4)));
        body.Push(block, MirStatement.Of(MirStatementKind.FakeRead, r, At(5)));
        body.Push(block, MirStatement.Assign(new(0), MirRvalue.Use(body.AddOperand(MirOperand.Move(value))), At(6)));
        body.Terminate(block, MirTerminator.Return(At(7)));
        new Stratum(c.Types).Run(program, Validated(c, body));
        Assert.Empty(program.Problems.All.ToArray());
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
