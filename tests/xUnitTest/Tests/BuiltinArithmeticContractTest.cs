// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class BuiltinArithmeticContractTest
{
    private const string Source = """
        func sum<T>(left: ref/T, right: ref/T) -> T
            T is Addable<T>
            T.(Addable<T>).Output is T
            return left.added(right)
        func difference<T>(left: ref/T, right: ref/T) -> T
            T is Subtractable<T>
            T.(Subtractable<T>).Output is T
            return left.subtracted(right)
        func product<T>(left: ref/T, right: ref/T) -> T
            T is Multipliable<T>
            T.(Multipliable<T>).Output is T
            return left.multiplied(right)
        func quotient<T>(left: ref/T, right: ref/T) -> T
            T is Dividable<T>
            T.(Dividable<T>).Output is T
            return left.divided(right)
        func remainder<T>(left: ref/T, right: ref/T) -> T
            T is RemainderProvider<T>
            T.(RemainderProvider<T>).Output is T
            return left.remainder(right)
        func negative<T>(value: ref/T) -> T
            T is Negatable
            T.(Negatable).Output is T
            return value.negated()
        require sum(40, 2) == 42 and difference(44, 2) == 42 else => $abort("add/sub")
        require product(6, 7) == 42 and quotient(84, 2) == 42 else => $abort("mul/div")
        require remainder(-2147483648, -1) == 0 and negative(42) == -42 else => $abort("rem/neg")
        let maximum: Wrapping<u8> = 255
        require sum(maximum, 1) == 0 and negative(maximum) == 1 else => $abort("wrapping")
        let floating: f32 = 1.5
        require product(floating, 2.0) == 3.0 and negative(floating) == -1.5 else => $abort("float")
        """;

    [Fact]
    public void SharedNumericRequirementsExecuteWithTheirOriginalArithmeticRules()
        => ScalarEmissionTest.EmitFixture("BuiltinArithmeticContracts", Source, string.Empty);

    [Theory]
    [InlineData("i32", "Addable<i32>", true)]
    [InlineData("u128", "RemainderProvider<u128>", true)]
    [InlineData("Wrapping<u32>", "Negatable", true)]
    [InlineData("f32", "Dividable<f32>", true)]
    [InlineData("f64", "RemainderProvider<f64>", false)]
    [InlineData("u32", "Negatable", false)]
    [InlineData("i32", "Addable<i64>", false)]
    [InlineData("i32", "LeftAddable<i32>", false)]
    [InlineData("bool", "Addable<bool>", false)]
    public void BuiltinMatrixHasNoMixedOrLeftConformances(string type, string contract, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("func accept<T>(value: ref/T)\n    T is " + contract + "\n    ()\nlet value: " + type + " = " + (type == "bool" ? "false" : type.StartsWith('f') ? "1.0" : "1") + "\naccept(value)");
        Assert.Equal(valid, c.Binding.Result.IsComplete);
    }

    [Fact]
    public void PrimitiveIntegerEvidenceSuppliesSameTypeOutput()
    {
        const string Program = "func add<T>(left: ref/T, right: ref/T) -> T\n    T is PrimitiveInteger\n    return left.added(right)\nrequire add(40, 2) == 42 else => $abort(\"generic\")";
        ScalarEmissionTest.EmitFixture("BuiltinArithmeticPrimitiveInteger", Program, string.Empty);
    }

    [Fact]
    public void GenericWrappingEvidenceSuppliesBinaryAndUnaryRequirements()
    {
        const string Program = "func add<T>(left: ref/Wrapping<T>, right: ref/Wrapping<T>) -> Wrapping<T>\n    T is PrimitiveInteger\n    return left.added(right)\nfunc negative<T>(value: ref/Wrapping<T>) -> Wrapping<T>\n    T is PrimitiveInteger\n    return value.negated()\nlet value: Wrapping<u8> = 255\nrequire add(value, 1) == 0 and negative(value) == 1 else => $abort(\"wrapping\")";
        ScalarEmissionTest.EmitFixture("BuiltinArithmeticGenericWrapping", Program, string.Empty);
    }

    [Theory]
    [MemberData(nameof(WrappingIntegerTest.Integers), MemberType = typeof(WrappingIntegerTest))]
    public void IntegerWitnessesUseEveryWidthAndWrappingRepresentation(string integer)
    {
        var program = new System.Text.StringBuilder(Source.AsSpan(0, Source.IndexOf("require sum", StringComparison.Ordinal)).ToString());
        foreach (var wrapping in new[] { false, true })
        {
            var type = wrapping ? "Wrapping<" + integer + ">" : integer;
            var name = wrapping ? "wrapped" : "value";
            program.Append("let ").Append(name).Append(": ").Append(type).Append(" = 6\nrequire sum(").Append(name).Append(", 2) == 8 and difference(").Append(name).Append(", 2) == 4 and product(").Append(name).Append(", 7) == 42 else => $abort(\"binary\")\n");
            if (!integer.Contains("128", StringComparison.Ordinal))
            {
                program.Append("require quotient(").Append(name).Append(", 2) == 3 and remainder(").Append(name).Append(", 4) == 2 else => $abort(\"division\")\n");
            }

            if (wrapping || integer[0] == 'i')
            {
                program.Append("require sum(negative(").Append(name).Append("), ").Append(name).Append(") == 0 else => $abort(\"negation\")\n");
            }
        }

        ScalarEmissionTest.EmitFixture("BuiltinArithmeticWidth" + integer, program.ToString(), string.Empty);
    }

    [Theory]
    [InlineData("i128")]
    [InlineData("Wrapping<u128>")]
    public void NativeDivisionBoundaryDoesNotRemoveLanguageConformance(string type)
    {
        var declarations = Source[..Source.IndexOf("require sum", StringComparison.Ordinal)];
        var c = MinimalEmissionTest.Analyze(declarations + "let value: " + type + " = 6\nlet result = quotient(value, 2)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out var failure));
        Assert.Contains("128-bit division", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("i32", "1")]
    [InlineData("Wrapping<i32>", "1")]
    [InlineData("f32", "1.0")]
    public void ConformanceDoesNotCreateNamedNumericMembers(string type, string literal)
    {
        var c = MinimalEmissionTest.Analyze("let value: " + type + " = " + literal + "\nlet result = value.added(value)");
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("Addable", "added", "2147483647", "1", "KIMI_E_INT_OVERFLOW: Integer overflow")]
    [InlineData("Dividable", "divided", "6", "0", "KIMI_E_INT_DIV_ZERO: Integer division or remainder by zero")]
    public void WitnessChecksAbortAtTheirRequirementCall(string contract, string method, string left, string right, string reason)
    {
        var program = "func operation<T>(left: ref/T, right: ref/T) -> T\n    T is " + contract + "<T>\n    T.(" + contract + "<T>).Output is T\n    return left." + method + "(right)\nlet value = operation(" + left + ", " + right + ")";
        ScalarEmissionTest.EmitFixture("BuiltinArithmeticAbort" + contract, program, string.Empty, 1, "Hello.kimi:4:12: abort " + reason + "\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmNumericWitnessesReuseTheirStorage()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }
}
