// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ValueCallBindingTest
{
    [Theory]
    [InlineData("func apply(f: (i32) -> bool) -> bool => f(42)")]
    [InlineData("func apply(f: () -> i32) -> i32 => f()")]
    [InlineData("func apply(f: (()) -> bool) -> bool => f(())")]
    [InlineData("func apply(f: ((i32, bool)) -> bool) -> bool => f((42, true))")]
    [InlineData("func apply(f: (i64, bool) -> i32) -> i32 => (f)(42, true)")]
    [InlineData("func apply<T>(f: (T) -> bool, value: T) -> bool => f(value)")]
    [InlineData("func apply(f: () -> Never) -> i32 => f()")]
    public void BindsPositionalSignature(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var call = Assert.Single(Nodes(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        var plan = Assert.IsType<BoundValueCall>(call.BoundValueCall);
        Assert.Null(call.BoundCall);
        Assert.Same(call.Method, plan.Receiver);
        Assert.Same(call.BoundType, plan.ReturnType);
        Assert.Equal(call.ArgumentNodes.Count, plan.Arguments.Length);
    }

    [Theory]
    [InlineData("func apply(f: (i32) -> bool) -> bool => f()")]
    [InlineData("func apply(f: (i32) -> bool) -> bool => f(1, 2)")]
    [InlineData("func apply(f: (i32) -> bool) -> bool => f(true)")]
    [InlineData("func apply(f: (i8) -> bool) -> bool => f(128)")]
    [InlineData("func apply(f: (i32) -> bool) -> bool => f(value: 1)")]
    [InlineData("func apply(f: (i32) -> bool) -> bool => f<i32>(1)")]
    [InlineData("func apply(f: (i32) -> bool) -> i32 => f(1)")]
    [InlineData("func f(value: i32) -> bool => true\nfunc apply(f: i32) -> bool => f(1)")]
    public void RejectsInvalidValueCalls(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void ReloadAndWarmBindingRetainValuePlan()
    {
        var c = MinimalEmissionTest.Analyze("func apply<T>(f: (T) -> bool, value: T) -> bool => f(value)");
        Assert.True(c.Binding.Result.IsComplete);
        var call = Assert.Single(Nodes(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        var plan = call.BoundValueCall;
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
            Assert.Same(plan, call.BoundValueCall);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.True(restored.Bind().IsComplete);
        Assert.NotNull(Assert.Single(Nodes(restored.Kotonoha.RootKoto).OfType<InvocationKoto>()).BoundValueCall);
    }

    private static IEnumerable<Koto> Nodes(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var descendant in Nodes(child))
            {
                yield return descendant;
            }
        }
    }
}
