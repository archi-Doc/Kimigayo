// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
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

    // SPEC 8.4.3: only a requirement may end with a formation Type; a specification fixes its Type, and a formation Type
    // elsewhere is an invalid Constraint, not an implementation limit.
    [Fact]
    public void FormationTypesOutsideRequirementsAreInvalid()
    {
        var c = MinimalEmissionTest.Analyze("contract C\n    associate Item(a)\nstruct S\n    Self is C\n    associate C.Item(a) is i32 for ref/Self during a");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidConstraint_Kd);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.UnsupportedBinding_Kd);
    }

    [Theory]
    [InlineData("", "ref/i32 during step", true)]
    [InlineData(" is Copy", "ref/i32 during step", true)]
    [InlineData("", "uniq/i32 during step", false)]
    public void FormationTypeDoesNotFixTheAssociatedType(string capability, string formation, bool valid)
    {
        var source = "contract C\n    associate Item(step)" + capability + " for " + formation + "\nstruct S\n    Self is C\n    associate C.Item(a) is i32\nfunc f() -> S.Item(static) => 42";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("origin a outlives b", true)]
    [InlineData("", false)]
    public void FormationTypePublishesNestedConditions(string relation, bool valid)
    {
        var source = "contract C\n    associate Item(a, b) for ref/(ref/i32 during a) during b\nstruct S\n    Self is C\n    associate C.Item(x, y) is i32\nfunc f(x: ref/i32 during a, y: ref/i32 during b) -> S.Item(a, b)\n    " + relation + "\n    return 42";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}
