// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class PatternTypeFormationBindingTest
{
    [Theory]
    [InlineData("Box<string>", "_")]
    [InlineData("unsafe/Box<string>", "_")]
    [InlineData("Option<Box<string>>", ".Some(_)\n    .None")]
    [InlineData("Option<Box<string>>", ".Some(_)")]
    [InlineData("Box<string>", "_\n    _")]
    [InlineData("Box<string>", "let item")]
    public void InvalidSubjectTypesCannotPublishCoverage(string type, string pattern)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\nfunc f(value?: " + type + ") => match value\n    " + pattern.Replace("\n", " => ()\n", StringComparison.Ordinal) + " => ()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(MatchCoverageState.Invalid, Plan(c).Coverage.State);
        Assert.Empty(c.Binding.PatternWarnings);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.NonExhaustiveMatch);
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(MatchCoverageState.Invalid, Plan(c).Coverage.State);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Equal(MatchCoverageState.Invalid, Plan(restored).Coverage.State);
    }

    [Fact]
    public void InvalidEnumContextCannotPublishWildcardCoverage()
    {
        var c = MinimalEmissionTest.Analyze("#Unknown\ngroup Invalid\n    public enum E\n        A\nfunc f(value?: Invalid.E) => match value\n    _ => ()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(MatchCoverageState.Invalid, Plan(c).Coverage.State);
    }

    [Theory]
    [InlineData("Box<i32>", "_")]
    [InlineData("unsafe/Box<i32>", "_")]
    [InlineData("Option<Box<i32>>", ".Some(_)\n    .None")]
    public void ValidSubjectTypesRetainCoverage(string type, string pattern)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\nfunc f(value?: " + type + ") => match value\n    " + pattern.Replace("\n", " => ()\n", StringComparison.Ordinal) + " => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(MatchCoverageState.Exhaustive, Plan(c).Coverage.State);
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.Equal(MatchCoverageState.Exhaustive, Plan(restored).Coverage.State);
    }

    [Fact]
    public void DependentSubjectUsesDefinitionEvidence()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\nfunc f<U>(value?: Box<U>)\n    U is i32\n    match value\n        _ => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void RemovingInvalidOwnerRestoresRetainedPlan()
    {
        var c = MinimalEmissionTest.Analyze("group Types\n    public enum E\n        A\nfunc f(value?: Types.E) => match value\n    _ => ()");
        Assert.True(c.Binding.Result.IsComplete);
        var plan = Plan(c);
        c.Kotonoha.AddSource(new SourceDocument("marker.kimi", "#Unknown\ngroup Types"));
        Assert.False(c.Bind().IsComplete);
        Assert.Same(plan, Plan(c));
        Assert.Equal(MatchCoverageState.Invalid, plan.Coverage.State);
        var owner = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Types");
        Assert.True(owner.RemoveAttribute(Assert.IsType<AttributeKoto>(owner.AttributeChain)));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(plan, Plan(c));
        Assert.Equal(MatchCoverageState.Exhaustive, plan.Coverage.State);
    }

    [Fact]
    public void LateEnumApiFailureInvalidatesEarlierCoverage()
    {
        var c = MinimalEmissionTest.Analyze("contract Hidden\npublic struct Source\n    Self is Hidden\npublic enum E<T>\n    T is Hidden\n    A\nfunc f(value?: E<Source>) => match value\n    _ => ()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        Assert.Equal(MatchCoverageState.Invalid, Plan(c).Coverage.State);
    }

    [Fact]
    public void UnrelatedInvalidContextPreservesValidPattern()
    {
        var c = MinimalEmissionTest.Analyze("#Unknown\ngroup Invalid\nfunc f(value?: bool) => match value\n    _ => ()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(MatchCoverageState.Exhaustive, Plan(c).Coverage.State);
    }

    [Fact]
    public void WarmPatternFormationChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\nfunc f(value?: Option<Box<i32>>) => match value\n    .Some(_) => ()\n    .None => ()");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Pattern formation failed.");
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

    private static BoundMatch Plan(Compilation c)
    {
        var function = Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        var match = Assert.IsType<MatchKoto>(function.ExpressionBody);
        Assert.True(c.Binding.TryGetMatch(match, out var plan));
        return Assert.IsType<BoundMatch>(plan);
    }

    private static IEnumerable<Koto> Walk(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var nested in Walk(child))
            {
                yield return nested;
            }
        }
    }
}
