// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class DefaultBindingTest
{
    [Theory]
    [InlineData("do => 1")]
    [InlineData("loop => exit 1")]
    [InlineData("label choice: do => exit to choice 1")]
    [InlineData("if true => 1 else => 2")]
    public void DefaultControlFlowUsesValueContextAndInternalTargets(string expression)
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32 = (" + expression + ")) => ()\nf(3)");
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        var function = (FunctionKoto)Assert.Single(Calls(c)).CallOf()!.Target.Declaration;
        var value = function.Parameters[0].DefaultValue!;
        Assert.True(Binding.IsValueContext(value));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("func outer() -> i32\n    func f(x: i32 = (return 1)) => ()\n    f(3)\n    return 2", "return")]
    [InlineData("func outer()\n    loop\n        func f(x: () = (exit)) => ()\n        exit", "exit")]
    [InlineData("func outer()\n    loop\n        func f(x: () = (continue)) => ()\n        exit", "continue")]
    [InlineData("func outer()\n    label outer: loop\n        func f(x: () = (exit to outer)) => ()\n        exit", "exit")]
    [InlineData("func f(x: i32 = (if false => return 1 else => 2)) => ()\nf(3)", "return")]
    public void DefaultsCannotTransferOutsideTheirOwnExpression(string source, string keyword)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        var jump = Assert.Single(KotoTree.Walk(function.Parameters[0].DefaultValue!).OfType<JumpKoto>());
        Assert.Null(KotoHelper.ResolveTransferTarget(jump));
        c.Binding.ReportDiagnostics();
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == nameof(DiagnosticCode.InvalidJumpTarget_Kd) && x.Span == jump.Span && x.Message.Contains("No valid target for " + keyword, StringComparison.Ordinal));
        Assert.False(c.Generate(TextWriter.Null));
    }

    // SPEC 7.2.3, 14.1: a default that never completes is valid; HirColumnsTest checks that its completion stays its own.
    [Fact]
    public void NoncompletingDefaultsDoNotChangeTheFunctionBodyCompletion()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32 = (loop => continue)) => ()\nf(3)");
        Assert.True(c.Binding.Result.IsComplete);
        c.ReportAnalysis();
        Assert.Empty(TestDiagnostics.Of(c));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmDefaultFlowAnalysisReusesStorage()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32 = (if true => 1 else => 2)) => ()\nf(3)");
        Assert.True(c.Binding.Result.IsComplete);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.True(valid);
    }

    [Fact]
    public void NamedArgumentsKeepSourceOrderBeforeOmittedDefaults()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32, y: i32 = x, z: i32 = y) => ()\nf(z: 3, x: 1)");
        Assert.True(c.Binding.Result.IsComplete);
        var call = Assert.Single(Calls(c));
        var plan = call.CallOf()!;
        var function = (FunctionKoto)plan.Target.Declaration;
        Assert.Equal(new[] { 2, 0 }, plan.ArgumentToParameter.ToArray());
        Assert.Same(call.ArgumentNodes[0], plan.ArgumentOperations[0].Source);
        Assert.Same(call.ArgumentNodes[1], plan.ArgumentOperations[1].Source);
        var omitted = Assert.Single(plan.DefaultArguments.ToArray());
        Assert.Same(c.Binding.ParameterSymbol(function, 1), omitted.Parameter);
        Assert.Same(function.Parameters[1].DefaultValue, omitted.Expression);
        Assert.Same(c.Binding.ParameterSymbol(function, 0), omitted.Expression.SymbolOf());
        Assert.Same(BoundType.I32, omitted.ParameterType);
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChainedDefaultsRetainDeclarationSlotsEvenBeforeTheDeclaration(bool forward)
    {
        const string Function = "func f(x: i32, y: i32 = x, z: i32 = y) => ()";
        var c = MinimalEmissionTest.Analyze(forward ? "f(1)\n" + Function : Function + "\nf(1)");
        Assert.True(c.Binding.Result.IsComplete);
        var plan = Assert.Single(Calls(c)).CallOf()!;
        var function = (FunctionKoto)plan.Target.Declaration;
        Assert.Equal(2, plan.DefaultArguments.Length);
        for (var i = 0; i < 2; i++)
        {
            Assert.Same(c.Binding.ParameterSymbol(function, i + 1), plan.DefaultArguments[i].Parameter);
            Assert.Same(c.Binding.ParameterSymbol(function, i), plan.DefaultArguments[i].Expression.SymbolOf());
        }
    }

    [Theory]
    [InlineData("x.f(1)", 1)]
    [InlineData("S.f(1, x)", 2)]
    public void ReceiverPositionDoesNotBecomeAnOmittedDefault(string expression, int explicitCount)
    {
        var c = MinimalEmissionTest.Analyze("struct S\n    public func f(first: i32, self: ref/Self, last: i32 = first) => ()\nfunc use(x: ref/S) => " + expression);
        Assert.True(c.Binding.Result.IsComplete);
        var plan = Assert.Single(Calls(c)).CallOf()!;
        var function = (FunctionKoto)plan.Target.Declaration;
        Assert.Equal(explicitCount, plan.ArgumentOperations.Length);
        Assert.Same(c.Binding.ParameterSymbol(function, 2), Assert.Single(plan.DefaultArguments.ToArray()).Parameter);
    }

    [Fact]
    public void DefaultsUseTheWinningCandidateAndKeepPerCallTypeSubstitutions()
    {
        var c = MinimalEmissionTest.Analyze("func f<T>(x: T, y: T = x)\n    T is Copy\n    ()\nfunc f(x: string, y: string = x@move) => ()\nf<i32>(1)\nf<i64>(2)");
        Assert.True(c.Binding.Result.IsComplete);
        var calls = Calls(c).ToArray();
        Assert.Equal(2, calls.Length);
        var first = Assert.Single(calls[0].CallOf()!.DefaultArguments.ToArray());
        var second = Assert.Single(calls[1].CallOf()!.DefaultArguments.ToArray());
        Assert.Same(first.Expression, second.Expression);
        Assert.Same(first.Parameter, second.Parameter);
        Assert.Same(BoundType.I32, first.ParameterType);
        Assert.Equal("i64", second.ParameterType.Name);
        Assert.Same(calls[1].CallOf()!.ArgumentOperations[0].ParameterType, second.ParameterType);
        Assert.Equal(BoundTypeKind.Parameter, first.Expression.TypeOf()!.Kind);
    }

    [Fact]
    public void DefaultParameterOriginsAreSubstitutedFromExplicitInputs()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: ref/i32 during a, y: ref/i32 during a = x) => ()\nfunc use(x: ref/i32 during b) => f(x)");
        Assert.True(c.Binding.Result.IsComplete);
        var plan = Assert.Single(Calls(c)).CallOf()!;
        var omitted = Assert.Single(plan.DefaultArguments.ToArray());
        Assert.Same(plan.ArgumentOperations[0].ParameterType!.Origin, omitted.ParameterType.Origin);
        Assert.NotSame(omitted.Expression.TypeOf()!.Origin, omitted.ParameterType.Origin);
    }

    [Fact]
    public void InheritedTypeFunctionDefaultsKeepTheirDeclaringTypeSubstitution()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base<T>\n    T is Copy\n    public func f(x: T, y: T = x) => ()\nstruct D: Base<i32>\nD.f(1)");
        Assert.True(c.Binding.Result.IsComplete);
        var plan = Assert.Single(Calls(c)).CallOf()!;
        Assert.Equal("Base", plan.DeclaringType!.Symbol!.Name);
        Assert.Same(BoundType.I32, Assert.Single(plan.DefaultArguments.ToArray()).ParameterType);
    }

    [Fact]
    public void ChangingTheSelectedDeclarationClearsOldDefaultOperations()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32, y: i32 = x) => ()\nf(1)");
        var call = Assert.Single(Calls(c));
        var oldPlan = call.CallOf()!;
        var original = (FunctionKoto)oldPlan.Target.Declaration;
        var replacementCompilation = MinimalEmissionTest.Analyze("func f(x: i32) => ()");
        var replacement = KotoTree.Walk(replacementCompilation.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        var parent = original.Parent!;
        Assert.True(KotoHelper.Replace(parent, original, replacement));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(oldPlan, call.CallOf());
        Assert.True(call.CallOf()!.DefaultArguments.IsEmpty);
        Assert.True(KotoHelper.Replace(parent, replacement, original));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(original.Parameters[1].DefaultValue, Assert.Single(call.CallOf()!.DefaultArguments.ToArray()).Expression);
    }

    [Fact]
    public void NamedDefaultsRunOnlyForOmittedArguments()
    {
        var valid = MinimalEmissionTest.Analyze("func f(x: i32 ! y: i32 = x) => ()\nf(1, y: 2)");
        Assert.True(valid.Binding.Result.IsComplete);
        Assert.True(Assert.Single(Calls(valid)).CallOf()!.DefaultArguments.IsEmpty);
        var omitted = MinimalEmissionTest.Analyze("func f(x: i32 ! y: i32 = x) => ()\nf(1)");
        Assert.True(omitted.Binding.Result.IsComplete);
        Assert.Equal(1, Assert.Single(Calls(omitted)).CallOf()!.DefaultArguments.Length);
    }

    [Fact]
    public void RebindingAndReloadRebuildDefaultPlansWithoutRetainingOldExpressions()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32, y: i32 = x) => ()\nf(1)");
        Assert.True(c.Binding.Result.IsComplete);
        var plan = Assert.Single(Calls(c)).CallOf()!;
        var expression = plan.DefaultArguments[0].Expression;
        Assert.True(c.Bind().IsComplete);
        Assert.Same(plan, Assert.Single(Calls(c)).CallOf());
        Assert.Same(expression, plan.DefaultArguments[0].Expression);
        var restored = CompilationTestHelper.Reload(c);
        Assert.True(restored.Bind().IsComplete);
        var restoredPlan = Assert.Single(Calls(restored)).CallOf()!;
        var restoredFunction = (FunctionKoto)restoredPlan.Target.Declaration;
        Assert.NotSame(expression, restoredPlan.DefaultArguments[0].Expression);
        Assert.Same(restoredFunction.Parameters[1].DefaultValue, restoredPlan.DefaultArguments[0].Expression);
        Assert.Same(restored.Binding.ParameterSymbol(restoredFunction, 1), restoredPlan.DefaultArguments[0].Parameter);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmOmittedDefaultPlansReuseTheirStorage()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32, y: i32 = x, z: i32 = y) => ()\nf(1)\nf(z: 3, x: 1)");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Omitted default plan Binding failed.");
            }
        }));
    }

    [Theory]
    [InlineData("func f(x: i32, y: i32 = x + 1) => ()")]
    [InlineData("func f(! label => x: i32, y: i32 = x) => ()")]
    [InlineData("func f(x: i32, y: i32 = x + 1, z: i32 = y + x) => ()")]
    [InlineData("func f<T>(x: T, y: T = x)\n    T is Copy\n    ()")]
    [InlineData("func f(x: i32, y: i32 = x) => ()\nf(1, 2)")]
    [InlineData("func f(x: i32, y: i32 = x) => ()\nf(1)")]
    public void DefaultsBindPrecedingParametersInDeclarationScope(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        // This theory checks declaration Binding. Scalar defaults now execute;
        // generic signatures remain outside the executable subset.
        if (source.Contains("<T>", StringComparison.Ordinal))
        {
            Assert.False(c.Generate(TextWriter.Null));
        }
        else if (source.Contains("\nf(", StringComparison.Ordinal))
        {
            Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        }
        else
        {
            Assert.False(c.Generate(TextWriter.Null)); // No executable startup expression.
        }
    }

    [Theory]
    [InlineData("func f(x: i32 = y, y: i32 = 1) => ()")]
    [InlineData("func f(x: i32 = x) => ()")]
    [InlineData("func f(x: i32, y: string = x) => ()\nf(1, \"supplied\")")]
    [InlineData("func f(y: i32 = caller) => ()\nlet caller = 7\nf()")]
    [InlineData("func f<T>(x: T = 1) => ()\nf()")]
    public void EveryDefaultIsCheckedWithoutLaterOrSelfParameters(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Generate(TextWriter.Null));
    }

    // SPEC 7.2.3 with 15.3.4 (the owner's D-1236 (A), PLAN G1362): a default's content names only the preceding parameters as Origins,
    // in a named function's signature (do_*) or body (do_body_*), a Closure's header (clo_*) or a local's annotation (let_*): a later
    // one, z, is the one record, at z. Its boundaries: the signature itself, a nested signature in the body, and a signature Origin (q) that a later
    // parameter's annotation introduces, visible in any order.
    [Theory]
    [InlineData("do_later", "func f(a: ref/i32, k: i32 = label w: do\n    func g(x: ref/i32 during z) -> i32 => 0\n    exit to w 0\n, z: ref/i32) -> i32 => k", false)]
    [InlineData("do_prec", "func f(a: ref/i32, k: i32 = label w: do\n    func g(x: ref/i32 during a) -> i32 => 0\n    exit to w 0\n, z: ref/i32) -> i32 => k", true)]
    [InlineData("clo_later", "func f(a: ref/i32, k: () -> i32 = func [] () -> i32\n    let c = func [] (x: ref/i32 during z) -> i32 => 0\n    return 0\n, z: ref/i32) -> i32 => k()", false)]
    [InlineData("clo_prec", "func f(a: ref/i32, k: () -> i32 = func [] () -> i32\n    let c = func [] (x: ref/i32 during a) -> i32 => 0\n    return 0\n, z: ref/i32) -> i32 => k()", true)]
    [InlineData("let_later", "func f(a: ref/i32, k: i32 = label w: do\n    let q: ref/i32 during z = a\n    exit to w 0\n, z: ref/i32) -> i32 => k", false)]
    [InlineData("let_prec", "func f(a: ref/i32, k: i32 = label w: do\n    let q: ref/i32 during a = a\n    exit to w 0\n, z: ref/i32) -> i32 => k", true)]
    [InlineData("do_body_later", "func f(a: ref/i32, k: i32 = label w: do\n    func g(x: ref/i32 during a) -> i32\n        let q: ref/i32 during z = x\n        return 0\n    exit to w 0\n, z: ref/i32) -> i32 => k", false)]
    [InlineData("do_body_prec", "func f(a: ref/i32, k: i32 = label w: do\n    func g(x: ref/i32 during a) -> i32\n        let q: ref/i32 during a = x\n        return 0\n    exit to w 0\n, z: ref/i32) -> i32 => k", true)]
    [InlineData("signature", "func f(a: ref/i32 during z, z: ref/i32) -> i32 => 0", true)]
    [InlineData("body", "func f(a: ref/i32, z: ref/i32) -> i32\n    func g(x: ref/i32 during z) -> i32 => 0\n    return 0", true)]
    [InlineData("do_set", "func f(a: ref/i32, k: i32 = label w: do\n    func g(x: ref/i32 during q) -> i32 => 0\n    exit to w 0\n, z: ref/i32 during q) -> i32 => k", true)]
    public void ADefaultNamesOnlyThePrecedingParametersAsOrigins(string row, string source, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, row + "\n" + MinimalEmissionTest.Describe(c));
        if (!valid)
        {
            c.Binding.ReportDiagnostics();
            var span = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f").Parameters[1].DefaultValue!.Span;
            var record = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
            Assert.True(record.Code == nameof(DiagnosticCode.MissingOriginBinding_Kd) && record.Text == "z" && record.Span.Start > span.Start &&
                record.Span.End <= span.End, row + ": " + record);
        }
    }

    [Fact]
    public void APrecedingParameterWinsOverAContainerMember()
    {
        var c = MinimalEmissionTest.Analyze("group Defaults\n    let x = 7\n    func f(x: i32, y: i32 = x) => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var function = Assert.Single(c.Kotonoha.RootKoto.NestedContainers.Single().Members.OfType<FunctionKoto>());
        Assert.Same(c.Binding.ParameterSymbol(function, 0), function.Parameters[1].DefaultValue!.SymbolOf());
    }

    [Fact]
    public void RebindingAndReloadPreserveDefaultParameterIdentity()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32, y: i32 = x) => ()");
        var function = Assert.Single(c.Kotonoha.GeneratedFunction!.Body!.Children().OfType<FunctionKoto>());
        Assert.True(c.Bind().IsComplete);
        Assert.Same(c.Binding.ParameterSymbol(function, 0), function.Parameters[1].DefaultValue!.SymbolOf());
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(TargetProfile.Windows.Target));
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(restored);
        Assert.True(restored.Bind().IsComplete);
        var restoredFunction = Assert.Single(kotonoha.GeneratedFunction!.Body!.Children().OfType<FunctionKoto>());
        Assert.Same(restored.Binding.ParameterSymbol(restoredFunction, 0), restoredFunction.Parameters[1].DefaultValue!.SymbolOf());
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmDefaultBindingAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32, y: i32 = x + 1) => ()");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Default argument Binding failed.");
            }
        }));
    }

    private static IEnumerable<InvocationKoto> Calls(Compilation c) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>();
}
