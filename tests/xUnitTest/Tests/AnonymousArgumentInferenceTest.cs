// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class AnonymousArgumentInferenceTest(ITestOutputHelper output)
{
    private const string Consume = "func consume<T, F>(action: F) -> T\n    F is Callable<owner, () -> T>\n    return action@move()\n";

    [Theory]
    [InlineData("Shared", "func [] () -> i32 => 42", "require result == 42 else => $abort(\"value\")", "")]
    [InlineData("Owned", "func [value@move] () -> string => value@move", "Console.writeLine(result)", "owned\n")]
    [InlineData("Parentheses", "(func [] () -> i32 => 42)", "require result == 42 else => $abort(\"value\")", "")]
    public void WrittenSignaturesInferBeforeConcreteClosureAcquisition(string name, string expression, string check, string output)
    {
        var source = Consume + "let value = \"owned\"\nlet result = consume(" + expression + ")\n" + check;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.BoundCall?.Target.Name == "consume");
        Assert.Equal(BoundTypeKind.Closure, call.BoundCall!.TypeArguments[1]!.Kind);
        Assert.Null(call.ArgumentNodes[0].ErasedFunctionType);
        ScalarEmissionTest.EmitFixture("AnonymousArgument" + name, source, output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentEvidenceSuppliesOmittedHeadersInEitherArgumentOrder(bool reversed)
    {
        var parameters = reversed ? "value: T, action: F" : "action: F, value: T";
        var arguments = reversed ? "42, func [] (x) => x" : "func [] (x) => x, 42";
        var source = "func apply<T, F>(" + parameters + ") -> T\n    F is Callable<owner, (T) -> T>\n    return action@move(value@move)\nlet result = apply(" + arguments + ")\nrequire result == 42 else => $abort(\"context\")";
        ScalarEmissionTest.EmitFixture("AnonymousArgumentOrder" + reversed, source, string.Empty);
    }

    [Fact]
    public void AnUnconstrainedClosureSlotKeepsItsOwnInferredResult()
    {
        const string Source = "func identity<F>(value: F) -> F => value@move\nlet action = identity(func [] () => 42)\nrequire action() == 42 else => $abort(\"result\")";
        ScalarEmissionTest.EmitFixture("AnonymousArgumentIdentity", Source, string.Empty);
    }

    [Theory]
    [InlineData("Input", "func [] (x: i32) => x")]
    [InlineData("Result", "func [] (x) -> i32 => x")]
    public void EachWrittenPartSuppliesSignatureEvidence(string name, string expression)
    {
        var source = "func apply<T, F>(action: F, value: T) -> T\n    F is Callable<owner, (T) -> T>\n    return action@move(value@move)\nlet result = apply(" + expression + ", 42)\nrequire result == 42 else => $abort(\"partial\")";
        ScalarEmissionTest.EmitFixture("AnonymousArgumentPartial" + name, source, string.Empty);
    }

    [Fact]
    public void AnInlineConcreteClosureCanBeMaterializedForSharedAcquisition()
    {
        const string Source = "func invoke<T, F>(action: ref/F) -> T\n    F is Callable<() -> T>\n    return action()\nrequire invoke(func [] () -> i32 => 42) == 42 else => $abort(\"borrow\")";
        ScalarEmissionTest.EmitFixture("AnonymousArgumentBorrow", Source, string.Empty);
    }

    [Fact]
    public void AWaitingBodyCannotInferAnotherOuterSlot()
    {
        var c = MinimalEmissionTest.Analyze(Consume + "let result = consume(func [] () => 42)");
        Assert.False(c.Binding.Result.IsComplete);
        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsAnonymous);
        Assert.Null(closure.BoundClosure);
    }

    [Fact]
    public void CompetingCandidatesNeverUseTheBodyToSelectAResult()
    {
        const string Source = "func apply<F>(value: i32, action: F)\n    F is Callable<() -> i32>\n    return\nfunc apply<F>(value: bool, action: F)\n    F is Callable<() -> bool>\n    return\napply(42, func [] () => true)";
        var path = Path.GetFullPath("anonymous-argument.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("TypeMismatch_Kd", error.Code);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal("true", Source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal("expected i32, found bool", error.Label);
        Assert.NotNull(error.Label);
        Assert.Contains(error.Reason!, x => x.Name == "actual" && x.Value == "bool");
        Assert.Contains(error.Reason!, x => x.Name == "expected" && x.Value == "i32");
        Assert.Null(error.Repairs);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains(error.Label, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains(error.Label, sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Fact]
    public void UnresolvedOuterCandidatesDoNotProbeAnonymousBodies()
    {
        const string Source = "func apply<F>(action: F)\n    F is Callable<() -> i32>\n    return\nfunc apply<F>(action: F, value: i32 = 0)\n    F is Callable<() -> i32>\n    return\napply(func [] () => missing)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Equal("UnsupportedBinding_Kd", Assert.Single(TestDiagnostics.Of(c)).Code);
        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsAnonymous);
        Assert.Null(closure.BoundClosure);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmInlineCaptureBindingAndEmissionAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Consume + "let value = \"owned\"\nlet result = consume(func [value@move] () -> string => value@move)\nConsole.writeLine(result)");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
