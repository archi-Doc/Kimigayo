// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>Rule W on the MIR (SPEC 15.6.5, 23.1.2; docs/dev/DECISIONS.md R4-M132): a Loan conflict caused only by Refuted Origin relations,
/// every value carrying the Loan past the access being the result or storage the body does not own that received it as the borrow or a
/// Copy or Reborrow of it into a part with a fixed Origin, is derived: the MIR problem keeps its code and records the Refuted problems as
/// Cause facts. Nothing is dropped and no verdict changes.</summary>
public sealed partial class MirBorrowsTest
{
    // A result with an unstated raw Field and Fixed Origins (MirOrigins.Fixed reads no Field), so mkE's rule-A rows are Value and Chain
    // (SPEC 15.6.4; DECISIONS BU-40), whose arrival in _0 rule W keeps as it is; only its innermost layer's Origin is the result's.
    private const string ChainHolder = "struct Holder {a}\n    public var p: raw/u8\n    public var r: ref/i32 during a\n" +
        "func mkE(x: ref/(ref/(ref/i32 during a) during b) during c) -> Holder during a\n    loop => ()\n";

    // The rows' sources (the 2a-2 round's programs, the 65be867e9 ledger's and the design review's), each with a `public func main`.
    private static readonly Dictionary<string, string> CausedSources = new()
    {
        // derived
        ["j-region"] = "func bad(input: ref/i32) -> ref/i32 during input\n    var view = input\n    let local = 5\n    view = local@ref\n" +
            "    return view\n",
        ["j-bad-v"] = "func bad(x: ref/i32) -> ref/i32 during x\n    let local: i32 = 1\n    var r = local@ref\n    return r\n",
        ["row2-v"] = "func r2(a: ref/i32) -> ref/i32 during a\n    var x = 1\n    var r: ref/i32 during a = a\n    var u = x@ref\n    r = u\n" +
            "    return r\n",
        ["m-write4-v"] = "func w4(slot: uniq/(ref/i32 during a))\n    var x = 1\n    var r = x@ref\n    slot@follow = r\n    x = 2\n",
        ["m-store-v"] = "func leakL(target: uniq/(ref/i32 during a))\n    let local = 1\n    var r = local@ref\n    target@follow = r\n",
        ["two-flows-v"] = "func tf(p: uniq/(ref/i32 during a)) -> ref/i32 during a\n    let local = 1\n    var r = local@ref\n    p@follow = r\n" +
            "    return r\n",
        ["jregion2"] = "func jr2(input: ref/i32, slot: uniq/(ref/i32 during input)) -> ref/i32 during input\n    let local = 1\n" +
            "    var r = local@ref\n    slot@follow = r\n    return r\n",
        ["loop-store-v"] = "func ls(p: uniq/(uniq/i32 during a), flag: bool)\n    var x = 1\n    while flag\n        var r = x@uniq\n" +
            "        p@follow = r\n",
        ["id-return-v"] = "func id(x: ref/i32) -> ref/i32 during x => x\nfunc ir(p: ref/i32) -> ref/i32 during p\n    let local = 1\n" +
            "    var r = id(local@ref)\n    return r\n",
        ["rv-g-param"] = "struct Data\n    public var value: i32 = 0\nenum Packet {a}\n    Data(Data)\n    Link(ref/Data during a)\n    Empty\n" +
            "func keep(slot: uniq/(ref/Data during s), value: ref/Data during s) -> bool => true\nfunc use<T>(value: ref/T) => ()\n" +
            "func gp(packet: ref/Packet, target: uniq/(ref/Data during t)) -> i32\n    match packet@follow\n" +
            "        .Data(let d) if keep(target, d) => return 1\n        _ => return 0\n",
        ["rv-g-param-i"] = "func keepI(slot: uniq/(ref/i32 during a), value: ref/i32 during a) -> bool\n    slot@follow = value\n    return true\n" +
            "func gp(target: uniq/(ref/i32 during a), x: Option<i32>) -> i32\n    match x\n        .Some(let v) if keepI(target, v@ref) => return 1\n" +
            "        _ => return 0\n",
        ["r3-b1b"] = "func keepA(p: uniq/(ref/i32 during a), q: uniq/i32 during a, c: bool)\n    let local: i32 = 1\n    var v = q@follow@ref\n" +
            "    if c\n        v = local@ref\n    p@follow = v\n    q@follow = 2\n",
        ["rf-block-v2"] = "func rb(x: ref/i32) -> ref/i32 during x\n    let local = 1\n    var l = local@ref\n    var r = x\n    r = label b: do\n" +
            "        exit to b l\n    return r\n",
        ["rf-if-v2"] = "func ri(x: ref/i32, c: bool) -> ref/i32 during x\n    let local = 1\n    var l = local@ref\n    var r = x\n" +
            "    r = if c => l else => x\n    return r\n",
        ["rf-match-v2"] = "func rm(x: ref/i32, o: Option<i32>) -> ref/i32 during x\n    let local = 1\n    var l = local@ref\n    var r = x\n" +
            "    r = match o\n        .Some(_) => l\n        _ => x\n    return r\n",
        ["e3c4d30c20af"] = "func leak(values: Slice<i32> during s) -> uniq/(Slice<i32> during s) during s\n    var local = values\n" +
            "    return local@uniq\n",
        ["0433a531b1ba"] = "func leak(view: UniqSlice<uniq/i32 during a> during s) -> uniq/i32 during a\n    var v = view@move\n" +
            "    return v[0]@follow@uniq\n",
        ["38df74b325ce"] = "func leak(p: uniq/i32 during a) -> uniq/i32 during a\n    var slot = p\n    var rr = slot@uniq\n" +
            "    return rr@follow@follow@uniq\n",
        ["27e4888a2ae9"] = "public struct Box<E>\n    public let item: E\n    public init(item: E) => self.item = item@move\n" +
            "func f<s/T>(x: s/T, y: ref/Box<s/T> during c) -> ref/T during c\n    s is ref\n    return x@follow@ref\n" +
            "func g(q: ref/Box<ref/i32 during s> during t) -> ref/i32 during t\n    let m: i32 = 5\n    return f(m@ref, q)\n",
        ["d4a09871a00b"] = "func assign<T>(a: uniq/T, b: uniq/T) -> () => ()\nfunc g(p: ref/i32) -> ref/i32 during p\n    var x = p\n" +
            "    let local: i32 = 1\n    var y = local@ref\n    assign(x@uniq, y@uniq)\n    return x\n",
        ["write-through-store"] = "func w(p: ref/i32) -> ref/i32 during p\n    var x = p\n    let local = 1\n    let r = x@uniq\n" +
            "    r@follow = local@ref\n    return x\n",
        ["content-retention"] = "func first(r: ref/(ref/i32 during a)) -> ref/i32 during a => r@follow\nfunc cr(p: ref/i32) -> ref/i32 during p\n" +
            "    let local = 1\n    var h = local@ref\n    return first(h@ref)\n",
        ["f02fb5c218bc"] = "func store(x: ref/i32, target: uniq/Array<ref/i32 during x>) -> ()\n    let local: i32 = 1\n" +
            "    target@follow.append(local@ref)\n",
        ["container-deep"] = "func deep(x: ref/i32, target: uniq/(uniq/Array<ref/i32 during x> during y)) -> ()\n    let local: i32 = 1\n" +
            "    target@follow@follow.append(local@ref)\n",
        ["3c4ffb8ea44b"] = "func keep(slot: uniq/Array<ref/i32 during s>, v: ref/i32 during s) => slot@follow.append(v)\nfunc leak() -> i32\n" +
            "    let arr = Array<ref/i32>.init(capacity: 0)\n    var f = func [var arr@move] () -> i32\n        let local: i32 = 1\n" +
            "        arr.append(local@ref)\n        return 0\n    return f@uniq()\n",
        ["noncfl-candidate-v"] = "func inspect<T>(value: ref/T) => ()\nfunc h(p: uniq/(ref/i32 during a))\n    var s = (1, 2)\n" +
            "    let keep = s.0@ref\n    var r = s.1@ref\n    p@follow = r\n    s.1 = 5\n    inspect(keep)\n",
        ["store-every-origin"] = "func se(x: ref/i32, p: uniq/(ref/(ref/i32 during x) during x))\n    var inner = x\n    let local = 1\n" +
            "    var r = local@ref\n    p@follow = r@ref\n",
        ["merge-two-paths-v"] = "func f(x: ref/i32, p: uniq/(ref/i32 during x), c: bool) -> ref/i32 during x\n    let local = 1\n" +
            "    var r = local@ref\n    var q = local@ref\n    if c\n        return r\n    p@follow = q\n    return x\n",
        ["chain-retention"] = ChainHolder + "func chainKept(q: ref/i32) -> Holder during q\n    let local = 1\n    var h = local@ref\n    var h2 = h@ref\n" +
            "    return mkE(h2@ref)\n",
        ["two-views"] = "func pp<s/T>(target: uniq/(ref/i32 during a), x: s/T)\n    s is owner or ref\n    let local = 1\n    var r = local@ref\n" +
            "    target@follow = r\n",
        // pins
        ["m-write"] = "func w(p: ref/i32) -> ref/i32 during p\n    var x = 1\n    let r = x@ref\n    x = 1\n    return r\n",
        ["m-write-var"] = "func w(p: ref/i32) -> ref/i32 during p\n    var x = 1\n    var r = x@ref\n    x = 1\n    return r\n",
        ["m-write-var2-v"] = "func w(p: ref/i32) -> ref/i32 during p\n    var x = 1\n    var r: ref/i32 during p = p\n    var u = x@ref\n    r = u\n" +
            "    x = 1\n    return r\n",
        ["m-write3"] = "func w3(p: ref/i32) -> ref/i32 during p\n    var x = 1\n    let r: ref/i32 during p = x@ref\n    x = 2\n    return r\n",
        ["m-scope"] = "func f(x: ref/i32) -> ref/i32 during x\n    var r = x\n    do\n        let local = 1\n        r = local@ref\n    return r\n",
        ["m-scope-use"] = "func inspect<T>(value: ref/T) => ()\nfunc f(x: ref/i32) -> ref/i32 during x\n    var r = x\n    do\n        let local = 1\n" +
            "        r = local@ref\n    inspect(r)\n    return r\n",
        ["m-twice-v"] = "func tw(p: ref/i32) -> ref/i32 during p\n    var x = 1\n    var a = x@ref\n    var b = x@ref\n    var pair = (a, b)\n" +
            "    return pair.0\n",
        ["same-root-v"] = "struct F\n    public var f: i32 = 0\n    public var g: i32 = 0\n    public init() => ()\n" +
            "func sr(p: ref/i32) -> ref/i32 during p\n    var x = F.init()\n    var pair = (x.f@ref, x.g@ref)\n    return pair.0\n",
        ["m-pair-v"] = "func pr(p: ref/i32) -> ref/i32 during p\n    var x = 1\n    var y = 2\n    var pair = (x@ref, y@ref)\n    return pair.0\n",
        ["fK"] = "func k(x: (ref/i32 during o, ref/i32 during q)) -> ref/i32 during q\n    return x.1\n" +
            "func fK(p: uniq/(ref/i32 during a), p0: ref/i32 during a) -> ref/i32 during a\n    var x = 1\n    let r = x@ref\n    p@follow = r\n" +
            "    return k((r, p0))\n",
        ["r3-b1c"] = "func fC(p: uniq/(ref/i32 during a), slot: uniq/(ref/i32 during a), p0: ref/i32 during a)\n    var x = 1\n    let r = x@ref\n" +
            "    p@follow = r\n    let pair = (r, p0)\n    slot@follow = pair.1\n",
        ["r3-b1d-v"] = "func make() -> uniq/(ref/i32 during q) during q\n    loop => ()\nfunc fB(anchor: ref/i32)\n    var x = 1\n" +
            "    var r: ref/i32 during anchor = anchor\n    var u = x@ref\n    r = u\n    let s = make()\n    s@follow = r\n",
        ["rc-fT-v"] = "func fT(slot: uniq/(ref/i32 during a), p0: ref/i32 during a)\n    var x = 1\n" +
            "    var t: (ref/i32 during a, ref/i32 during a) = (p0, p0)\n    var u = x@ref\n    t.0 = u\n    slot@follow = t.1\n",
        ["rc-flow"] = "func fI(slot: uniq/(ref/i32 during a), p0: ref/i32 during a)\n    var x = 1\n    var v = p0\n    v = x@ref\n    let keep = v\n" +
            "    v = p0\n    let pair = (v, keep)\n    slot@follow = pair.0\n",
        ["r2-b1a-v"] = "func b1a(p: uniq/((ref/i32 during a, ref/i32 during a)), q: uniq/i32 during a)\n    let local = 1\n    var l = local@ref\n" +
            "    p@follow = (l, q@follow@ref)\n    q@follow = 2\n",
        ["r2-b1b-v"] = "func b1b(p: uniq/(ref/i32 during a), q: uniq/i32 during b)\n    var u = q@follow@ref\n    p@follow = u\n    q@follow = 2\n",
        ["g-two"] = "struct Data\n    public var value: i32 = 0\nenum Packet {a}\n    Data(Data)\n    Link(ref/Data during a)\n    Empty\n" +
            "func keep(slot: uniq/(ref/Data during s), value: ref/Data during s) -> bool => true\nfunc use<T>(value: ref/T) => ()\n" +
            "func guardTwo(packet: ref/Packet, fallback: ref/Data) -> ref/Data during packet\n    var saved = fallback\n    var other = fallback\n" +
            "    match packet@follow\n        .Data(let d) if keep(saved@uniq, d) and keep(other@uniq, d) => ()\n        _ => ()\n    use(other)\n" +
            "    return saved\n",
        ["g-return"] = "struct Data\n    public var value: i32 = 0\nenum Packet {a}\n    Data(Data)\n    Link(ref/Data during a)\n    Empty\n" +
            "func keep(slot: uniq/(ref/Data during s), value: ref/Data during s) -> bool => true\nfunc use<T>(value: ref/T) => ()\n" +
            "func guardReturn(packet: ref/Packet, fallback: ref/Data) -> ref/Data during packet\n    var saved = fallback\n    match packet@follow\n" +
            "        .Data(let d) if keep(saved@uniq, d) => ()\n        _ => ()\n    return saved\n",
        ["cf-g-reassign"] = "struct Data\n    public var value: i32 = 0\nenum Packet {a}\n    Data(Data)\n    Link(ref/Data during a)\n    Empty\n" +
            "func keep(slot: uniq/(ref/Data during s), value: ref/Data during s) -> bool => true\nfunc use<T>(value: ref/T) => ()\n" +
            "func guardRe(packet: ref/Packet, fallback: ref/Data, fb2: ref/Data) -> ref/Data during packet\n    var saved = fallback\n" +
            "    var other = fallback\n    match packet@follow\n        .Data(let d) if keep(saved@uniq, d) and keep(other@uniq, d) => ()\n" +
            "        _ => ()\n    use(other)\n    saved = fb2\n    return saved\n",
        ["rv-g-annot-esc"] = "func keepI(slot: uniq/(ref/i32 during a), value: ref/i32 during a) -> bool\n    slot@follow = value\n    return true\n" +
            "func inspect<T>(value: ref/T) => ()\nfunc g(anchor: ref/i32, x: Option<i32>) -> i32\n    var t: ref/i32 during anchor = anchor\n" +
            "    match x\n        .Some(let v) if keepI(t@uniq, v@ref) => ()\n        _ => ()\n    inspect(t)\n    return 0\n",
        ["no-twin"] = "func inspect<T>(value: ref/T) => ()\nfunc nt() -> i32\n    var x = 1\n    let r = x@ref\n    x = 2\n    inspect(r)\n" +
            "    return 0\n",
        ["6227f1ba89a2"] = "func put<T>(v: T, slot: uniq/T) -> ()\n    slot@follow = v@move\nfunc use(x: ref/i32) -> i32\n" +
            "    var slot: ref/i32 during x = x\n    if x@follow > 0\n        let local: i32 = 5\n        put(local@ref, slot@uniq)\n" +
            "    return slot@follow\n",
        ["6d75d7b343f0"] = "func f(q: ref/i32) -> (Option<ref/(ref/i32 during q) during static>, ref/i32 during static)\n    return (.None, q)\n\n" +
            "func g() -> ref/i32 during static\n    let local: i32 = 5\n    let fr = f\n    let pair = fr(local@ref)\n    return pair.1\n",
        ["merge-derived-direct-v"] = "func inspect<T>(value: ref/T) => ()\nfunc g(x: ref/i32, c: bool) -> ref/i32 during x\n" +
            "    var outer: ref/i32 = x\n    if c\n        let local = 1\n        var r = local@ref\n        if c\n            return r\n" +
            "        outer = local@ref\n    inspect(outer)\n    return x\n",
        ["rv-guard-annot"] = "func keepI(slot: uniq/(ref/i32 during a), value: ref/i32 during a) -> bool\n    slot@follow = value\n    return true\n" +
            "func g(anchor: ref/i32, x: Option<i32>) -> i32\n    var t: ref/i32 during anchor = anchor\n    match x\n" +
            "        .Some(let v) if keepI(t@uniq, v@ref) => return 1\n        _ => return 0\n",
        ["j-bad"] = "func bad(x: ref/i32) -> ref/i32 during x\n    let local: i32 = 1\n    return local@ref\n",
        ["m-write4"] = "func w4(slot: uniq/(ref/i32 during a))\n    var x = 1\n    slot@follow = x@ref\n    x = 2\n",
        ["j-bad-v.r1"] = "func inspect<T>(value: ref/T) => ()\nfunc bad(x: ref/i32) -> ref/i32 during x\n    let local: i32 = 1\n" +
            "    var r = local@ref\n    inspect(r)\n    return x\n",
        ["chain-through-e"] = ChainHolder + "func chainViaE(q: ref/i32, slot: uniq/(ref/i32 during q)) -> Holder during q\n    let local = 1\n" +
            "    var h = local@ref\n    slot@follow = h\n    var r = slot@follow@ref\n    return mkE(r@ref)\n",
        ["content-through-e"] = "func firstE(r: ref/(ref/i32 during a)) -> ref/i32 during a => r@follow\n" +
            "func contentViaE(q: ref/i32, slot: uniq/(ref/i32 during q)) -> ref/i32 during q\n    let local = 1\n    var h = local@ref\n" +
            "    slot@follow = h\n    return firstE(slot@follow@ref)\n",
        ["m-store-v.r1"] = "func inspect<T>(value: ref/T) => ()\nfunc leakL(target: uniq/(ref/i32 during a))\n    let local = 1\n" +
            "    var r = local@ref\n    inspect(r)\n",
    };

