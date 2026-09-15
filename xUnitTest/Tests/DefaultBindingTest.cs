// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class DefaultBindingTest
{
    [Theory]
    [InlineData("func f(x: i32, y?: i32 = x + 1) => ()")]
    [InlineData("func f(label => x: i32, y?: i32 = x) => ()")]
    [InlineData("func f(x: i32, y?: i32 = x + 1, z?: i32 = y + x) => ()")]
    [InlineData("func f<T>(x: T, y?: T = x)\n    T is Copy\n    ()")]
    [InlineData("func f(x: i32, y: i32 = x) => ()\nf(1, 2)")]
    [InlineData("func f(x: i32, y?: i32 = x) => ()\nf(1)")]
    public void DefaultsBindPrecedingParametersInDeclarationScope(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        // Runtime pending-slot/default ownership plans remain an explicit M4 gate.
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("func f(x?: i32 = y, y?: i32 = 1) => ()")]
    [InlineData("func f(x?: i32 = x) => ()")]
    [InlineData("func f(x: i32, y?: string = x) => ()\nf(1, \"supplied\")")]
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
        var c = MinimalEmissionTest.Analyze("group Defaults\n    let x = 7\n    func f(x: i32, y?: i32 = x) => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var function = Assert.Single(c.Kotonoha.RootKoto.NestedContainers.Single().Members.OfType<FunctionKoto>());
        Assert.Same(c.Binding.ParameterSymbol(function, 0), function.Parameters[1].DefaultValue!.BoundSymbol);
    }

    [Fact]
    public void RebindingAndReloadPreserveDefaultParameterIdentity()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32, y?: i32 = x) => ()");
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
        var c = MinimalEmissionTest.Analyze("func f(x: i32, y?: i32 = x + 1) => ()");
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
}
