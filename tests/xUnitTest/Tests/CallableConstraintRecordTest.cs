// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 8.7, 15.6.1: a Callable Constraint whose proof fails only in the Origin part of its whole-contract comparison is Unknown, never
// Refuted, and reports the Constraint code UnprovenConstraint_Kd at the value whose Type binds F, naming the clause, the member and the
// relation with both ends, instead of NoApplicableOverload_Kd.
public class CallableConstraintRecordTest(ITestOutputHelper output)
{
    private const string Apply = "func apply<F>(f: ref/F) -> i32\n    F is Callable<() -> ref/i32>\n    let r = f()\n    return r@follow\n";

    private const string ParameterAdvice = "Pass an implementation whose 1st parameter accepts any borrow, as an input written without an Origin does";

    private const string ApplyInput = "func apply<F>(f: ref/F, x: ref/i32) -> i32\n    F is Callable<(ref/i32) -> ref/i32>\n    let r = f(x)\n    return r@follow\n";

    [Theory]
    [InlineData("EnvironmentResult", Apply + "public func main() -> ()\n    let n = 7\n    let f = func [n] () => n@ref\n    let v = apply(f@ref)\n", "f@ref", "the result", "closure", "call receiver", "static", "hidden environment receiver (SPEC 8.6)", "Return a borrow that outlives static and that the closure's environment does not own, such as a borrow of static storage")]
    [InlineData("CapturedBorrow", Apply + "public func main() -> ()\n    let n = 7\n    let view = n@ref\n    let f = func [view] () => view\n    let v = apply(f@ref)\n", "f@ref", "the result", "borrow", "n", "static", "judged from the premises alone", "Pass an implementation whose result outlives static, such as a borrow of static storage")]
    [InlineData("EnvironmentOverInput", ApplyInput + "public func main() -> ()\n    let n = 7\n    let f = func [n] (x: ref/i32) => n@ref\n    let m = 1\n    let v = apply(f@ref, m@ref)\n", "f@ref", "the result", "closure", "call receiver", "ref/i32", "hidden environment receiver (SPEC 8.6)", "Return a borrow that outlives the omitted Origin of ref/i32 and that the closure's environment does not own, such as a borrow of its input")]
    [InlineData("FixedInput", "func callIt<F>(f: uniq/F, x: ref/i32) -> i32\n    F is Callable<uniq, (ref/i32) -> ref/i32>\n    let r = f(x)\n    return r@follow\nfunc use(x: ref/i32) -> i32\n    let start: ref/i32 = x\n    var c = func [var start] (n: ref/i32 during x) -> ref/i32 during x\n        let old = start\n        start = n\n        return old\n    return callIt(c@uniq, x)\npublic func main() -> ()\n    let a: i32 = 3\n    Console.writeLine(\"\\(use(a@ref))\")\n", "c@uniq", "the 1st parameter", "omitted", "ref/i32", "x", "judged from the premises alone", ParameterAdvice)]
    [InlineData("FixedParameter", "func apply<F>(f: ref/F, x: ref/i32) -> i32\n    F is Callable<(ref/i32) -> i32>\n    return f(x)\nfunc pick(x: ref/i32 during static) -> i32 => x@follow\npublic func main() -> ()\n    let m = 1\n    let v = apply(pick, m@ref)\n", "pick", "the 1st parameter", "omitted", "ref/i32", "static", "judged from the premises alone", ParameterAdvice)]
    [InlineData("FixedResult", "func callIt<F>(f: ref/F, y: ref/i32) -> i32\n    F is Callable<(ref/i32) -> ref/i32>\n    let r = f(y)\n    return r@follow\nfunc use(x: ref/i32) -> i32\n    let c = func (n: ref/i32) -> ref/i32 during x => x\n    let local: i32 = 4\n    return callIt(c@ref, local@ref)\npublic func main() -> ()\n    let a: i32 = 3\n    Console.writeLine(\"\\(use(a@ref))\")\n", "c@ref", "the result", "expression", "x", "ref/i32", "judged from the premises alone", "Pass an implementation whose result outlives the omitted Origin of ref/i32, such as a borrow of its input")]
    public void AnOriginOnlyCallableFailureIsTheConstraintRecord(string name, string source, string at, string member, string longerKind, string longer, string shorter, string note, string advice)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        output.WriteLine($"{name}: {error.Note}");
        Assert.Equal((nameof(DiagnosticCode.UnprovenConstraint_Kd), DiagnosticCategory.Proof, at), (error.Code, error.Category, Text(source, error.Span)));
        var reason = error.Reason!;
        Assert.Equal(["subject", "constraint", "member", "relation", "longer", "shorter"], reason.Select(static x => x.Name));
        Assert.StartsWith("F is Callable<", reason[1].Value, StringComparison.Ordinal);
        Assert.Equal((member, "outlives"), (reason[2].Value, reason[3].Value));
        Assert.Equal((longerKind, longer), (reason[4].Origin, reason[4].Value));
        Assert.Equal(shorter, reason[5].Value);
        var constraint = Assert.Single(error.Related!, static x => x.Role == "constraint");
        Assert.StartsWith("F is Callable<", Text(source, constraint.Span), StringComparison.Ordinal);
        Assert.Contains(error.Related!, static x => x.Role == "origin");
        Assert.Contains(note, error.Note, StringComparison.Ordinal);

