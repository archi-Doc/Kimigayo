// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// R7-I1 (SPEC 10.5, 10.8, 14.9.2): the acceptance rows R7-I2 and R7-I3 are judged by, each with its twin where it has one. The forms are
// revision B's list (its reviews' probes, spec-b\a-recheck), labelled from the SPEC on dev; forms only revision B's held text decides are not here.
public partial class ApplicabilityChainRegressionTest
{
    private const string Sources = "func identity<T>(v: T) -> T => v@move\nfunc g<T>(x: T) -> i32 => 0\nfunc ga<T>(x: Array<T>) -> i32 => 0\nfunc h2<T>(a: T, b: T) -> i32 => 0\nfunc p2<T>(x: (T, T)) -> i32 => 0\nfunc ha<T>(x: [2 of T]) -> i32 => 0\nfunc fi2(x: [2 of i32]) -> i32 => 0\nfunc fai(x: Array<i32>) -> i32 => 0\nfunc fd(x: Dictionary<i64, i64>) -> i32 => 0\nfunc fd(x: Dictionary<i64, bool>) -> i32 => 1\nfunc fail() -> Never => $abort(\"no\")\n";

    private const string SourcesBody = "func k(c: bool, d: bool, x: i64, b: bool, y: i32, xs: Array<i32>) -> i32\n    var r: i32 = 0\n    var u: i32 = 1\n    var v: i32 = 2\n";

    private const string RefSources = "func identity<T>(v: T) -> T => v@move\nfunc one(v: i64) -> i32 => 0\nfunc fo(v: i64) -> i32 => 0\nfunc fo(v: bool) -> i32 => 1\nfunc ft(v: (i64, i32)) -> i32 => 0\nfunc ft(v: (bool, i32)) -> i32 => 1\nfunc inspect<T>(value: ref/T) -> i32 => 0\nfunc g<T>(x: T) -> i32 => 0\nfunc h2<T>(a: T, b: T) -> i32 => 0\nfunc k(c: bool, d: bool, w: ref/i64, x: i64, s: i64) -> i32\n";

    private const string FlowSources = "func identity<T>(value: T) -> T => value@move\nfunc keep<T>(value: T) -> T => value@move\nfunc stop() -> Never => $abort(\"stop\")\nfunc first<T>(value: (T, i64)) -> () => ()\nfunc takeT(value: (i64, i64)) -> () => ()\nfunc applyArg<F>(value: (F, i64)) -> ()\n    F is Callable<(i64) -> i64>\n    return ()\nfunc k(c: bool, d: bool, x: i64) -> i32\n";

    private const string LengthSources = "func zeros<length N>() -> [N of i64] => [N of 0]\nfunc takeN<length M>(x: [M of i64]) -> i32 => 0\nfunc g<T>(x: T) -> i32 => 0\nfunc consume<T>(x: Array<T>) -> i32 => 0\nfunc consume<length N, T>(x: [N of T]) -> i32 => 1\nfunc k(c: bool, x: i64) -> i32\n";

    private const string FieldSource = "struct P\n    public var x: i64 = 5\nfunc keep<T>(v: T) -> T => v@move\nfunc k(c: bool, p: P) -> i32\n";

    private const string NestedCalls = "func inner(x: i32) -> i32 => x\nfunc inner(x: i64) -> i64 => x\nfunc g<T>(v: T) -> i32 => 0\nfunc h2<T>(a: T, b: T) -> i32 => 0\nfunc k() -> i32\n";

    private const string LiteralAtReference = "func see(target: ref/i32) -> i32 => 0\nfunc mk() -> i32 => 1\nfunc k() -> i32\n";

    private const string FunctionContracts = "func kg(c: bool, y: i32, s: ref/i32 during j, t: ref/i32 during k, fa: (ref/i32 during j) -> ref/i32 during j, fb: (ref/i32 during k) -> ref/i32 during k) -> i32\n";

    private const string HeldDeclarations = "func identity<T>(v: T) -> T => v@move\nfunc f(x: i64) -> i32 => 0\nfunc ft(x: (i64, i32)) -> i32 => 0\nfunc fa(x: Array<i64>) -> i32 => 0\nfunc fd(x: Dictionary<string, i64>) -> i32 => 0\nfunc k(c: bool, d: bool) -> i32\n";

