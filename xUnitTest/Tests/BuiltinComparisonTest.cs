// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class BuiltinComparisonTest
{
    [Fact]
    public void ScalarRequirementsPreserveContractEqualityAndOrdering()
    {
        const string Source = """
            func equal<T>(left: ref/T, right: ref/T) -> bool
                T is Equatable
                return left.equals(right)
            func order<T>(left: ref/T, right: ref/T) -> i32
                T is Comparable
                return left.compare(right)
            let first: i32 = -2147483648
            let last: i32 = 2147483647
            require equal(first@ref, first@ref) and not equal(first@ref, last@ref) else => $abort("integer equality")
            require order(first@ref, last@ref) < 0 and order(last@ref, first@ref) > 0 else => $abort("integer order")
            require order(first@ref, first@ref) == 0 else => $abort("equal order")
            let yes = true
            let no = false
            require equal(yes@ref, yes@ref) and not equal(yes@ref, no@ref) else => $abort("bool equality")
            let nan: f64 = 0.0 / 0.0
            let zero: f64 = 0.0
            let negativeZero: f64 = -0.0
            require equal(nan@ref, nan@ref) and not equal(nan@ref, zero@ref) else => $abort("NaN contract")
            require not equal(zero@ref, nan@ref) and equal(zero@ref, negativeZero@ref) else => $abort("float contract")
            require not (nan == nan) and nan != nan else => $abort("IEEE equality")
            require first.equals(first@ref) and first.compare(last@ref) < 0 else => $abort("direct requirements")
            Console.writeLine("Builtin comparison witnesses preserved.")
            """;
        ScalarEmissionTest.EmitFixture("ComparisonBuiltinScalars", Source, "Builtin comparison witnesses preserved.\n");
    }

    [Theory]
    [InlineData("f32", "0.0")]
    [InlineData("f64", "0.0")]
    [InlineData("bool", "true")]
    public void NonOrderedScalarsDoNotSatisfyComparable(string type, string value)
    {
        var c = MinimalEmissionTest.Analyze($"""
            func order<T>(left: ref/T, right: ref/T) -> i32
                T is Comparable
                return left.compare(right)
            let value: {type} = {value}
            let result = order(value@ref, value@ref)
            """);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code.ToString() == "NoApplicableOverload_Kd");
    }
}
