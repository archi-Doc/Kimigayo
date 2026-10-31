// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// G216 (a) T1: Part 1's (argument completion's) rows beside d4's (OriginRelationDiagnosticTest.JoinArguments.cs), under the owner-adopted
// G216a-PLAN.md §3. Each row's first value names the Part 1 unit expected to pass it (U1.2 to U1.6). Forms are BU's design review
// (D1, N11), R4's design review (B1-B4) and BU's review 3 (B6), copied as written, one form per program, each with its twin.
public partial class OriginRelationDiagnosticTest
{
    private const string KHead = "func k(c: bool) -> i32\n";

    private const string ApWide = "func ap(f: (i64) -> i32) -> i32 => f(1)\n";

    private const string ApBool = "func ap(f: bool) -> i32 => 0\n";

    private const string RefOneBool = "func one(x: bool) -> i32 => 0\n";

    private const string ValueG = "func k(c: bool, g: (Option<i32>) -> i32) -> i32\n";

    private const string LabeledDo = "label s: do\n        if c => exit to s .None\n        exit to s ";

    private const string EnvClosure = "    let y: i32 = 2\n    let f = func [y@ref] (n: i32) => n + y@follow\n";

    private const string VarClosure = "    var total = 0\n    var f = func [var total] (n: i32) -> i32\n        total = total + n\n        return total\n";

    private const string CallAction = "func call(action: (i32) -> i32, x: i32) -> i32 => action(x)\n";

    private const string RunG2 = "func run2(g: ((i32) -> i32, i32) -> i32) -> i32\n";

    private const string IdentityT = "func identity<T>(v: T) -> T => v@move\n";

    private const string FTuples = "func f(x: (i64, i32)) -> i32 => 0\nfunc f(x: (bool, i32)) -> i32 => 1\n";

    private const string KX = "func k(c: bool, x: i64) -> i32\n";

    private const string FScalars = "func f(x: i64) -> i32 => 0\nfunc f(x: bool) -> i32 => 1\n";

    private const string FI64 = "func f(x: i64) -> i32 => 0\n";

    private const string FTuple = "func f(x: (i64, i32)) -> i32 => 0\n";

    private const string KXB = "func k(c: bool, x: i64, b: bool) -> i32\n";

    private const string Twice = "func twice(v: i64) -> i64 => v\n";

    private const string FFnPair = "func f(x: ((i64) -> i64, i32)) -> i32 => 0\n";

    private const string D2Join = "(if c => (identity(1), 2) else => (x, 3))";

    private const string FPairs = "func f(x: i64, y: i32) -> i32 => 0\nfunc f(x: bool, y: i32) -> i32 => 1\n";

    private const string GBool = "func g(x: bool) -> i32 => 0\n";

    private const string GOptions = "func g(x: Array<Option<bool>>) -> i32 => 0\n";

    private const string Pick = "func pick<T>(x: T, y: T) -> T => x@move\n";

    private const string BoxInit = "struct Box<T>\n    public let value: T\n    public init(value: T) => self.value = value@move\n";

    private const string WJoin = "if c => identity(1) else => 2";

    private const string WTuples = "if c => (identity(1), 2) else => (2, 3)";

    private const string WSources = "NoApplicableOverload_Kd identity(1); TypeMismatch_Kd 2";

    private const string KD = "func k(c: bool, d: bool) -> i32\n";

    private const string NdJoin = "(if c => (if d => identity(1) else => identity(2)) else => identity(3))";

    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";

    private const string SgJoin = "if d => identity(1) else => 2";

    private const string N3Join = "if c => [identity(1), 3] else => [x, 4]";

    private const string FFixed = "func f(x: [2 of i64]) -> i32 => 0\nfunc f(x: [2 of bool]) -> i32 => 1\n";

    private const string FArrays = "func f(x: Array<i64>) -> i32 => 0\nfunc f(x: Array<bool>) -> i32 => 1\n";

    // D1 (BU's design review): a join whose sources are typed values decides its own Type (SPEC 10.5.1 independently typable, 14.9.2
    // step 2) at a named call and at a value call alike, so it gets the record a typed argument gets there, the same on both call kinds
    // (`h(a)` and a value call `h(a)` give NoApplicableOverload_Kd at the call); its twins pass sources of the parameter Type.
    [Theory]
    [InlineData("U1.2", "d1h", false, "Option<bool>", "if c => a else => b", "NoApplicableOverload_Kd h(if c => a else => b)")]
    [InlineData("U1.2", "d1g", true, "Option<bool>", "if c => a else => b", "NoApplicableOverload_Kd h(if c => a else => b)")]
    [InlineData("U1.2", "d1h_ok", false, "Option<i32>", "if c => a else => b", "")]
    [InlineData("U1.2", "d1g_ok", true, "Option<i32>", "if c => a else => b", "")]
    [InlineData("U1.2", "d1hm", false, "Option<bool>", "match c\n        true => a\n        false => b\n    ", "NoApplicableOverload_Kd h(match c")]
    [InlineData("U1.2", "d1gm", true, "Option<bool>", "match c\n        true => a\n        false => b\n    ", "NoApplicableOverload_Kd h(match c")]
    [InlineData("U1.2", "d1hm_ok", false, "Option<i32>", "match c\n        true => a\n        false => b\n    ", "")]
    [InlineData("U1.2", "d1gm_ok", true, "Option<i32>", "match c\n        true => a\n        false => b\n    ", "")]
    public void ATypedJoinArgumentHasOneRecordOnBothCallKinds(string unit, string form, bool valueCall, string type, string join, string records)
    {
        var head = valueCall
            ? $"func run(c: bool, a: {type}, b: {type}, h: (Option<i32>) -> i32) -> i32\n"
            : $"{OptionH}func run(c: bool, a: {type}, b: {type}) -> i32\n";
        AssertRecords($"{form} [{unit}]", $"{head}    return h({join})\n", records, null);
    }

