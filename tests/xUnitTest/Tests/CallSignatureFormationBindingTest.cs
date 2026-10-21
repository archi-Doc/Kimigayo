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

    // SPEC 23.1.2: a candidate whose parameter Type failed rests the selection on that failure at every call, whether the call supplies
    // the parameter or omits it for its default, so the call publishes nothing of its own (bu21: an omitted one faulted beside an
    // unsolved slot, was refused beside a waiting argument and was passed over for another overload). The last row is the valid
    // neighbour: `during x` names the Origin of a borrowed `x`, and the default Copies that shared borrow (SPEC 7.2.3, 15.2.1).
    [Theory]
    [InlineData("func take<T>(x: T, y: ref/T during x = x@ref) => ()\n", "take(3)", "InvalidOriginBinding_Kd", "x")]
    [InlineData("func take<T>(x: T, y: ref/T during x = x@ref) => ()\n", "take(a, a@ref)", "InvalidOriginBinding_Kd", "x")]
    [InlineData("func take<T>(x: T, y: ref/T during x = x@ref) => ()\n", "take(a@ref)", "InvalidOriginBinding_Kd", "x")]
    [InlineData("func take(x: i32, y: ref/i32 during x = x@ref) => ()\n", "take(3)", "InvalidOriginBinding_Kd", "x")]
    [InlineData("func take<T>(x: T, y: Missing = 0) => ()\n", "take(3)", "UnresolvedBinding_Kd", "Missing")]
    [InlineData("func take<T>(x: Option<T>, y: Missing = 0) => ()\n", "take(.None)", "UnresolvedBinding_Kd", "Missing")]
    [InlineData("func take<F>(f: F, y: Missing = 0)\n    F is Callable<(i32) -> ()>\n    return\n", "take(func (v: i32) => ())", "UnresolvedBinding_Kd", "Missing")]
    [InlineData("func take<T>(x: T, y: Missing = 0) => ()\nfunc take(x: i32) => ()\n", "take(a)", "UnresolvedBinding_Kd", "Missing")]
    [InlineData("func take<T>(x: T, y: Missing) => ()\nfunc take(x: i32, y: i32) => ()\n", "take(a, 4)", "UnresolvedBinding_Kd", "Missing")]
    [InlineData("func take<T>(x: ref/T, y: ref/T during x = x) => ()\n", "take(a@ref)", null, null)]
    public void FailedParameterTypeHoldsEveryCall(string callee, string call, string? code, string? text)
    {
        var c = MinimalEmissionTest.Analyze(callee + "func call(a: i32) => " + call);
        c.Binding.ReportDiagnostics();
        (string?, string?)[] expected = code is null ? [] : [(code, text)];
        Assert.Equal(expected, TestDiagnostics.Of(c).Select(static x => ((string?)x.Code, x.Text)));
        Assert.Equal(code is null, c.Binding.Result.IsComplete);
        var function = c.Kotonoha.RootKoto.Members.OfType<FunctionKoto>().Concat(c.Kotonoha.GeneratedFunction?.Body?.Items.OfType<FunctionKoto>() ?? []).Single(x => x.Name == "call");
        Assert.Equal(code is null, Assert.IsType<InvocationKoto>(function.ExpressionBody).CallOf() is not null);
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
        Assert.True(c.Bind().IsComplete);
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
