// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// PLAN G812: a pair pattern under a call's bindings and the sources of its binder, each row's records by code.
public partial class PairAnnotationBindingTest
{
    // SPEC 8.1.2: `s/U` under s's binding W forms the one Type ApplySemantics gives: a caller's abstract pair (its original or an
    // application, `t/i32 during q`) at a callee's `s/i32`, nested in `Option` or a structure method's construction, the pair's own
    // target as its original (s07v, v10), a recursive call (s07r). dev rejects every valid row (NoApplicableOverload_Kd); the invalid
    // twins keep their records or, matched at the formed nested layer, give their Origin record (t03, t03s); a caller's plain T at a
    // pair slot (x01-x03) raises nothing beyond base's records.
    [Theory]
    [InlineData("func put<t/U>(m: t/U, y: t/i32 during q, q: ref/i32) -> i32\n    t is owner or ref\n    return 0\nfunc call<t/U>(m: t/U, y: t/i32 during q, q: ref/i32) -> i32\n    t is owner or ref\n    return put(m@move, y@move, q)\n", "clean")] // s07b
    [InlineData("func put<t/U>(m: t/U, y: t/i32) -> i32\n    t is owner or ref\n    return 0\nfunc call<t/U>(m: t/U, y: t/i32) -> i32\n    t is owner or ref\n    return put(m@move, y@move)\n", "clean")] // s07e
    [InlineData("func put<t/U, V>(m: t/U, y: t/V) -> i32\n    t is owner or ref\n    return 0\nfunc call<t/U>(m: t/U, y: t/i32) -> i32\n    t is owner or ref\n    return put(m@move, y@move)\n", "clean")] // s07g
    [InlineData("func put<t/U>(m: t/U, y: t/i32 during q, q: ref/i32) -> i32\n    t is owner or ref\n    return 0\nfunc call<t/U>(m: t/U, y: t/i32 during q, q: ref/i32) -> i32\n    t is owner or ref\n    return put<t/U>(m@move, y@move, q)\n", "clean")] // s07h
    [InlineData("func put<t/U>(m: t/U, y: t/i32, q: ref/i32) -> i32\n    t is owner or ref\n    return 0\nfunc call<t/U>(m: t/U, y: t/i32, q: ref/i32) -> i32\n    t is owner or ref\n    return put(m@move, y@move, q)\n", "clean")] // s07i
    [InlineData("func put<t/U>(m: t/U, y: Option<t/i32 during q>, q: ref/i32) -> i32\n    t is owner or ref\n    return 0\nfunc call<t/U>(m: t/U, y: Option<t/i32 during q>, q: ref/i32) -> i32\n    t is owner or ref\n    return put(m@move, y@move, q)\n", "clean")] // s07n
    [InlineData("func put<s/W>(m: s/W, y: s/i32) -> i32\n    s is owner or ref\n    return 0\nfunc call<t/U>(m: t/U, y: t/i32) -> i32\n    t is owner or ref\n    return put(m@move, y@move)\n", "clean")] // s07p
    [InlineData("func put<s/W>(y: s/i32, m: s/W) -> i32\n    s is owner or ref\n    return 0\nfunc call<t/U>(m: t/U, y: t/i32) -> i32\n    t is owner or ref\n    return put(y@move, m@move)\n", "clean")] // s07q
    [InlineData("func put<t/U>(m: t/U, y: t/i32) -> i32\n    t is owner or ref\n    return put(m@move, y@move)\n", "clean")] // s07r
    [InlineData("func put<t/U, V>(m: t/U, y: t/V) -> i32\n    t is owner or ref\n    return 0\nfunc call<t/U>(m: t/U, y: t/U) -> i32\n    t is owner or ref\n    return put(m@move, y@move)\n", "clean")] // s07v
    [InlineData("func put<s/W>(m: s/W, y: s/i64) -> i32\n    s is owner or ref\n    return 0\nfunc call<t/U>(m: t/i32, y: t/i64) -> i32\n    t is owner or ref\n    return put(m@move, y@move)\n", "clean")] // v03
    [InlineData("func put<t/U, V>(m: t/U, y: t/V) -> i32\n    t is owner or ref\n    return 0\nfunc call<t/U>(m: t/U, y: t/U) -> i32\n    t is owner or ref\n    return put<t/U, U>(m@move, y@move)\n", "clean")] // v10
    [InlineData("struct P<s/T, r/V>\n    s is owner or ref\n    r is owner or ref\n    public init(x: s/i32, y: r/i32) => ()\n    public func swap(self: ref/Self, x: s/i32, y: r/i32) -> i32\n        let q = P<r/V, s/T>.init(y@move, x@move)\n        return 0\n", "clean")] // v13
    [InlineData("struct Q<s/T>\n    s is owner or ref\n    public init(x: s/T) => ()\n    public func f<r/V>(self: ref/Self, m: r/V, y: r/i32) -> i32\n        r is owner or ref\n        return 0\n    public func g(self: ref/Self, m: s/T, y: s/i32) -> i32\n        return self.f(m@move, y@move)\n", "clean")] // w03
    [InlineData("func f<s/T, r/V>(m: s/T, n: r/V, x: s/V, y: r/i64, w: r/V) -> i32\n    s is owner or ref\n    r is owner or ref\n    let k: i64 = 0\n    let j: i64 = 0\n    let i: i64 = 0\n    return f(n@move, k@move, y@move, j@move, i@move)\n", "clean")] // w08
    [InlineData("func pick<s/W>(m: s/W, y: Option<s/i32 during a>) -> Option<s/i32 during a>\n    s is owner or ref\n    return y@move\nfunc call<t/U>(m: t/U, y: Option<t/i32 during q>, q: ref/i32) -> Option<t/i32 during q>\n    t is owner or ref\n    return pick(m@move, y@move)\n", "clean")] // w09
    [InlineData("func pick<s/W>(m: s/W, y: s/i32 during a) -> s/i32 during a\n    s is owner or ref\n    return y@move\nfunc call<t/U>(m: t/U, y: t/i32 during q, q: ref/i32) -> t/i32 during q\n    t is owner or ref\n    return pick(m@move, y@move)\n", "clean")] // w09s
    [InlineData("func pick<s/W>(m: s/W, y: Option<s/i32 during a>) -> Option<s/i32 during a>\n    s is owner or ref\n    return y@move\nfunc call<t/U>(m: t/U, y: Option<t/i32 during p>, p: ref/i32, q: ref/i32) -> Option<t/i32 during q>\n    t is owner or ref\n    return pick(m@move, y@move)\n", "1*UnprovenOriginRelation_Kd")] // t03
    [InlineData("func pick<s/W>(m: s/W, y: s/i32 during a) -> s/i32 during a\n    s is owner or ref\n    return y@move\nfunc call<t/U>(m: t/U, y: t/i32 during p, p: ref/i32, q: ref/i32) -> t/i32 during q\n    t is owner or ref\n    return pick(m@move, y@move)\n", "1*UnprovenOriginRelation_Kd")] // t03s
    [InlineData("func put<t/U>(m: t/U, y: t/i32) -> i32\n    t is owner or ref\n    return 0\nfunc call<t/U, r/V>(m: t/U, y: r/i32) -> i32\n    t is owner or ref\n    r is owner or ref\n    return put(m@move, y@move)\n", "1*NoApplicableOverload_Kd")] // s07x
    [InlineData("func put<t/U>(m: t/U, y: t/i32) -> i32\n    t is owner or ref\n    return 0\nfunc call<t/U>(m: t/U, y: t/i64) -> i32\n    t is owner or ref\n    return put(m@move, y@move)\n", "1*NoApplicableOverload_Kd")] // s07y
    [InlineData("func put<s/W, V>(m: s/W, y: s/V) -> i32\n    s is owner or ref\n    return 0\nfunc call<T>(m: T, y: T) -> i32\n    return put<T, T>(m@move, y@move)\n", "1*NoApplicableOverload_Kd")] // x01
    [InlineData("func put<s/W, V>(m: s/W, y: s/V) -> i32\n    return 0\nfunc call<T>(m: T, y: T) -> i32\n    return put<T, T>(m@move, y@move)\n", "1*NoApplicableOverload_Kd 1*UnprovenConstraint_Kd")] // x02
    [InlineData("func f<T, s/U>(m: s/U, x: s/T, k: T) -> i32\n    return 0\nfunc g<T>(k: T, j: T, i: T) -> i32\n    return f<T, T>(k@move, j@move, i@move)\n", "1*NoApplicableOverload_Kd 1*UnprovenConstraint_Kd")] // x03
    public void APairPatternFormsUnderItsBinding(string source, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(source));