    // B1 (R4's design review): a join of references that do not need `S` takes their common Function Item Type (SPEC 10.5.3, 14.9.2 step
    // 2), so it is independently typable and checked per candidate (10.1 step 5): with `ap` overloaded on (i32) -> i32 and (i64) -> i32
    // (or bool) only the first applies, as for `ap(one)`; in a `match` and an unannotated `let` too.
    [Theory]
    [InlineData("U1.3", "r7", RefOne + ApFunc + ApWide + RunHead + "    return ap(if c => one else => one)\n")]
    [InlineData("U1.3", "r7m", RefOne + ApFunc + ApWide + RunHead + "    return ap(match c\n        true => one\n        false => one\n    )\n")]
    [InlineData("U1.3", "r7l", RefOne + ApFunc + ApWide + RunHead + "    let a = ap(if c => one else => one)\n    return a\n")]
    [InlineData("U1.3", "r7f", RefOne + ApFunc + ApBool + RunHead + "    return ap(if c => one else => one)\n")]
    public void AJoinOfIndependentReferencesSelectsByItsType(string unit, string form, string body)
        => AssertRecords($"{form} [{unit}]", body, string.Empty, null);

    // The invalid twins: whatever the call kind, a typed argument that no candidate admits is NoApplicableOverload_Kd at the call (SPEC
    // 10.1 "No applicable candidate is an error"); B2 (R4's design review): a call that fails before selection still reports an
    // independent failure inside a join that waits for it, `undefinedName` (SPEC 23.1.2), beside the arity's NoApplicableOverload_Kd, at a
    // named call (nar1, nar3 a `match`, nar5 parenthesized, vcar4 in a `let`) and a value call (vcar6), with the single-failure twins
    // (b6ar is the named call's arity twin); B3: a labeled `if` and a labeled `do` with `exit to` are result sources of the argument (SPEC
    // 14.9.1, 14.9.2; `label` adds no grouping), so a failing source is the Language error at that source at a named call (l1_n, b1_n,
    // lt1_n inside a Tuple) as at a value call (l2_n, b1v_n); N11 (BU's design review): a waiting join takes no part in applicability
    // (SPEC 10.1, 10.5.1), so with `p` overloaded on Option<i32> and Option<bool> no candidate is best (10.4); B6 (BU's review 3): a
    // failing call in an unannotated `let` keeps its Language error, a `match` (b6m), a wrong second argument (b6y) and dev's anonymous
    // function join at a value call (b6x: no candidates, so the join is checked at k's parameter Type, 14.9.2 step 4); U1.6g3b: g050's
    // conflict between `idg`'s and `twice`'s array results waits for the call, so neither `f` is best (10.4; its 6:106 stays under G159).
    [Theory]
    [InlineData("U1.3", "r7_n", RefOneBool + ApFunc + ApWide + RunHead + "    return ap(if c => one else => one)\n", "NoApplicableOverload_Kd ap(if c => one else => one)")]
    [InlineData("U1.3", "r7m_n", RefOneBool + ApFunc + ApWide + RunHead + "    return ap(match c\n        true => one\n        false => one\n    )\n", "NoApplicableOverload_Kd ap(match c")]
    [InlineData("U1.3", "r7l_n", RefOneBool + ApFunc + ApWide + RunHead + "    let a = ap(if c => one else => one)\n    return a\n", "NoApplicableOverload_Kd ap(if c => one else => one)")]
    [InlineData("U1.3", "r7f_n", RefOneBool + ApFunc + ApBool + RunHead + "    return ap(if c => one else => one)\n", "NoApplicableOverload_Kd ap(if c => one else => one)")]
    [InlineData("U1.3", "nar1", OptionH + KHead + "    return h(if c => .None else => .Some(undefinedName), 2)\n", "NoApplicableOverload_Kd h(if c => .None else => .Some(undefinedName), 2); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.3", "nar3", OptionH + PbOption + "    return h(match o\n        .Some(let x) => .Some(undefinedName)\n        .None => .None\n    , 2)\n", "NoApplicableOverload_Kd h(match o; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.3", "nar5", OptionH + KHead + "    return h((if c => .None else => .Some(undefinedName)), 2)\n", "NoApplicableOverload_Kd h((if c => .None else => .Some(undefinedName)), 2); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.3", "vcar4", OptionH + KHead + "    let a = h(if c => .None else => .Some(undefinedName), 2)\n    return a\n", "NoApplicableOverload_Kd h(if c => .None else => .Some(undefinedName), 2); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.3", "vcar6", ValueG + "    return g(if c => .None else => .Some(undefinedName), 2)\n", "NoApplicableOverload_Kd g(if c => .None else => .Some(undefinedName), 2); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.3", "nar1_u", OptionH + KHead + "    return h(if c => .None else => .Some(undefinedName))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.3", "vcar6_u", ValueG + "    return g(if c => .None else => .Some(undefinedName))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.3", "vcar6_ar", ValueG + "    return g(if c => .None else => .Some(1), 2)\n", "NoApplicableOverload_Kd g(if c => .None else => .Some(1), 2)")]
    [InlineData("U1.3", "l1_n", OptionH + KHead + "    let a = h(label l: if c => .None else => .Some(true))\n    return a\n", "TypeMismatch_Kd .Some(true)")]
    [InlineData("U1.3", "b1_n", OptionH + KHead + "    let a = h(" + LabeledDo + ".Some(true)\n    )\n    return a\n", "TypeMismatch_Kd .Some(true)")]
    [InlineData("U1.3", "lt1_n", "func h2(t: (Option<i32>, i32)) -> i32 => 0\n" + KHead + "    return h2((label l: if c => .None else => .Some(true), 1))\n", "TypeMismatch_Kd .Some(true)")]
    [InlineData("U1.3", "l2_n", ValueG + "    return g(label l: if c => .None else => .Some(true))\n", "TypeMismatch_Kd .Some(true)")]
    [InlineData("U1.3", "b1v_n", ValueG + "    return g(" + LabeledDo + ".Some(true)\n    )\n", "TypeMismatch_Kd .Some(true)")]
    [InlineData("U1.3", "o2", "func p(o: Option<i32>) -> i32 => 0\nfunc p(o: Option<bool>) -> i32 => 1\n" + RunHead + "    return p(if c => .None else => .Some(1))\n", "AmbiguousBinding_Kd p(if c => .None else => .Some(1))")]
    [InlineData("U1.2", "b6m", OptionH + RunHead + "    let a = h(match c\n        true => .None\n        false => .Some(true)\n    )\n    return a\n", "TypeMismatch_Kd .Some(true)")]
    [InlineData("U1.2", "b6y", "func h(o: Option<i32>, n: i32) -> i32 => 0\n" + RunHead + "    let a = h(if c => .None else => .Some(1), true)\n    return a\n", "NoApplicableOverload_Kd h(if c => .None else => .Some(1), true)")]
    [InlineData("U1.2", "b6x", RunK + "    let a = k(if c => func [] (x: i32) -> i32 => x else => func [] (x: i64) -> i32 => 0)\n    return a\n", "TypeMismatch_Kd func [] (x: i64) -> i32 => 0")]
    [InlineData("U1.6g3b", "g050", Twice + Idg + "func f(x: Array<(i64) -> i64>) -> i32 => 0\nfunc f(x: Array<(bool) -> bool>) -> i32 => 1\nfunc k(gv1: (i64) -> i64, c2: bool, c3: bool) -> i32\n    return f(if c2 => [idg, func [] (v) => v] else if c3 => [gv1@move, func [] (v) => v] else => [twice, func [] (v) => v])\n", "AmbiguousBinding_Kd f(if c2 => [idg, func [] (v) => v] else if c3 => [gv1@move, func [] (v) => v] else => [twice, func [] (v) => v])")]
    public void AFailingArgumentKeepsItsIndependentLanguageErrors(string unit, string form, string body, string required)
        => AssertLanguageErrors($"{form} [{unit}]", body, required);