    // SPEC 15.6.5 (R4-M122's text; DECISIONS R4-M132): a conflict whose Loan every carrier past the access keeps only in the result in
    // transit (a Result temp, _0) or storage the body does not own (E), received as the borrow or a Copy or Reborrow of it into a part
    // every Origin of which is Fixed, is derived from the Refuted problems of its Borrow's Origin: a return through locals, a Reborrow and
    // a Slice handle, a generic call's result (the call clause), a store through a parameter, through two paths (both causes), in a loop,
    // through a guard candidate's call (err(ended)), a read through a reference to a whole value (B's read-through clause, Content
    // retention, and a Chain row's reads of whole holders, R4-M138; the outer layers' Loans, whose Origins mkE leaves free, have no Refuted
    // relation), a write through a reference to a whole local (B's write-through row), a container's or a closure environment's append (A's
    // receiving check over every Origin of the receiving Type), a candidate the access does not conflict with (R4-M96's exact scan, B), a
    // pair binder's view, and a conflict-free branch's write beside a derived end (keepA). Fail-before: no Cause fact exists
    // (chain-retention: no conflict at `local`, a Chain row read as one read).
    [Theory]
    [InlineData("j-region", "bad: let local = 5 <- view")]
    [InlineData("j-bad-v", "bad: let local: i32 = 1 <- r")]
    [InlineData("row2-v", "r2: var x = 1 <- u")]
    [InlineData("m-write4-v", "w4: x = 2 <- r")]
    [InlineData("m-store-v", "leakL: let local = 1 <- r")]
    [InlineData("two-flows-v", "tf: let local = 1 <- r; r")]
    [InlineData("jregion2", "jr2: let local = 1 <- r; r")]
    [InlineData("loop-store-v", "ls: x@uniq <- r | ls: var x = 1 <- r")]
    [InlineData("id-return-v", "ir: let local = 1 <- r")]
    [InlineData("rv-g-param", "gp: let d <- d")]
    [InlineData("rv-g-param-i", "gp: let v <- v@ref")]
    [InlineData("r3-b1b", "keepA: q@follow = 2 | keepA: let local: i32 = 1 <- v")]
    [InlineData("rf-block-v2", "rb: let local = 1 <- r")]
    [InlineData("rf-if-v2", "ri: let local = 1 <- r")]
    [InlineData("rf-match-v2", "rm: let local = 1 <- r")]
    [InlineData("e3c4d30c20af", "leak: var local = values <- local@uniq")]
    [InlineData("0433a531b1ba", "leak: var v = view@move <- v[0]@follow@uniq")]
    [InlineData("38df74b325ce", "leak: var slot = p <- rr@follow@follow@uniq")]
    // G216 (a) (SPEC 15.3.6; the planning session's decision for mb27, 2026-10-29): `s/T`'s Origin takes the meet of its upper bounds, y's
    // well-formedness among them, so the call's result, not q's well-formedness, fails: one record at that fit, no derived conflict.
    [InlineData("27e4888a2ae9", "")]
    [InlineData("d4a09871a00b", "g: let local: i32 = 1 <- x")]
    [InlineData("write-through-store", "w: let local = 1 <- x")]
    [InlineData("content-retention", "cr: let local = 1 <- first(h@ref)")]
    [InlineData("chain-retention", "chainKept: var h2 = h@ref | chainKept: var h = local@ref | chainKept: let local = 1 <- mkE(h2@ref)")]
    [InlineData("f02fb5c218bc", "store: let local: i32 = 1 <- local@ref")]
    [InlineData("container-deep", "deep: let local: i32 = 1 <- local@ref")]
    [InlineData("3c4ffb8ea44b", ": let local: i32 = 1 <- local@ref")]
    [InlineData("noncfl-candidate-v", "h: s.1 = 5 <- r")]
    [InlineData("store-every-origin", "se: var r = local@ref <- r@ref")]
    [InlineData("merge-two-paths-v", "f: let local = 1 <- q; r")]
    [InlineData("two-views", "pp: let local = 1 <- r")]
    public void AConflictCausedOnlyByRefutedRelationsIsDerived(string name, string expected)
        => Assert.Equal(expected, string.Join(" | ", Caused(CausedSources[name])));

