// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
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
        { "nosup", Cy + "func f<T>(t: ref/T)\n    T.(Cy2).X.(Mid).Next is T\n    need(t)\n", "UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd" }, // R7-C3 (F's C2 ruling 255, Q13): the use's proof reads neither a failed premise nor the named unproven input, so its requirement is the remainder's own (23.1.2); the input fails no premise (ruling 252)
        { "nosupc", Cy + "func needCopy<X>(x: ref/X)\n    X is Copy\n    return\nfunc f<T>(t: ref/T)\n    T.(Cy2).X.(Mid).Next is T\n    need(t)\n    needCopy(t)\n", "UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd" }, // R7-C3 (F's C2 rulings 254, 255): `T is Copy` has no premise: an independent requirement keeps its record (23.1.2)
        { "nosupi", Cy + "func needCopyV<X>(x: X)\n    X is Copy\n    return\nfunc f<T>(t: ref/T)\n    T.(Cy2).X.(Mid).Next is T\n    need(t)\n    needCopyV(1)\n", "UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd" }, // R7-C3 (F's C2 ruling 254): `i32 is Copy` is answered in the remainder, accepted
        { "selfsup", Selfs + "func f<T>(t: ref/T)\n    T.(InnerS).G.(MidS).Next is T\n    needS(t)\n", "UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd" }, // R7-C3 (F's (c), ruling 230): no record at the use; its requirement check rests on f's reported failure (8.2.3, 23.1.2)
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
        { "r4n", Q + "func f<T>(t: ref/T)\n    T is CyQ\n    T.(CyQ).X.(Mid2Q).Next is T\n    T.(Q2).H.(MidR).Next is T\n    needQ3(t)\n", "UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd" }, // R7-C3 (F's C2 ruling 255, Q13): the use's proof reads neither a failed premise nor the named unproven input, so its requirement is the remainder's own (23.1.2); the input fails no premise (ruling 252): r4n's only failure is `T is Q2`, unproven
        { "hr3", Qr + "func fq<T>(dst: uniq/T, p: raw/u8)\n    T is CyQI\n    T.(CyQI).X.(Mid2QI).Next is T\n    T.(InnerQI).G.(MidQI).Next is T\n    return\nfunc fqBad<T>(x: T) -> ()\n    T is CyQI\n    T.(CyQI).X.(Mid2QI).Next is T\n    T.(InnerQI).G.(MidQI).Next is T\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fq<T>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", "ComparisonLoanConflict_Kd" },
        { "hr3bare", Qr + "func fq<T>(dst: uniq/T, p: raw/u8)\n    T is CyQI0\n    T.(CyQI0).X.(Mid2QI0).Next is T\n    T.(InnerQI0).G.(MidQI0).Next is T\n    return\nfunc fqBad<T>(x: T) -> ()\n    T is CyQI0\n    T.(CyQI0).X.(Mid2QI0).Next is T\n    T.(InnerQI0).G.(MidQI0).Next is T\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fq<T>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", string.Empty },
        { "pm", Cy + "func f<T, W>(w: ref/W, p: raw/u8) -> T\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    W is T.(Inner).F\n    return w(p)\n", string.Empty },
        { "pmr", Cy + "func f<T, W>(w: ref/W, p: raw/u8) -> T\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    T.(Inner).F is W\n    return w(p)\n", string.Empty },
        { "pmn", Cy + "func f<T, W>(w: ref/W, p: raw/u8) -> T\n    T is Cy20\n    T.(Cy20).X.(Mid0).Next is T\n    W is T.(Inner).F\n    return w(p)\n", "NotCallable_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd" }, // R7-C3 (C2 rulings 245 (i), 250 (2), Q13): as fcn, the clause projections' records go (8.2.3 "once")
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
        { "pathqn", "contract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\nfunc f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    need(x)\n", "PremiseClosureLimit_Kd" },
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
    [MemberData(nameof(CycleReviewRows))]
    public void ACycleThroughAProjectionHasItsLeastFixpointRows(string row, string source, string codes)
    {
        var c = MinimalEmissionTest.Analyze(CyclePrelude + source + "public func main() => ()\n");
        var found = string.Join(" ", CompilationTestHelper.PublishedErrors(c).Select(static x => x.Code).Order(StringComparer.Ordinal));
        Assert.True(codes == found, row + ": " + found);
    }

    // R7-C3 C7 (rv.tp3-u, V; SPEC 8.2.3, 10.7): the Owned erasure of an anonymous function whose header names a Type identical with the
    // pair's target (`U is W`) reads the capability work its judge judged (JudgedCapabilityKey); with the written atom no longer judged
    // it read the written proposition's key and found no work: InternalInvariant_Kd. The program's remaining record is D-779's (G999).
    [Fact]
    public void AnErasureReadsTheCapabilityWorkItsJudgeJudged()
    {
        var c = MinimalEmissionTest.Analyze("func pick<s/T>(x: s/T, g: (T) -> ())\n    s is ref or uniq\n    ()\nfunc f<U, W>(x: ref/U during a)\n    U is W\n" +
            "    pick(x, func (v: U) => ())\npublic func main() => ()\n");
        Assert.DoesNotContain(CompilationTestHelper.PublishedErrors(c), static x => x.Code == "InternalInvariant_Kd");
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

    private const string RvA =
        "contract Cy\n    associate X is Inner\ncontract Cy0\n    associate X is Inner0\ncontract Cy2\n" +
        "    associate X is Mid\ncontract Cy20\n    associate X is Mid0\ncontract Ca\n    associate X is Mid\ncontract Ca0\n" +
        "    associate X is Mid0\n";

    private const string RvB =
        "contract Cy\n    associate X is Inner\ncontract Cy0\n    associate X is Inner0\ncontract Cy2\n" +
        "    associate X is Mid\ncontract Cy20\n    associate X is Mid0\ncontract CyO2\n    associate X is MidO\n" +
        "contract CyO20\n    associate X is MidO0\n";

    private const string RvC =
        "contract Cy\n    associate X is Inner\ncontract Cy0\n    associate X is Inner0\ncontract Cy2\n" +
        "    associate X is Mid\ncontract Cy20\n    associate X is Mid0\ncontract Dm\n    associate W\n";

    private const string RvD =
        "contract Cy\n    associate X is Inner\ncontract Cy0\n    associate X is Inner0\ncontract Cy2\n" +
        "    associate X is Mid\ncontract Cy20\n    associate X is Mid0\ncontract MidBad\n    associate Next is Inner\n" +
        "    associate Bad is Nope\ncontract CyB\n    associate X is MidBad\n";

    private const string RvE =
        "contract Cy\n    associate X is Inner\ncontract Cy0\n    associate X is Inner0\ncontract Cy2\n" +
        "    associate X is Mid\ncontract Cy20\n    associate X is Mid0\n";

    private const string RvF =
        "contract Cy\n    associate X is Inner\ncontract Cy0\n    associate X is Inner0\ncontract Cy2\n" +
        "    associate X is Mid\ncontract Cy20\n    associate X is Mid0\n";

    private const string RvG =
        "contract Cy\n    associate X is Inner\ncontract Cy0\n    associate X is Inner0\ncontract Cy2\n" +
        "    associate X is Mid\ncontract Cy20\n    associate X is Mid0\ncontract CyO2\n    associate X is MidO\n" +
        "contract CyO20\n    associate X is MidO0\n";

    private const string RvX =
        "contract Cy\n    associate X is Inner\ncontract Cy0\n    associate X is Inner0\ncontract Cy2\n" +
        "    associate X is Mid\ncontract Cy20\n    associate X is Mid0\ncontract Mk\n    associate Next is Mk\n" +
        "    associate Up\n    associate G\n    Self.(Mk).Next.(Mk).Up is Self\n" +
        "    Self.(Mk).G is Callable<(raw/u8) -> Self.(Mk).Up>\n    func next(self: ref/Self) -> ref/Self.(Mk).Next\n" +
        "    func getG(self: ref/Self) -> ref/Self.(Mk).G\ncontract MidK\n    associate Next is Mk\ncontract CyK\n" +
        "    associate X is MidK\ncontract Pq\n    associate Next is Pq\n    associate Up\n" +
        "    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\ncontract MidP\n    associate Next is Pq\ncontract CyP\n" +
        "    associate X is MidP\n";

    private const string RvY =
        "contract Cy\n    associate X is Inner\ncontract Cy0\n    associate X is Inner0\ncontract Cy2\n" +
        "    associate X is Mid\ncontract Cy20\n    associate X is Mid0\ncontract ZC\ncontract A3\n    associate X\n" +
        "    associate Z is ZC\n    Self.(A3).X is Self.(A3).Z\ncontract MidZ\n    associate Next is A3\ncontract CyZ\n" +
        "    associate X is MidZ\nfunc needZ<X>(x: ref/X)\n    X is ZC\n    return\nfunc needA3<X>(x: ref/X)\n    X is A3\n" +
        "    return\n";

    private const string RvZ =
        "contract Cy\n    associate X is Inner\ncontract Cy0\n    associate X is Inner0\ncontract Cy2\n" +
        "    associate X is Mid\ncontract Cy20\n    associate X is Mid0\ncontract ZC\ncontract ZC0\ncontract MidY\n" +
        "    associate Next\n    associate Alt is ZC\n    Self.(MidY).Next is Self.(MidY).Alt\ncontract MidY0\n" +
        "    associate Next\n    associate Alt is ZC0\n    Self.(MidY0).Next is Self.(MidY0).Alt\ncontract CyY\n" +
        "    associate X is MidY\ncontract CyY0\n    associate X is MidY0\nfunc needZ<X>(x: ref/X)\n    X is ZC\n    return\n";

    // PLAN G881b (the design review's twins, review-g881b.md's missing rows): a second source of
    // ACycleThroughAProjectionHasItsLeastFixpointRows, each row a whole program after CyclePrelude. A four-link cycle of mixed lengths
    // whose root has two Contracts only through it (q4*); two cycles on one parameter, sibling mates of one class, and two of its 48
    // premise and ask orders (dc2*, g*); a cycle whose mate's chain holds a second class's projection member (sc*); a cycle through a
    // Contract whose declaration fails, whose rows stay unavailable round after round (ub*); a Callable row and an effect bound reached
    // only through a cycle (fc*, fb*); a recursive Contract supplied only through a cycle (rk*, pkv; pk, the invalid twin of pkv, ends
    // at the round bound: its record is the limit's, PremiseClosureLimit_Kd); an identity recorded on the request's own chain from a row that arrives
    // through the cycle (mzc*, mzm*); an identity a Contract supplies at a prefix of a cycle's mate (yz*: yz itself, valid, is rejected,
    // the stated limit, a PLAN row; its row holds the record until that row lands); retention through each (h*). On G881's head hdc2,
    // hsc and hq4b (invalid) were accepted.
    public static TheoryData<string, string, string> CycleReviewRows => new()
    {
        { "q4", RvA + "contract Ff\n    associate Q is Ca\n    Self.(Ff).Q is Other\ncontract Ff0\n    associate Q is Ca\ncontract FfB\n    associate Q is Ca0\n    Self.(FfB).Q is Other\ncontract Ee\n    associate Next is Ff\ncontract Ee0\n    associate Next is Ff0\ncontract EeB\n    associate Next is FfB\ncontract Dd\n    associate W is Ee\ncontract Dd0\n    associate W is Ee0\ncontract DdB\n    associate W is EeB\ncontract Cb\n    associate Y is Dd\ncontract Cb0\n    associate Y is Dd0\ncontract CbB\n    associate Y is DdB\nfunc needCa<X>(x: ref/X)\n    X is Ca\n    return\nfunc f<T, U, V, W>(t: ref/T, u: ref/U)\n    U is Cb\n    U.(Cb).Y is V\n    V.(Dd).W.(Ee).Next is W\n    W.(Ff).Q is T\n    T.(Ca).X.(Mid).Next is U\n    need(u)\n    needO(t)\n    needCa(t)\n", string.Empty },
        { "q4o", RvA + "contract Ff\n    associate Q is Ca\n    Self.(Ff).Q is Other\ncontract Ff0\n    associate Q is Ca\ncontract FfB\n    associate Q is Ca0\n    Self.(FfB).Q is Other\ncontract Ee\n    associate Next is Ff\ncontract Ee0\n    associate Next is Ff0\ncontract EeB\n    associate Next is FfB\ncontract Dd\n    associate W is Ee\ncontract Dd0\n    associate W is Ee0\ncontract DdB\n    associate W is EeB\ncontract Cb\n    associate Y is Dd\ncontract Cb0\n    associate Y is Dd0\ncontract CbB\n    associate Y is DdB\nfunc needCa<X>(x: ref/X)\n    X is Ca\n    return\nfunc f<T, U, V, W>(t: ref/T, u: ref/U)\n    T.(Ca).X.(Mid).Next is U\n    W.(Ff).Q is T\n    V.(Dd).W.(Ee).Next is W\n    U.(Cb).Y is V\n    U is Cb\n    need(u)\n    needO(t)\n    needCa(t)\n", string.Empty },
        { "q4t", RvA + "contract Ff\n    associate Q is Ca\n    Self.(Ff).Q is Other\ncontract Ff0\n    associate Q is Ca\ncontract FfB\n    associate Q is Ca0\n    Self.(FfB).Q is Other\ncontract Ee\n    associate Next is Ff\ncontract Ee0\n    associate Next is Ff0\ncontract EeB\n    associate Next is FfB\ncontract Dd\n    associate W is Ee\ncontract Dd0\n    associate W is Ee0\ncontract DdB\n    associate W is EeB\ncontract Cb\n    associate Y is Dd\ncontract Cb0\n    associate Y is Dd0\ncontract CbB\n    associate Y is DdB\nfunc needCa<X>(x: ref/X)\n    X is Ca\n    return\nfunc f<T, U, V, W>(t: ref/T, u: ref/U)\n    U is Cb\n    U.(Cb).Y is V\n    V.(Dd).W.(Ee).Next is W\n    W.(Ff).Q is T\n    T.(Ca).X.(Mid).Next is U\n    needO(t)\n    need(u)\n", string.Empty },
        { "q4n", RvA + "contract Ff\n    associate Q is Ca\n    Self.(Ff).Q is Other\ncontract Ff0\n    associate Q is Ca\ncontract FfB\n    associate Q is Ca0\n    Self.(FfB).Q is Other\ncontract Ee\n    associate Next is Ff\ncontract Ee0\n    associate Next is Ff0\ncontract EeB\n    associate Next is FfB\ncontract Dd\n    associate W is Ee\ncontract Dd0\n    associate W is Ee0\ncontract DdB\n    associate W is EeB\ncontract Cb\n    associate Y is Dd\ncontract Cb0\n    associate Y is Dd0\ncontract CbB\n    associate Y is DdB\nfunc needCa<X>(x: ref/X)\n    X is Ca\n    return\nfunc f<T, U, V, W>(t: ref/T, u: ref/U)\n    U is Cb0\n    U.(Cb0).Y is V\n    V.(Dd0).W.(Ee0).Next is W\n    W.(Ff0).Q is T\n    T.(Ca).X.(Mid).Next is U\n    need(u)\n    needO(t)\n", "UnprovenConstraint_Kd" },
        { "q4m", RvA + "contract Ff\n    associate Q is Ca\n    Self.(Ff).Q is Other\ncontract Ff0\n    associate Q is Ca\ncontract FfB\n    associate Q is Ca0\n    Self.(FfB).Q is Other\ncontract Ee\n    associate Next is Ff\ncontract Ee0\n    associate Next is Ff0\ncontract EeB\n    associate Next is FfB\ncontract Dd\n    associate W is Ee\ncontract Dd0\n    associate W is Ee0\ncontract DdB\n    associate W is EeB\ncontract Cb\n    associate Y is Dd\ncontract Cb0\n    associate Y is Dd0\ncontract CbB\n    associate Y is DdB\nfunc needCa<X>(x: ref/X)\n    X is Ca\n    return\nfunc f<T, U, V, W>(t: ref/T, u: ref/U)\n    U is Cb\n    U.(Cb).Y is V\n    V.(Dd).W.(Ee).Next is W\n    W.(Ff).Q is T\n    T.(Ca).X.(Mid0).Next is U\n    need(u)\n", "UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd" }, // R7-C3 (F's C2 ruling 255, Q13): the use's proof reads neither a failed premise nor the named unproven input, so its requirement is the remainder's own (23.1.2); the input fails no premise (ruling 252): q4m's only failure is `T.(Ca).X is Mid0`, unproven (ruling 250 (2)'s re-check)
        { "hq4", RvE + "contract Ca\n    associate X is Mid\ncontract Ca0\n    associate X is Mid0\ncontract Ff\n    associate Q is Ca\n    Self.(Ff).Q is Other\ncontract Ff0\n    associate Q is Ca\ncontract FfB\n    associate Q is Ca0\n    Self.(FfB).Q is Other\ncontract Ee\n    associate Next is Ff\ncontract Ee0\n    associate Next is Ff0\ncontract EeB\n    associate Next is FfB\ncontract Dd\n    associate W is Ee\ncontract Dd0\n    associate W is Ee0\ncontract DdB\n    associate W is EeB\ncontract Cb\n    associate Y is Dd\ncontract Cb0\n    associate Y is Dd0\ncontract CbB\n    associate Y is DdB\nfunc needCa<X>(x: ref/X)\n    X is Ca\n    return\nfunc fj4<T, U, V, W>(dst: uniq/U, p: raw/u8)\n    U is Cb\n    U.(Cb).Y is V\n    V.(Dd).W.(Ee).Next is W\n    W.(Ff).Q is T\n    T.(Ca).X.(Mid).Next is U\n    return\nfunc fj4Bad<T, U, V, W>(x: U) -> ()\n    U is Cb\n    U.(Cb).Y is V\n    V.(Dd).W.(Ee).Next is W\n    W.(Ff).Q is T\n    T.(Ca).X.(Mid).Next is U\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fj4<T, U, V, W>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", "ComparisonLoanConflict_Kd" },
        { "hq4bare", RvE + "contract Ca\n    associate X is Mid\ncontract Ca0\n    associate X is Mid0\ncontract Ff\n    associate Q is Ca\n    Self.(Ff).Q is Other\ncontract Ff0\n    associate Q is Ca\ncontract FfB\n    associate Q is Ca0\n    Self.(FfB).Q is Other\ncontract Ee\n    associate Next is Ff\ncontract Ee0\n    associate Next is Ff0\ncontract EeB\n    associate Next is FfB\ncontract Dd\n    associate W is Ee\ncontract Dd0\n    associate W is Ee0\ncontract DdB\n    associate W is EeB\ncontract Cb\n    associate Y is Dd\ncontract Cb0\n    associate Y is Dd0\ncontract CbB\n    associate Y is DdB\nfunc needCa<X>(x: ref/X)\n    X is Ca\n    return\nfunc fj4<T, U, V, W>(dst: uniq/U, p: raw/u8)\n    U is CbB\n    U.(CbB).Y is V\n    V.(DdB).W.(EeB).Next is W\n    W.(FfB).Q is T\n    T.(Ca0).X.(Mid0).Next is U\n    return\nfunc fj4Bad<T, U, V, W>(x: U) -> ()\n    U is CbB\n    U.(CbB).Y is V\n    V.(DdB).W.(EeB).Next is W\n    W.(FfB).Q is T\n    T.(Ca0).X.(Mid0).Next is U\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fj4<T, U, V, W>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", string.Empty },
        { "dc2", RvB + "func f<T>(t: ref/T)\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    T is CyO2\n    T.(CyO2).X.(MidO).Next is T\n    need(t)\n    needO(t)\n", string.Empty },
        { "dc2n", RvB + "func f<T>(t: ref/T)\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    T is CyO20\n    T.(CyO20).X.(MidO0).Next is T\n    need(t)\n    needO(t)\n", "UnprovenConstraint_Kd" },
        { "dc2n1", RvB + "func f<T>(t: ref/T)\n    T is Cy20\n    T.(Cy20).X.(Mid0).Next is T\n    T is CyO2\n    T.(CyO2).X.(MidO).Next is T\n    need(t)\n    needO(t)\n", "UnprovenConstraint_Kd" },
        { "hdc2", RvE + "contract CyO2\n    associate X is MidO\ncontract CyO20\n    associate X is MidO0\nfunc fd<T>(dst: uniq/T, p: raw/u8)\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    T is CyO2\n    T.(CyO2).X.(MidO).Next is T\n    return\nfunc fdBad<T>(x: T) -> ()\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    T is CyO2\n    T.(CyO2).X.(MidO).Next is T\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fd<T>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", "ComparisonLoanConflict_Kd" },
        { "hdc2bare", RvE + "contract CyO2\n    associate X is MidO\ncontract CyO20\n    associate X is MidO0\nfunc fd<T>(dst: uniq/T, p: raw/u8)\n    T is Cy20\n    T.(Cy20).X.(Mid0).Next is T\n    T is CyO2\n    T.(CyO2).X.(MidO).Next is T\n    return\nfunc fdBad<T>(x: T) -> ()\n    T is Cy20\n    T.(Cy20).X.(Mid0).Next is T\n    T is CyO2\n    T.(CyO2).X.(MidO).Next is T\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fd<T>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", string.Empty },
        { "g0123a", RvG + "func f<T>(t: ref/T)\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    T is CyO2\n    T.(CyO2).X.(MidO).Next is T\n    need(t)\n    needO(t)\n", string.Empty },
        { "g3210b", RvG + "func f<T>(t: ref/T)\n    T.(CyO2).X.(MidO).Next is T\n    T is CyO2\n    T.(Cy2).X.(Mid).Next is T\n    T is Cy2\n    needO(t)\n    need(t)\n", string.Empty },
        { "gxa", RvG + "func f<T>(t: ref/T, x: ref/T.(Cy2).X.(Mid).Next, y: ref/T.(CyO2).X.(MidO).Next)\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    T is CyO2\n    T.(CyO2).X.(MidO).Next is T\n    need(x)\n    needO(y)\n    needO(x)\n    need(y)\n    need(t)\n    needO(t)\n", string.Empty },
        { "gxb", RvG + "func f<T>(t: ref/T, x: ref/T.(Cy2).X.(Mid).Next, y: ref/T.(CyO2).X.(MidO).Next)\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    T is CyO2\n    T.(CyO2).X.(MidO).Next is T\n    need(t)\n    needO(t)\n    needO(x)\n    need(y)\n    need(x)\n    needO(y)\n", string.Empty },
        { "gxn", RvG + "func f<T>(t: ref/T, x: ref/T.(Cy2).X.(Mid).Next)\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    T is CyO20\n    T.(CyO20).X.(MidO0).Next is T\n    need(x)\n    needO(x)\n", "UnprovenConstraint_Kd" },
        { "sc", RvC + "func f<T, U>(t: ref/T)\n    T is Cy2\n    U is Dm\n    T.(Cy2).X is U.(Dm).W\n    U.(Dm).W.(Mid).Next is T\n    need(t)\n", string.Empty },
        { "sco", RvC + "func f<T, U>(t: ref/T)\n    U.(Dm).W.(Mid).Next is T\n    T.(Cy2).X is U.(Dm).W\n    U is Dm\n    T is Cy2\n    need(t)\n", string.Empty },
        { "scx", RvC + "func f<T, U>(t: ref/T, x: ref/U.(Dm).W.(Mid).Next, y: ref/T.(Cy2).X.(Mid).Next)\n    T is Cy2\n    U is Dm\n    T.(Cy2).X is U.(Dm).W\n    U.(Dm).W.(Mid).Next is T\n    need(x)\n    need(y)\n    need(t)\n", string.Empty },
        { "scn", RvC + "func f<T, U>(t: ref/T)\n    T is Cy2\n    U is Dm\n    T.(Cy2).X is U.(Dm).W\n    U.(Dm).W.(Mid).Next is T\n    needO(t)\n", "UnprovenConstraint_Kd" },
        { "sc2", RvC + "func f<T, U>(t: ref/T)\n    T is Cy2\n    U is Dm\n    T.(Cy2).X is U.(Dm).W\n    U.(Dm).W.(Mid).Next is T\n    T.(Cy2).X.(Mid).Next is U.(Dm).W.(Mid).Next\n    need(t)\n", string.Empty },
        { "hsc", RvE + "contract Dm\n    associate W\nfunc fs<T, U>(dst: uniq/T, p: raw/u8)\n    T is Cy2\n    U is Dm\n    T.(Cy2).X is U.(Dm).W\n    U.(Dm).W.(Mid).Next is T\n    return\nfunc fsBad<T, U>(x: T) -> ()\n    T is Cy2\n    U is Dm\n    T.(Cy2).X is U.(Dm).W\n    U.(Dm).W.(Mid).Next is T\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fs<T, U>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", "ComparisonLoanConflict_Kd" },
        { "hscbare", RvE + "contract Dm\n    associate W\nfunc fs<T, U>(dst: uniq/T, p: raw/u8)\n    T is Cy20\n    U is Dm\n    T.(Cy20).X is U.(Dm).W\n    U.(Dm).W.(Mid0).Next is T\n    return\nfunc fsBad<T, U>(x: T) -> ()\n    T is Cy20\n    U is Dm\n    T.(Cy20).X is U.(Dm).W\n    U.(Dm).W.(Mid0).Next is T\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fs<T, U>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", string.Empty },
        { "ub", RvD + "func f<T>(t: ref/T)\n    T is CyB\n    T.(CyB).X.(MidBad).Next is T\n    need(t)\n", "InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd UnresolvedBinding_Kd" },
        { "uba", RvD + "func f<T, U>(u: ref/U)\n    T is CyB\n    T.(CyB).X.(MidBad).Next is U\n    need(u)\n", "InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd UnresolvedBinding_Kd" }, // R7-C3 (C2 ruling 250 (2), (c)): `T.(CyB).X`'s use of the failed `T is CyB` rests on it
        { "ubh", RvD + "func fb<T>(dst: uniq/T, p: raw/u8)\n    T is CyB\n    T.(CyB).X.(MidBad).Next is T\n    return\nfunc fbBad<T>(x: T) -> ()\n    T is CyB\n    T.(CyB).X.(MidBad).Next is T\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fb<T>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", "InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd UnresolvedBinding_Kd" }, // R7-C3 (C2 ruling 250 (2), (c)): `consume(t@move)` rests on fbBad's failed declaration
        { "ubc", RvD + "\ncontract CyO2\n    associate X is MidO\ncontract CyO20\n    associate X is MidO0\nfunc f<T>(t: ref/T)\n    T is CyB\n    T.(CyB).X.(MidBad).Next is T\n    T is CyO2\n    T.(CyO2).X.(MidO).Next is T\n    needO(t)\n    need(t)\n", "InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd UnresolvedBinding_Kd" },
        { "fc", RvF + "func callIt<X, Y>(x: ref/X, p: raw/u8) -> Y\n    X is Callable<(raw/u8) -> Y>\n    return x(p)\nfunc applyC<F, R>(f: ref/F, p: raw/u8) -> R\n    F is Callable<(raw/u8) -> R>\n        effect confined\n    return f(p)\nfunc f<T>(w: ref/T.(Inner).F, p: raw/u8) -> T\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    return callIt<T.(Inner).F, T>(w, p)\n", string.Empty },
        { "fcn", RvF + "func callIt<X, Y>(x: ref/X, p: raw/u8) -> Y\n    X is Callable<(raw/u8) -> Y>\n    return x(p)\nfunc applyC<F, R>(f: ref/F, p: raw/u8) -> R\n    F is Callable<(raw/u8) -> R>\n        effect confined\n    return f(p)\nfunc f<T, W>(w: ref/W, p: raw/u8) -> T\n    T is Cy20\n    T.(Cy20).X.(Mid0).Next is T\n    W is T.(Inner).F\n    return callIt<W, T>(w, p)\n", "UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd" }, // R7-C3 (C2 rulings 245 (i), 255; Q13): the clause projections' records go (8.2.3 "once"); the call's proof reads neither a failed premise nor the named input `T is Inner`: its own record (23.1.2)
        { "heqx", RvF + "func callIt<X, Y>(x: ref/X, p: raw/u8) -> Y\n    X is Callable<(raw/u8) -> Y>\n    return x(p)\nfunc f<T, U, W>(w: ref/W, p: raw/u8) -> T\n    U is Inner\n    W is T.(Inner).F\n    return callIt<W, T>(w, p)\n", "UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd" }, // R7-C3 (review h.eqx, I; F's C2 ruling 255, Q13): T.(Inner).F needs T is Inner (8.5.2); fcn's class: the call's proof reads neither a failed premise nor the named input, its own record (23.1.2)
        { "fbn", RvF + "func callIt<X, Y>(x: ref/X, p: raw/u8) -> Y\n    X is Callable<(raw/u8) -> Y>\n    return x(p)\nfunc applyC<F, R>(f: ref/F, p: raw/u8) -> R\n    F is Callable<(raw/u8) -> R>\n        effect confined\n    return f(p)\nfunc f<T, W>(w: ref/W, p: raw/u8) -> T\n    T is Cy2\n    T.(Cy2).X.(Mid).Next is T\n    W is T.(Inner).F\n    return applyC<W, T>(w, p)\n", "UnsatisfiedEffectBound_Kd" }, // R7-C3: applyC applies (W is Callable by Inner's clause); its bound fails at `w` (SPEC 8.11.4, 10.1)
        { "fbwn", RvF + "func callIt<X, Y>(x: ref/X, p: raw/u8) -> Y\n    X is Callable<(raw/u8) -> Y>\n    return x(p)\nfunc applyC<F, R>(f: ref/F, p: raw/u8) -> R\n    F is Callable<(raw/u8) -> R>\n        effect confined\n    return f(p)\nfunc f<T, W>(w: ref/W, p: raw/u8) -> T\n    T is Inner\n    W is T.(Inner).F\n    return applyC<W, T>(w, p)\n", "UnsatisfiedEffectBound_Kd" }, // R7-C3: applyC applies (W is Callable by Inner's clause); its bound fails at `w` (SPEC 8.11.4, 10.1)
        { "rk", RvX + "func f<T>(t: ref/T)\n    T is CyK\n    T.(CyK).X.(MidK).Next is T\n    need(t)\n", "UnprovenConstraint_Kd" },
        { "rkv", RvX + "func f<T>(t: ref/T)\n    T is CyK\n    T.(CyK).X.(MidK).Next is T\n    T is Inner\n    need(t)\n", string.Empty },
        { "rku", RvX + "func fu<T>(dst: uniq/T.(Mk).Next, p: raw/u8, src: ref/T)\n    T is CyK\n    T.(CyK).X.(MidK).Next is T\n    let a = src.next()\n    let b = a.next()\n    let g = b.getG()\n    dst@follow = g(p)\n", string.Empty },
        { "rkx", RvX + "func f<T>(x: ref/T.(Mk).Next.(Mk).Up)\n    T is CyK\n    T.(CyK).X.(MidK).Next is T\n    T is Inner\n    need(x)\n", string.Empty },
        { "pk", RvX + "func f<T>(x: ref/T.(Pq).Up)\n    T is CyP\n    T.(CyP).X.(MidP).Next is T\n    need(x)\n", "PremiseClosureLimit_Kd" },
        { "pkv", RvX + "func f<T>(x: ref/T.(Pq).Up)\n    T is CyP\n    T.(CyP).X.(MidP).Next is T\n    return\n", string.Empty },
        { "mzc", RvY + "func f<T, U>(u: ref/U)\n    T is CyZ\n    T.(CyZ).X.(MidZ).Next is T\n    T.(A3).X is U\n    needZ(u)\n", string.Empty },
        { "mzcp", RvY + "func f<T>(x: ref/T.(A3).X)\n    T is CyZ\n    T.(CyZ).X.(MidZ).Next is T\n    needZ(x)\n", string.Empty },
        { "mzc2", RvY + "func f<T, U>(u: ref/U, x: ref/T.(A3).X)\n    T is CyZ\n    T.(CyZ).X.(MidZ).Next is T\n    T.(A3).X is U\n    needZ(u)\n    needZ(x)\n", string.Empty },
        { "mzc3", RvY + "func f<T, U>(u: ref/U, x: ref/T.(A3).X)\n    T is CyZ\n    T.(CyZ).X.(MidZ).Next is T\n    T.(A3).X is U\n    needZ(x)\n    needZ(u)\n", string.Empty },
        { "mzc4", RvY + "func f<T, U>(t: ref/T, u: ref/U, x: ref/T.(A3).X)\n    T is CyZ\n    T.(CyZ).X.(MidZ).Next is T\n    T.(A3).X is U\n    needA3(t)\n    needZ(u)\n    needZ(x)\n", string.Empty },
        { "mzm", RvY + "func f<T, U>(u: ref/U)\n    T is CyZ\n    T.(CyZ).X.(MidZ).Next is T\n    T.(CyZ).X.(MidZ).Next.(A3).X is U\n    needZ(u)\n", string.Empty },
        { "mzm2", RvY + "func f<T, U>(u: ref/U, x: ref/T.(CyZ).X.(MidZ).Next.(A3).X)\n    T is CyZ\n    T.(CyZ).X.(MidZ).Next is T\n    T.(CyZ).X.(MidZ).Next.(A3).X is U\n    needZ(x)\n    needZ(u)\n", string.Empty },
        { "mzm3", RvY + "func f<T, U>(u: ref/U, x: ref/T.(CyZ).X.(MidZ).Next.(A3).X)\n    T is CyZ\n    T.(CyZ).X.(MidZ).Next is T\n    T.(CyZ).X.(MidZ).Next.(A3).X is U\n    needZ(u)\n    needZ(x)\n", string.Empty },
        { "mzcn", RvY + "func f<T, U>(u: ref/U)\n    T is CyZ\n    T.(CyZ).X.(MidZ).Next is T\n    T.(A3).X is U\n    need(u)\n", "UnprovenConstraint_Kd" },
        { "yz", RvZ + "func f<T>(t: ref/T)\n    T is CyY\n    T.(CyY).X.(MidY).Next is T\n    needZ(t)\n", "UnprovenConstraint_Kd" },
        { "yzx", RvZ + "func f<T>(x: ref/T.(CyY).X.(MidY).Next)\n    T is CyY\n    T.(CyY).X.(MidY).Next is T\n    needZ(x)\n", string.Empty },
        { "yz2", RvZ + "func f<T>(t: ref/T, x: ref/T.(CyY).X.(MidY).Next)\n    T is CyY\n    T.(CyY).X.(MidY).Next is T\n    needZ(x)\n    needZ(t)\n", string.Empty },
        { "yz3", RvZ + "func f<T>(t: ref/T, x: ref/T.(CyY).X.(MidY).Next)\n    T is CyY\n    T.(CyY).X.(MidY).Next is T\n    needZ(t)\n    needZ(x)\n", string.Empty },
        { "yzn", RvZ + "func f<T>(t: ref/T)\n    T is CyY0\n    T.(CyY0).X.(MidY0).Next is T\n    needZ(t)\n", "UnprovenConstraint_Kd" },
        { "yza", RvZ + "func f<T, U>(u: ref/U)\n    T is CyY\n    T.(CyY).X.(MidY).Next is U\n    needZ(u)\n", string.Empty },
        { "q4b", RvA + "contract MidT\n    associate Next is Ca\n    Self.(MidT).Next is Other\ncontract MidT0\n    associate Next is Ca\ncontract MidTB\n    associate Next is Ca0\n    Self.(MidTB).Next is Other\ncontract Gq\n    associate R is MidT\ncontract Gq0\n    associate R is MidT0\ncontract GqB\n    associate R is MidTB\ncontract Ff\n    associate Q is Gq\ncontract Ff0\n    associate Q is Gq0\ncontract FfB\n    associate Q is GqB\ncontract Ee\n    associate Next is Ff\ncontract Ee0\n    associate Next is Ff0\ncontract EeB\n    associate Next is FfB\ncontract Dd\n    associate W is Ee\ncontract Dd0\n    associate W is Ee0\ncontract DdB\n    associate W is EeB\ncontract MidD\n    associate Next is Dd\ncontract MidD0\n    associate Next is Dd0\ncontract MidDB\n    associate Next is DdB\ncontract Cb2\n    associate Y is MidD\ncontract Cb20\n    associate Y is MidD0\ncontract Cb2B\n    associate Y is MidDB\nfunc needCa<X>(x: ref/X)\n    X is Ca\n    return\nfunc f<T, U, V, W>(t: ref/T, u: ref/U)\n    U is Cb2\n    U.(Cb2).Y.(MidD).Next is V\n    V.(Dd).W.(Ee).Next is W\n    W.(Ff).Q.(Gq).R.(MidT).Next is T\n    T.(Ca).X.(Mid).Next is U\n    need(u)\n    needO(t)\n    needCa(t)\n", string.Empty },
        { "q4bo", RvA + "contract MidT\n    associate Next is Ca\n    Self.(MidT).Next is Other\ncontract MidT0\n    associate Next is Ca\ncontract MidTB\n    associate Next is Ca0\n    Self.(MidTB).Next is Other\ncontract Gq\n    associate R is MidT\ncontract Gq0\n    associate R is MidT0\ncontract GqB\n    associate R is MidTB\ncontract Ff\n    associate Q is Gq\ncontract Ff0\n    associate Q is Gq0\ncontract FfB\n    associate Q is GqB\ncontract Ee\n    associate Next is Ff\ncontract Ee0\n    associate Next is Ff0\ncontract EeB\n    associate Next is FfB\ncontract Dd\n    associate W is Ee\ncontract Dd0\n    associate W is Ee0\ncontract DdB\n    associate W is EeB\ncontract MidD\n    associate Next is Dd\ncontract MidD0\n    associate Next is Dd0\ncontract MidDB\n    associate Next is DdB\ncontract Cb2\n    associate Y is MidD\ncontract Cb20\n    associate Y is MidD0\ncontract Cb2B\n    associate Y is MidDB\nfunc needCa<X>(x: ref/X)\n    X is Ca\n    return\nfunc f<T, U, V, W>(t: ref/T, u: ref/U)\n    T.(Ca).X.(Mid).Next is U\n    W.(Ff).Q.(Gq).R.(MidT).Next is T\n    V.(Dd).W.(Ee).Next is W\n    U.(Cb2).Y.(MidD).Next is V\n    U is Cb2\n    need(u)\n    needO(t)\n    needCa(t)\n", string.Empty },
        { "q4bt", RvA + "contract MidT\n    associate Next is Ca\n    Self.(MidT).Next is Other\ncontract MidT0\n    associate Next is Ca\ncontract MidTB\n    associate Next is Ca0\n    Self.(MidTB).Next is Other\ncontract Gq\n    associate R is MidT\ncontract Gq0\n    associate R is MidT0\ncontract GqB\n    associate R is MidTB\ncontract Ff\n    associate Q is Gq\ncontract Ff0\n    associate Q is Gq0\ncontract FfB\n    associate Q is GqB\ncontract Ee\n    associate Next is Ff\ncontract Ee0\n    associate Next is Ff0\ncontract EeB\n    associate Next is FfB\ncontract Dd\n    associate W is Ee\ncontract Dd0\n    associate W is Ee0\ncontract DdB\n    associate W is EeB\ncontract MidD\n    associate Next is Dd\ncontract MidD0\n    associate Next is Dd0\ncontract MidDB\n    associate Next is DdB\ncontract Cb2\n    associate Y is MidD\ncontract Cb20\n    associate Y is MidD0\ncontract Cb2B\n    associate Y is MidDB\nfunc needCa<X>(x: ref/X)\n    X is Ca\n    return\nfunc f<T, U, V, W>(t: ref/T, u: ref/U)\n    U is Cb2\n    U.(Cb2).Y.(MidD).Next is V\n    V.(Dd).W.(Ee).Next is W\n    W.(Ff).Q.(Gq).R.(MidT).Next is T\n    T.(Ca).X.(Mid).Next is U\n    needO(t)\n    needCa(t)\n    need(u)\n", string.Empty },
        { "q4bx", RvA + "contract MidT\n    associate Next is Ca\n    Self.(MidT).Next is Other\ncontract MidT0\n    associate Next is Ca\ncontract MidTB\n    associate Next is Ca0\n    Self.(MidTB).Next is Other\ncontract Gq\n    associate R is MidT\ncontract Gq0\n    associate R is MidT0\ncontract GqB\n    associate R is MidTB\ncontract Ff\n    associate Q is Gq\ncontract Ff0\n    associate Q is Gq0\ncontract FfB\n    associate Q is GqB\ncontract Ee\n    associate Next is Ff\ncontract Ee0\n    associate Next is Ff0\ncontract EeB\n    associate Next is FfB\ncontract Dd\n    associate W is Ee\ncontract Dd0\n    associate W is Ee0\ncontract DdB\n    associate W is EeB\ncontract MidD\n    associate Next is Dd\ncontract MidD0\n    associate Next is Dd0\ncontract MidDB\n    associate Next is DdB\ncontract Cb2\n    associate Y is MidD\ncontract Cb20\n    associate Y is MidD0\ncontract Cb2B\n    associate Y is MidDB\nfunc needCa<X>(x: ref/X)\n    X is Ca\n    return\nfunc f<T, U, V, W>(t: ref/T, u: ref/U, v: ref/U.(Cb2).Y.(MidD).Next)\n    U is Cb2\n    U.(Cb2).Y.(MidD).Next is V\n    V.(Dd).W.(Ee).Next is W\n    W.(Ff).Q.(Gq).R.(MidT).Next is T\n    T.(Ca).X.(Mid).Next is U\n    need(u)\n    needO(t)\n", string.Empty },
        { "q4bn", RvA + "contract MidT\n    associate Next is Ca\n    Self.(MidT).Next is Other\ncontract MidT0\n    associate Next is Ca\ncontract MidTB\n    associate Next is Ca0\n    Self.(MidTB).Next is Other\ncontract Gq\n    associate R is MidT\ncontract Gq0\n    associate R is MidT0\ncontract GqB\n    associate R is MidTB\ncontract Ff\n    associate Q is Gq\ncontract Ff0\n    associate Q is Gq0\ncontract FfB\n    associate Q is GqB\ncontract Ee\n    associate Next is Ff\ncontract Ee0\n    associate Next is Ff0\ncontract EeB\n    associate Next is FfB\ncontract Dd\n    associate W is Ee\ncontract Dd0\n    associate W is Ee0\ncontract DdB\n    associate W is EeB\ncontract MidD\n    associate Next is Dd\ncontract MidD0\n    associate Next is Dd0\ncontract MidDB\n    associate Next is DdB\ncontract Cb2\n    associate Y is MidD\ncontract Cb20\n    associate Y is MidD0\ncontract Cb2B\n    associate Y is MidDB\nfunc needCa<X>(x: ref/X)\n    X is Ca\n    return\nfunc f<T, U, V, W>(t: ref/T, u: ref/U)\n    U is Cb20\n    U.(Cb20).Y.(MidD0).Next is V\n    V.(Dd0).W.(Ee0).Next is W\n    W.(Ff0).Q.(Gq0).R.(MidT0).Next is T\n    T.(Ca).X.(Mid).Next is U\n    need(u)\n    needO(t)\n", "UnprovenConstraint_Kd" },
        { "hq4b", RvE + "contract Ca\n    associate X is Mid\ncontract Ca0\n    associate X is Mid0\ncontract MidT\n    associate Next is Ca\n    Self.(MidT).Next is Other\ncontract MidT0\n    associate Next is Ca\ncontract MidTB\n    associate Next is Ca0\n    Self.(MidTB).Next is Other\ncontract Gq\n    associate R is MidT\ncontract Gq0\n    associate R is MidT0\ncontract GqB\n    associate R is MidTB\ncontract Ff\n    associate Q is Gq\ncontract Ff0\n    associate Q is Gq0\ncontract FfB\n    associate Q is GqB\ncontract Ee\n    associate Next is Ff\ncontract Ee0\n    associate Next is Ff0\ncontract EeB\n    associate Next is FfB\ncontract Dd\n    associate W is Ee\ncontract Dd0\n    associate W is Ee0\ncontract DdB\n    associate W is EeB\ncontract MidD\n    associate Next is Dd\ncontract MidD0\n    associate Next is Dd0\ncontract MidDB\n    associate Next is DdB\ncontract Cb2\n    associate Y is MidD\ncontract Cb20\n    associate Y is MidD0\ncontract Cb2B\n    associate Y is MidDB\nfunc needCa<X>(x: ref/X)\n    X is Ca\n    return\nfunc fj4<T, U, V, W>(dst: uniq/U, p: raw/u8)\n    U is Cb2\n    U.(Cb2).Y.(MidD).Next is V\n    V.(Dd).W.(Ee).Next is W\n    W.(Ff).Q.(Gq).R.(MidT).Next is T\n    T.(Ca).X.(Mid).Next is U\n    return\nfunc fj4Bad<T, U, V, W>(x: U) -> ()\n    U is Cb2\n    U.(Cb2).Y.(MidD).Next is V\n    V.(Dd).W.(Ee).Next is W\n    W.(Ff).Q.(Gq).R.(MidT).Next is T\n    T.(Ca).X.(Mid).Next is U\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fj4<T, U, V, W>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", "ComparisonLoanConflict_Kd" },
        { "hq4bbare", RvE + "contract Ca\n    associate X is Mid\ncontract Ca0\n    associate X is Mid0\ncontract MidT\n    associate Next is Ca\n    Self.(MidT).Next is Other\ncontract MidT0\n    associate Next is Ca\ncontract MidTB\n    associate Next is Ca0\n    Self.(MidTB).Next is Other\ncontract Gq\n    associate R is MidT\ncontract Gq0\n    associate R is MidT0\ncontract GqB\n    associate R is MidTB\ncontract Ff\n    associate Q is Gq\ncontract Ff0\n    associate Q is Gq0\ncontract FfB\n    associate Q is GqB\ncontract Ee\n    associate Next is Ff\ncontract Ee0\n    associate Next is Ff0\ncontract EeB\n    associate Next is FfB\ncontract Dd\n    associate W is Ee\ncontract Dd0\n    associate W is Ee0\ncontract DdB\n    associate W is EeB\ncontract MidD\n    associate Next is Dd\ncontract MidD0\n    associate Next is Dd0\ncontract MidDB\n    associate Next is DdB\ncontract Cb2\n    associate Y is MidD\ncontract Cb20\n    associate Y is MidD0\ncontract Cb2B\n    associate Y is MidDB\nfunc needCa<X>(x: ref/X)\n    X is Ca\n    return\nfunc fj4<T, U, V, W>(dst: uniq/U, p: raw/u8)\n    U is Cb2B\n    U.(Cb2B).Y.(MidDB).Next is V\n    V.(DdB).W.(EeB).Next is W\n    W.(FfB).Q.(GqB).R.(MidTB).Next is T\n    T.(Ca0).X.(Mid0).Next is U\n    return\nfunc fj4Bad<T, U, V, W>(x: U) -> ()\n    U is Cb2B\n    U.(Cb2B).Y.(MidDB).Next is V\n    V.(DdB).W.(EeB).Next is W\n    W.(FfB).Q.(GqB).R.(MidTB).Next is T\n    T.(Ca0).X.(Mid0).Next is U\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fj4<T, U, V, W>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\n", string.Empty },
        { "fbi", RvF + "func applyC<F>(f: ref/F, p: raw/u8) -> ()\n    F is Callable<(raw/u8) -> ()>\n        effect confined\n    f(p)\n    return\ncontract InnerU\n    associate F\n    Self.(InnerU).F is Callable<(raw/u8) -> ()>\ncontract MidU\n    associate Next is InnerU\ncontract CyU\n    associate X is MidU\nfunc f<T>(w: ref/T.(InnerU).F, p: raw/u8) -> ()\n    T is CyU\n    T.(CyU).X.(MidU).Next is T\n    T.(InnerU).F is Callable<(raw/u8) -> ()>\n        effect confined\n    applyC(w, p)\n", string.Empty },
        { "fbin", RvF + "func applyC<F>(f: ref/F, p: raw/u8) -> ()\n    F is Callable<(raw/u8) -> ()>\n        effect confined\n    f(p)\n    return\ncontract InnerU\n    associate F\n    Self.(InnerU).F is Callable<(raw/u8) -> ()>\ncontract MidU\n    associate Next is InnerU\ncontract CyU\n    associate X is MidU\nfunc f<T>(w: ref/T.(InnerU).F, p: raw/u8) -> ()\n    T is CyU\n    T.(CyU).X.(MidU).Next is T\n    applyC(w, p)\n", "UnsatisfiedEffectBound_Kd" },
        { "fbiw", RvF + "func applyC<F>(f: ref/F, p: raw/u8) -> ()\n    F is Callable<(raw/u8) -> ()>\n        effect confined\n    f(p)\n    return\ncontract InnerU\n    associate F\n    Self.(InnerU).F is Callable<(raw/u8) -> ()>\ncontract MidU\n    associate Next is InnerU\ncontract CyU\n    associate X is MidU\nfunc f<T>(w: ref/T.(InnerU).F, p: raw/u8) -> ()\n    T is InnerU\n    T.(InnerU).F is Callable<(raw/u8) -> ()>\n        effect confined\n    applyC(w, p)\n", string.Empty },
        { "fbiwn", RvF + "func applyC<F>(f: ref/F, p: raw/u8) -> ()\n    F is Callable<(raw/u8) -> ()>\n        effect confined\n    f(p)\n    return\ncontract InnerU\n    associate F\n    Self.(InnerU).F is Callable<(raw/u8) -> ()>\ncontract MidU\n    associate Next is InnerU\ncontract CyU\n    associate X is MidU\nfunc f<T>(w: ref/T.(InnerU).F, p: raw/u8) -> ()\n    T is InnerU\n    applyC(w, p)\n", "UnsatisfiedEffectBound_Kd" },
        { "fci", RvF + "func applyC<F>(f: ref/F, p: raw/u8) -> ()\n    F is Callable<(raw/u8) -> ()>\n        effect confined\n    f(p)\n    return\ncontract InnerU\n    associate F\n    Self.(InnerU).F is Callable<(raw/u8) -> ()>\ncontract MidU\n    associate Next is InnerU\ncontract CyU\n    associate X is MidU\nfunc f<T>(w: ref/T.(InnerU).F, p: raw/u8) -> ()\n    T is CyU\n    T.(CyU).X.(MidU).Next is T\n    w(p)\n", string.Empty },
    };

    private const string Pq =
        "contract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\n";

    private const string PqNeeds =
        "func needIO<X>(x: ref/X)\n    X is Inner or Other\n    return\nfunc needOI<X>(x: ref/X)\n    X is Other or Inner\n" +
        "    return\nfunc needB<X>(x: ref/X)\n    X is Other\n    X is Inner\n    return\n";

    // PLAN G881 with G881b (the owner's O-321 (b); SPEC 8.3.3 Limits, 23.1.1): a request whose extension rounds pass
    // Binding.PremiseClosureLimit is refused, and a judgment that rests on it is a resource failure, never Unknown or Refuted. Pq's
    // path equation to a deeper projection (allowed) supplies one deeper identity a round, so `T.(Pq).Up is Inner` never ends by
    // itself (pathqn above; before the bound it did not finish). Each row is a whole program after CyclePrelude. A declaration
    // whose requests are refused has the limit's record, PremiseClosureLimit_Kd, once (SPEC 8.3.3 Limits: "reported once ... at
    // the declaration that reaches it"), at its first refused use, and a later refused use rests on it (pq2: two uses, one record;
    // F's ruling 206), alone (pqmix beside a proven use, pqorn, pqand, pqdn, pqodd); a request refused where no check's frame is
    // open (a bare acquisition's Copy, judged at HIR publication) has it at the function whose premises it read (pqcopy, pqcopy2);
    // a judgment other evidence proves stands and nothing is
    // recorded (pqor, pqoro: a disjunction's Proven operand; before the bound they did not finish); a request proven within the
    // bound is proven (pqd1, pqd2, pqcopyv); a value that is moved needs no such judgment (pqmovev, pqletv, pqrefv ask none;
    // pqcallv's is MIR's Copy question after the pass, which keeps Unknown and fails no node; before the bound it did not finish).
    // The owner's O-335 (a) (SPEC 10.1, 10.4): a refusal a candidate trial reads is held to the selection and settled only by the
    // selected candidate's obligation, so a losing candidate whose `X is Inner` reaches the bound refuses nothing (xov, xovr: step 4
    // selects the candidate without the default, in either declaration order; xov0 the same loser without Pq), and the selected one's
    // is the limit's record at the call (xovn). Twins of other judgments a check reads: a bare owned Place to `ref/X` asks no Copy
    // (xbare), a signature's struct Constraint (xstruct, xstructv) and a conditional conformance's `when` premise (xcond, xcondv).
    public static TheoryData<string, string, string> RoundBoundRows => new()
    {
        { "xov", Pq + "func pick<X>(x: ref/X) -> () => ()\nfunc pick<X>(x: ref/X, tag: i32 = 0) -> ()\n    X is Inner\n    return\nfunc f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    pick(x)\n", string.Empty },
        { "xovr", Pq + "func pick<X>(x: ref/X, tag: i32 = 0) -> ()\n    X is Inner\n    return\nfunc pick<X>(x: ref/X) -> () => ()\nfunc f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    pick(x)\n", string.Empty },
        { "xovn", Pq + "func pick<X>(x: ref/X, tag: i32 = 0) -> ()\n    X is Inner\n    return\nfunc f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    pick(x)\n", "PremiseClosureLimit_Kd" },
        { "xov0", Pq + "func pick<X>(x: ref/X) -> () => ()\nfunc pick<X>(x: ref/X, tag: i32 = 0) -> ()\n    X is Inner0\n    return\nfunc f<T, U>(x: ref/U)\n    U is Mid\n    pick(x)\n", string.Empty },
        { "xbare", Pq + "func g<X>(x: ref/X) => ()\nfunc f<T>(x: T.(Pq).Up)\n    T is Pq\n    g(x)\n", string.Empty },
        { "xstruct", Pq + "struct Box<X>\n    X is Inner\n    var v: X\nfunc f<T>(x: ref/Box<T.(Pq).Up>)\n    T is Pq\n    return\n", "PremiseClosureLimit_Kd" },
        { "xstructv", Pq + "struct Box<X>\n    X is Inner\n    var v: X\nfunc f<T>(x: ref/Box<T.(Pq).Up>)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    return\n", string.Empty },
        { "xcond", Pq + "contract Flag\nfunc needF<X>(x: ref/X)\n    X is Flag\n    return\nstruct Wrap<X>\n    var v: X\n    Self is Flag when X is Inner\nfunc f<T>(w: ref/Wrap<T.(Pq).Up>)\n    T is Pq\n    needF(w)\n", "PremiseClosureLimit_Kd" },
        { "xcondv", Pq + "contract Flag\nfunc needF<X>(x: ref/X)\n    X is Flag\n    return\nstruct Wrap<X>\n    var v: X\n    Self is Flag when X is Inner\nfunc f<T>(w: ref/Wrap<T.(Pq).Up>)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    needF(w)\n", string.Empty },
        { "pq2", Pq + "func f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    need(x)\n    need(x)\n", "PremiseClosureLimit_Kd" },
        { "pqmix", Pq + "func f<T, V>(x: ref/T.(Pq).Up, o: ref/V)\n    T is Pq\n    V is Other\n    needO(o)\n    need(x)\n", "PremiseClosureLimit_Kd" },
        { "pqcopy", Pq + "func f<T>(x: T.(Pq).Up) -> T.(Pq).Up\n    T is Pq\n    return x\n", "PremiseClosureLimit_Kd" },
        { "pqcopyv", Pq + "func f<T>(x: T.(Pq).Up) -> T.(Pq).Up\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Up is Copy\n    return x\n", string.Empty },
        { "pqcopy2", Pq + "func f<T>(x: T.(Pq).Up) -> T.(Pq).Up\n    T is Pq\n    let y = x\n    return x\n", "PremiseClosureLimit_Kd" },
        { "pqor", Pq + PqNeeds + "func f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    T.(Pq).Up is Other\n    needIO(x)\n", string.Empty },
        { "pqoro", Pq + PqNeeds + "func f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    T.(Pq).Up is Other\n    needOI(x)\n", string.Empty },
        { "pqorn", Pq + PqNeeds + "func f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    needIO(x)\n", "PremiseClosureLimit_Kd" },
        { "pqand", Pq + PqNeeds + "func f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    T.(Pq).Up is Other\n    needB(x)\n", "PremiseClosureLimit_Kd" },
        { "pqmovev", Pq + "func f<T>(x: T.(Pq).Up) -> T.(Pq).Up\n    T is Pq\n    return x@move\n", string.Empty },
        { "pqletv", Pq + "func f<T>(x: T.(Pq).Up) -> T.(Pq).Up\n    T is Pq\n    let y = x@move\n    return y@move\n", string.Empty },
        { "pqrefv", Pq + "func f<T>(x: ref/T.(Pq).Up) -> ref/T.(Pq).Up\n    T is Pq\n    return x\n", string.Empty },
        { "pqcallv", Pq + "func f<T>(x: T.(Pq).Up)\n    T is Pq\n    consume(x@move)\n", string.Empty },
        { "pqd1", Pq + "func f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    need(x)\n", string.Empty },
        { "pqd2", Pq + "func f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    need(x)\n", string.Empty },
        { "pqdn", Pq + "func f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner0\n    need(x)\n", "PremiseClosureLimit_Kd" },
        { "pqodd", Pq + "func f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    need(x)\n", "PremiseClosureLimit_Kd" },
        // The Middle's C-335 re-check (F1, F2). F1, the owner's O-345 (b) (R7-C2c2 c2-4b R4.13): one declaration that asks both
        // spellings of the Type gets one verdict for both, in either order (sp34-ab, sp34-ba: the declaration's one limit record, as
        // spm-a2 and pq2; before R4.13 z's use supply let both prove), and premise order changes nothing (sp34-p). F2 (a PLAN open
        // issue to come: record selection): a member call found through Inner or Mkr at `T.Next^4.Up` or deeper is refused with the
        // limit's record at the call (dg-*, dm-*; dev gives UnresolvedBinding_Kd), while `need(x)` at the same depths is proven (dn-*).
        { "sp34-p", Pq + "func f<T>(x: ref/T.(Pq).Up)\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    T is Pq\n    need(x)\n", "PremiseClosureLimit_Kd" },
        { "sp34-ab", Pq + "func f<T>(x: ref/T.(Pq).Up, z: ref/T.(Pq).Next.(Pq).Next.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    need(x)\n    need(z)\n", "PremiseClosureLimit_Kd" },
        { "sp34-ba", Pq + "func f<T>(x: ref/T.(Pq).Up, z: ref/T.(Pq).Next.(Pq).Next.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    need(z)\n    need(x)\n", "PremiseClosureLimit_Kd" },
        { "dg-2", Pq + "func f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    let g = x.get()\n    return\n", "PremiseClosureLimit_Kd" },
        { "dg-3", Pq + "func f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    let g = x.get()\n    return\n", "PremiseClosureLimit_Kd" },
        { "dg-8", Pq + "func f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    let g = x.get()\n    return\n", "PremiseClosureLimit_Kd" },
        { "dm-2", Pq + "contract Mkr\n    func make(self: ref/Self) -> Self\nfunc f<T>(x: ref/T.(Pq).Up) -> T.(Pq).Up\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Mkr\n    return x.make()\n", "PremiseClosureLimit_Kd" },
        { "dm-3", Pq + "contract Mkr\n    func make(self: ref/Self) -> Self\nfunc f<T>(x: ref/T.(Pq).Up) -> T.(Pq).Up\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Mkr\n    return x.make()\n", "PremiseClosureLimit_Kd" },
        { "dm-8", Pq + "contract Mkr\n    func make(self: ref/Self) -> Self\nfunc f<T>(x: ref/T.(Pq).Up) -> T.(Pq).Up\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Mkr\n    return x.make()\n", "PremiseClosureLimit_Kd" },
        { "dn-2", Pq + "func f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    need(x)\n", string.Empty },
        { "dn-3", Pq + "func f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    need(x)\n", string.Empty },
        { "dn-8", Pq + "func f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    need(x)\n", string.Empty },
    };

    [Theory]
    [MemberData(nameof(RoundBoundRows))]
    public void ARequestPastTheRoundBoundIsAResourceFailure(string row, string source, string codes)
    {
        var c = MinimalEmissionTest.Analyze(CyclePrelude + source + "public func main() => ()\n");
        var found = string.Join(" ", CompilationTestHelper.PublishedErrors(c).Select(static x => x.Code).Order(StringComparer.Ordinal));
        Assert.True(codes == found, row + ": " + found);
        Assert.True(DiagnosticEntries.TryGet(DiagnosticCode.PremiseClosureLimit_Kd, out var limit) && limit.Category == DiagnosticCategory.Resource);
    }

    // The Middle's C-335 re-check, whole programs (no prelude). F1, the owner's O-345 (b) (SPEC 8.3.3 Limits: whether a limit is
    // reached never depends on spelling; R7-C2c2 c2-4b, DECISIONS R4.13: an extending request is its class's closure over the
    // declaration's grounds): x at `T.(Pq).Up` (spm-a), `T.Next^2.Up` (spm-b), `T.Next^4.Up` (spm-c), `T.Next^16.Up` (spm-e16),
    // `T.Next^32.Up` (spm-e32), x at `T.Next^64.Up` (spm-d: with c2-4b commit (2)'s forms; it did not finish before), and spm-a
    // beside an unused parameter spelled `T.Next^2.Up` (spm-ay) all reach the bound with the class
    // root's count: the limit's record at `need(x)` (before R4.13 the use supply of x's or y's spelling let all but spm-a prove; valid
    // programs SPEC 8.3.3 Limits lets the limit refuse); so do Pq1's (stride 1, the premise at 34 links) s1-a and s1-d32 (x at
    // `T.Next^32.Up`). x's prefix requests `T.(Pq).Next^k is Pq` are first walks that prove (8.3.2), so f without `need(x)` is accepted
    // (pfx-b, pfx-e16). Pinned as this head gives them, the owner's O-413 (a) known limit against 8.3.3 (STATUS) until R7-C3: a request
    // whose first walk proves through a use's supply never extends, so s1-a beside an unused `y` at `T.Next^33.Up` (s1-ay33) is
    // accepted, and so is s1-a after a body statement naming `T.Next^34` (bd-before), while the same two statements swapped reach the
    // bound (bd-after). spm-a2 (c2-4b's dr.a2, F's ruling 206): spm-a asking `need(x)` twice has the declaration's one record (SPEC
    // 8.3.3; dev 617c3db04 gives UnprovenConstraint_Kd at each use, G881b before ruling 206 the limit's record at each). F2 (a PLAN
    // open issue to come): the dg and dm forms without another Contract declaring the member (-solo) are refused at the call as with
    // the prelude.
    public static TheoryData<string, string, string> RoundBoundProgramRows => new()
    {
        { "spm-a", "contract Inner\ncontract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    need(x)\npublic func main() => ()\n", "PremiseClosureLimit_Kd" },
        { "spm-a2", "contract Inner\ncontract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    need(x)\n    need(x)\npublic func main() => ()\n", "PremiseClosureLimit_Kd" },
        { "spm-b", "contract Inner\ncontract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc f<T>(x: ref/T.(Pq).Next.(Pq).Next.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    need(x)\npublic func main() => ()\n", "PremiseClosureLimit_Kd" },
        { "spm-c", "contract Inner\ncontract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc f<T>(x: ref/T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    need(x)\npublic func main() => ()\n", "PremiseClosureLimit_Kd" },
        { "spm-e16", "contract Inner\ncontract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc f<T>(x: ref/T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    need(x)\npublic func main() => ()\n", "PremiseClosureLimit_Kd" },
        { "spm-e32", "contract Inner\ncontract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc f<T>(x: ref/T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    need(x)\npublic func main() => ()\n", "PremiseClosureLimit_Kd" },
        { "spm-d", "contract Inner\ncontract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc f<T>(x: ref/T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    need(x)\npublic func main() => ()\n", "PremiseClosureLimit_Kd" },
        { "spm-ay", "contract Inner\ncontract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc f<T>(x: ref/T.(Pq).Up, y: ref/T.(Pq).Next.(Pq).Next.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    need(x)\npublic func main() => ()\n", "PremiseClosureLimit_Kd" },
        { "pfx-b", Spm(2, string.Empty, "    return\n"), string.Empty },
        { "pfx-e16", Spm(16, string.Empty, "    return\n"), string.Empty },
        { "s1-a", Pq1(0, string.Empty, "    need(x)\n"), "PremiseClosureLimit_Kd" },
        { "s1-d32", Pq1(32, string.Empty, "    need(x)\n"), "PremiseClosureLimit_Kd" },
        { "s1-ay33", Pq1(0, ", y: ref/T" + Next(33) + ".(Pq).Up", "    need(x)\n"), string.Empty },
        { "bd-before", PqProgram(1, "func mark<Y>()\n    return\n", 0, string.Empty, "    mark<T" + Next(34) + ">()\n    need(x)\n"), string.Empty },
        { "bd-after", PqProgram(1, "func mark<Y>()\n    return\n", 0, string.Empty, "    need(x)\n    mark<T" + Next(34) + ">()\n"), "PremiseClosureLimit_Kd" },
        { "dg-2-solo", "contract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\nfunc f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    let g = x.get()\n    return\npublic func main() => ()\n", "PremiseClosureLimit_Kd" },
        { "dg-3-solo", "contract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\nfunc f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    let g = x.get()\n    return\npublic func main() => ()\n", "PremiseClosureLimit_Kd" },
        { "dg-8-solo", "contract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\nfunc f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Inner\n    let g = x.get()\n    return\npublic func main() => ()\n", "PremiseClosureLimit_Kd" },
        { "dm-2-solo", "contract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\ncontract Mkr\n    func make(self: ref/Self) -> Self\nfunc f<T>(x: ref/T.(Pq).Up) -> T.(Pq).Up\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Mkr\n    return x.make()\npublic func main() => ()\n", "PremiseClosureLimit_Kd" },
        { "dm-3-solo", "contract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\ncontract Mkr\n    func make(self: ref/Self) -> Self\nfunc f<T>(x: ref/T.(Pq).Up) -> T.(Pq).Up\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Mkr\n    return x.make()\npublic func main() => ()\n", "PremiseClosureLimit_Kd" },
        { "dm-8-solo", "contract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\ncontract Mkr\n    func make(self: ref/Self) -> Self\nfunc f<T>(x: ref/T.(Pq).Up) -> T.(Pq).Up\n    T is Pq\n    T.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Next.(Pq).Up is Mkr\n    return x.make()\npublic func main() => ()\n", "PremiseClosureLimit_Kd" },
    };

    [Theory]
    [MemberData(nameof(RoundBoundProgramRows))]
    public void ARoundBoundProgramHasItsRecords(string row, string source, string codes)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var found = string.Join(" ", CompilationTestHelper.PublishedErrors(c).Select(static x => x.Code).Order(StringComparer.Ordinal));
        Assert.True(codes == found, row + ": " + found);
    }

    // R7-C2c2 c2-4b commit (2) (design R3.5, R4.12; SPEC 8.2.3 Cycles and Identity premises): Triage8's rows, a parameter identical
    // to its own projection (`T is T.(C).X`, either side) and its twins: every use reads the Types in the class's form in its view,
    // so each valid row is accepted with no record (before: InternalInvariant_Kd from MIR's Type agreement for t00, t07-t12, t15,
    // t16, NoApplicableOverload_Kd for t14's call, TypeMismatch_Kd for t17's return); t13-ownbare, a bare owned value used as a
    // value, keeps its TransferRequired_Kd (SPEC 3.5).
    [Theory]
    [InlineData("t8.t00-cyc2", "contract C\n    associate X\nfunc f<T>(x: ref/T)\n    T is C\n    T is T.(C).X\n    let y = x\n    ()\npublic func main() -> ()\n    Console.writeLine(\"ok\")\n", "")]
    [InlineData("t8.t01-nolet", "contract C\n    associate X\nfunc f<T>(x: ref/T)\n    T is C\n    T is T.(C).X\n    ()\npublic func main() -> ()\n    Console.writeLine(\"ok\")\n", "")]
    [InlineData("t8.t02-nocycle", "contract C\n    associate X\nfunc f<T>(x: ref/T)\n    T is C\n    let y = x\n    ()\npublic func main() -> ()\n    Console.writeLine(\"ok\")\n", "")]
    [InlineData("t8.t03-swap", "contract C\n    associate X\nfunc f<T>(x: ref/T)\n    T is C\n    T.(C).X is T\n    let y = x\n    ()\npublic func main() -> ()\n    Console.writeLine(\"ok\")\n", "")]
    [InlineData("t8.t05-paramid", "contract C\n    associate X\nfunc f<T, U>(x: ref/T)\n    T is C\n    T is U\n    let y = x\n    ()\npublic func main() -> ()\n    Console.writeLine(\"ok\")\n", "")]
    [InlineData("t8.t06-projparam", "contract C\n    associate X\nfunc f<T, U>(x: ref/U)\n    T is C\n    U is T.(C).X\n    let y = x\n    ()\npublic func main() -> ()\n    Console.writeLine(\"ok\")\n", "")]
    [InlineData("t8.t07-projx", "contract C\n    associate X\nfunc f<T>(x: ref/T.(C).X)\n    T is C\n    T is T.(C).X\n    let y = x\n    ()\npublic func main() -> ()\n    Console.writeLine(\"ok\")\n", "")]
    [InlineData("t8.t08-annot", "contract C\n    associate X\nfunc f<T>(x: ref/T)\n    T is C\n    T is T.(C).X\n    let y: ref/T = x\n    ()\npublic func main() -> ()\n    Console.writeLine(\"ok\")\n", "")]
    [InlineData("t8.t09-discard", "contract C\n    associate X\nfunc f<T>(x: ref/T)\n    T is C\n    T is T.(C).X\n    _ = x\n    ()\npublic func main() -> ()\n    Console.writeLine(\"ok\")\n", "")]
    [InlineData("t8.t10-copy", "contract C\n    associate X\nfunc f<T>(x: ref/T)\n    T is C\n    T is T.(C).X\n    let y = x@copy\n    ()\npublic func main() -> ()\n    Console.writeLine(\"ok\")\n", "")]
    [InlineData("t8.t11-move", "contract C\n    associate X\nfunc f<T>(x: ref/T)\n    T is C\n    T is T.(C).X\n    let y = x@move\n    ()\npublic func main() -> ()\n    Console.writeLine(\"ok\")\n", "")]
    [InlineData("t8.t12-ownmove", "contract C\n    associate X\nfunc f<T>(x: T)\n    T is C\n    T is T.(C).X\n    let y = x@move\n    ()\npublic func main() -> ()\n    Console.writeLine(\"ok\")\n", "")]
    [InlineData("t8.t13-ownbare", "contract C\n    associate X\nfunc f<T>(x: T)\n    T is C\n    T is T.(C).X\n    let y = x\n    ()\npublic func main() -> ()\n    Console.writeLine(\"ok\")\n", "TransferRequired_Kd: let y = x")]
    [InlineData("t8.t14-call", "contract C\n    associate X\nfunc g<U>(v: ref/U)\n    ()\nfunc f<T>(x: ref/T)\n    T is C\n    T is T.(C).X\n    g(x)\npublic func main() -> ()\n    Console.writeLine(\"ok\")\n", "")]
    [InlineData("t8.t15-uniq", "contract C\n    associate X\nfunc f<T>(x: uniq/T)\n    T is C\n    T is T.(C).X\n    let y = x\n    ()\npublic func main() -> ()\n    Console.writeLine(\"ok\")\n", "")]
    [InlineData("t8.t16-var", "contract C\n    associate X\nfunc f<T>(x: ref/T)\n    T is C\n    T is T.(C).X\n    var y = x\n    ()\npublic func main() -> ()\n    Console.writeLine(\"ok\")\n", "")]
    [InlineData("t8.t17-rv", "contract C\n    associate X\nfunc f<T>(x: T) -> T\n    T is C\n    T is T.(C).X\n    return x@move\npublic func main() -> ()\n    Console.writeLine(\"ok\")\n", "")]
    [InlineData("t8.t18-ownret", "contract C\n    associate X\nfunc f<T>(x: T) -> T\n    T is C\n    return x@move\npublic func main() -> ()\n    Console.writeLine(\"ok\")\n", "")]
    [InlineData("t8.t19-refret", "contract C\n    associate X\nfunc f<T>(x: ref/T) -> ref/T\n    T is C\n    T is T.(C).X\n    return x\npublic func main() -> ()\n    Console.writeLine(\"ok\")\n", "")]
    public void ATriage8UseReadsItsClassesForm(string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [c2-4b]", program, records);

    // R7-C2c2 c2-4b (DECISIONS R4.13; the owner's O-345 (b)): an extending request re-roots whenever its class's table keys its root
    // lower, and starts its closure again from that key (Binding.IdentityRestarts, exact). mate: `T.(Q).C.(Q).Up` is `T.(Q).B.(Q).Up`
    // (T's clause) and that is `T.(Q).B.(Q).A` (T.B's clause, joined by a round), where the premise proves `need(x)` (mate-v, valid:
    // SPEC 8.2.3's grounds include the identities `T is Q` supplies at every depth); with the premise at `T.(Q).B.(Q).B.(Q).A` no
    // identity reaches x's class (mate-i, invalid: the proof is missing, SPEC 8.3.2). spm-a's x at `T.(Pq).Up` and spm-b's at
    // `T.Next^2.Up` each re-root once, to the least key their first round's table gives them (measured; R4.4's shape).
    [Theory]
    [MemberData(nameof(ReRootRows))]
    public void AnExtendingRequestReRootsToItsClassesLeastKey(string row, string source, int restarts, string codes)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(codes, string.Join(" ", CompilationTestHelper.PublishedErrors(c).Select(static x => x.Code)));
        Assert.True(restarts == c.Binding.IdentityRestarts, $"{row}: {c.Binding.IdentityRestarts} re-rootings");
    }

    public static TheoryData<string, string, int, string> ReRootRows => new()
    {
        { "mate-v", Mate("T.(Q).B.(Q).A"), 2, string.Empty },
        { "mate-i", Mate("T.(Q).B.(Q).B.(Q).A"), 2, "UnprovenConstraint_Kd" },
        { "spm-a", Spm(0, string.Empty, "    need(x)\n"), 0, "PremiseClosureLimit_Kd" }, // R7-C3 (C-c): keyed by its class from the start, no re-root
        { "spm-b", Spm(2, string.Empty, "    need(x)\n"), 0, "PremiseClosureLimit_Kd" },
    };

    private const string PqUses = "func f<T>(x: ref/T.(Pq).Up, y: ref/T.(Pq).Up)\n    T is Pq\n";

    private const string HasItems = "contract Has1\n    associate Item is Inner\ncontract Has2\n    associate Item is Inner\ncontract Has3\n    associate Item is Inner\n";

    private const string BoxWhen = HasItems + "struct Box<T>\n    var v: i32\n    Self is Has1 when T is Pq\n        associate Has1.Item is T.(Pq).Up\n" +
        "    Self is Has2 when T is Pq\n        associate Has2.Item is T.(Pq).Up\n";

    // The delta re-check's ov3, line for line: two paths to one conformance (D: C), C's under `T.Next^68.Up is Inner`, D's under that,
    // `T.Next^69.Up is Inner` and `T is Copy`; D's premise closure reaches the bound.
    private static readonly string Ov3SelfD = "    Self is D when T is Pq, T" + string.Concat(Enumerable.Repeat(".(Pq).Next", 68)) + ".(Pq).Up is Inner, T" +
        string.Concat(Enumerable.Repeat(".(Pq).Next", 69)) + ".(Pq).Up is Inner, T is Copy";

    private static readonly string Ov3 = "contract Inner\n    associate Z\n" + Pq + "contract C\n    associate Item\ncontract D: C\nstruct Box<T>\n    var v: i32\n" +
        "    Self is C when T is Pq, T" + string.Concat(Enumerable.Repeat(".(Pq).Next", 68)) + ".(Pq).Up is Inner\n" +
        "        associate C.Item is T.(Pq).Up.(Inner).Z\n" + Ov3SelfD + "\n        associate C.Item is T.(Pq).Next.(Pq).Up.(Inner).Z\npublic func main() => ()\n";

    // PLAN G881b, F's ruling 216 (SPEC 8.3.3 Limits: the limit "is reported once ... at the declaration that reaches it"; 8.2.1,
    // 7.5): the declaration whose premise closure reaches the bound is the nearest one with a Constraint region, so a refused use in
    // an `if`, `do`, `while` or `for` body, a match arm or a Closure (none states premises: a Closure's body is an ExecutableBody,
    // F.9.1) is its definition's and rests on the definition's one record, at its first refused use (the review's tw-blk, tw-blk2,
    // tw-do, tw-while with the nested use on `y`, so the record's line names the use: N1); a named function written in the body is
    // its own declaration (tw-local: two records); a Copy judged with no check frame open records at the declaration (tw-copyblk).
    // Each row is a whole declaration after CyclePrelude and Pq; b0cec6e90 gave the block's, arm's or Closure's own record too.
    // F's ruling 220 (SPEC 8.2.3, 8.8.1: a conditional conformance's scope is a constrained declaration of its own): a conformance
    // path's requests extend the closure of the declaration whose premises it extends, so each conditional conformance has its own
    // record (tw-cc2, tw-cc3; de975db32 gave the Type's one) and a direct conformance's are its Type's (tw-ccdir: one record).
    public static TheoryData<string, string, string> RoundBoundDeclarationRows => new()
    {
        { "tw-blk", PqUses + "    need(x)\n    if true\n        need(y)\n", "PremiseClosureLimit_Kd: need(x)" },
        { "tw-blk2", PqUses + "    if true\n        need(y)\n    need(x)\n", "PremiseClosureLimit_Kd: need(y)" },
        { "tw-do", PqUses + "    need(x)\n    do\n        need(y)\n", "PremiseClosureLimit_Kd: need(x)" },
        { "tw-while", PqUses + "    need(x)\n    while true\n        need(y)\n", "PremiseClosureLimit_Kd: need(x)" },
        { "tw-for", PqUses + "    need(x)\n    for _ in 0..2\n        need(y)\n", "PremiseClosureLimit_Kd: need(x)" },
        { "tw-arm", PqUses + "    need(x)\n    match 1\n        1 => need(y)\n        _ => ()\n", "PremiseClosureLimit_Kd: need(x)" },
        { "tw-closure", PqUses + "    need(x)\n    let g = func () => need(y)\n", "PremiseClosureLimit_Kd: need(x)" },
        { "tw-local", PqUses + "    need(x)\n    func g<U>(z: ref/U.(Pq).Up)\n        U is Pq\n        need(z)\n", "PremiseClosureLimit_Kd: need(x) | PremiseClosureLimit_Kd: need(z)" },
        { "tw-copyblk", "func f<T>(x: T.(Pq).Up) -> T.(Pq).Up\n    T is Pq\n    if true\n        return x\n    return x\n", "PremiseClosureLimit_Kd: func f<T>(x: T.(Pq).Up) -> T.(Pq).Up" },
        { "tw-cc2", BoxWhen, "PremiseClosureLimit_Kd: Self is Has1 when T is Pq | PremiseClosureLimit_Kd: Self is Has2 when T is Pq" },
        { "tw-cc3", BoxWhen + "    Self is Has3 when T is Pq\n        associate Has3.Item is T.(Pq).Up\n", "PremiseClosureLimit_Kd: Self is Has1 when T is Pq | PremiseClosureLimit_Kd: Self is Has2 when T is Pq | PremiseClosureLimit_Kd: Self is Has3 when T is Pq" },
        { "tw-ccdir", HasItems + "struct Box<T>\n    T is Pq\n    var v: i32\n    Self is Has1\n    associate Has1.Item is T.(Pq).Up\n    Self is Has2\n    associate Has2.Item is T.(Pq).Up\n", "PremiseClosureLimit_Kd: Self is Has1" },
    };

    [Theory]
    [MemberData(nameof(RoundBoundDeclarationRows))]
    public void ADeclarationsLimitIsRecordedOnceAtItsFirstRefusedUse(string row, string source, string records)
        => CompilationTestHelper.AssertRecordsAtLines(row, CyclePrelude + Pq + source + "public func main() => ()\n", records);

    // F's ruling 222 (SPEC 8.3.3 Limits, 23.1.2): a check that reads a declaration the premise-closure limit refused rests on the
    // limit's record and keeps none of its own: in ov3 the agreement of C's two paths (at `Self is C`) and the use check of `Self is D`
    // read D's conditional conformance, whose closure reached the bound (8d4e74206 gave each an InvalidConstraint_Kd beside the limit).
    // ov3-own: C's specification names no associate of Inner, an independent failure whose records stay (23.1.2).
    public static TheoryData<string, string, string> RefusedDeclarationReadRows => new()
    {
        { "ov3", Ov3, "PremiseClosureLimit_Kd: " + Ov3SelfD.Trim() },
        {
            "ov3-own", Ov3.Replace("(Inner).Z\n    Self is D", "(Inner).Zz\n    Self is D", StringComparison.Ordinal),
            "InvalidAssociatedType_Kd: " + Ov3.Split('\n')[11].Trim() + " | PremiseClosureLimit_Kd: " + Ov3SelfD.Trim() +
            " | UnresolvedBinding_Kd: associate C.Item is T.(Pq).Up.(Inner).Zz"
        },
    };

    [Theory]
    [MemberData(nameof(RefusedDeclarationReadRows))]
    public void ACheckReadingARefusedDeclarationRestsOnItsLimit(string row, string source, string records)
        => CompilationTestHelper.AssertRecordsAtLines(row, source, records);

    // PLAN G881 with G881b: a query made after the pass has counted its failures fails no node. MIR's destruction analysis asks Copy
    // of `T.(Pq).Up` (pqcallv, valid), a request that reaches the round bound: it keeps Unknown, as before the bound's rounds, and a
    // re-check finds the pass as it was counted (a version that failed `func f` there left a node failed after the count).
    [Fact]
    public void ARequestAfterThePassFailsNoNode()
    {
        var c = MinimalEmissionTest.Analyze(CyclePrelude + Pq + "func f<T>(x: T.(Pq).Up)\n    T is Pq\n    consume(x@move)\npublic func main() => ()\n");
        Assert.True(CompilationTestHelper.PublishedErrors(c).Length == 0, MinimalEmissionTest.Describe(c));
        Assert.True(c.Binding.IdentityRounds >= Binding.PremiseClosureLimit, $"{c.Binding.IdentityRounds} rounds");
        Assert.True(c.Binding.CheckBound().IsComplete, MinimalEmissionTest.Describe(c));
    }

    // PLAN G881 with G881b (the owner's O-335 (a); RP8's warm bytes): a refused request's Refusal is its node's, kept across passes,
    // so a warm rebind whose losing candidate's request reaches the round bound again allocates nothing (xovm; a new Refusal each
    // pass was 40 bytes). IdentityRounds only grows, so the measured Binds' own rounds are its growth across the measurement: each
    // of its operations (warm-up and measured) makes the refused request again, the bound's rounds each time.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebindPastTheRoundBoundAllocatesNothing()
    {
        var c = CompilationTestHelper.ParseSuccess(
            "contract Inner\n" + Pq + "func pick<X>(x: ref/X) -> () => ()\nfunc pick<X>(x: ref/X, tag: i32 = 0) -> ()\n    X is Inner\n    return\n" +
            "func f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    pick(x)\npublic func main() => ()\n");
        Assert.True(c.Bind().IsComplete);
        var before = c.Binding.IdentityRounds;
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
        var grown = c.Binding.IdentityRounds - before;
        const int Operations = AllocationMeasurement.WarmupOperations + AllocationMeasurement.MeasuredOperations;
        Assert.True(grown >= Binding.PremiseClosureLimit * Operations, $"{grown} rounds in the measured Binds");
    }

    // The rows' programs: Pq's chains (n links of `.(Pq).Next`); Pq of stride 2 with the premise at 68 links (spm) or of stride 1 at
    // 34 links (Pq1), `f`'s x at `T.Next^x.Up`, more parameters, its body and declarations before it; mate (Q's two clauses, the
    // premise `supplied is Inner`).
    private static string Next(int n) => string.Concat(Enumerable.Repeat(".(Pq).Next", n));

    private static string Spm(int x, string parameters, string body) => PqProgram(2, string.Empty, x, parameters, body);

    private static string Pq1(int x, string parameters, string body) => PqProgram(1, string.Empty, x, parameters, body);

    private static string PqProgram(int stride, string declarations, int x, string parameters, string body)
        => "contract Inner\ncontract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self" + Next(stride) + ".(Pq).Up\n" +
            "func need<X>(x: ref/X)\n    X is Inner\n    return\n" + declarations + "func f<T>(x: ref/T" + Next(x) + ".(Pq).Up" + parameters + ")\n" +
            "    T is Pq\n    T" + Next(34 * stride) + ".(Pq).Up is Inner\n" + body + "public func main() => ()\n";

    private static string Mate(string supplied)
        => "contract Inner\ncontract Q\n    associate A is Q\n    associate B is Q\n    associate C is Q\n    associate Up\n" +
            "    Self.(Q).C.(Q).Up is Self.(Q).B.(Q).Up\n    Self.(Q).Up is Self.(Q).A\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\n" +
            $"func f<T>(x: ref/T.(Q).C.(Q).Up)\n    T is Q\n    {supplied} is Inner\n    need(x)\npublic func main() => ()\n";
}