    // A row is (unit, form, program, records). Its verdict comes from SPEC, never from a build: records is empty for a valid program, which
    // also generates, and otherwise lists each expected Error as `Code: source line` (see CompilationTestHelper.AssertRecordsAtLines). unit
    // guard: the row runs today and must keep passing. Any other unit is the plan's unit that makes the row pass: the row is Explicit
    // until that unit lands and removes the marker. `dotnet xUnitTest.dll -class <class> -explicit only` runs those.
    [Theory]
    // SPEC 14.9.2 step 2 (revision B's B1; the sentence is on dev): sources that differ only in safe reference layers over one Type, or are literals of
    // it, have that common Type and are value-read, in a `let`, as a call argument, nested and in a `match`. Their twins misfit (q1n, q1fl).
    [InlineData("guard", "p41", Sources + SourcesBody + "    let t = identity(if c => u@ref else => 1)\n    return r\n", "")]
    [InlineData("guard", "q1a", Sources + SourcesBody + "    let t = identity(if c => 1 else => u@ref)\n    return r\n", "")]
    [InlineData("guard", "q1k", Sources + SourcesBody + "    let t = identity(match d\n        true => u@ref\n        false => 1\n    )\n    return r\n", "")]
    [InlineData("guard", "q1b", Sources + SourcesBody + "    r = g(if c => u@ref else => 1)\n    return r\n", "")]
    [InlineData("guard", "q1c", Sources + SourcesBody + "    r = h2(if c => u@ref else => 1, y)\n    return r\n", "")]
    [InlineData("guard", "q1d", Sources + SourcesBody + "    let j = if c => u@ref else => 1\n    return r\n", "")]
    [InlineData("guard", "q1h", Sources + SourcesBody + "    let t = identity(if c => (if d => u@ref else => 1) else => y)\n    return r\n", "")]
    [InlineData("guard", "q1f", RefSources + "    return one(if c => w else => 1)\n", "")]
    [InlineData("guard", "q1e", RefSources + "    return fo(if c => w else => 1)\n", "")]
    [InlineData("guard", "q1i", RefSources + "    return inspect(if c => w else => 1)\n", "")]
    [InlineData("guard", "t01", RefSources + "    return one(if c => w else => identity(w))\n", "")]
    [InlineData("guard", "t03", RefSources + "    return one(if c => w else => x)\n", "")]
    [InlineData("guard", "t04", RefSources + "    return fo(if c => w else => identity(w))\n", "")]
    [InlineData("guard", "r_one", RefSources + "    return one(if c => w else => w)\n", "")]
    [InlineData("guard", "q1n", RefSources + "    return one(if c => w else => true)\n", "*: return one(if c => w else => true) | ...")]
    [InlineData("guard", "q1fl", RefSources + "    let j = if c => w else => 2.5\n    return 0\n", "*: let j = if c => w else => 2.5 | ...")]

    // SPEC 14.9.2 step 2: a source holding a literal default or a waiting part supplies no candidate, so the Type is formed part by part, each part as
    // the step finds one, the sentence above included.
    [InlineData("guard", "p46", Sources + SourcesBody + "    let t = identity(if c => (u@ref, 1) else => (y, 2))\n    return r\n", "")]
    [InlineData("I2", "r18", Sources + SourcesBody + "    let j = if c => (u@ref, 1) else => (1, y)\n    return r\n", "", Explicit = true)]
    [InlineData("I2", "s07", Sources + SourcesBody + "    let t = identity(if c => (u@ref, 1) else => (1, y))\n    return r\n", "", Explicit = true)]
    [InlineData("I2", "s08", RefSources + "    return ft(if c => (w, 1) else => (1, 2))\n", "", Explicit = true)]

    // SPEC 14.9.2 step 2, 4.3.1 (revision B's B2-B4): an array literal source with every element typed is Array<T> and compared whole, so Array<ref/i32>
    // beside Array<i32> has no common Type and the argument waits; elements of one literal agree (4.3.1).
    [InlineData("guard", "q3b", Sources + SourcesBody + "    r = g(if c => [x] else => identity([x]))\n    return r\n", "")]
    [InlineData("guard", "p3f", LengthSources + "    return g(if c => [1@i64, 2@i64] else => zeros())\n", "*: return g(if c => [1@i64, 2@i64] else => zeros()) | ...")]
    [InlineData("guard", "q2a", Sources + SourcesBody + "    let j = if c => [u@ref] else => [y]\n    return r\n", "*: let j = if c => [u@ref] else => [y] | ...")]
    [InlineData("guard", "r02", Sources + SourcesBody + "    let j = [u@ref, y]\n    return r\n", "*: let j = [u@ref, y] | ...")]
    [InlineData("guard", "r01", Sources + SourcesBody + "    r = ha([u@ref, y])\n    return r\n", "*: r = ha([u@ref, y]) | ...")]
    [InlineData("guard", "p43", Sources + SourcesBody + "    let t = identity([u@ref, y])\n    return r\n", "*: let t = identity([u@ref, y]) | ...")]
    [InlineData("guard", "q2d", Sources + SourcesBody + "    r = ha(if c => [u@ref, y] else => [y, u])\n    return r\n", "*: r = ha(if c => [u@ref, y] else => [y, u]) | ...")]
    [InlineData("guard", "q2e", Sources + SourcesBody + "    r = ha(if c => [u@ref, y] else => [y, y])\n    return r\n", "*: r = ha(if c => [u@ref, y] else => [y, y]) | ...")]
    [InlineData("guard", "r09", Sources + SourcesBody + "    r = ha(if c => [u@ref, u@ref] else => [y, y])\n    return r\n", "*: r = ha(if c => [u@ref, u@ref] else => [y, y]) | ...")]
    [InlineData("guard", "q2f", Sources + SourcesBody + "    r = ga(if c => [u@ref] else => [y])\n    return r\n", "*: r = ga(if c => [u@ref] else => [y]) | ...")]
    [InlineData("guard", "p48", Sources + SourcesBody + "    let t = identity(if c => [u@ref] else => [y])\n    return r\n", "*: let t = identity(if c => [u@ref] else => [y]) | ...")]
    [InlineData("guard", "r15", Sources + SourcesBody + "    r = ha(if c => [u@ref, identity(1)] else => [y, 2])\n    return r\n", "*: r = ha(if c => [u@ref, identity(1)] else => [y, 2]) | ...")]