    // The valid twins: B3's labeled join arguments at a named call (l1, b1, lt1 inside a Tuple) and a value call (l2, b1v); N11's twin
    // o1a, where arity leaves one candidate and the waiting join is checked at its Option<i32> (SPEC 10.1, 10.5.1); B6's twins in an
    // unannotated `let` (b6m_ok, b6y_ok, b6x_ok at a value call).
    [Theory]
    [InlineData("U1.3", "l1", OptionH + KHead + "    return h(label l: if c => .None else => .Some(1))\n")]
    [InlineData("U1.3", "b1", OptionH + KHead + "    return h(" + LabeledDo + ".Some(1)\n    )\n")]
    [InlineData("U1.3", "lt1", "func h2(t: (Option<i32>, i32)) -> i32 => 0\n" + KHead + "    return h2((label l: if c => .None else => .Some(1), 1))\n")]
    [InlineData("U1.3", "l2", ValueG + "    return g(label l: if c => .None else => .Some(1))\n")]
    [InlineData("U1.3", "b1v", ValueG + "    return g(" + LabeledDo + ".Some(1)\n    )\n")]
    [InlineData("U1.3", "o1a", "func h(o: Option<i32>) -> i32 => 0\nfunc h(o: Option<bool>, n: i32) -> i32 => 1\n" + RunHead + "    return h(if c => .None else => .Some(1))\n")]
    [InlineData("U1.2", "b6m_ok", OptionH + RunHead + "    let a = h(match c\n        true => .None\n        false => .Some(1)\n    )\n    return a\n")]
    [InlineData("U1.2", "b6y_ok", "func h(o: Option<i32>, n: i32) -> i32 => 0\n" + RunHead + "    let a = h(if c => .None else => .Some(1), 2)\n    return a\n")]
    [InlineData("U1.2", "b6x_ok", RunK + "    let a = k(if c => func [] (x: i32) -> i32 => x else => func [] (x: i32) -> i32 => x + 1)\n    return a\n")]
    public void AWaitingJoinArgumentIsCheckedAtItsCall(string unit, string form, string body)
        => AssertRecords($"{form} [{unit}]", body, string.Empty, null);

    // B3 (R4's G1b N4): a Closure part of a value call at a Function parameter is erased as at a named call (SPEC 7.7.4, 10.5, 10.7): its
    // signature is applicability, and an environment that is not Owned (pv1) or a receiver that is not Shared (pv2) is the part's own
    // record after selection, as at the named call (pn1, pn2). R4's ev_env and en_env are pv1 and pn1 renamed.
    [Theory]
    [InlineData("U1.5", "pv1", RunG2 + EnvClosure + "    return g(f, 1)\n", "UnsatisfiedConstraint_Kd f")]
    [InlineData("U1.5", "pv2", RunG2 + VarClosure + "    return g(f, 1)\n", "TypeMismatch_Kd f")]
    [InlineData("U1.5", "pn1", CallAction + "func run2() -> i32\n" + EnvClosure + "    return call(f, 1)\n", "UnsatisfiedConstraint_Kd f")]
    [InlineData("U1.5", "pn2", CallAction + "func run2() -> i32\n" + VarClosure + "    return call(f, 1)\n", "TypeMismatch_Kd f")]
    public void AClosureIsErasedAtAValueCallAsAtANamedCall(string unit, string form, string body, string records)
        => AssertRecords($"{form} [{unit}]", body, records, null);