        // SPEC 8.6: a Callable signature writes no `during`, so the Advice names only an implementation that fits.
        Assert.Equal(advice, error.Advice);
        Assert.Null(error.Repairs);
    }

    // SPEC 10.5, 8.7: an Unknown Constraint proves neither applicability nor its negation, so another applicable candidate is not selected;
    // the one pending candidate's record explains the call.
    [Fact]
    public void AnUnknownCallableProofNeverSelectsAnotherCandidate()
    {
        var source = Apply + "func apply<F>(f: ref/F, extra: i32 = 0) -> i32 => 0\npublic func main() -> ()\n    let n = 7\n    let f = func [n] () => n@ref\n    let v = apply(f@ref)\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenConstraint_Kd), "f@ref"), (error.Code, Text(source, error.Span)));
    }

    // SPEC 10.5, 8.7: a Callable clause on an F that an independent argument binds waits on no argument, so its Unknown proof keeps the
    // candidate pending even beside a waiting anonymous argument: another candidate is never selected (the first row printed 0), and
    // the record is the Constraint record at the argument also when the candidate is the only one or would win (they were a fact-less
    // UnprovenConstraint_Kd at the call). An omitted header Type rests on that record.
    [Theory]
    [InlineData("func apply2<F, G>(f: ref/F, g: ref/G) -> i32 => 0\n", "func (k: i32) -> i32 => k + 1")]
    [InlineData("", "func (k: i32) -> i32 => k + 1")]
    [InlineData("func apply2<F, G>(f: ref/F, g: ref/G, extra: i32 = 0, more: i32 = 0) -> i32 => 0\n", "func (k: i32) -> i32 => k + 1")]
    [InlineData("", "func (k) => k + 1")]
    public void AWaitingArgumentNeverLetsAnUnknownProofFallBack(string other, string waiting)
    {
        var source = "func apply2<F, G>(f: ref/F, g: ref/G, extra: i32 = 0) -> i32\n    F is Callable<() -> ref/i32>\n    G is Callable<(i32) -> i32>\n    let r = f()\n    return g(r@follow)\n" + other +
            "public func main() -> ()\n    let n = 7\n    let f = func [n] () => n@ref\n    let v = apply2(f@ref, " + waiting + ")\n    Console.writeLine(\"\\(v)\")\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenConstraint_Kd), "f@ref"), (error.Code, Text(source, error.Span)));
        Assert.Contains("hidden environment receiver", error.Note, StringComparison.Ordinal);
    }

    // SPEC 10.5: any other Unknown clause that no waiting argument leaves open blocks the call the same way (it selected the other
    // candidate and printed 0); without Callable facts the record is the fact-less UnprovenConstraint_Kd at the call (STATUS limit).
    [Fact]
    public void AWaitingArgumentNeverLetsAnUnknownOwnedProofFallBack()
    {
        var source = "func apply<T, G>(v: T, g: ref/G, extra: i32 = 0) -> i32\n    T is Owned\n    G is Callable<(i32) -> i32>\n    return g(1)\nfunc apply<T, G>(v: T, g: ref/G) -> i32 => 0\n" +
            "func caller(p: ref/i32) -> i32 => apply(p, func (k: i32) -> i32 => k + 1)\npublic func main() => ()\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenConstraint_Kd), "apply(p, func (k: i32) -> i32 => k + 1)"), (error.Code, Text(source, error.Span)));
    }

    // SPEC 8.6, 10.7: a structural mismatch is still a Refuted proof, so the candidate does not apply.
    [Fact]
    public void AStructuralMismatchStaysInapplicable()
    {
        var source = Apply + "public func main() -> ()\n    let f = func () => 7\n    let v = apply(f@ref)\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.NoApplicableOverload_Kd), "apply(f@ref)"), (error.Code, Text(source, error.Span)));
    }

    // SPEC 23.3.6: the record and its related locations reach the CLI, the language server and JSON.
    [Fact]
    public void EveryOutputCarriesTheCallableRecord()
    {
        var source = Apply + "public func main() -> ()\n    let n = 7\n    let f = func [n] () => n@ref\n    let v = apply(f@ref)\n";
        var check = DiagnosticCorpus.Check(source);
        var record = Assert.Single(check.Diagnostics);
        var result = new DiagnosticResult(check.Diagnostics, check.Sources);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains("UnprovenConstraint_Kd", console.Text, StringComparison.Ordinal);
        Assert.Contains(" = constraint: ", console.Text, StringComparison.Ordinal);
        Assert.Contains("the closure's call receiver outlives static", console.Text, StringComparison.Ordinal);

        var identity = SourceIdentity.FromPath(check.Sources[record.Source].Path);
        var plain = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, false)[identity]);
        Assert.Equal((record.Code, record.Display!.Range), (plain.Code, plain.Range));
        var withRelated = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, true)[identity]);
        Assert.Equal(record.Related!.Length, withRelated.RelatedInformation!.Length);

        var json = System.Text.Json.JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Contains("\"constraint\"", json, StringComparison.Ordinal);
        Assert.Equal(result, System.Text.Json.JsonSerializer.Deserialize(json, DiagnosticJsonContext.Default.DiagnosticResult));
    }

    // SPEC 8.6, 10.7: the counterparts whose contracts hold run: a Copied result, and a result over the per-call input.
    [Theory]
    [InlineData("Copied", "func apply<F>(f: ref/F) -> i32\n    F is Callable<() -> i32>\n    return f()\nlet n = 7\nlet f = func [n] () => n\nrequire apply(f@ref) == 7 else => $abort(\"copied\")")]
    [InlineData("PerCallInput", ApplyInput + "let m = 5\nlet f = func (x: ref/i32) => x\nrequire apply(f@ref, m@ref) == 5 else => $abort(\"input\")")]
    [InlineData("AnyBorrowParameter", "func apply<F>(f: ref/F, x: ref/i32) -> i32\n    F is Callable<(ref/i32) -> i32>\n    return f(x)\nfunc pick(x: ref/i32) -> i32 => x@follow\nlet m = 4\nrequire apply(pick, m@ref) == 4 else => $abort(\"parameter\")")]
    [InlineData("Waiting", "func apply2<F, G>(f: ref/F, g: ref/G) -> i32\n    F is Callable<() -> i32>\n    G is Callable<(i32) -> i32>\n    return g(f())\nlet n = 7\nlet f = func [n] () => n\nrequire apply2(f@ref, func (k: i32) -> i32 => k + 1) + apply2(f@ref, func (k) => k + 2) == 17 else => $abort(\"waiting\")")]
    public void ContractsThatHoldRun(string name, string source)
        => ScalarEmissionTest.EmitFixture("CallableConstraintRecord" + name, source, string.Empty);

    // The waiting-argument check runs on every Unknown proof that a waiting argument leaves open; a warm rebind of a valid program that
    // takes it allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmWaitingCallableSelectionAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("func apply2<F, G>(f: ref/F, g: ref/G) -> i32\n    F is Callable<() -> i32>\n    G is Callable<(i32) -> i32>\n    return g(f())\nfunc apply2<F, G>(f: ref/F, g: ref/G, extra: i32 = 0) -> i32\n    F is Callable<() -> i32>\n    G is Callable<(i32) -> i32>\n    return 0\nlet n = 7\nlet f = func [n] () => n\nrequire apply2(f@ref, func (k: i32) -> i32 => k + 1) + apply2(f@ref, func (k) => k + 2) == 17 else => $abort(\"warm\")");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
}