    // SPEC 14.9.2 step 2: arrays, Dictionaries, fills and selections formed part by part. w10: at a bare slot a waiting element leaves T unbound (SPEC
    // 10.5.1).
    [InlineData("guard", "w13", Sources + SourcesBody + "    let j = if c => [(u@ref, 1)] else => [(y, 2)]\n    return r\n", "")]
    [InlineData("guard", "w13a", Sources + SourcesBody + "    let t = identity(if c => [(u@ref, 1)] else => [(y, 2)])\n    return r\n", "")]
    [InlineData("guard", "a01", Sources + SourcesBody + "    let j = if c => [(u@ref, 1), (y, 2)] else => [(y, 3)]\n    return r\n", "")]
    [InlineData("I2", "x10", Sources + SourcesBody + "    let j = if c => [(u@ref, 1)] else => [(1, 2)]\n    return r\n", "", Explicit = true)]
    [InlineData("I2", "x10a", Sources + SourcesBody + "    let t = identity(if c => [(u@ref, 1)] else => [(1, 2)])\n    return r\n", "", Explicit = true)]
    [InlineData("guard", "a13", Sources + SourcesBody + "    let j = if c => [u@ref: 1] else => [y: 2]\n    return r\n", "")]
    [InlineData("guard", "a11", Sources + SourcesBody + "    let t = identity(if c => ([2 of (u@ref, 1)], 3) else => ([2 of (y, 2)], 4))\n    return r\n", "")]
    [InlineData("guard", "c12", Sources + SourcesBody + "    let t = identity(if c => (if d => [(u@ref, 1)] else => [(y, 2)]) else => [(y, 3)])\n    return r\n", "")]
    [InlineData("guard", "c13", Sources + SourcesBody + "    let j = if c => (if d => [(u@ref, 1)] else => [(y, 2)]) else => [(y, 3)]\n    return r\n", "")]
    [InlineData("guard", "c14", Sources + SourcesBody + "    let t = identity((if c => [(u@ref, 1)] else => [(y, 2)]))\n    return r\n", "")]
    [InlineData("guard", "c15", Sources + SourcesBody + "    let t = identity(match d\n        true => [(u@ref, 1)]\n        false => [(y, 2)]\n    )\n    return r\n", "")]
    [InlineData("guard", "w10", Sources + SourcesBody + "    let t = identity(if c => [(identity(1), u@ref)] else => [(2, y)])\n    return r\n", "UnboundTypeArgument_Kd: let t = identity(if c => [(identity(1), u@ref)] else => [(2, y)])")]
    [InlineData("I2", "w11", Sources + SourcesBody + "    let j = if c => [(identity(1), u@ref)] else => [(2, y)]\n    return r\n", "", Explicit = true)]
    [InlineData("I2", "w12", Sources + SourcesBody + "    r = g(if c => [identity(1), u@ref] else => [2, y])\n    return r\n", "", Explicit = true)]
    [InlineData("guard", "w13w", Sources + SourcesBody + "    r = g(if c => [identity(y)] else => [u@ref])\n    return r\n", "*: r = g(if c => [identity(y)] else => [u@ref]) | ...")]
    [InlineData("guard", "w14", Sources + SourcesBody + "    r = g(if c => [2 of y] else => [y, y])\n    return r\n", "*: r = g(if c => [2 of y] else => [y, y]) | ...")]
    [InlineData("guard", "w08a", Sources + SourcesBody + "    let t = identity(if c => [2 of u@ref] else => [2 of y])\n    return r\n", "*: let t = identity(if c => [2 of u@ref] else => [2 of y]) | ...")]
    [InlineData("I2", "w09a", Sources + SourcesBody + "    let t = identity(if c => [2 of u@ref] else => [2 of 1])\n    return r\n", "", Explicit = true)]
    [InlineData("guard", "w18a", Sources + SourcesBody + "    let t = identity(if c => ([2 of u@ref], 1) else => ([2 of y], 2))\n    return r\n", "*: let t = identity(if c => ([2 of u@ref], 1) else => ([2 of y], 2)) | ...")]
    [InlineData("guard", "a05", Sources + SourcesBody + "    let t = identity(if c => [y, 1] else => [u@ref])\n    return r\n", "*: let t = identity(if c => [y, 1] else => [u@ref]) | ...")]
    [InlineData("guard", "a05m", Sources + SourcesBody + "    let t = identity(if c => [u@ref] else => [y, 1])\n    return r\n", "*: let t = identity(if c => [u@ref] else => [y, 1]) | ...")]