    // SPEC 8.1.2, 10.8: formation continues only into an owner binding's target, so a swapped, rotated or mutual recursion forms each
    // layer once and ends, nested in `Option` too (t01d; an earlier prototype did not end on v01 and v12).
    [Theory(Timeout = 60000)]
    [InlineData("func g<s/T, r/V>(m: s/T, n: r/V, x: s/i32, y: r/i32) -> i32\n    s is owner or ref\n    r is owner or ref\n    return g(n@move, m@move, y@move, x@move)\n", "clean")] // v01
    [InlineData("func f<s/T, r/V>(m: s/T, n: r/V, x: s/i32, y: r/i32) -> i32\n    s is owner or ref\n    r is owner or ref\n    let k: i64 = 0\n    let j: i32 = 0\n    return f(n@move, k@move, y@move, j@move)\n", "clean")] // v02
    [InlineData("func g<s/T, r/V, u/X>(m: s/T, n: r/V, o: u/X, x: s/i32, y: r/i32, z: u/i32) -> i32\n    s is owner or ref\n    r is owner or ref\n    u is owner or ref\n    return g(n@move, o@move, m@move, y@move, z@move, x@move)\n", "clean")] // w01
    [InlineData("func a<s/T, r/V>(m: s/T, n: r/V, x: s/i32, y: r/i32) -> i32\n    s is owner or ref\n    r is owner or ref\n    return b(n@move, m@move, y@move, x@move)\nfunc b<s/T, r/V>(m: s/T, n: r/V, x: s/i32, y: r/i32) -> i32\n    s is owner or ref\n    r is owner or ref\n    return a(n@move, m@move, y@move, x@move)\n", "clean")] // w02
    [InlineData("func g<s/T, r/V>(m: s/T, n: r/V, x: Option<s/i32 during q>, y: Option<r/i32 during q>, q: ref/i32) -> i32\n    s is owner or ref\n    r is owner or ref\n    return g(n@move, m@move, y@move, x@move, q)\n", "clean")] // t01d
    [InlineData("func g<s/T, r/V>(m: s/T, n: r/V, x: s/i32, y: r/i32) -> i32\n    s is owner or ref\n    r is owner or ref\n    return g(n@move, m@move, x@move, y@move)\n", "1*NoApplicableOverload_Kd")] // v12
    public async Task ARecursiveCallFormsEachLayerOnce(string source, string records)
        => Assert.Equal(records, await Task.Run(() => DiagnosticCorpus.RecordProfile(source), TestContext.Current.CancellationToken));

