// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// PLAN G641 kind 4 (R3-U2b): destroying a partly moved value destroys only its remaining components (SPEC 16.3.2), and a destruction
// checks the layout of what it destroys and no more (SPEC 8.15.3), on a runtime or an assumed path (SPEC 14.10.3, 8.15.3 paragraph 2). The
// valid rows move every component whose element fails out of a drop-free struct, a nested one, a fixed array, a base, a generic instance,
// on both branches or by a dead return, or leave only components that lay out: they fail on R3-U2a with R3-U1 (InvalidCLayout_Kd at the
// parameter, or at a dead local). Each twin destroys a remaining failing component, or a drop-bearing layer whose drop instance fails
// (SPEC 16.3.2 row 3, 15.1.3), and keeps its record at that use alone. Rows are AssertUses' (LayoutAttributeBindingTest.CLayoutUses.cs).
public partial class LayoutAttributeBindingTest
{
    private const string PartialTwo = "struct Two\n    public var a: Array<BadA>\n    public var b: Array<BadA>\n";

    private const string PartialDead = "func mk() -> Two => $abort(\"no\")\nfunc mkA() -> Array<BadA> => $abort(\"no\")\n";

    private const string PartialDrop = "struct G<T>\n    public var a: Array<i32>\n    public var n: i32\n    drop\n        let o: Option<T> = .None\n";

    private const string PartialPair = "struct P<T>\n    public var a: Array<T>\n    public var b: Array<T>\n";

    private const string PartialBase = "open struct B\n    public var x: Array<BadA>\nstruct D : B\n    public var d: Array<BadA>\n";

    private const string PartialDA = "struct DA\n    public var d: Dictionary<Bad, i32>\n    public var a: Array<BadA>\n";

    private const string PartialDV = "struct DV\n    public var d: Dictionary<i32, Bad>\n    public var a: Array<i32>\n";

    private const string PartialOuter = "struct Outer\n    public var inner: Two\n    public var c: Array<BadA>\n";

