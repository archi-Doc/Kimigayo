// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class EnumGenericConstraintBindingTest
{
    // SPEC 8.6.3, 12.3, 23.1.2 (PLAN G1278; DECISIONS R7-M80 as amended by G1278-A): a nested argument with a refuted clause is judged
    // once, where it is written, so the program stays rejected with one UnsatisfiedConstraint_Kd per written site (the qualified form's
    // qualifier is a second site). A contextual `.Empty` fixes no own slot, so it carries its expected Type and publishes its plan over
    // it (the Middle's C-563: no check reads it on a rejected program); the qualified form's `.Empty` rests on its qualifier's record.
    // Each row: the argument, the expression, the written sites and whether the plan is published.
    [Theory]
    [InlineData("Box<string>", ".Empty", 1, true)]
    [InlineData("Box<string>", "E<Box<string>>.Empty", 2, false)]
    [InlineData("(i32, Box<string>)", ".Empty", 1, true)]
    [InlineData("[2 of Box<string>]", ".Empty", 1, true)]
    [InlineData("raw/Box<string>", ".Empty", 1, true)]
    public void AnInvalidNestedArgumentIsRecordedAtItsWrittenSites(string argument, string expression, int sites, bool published)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\nenum E<T>\n    Empty\ngroup Consumer\n    func make() -> E<" + argument + "> => " + expression);
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(sites, c.Binding.Issues.Count(static x => x.Code == DiagnosticCode.UnsatisfiedConstraint_Kd));
        Assert.Equal(published, c.Binding.TryGetEnumConstruction(Use(c), out _));
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(published, c.Binding.TryGetEnumConstruction(Use(c), out _));
        var restored = CompilationTestHelper.Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Equal(published, restored.Binding.TryGetEnumConstruction(Use(restored), out _));
    }

    [Fact]
    public void InvalidPayloadTypeCannotPublishConstruction()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\nenum E\n    Value(Box<string>)\ngroup Consumer\n    func make(value: Box<string>) -> E => .Value(value)");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Binding.TryGetEnumConstruction(Use(c), out _));
    }

    [Theory]
    [InlineData("i32")]
    [InlineData("(i32, i32)")]
    public void ValidNestedArgumentsStillPublishPlans(string argument)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is " + argument + "\nenum E<T>\n    Empty\ngroup Consumer\n    func make() -> E<Box<" + argument + ">> => .Empty");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Binding.TryGetEnumConstruction(Use(c), out _));
        var restored = CompilationTestHelper.Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.True(restored.Binding.TryGetEnumConstruction(Use(restored), out _));
    }

    [Fact]
    public void DependentArgumentsRetainDefinitionEvidence()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is Copy\nenum E<T>\n    Empty\ngroup Consumer\n    func make<U>() -> E<Box<U>>\n        U is Copy\n        return .Empty");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void RebindingReturnArgumentReusesThePlanAfterRecovery()
    {
        const string source = "struct Box<T>\n    T is i32\nenum E<T>\n    Empty\ngroup Consumer\n    func make() -> E<Box<i32>> => .Empty";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Binding.TryGetEnumConstruction(Use(c), out var initial));
        var function = Function(c);
        var original = function.ReturnType!;
        var replacement = Function(MinimalEmissionTest.Analyze(source.Replace("Box<i32>", "Box<string>", StringComparison.Ordinal))).ReturnType!;
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(function, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.True(c.Binding.TryGetEnumConstruction(Use(c), out _)); // `.Empty` carries the refuted E<Box<string>> (G1278-A, C-563).
        Assert.True(KotoHelper.Replace(function, replacement, original));
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.TryGetEnumConstruction(Use(c), out var restored));
        Assert.Same(initial, restored);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmNestedEnumChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\nenum E<T>\n    Empty\ngroup Consumer\n    func make() -> E<Box<i32>> => .Empty");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Nested enum argument Binding failed.");
            }
        }));
    }

    private static FunctionKoto Function(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single();

    private static Koto Use(Compilation c)
        => Function(c).ExpressionBody!;
}
