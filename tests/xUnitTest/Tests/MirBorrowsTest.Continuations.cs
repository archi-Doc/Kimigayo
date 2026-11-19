// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>A5 on the assumed continuation of a Never form (SPEC 14.10.3, 15.6; PLAN G305, DECISIONS R4-M158): the continuation continues
/// from the state after the form's operands, where every check applies, so a value it uses is live back across the form; a P1 join takes no
/// assumed state, so a use after the join keeps nothing live on a continuation that flows into it.</summary>
public sealed partial class MirBorrowsTest
{
    // The continuation shapes: a Never callee, a consumer, counted callees, a callee with a shared parameter, a receiver with an
    // exclusive `self` with a Never callee of its Type, and holders of a reference with and without a `drop`, with a consumer of the first;
    // a comparison's right operands: pickers, callees with an exclusive or a consumed parameter (of a string, a Tuple and a Key, whose
    // comparison is its Contract's), a call with a shared one.
    private const string ContinuationShapes = """
        func take(t: string) -> Never => $abort("t")
        func consume(t: string) => ()
        func cnt(t: string) -> i32 => 0
        func cost(t: string) -> isize => 0
        func pair(a: ref/string, b: isize) -> i32 => 0
        func lose(c: Counter) -> Never => $abort("t")
        func eat(g: Guard) => ()
        func pick(a: string, b: string) -> string => "p"
        func pickB(b: bool, s: string) -> string => "p"
        func poke(t: uniq/string) -> string => "p"
        func poke2(t: uniq/string, s: string) -> string => "p"
        func chew(t: string) -> string => t@move
        func pickT(a: (string, i32), b: (string, i32)) -> (string, i32) => a@move
        func pokeT(u: uniq/(string, i32)) -> (string, i32) => ("q", 2)
        func chewT(u: (string, i32)) -> (string, i32) => u@move
        func mkT(a: ref/string, b: i32, s: string) -> (string, i32) => ("m", 1)
        func mkK(a: ref/string, b: i32, s: string) -> Key => Key.init()
        func pickK(a: Key, b: Key) -> Key => a@move
        func pokeK(u: uniq/Key) -> Key => Key.init()
        struct Key
            Self is Comparable
            public var n: i32 = 0
            public init() => ()
            public func equals(self: ref/Self, other: ref/Self) -> bool => true
            public func compare(self: ref/Self, other: ref/Self) -> i32 => 0
        struct Counter
            var count: i32
            public init() => self.count = 0
            public func bump(self: uniq/Self, by: i32) => self.count = self.count + by
        struct Guard {w}
            public let r: ref/string during w
            public init(r: ref/string during w) => self.r = r
            drop => ()
        struct View {w}
            public let r: ref/string during w
            public init(r: ref/string during w) => self.r = r

        """;

