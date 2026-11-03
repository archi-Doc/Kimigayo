// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
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

    private const string FDicts = "func fd(x: Dictionary<i64, i64>) -> i32 => 0\nfunc fd(x: Dictionary<i64, bool>) -> i32 => 1\n";

    private const string FDictFn = "func fdf(x: Dictionary<i64, (i64) -> i64>) -> i32 => 0\n";

    private const string KXB3 = "func k(c: bool, d: bool, e: bool, x: i64, b: bool) -> i32\n";

    private const string BoxInit = "struct Box<T>\n    public let value: T\n    public init(value: T) => self.value = value@move\n";

    private const string WJoin = "if c => identity(1) else => 2";

    private const string WTuples = "if c => (identity(1), 2) else => (2, 3)";

    private const string KD = "func k(c: bool, d: bool) -> i32\n";

    private const string NdJoin = "(if c => (if d => identity(1) else => identity(2)) else => identity(3))";

    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";

    private const string SgJoin = "if d => identity(1) else => 2";

    private const string N3Join = "if c => [identity(1), 3] else => [x, 4]";

    private const string FFixed = "func f(x: [2 of i64]) -> i32 => 0\nfunc f(x: [2 of bool]) -> i32 => 1\n";

    private const string FArrays = "func f(x: Array<i64>) -> i32 => 0\nfunc f(x: Array<bool>) -> i32 => 1\n";

    private const string ZerosN = "func zeros<length N>() -> [N of i64] => [N of 0]\nfunc takeN<length M>(x: [M of i64]) -> i32 => 0\nfunc takes3(x: [3 of i64]) -> i32 => 0\n";

    private const string Consume = "func consume<T>(x: Array<T>) -> i32 => 0\nfunc consume<length N, T>(x: [N of T]) -> i32 => 1\n";

    private const string RefLocals = "func k(c: bool) -> i32\n    var x: i32 = 1\n    var y: i32 = 2\n";

    private const string RefI64 = "func k(c: bool, r: ref/i64) -> i32\n";

    private const string OneI64 = "func one(v: i64) -> i32 => 0\n";

    private const string HaT = "func ha<T>(x: [2 of T]) -> i32 => 0\n";

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
    // SPEC revision B (DECISIONS BU-80; the owner's "Accept as is", 2026-11-02): a value call fits its argument sources one by one
    // before its one signature, so b6x's closure that fits no `(i32) -> i32` leaves the call no candidate (class (2)); g050 is valid
    // (class (1)'s erasure form, AWaitingJoinPositionIsCompletedByTheSelectedCandidate).
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
    [InlineData("U1.2", "b6x", RunK + "    let a = k(if c => func [] (x: i32) -> i32 => x else => func [] (x: i64) -> i32 => 0)\n    return a\n", "NoApplicableOverload_Kd k(if c => func [] (x: i32) -> i32 => x else => func [] (x: i64) -> i32 => 0)")]
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
    // of that Type the call has none (d2_n). SPEC revision B (BU-80, superseding U1.6g (a) at call arguments): typed parts that conflict
    // at a position are argument sources fitted per candidate, so no candidate admits them (e4; SPEC 10.5.1, 10.8). BU's landing B2
    // rows: a part-by-part join is fitted per position and its typed part is evidence (e11, t3x; SPEC 10.5.1), and a reference that needs
    // S waits while the typed source selects (e6b; SPEC 10.5.3, 10.5.1); a typed `o` beside `.None` selects (e1) and binds T (e2); SPEC
    // 10.5.1's example at a bare slot (w1); a typed Function value binds T, at which the anonymous function is checked (e5g; 10.8, 10.5.4).
    [Theory]
    [InlineData("U1.5", "d2", IdentityT + FTuples + KX + "    return f" + D2Join + "\n", "")]
    [InlineData("U1.5", "d2_m", IdentityT + FTuples + KX + "    return f(match c\n        true => (identity(1), 2)\n        false => (x, 3)\n    )\n", "")]
    [InlineData("U1.5", "d2_lit3", IdentityT + FTuples + KX + "    return f(if c => (identity(1), 2) else => (if c => x else => 5, 3))\n", "")]
    [InlineData("U1.5", "d2_root", IdentityT + FScalars + KX + "    return f(if c => identity(1) else => x)\n", "")]
    [InlineData("U1.5", "d2_elem", IdentityT + FTuples + KX + "    return f((if c => identity(1) else => x, 2))\n", "")]
    [InlineData("U1.5", "d2_g", IdentityT + "func g<T>(t: (T, i32)) -> i32 => 0\n" + KX + "    return g" + D2Join + "\n", "")]
    [InlineData("U1.5", "d2_g_t", IdentityT + "func g<T>(t: (T, i32)) -> i32 => 0\n" + KX + "    return g<i64>" + D2Join + "\n", "")]
    [InlineData("U1.5", "d2_n", IdentityT + "func f(x: (bool, i32)) -> i32 => 1\n" + KX + "    return f" + D2Join + "\n", "NoApplicableOverload_Kd f" + D2Join)]
    [InlineData("B2b", "e4", IdentityT + FTuples + "func k(c: u8, x: i64, b: bool) -> i32\n    return f(match c\n        0 => (identity(1), 2)\n        1 => (x, 3)\n        _ => (b, 4)\n    )\n", "NoApplicableOverload_Kd f(match c")]
    [InlineData("U1.2", "e11", IdentityT + FTuples + KX + "    return f(if c => (identity(1), 2) else => (if c => (x, 3) else => (identity(4), 5)))\n", "")]
    [InlineData("U1.2", "t3x", IdentityT + "func f(x: ((i64, i32), i32)) -> i32 => 0\nfunc f(x: ((bool, i32), i32)) -> i32 => 1\n" + KX + "    return f(if c => ((identity(1), 2), 3) else => ((x, 3), 4))\n", "")]
    [InlineData("U1.3", "e6b", "func one(x: i64) -> i64 => x\nfunc one(x: bool) -> bool => x\nfunc f(x: ((i64) -> i64, i32)) -> i32 => 0\nfunc f(x: ((bool) -> bool, i32)) -> i32 => 1\nfunc k(c: bool, gv: (i64) -> i64) -> i32\n    return f(if c => (one, 1) else => (gv@move, 2))\n", "")]
    [InlineData("U1.2", "e1", FOptionTuples + "func k(c: bool, o: Option<i64>) -> i32\n    return f(if c => (.None, 1) else => (o, 2))\n", "")]
    [InlineData("U1.5", "e2", "func g<T>(t: (Option<T>, i32)) -> i32 => 0\nfunc k(c: bool, o: Option<i64>) -> i32\n    return g(if c => (.None, 1) else => (o, 2))\n", "")]
    [InlineData("U1.5", "w1", "func identity<U>(x: U) -> U => x@move\nfunc g<T>(t: T) -> i32 => 0\n" + KX + "    return g(if c => (identity(1), 2) else => (x, 3))\n", "")]
    [InlineData("U1.5", "e5g", "func g<T>(t: ((T) -> T, i32)) -> i32 => 0\nfunc k(c: bool, gv: (i64) -> i64) -> i32\n    return g(if c => (func [] (v) => v, 1) else => (gv@move, 2))\n", "")]
    public void ATypedPartGivesItsPositionItsType(string unit, string form, string body, string records)
        => AssertRecords($"{form} [{unit}]", body, records, null);

    // SPEC revision B, argument sources (BU-80; the owner, 2026-11-02): an argument's sources are found through Tuple and array literals,
    // fills and control-flow expressions, and each is fitted per candidate at its position (SPEC 10.5.1). So `[x, 4]` beside
    // `[identity(1), 3]` fits `[2 of i64]` at a named call (n3) as at a value call (n3_v), as an Array<i64> parameter admits it (n3_arr),
    // and a typed array literal source beside a waiting `zeros()` fits per candidate (z6n_lit64, m_j3_lit64), as two typed sources do
    // (t_j2lit64_N, t_j2lit64_3). At a fixed-array position an array literal source's count and a fill source's length are length
    // evidence (4.4.1), so they bind `M` beside a waiting source (z6_join_n, m_jN_fill, m_jN_fill64); conflicting counts make the candidate
    // inapplicable (u1_counts; 10.8), and waiting sources alone bind nothing (u2_nozeros). Without an expected Type a join's typed array
    // literal is Array<i64>, so `zeros()` misfits (z8_lit64; 4.3.1, 14.9.2 step 2). Unchanged: an empty literal needs an expected element
    // Type (u4_empty), fixed and dynamic literals get no preference (c2_consume_join, c3_consume), and one join's sources that differ
    // only in safe reference layers over one Type agree on it (q1_join; 10.5.1, 14.9.2 step 2).
    [Theory]
    [InlineData("B2b", "n3", IdentityT + "func f(x: [2 of i64]) -> i32 => 0\n" + KX + "    return f(" + N3Join + ")\n", "")]
    [InlineData("B2b", "n3_v", IdentityT + "func g(x: [2 of i64]) -> i32 => 0\n" + KX + "    let h = g\n    return h(" + N3Join + ")\n", "")]
    [InlineData("U1.6", "n3_arr", IdentityT + "func f(x: Array<i64>) -> i32 => 0\n" + KX + "    return f(" + N3Join + ")\n", "")]
    [InlineData("B2b", "z6n_lit64", ZerosN + KHead + "    return takeN(if c => zeros() else => [1@i64, 2@i64])\n", "")]
    [InlineData("B2b", "m_j3_lit64", ZerosN + KHead + "    return takes3(if c => zeros() else => [1@i64, 2@i64, 3@i64])\n", "")]
    [InlineData("B2b", "t_j2lit64_N", ZerosN + KHead + "    return takeN(if c => [3@i64, 4@i64] else => [1@i64, 2@i64])\n", "")]
    [InlineData("B2b", "t_j2lit64_3", ZerosN + KHead + "    return takes3(if c => [3@i64, 4@i64, 5@i64] else => [1@i64, 2@i64, 6@i64])\n", "")]
    [InlineData("B2b", "z6_join_n", ZerosN + KHead + "    return takeN(if c => zeros() else => [1, 2])\n", "")]
    [InlineData("B2b", "m_jN_fill", ZerosN + KHead + "    return takeN(if c => zeros() else => [2 of 0])\n", "")]
    [InlineData("B2a", "m_jN_fill64", ZerosN + KHead + "    return takeN(if c => zeros() else => [2 of 0@i64])\n", "")]
    [InlineData("B2b", "u1_counts", ZerosN + KHead + "    return takeN(if c => [1, 2] else => [1, 2, 3])\n", "NoApplicableOverload_Kd takeN(if c => [1, 2] else => [1, 2, 3])")]
    [InlineData("B2a", "u2_nozeros", ZerosN + KHead + "    return takeN(if c => zeros() else => zeros())\n", "UnboundTypeArgument_Kd takeN(if c => zeros() else => zeros())")]
    [InlineData("B2b", "z8_lit64", ZerosN + KHead + "    let j = if c => zeros() else => [1@i64, 2@i64]\n    return 0\n", "NoApplicableOverload_Kd zeros()")]
    [InlineData("B2b", "u4_empty", "func g<T>(x: Array<T>) -> i32 => 0\n" + KHead + "    return g([])\n", "UnboundTypeArgument_Kd g([])")]
    [InlineData("B2b", "c2_consume_join", Consume + KHead + "    return consume(if c => [1, 2] else => [3, 4])\n", "AmbiguousBinding_Kd consume(if c => [1, 2] else => [3, 4])")]
    [InlineData("B2b", "c3_consume", Consume + KHead + "    return consume([1, 2, 3])\n", "AmbiguousBinding_Kd consume([1, 2, 3])")]
    [InlineData("B2b", "q1_join", IdentityT + RefLocals + "    let r = identity(if c => x@ref else => y)\n    return 0\n", "")]
    public void AnArgumentSourceIsFittedPerCandidate(string unit, string form, string body, string records)
        => AssertRecords($"{form} [{unit}]", body, records, null);

    // SPEC 10.5.1, 14.9.2 step 2 (revision B's fix round, BU-80; the group review's B1 and B4): the result sources of one control-flow
    // expression that differ only in safe reference layers over one Type, or are literals of it, agree on that Type and are value-read:
    // at the root argument (p41, its mirror q1a, `match` q1k), in a `let` (q1d) and nested (q1h) alike, at a fixed parameter (q1f), beside
    // an overload (q1e), at `ref/T` (q1i), beside a waiting call (t01) and between two references (r_one), where the position's Type is
    // evidence as a typed argument's is. They agree as 14.9.2 step 2 forms a `let` join's Type, part by part: Tuple sources by element
    // (p46 as its `let` twin; the literal clause in each part, r18 and its argument twins s07, s08), while an array literal source with a
    // typed element supplies a candidate and is compared whole (BU-80 Fix-D), so the elements of one literal (q2d, as alone r01) and of
    // different array sources (q2f, r09) are conflicting evidence.
    [Theory]
    [InlineData("p41", IdentityT + RefLocals + "    let r = identity(if c => x@ref else => 1)\n    return 0\n", "")]
    [InlineData("q1a", IdentityT + RefLocals + "    let r = identity(if c => 1 else => x@ref)\n    return 0\n", "")]
    [InlineData("q1k", IdentityT + RefLocals + "    let r = identity(match c\n        true => x@ref\n        false => 1\n    )\n    return 0\n", "")]
    [InlineData("q1d", RefLocals + "    let j = if c => x@ref else => 1\n    return 0\n", "")]
    [InlineData("q1h", IdentityT + RefLocals + "    let r = identity(if c => (if c => x@ref else => 1) else => y)\n    return 0\n", "")]
    [InlineData("q1f", OneI64 + RefI64 + "    return one(if c => r else => 1)\n", "")]
    [InlineData("q1e", FScalars + RefI64 + "    return f(if c => r else => 1)\n", "")]
    [InlineData("q1i", "func inspect<T>(value: ref/T) -> i32 => 0\n" + RefLocals + "    return inspect(if c => x@ref else => 1)\n", "")]
    [InlineData("t01", IdentityT + OneI64 + RefI64 + "    return one(if c => r else => identity(r))\n", "")]
    [InlineData("r_one", OneI64 + RefI64 + "    return one(if c => r else => r)\n", "")]
    [InlineData("p46", IdentityT + RefLocals + "    let r = identity(if c => (x@ref, 1) else => (y, 2))\n    return 0\n", "")]
    [InlineData("r18", RefLocals + "    let j = if c => (x@ref, 1) else => (1, y)\n    return 0\n", "")]
    [InlineData("s07", IdentityT + RefLocals + "    let r = identity(if c => (x@ref, 1) else => (1, y))\n    return 0\n", "")]
    [InlineData("s08", "func ft(x: (i64, i32)) -> i32 => 0\nfunc ft(x: (bool, i32)) -> i32 => 1\n" + RefI64 + "    return ft(if c => (r, 1) else => (1, 2))\n", "")]
    [InlineData("q2d", HaT + RefLocals + "    return ha(if c => [x@ref, y] else => [y, x])\n", "NoApplicableOverload_Kd ha(if c => [x@ref, y] else => [y, x]); TypeMismatch_Kd y")]
    [InlineData("r01", HaT + RefLocals + "    return ha([x@ref, y])\n", "NoApplicableOverload_Kd ha([x@ref, y])")]
    [InlineData("q2f", "func ga<T>(x: Array<T>) -> i32 => 0\n" + RefLocals + "    return ga(if c => [x@ref] else => [y])\n", "NoApplicableOverload_Kd ga(if c => [x@ref] else => [y])")]
    [InlineData("r09", HaT + RefLocals + "    return ha(if c => [x@ref, x@ref] else => [y, y])\n", "NoApplicableOverload_Kd ha(if c => [x@ref, x@ref] else => [y, y])")]
    public void OneJoinsResultSourcesAgreeByAValueRead(string form, string body, string records)
        => AssertRecords(form, body, records, null);

    // SPEC 10.5.1, 4.3.1, 14.9.2 step 2 (revision B's fix round, BU-80; the group review's B2 and B3): an array literal source is typed
    // whole. At a slot that takes it whole it gives Array<T> beside a waiting source (q3b), so a waiting `zeros()` beside it misfits
    // (p3f); in a `let` join it is typed as the literal alone, its elements' reference layers never merged (q2a, r10, as on dev).
    [Theory]
    [InlineData("q3b", IdentityT + "func g<T>(x: T) -> i32 => 0\n" + KX + "    return g(if c => [x] else => identity([x]))\n", "")]
    [InlineData("p3f", ZerosN + "func g<T>(x: T) -> i32 => 0\n" + KHead + "    return g(if c => [1@i64, 2@i64] else => zeros())\n", "NoApplicableOverload_Kd zeros()")]
    [InlineData("q2a", RefLocals + "    let j = if c => [x@ref] else => [y]\n    return 0\n", "TypeMismatch_Kd if c => [x@ref] else => [y]")]
    [InlineData("r10", RefLocals + "    let j = if c => [x@ref, y] else => [y, y]\n    return 0\n", "TypeMismatch_Kd y")]
    public void AnArrayLiteralSourceIsTypedWhole(string form, string body, string records)
        => AssertRecords(form, body, records, null);

    // SPEC 14.9.2 step 2, 10.5.1, 10.8 (revision B, the owner's (A); BU-80 Fix-D): formation membership is 14.9.2's own test, whether a
    // source supplies a candidate, in a `let` and as an argument alike. Sources that supply none are formed part by part, so references
    // and values at one part agree by a value read: an array of Tuples holding literals (w13, its argument twin w13a; a01 with two
    // elements; x10 beside literals, r18's class), a Dictionary (a13, a13a), fills holding a literal (a11). A source that supplies one is
    // compared whole: fills of references and of values conflict as their `let` twins do (w08a, w09a, w18a), and an array source's whole
    // evidence agrees with the Type an earlier source of the join fixed (a05 and its mirror a05m, p48), never fitted to it. A parameter
    // Type that an explicit Type argument or the signature fixes is the join's Target Result Type after selection (14.9.2 steps 1 and 2):
    // each source is fitted to it, a reference at a value element position value-read (3.5.3, 10.2; e2, r17; dev rejected both).
    [Theory]
    [InlineData("e2", "func g<T>(x: T) -> i32 => 0\n" + RefLocals + "    return g<Array<i32>>(if c => [x@ref] else => [y])\n", "")]
    [InlineData("r17", "func fi2(x: [2 of i32]) -> i32 => 0\n" + RefLocals + "    return fi2(if c => [x@ref, 1] else => [y, 2])\n", "")]
    [InlineData("w13", RefLocals + "    let j = if c => [(x@ref, 1)] else => [(y, 2)]\n    return 0\n", "")]
    [InlineData("w13a", IdentityT + RefLocals + "    let r = identity(if c => [(x@ref, 1)] else => [(y, 2)])\n    return 0\n", "")]
    [InlineData("a01", RefLocals + "    let j = if c => [(x@ref, 1), (y, 2)] else => [(y, 3)]\n    return 0\n", "")]
    [InlineData("x10", RefLocals + "    let j = if c => [(x@ref, 1)] else => [(1, 2)]\n    return 0\n", "")]
    [InlineData("x10a", IdentityT + RefLocals + "    let r = identity(if c => [(x@ref, 1)] else => [(1, 2)])\n    return 0\n", "")]
    [InlineData("a13", RefLocals + "    let j = if c => [x@ref: 1] else => [y: 2]\n    return 0\n", "")]
    [InlineData("a13a", IdentityT + RefLocals + "    let r = identity(if c => [x@ref: 1] else => [y: 2])\n    return 0\n", "")]
    [InlineData("a11", IdentityT + RefLocals + "    let r = identity(if c => ([2 of (x@ref, 1)], 3) else => ([2 of (y, 2)], 4))\n    return 0\n", "")]
    [InlineData("w08a", IdentityT + RefLocals + "    let r = identity(if c => [2 of x@ref] else => [2 of y])\n    return 0\n", "NoApplicableOverload_Kd identity(if c => [2 of x@ref] else => [2 of y])")]
    [InlineData("w09a", IdentityT + RefLocals + "    let r = identity(if c => [2 of x@ref] else => [2 of 1])\n    return 0\n", "TypeMismatch_Kd 1")]
    [InlineData("w18a", IdentityT + RefLocals + "    let r = identity(if c => ([2 of x@ref], 1) else => ([2 of y], 2))\n    return 0\n", "UnboundTypeArgument_Kd identity(if c => ([2 of x@ref], 1) else => ([2 of y], 2))")]
    [InlineData("a05", IdentityT + RefLocals + "    let r = identity(if c => [y, 1] else => [x@ref])\n    return 0\n", "NoApplicableOverload_Kd identity(if c => [y, 1] else => [x@ref])")]
    [InlineData("a05m", IdentityT + RefLocals + "    let r = identity(if c => [x@ref] else => [y, 1])\n    return 0\n", "NoApplicableOverload_Kd identity(if c => [x@ref] else => [y, 1])")]
    [InlineData("p48", IdentityT + RefLocals + "    let r = identity(if c => [x@ref] else => [y])\n    return 0\n", "NoApplicableOverload_Kd identity(if c => [x@ref] else => [y])")]
    public void FormationMembershipIsSupplyingACandidate(string form, string body, string records)
        => AssertRecords(form, body, records, null);

    // SPEC revision B (BU-80; supersedes U1.6g (a) and (b) at call arguments): typed sources in conflict at a call argument are argument
    // sources fitted per candidate (SPEC 10.5.1), as typed array elements are, so no candidate admits both: with two candidates (sc_ov,
    // the Tuple results sc_tov) and with one, at a named call (sc_fi, sc_elem, sc_tfi) and a value call (sc_h), the record is
    // NoApplicableOverload_Kd at the call, as a typed join's is (d1h). Tuple results of another length fit no candidate (sc_len; 10.5.1).
    // Outside a call argument the conflict is the join's (sc_let). B3a's review B1: a probe join with Dictionary results whose sources
    // conflict waits for its call as any other shape does, so the call keeps its records (d06: the one candidate's; n08: two candidates
    // both fit, as on d7f69e006). The fix moves n0f: its middle join, whose flattened parts conflict, now waits, and the call's record
    // replaces the join's own TypeMismatch_Kd, as at two levels (B3a design, class (2)).
    [Theory]
    [InlineData("B2b", "sc_ov", FScalars + KXB + "    return f(if c => x else => b)\n", "NoApplicableOverload_Kd f(if c => x else => b)")]
    [InlineData("B2b", "sc_fi", FI64 + KXB + "    return f(if c => x else => b)\n", "NoApplicableOverload_Kd f(if c => x else => b)")]
    [InlineData("B2b", "sc_elem", FI64 + KXB + "    return f((if c => x else => b))\n", "NoApplicableOverload_Kd f((if c => x else => b))")]
    [InlineData("B2b", "sc_h", FI64 + KXB + "    let h = f\n    return h(if c => x else => b)\n", "NoApplicableOverload_Kd h(if c => x else => b)")]
    [InlineData("B2b", "sc_tov", FTuples + KXB + "    return f(if c => (x, 1) else => (b, 2))\n", "NoApplicableOverload_Kd f(if c => (x, 1) else => (b, 2))")]
    [InlineData("B2b", "sc_tfi", FTuple + KXB + "    return f(if c => (x, 1) else => (b, 2))\n", "NoApplicableOverload_Kd f(if c => (x, 1) else => (b, 2))")]
    [InlineData("U1.6g", "sc_len", FTuple + KXB + "    return f(if c => (x, 1) else => (x, 2, 3))\n", "NoApplicableOverload_Kd f(if c => (x, 1) else => (x, 2, 3))")]
    [InlineData("U1.6g", "sc_let", KXB + "    let v = if c => x else => b\n    return 0\n", "TypeMismatch_Kd if c => x else => b")]
    [InlineData("B3a", "d06", "func fdi(x: Dictionary<i64, i64>) -> i32 => 0\nfunc k(c: bool, x: i64, y: i32) -> i32\n    return fdi(if c => [1: x] else => [2: y])\n", "TypeMismatch_Kd y")]
    [InlineData("B3a", "n08", FDicts + KXB + "    return fd(if c => [1: x] else => [2: b])\n", "AmbiguousBinding_Kd fd(if c => [1: x] else => [2: b])")]
    [InlineData("B3a", "n0f", IdentityT + FScalars + KXB3 + "    return f(if c => (if d => (if e => x else => b) else => identity(1)) else => x)\n", "NoApplicableOverload_Kd f(if c => (if d => (if e => x else => b) else => identity(1)) else => x)")]
    public void TypedSourcesInConflictAtACallArgumentFitNoCandidate(string unit, string form, string body, string records)
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
    // keeps one element position (14.9.2 step 2). SPEC revision B (BU-80 Fix-D; BU's decision 2): array sources that supply no candidate
    // are formed part by part (the Aggregate row), where class (3)'s late formation of a bare slot from a waiting element and literals is
    // still open (BU-80's open item beside p48), so u6f_g_wait keeps dev's UnboundTypeArgument_Kd.
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
    // (sg1; sg1b), and its one candidate gives the sources' records (sg1n). BU's landing B2 rows: a slot only waiting parts sit at stays
    // unbound, since a waiting argument is never evidence for an outer slot (SPEC 10.5.1): a deferred call beside Never (g125) or beside
    // another at a Tuple's child position (n2_flat).
    // SPEC revision B (DECISIONS BU-80; the owner's "Accept as is", 2026-11-02): the waiting part still takes no part, but a literal
    // or typed source beside it is fitted per candidate. Class (1): it rules out the candidates it does not fit, so pw8, pw16, pw17,
    // wj_ov, wj_elem, wj_tup, wj_tup_m, sg1 and sg1b select `f(i64)`. Class (2): with one candidate the sources' records become
    // NoApplicableOverload_Kd at the call (wj_one_n, wj_one_v, wj_pick_n, wj_ctor_n, wj_tup_n, sg1n).
    [Theory]
    [InlineData("U1.5", "wj_ov", IdentityT + FPairs + KHead + "    return f(" + WJoin + ", 3)\n", "")]
    [InlineData("U1.5", "pw8", IdentityT + FScalars + KHead + "    return f(" + WJoin + ")\n", "")]
    [InlineData("U1.5", "pw16", IdentityT + FScalars + KHead + "    return f(match c\n        true => identity(1)\n        false => 2\n    )\n", "")]
    [InlineData("U1.5", "pw17", IdentityT + FPairs + KHead + "    let a = f(" + WJoin + ", 0)\n    return a\n", "")]
    [InlineData("U1.5", "wj_elem", IdentityT + FTuples + KHead + "    return f((" + WJoin + ", 3))\n", "")]
    [InlineData("U1.5", "wj_tup", IdentityT + FTuples + KHead + "    return f(" + WTuples + ")\n", "")]
    [InlineData("U1.5", "wj_tup_m", IdentityT + FTuples + KHead + "    return f(match c\n        true => (identity(1), 2)\n        false => (2, 3)\n    )\n", "")]
    [InlineData("U1.5", "wj_one_n", IdentityT + GBool + KHead + "    return g(" + WJoin + ")\n", "NoApplicableOverload_Kd g(if c => identity(1) else => 2)")]
    [InlineData("U1.5", "wj_one_v", IdentityT + GBool + KHead + "    let h = g\n    return h(" + WJoin + ")\n", "NoApplicableOverload_Kd h(if c => identity(1) else => 2)")]
    [InlineData("U1.5", "wj_pick_n", IdentityT + Pick + "func k(c: bool, v: bool) -> bool\n    return pick(" + WJoin + ", v)\n", "NoApplicableOverload_Kd pick(if c => identity(1) else => 2, v)")]
    [InlineData("U1.5", "wj_ctor_n", IdentityT + BoxInit + KHead + "    let b: Box<bool> = Box.init(" + WJoin + ")\n    return 0\n", "NoApplicableOverload_Kd Box.init(if c => identity(1) else => 2)")]
    [InlineData("U1.5", "wj_tup_n", IdentityT + "func f(x: (bool, i32)) -> i32 => 0\n" + KHead + "    return f(" + WTuples + ")\n", "NoApplicableOverload_Kd f(if c => (identity(1), 2) else => (2, 3))")]
    [InlineData("U1.6", "sg1", IdentityT + Stop + FTuples + KD + "    return f(if c => (" + SgJoin + ", 3) else => stop())\n", "")]
    [InlineData("U1.6", "sg1b", IdentityT + FTuples + KD + "    return f(if c => (" + SgJoin + ", 3) else => (" + SgJoin + ", 3))\n", "")]
    [InlineData("U1.6", "sg1n", IdentityT + Stop + "func g(x: (bool, i32)) -> i32 => 0\n" + KD + "    return g(if c => (" + SgJoin + ", 3) else => stop())\n", "NoApplicableOverload_Kd g(if c => (if d => identity(1) else => 2, 3) else => stop())")]
    [InlineData("U1.3", "g125", IdentityT + Stop + "func g<T>(x: T) -> i32 => 0\nfunc k(c1: bool, c2: bool) -> i32\n    return g(label lp: if c2 => (if c1 => stop() else => identity(2)) else => stop())\n", "UnboundTypeArgument_Kd g(label lp: if c2 => (if c1 => stop() else => identity(2)) else => stop())")]
    [InlineData("U1.3", "n2_flat", IdentityT + "func first<T>(value: (T, i64)) -> () => ()\nfunc k(c: bool, d: bool, e: bool, x: i64) -> ()\n    first(if c => (identity(5), x) else => (identity(4), x))\n", "UnboundTypeArgument_Kd first(if c => (identity(5), x) else => (identity(4), x))")]
    public void AWaitingJoinPositionTakesNoPartInApplicability(string unit, string form, string body, string records)
        => AssertRecords($"{form} [{unit}]", body, records, null);

    // B1's valid twins: the candidate that the other positions or arguments fix completes the waiting position (wj_pick, wj_ctor_exp at
    // its expected Type, wj_nest, wj_fn, pw13, pw19, wj_tup_ok, and wj_ov_v at a value call); a literal position still selects
    // (wj_tup_lit), and a waiting anonymous function's header selects at the join's position (wj_fnh) as at a child position (wj_tup_fn;
    // SPEC 10.5.4). U1.6: a typed part types a child position of a join with one source beside Never (sg3). BU's landing B2 rows: a
    // waiting position is checked at the selected candidate (nd4; SPEC 10.5.1); Never supplies no candidate (SPEC 14.9.2 step 2), so a
    // Case beside it waits for the one candidate (g240) or an explicit Type argument (g199); the expected result fills a generic callee's
    // T (10.8) for a Case join (g126) and an array literal's waiting element (g065); references that need S wait (10.5.3) for the one
    // candidate (fs_root_refS_refS_fi) or the one an anonymous function's header selects (fs_root_refS_anonHeader_fov; 10.5.4).
    // SPEC revision B (DECISIONS BU-80; the owner's "Accept as is", 2026-11-02): g050, class (1)'s erasure form: `twice` and `gv1`
    // fit only `Array<(i64) -> i64>`, which is selected.
    // B3a (DECISIONS BU-80): a nested join inside an array (g046) or Tuple (g122) argument whose sources conflict waits for its call,
    // which completes it through its Join record. B3a's review B1: so does a probe join with Dictionary results whose sources conflict
    // (d01, its parenthesized form t0b).
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
    [InlineData("U1.5", "nd4", IdentityT + "func f(x: (i64, i32)) -> i32 => 0\nfunc k(c: bool, d: bool) -> i32\n    return f(if c => (if d => identity(1) else => identity(2), 3) else => (identity(4), 5))\n")]
    [InlineData("U1.2", "g240", Stop + "func f(x: Option<i64>) -> i32 => 0\nfunc k(c1: bool, c2: bool) -> i32\n    return f((if c2 => (if c1 => stop() else => .None) else => stop()))\n")]
    [InlineData("U1.3", "g199", Stop + "func g<T>(x: T) -> i32 => 0\nfunc k(c1: bool, c2: bool) -> i32\n    return g<Option<i64>>(label lp: if c2 => stop() else => (if c1 => .None else => stop()))\n")]
    [InlineData("U1.2", "g126", IdentityT + "func k(c1: bool) -> i32\n    let v: Option<i64> = identity(if c1 => .None else => .Some(2))\n    return 0\n")]
    [InlineData("U1.2", "g065", IdentityT + Stop + "func k(c1: bool, c2: bool) -> i32\n    let v: [2 of Option<i64>] = identity([if c1 => stop() else if c2 => identity(.Some(3)) else => .None, .None])\n    return 0\n")]
    [InlineData("U1.3", "fs_root_refS_refS_fi", Twice + Thrice + Idg + "func f(x: (i64) -> i64) -> i32 => 0\n" + KHead + "    return f(if c => idg else => idg)\n")]
    [InlineData("U1.3", "fs_root_refS_anonHeader_fov", Twice + Thrice + Idg + "func f(x: (i64) -> i64) -> i32 => 0\nfunc f(x: (bool) -> bool) -> i32 => 1\n" + KHead + "    return f(if c => idg else => func [] (v: i64) -> i64 => v)\n")]
    [InlineData("B2b-1", "g050", Twice + Idg + "func f(x: Array<(i64) -> i64>) -> i32 => 0\nfunc f(x: Array<(bool) -> bool>) -> i32 => 1\nfunc k(gv1: (i64) -> i64, c2: bool, c3: bool) -> i32\n    return f(if c2 => [idg, func [] (v) => v] else if c3 => [gv1@move, func [] (v) => v] else => [twice, func [] (v) => v])\n")]
    [InlineData("B3a", "g046", Twice + "func f(x: Array<(i64) -> i64>) -> i32 => 0\nfunc f(x: Array<(bool) -> bool>) -> i32 => 1\nfunc k(gv1: (i64) -> i64, c2: bool, c3: bool, c4: bool, c5: bool, gv6: (i64) -> i64, c7: bool, c8: bool) -> i32\n    return f([if c7 => func [] (v) => v else if c8 => (if c4 => (if c2 => gv1@move else if c3 => func [] (v: i64) -> i64 => v else => twice) else if c5 => func [] (v) => v else => func [] (v: i64) -> i64 => v) else => gv6@move, func [] (v) => v])\n")]
    [InlineData("B3a", "g122", Twice + Idg + FFnPair + "func k(gv1: (i64) -> i64, c2: bool, c3: bool, c4: bool, c5: bool) -> i32\n    return f((if c4 => idg else if c5 => (if c2 => twice else if c3 => idg else => gv1@move) else => func [] (v) => v, 3))\n")]
    [InlineData("B3a", "d01", Twice + FDictFn + "func k(c: bool, gv: (i64) -> i64) -> i32\n    return fdf(if c => [1: gv@move] else => [2: twice])\n")]
    [InlineData("B3a", "t0b", Twice + FDictFn + "func k(c: bool, gv: (i64) -> i64) -> i32\n    return fdf((if c => [1: gv@move] else => [2: twice]))\n")]
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
    // The G340 remainder, a slot or an ambiguity that only the failed part could decide (g028; n1_ov, f2_ov, e3_ov, g238), is U1.6h's
    // (OriginRelationDiagnosticTest.FailedFits).
    [Theory]
    [InlineData("U1.6", "n1", IdentityT + "func f(x: i64) -> i32 => 0\n" + KHead + "    return f(if c => undefinedName else => identity(1))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6", "f2", IdentityT + "func f(x: (i64, i32)) -> i32 => 0\n" + KHead + "    return f((if c => identity(1) else => undefinedName, 3))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6", "e3", IdentityT + "func f(x: (i64, i32)) -> i32 => 0\n" + KHead + "    return f(if c => (identity(1), 2) else => (undefinedName, 3))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6", "dl1", IdentityT + "func f(x: i64) -> i32 => 0\n" + KHead + "    return f(label s: do\n        if c => exit to s undefinedName\n        exit to s identity(1)\n    )\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6", "g026", IdentityT + GBool + KD + "    return g(if c => identity(7) else => (if d => undefinedName else => identity(4)))\n", "NoApplicableOverload_Kd identity(4); NoApplicableOverload_Kd identity(7); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6", "g026_fix", IdentityT + GBool + KD + "    return g(if c => identity(7) else => (if d => true else => identity(4)))\n", "NoApplicableOverload_Kd identity(4); NoApplicableOverload_Kd identity(7)")]
    [InlineData("U1.6", "g196", IdentityT + GOptions + KD + "    return g([if c => (if d => undefinedName else => identity(.Some(3))) else => .Some(8), .None])\n", "TypeMismatch_Kd 3; TypeMismatch_Kd 8; UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6", "g196_fix", IdentityT + GOptions + KD + "    return g([if c => (if d => .None else => identity(.Some(3))) else => .Some(8), .None])\n", "TypeMismatch_Kd 3; TypeMismatch_Kd 8")]
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

    // SPEC 10.5.1 (B3a, DECISIONS BU-80; U2j's readers, ComposedJoin and PartContribution, call the root test): IsRootArgumentJoin is
    // true for the probe's own join only, recorded where IsProbeJoin decides that it waits and kept after its call completes it
    // (`one(if c => identity(1) else => 2)`, `fo(if c => (if d => x else => y) else => x)`); false for a clean nested join, which has no
    // record, and for a nested join that waits on a conflict among its sources, whose conflict is its call's, though it holds a Join
    // record (`fo(if c => (if d => x else => b) else => x)`). The BU/R4 decision (2026-11-03, option (a)) replaced "no nested Join record".
    // It reads private members because no public layer observes the root test yet: U2j-a's observable rows replace this reflective pin.
    [Fact]
    public void OnlyTheRootArgumentJoinPassesTheRootTest()
    {
        const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var recorded = typeof(Binding).GetMethod("RecordedPart", Private)!;
        var rootTest = typeof(Binding).GetMethod("IsRootArgumentJoin", Private)!;
        IfKoto Join(Compilation c, string join)
            => KotoTree.Walk(c.SourceModules[0].RootKoto).OfType<IfKoto>().Single(x => x.ToString().StartsWith(join, StringComparison.Ordinal));
        object Record(Compilation c, string join) => recorded.Invoke(c.Binding, [Join(c, join)])!;
        string? Kind(Compilation c, string join) => Record(c, join) is var r ? r.GetType().GetProperty("Kind")!.GetValue(r)!.ToString() : null;
        bool Root(Compilation c, string join) => (bool)rootTest.Invoke(c.Binding, [Join(c, join)])!;
        var valid = MirBuildTest.Compile(IdentityT + "func one(x: i64) -> i32 => 0\nfunc k(c: bool) -> i32\n    return one(if c => identity(1) else => 2)\n" + Main);
        Assert.True(valid.Binding.Result.IsComplete, MinimalEmissionTest.Describe(valid)); // Its call completed it.
        Assert.Equal(("Join", true), (Kind(valid, "if c => identity(1)"), Root(valid, "if c => identity(1)")));
        const string Fo = "func fo(x: i64) -> i32 => 0\nfunc fo(x: bool) -> i32 => 1\nfunc k(c: bool, d: bool, x: i64, y: i64, b: bool) -> i32\n";
        var clean = MirBuildTest.Compile(Fo + "    return fo(if c => (if d => x else => y) else => x)\n" + Main);
        Assert.True(clean.Binding.Result.IsComplete, MinimalEmissionTest.Describe(clean));
        Assert.True(Root(clean, "if c =>"));
        Assert.Equal(Activator.CreateInstance(recorded.ReturnType), Record(clean, "if d => x")); // No record.
        Assert.False(Root(clean, "if d => x"));
        var conflict = MirBuildTest.Compile(Fo + "    return fo(if c => (if d => x else => b) else => x)\n" + Main);
        Assert.Equal(("Join", false), (Kind(conflict, "if d => x"), Root(conflict, "if d => x"))); // A conflict wait's Join record.
    }

    // SPEC revision B (the group review's B5): a warm rebind of a reference join at a call argument, whose sources revision B fits one
    // by one (tests/mir/calls.kimi's `shared`), allocates nothing, publication included.
    [Fact]
    [Trait("Purpose", "Allocation")]
    public void AWarmRebindOfAReferenceJoinArgumentAllocatesNothing()
    {
        var c = MirBuildTest.Compile("func see(target: ref/i32, by: i32) -> i32 => by\nfunc shared(flag: bool, n: i32) -> i32\n    let a: i32 = 1\n" +
            "    let b: i32 = 2\n    return see(if flag => a@ref else => b@ref, n)\n" + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var complete = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => complete &= c.Bind().IsComplete));
        Assert.True(complete, MinimalEmissionTest.Describe(c));
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
