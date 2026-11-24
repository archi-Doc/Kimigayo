// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 8.10: normalize the whole contract set, validate its shape, then select by ordinary argument ranking.
public class CallableSignatureSelectionTest
{
    private const string Main = "public func main() -> ()\n    Console.writeLine(\"done\")\n";

    // A Contract whose clause makes `Self.(Holder).F` callable, with requirements whose results are that Type at depth 1, 2 and 3.
    private const string HolderHead = "contract Holder\n    associate Next is Holder\n    associate F\n";

    private const string HolderResults = "    func near(self: ref/Self) -> ref/Self.(Holder).F\n    func deep(self: ref/Self) -> ref/Self.(Holder).Next.(Holder).F\n" +
        "    func deep3(self: ref/Self) -> ref/Self.(Holder).Next.(Holder).Next.(Holder).F\n";

    private const string Holder = HolderHead + "    Self.(Holder).F is Callable<(i32) -> i32>\n" + HolderResults;

    private const string HolderWithoutClause = HolderHead + HolderResults;

    private const string Apply = "func apply<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    return f(1)\n";

    private const string NoCall = "func noCall<F>(f: ref/F) -> i32\n    F is not Callable<(i32) -> i32>\n    return 0\n";

    // R7-C2c2 c2-5b: Holder whose clause is the negation, and its rows' callees: `noCall64`, another signature's negation; the pairs
    // `pick`, `pick64` and `pickr`, whose first requires the Callable Constraint and is strictly best (SPEC 10.1); `needNot`,
    // `needNot2` and `needCall`, signatures with per-call binders (a premise and a requirement written apart are one proposition, 8.10).
    private const string NotHolder = HolderHead + "    Self.(Holder).F is not Callable<(i32) -> i32>\n" + HolderResults;

    private const string NoCall64 = "func noCall64<F>(f: ref/F) -> i32\n    F is not Callable<(i64) -> i32>\n    return 0\n";

    private const string Pick = "func pick<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    return 1\nfunc pick<F>(f: ref/F, tag: i32 = 0) -> i32\n    return 2\n";

    private const string Pick64 = "func pick64<F>(f: ref/F) -> i32\n    F is Callable<(i64) -> i32>\n    return 1\nfunc pick64<F>(f: ref/F, tag: i32 = 0) -> i32\n    return 2\n";

    private const string PickRef = "func pickr<F>(f: ref/F) -> i32\n    F is Callable<(ref/i32) -> i32>\n    return 1\nfunc pickr<F>(f: ref/F, tag: i32 = 0) -> i32\n    return 2\n";

    private const string NeedNot = "func needNot<F>(f: ref/F) -> i32\n    F is not Callable<(ref/i32) -> i32>\n    return 0\n";

    private const string NeedNot2 = "func needNot2<F>(f: ref/F) -> i32\n    F is not Callable<(ref/i32, ref/i32) -> i32>\n    return 0\n";

    private const string NeedCall = "func needCall<F>(f: ref/F) -> i32\n    F is Callable<(ref/i32) -> i32>\n    return 0\n";

    private const string Forward = "func both<T, F>(action: ref/F, value: ref/T) -> i32\n    F is Callable<(ref/T) -> i32>\n    F is Callable<(ref/i32) -> i32>\n    return action(value)\nfunc read(value: ref/i32) -> i32 => value@follow\nlet n: i32 = 42\nrequire both(read, n@ref) == 42 else => $abort(\"selected\")";

