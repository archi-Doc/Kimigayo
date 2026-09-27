// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

public class AssociatedContractParameterTest
{
    [Theory]
    [InlineData("U.Item(a)")]
    [InlineData("U.(C<i32>).Item(a)")]
    public void FixedFamilySubstitutesBoundContractParameter(string result)
    {
        var source = "contract C<E>\n    associate Item(a) is E\nstruct S\n    Self is C<i32>\n    public init() => ()\nfunc f<U>(value: ref/U during a) -> " + result + "\n    U is C<i32>\n    return 42\nlet s = S.init()\nrequire f(s@ref) == 42 else => $abort(\"family\")\nConsole.writeLine(\"family\")";
        ScalarEmissionTest.EmitFixture("AssociatedContractParameter" + (result.Contains(".(") ? "Qualified" : "Short"), source, "family\n");
    }

    [Fact]
    public void RefinedFamilySubstitutesAncestorParameter()
    {
        const string source = "contract C<E>\n    associate Item(a) is E\ncontract D<F>: C<F>\nstruct S\n    Self is D<i32>\n    public init() => ()\nfunc f<U>(value: ref/U during a) -> U.(C<i32>).Item(a)\n    U is D<i32>\n    return 42\nlet s = S.init()\nrequire f(s@ref) == 42 else => $abort(\"ancestor\")\nConsole.writeLine(\"ancestor\")";
        ScalarEmissionTest.EmitFixture("AssociatedContractParameterAncestor", source, "ancestor\n");
    }

    [Theory]
    [InlineData("U.(C<string>).Item(a)", "C<i32>")]
    [InlineData("U.(C<i32, string>).Item(a)", "C<i32>")]
    [InlineData("U.(C<Missing>).Item(a)", "C<i32>")]
    [InlineData("U.(C<string>).Item(a)", "C<string>")]
    public void BoundReferenceChecksArgumentsAndEvidence(string result, string premise)
    {
        var source = "contract C<E>\n    associate Item(a) is E\nfunc f<U>(value: ref/U during a) -> " + result + "\n    U is " + premise + "\n    return 42";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void BoundReferenceStillChecksParameterConstraints()
    {
        const string source = "contract C<E>\n    E is Copy\n    associate Item(a) is E\nstruct S\n    Self is C<string>";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code is DiagnosticCode.UnprovenConstraint_Kd or DiagnosticCode.UnsatisfiedConstraint_Kd);
    }
}
