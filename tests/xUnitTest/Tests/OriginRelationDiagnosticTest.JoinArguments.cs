// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// The G216 (a) acceptance rows that implementation reviews 2 and 3 add to d3's (OriginRelationDiagnosticTest.ContextualParts.cs), written
// before the redesign one level higher (the owner, 2026-10-30): a join (`if`, `match`) at a call argument is valid (SPEC 10.5.1, 14.9.2);
// a join whose result sources wait for the call waits for it, for named calls and value calls alike, and an arm is bound in its own
// pattern scope; D-1 (the owner, 2026-10-29) records a relation the call judges at a position it opens at the argument, with the supplying
// payload related, whatever the Case's context; d3 §4.4 judges a join's sources as one value; a check never faults (AGENTS Diagnostics,
// SPEC 23). Forms are R4's (review 3's probes js*, jt2, mj2, pb1*) and BU's (review 2's B5 and G316's named twins, review 3's B6 and B7),
// copied as written, one form per program. Each row names its form and has its twin.
public partial class OriginRelationDiagnosticTest
{
    private const string PutT = "func put<T>(slot: uniq/T, v: T) => Kimi.Intrinsics.replace(slot, with: v@move)\n";

    private const string MatchNoneArms = "match c\n        true => .None\n        false => .None\n";

    private const string DepthHead = "func k(q: ref/i32, r: ref/i32, c: bool, d: bool) -> ref/i32 during r\n";

    private const string InnerDepth = "    func inner(p: ((Option<ref/(ref/i32 during q) during r>, i32), i32), x: ref/i32 during q) -> ref/i32 during r => x\n";

    private const string DepthJoin = "    return inner(if c => ((if d => .None else => .None, 1), 2) else => ((.None, 3), 4), q)\n";

    private const string PbH = "func h(o: Option<i32>) -> i32\n    return 0\n";

    private const string PbOption = "func k(o: Option<i32>) -> i32\n";

    private const string PbValue = "func k(o: Option<i32>, g: (Option<i32>) -> i32) -> i32\n";

    private const string OptionH = "func h(o: Option<i32>) -> i32 => 0\n";

    private const string RefOne = "func one(x: i32) -> i32 => x\n";

    private const string RefTwo = "func two(x: i32) -> i32 => x + 1\n";

    private const string RefThree = "func three(x: i64) -> i32 => 0\n";

    private const string ApFunc = "func ap(f: (i32) -> i32) -> i32 => f(1)\n";

    private const string RunHead = "func run(c: bool) -> i32\n";

    private const string RunG = "func run(c: bool, g: (Option<i32>) -> i32) -> i32\n";

    private const string RunK = "func run(c: bool, k: ((i32) -> i32) -> i32) -> i32\n";

    private static readonly string[] FaultCodes = ["CheckFaulted_Kd", "InternalInvariant_Kd", "Unsupported_Kd"];

