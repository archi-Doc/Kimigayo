// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ProjectedConstraintCallBindingTest
{
    [Theory]
    [InlineData("Source.Origin.Item", "i32")]
    [InlineData("(Source.Origin.Item, i32)", "(i32, i32)")]
    [InlineData("[2 of Source.Origin.Item]", "[2 of i32]")]
    [InlineData("Source.Origin.Item or string", "string")]
    [InlineData("Source.Origin.Item and Copy", "i32")]
    [InlineData("not Source.Origin.Item", "string")]
    public void ConcreteProjectedRequirementAdmitsMatchingArgument(string requirement, string argument)
    {
        var c = MinimalEmissionTest.Analyze(Source(requirement, argument));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        AssertCall(c, true);
        Assert.True(c.Bind().IsComplete);
        AssertCall(c, true);
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
        AssertCall(restored, true);
    }

    [Theory]
    [InlineData("Source.Origin.Item", "string")]
    [InlineData("(Source.Origin.Item, i32)", "(i32, string)")]
    [InlineData("[2 of Source.Origin.Item]", "[3 of i32]")]
    [InlineData("Source.Origin.Item and string", "i32")]
    [InlineData("not Source.Origin.Item", "i32")]
    public void RefutedProjectionConstraintRejectsCall(string requirement, string argument)
    {
        var c = MinimalEmissionTest.Analyze(Source(requirement, argument));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        AssertCall(c, false);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.NoApplicableCandidate);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        AssertCall(restored, false);
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("Source.Origin.Item", true)]
    [InlineData("Copy", false)]
    public void CallerEvidenceIsNormalizedWithoutAssumingCalleeConstraints(string evidence, bool valid)
    {
        var source = Source("Source.Origin.Item", "i32");
        source = source[..source.LastIndexOf("take<i32>()", StringComparison.Ordinal)] + "func caller<U>()\n    U is " + evidence + "\n    take<U>()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        var restored = Reload(c);
        Assert.Equal(valid, restored.Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructedQualifierSubstitutionUsesItsDeclarationEvidence(bool valid)
    {
        var source = Source("Source.Origin.Item", "i32").Replace("struct Source", "struct Source<U>\n    U is Copy", StringComparison.Ordinal).Replace("T is Source.Origin.Item", "T is Source<i32>.Origin.Item", StringComparison.Ordinal);
        if (!valid)
        {
            source = source.Replace("Source<i32>", "Source<string>", StringComparison.Ordinal);
        }

        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        AssertCall(c, valid);
    }

    [Theory]
    [InlineData("public", true)]
    [InlineData("internal", false)]
    public void LateInvalidWitnessCannotMakeNormalizedConstraintUsable(string access, bool valid)
    {
        var source = access + " contract Hidden\n    associate Value\npublic struct Local\n    Self is Hidden\n    associate Hidden.Value is i32\n" + Source("Source.Origin.Item", "i32")
            .Replace("associate Item", "associate Item\n    func f(self: ref/Self, x: i32) -> i32", StringComparison.Ordinal)
            .Replace("associate Origin.Item is i32", "associate Origin.Item is i32\n    public func f(self: ref/Self, x: Local.Hidden.Value) -> i32 => x", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        AssertCall(c, valid);
        var restored = Reload(c);
        Assert.Equal(valid, restored.Bind().IsComplete);
        AssertCall(restored, valid);
    }

    [Fact]
    public void ChangingArgumentRevokesAndRestoresTheRetainedPlan()
    {
        var c = MinimalEmissionTest.Analyze(Source("Source.Origin.Item", "i32"));
        var call = Call(c);
        var plan = call.BoundCall;
        Assert.NotNull(plan);
        var generic = Assert.IsType<GenericsKoto>(call.Method);
        var original = Assert.Single(generic.TypeArguments);
        var donor = MinimalEmissionTest.Analyze(Source("Source.Origin.Item", "string"));
        var replacement = Assert.Single(Assert.IsType<GenericsKoto>(Call(donor).Method).TypeArguments);
        Assert.True(KotoHelper.Replace(generic, original, replacement));
        Assert.False(c.Bind().IsComplete);
        AssertCall(c, false);
        Assert.True(KotoHelper.Replace(generic, replacement, original));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(plan, call.BoundCall);
    }

    [Fact]
    public void WarmProjectedConstraintsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source("([2 of Source.Origin.Item], i32)", "([2 of i32], i32)"));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Projected constraint call failed.");
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

    private static string Source(string requirement, string argument)
        => "public contract Origin\n    associate Item\npublic struct Source\n    Self is Origin\n    associate Origin.Item is i32\nfunc take<T>()\n    T is " + requirement + "\n    ()\ntake<" + argument + ">()";

    private static void AssertCall(Compilation c, bool valid)
        => Assert.Equal(valid, Call(c).BoundCall is not null);

    private static InvocationKoto Call(Compilation c)
        => Assert.IsType<InvocationKoto>(c.Kotonoha.GeneratedFunction!.Body!.Items.Last());
}