    // SPEC 15.6.5, 23.1.2 (DECISIONS R4-M132): a conflict some carrier keeps otherwise stays independent: a local live past the write (SPEC's
    // `keep`), a part read (m-twice, a Field's Loan, a pair), an aggregate stored, a write through a part, a guard's candidate stored where
    // its Origin is not Fixed, a local a call writes that is read later, a call result read by part, a conflict with no Refuted relation, a
    // report of one key both derived and direct (one direct problem, R4-M96 per report), a Content or a Chain row's read through E, the
    // storage the body does not own (B's read-through clause; one rule for both, R4-M138: Chain's read of E's state unmarked fails
    // chain-through-e); the twins' literal forms report the relation only (H11, R4-M122) and a repair loses the conflict. Pins: each passes
    // before.
    [Theory]
    [InlineData("m-write", "w: x = 1")]
    [InlineData("m-write-var", "w: x = 1")]
    [InlineData("m-write-var2-v", "w: x = 1")]
    [InlineData("m-write3", "w3: x = 2")]
    [InlineData("m-scope", "f: let local = 1")]
    [InlineData("m-scope-use", "f: let local = 1")]
    [InlineData("m-twice-v", "tw: var x = 1")]
    [InlineData("same-root-v", "sr: var x = F.init()")]
    [InlineData("m-pair-v", "pr: var y = 2 | pr: var x = 1")]
    [InlineData("fK", "fK: var x = 1")]
    [InlineData("r3-b1c", "fC: var x = 1")]
    [InlineData("r3-b1d-v", "fB: var x = 1")]
    [InlineData("rc-fT-v", "fT: var x = 1")]
    [InlineData("rc-flow", "fI: var x = 1")]
    [InlineData("r2-b1a-v", "b1a: q@follow = 2 | b1a: let local = 1")]
    [InlineData("r2-b1b-v", "b1b: q@follow = 2")]
    [InlineData("g-two", "guardTwo: let d")]
    [InlineData("g-return", "guardReturn: let d")]
    [InlineData("cf-g-reassign", "guardRe: let d")]
    [InlineData("rv-g-annot-esc", "g: let v")]
    [InlineData("no-twin", "nt: x = 2")]
    [InlineData("6227f1ba89a2", "use: let local: i32 = 5")]
    [InlineData("6d75d7b343f0", "g: let local: i32 = 5")]
    [InlineData("merge-derived-direct-v", "g: let local = 1")]
    [InlineData("content-through-e", "contentViaE: let local = 1")]
    [InlineData("chain-through-e", "chainViaE: var r = slot@follow@ref | chainViaE: let local = 1")]
    [InlineData("rv-guard-annot", "")]
    [InlineData("j-bad", "")]
    [InlineData("m-write4", "")]
    [InlineData("j-bad-v.r1", "")]
    [InlineData("m-store-v.r1", "")]
    public void AConflictAnotherCarrierKeepsStaysIndependent(string name, string expected)
        => Assert.Equal(expected, string.Join(" | ", Caused(CausedSources[name])));

