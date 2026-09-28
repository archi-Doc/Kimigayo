// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

public class AssociatedContractParameterTest
{
    [Theory]
    [InlineData("ref", "U.(C<E>).Item(a)")]
    [InlineData("uniq", "U.(C<E>).Item(a)")]
    [InlineData("ref", "U.Item(a)")]
    public void BorrowedParameterFormation(string semantics, string result)
    {
        var transfer = semantics == "uniq" ? "@move" : string.Empty;
        var source = "contract C<E>\n    associate Item(a) is " + semantics + "/E during a\nstruct S\n    Self is C<i32>\n    public init() => ()\nfunc f<U, E>(source: ref/U, value: " + semantics + "/E during a) -> " + result + "\n    U is C<E>\n    return value" + transfer + "\nlet s = S.init()\nvar value = 42\nlet result = f(s@ref, value@" + semantics + ")\nrequire result == 42 else => $abort(\"borrow\")\nConsole.writeLine(\"borrow\")";
        ScalarEmissionTest.EmitFixture("AssociatedContractParameterBorrow" + semantics + (result.Contains(".(") ? "Qualified" : "Short"), source, "borrow\n");
    }

    [Theory]
    [InlineData(" for ref/E during a", true)]
    [InlineData("", false)]
    public void ImplementationUsesOnlyTheSubstitutedPublicDomain(string domain, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("contract C<E>\n    associate Item(a)" + domain + "\nstruct S<T>\n    Self is C<T>\n    associate C.Item(b) is ref/T during b");
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("origin b outlives a", true)]
    [InlineData("", false)]
    [InlineData("origin a outlives b", false)]
    public void BorrowedTypeArgumentKeepsItsFormationConditions(string relation, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("contract C<E>\n    associate Item(step) for ref/E during step\nfunc f<U>(x: ref/i32 during a, y: ref/i32 during b) -> U.(C<ref/i32 during b>).Item(a)\n    U is C<ref/i32 during b>\n    " + relation + "\n    $abort(\"unused\")");
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void ParameterFamilyResultRetainsTheSourceLoan()
    {
        const string source = "contract C<E>\n    associate Item(a) is ref/E during a\nstruct S\n    Self is C<i32>\nfunc f(value: ref/i32 during a) -> S.Item(a) => value\nvar value = 7\nlet result = f(value@ref)\nvalue = 9\nrequire result == 7 else => $abort(\"loan\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
    }

    [Theory]
    [InlineData("ref/E during a", true)]
    [InlineData("E", false)]
    public void UnfixedFamilyChecksSubstitutedFormation(string input, bool valid)
    {
        var source = "contract C<E>\n    associate Item(a) for ref/E during a\nfunc f<U, E>(value: " + input + ", other: ref/i32 during a) -> U.(C<E>).Item(a)\n    U is C<E>\n    $abort(\"unused\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

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
