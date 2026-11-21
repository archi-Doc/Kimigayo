// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// R7-P1: the call pipeline's acceptance rows, as CompilationTestHelper.AssertRecordsAtLines reads them: (unit, form, program, records),
// each verdict from SPEC. A unit other than guard is the R7 plan's unit whose rule makes the row pass: the row is Explicit until that
// unit lands and removes the marker; a guard row holds on dev and must keep holding.
public partial class ApplicabilityChainRegressionTest
{
    private const string Take2 = "func take(v: i32, w: i32) -> () => ()\nfunc f() -> ()\n";

    private const string CTie = "func c(a: bool, b: i32) -> () => ()\nfunc c(a: bool, b: i64) -> () => ()\nfunc f() -> ()\n";

    // Two candidates (PLAN G354 p23, p24): the generic one's F is refuted (T is bound by `t`; the Closure's result borrows its hidden
    // receiver, SPEC 8.10), so the overload is the only applicable candidate; U stays unbound and G's Constraint is not judged (10.8).
    private const string MakeFirst =
        "func make<T, U, V, F, G>(f: ref/F, g: ref/G, t: T, v: V) -> i32\n    F is Callable<() -> T>\n    G is Callable<() -> U>\n    return 0\n" +
        "func make<F, G>(f: ref/F, g: ref/G, t: i32, v: i64) -> i32 => 1\n";

    private const string MakeSecond =
        "func make<T, U, V, F, G>(g: ref/G, f: ref/F, t: T, v: V) -> i32\n    F is Callable<() -> T>\n    G is Callable<() -> U>\n    return 0\n" +
        "func make<F, G>(g: ref/G, f: ref/F, t: i32, v: i64) -> i32 => 1\n";

    private const string MakeBody =
        "func k() -> i32\n    let values: Array<i32> = [11, 22]\n    let others: Array<i32> = [33, 44]\n" +
        "    let c = func [values@move] () => values@ref\n    let e = func [others@move] () => others@ref\n    let n: i32 = 4\n";

    private const string Find = "func find(items: ref/[3 of i32], test: (i32) -> bool) -> i32 => 0\nfunc k(items: ref/[3 of i32]) -> i32\n    let target: i32 = 6\n";

    private const string FindBool = "func find(items: ref/[3 of i32], test: (bool) -> bool) -> i32 => 0\nfunc k(items: ref/[3 of i32]) -> i32\n    let target: i32 = 6\n";

    private const string ValueCallee = "func k(h: ((i32) -> i32, i32) -> i32) -> i32\n";

    // SPEC 10.1, 10.4, 12.3.3, 23.1.2 (PLAN G377, G500): beside an unbound failed argument, a misfit or a tie that reads no Type of that
    // argument is an independent failure and keeps its record (G377: the unresolved name hid NoApplicable; w2's and w6's sources are lost,
    // these are the forms PLAN names). The pinned rows of ACallBesideAFailedPartReportsWhatReadsNoArgumentType go when these pass.
    [Theory]
    [InlineData("P3", "g377-tu", Take2 + "    take(true, undefinedName)\n", "NoApplicableOverload_Kd: take(true, undefinedName) | UnresolvedBinding_Kd: take(true, undefinedName)", Explicit = true)]
    [InlineData("P3", "g377-ut", Take2 + "    take(undefinedName, true)\n", "NoApplicableOverload_Kd: take(undefinedName, true) | UnresolvedBinding_Kd: take(undefinedName, true)", Explicit = true)]
    [InlineData("guard", "g377-t1", Take2 + "    take(true, 1)\n", "NoApplicableOverload_Kd: take(true, 1)")]
    [InlineData("guard", "g377-u1", Take2 + "    take(undefinedName, 1)\n", "UnresolvedBinding_Kd: take(undefinedName, 1)")]
    [InlineData("P2", "g500-tie", CTie + "    c(undefinedName, 1)\n", "AmbiguousBinding_Kd: c(undefinedName, 1) | UnresolvedBinding_Kd: c(undefinedName, 1)", Explicit = true)]
    [InlineData("guard", "g500-tie1", CTie + "    c(true, 1)\n", "AmbiguousBinding_Kd: c(true, 1)")]
    public void AnIndependentFailureBesideAFailedArgumentKeepsItsRecord(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // SPEC 10.8 ("Unbound slots": Constraints needing the slot are not judged; traversal order never chooses), 10.1 step 5, 8.10 (PLAN
    // G354's remainder, DECISIONS BU-86): a judgment on the unbound U neither hides F's refutation nor depends on the argument order.
    [Theory]
    [InlineData("P3", "p24", MakeFirst + MakeBody + "    return make(c@ref, e@ref, n, 2)\n", "", Explicit = true)]
    [InlineData("P3", "p23", MakeSecond + MakeBody + "    return make(e@ref, c@ref, n, 2)\n", "", Explicit = true)]
    public void AJudgmentOnAnUnboundSlotHidesNoRefutation(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // SPEC 10.5.4, 23.1.2 (PLAN R1e, F's ArgFlow ruling 90): an anonymous function's own failure, which reads no selection, is reported
    // beside its unselected call. r1e-mut is the mutation `overload-parameter-type` (tests/diagnostics/mutations.json) in small;
    // t-vc1m the value call's arity form. Their twins: the selected call keeps the body's record, the valid body leaves the call's alone.
    [Theory]
    [InlineData("R1e", "r1e-mut", Find + "    return find(items, func [target] (value: bool) => value == target)\n", "NoApplicableOverload_Kd: return find(items, func [target] (value: bool) => value == target) | TypeMismatch_Kd: return find(items, func [target] (value: bool) => value == target)", Explicit = true)]
    [InlineData("guard", "r1e-mutsel", FindBool + "    return find(items, func [target] (value: bool) => value == target)\n", "TypeMismatch_Kd: return find(items, func [target] (value: bool) => value == target)")]
    [InlineData("guard", "r1e-mutok", Find + "    return find(items, func [target] (value: i32) => value == target)\n", "")]
    [InlineData("R1e", "t-vc1m", ValueCallee + "    return h(func (x) => missing, 1, 2)\n", "NoApplicableOverload_Kd: return h(func (x) => missing, 1, 2) | UnresolvedBinding_Kd: return h(func (x) => missing, 1, 2)", Explicit = true)]
    [InlineData("guard", "t-vc1", ValueCallee + "    return h(func (x) => x, 1, 2)\n", "NoApplicableOverload_Kd: return h(func (x) => x, 1, 2)")]
    public void AnAnonymousFunctionsOwnFailureIsReportedBesideItsCall(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);
}
