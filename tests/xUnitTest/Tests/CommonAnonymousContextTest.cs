// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class CommonAnonymousContextTest(ITestOutputHelper output)
{
    private const string First = "func apply<F>(action: F) -> i32\n    F is Callable<owner, (i32) -> i32>\n    return action@move(42)\n";
    private const string Second = "func apply<G>(action: G, extra: i32 = 0) -> i32\n    G is Callable<owner, (i32) -> i32>\n    return 0\n";
    private const string Overloads = First + Second;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CommonContextLetsOrdinaryRankingSelectBeforeTheBody(bool reversed, bool borrow)
    {
        var source = (reversed ? Second + First : Overloads) + "require apply(func [] (x) => x) == 42 else => $abort(\"selection\")";
        if (borrow)
        {
            source = source.Replace("action: F", "action: ref/F", StringComparison.Ordinal).Replace("action: G", "action: ref/G", StringComparison.Ordinal)
                .Replace("Callable<owner,", "Callable<", StringComparison.Ordinal).Replace("action@move(", "action(", StringComparison.Ordinal);
        }

        ScalarEmissionTest.EmitFixture("CommonAnonymousDefaults" + reversed + borrow, source, string.Empty);
    }

    [Fact]
    public void NamedArgumentsCompareTheirOwnPositions()
    {
        const string Source = "func apply<F>(value: i32, action: F) -> i32\n    F is Callable<owner, (i32) -> i32>\n    return action@move(value)\n" +
            "func apply<G>(value: i32, action: G, extra: i32 = 0) -> i32\n    G is Callable<owner, (i32) -> i32>\n    return 0\n" +
            "require apply(action: func [] (x) => x, value: 42) == 42 else => $abort(\"named\")";
        ScalarEmissionTest.EmitFixture("CommonAnonymousNamed", Source, string.Empty);
    }

    [Fact]
    public void CapturesKeepConcreteStorageAndCleanup()
    {
        const string Source = Overloads + "struct Item\n    public let value: i32 = 7\n    drop => Console.writeLine(\"drop\")\nlet item = Item.init()\nrequire apply(func [item@move] (x) => x + item.value) == 49 else => $abort(\"capture\")\nConsole.writeLine(\"done\")";
        ScalarEmissionTest.EmitFixture("CommonAnonymousCapture", Source, "drop\ndone\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedBodyFailureKeepsItsOwnCause(bool independent)
    {
        var source = Overloads + "let result = apply(func [] (x) => true)" + (independent ? "\nlet bad: i32 = false" : string.Empty);
        var path = Path.GetFullPath("common-context.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal(independent ? 2 : 1, result.Diagnostics.Length);
        foreach (var error in result.Diagnostics)
        {
            Assert.Equal("TypeMismatch_Kd", error.Code);
            Assert.Equal(DiagnosticCategory.Language, error.Category);
            Assert.Equal(DiagnosticSeverity.Error, error.Severity);
            Assert.Equal("expected i32, found bool", error.Label);
            Assert.Null(error.Repairs);
        }

        Assert.Contains(result.Diagnostics, error => source.Substring(error.Span!.Value.Start, error.Span.Value.Length) == "true");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            Assert.Equal(result.Diagnostics.Length, sent.Length);
            Assert.All(sent, error => Assert.Contains("expected i32, found bool", error.Message, StringComparison.Ordinal));
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Fact]
    public void DifferentContextsNeverProbeBodiesToSelect()
    {
        var c = MinimalEmissionTest.Analyze(First + Second.Replace("(i32) -> i32", "(bool) -> bool", StringComparison.Ordinal) + "let result = apply(func [] (x) => missing)");
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Equal("UnsupportedBinding_Kd", Assert.Single(TestDiagnostics.Of(c)).Code);
        Assert.Null(Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsAnonymous).BoundClosure);
    }

    [Fact]
    public void EqualCandidatesReportAmbiguityBeforeLookingAtTheBody()
    {
        var source = First.Replace("action: F)", "action: F, other: bool = false)", StringComparison.Ordinal) + Second + "let result = apply(func [] (x) => missing)";
        var path = Path.GetFullPath("common-ambiguous.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("AmbiguousBinding_Kd", error.Code);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal("no unique best candidate among 2 candidates", error.Label);
        Assert.NotNull(error.Label);
        Assert.Equal("2", Assert.Single(error.Reason!).Value);
        Assert.Equal([0, 3], error.Related!.Select(x => x.Range!.Value.Start.Line));
        Assert.Null(error.Repairs);
        Assert.Null(Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsAnonymous).BoundClosure);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(error));
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Contains(error.Label, sent.Message, StringComparison.Ordinal);
            Assert.Equal(error.Display!.Range, sent.Range);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Fact]
    public void ReceiverConformanceFailureNeverFallsBackToAnotherCandidate()
    {
        var source = First.Replace("Callable<owner,", "Callable<", StringComparison.Ordinal) + Second +
            "func consume(value: string, result: i32) -> i32 => result\nlet item = \"owned\"\nlet result = apply(func [item@move] (x) => consume(item@move, x))";
        var path = Path.GetFullPath("common-receiver.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("NoApplicableOverload_Kd", error.Code);
        Assert.Equal("none of 1 candidates applies", error.Label);
        Assert.Equal("1", Assert.Single(error.Reason!).Value);
        Assert.Equal(0, Assert.Single(error.Related!).Range!.Value.Start.Line);
        Assert.Contains("closure requires owner; Callable requires ref", error.Note, StringComparison.Ordinal);
        Assert.Contains("Another overload is not selected", error.Note, StringComparison.Ordinal);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Contains("closure requires owner; Callable requires ref", sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmCommonContextBindingAndEmissionAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Overloads + "let result = apply(func [] (x) => x)");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void EachAnonymousArgumentUsesItsOwnCommonSignature()
    {
        const string Source = "func apply<F, G>(first: F, second: G) -> i32\n    F is Callable<owner, (i32) -> i32>\n    G is Callable<owner, (bool) -> bool>\n    require second@move(true) else => $abort(\"second\")\n    return first@move(42)\n" +
            "func apply<F, G>(first: F, second: G, extra: i32 = 0) -> i32\n    F is Callable<owner, (i32) -> i32>\n    G is Callable<owner, (bool) -> bool>\n    return 0\n" +
            "require apply(func [] (x) => x, func [] (x) => x) == 42 else => $abort(\"first\")";
        ScalarEmissionTest.EmitFixture("CommonAnonymousMultiple", Source, string.Empty);
    }
}