    // PD31 (DECISIONS R4-M132): each Cause fact names, by its index, an UnsatisfiedOriginRelation_Kd of the derived problem's body at the
    // fact's site whose finite source is its Borrow's Origin: anchored at the conflict's Borrow, or where two Refs of one root share the
    // atom (noncfl-candidate-v's s.0 and s.1) at the first of them.
    [Fact]
    public void EachCauseNamesARefutedProblemOfTheBorrowsOrigin()
    {
        var (causes, shared) = (0, new List<string>());
        foreach (var (name, source) in CausedSources)
        {
            var program = MirBuildTest.Compile(source + "public func main() => ()\n").BuildMir();
            var all = program.Problems.All.ToArray();
            for (var i = 0; i < all.Length; i++)
            {
                var facts = program.Problems.Facts(i).ToArray();
                foreach (var cause in facts.Where(static x => x.Kind == MirFactKind.Cause))
                {
                    var named = all[cause.Value];
                    Assert.Equal((DiagnosticCode.UnsatisfiedOriginRelation_Kd, all[i].Body, cause.Site), (named.Code, named.Body, named.Subject));
                    var anchor = program.Problems.Facts((int)cause.Value).ToArray().Single(static x => x.Kind == MirFactKind.Anchor);
                    if (anchor != facts.Single(static x => x.Kind == MirFactKind.Anchor))
                    {
                        shared.Add(name);
                    }

                    causes++;
                }
            }
        }

        Assert.Equal(["noncfl-candidate-v"], shared);
        Assert.Equal(33, causes); // G216 (a): 27e4888a2ae9's cause leaves with its derived conflict (SPEC 15.3.6, the mb27 decision).
    }

