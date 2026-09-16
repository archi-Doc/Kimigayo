// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class EnumGenericConstraintBindingTest
{
    [Theory]
    [InlineData("Box<string>", ".Empty")]
    [InlineData("Box<string>", "E<Box<string>>.Empty")]
    [InlineData("(i32, Box<string>)", ".Empty")]
    [InlineData("[2 of Box<string>]", ".Empty")]
    [InlineData("unsafe/Box<string>", ".Empty")]
    public void InvalidNestedArgumentCannotPublishConstruction(string argument, string expression)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\nenum E<T>\n    Empty\ngroup Consumer\n    func make() -> E<" + argument + "> => " + expression);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnsatisfiedConstraint_Kd);
        Assert.False(c.Binding.TryGetEnumConstruction(Use(c), out _));
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Binding.TryGetEnumConstruction(Use(c), out _));
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.False(restored.Binding.TryGetEnumConstruction(Use(restored), out _));
    }

    [Fact]
    public void InvalidPayloadTypeCannotPublishConstruction()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\nenum E\n    Value(Box<string>)\ngroup Consumer\n    func make(value: Box<string>) -> E => .Value(value)");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Binding.TryGetEnumConstruction(Use(c), out _));
    }

    [Theory]
    [InlineData("i32")]
    [InlineData("(i32, i32)")]
    public void ValidNestedArgumentsStillPublishPlans(string argument)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is " + argument + "\nenum E<T>\n    Empty\ngroup Consumer\n    func make() -> E<Box<" + argument + ">> => .Empty");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Binding.TryGetEnumConstruction(Use(c), out _));
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.True(restored.Binding.TryGetEnumConstruction(Use(restored), out _));
    }

    [Fact]
    public void DependentArgumentsRetainDefinitionEvidence()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is Copy\nenum E<T>\n    Empty\ngroup Consumer\n    func make<U>() -> E<Box<U>>\n        U is Copy\n        return .Empty");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Reload(c).Bind().IsComplete);
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
        Assert.True(KotoHelper.Replace(function, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Binding.TryGetEnumConstruction(Use(c), out _));
        Assert.True(KotoHelper.Replace(function, replacement, original));
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.TryGetEnumConstruction(Use(c), out var restored));
        Assert.Same(initial, restored);
    }

    [Fact]
    public void WarmNestedEnumChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\nenum E<T>\n    Empty\ngroup Consumer\n    func make() -> E<Box<i32>> => .Empty");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Nested enum argument Binding failed.");
            }
        }));
    }

    private static Compilation Reload(Compilation c)
    {
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        return restored;
    }

    private static FunctionKoto Function(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single();

    private static Koto Use(Compilation c)
        => Function(c).ExpressionBody!;
}
