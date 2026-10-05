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

    // SPEC 10.5, 10.8: a written header over per-call inputs is structural evidence at a common Function parameter or a Callable
    // slot. Its own inputs stand for the expected inputs, a slot is solved from their referents (keeping an open region where only a
    // per-call Origin occurs), and the bound closure then fits the expectation after selection by instantiating its per-call inputs.
    [Theory]
    [InlineData("Common", "func call(action: (ref/i32) -> ref/i32, x: ref/i32) -> ref/i32 during x\n    return action(x)\n", "require call(func (n: ref/i32) => n, x@ref)@follow == 5 else => $abort(\"call\")")]
    [InlineData("Callable", "func apply<F>(action: ref/F, x: ref/i32) -> ref/i32 during x\n    F is Callable<(ref/i32) -> ref/i32>\n    return action(x)\n", "require apply(func (n: ref/i32) -> ref/i32 => n, x@ref)@follow == 5 else => $abort(\"apply\")")]
    [InlineData("Exclusive", "func bump<F>(action: ref/F, x: uniq/i32) -> ()\n    F is Callable<(uniq/i32) -> ()>\n    action(x)\n", "var y: i32 = 4\nbump(func (n: uniq/i32) => n@follow += 1, y@uniq)\nrequire y == 5 else => $abort(\"bump\")")]
    [InlineData("SlotFromHeader", "func run<T, F>(action: ref/F) -> i32\n    F is Callable<(ref/T) -> i32>\n    return 7\n", "require run(func (n: ref/i32) -> i32 => n@follow) == 7 else => $abort(\"run\")")]
    [InlineData("GenericCommon", "func run<T>(value: ref/T, action: (ref/T) -> i32) -> i32\n    return action(value)\n", "require run(x@ref, func (n: ref/i32) => n@follow) == 5 else => $abort(\"run\")")]
    [InlineData("TwoInputs", "func both<F>(action: ref/F, p: ref/i32, q: ref/i32) -> i32\n    F is Callable<(ref/i32, ref/i32) -> ref/i32>\n    return action(p, q)@follow\n", "let w: i32 = 6\nrequire both(func (a: ref/i32, b: ref/i32) -> ref/i32 => a, x@ref, w@ref) == 5 else => $abort(\"both\")")]
    [InlineData("SlotStructure", "func run<T, F>(action: ref/F) -> i32\n    F is Callable<(T) -> ()>\n    return 7\n", "require run(func (n: ref/i32) => ()) == 7 else => $abort(\"slot\")")]
    [InlineData("FixedInput", "", "let r: ref/i32 = x@ref\nlet g: (ref/i32 during r) -> i32 = func (n: ref/i32) => n@follow\nrequire g(r) == 5 else => $abort(\"fixed\")")]
    public void WrittenPerCallHeadersAreSignatureEvidence(string name, string declarations, string use)
        => ScalarEmissionTest.EmitFixture("AnonymousArgumentPerCall" + name, declarations + "let x: i32 = 5\n" + use, string.Empty);

    // SPEC 10.8: a header whose layers differ from the expected ones gives no evidence, and the candidate does not apply. A per-call
    // Origin of the header leaves the slot's Origin open instead (the SlotStructure row above).
    [Fact]
    public void AHeaderMustMatchTheExpectedLayers()
    {
        var c = MinimalEmissionTest.Analyze("func run<T, F>(action: ref/F) -> i32\n    F is Callable<(ref/T) -> i32>\n    return 7\nlet r = run(func (n: i32) => n)");
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal((nameof(DiagnosticCode.NoApplicableOverload_Kd), "run(func (n: i32) => n)"), (error.Code, error.Text));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmPerCallHeaderBindingAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("func apply<F>(action: ref/F, x: ref/i32) -> ref/i32 during x\n    F is Callable<(ref/i32) -> ref/i32>\n    return action(x)\nlet x: i32 = 5\nrequire apply(func (n: ref/i32) => n, x@ref)@follow == 5 else => $abort(\"apply\")");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 10.8: the body is checked with the closed parts of S, and its inferred i32 result never binds T; the call is the
    // inference-boundary record of the SPEC 10.8 `consume` example.
    [Fact]
    public void AWaitingBodyCannotInferAnotherOuterSlot()
    {
        var c = MinimalEmissionTest.Analyze(Consume + "let result = consume(func [] () => 42)");
        Assert.False(c.Binding.Result.IsComplete);
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.Method is IdentifierNameKoto { IdentifierName: "consume" });
        Assert.Null(call.BoundCall);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal((nameof(DiagnosticCode.UnboundTypeArgument_Kd), "Write the anonymous function's result Type, or annotate the Type of the call's result"), (error.Code, error.Advice));
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
    public void SelectedOuterCandidateChecksItsBodyOnce()
    {
        const string Source = "func apply<F>(action: F)\n    F is Callable<() -> i32>\n    return\nfunc apply<F>(action: F, value: i32 = 0)\n    F is Callable<() -> i32>\n    return\napply(func [] () => missing)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("UnresolvedBinding_Kd", error.Code);
        Assert.Equal("missing", error.Text);
        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsAnonymous);
        Assert.NotNull(closure.BoundClosure);
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
