// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;
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

    private const string StaticArmType = "() -> Option<ref/i32 during static>";

    private const string JoinHead = "func k(x: ref/i32, c: bool) -> i32\n    let g: " + StaticArmType + " = ";

    private const string LeakArm = "func [] () -> Option<ref/i32 during x> => .None";

    private const string StaticArm = "func [] () -> Option<ref/i32 during static> => .None";

    private const string PairHead = "func k(s: ref/i32, c: bool) -> i32\n    let g: (ref/i32) -> (Option<ref/(ref/i32) during s>, ref/i32 during static) = ";

    private const string PairArm = "func [] (x: ref/i32) -> (Option<ref/(ref/i32 during x) during static>, ref/i32 during static) => (.None, x)";

    private const string PairUnsupported = "Unsupported_Kd ref/(ref/i32 during x) during static";

    private const string PairTargetUnsupported = "Unsupported_Kd ref/(ref/i32) during s";

    private const string UseStatic = "func use(f: (ref/i32) -> ref/i32 during static) -> i32 => 0\nfunc k() -> i32\n";

    private const string HoldStatic = "struct S\n    public let f: (ref/i32) -> ref/i32 during static\n    public init(f: (ref/i32) -> ref/i32 during static) => self.f = f@move\n";

    private const string WeakClosure = "func [] (q: ref/i32) -> ref/i32 => q";

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

    private const string FnC = "((ref/(ref/i32 during c) during a) -> i32) -> i32";

    private const string FnB = "((ref/(ref/i32 during b) during a) -> i32) -> i32";

    private const string ABCHead = "func k(a: ref/i32, b: ref/i32, c: ref/i32, ";

    private const string CB = "    origin c outlives b\n";

    private const string CBA = CB + "    origin b outlives a\n";

    private const string MkParameters = "(x: i32, a: ref/i32, b: ref/i32, c: ref/i32) -> Option<";

    private const string MkC = "func mk" + MkParameters + FnC + ">\n" + CB + "    return .None\n";

    private const string MkT = "func mk<T>(x: T, a: ref/i32, b: ref/i32, c: ref/i32) -> Option<" + FnB + ">\n    return .None\n";

    private const string KHead = "func k(a: ref/i32, b: ref/i32, c: ref/i32) -> i32\n";

    private const string CallMk = KHead + CB + "    let r: Option<" + FnB + "> = mk(1, a, b, c)\n    return 0\n";

    private const string HB = "func h(a: ref/i32, b: ref/i32, c: ref/i32, x: " + FnB + ", n: i32) -> i32\n" + CB + "    return 0\n";

    private const string HC = "func h(a: ref/i32, b: ref/i32, c: ref/i32, x: " + FnC + ", n: i64) -> i32\n    return 1\n";

    private const string KF = "func identity<T>(v: T) -> T => v@move\nfunc k(a: ref/i32, b: ref/i32, c: ref/i32, f: " + FnC + ") -> i32\n" + CB;

    private const string PairFG = "func pair<T>(x: T, y: T) -> i32 => 0\nfunc k(a: ref/i32, b: ref/i32, c: ref/i32, f: " + FnB + ", g: " + FnC + ") -> i32\n";

    private const string HPre = "func h(x: (ref/i32 during b) -> i32, n: i32, b: ref/i32) -> i32 => 0\n";

    private const string KPre = "func k(b: ref/i32, c: ref/i32, f: (ref/i32 during c) -> i32) -> i32\n    origin c outlives b\n    return h(f@move, 1, b)\n";

    private const string CallableApply = "func apply<F>(action: ref/F) -> i32\n    F is Callable<(ref/i32) -> Option<ref/(ref/i32)>>\n    return 0\nfunc k(s: ref/i32) -> i32\n";

    private const string FlatApply = "func apply<F>(f: ref/F) -> i32\n    F is Callable<() -> ref/i32>\n    return 0\n";

    private const string InnerHead = "func k(a: ref/i32, w: ref/(ref/i32 during b)) -> i32\n";

    private const string BOutlivesA = "    origin b outlives a\n";

    private const string BothWays = BOutlivesA + "    origin a outlives b\n";

    private const string InnerClosure = "func [] (p: ref/(ref/i32 during a)) -> i32 => p@follow@follow";

    private const string InnerLet = "    let f: (ref/(ref/i32 during b)) -> i32 = " + InnerClosure + "\n    return f(w)\n";

    private const string UniqHead = "func k(a: ref/i32, w: ref/(uniq/i32 during b)) -> i32\n";

    private const string UniqClosure = "func [] (p: ref/(uniq/i32 during a)) -> i32 => p@follow@follow";

    private const string UniqLet = "    let f: (ref/(uniq/i32 during b)) -> i32 = " + UniqClosure + "\n    return f(w)\n";

    private const string OuterClosure = "func [] (p: ref/i32 during a) -> i32 => p@follow";

    private const string OuterHead = "func k(a: ref/i32, w: ref/i32 during b) -> i32\n";

    private const string OuterLet = "    let f: (ref/i32 during b) -> i32 = " + OuterClosure + "\n    return f(w)\n";

    private const string ValueRun = "func run<T>(f: (T) -> i32, s: T) -> i32\n    return f(s@move)\n";

    private const string CallableHolder = "struct H<F, T>\n    F is Callable<(T) -> i32>\n    public init() => ()\n";

    private const string InnerHolder = "    let o: Option<H<(ref/(ref/i32 during a)) -> i32, ref/(ref/i32 during b)>> = .None\n    return 0\n";

    private const string NestedT = "struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n\n" +
        "    public init(x: ref/i32 during a) => self.x = x\n\n";

    private const string NestedClauseHead = NestedT + "func k(q: ref/i32, f: (() -> Option<T during q>) -> i32) -> i32\n    return 0\n\n";

    private const string ClauseK = NestedT + "func k(q: ref/i32, f: (() -> Option<T during q>) -> i32) -> i32\n" +
        "    origin q outlives static\n    return 0\n\n";

    private const string NestedClauseK = ClauseK + NestedHost;

    private const string NestedF = "f: (() -> Option<T during q>) -> i32";

    private const string ClauseTail = ") -> i32\n    origin q outlives static\n    return 0\n\n" + NestedHost;

    private const string NestedHost = "func host(s: ref/i32) -> i32\n";

    private const string NestedLet = NestedT + NestedHost + "    let f: (() -> Option<T during s>) -> i32 = ";

    private const string NestedWritten = "func [] (g: () -> Option<T during s>) -> i32 => 5";

    private const string StaticHost = "func host(s: ref/i32 during static) -> i32\n";

    private const string Hf = "func hf(x: () -> Option<T during static>) -> i32 => 5\n\n";

    private const string GenericK = NestedT + "func kk<U>(q: ref/U, " + NestedF + ") -> i32\n    origin q outlives static\n    return 0\n\n";

    private const string MethodK = NestedT + "struct H\n    public let n: i32\n\n    public init(n: i32) => self.n = n\n\n" +
        "    public func m(self: ref/Self, q: ref/i32, " + NestedF + ") -> i32\n        origin q outlives static\n        return 0\n\n";

    private const string PlainK = "func k3(q: ref/i32, f: (ref/i32 during q) -> i32) -> i32 => 0\n\n" +
        "func h3(x: ref/i32 during static) -> i32 => 5\n\nfunc h4(x: ref/i32) -> i32 => 5\n\n";

    private const string ClosurePlace = "func kc(f: () -> i32, q: ref/i32) -> i32 => 0\n\n" + NestedHost +
        "    let t = \"a\"\n    let c = func [t@move] () -> i32 => 1\n    let g = kc\n";

    private const string WellFormedOverS = "the 1st parameter's result's well-formedness requires s outlives static, which is not proven";

    private const string InputOverS = "the 1st parameter requires s outlives static, which is not proven";

    private const string PremiseHost = "func host(s: ref/i32, t: ref/i32) -> i32\n";

    private const string TwoAcquisitions = "func kc2(f: () -> i32, v: Array<i32>) -> i32 => 0\n\nfunc host() -> i32\n    let t = \"a\"\n" +
        "    let c = func [t@move] () -> i32 => 1\n    let values: Array<i32> = []\n    let g = kc2\n    return g(c, values)\n";

    private const string PremiseClosure = "    let c = func [] (x: ref/i32 during t) -> i32 => 5\n    let g = k3\n    return g(s, c)\n";

    private const string HolderConstraint = "UnprovenConstraint_Kd H<(ref/(ref/i32 during a)) -> i32, ref/(ref/i32 during b)>";

    private const string LeakMk = "func mk(x: ref/i32) -> (Option<ref/(ref/i32 during x) during static>, ref/i32 during static) => (.None, x)\n";

    private const string LeakUse = "func use(g: " + StaticLeakTarget + ") -> ref/i32 during static\n    let local: i32 = 5\n    let pair = g(local@ref)\n    return pair.1\n";

    private const string LeakUseOk = "func use(g: " + StaticLeakTarget + ", s: ref/i32 during static) -> ref/i32 during static\n    let pair = g(s)\n    return pair.1\n";

    private const string LeakFrom = "func leak() -> ref/i32 during static\n";

    private const string LeakFromStatic = "func leak(s: ref/i32 during static) -> ref/i32 during static\n";

    private const string LocalCall = "    let local: i32 = 5\n    let pair = g(local@ref)\n    return pair.1\n";

    private const string ResultClosure = "    let c = func [] (x: ref/i32) -> (Option<ref/(ref/i32 during x) during static>, i32)\n        return (.None, 1)\n    let pair = c(s)\n    return pair.1\n";

    private const string StaticResultClosure = "    let c = func [] (x: ref/i32 during static) -> (Option<ref/(ref/i32 during x) during static>, i32)\n        return (.None, 1)\n    let pair = c(s)\n    return pair.1\n";

    private const string FixedLeakTarget = "(ref/i32 during static) -> (Option<ref/(ref/i32 during static) during static>, ref/i32 during static)";

    private const string FixedLeakClosure = "    let c = func [] (x: ref/i32 during static) -> (Option<ref/(ref/i32 during x) during static>, ref/i32 during static)\n        return (.None, x)\n";

    private const string FixedUseOk = "func use(g: " + FixedLeakTarget + ", s: ref/i32 during static) -> ref/i32 during static\n    let pair = g(s)\n    return pair.1\n";

    private const string LocalNone = "    let local: i32 = 5\n    let o = g(local@ref)\n    return 0\n";

    private const string NestedLabel = "the 1st parameter's 1st parameter's well-formedness requires b outlives a, which is not proven";

    // Conversions of Function values and Items: a relation the converted signature needs follows from the target's own well-formedness by
    // transitivity (t1c, t1d, t1o, t1j) or by a meet (t1p). A written result relating a per-call Origin beyond its inputs (UseTarget,
    // StaticTarget: e1, e1a, e1f, e1r, cz5, cz6, cz7, cz13) or an enclosing Origin without a clause (ClosureR3: rv1, rv1_call) is
    // recorded where it is written, and a Closure body never assumes it (SPEC 15.3.4, 15.6.1; DECISIONS R4-M162). The invalid twins drop the use premise, take a target that does not
    // give the relation (t1j_n, t1p_n, cz11, cz12, cz14_n; a header that differs from the target only in its Origins is judged by its
    // erasure, SPEC 7.7.4, design-r2 Amendment 3: cz14) or convert under `origin r outlives q` alone (c2b, c2d, c2e, c2f; inside a tuple or array
    // literal the conversion's own record at the converted value, c2, c4). A conversion at a call argument is the selected
    // candidate's comparison record at the argument after selection (t1d_n; dev: NoApplicableOverload_Kd; landing review B1). A head
    // whose result relates Origins no clause relates (WeakerHead, LoneInputs) is also its declaration's record (R4-M162); its
    // well-formed form is the valid twin (c2b_c, t1j). Since H5 the MIR decides and publishes its records directly (DECISIONS R5-M184,
    // until A6's 2g-1): rv1's body relation is at its declaration, the MIR's span; on rejected programs (class 4, counted by code under
    // Q13) a located Unsupported_Kd at the annotation's written nested relation (e1n, e1f, e1fn; cz12 at the Closure's) and, at
    // t1j_n's annotation, the meet t1j's (e) row records (the body root's oracle lacks the nested inputs' premises; PLAN G216 (b)).
    // F-a (H3b; the owner's O-2, 「一覧に加える」: H5's (e) list, PLAN G216 (b)), the rows tagged F-a: Binding records nothing, so A5's
    // located Unsupported_Kd at the written nested relation keeps the invalid program rejected where its UnprovenOriginRelation_Kd was
    // (each record's text occurs once in its program, so it is the exact span); U-O3 (BU's R7-O3) moves the Language record to Binding at
    // the same site and re-points these rows back.
    [Theory]
    [InlineData("rv1", ClosureR3 + "    return 0\n", "UnprovenOriginRelation_Kd let x: ref/i32 during r = q; UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("rv1_call", ClosureR3 + "    let o = c()\n    return 0\n", "UnprovenOriginRelation_Kd let x: ref/i32 during r = q; UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("t1c", ChainHead + "i32\n    origin b outlives c\n    let g: " + ChainTarget + " = f@move\n    return 0\n", "")]
    [InlineData("t1c_n", ChainHead + "i32\n    let g: " + ChainTarget + " = f@move\n    return 0\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData("t1d", ChainHead + "i32\n    origin b outlives c\n    func use(g: " + ChainTarget + ") -> i32 => 0\n    return use(f@move)\n", "")]
    [InlineData("t1d_n", ChainHead + "i32\n    func use(g: " + ChainTarget + ") -> i32 => 0\n    return use(f@move)\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData("t1o", ChainHead + ChainTarget + "\n    origin b outlives c\n    return f@move\n", "")]
    [InlineData("t1o_n", ChainHead + ChainTarget + "\n    return f@move\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData("t1j", "func k(a: ref/i32, b: ref/i32, c: ref/i32, f: (" + MeetInputs + ") -> Option<ref/(ref/i32 during a) during c>) -> i32\n    let g: (" + MeetInputs + ") -> Option<ref/(ref/i32 during a) during (c and b)> = f@move\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during a) during (c and b)")] // valid; H5's (e) list (the owner, 2026-11-03, 「推奨どおり」, board O-1: one (e) list for H5): the MIR judges the nested result's meet under the body root's oracle (H3b T36); PLAN G216 (b) / R7-O3 (U-O3's class) restores the acceptance and re-points this row back
    [InlineData("t1j_n", "func k(a: ref/i32, b: ref/i32, c: ref/i32, f: (" + LoneInputs + ") -> Option<ref/(ref/i32 during a) during c>) -> i32\n    let g: (" + LoneInputs + ") -> Option<ref/(ref/i32 during a) during (c and b)> = f@move\n    return 0\n", "UnprovenOriginContract_Kd f@move; UnprovenOriginRelation_Kd ref/(ref/i32 during a) during (c and b); UnprovenOriginRelation_Kd ref/(ref/i32 during a) during c")]
    [InlineData("t1p", "func k(a: ref/i32, b: ref/i32, c: ref/i32, f: (Option<ref/(ref/i32 during a) during (b and c)>) -> i32) -> i32\n    let g: " + ChainTarget + " = f@move\n    return 0\n", "")]
    [InlineData("t1p_n", "func k(a: ref/i32, b: ref/i32, c: ref/i32, f: (Option<ref/(ref/i32 during (a and c)) during b>) -> i32) -> i32\n    let g: " + ChainTarget + " = f@move\n    return 0\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData("e1", LeakX + "func k(q: ref/i32) -> i32\n    origin q outlives static\n    let g: " + UseTarget + " = leak1\n    return 0\n", "Unsupported_Kd ref/(ref/i32) during q")] // F-a, the owner's O-2; U-O3 / R7-O3 restores the Language record
    [InlineData("e1n", LeakX + "func k(q: ref/i32) -> i32\n    let g: " + UseTarget + " = leak1\n    return 0\n", "UnprovenOriginContract_Kd leak1; Unsupported_Kd ref/(ref/i32) during q")]
    [InlineData("e1a", LeakX + "func use(q: ref/i32, g: " + UseTarget + ") -> i32 => 0\nfunc k(q: ref/i32) -> i32\n    origin q outlives static\n    return use(q, leak1)\n", "UnprovenOriginRelation_Kd ref/(ref/i32) during q")]
    [InlineData("e1a_n", LeakX + "func use(q: ref/i32, g: " + UseTarget + ") -> i32 => 0\nfunc k(q: ref/i32) -> i32\n    return use(q, leak1)\n", "NoApplicableOverload_Kd use(q, leak1); UnprovenOriginRelation_Kd ref/(ref/i32) during q")]
    [InlineData("e1f", StaticValue + "i32\n    origin q outlives static\n    let g: " + UseTarget + " = f@move\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32) during static; Unsupported_Kd ref/(ref/i32) during q")]
    [InlineData("e1fn", StaticValue + "i32\n    let g: " + UseTarget + " = f@move\n    return 0\n", "UnprovenOriginContract_Kd f@move; UnprovenOriginRelation_Kd ref/(ref/i32) during static; Unsupported_Kd ref/(ref/i32) during q")]
    [InlineData("e1r", StaticValue + UseTarget + "\n    origin q outlives static\n    return f@move\n", "UnprovenOriginRelation_Kd ref/(ref/i32) during q; UnprovenOriginRelation_Kd ref/(ref/i32) during static")]
    [InlineData("e1r_n", StaticValue + UseTarget + "\n    return f@move\n", "UnprovenOriginContract_Kd f@move; UnprovenOriginRelation_Kd ref/(ref/i32) during q; UnprovenOriginRelation_Kd ref/(ref/i32) during static")]
    [InlineData("cz5", LeakQ + "func mk(b: ref/i32) -> i32\n    let g: " + StaticTarget + " = leak1\n    return 0\n", "Unsupported_Kd ref/(ref/i32) during static")] // F-a, the owner's O-2; U-O3 / R7-O3 restores the Language record
    [InlineData("cz6", LeakQ + "func mk() -> i32\n    let fr = leak1\n    let g: " + StaticTarget + " = fr\n    return 0\n", "Unsupported_Kd ref/(ref/i32) during static")] // F-a, the owner's O-2; U-O3 / R7-O3 restores the Language record
    [InlineData("cz7", LeakQ + "func use(g: " + StaticTarget + ") -> i32 => 0\nfunc mk() -> i32\n    return use(leak1)\n", "UnprovenOriginRelation_Kd ref/(ref/i32) during static")]
    [InlineData("cz13", LeakQ + "func mk() -> " + StaticTarget + "\n    return leak1\n", "UnprovenOriginRelation_Kd ref/(ref/i32) during static")]
    [InlineData("cz11", LeakQ + "func mk() -> i32\n    let g: " + StaticWeaker + " = leak1\n    return 0\n", "UnprovenOriginContract_Kd leak1")]
    [InlineData("cz12", "func mk() -> i32\n    let g: " + StaticWeaker + " = func [] (q: ref/i32) -> Option<ref/(ref/i32 during q) during static> => .None\n    return 0\n", "UnprovenOriginContract_Kd func [] (q: ref/i32) -> Option<ref/(ref/i32 during q) during static> => .None; Unsupported_Kd ref/(ref/i32 during q) during static")]
    [InlineData("cz14", "func k(x: ref/i32) -> i32\n    let g: (ref/i32) -> Option<ref/i32 during x> = func [] (q: ref/i32) -> Option<ref/i32 during static> => .None\n    return 0\n", "")]
    [InlineData("cz14_n", "func k(x: ref/i32) -> i32\n    let g: (ref/i32) -> Option<ref/i32 during static> = func [] (q: ref/i32) -> Option<ref/i32 during x> => .None\n    return 0\n", "UnprovenOriginContract_Kd func [] (q: ref/i32) -> Option<ref/i32 during x> => .None")]
    [InlineData("c2b", WeakerHead + "    let g: " + WeakerTarget + " = f@move\n    return 0\n", "UnprovenOriginContract_Kd f@move; UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("c2b_c", StrongerHead + "    let g: " + WeakerTarget + " = f@move\n    return 0\n", "")]
    [InlineData("c2d", UseWeaker + WeakerHead + "    return use1(q, f@move)\n", "UnprovenOriginContract_Kd f@move; UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("c2d_c", UseWeaker + StrongerHead + "    return use1(q, f@move)\n", "")]
    [InlineData("c2e", WeakerReturn + "    return f@move\n", "UnprovenOriginContract_Kd f@move; UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("c2e_c", WeakerReturn + "    origin q outlives r\n    return f@move\n", "")]
    [InlineData("c2f", WeakerField + WeakerHead + "    let h = H.init(g: f@move)\n    return 0\n", "UnprovenOriginContract_Kd f@move; UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("c2f_c", WeakerField + StrongerHead + "    let h = H.init(g: f@move)\n    return 0\n", "")]
    [InlineData("c2", WeakerHead + "    let t: (" + WeakerTarget + ", i32) = (f@move, 1)\n    return 0\n", "UnprovenOriginContract_Kd f@move; UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("c2_c", StrongerHead + "    let t: (" + WeakerTarget + ", i32) = (f@move, 1)\n    return 0\n", "")]
    [InlineData("c4", WeakerHead + "    let a: Array<" + WeakerTarget + "> = [f@move]\n    return 0\n", "UnprovenOriginContract_Kd f@move; UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("c4_c", StrongerHead + "    let a: Array<" + WeakerTarget + "> = [f@move]\n    return 0\n", "")]
    public void AConversionAssumesItsTargetsOwnWellFormedness(string form, string body, string records)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, null);

    // G216 (a) T2, U2.1 (G216a-PLAN.md §3; ported from held/g216a-r3's rows and R4's implementation reviews 1-3): SPEC 15.3.7, 15.6.1,
    // 10.7 and the owner's (a): every Callable comparison judges the implementation's own well-formedness from the target's own
    // well-formedness and the use's premises, a Closure's at its erasure after selection: an initialization (e1x), a return (e1ret), a
    // named call's argument (e1arg2) and a value call's argument (v1, R2-N1: at its argument, as e1arg2), each an UnprovenOriginContract_Kd
    // at the Closure. Under DECISIONS R4-M162 the Closure's body records `(.None, x)` and a written target relating a per-call Origin
    // beyond its inputs is recorded where it is written, so the twins e1x_c, e1ret_c, e1arg2_c, v1_c and e1c (PLAN G277 (2)) are invalid,
    // and nest1 needs its head's clause. A comparison nested in another assumes the enclosing required
    // signature's well-formedness (nest1); without it the conversion's one record is Proof class (SPEC 15.6.1: a whole-contract
    // comparison's Origin part is Proven or Unknown, never a Type mismatch) naming the member inside the nested Function Type (nest1_n,
    // R2-B1), a nested member Origin (nest2) and depth 2 (nest2_n). Each twin takes a target or premise that gives the relation.
    // G216 (a) T2b, U2.1 (design-r2 §6; R4's design reviews 1 B1 and 2 D2-B1): a nested comparison inside an aggregate is recorded at the
    // aggregate value, its members named from the nearest comparison, at every fit root: an initialization (o1, o1t), a return (o1ret), a
    // Case construction (o1some, at `.Some(f@move)`, the value whose fit fails), an assignment (o1asg), a value call's argument (vo1, at
    // the argument as v1) and a getter's expression body (pg1); each Proof class (SPEC 15.6.1), never TypeMismatch_Kd or
    // NoApplicableOverload_Kd. The Callable proof of a Closure subject judges its well-formedness (cal2) and a whole-contract comparison's
    // Origin part is Unknown at the argument (q3_flat, q6_flat: the owner's G326 answer, 2026-10-31, 「すべて推奨の内容でお願いします」);
    // a conversion failing in several conditions is one record naming the first (two). Since H5 the MIR's records on these rejected
    // programs are published directly (DECISIONS R5-M184, until A6's 2g-1; class 4, counted by code under Q13): a located Unsupported_Kd
    // at the Closure's written nested relation (e1x to v1_c, cal2, e1c_n) and at the annotation's (e1x, e1x_c, e1c_n), and at nest1's
    // annotation the relation its head lacks, judged under the body root's oracle (PLAN G216 (b), as t1j).
    // F-a (H3b; the owner's O-2, 「一覧に加える」: H5's (e) list, PLAN G216 (b)), the rows tagged F-a: Binding records nothing, so A5's
    // located Unsupported_Kd at the written nested relation keeps the invalid program rejected where its UnprovenOriginRelation_Kd was
    // (each record's text occurs once in its program, so it is the exact span); U-O3 (BU's R7-O3) moves the Language record to Binding at
    // the same site and re-points these rows back.
    [Theory]
    [InlineData("U2.1", "e1x", "func k(s: ref/i32) -> ref/i32 during static\n" + LeakClosure + "    let g: " + LeakTarget + " = c\n    let pair = g(s)\n    return pair.1\n", "UnprovenOriginContract_Kd c; UnprovenOriginRelation_Kd (.None, x); " + PairUnsupported + "; Unsupported_Kd ref/(ref/i32) during s", LeakLabel)]
    [InlineData("U2.1", "e1x_c", "func k(s: ref/i32 during static) -> ref/i32 during static\n" + LeakClosure + "    let g: " + StaticLeakTarget + " = c\n    let pair = g(s)\n    return pair.1\n", "UnprovenOriginRelation_Kd (.None, x); " + PairUnsupported + "; Unsupported_Kd ref/(ref/i32) during static", null)]
    [InlineData("U2.1", "e1ret", "func mk(s: ref/i32) -> " + LeakTarget + "\n" + LeakClosure + "    return c\n", "UnprovenOriginContract_Kd c; UnprovenOriginRelation_Kd (.None, x); UnprovenOriginRelation_Kd ref/(ref/i32) during s; " + PairUnsupported, LeakLabel)]
    [InlineData("U2.1", "e1ret_c", "func mk(s: ref/i32) -> " + StaticLeakTarget + "\n" + LeakClosure + "    return c\n", "UnprovenOriginRelation_Kd (.None, x); UnprovenOriginRelation_Kd ref/(ref/i32) during static; " + PairUnsupported, null)]
    [InlineData("U2.1", "e1arg2", LeakApply + LeakClosure + "    return apply(s, c@move)\n", "UnprovenOriginContract_Kd c@move; UnprovenOriginRelation_Kd (.None, x); UnprovenOriginRelation_Kd ref/(ref/i32) during s; " + PairUnsupported, LeakLabel)]
    [InlineData("U2.1", "e1arg2_c", "func apply(s: ref/i32 during static, g: " + StaticLeakTarget + ") -> ref/i32 during static\n    let pair = g(s)\n    return pair.1\nfunc k(s: ref/i32 during static) -> ref/i32 during static\n" + LeakClosure + "    return apply(s, c@move)\n", "UnprovenOriginRelation_Kd (.None, x); UnprovenOriginRelation_Kd ref/(ref/i32) during static; " + PairUnsupported, null)]
    [InlineData("U2.1", "v1", "func k(s: ref/i32, h: (ref/i32, " + LeakTarget + ") -> ref/i32 during static) -> ref/i32 during static\n" + LeakClosure + "    return h(s, c@move)\n", "UnprovenOriginContract_Kd c@move; UnprovenOriginRelation_Kd (.None, x); UnprovenOriginRelation_Kd ref/(ref/i32) during s; " + PairUnsupported, LeakLabel)]
    [InlineData("U2.1", "v1_c", "func k(s: ref/i32 during static, h: (ref/i32, " + StaticLeakTarget + ") -> ref/i32 during static) -> ref/i32 during static\n" + LeakClosure + "    return h(s, c@move)\n", "UnprovenOriginRelation_Kd (.None, x); UnprovenOriginRelation_Kd ref/(ref/i32) during static; " + PairUnsupported, null)]
    [InlineData("U2.1", "e1c", "func k(q: ref/i32) -> i32\n    origin q outlives static\n    let g: (ref/i32) -> Option<ref/(ref/i32) during q> = " + ErasedClosure + "\n    return 0\n", "Unsupported_Kd ref/(ref/i32 during x) during static; Unsupported_Kd ref/(ref/i32) during q", null)] // F-a, the owner's O-2; U-O3 / R7-O3 restores the Language record
    [InlineData("U2.1", "e1c_n", "func k(q: ref/i32) -> i32\n    let g: (ref/i32) -> Option<ref/(ref/i32) during q> = " + ErasedClosure + "\n    return 0\n", "UnprovenOriginContract_Kd " + ErasedClosure + "; Unsupported_Kd ref/(ref/i32 during x) during static; Unsupported_Kd ref/(ref/i32) during q", null)]
    [InlineData("U2.1", "nest1", NestedHead + "Option<ref/(ref/i32 during b) during a>) -> i32\n    origin c outlives b\n    let g: " + NestedTarget + "Option<ref/(ref/i32 during b) during a> = f@move\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during b) during a; UnprovenOriginRelation_Kd ref/(ref/i32 during b) during a", null)]
    [InlineData("U2.1", "nest1_n", NestedHead + "i32) -> i32\n    origin c outlives b\n    let g: " + NestedTarget + "i32 = f@move\n    return 0\n", "UnprovenOriginContract_Kd f@move", "the 1st parameter's 1st parameter's well-formedness requires b outlives a, which is not proven")]
    [InlineData("U2.1", "nest2", MemberHead + MemberTarget, "UnprovenOriginContract_Kd f@move", null)]
    [InlineData("U2.1", "nest2_ok", MemberHead + "    origin c outlives b\n" + MemberTarget, "", null)]
    [InlineData("U2.1", "nest2_n", Nested2Head + "    origin c outlives b\n" + Nested2Target, "UnprovenOriginContract_Kd f@move", "the 1st parameter's 1st parameter's 1st parameter requires b outlives c, which is not proven")]
    [InlineData("U2.1", "nest2_c", Nested2Head + "    origin c outlives b\n    origin b outlives c\n" + Nested2Target, "", null)]
    [InlineData("U2.1", "o1", ABCHead + "o: Option<" + FnC + ">) -> i32\n" + CB + "    let g: Option<" + FnB + "> = o@move\n    return 0\n", "UnprovenOriginContract_Kd o@move", NestedLabel)]
    [InlineData("U2.1", "o1_c", ABCHead + "o: Option<" + FnC + ">) -> i32\n" + CBA + "    let g: Option<" + FnB + "> = o@move\n    return 0\n", "", null)]
    [InlineData("U2.1", "o1t", ABCHead + "t: (" + FnC + ", i32)) -> i32\n" + CB + "    let g: (" + FnB + ", i32) = t@move\n    return 0\n", "UnprovenOriginContract_Kd t@move", null)]
    [InlineData("U2.1", "o1t_c", ABCHead + "t: (" + FnC + ", i32)) -> i32\n" + CBA + "    let g: (" + FnB + ", i32) = t@move\n    return 0\n", "", null)]
    [InlineData("U2.1", "o1ret", ABCHead + "o: Option<" + FnC + ">) -> Option<" + FnB + ">\n" + CB + "    return o@move\n", "UnprovenOriginContract_Kd o@move", null)]
    [InlineData("U2.1", "o1ret_c", ABCHead + "o: Option<" + FnC + ">) -> Option<" + FnB + ">\n" + CBA + "    return o@move\n", "", null)]
    [InlineData("U2.1", "o1some", ABCHead + "f: " + FnC + ") -> i32\n" + CB + "    let g: Option<" + FnB + "> = .Some(f@move)\n    return 0\n", "UnprovenOriginContract_Kd f@move", null)] // D-1's site (the argument `.Some(f@move)`, with the payload related) comes with U2.2; the T2 branch keeps that expectation
    [InlineData("U2.1", "o1some_c", ABCHead + "f: " + FnC + ") -> i32\n" + CBA + "    let g: Option<" + FnB + "> = .Some(f@move)\n    return 0\n", "", null)]
    [InlineData("U2.1", "o1asg", ABCHead + "o: Option<" + FnC + ">, p: Option<" + FnB + ">) -> i32\n" + CB + "    var g = p@move\n    g = o@move\n    return 0\n", "UnprovenOriginContract_Kd o@move", null)]
    [InlineData("U2.1", "o1asg_c", ABCHead + "o: Option<" + FnC + ">, p: Option<" + FnB + ">) -> i32\n" + CBA + "    var g = p@move\n    g = o@move\n    return 0\n", "", null)]
    [InlineData("U2.1", "vo1", ABCHead + "o: Option<" + FnC + ">, h: (Option<" + FnB + ">) -> i32) -> i32\n" + CB + "    return h(o@move)\n", "UnprovenOriginContract_Kd o@move", null)]
    [InlineData("U2.1", "vo1_c", ABCHead + "o: Option<" + FnC + ">, h: (Option<" + FnB + ">) -> i32) -> i32\n" + CBA + "    return h(o@move)\n", "", null)]
    [InlineData("U2.1", "pg1", "struct S {a, b, c}\n" + CB + "    public computed p: Option<" + FnB + ">\n        get() -> Option<" + FnB + "> => self.mk()\n    public init() => ()\n    public func mk(self: ref/Self) -> Option<" + FnC + "> => .None\n", "UnprovenOriginContract_Kd self.mk()", null)] // the selected candidate's comparison record (landing review B1; closes PLAN G376)
    [InlineData("U2.1", "pg1_c", "struct S {a, b, c}\n" + CBA + "    public computed p: Option<" + FnB + ">\n        get() -> Option<" + FnB + "> => self.mk()\n    public init() => ()\n    public func mk(self: ref/Self) -> Option<" + FnC + "> => .None\n", "", null)]
    [InlineData("U2.1", "cal2", CallableApply + "    let c = " + ErasedClosure + "\n    return apply(c@ref)\n", "UnprovenConstraint_Kd c@ref; Unsupported_Kd ref/(ref/i32 during x) during static", null)]
    [InlineData("U2.1", "cal2_c", CallableApply + "    let c = func [] (x: ref/i32) -> Option<ref/(ref/i32 during static) during x> => .None\n    return apply(c@ref)\n", "", null)]
    [InlineData("U2.1", "q3_flat", FlatApply + "func use()\n    var local: i32 = 1\n    let p = local@ref\n    let c = func [p] () => p\n    let r = apply(c@ref)\n", "UnprovenConstraint_Kd c@ref", null)]
    [InlineData("U2.1", "q6_flat", FlatApply + "func use()\n    let n: i32 = 1\n    let c = func [n@ref] () => n\n    let r = apply(c@ref)\n", "UnprovenConstraint_Kd c@ref", null)]
    [InlineData("U2.1", "q3_flat_c", FlatApply + "func use(p: ref/i32 during static)\n    let c = func [p] () => p\n    let r = apply(c@ref)\n", "", null)]
    [InlineData("U2.1", "two", "func k(q: ref/i32, r: ref/i32, f: (ref/i32 during q) -> Option<ref/(ref/i32 during q) during r>) -> i32\n    let g: (ref/i32 during r) -> Option<ref/(ref/i32 during q) during q> = f@move\n    return 0\n", "UnprovenOriginContract_Kd f@move; UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r", "the 1st parameter requires r outlives q, which is not proven")]
    [InlineData("U2.1", "two_c", "func k(q: ref/i32, r: ref/i32, f: (ref/i32 during q) -> Option<ref/(ref/i32 during q) during r>) -> i32\n    origin r outlives q\n    origin q outlives r\n    let g: (ref/i32 during r) -> Option<ref/(ref/i32 during q) during q> = f@move\n    return 0\n", "", null)]
    public void EveryCallableComparisonJudgesTheImplementationsWellFormedness(string unit, string form, string body, string records, string? label)
        => OriginRelationDiagnosticTest.AssertRecords($"{form} [{unit}]", body, records, label);

    // Landing review B1 and its re-check (SPEC 10.1, 10.3, 10.4 step 2, 15.6.1; DECISIONS R4-M155): every fit before selection, of a
    // candidate's result, argument (also a waiting part's late Type) or slot, judges the structural part alone, so neither the
    // implementation's own well-formedness nor an Origin inside a Function Type drops a candidate for another to win; after selection the
    // whole contract is the record at the result or the argument. sel2 (its method form sel1) and asel (its waiting form aselw) stay
    // ambiguous, as their controls sel2ctl, sel2cba and aselcba; a lone candidate (sel2one, asel1, aselw1, apre1) and the strictly best
    // i32 candidate (sel4run, as dev selects it) are selected and their comparison is the record; pre1b and apre, a Function Type's member
    // Origin, are ambiguous (dev accepted them); two Function values at one slot (slot1) differ only in Origins, so T takes the first and
    // the second's comparison is the record, and with `b outlives a` it holds (slot1cba; dev rejected it).
    [Theory]
    [InlineData("sel2", MkC + MkT + CallMk, "AmbiguousBinding_Kd mk(1, a, b, c)")]
    [InlineData("sel1", "struct S {a, b, c}\n" + CB + "    public func p(self: ref/Self) -> i32\n        let r: Option<" + FnB + "> = self.mk(1)\n        return 0\n    public init() => ()\n    public func mk(self: ref/Self, x: i32) -> Option<" + FnC + "> => .None\n    public func mk<T>(self: ref/Self, x: T) -> Option<" + FnB + "> => .None\n", "AmbiguousBinding_Kd self.mk(1)")]
    [InlineData("sel2ctl", "func mk" + MkParameters + FnB + ">\n" + CB + "    return .None\n" + MkT + CallMk, "AmbiguousBinding_Kd mk(1, a, b, c)")]
    [InlineData("sel2cba", MkC + MkT + KHead + CBA + "    let r: Option<" + FnB + "> = mk(1, a, b, c)\n    return 0\n", "AmbiguousBinding_Kd mk(1, a, b, c)")]
    [InlineData("sel2one", MkC + CallMk, "UnprovenOriginContract_Kd mk(1, a, b, c)")]
    [InlineData("sel4run", MkC + MkT + KHead + CB + "    let v: i32 = 1\n    let r: Option<" + FnB + "> = mk(v, a, b, c)\n    return 0\n", "UnprovenOriginContract_Kd mk(v, a, b, c)")]
    [InlineData("pre1b", "func mk(x: i32, b: ref/i32, c: ref/i32) -> Option<(ref/i32 during b) -> i32>\n    return .None\nfunc mk<T>(x: T, b: ref/i32, c: ref/i32) -> Option<(ref/i32 during c) -> i32>\n    return .None\nfunc k(b: ref/i32, c: ref/i32) -> i32\n    origin c outlives b\n    let r2: Option<(ref/i32 during b) -> i32> = mk(1, b, c)\n    return 0\n", "AmbiguousBinding_Kd mk(1, b, c)")]
    [InlineData("asel", HB + HC + KF + "    return h(a, b, c, f@move, 1)\n", "AmbiguousBinding_Kd h(a, b, c, f@move, 1)")]
    [InlineData("aselcba", HB + HC + KF + "    origin b outlives a\n    return h(a, b, c, f@move, 1)\n", "AmbiguousBinding_Kd h(a, b, c, f@move, 1)")]
    [InlineData("asel1", HB + KF + "    return h(a, b, c, f@move, 1)\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData("aselw", HB + HC + KF + "    return h(a, b, c, identity(f@move), 1)\n", "AmbiguousBinding_Kd h(a, b, c, identity(f@move), 1)")]
    [InlineData("aselw1", HB + KF + "    return h(a, b, c, identity(f@move), 1)\n", "UnprovenOriginContract_Kd identity(f@move)")]
    [InlineData("apre", HPre + "func h(x: (ref/i32 during static) -> i32, n: i64, b: ref/i32) -> i32 => 1\n" + KPre, "AmbiguousBinding_Kd h(f@move, 1, b)")]
    [InlineData("apre1", HPre + KPre, "UnprovenOriginContract_Kd f@move")]
    [InlineData("slot1", PairFG + CB + "    return pair(f@move, g@move)\n", "UnprovenOriginContract_Kd g@move")]
    [InlineData("slot1cba", PairFG + CBA + "    return pair(f@move, g@move)\n", "")]
    public void ApplicabilityJudgesOnlyTheStructuralPart(string form, string body, string records)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, null);

    // G216 (a) T2b, U2.1, under DECISIONS R4-M162 (the strict form, superseding the owner's (A) result half): a nested signature's result
    // relating a per-call Origin to `static` beyond its inputs is recorded where it is written, never at a value call (SPEC 15.3.4,
    // 15.6.1): d1 (a Closure's own call), lp and lpn (a Function value parameter, from a Closure or a named function), lc and ln (a
    // converted local), ann and ann2 (an annotated local); a Closure's body records `(.None, x)`. A twin that passes a `static` value
    // keeps its meaning with the input fixed at `static` (d1_c, lp_ok, lc_ok, ln_ok; lpn_ok's valid form is ResultPremiseTest's lpfix),
    // and its wide form is invalid (d1_cw, lp_okw, lc_okw, ln_okw). Since H5 the MIR's records on these rejected programs are published
    // directly (DECISIONS R5-M184, until A6's 2g-1; class 4, counted by code under Q13): a located Unsupported_Kd at a written nested
    // relation (ann, ann_n, lp, lp_okw, lc, lc_okw) and, where `use` or `k` returns past `local`, A5's ComparisonLoanConflict_Kd at it.
    // F-a (H3b; the owner's O-2, 「一覧に加える」: H5's (e) list, PLAN G216 (b)), the rows tagged F-a: Binding records nothing, so A5's
    // located Unsupported_Kd at the written nested relation keeps the invalid program rejected where its UnprovenOriginRelation_Kd was
    // (each record's text occurs once in its program, so it is the exact span); U-O3 (BU's R7-O3) moves the Language record to Binding at
    // the same site and re-points these rows back.
    [Theory]
    [InlineData("U2.1", "d1", "func k(s: ref/i32) -> i32\n" + ResultClosure, "Unsupported_Kd ref/(ref/i32 during x) during static", null)] // F-a, the owner's O-2; U-O3 / R7-O3 restores the Language record
    [InlineData("U2.1", "d1_c", "func k(s: ref/i32 during static) -> i32\n" + StaticResultClosure, "", null)]
    [InlineData("U2.1", "d1_cw", "func k(s: ref/i32 during static) -> i32\n" + ResultClosure, "Unsupported_Kd ref/(ref/i32 during x) during static", null)] // F-a, the owner's O-2; U-O3 / R7-O3 restores the Language record
    [InlineData("U2.1", "lp", LeakUse + LeakFrom + LeakClosure + "    return use(c)\n", "ComparisonLoanConflict_Kd let local: i32 = 5; UnprovenOriginRelation_Kd (.None, x); UnprovenOriginRelation_Kd ref/(ref/i32) during static; Unsupported_Kd ref/(ref/i32 during x) during static", null)]
    [InlineData("U2.1", "lp_ok", FixedUseOk + LeakFromStatic + FixedLeakClosure + "    return use(c, s)\n", "", null)]
    [InlineData("U2.1", "lp_okw", LeakUseOk + LeakFromStatic + LeakClosure + "    return use(c, s)\n", "UnprovenOriginRelation_Kd (.None, x); UnprovenOriginRelation_Kd ref/(ref/i32) during static; Unsupported_Kd ref/(ref/i32 during x) during static", null)]
    [InlineData("U2.1", "lpn", LeakMk + LeakUse + LeakFrom + "    return use(mk)\n", "ComparisonLoanConflict_Kd let local: i32 = 5; UnprovenOriginRelation_Kd ref/(ref/i32) during static", null)]
    [InlineData("U2.1", "lpn_ok", LeakMk + LeakUseOk + LeakFromStatic + "    return use(mk, s)\n", "UnprovenOriginRelation_Kd ref/(ref/i32) during static", null)]
    [InlineData("U2.1", "lc", LeakFrom + LeakClosure + "    let g: " + StaticLeakTarget + " = c\n" + LocalCall, "ComparisonLoanConflict_Kd let local: i32 = 5; UnprovenOriginRelation_Kd (.None, x); Unsupported_Kd ref/(ref/i32 during x) during static; Unsupported_Kd ref/(ref/i32) during static", null)]
    [InlineData("U2.1", "lc_ok", LeakFromStatic + FixedLeakClosure + "    let g: " + FixedLeakTarget + " = c\n    let pair = g(s)\n    return pair.1\n", "", null)]
    [InlineData("U2.1", "lc_okw", LeakFromStatic + LeakClosure + "    let g: " + StaticLeakTarget + " = c\n    let pair = g(s)\n    return pair.1\n", "UnprovenOriginRelation_Kd (.None, x); Unsupported_Kd ref/(ref/i32 during x) during static; Unsupported_Kd ref/(ref/i32) during static", null)]
    [InlineData("U2.1", "ln", LeakMk + LeakFrom + "    let g: " + StaticLeakTarget + " = mk\n" + LocalCall, "ComparisonLoanConflict_Kd let local: i32 = 5; Unsupported_Kd ref/(ref/i32) during static", null)] // F-a, the owner's O-2; U-O3 / R7-O3 restores the Language record
    [InlineData("U2.1", "ln_ok", LeakMk + LeakFromStatic + "    let g: " + FixedLeakTarget + " = mk\n    let pair = g(s)\n    return pair.1\n", "", null)]
    [InlineData("U2.1", "ln_okw", LeakMk + LeakFromStatic + "    let g: " + StaticLeakTarget + " = mk\n    let pair = g(s)\n    return pair.1\n", "Unsupported_Kd ref/(ref/i32) during static", null)] // F-a, the owner's O-2; U-O3 / R7-O3 restores the Language record
    [InlineData("U2.1", "ann", "func k(f: " + StaticTarget + ") -> i32\n    let g: " + StaticTarget + " = f@move\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32) during static; Unsupported_Kd ref/(ref/i32) during static", null)]
    [InlineData("U2.1", "ann_n", "func k(f: " + StaticTarget + ") -> i32\n    let g: " + StaticTarget + " = f@move\n" + LocalNone, "UnprovenOriginRelation_Kd ref/(ref/i32) during static; Unsupported_Kd ref/(ref/i32) during static", null)]
    [InlineData("U2.1", "ann2", "func k() -> i32\n    let g: " + StaticTarget + " = " + ErasedClosure + "\n    return 0\n", "Unsupported_Kd ref/(ref/i32 during x) during static; Unsupported_Kd ref/(ref/i32) during static", null)] // F-a, the owner's O-2; U-O3 / R7-O3 restores the Language record
    [InlineData("U2.1", "ann2_n", "func k() -> i32\n    let g: " + StaticTarget + " = " + ErasedClosure + "\n" + LocalNone, "Unsupported_Kd ref/(ref/i32 during x) during static; Unsupported_Kd ref/(ref/i32) during static", null)] // F-a, the owner's O-2; U-O3 / R7-O3 restores the Language record
    public void ANestedSignaturesPerCallRelationIsRecordedWhereItIsWritten(string unit, string form, string body, string records, string? label)
        => OriginRelationDiagnosticTest.AssertRecords($"{form} [{unit}]", body, records, label);

    // G216 (a) U2.1 r2-2 (DECISIONS BU-43 amendment): SPEC 14.9.2, 23.1.2: a join's source that failed with its own record and kept its
    // Type (a Closure or a Function value whose conversion's condition failed) is no candidate of the join, which rests on it: the one
    // record is the source's contract at the converted value (j9, j9b, j11, j6 at each Closure, j12 at `f@move`), with no TypeMismatch_Kd
    // or contract copy at the `if`, and j4's independent capture records stay. Remaining sources that misfit keep the join's own record
    // (j9m, Conflict prevails); a `match` of two Closures (j10) and a lone Closure (j6s) are unchanged. Since H5 the MIR's records are
    // published directly (DECISIONS R5-M184, until A6's 2g-1; class 4, counted under Q13): j6 and j6s add its located Unsupported_Kd at
    // each arm's written nested relation and at the annotation's.
    [Theory]
    [InlineData("j9", JoinHead + "if c => " + LeakArm + " else => " + StaticArm + "\n    return 0\n", "UnprovenOriginContract_Kd " + LeakArm)]
    [InlineData("j9b", JoinHead + "if c => " + StaticArm + " else => " + LeakArm + "\n    return 0\n", "UnprovenOriginContract_Kd " + LeakArm)]
    [InlineData("j11", "func k(x: ref/i32, c: bool) -> Option<ref/i32 during static>\n    let g: " + StaticArmType + " = if c => " + LeakArm + " else => " + StaticArm + "\n    return g()\n", "UnprovenOriginContract_Kd " + LeakArm)]
    [InlineData("j6", PairHead + "if c => " + PairArm + " else => " + PairArm + "\n    return 0\n", "UnprovenOriginContract_Kd " + PairArm + "; UnprovenOriginContract_Kd " + PairArm + "; UnprovenOriginRelation_Kd (.None, x); UnprovenOriginRelation_Kd (.None, x); " + PairUnsupported + "; " + PairUnsupported + "; " + PairTargetUnsupported)]
    [InlineData("j12", "func k(x: ref/i32, c: bool, f: () -> Option<ref/i32 during x>, g: " + StaticArmType + ") -> i32\n    let h: " + StaticArmType + " = if c => f@move else => g@move\n    return 0\n", "UnprovenOriginContract_Kd f@move")]
    [InlineData("j4", "func k(x: ref/i32, y: ref/i32, c: bool) -> i32\n    let g: (ref/i32) -> ref/i32 during y = if c => func [] (p: ref/i32) -> ref/i32 during x => x else => func [] (p: ref/i32) -> ref/i32 during y => y\n    return 0\n", "InvalidCaptureBinding_Kd x; InvalidCaptureBinding_Kd y; UnprovenOriginContract_Kd func [] (p: ref/i32) -> ref/i32 during x => x")]
    [InlineData("j9m", "func k(x: ref/i32, n: i32) -> i32\n    let g: " + StaticArmType + " = match n\n        0 => " + LeakArm + "\n        1 => " + StaticArm + "\n        _ => 1\n    return 0\n", "TypeMismatch_Kd 1; UnprovenOriginContract_Kd " + LeakArm)]
    [InlineData("j10", JoinHead + "match c\n        true => " + LeakArm + "\n        false => " + StaticArm + "\n    return 0\n", "UnprovenOriginContract_Kd " + LeakArm)]
    [InlineData("j6s", PairHead + PairArm + "\n    return 0\n", "UnprovenOriginContract_Kd " + PairArm + "; UnprovenOriginRelation_Kd (.None, x); " + PairUnsupported + "; " + PairTargetUnsupported)]
    public void AJoinRestsOnASourceWhoseConversionFailed(string form, string body, string records)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, null);

    // G216 (a) U2.1 r2-3 (DECISIONS BU-43 amendment): SPEC 23.1.2, 10.5: a call's fit takes an argument that failed with its own record
    // as fitting every candidate the evidence walk keeps, so a later use of the call's result (a consultation re-selecting it) adds no
    // NoApplicableOverload_Kd beside the argument's one contract (na5a, na5, na5b, st1). A call no candidate admits by another argument
    // keeps its own record (na5d; its Closure is never judged); na1, na5c and st1b (no later use) are unchanged. An untyped failed part
    // is not this row's (ar2: an operator's untyped Closure keeps the call's NoApplicableOverload_Kd, U1.6h's).
    [Theory]
    [InlineData("ar2", "struct Value\n    Self is Addable<(i32) -> i32>\n    associate Output is i32\n    public func added(self: ref/Self, right: ref/((i32) -> i32)) -> i32 => 1\nfunc k() -> i32\n    return Value.init() + func [] (x) => x\n", "NoApplicableOverload_Kd Value.init() + func [] (x) => x")]
    [InlineData("na5a", UseStatic + "    let n = use(" + WeakClosure + ")\n    return n\n", "UnprovenOriginContract_Kd " + WeakClosure)]
    [InlineData("na5", UseStatic + "    let n = use(" + WeakClosure + ")\n    let local = 1\n    let r = local@ref\n    return n\n", "UnprovenOriginContract_Kd " + WeakClosure)]
    [InlineData("na5b", UseStatic + "    let n = use(" + WeakClosure + ")\n    let local = 1\n    return n\n", "UnprovenOriginContract_Kd " + WeakClosure)]
    [InlineData("st1", HoldStatic + "func k() -> ref/i32 during static\n    let s = S.init(" + WeakClosure + ")\n    let local = 1\n    return s.f(local@ref)\n", "UnprovenOriginContract_Kd " + WeakClosure)]
    [InlineData("na5d", "func use(f: (ref/i32) -> ref/i32 during static, n: i32) -> i32 => 0\nfunc k() -> i32\n    let n = use(" + WeakClosure + ", true)\n    return n\n", "NoApplicableOverload_Kd use(" + WeakClosure + ", true)")]
    [InlineData("na1", UseStatic + "    return use(" + WeakClosure + ")\n", "UnprovenOriginContract_Kd " + WeakClosure)]
    [InlineData("na5c", UseStatic + "    let n = use(" + WeakClosure + ")\n    return 0\n", "UnprovenOriginContract_Kd " + WeakClosure)]
    [InlineData("st1b", HoldStatic + "func k() -> i32\n    let s = S.init(" + WeakClosure + ")\n    return 0\n", "UnprovenOriginContract_Kd " + WeakClosure)]
    public void ACallFitsAnArgumentWhoseConversionFailed(string form, string body, string records)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, null);

    // G216 (a) U2.1 r2-2 (R3): an element of an array literal with an expected array Type that misfits the element Type the earlier
    // elements established is the element's record with the comparison's fact, as every other element misfit (FailMismatch); it was a
    // record without its Types.
    [Theory]
    [InlineData("a13", "func k(x: i32, s: string) -> i32\n    let a: Array<i32> = [x, s]\n    return 0\n", "TypeMismatch_Kd s", "expected i32, found string")]
    [InlineData("a15", "func k(x: i64, y: i32) -> i32\n    let a: [2 of i64] = [x, y]\n    return 0\n", "TypeMismatch_Kd y", "expected i64, found i32")]
    public void AnArrayElementMisfitStatesItsTypes(string form, string body, string records, string label)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, label);

    // WeakerHead's result needs `q outlives r`, which no clause states, so it is its declaration's record (DECISIONS R4-M162), a
    // Binding-level record beside which an independent TransferRequired_Kd is lost until R7 (R4-M162's lost records; PLAN G56). Since
    // H5 the MIR decides and publishes its records directly (R5-M184, until A6's 2g-1), so the independent Loan conflict at `x = 2` is
    // kept beside it (c2bg, c2dg, c15). Each twin's head is well-formed, its conversion holds and it keeps the independent record.
    [Theory]
    [InlineData("c2bg", WeakerHead + OriginRelationDiagnosticTest.IndependentConflict + "    let g: " + WeakerTarget + " = f@move\n    return 0\n", "ComparisonLoanConflict_Kd x = 2; UnprovenOriginContract_Kd f@move; UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("c2bg_c", StrongerHead + OriginRelationDiagnosticTest.IndependentConflict + "    let g: " + WeakerTarget + " = f@move\n    return 0\n", "ComparisonLoanConflict_Kd x = 2")]
    [InlineData("c2dg", UseWeaker + WeakerHead + OriginRelationDiagnosticTest.IndependentConflict + "    return use1(q, f@move)\n", "ComparisonLoanConflict_Kd x = 2; UnprovenOriginContract_Kd f@move; UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("c2dg_c", UseWeaker + StrongerHead + OriginRelationDiagnosticTest.IndependentConflict + "    return use1(q, f@move)\n", "ComparisonLoanConflict_Kd x = 2")]
    [InlineData("c2_fnvalue_converted_weaker", WeakerHead + "    let g: " + WeakerTarget + " = f\n    let o = g()\n    return 0\n", "UnprovenOriginContract_Kd f; UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("c2_fnvalue_converted_weaker_c", StrongerHead + "    let g: " + WeakerTarget + " = f\n    let o = g()\n    return 0\n", "TransferRequired_Kd f")]
    [InlineData("c15", WeakerHead + OriginRelationDiagnosticTest.IndependentConflict + "    let t: (" + WeakerTarget + ", i32) = (f@move, 1)\n    return 0\n", "ComparisonLoanConflict_Kd x = 2; UnprovenOriginContract_Kd f@move; UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("c15_c", StrongerHead + OriginRelationDiagnosticTest.IndependentConflict + "    let t: (" + WeakerTarget + ", i32) = (f@move, 1)\n    return 0\n", "ComparisonLoanConflict_Kd x = 2")]
    public void AnIllFormedHeadLosesTheIndependentRecordUntilH11(string form, string body, string records)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, null);

    // PLAN G404 (SPEC 10.7, 15.3.7; DECISIONS BU-93): a Closure's or a Function Type's whole-contract comparison, at a conversion and
    // in a Callable proof, is judged at its use with the use's premises, as a Function value's is (nest2_c): a member Origin that a
    // clause relates (one1, `b outlives a`), a member and the Closure's own well-formedness each related by one of two clauses (two2), an
    // invariant member equal through two clauses (uniq2), the Closure converted at an argument after selection (arg2) and a Function
    // Type proven Callable at a Type argument (cal2) are valid. Equality by one clause (eq2) holds without premises. The invalid twins
    // lack a clause the relation needs (one1_n, two2_n, two2_none, uniq2_n, cal2_n; arg2_n's solution of T is `(b and a)`, which the
    // Closure now fits, so its record is the argument `w`, whose fit needs `a outlives w`). An inferred parameter's Function Type,
    // identical to the required one, keeps its condition, its result's well-formedness at the use (pnest1, as ResultPremiseTest's n1a at
    // the root; dev: a fact-less UnprovenConstraint_Kd at the call); a `static` argument proves it (pnest1_c).
    [Theory]
    [InlineData("one1", OuterHead + BOutlivesA + OuterLet, "")]
    [InlineData("one1_n", OuterHead + "    origin a outlives b\n" + OuterLet, "UnprovenOriginContract_Kd " + OuterClosure)]
    [InlineData("two2", InnerHead + BothWays + InnerLet, "")]
    [InlineData("two2_n", InnerHead + BOutlivesA + InnerLet, "UnprovenOriginContract_Kd " + InnerClosure)]
    [InlineData("two2_none", InnerHead + InnerLet, "UnprovenOriginContract_Kd " + InnerClosure)]
    [InlineData("eq2", InnerHead + "    origin a == b\n" + InnerLet, "")]
    [InlineData("uniq2", UniqHead + BothWays + UniqLet, "")]
    [InlineData("uniq2_n", UniqHead + BOutlivesA + UniqLet, "UnprovenOriginContract_Kd " + UniqClosure)]
    [InlineData("arg2", ValueRun + InnerHead + BothWays + "    return run(" + InnerClosure + ", w)\n", "")]
    [InlineData("arg2_n", ValueRun + InnerHead + BOutlivesA + "    return run(" + InnerClosure + ", w)\n", "UnprovenOriginRelation_Kd w")]
    [InlineData("cal2", CallableHolder + InnerHead + BothWays + InnerHolder, "")]
    [InlineData("cal2_n", CallableHolder + InnerHead + BOutlivesA + InnerHolder, "UnprovenConstraint_Kd .None; " + HolderConstraint + "; " + HolderConstraint)]
    [InlineData("pnest1", NestedClauseHead + "func host() -> i32\n    let a = 1\n    return k(a@ref, func [] (g) => 5)\n", "UnprovenOriginContract_Kd func [] (g) => 5; UnprovenOriginRelation_Kd Option<T during q>")]
    [InlineData("pnest1_c", NestedClauseHead + "func host(s: ref/i32 during static) -> i32\n    return k(s, func [] (g) => 5)\n", "UnprovenOriginRelation_Kd Option<T during q>")]
    public void AClosuresContractIsJudgedWithItsUsesPremises(string form, string body, string records)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, null);

    // PLAN G404 (DECISIONS BU-93, R4-M162): a nested Function Type identical to the required one keeps its implementation's condition,
    // so the Closure records UnprovenOriginContract_Kd, a Binding-level relation record. The old ownership analysis stops beside that
    // record (PLAN G56's owner-accepted limit, R4-M162's lost records), so before H5 these independent records were lost: the let
    // annotation's UnprovenOriginRelation_Kd (pl_n at 8:19, p3_n at 8:20), that one and the Closure parameter annotation's (pl_n_written
    // at 8:19 and 8:66), and the second `v@move`'s MovedPlace_Kd at 10:13 with the annotation's UnprovenOriginRelation_Kd at 11:19
    // (pmv_let). Since the MIR decides acceptance (H5) its records are published beside Binding's (R6-check H3-2, DECISIONS R5-M184), so
    // they stay (SPEC 23.1.2; class 1r), each pinned at its site with the Closure's record (`sites`, line:column). A non-identical form
    // already recorded only the Closure (pl_nonid).
    [Theory]
    [InlineData("pl_n", NestedLet + "func [] (g) => 5\n    return 0\n", "UnprovenOriginContract_Kd func [] (g) => 5; UnprovenOriginRelation_Kd Option<T during s>", "UnprovenOriginContract_Kd 8:48; UnprovenOriginRelation_Kd 8:19")]
    [InlineData("p3_n", NestedT + NestedHost + "    let f: ((() -> Option<T during s>) -> i32) -> i32 = func [] (h) => 5\n    return 0\n", "UnprovenOriginContract_Kd func [] (h) => 5; UnprovenOriginRelation_Kd Option<T during s>", "UnprovenOriginContract_Kd 8:57; UnprovenOriginRelation_Kd 8:20")]
    [InlineData("pl_n_written", NestedLet + NestedWritten + "\n    return 0\n", "UnprovenOriginContract_Kd " + NestedWritten + "; UnprovenOriginRelation_Kd Option<T during s>; UnprovenOriginRelation_Kd Option<T during s>", "UnprovenOriginContract_Kd 8:48; UnprovenOriginRelation_Kd 8:19; UnprovenOriginRelation_Kd 8:66")]
    [InlineData("pmv_let", NestedT + NestedHost + "    var v = [1, 2]\n    let m = v@move\n    let n = v@move\n    let f: (() -> Option<T during s>) -> i32 = func [] (g) => 5\n    return 0\n", "MovedPlace_Kd v; UnprovenOriginContract_Kd func [] (g) => 5; UnprovenOriginRelation_Kd Option<T during s>", "MovedPlace_Kd 10:13; UnprovenOriginContract_Kd 11:48; UnprovenOriginRelation_Kd 11:19")]
    public void ANestedIdenticalContractKeepsTheIndependentRecords(string form, string body, string records, string sites)
    {
        OriginRelationDiagnosticTest.AssertRecords(form, body, records, null);
        Assert.Equal($"{form}: {sites}", $"{form}: {Sites(body + Main)}");
    }

    // PLAN G418 (SPEC 23.1.2, 10.7, 15.6.1; DECISIONS BU-96, BU-93): a Closure argument whose contract fails after selection is its own
    // record (UnprovenOriginContract_Kd), and the call still judges its clauses over the selected solution, which the Closure's verdict
    // does not enter: k's clause record at `s` stays beside it for a nested identical Function Type (pk_noclause, written in
    // pk_noclause_int), a non-identical one (pk_nonid) and a body TypeMismatch (pk_tm), and a body-local input refutes it (pk_local). A
    // `static` input proves the clause (pk_static, valid; pk_static_nonid keeps only the Closure's annotation record). pk_noclause_int's
    // annotation record at `Option<T during s>` is the Closure's own (DECISIONS R5-M184). Neither completion stops at a failed part:
    // a contextual Case beside the Closure (s_first_q, t_none; r_case's failed `.Some(undefinedName)` keeps the Closure's own record),
    // a second Closure (r_two) and another input's fit (r_fit) keep theirs. A clause located at the failed Closure is published with its
    // record (pk_first). By SPEC 15.6.1 a nested signature's inputs are its premises, proven at its calls, so the call need not prove
    // `T during s`; the plan check's UnprovenConstraint_Kd beside a Function value argument restates the clause (PLAN G464).
    [Theory]
    [InlineData("pk_noclause", NestedClauseK + "    return k(s, func [] (g) => 5)\n", "UnprovenOriginContract_Kd func [] (g) => 5; UnprovenOriginRelation_Kd s")]
    [InlineData("pk_noclause_int", NestedClauseK + "    return k(s, " + NestedWritten + ")\n", "UnprovenOriginContract_Kd " + NestedWritten + "; UnprovenOriginRelation_Kd Option<T during s>; UnprovenOriginRelation_Kd s")]
    [InlineData("pk_nonid", ClauseK + "func host(s: ref/i32, t: ref/i32 during static) -> i32\n    return k(s, func [] (g: () -> Option<T during t>) -> i32 => 5)\n", "UnprovenOriginContract_Kd func [] (g: () -> Option<T during t>) -> i32 => 5; UnprovenOriginRelation_Kd s")]
    [InlineData("pk_tm", NestedClauseK + "    return k(s, func [] (g) => true)\n", "TypeMismatch_Kd true; UnprovenOriginContract_Kd func [] (g) => true; UnprovenOriginRelation_Kd s")]
    [InlineData("pk_local", ClauseK + "func host() -> i32\n    let a = 1\n    return k(a@ref, func [] (g) => 5)\n", "UnprovenOriginContract_Kd func [] (g) => 5; UnsatisfiedOriginRelation_Kd a@ref")]
    [InlineData("pk_static", ClauseK + "func host(s: ref/i32 during static) -> i32\n    return k(s, func [] (g) => 5)\n", "")]
    [InlineData("pk_static_nonid", ClauseK + "func host(s: ref/i32 during static, t: ref/i32) -> i32\n    return k(s, func [] (g: () -> Option<T during t>) -> i32 => 5)\n", "UnprovenOriginRelation_Kd Option<T during t>")]
    [InlineData("s_first_q", NestedT + "func k7(q: ref/i32, o: Option<i32>, " + NestedF + ClauseTail + "    return k7(s, .Some(1), func [] (g) => 5)\n", "UnprovenOriginContract_Kd func [] (g) => 5; UnprovenOriginRelation_Kd s")]
    [InlineData("t_none", NestedT + "func k7(o: Option<i32>, q: ref/i32, " + NestedF + ClauseTail + "    return k7(.None, s, func [] (g) => 5)\n", "UnprovenOriginContract_Kd func [] (g) => 5; UnprovenOriginRelation_Kd s")]
    [InlineData("r_case", NestedT + "func k7(o: Option<i32>, q: ref/i32, " + NestedF + ClauseTail + "    return k7(.Some(undefinedName), s, func [] (g) => 5)\n", "UnprovenOriginContract_Kd func [] (g) => 5; UnprovenOriginRelation_Kd s; UnresolvedBinding_Kd undefinedName")]
    [InlineData("r_two", NestedT + "func k2(q: ref/i32, " + NestedF + ", e: (() -> Option<T during q>) -> i32" + ClauseTail + "    return k2(s, func [] (g) => 5, func [] (g) => 6)\n", "UnprovenOriginContract_Kd func [] (g) => 5; UnprovenOriginContract_Kd func [] (g) => 6; UnprovenOriginRelation_Kd s")]
    [InlineData("r_fit", NestedT + "func k3(q: ref/i32, " + NestedF + ", r: ref/i32 during static) -> i32\n    return 0\n\n" + NestedHost + "    return k3(s, func [] (g) => 5, s)\n", "UnprovenOriginContract_Kd func [] (g) => 5; UnprovenOriginRelation_Kd Option<T during q>; UnprovenOriginRelation_Kd s")]
    [InlineData("pk_first", NestedT + "func k4(" + NestedF + ", q: ref/i32" + ClauseTail + "    return k4(func [] (g) => 5, s)\n", "UnprovenOriginContract_Kd func [] (g) => 5; UnprovenOriginRelation_Kd func [] (g) => 5")]
    public void AFailedClosureContractKeepsTheCallsClauseRecords(string form, string body, string records)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, null);

    // PLAN G494 (SPEC 10.5.1, 10.5.4, 7.7.4, 15.6.4 steps 1-3): a value call completes its waiting parts and erases its callables at its
    // instantiated parameters, after solving the callee's Origins, as a named call does after selection; no Type of the caller names the
    // callee's own `q`. Valid at a generic instance (gv), a method value (mv), a Function Item (fi, k3), a Closure value (cl) and an
    // anonymous function, also without a clause (v8: `(ref/i32 during q) -> i32` at a free `s`) and through a join of Items (vj); dev
    // crashed (InternalInvariant_Kd) on v_fi_ok, gv_fi_ok, mv_fi_ok, v3_ok and vcl2_ok. A written header keeps only its own annotation
    // record (v_static_nonid, as pk_static_nonid). An erasure is decided once, under the use's premises (r04_cvprem: the caller's clause
    // admits a Closure value; dev crashed). The invalid twins keep their records, now over `s`; k's clause record at `s` beside a failed
    // part is unit 2's (rule W). A part that fails before the solve leaves the waiting parts their declared parameter, so their own
    // records stay (vd, va); of two acquisitions the call records the evidence part's, as its named twin does (r17).
    [Theory]
    [InlineData("v_ok", ClauseK + StaticHost + "    let g = k\n    return g(s, func [] (x) => 5)\n", "", null)]
    [InlineData("v_fi_ok", ClauseK + Hf + StaticHost + "    let g = k\n    return g(s, hf)\n", "", null)]
    [InlineData("gv_ok", GenericK + StaticHost + "    let g = kk<i32>\n    return g(s, func [] (x) => 5)\n", "", null)]
    [InlineData("gv_fi_ok", GenericK + Hf + StaticHost + "    let g = kk<i32>\n    return g(s, hf)\n", "", null)]
    [InlineData("mv_ok", MethodK + "func host(h: ref/H, s: ref/i32 during static) -> i32\n    let g = H.m\n    return g(h, s, func [] (x) => 5)\n", "", null)]
    [InlineData("mv_fi_ok", MethodK + Hf + "func host(h: ref/H, s: ref/i32 during static) -> i32\n    let g = H.m\n    return g(h, s, hf)\n", "", null)]
    [InlineData("v3_ok", PlainK + StaticHost + "    let g = k3\n    return g(s, h3)\n", "", null)]
    [InlineData("v8b_ok", PlainK + NestedHost + "    let g = k3\n    return g(s, h4)\n", "", null)]
    [InlineData("v8c_ok", PlainK + NestedHost + "    let g = k3\n    return g(s, func [] (x) => 5)\n", "", null)]
    [InlineData("vcl_ok", PlainK + NestedHost + "    let c = func [] (x: ref/i32) -> i32 => 5\n    let g = k3\n    return g(s, c)\n", "", null)]
    [InlineData("vcl2_ok", PlainK + StaticHost + "    let c = func [] (x: ref/i32 during static) -> i32 => 5\n    let g = k3\n    return g(s, c)\n", "", null)]
    [InlineData("vj_ok", PlainK + "func host(s: ref/i32, c: bool) -> i32\n    let g = k3\n    return g(s, if c => h4 else => h4)\n", "", null)]
    [InlineData("r10b_join_same_bad", PlainK + "func host(s: ref/i32, c: bool) -> i32\n    let g = k3\n    return g(s, if c => h3 else => h3)\n", "UnprovenOriginContract_Kd if c => h3 else => h3", InputOverS)]
    [InlineData("r04_cvprem_ok", PlainK + PremiseHost + "    origin s outlives t\n" + PremiseClosure, "", null)]
    [InlineData("r04_cvprem_bad", PlainK + PremiseHost + PremiseClosure, "UnprovenOriginContract_Kd c", "the 1st parameter requires s outlives t, which is not proven")]
    [InlineData("v_static_nonid", ClauseK + "func host(s: ref/i32 during static, t: ref/i32) -> i32\n    let g = k\n    return g(s, func [] (x: () -> Option<T during t>) -> i32 => 5)\n", "UnprovenOriginRelation_Kd Option<T during t>", null)]
    [InlineData("vc_closure", NestedClauseK + "    let g = k\n    return g(s, func [] (x) => 5)\n", "UnprovenOriginContract_Kd func [] (x) => 5", WellFormedOverS)]
    [InlineData("gv_bad", GenericK + NestedHost + "    let g = kk<i32>\n    return g(s, func [] (x) => 5)\n", "UnprovenOriginContract_Kd func [] (x) => 5", WellFormedOverS)]
    [InlineData("mv_bad", MethodK + "func host(h: ref/H, s: ref/i32) -> i32\n    let g = H.m\n    return g(h, s, func [] (x) => 5)\n", "UnprovenOriginContract_Kd func [] (x) => 5", WellFormedOverS)]
    [InlineData("v_fi", ClauseK + Hf + NestedHost + "    let g = k\n    return g(s, hf)\n", "UnprovenOriginContract_Kd hf", null)]
    [InlineData("v3_bad", PlainK + NestedHost + "    let g = k3\n    return g(s, h3)\n", "UnprovenOriginContract_Kd h3", InputOverS)]
    [InlineData("v8d_bad", PlainK + NestedHost + "    let g = k3\n    return g(s, func [] (x: ref/i32 during static) -> i32 => 5)\n", "UnprovenOriginContract_Kd func [] (x: ref/i32 during static) -> i32 => 5", InputOverS)]
    [InlineData("vcl_bad", PlainK + NestedHost + "    let c = func [] (x: ref/i32 during static) -> i32 => 5\n    let g = k3\n    return g(s, c)\n", "UnprovenOriginContract_Kd c", InputOverS)]
    [InlineData("vd_bad", "func kd(f: (i32) -> i32, q: ref/i32) -> i32 => 0\n\n" + NestedHost + "    let g = kd\n    return g(func [] (x) => undefinedName2, undefinedName)\n", "UnresolvedBinding_Kd undefinedName; UnresolvedBinding_Kd undefinedName2", null)]
    [InlineData("va_bad", ClosurePlace + "    return g(c, undefinedName)\n", "TransferRequired_Kd c; UnresolvedBinding_Kd undefinedName", null)]
    [InlineData("va2_bad", ClosurePlace + "    return g(c, s)\n", "TransferRequired_Kd c", null)]
    [InlineData("r17_two_acq_bad", TwoAcquisitions, "TransferRequired_Kd values", null)]
    public void AValueCallCompletesItsWaitingPartsAtTheInstantiatedParameter(string form, string body, string records, string? label)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, label);

    // The published Error records of a source: each code and its span's start as line:column (1-based), in ordinal order.
    private static string Sites(string source)
        => string.Join("; ", ContextualCaptureTest.PublishedRecords(source).Where(static x => x.Severity == DiagnosticSeverity.Error)
            .Select(x => $"{x.Code} {source.AsSpan(0, x.Span.Start).Count('\n') + 1}:{x.Span.Start - source.LastIndexOf('\n', Math.Max(x.Span.Start - 1, 0))}")
            .Order(StringComparer.Ordinal));
}
