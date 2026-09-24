// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class BuiltinComparisonTest
{
    [Fact]
    public void StringAndUnitRequirementsBorrowTheirOperands()
    {
        const string Source = """
            func equal<T>(left: ref/T, right: ref/T) -> bool
                T is Equatable
                return left.equals(right)
            func order<T>(left: ref/T, right: ref/T) -> i32
                T is Comparable
                return left.compare(right)
            let first = "a\0é"
            let same = "a\0é"
            let last = "a\0界"
            let empty = ""
            require equal(first, same) and not equal(first, last) else => $abort("string equality")
            require order(first, last) < 0 and order(last, first) > 0 else => $abort("string order")
            require order(first, same) == 0 and order(empty, first) < 0 else => $abort("string boundary")
            require first.equals(same) and first.compare(last) < 0 else => $abort("direct string")
            let unit = ()
            require equal(unit@ref, unit@ref) else => $abort("Unit equality")
            Console.writeLine(first)
            """;
        ScalarEmissionTest.EmitFixture("ComparisonBuiltinStringUnit", Source, "a\0é\n");
    }

    [Theory]
    [InlineData("i8", "-128", "127")]
    [InlineData("u8", "0", "255")]
    [InlineData("i16", "-32768", "32767")]
    [InlineData("u16", "0", "65535")]
    [InlineData("i32", "-2147483648", "2147483647")]
    [InlineData("u32", "0", "4294967295")]
    [InlineData("i64", "-9223372036854775808", "9223372036854775807")]
    [InlineData("u64", "0", "18446744073709551615")]
    [InlineData("i128", "-170141183460469231731687303715884105728", "170141183460469231731687303715884105727")]
    [InlineData("u128", "0", "340282366920938463463374607431768211455")]
    [InlineData("isize", "-9223372036854775808", "9223372036854775807")]
    [InlineData("usize", "0", "18446744073709551615")]
    [InlineData("char", "'a'", "'界'")]
    public void OrderedPrimitiveBoundaries(string type, string minimum, string maximum)
    {
        var source = $$"""
            func order<T>(left: ref/T, right: ref/T) -> i32
                T is Comparable
                return left.compare(right)
            let first: {{type}} = {{minimum}}
            let last: {{type}} = {{maximum}}
            require order(first@ref, last@ref) < 0 and order(last@ref, first@ref) > 0 else => $abort("order")
            require order(first@ref, first@ref) == 0 and first.equals(first@ref) else => $abort("equal")
            """;
        ScalarEmissionTest.EmitFixture("ComparisonBuiltinBoundary" + type, source, string.Empty);
    }

    [Theory]
    [InlineData("f32")]
    [InlineData("f64")]
    public void FloatingEqualityIncludesNaNsInfinitiesAndZeros(string type)
    {
        var source = $$"""
            func equal<T>(left: ref/T, right: ref/T) -> bool
                T is Equatable
                return left.equals(right)
            let zero: {{type}} = 0.0
            let negativeZero: {{type}} = -0.0
            let infinity: {{type}} = 1.0 / 0.0
            let negativeInfinity: {{type}} = -1.0 / 0.0
            let nan: {{type}} = 0.0 / 0.0
            let negativeNaN: {{type}} = -nan
            require equal(nan@ref, negativeNaN@ref) and not equal(nan@ref, infinity@ref) else => $abort("NaN")
            require equal(infinity@ref, infinity@ref) and not equal(infinity@ref, negativeInfinity@ref) else => $abort("infinity")
            require equal(zero@ref, negativeZero@ref) and not equal(zero@ref, nan@ref) else => $abort("zero")
            require not (nan == nan) and not (nan < zero) and not (nan >= zero) else => $abort("IEEE")
            """;
        ScalarEmissionTest.EmitFixture("ComparisonBuiltinFloat" + type, source, string.Empty);
    }

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
    [InlineData("()", "()")]
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
