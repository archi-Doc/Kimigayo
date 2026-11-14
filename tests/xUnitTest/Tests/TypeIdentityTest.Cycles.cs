// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// PLAN G881b (SPEC 8.2.3 Cycles and grounds, 8.3.2, 8.3.3, 8.5.2, 8.10, 15.6.4): an identity cycle through a projection
// (`T.(Cy2).X.(Mid).Next is T`) is no error, and the rows of its Types are the least fixpoint of ContractSolver's walk
// (EnumerateClosed), whichever Type a request enters first or spells.
public partial class TypeIdentityTest
{
    private const string CyclePrelude =
        "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n" +
        "    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n" +
        "    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n" +
        "    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n" +
        "    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n" +
        "    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\n" +
        "contract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\n" +
        "func need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\n";

    private const string Cy =
        "contract Cy\n    associate X is Inner\ncontract Cy0\n    associate X is Inner0\ncontract Cy2\n" +
        "    associate X is Mid\ncontract Cy20\n    associate X is Mid0\n";

    private const string C3 =
        "contract Ca\n    associate X is Mid\ncontract Ca0\n    associate X is Mid0\ncontract Cb\n    associate Y is MidO\n" +
        "contract Cc\n    associate Z is Ca\ncontract Cc0\n    associate Z is Ca0\nfunc needCa<X>(x: ref/X)\n    X is Ca\n" +
        "    return\n";

    private const string Q =
        "contract Q3\ncontract MidR\n    associate Next is Q3\ncontract Q2\n    associate H is MidR\ncontract Q20\n" +
        "contract MidQ\n    associate Next is Q2\ncontract MidQ0\n    associate Next is Q20\ncontract InnerQ\n" +
        "    associate G is MidQ\ncontract InnerQ0\n    associate G is MidQ0\ncontract Mid2Q\n    associate Next is InnerQ\n" +
        "contract Mid2Q0\n    associate Next is InnerQ0\ncontract CyQ\n    associate X is Mid2Q\ncontract CyQ0\n" +
        "    associate X is Mid2Q0\nfunc needQ2<X>(x: ref/X)\n    X is Q2\n    return\nfunc needQ3<X>(x: ref/X)\n" +
        "    X is Q3\n    return\n";

    private const string Qr =
        "contract MidQI\n    associate Next is Inner\ncontract MidQI0\n    associate Next is Inner0\ncontract InnerQI\n" +
        "    associate G is MidQI\ncontract InnerQI0\n    associate G is MidQI0\ncontract Mid2QI\n" +
        "    associate Next is InnerQI\ncontract Mid2QI0\n    associate Next is InnerQI0\ncontract CyQI\n" +
        "    associate X is Mid2QI\ncontract CyQI0\n    associate X is Mid2QI0\n";

    private const string Wr =
        "contract W2\n    func absorb(self: uniq/Self, p: raw/u8)\ncontract W20\n    func absorb(self: uniq/Self, p: i32)\n" +
        "contract MidW2\n    associate Next is W2\ncontract MidW20\n    associate Next is W20\ncontract CyW\n" +
        "    associate X is MidW2\ncontract CyW0\n    associate X is MidW20\n";

    private const string Ex =
        "contract CyE\n    associate X is Mid\n    Self.(CyE).X.(Mid).Next is Self\ncontract CyE0\n    associate X is Mid0\n" +
        "    Self.(CyE0).X.(Mid0).Next is Self\n";

    private const string Named =
        "contract NamedF\n    associate F\ncontract NamedG\n    associate G\n";

    private const string Selfs =
        "contract MidS\n    associate Next is InnerS\ncontract InnerS\n    associate G is MidS\nfunc needS<X>(x: ref/X)\n" +
        "    X is InnerS\n    return\n";

    private const string Mk =
        "contract Mk\n    associate Next is Mk\n    associate Up\n    associate G\n    Self.(Mk).Next.(Mk).Up is Self\n" +
        "    Self.(Mk).G is Callable<(raw/u8) -> Self.(Mk).Up>\n    func next(self: ref/Self) -> ref/Self.(Mk).Next\n" +
        "    func getG(self: ref/Self) -> ref/Self.(Mk).G\n";

    private const string Mx =
        "contract ZC\ncontract ZC0\ncontract B2\n    associate Y\ncontract A2\n    associate X is B2\n    associate Z is ZC\n" +
        "    Self.(A2).X.(B2).Y is Self.(A2).Z\ncontract A20\n    associate X is B2\n    associate Z is ZC0\n" +
        "    Self.(A20).X.(B2).Y is Self.(A20).Z\ncontract OuterA\n    associate N is A2\ncontract OuterA0\n" +
        "    associate N is A20\nfunc needZ<X>(x: ref/X)\n    X is ZC\n    return\n";

    private const string Mz =
        "contract ZC\ncontract ZC0\ncontract A3\n    associate X\n    associate Z is ZC\n    Self.(A3).X is Self.(A3).Z\n" +
        "contract A30\n    associate X\n    associate Z is ZC0\n    Self.(A30).X is Self.(A30).Z\ncontract OuterB\n" +
        "    associate N is A3\ncontract OuterB0\n    associate N is A30\nfunc needZ<X>(x: ref/X)\n    X is ZC\n    return\n";

