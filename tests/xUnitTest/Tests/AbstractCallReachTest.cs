// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 8.11.3, 15.6.4 (the adopted proposal Abstract Calls Reach by Paths): a call whose callee is abstract reaches what a concrete
// call reaches, the Places reachable by path from its inputs in their modes, and its result keeps the Loans of 15.6.4; no Origin of
// an input lets it reach another Loan, and nothing relates two calls.
public class AbstractCallReachTest
{
    private const string Source = "contract Source\n    associate Item\n    func take(self: uniq/Self) -> Option<Self.Item>\n";

    private const string Main = "public func main() => ()\n";

    private const string Counter = "struct Counter\n    Self is Iterator\n    associate Iterator.Item is i32\n    var value: i32\n    var limit: i32\n" +
        "    public init(value: i32, limit: i32)\n        self.value = value\n        self.limit = limit\n" +
        "    public func next(self: uniq/Self) -> Option<i32>\n        if self.value == self.limit => return .None\n        self.value += 1\n        return .Some(self.value)\n";

    private const string Native = "contract Take\n    associate Item\n    func take(self: uniq/Self) -> Option<Self.Item>\n" + Counter +
        "struct Merge<J>\n    J is Iterator\n    Self is Take\n    associate Take.Item is J.Item\n    var left: J\n    var right: J\n" +
        "    public init(left: J, right: J)\n        self.left = left@move\n        self.right = right@move\n" +
        "    public func take(self: uniq/Self) -> Option<J.Item>\n        match self.left.next()\n            .Some(let item) => return .Some(item@move)\n            .None => return self.right.next()\n" +
        "func takeTwo<S>(source: uniq/S) -> (Option<S.Item>, Option<S.Item>)\n    S is Take\n    let first = source.take()\n    let second = source.take()\n    return (first@move, second@move)\n" +
        "func zip<I>(left: uniq/I, right: uniq/I) -> (Option<I.Item>, Option<I.Item>)\n    I is Iterator\n    let a = right.next()\n    let b = left.next()\n    return (a@move, b@move)\n" +
        "func value(item: Option<i32>) -> i32\n    match item\n        .Some(let n) => return n\n        .None => return 0\n" +
        "var merge = Merge<Counter>.init(Counter.init(0, 1), Counter.init(10, 12))\nlet taken = takeTwo(merge@uniq)\n" +
        "require value(taken.0) == 1 and value(taken.1) == 11 else => $abort(\"takeTwo\")\n" +
        "var left = Counter.init(0, 2)\nvar right = Counter.init(20, 22)\nlet zipped = zip(left@uniq, right@uniq)\n" +
        "require value(zipped.0) == 21 and value(zipped.1) == 1 else => $abort(\"zip\")\nConsole.writeLine(\"reach by paths\")\n";

    // An Opener's items keep a Loan of the values it starts from; Concrete is its conformance over ArrayUniqIterator.
    private const string Opener = "contract Opener\n    associate Iter(o) is Iterator for uniq/Array<i32> during o\n" +
        "    func start(values: uniq/Array<i32> during o) -> Self.Iter(o)\nstruct Concrete\n    Self is Opener\n" +
        "    associate Opener.Iter(o) is ArrayUniqIterator<i32> during o\n" +
        "    public func start(values: uniq/Array<i32> during o) -> ArrayUniqIterator<i32> during o => values.iterateUniq()\n" +
        "func inspect<T>(value: ref/T) => ()\n";

    // The generic programs of the rows below, after Source.
    private const string TakeTwo = "func takeTwo<S>(source: uniq/S) -> (Option<S.Item>, Option<S.Item>)\n    S is Source\n    let first = source.take()\n    let second = source.take()\n    return (first@move, second@move)\n";

    private const string Tupled = "func tupled<S>(source: uniq/S) -> i32\n    S is Source\n    let pair = (source.take(), 1)\n    let second = source.take()\n    _ = pair@move\n    _ = second@move\n    return 0\n";