    // SPEC 10.8 (the owner's O-243 (a)): every source of a pair binder fixes it, an application included, in any order, and the expected
    // result fills only a binder no source fixes (w06: x makes s ref, so the expected `i64` fills nothing; e1: an agreeing `ref/i64`
    // fills W), beside another candidate too (c2); an annotated occurrence binds only its slot, never W's outer Origin, in either order
    // (v11, v11s). dev rejects every valid row and gives w06 UnboundTypeArgument_Kd.
    [Theory]
    [InlineData("struct Two<A, B>\n    public let a: A\n    public let b: B\n\n    public init(a: A, b: B)\n        self.a = a@move\n        self.b = b@move\n\nfunc f<s/T, U>(x: Two<s/U during q, s/T>, q: ref/i32) -> i32\n    s is owner or ref\n    return 0\n\npublic func main()\n    let z: i32 = 0\n    let p = Two<i32, i64>.init(1, 2)\n    let r = f(p@move, z@ref)\n", "clean")] // v06
    [InlineData("func stop() -> Never => $abort(\"stop\")\n\nfunc mk<s/T, U>(x: s/U) -> s/T\n    s is owner or ref\n    return stop()\n\npublic func main()\n    let a: i32 = 1\n    let r: i64 = mk(a@move)\n", "clean")] // v07
    [InlineData("struct Two<A, B>\n    public let a: A\n    public let b: B\n\n    public init(a: A, b: B)\n        self.a = a@move\n        self.b = b@move\n\nfunc f<s/T, r/V, U, X>(a: Two<s/U during q, r/V>, b: Two<r/X during q, s/T>, q: ref/i32) -> i32\n    s is owner or ref\n    r is owner or ref\n    return 0\n\npublic func main()\n    let z: i32 = 0\n    let a = Two<i32, i64>.init(1, 2)\n    let b = Two<u8, u16>.init(3, 4)\n    let r = f(a@move, b@move, z@ref)\n", "clean")] // w04
    [InlineData("struct Two<A, B>\n    public let a: A\n    public let b: B\n\n    public init(a: A, b: B)\n        self.a = a@move\n        self.b = b@move\n\nfunc f<s/T, r/V, U, X>(b: Two<r/X during q, s/T>, a: Two<s/U during q, r/V>, q: ref/i32) -> i32\n    s is owner or ref\n    r is owner or ref\n    return 0\n\npublic func main()\n    let z: i32 = 0\n    let a = Two<i32, i64>.init(1, 2)\n    let b = Two<u8, u16>.init(3, 4)\n    let r = f(b@move, a@move, z@ref)\n", "clean")] // w04s
    [InlineData("struct Two<A, B>\n    public let a: A\n    public let b: B\n\n    public init(a: A, b: B)\n        self.a = a@move\n        self.b = b@move\n\nfunc stop() -> Never => $abort(\"stop\")\n\nfunc f<s/T, r/V, U, X>(x: s/U, y: Two<s/T, r/X during q>, q: ref/i32) -> r/V\n    s is owner or ref\n    r is owner or ref\n    return stop()\n\npublic func main()\n    let z: i32 = 0\n    let a: i32 = 1\n    let p = Two<u16, u8>.init(2, 3)\n    let res: i64 = f(a@move, p@move, z@ref)\n", "clean")] // w05
    [InlineData("struct Two<A, B>\n    public let a: A\n    public let b: B\n\n    public init(a: A, b: B)\n        self.a = a@move\n        self.b = b@move\n\nfunc stop() -> Never => $abort(\"stop\")\n\nfunc f<s/T, r/V, U, X>(y: Two<s/T, r/X during q>, x: s/U, q: ref/i32) -> r/V\n    s is owner or ref\n    r is owner or ref\n    return stop()\n\npublic func main()\n    let z: i32 = 0\n    let a: i32 = 1\n    let p = Two<u16, u8>.init(2, 3)\n    let res: i64 = f(p@move, a@move, z@ref)\n", "clean")] // w05s
    [InlineData("func h<s/W>(b: s/W during a, c: s/W) -> s/W\n    s is owner or ref\n    return c@move\nfunc call(p: ref/i32, q: ref/i32, b: ref/i32 during p, c: ref/i32 during q) -> ref/i32 during q\n    return h(b, c)\n", "clean")] // v11
    [InlineData("func h<s/W>(c: s/W, b: s/W during a) -> s/W\n    s is owner or ref\n    return c@move\nfunc call(p: ref/i32, q: ref/i32, b: ref/i32 during p, c: ref/i32 during q) -> ref/i32 during q\n    return h(c, b)\n", "clean")] // v11s
    [InlineData("func stop() -> Never => $abort(\"stop\")\n\nfunc mk<s/T, U>(x: s/U) -> s/T\n    s is owner or ref\n    return stop()\n\nfunc use(a: ref/i32) -> i32\n    let r: ref/i64 = mk(a)\n    return 0\n", "clean")] // e1
    [InlineData("struct Bx<A>\n    public let a: A\n\n    public init(a: A)\n        self.a = a@move\n\nstruct Cy<A>\n    public let a: A\n\n    public init(a: A)\n        self.a = a@move\n\nfunc mk<s/T, U>(x: s/Bx<U>, y: s/T) -> i32\n    s is owner or ref\n    return 0\nfunc mk<r/X, V>(x: r/Cy<V>, y: r/X) -> i32\n    r is owner or ref\n    return 0\nfunc use() -> i32\n    let b = Bx<i32>.init(1)\n    return mk(b@move, 1)\n", "clean")] // c2
    [InlineData("func stop() -> Never => $abort(\"stop\")\n\nfunc mk<s/T, U>(x: s/U) -> s/T\n    s is owner or ref\n    return stop()\n\nfunc use(a: ref/i32) -> i64\n    let r: i64 = mk(a)\n    return r@move\n", "1*NoApplicableOverload_Kd")] // w06
    [InlineData("func put<s/W, V>(m: s/W, y: s/V, z: s/W) -> i32\n    s is owner or ref\n    return 0\npublic func main()\n    let a: i32 = 1\n    let b: i64 = 2\n    let c: i32 = 3\n    let r = put(a@move, b@move, c@ref)\n", "1*NoApplicableOverload_Kd")] // x04
    public void EverySourceOfAPairBinderFixesIt(string source, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(source));
}