    // SPEC 10.8: evidence for one slot agrees across arguments, elements and joins in every order; o18's two positions are two parts and need not agree.
    [InlineData("guard", "o01", Sources + SourcesBody + "    r = h2([y], [u@ref])\n    return r\n", "*: r = h2([y], [u@ref]) | ...")]
    [InlineData("guard", "o02", Sources + SourcesBody + "    r = h2([u@ref], [y])\n    return r\n", "*: r = h2([u@ref], [y]) | ...")]
    [InlineData("guard", "o03", Sources + SourcesBody + "    r = p2(([y], [u@ref]))\n    return r\n", "*: r = p2(([y], [u@ref])) | ...")]
    [InlineData("guard", "o04", Sources + SourcesBody + "    r = p2(([u@ref], [y]))\n    return r\n", "*: r = p2(([u@ref], [y])) | ...")]
    [InlineData("guard", "o05", Sources + SourcesBody + "    r = g(if c => [y] else => [u@ref])\n    return r\n", "*: r = g(if c => [y] else => [u@ref]) | ...")]
    [InlineData("guard", "o06", Sources + SourcesBody + "    r = g(if c => [u@ref] else => [y])\n    return r\n", "*: r = g(if c => [u@ref] else => [y]) | ...")]
    [InlineData("guard", "o07m", Sources + SourcesBody + "    r = h2(xs@move, if c => [u@ref] else => [u@ref])\n    return r\n", "*: r = h2(xs@move, if c => [u@ref] else => [u@ref]) | ...")]
    [InlineData("guard", "o09m", Sources + SourcesBody + "    r = g(if c => xs@move else => [u@ref])\n    return r\n", "*: r = g(if c => xs@move else => [u@ref]) | ...")]
    [InlineData("guard", "o10m", Sources + SourcesBody + "    r = g(if c => [u@ref] else => xs@move)\n    return r\n", "*: r = g(if c => [u@ref] else => xs@move) | ...")]
    [InlineData("guard", "o11m", Sources + SourcesBody + "    let j = if c => xs@move else => [u@ref]\n    return r\n", "*: let j = if c => xs@move else => [u@ref] | ...")]
    [InlineData("guard", "o12", Sources + SourcesBody + "    r = p2(if c => ([y], [u@ref]) else => ([y], [y]))\n    return r\n", "*: r = p2(if c => ([y], [u@ref]) else => ([y], [y])) | ...")]
    [InlineData("guard", "o16", Sources + SourcesBody + "    r = g(if c => ([y], 1) else => ([u@ref], 2))\n    return r\n", "*: r = g(if c => ([y], 1) else => ([u@ref], 2)) | ...")]
    [InlineData("I2", "o18", Sources + SourcesBody + "    let t = identity(([y], [u@ref]))\n    return r\n", "", Explicit = true)]
    [InlineData("guard", "o19", Sources + SourcesBody + "    let j = if c => ([y], [u@ref]) else => ([y], [y])\n    return r\n", "*: let j = if c => ([y], [u@ref]) else => ([y], [y]) | ...")]
    [InlineData("guard", "c16", Sources + SourcesBody + "    let t = identity(match d\n        true => [u@ref]\n        false => [y]\n    )\n    return r\n", "*: * | ...")]
    [InlineData("guard", "c17", Sources + SourcesBody + "    let j = match d\n        true => [u@ref]\n        false => [y]\n    return r\n", "*: * | ...")]
    [InlineData("guard", "d10", Sources + SourcesBody + "    r = g(if c => [u@ref: y] else => [y: y])\n    return r\n", "*: r = g(if c => [u@ref: y] else => [y: y]) | ...")]
    [InlineData("guard", "d11", Sources + SourcesBody + "    let j = if c => [u@ref: y] else => [y: y]\n    return r\n", "*: let j = if c => [u@ref: y] else => [y: y] | ...")]
    [InlineData("guard", "d12", Sources + SourcesBody + "    r = g(if c => [y: y] else => [u@ref: y])\n    return r\n", "*: r = g(if c => [y: y] else => [u@ref: y]) | ...")]
    [InlineData("guard", "d13", Sources + SourcesBody + "    r = fd(if c => [1: x] else => [2: b])\n    return r\n", "*: r = fd(if c => [1: x] else => [2: b]) | ...")]

    // SPEC 14.9.2 step 1, 10.2: a fixed expectation, an explicit Type argument's or a parameter's, fits each source, a reference at a value element
    // position value-read. A source conflict only makes the argument wait for the selected candidate (SPEC 10.5.1).
    [InlineData("guard", "e10", Sources + SourcesBody + "    r = g<Array<i32>>([u@ref])\n    return r\n", "")]
    [InlineData("I2", "e11", Sources + SourcesBody + "    r = g<Array<i32>>(if c => [y] else => [u@ref])\n    return r\n", "", Explicit = true)]
    [InlineData("I2", "e12", Sources + SourcesBody + "    r = ga<i32>([u@ref])\n    return r\n", "", Explicit = true)]
    [InlineData("I2", "e13", Sources + SourcesBody + "    r = ga<i32>(if c => [u@ref] else => [y])\n    return r\n", "", Explicit = true)]
    [InlineData("guard", "e14", Sources + SourcesBody + "    r = fai([u@ref])\n    return r\n", "")]
    [InlineData("I2", "e15", Sources + SourcesBody + "    r = fai(if c => [u@ref] else => [y])\n    return r\n", "", Explicit = true)]
    [InlineData("guard", "e16", Sources + SourcesBody + "    let a: Array<i32> = [u@ref]\n    return r\n", "")]
    [InlineData("guard", "e17", Sources + SourcesBody + "    let a: Array<i32> = if c => [u@ref] else => [y]\n    return r\n", "")]
    [InlineData("guard", "e18", Sources + SourcesBody + "    r = h2<Array<i32>>([y], [u@ref])\n    return r\n", "")]

