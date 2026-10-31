// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// G216 (a) T1: Part 1's (argument completion's) rows beside d4's (OriginRelationDiagnosticTest.JoinArguments.cs), under the owner-adopted
// G216a-PLAN.md §3. Each row's first value names the Part 1 unit expected to pass it (U1.2 to U1.5). Forms are BU's design review
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
    // function join at a value call (b6x: no candidates, so the join is checked at k's parameter Type, 14.9.2 step 4).
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

    // B4 (R4's design review): a join inside a Tuple inside a join, nested to depth 1 to 32, at a named call and a value call, is valid
    // with `.None` leaves and, with one `.Some(true)` leaf, a Language error at that leaf (SPEC 10.5.1 last paragraph, 14.9.2 step 4); with
    // literal leaves at an `i64` position (R4's dlit, dvlit), each literal takes its position's Type (SPEC 12.3, 14.9.2 step 3). The row
    // asserts the verdict at every depth; the linear work it requires is shown by the row finishing in the unit run (TypeDagScalingTest's
    // pattern: a regression is a hang the runner's process timeout reports).
    [Theory]
    [InlineData("U1.4", "dn", false, false)]
    [InlineData("U1.4", "dn_n", false, true)]
    [InlineData("U1.4", "dv", true, false)]
    [InlineData("U1.4", "dv_n", true, true)]
    [InlineData("U1.4", "dlit", false, false)]
    [InlineData("U1.4", "dvlit", true, false)]
    public void NestedJoinArgumentsHaveTheirVerdictAtEveryDepth(string unit, string form, bool valueCall, bool failing)
    {
        for (var depth = 1; depth <= 32; depth++)
        {
            var body = NestedJoinArgument(depth, valueCall, failing, literal: form.EndsWith("lit", StringComparison.Ordinal));
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
}
