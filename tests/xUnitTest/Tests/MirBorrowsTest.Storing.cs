// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>A5's storing rule (PLAN G581-1; SPEC 5.2.1 Retained dependencies, 15.6.4 rule A; the owner's O-56 (a), O-62 (a), O-72 (a),
/// O-110 (a)): a safe body's direct store of an input's own part where its own contract refuses it is UnretainedRawStore_Kd; a self-link,
/// another input's part, a redirected landing and every named silence are not.</summary>
public sealed partial class MirBorrowsTest
{
    private static readonly Dictionary<string, string> StoringForms = new()
    {
        // Invalid (SPEC 5.2.1, 15.6.4 rule A): an input's own part certainly stored past it, or in another of its layers' storage.
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

        // Valid: another input's part (rule A), a read through the input (Chain), a self-link (O-56 (a)), a redirected landing.
        ["other"] = "func st(t: uniq/(uniq/(raw/u8) during a), u: uniq/(raw/u8))\n    t@follow@follow = u@follow@raw@raw/u8",
        ["reached"] = "func st(t: uniq/(uniq/(raw/u8) during a))\n    t@follow@follow = t@follow@follow@raw@raw/u8",
        ["selfLink"] = "func st(t: uniq/(raw/u8))\n    let v = t@follow@raw@raw/u8\n    let r = t@follow@uniq\n    r@follow = v",
        ["selfLinkPart"] = "func st(p: (uniq/(raw/u8) during a, uniq/u8 during a))\n    p.0@follow = p.0@follow@raw@raw/u8",
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
        // result, a redirect later in the body, a value read back, Binding's joined record, own-step storage, an `unsafe` scope.
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
        ["objPayload"] = "struct PR\n    public var p: raw/u8\n    public init() => self.p = null\nfunc st(t: uniq/(obj/PR))\n" +
            "    t@follow.p = t@follow@raw@raw/u8",
        ["unsafeScope"] = "func st(t: uniq/(uniq/(raw/u8) during a))\n    unsafe => t@follow@follow = t@follow@raw@raw/u8",
    };

    [Theory]
    [InlineData("direct", "st")]
    [InlineData("field", "st")]
    [InlineData("method", "st")]
    [InlineData("tuple", "st")]
    [InlineData("array", "st")]
    [InlineData("cast", "st")]
    [InlineData("displaced", "st")]
    [InlineData("localRoot", "st")]
    [InlineData("loop", "st")]
    [InlineData("replaced", "st")]
    [InlineData("structField", "st")]
    [InlineData("generic", "st")]
    [InlineData("threeLayers", "st")]
    [InlineData("scalarCall", "st")]
    [InlineData("twoLayers", "st")]
    [InlineData("rawPart", "st")]
    [InlineData("other", "")]
    [InlineData("reached", "")]
    [InlineData("selfLink", "")]
    [InlineData("selfLinkPart", "")]
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
    [InlineData("objPayload", "")]
    [InlineData("unsafeScope", "")]
    public void ABodyStoresAnInputsOwnPartOnlyWhereItsContractRetainsIt(string form, string expected)
    {
        var c = MirBuildTest.Compile(Prelude + StoringForms[form]);
        var program = Analyze(c);
        var found = program.Problems.All.ToArray()
            .Where(static x => !x.IsRecord && x.Subject.Module == 0 && (x.Code == DiagnosticCode.UnretainedRawStore_Kd || LoanCodes.Contains(x.Code)))
            .Select(x => x.Code == DiagnosticCode.UnretainedRawStore_Kd ? Function(c.SyntaxOf(x.Subject)) : $"{x.Code}");
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", found)}]");
    }
}