    private const string Through = "func through<S>(source: uniq/S) -> (Option<S.Item>, Option<S.Item>)\n    S is Source\n    let first = source.take()\n    let second = Helpers.again(source)\n    return (first@move, second@move)\ngroup Helpers\n    public func again<T>(value: uniq/T) -> Option<T.Item>\n        T is Source\n        return value.take()\n";

    private const string NextPair = "func nextPair<I>(source: uniq/I) -> (Option<I.Item>, Option<I.Item>)\n    I is Iterator\n    let first = source.next()\n    let second = source.next()\n    return (first@move, second@move)\n";

    private const string SumTwo = "contract View\n    func read(self: ref/Self) -> ref/i32\n        effect confined\nfunc sumTwo<V>(view: ref/V) -> i32\n    V is View\n    let first = view.read()\n    let second = view.read()\n    return first@follow + second@follow\n";

    private const string Pairs = "func pairs<C>(values: ref/C) -> i32\n    C is Iterable\n    var count: i32 = 0\n    for a in values\n        for b in values\n            count += 1\n    return count\n";

    private const string TakeLate = "func takeLate<S>(source: uniq/S) -> Option<S.Item>\n    S is Source\n    let first = source.take()\n    _ = first@move\n    let second = source.take()\n    return second@move\n";

    private const string Zip = "func zip<I>(left: uniq/I, right: uniq/I) -> (Option<I.Item>, Option<I.Item>)\n    I is Iterator\n    let a = left.next()\n    let b = right.next()\n    return (a@move, b@move)\n";

    private const string Both = "func both<S>(left: uniq/S, right: uniq/S) -> (Option<S.Item>, Option<S.Item>)\n    S is Source\n    let a = left.take()\n    let b = right.take()\n    return (a@move, b@move)\n";

    private const string KeepTouch = "func keep<V>(v: V) -> Option<V>\n    V is Copy\n    return .Some(v)\nfunc touch<V>(v: V) -> isize\n    V is Copy\n    return 1\nfunc put<K>(k: K) -> isize\n    K is Copy\n    let r = keep(k)\n    let n = touch(k)\n    match r\n        .Some(_) => return n\n        .None => return 0\n";

    private const string Fill = "func fill<V>(d: uniq/Dictionary<i32, V>, v: V) -> Result<(), (i32, V)>\n    V is Copy\n    return d.tryInsert(1, v)\nfunc put<V>(v: V) -> isize\n    V is Copy\n    var d: Dictionary<i32, V> = [:]\n    let r = fill(d@uniq, v)\n    var e: Dictionary<i32, V> = [:]\n    let s = fill(e@uniq, v)\n    match r\n        .Ok(()) => return 1\n        .Err(_) => return 0\n";

    private const string Merge = "struct Merge<J>\n    J is Iterator\n    Self is Source\n    associate Source.Item is J.Item\n    var left: J\n    var right: J\n    public func take(self: uniq/Self) -> Option<J.Item>\n        match self.left.next()\n            .Some(let item) => return .Some(item@move)\n            .None => return self.right.next()\n";

    private const string Swapped = "struct Swapped<J>\n    J is Iterator\n    Self is Source\n    associate Source.Item is J.Item\n    var inner: J\n    var spare: J\n    public func take(self: uniq/Self) -> Option<J.Item>\n        Kimi.Intrinsics.swap(self.inner@uniq, self.spare@uniq)\n        return self.inner.next()\n";

    private const string Lending = "struct Lending<J> {a}\n    J is LendingIterator\n    Self is Source\n    associate Source.Item is ref/i32 during a\n    var inner: J\n    let value: ref/i32 during a\n    public func take(self: uniq/Self) -> Option<ref/i32 during a>\n        _ = self.inner.next()\n        return .Some(self.value)\n";

    private const string Topped = "contract Refill\n    associate Item\n    func take(self: uniq/Self, spare: Option<Self.Item>) -> Option<Self.Item>\n" +
        "struct Topped<J>\n    J is Iterator\n    Self is Refill\n    associate Refill.Item is J.Item\n    var inner: J\n    public func take(self: uniq/Self, spare: Option<J.Item>) -> Option<J.Item>\n" +
        "        match self.inner.next()\n            .Some(let item) => return .Some(item@move)\n            .None => return spare@move\n";