    // SPEC 14.9.2 step 2, 10.8: Never supplies no candidate, nor does a literal or fill holding it, also inside a part; a part Never in every source is
    // Never, and Never still fits its position.
    [InlineData("guard", "n10", Sources + SourcesBody + "    let t = identity(if c => (u@ref, (return 1)) else => (y, 2))\n    return r\n", "")]
    [InlineData("guard", "n11", Sources + SourcesBody + "    let j = if c => (u@ref, (return 1)) else => (y, 2)\n    return r\n", "")]
    [InlineData("guard", "n12", Sources + SourcesBody + "    let t = identity(if c => (u@ref, (return 1)) else => (y, y))\n    return r\n", "")]
    [InlineData("guard", "n13", Sources + SourcesBody + "    let j = if c => (u@ref, (return 1)) else => (y, y)\n    return r\n", "")]
    [InlineData("guard", "n14", Sources + SourcesBody + "    let t = identity(if c => [(u@ref, fail())] else => [(y, 2)])\n    return r\n", "")]
    [InlineData("guard", "n15", Sources + SourcesBody + "    let t = identity(if c => [2 of (u@ref, (return 1))] else => [2 of (y, 2)])\n    return r\n", "")]
    [InlineData("guard", "n16", Sources + SourcesBody + "    let j = if c => [2 of (u@ref, (return 1))] else => [2 of (y, 2)]\n    return r\n", "")]
    [InlineData("guard", "n17", Sources + SourcesBody + "    let j = if c => [2 of (y, (return 3))] else => [3 of ((return 4), y)]\n    return r\n", "*: let j = if c => [2 of (y, (return 3))] else => [3 of ((return 4), y)] | ...")]
    // n18, n23: the formation's verdict is acceptance; A1 reports the call's receipt of a value whose Never part no path placed (PLAN
    // G442, SPEC 14.10.3, 15.1.3; the owner's O-99 (a)).
    [InlineData("guard", "n18", FlowSources + "    first(if c => (x, stop()) else => stop())\n    return 0\n", "UninitializedPlace_Kd: first(if c => (x, stop()) else => stop())")]
    [InlineData("guard", "n19", FlowSources + "    let j = if c => (if d => (x, (return 1)) else => (return 2)) else => (x, 2)\n    return 0\n", "")]
    [InlineData("guard", "n20", FlowSources + "    let j = if c => [\"k\": (x, (return 1))] else => [\"k\": ((return 2), x)]\n    return 0\n", "")]
    [InlineData("guard", "n21", FlowSources + "    keep((x, stop()))\n    return 0\n", "UnboundTypeArgument_Kd: keep((x, stop()))")]
    [InlineData("guard", "n22", FlowSources + "    keep([x, stop()])\n    return 0\n", "UnboundTypeArgument_Kd: keep([x, stop()])")]
    [InlineData("guard", "n23", FlowSources + "    takeT((x, stop()))\n    return 0\n", "UninitializedPlace_Kd: takeT((x, stop()))")]

    // SPEC 4.4.1, 10.5.1, 10.8: lengths at fixed-array parameters. Revision B's text is not on dev: rows here are the forms both texts judge alike.
    [InlineData("guard", "u1c", LengthSources + "    return takeN(if c => [1, 2] else => [1, 2, 3])\n", "*: return takeN(if c => [1, 2] else => [1, 2, 3]) | ...")]
    [InlineData("guard", "u1t", LengthSources + "    return takeN(if c => [1, 2] else => [3, 4])\n", "")]
    [InlineData("I2", "u2n", LengthSources + "    return takeN(if c => zeros() else => zeros())\n", "UnboundTypeArgument_Kd: return takeN(if c => zeros() else => zeros())", Explicit = true)]
    [InlineData("I2", "mjf", LengthSources + "    return takeN(if c => zeros() else => [2 of 0@i64])\n", "", Explicit = true)]
    [InlineData("guard", "z8", LengthSources + "    let j = if c => zeros() else => [1@i64, 2@i64]\n    return 0\n", "*: let j = if c => zeros() else => [1@i64, 2@i64] | ...")]
    [InlineData("guard", "u4e", LengthSources + "    return g([])\n", "*: return g([]) | ...")]
    [InlineData("guard", "c2j", LengthSources + "    return consume(if c => [1, 2] else => [3, 4])\n", "AmbiguousBinding_Kd: return consume(if c => [1, 2] else => [3, 4])")]

    // SPEC 14.9.2 step 2 (PLAN G443): sources are compared by their structural part only; the common Type's Origins are local regions and each fit an
    // ordinary Origin relation, never a mismatch.
    [InlineData("I2", "q20", Sources + SourcesBody + "    r = g(if c => [u@ref] else => [v@ref])\n    return r\n", "", Explicit = true)]
    [InlineData("I2", "q21", Sources + SourcesBody + "    let t = identity(if c => [u@ref] else => [v@ref])\n    return r\n", "", Explicit = true)]
    [InlineData("I2", "q22", Sources + SourcesBody + "    let j = if c => [u@ref] else => [v@ref]\n    return r\n", "", Explicit = true)]
    [InlineData("I2", "q23", Sources + SourcesBody + "    r = g(if c => [(u@ref, y)] else => [(v@ref, y)])\n    return r\n", "", Explicit = true)]
    [InlineData("guard", "q25", Sources + SourcesBody + "    r = g(if c => [u@ref, v@ref] else => [v@ref])\n    return r\n", "")]
    [InlineData("I2", "q26", Sources + SourcesBody + "    r = ga(if c => [u@ref] else => [v@ref])\n    return r\n", "", Explicit = true)]
    [InlineData("I2", "q27", Sources + SourcesBody + "    r = g(if c => [2 of u@ref] else => [2 of v@ref])\n    return r\n", "", Explicit = true)]
    [InlineData("I2", "q28", Sources + SourcesBody + "    r = g(if c => (u@ref, y) else => (v@ref, y))\n    return r\n", "", Explicit = true)]
    [InlineData("I2", "q29", Sources + SourcesBody + "    r = g(if c => [y: u@ref] else => [y: v@ref])\n    return r\n", "", Explicit = true)]
    [InlineData("guard", "q30", Sources + SourcesBody + "    r = g(if c => [u@ref] else => [u@ref])\n    return r\n", "")]
    [InlineData("I2", "o20", Sources + SourcesBody + "    r = g(if c => ([u@ref], 1) else => ([v@ref], 2))\n    return r\n", "", Explicit = true)]