    // R3-B1 (review 3): put's `v: T` is a position the call opens, so a contextual payload's relation inside a join argument, also inside a
    // Tuple element (js2, js5), is recorded once at the argument with the payload related, as the direct and Tuple forms are (js3, js4);
    // a join's well-formedness is recorded once at the argument (jt2, mj2). Each `_ok` twin passes a value that outlives `q` or states the
    // relation.
    [Theory]
    [InlineData("js2", PutT + "func bad(p: uniq/(Option<ref/i32 during q>), c: bool) -> i32\n    let short = 2\n    put(p, if c => .Some(short@ref) else => .None)\n    return 0\n", "UnsatisfiedOriginRelation_Kd if c => .Some(short@ref) else => .None", "short@ref")]
    [InlineData("js2_ok", PutT + "func good(p: uniq/(Option<ref/i32 during q>), c: bool, w: ref/i32 during q) -> i32\n    put(p, if c => .Some(w) else => .None)\n    return 0\n", "", null)]
    [InlineData("js3", PutT + "func bad(p: uniq/(Option<ref/i32 during q>)) -> i32\n    let short = 2\n    put(p, .Some(short@ref))\n    return 0\n", "UnsatisfiedOriginRelation_Kd .Some(short@ref)", "short@ref")]
    [InlineData("js3_ok", PutT + "func good(p: uniq/(Option<ref/i32 during q>), w: ref/i32 during q) -> i32\n    put(p, .Some(w))\n    return 0\n", "", null)]
    [InlineData("js4", PutT + "func bad(p: uniq/((Option<ref/i32 during q>, i32))) -> i32\n    let short = 2\n    put(p, (.Some(short@ref), 1))\n    return 0\n", "UnsatisfiedOriginRelation_Kd (.Some(short@ref), 1)", "short@ref")]
    [InlineData("js4_ok", PutT + "func good(p: uniq/((Option<ref/i32 during q>, i32)), w: ref/i32 during q) -> i32\n    put(p, (.Some(w), 1))\n    return 0\n", "", null)]
    [InlineData("js5", PutT + "func bad(p: uniq/((Option<ref/i32 during q>, i32)), c: bool) -> i32\n    let short = 2\n    put(p, (if c => .Some(short@ref) else => .None, 1))\n    return 0\n", "UnsatisfiedOriginRelation_Kd (if c => .Some(short@ref) else => .None, 1)", "short@ref")]
    [InlineData("js5_ok", PutT + "func good(p: uniq/((Option<ref/i32 during q>, i32)), c: bool, w: ref/i32 during q) -> i32\n    put(p, (if c => .Some(w) else => .None, 1))\n    return 0\n", "", null)]
    [InlineData("jt2", JoinHead + NestedInner + "    return inner(if c => .None else => .None, q)\n", "UnprovenOriginRelation_Kd if c => .None else => .None", null)]
    [InlineData("jt2_ok", JoinHead + WithPremise + NestedInner + "    return inner(if c => .None else => .None, q)\n", "", null)]
    [InlineData("mj2", JoinHead + NestedInner + "    return inner((" + MatchNoneArms + "    ), q)\n", "UnprovenOriginRelation_Kd (match c", null)]
    [InlineData("mj2_ok", JoinHead + WithPremise + NestedInner + "    return inner((" + MatchNoneArms + "    ), q)\n", "", null)]
    public void AJoinArgumentIsRecordedOnceAtTheArgument(string form, string body, string records, string? payload)
        => AssertRecordsRelating(form, body, records, payload);

    // Depth 2 (the redesign's structure row): a join inside a Tuple inside a join is one argument, judged and recorded once at it (dn2), with
    // a payload at an opened position related (dn2p, D-1); each twin states the relation or passes a value that outlives `q`.
    [Theory]
    [InlineData("dn2", DepthHead + InnerDepth + DepthJoin, "UnprovenOriginRelation_Kd if c => ((if d => .None else => .None, 1), 2) else => ((.None, 3), 4)", null)]
    [InlineData("dn2_ok", DepthHead + WithPremise + InnerDepth + DepthJoin, "", null)]
    [InlineData("dn2p", PutT + "func bad(p: uniq/((Option<ref/i32 during q>, i32)), c: bool, d: bool) -> i32\n    let short = 2\n    put(p, if c => (if d => .Some(short@ref) else => .None, 1) else => (.None, 2))\n    return 0\n", "UnsatisfiedOriginRelation_Kd if c => (if d => .Some(short@ref) else => .None, 1) else => (.None, 2)", "short@ref")]
    [InlineData("dn2p_ok", PutT + "func good(p: uniq/((Option<ref/i32 during q>, i32)), c: bool, d: bool, w: ref/i32 during q) -> i32\n    put(p, if c => (if d => .Some(w) else => .None, 1) else => (.None, 2))\n    return 0\n", "", null)]
    public void ANestedJoinArgumentIsRecordedOnceAtTheArgument(string form, string body, string records, string? payload)
        => AssertRecordsRelating(form, body, records, payload);

