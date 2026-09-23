// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class PatternWarningCompletionTest
{
    [Theory]
    [InlineData("_", "_")]
    [InlineData(".A", ".A")]
    public void LateInvalidTypeSuppressesUnreachableWarning(string first, string second)
    {
        var c = MinimalEmissionTest.Analyze(Source("internal", first, second));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        Assert.Empty(c.Binding.PatternWarnings);
        Assert.False(c.Bind().IsComplete);
        Assert.Empty(c.Binding.PatternWarnings);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Empty(restored.Binding.PatternWarnings);
    }

    [Theory]
    [InlineData("_", "_")]
    [InlineData(".A", ".A")]
    public void ValidTypesRetainSingleCoveringArmWarning(string first, string second)
    {
        var c = MinimalEmissionTest.Analyze(Source("public", first, second));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Equal(0, Assert.Single(c.Binding.PatternWarnings).CoveringArm);
        Assert.True(c.Binding.CheckBound().IsComplete);
        Assert.Single(c.Binding.PatternWarnings);
        Assert.True(c.Bind().IsComplete);
        Assert.Single(c.Binding.PatternWarnings);
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.Single(restored.Binding.PatternWarnings);
    }

    [Fact]
    public void BodyFailureDoesNotSuppressIndependentCoverageWarning()
    {
        var c = MinimalEmissionTest.Analyze(Source("public", "_", "_").Replace("=> ()", "=> missing()", StringComparison.Ordinal));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Single(c.Binding.PatternWarnings);
    }

    [Fact]
    public void ValidNonExhaustivePatternsStillWarnAboutDuplicateArms()
    {
        var c = MinimalEmissionTest.Analyze("group Consumer\n    func inspect(value: bool) => match value\n        true => ()\n        true => ()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.NonExhaustiveMatch);
        Assert.Single(c.Binding.PatternWarnings);
    }

    [Fact]
    public void WarmWarningCompletionAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source("public", "_", "_"));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete || c.Binding.PatternWarnings.Count != 1)
            {
                throw new InvalidOperationException("Pattern warning completion failed.");
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

    private static string Source(string access, string first, string second)
        => access + " contract Hidden\npublic struct Value\n    Self is Hidden\npublic enum E<T>\n    T is Hidden\n    A\ngroup Consumer\n    func inspect(value: E<Value>) => match value@move\n        " + first + " => ()\n        " + second + " => ()";
}
