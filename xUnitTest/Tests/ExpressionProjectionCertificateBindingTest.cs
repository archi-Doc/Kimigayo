// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ExpressionProjectionCertificateBindingTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateInvalidWitnessRevokesExpressionCertificate(bool runtime)
    {
        var c = MinimalEmissionTest.Analyze(Source("internal", runtime));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        AssertCertificate(c, runtime, false);
        Assert.False(c.Bind().IsComplete);
        AssertCertificate(c, runtime, false);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        AssertCertificate(restored, runtime, false);
    }

    [Theory]
    [InlineData(false, "public", true)]
    [InlineData(true, "public", true)]
    [InlineData(false, "internal", false)]
    [InlineData(true, "internal", false)]
    public void NestedProjectionInputsKeepWitnessValidity(bool runtime, string access, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Source(access, runtime).Replace("<Source.Origin.Item>", "<([2 of Source.Origin.Item], i32)>", StringComparison.Ordinal));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        AssertCertificate(c, runtime, valid);
        var restored = Reload(c);
        Assert.Equal(valid, restored.Bind().IsComplete);
        AssertCertificate(restored, runtime, valid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixingWitnessRestoresCertificate(bool runtime)
    {
        var source = Source("internal", runtime);
        var c = MinimalEmissionTest.Analyze(source);
        AssertCertificate(c, runtime, false);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<FunctionKoto>().Single();
        var original = function.Parameters[1].Type;
        var donor = MinimalEmissionTest.Analyze(source.Replace("x: Local.Hidden.Item", "x: i32", StringComparison.Ordinal));
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<FunctionKoto>().Single().Parameters[1].Type;
        Assert.True(KotoHelper.Replace(function, original, replacement));
        Assert.True(c.Bind().IsComplete);
        AssertCertificate(c, runtime, true);
        Assert.True(KotoHelper.Replace(function, replacement, original));
        Assert.False(c.Bind().IsComplete);
        AssertCertificate(c, runtime, false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentMemberErrorPreservesValidProjection(bool runtime)
    {
        var c = MinimalEmissionTest.Analyze(Source("public", runtime) + "\nstruct Unrelated\n    let field: Missing");
        Assert.False(c.Binding.Result.IsComplete);
        AssertCertificate(c, runtime, true);
    }

    [Fact]
    public void WarmWitnessChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source("public", false));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Projection witness validation failed.");
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

    private static string Source(string access, bool runtime)
        => access + " contract Hidden\n    associate Item\npublic struct Local\n    Self is Hidden\n    associate Hidden.Item is i32\npublic contract Origin\n    associate Item\n    func f(self: ref/Self, x: i32) -> i32\npublic struct Source\n    Self is Origin\n    associate Origin.Item is i32\n    public func f(self: ref/Self, x: Local.Hidden.Item) -> i32 => x\n" + (runtime
            ? "struct Target<T>\ngroup G\n    func call(x: objref/Target<i32>) -> bool => x is Target<Source.Origin.Item>"
            : "group G\n    func take<T>() => ()\n    func call() => take<Source.Origin.Item>()");

    private static void AssertCertificate(Compilation c, bool runtime, bool valid)
    {
        var expression = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "call").ExpressionBody;
        Assert.Equal(valid, runtime ? Assert.IsType<IsKoto>(expression).BoundRuntimeTest is not null : Assert.IsType<InvocationKoto>(expression).BoundCall is not null);
    }
}
