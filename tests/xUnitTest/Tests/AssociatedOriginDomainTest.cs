// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class AssociatedOriginDomainTest
{
    private const string Related = "contract C\n    associate Item(a, b) is ref/i32 during b\n        origin a outlives b\nstruct S\n    Self is C\n";

    [Theory]
    [InlineData("contract C\n    associate Item(a)\n        origin a outlives static")]
    [InlineData("contract C\n    associate Item(a, b) is i32\n        origin a outlives b")]
    public void RequirementPublishesOriginConditions(string source)
        => Assert.True(MinimalEmissionTest.Analyze(source).Binding.Result.IsComplete);

    [Theory]
    [InlineData("origin a outlives b", true)]
    [InlineData("origin a == b", true)]
    [InlineData("", false)]
    [InlineData("origin b outlives a", false)]
    public void ApplicationMustProvePublishedRelations(string relation, bool valid)
    {
        var source = Related + "func f(x: ref/i32 during a, y: ref/i32 during b) -> S.(C).Item(a, b)\n    " + relation + "\n    return y";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void RelatedFamilyExecutes()
    {
        var source = Related + "func f(x: ref/i32 during a) -> S.(C).Item(a, a) => x\nlet value = 7\nlet result = f(value@ref)\nrequire result == 7 else => $abort(\"domain\")\nConsole.writeLine(\"domain\")";
        ScalarEmissionTest.EmitFixture("AssociatedDomainRelation", source, "domain\n");
    }

    [Fact]
    public void ImplementationCannotIntroduceConditions()
    {
        const string source = "contract C\n    associate Item(a, b)\nstruct S\n    Self is C\n    associate C.Item(a, b) is ref/i32 during b\n        origin a outlives b";
        Assert.False(MinimalEmissionTest.Analyze(source).Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("origin left outlives right", true)]
    [InlineData("origin static outlives left", true)]
    [InlineData("origin right outlives left", false)]
    [InlineData("origin left == right", false)]
    public void ImplementationConditionsMustFollowFromTheRequirement(string relation, bool valid)
    {
        var source = "contract C\n    associate Item(a, b)\n        origin a outlives b\nstruct S\n    Self is C\n    associate C.Item(left, right) is ref/i32 during right\n        " + relation;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(valid, c.Bind().IsComplete);
    }
}