    // SPEC 14.9.2 step 2, 10.5.1 (PLAN G159 (2)): `identity(5)` waits, so its Tuple supplies no candidate; the Type is formed by parts and fitted by
    // position.
    [InlineData("guard", "g159a", FlowSources + "    first(if c => (x, identity(5)) else => (stop(), x))\n    return 0\n", "")]
    [InlineData("guard", "g159b", FlowSources + "    applyArg(if c => (func [] (v) => v, identity(5)) else => (stop(), x))\n    return 0\n", "")]
    [InlineData("guard", "g159an", FlowSources + "    first(if c => (x, identity(5)) else => (stop(), true))\n    return 0\n", "*: first(if c => (x, identity(5)) else => (stop(), true)) | ...")]
    [InlineData("guard", "g159bn", FlowSources + "    applyArg(if c => (func [] (v) => v, identity(5)) else => (stop(), true))\n    return 0\n", "*: applyArg(if c => (func [] (v) => v, identity(5)) else => (stop(), true)) | ...")]

    // SPEC 14.9.2 step 2, 10.5.1 (PLAN G152): a typed source beside a literal is independently typable. x13o (PLAN G540): a Case takes the typed
    // source's Type (step 3).
    [InlineData("guard", "g152a", FieldSource + "    let t = keep(if c => p.x else => 2)\n    return 0\n", "")]
    [InlineData("guard", "g152b", FieldSource + "    let t: i64 = keep(if c => p.x else => 2)\n    return 0\n", "")]
    [InlineData("guard", "g152c", FieldSource + "    let t = keep(if c => 2 else => p.x)\n    return 0\n", "")]
    [InlineData("guard", "g152m", FieldSource + "    let t = keep(match c\n        true => p.x\n        false => 2\n    )\n    return 0\n", "")]
    [InlineData("guard", "g152n", FieldSource + "    let t: bool = keep(if c => p.x else => 2)\n    return 0\n", "*: let t: bool = keep(if c => p.x else => 2) | ...")]
    [InlineData("guard", "g152f", FieldSource + "    let t = keep(if c => p.x else => 2.5)\n    return 0\n", "*: let t = keep(if c => p.x else => 2.5) | ...")]
    [InlineData("I2", "x13o", "func k(c: bool, w: ref/i32) -> i32\n    let j = if c => Option.Some(w) else => Option.None\n    return 0\n", "", Explicit = true)]

    // SPEC 10.5.1, 10.8: a nested call with two candidates waits, is no evidence, and is checked at the Type another argument binds.
    [InlineData("guard", "na1", NestedCalls + "    return g(inner(1))\n", "*: return g(inner(1)) | ...")]
    [InlineData("guard", "na2", NestedCalls + "    return h2(inner(1), 5@i64)\n", "")]
    [InlineData("guard", "na3", NestedCalls + "    return h2(5@i64, inner(1))\n", "")]

    // SPEC 10.2 row 2, 10.2.2: an owner temporary at `ref/U` is materialized once and shared-borrowed, a literal's default included, at a call
    // argument (lr1) and an aggregate element (lr2); w09a above is the same fit at a fill the join's other source types.
    [InlineData("guard", "lr1", LiteralAtReference + "    return see(1)\n", "")]
    [InlineData("guard", "lr2", LiteralAtReference + "    let a: [2 of ref/i32] = [2 of mk()]\n    return 0\n", "")]

    // SPEC 14.9.2, 10.5.1 (PLAN G159): at a fixed parameter, Tuple sources differing in an element's reference layer are valid under either reading
    // of step 2: value-read to the common Type, or no common Type, so the argument waits and each source fits the selected Type. Without a fixed
    // Type the reading is open, so `g(...)` and `let j = ...` have no row here.
    [InlineData("I2", "g159t", FlowSources + "    let y: i64 = 1\n    takeT(if c => (y, x@ref) else => (y, x))\n    return 0\n", "", Explicit = true)]
    [InlineData("I2", "g159tm", FlowSources + "    let y: i64 = 1\n    takeT(if c => (y, x) else => (y, x@ref))\n    return 0\n", "", Explicit = true)]
    [InlineData("guard", "g159ta", FlowSources + "    let y: i64 = 1\n    let t: (i64, i64) = if c => (y, x@ref) else => (y, x)\n    return 0\n", "")]
    [InlineData("guard", "g159tn", FlowSources + "    let y: i64 = 1\n    takeT(if c => (y, x@ref) else => (y, true))\n    return 0\n", "*: takeT(if c => (y, x@ref) else => (y, true)) | ...")]

    // SPEC 14.9.2 steps 2-3, 10.2 row 1 (PLAN G394): a source holding a literal default takes the typed source's Type, and a Place at a reference
    // element is a new shared borrow, as in the plain form g394p.
    [InlineData("I2", "g394", Sources + SourcesBody + "    let j = if c => [(u@ref, y)] else => [(y, 2)]\n    return r\n", "", Explicit = true)]
    [InlineData("I2", "g394m", Sources + SourcesBody + "    let j = if c => [(y, 2)] else => [(u@ref, y)]\n    return r\n", "", Explicit = true)]
    [InlineData("I2", "g394a", Sources + SourcesBody + "    let t = identity(if c => [(u@ref, y)] else => [(y, 2)])\n    return r\n", "", Explicit = true)]
    [InlineData("guard", "g394p", Sources + SourcesBody + "    let a: (ref/i32, i32) = (y, 2)\n    return r\n", "")]

