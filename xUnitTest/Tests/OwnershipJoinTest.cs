// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class OwnershipJoinTest
{
    private const string Pair = "struct Cell\n    public var value: i32 = 7\nfunc inspect(c: bool)\n    var a = Cell.init()\n    var b = Cell.init()\n";

    [Fact]
    public void Milestone15()
    {
        var c = MinimalEmissionTest.Analyze(Source());
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
    }

    [Theory]
    [InlineData("    else\n        current = Item.init(12)\n", "", OwnershipFailure.UninitializedUse)]
    [InlineData("            current = Item.init(20)", "            // current = Item.init(20)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("            consume(current, initial + 1)", "            consume(current, initial + 1)\n            let invalid = current.value", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("        current.value = current.value + 1", "        current.value = current.value + 1\n        consume(current, initial + 1)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("            total = total + selected.value", "            current.value = 99\n            total = total + selected.value", OwnershipFailure.ComparisonLoanConflict)]
    public void RejectsInvalidJoin(string before, string after, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source().Replace(before, after, StringComparison.Ordinal));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, issue => issue.Failure == failure);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("if c => a@ref else => b@ref")]
    [InlineData("if c => b@ref else => a@ref")]
    [InlineData("if c\n        yield a@ref\n    else => b@ref")]
    [InlineData("match c\n        true => a@ref\n        false => b@ref")]
    [InlineData("choose: do\n        if c => exit to choose: a@ref\n        exit to choose: b@ref")]
    [InlineData("if c => (if c => a@ref else => b@ref) else => a@ref")]
    public void BorrowResultsRetainBothDependencies(string expression)
    {
        foreach (var annotation in new[] { string.Empty, ": ref/Cell" })
        {
            var source = Pair + "    let selected" + annotation + " = " + expression + "\n    let n = selected.value\n    a.value = n\n    b.value = n\ninspect(true)\ninspect(false)";
            var c = MinimalEmissionTest.Analyze(source);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
            var body = Assert.Single(c.Ownership.Bodies, b => b.Function.Name == "inspect");
            var selected = Assert.Single(body.Places, p => p.Source is Kimi.Compiler.Parsing.FieldKoto field && field.NameKoto.ToString() == "selected");
            Assert.Equal(OriginKind.Intersection, selected.Type.Origin!.Kind);
            Assert.Equal(2, selected.Type.Origin.Operands.Count);
            foreach (var owner in new[] { "a", "b" })
            {
                var invalid = MinimalEmissionTest.Analyze(source.Replace("    let n =", "    " + owner + ".value = 99\n    let n =", StringComparison.Ordinal));
                Assert.True(invalid.Binding.Result.IsComplete);
                Assert.Contains(invalid.Ownership.Issues, issue => issue.Failure == OwnershipFailure.ComparisonLoanConflict);
                Assert.False(invalid.Emission.Validate(out _));
            }
        }
    }

    [Fact]
    public void JoinedBorrowCannotOutliveEitherLocalSource()
    {
        var c = MinimalEmissionTest.Analyze(Pair + "    let selected = choose: do\n        let local = Cell.init()\n        exit to choose: if c => a@ref else => local@ref\n    let n = selected.value\ninspect(true)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, issue => issue.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("ref{static}/Cell", "a@ref", "b@ref")]
    [InlineData("ref/Cell", "a@ref", "b@uniq")]
    [InlineData("ref/Cell", "a@ref", "true")]
    public void BorrowJoinCannotChangeItsContract(string annotation, string first, string second)
    {
        var c = MinimalEmissionTest.Analyze(Pair + "    let selected: " + annotation + " = if c => " + first + " else => " + second + "\ninspect(true)");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void CheckedValuesRejectLostOriginDependency()
    {
        var c = MinimalEmissionTest.Analyze(Pair + "    let selected = if c => a@ref else => b@ref\n    let n = selected.value\ninspect(true)");
        Assert.True(c.Emission.Validate(out var error), MinimalEmissionTest.Describe(c, error));
        var body = Assert.Single(c.Ownership.Bodies, b => b.Function.Name == "inspect");
        var phi = body.Values.FindIndex(value => value.Kind == OwnershipValueKind.Phi);
        var incoming = body.PhiInputs[body.Values[phi].Start].Value;
        var narrowed = body.Places[body.Operations[incoming].Input].Type;
        var place = body.Operations[phi].Place;
        body.PlaceStorage[place] = body.Places[place] with { Type = narrowed };
        body.Operations[phi].Source.BoundType = narrowed;
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Equal(string.Empty, writer.ToString());
    }

    private static string Source()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kimigayo.slnx")))
        {
            directory = directory.Parent;
        }

        return File.ReadAllText(Path.Combine(directory!.FullName, "milestones", "Milestone15.kimi")).Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
