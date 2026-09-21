// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class CallTypeCompletionBindingTest
{
    [Theory]
    [InlineData("E<Source>")]
    [InlineData("unsafe/E<Source>")]
    public void LateInvalidGenericArgumentCannotRemainCallable(string type)
    {
        var c = MinimalEmissionTest.Analyze("contract Hidden\npublic struct Source\n    Self is Hidden\npublic enum E<T>\n    T is Hidden\n    A\ngroup Consumer\n    func take<T>() => ()\n    func call() => take<" + type + ">()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        Assert.Null(Call(c).BoundCall);
        Assert.False(c.Bind().IsComplete);
        Assert.Null(Call(c).BoundCall);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Null(Call(restored).BoundCall);
    }

    [Fact]
    public void InvalidConstructedReceiverCannotRemainCallable()
    {
        var c = MinimalEmissionTest.Analyze("struct Owner<T>\n    T is i32\n    public func take(self: ref/Self) => ()\ngroup Consumer\n    func call(value: ref/Owner<string>) => value.take()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Null(Call(c).BoundCall);
    }

    [Theory]
    [InlineData("E<Source>")]
    [InlineData("unsafe/E<Source>")]
    [InlineData("(E<Source>, i32)")]
    public void LateInvalidSignatureTypeCannotRemainCallable(string type)
    {
        var c = MinimalEmissionTest.Analyze(Prefix("internal") + "group Consumer\n    func take(value: " + type + ") -> " + type + " => value\n    func call(value: " + type + ") => take(value)");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        Assert.Null(Call(c).BoundCall);
    }

    [Theory]
    [InlineData("E<Source>")]
    [InlineData("unsafe/E<Source>")]
    [InlineData("(E<Source>, i32)")]
    public void ValidInstantiatedTypesRetainCallPlan(string type)
    {
        var c = MinimalEmissionTest.Analyze(Prefix("public") + "group Consumer\n    func take<T>(value: T) -> T => value\n    func call(value: " + type + ") => take(value)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.NotNull(Call(c).BoundCall);
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.NotNull(Call(restored).BoundCall);
    }

    [Theory]
    [InlineData("i32", "")]
    [InlineData("U", "<U>")]
    public void ValidConstructedReceiverRemainsCallable(string type, string generic)
    {
        var constraint = generic.Length == 0 ? string.Empty : "\n        U is i32";
        var body = generic.Length == 0 ? " => value.take()" : constraint + "\n        value.take()";
        var c = MinimalEmissionTest.Analyze("struct Owner<T>\n    T is i32\n    public func take(self: ref/Self) => ()\ngroup Consumer\n    func call" + generic + "(value: ref/Owner<" + type + ">)" + body);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void CorrectingArgumentRestoresRetainedPlan()
    {
        var c = MinimalEmissionTest.Analyze(Prefix("internal") + "group Consumer\n    func take<T>() => ()\n    func call() => take<i32>()");
        Assert.False(c.Binding.Result.IsComplete);
        var call = Call(c);
        var plan = call.BoundCall;
        Assert.NotNull(plan);
        var generic = Assert.IsType<GenericsKoto>(call.Method);
        var original = generic.TypeArguments.Single();
        var donor = MinimalEmissionTest.Analyze(Prefix("internal") + "group Consumer\n    func take<T>() => ()\n    func call() => take<E<Source>>()");
        var replacement = Assert.IsType<GenericsKoto>(Call(donor).Method).TypeArguments.Single();
        Assert.True(KotoHelper.Replace(generic, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.Null(call.BoundCall);
        Assert.True(KotoHelper.Replace(generic, replacement, original));
        Assert.False(c.Bind().IsComplete); // The unrelated public E constraint still fails API validation.
        Assert.Same(plan, call.BoundCall);
    }

    [Fact]
    public void WarmInstantiatedCallChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Prefix("public") + "group Consumer\n    func take<T>(value: T) -> T => value\n    func call(value: unsafe/E<Source>) => take(value)");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Call Type completion failed.");
            }
        }));
    }

    private static string Prefix(string access)
        => access + " contract Hidden\npublic struct Source\n    Self is Hidden\npublic enum E<T>\n    T is Hidden\n    A\n";

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
