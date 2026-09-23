// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class GenericCallFormationBindingTest
{
    [Theory]
    [InlineData("Box<string>")]
    [InlineData("(i32, Box<string>)")]
    [InlineData("[2 of Box<string>]")]
    [InlineData("unsafe/Box<string>")]
    [InlineData("() -> Box<string>")]
    public void InvalidExplicitArgumentCannotPublishCall(string argument)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ngroup Consumer\n    func take<T>() => ()\n    func call() => take<" + argument + ">()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnsatisfiedConstraint_Kd);
        Assert.Null(Call(c).BoundCall);
        Assert.False(c.Bind().IsComplete);
        Assert.Null(Call(c).BoundCall);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Null(Call(restored).BoundCall);
    }

    [Fact]
    public void InvalidInferredArgumentCannotPublishCall()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ngroup Consumer\n    func take<T>(value: T) => ()\n    func call(value: Box<string>) => take(value)");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Null(Call(c).BoundCall);
    }

    [Theory]
    [InlineData("Box<i32>")]
    [InlineData("(i32, Box<i32>)")]
    [InlineData("[2 of Box<i32>]")]
    [InlineData("unsafe/Box<i32>")]
    [InlineData("() -> Box<i32>")]
    public void ValidExplicitArgumentsRemainApplicable(string argument)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ngroup Consumer\n    func take<T>() => ()\n    func call() => take<" + argument + ">()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.NotNull(Call(c).BoundCall);
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.NotNull(Call(restored).BoundCall);
    }

    [Theory]
    [InlineData("take<Box<U>>(value@move)")]
    [InlineData("take(value@move)")]
    public void DependentArgumentsUseCallerEvidence(string expression)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is Copy\ngroup Consumer\n    func take<T>(value: T) => ()\n    func call<U>(value: Box<U>)\n        U is Copy\n        " + expression);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void InvalidDeclarationContextCannotSupplyArgument()
    {
        var c = MinimalEmissionTest.Analyze("#Unknown\ngroup Invalid\n    public struct S\ngroup Consumer\n    func take<T>() => ()\n    func call() => take<Invalid.S>()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Null(Call(c).BoundCall);
    }

    [Fact]
    public void ChangingArgumentRevokesAndRestoresSameCallPlan()
    {
        const string source = "struct Box<T>\n    T is i32\ngroup Consumer\n    func take<T>() => ()\n    func call() => take<Box<i32>>()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        var call = Call(c);
        var plan = call.BoundCall;
        Assert.NotNull(plan);
        var generic = Assert.IsType<GenericsKoto>(call.Method);
        var original = Assert.Single(generic.TypeArguments);
        var donor = MinimalEmissionTest.Analyze(source.Replace("Box<i32>", "Box<string>", StringComparison.Ordinal));
        var replacement = Assert.Single(Assert.IsType<GenericsKoto>(Call(donor).Method).TypeArguments);
        Assert.True(KotoHelper.Replace(generic, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.Null(call.BoundCall);
        Assert.True(KotoHelper.Replace(generic, replacement, original));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(plan, call.BoundCall);
    }

    [Fact]
    public void WarmGenericCallChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ngroup Consumer\n    func take<T>() => ()\n    func call() => take<Box<i32>>()");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Generic call formation failed.");
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

    private static InvocationKoto Call(Compilation c)
        => Assert.IsType<InvocationKoto>(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single(x => x.Name == "call").ExpressionBody);
}
