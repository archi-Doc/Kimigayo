// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConditionalPremiseAccessBindingTest
{
    [Theory]
    [InlineData("Hidden")]
    [InlineData("Source.Hidden.Item")]
    [InlineData("([2 of Source.Hidden.Item], i32)")]
    public void PublicConformanceCannotExposeRestrictedPremise(string requirement)
    {
        var c = MinimalEmissionTest.Analyze(Source("public", "public", "internal", "public", requirement));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        Assert.False(Definition(c).IsVerified);
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.False(Definition(restored).IsVerified);
    }

    [Theory]
    [InlineData("public", "internal", "internal", "public", "Hidden", true)]
    [InlineData("internal", "public", "internal", "public", "Hidden", true)]
    [InlineData("public", "public", "public", "public", "Hidden", true)]
    [InlineData("public", "internal", "internal", "public", "Source.Hidden.Item", true)]
    [InlineData("internal", "public", "internal", "public", "Source.Hidden.Item", true)]
    [InlineData("public", "public", "public", "public", "Source.Hidden.Item", true)]
    [InlineData("public", "public", "public", "internal", "Source.Hidden.Item", false)]
    [InlineData("public", "internal", "public", "internal", "Source.Hidden.Item", true)]
    [InlineData("internal", "public", "public", "internal", "Source.Hidden.Item", true)]
    public void PremisesUseTheEffectiveIntersection(string typeAccess, string contractAccess, string hiddenAccess, string sourceAccess, string requirement, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Source(typeAccess, contractAccess, hiddenAccess, sourceAccess, requirement));
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, Definition(c).IsVerified);
        var restored = Reload(c);
        Assert.Equal(valid, restored.Bind().IsComplete);
        Assert.Equal(valid, Definition(restored).IsVerified);
    }

    [Theory]
    [InlineData("Hidden")]
    [InlineData("Source.Hidden.Item")]
    public void NarrowChildCannotHideAnAncestorsPremise(string requirement)
    {
        var source = Source("public", "internal", "internal", "public", requirement).Replace("internal contract C", "public contract Parent\ninternal contract C: Parent", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        Assert.False(Definition(c).IsVerified);
        Assert.False(Definition(c, "Parent").IsVerified);
    }

    [Theory]
    [InlineData("Restricted")]
    [InlineData("([2 of Restricted], i32)")]
    public void RestrictedConcreteRequirementsAlsoReject(string requirement)
    {
        var c = MinimalEmissionTest.Analyze("internal struct Restricted\n" + Source("public", "public", "public", "public", requirement));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        Assert.False(Definition(c).IsVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalEnumPremisesRetainAccessDomains(bool valid)
    {
        var source = Source("public", valid ? "internal" : "public", "internal", "public", "Source.Hidden.Item").Replace("struct S<T>", "enum S<T>\n    A", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, Definition(c).IsVerified);
    }

    [Theory]
    [InlineData("Hidden")]
    [InlineData("Source.Hidden.Item")]
    public void IndependentPathRemainsVerified(string requirement)
    {
        var source = Source("public", "public", "internal", "public", requirement).Replace("public contract C", "public contract D\npublic contract C", StringComparison.Ordinal) + "\n    Self is D when T is Copy";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(Definition(c).IsVerified);
        Assert.True(Definition(c, "D").IsVerified);
        Assert.Equal(BindingState.Resolved, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").BindingState);
    }

    [Theory]
    [InlineData("Hidden")]
    [InlineData("Source.Hidden.Item")]
    public void ReplacingTheRequirementRestoresConformance(string requirement)
    {
        var source = Source("public", "public", "internal", "public", requirement);
        var c = MinimalEmissionTest.Analyze(source);
        var condition = Condition(c);
        var original = condition.Right;
        var donor = MinimalEmissionTest.Analyze(Source("public", "public", "internal", "public", "i32"));
        var replacement = Condition(donor).Right;
        Assert.True(KotoHelper.Replace(condition, original, replacement));
        Assert.True(c.Bind().IsComplete);
        Assert.True(Definition(c).IsVerified);
        Assert.True(KotoHelper.Replace(condition, replacement, original));
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
    }

    [Fact]
    public void WarmConditionalAccessChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source("public", "internal", "internal", "public", "([2 of Source.Hidden.Item], i32)"));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Conditional premise access check failed.");
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

    private static string Source(string typeAccess, string contractAccess, string hiddenAccess, string sourceAccess, string requirement)
        => hiddenAccess + " contract Hidden\n    associate Item\n" + sourceAccess + " struct Source\n    Self is Hidden\n    associate Hidden.Item is i32\n" + contractAccess + " contract C\n" + typeAccess + " struct S<T>\n    Self is C when T is " + requirement;

    private static IsKoto Condition(Compilation c)
        => Assert.IsType<IsKoto>(Assert.IsType<SyntaxFormKoto>(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").Members.OfType<SyntaxFormKoto>().Single().Operands[1]).Operands[0]);

    private static BoundConformance Definition(Compilation c, string contractName = "C")
        => c.Binding.GetConformanceDefinition(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").BoundType!, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == contractName).BoundSymbol!)!;
}
