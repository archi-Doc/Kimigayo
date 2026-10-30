// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// The G216 (a) acceptance tests of conversions (see OriginRelationDiagnosticTest.NestedSignatures.cs): SPEC 15.3.6, 15.3.7, 10.7 and the
// owner's (a) (2026-10-27): a conversion, an erasure or a Callable conformance proves the implementation's conditions from the
// requirement's premises, and a Function Item converted to a Function Type, as a Function value, may assume the target Type's own
// well-formedness, which each call of the target proves; a weaker target stays an error.
public partial class CallableOriginForwardingTest
{
    private const string WeakerHead = "func k(q: ref/i32, r: ref/i32, f: () -> Option<ref/(ref/i32 during q) during r>) -> i32\n    origin r outlives q\n";

    private const string StrongerHead = WeakerHead + "    origin q outlives r\n";

    private const string WeakerTarget = "() -> Option<ref/(ref/i32 during q) during q>";

    private const string UseWeaker = "func use1(q: ref/i32, g: " + WeakerTarget + ") -> i32\n    let o = g()\n    return 0\n";

    private const string WeakerReturn = "func k(q: ref/i32, r: ref/i32, f: () -> Option<ref/(ref/i32 during q) during r>) -> " + WeakerTarget + "\n    origin r outlives q\n";

    private const string WeakerField = "struct H {q}\n    var g: " + WeakerTarget + "\n    public init(g: " + WeakerTarget + ") => self.g = g@move\n";

    private const string ChainHead = "func k(a: ref/i32, b: ref/i32, c: ref/i32, f: (Option<ref/(ref/i32 during a) during c>) -> i32) -> ";

    private const string ChainTarget = "(Option<ref/(ref/i32 during a) during b>) -> i32";

    private const string MeetInputs = "Option<ref/(ref/i32 during a) during b>, Option<ref/(ref/i32 during b) during c>";

    private const string LoneInputs = "Option<ref/(ref/i32 during a) during b>, ref/i32 during c";

    private const string LeakQ = "func leak1(q: ref/i32) -> Option<ref/(ref/i32 during q) during static>\n    return .None\n";

    private const string LeakX = "func leak1(x: ref/i32) -> Option<ref/(ref/i32 during x) during static>\n    return .None\n";

    private const string StaticTarget = "(ref/i32) -> Option<ref/(ref/i32) during static>";

    private const string StaticWeaker = "(ref/i32) -> Option<ref/(ref/i32 during static) during static>";

    private const string UseTarget = "(ref/i32) -> Option<ref/(ref/i32) during q>";

    private const string StaticValue = "func k(q: ref/i32, f: " + StaticTarget + ") -> ";

    private const string ClosureR3 = "func k(q: ref/i32, r: ref/i32) -> i32\n    let c = func [q] () -> Option<ref/(ref/i32 during q) during r>\n" +
        "        let x: ref/i32 during r = q\n        return .None\n";

    private const string LeakClosure = "    let c = func [] (x: ref/i32) -> (Option<ref/(ref/i32 during x) during static>, ref/i32 during static)\n        return (.None, x)\n";

    private const string LeakTarget = "(ref/i32) -> (Option<ref/(ref/i32) during s>, ref/i32 during static)";

    private const string StaticLeakTarget = "(ref/i32) -> (Option<ref/(ref/i32) during static>, ref/i32 during static)";

    private const string LeakApply = "func apply(s: ref/i32, g: " + LeakTarget + ") -> ref/i32 during static\n    let pair = g(s)\n    return pair.1\n" +
        "func k(s: ref/i32) -> ref/i32 during static\n";

    private const string LeakLabel = "the result's well-formedness requires the omitted Origin of ref/i32 outlives static, which is not proven";

    private const string NestedHead = "func k(a: ref/i32, b: ref/i32, c: ref/i32, f: ((ref/(ref/i32 during c) during a) -> i32) -> ";

    private const string NestedTarget = "((ref/(ref/i32 during b) during a) -> i32) -> ";