    // Each row is a whole program after CyclePrelude; a valid row has no error, an invalid twin its records' codes. On G881's head
    // (63c42bdf1, the skip guard): the invalid hcy2, hcy2o, hw, hext and ambcy were accepted (class 1), the valid cy2, cy2o, cy2m,
    // cyget2, ambcyv, nestu, r3, r3o, r3x, r4, r4o, hr3bare, pm, pmr and ext rejected. Two-link cycle (cy2*, ord*: either spelling,
    // side, order and check order; discovery cyget2, amb*; nosup, selfsup: no evidence from a fact that needs its own judgment),
    // three-link cycle whose root has its Contract only through the cycle (c3*), rows that need two and three rounds (r3*, r4*), a
    // cycle reached through a prefix's mate (pm*), a requirement writer (hw*), a cycle a Contract premise supplies (ext*, an
    // extension round), a parameter identity beside the cycle (nest*: the readings' nested demands), and retention through each
    // (h*: the contract hands what the cycle supplies; the bare twins hand nothing with a raw input). Guards of the extension's reach
    // beside the rounds: a recursive Contract supplying an identity at every depth (u-*, rec*: the walk ends, the base's answers) and
    // an identity a Contract supplies at a prefix of a mate's chain (mate*).
    public static TheoryData<string, string, string> CycleRows => new()
    {
        { "cy2", Cy + "func f<T>(t: ref/T)\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    need(t)\n", string.Empty },
        { "cy2o", Cy + "func f<T>(t: ref/T)\n    T.(Cy2).X.(Mid).Next is T\n    T is Cy2\n    need(t)\n", string.Empty },
        { "cy2x", Cy + "func f<T>(x: ref/T.(Cy2).X.(Mid).Next)\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    need(x)\n", string.Empty },
        { "cy2n", Cy + "func f<T>(t: ref/T)\n    T is Cy20\n    T.(Cy20).X.(Mid0).Next is T\n    need(t)\n", "UnprovenConstraint_Kd" },
        { "cy2m", Cy + "func f<T>(t: ref/T)\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    need(t)\n    let g = t.get()\n    need(t)\n", string.Empty },
        { "cyget2", Cy + "func f<T>(t: ref/T)\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    let g = t.get()\n    return\n", string.Empty },
        { "ord1", Cy + "func f<T>(t: ref/T, x: ref/T.(Cy2).X.(Mid).Next)\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    need(x)\n    need(t)\n", string.Empty },
        { "ord2", Cy + "func f<T>(t: ref/T, x: ref/T.(Cy2).X.(Mid).Next)\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    need(t)\n    need(x)\n", string.Empty },
        { "nestu", Cy + "func f<T, U>(u: ref/U)\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    U is T\n    need(u)\n", string.Empty },
        { "nestun", Cy + "func f<T, U>(u: ref/U)\n    T is Cy20\n    T.(Cy20).X.(Mid0).Next is T\n    U is T\n    need(u)\n", "UnprovenConstraint_Kd" },
        { "nestp", Cy + "func f<T, U>(x: ref/U.(Inner).F)\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    U is T\n    return\n", string.Empty },
        { "ambcy", Cy + Named + "func f<T>(t: ref/T, e: ref/T.F)\n    T is Cy2\n    T is NamedF\n    T.(Cy2).X.(Mid).Next is T\n    return\n", "AmbiguousBinding_Kd" },
        { "ambcyv", Cy + Named + "func f<T>(t: ref/T, e: ref/T.F)\n    T is Cy2\n    T is NamedG\n    T.(Cy2).X.(Mid).Next is T\n    return\n", string.Empty },
        { "nosup", Cy + "func f<T>(t: ref/T)\n    T.(Cy2).X.(Mid).Next is T\n    need(t)\n", "InvalidConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd" },
        { "selfsup", Selfs + "func f<T>(t: ref/T)\n    T.(InnerS).G.(MidS).Next is T\n    needS(t)\n", "InvalidConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd" },
        { "hcy2", Cy + "func fjc2<T>(dst: uniq/T, p: raw/u8)\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    return\nfunc fjc2Bad<T>(x: T) -> ()\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fjc2<T>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", "ComparisonLoanConflict_Kd" },
        { "hcy2bare", Cy + "func fjc2<T>(dst: uniq/T, p: raw/u8)\n    T is Cy20\n    T.(Cy20).X.(Mid0).Next is T\n    return\nfunc fjc2Bad<T>(x: T) -> ()\n    T is Cy20\n    T.(Cy20).X.(Mid0).Next is T\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fjc2<T>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", string.Empty },
        { "hcy2o", Cy + "func fjc2<T>(dst: uniq/T, p: raw/u8)\n    T.(Cy2).X.(Mid).Next is T\n    T is Cy2\n    return\nfunc fjc2Bad<T>(x: T) -> ()\n    T.(Cy2).X.(Mid).Next is T\n    T is Cy2\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fjc2<T>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", "ComparisonLoanConflict_Kd" },
        { "c3", C3 + "func f<T, U, V>(t: ref/T, u: ref/U, v: ref/V)\n    U is Cb\n    V is Cc\n    V.(Cc).Z is T\n    T.(Ca).X.(Mid).Next is U\n    U.(Cb).Y.(MidO).Next is V\n    need(u)\n    needO(v)\n    needCa(t)\n", string.Empty },
        { "c3o", C3 + "func f<T, U, V>(t: ref/T, u: ref/U, v: ref/V)\n    U.(Cb).Y.(MidO).Next is V\n    T.(Ca).X.(Mid).Next is U\n    V.(Cc).Z is T\n    V is Cc\n    U is Cb\n    need(u)\n    needO(v)\n    needCa(t)\n", string.Empty },
        { "c3n", C3 + "func f<T, U, V>(t: ref/T, u: ref/U, v: ref/V)\n    U is Cb\n    V is Cc0\n    V.(Cc0).Z is T\n    T.(Ca0).X.(Mid0).Next is U\n    U.(Cb).Y.(MidO).Next is V\n    need(u)\n", "UnprovenConstraint_Kd" },
        { "c3a", C3 + "func f<T, U, V>(t: ref/T, u: ref/U, v: ref/V)\n    U is Cb\n    V is Cc\n    V.(Cc).Z is T\n    T.(Ca).X.(Mid).Next is U\n    U.(Cb).Y.(MidO).Next is V\n    return\n", string.Empty },
        { "c3b", C3 + "func f<T, U, V>(t: ref/T, u: ref/U, v: ref/V)\n    U is Cb\n    V is Cc\n    T is Ca\n    T.(Ca).X.(Mid).Next is U\n    U.(Cb).Y.(MidO).Next is V\n    need(u)\n    needO(v)\n    needCa(t)\n", string.Empty },
        { "c3c", C3 + "func f<T, U, V>(t: ref/T, u: ref/U, v: ref/V)\n    U is Cb\n    V is Cc\n    T is Ca\n    V.(Cc).Z is T\n    T.(Ca).X.(Mid).Next is U\n    U.(Cb).Y.(MidO).Next is V\n    need(u)\n    needO(v)\n    needCa(t)\n", string.Empty },
        { "c3d2", C3 + "func f<T, U, V>(t: ref/T, u: ref/U, v: ref/V)\n    U is Cb\n    V is Cc\n    V.(Cc).Z is T\n    T.(Ca).X.(Mid).Next is U\n    need(u)\n    needCa(t)\n", string.Empty },
        { "c3d", C3 + "func f<T, U, V>(t: ref/T, u: ref/U, v: ref/V)\n    U is Cb\n    V is Cc\n    V.(Cc).Z is T\n    T.(Ca).X.(Mid).Next is U\n    need(u)\n    needO(v)\n    needCa(t)\n", "UnprovenConstraint_Kd" },
        { "c3x", C3 + "func f<T, U, V>(x: ref/T.(Ca).X.(Mid).Next, y: ref/U.(Cb).Y.(MidO).Next, z: ref/V.(Cc).Z)\n    U is Cb\n    V is Cc\n    V.(Cc).Z is T\n    T.(Ca).X.(Mid).Next is U\n    U.(Cb).Y.(MidO).Next is V\n    need(x)\n    needO(y)\n    needCa(z)\n", string.Empty },
        { "hc3", C3 + "func fc3<T, U, V>(dst: uniq/U, p: raw/u8)\n    U is Cb\n    V is Cc\n    V.(Cc).Z is T\n    T.(Ca).X.(Mid).Next is U\n    U.(Cb).Y.(MidO).Next is V\n    return\nfunc fc3Bad<T, U, V>(x: U) -> ()\n    U is Cb\n    V is Cc\n    V.(Cc).Z is T\n    T.(Ca).X.(Mid).Next is U\n    U.(Cb).Y.(MidO).Next is V\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fc3<T, U, V>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", "ComparisonLoanConflict_Kd" },
        { "hc3bare", C3 + "func fc3<T, U, V>(dst: uniq/U, p: raw/u8)\n    U is Cb\n    V is Cc0\n    V.(Cc0).Z is T\n    T.(Ca0).X.(Mid0).Next is U\n    U.(Cb).Y.(MidO).Next is V\n    return\nfunc fc3Bad<T, U, V>(x: U) -> ()\n    U is Cb\n    V is Cc0\n    V.(Cc0).Z is T\n    T.(Ca0).X.(Mid0).Next is U\n    U.(Cb).Y.(MidO).Next is V\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fc3<T, U, V>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", string.Empty },
        { "r3", Q + "func f<T>(t: ref/T)\n    T is CyQ\n    T.(CyQ).X.(Mid2Q).Next is T\n    T.(InnerQ).G.(MidQ).Next is T\n    needQ2(t)\n", string.Empty },
        { "r3o", Q + "func f<T>(t: ref/T)\n    T.(InnerQ).G.(MidQ).Next is T\n    T.(CyQ).X.(Mid2Q).Next is T\n    T is CyQ\n    needQ2(t)\n", string.Empty },
        { "r3x", Q + "func f<T>(x: ref/T.(InnerQ).G.(MidQ).Next)\n    T is CyQ\n    T.(CyQ).X.(Mid2Q).Next is T\n    T.(InnerQ).G.(MidQ).Next is T\n    needQ2(x)\n", string.Empty },
        { "r3n", Q + "func f<T>(t: ref/T)\n    T is CyQ0\n    T.(CyQ0).X.(Mid2Q0).Next is T\n    T.(InnerQ0).G.(MidQ0).Next is T\n    needQ2(t)\n", "UnprovenConstraint_Kd" },
        { "r4", Q + "func f<T>(t: ref/T)\n    T is CyQ\n    T.(CyQ).X.(Mid2Q).Next is T\n    T.(InnerQ).G.(MidQ).Next is T\n    T.(Q2).H.(MidR).Next is T\n    needQ3(t)\n", string.Empty },
        { "r4o", Q + "func f<T>(t: ref/T)\n    T.(Q2).H.(MidR).Next is T\n    T.(InnerQ).G.(MidQ).Next is T\n    T.(CyQ).X.(Mid2Q).Next is T\n    T is CyQ\n    needQ3(t)\n", string.Empty },
        { "r4n", Q + "func f<T>(t: ref/T)\n    T is CyQ\n    T.(CyQ).X.(Mid2Q).Next is T\n    T.(Q2).H.(MidR).Next is T\n    needQ3(t)\n", "InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd" },
        { "hr3", Qr + "func fq<T>(dst: uniq/T, p: raw/u8)\n    T is CyQI\n    T.(CyQI).X.(Mid2QI).Next is T\n    T.(InnerQI).G.(MidQI).Next is T\n    return\nfunc fqBad<T>(x: T) -> ()\n    T is CyQI\n    T.(CyQI).X.(Mid2QI).Next is T\n    T.(InnerQI).G.(MidQI).Next is T\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fq<T>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", "ComparisonLoanConflict_Kd" },
        { "hr3bare", Qr + "func fq<T>(dst: uniq/T, p: raw/u8)\n    T is CyQI0\n    T.(CyQI0).X.(Mid2QI0).Next is T\n    T.(InnerQI0).G.(MidQI0).Next is T\n    return\nfunc fqBad<T>(x: T) -> ()\n    T is CyQI0\n    T.(CyQI0).X.(Mid2QI0).Next is T\n    T.(InnerQI0).G.(MidQI0).Next is T\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fq<T>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", string.Empty },
        { "pm", Cy + "func f<T, W>(w: ref/W, p: raw/u8) -> T\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    W is T.(Inner).F\n    return w(p)\n", string.Empty },
        { "pmr", Cy + "func f<T, W>(w: ref/W, p: raw/u8) -> T\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    T.(Inner).F is W\n    return w(p)\n", string.Empty },
        { "pmn", Cy + "func f<T, W>(w: ref/W, p: raw/u8) -> T\n    T is Cy20\n    T.(Cy20).X.(Mid0).Next is T\n    W is T.(Inner).F\n    return w(p)\n", "InvalidConstraint_Kd InvalidConstraint_Kd NotCallable_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd" },
        { "hw", Wr + "func fw<T>(dst: uniq/T, p: raw/u8)\n    T is CyW\n    T.(CyW).X.(MidW2).Next is T\n    return\nfunc fwBad<T>(x: T) -> ()\n    T is CyW\n    T.(CyW).X.(MidW2).Next is T\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fw<T>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", "ComparisonLoanConflict_Kd" },
        { "hwbare", Wr + "func fw<T>(dst: uniq/T, p: raw/u8)\n    T is CyW0\n    T.(CyW0).X.(MidW20).Next is T\n    return\nfunc fwBad<T>(x: T) -> ()\n    T is CyW0\n    T.(CyW0).X.(MidW20).Next is T\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fw<T>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", string.Empty },
        { "ext", Ex + "func f<T>(t: ref/T)\n    T is CyE\n    need(t)\n", string.Empty },
        { "extn", Ex + "func f<T>(t: ref/T)\n    T is CyE0\n    need(t)\n", "UnprovenConstraint_Kd" },
        { "hext", Ex + "func fe<T>(dst: uniq/T, p: raw/u8)\n    T is CyE\n    return\nfunc feBad<T>(x: T) -> ()\n    T is CyE\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fe<T>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", "ComparisonLoanConflict_Kd" },
        { "hextbare", Ex + "func fe<T>(dst: uniq/T, p: raw/u8)\n    T is CyE0\n    return\nfunc feBad<T>(x: T) -> ()\n    T is CyE0\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fe<T>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", string.Empty },
        { "u-callee", Mk + "func fu<T>(dst: uniq/T.(Mk).Next, p: raw/u8, src: ref/T)\n    T is Mk\n    let a = src.next()\n    let b = a.next()\n    let g = b.getG()\n    dst@follow = g(p)\n", string.Empty },
        { "u-bad", Mk + "func fu<T>(dst: uniq/T.(Mk).Next, p: raw/u8, src: ref/T)\n    T is Mk\n    let a = src.next()\n    let b = a.next()\n    let g = b.getG()\n    dst@follow = g(p)\nfunc fuBad<T>(x: T.(Mk).Next, s: ref/T) -> ()\n    T is Mk\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fu<T>(t@uniq, w@raw@raw/u8, s)\n    y = 5\n    consume(t@move)\n", "ComparisonLoanConflict_Kd" },
        { "u-con", Mk + "func fu<T>(dst: uniq/T.(Mk).Next, p: raw/u8, src: ref/T)\n    T is Mk\n    return\nfunc fuBad<T>(x: T.(Mk).Next, s: ref/T) -> ()\n    T is Mk\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fu<T>(t@uniq, w@raw@raw/u8, s)\n    y = 5\n    consume(t@move)\n", "ComparisonLoanConflict_Kd" },
        { "recn", Mk + "func f<T>(t: ref/T)\n    T is Mk\n    need(t)\n", "UnprovenConstraint_Kd" },
        { "recv", Mk + "func f<T>(x: ref/T.(Mk).Next.(Mk).Up)\n    T is Mk\n    T is Inner\n    need(x)\n", string.Empty },
        { "pathq", "contract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\nfunc f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    return\n", string.Empty },
        { "matexf", Mx + "func f<T, U>(x: ref/U.(B2).Y)\n    T is OuterA\n    U is T.(OuterA).N.(A2).X\n    needZ(x)\n", string.Empty },
        { "matexn", Mx + "func f<T, U>(x: ref/U.(B2).Y)\n    T is OuterA0\n    T.(OuterA0).N.(A20).X is U\n    needZ(x)\n", "UnprovenConstraint_Kd" },
        { "matez", Mz + "func f<T, U>(u: ref/U)\n    T is OuterB\n    T.(OuterB).N.(A3).X is U\n    needZ(u)\n", string.Empty },
        { "matezf", Mz + "func f<T, U>(u: ref/U)\n    T is OuterB\n    U is T.(OuterB).N.(A3).X\n    needZ(u)\n", string.Empty },
        { "matezp", Mz + "func f<T, U>(x: ref/T.(OuterB).N.(A3).X)\n    T is OuterB\n    T.(OuterB).N.(A3).X is U\n    needZ(x)\n", string.Empty },
        { "matez2", Mz + "func f<T, U>(u: ref/U, x: ref/T.(OuterB).N.(A3).X)\n    T is OuterB\n    T.(OuterB).N.(A3).X is U\n    needZ(u)\n    needZ(x)\n", string.Empty },
        { "matez3", Mz + "func f<T, U>(u: ref/U, x: ref/T.(OuterB).N.(A3).X)\n    T is OuterB\n    T.(OuterB).N.(A3).X is U\n    needZ(x)\n    needZ(u)\n", string.Empty },
        { "matezn", Mz + "func f<T, U>(u: ref/U)\n    T is OuterB0\n    T.(OuterB0).N.(A30).X is U\n    needZ(u)\n", "UnprovenConstraint_Kd" },
    };