    // SPEC 7.2.3 (R26; 2g-5's PreparedInput grouping): a default template's Borrow its result keeps is DefaultArgumentBorrow_Kd, a
    // requirement of its own that rule W never derives: no problem of the program carries a Cause fact.
    [Fact]
    public void ADefaultArgumentBorrowRecordsNoCause()
    {
        var program = MirBuildTest.Compile("func f(x: uniq/i32 during a, y: ref/i32 during a = x) => ()\npublic func main() => ()\n").BuildMir();
        Assert.Contains(program.Problems.All.ToArray(), static x => x.Code == DiagnosticCode.DefaultArgumentBorrow_Kd);
        Assert.Empty(DerivedProblems(program));
    }

    // V16 (DECISIONS R4-M132; the design review's A-B1): rule W keeps A5 within its dense bound where every stored Loan is a W element: N
    // locals borrowed and stored through a parameter, stored on one branch (joined), read through the parameter after each store (A-B1's
    // shape: one marker added at a time made its bytes cubic), held in an aggregate read by part and copied out of it; each conflict is
    // derived.
    [Theory]
    [InlineData("    var x{0} = {0}\n    var r{0} = x{0}@ref\n    p@follow = r{0}\n")]
    [InlineData("    var x{0} = {0}\n    var r{0} = x{0}@ref\n    if flag => p@follow = r{0}\n")]
    [InlineData("    var x{0} = {0}\n    var r{0} = x{0}@ref\n    p@follow = r{0}\n    inspect(p@follow)\n")]
    [InlineData("    var x{0} = {0}\n    var r{0} = x{0}@ref\n    p@follow = r{0}\n    var t{0} = (p@follow, {0})\n    inspect(t{0}.0)\n")]
    [InlineData("    var x{0} = {0}\n    var r{0} = x{0}@ref\n    p@follow = r{0}\n    var k{0} = p@follow\n    inspect(k{0})\n")]
    public void RuleWScalesWithTheProgram(string each)
    {
        var (sizes, work, bytes) = (new[] { 32, 64, 128 }, new long[3], new long[3]);
        for (var i = 0; i < sizes.Length; i++)
        {
            var text = new StringBuilder(Prelude).Append("func shape(flag: bool, p: uniq/(ref/i32 during a))\n");
            for (var n = 0; n < sizes[i]; n++)
            {
                text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, each, n);
            }

            var c = MirBuildTest.Compile(text.ToString());
            var program = c.BuildMir();
            Assert.Empty(program.Findings);
            var stratum = new Stratum(c.Types);
            stratum.Run(program, Body(program, "shape"));
            Assert.Equal(sizes[i], program.Problems.All.ToArray().Count(static x => LoanCodes.Contains(x.Code)));
            Assert.Equal(sizes[i], DerivedProblems(program).Length);
            (work[i], bytes[i]) = (stratum.Borrows.Work, stratum.Borrows.Bytes);
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: work {string.Join(", ", work)}, bytes {string.Join(", ", bytes)}");
        Assert.True(Grows(work, 5), $"work {string.Join(", ", work)}");
        Assert.True(Grows(bytes, 5), $"bytes {string.Join(", ", bytes)}");
    }