    // A Move or write before a Never form, in its operands or earlier, conflicts with a Loan a use on its continuation keeps (a Never
    // call, also in a `defer`, `$abort`, `return`, `exit`, `exit to`, `yield`, `continue`, `loop`; a placement's secured right side, a
    // shared argument, a later statement, a reservation's activation, a holder's `drop` as cleanup, SPEC 14.10.3, 15.6.6). Every error
    // row asserts its location. A reachable twin, named with an n, errs at the corresponding location: ad4n for ad4, ad4r and ad4a; s1n
    // for s2, ab1 and s8; exton, yldn, df2n, s7bn, df1n, d1n and d2n each for the row before it; contn for s9 and cont. c1, s3, s7 and
    // r2 have no twin. Without a conflicting access (ad4m, r3, d1v, d1x) or with the use after a P1 join (s4, s5, s6) the program is
    // valid. The one exception (SPEC 14.10.3, 13.4; PLAN G396 X-5 N2, D-900): a control transfer out of a comparison's operand
    // abandons the comparison, whose own Loans end before the assumed continuation, so an exclusive borrow (cmp1, cmp2r, cmpo, cmpr,
    // cmpt, cmpk, the nested cmpn) or a Move (cmpm, cmptm) of the inspected root there is valid; its reachable twin cmp1n errs. Loans
    // others need stay: a reference used later (cmp3), a call's argument (cmpc) or reservation (cmpv), a call in a tuple operator's left
    // operand (cmpl) or in a Contract comparison's (cmplk). Nothing else abandons it: `$abort` (cmpa), an exit its operand catches, whose
    // join only assumed paths reach (cmpw; in cmpwy that exit abandons only the comparison opened inside its target), and a join with a
    // path that did not abandon it (cmpj).
    // Each row is a body of `func <name>(c: bool) -> i32` after `var x = "x"`, returning 0, and its one problem or none.
    [Theory]
    [InlineData("ad4", "    let y = \"y\"\n    var us: [1 of ref/string] = [y@ref]\n    us[take(x@move)] = x@ref\n", "x")]
    [InlineData("ad4r", "    let y = \"y\"\n    var us: [1 of ref/string] = [y@ref]\n    us[(return cnt(x@move))] = x@ref\n", "x")]
    [InlineData("ad4a", "    let y = \"y\"\n    var us: [1 of ref/string] = [y@ref]\n    us[$abort(x@move)] = x@ref\n", "x")]
    [InlineData("ad4n", "    let y = \"y\"\n    var us: [1 of ref/string] = [y@ref]\n    us[cost(x@move)] = x@ref\n", "x")]
    [InlineData("ad4m", "    let y = \"y\"\n    var us: [1 of ref/string] = [y@ref]\n    us[take(\"t\")] = x@ref\n", "")]
    [InlineData("c1", "    _ = pair(x@ref, take(x@move))\n", "x")]
    [InlineData("s2", "    let r = x@ref\n    take(x@move)\n    inspect(r)\n", "x")]
    [InlineData("ab1", "    let r = x@ref\n    $abort(x@move)\n    inspect(r)\n", "x")]
    [InlineData("s8", "    let r = x@ref\n    consume(x@move)\n    $abort(\"t\")\n    inspect(r)\n", "x")]
    [InlineData("s1n", "    let r = x@ref\n    consume(x@move)\n    inspect(r)\n", "x")]
    [InlineData(
        "exto",
        "    let r = x@ref\n    let v = label w: do\n        if c => exit to w 1\n        exit to w cnt(x@move)\n        inspect(r)\n        exit to w 2\n",
        "x")]
    [InlineData(
        "exton",
        "    let r = x@ref\n    let v = label w: do\n        if c => exit to w 1\n        let n = cnt(x@move)\n        inspect(r)\n        exit to w n\n",
        "x")]
    [InlineData("yld", "    let r = x@ref\n    let v: i32 = if c\n        yield cnt(x@move)\n        inspect(r)\n        yield 2\n    else => 1\n", "x")]
    [InlineData("yldn", "    let r = x@ref\n    let v: i32 = if c\n        let n = cnt(x@move)\n        inspect(r)\n        yield n\n    else => 1\n", "x")]
    [InlineData("df2", "    let r = x@ref\n    do\n        defer\n            take(x@move)\n            inspect(r)\n        cnt(\"t\")\n", "x")]
    [InlineData("df2n", "    let r = x@ref\n    do\n        defer\n            consume(x@move)\n            inspect(r)\n        cnt(\"t\")\n", "x")]
    [InlineData("s3", "    let r = x@ref\n    x = \"z\"\n    take(\"t\")\n    inspect(r)\n", "x = \"z\"")]
    [InlineData("s7", "    let r = x@ref\n    x = \"z\"\n    loop => ()\n    inspect(r)\n", "x = \"z\"")]
    [InlineData("s7b", "    let r = x@ref\n    loop\n        x = \"z\"\n    inspect(r)\n", "x = \"z\"")]
    [InlineData("s7bn", "    let r = x@ref\n    while c\n        x = \"z\"\n    inspect(r)\n", "x = \"z\"")]
    [InlineData("s9", "    let r = x@ref\n    while c\n        x = \"z\"\n        exit\n        inspect(r)\n", "x = \"z\"")]
    [InlineData("cont", "    let r = x@ref\n    while c\n        x = \"z\"\n        continue\n        inspect(r)\n", "x = \"z\"")]
    [InlineData("contn", "    let r = x@ref\n    while c\n        x = \"z\"\n        inspect(r)\n", "x = \"z\"")]
    [InlineData("df1", "    let r = x@ref\n    do\n        defer => take(\"t\")\n        x = \"z\"\n    inspect(r)\n", "x = \"z\"")]
    [InlineData("df1n", "    let r = x@ref\n    do\n        defer => cnt(\"t\")\n        x = \"z\"\n    inspect(r)\n", "x = \"z\"")]
    [InlineData("d1", "    let g = Guard.init(x@ref)\n    x = \"z\"\n    take(\"t\")\n", "x = \"z\"")]
    [InlineData("d1n", "    let g = Guard.init(x@ref)\n    x = \"z\"\n", "x = \"z\"")]
    [InlineData("d1v", "    let g = View.init(x@ref)\n    x = \"z\"\n    take(\"t\")\n", "")]
    [InlineData("d1x", "    let g = Guard.init(x@ref)\n    eat(g@move)\n    x = \"z\"\n    take(\"t\")\n", "")]
    [InlineData("d2", "    let g = Guard.init(x@ref)\n    loop\n        x = \"z\"\n", "x = \"z\"")]
    [InlineData("d2n", "    let g = Guard.init(x@ref)\n    while c\n        x = \"z\"\n", "x = \"z\"")]
    [InlineData("s4", "    let r = x@ref\n    if c\n        take(\"t\")\n        consume(x@move)\n    inspect(r)\n", "")]
    [InlineData("s5", "    let r = x@ref\n    if c\n        take(x@move)\n    inspect(r)\n", "")]
    [InlineData("s6", "    let r = x@ref\n    if c\n        consume(x@move)\n        return 1\n    inspect(r)\n", "")]
    [InlineData("r2", "    var k = Counter.init()\n    k.bump(lose(k@move))\n", "k CallReservationConflict_Kd")]
    [InlineData("r3", "    var k = Counter.init()\n    k.bump((return 1))\n", "")]
    [InlineData("cmp1", "    _ = x == pick((return 1), poke(x@uniq))\n", "")]
    [InlineData("cmp1n", "    _ = x == pick(\"t\", poke(x@uniq))\n", "poke(x@uniq) CallActivationConflict_Kd")]
    [InlineData("cmp2r", "    _ = x == label w: do\n        return 1\n        _ = poke(x@uniq)\n        exit to w \"c\"\n", "")]
    [InlineData("cmpo", "    label o: do\n        _ = x == pick((exit to o), poke(x@uniq))\n", "")]
    [InlineData("cmpr", "    let r = x@ref\n    _ = r == pick((return 1), poke(x@uniq))\n", "")]
    [InlineData("cmpm", "    _ = x == pick((return 1), chew(x@move))\n", "")]
    [InlineData("cmpt", "    var t = (\"a\", 1)\n    _ = t == pickT((return 1), pokeT(t@uniq))\n", "")]
    [InlineData("cmptm", "    var t = (\"a\", 1)\n    _ = t == pickT((return 1), chewT(t@move))\n", "")]
    [InlineData("cmpk", "    var q = Key.init()\n    _ = q == pickK((return 1), pokeK(q@uniq))\n", "")]
    [InlineData("cmpn", "    var y = \"y\"\n    _ = x == pickB(y == pick((return 1), poke(y@uniq)), poke(x@uniq))\n", "")]
    [InlineData("cmp3", "    let r = x@ref\n    _ = x == pick((return 1), poke(x@uniq))\n    inspect(r)\n", "poke(x@uniq) CallActivationConflict_Kd")]
    [InlineData("cmpc", "    _ = pair(x@ref, cost(pick((return 1), poke(x@uniq))))\n", "poke(x@uniq) CallActivationConflict_Kd")]
    [InlineData("cmpv", "    _ = poke2(x@uniq, pick((return 1), poke(x@uniq)))\n", "poke(x@uniq) CallReservationConflict_Kd")]
    [InlineData("cmpl", "    var t = (\"a\", 1)\n    _ = mkT(x@ref, (return 1), poke(x@uniq)) == t\n", "poke(x@uniq) CallActivationConflict_Kd")]
    [InlineData("cmplk", "    var q = Key.init()\n    _ = mkK(x@ref, (return 1), poke(x@uniq)) < q\n", "poke(x@uniq) CallActivationConflict_Kd")]
    [InlineData("cmpa", "    _ = x == pick($abort(\"t\"), poke(x@uniq))\n", "poke(x@uniq) CallActivationConflict_Kd")]
    [InlineData(
        "cmpw",
        "    _ = x == label w: do\n        take(\"t\")\n        exit to w \"b\"\n        _ = poke(x@uniq)\n        exit to w \"c\"\n",
        "poke(x@uniq) CallActivationConflict_Kd")]
    [InlineData(
        "cmpwy",
        "    var y = \"y\"\n    _ = x == label w: do\n        take(\"t\")\n        _ = y == pick((exit to w \"b\"), poke(y@uniq))\n        _ = poke(x@uniq)\n        exit to w \"c\"\n",
        "poke(x@uniq) CallActivationConflict_Kd")]
    [InlineData("cmpj", "    _ = x == pick(if c => (return 1) else => take(\"t\"), poke(x@uniq))\n", "poke(x@uniq) CallActivationConflict_Kd")]
    public void ALoanStaysLiveIntoANeverFormsContinuation(string name, string body, string problem)
        => Assert.Equal(
            problem.Length == 0 ? [] : [$"{name}: {problem}"],
            Problems($"{ContinuationShapes}func {name}(c: bool) -> i32\n    var x = \"x\"\n{body}    return 0\n"));

    // SPEC 14.10.3 (X-5 N2's invalid twin): only the abandoned comparison's own Loan ends, so in cmp3 r's Loan, which inspect(r) needs,
    // witnesses the conflict on the assumed continuation (fail-before: the comparison's Loan witnessed it); and a read of the root a Move
    // left Moved on that continuation keeps A1's record alone (cmpm with `inspect(x)`).
    [Fact]
    public void AnAbandonedComparisonEndsOnlyItsOwnLoans()
    {
        Assert.Equal(
            ["poke(x@uniq): Anchor x@ref, Use inspect(r), Reservation x@uniq"],
            Witnessed($"{Prelude}{ContinuationShapes}func f() -> i32\n    var x = \"x\"\n    let r = x@ref\n    _ = x == pick((return 1), poke(x@uniq))\n    inspect(r)\n    return 0\n"));
        var c = MirBuildTest.Compile($"{Prelude}{ContinuationShapes}func moved() -> i32\n    var x = \"x\"\n    _ = x == pick((return 1), chew(x@move))\n    inspect(x)\n    return 0\n");
        Assert.Equal(["moved: x MovedPlace_Kd"], Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.MovedPlace_Kd]));
    }
}