    // Scaling (the redesign's structure row): k join arguments of join-in-Tuple depth d each record once at their argument, k records at
    // every size (kd); with the premise every size is valid (kd_ok). The class has no scaling helper, so the row asserts the records at
    // each size, not the work.
    [Theory]
    [InlineData("kd", false)]
    [InlineData("kd_ok", true)]
    public void JoinArgumentsRecordOncePerArgumentAtEverySize(string form, bool premise)
    {
        foreach (var count in new[] { 1, 2, 4, 8 })
        {
            foreach (var depth in new[] { 1, 2, 3 })
            {
                var (type, join) = NestedJoin(depth);
                var parameters = string.Concat(Enumerable.Range(0, count).Select(i => $"p{i}: {type}, "));
                var arguments = string.Concat(Enumerable.Repeat(join + ", ", count));
                var body = JoinHead + (premise ? WithPremise : string.Empty) + $"    func inner({parameters}x: ref/i32 during q) -> ref/i32 during r => x\n    return inner({arguments}q)\n";
                var records = premise ? string.Empty : string.Join("; ", Enumerable.Repeat($"UnprovenOriginRelation_Kd {join}", count));
                AssertRecords($"{form} k={count} d={depth}", body, records, null);
            }
        }
    }

    // R3-B2 (review 3): a `match` argument whose arm result uses the arm's pattern binding, at a value call (pb1b; in a `let`, pb1g; pb1h_v
    // over a by-value Subject) and a named call (pb1e, in a `let`), and the named forms without an arm binding (pb1c) or over a bool
    // (pb1d), is valid: the arm is bound in its own scope (SPEC 14.9.2) and the join waits for its call (SPEC 10.5.1). Dev accepts pb1b
    // and pb1g; the invalid twins are AFailingJoinArgumentIsALanguageError's pb1*_n rows and pb1h.
    [Theory]
    [InlineData("pb1b", PbValue + "    return g(match o\n        .Some(let x) => .Some(x)\n        .None => .None\n    )\n")]
    [InlineData("pb1c", PbH + PbOption + "    return h(match o\n        .Some(let x) => .Some(1)\n        .None => .None\n    )\n")]
    [InlineData("pb1d", PbH + "func k(c: bool) -> i32\n    return h(match c\n        true => .Some(1)\n        false => .None\n    )\n")]
    [InlineData("pb1e", PbH + PbOption + "    let r = h(match o\n        .Some(let x) => .Some(x)\n        .None => .None\n    )\n    return r\n")]
    [InlineData("pb1g", PbValue + "    let r = g(match o\n        .Some(let x) => .Some(x)\n        .None => .None\n    )\n    return r\n")]
    [InlineData("pb1h_v", PbValue + "    return g(match o@move\n        .Some(let x) => Option.Some(x)\n        .None => .None\n    )\n")]
    public void AMatchArgumentBindsItsArmInItsOwnScope(string form, string body)
        => AssertRecords(form, body, string.Empty, null);

    // BU's review-2 B5 forms at a value call (b5g, b5go, b5k) and G316's named twins (b5h, b5ho, b5ap), B7's `match` of references at a
    // value call (b7k) and its named twin (b7ap), and B6's valid twins in an unannotated `let` (b6h_ok, b6g_ok, b6ap_ok, b6fn_ok): a join
    // whose sources wait for the call is checked at the selected parameter Type on both call kinds (SPEC 10.5.1, 10.5.3, 14.9.2), so each
    // is valid. Dev accepts b5g, b5go, b5k, b7k, b6g_ok and b6fn_ok.
    [Theory]
    [InlineData("b5g", RunG + "    return g(if c => .None else => .Some(1))\n")]
    [InlineData("b5go", RunG + "    return g(if c => Option.None else => .Some(1))\n")]
    [InlineData("b5k", RefOne + RefTwo + RunK + "    return k(if c => one else => two)\n")]
    [InlineData("b5h", OptionH + RunHead + "    return h(if c => .None else => .Some(1))\n")]
    [InlineData("b5ho", OptionH + RunHead + "    return h(if c => Option.None else => .Some(1))\n")]
    [InlineData("b5ap", RefOne + RefTwo + ApFunc + RunHead + "    return ap(if c => one else => two)\n")]
    [InlineData("b7k", RefOne + RefTwo + RunK + "    return k(match c\n        true => one\n        false => two\n    )\n")]
    [InlineData("b7ap", RefOne + RefTwo + ApFunc + RunHead + "    return ap(match c\n        true => one\n        false => two\n    )\n")]
    [InlineData("b6h_ok", OptionH + RunHead + "    let a = h(if c => .None else => .Some(1))\n    return a\n")]
    [InlineData("b6g_ok", RunG + "    let a = g(if c => .None else => .Some(1))\n    return a\n")]
    [InlineData("b6ap_ok", RefOne + RefTwo + ApFunc + RunHead + "    let a = ap(if c => one else => two)\n    return a\n")]
    [InlineData("b6fn_ok", ApFunc + RunHead + "    let a = ap(if c => func [] (x: i32) -> i32 => x else => func [] (x: i32) -> i32 => x + 1)\n    return a\n")]
    public void AJoinArgumentWaitsForItsCallOnBothCallKinds(string form, string body)
        => AssertRecords(form, body, string.Empty, null);