    public static TheoryData<string, string, string> PartialMoveRows => new()
    {
        {
            "V_openAllMoved",
            HandleBadA + PartialTwo + "func f(t: Two) -> (Array<BadA>, Array<BadA>)\n    return (t.a@move, t.b@move)\nlet zz = 1\n",
            string.Empty
        },
        { "I_openOne", HandleBadA + PartialTwo + "func f(t: Two) -> Array<BadA>\n    return t.a@move\nlet zz = 1\n", "InvalidCLayout_Kd t: Two)" },
        {
            "V_openOkLeft",
            HandleBadA + "struct Mix\n    public var a: Array<BadA>\n    public var b: Array<i32>\nfunc f(t: Mix) -> Array<BadA>\n    return t.a@move\n" +
                "let zz = 1\n",
            string.Empty
        },
        {
            "V_openMixed",
            HandleBadA + "struct Mix\n    public var a: Array<BadA>\n    public var n: i32\nfunc f(t: Mix) -> Array<BadA>\n    return t.a@move\nlet zz = 1\n",
            string.Empty
        },
        {
            "V_openNested",
            HandleBadA + PartialTwo + PartialOuter + "func f(t: Outer) -> (Array<BadA>, Array<BadA>, Array<BadA>)\n" +
                "    return (t.inner.a@move, t.inner.b@move, t.c@move)\nlet zz = 1\n",
            string.Empty
        },
        {
            "I_openNestedOne",
            HandleBadA + PartialTwo + PartialOuter + "func f(t: Outer) -> (Array<BadA>, Array<BadA>)\n    return (t.inner.a@move, t.c@move)\nlet zz = 1\n",
            "InvalidCLayout_Kd t: Outer)"
        },
        {
            "V_openDict",
            HandleBad + HandleBadA + PartialDA + "func f(t: DA) -> (Dictionary<Bad, i32>, Array<BadA>)\n    return (t.d@move, t.a@move)\nlet zz = 1\n",
            string.Empty
        },
        {
            "I_openDictOne",
            HandleBad + HandleBadA + PartialDA + "func f(t: DA) -> Dictionary<Bad, i32>\n    return t.d@move\nlet zz = 1\n",
            "InvalidCLayout_Kd t: DA)"
        },
        {
            "V_deadOpenAll",
            HandleBadA + PartialTwo + "func f(t: Two) -> (Array<BadA>, Array<BadA>)\n    return (t.a@move, t.b@move)\n    let n = 1\nlet zz = 1\n",
            string.Empty
        },
        {
            "I_deadOpenKeep",
            HandleBadA + PartialTwo + "func f(t: Two) -> (Array<BadA>, Array<BadA>)\n    return (t.a@move, t.b@move)\n    let w: Option<Two> = .None\n" +
                "let zz = 1\n",
            "InvalidCLayout_Kd let w: Option<Two> = .None"
        },
        {
            "I_deadOpenU",
            HandleBadA + PartialTwo + "func f(t: Two, u: Two) -> (Array<BadA>, Array<BadA>)\n    return (t.a@move, t.b@move)\n    let x = u.a@move\n" +
                "let zz = 1\n",
            "InvalidCLayout_Kd u: Two)|InvalidCLayout_Kd let x = u.a@move"
        },
        {
            "T_condAll",
            HandleBadA + PartialTwo + "func f(t: Two, c: bool) -> (Array<BadA>, Array<BadA>)\n    var r: Array<BadA>\n    if c\n        r = t.a@move\n" +
                "    else\n        r = t.a@move\n    return (r@move, t.b@move)\nlet zz = 1\n",
            string.Empty
        },
        {
            "T_condLeft",
            HandleBadA + PartialTwo + "func f(t: Two, c: bool) -> (Array<BadA>, Array<BadA>)\n    var r: Array<BadA>\n    if c\n        r = t.a@move\n" +
                "    else\n        r = []\n    return (r@move, t.b@move)\nlet zz = 1\n",
            "InvalidCLayout_Kd t: Two, c: bool)"
        },
        {
            "T_fixAll",
            HandleBadA + "func f(t: [2 of Array<BadA>]) -> (Array<BadA>, Array<BadA>)\n    return (t[0]@move, t[1]@move)\nlet zz = 1\n",
            string.Empty
        },
        {
            "T_fixOne",
            HandleBadA + "func f(t: [2 of Array<BadA>]) -> Array<BadA>\n    return t[0]@move\nlet zz = 1\n",
            "InvalidCLayout_Kd t: [2 of Array<BadA>])"
        },
        {
            "T_baseAll",
            HandleBadA + PartialBase + "func f(t: D) -> (Array<BadA>, Array<BadA>)\n    return (t.d@move, t.x@move)\nlet zz = 1\n",
            string.Empty
        },
        { "T_baseLeft", HandleBadA + PartialBase + "func f(t: D) -> Array<BadA>\n    return t.d@move\nlet zz = 1\n", "InvalidCLayout_Kd t: D)" },
        {
            "T_genAll",
            HandleBadA + PartialPair + "func h<T>(t: P<T>) -> (Array<T>, Array<T>)\n    return (t.a@move, t.b@move)\n" +
                "func f(t: P<BadA>) -> (Array<BadA>, Array<BadA>)\n    return h(t@move)\nlet zz = 1\n",
            string.Empty
        },
        {
            "T_genLeft",
            HandleBadA + PartialPair + "func h<T>(t: P<T>) -> Array<T>\n    return t.a@move\nfunc f(t: P<BadA>) -> Array<BadA>\n    return h(t@move)\nlet zz = 1\n",
            "InvalidCLayout_Kd t: P<T>)"
        },
        {
            "T_dictValGone",
            HandleBad + PartialDV + "func f(t: DV) -> Dictionary<i32, Bad>\n    return t.d@move\nlet zz = 1\n",
            string.Empty
        },
        { "T_dictValLeft", HandleBad + PartialDV + "func f(t: DV) -> Array<i32>\n    return t.a@move\nlet zz = 1\n", "InvalidCLayout_Kd t: DV)" },
        {
            "T_deadRetAll",
            HandleBadA + PartialTwo + PartialDead + "func f(t: Two) -> (Array<BadA>, Array<BadA>)\n    return (t.a@move, t.b@move)\n" +
                "    let w = mk()\n    return (w.a@move, w.b@move)\nlet zz = 1\n",
            string.Empty
        },
        {
            "T_deadRet",
            HandleBadA + PartialTwo + PartialDead + "func f(t: Two) -> (Array<BadA>, Array<BadA>)\n    return (t.a@move, t.b@move)\n" +
                "    let w = mk()\n    return (w.a@move, mkA())\nlet zz = 1\n",
            "InvalidCLayout_Kd let w = mk()"
        },
        {
            "T_deadFlag",
            HandleBadA + PartialTwo + PartialDead + "func f(t: Two, c: bool) -> (Array<BadA>, Array<BadA>)\n    return (t.a@move, t.b@move)\n" +
                "    let w = mk()\n    if c\n        return (w.a@move, w.b@move)\n    return (mkA(), mkA())\nlet zz = 1\n",
            "InvalidCLayout_Kd let w = mk()"
        },
        {
            "T_dtorGen",
            HandleBadA + PartialDrop + "func mk() -> G<BadA> => $abort(\"no\")\nfunc f(t: Array<i32>) -> Array<i32>\n    return t@move\n" +
                "    let h = mk()\n    return h.a@move\nlet zz = 1\n",
            "InvalidCLayout_Kd let o: Option<T> = |InvalidCLayout_Kd .None\nfunc mk"
        },
        {
            "T_dtorNested",
            HandleBadA + PartialDrop + "struct O\n    public var g: G<BadA>\n    public var c: Array<i32>\nfunc mk() -> O => $abort(\"no\")\n" +
                "func f(t: Array<i32>) -> Array<i32>\n    return t@move\n    let o = mk()\n    return o.g.a@move\nlet zz = 1\n",
            "InvalidCLayout_Kd let o: Option<T> = |InvalidCLayout_Kd .None\nstruct O"
        },
    };

    [Theory]
    [MemberData(nameof(PartialMoveRows))]
    public void ADropOfAPartlyMovedValueChecksOnlyWhatItDestroys(string form, string source, string records)
        => AssertUses(form, source, records);
}
