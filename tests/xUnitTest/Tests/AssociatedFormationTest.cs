// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

public class AssociatedFormationTest
{
    private const string Nested = "contract C\n    associate Item(a, b) is ref/(ref/i32 during a) during b\nstruct S\n    Self is C\n";

    // SPEC 15.3.7: the published condition `a outlives b` of the result S.Item(a, b) is a premise of f and an obligation of its calls,
    // so f is valid with or without a clause (it was a definition obligation, and f was rejected without `a outlives b`).
    [Theory]
    [InlineData("origin a outlives b")]
    [InlineData("")]
    [InlineData("origin b outlives a")]
    public void FixedTypePublishesNestedFormationConditions(string relation)
    {
        var source = Nested + "func f(x: ref/i32 during a, y: ref/i32 during b) -> S.Item(a, b)\n    " + relation + "\n    $abort(\"unused\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 15.3.7: a call proves the published condition for its substituted result. An argument that pins b to d at an invariant
    // position leaves `c outlives d`, which g does not prove: one wellFormed record at the call. A call that leaves b free is valid.
    [Fact]
    public void ACallProvesThePublishedFormationCondition()
    {
        var callee = Nested + "func f(x: ref/i32 during a, y: ref/i32 during b, keep: uniq/(ref/i32 during b)) -> S.Item(a, b)\n    $abort(\"unused\")\n";
        var source = callee + "func g(x: ref/i32 during c, y: ref/i32 during d, keep: uniq/(ref/i32 during d)) -> i32\n    let r = f(x, y, keep)\n    return 0\npublic func main() => ()\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "requires c outlives d, which is not proven", "wellFormed"), (error.Code, error.Label, error.Reason![3].Value));
        Assert.Equal("f(x, y, keep)", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.True(DiagnosticCorpus.Check(source.Replace("    let r = f(x, y, keep)", "    origin c outlives d\n    let r = f(x, y, keep)", StringComparison.Ordinal)).Accepted);
        Assert.True(DiagnosticCorpus.Check(Nested + "func h(x: ref/i32 during a, y: ref/i32 during b) -> S.Item(a, b)\n    $abort(\"unused\")\nfunc k(x: ref/i32 during c, y: ref/i32 during d) -> i32\n    let r = h(x, y)\n    return 0\npublic func main() => ()\n").Accepted);
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
