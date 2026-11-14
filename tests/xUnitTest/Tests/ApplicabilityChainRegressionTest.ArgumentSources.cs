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

    private const string FixedLengths = "func take2(x: [2 of i64]) -> i32 => 0\nfunc dec<length N>() -> [(N - 1) of i64] => [(N - 1) of 0]\n";

    private const string FieldSource = "struct P\n    public var x: i64 = 5\nfunc keep<T>(v: T) -> T => v@move\nfunc k(c: bool, p: P) -> i32\n";

    private const string NestedCalls = "func inner(x: i32) -> i32 => x\nfunc inner(x: i64) -> i64 => x\nfunc g<T>(v: T) -> i32 => 0\nfunc h2<T>(a: T, b: T) -> i32 => 0\nfunc k() -> i32\n";

    private const string LiteralAtReference = "func see(target: ref/i32) -> i32 => 0\nfunc mk() -> i32 => 1\nfunc k() -> i32\n";

    private const string ElementRanks = "func fq(p: (i32, i32)) -> bool => true\nfunc fq(p: (ref/i32 during a, i32)) -> i32 => 0\nfunc fg(p: (i32, i32)) -> bool => true\nfunc fg<T>(p: (T, i32)) -> i32 => 0\nfunc fb(p: (i32, i32)) -> i32 => 0\nfunc fb(p: (i32, i64)) -> i32 => 1\nfunc fo(p: (ref/i32 during a, i32)) -> i32 => 0\nfunc fo(p: (i32, ref/i32 during a)) -> bool => true\nfunc fn2(p: ((i32, i32), i32)) -> bool => true\nfunc fn2(p: ((ref/i32 during a, i32), i32)) -> i32 => 0\nfunc fa(x: Array<i32>) -> bool => true\nfunc fa(x: Array<ref/i32 during a>) -> i32 => 0\nfunc ff(x: [2 of i32]) -> bool => true\nfunc ff(x: [2 of ref/i32 during a]) -> i32 => 0\nfunc cc(x: Array<i32>) -> i32 => 0\nfunc cc<length N>(x: [N of i32]) -> i32 => 1\nfunc fk(! p: (i32, i32), x: i32) -> bool => true\nfunc fk(! p: (ref/i32 during a, i32), x: i32) -> i32 => 0\nfunc fe(p: (i32, i32)) -> bool => true\nfunc fe(p: (ref/i32 during a, i32), w: i32 = 0) -> i32 => 0\nfunc cd(x: Array<i32>) -> bool => true\nfunc cd<length N>(x: [N of ref/i32 during a]) -> i32 => 0\nfunc sel(p: (i32, i32)) -> i32 => 0\nfunc sel(p: (ref/i32 during a, i32)) -> i32 => 1\nfunc fg2<T>(p: (T, i32)) -> i32 => 0\nfunc fg2<T>(p: (ref/T during a, i32)) -> bool => true\nfunc inc(x: i32) -> i32 => x\nfunc fy(p: (ref/i32 during a, (i32) -> i32)) -> i32 => 0\nfunc fy<F>(p: (i32, F)) -> bool => true\nstruct S\n    public var k: i32\n    public init(k: i32) => self.k = k\n    public func m(self: ref/Self, p: (i32, i32)) -> bool => true\n    public func m(self: ref/Self, p: (ref/i32 during a, i32)) -> i32 => 0\nstruct P\n    public var k: i32\n    public init(p: (i32, i32)) => self.k = 0\n    public init(p: (ref/i32 during a, i32)) => self.k = 1\nfunc k(c: bool, n: ref/i32, m: ref/i32, v: i32) -> i32\n";

    private const string NestedRanks = "func fn3(p: ((i32, i32), i32)) -> bool => true\nfunc fn3(p: (ref/(ref/i32 during a, i32) during a, i32)) -> i32 => 0\nfunc fn4(p: ((i32, i32), i32)) -> bool => true\nfunc fn4(p: (ref/(i32, i32) during a, i32)) -> i32 => 0\nfunc fn5(p: (ref/(ref/i32 during a, i32) during a, i32)) -> i32 => 0\nfunc fa1(p: (ref/(i32, i32) during a, i32)) -> i32 => 0\nfunc fg1<T>(p: (ref/(T, i32) during a, i32)) -> i32 => 0\nfunc fc1(p: (ref/[2 of i32] during a, i32)) -> i32 => 0\nfunc fg(p: ([2 of ref/i32 during a], i32)) -> bool => true\nfunc fg(p: (ref/[2 of ref/i32 during a] during a, i32)) -> i32 => 0\nfunc fh(p: ([2 of i32], i32)) -> bool => true\nfunc fh(p: (ref/[2 of ref/i32 during a] during a, i32)) -> i32 => 0\nfunc fk(p: (((i32, i32), i32), i32)) -> bool => true\nfunc fk(p: ((ref/(ref/i32 during a, i32) during a, i32), i32)) -> i32 => 0\nfunc fl(p: (ref/[2 of i32] during a, i32)) -> bool => true\nfunc fl(p: (ref/[2 of ref/i32 during a] during a, i32)) -> i32 => 0\nfunc fu1(p: (uniq/(i32, i32) during a, i32)) -> i32 => 0\nfunc fu2(p: ((i32, i32), i32)) -> bool => true\nfunc fu2(p: (uniq/(i32, i32) during a, i32)) -> i32 => 0\nfunc k(c: bool, n: ref/i32, m: ref/i32, v: i32) -> i32\n";

    private const string NestedTwins = "func fv(p: (ref/(i32, i32) during a, i32)) -> i32 => p.1\nfunc fw(p: (ref/(ref/i32 during a, i32) during a, i32)) -> i32 => p.1\nfunc fw2(p: (ref/(ref/i32 during a, i32) during a, i32), w: i32) -> i32 => p.1\nstruct S2\n    public var k: i32\n    public init(k: i32) => self.k = k\n    public func m(self: ref/Self, p: ((i32, i32), i32)) -> bool => true\n    public func m(self: ref/Self, p: (ref/(ref/i32 during a, i32) during a, i32)) -> i32 => 0\nstruct P3\n    public var k: i32\n    public init(p: ((i32, i32), i32)) => self.k = 0\n    public init(p: (ref/(ref/i32 during a, i32) during a, i32)) => self.k = 1\nfunc fgy<T>(p: (ref/(T, i32) during a, i32)) -> i32 => 0\nfunc fgy(p: ((i32, i32), i32)) -> bool => true\nfunc fa4(x: Array<ref/[2 of ref/i32 during a] during a>) -> i32 => 0\nfunc fa4(x: Array<[2 of i32]>) -> bool => true\nfunc fgx<T>(p: (ref/T during a, i32)) -> i32 => 0\nfunc fgx(p: ((i32, i32), i32)) -> bool => true\nfunc fa3(x: Array<ref/[2 of i32] during a>) -> i32 => 0\nfunc fa3(x: Array<[2 of i32]>) -> bool => true\nfunc fl3(p: (ref/[2 of ref/i32 during a] during a, i32)) -> i32 => 0\nfunc fl3(p: ([2 of i32], i32)) -> bool => true\nfunc fgu<T>(p: (uniq/T during a, i32)) -> i32 => 0\nfunc k(c: bool, n: ref/i32, m: ref/i32, v: i32) -> i32\n";

    private const string FixedSlots = "struct Node\n    public var weight: i32\n    public init(weight: i32) => self.weight = weight\nstruct Tool\n    public var id: i32\n    public init(id: i32) => self.id = id\n    public func pick<U>(self: ref/Self, v: U) -> U => v@move\nfunc identity<U>(value: U) -> U => value@move\nfunc hg<U>(v: U) -> U\n    U is PrimitiveInteger\n    return v\nfunc same<U>(a: U, b: U) -> U => a@move\nfunc two<A, B>(a: A, b: B) -> A => a@move\nfunc look<U>(v: ref/U) -> () => ()\n";

    private const string FixedSlotForms = FixedSlots + "struct Pair<A>\n    public var a: A\n    public init(a: A) => self.a = a@move\n    public func with<B>(self: ref/Self, x: A, y: B) -> B => y@move\nfunc keep<s/T>(x: s/T) -> () => ()\nfunc lk<length N>(v: ref/[N of i32]) -> i32 => 0\nfunc ln<length N, T>(v: ref/[N of T], w: T) -> T => w@move\nfunc app<U>(v: U, f: (U) -> U) -> U => v@move\nfunc dflt<U>(v: U, w: i32 = 0) -> U => v@move\n";

    private const string FixedViews = "struct View<T> {source}\n    public let data: ref/T during source\n    public init(data: ref/T during source) => self.data = data\n";

    private const string FixedCases = "enum R<T> {src}\n    A(ref/T during src)\n    B((T, ref/i32 during src))\n    N\n";

    private const string FixedItems = "func zero() -> u32 => 0\nfunc one() -> u32 => 1\nfunc inc(value: u32) -> u32 => value + 1\nunsafe func raw() -> u32 => 0\nfunc keep<T>(value: T) -> T => value@move\nfunc both<T>(a: T, b: T) -> T => a@move\nfunc pair<T, U>(first: T, second: U) -> T => first@move\nfunc look<T>(value: ref/T) -> () => ()\nstruct Box<T>\n    public let value: T\n    public init(value: T) => self.value = value@move\n";

    private const string FunctionContracts = "func kg(c: bool, y: i32, s: ref/i32 during j, t: ref/i32 during k, fa: (ref/i32 during j) -> ref/i32 during j, fb: (ref/i32 during k) -> ref/i32 during k) -> i32\n";

    private const string JoinPositions = "func pt(p: (i32, i32)) -> i32 => p.0\nfunc pn(p: ((i32, i32), i32)) -> i32 => p.1\nfunc pr(p: (ref/i32 during a, i32)) -> i32 => p.1\nfunc pu(p: (uniq/i32 during a, i32)) -> i32 => p.1\nfunc gr<T>(p: (ref/T during a, i32)) -> i32 => p.1\nfunc gu<T>(p: (uniq/T during a, i32)) -> i32 => p.1\nfunc p2<T>(x: (T, T)) -> i32 => 0\nfunc fdt(p: (Dictionary<i32, i32>, i32)) -> i32 => p.1\nfunc pick(p: (ref/i32 during a, i32)) -> ref/i32 during a => p.0\nfunc k(c: bool, n: ref/i32, m: ref/i32, v: i32, r: ref/i64, s: ref/i64, u: uniq/i32, w: uniq/i32, o: Option<i32>) -> i32\n";

    private const string HandleJoins = "struct Box\n    public var v: i32\n    public init(v: i32) => self.v = v\n    public func read(self: objref/Self) -> i32 => self.v\nfunc firstO(p: (objref/Box during a, i32)) -> objref/Box during a => p.0\nfunc lookO(p: (objref/Box during a, i32)) -> i32 => p.1\nfunc f(c: bool) -> i32\n";

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
    [InlineData("G729", "r18", Sources + SourcesBody + "    let j = if c => (u@ref, 1) else => (1, y)\n    return r\n", "")]
    [InlineData("G729", "s07", Sources + SourcesBody + "    let t = identity(if c => (u@ref, 1) else => (1, y))\n    return r\n", "")]
    [InlineData("G729", "s08", RefSources + "    return ft(if c => (w, 1) else => (1, 2))\n", "")]

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
    [InlineData("G729", "x10", Sources + SourcesBody + "    let j = if c => [(u@ref, 1)] else => [(1, 2)]\n    return r\n", "")]
    [InlineData("G729", "x10a", Sources + SourcesBody + "    let t = identity(if c => [(u@ref, 1)] else => [(1, 2)])\n    return r\n", "")]
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
    [InlineData("I2", "o18", Sources + SourcesBody + "    let t = identity(([y], [u@ref]))\n    return r\n", "")]
    [InlineData("guard", "o19", Sources + SourcesBody + "    let j = if c => ([y], [u@ref]) else => ([y], [y])\n    return r\n", "*: let j = if c => ([y], [u@ref]) else => ([y], [y]) | ...")]
    [InlineData("guard", "c16", Sources + SourcesBody + "    let t = identity(match d\n        true => [u@ref]\n        false => [y]\n    )\n    return r\n", "*: * | ...")]
    [InlineData("guard", "c17", Sources + SourcesBody + "    let j = match d\n        true => [u@ref]\n        false => [y]\n    return r\n", "*: * | ...")]
    [InlineData("guard", "d10", Sources + SourcesBody + "    r = g(if c => [u@ref: y] else => [y: y])\n    return r\n", "*: r = g(if c => [u@ref: y] else => [y: y]) | ...")]
    [InlineData("guard", "d11", Sources + SourcesBody + "    let j = if c => [u@ref: y] else => [y: y]\n    return r\n", "*: let j = if c => [u@ref: y] else => [y: y] | ...")]
    [InlineData("guard", "d12", Sources + SourcesBody + "    r = g(if c => [y: y] else => [u@ref: y])\n    return r\n", "*: r = g(if c => [y: y] else => [u@ref: y]) | ...")]
    [InlineData("guard", "d13", Sources + SourcesBody + "    r = fd(if c => [1: x] else => [2: b])\n    return r\n", "*: r = fd(if c => [1: x] else => [2: b]) | ...")]

    // SPEC 10.8, 10.5.1, 4.3.1 (PLAN G378's literal forms, G725): a whole Tuple or array literal at a bare slot is one piece of evidence when
    // every element is, typed as its own binder types it; it agrees or conflicts with the slot's other evidence in every order (o21 to o23,
    // o33; o28: G725's list literal). A literal holding a Literal element is none: its parts fit the slot's binding (o26, o27, o29 to
    // o32) or, with no other evidence, take their defaults after selection (o24, o25: the two defaulted Types conflict in either order).
    [InlineData("guard", "o21", Sources + SourcesBody + "    r = h2((y, u), (y, u@ref))\n    return r\n", "*: r = h2((y, u), (y, u@ref)) | ...")]
    [InlineData("guard", "o22", Sources + SourcesBody + "    r = h2((y, u@ref), (y, u))\n    return r\n", "*: r = h2((y, u@ref), (y, u)) | ...")]
    [InlineData("guard", "o23", Sources + SourcesBody + "    r = p2(((y, u), (y, u@ref)))\n    return r\n", "*: r = p2(((y, u), (y, u@ref))) | ...")]
    [InlineData("guard", "o24", Sources + SourcesBody + "    r = h2((x, 1), (2, y))\n    return r\n", "*: r = h2((x, 1), (2, y)) | ...")]
    [InlineData("guard", "o25", Sources + SourcesBody + "    r = h2((2, y), (x, 1))\n    return r\n", "*: r = h2((2, y), (x, 1)) | ...")]
    [InlineData("guard", "o26", Sources + SourcesBody + "    r = h2((x, 1), (x, y))\n    return r\n", "")]
    [InlineData("guard", "o27", Sources + SourcesBody + "    r = h2((x, y), (x, 1))\n    return r\n", "")]
    [InlineData("guard", "o28", Sources + SourcesBody + "    let t = identity([y])\n    return r\n", "")]
    [InlineData("guard", "o29", Sources + SourcesBody + "    r = h2([y], [1])\n    return r\n", "")]
    [InlineData("guard", "o30", Sources + SourcesBody + "    r = h2([1], [y])\n    return r\n", "")]
    [InlineData("guard", "o31", Sources + SourcesBody + "    let t = identity([[y], [1]])\n    return r\n", "")]
    [InlineData("guard", "o32", Sources + SourcesBody + "    let t = identity([[1], [y]])\n    return r\n", "")]
    [InlineData("guard", "o33", Sources + SourcesBody + "    let t = identity([[y], [x]])\n    return r\n", "*: let t = identity([[y], [x]]) | ...")]
    // The owner's O-267 (a) (M3): a nested literal is typed by its binder's element rule, so one that fixes its Type (each child position
    // holding evidence, `[x, 1]`) establishes the element Type and the others adopt it, in every order (o34 to o39: arrays, depth 3, a
    // Dictionary value, a Tuple).
    [InlineData("guard", "o34", Sources + SourcesBody + "    let t = identity([[1], [x, 1]])\n    return r\n", "")]
    [InlineData("guard", "o35", Sources + SourcesBody + "    let t = identity([[x, 1], [1]])\n    return r\n", "")]
    [InlineData("guard", "o36", Sources + SourcesBody + "    let t = identity([[[1]], [[x, 1]]])\n    return r\n", "")]
    [InlineData("guard", "o37", Sources + SourcesBody + "    let t = identity([[1: [2]], [x: [x, 1]]])\n    return r\n", "")]
    [InlineData("guard", "o38", Sources + SourcesBody + "    let t = identity([(1, [2]), (x, [x, 1])])\n    return r\n", "")]
    [InlineData("guard", "o39", Sources + SourcesBody + "    let t = identity([(x, [x, 1]), (1, [2])])\n    return r\n", "")]
    // SPEC 14.9.2 step 1, 10.2: a fixed expectation, an explicit Type argument's or a parameter's, fits each source, a reference at a value element
    // position value-read. A source conflict only makes the argument wait for the selected candidate (SPEC 10.5.1).
    [InlineData("guard", "e10", Sources + SourcesBody + "    r = g<Array<i32>>([u@ref])\n    return r\n", "")]
    [InlineData("I2", "e11", Sources + SourcesBody + "    r = g<Array<i32>>(if c => [y] else => [u@ref])\n    return r\n", "", Explicit = true)]
    [InlineData("guard", "e12", Sources + SourcesBody + "    r = ga<i32>([u@ref])\n    return r\n", "")]
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
    [InlineData("guard", "u2n", LengthSources + "    return takeN(if c => zeros() else => zeros())\n", "UnboundTypeArgument_Kd: return takeN(if c => zeros() else => zeros())")]
    [InlineData("guard", "mjf", LengthSources + "    return takeN(if c => zeros() else => [2 of 0@i64])\n", "")]

    // SPEC 10.8, 10.5.1 (PLAN G587): a length slot that no evidence binds is an unbound slot as a Type slot is: the candidate stays applicable, a
    // nested call waits for its position, and a selected call's unbound length is UnboundTypeArgument_Kd. SPEC 4.4.1: a length equation is not
    // solved backward.
    [InlineData("guard", "g587a", FixedLengths + LengthSources + "    return take2(zeros())\n", "")]
    [InlineData("guard", "g587b", LengthSources + "    let j = zeros()\n    return 0\n", "UnboundTypeArgument_Kd: let j = zeros()")]
    [InlineData("guard", "g587c", LengthSources + "    return takeN(zeros())\n", "UnboundTypeArgument_Kd: return takeN(zeros())")]
    [InlineData("guard", "g587d", FixedLengths + LengthSources + "    return take2(dec())\n", "UnboundTypeArgument_Kd: return take2(dec())")]
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
    // SPEC 10.5.1, 14.9.2 step 2, 10.2 (PLAN G729): a join formed part by part, at a call, is fitted as a Tuple literal argument: each
    // position's typed parts are one control-flow argument, adapted at the candidate's Type (value reads, borrows, a shared reference
    // through a uniq slot), and its borrow covers every part's Loan; a waiting Dictionary literal part fits whole (BU-43). Invalid twins:
    // a part that adapts to no common Type, an exclusive/shared mismatch. j14 (layers value-read into a common i32) passes on dev and
    // must not regress; j15 needs the position's value adapted once (R7-I2 d's position source). A position whose one value, of a value
    // Type, is borrowed at a `ref/U` element is an owner temporary at the join's position (SPEC 10.2 row 2, 3.6), which nothing plans yet:
    // the call's located Unsupported_Kd at the argument until R7-I2 d's position source (G729's interim; F's rulings 11 and 13): Places
    // (j04, j07, j09 retained, j09n, j18), layers value-read (j17, j26), a value beside a uniq slot (j27), beside an exclusive borrow of a
    // part (j28), at a join inside a Tuple literal argument (j39), and every other borrowed row off the whitelist, an owning `rc`, `obj`
    // or `arc` handle's object borrow at `objref/U` among them (j31-j38: bare handle Places, retaining or not, and moved handles used in
    // the statement). Only a by-value element (j01) and a borrowed element whose common Type is itself a reference (j05, j29; uniq at
    // uniq, j30) keep their verdicts.
    [InlineData("G729", "j01", JoinPositions + "    return pt(if c => (n, 1) else => (m, 2))\n", "")]
    [InlineData("G729", "j02", JoinPositions + "    return pn(if c => ((n, 1), 2) else => ((m, 3), 4))\n", "")]
    [InlineData("G729", "j03", JoinPositions + "    return pt(match o\n        .Some(_) => (n, 1)\n        .None => (m, 2)\n    )\n", "")]
    [InlineData("G729", "j04", JoinPositions + "    var x: i32 = 1\n    var y: i32 = 2\n    return pr(if c => (x, 1) else => (y, 2))\n", "Unsupported_Kd: return pr(if c => (x, 1) else => (y, 2))")]
    [InlineData("G729", "j05", JoinPositions + "    return pr(if c => (u, 1) else => (w, 2))\n", "")]
    [InlineData("G729", "j06", JoinPositions + "    return pt(if c => (n, 1) else => (m, (return 2)))\n", "")]
    [InlineData("G729", "j07", JoinPositions + "    var x: i32 = 1\n    var y: i32 = 2\n    return gr(if c => (x, 1) else => (y, 2))\n", "Unsupported_Kd: return gr(if c => (x, 1) else => (y, 2))")]
    [InlineData("G729", "j08", JoinPositions + "    return pn((if c => (n, 1) else => (m, 2), 3))\n", "")]
    [InlineData("G729", "j09", JoinPositions + "    var x: i32 = 1\n    var y: i32 = 2\n    let q = pick(if c => (x, 1) else => (y, 2))\n    return q\n", "Unsupported_Kd: let q = pick(if c => (x, 1) else => (y, 2))")]
    [InlineData("guard", "j14", JoinPositions + "    let nn = m@ref\n    return p2(if c => (n, 1) else => (nn, 2))\n", "")]
    [InlineData("I2", "j15", JoinPositions + "    let nn = m@ref\n    return gr(if c => (n, 1) else => (nn, 2))\n", "", Explicit = true)]
    [InlineData("guard", "j10", JoinPositions + "    return fdt(if c => ([1: 2], v) else => ([3: 4], 5))\n", "")]
    [InlineData("guard", "j11", JoinPositions + "    let d: Dictionary<i32, i32> = [1: 2]\n    return fdt(if c => (d@move, 1) else => ([3: 4], 2))\n", "")]
    [InlineData("guard", "j12", JoinPositions + "    return fdt(if c => ([1: 2], 1) else => ([3: 4], 2))\n", "")]
    [InlineData("guard", "j13", JoinPositions + "    return fdt(if c => ([1: v], v) else => ([3: 4], 5))\n", "")]
    [InlineData("guard", "j01n", JoinPositions + "    return pt(if c => (n, 1) else => (r, 2))\n", "TypeMismatch_Kd: return pt(if c => (n, 1) else => (r, 2))")]
    [InlineData("guard", "j02n", JoinPositions + "    return pn((if c => (r, 1) else => (s, 2), 3))\n", "NoApplicableOverload_Kd: return pn((if c => (r, 1) else => (s, 2), 3))")]
    [InlineData("guard", "j03n", JoinPositions + "    return pu(match o\n        .Some(_) => (u, 1)\n        .None => (n, 2)\n    )\n", "NoApplicableOverload_Kd: return pu(match o")]
    [InlineData("guard", "j07n", JoinPositions + "    return gu(if c => (u, 1) else => (n, 2))\n", "NoApplicableOverload_Kd: return gu(if c => (u, 1) else => (n, 2))")]
    [InlineData("G729", "j09n", JoinPositions + "    var x: i32 = 1\n    var y: i32 = 2\n    let q = pick(if c => (x, 1) else => (y, 2))\n    y = 3\n    return q\n", "Unsupported_Kd: let q = pick(if c => (x, 1) else => (y, 2))")]
    [InlineData("G729", "j26", JoinPositions + "    var x: i32 = 1\n    var y: i32 = 2\n    let q = pick(if c => (x@ref, 1) else => (y@uniq, 2))\n    return q\n", "Unsupported_Kd: let q = pick(if c => (x@ref, 1) else => (y@uniq, 2))")]
    [InlineData("G729", "j27", JoinPositions + "    let q = pick(if c => (v, 1) else => (u, 2))\n    return q\n", "Unsupported_Kd: let q = pick(if c => (v, 1) else => (u, 2))")]
    [InlineData("G729", "j28", "func both(p: (ref/i32 during a, i32), q: uniq/i32) -> i32 => p.1\n" + JoinPositions + "    var x: i32 = 1\n    var y: i32 = 2\n    return both(if c => (x, 1) else => (y, 2), x@uniq)\n", "Unsupported_Kd: return both(if c => (x, 1) else => (y, 2), x@uniq)")]
    [InlineData("guard", "j29", JoinPositions + "    let q = pick(if c => (n, 1) else => (m, 2))\n    return q\n", "")]
    [InlineData("guard", "j30", JoinPositions + "    return pu(if c => (u, 1) else => (w, 2))\n", "")]
    [InlineData("G729", "j31", HandleJoins + "    let h1 = Box.init(v: 10)@rc\n    let h2 = Box.init(v: 20)@rc\n    let k = firstO(if c => (h1, 1) else => (h2, 2))\n    return k.read()\n", "Unsupported_Kd: let k = firstO(if c => (h1, 1) else => (h2, 2))")]
    [InlineData("G729", "j32", HandleJoins + "    let h1 = Box.init(v: 10)@obj\n    let h2 = Box.init(v: 20)@obj\n    let k = firstO(if c => (h1, 1) else => (h2, 2))\n    return k.read()\n", "Unsupported_Kd: let k = firstO(if c => (h1, 1) else => (h2, 2))")]
    [InlineData("G729", "j33", HandleJoins + "    let h1 = Box.init(v: 10)@rc\n    let h2 = Box.init(v: 20)@rc\n    return firstO(if c => (h1@move, 1) else => (h2@move, 2)).read()\n", "Unsupported_Kd: return firstO(if c => (h1@move, 1) else => (h2@move, 2)).read()")]
    [InlineData("G729", "j34", HandleJoins + "    let h1 = Box.init(v: 10)@obj\n    let h2 = Box.init(v: 20)@obj\n    return firstO(if c => (h1@move, 1) else => (h2@move, 2)).read()\n", "Unsupported_Kd: return firstO(if c => (h1@move, 1) else => (h2@move, 2)).read()")]
    [InlineData("G729", "j35", HandleJoins + "    let h1 = Box.init(v: 10)@arc\n    let h2 = Box.init(v: 20)@arc\n    let k = firstO(if c => (h1, 1) else => (h2, 2))\n    return k.read()\n", "Unsupported_Kd: let k = firstO(if c => (h1, 1) else => (h2, 2))")]
    [InlineData("G729", "j36", HandleJoins + "    let h1 = Box.init(v: 10)@arc\n    let h2 = Box.init(v: 20)@arc\n    return lookO(if c => (h1, 1) else => (h2, 2))\n", "Unsupported_Kd: return lookO(if c => (h1, 1) else => (h2, 2))")]
    [InlineData("G729", "j37", HandleJoins + "    let h1 = Box.init(v: 10)@rc\n    let h2 = Box.init(v: 20)@rc\n    return lookO(if c => (h1, 1) else => (h2, 2))\n", "Unsupported_Kd: return lookO(if c => (h1, 1) else => (h2, 2))")]
    [InlineData("G729", "j38", HandleJoins + "    let h1 = Box.init(v: 10)@arc\n    let h2 = Box.init(v: 20)@arc\n    return firstO(if c => (h1@move, 1) else => (h2@move, 2)).read()\n", "Unsupported_Kd: return firstO(if c => (h1@move, 1) else => (h2@move, 2)).read()")]
    [InlineData("G729", "j39", "func fe(p: ((ref/i32 during a, i32), i32)) -> i32 => p.1\n" + JoinPositions + "    var x: i32 = 1\n    var y: i32 = 2\n    return fe((if c => (x, 1) else => (y, 2), 3))\n", "Unsupported_Kd: return fe((if c => (x, 1) else => (y, 2), 3))")]
    // PLAN G729: the adaptation decides an overload (j16: no numeric conversion to i64); a shared reference beside a uniq slot, value-read
    // into one i32 (j17, the interim above); an explicit T at a ref/T slot (j18, the interim) and its twin (j18n: an i32 Place has no
    // ref/i64 row). A typed Tuple or array literal
    // part is typed whole (BU-43): its Type, not its elements, fits (j19n, j20n: no element adaptation; j19, j20: the same Type, and a
    // temporary of it borrowed at ref/(...) ranked below Exact).
    [InlineData("G729", "j16", "func fo(p: (i32, i32)) -> i32 => 0\nfunc fo(p: (i64, i32)) -> i32 => 1\n" + JoinPositions + "    return fo(if c => (n, 1) else => (m, 2))\n", "")]
    [InlineData("G729", "j17", JoinPositions + "    return pr(if c => (n, 1) else => (u, 2))\n", "Unsupported_Kd: return pr(if c => (n, 1) else => (u, 2))")]
    [InlineData("G729", "j18", JoinPositions + "    var x: i32 = 1\n    var y: i32 = 2\n    return gr<i32>(if c => (x, 1) else => (y, 2))\n", "Unsupported_Kd: return gr<i32>(if c => (x, 1) else => (y, 2))")]
    [InlineData("guard", "j18n", JoinPositions + "    var x: i32 = 1\n    var y: i32 = 2\n    return gr<i64>(if c => (x, 1) else => (y, 2))\n", "NoApplicableOverload_Kd: return gr<i64>(if c => (x, 1) else => (y, 2))")]
    [InlineData("guard", "j19", "func pn2(p: ((ref/i32 during a, ref/i32 during a), i32)) -> i32 => p.1\n" + JoinPositions + "    return pn2(if c => ((n, m), 1) else => ((m, n), 2))\n", "")]
    [InlineData("guard", "j19n", JoinPositions + "    return pn(if c => ((n, m), 1) else => ((m, n), 2))\n", "NoApplicableOverload_Kd: return pn(if c => ((n, m), 1) else => ((m, n), 2))")]
    [InlineData("guard", "j20", "func fw(p: ((ref/i32 during a, ref/i32 during a), i32)) -> bool => true\nfunc fw(p: (ref/(ref/i32 during a, ref/i32 during a) during b, i32)) -> i32 => 0\n" + JoinPositions + "    let q2 = (n, m)\n    let q = fw(if c => ((n, m), 1) else => (q2, 2))\n    let z: bool = q\n    return 0\n", "")]
    [InlineData("guard", "j20n", "func fta(p: (Array<i32>, i32)) -> i32 => p.1\n" + JoinPositions + "    return fta(if c => ([n], 1) else => ([m], 2))\n", "NoApplicableOverload_Kd: return fta(if c => ([n], 1) else => ([m], 2))")]
    // SPEC 14.9.2 step 2 (PLAN G729, finding I1): a position holding literals beside safe reference layers over a read Type has that Type,
    // the references value-read, as a join's own sources do (BesideLiterals): no temporary, at a call (j21, j22 the overload it decides, j24
    // nested, j25 with no Loan after the call) and at a let (j23; j23n its twin, the position's i32 at bool).
    [InlineData("G729", "j21", JoinPositions + "    return pt(if c => (n, 1) else => (1, 2))\n", "")]
    [InlineData("G729", "j22", "func fo2(p: (i64, i32)) -> i32 => 0\nfunc fo2(p: (bool, i32)) -> i32 => 1\n" + JoinPositions + "    return fo2(if c => (r, 1) else => (1, 2))\n", "")]
    [InlineData("G729", "j23", JoinPositions + "    let q = if c => (n, 1) else => (1, 2)\n    return q.0\n", "")]
    [InlineData("guard", "j23n", JoinPositions + "    let q = if c => (n, 1) else => (1, 2)\n    let z: bool = q.0\n    return 0\n", "TypeMismatch_Kd: let z: bool = q.0")]
    [InlineData("G729", "j24", JoinPositions + "    return pn(if c => ((n, 1), 2) else => ((1, 2), 3))\n", "")]
    [InlineData("G729", "j25", JoinPositions + "    var x: i32 = 1\n    let q = pt(if c => (x@ref, 1) else => (1, 2))\n    x = 3\n    return q\n", "")]
    public void ACommonTypeFormationHasItsVerdict(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // SPEC 10.2.2 step 1, 10.2, 8.1.3 (PLAN G719): explicit Type arguments bind first and supply every slot, so each argument fits its fixed
    // parameter Type by the common adaptation: a value read at a read Type (fr1-fr4; fr5 through a caller's proven parameter), a new shared
    // borrow or one shared reference at a fixed `ref/U` (fb1-fb3), each argument alone (fs1, fs2), at a method (fm1), a join's sources and a
    // call's result (fj1, fc1). The n rows are their invalid twins: no conversion, no read of a Type that is no read Type and no implicit
    // exclusive borrow. A pair layer is read once G703 lands (fp1, fp2); a pair that does not qualify is never followed (fp1n). The same
    // holds beside the declaring Type's slot, at a `uniq` receiver, a pair slot, length slots, an anonymous function, a default, named
    // arguments, a Never source, a Tuple and an exact argument beside a borrow (fm2-ft1). A fixed slot can make a further candidate
    // applicable, and SPEC 10.4 ranks it (fo1-fo3): it is selected (fo1), opposing advantages leave none (fo1n), and the selected
    // candidate's result is the call's (fo2n).
    // The same entry serves a construction's Type (fv1: `ConstructorType` forms `source` from the adapted argument) and a Case
    // construction's payload whose Type holds the enum's own Origin (fc1, fc2), and a written Function Type takes an Item's or
    // a group member's call signature as its evidence, erased at the fit (fe1-fe5; PLAN G731 c01). An unsafe function's Item is no
    // value (fe3n), nor an import's (fe4n; SPEC 22.6, DECISIONS BU-158).
    [Theory]
    [InlineData("guard", "fr1", FixedSlots + "func k(n: ref/i32) -> i32 => identity<i32>(n)\n", "")]
    [InlineData("guard", "fr2", FixedSlots + "func k(n: ref/i32) -> i32\n    let a = identity<i32>(n)\n    return a\n", "")]
    [InlineData("guard", "fr3", FixedSlots + "func k(u: uniq/i32) -> i32 => identity<i32>(u)\n", "")]
    [InlineData("guard", "fr4", FixedSlots + "func k(n: ref/i32) -> i32\n    let rr = n@ref\n    return identity<i32>(rr)\n", "")]
    [InlineData("guard", "fr5", FixedSlots + "func k<T>(n: ref/T) -> T\n    T is PrimitiveInteger\n    return identity<T>(n)\n", "")]
    [InlineData("guard", "fb1", FixedSlots + "func k(v: i32) -> i32\n    let r = identity<ref/i32>(v)\n    return r\n", "")]
    [InlineData("guard", "fb2", FixedSlots + "func k(un: uniq/Node) -> i32\n    let r = identity<ref/Node>(un)\n    return r.weight\n", "")]
    [InlineData("guard", "fb3", FixedSlots + "func k(n: ref/i32) -> ()\n    let rr = n@ref\n    look<i32>(rr)\n", "")]
    [InlineData("guard", "fs1", FixedSlots + "func k(n: ref/i32, v: i32) -> i32 => same<i32>(v, n)\n", "")]
    [InlineData("guard", "fs2", FixedSlots + "func k(n: ref/i32, m: ref/i64) -> i32 => two<i32, i64>(n, m)\n", "")]
    [InlineData("guard", "fm1", FixedSlots + "func k(tool: ref/Tool, n: ref/i32) -> i32 => tool.pick<i32>(n)\n", "")]
    [InlineData("guard", "fj1", FixedSlots + "func k(c: bool, n: ref/i32, m: ref/i32) -> i32 => identity<i32>(if c => n else => m)\n", "")]
    [InlineData("guard", "fc1", FixedSlots + "func k(n: ref/i32) -> i32 => identity<i32>(identity(n))\n", "")]
    [InlineData("guard", "fr1n", FixedSlots + "func k(n: ref/i32) -> i64 => identity<i64>(n)\n", "NoApplicableOverload_Kd: func k(n: ref/i32) -> i64 => identity<i64>(n)")]
    [InlineData("guard", "fr5n", FixedSlots + "func k<T>(n: ref/T) -> T => identity<T>(n)\n", "NoApplicableOverload_Kd: func k<T>(n: ref/T) -> T => identity<T>(n)")]
    [InlineData("guard", "fr6n", FixedSlots + "func k(node: ref/Node) -> i32\n    let c = identity<Node>(node)\n    return c.weight\n", "NoApplicableOverload_Kd: let c = identity<Node>(node) | ...")]
    [InlineData("guard", "fb1n", FixedSlots + "func k() -> i32\n    var node = Node.init(1)\n    let r = identity<uniq/Node>(node)\n    return r.weight\n", "ExclusiveBorrowRequired_Kd: let r = identity<uniq/Node>(node) | ...")]
    [InlineData("guard", "fb3n", FixedSlots + "func k(n: ref/i32) -> () => look<i64>(n)\n", "NoApplicableOverload_Kd: func k(n: ref/i32) -> () => look<i64>(n)")]
    [InlineData("guard", "fs2n", FixedSlots + "func k(n: ref/i32, m: ref/i64) -> i64 => two<i64, i32>(n, m)\n", "NoApplicableOverload_Kd: func k(n: ref/i32, m: ref/i64) -> i64 => two<i64, i32>(n, m)")]
    [InlineData("guard", "fm1n", FixedSlots + "func k(tool: ref/Tool, n: ref/i32) -> i64 => tool.pick<i64>(n)\n", "NoApplicableOverload_Kd: func k(tool: ref/Tool, n: ref/i32) -> i64 => tool.pick<i64>(n)")]
    [InlineData("guard", "fo1", FixedSlots + "func f<U>(v: ref/U, w: ref/(ref/i64 during b)) -> i32 => 0\nfunc f<U>(v: ref/i32, w: ref/i64) -> (U, bool) => $abort(\"b\")\nfunc t(n: ref/i32, m: ref/i64) -> i32\n    let x = n@ref\n    let y = m@ref\n    let r = f<i32>(x, y)\n    return r\n", "")]
    [InlineData("guard", "fo2", FixedSlots + "func f<U>(v: U, w: ref/(ref/i64 during b)) -> i32 => 0\nfunc f<U>(v: U, w: ref/i64) -> bool => true\nfunc t(n: ref/i32, m: ref/i64) -> i32\n    let y = m@ref\n    let r = f<i32>(n, y)\n    return r\n", "")]
    [InlineData("guard", "fo3", FixedSlots + "func f<U>(v: ref/U) -> i32 => 0\nfunc f<U>(v: ref/(ref/U during c)) -> bool => true\nfunc t(n: ref/i32) -> bool\n    let x = n@ref\n    let r = f<i32>(x)\n    return r\n", "")]
    [InlineData("guard", "fm2", FixedSlotForms + "func t(p: ref/Pair<i32>, n: ref/i32, m: ref/i64) -> i64 => p.with<i64>(n, m)\n", "")]
    [InlineData("guard", "fm3", FixedSlotForms + "func t(tool: uniq/Tool, n: ref/i32) -> i32 => tool.pick<i32>(n)\n", "")]
    [InlineData("guard", "fp3", FixedSlotForms + "func t(n: ref/i32) -> () => keep<i32>(n)\n", "")]
    [InlineData("guard", "fl1", FixedSlotForms + "func t(a: ref/[2 of i32]) -> i32\n    let rr = a@ref\n    return lk<2>(rr)\n", "")]
    [InlineData("guard", "fl2", FixedSlotForms + "func t(a: ref/[2 of i32], n: ref/i32) -> i32 => ln<2, i32>(a, n)\n", "")]
    [InlineData("guard", "fa1", FixedSlotForms + "func t(n: ref/i32) -> i32 => app<i32>(n, func (x: i32) -> i32 => x)\n", "")]
    [InlineData("guard", "fs3", FixedSlotForms + "func t(n: ref/i32, v: i32) -> i32\n    let r = same<ref/i32>(n, v)\n    return r\n", "")]
    [InlineData("guard", "fs4", FixedSlotForms + "func t(n: ref/i32, v: i32) -> i32\n    let r = same<ref/i32>(v, n)\n    return r\n", "")]
    [InlineData("guard", "fs5", FixedSlotForms + "func t(n: ref/i32, m: ref/i64) -> i32 => two<i32, i64>(b: m, a: n)\n", "")]
    [InlineData("guard", "fj2", FixedSlotForms + "func t(c: bool, n: ref/i32) -> i32 => identity<i32>(if c => n else => (return 0))\n", "")]
    [InlineData("guard", "fb4", FixedSlotForms + "func t(u: uniq/i32) -> ()\n    let ru = u@ref\n    look<i32>(ru)\n", "")]
    [InlineData("guard", "fd1", FixedSlotForms + "func t(n: ref/i32) -> i32 => dflt<i32>(n)\n", "")]
    [InlineData("guard", "ft1", FixedSlots + "func p2<T>(x: (T, T)) -> i32 => 0\nfunc t(n: ref/i32, m: ref/i32) -> i32 => p2<i32>((n, m))\n", "")]
    [InlineData("guard", "fo1n", FixedSlots + "func f<U>(v: ref/U, w: ref/(ref/i64 during b)) -> i32 => 0\nfunc f<U>(v: ref/(ref/U during c), w: ref/i64) -> i32 => 1\nfunc t(n: ref/i32, m: ref/i64) -> i32\n    let x = n@ref\n    let y = m@ref\n    return f<i32>(x, y)\n", "AmbiguousBinding_Kd: return f<i32>(x, y)")]
    [InlineData("guard", "fo2n", FixedSlots + "func f<U>(v: ref/U, w: ref/(ref/i64 during b)) -> i32 => 0\nfunc f<U>(v: ref/i32, w: ref/i64) -> (U, bool) => $abort(\"b\")\nfunc t(n: ref/i32, m: ref/i64) -> bool\n    let x = n@ref\n    let y = m@ref\n    let r = f<i32>(x, y)\n    return r.1\n", "TypeMismatch_Kd: return r.1")]
    [InlineData("guard", "fm2n", FixedSlotForms + "func t(p: ref/Pair<i32>, n: ref/i32, m: ref/i64) -> i64 => p.with<i64>(m, n)\n", "NoApplicableOverload_Kd: func t(p: ref/Pair<i32>, n: ref/i32, m: ref/i64) -> i64 => p.with<i64>(m, n)")]
    [InlineData("guard", "fp3n", FixedSlotForms + "func t(n: ref/i32) -> () => keep<i64>(n)\n", "NoApplicableOverload_Kd: func t(n: ref/i32) -> () => keep<i64>(n)")]
    [InlineData("guard", "fl1n", FixedSlotForms + "func t(a: ref/[2 of i32]) -> i32\n    let rr = a@ref\n    return lk<3>(rr)\n", "NoApplicableOverload_Kd: return lk<3>(rr)")]
    [InlineData("guard", "fl2n", FixedSlotForms + "func t(a: ref/[2 of i32], n: ref/i32) -> i64 => ln<2, i64>(a, n)\n", "NoApplicableOverload_Kd: func t(a: ref/[2 of i32], n: ref/i32) -> i64 => ln<2, i64>(a, n)")]
    [InlineData("guard", "fa1n", FixedSlotForms + "func t(n: ref/i32) -> i64 => app<i64>(n, func (x: i64) -> i64 => x)\n", "NoApplicableOverload_Kd: func t(n: ref/i32) -> i64 => app<i64>(n, func (x: i64) -> i64 => x)")]
    [InlineData("guard", "fx1n", FixedSlotForms + "func t(n: ref/i32) -> i32 => identity<i32, i32>(n)\n", "NoApplicableOverload_Kd: func t(n: ref/i32) -> i32 => identity<i32, i32>(n)")]
    [InlineData("guard", "fx2n", FixedSlotForms + "func t(n: ref/i32) -> i64\n    let r: i64 = identity<i32>(n)\n    return r\n", "NoApplicableOverload_Kd: let r: i64 = identity<i32>(n)")]
    [InlineData("guard", "fb5n", FixedSlotForms + "func t(n: ref/i32) -> i32\n    let r = identity<uniq/i32>(n)\n    return r\n", "NoApplicableOverload_Kd: let r = identity<uniq/i32>(n)")]
    [InlineData("guard", "fb6n", FixedSlotForms + "func t() -> () => look<bool>(1)\n", "NoApplicableOverload_Kd: func t() -> () => look<bool>(1)")]
    [InlineData("guard", "fv1", FixedViews + "func t(n: ref/i32) -> ref/i32\n    let rr = n@ref\n    let w = View<i32>.init(rr)\n    return w.data\n", "")]
    [InlineData("guard", "fc1", FixedCases + "func t(n: ref/i32) -> i32\n    let rr = n@ref\n    let e = R<i32>.A(rr)\n    return 0\n", "")]
    [InlineData("guard", "fc2", FixedCases + "func t(n: ref/i32, r: ref/i32) -> i32\n    let e = R<i32>.B((n, r))\n    return 0\n", "")]
    [InlineData("guard", "fe1", FixedItems + "func t() -> u32\n    let f = keep<() -> u32>(zero)\n    return f()\n", "")]
    [InlineData("guard", "fe2", FixedItems + "func t() -> u32\n    let b = Box<() -> u32>.init(zero)\n    return b.value()\n", "")]
    [InlineData("guard", "fe3", FixedItems + "func t() -> u32\n    let f = both<() -> u32>(zero, one)\n    return f()\n", "")]
    [InlineData("guard", "fe4", FixedItems + "func t() -> u32\n    let f = keep<(u32) -> u32>(inc)\n    return f(1)\n", "")]
    [InlineData("guard", "fe5", FixedItems + "func t() -> u32\n    let f = pair<() -> u32, u32>(zero, 1@u32)\n    return f()\n", "")]
    [InlineData("guard", "fv1n", FixedViews + "func t(n: ref/i32) -> i32\n    let rr = n@ref\n    let w = View<i64>.init(rr)\n    return 0\n", "NoApplicableOverload_Kd: let w = View<i64>.init(rr)")]
    [InlineData("guard", "fc1n", FixedCases + "func t(n: ref/i32) -> i32\n    let rr = n@ref\n    let e = R<i64>.A(rr)\n    return 0\n", "TypeMismatch_Kd: let e = R<i64>.A(rr)")]
    [InlineData("guard", "fc2n", FixedCases + "func t(n: ref/i32, r: ref/i32) -> i32\n    let e = R<i64>.B((n, r))\n    return 0\n", "TypeMismatch_Kd: let e = R<i64>.B((n, r))")]
    [InlineData("guard", "fe1n", FixedItems + "func t() -> ()\n    let f = keep<() -> i32>(zero)\n", "NoApplicableOverload_Kd: let f = keep<() -> i32>(zero)")]
    [InlineData("guard", "fe2n", FixedItems + "func t() -> ()\n    look<() -> u32>(zero)\n", "NoApplicableOverload_Kd: look<() -> u32>(zero)")]
    [InlineData("guard", "fe3n", FixedItems + "func t() -> ()\n    let f = keep<() -> u32>(raw)\n", "*: let f = keep<() -> u32>(raw)")]
    [InlineData("guard", "fe4n", FixedItems + "group Native\n    #LibraryImport(\"kernel32\", \"GetLastError\")\n    public func value() -> u32\nfunc t() -> ()\n    let f = keep<() -> u32>(Native.value)\n", "*: let f = keep<() -> u32>(Native.value)")]
    [InlineData("G703", "fp1", FixedSlots + "func k<s/T>(x: s/i32, m: s/T) -> i32\n    s is owner or ref\n    return identity<i32>(x)\n", "")]
    [InlineData("G703", "fp2", FixedSlots + "func k<s/T>(x: s/i32, m: s/T) -> i32\n    s is owner or ref\n    return hg<i32>(x)\n", "")]
    [InlineData("guard", "fp1n", FixedSlots + "func k<s/T>(x: s/i32, m: s/T) -> i32\n    return identity<i32>(x)\n", "NoApplicableOverload_Kd: return identity<i32>(x)")]
    public void AnExplicitTypeArgumentFixesItsParameterType(string unit, string form, string program, string records)
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

    // SPEC 10.4 step 1 (the owner's O-162 (A), PLAN G743): a Tuple or array literal argument is ranked by each element position as an argument
    // there, recursively, beside its own fit (receivers excluded, named arguments by source, before defaults, across the Array and [N of]
    // pair, inside a nested call and an init); a fill stays one argument (e7n) and step 2 is unchanged (e6n). A join formed part by part
    // ranks each position as one control-flow argument, once, by its common Type's class there (SPEC 10.5.1, 10.2.2; PLAN G729): e9
    // (its only non-Never part), e17 (common i32: Exact beats the borrowed value, as its element twin e16), e18 and e19 (one value, two
    // i32 Places), never by its parts' classes (they oppose in e17). The result Type shows the selected candidate; the n rows tie, oppose or meet an
    // erasure.
    [Theory]
    [InlineData("guard", "e1", ElementRanks + "    let q = fq((n, 1))\n    let z: i32 = q\n    return z\n", "")]
    [InlineData("guard", "e2", ElementRanks + "    let q = fq((v, 1))\n    let z: bool = q\n    return 0\n", "")]
    [InlineData("guard", "e3", ElementRanks + "    let q = fg((n, 1))\n    let z: i32 = q\n    return z\n", "")]
    [InlineData("guard", "e4", ElementRanks + "    let q = fo((n, v))\n    let z: i32 = q\n    return z\n", "")]
    [InlineData("guard", "e5", ElementRanks + "    let q = fn2(((n, 1), 2))\n    let z: i32 = q\n    return z\n", "")]
    [InlineData("guard", "e6", ElementRanks + "    let q = fa([n, m])\n    let z: i32 = q\n    return z\n", "")]
    [InlineData("guard", "e7", ElementRanks + "    let q = ff([n, m])\n    let z: i32 = q\n    return z\n", "")]
    [InlineData("guard", "e8", ElementRanks + "    let q = fq(if c => (n, 1) else => (m, 2))\n    let z: i32 = q\n    return z\n", "")]
    [InlineData("G729", "e9", ElementRanks + "    let q = fq(if c => (n, 1) else => ((return 0), 2))\n    let z: i32 = q\n    return z\n", "")]
    [InlineData("guard", "e10", ElementRanks + "    let s = S.init(1)\n    let q = s.m((n, 1))\n    let z: i32 = q\n    return z\n", "")]
    [InlineData("guard", "e11", ElementRanks + "    let q = fk(x: v, p: (n, 1))\n    let z: i32 = q\n    return z\n", "")]
    [InlineData("guard", "e12", ElementRanks + "    let q = fe((n, 1))\n    let z: i32 = q\n    return z\n", "")]
    [InlineData("guard", "e13", ElementRanks + "    let q = cd([n, m])\n    let z: i32 = q\n    return z\n", "")]
    [InlineData("guard", "e14", ElementRanks + "    let q = fq((n, sel((m, 1))))\n    let z: i32 = q\n    return z\n", "")]
    [InlineData("guard", "e15", ElementRanks + "    let s = P.init((n, 1))\n    return s.k\n", "")]
    [InlineData("guard", "e16", ElementRanks + "    let q = fq((if c => n else => v, 1))\n    let z: bool = q\n    return 0\n", "")]
    [InlineData("guard", "e17", ElementRanks + "    let q = fq(if c => (n, 1) else => (v, 2))\n    let z: bool = q\n    return 0\n", "")]
    [InlineData("G729", "e18", ElementRanks + "    let q = fq(if c => (v, 1) else => (v, 2))\n    let z: bool = q\n    return 0\n", "")]
    [InlineData("G729", "e19", ElementRanks + "    var x: i32 = 1\n    var y: i32 = 2\n    let q = fq(if c => (x, 1) else => (y, 2))\n    let z: bool = q\n    return 0\n", "")]
    [InlineData("guard", "e1n", ElementRanks + "    let q = fb((n, 1))\n    return 0\n", "AmbiguousBinding_Kd: let q = fb((n, 1))")]
    [InlineData("guard", "e4n", ElementRanks + "    let q = fo((n, n))\n    return 0\n", "AmbiguousBinding_Kd: let q = fo((n, n))")]
    [InlineData("guard", "e6n", ElementRanks + "    let q = cc([n, m])\n    return 0\n", "AmbiguousBinding_Kd: let q = cc([n, m])")]
    [InlineData("guard", "e7n", ElementRanks + "    let q = ff([2 of n])\n    return 0\n", "AmbiguousBinding_Kd: let q = ff([2 of n])")]
    [InlineData("guard", "e8n", ElementRanks + "    let q = fa([n, 1])\n    return 0\n", "AmbiguousBinding_Kd: let q = fa([n, 1])")]
    [InlineData("guard", "e10n", ElementRanks + "    let q = fy((n, inc))\n    return 0\n", "AmbiguousBinding_Kd: let q = fy((n, inc))")]
    [InlineData("guard", "e11n", ElementRanks + "    let q = fg2((n, 1))\n    return 0\n", "AmbiguousBinding_Kd: let q = fg2((n, 1))")]
    public void ALiteralArgumentRanksElementByElement(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // SPEC 10.5.1, 10.4 step 1, 10.2 (PLAN G746): a literal nested in a Tuple or array literal argument fits its element position as an
    // argument there: at ref/(...) a borrowed temporary, ranked Cross-Semantics against a by-value fit's Literal (n1n-n4n oppose or tie
    // where the other candidate was inapplicable; m1n-m4n in a method, an init, a generic and an array of arrays), never an exclusive
    // borrow (n5n, m5n); its own positions follow. After selection a borrowed element is a temporary at its position, as at the root
    // (PLAN G585: n5-n8, as a function value's call m1-m3). The m1-m6 guards keep their base verdict.
    [Theory]
    [InlineData("guard", "n1", NestedRanks + "    let q = fn4(((1, 2), 3))\n    let z: bool = q\n    return 0\n", "")]
    [InlineData("guard", "n2", NestedRanks + "    let q = fn4(((v, 1), 2))\n    let z: bool = q\n    return 0\n", "")]
    [InlineData("guard", "n3", NestedRanks + "    let q = fg(([n, m], 1))\n    let z: bool = q\n    return 0\n", "")]
    [InlineData("guard", "n4", NestedRanks + "    let q = fu2(((1, 2), 3))\n    let z: bool = q\n    return 0\n", "")]
    [InlineData("G585", "n5", NestedRanks + "    let q = fn5(((n, 1), 2))\n    return q\n", "")]
    [InlineData("G585", "n6", NestedRanks + "    let q = fa1(((1, 2), 3))\n    return q\n", "")]
    [InlineData("G585", "n7", NestedRanks + "    let q = fg1(((v, 1), 2))\n    return q\n", "")]
    [InlineData("G585", "n8", NestedRanks + "    let q = fc1(([2 of 7], 3))\n    return q\n", "")]
    [InlineData("G746", "n1n", NestedRanks + "    let q = fn3(((n, 1), 2))\n    return 0\n", "AmbiguousBinding_Kd: let q = fn3(((n, 1), 2))")]
    [InlineData("G746", "n2n", NestedRanks + "    let q = fh(([n, m], 1))\n    return 0\n", "AmbiguousBinding_Kd: let q = fh(([n, m], 1))")]
    [InlineData("G746", "n3n", NestedRanks + "    let q = fk((((n, 1), 2), 3))\n    return 0\n", "AmbiguousBinding_Kd: let q = fk((((n, 1), 2), 3))")]
    [InlineData("G746", "n4n", NestedRanks + "    let q = fl(([2 of n], 1))\n    return 0\n", "AmbiguousBinding_Kd: let q = fl(([2 of n], 1))")]
    [InlineData("guard", "n5n", NestedRanks + "    let q = fu1(((1, 2), 3))\n    return q\n", "NoApplicableOverload_Kd: let q = fu1(((1, 2), 3))")]
    [InlineData("guard", "m1", NestedTwins + "    let g = fv\n    return g(((1, 2), 3))\n", "")]
    [InlineData("guard", "m2", NestedTwins + "    let g = fw\n    return g(((n, 1), 2))\n", "")]
    [InlineData("guard", "m3", NestedTwins + "    let g = fw2\n    return g(((n, 1), 2), 3)\n", "")]
    [InlineData("guard", "m4", NestedTwins + "    let q = fgx(((v, v), 3))\n    let z: bool = q\n    return 0\n", "")]
    [InlineData("guard", "m5", NestedTwins + "    let q = fa3([[1, 2], [3, 4]])\n    let z: bool = q\n    return 0\n", "")]
    [InlineData("guard", "m6", NestedTwins + "    let q = fl3(([2 of n], 1))\n    let z: bool = q\n    return 0\n", "")]
    [InlineData("G746", "m1n", NestedTwins + "    let s = S2.init(1)\n    let q = s.m(((n, 1), 2))\n    return 0\n", "AmbiguousBinding_Kd: let q = s.m(((n, 1), 2))")]
    [InlineData("G746", "m2n", NestedTwins + "    let s = P3.init(((n, 1), 2))\n    return s.k\n", "AmbiguousBinding_Kd: let s = P3.init(((n, 1), 2))")]
    [InlineData("G746", "m3n", NestedTwins + "    let q = fgy(((n, 1), 2))\n    return 0\n", "AmbiguousBinding_Kd: let q = fgy(((n, 1), 2))")]
    [InlineData("G746", "m4n", NestedTwins + "    let q = fa4([[n, m], [n, m]])\n    return 0\n", "AmbiguousBinding_Kd: let q = fa4([[n, m], [n, m]])")]
    [InlineData("guard", "m5n", NestedTwins + "    let q = fgu(((1, 2), 3))\n    return q\n", "UnboundTypeArgument_Kd: let q = fgu(((1, 2), 3))")]
    public void ANestedLiteralFitsItsPositionAsAnArgument(string unit, string form, string program, string records)
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
