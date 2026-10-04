// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Verification;
using Xunit;

namespace XunitTest;

public class ContextualFunctionReferenceTest(ITestOutputHelper output)
{
    private const string Functions = "func choose(value: i32) -> i32 => value + 1\nfunc choose(value: bool) -> bool => value\n";
    private const string Apply = "func apply<T, F>(value: T, action: ref/F) -> T\n    F is Callable<(T) -> T>\n    return action(value@move)\n";

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FixedCallableSignatureSelectsAConcreteItem(bool actionFirst, bool owning)
    {
        var apply = owning ? Apply.Replace("action: ref/F", "action: F", StringComparison.Ordinal).Replace("Callable<(", "Callable<owner, (", StringComparison.Ordinal).Replace("return action(", "return action@move(", StringComparison.Ordinal) : Apply;
        var arguments = actionFirst ? "action: choose, value: 41" : "value: 41, action: choose";
        var source = Functions + apply + "require apply(" + arguments + ") == 42 else => $abort(\"item\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.BoundCall?.Target.Name == "apply");
        Assert.Equal(BoundTypeKind.FunctionItem, call.BoundCall!.TypeArguments[1]!.Kind);
        Assert.All(call.ArgumentNodes, x => Assert.Null(x.ErasedFunctionType));
        ScalarEmissionTest.EmitFixture("ContextualReferenceItem" + actionFirst + owning, source, string.Empty);
    }

    [Fact]
    public void CommonOuterContextSelectsTheReferenceOnce()
    {
        const string Source = Functions + Apply + "func apply<T, F>(value: T, action: ref/F, extra: i32 = 0) -> T\n    F is Callable<(T) -> T>\n    return value@move\n" +
            "require apply(action: choose, value: 41) == 42 else => $abort(\"common\")";
        ScalarEmissionTest.EmitFixture("ContextualReferenceCommon", Source, string.Empty);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void QualifiedReferenceKeepsItsSelectedDeclaration(int parentheses)
    {
        var reference = new string('(', parentheses) + "Tools.choose" + new string(')', parentheses);
        var source = "group Tools\n    public func choose(value: i32) -> i32 => value + 1\n    public func choose(value: bool) -> bool => value\n" + Apply +
            "require apply(41, " + reference + ") == 42 else => $abort(\"qualified\")";
        ScalarEmissionTest.EmitFixture("ContextualReferenceQualified" + parentheses, source, string.Empty);
    }

    [Fact]
    public void AnUnselectedUnsafeOverloadDoesNotRejectTheReference()
    {
        var source = Functions.Replace("func choose(value: bool)", "unsafe func choose(value: bool)", StringComparison.Ordinal) + Apply +
            "require apply(41, choose) == 42 else => $abort(\"safe selection\")";
        ScalarEmissionTest.EmitFixture("ContextualReferenceUnselectedUnsafe", source, string.Empty);
    }

    [Fact]
    public void TheSelectedUnsafeReferenceFailsWithoutFallback()
    {
        var source = Functions.Replace("func choose(value: i32)", "unsafe func choose(value: i32)", StringComparison.Ordinal) + Apply + "let result = apply(41, choose)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("UnsafeFunctionValue_Kd", error.Code);
        Assert.Equal("choose", error.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFailedReferenceSelectionExplainsItsFixedSignature(bool independent)
    {
        var source = Functions.Replace("i32", "i64", StringComparison.Ordinal) + Apply + "let result = apply(41, choose)" +
            (independent ? "\nlet bad: i32 = false" : string.Empty);
        var path = Path.GetFullPath("contextual-reference.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal(independent ? 2 : 1, result.Diagnostics.Length);
        var error = Assert.Single(result.Diagnostics, x => x.Code == "NoApplicableOverload_Kd");
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal("choose", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal("none of 2 candidates applies", error.Label);
        Assert.NotNull(error.Label);
        Assert.Equal("2", Assert.Single(error.Reason!).Value);
        Assert.Equal([0, 1], error.Related!.Select(x => x.Range!.Value.Start.Line));
        Assert.Contains("fixed call signature (i32) -> i32", error.Note, StringComparison.Ordinal);
        Assert.Null(error.Repairs);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            Assert.Equal(result.Diagnostics.Length, sent.Length);
            Assert.Contains(sent, x => x.Message.Contains("fixed call signature (i32) -> i32", StringComparison.Ordinal));
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Fact]
    public void ReferencesAndAnonymousFunctionsShareOneCompletionPass()
    {
        const string Source = Functions + "func combine<T, F, G>(value: T, first: ref/F, second: ref/G) -> T\n    F is Callable<(T) -> T>\n    G is Callable<(T) -> T>\n    return second(first(value@move))\nrequire combine(second: func (x) => x + 1, first: choose, value: 40) == 42 else => $abort(\"mixed\")";
        ScalarEmissionTest.EmitFixture("ContextualReferenceMixed", Source, string.Empty);
    }

    [Fact]
    public void GenericReferenceBindsItsSlotsFromTheCallableContext()
    {
        const string Source = "func choose<T>(value: T) -> T => value@move\n" + Apply + "require apply(41, choose) == 41 else => $abort(\"generic\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.BoundCall?.Target.Name == "apply");
        Assert.Same(BoundType.I32, Assert.Single(call.BoundCall!.TypeArguments[1]!.Components));
        ScalarEmissionTest.EmitFixture("ContextualReferenceGeneric", Source, string.Empty);
    }

    [Fact]
    public void DifferingOuterReferenceContextsRemainExplicitlyUnsupported()
    {
        var c = MinimalEmissionTest.Analyze(Functions + "func select<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    return action(41)\nfunc select<F>(action: ref/F, extra: i32 = 0) -> bool\n    F is Callable<(bool) -> bool>\n    return action(true)\nlet result = select(choose)");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnsupportedBinding_Kd);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void WaitingReferencesSupplyNoOuterResultEvidence()
    {
        var c = MinimalEmissionTest.Analyze(Functions + "func produce<R, F>(action: ref/F) -> R\n    F is Callable<(i32) -> R>\n    return action(41)\nlet result = produce(choose)");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.DoesNotContain(KotoTree.Walk(c.Kotonoha.RootKoto), x => x is IdentifierNameKoto { IdentifierName: "choose", BoundType.Kind: BoundTypeKind.FunctionItem });
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmReferenceSelectionAndEmissionAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.ContextualFunctionReference);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
