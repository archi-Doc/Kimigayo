// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 10.6, 10.8: a required structural slot that neither explicit Type arguments nor evidence bind leaves its candidate applicable on its
// other checks; the selected candidate's slot is UnboundTypeArgument_Kd at the call, naming the slot and relating the declaration and the
// waiting argument whose fixed expected call signature holds it.
public class UnboundCallSlotTest(ITestOutputHelper output)
{
    private const string RunR = "func run<R, F>(action: ref/F) -> R\n    F is Callable<(i32) -> R>\n    return action(1)\n";

    [Theory]
    [InlineData("OmittedParameter", "func run<T, F>(action: ref/F) -> i32\n    F is Callable<(T) -> i32>\n    return 7\npublic func main() -> ()\n    require run(func (n) => 1) == 7 else => $abort(\"c0c\")\n", "run(func (n) => 1)", "T", "func (n) => 1")]
    [InlineData("OmittedResult", RunR + "public func main() -> ()\n    let r = run(func (n) => n + 1)\n", "run(func (n) => n + 1)", "R", "func (n) => n + 1")]
    [InlineData("Overloads", "func g(n: i32) -> i32 => n + 1\nfunc g(n: i64) -> i64 => n + 2\n" + RunR + "public func main() -> ()\n    let r = run(g)\n", "run(g)", "R", "g")]
    [InlineData("GenericReference", "func identity<T>(value: T) -> T => value@move\n" + RunR + "public func main() -> ()\n    let r = run(identity)\n", "run(identity)", "R", "identity")]
    [InlineData("NoEvidence", "func make<T>() -> i32 => 7\npublic func main() -> ()\n    require make() == 7 else => $abort(\"u1\")\n", "make()", "T", null)]
    [InlineData("ContextualCase", "func size<T>(value: Option<T>) -> i32 => 7\npublic func main() -> ()\n    require size(.None) == 7 else => $abort(\"u2\")\n", "size(.None)", "T", null)]
    [InlineData("CommonFunction", "func runOnly<T>(action: (T) -> i32) -> i32\n    return 7\npublic func main() -> ()\n    require runOnly(func (n) => 7) == 7 else => $abort(\"u3\")\n", "runOnly(func (n) => 7)", "T", "func (n) => 7")]
    [InlineData("Result", "func make<T>() -> Option<T> => .None\npublic func main() -> ()\n    let x = make()\n", "make()", "T", null)]
    [InlineData("Constraint", "func make<T>() -> i32\n    T is Equatable\n    return 1\npublic func main() -> ()\n    require make() == 1 else => $abort(\"u7b\")\n", "make()", "T", null)]
    [InlineData("TypeIdentity", "func count<I, E>(items: uniq/I) -> i32\n    I is Iterator\n    I.Item is E\n    return 0\npublic func main() -> ()\n    var values = [1, 2, 3]\n    var it = values.iterate()\n    require count(it@uniq) == 0 else => $abort(\"ident\")\n", "count(it@uniq)", "E", null)]
    [InlineData("NeverElement", "func id<T>(value: T) -> T => value@move\nfunc f() -> i64\n    let a: i64 = 2\n    let x = id((a, (return 1)))\n    return 0\npublic func main() -> () => ()\n", "id((a, (return 1)))", "T", null)]
    public void AnUnsolvedSlotOfTheSelectedCandidateIsTheInferenceBoundary(string name, string source, string call, string slot, string? argument)
    {
        var check = DiagnosticCorpus.Check(source);
        var error = Assert.Single(check.Diagnostics);
        output.WriteLine($"{name}: {error.Note}");
        Assert.Equal((nameof(DiagnosticCode.UnboundTypeArgument_Kd), DiagnosticCategory.Language, call), (error.Code, error.Category, Text(source, error.Span)));
        Assert.Equal($"Generic parameter '{slot}' is not bound", error.Label);
        Assert.Null(error.Repairs);
        var related = error.Related!;
        Assert.Contains(related, static x => x.Role == "declaration");
        Assert.Equal(argument, related.SingleOrDefault(static x => x.Role == "argument") is { } waiting ? Text(source, waiting.Span) : null);
        Assert.Contains(argument is null ? $"binds {slot} (SPEC 10.8)" : "never evidence for an outer slot", error.Note, StringComparison.Ordinal);
    }

