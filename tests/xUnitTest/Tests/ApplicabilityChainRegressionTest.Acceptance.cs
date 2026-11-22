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

    // PLAN G354 (p23, p24 and their single-candidate twins): the generic candidate's F is refuted (T is bound by `t`; the Closure's
    // result borrows its hidden receiver, SPEC 8.10); U stays unbound and G's Constraint is not judged (10.8). With the overload it is
    // the only applicable candidate; alone, the call has none.
    private const string MakeFirstOne =
        "func make<T, U, V, F, G>(f: ref/F, g: ref/G, t: T, v: V) -> i32\n    F is Callable<() -> T>\n    G is Callable<() -> U>\n    return 0\n";

    private const string MakeFirst = MakeFirstOne + "func make<F, G>(f: ref/F, g: ref/G, t: i32, v: i64) -> i32 => 1\n";

    private const string MakeSecondOne =
        "func make<T, U, V, F, G>(g: ref/G, f: ref/F, t: T, v: V) -> i32\n    F is Callable<() -> T>\n    G is Callable<() -> U>\n    return 0\n";

    private const string MakeSecond = MakeSecondOne + "func make<F, G>(g: ref/G, f: ref/F, t: i32, v: i64) -> i32 => 1\n";

    private const string MakeBody =
        "func k() -> i32\n    let values: Array<i32> = [11, 22]\n    let others: Array<i32> = [33, 44]\n" +
        "    let c = func [values@move] () => values@ref\n    let e = func [others@move] () => others@ref\n    let n: i32 = 4\n";

    private const string Find = "func find(items: ref/[3 of i32], test: (i32) -> bool) -> i32 => 0\nfunc k(items: ref/[3 of i32]) -> i32\n    let target: i32 = 6\n";

    private const string FindBool = "func find(items: ref/[3 of i32], test: (bool) -> bool) -> i32 => 0\nfunc k(items: ref/[3 of i32]) -> i32\n    let target: i32 = 6\n";

    private const string ValueCallee = "func k(h: ((i32) -> i32, i32) -> i32) -> i32\n";

    private const string MethodTie =
        "struct H\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func m(self: ref/Self, a: bool, b: i32) -> () => ()\n" +
        "    public func m(self: ref/Self, a: bool, b: i64) -> () => ()\nfunc f(h: ref/H) -> ()\n";

    private const string MethodOne =
        "struct H\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func m(self: ref/Self, a: i32, b: i32) -> () => ()\nfunc f(h: ref/H) -> ()\n";

    private const string InitTie =
        "struct P\n    public var a: i32\n    public init(a: bool, b: i32) => self.a = b\n    public init(a: bool, b: i64) => self.a = 0\nfunc k() -> ()\n";

    private const string InitOne = "struct P\n    public var a: i32\n    public init(a: i32, b: i32) => self.a = b\nfunc k() -> ()\n";

    private const string ValueTwo = "func f(g: (i32, i32) -> ()) -> ()\n";

    private const string CallableTie = "func both<F>(action: ref/F) -> i32\n    F is Callable<(bool, i32) -> i32>\n    F is Callable<(bool, i64) -> i32>\n";

    private const string CallableBoth = "func both<F>(action: ref/F) -> i32\n    F is Callable<(i32, i32) -> i32>\n    F is Callable<(i64, i64) -> i32>\n";

    private const string CallableOne = "func one<F>(action: ref/F) -> i32\n    F is Callable<(i32, i32) -> i32>\n";

    // Two Indexable conformances whose keys a literal fits alike (SPEC 4.7.1's own example: "pair[1] is ambiguous; write pair[1@i64]").
    private const string IndexPair =
        "struct Pair\n    Self is Indexable<i32>\n    Self is Indexable<i64>\n    associate Indexable<i32>.Element is i32\n    associate Indexable<i64>.Element is i32\n" +
        "    var a: i32 = 1\n    public init() => ()\n    public func index(self, key: ref/i32) -> ref/i32 during self => self.a@ref\n" +
        "    public func index(self, key: ref/i64) -> ref/i32 during self => self.a@ref\nfunc k(pair: ref/Pair) -> i32\n";

    private const string IndexOne =
        "struct One\n    Self is Indexable<i64>\n    associate Element is i32\n    var a: i32 = 1\n    public init() => ()\n" +
        "    public func index(self, key: ref/i64) -> ref/i32 during self => self.a@ref\nfunc k(one: ref/One) -> i32\n";

    // Two Addable conformances whose counterparts a literal fits alike (SPEC 13.3.4: candidates are never ranked).
    private const string AddPair =
        "struct V\n    Self is Addable<i32>\n    Self is Addable<i64>\n    associate Addable<i32>.Output is i32\n    associate Addable<i64>.Output is i32\n" +
        "    public init() => ()\n    public func added(self: ref/Self, right: ref/i32) -> i32 => 0\n" +
        "    public func added(self: ref/Self, right: ref/i64) -> i32 => 1\nfunc k(v: ref/V) -> i32\n";

    // A generic function, a generic constructor and Option's Cases, each with an argument that binds T or none.
    private const string Slots =
        "func mk<T>(v: T) -> Option<T> => .Some(v@move)\nfunc none<T>() -> Option<T> => .None\n" +
        "struct Bin<T>\n    public var v: T\n    public init(v: T) => self.v = v@move\nfunc k() -> ()\n    let n: i32 = 1\n";

    // SPEC 10.1, 10.4, 12.3.3, 23.1.2 (PLAN G377, G500): beside an unbound failed argument, a misfit or a tie that reads no Type of that
    // argument is an independent failure and keeps its record (G377: the unresolved name hid NoApplicable; w2's and w6's sources are lost,
    // these are the forms PLAN names). g377-tu contradicts ACallBesideAFailedPartReportsWhatReadsNoArgumentType's `take(true,
    // undefinedName)` row (UnresolvedBinding_Kd alone), which P3 moves with it; no existing row pins g377-ut's or g500-tie's form.
    [Theory]
    [InlineData("P3", "g377-tu", Take2 + "    take(true, undefinedName)\n", "NoApplicableOverload_Kd: take(true, undefinedName) | UnresolvedBinding_Kd: take(true, undefinedName)", Explicit = true)]
    [InlineData("P3", "g377-ut", Take2 + "    take(undefinedName, true)\n", "NoApplicableOverload_Kd: take(undefinedName, true) | UnresolvedBinding_Kd: take(undefinedName, true)", Explicit = true)]
    [InlineData("guard", "g377-t1", Take2 + "    take(true, 1)\n", "NoApplicableOverload_Kd: take(true, 1)")]
    [InlineData("guard", "g377-u1", Take2 + "    take(undefinedName, 1)\n", "UnresolvedBinding_Kd: take(undefinedName, 1)")]
    [InlineData("P2", "g500-tie", CTie + "    c(undefinedName, 1)\n", "AmbiguousBinding_Kd: c(undefinedName, 1) | UnresolvedBinding_Kd: c(undefinedName, 1)", Explicit = true)]
    [InlineData("guard", "g500-tie1", CTie + "    c(true, 1)\n", "AmbiguousBinding_Kd: c(true, 1)")]
    public void AnIndependentFailureBesideAFailedArgumentKeepsItsRecord(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // R7-P1's call classes (R7-P0's census: P3 makes the forms one pipeline), SPEC 10.1, 10.4, 12.3.3, 23.1.2: the same two rules at each
    // form that takes arguments, a method, a constructor, a function-typed value and a constraint-based value call (the direct form is
    // above): a tie that only a literal could break is ambiguous, and beside an unbound failed argument a misfit or a tie that reads no
    // Type of that argument keeps its record. On dev a763e10f7 the value calls keep a misfit's record (cf-v-mis-u, cf-f-mis-u), as the enum
    // Case form does (`E.A(true, undefinedName)` above), while the direct, method and constructor forms lose it and every form loses a tie.
    // With two Callable clauses the constraint-based call loses the misfit too (SPEC 8.10 "Several Constraints": `true` fits neither
    // signature, whatever the name is; PLAN G500 names these forms): cf-f2-tu and cf-f2-ut contradict the two `both` rows over
    // `(i32, i32)` and `(i64, i64)` of ACallBesideAFailedPartReportsWhatReadsNoArgumentType (UnresolvedBinding_Kd alone), which P2
    // moves with them. No existing row pins the other Explicit rows' forms.
    [Theory]
    [InlineData("P2", "cf-f2-tu", CallableBoth + "    return action(true, undefinedName)\n", "NoApplicableOverload_Kd: return action(true, undefinedName) | UnresolvedBinding_Kd: return action(true, undefinedName)", Explicit = true)]
    [InlineData("P2", "cf-f2-ut", CallableBoth + "    return action(undefinedName, true)\n", "NoApplicableOverload_Kd: return action(undefinedName, true) | UnresolvedBinding_Kd: return action(undefinedName, true)", Explicit = true)]
    [InlineData("guard", "cf-m-tie", MethodTie + "    h.m(true, 1)\n", "AmbiguousBinding_Kd: h.m(true, 1)")]
    [InlineData("P2", "cf-m-tie-u", MethodTie + "    h.m(undefinedName, 1)\n", "AmbiguousBinding_Kd: h.m(undefinedName, 1) | UnresolvedBinding_Kd: h.m(undefinedName, 1)", Explicit = true)]
    [InlineData("guard", "cf-m-mis", MethodOne + "    h.m(true, 1)\n", "NoApplicableOverload_Kd: h.m(true, 1)")]
    [InlineData("P3", "cf-m-mis-u", MethodOne + "    h.m(true, undefinedName)\n", "NoApplicableOverload_Kd: h.m(true, undefinedName) | UnresolvedBinding_Kd: h.m(true, undefinedName)", Explicit = true)]
    [InlineData("guard", "cf-c-tie", InitTie + "    let p = P.init(true, 1)\n", "AmbiguousBinding_Kd: let p = P.init(true, 1)")]
    [InlineData("P2", "cf-c-tie-u", InitTie + "    let p = P.init(undefinedName, 1)\n", "AmbiguousBinding_Kd: let p = P.init(undefinedName, 1) | UnresolvedBinding_Kd: let p = P.init(undefinedName, 1)", Explicit = true)]
    [InlineData("guard", "cf-c-mis", InitOne + "    let p = P.init(true, 1)\n", "NoApplicableOverload_Kd: let p = P.init(true, 1)")]
    [InlineData("P3", "cf-c-mis-u", InitOne + "    let p = P.init(true, undefinedName)\n", "NoApplicableOverload_Kd: let p = P.init(true, undefinedName) | UnresolvedBinding_Kd: let p = P.init(true, undefinedName)", Explicit = true)]
    [InlineData("guard", "cf-v-mis", ValueTwo + "    g(true, 1)\n", "NoApplicableOverload_Kd: g(true, 1)")]
    [InlineData("guard", "cf-v-mis-u", ValueTwo + "    g(true, undefinedName)\n", "NoApplicableOverload_Kd: g(true, undefinedName) | UnresolvedBinding_Kd: g(true, undefinedName)")]
    [InlineData("guard", "cf-f-tie", CallableTie + "    return action(true, 1)\n", "AmbiguousBinding_Kd: return action(true, 1)")]
    [InlineData("P2", "cf-f-tie-u", CallableTie + "    return action(undefinedName, 1)\n", "AmbiguousBinding_Kd: return action(undefinedName, 1) | UnresolvedBinding_Kd: return action(undefinedName, 1)", Explicit = true)]
    [InlineData("guard", "cf-f-mis", CallableOne + "    return action(true, 1)\n", "NoApplicableOverload_Kd: return action(true, 1)")]
    [InlineData("guard", "cf-f-mis-u", CallableOne + "    return action(true, undefinedName)\n", "NoApplicableOverload_Kd: return action(true, undefinedName) | UnresolvedBinding_Kd: return action(true, undefinedName)")]
    public void EachCallFormTiesAndKeepsIndependentRecordsByOneRule(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // R7-P1's call classes, the synthesized forms (R7-P0's census: indexer and requirement/operator ride BindCall): a literal that fits
    // two conformances alike is ambiguous, and a key or counterpart that fits none is an error. SPEC 4.7.1 step 1 ("Exactly one
    // conformance must fit: none is a Type error and several are an ambiguity error"; its example `pair[1]`), 13.3.4 ("More than one
    // candidate is ambiguous: candidates are never ranked"), 12.3.3; each tie's typed twin is valid. ix-mis (PLAN G1252): with two
    // conformances dev reports `pair[true]`, which no key fits, as AmbiguousBinding_Kd; it contradicts the `Option<u8>` key row of
    // ContractBindingTest.ABoundReferenceIsTheTypeItsProjectionArgumentNames (AmbiguousBinding_Kd), which G1252's unit moves with it.
    // SPEC names no code: NoApplicableOverload_Kd (ix-mis1's) is G1252's unit's proposal, and dev gives 4.7.1's own `names[id]`
    // TypeMismatch_Kd at the key, so that unit chooses one record for every index that no conformance fits.
    [Theory]
    [InlineData("guard", "ix-tie", IndexPair + "    return pair[1]\n", "AmbiguousBinding_Kd: return pair[1]")]
    [InlineData("guard", "ix-typed", IndexPair + "    return pair[1@i64]\n", "")]
    [InlineData("G1252", "ix-mis", IndexPair + "    return pair[true]\n", "NoApplicableOverload_Kd: return pair[true]", Explicit = true)]
    [InlineData("guard", "ix-mis1", IndexOne + "    return one[true]\n", "NoApplicableOverload_Kd: return one[true]")]
    [InlineData("guard", "op-tie", AddPair + "    return v + 1\n", "ArithmeticSelection_Kd: return v + 1")]
    [InlineData("guard", "op-typed", AddPair + "    return v + 1@i64\n", "")]
    [InlineData("guard", "op-mis", AddPair + "    return v + true\n", "ArithmeticSelection_Kd: return v + true")]
    public void EachSynthesizedCallFormTiesAndMisfitsByOneRule(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // R7-P1's R-b at the other synthesized forms, each over a fixed callee whose argument or conformance fails: a view's key (4.7.1
    // step 1: on a UniqSlice the key is a position typed by 4.6.2), a substitution (12.4.2: it needs Utf8Format) and iteration
    // (14.6.2: a missing conformance is an error; 4.6.2's `0@i32..2@i64` is not iterable). Object creation's misfit (13.6.2's
    // `1@obj/i64`) is ObjectCreationAdaptationTest.InvalidOperationsKeepTheirSpecificCause's row, a range boundary's (`true..false`)
    // and a comparison's (`(0..3) == (0..=2)`) IntegerRangeTest.RejectsWhatTheRangeRulesDoNotAdmit's. Object creation has no tie: `@`
    // cannot be overloaded and never reselects (13.5, 13.5.2).
    [Theory]
    [InlineData("guard", "vw-mis", "func k() -> ()\n    var values: [3 of i32] = [1, 2, 3]\n    var view = values.sliceUniq()\n    let x = view[true]\n", "TypeMismatch_Kd: let x = view[true]")]
    [InlineData("guard", "vw-ok", "func k() -> ()\n    var values: [3 of i32] = [1, 2, 3]\n    var view = values.sliceUniq()\n    let x = view[0]\n", "")]
    [InlineData("guard", "fmt-mis", "struct S\n    public init() => ()\nfunc k() -> ()\n    let s = S.init()\n    let t = \"\\(s)\"\n", "NoApplicableOverload_Kd: let t = \"\\(s)\"")]
    [InlineData("guard", "fmt-ok", "func k() -> ()\n    let n: i32 = 1\n    let t = \"\\(n)\"\n", "")]
    [InlineData("guard", "it-mis", "func k() -> ()\n    for i in 0@i32..2@i64 => ()\n", "UnsatisfiedConstraint_Kd: for i in 0@i32..2@i64 => ()")]
    [InlineData("guard", "it-ok", "func k() -> ()\n    for i in 0..2 => ()\n", "")]
    public void EachFixedSynthesizedCallReportsItsMisfit(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // R7-P1's R-c: argument evidence binds a slot first and the expected result fills only unbound parts (SPEC 10.8, 10.1 step 4,
    // 10.3), at a generic function (rc-d-*) and a generic constructor (rc-c-*, 10.8.1 with its `takeBox(Box.init(1))`); a literal-only
    // argument is checked against the Type the expected result gives (10.8's `identity([2 of 1])`), and a slot nothing binds is an
    // error (10.8's `identity(stop())`). An enum Case takes the other order by its own rule (6.5.2 step 2: the expected Type's
    // arguments first, then the payloads adapt; its examples `Option.Some(1)` at `Option<i64>` and `Option.None`): rc-e-* state it.
    // The open slots' codes are dev's (UnboundTypeArgument_Kd at a function, UnresolvedBinding_Kd at a Case); P3's merge of the enum
    // Case path may move rc-e-open's code (a Q13 record move).
    [Theory]
    [InlineData("guard", "rc-d-arg", Slots + "    let a: Option<i64> = mk(n)\n", "NoApplicableOverload_Kd: let a: Option<i64> = mk(n)")]
    [InlineData("guard", "rc-d-lit", Slots + "    let a: Option<i64> = mk(1)\n", "")]
    [InlineData("guard", "rc-d-fill", Slots + "    let a: Option<i64> = none()\n", "")]
    [InlineData("guard", "rc-d-open", Slots + "    let a = none()\n", "UnboundTypeArgument_Kd: let a = none()")]
    [InlineData("guard", "rc-c-arg", Slots + "    let b: Bin<i64> = Bin.init(n)\n", "NoApplicableOverload_Kd: let b: Bin<i64> = Bin.init(n)")]
    [InlineData("guard", "rc-c-lit", Slots + "    let b: Bin<i64> = Bin.init(1)\n", "")]
    [InlineData("guard", "rc-e-arg", Slots + "    let o: Option<i64> = Option.Some(n)\n", "TypeMismatch_Kd: let o: Option<i64> = Option.Some(n)")]
    [InlineData("guard", "rc-e-lit", Slots + "    let o: Option<i64> = Option.Some(1)\n", "")]
    [InlineData("guard", "rc-e-open", Slots + "    let o = Option.None\n", "UnresolvedBinding_Kd: let o = Option.None")]
    public void ArgumentsBindSlotsBeforeTheExpectedResultFillsTheRest(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // SPEC 10.8 ("Unbound slots": Constraints needing the slot are not judged; traversal order never chooses), 10.1 step 5, 8.10 (PLAN
    // G354's remainder, DECISIONS BU-86): a judgment on the unbound U neither hides F's refutation nor depends on the argument order.
    // With the overload the call is valid (p24, p23); alone the generic candidate is inapplicable (p24n, p23n). The four contradict
    // CallableSignatureInferenceTest.TheEnvironmentJudgmentIsChosenBySlotOrder's rows (UnprovenConstraint_Kd, "no evidence for U"):
    // p24 and p23 its rows 3-4, p24n and p23n its rows 1-2, which P3 moves with them. G354's one-slot form (`make(c@ref, n, 2)`) is
    // that class's AnEnvironmentResultDecidesOnlyAnUnsolvedSlot row 1.
    [Theory]
    [InlineData("P3", "p24", MakeFirst + MakeBody + "    return make(c@ref, e@ref, n, 2)\n", "", Explicit = true)]
    [InlineData("P3", "p23", MakeSecond + MakeBody + "    return make(e@ref, c@ref, n, 2)\n", "", Explicit = true)]
    [InlineData("P3", "p24n", MakeFirstOne + MakeBody + "    return make(c@ref, e@ref, n, 2)\n", "NoApplicableOverload_Kd: return make(c@ref, e@ref, n, 2)", Explicit = true)]
    [InlineData("P3", "p23n", MakeSecondOne + MakeBody + "    return make(e@ref, c@ref, n, 2)\n", "NoApplicableOverload_Kd: return make(e@ref, c@ref, n, 2)", Explicit = true)]
    public void AJudgmentOnAnUnboundSlotHidesNoRefutation(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // SPEC 10.5.4, 23.1.2 (PLAN R1e, F's ArgFlow ruling 90): an anonymous function's own failure, which reads no selection, is reported
    // beside its unselected call. r1e-mut is the mutation `overload-parameter-type` (tests/diagnostics/mutations.json) in small and
    // contradicts that mutation's expectation (NoApplicableOverload_Kd alone), which R1e moves with it; t-vc1m is the value call's
    // arity form, which no existing row pins. Their twins: the selected call keeps the body's record, the valid body leaves the call's alone.
    [Theory]
    [InlineData("R1e", "r1e-mut", Find + "    return find(items, func [target] (value: bool) => value == target)\n", "NoApplicableOverload_Kd: return find(items, func [target] (value: bool) => value == target) | TypeMismatch_Kd: return find(items, func [target] (value: bool) => value == target)", Explicit = true)]
    [InlineData("guard", "r1e-mutsel", FindBool + "    return find(items, func [target] (value: bool) => value == target)\n", "TypeMismatch_Kd: return find(items, func [target] (value: bool) => value == target)")]
    [InlineData("guard", "r1e-mutok", Find + "    return find(items, func [target] (value: i32) => value == target)\n", "")]
    [InlineData("R1e", "t-vc1m", ValueCallee + "    return h(func (x) => missing, 1, 2)\n", "NoApplicableOverload_Kd: return h(func (x) => missing, 1, 2) | UnresolvedBinding_Kd: return h(func (x) => missing, 1, 2)", Explicit = true)]
    [InlineData("guard", "t-vc1", ValueCallee + "    return h(func (x) => x, 1, 2)\n", "NoApplicableOverload_Kd: return h(func (x) => x, 1, 2)")]
    public void AnAnonymousFunctionsOwnFailureIsReportedBesideItsCall(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);
}
