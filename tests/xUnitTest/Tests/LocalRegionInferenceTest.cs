// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class LocalRegionInferenceTest(ITestOutputHelper output)
{
    // A stored function's input makes `s` contravariant.
    private const string Callback = "struct C {s}\n    public let call: (ref/i32 during s) -> i32\n    public init(call: (ref/i32 during s) -> i32) => self.call = call@move\n\n";

    // A local's contravariant slot with two lower bounds proven in neither direction, the initializer's `a` and the clause's `b`, which the
    // call's argument `c` outlives.
    private const string NoPrincipal = Callback + "func f(a: ref/i32, b: ref/i32, c: ref/i32, g: (ref/i32 during a) -> i32) -> i32\n    origin c outlives a\n" +
        "    origin c outlives b\n    let h0 = C.init(g@move)\n    let h: C{x} = h0@move\n        origin x.s outlives b\n    return h.call(c)\npublic func main() => ()\n";

    // The principal candidate `c` of a contravariant slot comes from its second clause; `local@ref` does not outlive it.
    private const string Principal = Callback + "func f(a: ref/i32, b: ref/i32, c: ref/i32, g: (ref/i32 during a) -> i32) -> i32\n    origin c outlives a\n" +
        "    origin c outlives b\n    let h0 = C.init(g@move)\n    let h: C{x} = h0@move\n        CLAUSES\n    let local: i32 = 5\n    return h.call(local@ref)\n" +
        "public func main() => ()\n";

    // A Closure appends a call-local borrow to the `var` capture of a local whose element Origin has no candidate.
    private const string Capture = "func leak() -> i32\n    LOCAL\n    var f = func [var arr@move] () -> i32\n        let local: i32 = 1\n        STORE\n        return 0\n" +
        "    return f@uniq()\npublic func main() => ()\n";

    // A block-local borrow stored into a value that names a `let` local's region, outside any Closure (BU's review of 2a0-3b, round 3).
    private const string Moved = "func leak() -> i32\n    LOCAL\n    var w = arr@move\n    if true\n        let local: i32 = 1\n        STORE\n    return w[0]@follow\n" +
        "public func main() => ()\n";

    // PLAN G65's h2: a Closure stores a call-local borrow into its `var` capture of a `let` local whose two qualifier slots bind each other.
    private const string H2 = "func leak() -> i32\n    let o = Option<Array<ref/i32>>.Some(Array<ref/i32>.init(capacity: 0))\n" +
        "    var f = func [var o@move] (n: i32) -> i32\nREAD        let local: i32 = n\n        var inner: Array<ref/i32> = []\n" +
        "        inner.append(local@ref)\n        o = .Some(inner@move)\n        return 0\n    let first = f@uniq(41)\n    return first + f@uniq(7)\n" +
        "public func main() => ()\n";

    // h2b: the next call reads the stored borrow in place.
    private const string H2bRead = "        match o\n            .Some(let a)\n                if a.length > 0\n                    let old: i32 = a[0]@follow\n" +
        "                    return old\n            _ => return 0\n";

    // The S3 demo (audit-origins 5): a local Function input bounded through a clause by another inferred slot.
    private const string S3Demo = "struct View<T> {source}\n    public let data: ref/T during source\n" +
        "func demo<T>(x: View<T>, anchor: ref/i32 during s, sink: (View<T> during s) -> ()) -> View<T> during x.source\n    origin x.source outlives s\n" +
        "    let pair: (View<T>{w}, (View<T>{k}) -> ()) = (x@move, sink@move)\n        origin k.source outlives w.source\n    return pair.0@move\n" +
        "public func main() => ()";

    [Theory]
    [InlineData("Reassign", "let first = 0\nvar view = first@ref\nlet second = 5\nview = second@ref\nrequire view@follow == 5 else => $abort(\"view\")")]
    [InlineData("BothUses", "let first = 0\nvar view = first@ref\nrequire view@follow == 0 else => $abort(\"before\")\nlet second = 5\nview = second@ref\nrequire view@follow == 5 else => $abort(\"after\")")]
    [InlineData("EndedLoan", "var first = 0\nvar view = first@ref\nlet second = 5\nview = second@ref\nfirst = 9\nrequire view@follow == 5 and first == 9 else => $abort(\"ended\")")]
    [InlineData("Annotation", "let first = 0\nvar view: ref/i32 = first@ref\nlet second = 5\nview = second@ref\nrequire view@follow == 5 else => $abort(\"view\")")]
    [InlineData("Inputs", "func pick(a: ref/i32, b: ref/i32) -> ref/i32 during (a and b)\n    var view = a\n    view = b\n    return view\nlet first = 1\nlet second = 5\nrequire pick(first@ref, second@ref)@follow == 5 else => $abort(\"inputs\")")]
    public void UsesOneLocalTypeAndTheCurrentValue(string name, string source)
        => ScalarEmissionTest.EmitFixture("LocalRegion" + name, source, string.Empty);

    [Theory]
    [InlineData("Branch", "let a = 1\nlet b = 2\nvar view = a@ref\nif a == 1\n    view = b@ref\nelse\n    view = a@ref\nrequire view@follow == 2 else => $abort(\"branch\")")]
    [InlineData("Loop", "let a = 1\nlet b = 2\nvar view = a@ref\nvar n = 0\nwhile n < 3\n    require view@follow >= 1 else => $abort(\"before\")\n    view = b@ref\n    n += 1\nrequire view@follow == 2 else => $abort(\"loop\")")]
    [InlineData("CopyEnds", "var a = 1\nlet b = 2\nvar view = a@ref\nlet saved = view\nview = b@ref\nrequire saved@follow == 1 else => $abort(\"saved\")\na = 3\nrequire view@follow == 2 else => $abort(\"current\")")]
    [InlineData("Uniq", "var a = 1\nvar b = 2\nvar view = a@uniq\nview = b@uniq\na = 3\nview@follow = 5\nrequire b == 5 and a == 3 else => $abort(\"uniq\")")]
    public void FollowsControlFlowAndEndsOnlyUnusedLoans(string name, string source)
        => ScalarEmissionTest.EmitFixture("LocalRegion" + name, source, string.Empty);

    [Theory]
    [InlineData("var a = 1\nvar b = 2\nvar view = a@ref\nif a == 1\n    view = b@ref\na = 3\nrequire view@follow == 2 else => $abort(\"branch\")", "a = 3")]
    [InlineData("var a = 1\nvar b = 2\nvar view = a@ref\nvar n = 0\nwhile n < 3\n    b = 3\n    require view@follow >= 1 else => $abort(\"old\")\n    view = b@ref\n    require view@follow == 3 else => $abort(\"loop\")\n    n += 1\nrequire view@follow == 3 else => $abort(\"after\")", "b = 3")]
    public void KeepsEveryLoanReachingAJoin(string source, string location)
    {
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Contains(errors, x => x.Code == nameof(DiagnosticCode.ComparisonLoanConflict_Kd) && source.Substring(x.Span!.Value.Start, x.Span.Value.Length) == location);
        Assert.DoesNotContain(errors, x => x.Category == DiagnosticCategory.Unsupported);
    }

    [Theory]
    [InlineData("var first = 0\nvar view = first@ref\nlet saved = view\nlet second = 5\nview = second@ref\nfirst = 9\nrequire saved@follow == 0 else => $abort(\"saved\")", "first = 9")]
    [InlineData("var first = 0\nvar view = first@ref\nvar second = 5\nview = second@ref\nsecond = 9\nrequire view@follow == 5 else => $abort(\"view\")", "second = 9")]
    public void AssignmentKeepsEveryLiveHolder(string source, string location)
    {
        var errors = DiagnosticCorpus.Check(source).Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error).ToArray();
        var error = Assert.Single(errors);
        Assert.Equal(nameof(DiagnosticCode.ComparisonLoanConflict_Kd), error.Code);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal(location, source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.NotEmpty(error.Related!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    loop => ()\n")]
    public void FiniteToInferredToFixedIsRefutedEvenAfterNoncompletion(string continuation)
    {
        var source = "func bad(input: ref/i32) -> ref/i32 during input\n    var view = input\n    let local = 5\n    view = local@ref\n" + continuation + "    return view\npublic func main() => ()";
        var errors = DiagnosticCorpus.Check(source).Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error).ToArray();
        var error = Assert.Single(errors);
        Assert.Equal(nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), error.Code);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Contains("outlives", error.Label, StringComparison.Ordinal);
        Assert.Contains(error.Related!, x => x.Role == "origin");
    }

    [Fact]
    public void FixedInputRelationsStillNeedAPremise()
    {
        const string Source = "func bad(a: ref/i32, b: ref/i32) -> ref/i32 during a\n    var view = a\n    view = b\n    return view\npublic func main() => ()";
        var error = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnprovenOriginRelation_Kd), error.Code);
        Assert.Equal(DiagnosticCategory.Proof, error.Category);
        Assert.Contains("b outlives a", error.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void FiniteChainHasTheSameEvidenceInEveryOutput()
    {
        const string Source = "func bad(input: ref/i32) -> ref/i32 during input\n    var view = input\n    let local = 5\n    view = local@ref\n    return view\npublic func main() => ()";
        var check = DiagnosticCorpus.Check(Source);
        var record = Assert.Single(check.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), record.Code);
        Assert.Equal("view", Source.Substring(record.Span!.Value.Start, record.Span.Value.Length));
        Assert.Equal("requires the borrow local@ref outlives input, which is false", record.Label);
        Assert.Contains(record.Related!, x => Source.Substring(x.Span!.Value.Start, x.Span.Value.Length) == "local@ref");
        var result = new DiagnosticResult(check.Diagnostics, check.Sources);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains("local@ref", console.Text, StringComparison.Ordinal);
        Assert.Contains(" = origin:", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(check.Sources[record.Source].Path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, related)[identity]);
            Assert.Equal((record.Code, record.Display!.Range), (sent.Code, sent.Range));
        }

        var json = JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Equal(result, JsonSerializer.Deserialize(json, DiagnosticJsonContext.Default.DiagnosticResult));
    }

    // SPEC 15.3.6: a body variable of a local whose row has no candidate is a local region of the local, never invented as static, so each
    // program is valid on both pipelines (DECISIONS R4-M112): an initializer's call qualifier slot outside the local's Type, a variable
    // bounded only by variables, and a contravariant slot with no principal lower bound, whose bounds hold through the region's one
    // source, the call's argument.
    [Theory]
    [InlineData("func f() -> isize\n    let n = Array<ref/i32>.init(capacity: 0).length\n    return n\npublic func main() => ()")]
    [InlineData("func inspect<T>(value: T) -> i32 => 0\npublic func main()\n    let a: Option<ref/i32> = .None\n    let b: Option<ref/i32> = a\n    let n = inspect(b@move)")]
    [InlineData(NoPrincipal)]
    public void AVariableWithNoCandidateIsALocalRegion(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var verified = c.Bind().IsComplete && c.Binding.CheckStartup(OutputKind.Application).IsComplete && c.Analyze() is { IsVerified: true, ErrorCount: 0 };
        Assert.True(verified, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 15.3.6, 15.4.4, 15.6.5 (BU's review of 2a0-3b, B1; PLAN G225; DECISIONS R4-M112, BU-28): a call-local borrow stored through a
    // `var` capture of a `let` local's region (an initializer qualifier's slot, a written slot bound only to itself, a store through a
    // call) is Refuted at the store, since the Closure body fixes its captures' Origins (G227); before G227 the region followed it past
    // the call to a Loan conflict.
    [Theory]
    [InlineData("let arr = Array<ref/i32>.init(capacity: 0)", "arr.append(local@ref)")]
    [InlineData("let arr: Array<ref/i32> = []", "arr.append(local@ref)")]
    [InlineData("let arr = Array<ref/i32>.init(capacity: 0)", "keep(arr@uniq, local@ref)")]
    public void AStoreIntoARegionWithNoCandidateIsFollowed(string local, string store)
    {
        var source = "func keep(slot: uniq/Array<ref/i32 during s>, v: ref/i32 during s) => slot@follow.append(v)\n" +
            Capture.Replace("LOCAL", local, StringComparison.Ordinal).Replace("STORE", store, StringComparison.Ordinal);
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), error.Code);
        Assert.StartsWith("local@ref", source[error.Span!.Value.Start..], StringComparison.Ordinal);
    }

    // SPEC 15.3.6, 15.4.4, 15.6.5: the region records the Loans later stored into it, so a block-local borrow stored into a value that
    // names it is followed past the block (BU's review of 2a0-3b, B1 and N18; PLAN G225): an initializer qualifier's slot and a written slot
    // bound only to itself, stored by a method and through a call. Dev rejects these with UnprovenConstraint_Kd at the local.
    [Theory]
    [InlineData("let arr = Array<ref/i32>.init(capacity: 0)", "w.append(local@ref)")]
    [InlineData("let arr: Array<ref/i32> = []", "w.append(local@ref)")]
    [InlineData("let arr = Array<ref/i32>.init(capacity: 0)", "keep(w@uniq, local@ref)")]
    public void AStoreIntoAMovedRegionIsFollowedPastItsBlock(string local, string store)
    {
        var source = "func keep(slot: uniq/Array<ref/i32 during s>, v: ref/i32 during s) => slot@follow.append(v)\n" +
            Moved.Replace("LOCAL", local, StringComparison.Ordinal).Replace("STORE", store, StringComparison.Ordinal);
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ComparisonLoanConflict_Kd), error.Code);
        Assert.StartsWith("let local", source[error.Span!.Value.Start..], StringComparison.Ordinal);
    }

    // SPEC 15.6.5 (DECISIONS R4-M112, BU-28): PLAN G65's h2, a `let` local's region captured `[var o@move]` and assigned a call-local
    // borrow, is Refuted at the store whether or not a later call reads it (h2b), since the Closure body fixes its captures' Origins
    // (G227); it was accepted, then pinned as a recorded difference, before G227.
    [Theory]
    [InlineData("")]
    [InlineData(H2bRead)]
    public void ACallLocalBorrowStoredIntoACapturedRegionIsRefutedAtTheStore(string read)
    {
        var source = H2.Replace("READ", read, StringComparison.Ordinal);
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), error.Code);
        Assert.StartsWith("inner@move", source[error.Span!.Value.Start..], StringComparison.Ordinal);
    }

    // SPEC 15.3.6: the principal lower bound is chosen after every bound is seen, whatever the clause order, so both orders keep the one
    // record at the borrow that does not outlive it (BU's review of 2a0-3b, B2).
    [Theory]
    [InlineData("origin x.s outlives b\n        origin x.s outlives c")]
    [InlineData("origin x.s outlives c\n        origin x.s outlives b")]
    public void AClauseOrderKeepsThePrincipalCandidate(string clauses)
    {
        var source = Principal.Replace("CLAUSES", clauses, StringComparison.Ordinal);
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "local@ref"), (error.Code, source.Substring(error.Span!.Value.Start, error.Span.Value.Length)));
    }

    // SPEC 15.3.6, 15.6.5: the region's source must outlive each of its bounds; without the premise for the clause's bound, the clause's
    // row is the relation record at the clause (the old analysis's until A5-F4), never MissingOriginBinding_Kd at the local.
    [Fact]
    public void ARegionsSourceMustOutliveItsClauseBound()
    {
        var source = NoPrincipal.Replace("    origin c outlives b\n", string.Empty, StringComparison.Ordinal);
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "origin x.s outlives b"), (error.Code, source.Substring(error.Span!.Value.Start, error.Span.Value.Length)));
    }

    // Pinned: outside a local an omitted Origin keeps its record, a Field's (SPEC 15.3.6) and an expression statement's call qualifier's
    // (PLAN G10), and the S3 demo, a local Function input bounded through a clause by another inferred slot, stays the located
    // Unsupported_Kd at its local (OQ-1 (c); DECISIONS R4-M112).
    [Theory]
    [InlineData("struct S\n    public let r: ref/i32\npublic func main() => ()", nameof(DiagnosticCode.MissingOriginBinding_Kd), "ref/i32")]
    [InlineData("public func main()\n    let n = 0\n    Array<ref/i32>.init(capacity: 0).length", nameof(DiagnosticCode.MissingOriginBinding_Kd), "ref/i32")]
    [InlineData(S3Demo, nameof(DiagnosticCode.Unsupported_Kd), "let pair")]
    public void AnOriginOutsideTheRegionRulesKeepsItsRecord(string source, string code, string at)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(code, error.Code);
        Assert.StartsWith(at, source[error.Span!.Value.Start..], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void WarmConstraintsLoansAndEmissionReuseStorage()
    {
        const string Source = "var a = 1\nlet b = 2\nvar view = a@ref\nlet saved = view\nview = b@ref\nrequire saved@follow == 1 else => $abort(\"saved\")\na = 3\nrequire view@follow == 2 else => $abort(\"current\")";
        var c = MinimalEmissionTest.Analyze(Source);
        var expected = CompilationTestHelper.WriteIr(c);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(expected, CompilationTestHelper.WriteIr(c));
    }
}
