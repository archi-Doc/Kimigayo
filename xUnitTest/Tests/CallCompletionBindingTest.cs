// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class CallCompletionBindingTest
{
    [Theory]
    [InlineData("Source.Hidden.Element", "i32")]
    [InlineData("i32", "Source.Hidden.Element")]
    public void LateApiFailureInvalidatesEarlierCallPlan(string input, string result)
    {
        var c = MinimalEmissionTest.Analyze("contract Hidden\n    associate Element\npublic struct Source\n    Self is Hidden\n    associate Hidden.Element is i32\npublic group Api\n    public func identity(value?: " + input + ") -> " + result + " => value\ngroup Consumer\n    func call() => Api.identity(1)");
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
    public void LateConstraintApiFailureInvalidatesEarlierCallPlan()
    {
        var c = MinimalEmissionTest.Analyze("contract Hidden\npublic struct Source\n    Self is Hidden\npublic group Api\n    public func take<T>()\n        T is Hidden\n        ()\ngroup Consumer\n    func call() => Api.take<Source>()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        Assert.Null(Call(c).BoundCall);
    }

    [Theory]
    [InlineData("public", "public")]
    [InlineData("internal", "internal")]
    public void ValidProjectionApiRetainsCallPlan(string contractAccess, string apiAccess)
    {
        var c = MinimalEmissionTest.Analyze(Source(contractAccess, apiAccess));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.NotNull(Call(c).BoundCall);
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.NotNull(Call(restored).BoundCall);
    }

    [Fact]
    public void UnrelatedApiFailureDoesNotDiscardValidCall()
    {
        var c = MinimalEmissionTest.Analyze(Source("internal", "public").Replace("Api.identity(1)", "take(1)", StringComparison.Ordinal) + "\n    func take(value?: i32) -> i32 => value");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        Assert.NotNull(Call(c).BoundCall);
    }

    [Fact]
    public void CorrectingApiRestoresSameRetainedCallPlan()
    {
        var c = MinimalEmissionTest.Analyze(Source("internal", "public"));
        var target = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api").Members.OfType<FunctionKoto>().Single();
        var original = target.ReturnType!;
        var donor = MinimalEmissionTest.Analyze(Source("internal", "public").Replace("-> Source.Hidden.Element", "-> i32", StringComparison.Ordinal));
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api").Members.OfType<FunctionKoto>().Single().ReturnType!;
        Assert.True(KotoHelper.Replace(target, original, replacement));
        Assert.True(c.Bind().IsComplete);
        var call = Call(c);
        var plan = call.BoundCall;
        Assert.NotNull(plan);
        Assert.True(KotoHelper.Replace(target, replacement, original));
        Assert.False(c.Bind().IsComplete);
        Assert.Null(call.BoundCall);
        Assert.True(KotoHelper.Replace(target, original, replacement));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(plan, call.BoundCall);
    }

    [Fact]
    public void WarmCompletionChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source("public", "public"));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Call completion failed.");
            }
        }));
    }

    private static string Source(string contractAccess, string apiAccess)
        => contractAccess + " contract Hidden\n    associate Element\npublic struct Source\n    Self is Hidden\n    associate Hidden.Element is i32\n" + apiAccess + " group Api\n    public func identity(value?: i32) -> Source.Hidden.Element => value\ngroup Consumer\n    func call() => Api.identity(1)";

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
