// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class CallSignatureFormationBindingTest
{
    [Theory]
    [InlineData("Box<string>")]
    [InlineData("raw/Box<string>")]
    [InlineData("(Box<string>, i32)")]
    [InlineData("[2 of Box<string>]")]
    [InlineData("() -> Box<string>")]
    public void InvalidSignatureCannotPublishCall(string type)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ngroup Consumer\n    func take(value: " + type + ") -> " + type + " => value\n    func call(value: " + type + ") => take(value)");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Null(Call(c).CallOf());
        Assert.False(c.Bind().IsComplete);
        Assert.Null(Call(c).CallOf());
        var restored = CompilationTestHelper.Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Null(Call(restored).CallOf());
    }

    [Fact]
    public void InvalidEnclosingDeclarationCannotSupplyCallTarget()
    {
        var c = MinimalEmissionTest.Analyze("#Unknown\ngroup Invalid\n    public func take() => ()\ngroup Consumer\n    func call() => Invalid.take()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Null(Call(c).CallOf());
    }

    [Theory]
    [InlineData("Box<i32>")]
    [InlineData("raw/Box<i32>")]
    [InlineData("(Box<i32>, i32)")]
    public void ValidSignaturesRemainCallable(string type)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ngroup Consumer\n    func take(value: " + type + ") -> " + type + " => value@move\n    func call(value: " + type + ") => take(value@move)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.NotNull(Call(c).CallOf());
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void GenericSignatureUsesItsDefinitionConstraints()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ngroup Consumer\n    func take<U>(value: Box<U>)\n        U is i32\n        ()\n    func call(value: Box<i32>) => take(value@move)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.NotNull(Call(c).CallOf());
    }

    [Fact]
    public void InvalidUnrelatedGroupDoesNotDiscardValidCall()
    {
        var c = MinimalEmissionTest.Analyze("#Unknown\ngroup Invalid\ngroup Consumer\n    func take() => ()\n    func call() => take()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.NotNull(Call(c).CallOf());
    }

    [Fact]
    public void RemovingInvalidContextRestoresSameCallPlan()
    {
        var c = MinimalEmissionTest.Analyze("group Target\n    public func take() => ()\ngroup Consumer\n    func call() => Target.take()");
        Assert.True(c.Binding.Result.IsComplete);
        var call = Call(c);
        var plan = call.CallOf();
        Assert.NotNull(plan);
        c.Kotonoha.AddSource(new SourceDocument("marker.kimi", "#Unknown\ngroup Target"));
        Assert.False(c.Bind().IsComplete);
        Assert.Null(call.CallOf());
        var target = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Target");
        Assert.True(target.RemoveAttribute(Assert.IsType<AttributeKoto>(target.AttributeChain)));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(plan, call.CallOf());
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmSignatureChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ngroup Consumer\n    func take(value: raw/Box<i32>) => ()\n    func call(value: raw/Box<i32>) => take(value)");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Call signature formation failed.");
            }
        }));
    }

    private static InvocationKoto Call(Compilation c)
        => Assert.IsType<InvocationKoto>(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single(x => x.Name == "call").ExpressionBody);
}
