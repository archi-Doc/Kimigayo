// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Verification;
using Xunit;

namespace XunitTest;

// SPEC 10.5: a generic function reference binds its own slots from the fixed expected call signature, and its Function Item
// keeps those bound arguments (SPEC 7.6.4); calls and erasure enter the instance of the bound arguments.
public class GenericFunctionReferenceTest
{
    private const string Identity = "func identity<T>(value: T) -> T => value@move\n";
    private const string Show = "func show(value: i32) -> () => Console.writeLine(\"plain\")\nfunc show<T>(value: T) -> () => Console.writeLine(\"generic\")\n";
    private const string Invoke = "func invoke<F>(action: ref/F) -> ()\n    F is Callable<(i32) -> ()>\n    action(1)\n";
    private const string Apply = "func apply<F>(action: ref/F, value: i32) -> i32\n    F is Callable<(i32) -> i32>\n    return action(value)\n";

    [Theory]
    [InlineData("Erased", Identity + "let b: (i32) -> i32 = identity\nrequire b(2) == 2 else => $abort(\"erased\")", "")]
    [InlineData("Returned", Identity + "func make() -> (i32) -> i32 => identity\nlet f = make()\nrequire f(3) == 3 else => $abort(\"returned\")", "")]
    [InlineData("PreferNongeneric", Show + Invoke + "let a: (i32) -> () = show\na(1)\ninvoke(show)", "plain\nplain\n")]
    [InlineData("OnlyGeneric", Show + "let s: (bool) -> () = show\ns(true)", "generic\n")]
    [InlineData("PerCallOrigin", "func inspect<T>(value: ref/T) -> () => Console.writeLine(\"inspect\")\nlet c: (ref/i32) -> () = inspect\nlet n: i32 = 1\nc(n@ref)", "inspect\n")]
    [InlineData("CallableItem", Identity + Apply + "require apply(identity, 5) == 5 else => $abort(\"callable\")", "")]
    [InlineData("FunctionParameter", Identity + "func run(action: (i32) -> i32, value: i32) -> i32 => action(value)\nrequire run(identity, 6) == 6 else => $abort(\"parameter\")", "")]
    [InlineData("GenericBody", Identity + "func twice<U>(value: U) -> U\n    U is Copy\n    U is Owned\n    let f: (U) -> U = identity\n    return f(f(value))\nrequire twice(7) == 7 else => $abort(\"body\")", "")]
    [InlineData("NonCopyInstance", Identity + "let f: (string) -> string = identity\nlet text = f(\"text\")\nConsole.writeLine(text)", "text\n")]
    [InlineData("Constrained", "func pair<T>(value: T) -> (T, T)\n    T is Copy\n    return (value, value)\nlet f: (i32) -> (i32, i32) = pair\nlet p = f(4)\nrequire p.0 + p.1 == 8 else => $abort(\"constrained\")", "")]
    public void GenericReferencesEnterTheirBoundInstance(string name, string source, string stdout)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("GenericReference" + name, source, stdout);
    }

    [Fact]
    public void TheItemRecordsItsBoundArguments()
    {
        var c = MinimalEmissionTest.Analyze(Identity + Apply + "let r = apply(identity, 5)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.BoundCall?.Target.Name == "apply");
        var item = call.BoundCall!.TypeArguments[0]!;
        Assert.Equal(BoundTypeKind.FunctionItem, item.Kind);
        Assert.Same(BoundType.I32, Assert.Single(item.Components));
        Assert.Equal("function item identity<i32>", Binding.DiagnosticTypeName(item));
    }

    [Theory]
    [InlineData(Identity + "let g: (ref/i32) -> ref/i32 = identity")] // T would hold a per-call Origin.
    [InlineData("func pair<T>(value: T) -> (T, T)\n    T is Copy\n    return (value, value)\nlet f: (string) -> (string, string) = pair")] // T is Copy is refuted.
    [InlineData(Identity + "func once<U>(value: U) -> U\n    let f: (U) -> U = identity\n    return f(value@move)")] // identity<U> is not proven Owned.
    [InlineData(Identity + "let f: (i32, i32) -> i32 = identity")] // Arity differs.
    public void InapplicableGenericReferencesAreTypeMismatches(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), error.Code);
        Assert.True(error.Text is "identity" or "pair", error.ToString() + " " + error.Text); // Located at the reference.
        Assert.Contains(source.Contains("once", StringComparison.Ordinal) ? "Owned bound generic arguments" : "Type parameters are bound from the expected signature", error.Note, StringComparison.Ordinal);
    }

    // SPEC 10.5: explicit Type arguments narrow the candidates to those that take them; one remaining candidate is a value.
    [Theory]
    [InlineData("Value", Show + "let d = show<i32>\nd(3)", "generic\n")]
    [InlineData("Expected", Show + "let a: (i32) -> () = show<i32>\na(4)", "generic\n")]
    [InlineData("Callable", Identity + Apply + "require apply(identity<i32>, 5) == 5 else => $abort(\"callable\")", "")]
    [InlineData("String", Identity + "let f = identity<string>\nConsole.writeLine(f(\"text\"))", "text\n")]
    [InlineData("Qualified", "group Tools\n    public func echo<T>(value: T) -> T => value@move\nlet g = Tools.echo<i32>\nrequire g(6) == 6 else => $abort(\"qualified\")", "")]
    [InlineData("Parenthesized", Identity + "let h: (bool) -> bool = (identity<bool>)\nrequire h(true) else => $abort(\"parenthesized\")", "")]
    public void ExplicitTypeArgumentsSelectTheReference(string name, string source, string stdout)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("GenericReferenceExplicit" + name, source, stdout);
    }

    [Fact]
    public void AnOverloadSetWithoutASignatureIsAmbiguous()
    {
        var c = MinimalEmissionTest.Analyze(Show + "let e = show");
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(nameof(DiagnosticCode.AmbiguousBinding_Kd), error.Code);
        Assert.Equal("show", error.Text);
        Assert.Contains("without a fixed expected call signature", error.Note, StringComparison.Ordinal);
        Assert.Contains("explicit Type arguments", error.Advice, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("let f = identity")]
    [InlineData("let o: Option<i32> = .None\nlet f = (identity)")]
    public void AGenericReferenceWithoutASignatureLeavesItsSlotUnbound(string body)
    {
        var path = Path.GetFullPath("Hello.kimi");
        var c = MinimalEmissionTest.Analyze(Identity + body, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(nameof(DiagnosticCode.UnboundTypeArgument_Kd), error.Code);
        Assert.True(error.Text is "identity" or "(identity)", error.Text);
        Assert.Contains("'T'", error.Label, StringComparison.Ordinal);
        Assert.Contains("identity<i32>", error.Advice, StringComparison.Ordinal);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Equal("declaration", Assert.Single(record.Related!).Role);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("Type parameter 'T' is not bound", console.Text, StringComparison.Ordinal);
        var identity = Kimi.Checking.SourceIdentity.FromPath(path);
        foreach (var capability in new[] { false, true })
        {
            var sent = Assert.Single(Kimi.Lsp.WorkspaceCheck.Place(new(Kimi.Checking.CheckOutcome.Completed, false, Kimi.Checking.TestPresence.No, result), [identity], identity, capability)[identity]);
            Assert.Equal(record.Code, sent.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
        }
    }

    [Theory]
    [InlineData(Identity + "let g = identity<i32, bool>", nameof(DiagnosticCode.NoApplicableOverload_Kd))]
    [InlineData(Show + "let u: (i32) -> () = show<i32, bool>", nameof(DiagnosticCode.NoApplicableOverload_Kd))]
    [InlineData("func pair<T>(value: T) -> (T, T)\n    T is Copy\n    return (value, value)\nlet p = pair<string>", nameof(DiagnosticCode.UnsatisfiedConstraint_Kd))]
    public void ExplicitArgumentsThatNoCandidateTakesAreRejected(string source, string code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Equal(code, Assert.Single(TestDiagnostics.Of(c), x => x.Severity == DiagnosticSeverity.Error).Code);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmGenericReferenceSelectionAndEmissionAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.GenericFunctionReference);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