    // V16 (DECISIONS R4-M132; G:A5-F4's group review N2, after the 2a-2 review's A-B1): rule W's Bytes stay within a constant c times the
    // W-free run's on A-B1's shape (N locals borrowed, each stored through a parameter and read through it after the store, each conflict
    // derived), against its twin storing into a local slot (no Refuted relation, so no W element, no marker and no conflict). Measured
    // 3.87, 3.94 and 3.97 at N = 32, 64 and 128, tending to 4; c = 4.5 leaves 13% over the largest. A-B1's cubic form (one marker added at
    // a time) would grow the ratio with N.
    [Fact]
    public void RuleWsBytesStayWithinAConstantOfItsWFreeTwin()
    {
        const string Each = "    var x{0} = {0}\n    var r{0} = x{0}@ref\n    p@follow = r{0}\n    inspect(p@follow)\n";
        (long Bytes, int Problems, int Derived) Measure(string head, int size)
        {
            var text = new StringBuilder(Prelude).Append(head);
            for (var n = 0; n < size; n++)
            {
                text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, Each, n);
            }

            var c = MirBuildTest.Compile(text.ToString());
            var program = c.BuildMir();
            Assert.Empty(program.Findings);
            var stratum = new Stratum(c.Types);
            stratum.Run(program, Body(program, "shape"));
            return (stratum.Borrows.Bytes, program.Problems.All.ToArray().Count(static x => LoanCodes.Contains(x.Code)), DerivedProblems(program).Length);
        }

