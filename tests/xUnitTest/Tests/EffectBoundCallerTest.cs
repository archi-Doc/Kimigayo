// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 8.4.10.4, 8.4.10.6: a generic requirement call may affect every Loan that its abstract inputs' Types may denote; a live
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
        c.Ownership.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        if (at is not null)
        {
            Assert.Equal(nameof(DiagnosticCode.CallEffectConflict_Kd), error.Code);
            Assert.Equal(at, error.Text);
            Assert.Equal("take may affect a Loan that first keeps".Replace("first", declarations.Contains("pair", StringComparison.Ordinal) ? "pair" : "first", StringComparison.Ordinal), error.Label);
            Assert.Contains("no bound excludes it", error.Note);
            Assert.Contains("preserves results", error.Advice);
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
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 4.7.5, 8.4.10.4: only a Kimi operation's published summary replaces the unbounded model; a user generic function keeps it,
    // even when its body calls such an operation, so an earlier user generic result still conflicts.
    [Theory]
    [InlineData("func keep<V>(v: V) -> Option<V>\n    V is Copy\n    return .Some(v)\nfunc touch<V>(v: V) -> isize\n    V is Copy\n    return 1\nfunc put<K>(k: K) -> isize\n    K is Copy\n    let r = keep(k)\n    let n = touch(k)\n    match r\n        .Some(_) => return n\n        .None => return 0\n", "touch(k)")]
    [InlineData("func fill<V>(d: uniq/Dictionary<i32, V>, v: V) -> Result<(), (i32, V)>\n    V is Copy\n    return d.tryInsert(1, v)\nfunc put<V>(v: V) -> isize\n    V is Copy\n    var d: Dictionary<i32, V> = [:]\n    let r = fill(d@uniq, v)\n    var e: Dictionary<i32, V> = [:]\n    let s = fill(e@uniq, v)\n    match r\n        .Ok(()) => return 1\n        .Err(_) => return 0\n", "fill(e@uniq, v)")]
    public void UserGenericCallsKeepTheUnboundedModel(string declarations, string at)
    {
        var c = MinimalEmissionTest.Analyze(declarations + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        c.Ownership.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal((nameof(DiagnosticCode.CallEffectConflict_Kd), at), (error.Code, error.Text));
    }

    // A lending item keeps the receiver borrow itself; the existing activation check reports it once.
    [Fact]
    public void ALendingItemKeepsItsActivationDiagnostic()
    {
        var c = MinimalEmissionTest.Analyze("func twice<I>(source: uniq/I) -> ()\n    I is LendingIterator\n    let first = source.next()\n    let second = source.next()\n    _ = first@move\n    _ = second@move\n" + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        c.Ownership.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.CallActivationConflict_Kd), error.Code);
    }

    // SPEC 8.4.10.6: the related locations are the Place keeping the Loan and the call that created it.
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
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        for (var i = 0; i < 4; i++)
        {
            c.Ownership.Analyze();
        }

        var verified = true;
        var allocated = AllocationMeasurement.Measure(() => verified &= c.Ownership.Analyze().IsVerified);
        Assert.True(verified);
        Assert.Equal(0, allocated);
    }
}
