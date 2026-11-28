// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>A5's storing rule (PLAN G581-1; SPEC 5.2.1 Retained dependencies, 15.6.4 rule A; the owner's O-56 (a), O-62 (a), O-72 (a),
/// O-110 (a)): a safe body's direct store of an input's own part where its own contract refuses it is UnretainedRawStore_Kd; a self-link,
/// another input's part, a stated layer, a redirected landing and every named silence are not; the record lies at the store, with
/// no fact (DECISIONS Q13).</summary>
public sealed partial class MirBorrowsTest
{
    // An object payload's Type: storage an input's layer reaches through an own step is other storage (SPEC 5.2.1, 15.6.2).
    private const string PayloadType = "struct PR\n    public var p: raw/u8\n    public var q: u8\n    public init()\n        self.p = null\n" +
        "        self.q = 0\n";

    // A payload Field holding a stated (`during self`) and an unstated raw layer, written whole (SPEC 5.7: element by element).
    private const string MixedType = "struct NS3\n    public var pair: (raw/u8 during self, raw/u8)\n    public var q: u8\n    public init()\n" +
        "        self.pair = (null, null)\n        self.q = 0\nfunc st(t: uniq/(obj/NS3))\n";

    private static readonly Dictionary<string, string> StoringForms = new()
    {
        // Invalid (SPEC 5.2.1, 15.6.4 rule A): an input's own part certainly stored past it, in another of its layers' storage, or in
        // storage an own step reaches (an object payload: other storage, never a self-link, also for the payload's own part; O-124 (a))
        // where the destination's raw layers all state no Origin (O-190 (b)).
        ["direct"] = "func st(t: uniq/(uniq/(raw/u8) during a))\n    t@follow@follow = t@follow@raw@raw/u8",
        ["field"] = "struct SR {s}\n    public var r: uniq/(raw/u8) during s\n    public init(r: uniq/(raw/u8) during s) => self.r = r\n" +
            "func st(h: uniq/(SR during b))\n    h@follow.r@follow = h@follow@raw@raw/u8",
        ["method"] = "struct SR {s}\n    public var r: uniq/(raw/u8) during s\n    public init(r: uniq/(raw/u8) during s) => self.r = r\n" +
            "    public func st(self: uniq/Self)\n        self.r@follow = self@follow@raw@raw/u8",
        ["tuple"] = "func st(t: uniq/(uniq/((raw/u8, i32)) during a))\n    t@follow@follow = (t@follow@raw@raw/u8, 0)",
        ["array"] = "func st(t: uniq/(uniq/([2 of raw/u8]) during a))\n    t@follow@follow = [t@follow@raw@raw/u8, null]",
        ["cast"] = "func st(t: uniq/(uniq/(raw/u8) during a))\n    t@follow@follow = t@follow@raw@raw/(raw/u16)@raw/u8",
        ["displaced"] = "func st(t: uniq/(uniq/(raw/u8) during a))\n    var v: raw/u8 = null\n    unsafe => v = t@follow@raw@raw/u8 + 0\n" +
            "    t@follow@follow = v",
        ["localRoot"] = "func st(t: uniq/(uniq/(raw/u8) during a))\n    let v = t@follow@raw@raw/u8\n    let r = t@follow@follow@uniq\n    r@follow = v",
        ["loop"] = "func st(t: uniq/(uniq/(raw/u8) during a))\n    var n: i32 = 0\n    while n < 2\n        t@follow@follow = t@follow@raw@raw/u8\n" +
            "        n = n + 1",
        ["readFirst"] = "func st(t: uniq/(uniq/(raw/u8) during a))\n    var m: raw/u8 = null\n    m = t@follow@follow\n" +
            "    t@follow@follow = t@follow@raw@raw/u8",
        ["replaced"] = "func st(t: uniq/(uniq/(raw/u8) during a), u: uniq/(raw/u8))\n    var v: raw/u8 = u@follow@raw@raw/u8\n" +
            "    v = t@follow@raw@raw/u8\n    t@follow@follow = v",
        ["structField"] = "struct NR\n    public var a: raw/u8\n    public init() => self.a = null\n" +
            "func st(t: uniq/(uniq/NR during a))\n    t@follow@follow.a = t@follow@raw@raw/u8",
        ["generic"] = "func st<T>(t: uniq/(uniq/(raw/T) during a))\n    t@follow@follow = t@follow@raw@raw/T",
        ["threeLayers"] = "func st(t: uniq/(uniq/(uniq/(raw/u8) during a) during a))\n    t@follow@follow@follow = t@follow@raw@raw/u8",
        ["scalarCall"] = "func take(n: i32) => ()\nfunc st(t: uniq/(uniq/(raw/u8) during a))\n    take(5)\n    t@follow@follow = t@follow@raw@raw/u8",
        ["twoLayers"] = "func st(p: (uniq/(raw/u8) during a, uniq/u8 during a))\n    p.0@follow = p.1@follow@raw@raw/u8",
        ["rawPart"] = "func st(p: (uniq/(raw/u8) during a, raw/u8))\n    p.0@follow = p.1",
        ["objPayload"] = PayloadType + "func st(t: uniq/(obj/PR))\n    t@follow.p = t@follow@raw@raw/u8",
        ["payloadSelf"] = PayloadType + "func st(t: uniq/(obj/PR))\n    t@follow.p = t@follow.q@raw@raw/u8",
        ["slotPayload"] = PayloadType + "func st(t: uniq/((raw/u8, obj/PR)))\n    t@follow.0 = t@follow.1.q@raw@raw/u8",
        ["payloadLocal"] = PayloadType + "func st(t: uniq/(obj/PR))\n    let v = t@follow@raw@raw/u8\n    let r = t@follow@uniq\n    r@follow.p = v",
        ["payloadBorrow"] = PayloadType + "func st(t: uniq/(obj/PR))\n    let v = t@follow.q@raw@raw/u8\n    let r = t@follow.p@uniq\n    r@follow = v",
        ["mixedOtherLayer"] = "struct SM3\n    public var pair: (raw/u8 during self, raw/u8)\n    public init() => self.pair = (null, null)\n" +
            "func st(p: (uniq/SM3 during a, uniq/u8 during a))\n    p.0@follow.pair = (null, p.1@follow@raw@raw/u8)",
        ["twoRaw"] = "func st(t: uniq/(uniq/(raw/(raw/u8)) during a))\n    t@follow@follow = t@follow@raw@raw/(raw/u8)",
        ["dropGone"] = "func st(t: uniq/(uniq/(raw/u8) during a), s: raw/u8)\n    t@follow@follow = t@follow@raw@raw/u8",
        ["dropKept"] = "func st(t: uniq/(uniq/(raw/u8) during a), s: string)\n    t@follow@follow = t@follow@raw@raw/u8",
        ["local"] = "func st(t: uniq/(uniq/(raw/u8) during a))\n    let v: raw/u8 = t@follow@raw@raw/u8\n    t@follow@follow = v",
        ["castLocal"] = "func st(t: uniq/(uniq/(raw/u8) during a))\n    let v: raw/u8 = t@follow@raw@raw/u8\n    t@follow@follow = v@raw/u8",
        ["refBeside"] = "func st<U>(t: uniq/(uniq/(raw/u8) during a), w: ref/U)\n    t@follow@follow = t@follow@raw@raw/u8",

        // Invalid under an identity premise (SPEC 8.2.3: T is raw/u8 in st's scope, so each means what its spelled form above means; PLAN
        // G355's interim readers, G986): the store, also where only the unpremised Type would need a drop (dropGone) or hold a pointer
        // (dropKept, a string's destruction), and a by-value raw part spelled T.
        ["id"] = "func st<T>(t: uniq/(uniq/T during a))\n    T is raw/u8\n    t@follow@follow = t@follow@raw@raw/u8",
        ["idLocal"] = "func st<T>(t: uniq/(uniq/T during a))\n    T is raw/u8\n    let v: T = t@follow@raw@raw/u8\n    t@follow@follow = v",
        ["idTwoRaw"] = "func st<T>(t: uniq/(uniq/T during a))\n    T is raw/(raw/u8)\n    t@follow@follow = t@follow@raw@raw/(raw/u8)",
        ["idThreeLayers"] = "func st<T>(t: uniq/(uniq/(uniq/T during a) during a))\n    T is raw/u8\n    t@follow@follow@follow = t@follow@raw@raw/u8",
        ["idProjection"] = "contract HasItem\n    associate Item\nfunc st<I>(t: uniq/(uniq/(I.Item) during a))\n    I is HasItem\n" +
            "    I.Item is raw/u8\n    t@follow@follow = t@follow@raw@raw/u8",
        ["idDropGone"] = "func st<T>(t: uniq/(uniq/(raw/u8) during a), s: T)\n    T is raw/u8\n    t@follow@follow = t@follow@raw@raw/u8",
        ["idDropKept"] = "func st<T>(t: uniq/(uniq/(raw/u8) during a), s: T)\n    T is string\n    t@follow@follow = t@follow@raw@raw/u8",
        ["idRawPart"] = "func st<T>(p: (uniq/(raw/u8) during a, T))\n    T is raw/u8\n    p.0@follow = p.1",
        ["idCast"] = "func st<T>(t: uniq/(uniq/(raw/u8) during a))\n    T is raw/u8\n    let v: T = t@follow@raw@raw/u8\n    t@follow@follow = v@raw/u8",
        ["idRefBeside"] = "func st<T, U>(t: uniq/(uniq/T during a), w: ref/U)\n    T is raw/u8\n    t@follow@follow = t@follow@raw@raw/u8",

        // Invalid beside a Drop A2 elides in every view (`w: s/U` under `s is ref or uniq`, or moved out), or in the view A5 checks (a
        // body a view decides is checked in each view; one no view decides in view 0 only, PLAN G1507): a Drop that destroys nothing
        // redirects nothing (MirDrops.Elides).
        ["pair"] = "func st<s/U>(t: uniq/(uniq/(raw/u8) during a), w: s/U)\n    s is ref or uniq\n    t@follow@follow = t@follow@raw@raw/u8",
        ["oneViewDecided"] = "func st<s/U>(t: uniq/(uniq/(raw/u8) during a), w: s/U)\n    s is owner or ref\n    let z = w@follow@ref\n" +
            "    t@follow@follow = t@follow@raw@raw/u8",
        ["refObj"] = "func st<s/U>(t: uniq/(uniq/(raw/u8) during a), w: s/U)\n    s is ref or obj\n    t@follow@follow = t@follow@raw@raw/u8",
        ["idPair"] = "func st<T, s/U>(t: uniq/(uniq/T during a), w: s/U)\n    s is ref or uniq\n    T is raw/u8\n" +
            "    t@follow@follow = t@follow@raw@raw/u8",
        ["pairReturned"] = "func st<s/U>(t: uniq/(uniq/(raw/u8) during a), w: s/U during b) -> s/U during b\n    s is ref or uniq\n" +
            "    t@follow@follow = t@follow@raw@raw/u8\n    return w",
        ["ownReturned"] = "func st<U>(t: uniq/(uniq/(raw/u8) during a), w: U) -> U\n    t@follow@follow = t@follow@raw@raw/u8\n    return w@move",

        // Valid: another input's part (rule A), a read through the input (Chain), a self-link (O-56 (a); through objuniq, a dereference,
        // not an own step), a redirected landing; a store into a stated layer, also a payload's `during self` Field (5.2.1, 5.7).
        ["other"] = "func st(t: uniq/(uniq/(raw/u8) during a), u: uniq/(raw/u8))\n    t@follow@follow = u@follow@raw@raw/u8",
        ["reached"] = "func st(t: uniq/(uniq/(raw/u8) during a))\n    t@follow@follow = t@follow@follow@raw@raw/u8",
        ["selfLink"] = "func st(t: uniq/(raw/u8))\n    let v = t@follow@raw@raw/u8\n    let r = t@follow@uniq\n    r@follow = v",
        ["selfLinkPart"] = "func st(p: (uniq/(raw/u8) during a, uniq/u8 during a))\n    p.0@follow = p.0@follow@raw@raw/u8",
        ["fieldPartSelfLink"] = PayloadType + "func st(t: uniq/PR)\n    t@follow.p = t@follow.q@raw@raw/u8",
        ["objuniqSelfLink"] = PayloadType + "func st(t: objuniq/PR)\n    t@follow.p = t@follow.q@raw@raw/u8",
        ["redirectOther"] = "func st(t: uniq/(uniq/(raw/u8) during a), u: uniq/(raw/u8) during a)\n    t@follow = u@follow@uniq\n" +
            "    t@follow@follow = t@follow@raw@raw/u8",
        ["redirectSelfCell"] = "func st(t: uniq/((raw/u8, uniq/(raw/u8) during a)) during a)\n    let v = t@follow@raw@raw/u8\n" +
            "    t@follow.1 = t@follow.0@uniq\n    t@follow.1@follow = v",
        ["redirectSelfLocal"] = "func st(t: uniq/((raw/u8, uniq/(raw/u8) during a)) during a)\n    let v = t@follow@raw@raw/u8\n" +
            "    t@follow.1 = t@follow.0@uniq\n    let r = t@follow.1@follow@uniq\n    r@follow = v",
        ["redirectPastU"] = "func st(t: uniq/(uniq/(raw/u8) during a), u: uniq/(uniq/(raw/u8) during a) during a)\n" +
            "    t@follow = u@follow@follow@uniq\n    t@follow@follow = t@follow@raw@raw/u8",
        ["redirectViaLocal"] = "func st(t: uniq/(uniq/(raw/u8) during a), u: uniq/(raw/u8) during a)\n    let r = t@follow@uniq\n" +
            "    r@follow = u@follow@uniq\n    t@follow@follow = t@follow@raw@raw/u8",
        ["redirectMiddle"] = "func st(t: uniq/(uniq/(uniq/(raw/u8) during a) during a), u: uniq/(raw/u8) during a)\n" +
            "    t@follow@follow = u@follow@uniq\n    t@follow@follow@follow = t@follow@raw@raw/u8",
        ["exposedOther"] = "func st(t: uniq/(uniq/(raw/u8) during a), u: uniq/(raw/u8))\n    var v: raw/u8 = t@follow@raw@raw/u8\n" +
            "    let pv = v@raw\n    unsafe => *pv = u@follow@raw@raw/u8\n    t@follow@follow = v",

        // Valid under an identity premise (SPEC 8.2.3, 15.6.4 rule A): another input's part, each the neighbour of an invalid form above,
        // and SPEC 5.2.1's own self-link spelled T.
        ["idOther"] = "func st<T>(t: uniq/(uniq/T during a), u: uniq/(raw/u8))\n    T is raw/u8\n    t@follow@follow = u@follow@raw@raw/u8",
        ["idTwoRawOther"] = "func st<T>(t: uniq/(uniq/T during a), u: uniq/(raw/(raw/u8)))\n    T is raw/(raw/u8)\n" +
            "    t@follow@follow = u@follow@raw@raw/(raw/u8)",
        ["idThreeLayersOther"] = "func st<T>(t: uniq/(uniq/(uniq/T during a) during a), u: uniq/(raw/u8))\n    T is raw/u8\n" +
            "    t@follow@follow@follow = u@follow@raw@raw/u8",
        ["idProjectionOther"] = "contract HasItem\n    associate Item\nfunc st<I>(t: uniq/(uniq/(I.Item) during a), u: uniq/(raw/u8))\n" +
            "    I is HasItem\n    I.Item is raw/u8\n    t@follow@follow = u@follow@raw@raw/u8",
        ["idDropGoneOther"] = "func st<T>(t: uniq/(uniq/(raw/u8) during a), s: T, u: uniq/(raw/u8))\n    T is raw/u8\n" +
            "    t@follow@follow = u@follow@raw@raw/u8",
        ["idDropKeptOther"] = "func st<T>(t: uniq/(uniq/(raw/u8) during a), s: T, u: uniq/(raw/u8))\n    T is string\n" +
            "    t@follow@follow = u@follow@raw@raw/u8",
        ["idSelfLink"] = "func st<T>(t: uniq/T)\n    T is raw/u8\n    t@follow = t@follow@raw@raw/u8",
        ["idRawPartOther"] = "func st<T>(p: (uniq/(raw/u8) during a, T), u: uniq/(raw/u8))\n    T is raw/u8\n    p.0@follow = u@follow@raw@raw/u8",
        ["idCastOther"] = "func st<T>(t: uniq/(uniq/(raw/u8) during a), u: uniq/(raw/u8))\n    T is raw/u8\n    let v: T = u@follow@raw@raw/u8\n" +
            "    t@follow@follow = v@raw/u8",
        ["idPairOther"] = "func st<T, s/U>(t: uniq/(uniq/T during a), w: s/U, u: uniq/(raw/u8))\n    s is ref or uniq\n    T is raw/u8\n" +
            "    t@follow@follow = u@follow@raw@raw/u8",
        ["pairOther"] = "func st<s/U>(t: uniq/(uniq/(raw/u8) during a), w: s/U, u: uniq/(raw/u8))\n    s is ref or uniq\n" +
            "    t@follow@follow = u@follow@raw@raw/u8",

        // Invalid and silent, named limits (O-62 (a); DECISIONS, STATUS): a join, a part read, a loop head, an exposed holder, a call
        // result, a redirect later in the body, a value read back, Binding's joined record, an `unsafe` scope, an owned input's Drop A2
        // keeps (its destruction may run code), and a view A5 does not run (PLAN G1507: only view 0's, owner, where A2 keeps w's Drop).
        ["join"] = "func st(t: uniq/(uniq/(raw/u8) during a), c: bool)\n    var v: raw/u8 = null\n    if c\n        v = t@follow@raw@raw/u8\n" +
            "    else\n        v = t@follow@raw@raw/u8\n    t@follow@follow = v",
        ["partRead"] = "func st(t: uniq/(uniq/(raw/u8) during a), u: uniq/(raw/u8))\n    let q = (t@follow@raw@raw/u8, u@follow@raw@raw/u8)\n" +
            "    t@follow@follow = q.0",
        ["loopCarried"] = "func st(t: uniq/(uniq/(raw/u8) during a))\n    let v = t@follow@raw@raw/u8\n    var n: i32 = 0\n    while n < 2\n" +
            "        t@follow@follow = v\n        n = n + 1",
        ["exposedOwn"] = "func st(t: uniq/(uniq/(raw/u8) during a), u: uniq/(raw/u8))\n    var v: raw/u8 = u@follow@raw@raw/u8\n" +
            "    let pv = v@raw\n    unsafe => *pv = t@follow@raw@raw/u8\n    t@follow@follow = v",
        ["callResult"] = "func idr(v: raw/u8) -> raw/u8 => v\nfunc st(t: uniq/(uniq/(raw/u8) during a))\n    t@follow@follow = idr(t@follow@raw@raw/u8)",
        ["redirectAfter"] = "func st(t: uniq/(uniq/(raw/u8) during a), u: uniq/(raw/u8) during a)\n    t@follow@follow = t@follow@raw@raw/u8\n" +
            "    t@follow = u@follow@uniq",
        ["readBack"] = "func st(t: uniq/(uniq/(raw/u8) during a), u: uniq/(raw/u8))\n    u@follow = t@follow@raw@raw/u8\n    t@follow@follow = u@follow",
        ["joinedRecord"] = "func st(t: uniq/(uniq/((raw/u8 during a, raw/u8)) during a) during a)\n    t@follow@follow.1 = t@follow@raw@raw/u8",
        ["unsafeScope"] = "func st(t: uniq/(uniq/(raw/u8) during a))\n    unsafe => t@follow@follow = t@follow@raw@raw/u8",
        ["ownDropped"] = "func st<U>(t: uniq/(uniq/(raw/u8) during a), w: U)\n    t@follow@follow = t@follow@raw@raw/u8",
        ["oneView"] = "func st<s/U>(t: uniq/(uniq/(raw/u8) during a), w: s/U)\n    s is owner or ref\n    t@follow@follow = t@follow@raw@raw/u8",
        ["unsafeFn"] = "unsafe func st(t: uniq/(uniq/(raw/u8) during a))\n    t@follow@follow = t@follow@raw@raw/u8",
        ["statedPast"] = "func st(t: uniq/(uniq/(raw/u8 during a) during a) during a)\n    t@follow@follow = t@follow@raw@raw/u8",
        ["statedTupleElem"] = "func st(t: uniq/(uniq/((raw/u8 during a, raw/u8)) during a) during a)\n    t@follow@follow.0 = t@follow@raw@raw/u8",
        ["statedSibling"] = "func st(t: uniq/((uniq/(raw/u8 during a) during a, uniq/(raw/u8) during a)) during a)\n" +
            "    t@follow.0@follow = t@follow@raw@raw/u8",
        ["statedOwnByValue"] = "struct SS {s}\n    public var c: uniq/(raw/u8 during s) during s\n    public var d: uniq/(raw/u8) during s\n" +
            "    public init(c: uniq/(raw/u8 during s) during s, d: uniq/(raw/u8) during s)\n        self.c = c\n        self.d = d\n" +
            "func st(p: SS during a)\n    p.c@follow = p.d@follow@raw@raw/u8",
        ["statedPayload"] = "struct NS\n    public var p: raw/u8 during self\n    public var x: raw/u8\n    public var q: u8\n    public init()\n" +
            "        self.p = null\n        self.x = null\n        self.q = 0\nfunc st(t: uniq/(obj/NS))\n    t@follow.p = t@follow.q@raw@raw/u8",
        ["mixedStated"] = MixedType + "    t@follow.pair = (t@follow@raw@raw/u8, null)",
        ["selfLinkField"] = "func st(p: (uniq/((raw/u8, u8)) during a, i32))\n    p.0@follow.0 = p.0@follow.1@raw@raw/u8",
        ["callStore"] = "func st(t: uniq/(uniq/(raw/u8) during a), u: uniq/(raw/u8))\n    var v: raw/u8 = u@follow@raw@raw/u8\n" +
            "    let old = Kimi.Intrinsics.exchange(v@uniq, with: t@follow@raw@raw/u8)\n    t@follow@follow = v",
        ["partStore"] = "func st(t: uniq/(uniq/((raw/u8, i32)) during a))\n    var q: (raw/u8, i32) = (t@follow@raw@raw/u8, 0)\n    q.1 = 5\n" +
            "    t@follow@follow = q",
        ["closureBody"] = "func st(t: uniq/(uniq/(raw/u8) during a))\n    let v = t@follow@raw@raw/u8\n    let r = t@follow@follow@uniq\n" +
            "    let f = func [r@move, v] () => r@follow = v\n    f@move()",
        ["aggregatePart"] = "func st(p: (uniq/((raw/u8, i32)) during a, (raw/u8, i32)))\n    p.0@follow = p.1",
        ["dropReceiver"] = "struct DR {s}\n    public var r: uniq/(raw/u8) during s\n    public init(r: uniq/(raw/u8) during s) => self.r = r\n" +
            "    drop\n        self.r@follow = self@raw@raw/u8",

        // Invalid and silent, the owner's O-190 (b): at a destination with a stated layer (here a mixed one) the own step is not applied,
        // so the payload reads as the layer's referent and i's own value stored into the unstated layer passes as a self-link (PLAN G581).
        ["mixedUnstated"] = MixedType + "    t@follow.pair = (null, t@follow@raw@raw/u8)",
    };

