// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class AssociatedFormationTest
{
    private const string Nested = "contract C\n    associate Item(a, b) is ref/(ref/i32 during a) during b\nstruct S\n    Self is C\n";

    [Theory]
    [InlineData("origin a outlives b", true)]
    [InlineData("", false)]
    [InlineData("origin b outlives a", false)]
    public void FixedTypePublishesNestedFormationConditions(string relation, bool valid)
    {
        var source = Nested + "func f(x: ref/i32 during a, y: ref/i32 during b) -> S.Item(a, b)\n    " + relation + "\n    $abort(\"unused\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void WellFormedInputProvesApplicationDomain()
    {
        var source = Nested + "func f(x: ref/(ref/i32 during a) during b) -> S.Item(a, b) => x\nlet value = 7\nlet slot = value@ref\nlet result = f(slot@ref)\nrequire result == 7 else => $abort(\"nested\")\nConsole.writeLine(\"nested\")";
        ScalarEmissionTest.EmitFixture("AssociatedFormationNested", source, "nested\n");
    }

    [Fact]
    public void ImplementationCannotStrengthenFormation()
    {
        const string source = "contract C\n    associate Item(a, b)\nstruct S\n    Self is C\n    associate C.Item(a, b) is ref/(ref/i32 during a) during b";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
    }
}