    private const string CallablePair = "struct Pair<F,I>\n    I is Iterator\n    F is Callable<uniq, () -> Option<I.Item>>\n    Self is Source\n    associate Source.Item is I.Item\n    var first: F\n    var second: F\n" +
        "    public func take(self: uniq/Self) -> Option<I.Item>\n        if true => return self.first@uniq()\n        return self.second@uniq()\n";

    // The bodies of the guard rows, after an Opener's Iterator over a local Array.
    private const string GuardWrite = "    let item = it.next()\n    values[0] = 5\n    inspect(item@ref)\n";

    private const string GuardReborrow = "    var r = it@uniq\n    let item = r.next()\n    values[0] = 5\n    inspect(item@ref)\n";

    private const string GuardReassign = "    let item = it.next()\n    it = S.start(values@uniq)\n    inspect(item@ref)\n";

    private const string GuardAggregate = "    let item = it.next()\n    var box = (item@move, 0)\n    values[0] = 5\n    inspect(box@ref)\n";

    private const string LendingTwice = "func twice<I>(source: uniq/I) -> ()\n    I is LendingIterator\n    let first = source.next()\n    let second = source.next()\n    _ = first@move\n    _ = second@move\n";

    // The proposal's section 6 again: a first result through a Reborrow of the receiver, then a call on the receiver itself.
    private const string Again = "func again<I>(it: uniq/I) -> (Option<I.Item>, Option<I.Item>)\n    I is Iterator\n" +
        "    var r = it@follow@uniq\n    let first = r.next()\n    let second = it.next()\n    return (first@move, second@move)\n";

    // PLAN G217's gx9 body: a shared read of the source while an exclusive item lives.
    private const string SharedRead = "    let item = it.next()\n    inspect(values@ref)\n    inspect(item@ref)\n";

    // Instances whose implementations use most of their contracts: a Source whose items keep a uniq Loan of its source, a LendingIterator
    // whose item keeps the step Loan, and a View that lends its own Field.
    private const string Cells = "struct Cells {src}\n    Self is Source\n    associate Source.Item is uniq/i32 during src\n" +
        "    var rest: ArrayUniqIterator<i32> during src\n    public func take(self: uniq/Self) -> Option<uniq/i32 during src> => self.rest.next()\n";

    private const string Cursor = "struct Cursor {source}\n    Self is LendingIterator\n    associate LentItem(step) is ref/i32 during step\n" +
        "    let value: uniq/i32 during source\n    public func next(self: uniq/Self during step) -> Option<ref/i32 during step>\n" +
        "        return .Some(self.value)\n";

    private const string Cell = "struct Cell\n    Self is View\n    var value: i32 = 1\n    public func read(self: ref/Self) -> ref/i32 => self.value@ref\n";

    // A conformance to RequirementIdentityTest's Q<i32>, one take per key Type, and one to Q<uniq/i32 during src> whose results keep a
    // uniq Loan of its source.
    private const string Keyed = "struct Keyed\n    Self is Q<i32>\n    public func take(self: uniq/Self, key: i32) -> i32 => key\n" +
        "    public func take(self: uniq/Self, key: i64) -> i32 => 2\n";

    private const string KeyedCells = "struct KeyedCells {src}\n    Self is Q<uniq/i32 during src>\n    var rest: ArrayUniqIterator<i32> during src\n" +
        "    public func take(self: uniq/Self, key: i32) -> uniq/i32 during src\n        match self.rest.next()\n" +
        "            .Some(let item) => return item@move\n            .None => $abort(\"empty\")\n" +
        "    public func take(self: uniq/Self, key: i64) -> uniq/i32 during src\n        match self.rest.next()\n" +
        "            .Some(let item) => return item@move\n            .None => $abort(\"empty\")\n";