    [Theory]
    [InlineData("direct", "st: t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("field", "st: h@follow.r@follow = h@follow@raw@raw/u8")]
    [InlineData("method", "st: self.r@follow = self@follow@raw@raw/u8")]
    [InlineData("tuple", "st: t@follow@follow = (t@follow@raw@raw/u8, 0)")]
    [InlineData("array", "st: t@follow@follow = [t@follow@raw@raw/u8, null]")]
    [InlineData("cast", "st: t@follow@follow = t@follow@raw@raw/(raw/u16)@raw/u8")]
    [InlineData("displaced", "st: t@follow@follow = v")]
    [InlineData("localRoot", "st: r@follow = v")]
    [InlineData("loop", "st: t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("readFirst", "st: t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("replaced", "st: t@follow@follow = v")]
    [InlineData("structField", "st: t@follow@follow.a = t@follow@raw@raw/u8")]
    [InlineData("generic", "st: t@follow@follow = t@follow@raw@raw/T")]
    [InlineData("threeLayers", "st: t@follow@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("scalarCall", "st: t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("twoLayers", "st: p.0@follow = p.1@follow@raw@raw/u8")]
    [InlineData("rawPart", "st: p.0@follow = p.1")]
    [InlineData("objPayload", "st: t@follow.p = t@follow@raw@raw/u8")]
    [InlineData("payloadSelf", "st: t@follow.p = t@follow.q@raw@raw/u8")]
    [InlineData("slotPayload", "st: t@follow.0 = t@follow.1.q@raw@raw/u8")]
    [InlineData("payloadLocal", "st: r@follow.p = v")]
    [InlineData("payloadBorrow", "st: r@follow = v")]
    [InlineData("mixedOtherLayer", "st: p.0@follow.pair = (null, p.1@follow@raw@raw/u8)")]
    [InlineData("twoRaw", "st: t@follow@follow = t@follow@raw@raw/(raw/u8)")]
    [InlineData("dropGone", "st: t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("dropKept", "st: t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("local", "st: t@follow@follow = v")]
    [InlineData("castLocal", "st: t@follow@follow = v@raw/u8")]
    [InlineData("refBeside", "st: t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("id", "st: t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("idLocal", "st: t@follow@follow = v")]
    [InlineData("idTwoRaw", "st: t@follow@follow = t@follow@raw@raw/(raw/u8)")]
    [InlineData("idThreeLayers", "st: t@follow@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("idProjection", "st: t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("idDropGone", "st: t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("idDropKept", "st: t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("idRawPart", "st: p.0@follow = p.1")]
    [InlineData("idCast", "st: t@follow@follow = v@raw/u8")]
    [InlineData("idRefBeside", "st: t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("pair", "st: t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("oneViewDecided", "st: t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("refObj", "st: t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("idPair", "st: t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("pairReturned", "st: t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("ownReturned", "st: t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("other", "")]
    [InlineData("reached", "")]
    [InlineData("selfLink", "")]
    [InlineData("selfLinkPart", "")]
    [InlineData("fieldPartSelfLink", "")]
    [InlineData("objuniqSelfLink", "")]
    [InlineData("redirectOther", "")]
    [InlineData("redirectSelfCell", "")]
    [InlineData("redirectSelfLocal", "")]
    [InlineData("redirectPastU", "")]
    [InlineData("redirectViaLocal", "")]
    [InlineData("redirectMiddle", "")]
    [InlineData("exposedOther", "")]
    [InlineData("idOther", "")]
    [InlineData("idTwoRawOther", "")]
    [InlineData("idThreeLayersOther", "")]
    [InlineData("idProjectionOther", "")]
    [InlineData("idDropGoneOther", "")]
    [InlineData("idDropKeptOther", "")]
    [InlineData("idSelfLink", "")]
    [InlineData("idRawPartOther", "")]
    [InlineData("idCastOther", "")]
    [InlineData("idPairOther", "")]
    [InlineData("pairOther", "")]
    [InlineData("join", "")]
    [InlineData("partRead", "")]
    [InlineData("loopCarried", "")]
    [InlineData("exposedOwn", "")]
    [InlineData("callResult", "")]
    [InlineData("redirectAfter", "")]
    [InlineData("readBack", "")]
    [InlineData("joinedRecord", "")]
    [InlineData("unsafeScope", "")]
    [InlineData("ownDropped", "")]
    [InlineData("oneView", "")]
    [InlineData("unsafeFn", "st: t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("statedPast", "")]
    [InlineData("statedTupleElem", "")]
    [InlineData("statedSibling", "")]
    [InlineData("statedOwnByValue", "")]
    [InlineData("statedPayload", "")]
    [InlineData("mixedStated", "")]
    [InlineData("selfLinkField", "")]
    [InlineData("callStore", "")]
    [InlineData("partStore", "")]
    [InlineData("closureBody", "")]
    [InlineData("aggregatePart", "")]
    [InlineData("dropReceiver", "")]
    [InlineData("mixedUnstated", "")]
    public void ABodyStoresAnInputsOwnPartOnlyWhereItsContractRetainsIt(string form, string expected)
    {
        var c = MirBuildTest.Compile(Prelude + StoringForms[form]);
        var program = Analyze(c);
        var found = program.Problems.All.ToArray()
            .Where(static x => !x.IsRecord && x.Subject.Module == 0 && (x.Code == DiagnosticCode.UnretainedRawStore_Kd || LoanCodes.Contains(x.Code)))
            .Select(x => x.Code == DiagnosticCode.UnretainedRawStore_Kd ? $"{Function(c.SyntaxOf(x.Subject))}: {Line(c, x.Subject)}" : $"{x.Code}");
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", found)}]");
        var all = program.Problems.All.ToArray();
        Assert.All(
            Enumerable.Range(0, all.Length).Where(i => all[i].Code == DiagnosticCode.UnretainedRawStore_Kd),
            i => Assert.Equal(0, program.Problems.Facts(i).Length));
    }

    // SPEC 5.2.1, 15.6.4 rule A (the Middle's G408-B A5 ruling C5, twin (d), and its consent amendment 2, superseding consent ruling 3's
    // five records): st's store of t's own part past t is invalid, as "direct"; its input d holds an i64 34 entries down, past
    // StorageBound, and t's written storage and d can each carry a Loan or an address, so st's own contract is Deep (G408B-17). d is by
    // value, so it is dropped at st's exit, and its drop is undecided past the bound (MayNeedDrop fails closed): Redirects' drop clause
    // caps the landing, so st's store is silent on dev and the head (O-62 (a)'s named silence; an open issue). st's raw layer states no
    // Origin and st writes it, so by SPEC 15.6.4 each call retains every argument, d included, and d's first component borrows x within
    // the bound: x's Loan lives in t's referent past x, `var x = 1`'s ComparisonLoanConflict_Kd, a decided row's record beside the call's
    // Deep record (twin (c)'s pattern, SPEC 23.1.2), and likewise y's at h's call. Each caller's records stand.
    [Fact]
    public void ABodyWhoseContractIsDeepStoresSilentlyAndEachCallerRecords()
    {
        var nest = string.Concat(Enumerable.Repeat("(i32, ", 33)) + "i64" + new string(')', 33);
        var source = $"func make(x: ref/i32 during q) -> (ref/i32 during q, {nest}) => $abort(\"x\")\n" +
            $"func st(t: uniq/(uniq/(raw/u8) during a), d: (ref/i32 during q, {nest}))\n    t@follow@follow = t@follow@raw@raw/u8\n" +
            "func g(t: uniq/(uniq/(raw/u8) during a))\n    var x = 1\n    st(t, make(x@ref))\n" +
            "func h(u: uniq/(uniq/(raw/u8) during b))\n    var y = 2\n    st(u, make(y@ref))\n" +
            "public func main() => ()\n";
        Assert.StartsWith("deep", RetentionTest.Declared(MirBuildTest.Compile(source), "st"), StringComparison.Ordinal);
        Assert.Equal(
            "ComparisonLoanConflict_Kd var x = 1; OwnershipDepthLimit_Kd st(t, make(x@ref)); " +
            "ComparisonLoanConflict_Kd var y = 2; OwnershipDepthLimit_Kd st(u, make(y@ref))",
            Records(source));
    }

    // SPEC 5.2.1, 15.6.4 rule A, 8.3.3 (the Middle's G408-B A5 consent ruling 1 and its amendment 2; its twins dstU0, dstFieldU0): a
    // StoringForms body with an input d past StorageBound (an i64 34 entries down) beside the storage it writes, so its own contract is
    // Deep, and no call of it. d is a borrow, which needs no drop. Its referent's parts are no sources of their own (SPEC 15.6.3), so
    // the input fold asks past the bound only at an exclusive layer. d's referent holds one 33 entries down, the first meeting past
    // StorageBound, so d's sources are undecided there and st's own contract is Deep. A by-value d is dropped at st's exit, where
    // MayNeedDrop fails closed, so Redirects caps the landing and the store is silent (O-62 (a); twin (d)); with scalars alone past the
    // bound d is exact (dstU0). "direct" and "field" store t's (h's) own
    // part past it, invalid: the rule refuses on the decided rows, a missing row of a lower bound, so the store is the depth limit, its
    // one record (a Q13 class 4 move from dev's UnretainedRawStore_Kd at the same store). "other" stores u's part, other storage (rule A
    // holds): valid, no record.
    [Theory]
    [InlineData("direct", "OwnershipDepthLimit_Kd t@follow@follow = t@follow@raw@raw/u8")]
    [InlineData("field", "OwnershipDepthLimit_Kd h@follow.r@follow = h@follow@raw@raw/u8")]
    [InlineData("other", "")]
    public void AStoreABodyWithADeepContractRefusesIsTheDepthLimit(string form, string expected)
    {
        var nest = string.Concat(Enumerable.Repeat("(i32, ", 31)) + "uniq/i64 during q" + new string(')', 31);
        var body = StoringForms[form];
        var header = body.IndexOf('\n', body.IndexOf("func st(", StringComparison.Ordinal));
        var source = body[..(header - 1)] + $", d: uniq/(ref/i32 during q, {nest}))" + body[header..] + "\npublic func main() => ()\n";
        Assert.StartsWith("deep", RetentionTest.Declared(MirBuildTest.Compile(source), "st"), StringComparison.Ordinal);
        Assert.Equal(expected, Records(source));
    }

    // Invalid and silent, a named limit (MirBorrows.Storing: an Opaque site rests on an error; DECISIONS Z17, rule W): the landing fact
    // takes the least depth for every input of a body with an Opaque site, whatever the site names, since a part the replaced form reads
    // reaches that form as a value, not as a use: a site naming a literal's temp, one whose right side is read at its own node (its
    // resolved twin, readFirst above, has the record) and one naming nothing, a failed call taking no value (fail-before: the record).
    [Theory]
    [InlineData("    missing = 1\n")]
    [InlineData("    missing = t@follow@follow\n")]
    [InlineData("    missing()\n")]
    public void ABodyWithAnOpaqueSiteStoresNoUnretainedPart(string statement)
    {
        var c = MirBuildTest.Compile(Prelude + "func st(t: uniq/(uniq/(raw/u8) during a))\n" + statement + "    t@follow@follow = t@follow@raw@raw/u8\n");
        Assert.Equal([DiagnosticCode.UnresolvedBinding_Kd], c.Binding.Issues.Select(static x => x.Code));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        new Stratum(c.Types).Run(program);
        Assert.DoesNotContain(
            program.Problems.All.ToArray(),
            static x => x.Subject.Module == 0 && x.Code is DiagnosticCode.UnretainedRawStore_Kd or DiagnosticCode.InternalInvariant_Kd);
    }
}
