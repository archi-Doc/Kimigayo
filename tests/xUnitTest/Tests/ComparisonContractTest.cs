// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ComparisonContractTest
{
    private const string PairTarget = "func f<s/T>(a: s/T, b: ref/T) -> bool\n    s is ref or uniq\n    T is ";
    private const string Chain = "contract Holder\n    associate Item\nfunc f<H, T>(a: ref/T, b: ref/T) -> ";

    [Fact]
    public void LibraryDeclarationsHaveRecognizedIdentitiesAndRefinement()
    {
        var c = MinimalEmissionTest.Analyze("()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var equality = c.Library.GetSymbol(KimiDeclarationId.Equatable)!;
        var ordering = c.Library.GetSymbol(KimiDeclarationId.Comparable)!;
        Assert.Equal("equals", Assert.Single(equality.Contract!.Requirements).Symbol.Name);
        Assert.Contains(equality, ordering.Contract!.Ancestors);
        Assert.Equal(2, ordering.Contract.Requirements.Count);
    }

    [Fact]
    public void GenericCallsKeepUserComparisonWitnesses()
    {
        const string Source = """
            struct Key
                Self is Comparable
                public let value: i32
                public init(value: i32) => self.value = value
                public func equals(self: ref/Self, other: ref/Self) -> bool => self.value == other.value
                public func compare(self: ref/Self, other: ref/Self) -> i32
                    if self.value < other.value => return -7
                    if self.value > other.value => return 9
                    return 0
            func equal<T>(left: ref/T, right: ref/T) -> bool
                T is Equatable
                return left.equals(right)
            func order<T>(left: ref/T, right: ref/T) -> i32
                T is Comparable
                return left.compare(right)
            let first = Key.init(2)
            let same = Key.init(2)
            let last = Key.init(5)
            require equal(first@ref, same@ref) and not equal(first@ref, last@ref) else => $abort("equality")
            require order(first@ref, last@ref) == -7 and order(last@ref, first@ref) == 9 else => $abort("order")
            require order(first@ref, same@ref) == 0 else => $abort("equal order")
            require first.value == 2 and last.value == 5 else => $abort("consumed operands")
            Console.writeLine("User comparison witnesses preserved.")
            """;
        ScalarEmissionTest.EmitFixture("ComparisonContractUserCalls", Source, "User comparison witnesses preserved.\n");
    }

    [Theory]
    [InlineData("public func equals(self: ref/Self, other: ref/Self) -> i32 => 0", "IncompatibleContractImplementation_Kd")]
    [InlineData("public func equals(self: uniq/Self, other: ref/Self) -> bool => true", "MissingContractImplementation_Kd")]
    [InlineData("private func equals(self: ref/Self, other: ref/Self) -> bool => true", "IncompatibleContractImplementation_Kd")]
    [InlineData("public func equals(self: ref/Self, other: Self) -> bool => true", "MissingContractImplementation_Kd")]
    public void IncompatibleEqualityWitnessIsRejected(string member, string diagnostic)
    {
        var c = MinimalEmissionTest.Analyze("struct Key\n    Self is Equatable\n    " + member);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code.ToString() == diagnostic);
    }

    // SPEC 8.2.3, 13.4, 4.7.1 (OT2; PLAN G157 (29)): an operand is compared and indexed at the Type its own scope's premises make it
    // (Binding.OperandTerminal premises by CallableContractType, as MIR reads it), so the pair target T under `T is string` or `T is i32`
    // compares as string or i32 (t2, t3), as T does under the chain `T is H.Item`, `H.Item is f64` (nf1), and their mismatches are the
    // Language records at the premised Types (x6, x7; nb1, bool has no ordering). An index through a ref layer on a chain-premised
    // [2 of i32] is the implementation limit (nc3), and on i32 is NotIndexable_Kd. Before, t2, t3, x6 and x7 were UnprovenConstraint_Kd,
    // nf1 and nb1 UnsatisfiedConstraint_Kd and nc3 NotIndexable_Kd.
    [Theory]
    [InlineData("t2", PairTarget + "string\n    return b == \"x\"\n", "")]
    [InlineData("t3", PairTarget + "i32\n    return b < 1\n", "")]
    [InlineData("x6", PairTarget + "string\n    return b == 3\n", "TypeMismatch_Kd: return b == 3")]
    [InlineData("x7", PairTarget + "i32\n    return b < \"x\"\n", "TypeMismatch_Kd: return b < \"x\"")]
    [InlineData("nf1", Chain + "bool\n    H is Holder\n    H.Item is f64\n    T is H.Item\n    return a < b\n", "")]
    [InlineData("nb1", Chain + "bool\n    H is Holder\n    H.Item is bool\n    T is H.Item\n    return a < b\n", "OperatorNotDefined_Kd: return a < b")]
    [InlineData("nc3", Chain + "i32\n    H is Holder\n    H.Item is [2 of i32]\n    T is H.Item\n    return a[1]\n", "Unsupported_Kd: return a[1]")]
    [InlineData("nc4", Chain + "i32\n    H is Holder\n    H.Item is i32\n    T is H.Item\n    return a[1]\n", "NotIndexable_Kd: return a[1]")]
    public void AnOperandIsReadAtItsPremisedTerminal(string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines(form, program + "()", records);

    // SPEC 8.2.3, 13.4 (OT2): under the chain `T is H.Item`, `H.Item is f64`, T is f64, so `==` and `!=` on NaN are the source operators'
    // (false, true), as under `T is f64`; before, the chain compared by the Contract's NaN-reflexive mapping (true, false).
    [Fact]
    public void AChainPremisedFloatComparesByItsOperators()
    {
        const string Source = """
            contract Holder
                associate Item
            struct Src
                Self is Holder
                associate Item is f64
            func rel<H, T>(a: ref/T, b: ref/T) -> string
                H is Holder
                H.Item is f64
                T is H.Item
                return "\(a == b) \(a != b)"
            let nan: f64 = 0.0 / 0.0
            Console.writeLine(rel<Src, f64>(nan@ref, nan@ref))
            """;
        ScalarEmissionTest.EmitFixture("ComparisonContractChainPremise", Source, "false true\n");
    }

    [Fact]
    public void ComparableRequiresItsInheritedEqualityWitness()
    {
        var c = MinimalEmissionTest.Analyze("struct Key\n    Self is Comparable\n    public func compare(self: ref/Self, other: ref/Self) -> i32 => 0");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code.ToString() == "MissingContractImplementation_Kd");
    }
}