    // D2 (R4's U1.5 design review 2): a position with a typed part is typed at its typed parts' common Type whatever else it holds, and
    // the waiting `identity(1)` beside `x` takes it (SPEC 14.9.2 steps 2-3), at a join's child position (d2, its `match` d2_m, d2_lit3)
    // as at its root (d2_root) and as an element (d2_elem); so it is evidence for a slot (d2_g; d2_g_t writes it), and with no candidate
    // of that Type the call has none (d2_n). U1.6g (a): typed parts that conflict at a position fix no Type without the call, so the
    // position waits and no candidate is best (e4; SPEC 10.5.1, 10.4; it was the join's own record under BU-43, U1.6).
    [Theory]
    [InlineData("U1.5", "d2", IdentityT + FTuples + KX + "    return f" + D2Join + "\n", "")]
    [InlineData("U1.5", "d2_m", IdentityT + FTuples + KX + "    return f(match c\n        true => (identity(1), 2)\n        false => (x, 3)\n    )\n", "")]
    [InlineData("U1.5", "d2_lit3", IdentityT + FTuples + KX + "    return f(if c => (identity(1), 2) else => (if c => x else => 5, 3))\n", "")]
    [InlineData("U1.5", "d2_root", IdentityT + FScalars + KX + "    return f(if c => identity(1) else => x)\n", "")]
    [InlineData("U1.5", "d2_elem", IdentityT + FTuples + KX + "    return f((if c => identity(1) else => x, 2))\n", "")]
    [InlineData("U1.5", "d2_g", IdentityT + "func g<T>(t: (T, i32)) -> i32 => 0\n" + KX + "    return g" + D2Join + "\n", "")]
    [InlineData("U1.5", "d2_g_t", IdentityT + "func g<T>(t: (T, i32)) -> i32 => 0\n" + KX + "    return g<i64>" + D2Join + "\n", "")]
    [InlineData("U1.5", "d2_n", IdentityT + "func f(x: (bool, i32)) -> i32 => 1\n" + KX + "    return f" + D2Join + "\n", "NoApplicableOverload_Kd f" + D2Join)]
    [InlineData("U1.6g", "e4", IdentityT + FTuples + "func k(c: u8, x: i64, b: bool) -> i32\n    return f(match c\n        0 => (identity(1), 2)\n        1 => (x, 3)\n        _ => (b, 4)\n    )\n", "AmbiguousBinding_Kd f(match c")]
    public void ATypedPartGivesItsPositionItsType(string unit, string form, string body, string records)
        => AssertRecords($"{form} [{unit}]", body, records, null);

    // R4's N3 (U1.6): `[x, 4]` holds no Never and no waiting part, so it is independently typable as Array<i64> (SPEC 4.3.1, 12.3.1) and
    // supplies the join its Type (14.9.2 step 2). No `[N of T]` parameter admits an established Array (4.1, 10.2, 4.4.1), so the call has
    // no candidate at a named call (n3) as at a value call (n3_v); an Array<i64> parameter admits it (n3_arr).
    [Theory]
    [InlineData("U1.6", "n3", IdentityT + "func f(x: [2 of i64]) -> i32 => 0\n" + KX + "    return f(" + N3Join + ")\n", "NoApplicableOverload_Kd f(" + N3Join + ")")]
    [InlineData("U1.6", "n3_v", IdentityT + "func g(x: [2 of i64]) -> i32 => 0\n" + KX + "    let h = g\n    return h(" + N3Join + ")\n", "NoApplicableOverload_Kd h(" + N3Join + ")")]
    [InlineData("U1.6", "n3_arr", IdentityT + "func f(x: Array<i64>) -> i32 => 0\n" + KX + "    return f(" + N3Join + ")\n", "")]
    public void ATypedArraySourceTypesItsJoinAsAWhole(string unit, string form, string body, string records)
        => AssertRecords($"{form} [{unit}]", body, records, null);

    // U1.6g (a) and (b) (SPEC 10.5.1, 14.9.2 step 2; supersedes BU-43's "fails at once" for call arguments): typed parts with no common
    // Type fix no complete Type without the call, so a call argument holding them waits. No candidate is best with two (sc_ov, and the
    // Tuple results sc_tov; 10.4); one candidate completes it at its Type, where a typed source that does not fit is its own record (sc_fi,
    // sc_elem, and the value call sc_h; 23.1.2), a Tuple source that awaits its position still the join's (sc_tfi), and Tuple results of
    // another length fit no candidate (sc_len; 10.5.1's last paragraph). Outside a call argument the conflict is the join's (sc_let).
    [Theory]
    [InlineData("U1.6g", "sc_ov", FScalars + KXB + "    return f(if c => x else => b)\n", "AmbiguousBinding_Kd f(if c => x else => b)")]
    [InlineData("U1.6g", "sc_fi", FI64 + KXB + "    return f(if c => x else => b)\n", "TypeMismatch_Kd b")]
    [InlineData("U1.6g", "sc_elem", FI64 + KXB + "    return f((if c => x else => b))\n", "TypeMismatch_Kd b")]
    [InlineData("U1.6g", "sc_h", FI64 + KXB + "    let h = f\n    return h(if c => x else => b)\n", "TypeMismatch_Kd b")]
    [InlineData("U1.6g", "sc_tov", FTuples + KXB + "    return f(if c => (x, 1) else => (b, 2))\n", "AmbiguousBinding_Kd f(if c => (x, 1) else => (b, 2))")]
    [InlineData("U1.6g", "sc_tfi", FTuple + KXB + "    return f(if c => (x, 1) else => (b, 2))\n", "TypeMismatch_Kd if c => (x, 1) else => (b, 2)")]
    [InlineData("U1.6g", "sc_len", FTuple + KXB + "    return f(if c => (x, 1) else => (x, 2, 3))\n", "NoApplicableOverload_Kd f(if c => (x, 1) else => (x, 2, 3))")]
    [InlineData("U1.6g", "sc_let", KXB + "    let v = if c => x else => b\n    return 0\n", "TypeMismatch_Kd if c => x else => b")]
    public void TypedPartsInConflictAtACallArgumentWaitForTheCall(string unit, string form, string body, string records)
        => AssertRecords($"{form} [{unit}]", body, records, null);