    // The invalid twins: a join source that does not fit the selected parameter Type is a Language error at that source, on both call
    // kinds (SPEC 10.1: a waiting argument is checked after selection, not per candidate; 14.9.2 step 4), and B6's forms in an unannotated
    // `let` (a mismatch, a wrong arity, an unresolved payload name, a value call, references, and anonymous functions, which fault on dev
    // too) keep their Language errors. SPEC 23.1.2 leaves dependent records implementation-defined, so the row requires the failing part's
    // record (`required`, `Code text`; empty: any record) and allows further records, never Unsupported_Kd or a fault of the check. b6fn's
    // location is open: SPEC 10.5.4 lets an anonymous function's header filter candidates, which a join of them may or may not do. pb1h and
    // pb1h_n: a bare Place Subject is borrowed (SPEC 15.1.6, 14.8.1: `x` is `ref/i32`), so the qualified `Option.Some(x)` keeps its own
    // Type (10.5.1), which fits no candidate at either call kind (D1; R4, 2026-10-31).
    [Theory]
    [InlineData("pb1b_n", PbValue + "    return g(match o\n        .Some(let x) => .Some(x == 0)\n        .None => .None\n    )\n", "TypeMismatch_Kd .Some(x == 0)")]
    [InlineData("pb1c_n", PbH + PbOption + "    return h(match o\n        .Some(let x) => .Some(true)\n        .None => .None\n    )\n", "TypeMismatch_Kd .Some(true)")]
    [InlineData("pb1d_n", PbH + "func k(c: bool) -> i32\n    return h(match c\n        true => .Some(true)\n        false => .None\n    )\n", "TypeMismatch_Kd .Some(true)")]
    [InlineData("pb1e_n", PbH + PbOption + "    let r = h(match o\n        .Some(let x) => .Some(x == 0)\n        .None => .None\n    )\n    return r\n", "TypeMismatch_Kd .Some(x == 0)")]
    [InlineData("pb1g_n", PbValue + "    let r = g(match o\n        .Some(let x) => .Some(x == 0)\n        .None => .None\n    )\n    return r\n", "TypeMismatch_Kd .Some(x == 0)")]
    [InlineData(
        "pb1h",
        PbValue + "    return g(match o\n        .Some(let x) => Option.Some(x)\n        .None => .None\n    )\n",
        "NoApplicableOverload_Kd g(match o")]
    [InlineData(
        "pb1h_n",
        PbValue + "    return g(match o\n        .Some(let x) => Option.Some(x == 0)\n        .None => .None\n    )\n",
        "NoApplicableOverload_Kd g(match o")]
    [InlineData("b5g_n", RunG + "    return g(if c => .None else => .Some(true))\n", "TypeMismatch_Kd .Some(true)")]
    [InlineData("b5go_n", RunG + "    return g(if c => Option.None else => .Some(true))\n", "TypeMismatch_Kd .Some(true)")]
    [InlineData("b5k_n", RefOne + RefThree + RunK + "    return k(if c => one else => three)\n", "TypeMismatch_Kd three")]
    [InlineData("b5h_n", OptionH + RunHead + "    return h(if c => .None else => .Some(true))\n", "TypeMismatch_Kd .Some(true)")]
    [InlineData("b5ho_n", OptionH + RunHead + "    return h(if c => Option.None else => .Some(true))\n", "TypeMismatch_Kd .Some(true)")]
    [InlineData("b5ap_n", RefOne + RefThree + ApFunc + RunHead + "    return ap(if c => one else => three)\n", "TypeMismatch_Kd three")]
    [InlineData("b7k_n", RefOne + RefThree + RunK + "    return k(match c\n        true => one\n        false => three\n    )\n", "TypeMismatch_Kd three")]
    [InlineData("b7ap_n", RefOne + RefThree + ApFunc + RunHead + "    return ap(match c\n        true => one\n        false => three\n    )\n", "TypeMismatch_Kd three")]
    [InlineData("b6h", OptionH + RunHead + "    let a = h(if c => .None else => .Some(true))\n    return a\n", "TypeMismatch_Kd .Some(true)")]
    [InlineData("b6ar", OptionH + RunHead + "    let a = h(if c => .None else => .Some(1), 2)\n    return a\n", "NoApplicableOverload_Kd h(if c => .None else => .Some(1), 2)")]
    [InlineData("b6u", OptionH + RunHead + "    let a = h(if c => .None else => .Some(undefinedName))\n    return a\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("b6g", RunG + "    let a = g(if c => .None else => .Some(true))\n    return a\n", "TypeMismatch_Kd .Some(true)")]
    [InlineData("b6ap", RefOne + RefThree + ApFunc + RunHead + "    let a = ap(if c => one else => three)\n    return a\n", "TypeMismatch_Kd three")]
    [InlineData("b6fn", ApFunc + RunHead + "    let a = ap(if c => func [] (x: i32) -> i32 => x else => func [] (x: i64) -> i32 => 0)\n    return a\n", "")]
    public void AFailingJoinArgumentIsALanguageError(string form, string body, string required)
        => AssertLanguageErrors(form, body, required);

    // The SPEC 23.1.2 check of an invalid form: the program parses without a record, and its Error records include each of `required`
    // (each `Code text`, "; " between them; empty: any record), allow further dependent records, and never Unsupported_Kd or a fault of
    // the check.
    private static void AssertLanguageErrors(string form, string body, string required)
    {
        var source = body + Main;
        CompilationTestHelper.ParseSuccess(source, "Hello.kimi");
        var errors = ContextualCaptureTest.PublishedRecords(source).Where(static x => x.Severity == DiagnosticSeverity.Error).ToArray();
        var shown = errors.Select(static x => $"{x.Code} {(x.Text ?? string.Empty).Split('\n')[0]}").ToArray();
        string[] each = required.Length == 0 ? [] : required.Split("; ");
        Assert.True(
            shown.Length > 0 && each.All(shown.Contains) && !errors.Any(static x => FaultCodes.Contains(x.Code)),
            $"{form}: expected a Language error{(required.Length == 0 ? string.Empty : $" with [{required}]")} and no {string.Join(", ", FaultCodes)}, found [{string.Join("; ", shown)}]");
    }

    // The depth-d join of JoinArgumentsRecordOncePerArgumentAtEverySize and its parameter Type: a join whose sources are Tuples holding the
    // depth d - 1 join or a plain part of the same Type.
    private static (string Type, string Join) NestedJoin(int depth)
    {
        var (type, join, plain) = ("Option<ref/(ref/i32 during q) during r>", ".None", ".None");
        for (var i = 1; i <= depth; i++)
        {
            (type, join, plain) = ($"({type}, i32)", $"if c => ({join}, {i}) else => ({plain}, 0)", $"({plain}, 0)");
        }

        return (type, join);
    }
}
