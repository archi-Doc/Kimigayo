// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ComparisonContractTest
{
    [Fact]
    public void LibraryDeclarationsHaveRecognizedIdentitiesAndRefinement()
    {
        var c = MinimalEmissionTest.Analyze("()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(KimiDeclarationState.Validated, c.Library.GetDeclarationState(KimiDeclarationId.Equatable));
        Assert.Equal(KimiDeclarationState.Validated, c.Library.GetDeclarationState(KimiDeclarationId.Comparable));
        var equality = c.Library.GetSymbol(KimiDeclarationId.Equatable)!;
        var ordering = c.Library.GetSymbol(KimiDeclarationId.Comparable)!;
        Assert.Equal("equals", Assert.Single(equality.Contract!.Requirements).Name);
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

    [Fact]
    public void ComparableRequiresItsInheritedEqualityWitness()
    {
        var c = MinimalEmissionTest.Analyze("struct Key\n    Self is Comparable\n    public func compare(self: ref/Self, other: ref/Self) -> i32 => 0");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code.ToString() == "MissingContractImplementation_Kd");
    }

    [Theory]
    [InlineData(KimiDeclarationId.Equatable)]
    [InlineData(KimiDeclarationId.Comparable)]
    public void LibraryContractsRejectExtraRequirements(KimiDeclarationId id)
    {
        var c = Compilation.CreateForTest();
        var declaration = (ContractKoto)c.Library.GetSymbol(id)!.Declaration;
        c.Library.Kotonoha.CreateCodeContext().Parse(declaration, "func extra(self: ref/Self) -> bool");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(KimiDeclarationState.Invalid, c.Library.GetDeclarationState(id));
    }
}
