// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class DefaultBindingTest
{
    [Theory]
    [InlineData("do => 1")]
    [InlineData("loop => exit 1")]
    [InlineData("choice: do => exit to choice: 1")]
    [InlineData("if true => 1 else => 2")]
    public void DefaultControlFlowUsesValueContextAndInternalTargets(string expression)
    {
        var c = MinimalEmissionTest.Analyze("func f(x?: i32 = (" + expression + ")) => ()\nf(3)");
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        var function = (FunctionKoto)Assert.Single(Calls(c)).BoundCall!.Target.Declaration;
        var value = function.Parameters[0].DefaultValue!;
        var flow = c.Ownership.ControlFlow!;
        Assert.True(flow.Nodes.ContainsKey(value));
        Assert.Empty(flow.Issues);
        Assert.Empty(flow.PendingBinding);
        Assert.True(KotoHelper.IsValueContext(value));
        Assert.True(c.Emission.Validate(out var error), error);
    }

    [Theory]
    [InlineData("func outer() -> i32\n    func f(x?: i32 = (return 1)) => ()\n    f(3)\n    return 2", "return")]
    [InlineData("func outer()\n    loop\n        func f(x?: () = (exit)) => ()\n        exit", "exit")]
    [InlineData("func outer()\n    loop\n        func f(x?: () = (continue)) => ()\n        exit", "continue")]
    [InlineData("func outer()\n    outer: loop\n        func f(x?: () = (exit to outer)) => ()\n        exit", "exit")]
    [InlineData("func f(x?: i32 = (if false => return 1 else => 2)) => ()\nf(3)", "return")]
    public void DefaultsCannotTransferOutsideTheirOwnExpression(string source, string keyword)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var function = Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        var jump = Assert.Single(Walk(function.Parameters[0].DefaultValue!).OfType<JumpKoto>());
        Assert.Null(KotoHelper.ResolveTransferTarget(jump));
        var flow = c.Ownership.ControlFlow!;
        Assert.Contains(flow.Issues, x => ReferenceEquals(x.Node, jump) && x.Message.Contains("No valid target for " + keyword, StringComparison.Ordinal));
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void DefaultFlowIsCheckedOnAnUnusedBodylessRequirement()
    {
        var c = MinimalEmissionTest.Analyze("contract C\n    func f(x?: i32 = (return 1))");
        var flow = c.Ownership.ControlFlow!;
        Assert.Contains(flow.Issues, x => x.Node is ReturnKoto);
    }

    [Fact]
    public void NoncompletingDefaultsDoNotChangeTheFunctionBodyCompletion()
    {
        var c = MinimalEmissionTest.Analyze("func f(x?: i32 = (loop => continue)) => ()\nf(3)");
        Assert.True(c.Binding.Result.IsComplete);
        var function = (FunctionKoto)Assert.Single(Calls(c)).BoundCall!.Target.Declaration;
        var flow = c.Ownership.ControlFlow!;
        Assert.Empty(flow.Issues);
        Assert.False(flow.Nodes[function.Parameters[0].DefaultValue!].CanCompleteNormally);
        Assert.True(flow.Nodes[function].CanCompleteNormally);
    }

    [Fact]
    public void WarmDefaultFlowAnalysisReusesStorage()
    {
        var c = MinimalEmissionTest.Analyze("func f(x?: i32 = (if true => 1 else => 2)) => ()\nf(3)");
        Assert.True(c.Binding.Result.IsComplete);
        var flow = c.Ownership.ControlFlow!;
        for (var i = 0; i < 100; i++)
        {
            flow.Reanalyze(c.Kotonoha.RootKoto);
        }

        Assert.Empty(flow.Issues);
        Assert.Equal(0, AllocationMeasurement.Measure(() => flow.Reanalyze(c.Kotonoha.RootKoto)));
    }

    [Fact]
    public void NamedArgumentsKeepSourceOrderBeforeOmittedDefaults()
    {
        var c = MinimalEmissionTest.Analyze("func f(x?: i32, y?: i32 = x, z?: i32 = y) => ()\nf(z: 3, x: 1)");
        Assert.True(c.Binding.Result.IsComplete);
        var call = Assert.Single(Calls(c));
        var plan = call.BoundCall!;
        var function = (FunctionKoto)plan.Target.Declaration;
        Assert.Equal(new[] { 2, 0 }, plan.ArgumentToParameter.ToArray());
        Assert.Same(call.ArgumentNodes[0], plan.ArgumentOperations[0].Source);
        Assert.Same(call.ArgumentNodes[1], plan.ArgumentOperations[1].Source);
        var omitted = Assert.Single(plan.DefaultArguments.ToArray());
        Assert.Same(c.Binding.ParameterSymbol(function, 1), omitted.Parameter);
        Assert.Same(function.Parameters[1].DefaultValue, omitted.Expression);
        Assert.Same(c.Binding.ParameterSymbol(function, 0), omitted.Expression.BoundSymbol);
        Assert.Same(BoundType.I32, omitted.ParameterType);
        Assert.True(c.Emission.Validate(out var error), error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChainedDefaultsRetainDeclarationSlotsEvenBeforeTheDeclaration(bool forward)
    {
        const string Function = "func f(x?: i32, y?: i32 = x, z?: i32 = y) => ()";
        var c = MinimalEmissionTest.Analyze(forward ? "f(1)\n" + Function : Function + "\nf(1)");
        Assert.True(c.Binding.Result.IsComplete);
        var plan = Assert.Single(Calls(c)).BoundCall!;
        var function = (FunctionKoto)plan.Target.Declaration;
        Assert.Equal(2, plan.DefaultArguments.Length);
        for (var i = 0; i < 2; i++)
        {
            Assert.Same(c.Binding.ParameterSymbol(function, i + 1), plan.DefaultArguments[i].Parameter);
            Assert.Same(c.Binding.ParameterSymbol(function, i), plan.DefaultArguments[i].Expression.BoundSymbol);
        }
    }

    [Theory]
    [InlineData("x.f(1)", 1)]
    [InlineData("S.f(1, x)", 2)]
    public void ReceiverPositionDoesNotBecomeAnOmittedDefault(string expression, int explicitCount)
    {
        var c = MinimalEmissionTest.Analyze("struct S\n    public func f(first?: i32, self: ref/Self, last?: i32 = first) => ()\nfunc use(x?: ref/S) => " + expression);
        Assert.True(c.Binding.Result.IsComplete);
        var plan = Assert.Single(Calls(c)).BoundCall!;
        var function = (FunctionKoto)plan.Target.Declaration;
        Assert.Equal(explicitCount, plan.ArgumentOperations.Length);
        Assert.Same(c.Binding.ParameterSymbol(function, 2), Assert.Single(plan.DefaultArguments.ToArray()).Parameter);
    }

    [Fact]
    public void DefaultsUseTheWinningCandidateAndKeepPerCallTypeSubstitutions()
    {
        var c = MinimalEmissionTest.Analyze("func f<T>(x?: T, y?: T = x)\n    T is Copy\n    ()\nfunc f(x?: string, y?: string = x) => ()\nf<i32>(1)\nf<i64>(2)");
        Assert.True(c.Binding.Result.IsComplete);
        var calls = Calls(c).ToArray();
        Assert.Equal(2, calls.Length);
        var first = Assert.Single(calls[0].BoundCall!.DefaultArguments.ToArray());
        var second = Assert.Single(calls[1].BoundCall!.DefaultArguments.ToArray());
        Assert.Same(first.Expression, second.Expression);
        Assert.Same(first.Parameter, second.Parameter);
        Assert.Same(BoundType.I32, first.ParameterType);
        Assert.Equal("i64", second.ParameterType.Name);
        Assert.Same(calls[1].BoundCall!.ArgumentOperations[0].ParameterType, second.ParameterType);
        Assert.Equal(BoundTypeKind.Parameter, first.Expression.BoundType!.Kind);
    }

    [Fact]
    public void DefaultParameterOriginsAreSubstitutedFromExplicitInputs()
    {
        var c = MinimalEmissionTest.Analyze("func f {a}(x?: ref{a}/i32, y?: ref{a}/i32 = x) => ()\nfunc use {b}(x?: ref{b}/i32) => f(x)");
        Assert.True(c.Binding.Result.IsComplete);
        var plan = Assert.Single(Calls(c)).BoundCall!;
        var omitted = Assert.Single(plan.DefaultArguments.ToArray());
        Assert.Same(plan.ArgumentOperations[0].ParameterType!.Origin, omitted.ParameterType.Origin);
        Assert.NotSame(omitted.Expression.BoundType!.Origin, omitted.ParameterType.Origin);
    }

    [Fact]
    public void InheritedTypeFunctionDefaultsKeepTheirDeclaringTypeSubstitution()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base<T>\n    T is Copy\n    public func f(x?: T, y?: T = x) => ()\nstruct D: Base<i32>\nD.f(1)");
        Assert.True(c.Binding.Result.IsComplete);
        var plan = Assert.Single(Calls(c)).BoundCall!;
        Assert.Equal("Base", plan.DeclaringType!.Symbol!.Name);
        Assert.Same(BoundType.I32, Assert.Single(plan.DefaultArguments.ToArray()).ParameterType);
    }

    [Fact]
    public void ChangingTheSelectedDeclarationClearsOldDefaultOperations()
    {
        var c = MinimalEmissionTest.Analyze("func f(x?: i32, y?: i32 = x) => ()\nf(1)");
        var call = Assert.Single(Calls(c));
        var oldPlan = call.BoundCall!;
        var original = (FunctionKoto)oldPlan.Target.Declaration;
        var replacementCompilation = MinimalEmissionTest.Analyze("func f(x?: i32) => ()");
        var replacement = Walk(replacementCompilation.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        var parent = original.Parent!;
        Assert.True(KotoHelper.Replace(parent, original, replacement));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(oldPlan, call.BoundCall);
        Assert.True(call.BoundCall!.DefaultArguments.IsEmpty);
        Assert.True(KotoHelper.Replace(parent, replacement, original));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(original.Parameters[1].DefaultValue, Assert.Single(call.BoundCall!.DefaultArguments.ToArray()).Expression);
    }

    [Fact]
    public void NamedDefaultsRunOnlyForOmittedArguments()
    {
        var valid = MinimalEmissionTest.Analyze("func f(x?: i32, y: i32 = x) => ()\nf(1, y: 2)");
        Assert.True(valid.Binding.Result.IsComplete);
        Assert.True(Assert.Single(Calls(valid)).BoundCall!.DefaultArguments.IsEmpty);
        var omitted = MinimalEmissionTest.Analyze("func f(x?: i32, y: i32 = x) => ()\nf(1)");
        Assert.True(omitted.Binding.Result.IsComplete);
        Assert.Equal(1, Assert.Single(Calls(omitted)).BoundCall!.DefaultArguments.Length);
    }

    [Fact]
    public void RebindingAndReloadRebuildDefaultPlansWithoutRetainingOldExpressions()
    {
        var c = MinimalEmissionTest.Analyze("func f(x?: i32, y?: i32 = x) => ()\nf(1)");
        Assert.True(c.Binding.Result.IsComplete);
        var plan = Assert.Single(Calls(c)).BoundCall!;
        var expression = plan.DefaultArguments[0].Expression;
        Assert.True(c.Bind().IsComplete);
        Assert.Same(plan, Assert.Single(Calls(c)).BoundCall);
        Assert.Same(expression, plan.DefaultArguments[0].Expression);
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(restored);
        Assert.True(restored.Bind().IsComplete);
        var restoredPlan = Assert.Single(Calls(restored)).BoundCall!;
        var restoredFunction = (FunctionKoto)restoredPlan.Target.Declaration;
        Assert.NotSame(expression, restoredPlan.DefaultArguments[0].Expression);
        Assert.Same(restoredFunction.Parameters[1].DefaultValue, restoredPlan.DefaultArguments[0].Expression);
        Assert.Same(restored.Binding.ParameterSymbol(restoredFunction, 1), restoredPlan.DefaultArguments[0].Parameter);
    }

    [Fact]
    public void WarmOmittedDefaultPlansReuseTheirStorage()
    {
        var c = MinimalEmissionTest.Analyze("func f(x?: i32, y?: i32 = x, z?: i32 = y) => ()\nf(1)\nf(z: 3, x: 1)");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Omitted default plan Binding failed.");
            }
        }));
    }

    [Theory]
    [InlineData("func f(x?: i32, y?: i32 = x + 1) => ()")]
    [InlineData("func f(label => x: i32, y?: i32 = x) => ()")]
    [InlineData("func f(x?: i32, y?: i32 = x + 1, z?: i32 = y + x) => ()")]
    [InlineData("func f<T>(x?: T, y?: T = x)\n    T is Copy\n    ()")]
    [InlineData("func f(x?: i32, y?: i32 = x) => ()\nf(1, 2)")]
    [InlineData("func f(x?: i32, y?: i32 = x) => ()\nf(1)")]
    public void DefaultsBindPrecedingParametersInDeclarationScope(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        // This theory checks declaration Binding. Scalar defaults now execute;
        // generic signatures remain outside the executable subset.
        if (source.Contains("<T>", StringComparison.Ordinal))
        {
            Assert.False(c.Emission.Validate(out _));
        }
        else if (source.Contains("\nf(", StringComparison.Ordinal))
        {
            Assert.True(c.Emission.Validate(out var error), error);
        }
        else
        {
            Assert.False(c.Emission.Validate(out _)); // No executable startup expression.
        }
    }

    [Theory]
    [InlineData("func f(x?: i32 = y, y?: i32 = 1) => ()")]
    [InlineData("func f(x?: i32 = x) => ()")]
    [InlineData("func f(x?: i32, y?: string = x) => ()\nf(1, \"supplied\")")]
    [InlineData("func f(y?: i32 = caller) => ()\nlet caller = 7\nf()")]
    [InlineData("func f<T>(x?: T = 1) => ()\nf()")]
    public void EveryDefaultIsCheckedWithoutLaterOrSelfParameters(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void APrecedingParameterWinsOverAContainerMember()
    {
        var c = MinimalEmissionTest.Analyze("group Defaults\n    let x = 7\n    func f(x?: i32, y?: i32 = x) => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var function = Assert.Single(c.Kotonoha.RootKoto.NestedContainers.Single().Members.OfType<FunctionKoto>());
        Assert.Same(c.Binding.ParameterSymbol(function, 0), function.Parameters[1].DefaultValue!.BoundSymbol);
    }

    [Fact]
    public void RebindingAndReloadPreserveDefaultParameterIdentity()
    {
        var c = MinimalEmissionTest.Analyze("func f(x?: i32, y?: i32 = x) => ()");
        var function = Assert.Single(c.Kotonoha.GeneratedFunction!.Body!.ChildNodes.OfType<FunctionKoto>());
        Assert.True(c.Bind().IsComplete);
        Assert.Same(c.Binding.ParameterSymbol(function, 0), function.Parameters[1].DefaultValue!.BoundSymbol);
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(restored);
        Assert.True(restored.Bind().IsComplete);
        var restoredFunction = Assert.Single(kotonoha.GeneratedFunction!.Body!.ChildNodes.OfType<FunctionKoto>());
        Assert.Same(restored.Binding.ParameterSymbol(restoredFunction, 0), restoredFunction.Parameters[1].DefaultValue!.BoundSymbol);
    }

    [Fact]
    public void WarmDefaultBindingAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("func f(x?: i32, y?: i32 = x + 1) => ()");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Default argument Binding failed.");
            }
        }));
    }

    private static IEnumerable<InvocationKoto> Calls(Compilation c) => Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>();

    private static IEnumerable<Koto> Walk(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var nested in Walk(child))
            {
                yield return nested;
            }
        }
    }
}