    // L1's generic cases: the section 7 generic programs and section 6's again, U1's guard rows with gx9, and the 2f-S1 and spec-readings
    // ids that exist as tests (c11 takeFirst, c13/h5 takeTwo, c14 tupled, c15/h1 through, c16 either, c17 and c18 the Callable rows, c19
    // zip and both, c20 pairs, c21 takeLate and sumTwo, c30 Merge, c31 and c32 the ordinary conformances, c33 Lending, c35 Swapped and
    // Topped), each with the Type parameter its instances replace and their names: the concrete Types its tests use and one whose
    // implementation uses most of the contract, whose items keep a Loan of their source, except under a Callable constraint, which cannot
    // name the instance's Origin (InvalidConstraint_Kd). Either fixes its R. Left out, adding no form: shared-item Iterators such as
    // ArrayIterator (p4's own, whose again instance shows again's pinned record) and zip with the right call first.
    private static readonly (string Name, string Parameter, string Text, string Instances)[] Generics =
    [
        ("takeTwo", "S", TakeTwo, "Merge Cells"),
        ("tupled", "S", Tupled, "Merge Cells"),
        ("through", "S", Through, "Merge Cells"),
        ("takeLate", "S", TakeLate, "Merge Cells"),
        ("both", "S", Both, "Merge Cells"),
        ("takeFirst", "S", DiagnosticCorpus.SpecExample("15.6.3", "func takeFirst<S>").Replace("S is Take", "S is Source", StringComparison.Ordinal),
            "Merge Cells"),
        ("either", "S", Instantiate(RequirementIdentityTest.EitherRequirement("i64"), "R", "i32", string.Empty), "Keyed"),
        ("eitherLent", "S", Instantiate(RequirementIdentityTest.EitherRequirement("i64"), "R", "uniq/i32 during q", string.Empty), "KeyedCells"),
        ("nextPair", "I", NextPair, "Counter UniqItems"),
        ("zip", "I", Zip, "Counter UniqItems"),
        ("again", "I", Again, "Counter UniqItems"),
        ("sumTwo", "V", SumTwo, "Cell"),
        ("pairs", "C", Pairs, "Array"),
        ("keep", "K", KeepTouch, "i32 Shared"),
        ("fill", "V", Fill, "i32 Shared"),
        ("lend", "I", LendingTwice, "Counter Cursor"),
        ("callable", "S", CallableEffectBoundTest.TwiceByReference, "Merge"),
        ("sameCallable", "S", CallableEffectBoundTest.Replacing(CallableEffectBoundTest.SameCallable), "Merge"),
        ("replacedCallable", "S", CallableEffectBoundTest.Replacing(CallableEffectBoundTest.ReplacedCallable), "Merge"),
        ("otherCallable", "S", CallableEffectBoundTest.Replacing(CallableEffectBoundTest.OtherCallable), "Merge"),
        ("guardWrite", "S", Opened(true, GuardWrite), "Concrete"),
        ("guardReborrow", "S", Opened(true, GuardReborrow), "Concrete"),
        ("guardReassign", "S", Opened(true, GuardReassign), "Concrete"),
        ("guardAggregate", "S", Opened(true, GuardAggregate), "Concrete"),
        ("gx9", "S", Opened(true, SharedRead), "Concrete"),
        ("Merge", "J", Merge, "Counter UniqItems"),
        ("Swapped", "J", Swapped, "Counter UniqItems"),
        ("Lending", "J", Lending, "Counter Cursor"),
        ("Topped", "J", Topped, "Counter UniqItems"),
        ("Pair", "I", CallablePair, "Counter"),
        ("Wrap", "T", IteratorOriginEffectsTest.OrdinaryWrap, "i32 Shared"),
        ("Hold", "I", IteratorOriginEffectsTest.OrdinaryHold, "Counter UniqItems"),
        ("Outer", "I", IteratorOriginEffectsTest.OrdinaryOuter, "Counter UniqItems"),
        ("Chain", "I", IteratorOriginEffectsTest.OrdinaryChain, "Counter UniqItems"),
        ("Peek", "I", IteratorOriginEffectsTest.OrdinaryPeek, "Counter UniqItems"),
    ];