    [Theory]
    [InlineData("func both<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i32>\n    return action(1)\n", "action(1)", DiagnosticCode.AmbiguousBinding_Kd)]
    [InlineData("func both<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i32>\n    return action(true)\n", "action(true)", DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData("func both<F>(action: ref/F) -> ()\n    F is Callable<(i32) -> ()>\n    F is Callable<(ref/i32) -> ()>\n    let n: i32 = 1\n    action(n)\n", "action(n)", DiagnosticCode.ParameterShapeMismatch_Kd)]
    [InlineData("func both<F>(action: uniq/F) -> ()\n    F is Callable<ref, (i32) -> ()>\n    F is Callable<uniq, (i64) -> ()>\n    action(1@i32)\n", "action(1@i32)", DiagnosticCode.ReceiverShapeMismatch_Kd)]
    [InlineData("func generic<T, F>(action: ref/F, value: T) -> T\n    F is Callable<(T) -> T>\n    F is Callable<(i32) -> i32>\n    return action(value)\n", "action(value)", DiagnosticCode.ParameterShapeMismatch_Kd)]
    public void InvalidCallsExplainTheSelectionFailure(string body, string text, DiagnosticCode code)
    {
        var source = body + Main;
        var output = DiagnosticCorpus.Check(source);
        var error = Assert.Single(output.Diagnostics);
        Assert.Equal((code.ToString(), DiagnosticCategory.Language, text), (error.Code, error.Category, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
        Assert.Equal(2, error.Related!.Length);
        Assert.False(string.IsNullOrWhiteSpace(error.Note));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedArgumentsSelectIndependentlyOfConstraintOrder(bool reversed)
    {
        var clauses = new[] { "    F is Callable<(i32) -> i32>\n", "    F is Callable<(i64) -> i64>\n" };
        var source = "func both<F>(action: ref/F) -> i32\n" + clauses[reversed ? 1 : 0] + clauses[reversed ? 0 : 1] + "    return action(1@i32)\n" + Main;
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    [Fact]
    public void ExpectedResultsExcludeButDoNotRankCandidates()
        => Assert.Empty(DiagnosticCorpus.Check("func both<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i64>\n    return action(1)\n" + Main).Diagnostics);

    [Fact]
    public void SelectedContractsExecuteAfterGenericSubstitution()
        => ScalarEmissionTest.EmitFixture("CallableSelectionForward", Forward, string.Empty);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdentityAndPerCallBindersNormalizeBeforeReceiverMerging(bool reversed)
    {
        var a = "    F is Callable<uniq, (ref/T) -> ref/T>\n";
        var b = "    F is Callable<(ref/i32) -> ref/i32>\n";
        var source = "func same<T, F>(f: ref/F, n: ref/i32) -> ref/i32\n    T is i32\n" + (reversed ? b + a : a + b) + "    return f(n)\nfunc identity(n: ref/i32) -> ref/i32 => n\nlet n: i32 = 42\nlet f: (ref/i32) -> ref/i32 = identity\nrequire same<i32, (ref/i32) -> ref/i32>(f, n@ref)@follow == 42 else => $abort(\"identity\")";
        ScalarEmissionTest.EmitFixture("CallableSelectionIdentity" + reversed, source, string.Empty);
    }

    [Theory]
    [InlineData("((i32, bool)) -> i32", "((i64, bool)) -> i64", "(1@i32, true)")]
    [InlineData("([2 of i32]) -> i32", "([3 of i32]) -> i32", "[1, 2]")]
    [InlineData("(i32?) -> i32", "(i64?) -> i64", ".Some(1@i32)")]
    public void ContextualInputsAreProbedWithoutCommittingAnAlternative(string first, string second, string argument)
    {
        var source = "func both<F>(f: ref/F) -> i32\n    F is Callable<" + first + ">\n    F is Callable<" + second + ">\n    return f(" + argument + ")\n" + Main;
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    [Fact]
    public void ATypeMustSatisfyEveryCallableConstraint()
    {
        var source = "func both<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i64>\n    return f(1@i32)\nfunc identity(n: i32) -> i32 => n\nlet n = both(identity)";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.NoApplicableOverload_Kd), error.Code);
        Assert.Equal("both(identity)", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
    }

    [Fact]
    public void TheSelectedSignatureSuppliesAnAnonymousArgumentsContextOnce()
    {
        var source = "func both<F>(f: ref/F) -> i32\n    F is Callable<((i32) -> i32, i32) -> i32>\n    F is Callable<((i64) -> i64, i64) -> i32>\n    F is Owned\n    return f(func [] (n) => n, 1@i32)\n" + Main;
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    // SPEC 3.2.1, 6.1.3, 15.2.3, 7.7.4 (the owner's O-249 Q4 (a); PLAN G846, G856): an anonymous argument erased in `both<F>` holds F in its
    // instantiation, so without `F is Owned` its erasure is invalid, beside its body's own failure (the two rows around this one until
    // G856, accepted then).
    [Theory]
    [InlineData("n", new[] { nameof(DiagnosticCode.UnprovenConstraint_Kd) })]
    [InlineData("\"bad\"", new[] { nameof(DiagnosticCode.UnprovenConstraint_Kd), nameof(DiagnosticCode.TypeMismatch_Kd) })]
    public void AnAnonymousArgumentsErasureNeedsTheEnclosingParameterOwned(string body, string[] codes)
    {
        var source = "func both<F>(f: ref/F) -> i32\n    F is Callable<((i32) -> i32, i32) -> i32>\n    F is Callable<((i64) -> i64, i64) -> i32>\n    return f(func [] (n) => " + body + ", 1@i32)\n" + Main;
        Assert.Equal(codes, DiagnosticCorpus.Check(source).Diagnostics.Select(x => x.Code));
    }

    [Fact]
    public void ResultsWithoutAnExpectationNeverRank()
    {
        var source = "func both<F>(f: ref/F)\n    F is Callable<(i32) -> i32>\n    F is Callable<(i32) -> i64>\n    _ = f(1@i32)\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.AmbiguousBinding_Kd), error.Code);
    }

    [Fact]
    public void ASelectedAnonymousBodyFailureDoesNotRetryAnotherSignature()
    {
        var source = "func both<F>(f: ref/F) -> i32\n    F is Callable<((i32) -> i32, i32) -> i32>\n    F is Callable<((i64) -> i64, i64) -> i32>\n    F is Owned\n    return f(func [] (n) => \"bad\", 1@i32)\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), error.Code);
        Assert.Equal("\"bad\"", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("i64", false)]
    public void OnlyTheSelectedContractsBoundsApply(string type, bool confined)
    {
        var source = "func both<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n        effect confined\n    F is Callable<(i64) -> i32>\n    return f(1@" + type + ")\n" + Main;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), static x => x.ValueCallOf() is not null);
        var plan = call.ValueCallOf()!;
        Assert.Equal(confined, c.Binding.AvailableCallableEffects(plan.CalleeType.Components[0], plan.DeclaredSignature, plan.ReceiverKind, call));
    }

    [Fact]
    public void NormalizedSignatureEffectsRemainAvailable()
    {
        const string Source = "func bounded<G>(g: ref/G, n: ref/i32) -> ref/i32\n    G is Callable<(ref/i32) -> ref/i32>\n        effect confined\n    return g(n)\nfunc forward<T, F>(f: ref/F, n: ref/i32) -> ref/i32\n    T is i32\n    F is Callable<(ref/T) -> ref/T>\n        effect confined\n    F is Callable<uniq, (ref/i32) -> ref/i32>\n    return bounded(f, n)\n";
        Assert.Empty(DiagnosticCorpus.Check(Source + Main).Diagnostics);
    }

    [Fact]
    public void AChangedContractInvalidatesThePreviousSelection()
    {
        var c = MinimalEmissionTest.Analyze("func both<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i64>\n    return f(1)\n" + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var clause = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<IsKoto>().Last();
        var donor = MinimalEmissionTest.Analyze("func other<F>(f: ref/F)\n    F is Callable<(i64) -> i32>\n    return\n" + Main);
        var replacement = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<IsKoto>().Last();
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(clause.Parent!, clause, replacement));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.AmbiguousBinding_Kd);
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void WarmSelectionReusesCandidateAndOperationStorage()
    {
        var c = MinimalEmissionTest.Analyze(Forward);
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void RejectedSelectionReusesItsDiagnosticFacts()
    {
        var c = MinimalEmissionTest.Analyze("func both<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i32>\n    return f(1)\n" + Main);
        Assert.False(c.Binding.Result.IsComplete);
        var rejected = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => rejected &= !c.Bind().IsComplete));
        Assert.True(rejected);
        Assert.Single(c.Binding.Issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbiguityNamesTheAvailableContractsInCliAndLsp(bool checkBound)
    {
        const string Source = "func both<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i32>\n    return f(1)\n" + Main;
        var path = Path.GetFullPath("CallableSelection.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        if (checkBound)
        {
            Assert.False(c.Binding.CheckBound().IsComplete);
        }

        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.AmbiguousBinding_Kd), record.Code);
        Assert.Equal(2, record.Related!.Length);
        Assert.All(record.Related!, static x => Assert.Equal("candidate", x.Role));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("Callable<ref, (i32) -> i32>", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(record.Code, sent.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
        }
    }

    // R7-C2c2 c2-3b (SPEC 8.3.2, 8.10, 10.5.5): `T is Holder` supplies Holder's Callable clause at every depth of T's chain, whatever
    // spelling the scope gives the Type. A value whose Type is a requirement's result or a field's Type is called (the call's
    // candidates, kd, rfa) and passed to a function requiring the Constraint (its proof, kdp) as one spelled in the scope is, also
    // through a parameter identity (SPEC 8.2.3); without the clause, or for another signature, each is rejected as at depth 1. A
    // `not Callable` premise at such a depth contradicts the supplied clause (rncp: accepted before c2-3b, class 1). R7-C2c2 c2-5b
    // (SPEC 8.3.2 Exact assumption and Negation, 10.1): a negated clause is supplied at every depth too: it proves a `not Callable`
    // requirement (nd) and refutes a Callable Constraint, so the candidate requiring it is inapplicable (ndr); a written Callable
    // premise at such a depth contradicts it (ncp: accepted before c2-5b, class 1). Without the clause, with the positive clause, for
    // another signature or on T itself each stays as it was, and depth 1 (nd1, ncp1s) answers as before. The depth-0 forms with no
    // projection and no identity premise (na: per-call binders, an overload pair, a contradiction) are answered by the rows instead
    // of Supply's fact scan, as before. Rows as CompilationTestHelper.AssertRecordsAtLines reads them.
    [Theory]
    [InlineData("kd2", Holder + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    let g = t.deep()\n    return g(1)\n", "")]
    [InlineData("kd3", Holder + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    let g = t.deep3()\n    return g(1)\n", "")]
    [InlineData("kd2i", Holder + "func probe<T, U>(t: ref/U) -> i32\n    U is T\n    T is Holder\n    let g = t.deep()\n    return g(1)\n", "")]
    [InlineData("rfa2", Holder + "struct Box<T>\n    T is Holder\n    public var f: T.(Holder).Next.(Holder).F\nfunc probe<T>(b: ref/Box<T>) -> i32\n    T is Holder\n    let g = b.f@ref\n    return g(1)\n", "")]
    [InlineData("kd2n", HolderWithoutClause + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    let g = t.deep()\n    return g(1)\n", "NotCallable_Kd: return g(1)")]
    [InlineData("kdp2", Holder + Apply + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    let g = t.deep()\n    return apply(g)\n", "")]
    [InlineData("kdp3", Holder + Apply + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    let g = t.deep3()\n    return apply(g)\n", "")]
    [InlineData("kdp2i", Holder + Apply + "func probe<T, U>(t: ref/U) -> i32\n    U is T\n    T is Holder\n    let g = t.deep()\n    return apply(g)\n", "")]
    [InlineData("kdp2n", HolderWithoutClause + Apply + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    let g = t.deep()\n    return apply(g)\n", "UnprovenConstraint_Kd: return apply(g)")]
    [InlineData("kdpb2", Holder + "func applyB<F>(f: ref/F) -> i32\n    F is Callable<(bool) -> bool>\n    return 0\n" + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    let g = t.deep()\n    return applyB(g)\n", "NoApplicableOverload_Kd: return applyB(g)")]
    [InlineData("rncp2", Holder + NoCall + "func probe<T, U>(t: ref/T) -> i32\n    T is Holder\n    U is T\n    U.(Holder).Next.(Holder).F is not Callable<(i32) -> i32>\n    let g = t.deep()\n    return noCall(g)\n", "InvalidConstraint_Kd: func probe<T, U>(t: ref/T) -> i32 <- U.(Holder).Next.(Holder).F is not Callable<(i32) -> i32>")] // R7-C3 C3-1b (F's C2 rulings 284 (3), 294; DECISIONS Q13, location): one premise error, reported where the premises meet (SPEC 8.2.3): at `func probe`, naming the clause
    [InlineData("rncp2o", Holder + NoCall + "func probe<T, U>(t: ref/T) -> i32\n    U.(Holder).Next.(Holder).F is not Callable<(i32) -> i32>\n    U is T\n    T is Holder\n    let g = t.deep()\n    return noCall(g)\n", "InvalidConstraint_Kd: func probe<T, U>(t: ref/T) -> i32 <- U.(Holder).Next.(Holder).F is not Callable<(i32) -> i32>")] // R7-C3 C3-1b (F's C2 rulings 284 (3), 294; DECISIONS Q13, location): one premise error, reported where the premises meet (SPEC 8.2.3): at `func probe`, naming the clause
    [InlineData("rncp2r", Holder + NoCall + "func probe<T, U>(t: ref/T) -> i32\n    T is Holder\n    T is U\n    U.(Holder).Next.(Holder).F is not Callable<(i32) -> i32>\n    let g = t.deep()\n    return noCall(g)\n", "InvalidConstraint_Kd: func probe<T, U>(t: ref/T) -> i32 <- U.(Holder).Next.(Holder).F is not Callable<(i32) -> i32>")] // R7-C3 C3-1b (F's C2 rulings 284 (3), 294; DECISIONS Q13, location): one premise error, reported where the premises meet (SPEC 8.2.3): at `func probe`, naming the clause
    [InlineData("rncp2s", Holder + NoCall + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    T.(Holder).Next.(Holder).F is not Callable<(i32) -> i32>\n    let g = t.deep()\n    return noCall(g)\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) -> i32 <- T.(Holder).Next.(Holder).F is not Callable<(i32) -> i32>")] // R7-C3 C3-1b (F's C2 rulings 284 (3), 294; DECISIONS Q13, location): one premise error, reported where the premises meet (SPEC 8.2.3): at `func probe`, naming the clause
    [InlineData("nd2", NotHolder + NoCall + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    let g = t.deep()\n    return noCall(g)\n", "")] // c2-5b: rejected before
    [InlineData("nd3", NotHolder + NoCall + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    let g = t.deep3()\n    return noCall(g)\n", "")] // c2-5b: rejected before
    [InlineData("nd2i", NotHolder + NoCall + "func probe<T, U>(t: ref/U) -> i32\n    U is T\n    T is Holder\n    let g = t.deep()\n    return noCall(g)\n", "")] // c2-5b: rejected before
    [InlineData("ndr2", NotHolder + Pick + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    let g = t.deep()\n    return pick(g)\n", "")] // c2-5b: rejected before
    [InlineData("ncp2s", NotHolder + Apply + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    T.(Holder).Next.(Holder).F is Callable<(i32) -> i32>\n    let g = t.deep()\n    return apply(g)\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) -> i32 <- T.(Holder).Next.(Holder).F is Callable<(i32) -> i32>")] // c2-5b: class 1, accepted before
    [InlineData("ncp3s", NotHolder + Apply + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    T.(Holder).Next.(Holder).Next.(Holder).F is Callable<(i32) -> i32>\n    let g = t.deep3()\n    return apply(g)\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) -> i32 <- T.(Holder).Next.(Holder).Next.(Holder).F is Callable<(i32) -> i32>")] // c2-5b: class 1, accepted before
    [InlineData("ncp2i", NotHolder + Apply + "func probe<T, U>(t: ref/T) -> i32\n    T is Holder\n    U is T\n    U.(Holder).Next.(Holder).F is Callable<(i32) -> i32>\n    let g = t.deep()\n    return apply(g)\n", "InvalidConstraint_Kd: func probe<T, U>(t: ref/T) -> i32 <- U.(Holder).Next.(Holder).F is Callable<(i32) -> i32>")] // c2-5b: class 1, accepted before
    [InlineData("nd1", NotHolder + NoCall + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    let g = t.near()\n    return noCall(g)\n", "")]
    [InlineData("nd2n-sig", NotHolder + NoCall64 + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    let g = t.deep()\n    return noCall64(g)\n", "UnprovenConstraint_Kd: return noCall64(g)")]
    [InlineData("nd2n-bare", HolderWithoutClause + NoCall + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    let g = t.deep()\n    return noCall(g)\n", "UnprovenConstraint_Kd: return noCall(g)")]
    [InlineData("nd2n-pos", Holder + NoCall + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    let g = t.deep()\n    return noCall(g)\n", "NoApplicableOverload_Kd: return noCall(g)")]
    [InlineData("nd0n-self", NotHolder + NoCall + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    return noCall(t)\n", "UnprovenConstraint_Kd: return noCall(t)")]
    [InlineData("ndr2-pos", Holder + Pick + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    let g = t.deep()\n    return pick(g)\n", "")]
    [InlineData("ndr2n-bare", HolderWithoutClause + Pick + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    let g = t.deep()\n    return pick(g)\n", "UnprovenConstraint_Kd: return pick(g)")]
    [InlineData("ndr2n-sig", NotHolder + Pick64 + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    let g = t.deep()\n    return pick64(g)\n", "UnprovenConstraint_Kd: return pick64(g)")]
    [InlineData("ndr2n-self", NotHolder + Pick + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    return pick(t)\n", "UnprovenConstraint_Kd: return pick(t)")]
    [InlineData("ncp1s", NotHolder + Apply + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    T.(Holder).F is Callable<(i32) -> i32>\n    let g = t.near()\n    return apply(g)\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) -> i32")]
    [InlineData("ncp2-sig", NotHolder + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    T.(Holder).Next.(Holder).F is Callable<(i64) -> i32>\n    return 0\n", "")]
    [InlineData("ncp2-bare", HolderWithoutClause + Apply + "func probe<T>(t: ref/T) -> i32\n    T is Holder\n    T.(Holder).Next.(Holder).F is Callable<(i32) -> i32>\n    let g = t.deep()\n    return apply(g)\n", "")]
    [InlineData("na0", NeedNot + "func probe<G>(g: ref/G) -> i32\n    G is not Callable<(ref/i32) -> i32>\n    return needNot(g)\n", "")]
    [InlineData("na0two", NeedNot2 + "func probe<G>(g: ref/G) -> i32\n    G is not Callable<(ref/i32, ref/i32) -> i32>\n    return needNot2(g)\n", "")]
    [InlineData("na0r", PickRef + "func probe<G>(g: ref/G) -> i32\n    G is not Callable<(ref/i32) -> i32>\n    return pickr(g)\n", "")]
    [InlineData("na0p", NeedCall + "func probe<G>(g: ref/G) -> i32\n    G is Callable<(ref/i32) -> i32>\n    return needCall(g)\n", "")]
    [InlineData("na0i", NoCall + "func probe<G>(g: ref/G) -> i32\n    G is not Callable<(i32) -> i32>\n    return noCall(g)\n", "")]
    [InlineData("na0c", "func probe<G>(g: ref/G) -> i32\n    G is Callable<(ref/i32) -> i32>\n    G is not Callable<(ref/i32) -> i32>\n    return 0\n", "InvalidConstraint_Kd: func probe<G>(g: ref/G) -> i32")]
    [InlineData("na0n-sig", NeedNot + "func probe<G>(g: ref/G) -> i32\n    G is not Callable<(ref/i64) -> i32>\n    return needNot(g)\n", "UnprovenConstraint_Kd: return needNot(g)")]
    [InlineData("na0n-none", NeedNot + "func probe<G>(g: ref/G) -> i32\n    return needNot(g)\n", "UnprovenConstraint_Kd: return needNot(g)")]
    [InlineData("na0rn", PickRef + "func probe<G>(g: ref/G) -> i32\n    return pickr(g)\n", "UnprovenConstraint_Kd: return pickr(g)")]
    [InlineData("nw2i", HolderWithoutClause + NoCall + "func probe<T, U>(t: ref/T) -> i32\n    T is Holder\n    U is T\n    U.(Holder).Next.(Holder).F is not Callable<(i32) -> i32>\n    let g = t.deep()\n    return noCall(g)\n", "")]
    public void AContractsCallableClauseIsSuppliedAtEveryDepth(string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines(form, program + "public func main() => ()\n", records);

    // One signature with a shared and an exclusive receiver is one candidate, and forwarding several signatures makes no call.
    [Theory]
    [InlineData("func one<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<uniq, (i32) -> i32>\n    return action(1)\nfunc inc(value: i32) -> i32 => value + 1\npublic func main() -> ()\n    require one(inc) == 2 else => $abort(\"b\")\n    Console.writeLine(\"done\")\n")]
    [InlineData("func both<F>(action: ref/F) -> ()\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i64>\n    return ()\nfunc outer<G>(action: ref/G) -> ()\n    G is Callable<(i32) -> i32>\n    G is Callable<(i64) -> i64>\n    both(action)\n" + Main)]
    public void OneSignatureOrNoCallIsAccepted(string source)
        => Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
}