    private const string Nested2Head = "func k(a: ref/i32, b: ref/i32, c: ref/i32, f: (((ref/(ref/i32 during c) during a) -> i32) -> i32) -> i32) -> i32\n";

    private const string Nested2Target = "    let g: (((ref/(ref/i32 during b) during a) -> i32) -> i32) -> i32 = f@move\n    return 0\n";

    private const string MemberHead = "func k(b: ref/i32, c: ref/i32, f: ((ref/i32 during c) -> i32) -> i32) -> i32\n";

    private const string MemberTarget = "    let g: ((ref/i32 during b) -> i32) -> i32 = f@move\n    return 0\n";

    private const string ErasedClosure = "func [] (x: ref/i32) -> Option<ref/(ref/i32 during x) during static> => .None";

    // Conversions of Function values and Items: a relation the converted signature needs follows from the target's own well-formedness by
    // transitivity (t1c, t1d, t1o, t1j), by a meet (t1p) or combined with a premise of the use (e1, e1a, e1f, e1r); a Closure body assumes
    // its own result's well-formedness (rv1), which each call proves. The invalid twins drop the use premise, take a target that does not
    // give the relation (t1j_n, t1p_n, cz11, cz12) or convert under `origin r outlives q` alone (c2b, c2d, c2e, c2f; inside a tuple or array
    // literal the conversion's own record at the converted value, c2, c4). A conversion at a call argument is the call's
    // NoApplicableOverload_Kd as dev reports t1d_n.
    [Theory]
    [InlineData("rv1", ClosureR3 + "    return 0\n", "")]
    [InlineData("rv1_call", ClosureR3 + "    let o = c()\n    return 0\n", "UnprovenOriginRelation_Kd c()")]
    [InlineData("t1c", ChainHead + "i32\n    origin b outlives c\n    let g: " + ChainTarget + " = f@move\n    return 0\n", "")]
    [InlineData("t1c_n", ChainHead + "i32\n    let g: " + ChainTarget + " = f@move\n    return 0\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData("t1d", ChainHead + "i32\n    origin b outlives c\n    func use(g: " + ChainTarget + ") -> i32 => 0\n    return use(f@move)\n", "")]
    [InlineData("t1d_n", ChainHead + "i32\n    func use(g: " + ChainTarget + ") -> i32 => 0\n    return use(f@move)\n", "NoApplicableOverload_Kd use(f@move)")]
    [InlineData("t1o", ChainHead + ChainTarget + "\n    origin b outlives c\n    return f@move\n", "")]
    [InlineData("t1o_n", ChainHead + ChainTarget + "\n    return f@move\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData("t1j", "func k(a: ref/i32, b: ref/i32, c: ref/i32, f: (" + MeetInputs + ") -> Option<ref/(ref/i32 during a) during c>) -> i32\n    let g: (" + MeetInputs + ") -> Option<ref/(ref/i32 during a) during (c and b)> = f@move\n    return 0\n", "")]
    [InlineData("t1j_n", "func k(a: ref/i32, b: ref/i32, c: ref/i32, f: (" + LoneInputs + ") -> Option<ref/(ref/i32 during a) during c>) -> i32\n    let g: (" + LoneInputs + ") -> Option<ref/(ref/i32 during a) during (c and b)> = f@move\n    return 0\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData("t1p", "func k(a: ref/i32, b: ref/i32, c: ref/i32, f: (Option<ref/(ref/i32 during a) during (b and c)>) -> i32) -> i32\n    let g: " + ChainTarget + " = f@move\n    return 0\n", "")]
    [InlineData("t1p_n", "func k(a: ref/i32, b: ref/i32, c: ref/i32, f: (Option<ref/(ref/i32 during (a and c)) during b>) -> i32) -> i32\n    let g: " + ChainTarget + " = f@move\n    return 0\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData("e1", LeakX + "func k(q: ref/i32) -> i32\n    origin q outlives static\n    let g: " + UseTarget + " = leak1\n    return 0\n", "")]
    [InlineData("e1n", LeakX + "func k(q: ref/i32) -> i32\n    let g: " + UseTarget + " = leak1\n    return 0\n", "UnprovenOriginContract_Kd leak1")]
    [InlineData("e1a", LeakX + "func use(q: ref/i32, g: " + UseTarget + ") -> i32 => 0\nfunc k(q: ref/i32) -> i32\n    origin q outlives static\n    return use(q, leak1)\n", "")]
    [InlineData("e1a_n", LeakX + "func use(q: ref/i32, g: " + UseTarget + ") -> i32 => 0\nfunc k(q: ref/i32) -> i32\n    return use(q, leak1)\n", "NoApplicableOverload_Kd use(q, leak1)")]
    [InlineData("e1f", StaticValue + "i32\n    origin q outlives static\n    let g: " + UseTarget + " = f@move\n    return 0\n", "")]
    [InlineData("e1fn", StaticValue + "i32\n    let g: " + UseTarget + " = f@move\n    return 0\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData("e1r", StaticValue + UseTarget + "\n    origin q outlives static\n    return f@move\n", "")]
    [InlineData("e1r_n", StaticValue + UseTarget + "\n    return f@move\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData("cz5", LeakQ + "func mk(b: ref/i32) -> i32\n    let g: " + StaticTarget + " = leak1\n    return 0\n", "")]
    [InlineData("cz6", LeakQ + "func mk() -> i32\n    let fr = leak1\n    let g: " + StaticTarget + " = fr\n    return 0\n", "")]
    [InlineData("cz7", LeakQ + "func use(g: " + StaticTarget + ") -> i32 => 0\nfunc mk() -> i32\n    return use(leak1)\n", "")]
    [InlineData("cz13", LeakQ + "func mk() -> " + StaticTarget + "\n    return leak1\n", "")]
    [InlineData("cz11", LeakQ + "func mk() -> i32\n    let g: " + StaticWeaker + " = leak1\n    return 0\n", "UnprovenOriginContract_Kd leak1")]
    [InlineData("cz12", "func mk() -> i32\n    let g: " + StaticWeaker + " = func [] (q: ref/i32) -> Option<ref/(ref/i32 during q) during static> => .None\n    return 0\n", "UnprovenOriginContract_Kd func [] (q: ref/i32) -> Option<ref/(ref/i32 during q) during static> => .None")]
    [InlineData("c2b", WeakerHead + "    let g: " + WeakerTarget + " = f@move\n    return 0\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData("c2b_c", StrongerHead + "    let g: " + WeakerTarget + " = f@move\n    return 0\n", "")]
    [InlineData("c2d", UseWeaker + WeakerHead + "    return use1(q, f@move)\n", "NoApplicableOverload_Kd use1(q, f@move)")]
    [InlineData("c2d_c", UseWeaker + StrongerHead + "    return use1(q, f@move)\n", "")]
    [InlineData("c2e", WeakerReturn + "    return f@move\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData("c2e_c", WeakerReturn + "    origin q outlives r\n    return f@move\n", "")]
    [InlineData("c2f", WeakerField + WeakerHead + "    let h = H.init(g: f@move)\n    return 0\n", "NoApplicableOverload_Kd H.init(g: f@move)")]
    [InlineData("c2f_c", WeakerField + StrongerHead + "    let h = H.init(g: f@move)\n    return 0\n", "")]
    [InlineData("c2", WeakerHead + "    let t: (" + WeakerTarget + ", i32) = (f@move, 1)\n    return 0\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData("c2_c", StrongerHead + "    let t: (" + WeakerTarget + ", i32) = (f@move, 1)\n    return 0\n", "")]
    [InlineData("c4", WeakerHead + "    let a: Array<" + WeakerTarget + "> = [f@move]\n    return 0\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData("c4_c", StrongerHead + "    let a: Array<" + WeakerTarget + "> = [f@move]\n    return 0\n", "")]
    public void AConversionAssumesItsTargetsOwnWellFormedness(string form, string body, string records)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, null);

    // G216 (a) T2, U2.1 (G216a-PLAN.md §3; ported from held/g216a-r3's rows and R4's implementation reviews 1-3): SPEC 15.3.7, 15.6.1,
    // 10.7 and the owner's (a): every Callable comparison judges the implementation's own well-formedness from the target's own
    // well-formedness and the use's premises, a Closure's at its erasure after selection: an initialization (e1x), a return (e1ret), a
    // named call's argument (e1arg2) and a value call's argument (v1, R2-N1: at its argument, as e1arg2), each an UnprovenOriginContract_Kd
    // at the Closure, whose body assumes its own result's well-formedness (dev records the body's `(.None, x)`, twins included); e1c (PLAN G277 (2), "same cause": its header fit and its conversion record are two judgments of one comparison) is
    // valid by the target's own well-formedness and the premise, as e1x_c. A comparison nested in another assumes the enclosing required
    // signature's well-formedness (nest1); without it the conversion's one record is Proof class (SPEC 15.6.1: a whole-contract
    // comparison's Origin part is Proven or Unknown, never a Type mismatch) naming the member inside the nested Function Type (nest1_n,
    // R2-B1), a nested member Origin (nest2) and depth 2 (nest2_n). Each twin takes a target or premise that gives the relation.
    [Theory]
    [InlineData("U2.1", "e1x", "func k(s: ref/i32) -> ref/i32 during static\n" + LeakClosure + "    let g: " + LeakTarget + " = c\n    let pair = g(s)\n    return pair.1\n", "UnprovenOriginContract_Kd c", LeakLabel)]
    [InlineData("U2.1", "e1x_c", "func k(s: ref/i32 during static) -> ref/i32 during static\n" + LeakClosure + "    let g: " + StaticLeakTarget + " = c\n    let pair = g(s)\n    return pair.1\n", "", null)]
    [InlineData("U2.1", "e1ret", "func mk(s: ref/i32) -> " + LeakTarget + "\n" + LeakClosure + "    return c\n", "UnprovenOriginContract_Kd c", LeakLabel)]
    [InlineData("U2.1", "e1ret_c", "func mk(s: ref/i32) -> " + StaticLeakTarget + "\n" + LeakClosure + "    return c\n", "", null)]
    [InlineData("U2.1", "e1arg2", LeakApply + LeakClosure + "    return apply(s, c@move)\n", "UnprovenOriginContract_Kd c@move", LeakLabel)]
    [InlineData("U2.1", "e1arg2_c", "func apply(s: ref/i32 during static, g: " + StaticLeakTarget + ") -> ref/i32 during static\n    let pair = g(s)\n    return pair.1\nfunc k(s: ref/i32 during static) -> ref/i32 during static\n" + LeakClosure + "    return apply(s, c@move)\n", "", null)]
    [InlineData("U2.1", "v1", "func k(s: ref/i32, h: (ref/i32, " + LeakTarget + ") -> ref/i32 during static) -> ref/i32 during static\n" + LeakClosure + "    return h(s, c@move)\n", "UnprovenOriginContract_Kd c@move", LeakLabel)]
    [InlineData("U2.1", "v1_c", "func k(s: ref/i32 during static, h: (ref/i32, " + StaticLeakTarget + ") -> ref/i32 during static) -> ref/i32 during static\n" + LeakClosure + "    return h(s, c@move)\n", "", null)]
    [InlineData("U2.1", "e1c", "func k(q: ref/i32) -> i32\n    origin q outlives static\n    let g: (ref/i32) -> Option<ref/(ref/i32) during q> = " + ErasedClosure + "\n    return 0\n", "", null)]
    [InlineData("U2.1", "e1c_n", "func k(q: ref/i32) -> i32\n    let g: (ref/i32) -> Option<ref/(ref/i32) during q> = " + ErasedClosure + "\n    return 0\n", "UnprovenOriginContract_Kd " + ErasedClosure, null)]
    [InlineData("U2.1", "nest1", NestedHead + "Option<ref/(ref/i32 during b) during a>) -> i32\n    origin c outlives b\n    let g: " + NestedTarget + "Option<ref/(ref/i32 during b) during a> = f@move\n    return 0\n", "", null)]
    [InlineData("U2.1", "nest1_n", NestedHead + "i32) -> i32\n    origin c outlives b\n    let g: " + NestedTarget + "i32 = f@move\n    return 0\n", "UnprovenOriginContract_Kd f@move", "the 1st parameter's 1st parameter's well-formedness requires b outlives a, which is not proven")]
    [InlineData("U2.1", "nest2", MemberHead + MemberTarget, "UnprovenOriginContract_Kd f@move", null)]
    [InlineData("U2.1", "nest2_ok", MemberHead + "    origin c outlives b\n" + MemberTarget, "", null)]
    [InlineData("U2.1", "nest2_n", Nested2Head + "    origin c outlives b\n" + Nested2Target, "UnprovenOriginContract_Kd f@move", "the 1st parameter's 1st parameter's 1st parameter requires b outlives c, which is not proven")]
    [InlineData("U2.1", "nest2_c", Nested2Head + "    origin c outlives b\n    origin b outlives c\n" + Nested2Target, "", null)]
    public void EveryCallableComparisonJudgesTheImplementationsWellFormedness(string unit, string form, string body, string records, string? label)
        => OriginRelationDiagnosticTest.AssertRecords($"{form} [{unit}]", body, records, label);

    // Not fixed yet (the owner's pending question about conversion records): a Loan conflict or TransferRequired_Kd independent of a
    // failed conversion stays beside the conversion's record (SPEC 23.1.2: records kept). Each twin's conversion holds and keeps the
    // independent record.
    [Theory]
    [InlineData("c2bg", WeakerHead + OriginRelationDiagnosticTest.IndependentConflict + "    let g: " + WeakerTarget + " = f@move\n    return 0\n", "ComparisonLoanConflict_Kd x = 2; UnprovenOriginContract_Kd f@move")]
    [InlineData("c2bg_c", StrongerHead + OriginRelationDiagnosticTest.IndependentConflict + "    let g: " + WeakerTarget + " = f@move\n    return 0\n", "ComparisonLoanConflict_Kd x = 2")]
    [InlineData("c2dg", UseWeaker + WeakerHead + OriginRelationDiagnosticTest.IndependentConflict + "    return use1(q, f@move)\n", "ComparisonLoanConflict_Kd x = 2; NoApplicableOverload_Kd use1(q, f@move)")]
    [InlineData("c2dg_c", UseWeaker + StrongerHead + OriginRelationDiagnosticTest.IndependentConflict + "    return use1(q, f@move)\n", "ComparisonLoanConflict_Kd x = 2")]
    [InlineData("c2_fnvalue_converted_weaker", WeakerHead + "    let g: " + WeakerTarget + " = f\n    let o = g()\n    return 0\n", "TransferRequired_Kd f; UnprovenOriginContract_Kd f")]
    [InlineData("c2_fnvalue_converted_weaker_c", StrongerHead + "    let g: " + WeakerTarget + " = f\n    let o = g()\n    return 0\n", "TransferRequired_Kd f")]
    [InlineData("c15", WeakerHead + OriginRelationDiagnosticTest.IndependentConflict + "    let t: (" + WeakerTarget + ", i32) = (f@move, 1)\n    return 0\n", "ComparisonLoanConflict_Kd x = 2; UnprovenOriginContract_Kd f@move")]
    [InlineData("c15_c", StrongerHead + OriginRelationDiagnosticTest.IndependentConflict + "    let t: (" + WeakerTarget + ", i32) = (f@move, 1)\n    return 0\n", "ComparisonLoanConflict_Kd x = 2")]
    public void AnIndependentRecordStaysBesideAConversionRecordPendingOwnerConversionRecords(string form, string body, string records)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, null);
}
