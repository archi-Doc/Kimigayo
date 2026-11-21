// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// J2's fix for the Middle's NO CONSENT on C-373 (PLAN G936; design `J2\design-fix.md` revision 3; the owner's O-358 (a), O-362 (a),
// O-368 (a); SPEC 10.1, 10.4, 10.8, 10.8.1, 15.3.3, 15.3.6, 15.6.1, 15.6.5, 15.8.2; DECISIONS BU-104). A waited variable's solution
// is one value for every reader, `(E and region)`: the evidence candidate met with the call's region. Each row is labelled from SPEC
// before any build's output; its id is the design's.
public partial class CallOriginRelationTest
{
    // Valid programs: accepted. Rejected by J2's head 3bdcde1ab (a regression against G913) or by G913 too (an acceptance move
    // toward the label), as the design's coverage table names each.
    [Theory]
    // A Type's declared relation over the variable (D3a): at a call's input, in a slot's solution, at a result, through a later use, a pattern, a function reference, a conformance or Constraint subject.
    // t7cs
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc st() -> ref/i32 during static => $abort(\"no\")\nfunc g(x: Option<T during q>, y: ref/i32 during q) -> i32\n    return 0\nfunc k(p: ref/i32) -> i32\n    return g(.None, st())\n")]
    // t7c0
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc g0(x: Option<T during q>) -> i32\n    return 0\nfunc k(p: ref/i32) -> i32\n    return g0(.None)\n")]
    // tt3v
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc h<U>(x: Option<ref/U during c>, y: ref/U during c) -> i32\n    return 0\nfunc k(p: ref/i32, t: T during p) -> i32\n    origin p outlives static\n    return h(.None, t@ref)\n")]
    // rt2v
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc g2(x: Option<ref/i32 during q>, y: ref/i32 during q) -> Option<T during q>\n    origin q outlives static\n    return .None\nfunc k(p: ref/i32) -> i32\n    origin p outlives static\n    let t = g2(.None, p)\n    return 0\n")]
    // use1v
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc mk(x: Option<ref/i32 during q>, y: ref/i32 during q) -> T during q\n    origin q outlives static\n    return T.init(x: y)\nfunc k(p: ref/i32) -> i32\n    origin p outlives static\n    let t = mk(.None, p)\n    let z = t.x\n    return 0\n")]
    // rk1
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc gr(x: Option<ref/i32 during q>, y: ref/i32 during q) -> Option<T during q>\n    origin q outlives static\n    return .None\nfunc k(p: ref/i32) -> i32\n    origin p outlives static\n    return match gr(.None, p)\n        .Some(_) => 0\n        .None => 1\n")]
    // dc1v
    [InlineData("func g(x: Option<ref/i32 during q>, y: ref/i32 during q) -> i32\n    origin q outlives static\n    return 0\nfunc k(p: ref/i32) -> i32\n    origin p outlives static\n    return g(.None, p)\n")]
    // fr1v
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc g0(x: Option<T during q>) -> i32\n    return 0\nfunc k() -> i32\n    let f = g0\n    return f(.None)\n")]
    // fr2v
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc g(x: Option<T during q>, y: ref/i32 during q) -> i32\n    return 0\nfunc k(p: ref/i32) -> i32\n    origin p outlives static\n    let f = g\n    return f(.None, p)\n")]
    // cs1v
    [InlineData("contract Mk\n    associate Tag(a) is i32\nstruct T {a}\n    Self is Mk\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc mk(x: Option<ref/i32 during q>, y: ref/i32 during q) -> T during q\n    origin q outlives static\n    return T.init(x: y)\nfunc need<U>(x: ref/U during c) -> i32\n    U is Mk\n    return 0\nfunc k(p: ref/i32) -> i32\n    origin p outlives static\n    let t = mk(.None, p)\n    return need(t@ref)\n")]
    // rf7
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc g(x: Option<T during q>, y: ref/i32 during q) -> i32\n    return 0\nfunc k(p: ref/i32) -> i32\n    origin p outlives static\n    var a = p\n    return g(.None, a)\n")]
    // rf13
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc g0(x: Option<T during q>, y: Option<ref/i32 during q>) -> i32\n    return 0\nfunc k(p: ref/i32) -> i32\n    origin p outlives static\n    return g0(.None, .Some(p))\n")]
    // bx2v
    [InlineData("struct Bx<T>\n    T is Owned\n    public let item: T\n    public init(item: T) => self.item = item@move\nfunc keepB<T>(x: Option<Bx<T>>, y: T) -> i32\n    T is Owned\n    return 0\nfunc one() -> ref/i32 during static => $abort(\"no\")\nfunc use() -> i32\n    return keepB(.None, one())\n")]
    // bxk1v
    [InlineData("struct Bx<T>\n    T is Owned\n    public let item: T\n    public init(item: T) => self.item = item@move\nfunc keepK(x: Option<Bx<ref/i32 during k>>, y: ref/i32 during k) -> i32\n    origin k outlives static\n    return 0\nfunc one() -> ref/i32 during static => $abort(\"no\")\nfunc use() -> i32\n    return keepK(.None, one())\n")]
    // ow1v
    [InlineData("func keepO<T>(x: Option<T>, y: T) -> i32\n    T is Owned\n    return 0\nfunc use(p: ref/i32) -> i32\n    origin p outlives static\n    return keepO(.Some(p), p)\n")]
    // A construction: one value for the fixed-binding re-check (D1, D2), the owner Type's declared relation as the call's row (D3b) or the proof's, the owner's Constraints (D4).
    // gc2c
    [InlineData("struct Gc<T>\n    public let t: T\n    public let o: Option<T>\n    public init(x: T, o: Option<T>)\n        self.t = x@move\n        self.o = o@move\nfunc g(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let f = func [a, b] () -> ()\n        let x = Gc.init(b, .Some(a))\n    f()\n    return 0\n")]
    // gc2e
    [InlineData("struct Gc<T>\n    public let t: T\n    public let o: Option<T>\n    public init(x: T, o: Option<T>)\n        self.t = x@move\n        self.o = o@move\nfunc g(q: ref/i32) -> i32\n    let s: Option<ref/i32> = .Some(q)\n    let x = Gc.init(q, s)\n    return 0\n")]
    // gc3v
    [InlineData("struct Gc<T>\n    public let t: T\n    public let o: Option<T>\n    public init(x: T, o: Option<T>)\n        self.t = x@move\n        self.o = o@move\nfunc g(q: ref/i32) -> i32\n    let x = Gc.init(q, .None)\n    return 0\n")]
    // gc4v
    [InlineData("struct Gc<T>\n    public let t: T\n    public let o: Option<T>\n    public init(x: T, o: Option<T>)\n        self.t = x@move\n        self.o = o@move\n    public init(x: T, o: Option<T>, n: i32)\n        self.t = x@move\n        self.o = o@move\nfunc g(q: ref/i32) -> i32\n    let x = Gc.init(q, .Some(q))\n    return 0\n")]
    // gcs1v
    [InlineData("struct Gc<T>\n    public let t: T\n    public let o: Option<T>\n    public init(x: T, o: Option<T>)\n        self.t = x@move\n        self.o = o@move\nfunc one() -> ref/i32 during static => $abort(\"no\")\nfunc g() -> i32\n    let x = Gc.init(one(), .Some(one()))\n    return 0\n")]
    // rk5
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nstruct Gc<U>\n    public let t: U\n    public let o: Option<U>\n    public init(x: U, o: Option<U>)\n        self.t = x@move\n        self.o = o@move\nfunc k(p: ref/i32, t1: T during p, t2: T during p) -> i32\n    let g = Gc.init(t1@ref, .Some(t2@ref))\n    return 0\n")]
    // rf9
    [InlineData("struct W {a}\n    public let x: ref/i32 during a\n    public let o: Option<ref/i32 during a>\n    public init(x: ref/i32 during a, o: Option<ref/i32 during a>)\n        self.x = x\n        self.o = o@move\nfunc st() -> ref/i32 during static => $abort(\"no\")\nfunc k() -> i32\n    let w = W.init(st(), .Some(st()))\n    return 0\n")]
    // rf10
    [InlineData("struct W {a}\n    public let x: ref/i32 during a\n    public let o: Option<ref/i32 during a>\n    public init(x: ref/i32 during a, o: Option<ref/i32 during a>)\n        self.x = x\n        self.o = o@move\nfunc st() -> ref/i32 during static => $abort(\"no\")\nfunc k(q: ref/i32) -> i32\n    let w = W.init(q, .Some(q))\n    return 0\n")]
    // gco1v
    [InlineData("struct Go<T>\n    T is Owned\n    public let t: T\n    public let o: Option<T>\n    public init(x: T, o: Option<T>)\n        self.t = x@move\n        self.o = o@move\nfunc one() -> ref/i32 during static => $abort(\"no\")\nfunc g() -> i32\n    let x = Go.init(one(), .Some(one()))\n    return 0\n")]
    // ap1v
    [InlineData("struct Ap<V, F>\n    F is Callable<() -> V>\n    public let f: F\n    public let y: V\n    public let o: Option<V>\n    public init(f: F, y: V, x: Option<V>)\n        self.f = f@move\n        self.y = y@move\n        self.o = x@move\nfunc use(q: ref/i32) -> i32\n    let a = Ap.init(func [q] () => q, q, .Some(q))\n    return 0\n")]
    // rc1v
    [InlineData("struct T3 {a}\n    origin a outlives static\n    public let o: Option<ref/i32 during a>\n    public init(o: Option<ref/i32 during a>) => self.o = o\n\nfunc st() -> ref/i32 during static => $abort(\"no\")\nfunc k() -> i32\n    let t = T3.init(o: .Some(st()))\n    return 0\n")]
    // rc2v
    [InlineData("struct T3 {a}\n    origin a outlives static\n    public let o: Option<ref/i32 during a>\n    public init(o: Option<ref/i32 during a>) => self.o = o\n\nfunc st() -> ref/i32 during static => $abort(\"no\")\nfunc k(p: ref/i32) -> i32\n    origin p outlives static\n    let t = T3.init(o: .Some(p))\n    return 0\n")]
    // rc3v
    [InlineData("struct T4 {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public let o: Option<ref/i32 during a>\n    public init(x: ref/i32 during a, o: Option<ref/i32 during a>)\n        self.x = x\n        self.o = o\n\nfunc st() -> ref/i32 during static => $abort(\"no\")\nfunc k() -> i32\n    let t = T4.init(x: st(), o: .Some(st()))\n    return 0\n")]
    // ec1v
    [InlineData("enum E2 {a}\n    origin a outlives static\n    One(ref/i32 during a)\n\nfunc st() -> ref/i32 during static => $abort(\"no\")\nfunc k() -> i32\n    let e = E2.One(st())\n    return 0\n")]
    // ec2v
    [InlineData("enum E3 {a}\n    origin a outlives static\n    Two(Option<ref/i32 during a>)\n\nfunc st() -> ref/i32 during static => $abort(\"no\")\nfunc k() -> i32\n    let e = E3.Two(.Some(st()))\n    return 0\n")]
    // hole6_ok
    [InlineData("func mk(p: Option<ref/(ref/i32 during a) during b>, u: Option<ref/(ref/i32 during e) during a>, w: Option<ref/(ref/i32 during d) during b>, y: ref/i32 during b, z: ref/i32 during e, x: ref/i32 during (a and d), f: (ref/i32 during a, bool) -> i32) -> i32 => 0\nfunc k(c: bool, s: ref/i32 during static) -> i32\n    let short = 2\n    return mk(if c => .None else => .None, if c => .None else => .None, if c => .None else => .None, s, s, s, func [s] (v, c2) => mk(if c2 => .None else => .None, if c2 => .None else => .None, if c2 => .None else => .None, s, s, v, func [] (v2, c3) => 0))\n")]
    // The solve of the callee's other variables (D1's Origin half): a clause that bounds another variable through the waited one (reading RF7), an invariant end, an equality, an associate.
    // as1v
    [InlineData("contract C\n    associate Item(a, b) is ref/(ref/i32 during a) during b\nstruct S\n    Self is C\nfunc f(x: Option<ref/i32 during q>, y: ref/i32 during q, z: ref/i32 during c) -> S.Item(q, c)\n    origin q outlives c\n    $abort(\"unused\")\nfunc k(p: ref/i32, d: ref/i32) -> i32\n    origin p outlives d\n    let r = f(.None, p, d)\n    return 0\n")]
    // as1i
    [InlineData("contract C\n    associate Item(a, b) is ref/(ref/i32 during a) during b\nstruct S\n    Self is C\nfunc f(x: Option<ref/i32 during q>, y: ref/i32 during q, z: ref/i32 during c) -> S.Item(q, c)\n    origin q outlives c\n    $abort(\"unused\")\nfunc k(p: ref/i32, d: ref/i32) -> i32\n    let r = f(.None, p, d)\n    return 0\n")]
    // as2v
    [InlineData("func f2(x: Option<ref/i32 during q>, y: ref/i32 during q, z: uniq/(ref/i32 during c)) -> i32\n    origin q outlives c\n    return 0\nfunc k(p: ref/i32, d: ref/i32, ud: uniq/(ref/i32 during d)) -> i32\n    origin p outlives d\n    return f2(.None, p, ud)\n")]
    // as3v
    [InlineData("contract C\n    associate Item(a, b)\n        origin a outlives b\nstruct S\n    Self is C\n    associate C.Item(a, b) is i32\nfunc f3(x: Option<ref/i32 during q>, y: ref/i32 during q, z: uniq/(ref/i32 during c)) -> S.Item(q, c)\n    origin q outlives c\n    return 42\nfunc k(p: ref/i32, d: ref/i32, ud: uniq/(ref/i32 during d)) -> i32\n    origin p outlives d\n    let r = f3(.None, p, ud)\n    return 0\n")]
    // t5v
    [InlineData("func f5(x: Option<ref/i32 during q>, y: ref/i32 during q, z: ref/i32 during c) -> i32\n    origin q outlives c\n    return 0\nfunc k(w: ref/i32, p: ref/i32, d: ref/i32) -> i32\n    return f5(.Some(w), p, d)\n")]
    // t6v
    [InlineData("func f2(x: Option<ref/i32 during q>, y: ref/i32 during q, z: uniq/(ref/i32 during c)) -> i32\n    origin q outlives c\n    return 0\nfunc k(w: ref/i32, p: ref/i32, d: ref/i32, ud: uniq/(ref/i32 during d)) -> i32\n    origin p outlives d\n    origin w outlives d\n    return f2(.Some(w), p, ud)\n")]
    // t7v
    [InlineData("func f7(x: Option<ref/i32 during q>, y: ref/i32 during q, z: ref/i32 during c) -> ref/i32 during c\n    origin q outlives c\n    $abort(\"no\")\nfunc k(w: ref/i32, p: ref/i32, d: ref/i32) -> ref/i32 during (d and p and w)\n    return f7(.Some(w), p, d)\n")]
    // eq1v
    [InlineData("func f6(x: Option<ref/i32 during q>, y: ref/i32 during q, z: ref/i32 during c) -> i32\n    origin q == c\n    return 0\nfunc k(w: ref/i32, p: ref/i32, d: ref/i32) -> i32\n    return f6(.None, p, d)\n")]
    // eq2v
    [InlineData("func f6(x: Option<ref/i32 during q>, y: ref/i32 during q, z: ref/i32 during c) -> i32\n    origin q == c\n    return 0\nfunc k(w: ref/i32, p: ref/i32, d: ref/i32) -> i32\n    return f6(.Some(w), p, d)\n")]
    // The region keeps its recorded bounds (D1c): Owned over the result of either half, through a local and in place.
    // cs2v
    [InlineData("func one() -> ref/i32 during static => $abort(\"no\")\nfunc keep<V>(x: Option<V>, y: V) -> V => y@move\nfunc needO<U>(x: U) -> i32\n    U is Owned\n    return 0\nfunc k() -> i32\n    let r = keep(.Some(one()), one())\n    return needO(r)\n")]
    // cs3v
    [InlineData("func keep<V>(x: Option<V>, y: V) -> V => y@move\nfunc needO<U>(x: U) -> i32\n    U is Owned\n    return 0\nfunc k(p: ref/i32) -> i32\n    origin p outlives static\n    let r = keep(.Some(p), p)\n    return needO(r)\n")]
    // cs4v
    [InlineData("func keep<V>(x: Option<V>, y: V) -> V => y@move\nfunc needO<U>(x: U) -> i32\n    U is Owned\n    return 0\nfunc k(p: ref/i32) -> i32\n    origin p outlives static\n    let r = keep(.None, p)\n    return needO(r)\n")]
    // rq1
    [InlineData("func keepk(x: Option<ref/(ref/i32 during k) during c>, y: ref/(ref/i32 during k) during c) -> ref/i32 during k => $abort(\"no\")\nfunc needO<U>(x: U) -> i32\n    U is Owned\n    return 0\nfunc use(p: ref/i32) -> i32\n    origin p outlives static\n    let r = keepk(.Some(p@ref), p@ref)\n    return needO(r)\n")]
    // rq2
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc needO<U>(x: U) -> i32\n    U is Owned\n    return 0\nfunc use(p: ref/i32) -> i32\n    origin p outlives static\n    let r = keep(.Some(p@ref), p@ref)\n    return needO(r)\n")]
    // r2v
    [InlineData("func keep<V>(x: Option<V>, y: V) -> V => y@move\nfunc needO<U>(x: U) -> i32\n    U is Owned\n    return 0\nfunc pin(x: uniq/(ref/i32 during a), y: ref/i32 during a) -> i32 => 0\nfunc use(p: ref/i32, q: ref/i32) -> i32\n    origin p outlives static\n    origin q outlives static\n    var r = keep(.Some(p), p)\n    let n = pin(r@uniq, q)\n    return needO(r)\n")]
    // rr1v
    [InlineData("func keep<T>(x: Option<T>, y: T) -> T => $abort(\"no\")\nfunc set<U>(x: uniq/U, v: U) -> i32\n    U is Owned\n    return 0\nfunc use(p: ref/i32, q: ref/i32) -> i32\n    origin p outlives static\n    origin q outlives static\n    return set(keep(.Some(p), p)@uniq, q)\n")]
    // A Closure that captures the result (C2): the evidence bound's own record beside the located limit.
    // cap11
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let r = keep(.Some(a@ref), b@ref)\n    let g = func [r] () -> i32 => r@follow\n    return g()\n")]
    // cap14
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let r = keep(.Some(a@ref), b@ref)\n    let g = func [r] () -> ref/i32 during r\n        return r\n    return g()@follow\n")]
    // cap14d
    [InlineData("func use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let r = b\n    let g = func [r] () -> ref/i32 during r\n        return r\n    return g()@follow\n")]
    // cap15d
    [InlineData("func use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let r = b\n    let g = func [r] () -> ref/i32 during b\n        return r\n    return g()@follow\n")]
    // Two overloads (10.1 Unknown applicability, 10.4): the selected candidate shows at `let z: i32 = r`.
    // tr1v
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc g(x: Option<T during q>, y: ref/i32 during q) -> i32 => 0\nfunc g(x: Option<T during q>, y: ref/i32 during q, n: i32 = 0) -> bool => true\nfunc k(p: ref/i32) -> i32\n    origin p outlives static\n    let r = g(.None, p)\n    let z: i32 = r\n    return z\n")]
    // tr2v
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc h(x: Option<ref/T during c>, y: ref/T during c) -> i32 => 0\nfunc h<U>(x: Option<ref/U during c>, y: ref/U during c) -> bool => true\nfunc k(p: ref/i32, t: T during p) -> i32\n    origin p outlives static\n    let r = h(.None, t@ref)\n    let z: i32 = r\n    return z\n")]
    // tr3v
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc h<U>(x: Option<ref/U during c>, y: ref/U during c) -> i32 => 0\nfunc h<U>(x: Option<ref/U during c>, y: ref/U during c, n: i32 = 0) -> bool => true\nfunc k(p: ref/i32, t: T during p) -> i32\n    origin p outlives static\n    let r = h(.None, t@ref)\n    let z: i32 = r\n    return z\n")]
    // tr5v
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc g0(x: Option<T during q>) -> i32 => 0\nfunc g0(x: Option<T during q>, n: i32 = 0) -> bool => true\nfunc k() -> i32\n    let r = g0(.None)\n    let z: i32 = r\n    return z\n")]
    // A call's result Type's declared relation over the call's region (revision 3): the result row at the call, which every derived place (a later call, a pattern, a field, a returned value, a default) rests on.
    // c3e1v
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc st() -> ref/i32 during static => $abort(\"no\")\nfunc mk(x: Option<ref/i32 during q>, y: ref/i32 during q) -> T during q => $abort(\"no\")\nfunc k() -> i32\n    let t = mk(.None, st())\n    return 0\n")]
    // c3e3v
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc mk(x: Option<ref/i32 during q>, y: ref/i32 during q) -> T during q => $abort(\"no\")\nfunc k(p: ref/i32) -> i32\n    origin p outlives static\n    let t = mk(.None, p)\n    return 0\n")]
    // c3b3v
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc mk3(x: Option<ref/i32 during q>) -> T during q => $abort(\"no\")\nfunc k(p: ref/i32) -> i32\n    origin p outlives static\n    let t = mk3(.Some(p))\n    return 0\n")]
    // d4e3v
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc gr(x: Option<ref/i32 during q>, y: ref/i32 during q) -> Option<T during q> => $abort(\"no\")\nfunc k(p: ref/i32) -> i32\n    origin p outlives static\n    return match gr(.None, p)\n        .Some(_) => 0\n        .None => 1\n")]
    // ff2bv
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nstruct S2 {b}\n    origin b outlives static\n    public let t: Option<T during b>\n    public init(t: Option<T during b>) => self.t = t@move\nfunc mks(x: Option<ref/i32 during q>, y: ref/i32 during q) -> S2 during q => $abort(\"no\")\nfunc st() -> ref/i32 during static => $abort(\"no\")\nfunc k() -> i32\n    let s = mks(.None, st())\n    let o = s.t@ref\n    return 0\n")]
    // da1v
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc gd(y: ref/i32 during q, x: Option<T during q> = .None) -> i32\n    return 0\nfunc st() -> ref/i32 during static => $abort(\"no\")\nfunc k() -> i32\n    return gd(st())\n")]
    // PLAN G492-a (ii) Revision 5 (design-ii.md §13.18.9-11; labels rev5\labels-b1.md, labels-var.md, r2's): in a nested body an enclosing
    // local region at a longer end reads as its solved value, a `let` region's value or a `var` region's meet of its recorded bounds (the
    // owner's O-422 (a)): two and three bodies down (q1-q3), a `var` capture over an enclosing `let` (q2v, q2w, q2wl), pk7vv, cap15dvar
    // and f4v (valid programs dev rejects), and definition cycles (s1v-s3v).
    // q1
    [InlineData("func f(p: ref/i64) -> i64\n    let a = p\n    let g = func [a] () -> i64\n        let b = a\n        let h = func [b] () -> ref/i64 during p => b\n        return 0\n    return 0\n")]
    // q2
    [InlineData("func f() -> i32\n    let n = 1\n    var r = n@ref\n    let a = r\n    let g = func [a] () -> i32\n        let b = a\n        let h = func [b] () -> ref/i32 during r => b\n        return 0\n    return 0\n")]
    // q3
    [InlineData("func f() -> i32\n    let n = 1\n    var r = n@ref\n    let a = r\n    let g = func [a] () -> i32\n        let h = func [a] () -> ref/i32 during r => a\n        return 0\n    return 0\n")]
    // q2v
    [InlineData("func f() -> i32\n    let n = 1\n    var r = n@ref\n    let a = r\n    let g = func [a] () -> i32\n        var b = a\n        let h = func [b] () -> ref/i32 during r => b\n        return 0\n    return 0\n")]
    // q2w
    [InlineData("func f(p: ref/i32) -> i32\n    var r = p\n    let a = r\n    let g = func [a] () -> i32\n        var b = a\n        let h = func [b] () -> ref/i32 during r => b\n        return 0\n    return 0\n")]
    // q2wl
    [InlineData("func f(p: ref/i32) -> i32\n    let a = p\n    let g = func [a] () -> i32\n        var b = a\n        let h = func [b] () -> ref/i32 during p => b\n        return 0\n    return 0\n")]
    // pk7vv
    [InlineData("func f() -> i32\n    let n = 1\n    var r = n@ref\n    var a = r\n    let k = func [a] () -> ref/i32 during r => a\n    return 0\n")]
    // cap15dvar
    [InlineData("func use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    var r = b\n    let g = func [r] () -> ref/i32 during b\n        return r\n    return g()@follow\n")]
    // s1v
    [InlineData("func f(p: ref/i32, q: ref/i32) -> i32\n    let a = p\n    let c = q\n    let g = func [a, c] () -> i32\n        var b = a\n        b = c\n        let h = func [b] () -> ref/i32 during (a and c) => b\n        return 0\n    return 0\n")]
    // s2v
    [InlineData("func f(p: ref/i32, q: ref/i32) -> i32\n    var r = p\n    let g = func [r] () -> i32\n        var b = r\n        let h = func [b] () -> ref/i32 during r => b\n        return 0\n    return 0\n")]
    // s3v
    [InlineData("func f(p: ref/i32, q: ref/i32) -> i32\n    var x = p\n    var w = x\n    x = w\n    let h = func [x] () -> ref/i32 during p => x\n    return 0\n")]
    // f4v
    [InlineData("func see(p: ref/i32) => ()\nfunc t()\n    var b: i32 = 2\n    var s = b@ref\n    var r = s\n    let c = func [r] () -> ref/i32 during s => r\n    let v = c()\n    see(v)\n")]
    public void AWaitedVariablesSolutionIsOneValueForEveryReader(string body)
    {
        var check = DiagnosticCorpus.Check(body + Main);
        Assert.True(check.Accepted, string.Join("\n", check.Diagnostics.Select(static x => $"{x.Code}: {x.Label}")));
    }

    // Invalid twins: each Error record, its code and the source at its span, sorted. A relation over the variable is judged once,
    // as an Origin relation at its own site; a relation between Origins fixed in the body stays the formation proof's.
    [Theory]
    // A Type's declared relation over the variable (D3a): at a call's input, in a slot's solution, at a result, through a later use, a pattern, a function reference, a conformance or Constraint subject.
    // rt2i (two independent declared relations, the callee's clause and its result Type's, coincide after substitution: each its row; F's J2 ruling 27 (i))
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc g2(x: Option<ref/i32 during q>, y: ref/i32 during q) -> Option<T during q>\n    origin q outlives static\n    return .None\nfunc k(p: ref/i32) -> i32\n    let t = g2(.None, p)\n    return 0\n", "UnprovenOriginRelation_Kd@.None, p)|UnprovenOriginRelation_Kd@g2(.None, p)")]
    // rk1i (two independent declared relations, the callee's clause and its result Type's, coincide after substitution: each its row; F's J2 ruling 27 (i))
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc gr(x: Option<ref/i32 during q>, y: ref/i32 during q) -> Option<T during q>\n    origin q outlives static\n    return .None\nfunc k(p: ref/i32) -> i32\n    return match gr(.None, p)\n        .Some(_) => 0\n        .None => 1\n", "UnprovenOriginRelation_Kd@.None, p)|UnprovenOriginRelation_Kd@gr(.None, p)")]
    // dc1i
    [InlineData("func g(x: Option<ref/i32 during q>, y: ref/i32 during q) -> i32\n    origin q outlives static\n    return 0\nfunc k(p: ref/i32) -> i32\n    return g(.None, p)\n", "UnprovenOriginRelation_Kd@.None, p)")]
    // fr1i
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc g1(x: Option<T during q>, y: Option<ref/i32 during q>) -> i32\n    return 0\nfunc k() -> i32\n    let v: i32 = 5\n    let f = g1\n    return f(.None, .Some(v@ref))\n", "UnsatisfiedOriginRelation_Kd@.None, .Some(v@ref))")]
    // fr2i
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc g(x: Option<T during q>, y: ref/i32 during q) -> i32\n    return 0\nfunc k(p: ref/i32) -> i32\n    let f = g\n    return f(.None, p)\n", "UnprovenOriginRelation_Kd@.None, p)")]
    // w166 (R7-O1 b1; the Middle's ruling of 2026-11-19): fr2i's direct-call twin, one record, the call's row at `.None`; a value call
    // follows the direct call's rule (J2, SlotSolution.OpenWaited), so `.None` is completed over the call's region and T's clause is the
    // call's row alone (fail-before on 002d32cb0: fr2i gave a second record, Binding's UnprovenConstraint_Kd at `.None`).
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc g(x: Option<T during q>, y: ref/i32 during q) -> i32\n    return 0\nfunc k(p: ref/i32) -> i32\n    return g(.None, p)\n", "UnprovenOriginRelation_Kd@.None, p)")]
    // n4gv, n4gw (R7-O1 b1, the Front's N4): a generic callee's value call opens Origin parameters only, its Type slots fixed at the
    // callee's reference (SPEC 10.8; SlotSolution.OpenWaited's FixedSlot). n4gv: fr2i over a generic callee, the same one record; n4gw
    // (valid): V is `ref/i32 during static` at the reference, so the result returned for static holds, where an opened V would meet its
    // Origin with the call's region.
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc gv<V>(x: Option<T during q>, y: ref/i32 during q, v: V) -> i32\n    return 0\nfunc k(p: ref/i32) -> i32\n    let f = gv<i32>\n    return f(.None, p, 1)\n", "UnprovenOriginRelation_Kd@.None, p, 1)")]
    [InlineData("func gw<V>(x: Option<V>, y: V, z: ref/i32 during q) -> V => y@move\nfunc k(p: ref/i32 during static, r: ref/i32) -> ref/i32 during static\n    let f = gw<ref/i32 during static>\n    return f(.None, p, r)\n", "")]
    // rf5
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc g(x: Option<T during q>, y: ref/i32 during q) -> i32\n    return 0\nfunc k() -> i32\n    let v: i32 = 5\n    return g(.None, v@ref)\n", "UnsatisfiedOriginRelation_Kd@.None, v@ref)")]
    // rf6
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc g(x: Option<T during q>, y: ref/i32 during q) -> i32\n    return 0\nfunc k(p: ref/i32) -> i32\n    var a = p\n    return g(.None, a)\n", "UnprovenOriginRelation_Kd@.None, a)")]
    // rf11
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc g0(x: Option<T during q>, y: Option<ref/i32 during q>) -> i32\n    return 0\nfunc k() -> i32\n    let v: i32 = 5\n    return g0(.None, .Some(v@ref))\n", "UnsatisfiedOriginRelation_Kd@.None, .Some(v@ref))")]
    // rf12
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc g0(x: Option<T during q>, y: Option<ref/i32 during q>) -> i32\n    return 0\nfunc k(p: ref/i32) -> i32\n    return g0(.None, .Some(p))\n", "UnprovenOriginRelation_Kd@.None, .Some(p))")]
    // ko2i
    [InlineData("func keepO<T>(x: Option<T>, y: T) -> i32\n    T is Owned\n    return 0\nfunc one() -> ref/i32 during static => $abort(\"no\")\nfunc use(q: ref/i32) -> i32\n    return keepO(.Some(q), one())\n", "UnprovenOriginRelation_Kd@q), one())")]
    // A construction: one value for the fixed-binding re-check (D1, D2), the owner Type's declared relation as the call's row (D3b) or the proof's, the owner's Constraints (D4).
    // gco1i
    [InlineData("struct Go<T>\n    T is Owned\n    public let t: T\n    public let o: Option<T>\n    public init(x: T, o: Option<T>)\n        self.t = x@move\n        self.o = o@move\nfunc one() -> ref/i32 during static => $abort(\"no\")\nfunc g(q: ref/i32) -> i32\n    let x = Go.init(q, .Some(q))\n    return 0\n", "UnprovenConstraint_Kd@Go.init(q, .Some(q))|UnprovenConstraint_Kd@let x = Go.init(q, .|UnprovenConstraint_Kd@x = Go.init(q, .Some")]
    // gco2i
    [InlineData("struct Go<T>\n    T is Owned\n    public let t: T\n    public let o: Option<T>\n    public init(x: T, o: Option<T>)\n        self.t = x@move\n        self.o = o@move\nfunc one() -> ref/i32 during static => $abort(\"no\")\nfunc g(q: ref/i32) -> i32\n    let x = Go.init(one(), .Some(q))\n    return 0\n", "UnprovenOriginRelation_Kd@q))")]
    // rc1i
    [InlineData("struct T3 {a}\n    origin a outlives static\n    public let o: Option<ref/i32 during a>\n    public init(o: Option<ref/i32 during a>) => self.o = o\n\nfunc st() -> ref/i32 during static => $abort(\"no\")\nfunc k() -> i32\n    let v: i32 = 5\n    let t = T3.init(o: .Some(v@ref))\n    return 0\n", "UnsatisfiedOriginRelation_Kd@T3.init(o: .Some(v@r")]
    // rc2i
    [InlineData("struct T3 {a}\n    origin a outlives static\n    public let o: Option<ref/i32 during a>\n    public init(o: Option<ref/i32 during a>) => self.o = o\n\nfunc st() -> ref/i32 during static => $abort(\"no\")\nfunc k(p: ref/i32) -> i32\n    let t = T3.init(o: .Some(p))\n    return 0\n", "UnprovenOriginRelation_Kd@T3.init(o: .Some(p))")]
    // rc3i
    [InlineData("struct T4 {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public let o: Option<ref/i32 during a>\n    public init(x: ref/i32 during a, o: Option<ref/i32 during a>)\n        self.x = x\n        self.o = o\n\nfunc st() -> ref/i32 during static => $abort(\"no\")\nfunc k() -> i32\n    let v: i32 = 5\n    let t = T4.init(x: st(), o: .Some(v@ref))\n    return 0\n", "UnsatisfiedOriginRelation_Kd@v@ref))")] // One record (R7-O1 H: the twin went with Binding's).
    // rf1
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc k() -> i32\n    let v: i32 = 5\n    let t = T.init(x: v@ref)\n    return 0\n", "UnprovenConstraint_Kd@T.init(x: v@ref)")]
    // rf2
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc k(p: ref/i32) -> i32\n    var a = p\n    let t = T.init(x: a)\n    return 0\n", "UnprovenConstraint_Kd@T.init(x: a)")]
    // rf3
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc k(p: ref/i32) -> i32\n    let t = T.init(x: p)\n    return 0\n", "UnprovenConstraint_Kd@T.init(x: p)")]
    // rf8
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc k(p: ref/i32) -> i32\n    var a = p\n    let o = Option.Some(T.init(x: a))\n    return 0\n", "UnprovenConstraint_Kd@Option.Some(T.init(x|UnprovenConstraint_Kd@T.init(x: a))")]
    // ec1i
    [InlineData("enum E2 {a}\n    origin a outlives static\n    One(ref/i32 during a)\n\nfunc st() -> ref/i32 during static => $abort(\"no\")\nfunc k() -> i32\n    let v: i32 = 5\n    let e = E2.One(v@ref)\n    return 0\n", "UnprovenConstraint_Kd@E2.One(v@ref)")]
    // ec2i
    [InlineData("enum E3 {a}\n    origin a outlives static\n    Two(Option<ref/i32 during a>)\n\nfunc st() -> ref/i32 during static => $abort(\"no\")\nfunc k() -> i32\n    let v: i32 = 5\n    let e = E3.Two(.Some(v@ref))\n    return 0\n", "UnsatisfiedOriginRelation_Kd@v@ref))")] // One record (R7-O1 H: the twin went with Binding's).
    // The solve of the callee's other variables (D1's Origin half): a clause that bounds another variable through the waited one (reading RF7), an invariant end, an equality, an associate.
    // as2i
    [InlineData("func f2(x: Option<ref/i32 during q>, y: ref/i32 during q, z: uniq/(ref/i32 during c)) -> i32\n    origin q outlives c\n    return 0\nfunc k(p: ref/i32, d: ref/i32, ud: uniq/(ref/i32 during d)) -> i32\n    return f2(.None, p, ud)\n", "UnprovenOriginRelation_Kd@.None, p, ud)")]
    // as3i
    [InlineData("contract C\n    associate Item(a, b)\n        origin a outlives b\nstruct S\n    Self is C\n    associate C.Item(a, b) is i32\nfunc f3(x: Option<ref/i32 during q>, y: ref/i32 during q, z: uniq/(ref/i32 during c)) -> S.Item(q, c)\n    origin q outlives c\n    return 42\nfunc k(p: ref/i32, d: ref/i32, ud: uniq/(ref/i32 during d)) -> i32\n    let r = f3(.None, p, ud)\n    return 0\n", "UnprovenOriginRelation_Kd@.None, p, ud)")]
    // t6i: b1's record at `w`; the clause row `(p and q) outlives d` at `.Some(w)` stays whole for MeetNode (R7-O1 H's F-H-13), which adds
    // a record of the same failure there (a dependent record on a rejected program, counted: DECISIONS Q13).
    [InlineData("func f2(x: Option<ref/i32 during q>, y: ref/i32 during q, z: uniq/(ref/i32 during c)) -> i32\n    origin q outlives c\n    return 0\nfunc k(w: ref/i32, p: ref/i32, d: ref/i32, ud: uniq/(ref/i32 during d)) -> i32\n    origin p outlives d\n    return f2(.Some(w), p, ud)\n", "UnprovenOriginRelation_Kd@.Some(w), p, ud)|UnprovenOriginRelation_Kd@w), p, ud)")]
    // t7i
    [InlineData("func f7(x: Option<ref/i32 during q>, y: ref/i32 during q, z: ref/i32 during c) -> ref/i32 during c\n    origin q outlives c\n    $abort(\"no\")\nfunc k(w: ref/i32, p: ref/i32, d: ref/i32) -> ref/i32 during (d and p)\n    return f7(.Some(w), p, d)\n", "UnprovenOriginRelation_Kd@.Some(w), p, d)|UnprovenOriginRelation_Kd@f7(.Some(w), p, d)|UnprovenOriginRelation_Kd@w), p, d)")]
    // The region keeps its recorded bounds (D1c): Owned over the result of either half, through a local and in place.
    // cs3i
    [InlineData("func keep<V>(x: Option<V>, y: V) -> V => y@move\nfunc needO<U>(x: U) -> i32\n    U is Owned\n    return 0\nfunc k(p: ref/i32) -> i32\n    let r = keep(.Some(p), p)\n    return needO(r)\n", "UnprovenConstraint_Kd@needO(r)")]
    // rq1i
    [InlineData("func keepk(x: Option<ref/(ref/i32 during k) during c>, y: ref/(ref/i32 during k) during c) -> ref/i32 during k => $abort(\"no\")\nfunc needO<U>(x: U) -> i32\n    U is Owned\n    return 0\nfunc use(p: ref/i32) -> i32\n    let r = keepk(.Some(p@ref), p@ref)\n    return needO(r)\n", "UnprovenConstraint_Kd@needO(r)")]
    // r2i
    [InlineData("func keep<V>(x: Option<V>, y: V) -> V => y@move\nfunc needO<U>(x: U) -> i32\n    U is Owned\n    return 0\nfunc pin(x: uniq/(ref/i32 during a), y: ref/i32 during a) -> i32 => 0\nfunc use(p: ref/i32, q: ref/i32) -> i32\n    origin p outlives static\n    var r = keep(.Some(p), p)\n    let n = pin(r@uniq, q)\n    return needO(r)\n", "UnprovenConstraint_Kd@needO(r)")]
    // rr1i
    [InlineData("func keep<T>(x: Option<T>, y: T) -> T => $abort(\"no\")\nfunc set<U>(x: uniq/U, v: U) -> i32\n    U is Owned\n    return 0\nfunc use(p: ref/i32, q: ref/i32) -> i32\n    origin p outlives static\n    return set(keep(.Some(p), p)@uniq, q)\n", "UnprovenConstraint_Kd@set(keep(.Some(p), p")]
    // A Closure that captures the result (C2): the evidence bound's own record beside the located limit.
    // cap7
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let r = keep(.Some(a@ref), b@ref)\n    let g = func [r] (q: ref/i32) -> ref/i32 during q\n        return r\n    return 0\n", "UnprovenOriginRelation_Kd@r|Unsupported_Kd@r")]
    // cap8
    [InlineData("func keepk(x: Option<ref/(ref/i32 during k) during c>, y: ref/(ref/i32 during k) during c) -> ref/i32 during k => $abort(\"no\")\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let r = keepk(.Some(a@ref), b@ref)\n    let g = func [r] (q: ref/i32) -> ref/i32 during q\n        return r\n    return 0\n", "UnprovenOriginRelation_Kd@r|Unsupported_Kd@r")]
    // cap9
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc use(p: ref/i32) -> i32\n    let a = p\n    var b = p\n    let r = keep(.Some(a@ref), b@ref)\n    let g = func [r] (q: ref/i32) -> ref/i32 during q\n        return r\n    return 0\n", "UnprovenOriginRelation_Kd@r|Unsupported_Kd@r")]
    // cap10
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let r = keep(.Some(a@ref), b@ref)\n    let g = func [r] (out: uniq/(ref/i32)) -> ()\n        out@follow = r\n    return 0\n", "UnprovenOriginRelation_Kd@r|Unsupported_Kd@r")]
    // cap7d
    [InlineData("func use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let r = b\n    let g = func [r] (q: ref/i32) -> ref/i32 during q\n        return r\n    return 0\n", "UnprovenOriginRelation_Kd@r")]
    // cap15e
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let o: Option<ref/(ref/i32)> = .Some(a@ref)\n    let r = keep(o, b@ref)\n    let g = func [r] () -> ref/i32 during b\n        return r\n    return g()@follow\n", "")] // cap15e, R7-O2 A1: valid (r's candidate (o's inner and b), each read in turn to p; O-466 (b))
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc use(p: ref/i32) -> i32\n    var a = p\n    let n: i32 = 1\n    a = n@ref\n    var b = p\n    let o: Option<ref/(ref/i32)> = .Some(a@ref)\n    let r = keep(o, b@ref)\n    let g = func [r] () -> ref/i32 during b\n        return r\n    return g()@follow\n", "UnprovenOriginRelation_Kd@r")] // cap15e's invalid twin: a's candidate (p and n@ref) read in turn, n@ref does not outlive p
    // cap12
    [InlineData("func keep0(x: Option<ref/(ref/i32 during k) during c>) -> ref/i32 during k => $abort(\"no\")\nfunc use(p: ref/i32) -> i32\n    var a = p\n    let r = keep0(.Some(a@ref))\n    let g = func [r] (q: ref/i32) -> ref/i32 during q\n        return r\n    return 0\n", "Unsupported_Kd@r")]
    // cap15
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let r = keep(.Some(a@ref), b@ref)\n    let g = func [r] () -> ref/i32 during b\n        return r\n    return g()@follow\n", "Unsupported_Kd@r")]
    // Two overloads (10.1 Unknown applicability, 10.4): the selected candidate shows at `let z: i32 = r`.
    // tr1i
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc g(x: Option<T during q>, y: ref/i32 during q) -> i32 => 0\nfunc g(x: Option<T during q>, y: ref/i32 during q, n: i32 = 0) -> bool => true\nfunc k(p: ref/i32) -> i32\n    let r = g(.None, p)\n    let z: i32 = r\n    return z\n", "UnprovenOriginRelation_Kd@.None, p)")]
    // tr4i
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc g(x: Option<T during q>, y: ref/i32 during q, n: i32 = 0) -> i32 => 0\nfunc g(x: Option<T during q>, y: ref/i32 during q, m: bool = false) -> bool => true\nfunc k(p: ref/i32) -> i32\n    origin p outlives static\n    let r = g(.None, p)\n    let z: i32 = r\n    return z\n", "AmbiguousBinding_Kd@g(.None, p)")]
    // A call's result Type's declared relation over the call's region (revision 3): the result row at the call, which every derived place (a later call, a pattern, a field, a returned value, a default) rests on.
    // c3e1
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc mk(x: Option<ref/i32 during q>, y: ref/i32 during q) -> T during q => $abort(\"no\")\nfunc k() -> i32\n    let v: i32 = 5\n    let t = mk(.None, v@ref)\n    return 0\n", "UnsatisfiedOriginRelation_Kd@mk(.None, v@ref)")]
    // c3e2
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc mk(x: Option<ref/i32 during q>, y: ref/i32 during q) -> T during q => $abort(\"no\")\nfunc k(p: ref/i32) -> i32\n    var a = p\n    let t = mk(.None, a)\n    return 0\n", "UnprovenOriginRelation_Kd@mk(.None, a)")]
    // c3e3
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc mk(x: Option<ref/i32 during q>, y: ref/i32 during q) -> T during q => $abort(\"no\")\nfunc k(p: ref/i32) -> i32\n    let t = mk(.None, p)\n    return 0\n", "UnprovenOriginRelation_Kd@mk(.None, p)")]
    // c3e4
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc keep<V>(x: Option<V>, y: V) -> V => y@move\nfunc mk(x: Option<ref/i32 during q>, y: ref/i32 during q) -> T during q => $abort(\"no\")\nfunc k(p: ref/i32) -> i32\n    let t = mk(.None, keep(.None, p))\n    return 0\n", "UnprovenOriginRelation_Kd@mk(.None, keep(.None")]
    // c3b3
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc mk3(x: Option<ref/i32 during q>) -> T during q => $abort(\"no\")\nfunc k(p: ref/i32) -> i32\n    let t = mk3(.Some(p))\n    return 0\n", "UnprovenOriginRelation_Kd@mk3(.Some(p))")]
    // c3t1
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc mkt<U>(x: Option<ref/U during q>, y: ref/U during q) -> T during q => $abort(\"no\")\nfunc k() -> i32\n    let v: i32 = 5\n    let t = mkt(.None, v@ref)\n    return 0\n", "UnsatisfiedOriginRelation_Kd@mkt(.None, v@ref)")]
    // c3p1
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc mkp(x: Option<ref/i32 during q>, y: ref/i32 during q, t: T during static) -> T during q => t@move\nfunc k(t0: T during static) -> i32\n    let v: i32 = 5\n    let t = mkp(.None, v@ref, t0@move)\n    return 0\n", "UnsatisfiedOriginRelation_Kd@mkp(.None, v@ref, t0")]
    // c3u1
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc mk(x: Option<ref/i32 during q>, y: ref/i32 during q) -> T during q => $abort(\"no\")\nfunc use1(t: T during s) -> i32 => 0\nfunc k() -> i32\n    let v: i32 = 5\n    let t = mk(.None, v@ref)\n    return use1(t@move)\n", "UnsatisfiedOriginRelation_Kd@mk(.None, v@ref)")]
    // d4e1
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc gr(x: Option<ref/i32 during q>, y: ref/i32 during q) -> Option<T during q> => $abort(\"no\")\nfunc k() -> i32\n    let v: i32 = 5\n    return match gr(.None, v@ref)\n        .Some(_) => 0\n        .None => 1\n", "UnsatisfiedOriginRelation_Kd@gr(.None, v@ref)")]
    // d4e3
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc gr(x: Option<ref/i32 during q>, y: ref/i32 during q) -> Option<T during q> => $abort(\"no\")\nfunc k(p: ref/i32) -> i32\n    return match gr(.None, p)\n        .Some(_) => 0\n        .None => 1\n", "UnprovenOriginRelation_Kd@gr(.None, p)")]
    // ff2b
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nstruct S2 {b}\n    origin b outlives static\n    public let t: Option<T during b>\n    public init(t: Option<T during b>) => self.t = t@move\nfunc mks(x: Option<ref/i32 during q>, y: ref/i32 during q) -> S2 during q => $abort(\"no\")\nfunc k() -> i32\n    let v: i32 = 5\n    let s = mks(.None, v@ref)\n    let o = s.t@ref\n    return 0\n", "UnsatisfiedOriginRelation_Kd@mks(.None, v@ref)")]
    // ff1b
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nstruct S2 {b}\n    public let t: Option<T during b>\n    public init(t: Option<T during b>) => self.t = t@move\nfunc mks(x: Option<ref/i32 during q>, y: ref/i32 during q) -> S2 during q => $abort(\"no\")\nfunc k() -> i32\n    let v: i32 = 5\n    let s = mks(.None, v@ref)\n    let o = s.t@ref\n    return 0\n", "UnprovenConstraint_Kd@let t: Option<T duri")]
    // cc1
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc mk(x: Option<ref/i32 during q>, y: ref/i32 during q) -> T during q => $abort(\"no\")\nfunc k() -> i32\n    let v: i32 = 5\n    let r = v@ref\n    let f = func [r] () -> i32\n        let t = mk(.None, r)\n        return 0\n    return f()\n", "UnprovenOriginRelation_Kd@mk(.None, r)")]
    // da1
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc gd(y: ref/i32 during q, x: Option<T during q> = .None) -> i32\n    return 0\nfunc k() -> i32\n    let v: i32 = 5\n    return gd(v@ref)\n", "UnsatisfiedOriginRelation_Kd@gd(v@ref)")]
    // The Middle's W-249 A5 oracle row (`g216a-oracle` s11w_bare_someshort), valid: the waited `c` takes the meet of the two inputs,
    // the local `short`'s region, and nothing escapes it (SPEC 15.3.6; the owner's O-362 (a)); J2 accepts it (dev before J2:
    // UnsatisfiedOriginRelation_Kd twice). PLAN G962 moved cl2 (an anonymous callee) to ResultPremiseTest's
    // ANestedResultIsJudgedWhereItIsWritten: its rule is the written result's formation, not the call's.
    // s11w_bare_someshort
    [InlineData("func sw<T>(x: Option<uniq/T during c>, y: uniq/T during c) => ()\nfunc bad(p: uniq/i32, c: bool, d: bool) -> i32\n    var short = 2\n    sw(.Some(short@uniq), p)\n    return 0\n", "")]
    // The same reading where a bound fails: one Closure level deeper than cap7 (q4, the evidence bound's record beside the located
    // limit), cap7's `var` twin (cap7var) and the cycles' invalid twins (s1i-s3i), each record naming the value's failing operand.
    // q4
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let r = keep(.Some(a@ref), b@ref)\n    let g = func [r] () -> i32\n        let s = r\n        let h = func [s] (q: ref/i32) -> ref/i32 during q\n            return s\n        return 0\n    return 0\n", "UnprovenOriginRelation_Kd@s|Unsupported_Kd@s")]
    // cap7var
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    var r = keep(.Some(a@ref), b@ref)\n    let g = func [r] (q: ref/i32) -> ref/i32 during q\n        return r\n    return 0\n", "UnprovenOriginRelation_Kd@r|Unsupported_Kd@r")]
    // s1i
    [InlineData("func f(p: ref/i32, q: ref/i32) -> i32\n    let a = p\n    let c = q\n    let g = func [a, c] () -> i32\n        var b = a\n        b = c\n        let h = func [b] () -> ref/i32 during a => b\n        return 0\n    return 0\n", "UnprovenOriginRelation_Kd@b")]
    // s2i
    [InlineData("func f(p: ref/i32, q: ref/i32) -> i32\n    var r = p\n    let g = func [r] () -> i32\n        var b = r\n        let h = func [b] () -> ref/i32 during q => b\n        return 0\n    return 0\n", "UnprovenOriginRelation_Kd@b")]
    // s3i
    [InlineData("func f(p: ref/i32, q: ref/i32) -> i32\n    var x = p\n    var w = x\n    x = w\n    let h = func [x] () -> ref/i32 during q => x\n    return 0\n", "UnprovenOriginRelation_Kd@x")]
    public void AWaitedVariablesReadersKeepTheirRecords(string body, string records)
    {
        var source = body + Main;
        var check = DiagnosticCorpus.Check(source);
        var shown = check.Diagnostics.Where(static x => x.Severity == DiagnosticSeverity.Error).Select(x => x.Code + "@" + At(source, x.Span)).Order(StringComparer.Ordinal);
        Assert.Equal(records, string.Join("|", shown));
    }
}