        var ratios = new List<double>();
        foreach (var size in new[] { 32, 64, 128 })
        {
            var ruled = Measure("func shape(p: uniq/(ref/i32 during a))\n", size);
            var free = Measure("func shape()\n    var y = 0\n    var slot: ref/i32 = y@ref\n    let p = slot@uniq\n", size);
            Assert.Equal((size, size, 0, 0), (ruled.Problems, ruled.Derived, free.Problems, free.Derived));
            ratios.Add((double)ruled.Bytes / free.Bytes);
            output.WriteLine($"N {size}: bytes {ruled.Bytes} with rule W, {free.Bytes} without");
        }

        Assert.All(ratios, static x => Assert.True(x <= 4.5, x.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    // DECISIONS R4-M132 (the gate, the design review's row 2): rule W sets nothing up in a body MirOrigins finds no Refuted relation in,
    // so a valid body's Work and Bytes are the same whether or not another body of the program has one.
    [Fact]
    public void AValidBodyCostsTheSameBesideARefutedRelation()
    {
        const string Valid = "func shape(flag: bool)\n    var x = 1\n    var r = x@ref\n    var t = (r, r)\n    inspect(t.0)\n    var y = r\n" +
            "    if flag => y = x@ref\n    inspect(y)\n";
        long[] Measure(string source)
        {
            var c = MirBuildTest.Compile(Prelude + source);
            var program = c.BuildMir();
            var stratum = new Stratum(c.Types);
            stratum.Run(program, Body(program, "shape"));
            return [stratum.Borrows.Work, stratum.Borrows.Bytes];
        }

        Assert.Equal(Measure(Valid), Measure(Valid + CausedSources["m-store-v"]));
    }

    // V16: rule W on a program version it checked before allocates nothing: its elements, markers, memo and causes keep their storage,
    // over derived conflicts of every kind of carrier (a return, two paths, a loop, err(ended), a read-through, a container, a write-through)
    // and a Chain row's read through E.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRuleWAllocatesNothing()
    {
        string[] names = ["j-region", "two-flows-v", "loop-store-v", "rv-g-param-i", "content-retention", "f02fb5c218bc", "d4a09871a00b", "merge-two-paths-v",
            "chain-through-e"];
        var c = MirBuildTest.Compile(string.Concat(names.Select(static x => CausedSources[x])) + "public func main() => ()\n");
        var program = c.BuildMir();
        var stratum = new Stratum(c.Types);
        Assert.Equal(0, AllocationMeasurement.Measure(() => stratum.Run(program)));
        Assert.Equal(9, DerivedProblems(program).Length);
    }

    // A program's Loan problems as rule W reads them: function, the subject's first line, the code unless a comparison's, and its causes'
    // subjects; the MIR the driver analyzed, with no validator finding.
    private static string[] Caused(string source)
    {
        var c = MirBuildTest.Compile(source + "public func main() => ()\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        return [.. program.Problems.All.ToArray().Select(static (x, i) => (Problem: x, Index: i))
            .Where(static x => LoanCodes.Contains(x.Problem.Code) && x.Problem.Subject.Module == 0)
            .Select(x => $"{Function(c.SyntaxOf(x.Problem.Subject))}: {Line(c, x.Problem.Subject)}{Code(x.Problem.Code)}" + Causes(c, program, x.Index))];

        static string Code(DiagnosticCode code) => code == DiagnosticCode.ComparisonLoanConflict_Kd ? string.Empty : " " + code;
    }

    // The problems rule W derived, with Cause facts (their tail).
    private static int[] DerivedProblems(MirProgram program)
        => [.. Enumerable.Range(0, program.Problems.All.Length).Where(x => program.Problems.Facts(x) is [.., { Kind: MirFactKind.Cause }])];

    // A problem's causes' subjects, after " <- ", none for a direct problem.
    private static string Causes(Compilation c, MirProgram program, int problem)
    {
        var causes = program.Problems.Facts(problem).ToArray().Where(static x => x.Kind == MirFactKind.Cause).Select(x => Line(c, x.Site)).ToArray();
        return causes.Length == 0 ? string.Empty : " <- " + string.Join("; ", causes);
    }
}
