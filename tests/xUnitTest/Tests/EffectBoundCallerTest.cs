// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 8.11.4, 8.11.1: a generic requirement call may affect every Loan that its abstract inputs' Types may denote; a live
// earlier result keeping such a Loan through the same value conflicts unless an available bound excludes it.
public class EffectBoundCallerTest
{
    private const string Source = "contract Source\n    associate Item\n    func take(self: uniq/Self) -> Option<Self.Item>\ncontract StableSource: Source\n    effect Source.take preserves results\n";

    private const string Main = "public func main() => ()\n";

    [Theory]
    [InlineData("func takeTwo<S>(source: uniq/S) -> (Option<S.Item>, Option<S.Item>)\n    S is Source\n    let first = source.take()\n    let second = source.take()\n    return (first@move, second@move)\n", "source.take()")]
    [InlineData("func tupled<S>(source: uniq/S) -> i32\n    S is Source\n    let pair = (source.take(), 1)\n    let second = source.take()\n    _ = pair@move\n    _ = second@move\n    return 0\n", "source.take()")]
    [InlineData("func through<S>(source: uniq/S) -> (Option<S.Item>, Option<S.Item>)\n    S is StableSource\n    let first = source.take()\n    let second = Helpers.again(source)\n    return (first@move, second@move)\ngroup Helpers\n    public func again<T>(value: uniq/T) -> Option<T.Item>\n        T is Source\n        return value.take()\n", null)]
    public void RejectsEffectsOnLoansOfEarlierResults(string declarations, string? at)
    {
        var c = MinimalEmissionTest.Analyze(Source + declarations + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        c.ReportAnalysis();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        if (at is not null)
        {
            Assert.Equal(nameof(DiagnosticCode.CallEffectConflict_Kd), error.Code);
            Assert.Equal(at, error.Text);
            Assert.Equal("take may affect a Loan that first keeps".Replace("first", declarations.Contains("pair", StringComparison.Ordinal) ? "pair" : "first", StringComparison.Ordinal), error.Label);
            Assert.Contains("no bound excludes it", error.Note);
        }
    }

    [Theory]
    [InlineData("func takeTwoStable<S>(source: uniq/S) -> (Option<S.Item>, Option<S.Item>)\n    S is StableSource\n    let first = source.take()\n    let second = source.take()\n    return (first@move, second@move)\n")]
    [InlineData("func nextPair<I>(source: uniq/I) -> (Option<I.Item>, Option<I.Item>)\n    I is Iterator\n    let first = source.next()\n    let second = source.next()\n    return (first@move, second@move)\n")]
    [InlineData("contract View\n    func read(self: ref/Self) -> ref/i32\n        effect confined\nfunc sumTwo<V>(view: ref/V) -> i32\n    V is View\n    let first = view.read()\n    let second = view.read()\n    return first@follow + second@follow\n")]
    [InlineData("func pairs<C>(values: ref/C) -> i32\n    C is Iterable\n    var count: i32 = 0\n    for a in values\n        for b in values\n            count += 1\n    return count\n")]
    [InlineData("func takeLate<S>(source: uniq/S) -> Option<S.Item>\n    S is Source\n    let first = source.take()\n    _ = first@move\n    let second = source.take()\n    return second@move\n")]
    [InlineData("func zip<I>(left: uniq/I, right: uniq/I) -> (Option<I.Item>, Option<I.Item>)\n    I is Iterator\n    let a = left.next()\n    let b = right.next()\n    return (a@move, b@move)\n")]
    [InlineData("func both<S>(left: uniq/S, right: uniq/S) -> (Option<S.Item>, Option<S.Item>)\n    S is Source\n    let a = left.take()\n    let b = right.take()\n    return (a@move, b@move)\n")]
    public void AcceptsCallsThatTheBoundsOrTheValuesSeparate(string declarations)
    {
        var c = MinimalEmissionTest.Analyze(Source + declarations + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 4.9.6, 8.11.4: only a Kimi operation's published summary replaces the unbounded model; a user generic function keeps it,
    // even when its body calls such an operation, so an earlier user generic result still conflicts.
    [Theory]
    [InlineData("func keep<V>(v: V) -> Option<V>\n    V is Copy\n    return .Some(v)\nfunc touch<V>(v: V) -> isize\n    V is Copy\n    return 1\nfunc put<K>(k: K) -> isize\n    K is Copy\n    let r = keep(k)\n    let n = touch(k)\n    match r\n        .Some(_) => return n\n        .None => return 0\n", "touch(k)")]
    [InlineData("func fill<V>(d: uniq/Dictionary<i32, V>, v: V) -> Result<(), (i32, V)>\n    V is Copy\n    return d.tryInsert(1, v)\nfunc put<V>(v: V) -> isize\n    V is Copy\n    var d: Dictionary<i32, V> = [:]\n    let r = fill(d@uniq, v)\n    var e: Dictionary<i32, V> = [:]\n    let s = fill(e@uniq, v)\n    match r\n        .Ok(()) => return 1\n        .Err(_) => return 0\n", "fill(e@uniq, v)")]
    public void UserGenericCallsKeepTheUnboundedModel(string declarations, string at)
    {
        var c = MinimalEmissionTest.Analyze(declarations + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        c.ReportAnalysis();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal((nameof(DiagnosticCode.CallEffectConflict_Kd), at), (error.Code, error.Text));
    }

    // SPEC 15.6.4, 15.6.3, 8.11.3 rule 1 by path (the adopted proposal Abstract Calls Reach by Paths, U1's guard rows): an abstract
    // call's result keeps the Loans its Type's Origins name, since a projection may retain any Origin of its base and an unproven
    // absence of retention never permits access; so a generic body that builds an Iterator from a local Borrow cannot write, borrow
    // or re-borrow the source while an item lives, directly, through a Reborrow, by re-assigning the Iterator or through an
    // aggregate, as the same body over a concrete conformance cannot.
    [Theory]
    [InlineData(true, "    let item = it.next()\n    values[0] = 5\n    inspect(item@ref)\n", "values[0] = 5")]
    [InlineData(false, "    let item = it.next()\n    values[0] = 5\n    inspect(item@ref)\n", "values[0] = 5")]
    [InlineData(true, "    var r = it@uniq\n    let item = r.next()\n    values[0] = 5\n    inspect(item@ref)\n", "values[0] = 5")]
    [InlineData(false, "    var r = it@uniq\n    let item = r.next()\n    values[0] = 5\n    inspect(item@ref)\n", "values[0] = 5")]
    [InlineData(true, "    let item = it.next()\n    it = S.start(values@uniq)\n    inspect(item@ref)\n", "it = S.start(values@uniq)")]
    [InlineData(false, "    let item = it.next()\n    it = S.start(values@uniq)\n    inspect(item@ref)\n", "it = S.start(values@uniq)")]
    [InlineData(true, "    let item = it.next()\n    var box = (item@move, 0)\n    values[0] = 5\n    inspect(box@ref)\n", "values[0] = 5")]
    [InlineData(false, "    let item = it.next()\n    var box = (item@move, 0)\n    values[0] = 5\n    inspect(box@ref)\n", "values[0] = 5")]
    public void AbstractResultsKeepTheLoansTheirOriginsName(bool generic, string body, string at)
    {
        const string opener = "contract Opener\n    associate Iter(o) is Iterator for uniq/Array<i32> during o\n" +
            "    func start(values: uniq/Array<i32> during o) -> Self.Iter(o)\nstruct Concrete\n    Self is Opener\n" +
            "    associate Opener.Iter(o) is ArrayUniqIterator<i32> during o\n" +
            "    public func start(values: uniq/Array<i32> during o) -> ArrayUniqIterator<i32> during o => values.iterateUniq()\n" +
            "func inspect<T>(value: ref/T) => ()\n";
        var head = generic ? "func f<S>() -> ()\n    S is Opener\n" : "func f() -> ()\n";
        var opened = (head + "    var values: Array<i32> = [1, 2]\n    var it = S.start(values@uniq)\n" + body).Replace("S.start", generic ? "S.start" : "Concrete.start", StringComparison.Ordinal);
        var write = generic ? at : at.Replace("S.start", "Concrete.start", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(opener + opened + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        c.ReportAnalysis();
        var errors = TestDiagnostics.Of(c);
        Assert.Contains(errors, x => x.Code is nameof(DiagnosticCode.ComparisonLoanConflict_Kd) or nameof(DiagnosticCode.CallActivationConflict_Kd) &&
            x.Text is { Length: > 0 } text && write.Contains(text, StringComparison.Ordinal));
    }

    // A lending item keeps the receiver borrow itself; the existing activation check reports it once.
    [Fact]
    public void ALendingItemKeepsItsActivationDiagnostic()
    {
        var c = MinimalEmissionTest.Analyze("func twice<I>(source: uniq/I) -> ()\n    I is LendingIterator\n    let first = source.next()\n    let second = source.next()\n    _ = first@move\n    _ = second@move\n" + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        c.ReportAnalysis();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.CallActivationConflict_Kd), error.Code);
    }

    // SPEC 8.11.1: the related locations are the Place keeping the Loan and the call that created it.
    [Fact]
    public void RelatesTheHeldResultAndItsCall()
    {
        var result = DiagnosticCorpus.Check(Source + "func takeTwo<S>(source: uniq/S) -> (Option<S.Item>, Option<S.Item>)\n    S is Source\n    let first = source.take()\n    let second = source.take()\n    return (first@move, second@move)\n" + Main);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.CallEffectConflict_Kd), error.Code);
        Assert.Equal(["call", "loan"], error.Related!.Select(static x => x.Role).ToArray());
    }

    // Warm ownership analysis of a body with requirement effects reuses its regions, holders and tables.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmRequirementEffectAnalysisDoesNotAllocate()
    {
        var c = MinimalEmissionTest.Analyze(Source + "func takeTwoStable<S>(source: uniq/S) -> (Option<S.Item>, Option<S.Item>)\n    S is StableSource\n    let first = source.take()\n    let second = source.take()\n    return (first@move, second@move)\n" + Main);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
        var verified = true;
        var allocated = AllocationMeasurement.Measure(() => verified &= c.Analyze().IsVerified);
        Assert.True(verified);
        Assert.Equal(0, allocated);
    }
}