    // U1.6g2 (SPEC 10.5.1's last paragraph, 14.9.2 step 3, 7.7.4): step 3 applies at every position of a join's results, so at a child
    // position typed at a Function Item's Type the anonymous function beside it takes that Type, with no call, and fails at itself (c1;
    // the Closure value c2). Parts that do not wait keep the position's Type and are adapted at the call (c3, ch_same: valid).
    [Theory]
    [InlineData("U1.6g2", "c1", Twice + FFnPair + KHead + "    let t = twice\n    return f(if c => (t, 3) else => (func [] (v) => v, 4))\n", "TypeMismatch_Kd func [] (v) => v")]
    [InlineData("U1.6g2", "c2", Twice + FFnPair + KHead + "    let cl = func [] (v: i64) -> i64 => v\n    return f(if c => (cl@move, 3) else => (func [] (v) => v, 4))\n", "TypeMismatch_Kd func [] (v) => v")]
    [InlineData("U1.6g2", "c3", Twice + FFnPair + KHead + "    let t = twice\n    return f(if c => (t, 3) else => (t, 4))\n", "")]
    [InlineData("U1.6g2", "ch_same", Twice + FFnPair + KHead + "    return f(if c => (twice, 3) else => (twice, 4))\n", "")]
    public void StepThreeTypesTheWaitingPartsOfEveryTypedPosition(string unit, string form, string body, string records)
        => AssertRecords($"{form} [{unit}]", body, records, null);

    // U1.6f (the owner, 2026-10-31, reading I): a join's array results are classified per index at every number of sources (SPEC
    // 10.5.1), as an array argument's elements are, so index 1's literals select between `[2 of i64]` and `[2 of bool]` with two sources
    // (sg4b; wj_arr, which U1.5 held ambiguous), three (u6f_fix_three), one beside Never (sg4, b1_afix_W_fov) or none (sg4a, the
    // literal-only u6f_fix_lit), Tuple elements included (u6f_fix_tup); an `Array<T>` index holds the elements of the sources that have
    // it (u6f_dyn_len, u6f_g_len). A literal index that fits no candidate leaves none (g244, b1_afix_W_fb), as a source of another length
    // does at `[N of T]` (u6f_fix_len, 4.3.1); with every index waiting no candidate is best (u6f_dyn_wait); at a bare slot the join's Type
    // keeps one element position (14.9.2 step 2), so a waiting element leaves T unbound (u6f_g_wait).
    [Theory]
    [InlineData("U1.6f", "sg4b", IdentityT + FFixed + KD + "    return f(if c => [if d => identity(1) else => 2, 3] else => [4, 5])\n", "")]
    [InlineData("U1.6f", "wj_arr", IdentityT + FFixed + KHead + "    return f(if c => [identity(1), 2] else => [2, 3])\n", "")]
    [InlineData("U1.6f", "u6f_fix_three", IdentityT + FFixed + KD + "    return f(if c => [identity(1), 2] else => (if d => [identity(3), 4] else => [5, 6]))\n", "")]
    [InlineData("U1.6f", "sg4", IdentityT + Stop + FFixed + KD + "    return f(if c => [if d => identity(1) else => 2, 3] else => stop())\n", "")]
    [InlineData("U1.6f", "b1_afix_W_fov", IdentityT + Stop + FFixed + KD + "    return f(if c => [(if d => identity(1) else => 2), 3] else => stop())\n", "")]
    [InlineData("U1.6f", "sg4a", IdentityT + FFixed + KD + "    return f([if d => identity(1) else => 2, 3])\n", "")]
    [InlineData("U1.6f", "u6f_fix_lit", FFixed + KHead + "    return f(if c => [1, 2] else => [3, 4])\n", "")]
    [InlineData("U1.6f", "u6f_fix_tup", IdentityT + "func f(x: [2 of (i64, i32)]) -> i32 => 0\nfunc f(x: [2 of (bool, i32)]) -> i32 => 1\n" + KHead + "    return f(if c => [(identity(1), 1), (2, 2)] else => [(3, 3), (4, 4)])\n", "")]
    [InlineData("U1.6f", "u6f_dyn_len", IdentityT + FArrays + KHead + "    return f(if c => [identity(1), 2, 3] else => [4, 5])\n", "")]
    [InlineData("U1.6f", "u6f_g_len", IdentityT + "func g<T>(x: Array<T>) -> i32 => 0\n" + KHead + "    return g(if c => [identity(1), 2, 3] else => [4])\n", "")]
    [InlineData("U1.6f", "g244", IdentityT + Stop + "func f(x: [2 of bool]) -> i32 => 1\n" + KHead + "    return f(if c => [identity(3), 4] else => [stop(), 3])\n", "NoApplicableOverload_Kd f(if c => [identity(3), 4] else => [stop(), 3])")]
    [InlineData("U1.6f", "b1_afix_W_fb", IdentityT + Stop + "func f(x: [2 of bool]) -> i32 => 1\n" + KD + "    return f(if c => [(if d => identity(1) else => 2), 3] else => stop())\n", "NoApplicableOverload_Kd f(if c => [(if d => identity(1) else => 2), 3] else => stop())")]
    [InlineData("U1.6f", "u6f_fix_len", IdentityT + FFixed + KHead + "    return f(if c => [identity(1), 2] else => [3, 4, 5])\n", "NoApplicableOverload_Kd f(if c => [identity(1), 2] else => [3, 4, 5])")]
    [InlineData("U1.6f", "u6f_dyn_wait", IdentityT + FArrays + KHead + "    return f(if c => [identity(1)] else => [identity(2)])\n", "AmbiguousBinding_Kd f(if c => [identity(1)] else => [identity(2)])")]
    [InlineData("U1.6f", "u6f_g_wait", IdentityT + "func g<T>(x: T) -> i32 => 0\n" + KHead + "    return g(if c => [identity(1), 2] else => [3, 4])\n", "UnboundTypeArgument_Kd g(if c => [identity(1), 2] else => [3, 4])")]
    public void AJoinsArrayResultsAreClassifiedPerIndex(string unit, string form, string body, string records)
        => AssertRecords($"{form} [{unit}]", body, records, null);

