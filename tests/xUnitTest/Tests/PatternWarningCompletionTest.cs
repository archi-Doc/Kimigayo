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
        Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.Access);
        Assert.Empty(c.Binding.PatternWarnings);
        Assert.False(c.Bind().IsComplete);
        Assert.Empty(c.Binding.PatternWarnings);
        var restored = CompilationTestHelper.Reload(c);
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
        var restored = CompilationTestHelper.Reload(c);
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
        Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.NonExhaustiveMatch);
        Assert.Single(c.Binding.PatternWarnings);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmWarningCompletionAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source("public", "_", "_"));
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete || c.Binding.PatternWarnings.Count != 1)
            {
                throw new InvalidOperationException("Pattern warning completion failed.");
            }
        }));
    }

    // PLAN G800 (SPEC 14.8.4): a warning names the first earlier unguarded arm that contains the Pattern, whether that arm is found
    // by its structure (no Wildcard or Binding) or compared (one with a Wildcard); a guarded arm contains nothing, and after a
    // whole-position Wildcard that arm is the latest candidate.
    [Fact]
    public void AWarningNamesTheFirstContainingArm()
    {
        var c = MinimalEmissionTest.Analyze(
            "func f(x: (i32, i32), c: bool) -> i32 => match x\n    (0, 1) => 0\n    (0, _) => 1\n    (0, 1) => 2\n    (0, 2) => 3\n" +
            "    (1, 1) if c => 4\n    (1, 1) => 5\n    (1, 1) => 6\n    _ => 7\n    (2, 2) => 8\n    (2, 2) => 9\n    _ => 10");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal("(0, 1) 0|(0, 2) 1|(1, 1) 5|(2, 2) 7|(2, 2) 7|_ 7", string.Join("|", c.Binding.PatternWarnings.Select(static x => $"{x.Pattern} {x.CoveringArm}")));
    }

    // PLAN G800: the containment work (positions keyed and pairwise checks) grows linearly in the arms when no arm before the last
    // has a Wildcard or a Binding, a Literal, a Case or a Tuple alike, and when a whole-position Wildcard comes first (a warning each).
    [Theory]
    [InlineData("i32", "{0}", false)]
    [InlineData("i32", "{0}", true)]
    [InlineData("Option<i32>", ".Some({0})", false)]
    [InlineData("(i32, i32)", "({0}, 1)", false)]
    public void ContainmentWorkGrowsLinearlyInTheArms(string subject, string pattern, bool wildcardFirst)
    {
        var work = new[] { 256, 512, 1024 }.Select(Work).ToArray();
        Assert.True(MirBorrowsTest.Grows(work, 2), $"work {string.Join(", ", work)}");

        long Work(int count)
        {
            var arms = string.Concat(Enumerable.Range(0, count).Select(i => $"    {string.Format(pattern, i)} => 1\n"));
            var c = MinimalEmissionTest.Analyze($"func f(x: {subject}) -> i32 => match x\n" + (wildcardFirst ? "    _ => 0\n" + arms : arms + "    _ => 0\n"));
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
            Assert.Equal(wildcardFirst ? count : 0, c.Binding.PatternWarnings.Count);
            return c.Binding.PatternContainmentWork;
        }
    }

    private static string Source(string access, string first, string second)
        => access + " contract Hidden\npublic struct Value\n    Self is Hidden\npublic enum E<T>\n    T is Hidden\n    A\ngroup Consumer\n    func inspect(value: E<Value>) => match value@move\n        " + first + " => ()\n        " + second + " => ()";
}
