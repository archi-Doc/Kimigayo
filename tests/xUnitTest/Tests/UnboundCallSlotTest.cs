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
    [InlineData("OmittedParameter", "func run<T, F>(action: ref/F) -> i32\n    F is Callable<(T) -> i32>\n    return 7\npublic func main() -> ()\n    require run(func (n) => 1) == 7 else => $abort(\"c0c\")\n", "run(func (n) => 1)", "T", "func (n) => 1", "Write the anonymous function's parameter Types")]
    [InlineData("OmittedResult", RunR + "public func main() -> ()\n    let r = run(func (n) => n + 1)\n", "run(func (n) => n + 1)", "R", "func (n) => n + 1", "Write the anonymous function's result Type, or annotate the Type of the call's result")]
    [InlineData("Overloads", "func g(n: i32) -> i32 => n + 1\nfunc g(n: i64) -> i64 => n + 2\n" + RunR + "public func main() -> ()\n    let r = run(g)\n", "run(g)", "R", "g", "Bind the function reference to a local whose Function Type is written, or annotate the Type of the call's result")]
    [InlineData("GenericReference", "func identity<T>(value: T) -> T => value@move\n" + RunR + "public func main() -> ()\n    let r = run(identity)\n", "run(identity)", "R", "identity", "Write explicit Type arguments for the function reference, as in identity<Type>, or annotate the Type of the call's result")]
    [InlineData("NoEvidence", "func make<T>() -> i32 => 7\npublic func main() -> ()\n    require make() == 7 else => $abort(\"u1\")\n", "make()", "T", null, "Write explicit Type arguments for make")]
    [InlineData("ContextualCase", "func size<T>(value: Option<T>) -> i32 => 7\npublic func main() -> ()\n    require size(.None) == 7 else => $abort(\"u2\")\n", "size(.None)", "T", null, "Write explicit Type arguments for size")]
    [InlineData("CommonFunction", "func runOnly<T>(action: (T) -> i32) -> i32\n    return 7\npublic func main() -> ()\n    require runOnly(func (n) => 7) == 7 else => $abort(\"u3\")\n", "runOnly(func (n) => 7)", "T", "func (n) => 7", "Write the anonymous function's parameter Types")]
    [InlineData("Result", "func make<T>() -> Option<T> => .None\npublic func main() -> ()\n    let x = make()\n", "make()", "T", null, "Write explicit Type arguments for make, or annotate the Type of the call's result")]
    [InlineData("Constraint", "func make<T>() -> i32\n    T is Equatable\n    return 1\npublic func main() -> ()\n    require make() == 1 else => $abort(\"u7b\")\n", "make()", "T", null, "Write explicit Type arguments for make")]
    [InlineData("TypeIdentity", "func count<I, E>(items: uniq/I) -> i32\n    I is Iterator\n    I.Item is E\n    return 0\npublic func main() -> ()\n    var values = [1, 2, 3]\n    var it = values.iterate()\n    require count(it@uniq) == 0 else => $abort(\"ident\")\n", "count(it@uniq)", "E", null, "Write explicit Type arguments for count")]
    public void AnUnsolvedSlotOfTheSelectedCandidateIsTheInferenceBoundary(string name, string source, string call, string slot, string? argument, string advice)
    {
        var check = DiagnosticCorpus.Check(source);
        var error = Assert.Single(check.Diagnostics);
        output.WriteLine($"{name}: {error.Note}");
        Assert.Equal((nameof(DiagnosticCode.UnboundTypeArgument_Kd), DiagnosticCategory.Language, call), (error.Code, error.Category, Text(source, error.Span)));
        Assert.Equal($"Type parameter '{slot}' is not bound", error.Label);
        Assert.Equal(advice, error.Advice);
        Assert.Null(error.Repairs);
        var related = error.Related!;
        Assert.Contains(related, static x => x.Role == "declaration");
        Assert.Equal(argument, related.SingleOrDefault(static x => x.Role == "argument") is { } waiting ? Text(source, waiting.Span) : null);
        Assert.Contains(argument is null ? $"binds {slot} (SPEC 10.8)" : "never evidence for an outer slot", error.Note, StringComparison.Ordinal);
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

    // SPEC 10.5, 23.3.6.4: the closed parameter Types of S type the body, so its independent problem stays visible; an omitted parameter
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

    // PLAN G10: ranking a candidate with an unsolved slot against others is not implemented yet; such a call stays the fact-less
    // UnprovenConstraint_Kd it was.
    [Fact]
    public void SeveralCandidatesStillBlock()
    {
        var source = "func make<T>() -> i32 => 0\nfunc make(extra: i32 = 0) -> i32 => 1\npublic func main() -> ()\n    require make() == 1 else => $abort(\"o1\")\n";
        Assert.Equal(nameof(DiagnosticCode.UnprovenConstraint_Kd), Assert.Single(DiagnosticCorpus.Check(source).Diagnostics).Code);
    }

    // SPEC 23.3.6: the record and its related locations reach the CLI, the language server and JSON.
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
        Assert.Contains("Type parameter 'R' is not bound", console.Text, StringComparison.Ordinal);
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

    // SPEC 10.5, 10.8: the repairs the Advice names bind the slot, and the calls run.
    [Theory]
    [InlineData("ExpectedResult", RunR + "let r: i32 = run(func (n) => n + 1)\nrequire r == 2 else => $abort(\"q2\")")]
    [InlineData("WrittenResult", RunR + "let r = run(func (n) -> i32 => n + 1)\nrequire r == 2 else => $abort(\"q3\")")]
    [InlineData("ExpectedOption", "func make<T>() -> Option<T> => .None\nlet x: Option<i32> = make()")]
    [InlineData("Explicit", "func make<T>() -> i32 => 7\nfunc size<T>(value: Option<T>) -> i32 => 7\nfunc runOnly<T>(action: (T) -> i32) -> i32\n    return 7\nfunc run<T, F>(action: ref/F) -> i32\n    F is Callable<(T) -> i32>\n    return 7\nrequire make<i32>() + size<i32>(.None) + runOnly(func (n: i32) => 7) + run(func (n: i32) => 1) == 28 else => $abort(\"v1\")")]
    public void TheAdvisedRepairsRun(string name, string source)
        => ScalarEmissionTest.EmitFixture("UnboundSlotCounterpart" + name, source, string.Empty);

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(RunR + "let r: i32 = run(func (n) => n + 1)\nrequire r == 2 else => $abort(\"q2\")")]
    [InlineData("func make<T>() -> Option<T> => .None\nlet x: Option<i32> = make()")]
    public void WarmSlotBindingAllocatesNothing(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
}