    // SPEC 14.9.2 step 1, 10.2: the e rows' invalid twins: a value read reaches i32, not the fixed i64.
    [InlineData("guard", "e11n", Sources + SourcesBody + "    r = g<Array<i64>>(if c => [y] else => [u@ref])\n    return r\n", "*: r = g<Array<i64>>(if c => [y] else => [u@ref]) | ...")]
    [InlineData("guard", "e12n", Sources + SourcesBody + "    r = ga<i64>([u@ref])\n    return r\n", "*: r = ga<i64>([u@ref]) | ...")]
    [InlineData("guard", "e13n", Sources + SourcesBody + "    r = ga<i64>(if c => [u@ref] else => [y])\n    return r\n", "*: r = ga<i64>(if c => [u@ref] else => [y]) | ...")]
    [InlineData("guard", "e17n", Sources + SourcesBody + "    let a: Array<i64> = if c => [u@ref] else => [y]\n    return r\n", "*: let a: Array<i64> = if c => [u@ref] else => [y] | ...")]

    // SPEC 14.9.2 steps 2-3 (PLAN G443): the twins of ALateSourceDifferingInOriginsAloneIsUnsupportedUntilG443's forms. Valid where no such
    // Origin is needed: one Loan (h7), a value read (h8), part by part (h9), a declared Type (h10). Structural misfits keep their record (h11 to
    // h13), as does a Function component's Origin part, a whole-contract comparison (SPEC 10.7, 15.6.1) the element's own fit judges (h14).
    [InlineData("guard", "g586h7", Sources + SourcesBody + "    let j = if c => (y, u@ref) else => (2, u@ref)\n    return r\n", "")]
    [InlineData("guard", "g586h8", Sources + SourcesBody + "    let j = if c => (y, u) else => (2, v@ref)\n    return r\n", "")]
    [InlineData("guard", "g586h9", Sources + SourcesBody + "    let j = if c => (u@ref, 1) else => (v@ref, 2)\n    return r\n", "")]
    [InlineData("guard", "g586h10", Sources + SourcesBody + "    let j: (i32, i32) = if c => (y, u@ref) else => (2, v@ref)\n    return r\n", "")]
    [InlineData("guard", "g586h11", Sources + SourcesBody + "    let j = if c => (y, u@ref) else => (2, x)\n    return r\n", "TypeMismatch_Kd: let j = if c => (y, u@ref) else => (2, x)")]
    [InlineData("guard", "g586h12", Sources + SourcesBody + "    let j = if c => (y, u@uniq) else => (2, v)\n    return r\n", "TypeMismatch_Kd: let j = if c => (y, u@uniq) else => (2, v)")]
    [InlineData("guard", "g586h13", Sources + SourcesBody + "    let j = if c => (y, u@ref) else => (2, b)\n    return r\n", "TypeMismatch_Kd: let j = if c => (y, u@ref) else => (2, b)")]
    [InlineData("guard", "g586h14", FunctionContracts + "    let w = if c => (y, fa@move) else => (2, fb@move)\n    return 0\n", "UnprovenOriginContract_Kd: let w = if c => (y, fa@move) else => (2, fb@move)")]

