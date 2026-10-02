// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class AssociatedParameterRefinementTest
{
    [Theory]
    [InlineData("ref/E.Element during a", true)]
    [InlineData("E.Element", false)]
    public void ProjectedReferentNeedsLifetimeEvidence(string input, bool valid)
    {
        var source = "contract A\n    associate Element\ncontract C<E>\n    E is A\n    associate Item(a) is ref/E.Element during a\nfunc f<U, E>(value: " + input + ", other: ref/i32 during a) -> U.(C<E>).Item(a)\n    E is A\n    U is C<E>\n    $abort(\"unused\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void ProjectedReferentExecutesThroughGenericCaller()
    {
        const string source = "contract A\n    associate Element\ncontract C<E>\n    E is A\n    associate Item(a) is ref/E.Element during a\nstruct ElementSource\n    Self is A\n    associate Element is i32\nstruct S\n    Self is C<ElementSource>\n    public init() => ()\nfunc f<U, E>(source: ref/U, element: ref/E, value: ref/E.Element during a) -> U.(C<E>).Item(a)\n    E is A\n    U is C<E>\n    return value\nfunc call(value: ref/i32 during a) -> ref/i32 during a\n    let source = S.init()\n    let element = ElementSource.init()\n    return f(source@ref, element@ref, value)\nlet value = 42\nrequire call(value@ref) == 42 else => $abort(\"projection\")\nConsole.writeLine(\"projection\")";
        ScalarEmissionTest.EmitFixture("AssociatedProjectedParameter", source.Replace("    associate Element is i32\n", "    associate Element is i32\n    public init() => ()\n", StringComparison.Ordinal), "projection\n");
    }

    [Theory]
    [InlineData("let value: i32 = 42\naccept(value, marker@ref)", true)]
    [InlineData("let value = \"wrong\"\naccept(value@ref, marker@ref)", false)]
    [InlineData("let value: i32 = 42\nonly(value)", false)]
    public void ProjectedInputsAreCheckedAfterReceiverInference(string call, bool valid)
    {
        var source = "contract A\n    associate Element\nstruct S\n    Self is A\n    associate Element is i32\n    public init() => ()\nfunc accept<T>(value: ref/T.Element, marker: ref/T)\n    T is A\n    ()\nfunc only<T>(value: ref/T.Element)\n    T is A\n    ()\nlet marker = S.init()\n" + call;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void UnrelatedProjectedInputsCannotBeUnified()
    {
        const string source = "contract A\n    associate Element\nfunc accept<T>(value: ref/T.Element, marker: ref/T)\n    T is A\n    ()\nfunc caller<T, U>(value: ref/T.Element, marker: ref/U)\n    T is A\n    U is A\n    accept(value, marker)";
        Assert.False(MinimalEmissionTest.Analyze(source).Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData(" for ref/E during a", true)]
    [InlineData("", false)]
    public void RefinedContractUsesSubstitutedFormationDomain(string domain, bool valid)
    {
        var source = "contract C<E>\n    associate Item(a)" + domain + "\ncontract D<F>: C<F>\n    associate C.Item(b) is ref/F during b";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("T.(C<E>).Item(a)")]
    [InlineData("T.Item(a)")]
    public void BorrowedRequirementSubstitutesParameterAndOrigin(string result)
    {
        var source = "contract C<E>\n    associate Item(step) is ref/E during step\n    func borrow(self: ref/Self, value: ref/E during a) -> Self.Item(a)\nstruct S\n    Self is C<i32>\n    public init() => ()\n    public func borrow(self: ref/Self, value: ref/i32 during a) -> ref/i32 during a => value\nfunc relay<T, E>(source: ref/T, value: ref/E during a) -> " + result + "\n    T is C<E>\n    return source.borrow(value)\nlet s = S.init()\nvar value = 7\nlet result = relay(s@ref, value@ref)\nrequire result == 7 else => $abort(\"witness\")\nConsole.writeLine(\"witness\")";
        ScalarEmissionTest.EmitFixture("AssociatedParameterWitness" + (result.Contains(".(") ? "Qualified" : "Short"), source, "witness\n");
    }

    // SPEC 8.4.9: a family of a generic Contract is identified by its bound reference, so a parameter conforming to C<i32>
    // and C<i64> keeps `T.(C<i32>).Item(a)` and `T.(C<i64>).Item(a)` apart, and the instance uses the matching witness.
    private const string TwoFamilies = "contract C<E>\n    associate Item(a)\n    func borrow(self: ref/Self, value: ref/E during a) -> Self.Item(a)\n" +
        "struct S\n    Self is C<i32>\n    Self is C<i64>\n    associate C<i32>.Item(a) is ref/i32 during a\n    associate C<i64>.Item(a) is ref/i64 during a\n    public init() => ()\n" +
        "    public func borrow(self: ref/Self, value: ref/i32 during a) -> ref/i32 during a => value\n    public func borrow(self: ref/Self, value: ref/i64 during a) -> ref/i64 during a => value\n";

    [Fact]
    public void FamiliesOfDistinctBoundReferencesStayDistinct()
    {
        const string Use = "func relay<T>(source: ref/T, small: ref/i32 during a, large: ref/i64 during a) -> T.(C<i64>).Item(a)\n    T is C<i32>\n    T is C<i64>\n" +
            "    let first: T.(C<i32>).Item(a) = source.borrow(small)\n    return source.borrow(large)\n" +
            "let s = S.init()\nvar small: i32 = 7\nvar large: i64 = 9\nlet b = relay(s@ref, small@ref, large@ref)\nrequire b == 9 else => $abort(\"relay\")\nConsole.writeLine(\"families\")";
        ScalarEmissionTest.EmitFixture("AssociatedParameterTwoFamilies", TwoFamilies + Use, "families\n");
    }

    [Fact]
    public void AFamilyOfAnotherBoundReferenceIsRejected()
    {
        const string Mixed = "func mix<T>(source: ref/T, large: ref/i64 during a) -> T.(C<i32>).Item(a)\n    T is C<i32>\n    T is C<i64>\n    return source.borrow(large)\n";
        var c = MinimalEmissionTest.Analyze(TwoFamilies + Mixed);
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("C<i32>", "i32", true)]
    [InlineData("C<i32>", "string", false)]
    public void RefiningContractKeepsBoundFamilyIdentity(string constraint, string result, bool valid)
    {
        var source = "contract C<E>\n    associate Item(a) is E\ncontract D<F>: C<F>\nfunc f<U>(source: ref/U during a, value: U.(" + constraint + ").Item(a)) -> " + result + "\n    U is D<i32>\n    return value";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}
