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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void RejectsUnknownPayloadOrigin()
    {
        var c = MinimalEmissionTest.Analyze(Source().Replace("Found(ref/Cell during source)", "Found(ref/Cell during missing)", StringComparison.Ordinal));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Generate(TextWriter.Null));
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
        Assert.False(c.Binding.Result.IsComplete && c.Analysis.IsVerified);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Theory]
    [InlineData("        require selected.value", "        first.value = 99\n        require selected.value")]
    [InlineData("        require selected.value", "        second.value = 99\n        require selected.value")]
    [InlineData("        child.value =", "        target.value = 99\n        child.value =")]
    [InlineData("            exit to selection ", "            first.value = 99\n            exit to selection ")]
    [InlineData("            exit to selection ", "            second.value = 99\n            exit to selection ")]
    [InlineData("            exit to selection ", "            var region = first@ref\n            first.value = 99\n            exit to selection ")]
    [InlineData("            exit to selection ", "            var region = second@ref\n            second.value = 99\n            exit to selection ")]
    public void RejectsConflictingAccess(string before, string after)
    {
        var c = MinimalEmissionTest.Analyze(Source().Replace(before, after, StringComparison.Ordinal));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Contains(c.Problems, issue => issue.Code.IsLoanConflict());
        Assert.Contains(c.BuildMir().Problems.All.ToArray(), static issue => issue.Code.IsLoanConflict());
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Fact]
    public void RejectsEscapingLocalSource()
    {
        var source = Source().Replace("    var first = Cell.init(10)\n", string.Empty, StringComparison.Ordinal)
            .Replace("            let pair =", "            var first = Cell.init(10)\n            let pair =", StringComparison.Ordinal)
            .Replace("    add(first@uniq, 2)\n", string.Empty, StringComparison.Ordinal)
            .Replace("first.value == 13 and ", string.Empty, StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Contains(c.Problems, issue => issue.Code.IsLoanConflict());
        Assert.False(c.Generate(TextWriter.Null));
    }

    // SPEC 15.6.3: possible call provenance overlaps individual meet constituents; it does not require either whole meet.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallResultsKeepEachMeetConstituent(bool reverse)
    {
        var source = "struct Cell\n    public var value: i32 = 1\n" +
            "struct Holder {source}\n    public let item: ref/Cell during source\n    public init(item: ref/Cell during source) => self.item = item\n" +
            "func choose(first: ref/Holder, second: ref/Holder, flag: bool) -> ref/Cell during (first.source and second.source)\n    if flag => return first.item\n    return second.item\n" +
            "func run(flag: bool)\n    var a = Cell.init()\n    var b = Cell.init()\n    var c = Cell.init()\n    var d = Cell.init()\n" +
            "    let x = if flag => a@ref else => b@ref\n    let y = if flag => b@ref else => c@ref\n    let left = Holder.init(x)\n    let right = Holder.init(y)\n    var region = d@ref\n" +
            "    let result = choose(left@ref, right@ref, flag)\n    let n = result.value\nrun(true)\nrun(false)";
        if (reverse)
        {
            source = source.Replace("choose(left@ref, right@ref, flag)", "choose(right@ref, left@ref, flag)", StringComparison.Ordinal);
        }

        foreach (var owner in new[] { "a", "b", "c", "d" })
        {
            var c = MinimalEmissionTest.Analyze(source.Replace("    let n =", "    " + owner + ".value = 99\n    let n =", StringComparison.Ordinal));
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
            Assert.Equal(owner != "d", c.Problems.Any(static issue => issue.Code.IsLoanConflict()));
            var problems = c.BuildMir().Problems.All.ToArray();
            Assert.Equal(owner != "d", problems.Any(static issue => issue.Code.IsLoanConflict()));
            if (owner == "d")
            {
                Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
                Assert.Empty(problems);
            }
        }
    }

    private static string Source() => Repository.ReadText("tests", "milestones", "Milestone16.kimi");
}