    // L1's concrete Types by name: the Type, its Item and its declarations, which follow the case's text in both compilations; a Type
    // with Loans names its Origin q (Instantiate). Concrete is declared with Opener.
    private static readonly Dictionary<string, (string Type, string Item, string Declarations)> Instances = new()
    {
        ["Counter"] = ("Counter", "i32", Counter),
        ["UniqItems"] = ("(ArrayUniqIterator<i32> during q)", "uniq/i32 during q", string.Empty),
        ["Merge"] = ("Merge<Counter>", "i32", Counter + Merge),
        ["Cells"] = ("(Cells during q)", "uniq/i32 during q", Cells),
        ["Cursor"] = ("(Cursor during q)", string.Empty, Cursor),
        ["Concrete"] = ("Concrete", string.Empty, string.Empty),
        ["Cell"] = ("Cell", string.Empty, Cell),
        ["Array"] = ("Array<i32>", string.Empty, string.Empty),
        ["i32"] = ("i32", string.Empty, string.Empty),
        ["Shared"] = ("ref/i32 during q", string.Empty, string.Empty),
        ["Keyed"] = ("Keyed", string.Empty, RequirementIdentityTest.KeyedTake + Keyed),
        ["KeyedCells"] = ("(KeyedCells during q)", string.Empty, RequirementIdentityTest.KeyedTake + KeyedCells),
    };

    // The default pipeline's two listed L1 exceptions (PLAN G217), each pinned as the one record its generic does not cover, so the row
    // fails when the old path changes; the MIR has none. gx9 (G217's N9 fact): the generic body is accepted while its concrete twin's
    // shared read of the source meets the exclusive item (H: AddType gives a projection's Origin argument the schema's shared mode).
    // again runs the opposite way: the old path rejects the concrete instance, which SPEC 15.6.3 and 15.6.4 and the MIR accept as they
    // accept the generic (its over-strictness on concrete code, G217's p4), so no weaker abstract model and no counterexample that the
    // proposal's 4.3 routes to a restriction of the base rule; its pin fails when p4 is fixed or the old path goes at R6-check.
    private static readonly Dictionary<string, string> OldPathGaps = new()
    {
        ["gx9 Concrete"] = "ComparisonLoanConflict_Kd at inspect(values@ref)",
        ["again UniqItems"] = "CallActivationConflict_Kd at let second = it.next()",
    };

    /// <summary>Gets L1's pairs: each generic case with each of its instances.</summary>
    public static TheoryData<string, string> Instantiations => [.. Generics.SelectMany(static x => x.Instances.Split(' ').Select(i => (x.Name, i)))];

