// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ComparisonOperatorTest
{
    [Fact]
    public void ForwardingAndExplicitSpecializationPreserveNaNContractEquality()
    {
        const string Source = """
            func equal<T>(left: ref/T, right: ref/T) -> bool
                T is Equatable
                return left == right
            specialize func equal<f64>(left: ref/f64, right: ref/f64) -> bool
                Console.writeLine("specialized")
                return left.equals(right)
            func forward<T>(left: ref/T, right: ref/T) -> bool
                T is Equatable
                return equal<T>(left, right)
            let nan: f64 = 0.0 / 0.0
            let zero: f64 = 0.0
            require forward(nan@ref, nan@ref) else => $abort("NaN")
            require not forward(nan@ref, zero@ref) else => $abort("different")
            require not (nan == nan) else => $abort("IEEE")
            """;
        ScalarEmissionTest.EmitFixture("ComparisonOperatorSpecialization", Source, "specialized\nspecialized\n");
    }

    [Fact]
    public void ReturningDuringTheRightOperandAbandonsTheComparison()
    {
        const string Source = """
            struct Key
                Self is Equatable
                public func equals(self: ref/Self, other: ref/Self) -> bool
                    Console.writeLine("unexpected witness")
                    return true
                deinit => Console.writeLine("destroyed")
            func run() -> i32
                let first = Key.init()
                defer => Console.writeLine("deferred")
                let ignored = first == (do => return 7)
                return 0
            require run() == 7 else => $abort("return")
            """;
        ScalarEmissionTest.EmitFixture("ComparisonOperatorReturn", Source, "deferred\ndestroyed\n");
    }

    [Fact]
    public void OperandsEvaluateOnceAndTemporariesAreDestroyedAfterTheWitness()
    {
        const string Source = """
            struct Key
                Self is Equatable
                let first: bool
                public init(first: bool) => self.first = first
                public func equals(self: ref/Self, other: ref/Self) -> bool
                    Console.writeLine("equals")
                    return false
                deinit
                    if self.first => Console.writeLine("left")
                    else => Console.writeLine("right")
            func create(label: string, first: bool) -> Key
                Console.writeLine(label)
                return Key.init(first)
            require create("left", true) != create("right", false) else => $abort("equality")
            Console.writeLine("finished")
            """;
        ScalarEmissionTest.EmitFixture("ComparisonOperatorOrder", Source, "left\nright\nequals\nright\nleft\nfinished\n");
    }

    [Fact]
    public void GenericComparisonRequiresItsContractPremise()
    {
        var c = MinimalEmissionTest.Analyze("func equal<T>(left: ref/T, right: ref/T) -> bool => left == right");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code.ToString() == "UnprovenConstraint_Kd");
    }

    [Fact]
    public void LeftOperandLoanRejectsMovingItDuringRightEvaluation()
    {
        var c = MinimalEmissionTest.Analyze("""
            struct Key
                Self is Equatable
                public let value: i32 = 1
                public func equals(self: ref/Self, other: ref/Self) -> bool => self.value == other.value
            func take(value: Key) -> Key => value@move
            let value = Key.init()
            let invalid = value == take(value@move)
            """);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
    }

    [Fact]
    public void UserAndGenericOperatorsUseTheDeclaredWitness()
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
                return left == right
            func before<T>(left: ref/T, right: ref/T) -> bool
                T is Comparable
                return left < right
            let first = Key.init(2)
            let same = Key.init(2)
            let last = Key.init(5)
            require first == same and first != last else => $abort("equality")
            require first < last and first <= same and last > first and last >= same else => $abort("ordering")
            require not (first > same) and not (first < same) else => $abort("zero")
            require equal(first@ref, same@ref) and before(first@ref, last@ref) else => $abort("generic")
            require first@ref == same@ref and first == same@ref and first@ref == same else => $abort("borrow operands")
            let nan: f64 = 0.0 / 0.0
            require equal(nan@ref, nan@ref) and not (nan == nan) else => $abort("generic NaN")
            require first.value == 2 and last.value == 5 else => $abort("moved")
            Console.writeLine("Contract operators preserved.")
            """;
        ScalarEmissionTest.EmitFixture("ComparisonUserOperators", Source, "Contract operators preserved.\n");
    }

    [Fact]
    public void SameNamedMembersDoNotSupplyAnUndeclaredConformance()
    {
        var c = MinimalEmissionTest.Analyze("""
            struct Key
                public func equals(self: ref/Self, other: ref/Self) -> bool => true
            let left = Key.init()
            let right = Key.init()
            let result = left == right
            """);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code.ToString() == "UnsatisfiedConstraint_Kd");
    }
}