    // B1 (R4's G1b B1 and U1.5 design review D1): a join its call classifies fits position by position (SPEC 10.5.1 last paragraph,
    // 10.5.4, 14.9.2). A position with no typed part whose part awaits its call (`identity(1)` beside `2`) is checked by the selected
    // candidate only, its literal taking no part in applicability, so with `f` overloaded no candidate is best (10.4): at the join's own
    // position (wj_ov, pw8, its `match` pw16, pw17 in a `let`), as an element (wj_elem) and at a child position (wj_tup, its `match`
    // wj_tup_m; wj_arr moved to U1.6f's rows). With one candidate the sources get their own records, as `g(identity(1))` and `g(2)` do (wj_one_n, wj_pick_n,
    // wj_ctor_n, wj_tup_n, and wj_one_v at a value call). U1.6 (BU's U1.5 review B1): every part of a child position is formed alike
    // (AddResultPart), so a nested waiting join in the Tuple result of a join with one source beside Never is flattened as with two
    // (sg1; sg1b), and its one candidate gives the sources' records (sg1n).
    [Theory]
    [InlineData("U1.5", "wj_ov", IdentityT + FPairs + KHead + "    return f(" + WJoin + ", 3)\n", "AmbiguousBinding_Kd f(" + WJoin + ", 3)")]
    [InlineData("U1.5", "pw8", IdentityT + FScalars + KHead + "    return f(" + WJoin + ")\n", "AmbiguousBinding_Kd f(" + WJoin + ")")]
    [InlineData("U1.5", "pw16", IdentityT + FScalars + KHead + "    return f(match c\n        true => identity(1)\n        false => 2\n    )\n", "AmbiguousBinding_Kd f(match c")]
    [InlineData("U1.5", "pw17", IdentityT + FPairs + KHead + "    let a = f(" + WJoin + ", 0)\n    return a\n", "AmbiguousBinding_Kd f(" + WJoin + ", 0)")]
    [InlineData("U1.5", "wj_elem", IdentityT + FTuples + KHead + "    return f((" + WJoin + ", 3))\n", "AmbiguousBinding_Kd f((" + WJoin + ", 3))")]
    [InlineData("U1.5", "wj_tup", IdentityT + FTuples + KHead + "    return f(" + WTuples + ")\n", "AmbiguousBinding_Kd f(" + WTuples + ")")]
    [InlineData("U1.5", "wj_tup_m", IdentityT + FTuples + KHead + "    return f(match c\n        true => (identity(1), 2)\n        false => (2, 3)\n    )\n", "AmbiguousBinding_Kd f(match c")]
    [InlineData("U1.5", "wj_one_n", IdentityT + GBool + KHead + "    return g(" + WJoin + ")\n", WSources)]
    [InlineData("U1.5", "wj_one_v", IdentityT + GBool + KHead + "    let h = g\n    return h(" + WJoin + ")\n", WSources)]
    [InlineData("U1.5", "wj_pick_n", IdentityT + Pick + "func k(c: bool, v: bool) -> bool\n    return pick(" + WJoin + ", v)\n", WSources)]
    [InlineData("U1.5", "wj_ctor_n", IdentityT + BoxInit + KHead + "    let b: Box<bool> = Box.init(" + WJoin + ")\n    return 0\n", WSources)]
    [InlineData("U1.5", "wj_tup_n", IdentityT + "func f(x: (bool, i32)) -> i32 => 0\n" + KHead + "    return f(" + WTuples + ")\n", WSources)]
    [InlineData("U1.6", "sg1", IdentityT + Stop + FTuples + KD + "    return f(if c => (" + SgJoin + ", 3) else => stop())\n", "AmbiguousBinding_Kd f(if c => (" + SgJoin + ", 3) else => stop())")]
    [InlineData("U1.6", "sg1b", IdentityT + FTuples + KD + "    return f(if c => (" + SgJoin + ", 3) else => (" + SgJoin + ", 3))\n", "AmbiguousBinding_Kd f(if c => (" + SgJoin + ", 3) else => (" + SgJoin + ", 3))")]
    [InlineData("U1.6", "sg1n", IdentityT + Stop + "func g(x: (bool, i32)) -> i32 => 0\n" + KD + "    return g(if c => (" + SgJoin + ", 3) else => stop())\n", WSources)]
    public void AWaitingJoinPositionTakesNoPartInApplicability(string unit, string form, string body, string records)
        => AssertRecords($"{form} [{unit}]", body, records, null);

