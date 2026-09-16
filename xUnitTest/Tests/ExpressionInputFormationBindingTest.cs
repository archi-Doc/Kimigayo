// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ExpressionInputFormationBindingTest
{
    [Theory]
    [InlineData("Source<string>.Origin.Item")]
    [InlineData("(Source<string>.Origin.Item, i32)")]
    [InlineData("[2 of Source<string>.Origin.Item]")]
    public void InvalidExplicitCallInputsCannotCertify(string argument)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "group G\n    func take<T>() => ()\n    func call() => take<" + argument + ">()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Null(Call(c).BoundCall);
        Assert.False(c.Bind().IsComplete);
        Assert.Null(Call(c).BoundCall);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Null(Call(restored).BoundCall);
    }

    [Theory]
    [InlineData("Source<string>.Origin.Item")]
    [InlineData("(Source<string>.Origin.Item, i32)")]
    [InlineData("[2 of Source<string>.Origin.Item]")]
    public void InvalidRuntimeTargetInputsCannotCertify(string argument)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "struct Target<T>\ngroup G\n    func call(x: objref/Target<i32>) -> bool => x is Target<" + argument + ">");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Null(Test(c).BoundRuntimeTest);
        Assert.False(c.Bind().IsComplete);
        Assert.Null(Test(c).BoundRuntimeTest);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Null(Test(restored).BoundRuntimeTest);
    }

    [Theory]
    [InlineData("Source<i32>.Origin.Item")]
    [InlineData("(Source<i32>.Origin.Item, i32)")]
    [InlineData("[2 of Source<i32>.Origin.Item]")]
    [InlineData("() -> Source<i32>.Origin.Item")]
    public void ValidInputsPreserveBothCertificates(string argument)
    {
        var call = MinimalEmissionTest.Analyze(Prefix + "group G\n    func take<T>() => ()\n    func call() => take<" + argument + ">()");
        Assert.True(call.Binding.Result.IsComplete, MinimalEmissionTest.Describe(call, null));
        Assert.NotNull(Call(call).BoundCall);
        var runtime = MinimalEmissionTest.Analyze(Prefix + "struct Target<T>\ngroup G\n    func call(x: objref/Target<i32>) -> bool => x is Target<" + argument + ">");
        Assert.True(runtime.Binding.Result.IsComplete, MinimalEmissionTest.Describe(runtime, null));
        Assert.NotNull(Test(runtime).BoundRuntimeTest);
        var restoredCall = Reload(call);
        Assert.True(restoredCall.Bind().IsComplete);
        Assert.NotNull(Call(restoredCall).BoundCall);
        var restoredRuntime = Reload(runtime);
        Assert.True(restoredRuntime.Bind().IsComplete);
        Assert.NotNull(Test(restoredRuntime).BoundRuntimeTest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DependentInputUsesCallerEvidence(bool runtime)
    {
        var source = runtime
            ? "struct Target<T>\ngroup G\n    func call<U>(x: objref/Target<i32>) -> bool\n        U is i32\n        return x is Target<Source<U>.Origin.Item>"
            : "group G\n    func take<T>() => ()\n    func call<U>()\n        U is i32\n        take<Source<U>.Origin.Item>()";
        var c = MinimalEmissionTest.Analyze(Prefix + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacingInputRevokesAndRestoresCertificate(bool runtime)
    {
        var source = Prefix + (runtime
            ? "struct Target<T>\ngroup G\n    func call(x: objref/Target<i32>) -> bool => x is Target<Source<i32>.Origin.Item>"
            : "group G\n    func take<T>() => ()\n    func call() => take<Source<i32>.Origin.Item>()");
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        var donor = MinimalEmissionTest.Analyze(source.Replace("Source<i32>", "Source<string>", StringComparison.Ordinal));
        var owner = runtime ? (Koto)Test(c) : Assert.IsType<GenericsKoto>(Call(c).Method);
        var original = runtime ? Test(c).Right : Assert.Single(((GenericsKoto)owner).TypeArguments);
        var replacement = runtime ? Test(donor).Right : Assert.Single(Assert.IsType<GenericsKoto>(Call(donor).Method).TypeArguments);
        Assert.True(KotoHelper.Replace(owner, original, replacement));
        Assert.False(c.Bind().IsComplete);
        if (runtime)
        {
            Assert.Null(Test(c).BoundRuntimeTest);
        }
        else
        {
            Assert.Null(Call(c).BoundCall);
        }

        Assert.True(KotoHelper.Replace(owner, replacement, original));
        Assert.True(c.Bind().IsComplete);
        if (runtime)
        {
            Assert.NotNull(Test(c).BoundRuntimeTest);
        }
        else
        {
            Assert.NotNull(Call(c).BoundCall);
        }
    }

    [Fact]
    public void WarmExpressionInputChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "struct Target<T>\ngroup G\n    func take<T>() => ()\n    func call() => take<Source<i32>.Origin.Item>()\n    func check(x: objref/Target<i32>) -> bool => x is Target<Source<i32>.Origin.Item>");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Expression input formation failed.");
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

    private const string Prefix = "contract Origin\n    associate Item\nstruct Source<T>\n    T is i32\n    Self is Origin\n    associate Origin.Item is i32\n";

    private static Koto Expression(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "call").ExpressionBody!;

    private static InvocationKoto Call(Compilation c) => Assert.IsType<InvocationKoto>(Expression(c));

    private static IsKoto Test(Compilation c) => Assert.IsType<IsKoto>(Expression(c));
}
