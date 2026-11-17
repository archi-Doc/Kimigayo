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

        // Invalid and silent, named limits (O-62 (a); DECISIONS, STATUS): a join, a part read, a loop head, an exposed holder, a call
        // result, a redirect later in the body, a value read back, Binding's joined record, an `unsafe` scope.
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
    [InlineData("join", "")]
    [InlineData("partRead", "")]
    [InlineData("loopCarried", "")]
    [InlineData("exposedOwn", "")]
    [InlineData("callResult", "")]
    [InlineData("redirectAfter", "")]
    [InlineData("readBack", "")]
    [InlineData("joinedRecord", "")]
    [InlineData("unsafeScope", "")]
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
}