    // SPEC 14.9.2 steps 2-4 (PLAN G540): x13o's other order, and its invalid twin, where no source supplies a Type.
    [InlineData("I2", "x13r", "func k(c: bool, w: ref/i32) -> i32\n    let j = if c => Option.None else => Option.Some(w)\n    return 0\n", "", Explicit = true)]
    [InlineData("guard", "x13n", "func k(c: bool) -> i32\n    let j = if c => Option.None else => Option.None\n    return 0\n", "*: let j = if c => Option.None else => Option.None | ...")]
    public void ACommonTypeFormationHasItsVerdict(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // Rows as CompilationTestHelper.AssertRecordsAtLines reads them: (unit, form, program, records), the verdict from SPEC. A unit other than
    // guard is the plan's unit that makes the row pass: the row is Explicit until that unit lands and removes the marker.
    [Theory]
    // SPEC 14.9.2, 10.5.1 (PLAN G275 (2)): a held join has one verdict whatever delivers it, a label, an expression body, parentheses, or a Tuple, array
    // or Dictionary part: valid, beside a misfit (the `t` forms) and over a failed leaf (the `f` forms). h_dict itself waits for the Dictionary
    // question.
    [InlineData("guard", "h_lab", HeldDeclarations + "    return f(if c => (label l: if d => identity(1) else => identity(2)) else => 3)\n", "")]
    [InlineData("guard", "h_do", HeldDeclarations + "    return f(if c => (do => (if d => identity(1) else => identity(2))) else => 3)\n", "")]
    [InlineData("guard", "h_par", HeldDeclarations + "    return f(((if c => ((if d => identity(1) else => identity(2))) else => 3)))\n", "")]
    [InlineData("guard", "h_tup", HeldDeclarations + "    return ft(if c => ((if d => identity(1) else => identity(2)), 1) else => (3, 2))\n", "")]
    [InlineData("guard", "h_arr", HeldDeclarations + "    return fa(if c => [(if d => identity(1) else => identity(2)), 1] else => [3])\n", "")]
    [InlineData("guard", "h_labt", HeldDeclarations + "    return f(if c => (label l: if d => identity(1) else => identity(2)) else => true)\n", "*: return f(if c => (label l: if d => identity(1) else => identity(2)) else => true) | ...")]
    [InlineData("guard", "h_dot", HeldDeclarations + "    return f(if c => (do => (if d => identity(1) else => identity(2))) else => true)\n", "*: return f(if c => (do => (if d => identity(1) else => identity(2))) else => true) | ...")]
    [InlineData("guard", "h_part", HeldDeclarations + "    return f(((if c => ((if d => identity(1) else => identity(2))) else => true)))\n", "*: return f(((if c => ((if d => identity(1) else => identity(2))) else => true))) | ...")]
    [InlineData("guard", "h_tupt", HeldDeclarations + "    return ft(if c => ((if d => identity(1) else => identity(2)), 1) else => (true, 2))\n", "*: return ft(if c => ((if d => identity(1) else => identity(2)), 1) else => (true, 2)) | ...")]
    [InlineData("guard", "h_arrt", HeldDeclarations + "    return fa(if c => [(if d => identity(1) else => identity(2)), 1] else => [true])\n", "*: return fa(if c => [(if d => identity(1) else => identity(2)), 1] else => [true]) | ...")]
    [InlineData("guard", "h_dictt", HeldDeclarations + "    return fd(if c => [\"k\": (if d => identity(1) else => identity(2))] else => [\"k\": true])\n", "*: return fd(if c => [\"k\": (if d => identity(1) else => identity(2))] else => [\"k\": true]) | ...")]
    [InlineData("guard", "h_labf", HeldDeclarations + "    return f(if c => (label l: if d => identity(1) else => undefinedName) else => 3)\n", "UnresolvedBinding_Kd: return f(if c => (label l: if d => identity(1) else => undefinedName) else => 3) | ...")]
    [InlineData("guard", "h_dof", HeldDeclarations + "    return f(if c => (do => (if d => identity(1) else => undefinedName)) else => 3)\n", "UnresolvedBinding_Kd: return f(if c => (do => (if d => identity(1) else => undefinedName)) else => 3) | ...")]
    [InlineData("guard", "h_parf", HeldDeclarations + "    return f(((if c => ((if d => identity(1) else => undefinedName)) else => 3)))\n", "UnresolvedBinding_Kd: return f(((if c => ((if d => identity(1) else => undefinedName)) else => 3))) | ...")]
    [InlineData("guard", "h_tupf", HeldDeclarations + "    return ft(if c => ((if d => identity(1) else => undefinedName), 1) else => (3, 2))\n", "UnresolvedBinding_Kd: return ft(if c => ((if d => identity(1) else => undefinedName), 1) else => (3, 2)) | ...")]
    [InlineData("guard", "h_arrf", HeldDeclarations + "    return fa(if c => [(if d => identity(1) else => undefinedName), 1] else => [3])\n", "UnresolvedBinding_Kd: return fa(if c => [(if d => identity(1) else => undefinedName), 1] else => [3]) | ...")]
    [InlineData("guard", "h_dictf", HeldDeclarations + "    return fd(if c => [\"k\": (if d => identity(1) else => undefinedName)] else => [\"k\": 3])\n", "UnresolvedBinding_Kd: return fd(if c => [\"k\": (if d => identity(1) else => undefinedName)] else => [\"k\": 3]) | ...")]
    public void AHeldJoinKeepsItsVerdictThroughWhatDeliversIt(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // These rows pin a temporary record, not a SPEC verdict (DECISIONS BU-156): each program is valid (SPEC 14.9.2 step 2: a source's fit to the
    // common Type is never a Type mismatch), but the step-2 Type keeps one typed source's own Origins (PLAN G443), so a source that takes it late
    // and differs from it in Origins alone is the located Unsupported_Kd at that source, never an internal fault: another Loan (h1, its other
    // order h2, the argument h3), a new shared borrow (h4), uniq (h5), a call that defaults first (h6). Removal: G443's unit (R7-I2 d) deletes
    // the interim and moves these forms to ACommonTypeFormationHasItsVerdict with no records.
    [Theory]
    [InlineData("g586h1", Sources + SourcesBody + "    let j = if c => (y, u@ref) else => (2, v@ref)\n    return r\n", "Unsupported_Kd: let j = if c => (y, u@ref) else => (2, v@ref)")]
    [InlineData("g586h2", Sources + SourcesBody + "    let j = if c => (2, v@ref) else => (y, u@ref)\n    return r\n", "Unsupported_Kd: let j = if c => (2, v@ref) else => (y, u@ref)")]
    [InlineData("g586h3", Sources + SourcesBody + "    r = g(if c => (y, u@ref) else => (2, v@ref))\n    return r\n", "Unsupported_Kd: r = g(if c => (y, u@ref) else => (2, v@ref))")]
    [InlineData("g586h4", Sources + SourcesBody + "    let j = if c => (y, u@ref) else => (2, y)\n    return r\n", "Unsupported_Kd: let j = if c => (y, u@ref) else => (2, y)")]
    [InlineData("g586h5", Sources + SourcesBody + "    let j = if c => (y, u@uniq) else => (2, v@uniq)\n    return r\n", "Unsupported_Kd: let j = if c => (y, u@uniq) else => (2, v@uniq)")]
    [InlineData("g586h6", Sources + SourcesBody + "    let j = if c => (identity(1), u@ref) else => (2, y)\n    return r\n", "Unsupported_Kd: let j = if c => (identity(1), u@ref) else => (2, y)")]
    public void ALateSourceDifferingInOriginsAloneIsUnsupportedUntilG443(string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines(form, program + "()", records);
}
