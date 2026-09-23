// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ExternalOriginForwardingTest
{
    [Fact]
    public void Milestone16()
    {
        var c = MinimalEmissionTest.Analyze(Source());
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
    }

    [Theory]
    [InlineData("self.right and self.left", "second@ref")]
    [InlineData("self.left and self.right and self.left", "second@ref")]
    [InlineData("self.left and self.right", "first@ref")]
    public void NormalizesInstantiatedIntersections(string origin, string second)
    {
        var c = MinimalEmissionTest.Analyze(Source()
            .Replace("self.left and self.right", origin, StringComparison.Ordinal)
            .Replace("Pair.init(first@ref, second@ref)", "Pair.init(first@ref, " + second + ")", StringComparison.Ordinal));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
    }

    [Fact]
    public void RejectsUnknownPayloadOrigin()
    {
        var c = MinimalEmissionTest.Analyze(Source().Replace("Found(ref/Cell during source)", "Found(ref/Cell during missing)", StringComparison.Ordinal));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("static")]
    [InlineData("self.left")]
    public void RejectsInsufficientReturnOrigin(string origin)
    {
        var source = Source();
        var changed = source.Replace("origin selection.source == self.left and self.right", "origin selection.source == " + origin, StringComparison.Ordinal);
        Assert.NotEqual(source, changed);
        var c = MinimalEmissionTest.Analyze(changed);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("        require selected.value", "        first.value = 99\n        require selected.value")]
    [InlineData("        require selected.value", "        second.value = 99\n        require selected.value")]
    [InlineData("        child.value =", "        target.value = 99\n        child.value =")]
    [InlineData("            exit to selection:", "            first.value = 99\n            exit to selection:")]
    [InlineData("            exit to selection:", "            second.value = 99\n            exit to selection:")]
    public void RejectsConflictingAccess(string before, string after)
    {
        var c = MinimalEmissionTest.Analyze(Source().Replace(before, after, StringComparison.Ordinal));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, issue => issue.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void RejectsEscapingLocalSource()
    {
        var source = Source().Replace("    var first = Cell.init(10)\n", string.Empty, StringComparison.Ordinal)
            .Replace("            let pair =", "            var first = Cell.init(10)\n            let pair =", StringComparison.Ordinal)
            .Replace("    add(first@uniq, 2)\n", string.Empty, StringComparison.Ordinal)
            .Replace("first.value == 13 and ", string.Empty, StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, issue => issue.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.Validate(out _));
    }

    private static string Source()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kimigayo.slnx")))
        {
            directory = directory.Parent;
        }

        return File.ReadAllText(Path.Combine(directory!.FullName, "milestones", "Milestone16.kimi")).Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