    // A later call that reaches no Place a kept result's Loans name is valid, whatever earlier call on the same value, another value of
    // the same Type or a user generic function produced the result (Take-only takeTwo, tupled, through, zip, both and the concrete
    // callees of user generic functions were errors under rule 1's former Origin reach, which the adoption deleted).
    [Theory]
    [InlineData(TakeTwo)]
    [InlineData(Tupled)]
    [InlineData(Through)]
    [InlineData(NextPair)]
    [InlineData(SumTwo)]
    [InlineData(Pairs)]
    [InlineData(TakeLate)]
    [InlineData(Zip)]
    [InlineData(Both)]
    [InlineData(KeepTouch)]
    [InlineData(Fill)]
    public void AcceptsLaterCallsThatReachNoKeptLoan(string declarations)
    {
        var c = MinimalEmissionTest.Analyze(Source + declarations + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    // An implementation body is an ordinary body: its abstract calls on stored values, of one requirement or of a Callable, reach their
    // Places by path, whichever value, earlier result or argument produced what it returns (the adopted proposal's Merge, a swap of the
    // stepped value, a lending inner iterator, an argument returned as the result and two Callable values were preserves results violations
    // before the adoption; their programs now declare no bound).
    [Theory]
    [InlineData(Merge)]
    [InlineData(Swapped)]
    [InlineData(Lending)]
    [InlineData(Topped)]
    [InlineData(CallablePair)]
    public void AnImplementationBodyReachesByPaths(string declarations)
    {
        var c = MinimalEmissionTest.Analyze(Source + declarations + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    // The old Emission generates the abstract calls the adoption accepts (the proposal's section 6 forms): a Take-only takeTwo over a
    // Merge of two iterators, and zip, each keeping its first result across the second call.
    [Fact]
    public void AcceptedAbstractCallsRunNatively()
        => ScalarEmissionTest.EmitFixture("AbstractCallsReachByPaths", Native, "reach by paths\n");

    // SPEC 15.6.4, 15.6.3, 8.11.3 rule 1 by path (the adopted proposal Abstract Calls Reach by Paths, U1's guard rows): an abstract
    // call's result keeps the Loans its Type's Origins name, since a projection may retain any Origin of its base and an unproven
    // absence of retention never permits access; so a generic body that builds an Iterator from a local Borrow cannot write, borrow
    // or re-borrow the source while an item lives, directly, through a Reborrow, by re-assigning the Iterator or through an
    // aggregate, as the same body over a concrete conformance cannot.
    [Theory]
    [InlineData(true, GuardWrite, "values[0] = 5")]
    [InlineData(false, GuardWrite, "values[0] = 5")]
    [InlineData(true, GuardReborrow, "values[0] = 5")]
    [InlineData(false, GuardReborrow, "values[0] = 5")]
    [InlineData(true, GuardReassign, "it = S.start(values@uniq)")]
    [InlineData(false, GuardReassign, "it = S.start(values@uniq)")]
    [InlineData(true, GuardAggregate, "values[0] = 5")]
    [InlineData(false, GuardAggregate, "values[0] = 5")]
    public void AbstractResultsKeepTheLoansTheirOriginsName(bool generic, string body, string at)
    {
        var write = generic ? at : at.Replace("S.start", "Concrete.start", StringComparison.Ordinal);
        var source = Opener + Opened(generic, body) + Main;
        var start = source.LastIndexOf(write, StringComparison.Ordinal);
        Assert.True(start >= 0, write);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        c.ReportAnalysis();
        var errors = TestDiagnostics.Of(c);
        Assert.Contains(errors, x => x.Code is nameof(DiagnosticCode.ComparisonLoanConflict_Kd) or nameof(DiagnosticCode.CallActivationConflict_Kd) &&
            x.Span.Start >= start && x.Span.End <= start + write.Length);
    }

    // A lending item keeps the receiver borrow itself; the existing activation check reports it once.
    [Fact]
    public void ALendingItemKeepsItsActivationDiagnostic()
    {
        var c = MinimalEmissionTest.Analyze(LendingTwice + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        c.ReportAnalysis();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.CallActivationConflict_Kd), error.Code);
    }

    // SPEC 8.15.1, 15.3.7 (L1 of the adopted proposal Abstract Calls Reach by Paths, its 4.1): a generic body is checked at least as
    // strictly as each concrete instantiation, so every record an instance reports, its generic reports in the same declaration at the
    // same or an earlier line (and an accepted generic has accepted instances): on the MIR for every pair, on the default pipeline but
    // for its listed gaps.
    [Theory]
    [MemberData(nameof(Instantiations))]
    public void AGenericBodyIsAtLeastAsStrictAsItsInstances(string name, string instance)
    {
        var (_, parameter, text, _) = Generics.Single(x => x.Name == name);
        var (type, item, declarations) = Instances[instance];
        var generic = Records(Source + Opener + text + "\n" + declarations + Main);
        var concrete = Records(Source + Opener + Instantiate(text, parameter, type, item) + "\n" + declarations + Main);
        Assert.Empty(Uncovered(generic.Mir, concrete.Mir));
        string[] gaps = OldPathGaps.TryGetValue(name + " " + instance, out var gap) ? [gap] : [];
        Assert.Equal(gaps, Uncovered(generic.Old, concrete.Old));
    }

    // Warm ownership analysis of a body with requirement calls reuses its tables.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmRequirementCallAnalysisDoesNotAllocate()
    {
        var c = MinimalEmissionTest.Analyze(Source + TakeTwo + Main);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
        var verified = true;
        var allocated = AllocationMeasurement.Measure(() => verified &= c.Analyze().IsVerified);
        Assert.True(verified);
        Assert.Equal(0, allocated);
    }

    // The guard rows' function over an Opener's Iterator of a local Array: generic over S is Opener, or over Concrete.
    private static string Opened(bool generic, string body)
    {
        var head = generic ? "func f<S>() -> ()\n    S is Opener\n" : "func f() -> ()\n";
        return (head + "    var values: Array<i32> = [1, 2]\n    var it = S.start(values@uniq)\n" + body).Replace("S.start", generic ? "S.start" : "Concrete.start", StringComparison.Ordinal);
    }

    // An instance of a generic program, every line kept: the parameter's constraint lines become blank, the declarations it parameterizes
    // lose it, its projections P.Item and P.(C).Item become the Type's Item, and P becomes the Type. The Type's Origin q becomes the first
    // own Origin of a struct that loses P, or such a struct declares q; a function's q is implicit.
    private static string Instantiate(string text, string parameter, string type, string item)
    {
        var p = Regex.Escape(parameter);
        if (Regex.Match(text, $@"(?m)^struct \w+<[^>\n]*\b{p}\b[^>\n]*> \{{(\w+)") is { Success: true } own)
        {
            (type, item) = (Regex.Replace(type, @"\bq\b", own.Groups[1].Value), Regex.Replace(item, @"\bq\b", own.Groups[1].Value));
        }

        var origin = type.Contains("during q", StringComparison.Ordinal) ? "q" : string.Empty;
        text = Regex.Replace(text, $@"(?m)^ +{p} is .*$", string.Empty);
        text = Regex.Replace(text, @"(?m)^( *(?:public )?(func|struct) \w+)<([^>\n]*)>(?: \{([^}\n]*)\})?", m =>
        {
            var parameters = m.Groups[3].Value.Split(',', StringSplitOptions.TrimEntries);
            if (!parameters.Contains(parameter))
            {
                return m.Value;
            }

            var kept = string.Join(", ", parameters.Where(x => x != parameter));
            var origins = string.Join(", ", new[] { m.Groups[4].Value, m.Groups[2].Value == "struct" ? origin : string.Empty }.Where(static x => x.Length > 0));
            return m.Groups[1].Value + (kept.Length > 0 ? $"<{kept}>" : string.Empty) + (origins.Length > 0 ? $" {{{origins}}}" : string.Empty);
        });
        text = Regex.Replace(text, $@"\b{p}\.(?:\(\w+\)\.)?Item\b", item);
        return Regex.Replace(text, $@"\b{p}\b", type);
    }

    // A program's Error records in its source as their declaration's line, their line and their code at the line's text, independent of
    // KIMI_PIPELINE: on the MIR, its construction records and every analysis's problems, with no validator finding; on the default
    // pipeline, the old path's records.
    private static ((int Owner, int Line, string Text)[] Mir, (int Owner, int Line, string Text)[] Old) Records(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var old = LegacyRecords.Of(c).Where(static x => x.Node.CodeContext?.SourceDocument?.Path == "Hello.kimi").Select(static x => (x.Code, x.Node));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var mir = program.Records.Concat(program.Problems.All.ToArray().Select(static x => new Problem(x.Code, x.Subject)))
            .Where(static x => x.Subject.Module == 0).Select(x => (x.Code, c.SyntaxOf(x.Subject)));
        var lines = source.Split('\n');
        return ([.. Sites(mir)], [.. Sites(old)]);

        IEnumerable<(int, int, string)> Sites(IEnumerable<(DiagnosticCode Code, Koto Node)> records)
            => records.Where(static x => DiagnosticEntries.TryGet(x.Code, out var entry) && entry.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error)
                .Select(x => (Line(Function(x.Node)), Line(x.Node), $"{x.Code} at {lines[Line(x.Node)].Trim()}"));

        int Line(Koto? node) => node is null ? -1 : source.AsSpan(0, node.Span.Start).Count('\n');

        static Koto? Function(Koto? node)
        {
            while (node is not null and not FunctionKoto)
            {
                node = node.Parent;
            }

            return node;
        }
    }

    // An instance's records that no generic record of the same declaration reports at the same or an earlier line.
    private static string[] Uncovered((int Owner, int Line, string Text)[] generic, (int Owner, int Line, string Text)[] instance)
        => [.. instance.Where(x => !generic.Any(g => g.Owner == x.Owner && g.Line <= x.Line)).Select(static x => x.Text).Distinct()];
}