    // B1's valid twins: the candidate that the other positions or arguments fix completes the waiting position (wj_pick, wj_ctor_exp at
    // its expected Type, wj_nest, wj_fn, pw13, pw19, wj_tup_ok, and wj_ov_v at a value call); a literal position still selects
    // (wj_tup_lit), and a waiting anonymous function's header selects at the join's position (wj_fnh) as at a child position (wj_tup_fn;
    // SPEC 10.5.4). U1.6: a typed part types a child position of a join with one source beside Never (sg3).
    [Theory]
    [InlineData("U1.5", "wj_pick", IdentityT + Pick + "func k(c: bool, v: i64) -> i64\n    return pick(" + WJoin + ", v)\n")]
    [InlineData("U1.5", "wj_ctor_exp", IdentityT + BoxInit + KHead + "    let b: Box<i64> = Box.init(" + WJoin + ")\n    return 0\n")]
    [InlineData("U1.5", "wj_nest", IdentityT + Pick + "func k(c: bool, v: i64) -> i64\n    return pick(identity(" + WJoin + "), v)\n")]
    [InlineData("U1.5", "wj_fn", IdentityT + "func apply<F>(f: ref/F, x: i32) -> i32\n    F is Callable<(i32) -> i32>\n    return f(x)\nfunc twice(x: i32) -> i32 => x + x\nfunc mk() -> (i32) -> i32 => twice\n" + KHead + "    return apply(if c => identity(mk()) else => mk(), 1)\n")]
    [InlineData("U1.5", "pw13", IdentityT + "func pair<T>(a: T, b: T) -> i32 => 0\n" + KHead + "    let w: i64 = 3\n    return pair(" + WJoin + ", w)\n")]
    [InlineData("U1.5", "pw19", "struct C<T>\n    public var tag: i32 = 0\n    public init(x: T, y: T) => self.tag = 1\n" + IdentityT + "func k(c: bool, w: i64) -> i32\n    let v = C.init(" + WJoin + ", w)\n    return 0\n")]
    [InlineData("U1.5", "wj_tup_ok", IdentityT + "func f(x: (i64, i32)) -> i32 => 0\n" + KHead + "    return f(" + WTuples + ")\n")]
    [InlineData("U1.5", "wj_ov_v", IdentityT + "func g(x: i64, y: i32) -> i32 => 0\n" + KHead + "    let h = g\n    return h(" + WJoin + ", 3)\n")]
    [InlineData("U1.5", "wj_tup_lit", IdentityT + FTuples + KHead + "    return f(if c => (1, identity(2)) else => (2, 3))\n")]
    [InlineData("U1.5", "wj_fnh", "func run(action: () -> ()) -> i32 => 0\nfunc run(action: () -> i32) -> i32 => 1\n" + KHead + "    return run(if c => func () -> i32 => 1 else => func () -> i32 => 2)\n")]
    [InlineData("U1.5", "wj_tup_fn", "func f(x: (() -> (), i32)) -> i32 => 0\nfunc f(x: (() -> i32, i32)) -> i32 => 1\n" + KHead + "    return f(if c => (func () -> i32 => 1, 2) else => (func () -> i32 => 2, 3))\n")]
    [InlineData("U1.6", "sg3", IdentityT + Stop + FTuples + "func k(c: bool, d: bool, x: i64) -> i32\n    return f(if c => (if d => identity(1) else => x, 3) else => stop())\n")]
    public void AWaitingJoinPositionIsCompletedByTheSelectedCandidate(string unit, string form, string body)
        => AssertRecords($"{form} [{unit}]", body, string.Empty, null);

    // B2 (BU's U1.5 review, U1.6 design 1): a part that awaits its call awaits its position whatever its kind (SPEC 10.5.1, 14.9.2 step
    // 3), so a nested join all of whose sources are deferred calls awaits the outer join's position, and the selected candidate completes
    // it: at a named call (nd1, nd1b beside a literal), with explicit Type arguments (nd1g), under a `let` expectation (nd1e) and at a
    // value call (nd1v); nd1c, a literal in the inner join, was valid before.
    [Theory]
    [InlineData("U1.6", "nd1", IdentityT + "func f(x: i64) -> i32 => 0\n" + KD + "    return f" + NdJoin + "\n")]
    [InlineData("U1.6", "nd1b", IdentityT + "func f(x: i64) -> i32 => 0\n" + KD + "    return f(if c => (if d => identity(1) else => identity(2)) else => 3)\n")]
    [InlineData("U1.6", "nd1c", IdentityT + "func f(x: i64) -> i32 => 0\n" + KD + "    return f(if c => (if d => identity(1) else => 2) else => identity(3))\n")]
    [InlineData("U1.6", "nd1e", IdentityT + "func k(c: bool, d: bool) -> i64\n    let v: i64 = identity" + NdJoin + "\n    return v\n")]
    [InlineData("U1.6", "nd1g", IdentityT + "func g<T>(x: T) -> T => x@move\nfunc k(c: bool, d: bool) -> i64\n    return g<i64>" + NdJoin + "\n")]
    [InlineData("U1.6", "nd1v", IdentityT + "func f(x: i64) -> i32 => 0\n" + KD + "    let h = f\n    return h" + NdJoin + "\n")]
    public void ANestedJoinOfDeferredCallsAwaitsItsPosition(string unit, string form, string body)
        => AssertRecords($"{form} [{unit}]", body, string.Empty, null);