    [Theory]
    [MemberData(nameof(CycleRows))]
    public void ACycleThroughAProjectionHasItsLeastFixpointRows(string row, string source, string codes)
    {
        var c = MinimalEmissionTest.Analyze(CyclePrelude + source + "public func main() => ()\n");
        var found = string.Join(" ", CompilationTestHelper.PublishedErrors(c).Select(static x => x.Code).Order(StringComparer.Ordinal));
        Assert.True(codes == found, row + ": " + found);
    }

    private const string ExposurePrelude =
        "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n" +
        "    func get(self: ref/Self) -> ref/Self.(Inner).F\n";

    // PLAN G881b (the Middle's C-335 consent, B1): a cycle form above (hcy2, hw, hext, ambcy) beside a two-link mate written Q last
    // (`S.(OuterP).Next.(MidP).Next is Q`), which G881's row (5) newly proves. Before G881 each invalid program was rejected only by
    // that mate's false UnprovenConstraint_Kd, and G881's head (the skip guard) accepted it: the cycle's rows reject it now. The
    // valid partners (x-exp, the bare twins, x-cy-need-exp) stay or become accepted; a direct twin (-d: the cycle's fact also
    // written) keeps its record. Each row is a whole program after ExposurePrelude.
    public static TheoryData<string, string, string> CycleExposureRows => new()
    {
        { "x-cy-ret-exp", "contract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Cy2\n    associate X is Mid\ncontract Cy20\n    associate X is Mid0\ncontract NamedF\n    associate F\ncontract Plain\n    func tag(self: ref/Self) -> i32\ncontract MidP\n    associate Next is Plain\ncontract OuterP\n    associate Next is MidP\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needP<X>(x: ref/X)\n    X is Plain\n    return\nfunc fj<T, S, Q>(dst: uniq/T, p: raw/u8, q: ref/Q)\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    S is OuterP\n    S.(OuterP).Next.(MidP).Next is Q\n    needP(q)\n    return\nfunc fjBad<T, S>(x: T, s: ref/S.(OuterP).Next.(MidP).Next) -> ()\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    S is OuterP\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fj<T, S, S.(OuterP).Next.(MidP).Next>(t@uniq, w@raw@raw/u8, s)\n    y = 5\n    consume(t@move)\npublic func main() => ()\n", "ComparisonLoanConflict_Kd" },
        { "x-cy-amb-exp", "contract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Cy2\n    associate X is Mid\ncontract Cy20\n    associate X is Mid0\ncontract NamedF\n    associate F\ncontract Plain\n    func tag(self: ref/Self) -> i32\ncontract MidP\n    associate Next is Plain\ncontract OuterP\n    associate Next is MidP\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needP<X>(x: ref/X)\n    X is Plain\n    return\nfunc f<T, S, Q>(t: ref/T, e: ref/T.F, q: ref/Q)\n    T is NamedF\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    S is OuterP\n    S.(OuterP).Next.(MidP).Next is Q\n    needP(q)\n    return\npublic func main() => ()\n", "AmbiguousBinding_Kd" },
        { "x-cy-ret-sep", "contract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Cy2\n    associate X is Mid\ncontract Cy20\n    associate X is Mid0\ncontract NamedF\n    associate F\ncontract Plain\n    func tag(self: ref/Self) -> i32\ncontract MidP\n    associate Next is Plain\ncontract OuterP\n    associate Next is MidP\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needP<X>(x: ref/X)\n    X is Plain\n    return\nfunc fj<T>(dst: uniq/T, p: raw/u8)\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    return\nfunc fjBad<T>(x: T) -> ()\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fj<T>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\nfunc g<S, Q>(q: ref/Q)\n    S is OuterP\n    S.(OuterP).Next.(MidP).Next is Q\n    needP(q)\npublic func main() => ()\n", "ComparisonLoanConflict_Kd" },
        { "x-hw-exp", "contract Mid\n    associate Next is Inner\ncontract W2\n    func absorb(self: uniq/Self, p: raw/u8)\ncontract W20\n    func absorb(self: uniq/Self, p: i32)\ncontract MidW2\n    associate Next is W2\ncontract MidW20\n    associate Next is W20\ncontract CyW\n    associate X is MidW2\ncontract CyW0\n    associate X is MidW20\ncontract CyE\n    associate X is Mid\n    Self.(CyE).X.(Mid).Next is Self\ncontract Plain\n    func tag(self: ref/Self) -> i32\ncontract MidP\n    associate Next is Plain\ncontract OuterP\n    associate Next is MidP\nfunc needP<X>(x: ref/X)\n    X is Plain\n    return\nfunc fj<T, S, Q>(dst: uniq/T, p: raw/u8, q: ref/Q) -> i32\n    T is CyW\n    T.(CyW).X.(MidW2).Next is T\n    S is OuterP\n    S.(OuterP).Next.(MidP).Next is Q\n    needP(q)\n    return 0\nfunc fjBad<T, S>(x: T, s: ref/S.(OuterP).Next.(MidP).Next) -> ()\n    T is CyW\n    T.(CyW).X.(MidW2).Next is T\n    S is OuterP\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    let k = fj<T, S, S.(OuterP).Next.(MidP).Next>(t@uniq, w@raw@raw/u8, s)\n    y = 5\n    consume(t@move)\npublic func main() => ()\n", "ComparisonLoanConflict_Kd" },
        { "x-hext-exp", "contract Mid\n    associate Next is Inner\ncontract W2\n    func absorb(self: uniq/Self, p: raw/u8)\ncontract W20\n    func absorb(self: uniq/Self, p: i32)\ncontract MidW2\n    associate Next is W2\ncontract MidW20\n    associate Next is W20\ncontract CyW\n    associate X is MidW2\ncontract CyW0\n    associate X is MidW20\ncontract CyE\n    associate X is Mid\n    Self.(CyE).X.(Mid).Next is Self\ncontract Plain\n    func tag(self: ref/Self) -> i32\ncontract MidP\n    associate Next is Plain\ncontract OuterP\n    associate Next is MidP\nfunc needP<X>(x: ref/X)\n    X is Plain\n    return\nfunc fj<T, S, Q>(dst: uniq/T, p: raw/u8, q: ref/Q) -> i32\n    T is CyE\n    S is OuterP\n    S.(OuterP).Next.(MidP).Next is Q\n    needP(q)\n    return 0\nfunc fjBad<T, S>(x: T, s: ref/S.(OuterP).Next.(MidP).Next) -> ()\n    T is CyE\n    S is OuterP\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    let k = fj<T, S, S.(OuterP).Next.(MidP).Next>(t@uniq, w@raw@raw/u8, s)\n    y = 5\n    consume(t@move)\npublic func main() => ()\n", "ComparisonLoanConflict_Kd" },
        { "x-hw-call", "contract Mid\n    associate Next is Inner\ncontract W2\n    func absorb(self: uniq/Self, p: raw/u8)\ncontract W20\n    func absorb(self: uniq/Self, p: i32)\ncontract MidW2\n    associate Next is W2\ncontract MidW20\n    associate Next is W20\ncontract CyW\n    associate X is MidW2\ncontract CyW0\n    associate X is MidW20\ncontract CyE\n    associate X is Mid\n    Self.(CyE).X.(Mid).Next is Self\ncontract Plain\n    func tag(self: ref/Self) -> i32\ncontract MidP\n    associate Next is Plain\ncontract OuterP\n    associate Next is MidP\nfunc needP<X>(x: ref/X)\n    X is Plain\n    return\nfunc fj<T, S, Q>(dst: uniq/T, p: raw/u8, q: ref/Q) -> i32\n    T is CyW\n    T.(CyW).X.(MidW2).Next is T\n    S is OuterP\n    S.(OuterP).Next.(MidP).Next is Q\n    return q.tag()\nfunc fjBad<T, S>(x: T, s: ref/S.(OuterP).Next.(MidP).Next) -> ()\n    T is CyW\n    T.(CyW).X.(MidW2).Next is T\n    S is OuterP\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    let k = fj<T, S, S.(OuterP).Next.(MidP).Next>(t@uniq, w@raw@raw/u8, s)\n    y = 5\n    consume(t@move)\npublic func main() => ()\n", "ComparisonLoanConflict_Kd" },
        { "x-exp", "contract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Cy2\n    associate X is Mid\ncontract Cy20\n    associate X is Mid0\ncontract NamedF\n    associate F\ncontract Plain\n    func tag(self: ref/Self) -> i32\ncontract MidP\n    associate Next is Plain\ncontract OuterP\n    associate Next is MidP\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needP<X>(x: ref/X)\n    X is Plain\n    return\nfunc g<S, Q>(q: ref/Q)\n    S is OuterP\n    S.(OuterP).Next.(MidP).Next is Q\n    needP(q)\npublic func main() => ()\n", string.Empty },
        { "x-cy-ret-exp-bare", "contract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Cy2\n    associate X is Mid\ncontract Cy20\n    associate X is Mid0\ncontract NamedF\n    associate F\ncontract Plain\n    func tag(self: ref/Self) -> i32\ncontract MidP\n    associate Next is Plain\ncontract OuterP\n    associate Next is MidP\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needP<X>(x: ref/X)\n    X is Plain\n    return\nfunc fj<T, S, Q>(dst: uniq/T, p: raw/u8, q: ref/Q)\n    T is Cy20\n    T.(Cy20).X.(Mid0).Next is T\n    S is OuterP\n    S.(OuterP).Next.(MidP).Next is Q\n    needP(q)\n    return\nfunc fjBad<T, S>(x: T, s: ref/S.(OuterP).Next.(MidP).Next) -> ()\n    T is Cy20\n    T.(Cy20).X.(Mid0).Next is T\n    S is OuterP\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fj<T, S, S.(OuterP).Next.(MidP).Next>(t@uniq, w@raw@raw/u8, s)\n    y = 5\n    consume(t@move)\npublic func main() => ()\n", string.Empty },
        { "x-hw-exp-bare", "contract Mid\n    associate Next is Inner\ncontract W2\n    func absorb(self: uniq/Self, p: raw/u8)\ncontract W20\n    func absorb(self: uniq/Self, p: i32)\ncontract MidW2\n    associate Next is W2\ncontract MidW20\n    associate Next is W20\ncontract CyW\n    associate X is MidW2\ncontract CyW0\n    associate X is MidW20\ncontract CyE\n    associate X is Mid\n    Self.(CyE).X.(Mid).Next is Self\ncontract Plain\n    func tag(self: ref/Self) -> i32\ncontract MidP\n    associate Next is Plain\ncontract OuterP\n    associate Next is MidP\nfunc needP<X>(x: ref/X)\n    X is Plain\n    return\nfunc fj<T, S, Q>(dst: uniq/T, p: raw/u8, q: ref/Q) -> i32\n    T is CyW0\n    T.(CyW0).X.(MidW20).Next is T\n    S is OuterP\n    S.(OuterP).Next.(MidP).Next is Q\n    needP(q)\n    return 0\nfunc fjBad<T, S>(x: T, s: ref/S.(OuterP).Next.(MidP).Next) -> ()\n    T is CyW0\n    T.(CyW0).X.(MidW20).Next is T\n    S is OuterP\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    let k = fj<T, S, S.(OuterP).Next.(MidP).Next>(t@uniq, w@raw@raw/u8, s)\n    y = 5\n    consume(t@move)\npublic func main() => ()\n", string.Empty },
        { "x-hw-call-bare", "contract Mid\n    associate Next is Inner\ncontract W2\n    func absorb(self: uniq/Self, p: raw/u8)\ncontract W20\n    func absorb(self: uniq/Self, p: i32)\ncontract MidW2\n    associate Next is W2\ncontract MidW20\n    associate Next is W20\ncontract CyW\n    associate X is MidW2\ncontract CyW0\n    associate X is MidW20\ncontract CyE\n    associate X is Mid\n    Self.(CyE).X.(Mid).Next is Self\ncontract Plain\n    func tag(self: ref/Self) -> i32\ncontract MidP\n    associate Next is Plain\ncontract OuterP\n    associate Next is MidP\nfunc needP<X>(x: ref/X)\n    X is Plain\n    return\nfunc fj<T, S, Q>(dst: uniq/T, p: raw/u8, q: ref/Q) -> i32\n    T is CyW0\n    T.(CyW0).X.(MidW20).Next is T\n    S is OuterP\n    S.(OuterP).Next.(MidP).Next is Q\n    return q.tag()\nfunc fjBad<T, S>(x: T, s: ref/S.(OuterP).Next.(MidP).Next) -> ()\n    T is CyW0\n    T.(CyW0).X.(MidW20).Next is T\n    S is OuterP\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    let k = fj<T, S, S.(OuterP).Next.(MidP).Next>(t@uniq, w@raw@raw/u8, s)\n    y = 5\n    consume(t@move)\npublic func main() => ()\n", string.Empty },
        { "x-cy-ret-exp-d", "contract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Cy2\n    associate X is Mid\ncontract Cy20\n    associate X is Mid0\ncontract NamedF\n    associate F\ncontract Plain\n    func tag(self: ref/Self) -> i32\ncontract MidP\n    associate Next is Plain\ncontract OuterP\n    associate Next is MidP\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needP<X>(x: ref/X)\n    X is Plain\n    return\nfunc fj<T, S, Q>(dst: uniq/T, p: raw/u8, q: ref/Q)\n    T is Inner\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    S is OuterP\n    S.(OuterP).Next.(MidP).Next is Q\n    needP(q)\n    return\nfunc fjBad<T, S>(x: T, s: ref/S.(OuterP).Next.(MidP).Next) -> ()\n    T is Inner\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    S is OuterP\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fj<T, S, S.(OuterP).Next.(MidP).Next>(t@uniq, w@raw@raw/u8, s)\n    y = 5\n    consume(t@move)\npublic func main() => ()\n", "ComparisonLoanConflict_Kd" },
        { "x-hw-exp-d", "contract Mid\n    associate Next is Inner\ncontract W2\n    func absorb(self: uniq/Self, p: raw/u8)\ncontract W20\n    func absorb(self: uniq/Self, p: i32)\ncontract MidW2\n    associate Next is W2\ncontract MidW20\n    associate Next is W20\ncontract CyW\n    associate X is MidW2\ncontract CyW0\n    associate X is MidW20\ncontract CyE\n    associate X is Mid\n    Self.(CyE).X.(Mid).Next is Self\ncontract Plain\n    func tag(self: ref/Self) -> i32\ncontract MidP\n    associate Next is Plain\ncontract OuterP\n    associate Next is MidP\nfunc needP<X>(x: ref/X)\n    X is Plain\n    return\nfunc fj<T, S, Q>(dst: uniq/T, p: raw/u8, q: ref/Q) -> i32\n    T is W2\n    T is CyW\n    T.(CyW).X.(MidW2).Next is T\n    S is OuterP\n    S.(OuterP).Next.(MidP).Next is Q\n    needP(q)\n    return 0\nfunc fjBad<T, S>(x: T, s: ref/S.(OuterP).Next.(MidP).Next) -> ()\n    T is W2\n    T is CyW\n    T.(CyW).X.(MidW2).Next is T\n    S is OuterP\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    let k = fj<T, S, S.(OuterP).Next.(MidP).Next>(t@uniq, w@raw@raw/u8, s)\n    y = 5\n    consume(t@move)\npublic func main() => ()\n", "ComparisonLoanConflict_Kd" },
        { "x-hext-exp-d", "contract Mid\n    associate Next is Inner\ncontract W2\n    func absorb(self: uniq/Self, p: raw/u8)\ncontract W20\n    func absorb(self: uniq/Self, p: i32)\ncontract MidW2\n    associate Next is W2\ncontract MidW20\n    associate Next is W20\ncontract CyW\n    associate X is MidW2\ncontract CyW0\n    associate X is MidW20\ncontract CyE\n    associate X is Mid\n    Self.(CyE).X.(Mid).Next is Self\ncontract Plain\n    func tag(self: ref/Self) -> i32\ncontract MidP\n    associate Next is Plain\ncontract OuterP\n    associate Next is MidP\nfunc needP<X>(x: ref/X)\n    X is Plain\n    return\nfunc fj<T, S, Q>(dst: uniq/T, p: raw/u8, q: ref/Q) -> i32\n    T is Inner\n    T is CyE\n    S is OuterP\n    S.(OuterP).Next.(MidP).Next is Q\n    needP(q)\n    return 0\nfunc fjBad<T, S>(x: T, s: ref/S.(OuterP).Next.(MidP).Next) -> ()\n    T is Inner\n    T is CyE\n    S is OuterP\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    let k = fj<T, S, S.(OuterP).Next.(MidP).Next>(t@uniq, w@raw@raw/u8, s)\n    y = 5\n    consume(t@move)\npublic func main() => ()\n", "ComparisonLoanConflict_Kd" },
        { "x-cy-need-exp", "contract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Cy2\n    associate X is Mid\ncontract Cy20\n    associate X is Mid0\ncontract NamedF\n    associate F\ncontract Plain\n    func tag(self: ref/Self) -> i32\ncontract MidP\n    associate Next is Plain\ncontract OuterP\n    associate Next is MidP\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needP<X>(x: ref/X)\n    X is Plain\n    return\nfunc f<T, S, Q>(t: ref/T, q: ref/Q)\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    S is OuterP\n    S.(OuterP).Next.(MidP).Next is Q\n    need(t)\n    needP(q)\npublic func main() => ()\n", string.Empty },
        { "x-cy-amb", "contract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Cy2\n    associate X is Mid\ncontract Cy20\n    associate X is Mid0\ncontract NamedF\n    associate F\ncontract Plain\n    func tag(self: ref/Self) -> i32\ncontract MidP\n    associate Next is Plain\ncontract OuterP\n    associate Next is MidP\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needP<X>(x: ref/X)\n    X is Plain\n    return\nfunc f<T>(t: ref/T, e: ref/T.F)\n    T is NamedF\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    return\npublic func main() => ()\n", "AmbiguousBinding_Kd" },
    };

    [Theory]
    [MemberData(nameof(CycleExposureRows))]
    public void ACycleBesideANewlyProvenMateKeepsItsRows(string row, string source, string codes)
    {
        var c = MinimalEmissionTest.Analyze(ExposurePrelude + source);
        var found = string.Join(" ", CompilationTestHelper.PublishedErrors(c).Select(static x => x.Code).Order(StringComparer.Ordinal));
        Assert.True(codes == found, row + ": " + found);
    }
}
