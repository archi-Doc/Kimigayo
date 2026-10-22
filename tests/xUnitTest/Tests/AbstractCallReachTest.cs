// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
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
}