    // SPEC 10.8 (DECISIONS BU-2): an expected result Never is ordinary evidence, so it fills the slot that a Never argument does not.
    [Fact]
    public void AnExpectedNeverResultFillsTheSlot()
    {
        var c = MinimalEmissionTest.Analyze("func identity<T>(value: T) -> T => value@move\nfunc stop() -> Never => $abort(\"stop\")\nlet x: Never = identity(stop())");
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), static x => x.CallOf()?.Target.Name == "identity");
        Assert.Same(BoundType.Never, call.CallOf()!.TypeArguments[0]);
    }

    // SPEC 10.8, 3.8 (DECISIONS BU-2, R11): a Never component of a typed argument's Type is ordinary evidence.
    [Fact]
    public void ANeverComponentOfATypedArgumentIsEvidence()
    {
        var c = MinimalEmissionTest.Analyze("func h<T>(x: (T, i64)) -> () => ()\nfunc f(u: (Never, i64)) => h(u@move)");
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), static x => x.CallOf()?.Target.Name == "h");
        Assert.Same(BoundType.Never, call.CallOf()!.TypeArguments[0]);
    }

    // SPEC 10.8, 10.3: the parts of a fit that do not contain the slot are still checked, so a result whose outer structure the expected
    // Type cannot admit makes the candidate inapplicable, as for a nongeneric function.
    [Theory]
    [InlineData("func make<T>() -> Option<T> => .None\n")]
    [InlineData("func make() -> Option<i32> => .None\n")]
    public void TheSlotFreeStructureOfTheResultStillFilters(string declaration)
    {
        var source = declaration + "public func main() -> ()\n    let x: string = make()\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.NoApplicableOverload_Kd), "make()"), (error.Code, Text(source, error.Span)));
    }

    // SPEC 10.5, 23.1.2: the closed parameter Types of S type the body, so its independent problem stays visible; an omitted parameter
    // whose Type S leaves open rests on the call.
    [Fact]
    public void ClosedPartsStillTypeTheBody()
    {
        var source = RunR + "public func main() -> ()\n    let r = run(func (n) => n + missing)\n";
        var diagnostics = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal(2, diagnostics.Length);
        Assert.Equal((nameof(DiagnosticCode.UnboundTypeArgument_Kd), "run(func (n) => n + missing)"), (diagnostics[0].Code, Text(source, diagnostics[0].Span)));
        Assert.Equal((nameof(DiagnosticCode.UnresolvedBinding_Kd), "missing"), (diagnostics[1].Code, Text(source, diagnostics[1].Span)));

        var open = "func run<T, F>(action: ref/F) -> i32\n    F is Callable<(T) -> i32>\n    return 7\npublic func main() -> ()\n    let r = run(func (n) => n + missing)\n";
        Assert.Equal(nameof(DiagnosticCode.UnboundTypeArgument_Kd), Assert.Single(DiagnosticCorpus.Check(open).Diagnostics).Code);
    }

    // SPEC 14.9.2, 23.1.2: the open result is inferred from the body's own sources, so their conflict (the value i32 and a returned bool)
    // is an independent problem reported beside the call's unbound slot.
    [Fact]
    public void AConflictAmongTheBodysOwnSourcesStaysVisible()
    {
        var source = RunR + "public func main() -> ()\n    let r = run(func (n) => if n > 0 => n else => (return true))\n";
        var diagnostics = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal(2, diagnostics.Length);
        Assert.Equal((nameof(DiagnosticCode.UnboundTypeArgument_Kd), "run(func (n) => if n > 0 => n else => (return true))"), (diagnostics[0].Code, Text(source, diagnostics[0].Span)));
        Assert.Equal((nameof(DiagnosticCode.TypeMismatch_Kd), "func (n) => if n > 0 => n else => (return true)"), (diagnostics[1].Code, Text(source, diagnostics[1].Span)));
    }

    // SPEC 10.4, 10.5, 10.8: a candidate with an unsolved slot, or one whose F has no Callable Constraint, is compared unchanged, with the
    // waiting position equal in step 2; the selected candidate's signature types the body, and a candidate that is not selected never
    // needs to supply one.
    [Theory]
    [InlineData("FewerDefaults", "func run<F>(action: ref/F, x: i32) -> i32\n    F is Callable<(i32) -> i32>\n    return action(x)\nfunc run<F>(action: ref/F, x: i32, y: i32 = 0) -> i32\n    return 0\nrequire run(func (n) => n + 1, 1) == 2 else => $abort(\"m2\")")]
    [InlineData("WrittenHeader", "func run<F>(action: ref/F, x: i32, y: i32 = 0) -> i32\n    F is Callable<(i32) -> i32>\n    return action(x)\nfunc run<F>(action: ref/F, x: i32) -> i32\n    return 0\nrequire run(func (n: i32) => n + 1, 1) == 0 else => $abort(\"m2d\")")]
    [InlineData("UnsolvedLoses", "func run<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    return action(1)\nfunc run<T, F>(action: ref/F, extra: i32 = 0) -> i32\n    F is Callable<(T) -> T>\n    return 0\nrequire run(func (n) => n + 1) == 2 else => $abort(\"m4\")")]
    [InlineData("OpenResultLoses", "func run<R, F>(action: ref/F, extra: i32 = 0) -> R\n    F is Callable<(i32) -> R>\n    return action(1)\nfunc run<F>(action: ref/F) -> i64\n    F is Callable<(i64) -> i64>\n    return action(5)\nlet r = run(func (n) => n + 1)\nrequire r == 6 else => $abort(\"m13\")")]
    [InlineData("NongenericWins", "func make<T>() -> i32 => 0\nfunc make(extra: i32 = 0) -> i32 => 1\nrequire make() == 1 else => $abort(\"o1\")")]
    [InlineData("UnjudgedConstraint", "func make<T>() -> i32\n    T is Equatable\n    return 1\nfunc make(extra: i32 = 0) -> i32 => 2\nrequire make() == 2 else => $abort(\"u7\")")]
    [InlineData("Reference", "func g(n: i32) -> i32 => n + 1\nfunc g(n: i64) -> i64 => n + 2\nfunc run<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    return action(1)\nfunc run<T, F>(action: ref/F, extra: i32 = 0) -> i32\n    F is Callable<(T) -> T>\n    return 0\nrequire run(g) == 2 else => $abort(\"m7\")")]
    [InlineData("ContextlessReference", "func g(n: i32) -> i32 => n + 1\nfunc g(n: i64) -> i64 => n + 2\nfunc run<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    return action(1)\nfunc run<F>(action: ref/F, extra: i32 = 0) -> i32\n    return 0\nrequire run(g) == 2 else => $abort(\"m8\")")]
    public void CandidatesWithoutAContextRankBeforeTheBody(string name, string source)
        => ScalarEmissionTest.EmitFixture("PartialContext" + name, source, string.Empty);

    // SPEC 10.5, 10.6, 10.8: the selected candidate supplies only what it has: no signature (the omitted parameter needs an annotation, an
    // overload set is no value), an unsolved slot (the inference-boundary record), or an unused default whose Type holds one.
    [Theory]
    [InlineData("func run<F>(action: ref/F, x: i32, y: i32 = 0) -> i32\n    F is Callable<(i32) -> i32>\n    return action(x)\nfunc run<F>(action: ref/F, x: i32) -> i32\n    return 0\npublic func main() -> ()\n    let r = run(func (n) => n + 1, 1)\n", nameof(DiagnosticCode.UnresolvedBinding_Kd), "n")]
    [InlineData(RunR + "func run<F>(action: ref/F, extra: i32 = 0) -> i32\n    F is Callable<(i64) -> i64>\n    return 0\npublic func main() -> ()\n    let r = run(func (n) => n + 1)\n", nameof(DiagnosticCode.UnboundTypeArgument_Kd), "run(func (n) => n + 1)")]
    [InlineData("func g(n: i32) -> i32 => n + 1\nfunc g(n: i64) -> i64 => n + 2\nfunc run<F>(action: ref/F, extra: i32 = 0) -> i32\n    F is Callable<(i32) -> i32>\n    return action(1)\nfunc run<F>(action: ref/F) -> i32\n    return 0\npublic func main() -> ()\n    let r = run(g)\n", nameof(DiagnosticCode.AmbiguousBinding_Kd), "g")]
    [InlineData("func f<T>(x: i32, y: Option<T> = .None) -> i32 => 1\npublic func main() -> ()\n    let r = f(1)\n", nameof(DiagnosticCode.UnboundTypeArgument_Kd), "f(1)")]
    public void TheSelectedCandidateSuppliesOnlyWhatItHas(string source, string code, string at)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((code, at), (error.Code, Text(source, error.Span)));
    }

    // SPEC 10.8, 10.2.1: a parameter Type that holds an unsolved slot is neither identical to nor a subtype of another Type, so the
    // candidates tie in step 2 even though one uses a default, and a Literal is no evidence; omitted header Types rest on the ambiguity.
    [Theory]
    [InlineData("func size<T>(value: Option<T>) -> i32 => 1\nfunc size(value: Option<i32>, extra: i32 = 0) -> i32 => 2\npublic func main() -> ()\n    let r = size(.None)\n", "size(.None)")]
    [InlineData("func run(action: (i32) -> i32) -> i32 => action(1)\nfunc run<T>(action: (T) -> T, extra: i32 = 0) -> i32 => 0\npublic func main() -> ()\n    let r = run(func (n) => n + 1)\n", "run(func (n) => n + 1)")]
    [InlineData("func h(value: i32) -> i32 => 1\nfunc h<T>(value: T) -> i32 => 2\npublic func main() -> ()\n    let r = h(1)\n", "h(1)")]
    [InlineData("func g2(value: f64) -> i32 => 1\nfunc g2<T>(value: T) -> i32 => 2\npublic func main() -> ()\n    let r = g2(1.5)\n", "g2(1.5)")]
    public void OpenParameterTypesAreIncomparable(string source, string at)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.AmbiguousBinding_Kd), at), (error.Code, Text(source, error.Span)));
        Assert.Equal(2, error.Related!.Count(static x => x.Role == "candidate"));
    }

    // SPEC 10.5, 23.1.2: the body of the selected candidate's argument keeps its own error without a fallback to another candidate, and
    // a failed selection leaves the omitted header Types derived. SPEC 10.2.1: a check that fails once the defaults apply is the selected
    // candidate's own record: a literal that its default does not fit, or a refuted Constraint.
    [Theory]
    [InlineData("func run<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    return action(1)\nfunc run<T, F>(action: ref/F, extra: i32 = 0) -> i32\n    F is Callable<(T) -> T>\n    return 0\npublic func main() -> ()\n    let r = run(func (n) => n + missing)\n", nameof(DiagnosticCode.UnresolvedBinding_Kd), "missing")]
    [InlineData("func run<F>(action: ref/F, x: i32) -> i32\n    F is Callable<(i32) -> i32>\n    return action(x)\npublic func main() -> ()\n    let r = run(func (n) => n + 1, true)\n", nameof(DiagnosticCode.NoApplicableOverload_Kd), "run(func (n) => n + 1, true)")]
    [InlineData("func run<F>(action: ref/F) -> i32\n    return 0\npublic func main() -> ()\n    let r = run(func (n) => n + 1)\n", nameof(DiagnosticCode.UnresolvedBinding_Kd), "n")]
    [InlineData("func f<T>(x: T) => ()\npublic func main() -> ()\n    f(3000000000)\n", nameof(DiagnosticCode.InvalidNumericLiteral_Kd), "3000000000")]
    [InlineData("func fi<T>(x: T)\n    T is PrimitiveInteger\n    return\npublic func main() -> ()\n    fi(1.5)\n", nameof(DiagnosticCode.NoApplicableOverload_Kd), "fi(1.5)")]
    public void OneRecordPerIndependentProblem(string source, string code, string at)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((code, at), (error.Code, Text(source, error.Span)));
    }

    // SPEC 10.5.1, 10.5.3, 10.5.4 (D4; owner decision 2026-10-18): a nested call or construction whose slot only a waiting callable binds
    // is typed by that callable when it needs no S and otherwise waits for its outer expectation, a call and a construction alike: the
    // typed result is then fitted as a typed argument is, and a waiting one that no expectation types is the outer call's record.
    [Theory]
    [InlineData("takeF(Option.Some(func (v: i64) -> i64 => v))", nameof(DiagnosticCode.NoApplicableOverload_Kd))]
    [InlineData("takeF(wrap(func (v: i64) -> i64 => v))", nameof(DiagnosticCode.NoApplicableOverload_Kd))]
    [InlineData("keep(Option.Some(func (v: i64) -> i64 => v))", null)]
    [InlineData("keep(wrap(func (v: i64) -> i64 => v))", null)]
    [InlineData("takeF(Option.Some(func (v) => v))", null)]
    [InlineData("takeF(wrap(func (v) => v))", null)]
    [InlineData("keep(Option.Some(func (v) => v))", nameof(DiagnosticCode.UnboundTypeArgument_Kd))]
    [InlineData("keep(wrap(func (v) => v))", nameof(DiagnosticCode.UnboundTypeArgument_Kd))]
    public void AWaitingCallableFixesItsSlotWhenItNeedsNoS(string call, string? code)
    {
        var source = "func wrap<T>(value: T) -> Option<T> => Option.Some(value@move)\nfunc takeF(value: Option<(i64) -> i64>) => ()\nfunc keep<T>(value: T) => ()\npublic func main() -> ()\n    " + call + "\n";
        var errors = DiagnosticCorpus.Check(source).Diagnostics.Select(x => (x.Code, Text(source, x.Span))).ToArray();
        Assert.Equal(code is null ? [] : new[] { (code, call) }, errors);
    }

    // SPEC 10.2.1, 23.1.2: every Literal that its default does not fit is its own record, an argument, element or payload alike.
    [Theory]
    [InlineData("func f2<T>(a: T, b: T) => ()\npublic func main() -> ()\n    f2(3000000000, 5000000000)\n", "3000000000", "5000000000")]
    [InlineData("func id<T>(value: T) -> T => value@move\npublic func main() -> ()\n    let x = id((3000000000, 4000000000))\n", "3000000000", "4000000000")]
    [InlineData("enum Two<T>\n    Both(T, T)\npublic func main() -> ()\n    let x = Two.Both(3000000000, 5000000000)\n", "3000000000", "5000000000")]
    public void EveryLiteralItsDefaultDoesNotFitIsItsOwnRecord(string source, string first, string second)
    {
        var errors = DiagnosticCorpus.Check(source).Diagnostics.Select(x => (x.Code, Text(source, x.Span))).ToArray();
        Assert.Equal(new[] { (nameof(DiagnosticCode.InvalidNumericLiteral_Kd), first), (nameof(DiagnosticCode.InvalidNumericLiteral_Kd), second) }, errors);
    }

    // SPEC 23.1: the record and its related locations reach the CLI, the language server and JSON.
    [Fact]
    public void EveryOutputCarriesTheSlotAndTheWaitingArgument()
    {
        var source = RunR + "public func main() -> ()\n    let r = run(func (n) => n + 1)\n";
        var check = DiagnosticCorpus.Check(source);
        var record = Assert.Single(check.Diagnostics);
        var result = new DiagnosticResult(check.Diagnostics, check.Sources);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains("UnboundTypeArgument_Kd", console.Text, StringComparison.Ordinal);
        Assert.Contains("Generic parameter 'R' is not bound", console.Text, StringComparison.Ordinal);
        Assert.Contains(" = argument: ", console.Text, StringComparison.Ordinal);
        Assert.Contains(" = declaration: ", console.Text, StringComparison.Ordinal);

        var identity = SourceIdentity.FromPath(check.Sources[record.Source].Path);
        var plain = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, false)[identity]);
        Assert.Equal((record.Code, record.Display!.Range), (plain.Code, plain.Range));
        var withRelated = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, true)[identity]);
        Assert.Equal(2, withRelated.RelatedInformation!.Length);

        var json = System.Text.Json.JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Contains("\"parameter\"", json, StringComparison.Ordinal);
        Assert.Equal(result, System.Text.Json.JsonSerializer.Deserialize(json, DiagnosticJsonContext.Default.DiagnosticResult));
    }

    // SPEC 10.5, 10.8: the written repairs bind the slot, and the calls run.
    [Theory]
    [InlineData("ExpectedResult", RunR + "let r: i32 = run(func (n) => n + 1)\nrequire r == 2 else => $abort(\"q2\")")]
    [InlineData("WrittenResult", RunR + "let r = run(func (n) -> i32 => n + 1)\nrequire r == 2 else => $abort(\"q3\")")]
    [InlineData("ExpectedOption", "func make<T>() -> Option<T> => .None\nlet x: Option<i32> = make()")]
    [InlineData("Explicit", "func make<T>() -> i32 => 7\nfunc size<T>(value: Option<T>) -> i32 => 7\nfunc runOnly<T>(action: (T) -> i32) -> i32\n    return 7\nfunc run<T, F>(action: ref/F) -> i32\n    F is Callable<(T) -> i32>\n    return 7\nrequire make<i32>() + size<i32>(.None) + runOnly(func (n: i32) => 7) + run(func (n: i32) => 1) == 28 else => $abort(\"v1\")")]
    public void TheWrittenRepairsRun(string name, string source)
        => ScalarEmissionTest.EmitFixture("UnboundSlotCounterpart" + name, source, string.Empty);

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(RunR + "let r: i32 = run(func (n) => n + 1)\nrequire r == 2 else => $abort(\"q2\")")]
    [InlineData("func make<T>() -> Option<T> => .None\nlet x: Option<i32> = make()")]
    [InlineData("func run<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    return action(1)\nfunc run<T, F>(action: ref/F, extra: i32 = 0) -> i32\n    F is Callable<(T) -> T>\n    return 0\nrequire run(func (n) => n + 1) == 2 else => $abort(\"m4\")")]
    [InlineData("func run<R, F>(action: ref/F, extra: i32 = 0) -> R\n    F is Callable<(i32) -> R>\n    return action(1)\nfunc run<F>(action: ref/F) -> i64\n    F is Callable<(i64) -> i64>\n    return action(5)\nlet r = run(func (n) => n + 1)\nrequire r == 6 else => $abort(\"m13\")")]
    public void WarmSlotBindingAllocatesNothing(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
}