    // N1 (BU's U1.6 survivors design 1, G340): a failed part takes no part in its position's kind and fits every candidate (SPEC
    // 23.1.2), so a name that fails at a waiting position adds no record at the call: at the join's own position (n1), as an element
    // (f2), at a child position (e3) and in a labeled `do` (dl1). A failed join that waited binds its other sources at the Type its
    // position decided, so their own records stand beside the name's (g026, g196), as in their twins without it (g026_fix, g196_fix).
    // Open, the G340 remainder: a slot or an ambiguity that only the failed part could decide is dependent too, but these still give
    // AmbiguousBinding_Kd with two candidates (n1_ov, f2_ov, e3_ov, g238) and UnboundTypeArgument_Kd at a generic callee (g028).
    [Theory]
    [InlineData("U1.6", "n1", IdentityT + "func f(x: i64) -> i32 => 0\n" + KHead + "    return f(if c => undefinedName else => identity(1))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6", "f2", IdentityT + "func f(x: (i64, i32)) -> i32 => 0\n" + KHead + "    return f((if c => identity(1) else => undefinedName, 3))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6", "e3", IdentityT + "func f(x: (i64, i32)) -> i32 => 0\n" + KHead + "    return f(if c => (identity(1), 2) else => (undefinedName, 3))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6", "dl1", IdentityT + "func f(x: i64) -> i32 => 0\n" + KHead + "    return f(label s: do\n        if c => exit to s undefinedName\n        exit to s identity(1)\n    )\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6", "g026", IdentityT + GBool + KD + "    return g(if c => identity(7) else => (if d => undefinedName else => identity(4)))\n", "NoApplicableOverload_Kd identity(4); NoApplicableOverload_Kd identity(7); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6", "g026_fix", IdentityT + GBool + KD + "    return g(if c => identity(7) else => (if d => true else => identity(4)))\n", "NoApplicableOverload_Kd identity(4); NoApplicableOverload_Kd identity(7)")]
    [InlineData("U1.6", "g196", IdentityT + GOptions + KD + "    return g([if c => (if d => undefinedName else => identity(.Some(3))) else => .Some(8), .None])\n", "TypeMismatch_Kd 3; TypeMismatch_Kd 8; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6", "g196_fix", IdentityT + GOptions + KD + "    return g([if c => (if d => .None else => identity(.Some(3))) else => .Some(8), .None])\n", "TypeMismatch_Kd 3; TypeMismatch_Kd 8")]
    [InlineData("open", "n1_ov", IdentityT + FScalars + KHead + "    return f(if c => undefinedName else => identity(1))\n", "AmbiguousBinding_Kd f(if c => undefinedName else => identity(1)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("open", "f2_ov", IdentityT + FTuples + KHead + "    return f((if c => identity(1) else => undefinedName, 3))\n", "AmbiguousBinding_Kd f((if c => identity(1) else => undefinedName, 3)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("open", "e3_ov", IdentityT + FTuples + KHead + "    return f(if c => (identity(1), 2) else => (undefinedName, 3))\n", "AmbiguousBinding_Kd f(if c => (identity(1), 2) else => (undefinedName, 3)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("open", "g028", IdentityT + "func g<T>(x: (T, i32)) -> i32 => 0\n" + KD + "    return g((if c => .None else if d => undefinedName else => identity(.Some(3)), 3))\n", "UnboundTypeArgument_Kd g((if c => .None else if d => undefinedName else => identity(.Some(3)), 3)); UnresolvedBinding_Kd undefinedName")]
    [InlineData("open", "g238", IdentityT + "func f(x: (Option<i64>, i32)) -> i32 => 0\nfunc f(x: (Option<bool>, i32)) -> i32 => 1\n" + KD + "    return f((if c => (if d => .None else => identity(.Some(4))) else => undefinedName, 3))\n", "AmbiguousBinding_Kd f((if c => (if d => .None else => identity(.Some(4))) else => undefinedName, 3)); UnresolvedBinding_Kd undefinedName")]
    public void AFailedPartTakesNoPartInItsPosition(string unit, string form, string body, string records)
        => AssertRecords($"{form} [{unit}]", body, records, null);

    // B4 (R4's design review): a join inside a Tuple inside a join, nested to depth 1 to 32, at a named call and a value call, is valid
    // with `.None` leaves and, with one `.Some(true)` leaf, a Language error at that leaf (SPEC 10.5.1 last paragraph, 14.9.2 step 4); with
    // literal leaves at an `i64` position (R4's dlit, dvlit), each literal takes its position's Type (SPEC 12.3, 14.9.2 step 3); B2's nd
    // (U1.6), joins nested directly in a join's first source with deferred calls as every source, is valid. The row asserts the verdict
    // at every depth; the linear work it requires is shown by the row finishing in the unit run (TypeDagScalingTest's pattern: a
    // regression is a hang the runner's process timeout reports).
    [Theory]
    [InlineData("U1.4", "dn", false, false)]
    [InlineData("U1.4", "dn_n", false, true)]
    [InlineData("U1.4", "dv", true, false)]
    [InlineData("U1.4", "dv_n", true, true)]
    [InlineData("U1.4", "dlit", false, false)]
    [InlineData("U1.4", "dvlit", true, false)]
    [InlineData("U1.6", "nd", false, false)]
    public void NestedJoinArgumentsHaveTheirVerdictAtEveryDepth(string unit, string form, bool valueCall, bool failing)
    {
        for (var depth = 1; depth <= 32; depth++)
        {
            var body = form == "nd"
                ? NestedDeferredJoinArgument(depth)
                : NestedJoinArgument(depth, valueCall, failing, literal: form.EndsWith("lit", StringComparison.Ordinal));
            if (failing)
            {
                AssertLanguageErrors($"{form} d={depth} [{unit}]", body, "TypeMismatch_Kd .Some(true)");
            }
            else
            {
                AssertRecords($"{form} d={depth} [{unit}]", body, string.Empty, null);
            }
        }
    }

    // U1.4 (warm Bind allocation): a warm rebind of the depth-32 nested join argument allocates nothing.
    [Fact]
    [Trait("Purpose", "Allocation")]
    public void AWarmRebindOfANestedJoinArgumentAllocatesNothing()
    {
        var c = MirBuildTest.Compile(NestedJoinArgument(32, valueCall: false, failing: false, literal: false) + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var complete = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => complete &= c.Bind().IsComplete));
        Assert.True(complete, MinimalEmissionTest.Describe(c));
    }

    // The B4 program of `depth`: a join inside a Tuple inside a join, at a named call or a value call, with `.None` leaves (one
    // `.Some(true)` when failing) at Option<i32>, or literal leaves at i64.
    private static string NestedJoinArgument(int depth, bool valueCall, bool failing, bool literal)
    {
        var (type, join, plain) = literal ? ("i64", "1", "2") : ("Option<i32>", failing ? ".Some(true)" : ".None", ".None");
        for (var i = 1; i <= depth; i++)
        {
            (type, join, plain) = ($"({type}, i32)", $"if c => ({join}, {i}) else => ({plain}, 0)", $"({plain}, 0)");
        }

        return valueCall
            ? $"func k(c: bool, g: ({type}) -> i32) -> i32\n    return g({join})\n"
            : $"func inner(x: {type}) -> i32 => 0\nfunc k(c: bool) -> i32\n    return inner({join})\n";
    }

    // B2's nd program of `depth` (BU's U1.6 design): each join nested in the first source of the next, every other source a deferred
    // `identity(·)`, at f(i64).
    private static string NestedDeferredJoinArgument(int depth)
    {
        var join = "identity(1)";
        for (var i = 1; i <= depth; i++)
        {
            join = $"(if c => {join} else => identity({i + 1}))";
        }

        return $"{IdentityT}func f(x: i64) -> i32 => 0\nfunc k(c: bool) -> i32\n    return f{join}\n";
    }
}
