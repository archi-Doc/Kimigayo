// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class NumericReplacementTest
{
    [Theory]
    [InlineData("2.5")]
    [InlineData("18446744073709551615")]
    [InlineData("0xffffffffffffffff")]
    [InlineData("1.00000000000000000001")]
    public void ReplacementPreservesDigitsAndDiagnosticLocation(string literal)
    {
        var c = MinimalEmissionTest.Analyze("let value = 0");
        var variable = Variable(c);
        var old = variable.InitializerKoto!;
        var donor = MinimalEmissionTest.Analyze("let donor = " + literal);
        var number = Assert.IsType<NumberLiteralKoto>(Variable(donor).InitializerKoto);
        Assert.True(KotoHelper.Replace(variable, old, number));
        Assert.Same(old.CodeContext, number.CodeContext);
        Assert.Equal(old.Span, number.Span);
        Assert.Equal(literal, number.SourceSpelling.ToString());
        var another = MinimalEmissionTest.Analyze("let value = 123456");
        var next = Variable(another);
        Assert.True(KotoHelper.Replace(next, next.InitializerKoto!, number));
        Assert.Equal(literal, number.SourceSpelling.ToString());
    }

    [Fact]
    public void NumericReplacementRecomputesInferredPropertyType()
    {
        var c = MinimalEmissionTest.Analyze("enum Hidden\n    A\npublic group Api\n    public let item = Hidden.A");
        Assert.False(c.Binding.Result.IsComplete);
        var property = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api").Members.OfType<PropertyKoto>().Single();
        var donor = MinimalEmissionTest.Analyze("let replacement = 1");
        var replacement = Variable(donor).InitializerKoto!;
        Assert.True(KotoHelper.Replace(property, property.InitializerKoto!, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(property.BoundSymbol!.Property!.IsVerified);
    }

    [Theory]
    [InlineData("Float", "f64", "2.5", "2.5")]
    [InlineData("RoundedF32", "f32", "16777217", "16777216.0")]
    [InlineData("WideFloat", "f64", "18446744073709551615", "18446744073709551616.0")]
    [InlineData("Hex", "u64", "0xffffffffffffffff", "18446744073709551615")]
    [InlineData("U128", "u128", "340282366920938463463374607431768211455", "340282366920938463463374607431768211455")]
    public void ReplacementFittingAndEmissionUseTheNewValue(string name, string type, string literal, string expected)
    {
        var c = MinimalEmissionTest.Analyze($"let value: {type} = 0@{type}\nif value == {expected} => writeLine(\"ok\")");
        var variable = Variable(c);
        var donor = MinimalEmissionTest.Analyze("let donor = " + literal);
        var replacement = Variable(donor).InitializerKoto!;
        var conversion = Assert.IsType<ConversionKoto>(variable.InitializerKoto);
        Assert.True(KotoHelper.Replace(conversion, conversion.Left, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified);
        using var writer = new StringWriter();
        Assert.True(c.Emission.WriteIr(writer, out var error), error);
        ScalarEmissionTest.WriteFixture("NumericReplacement" + name, writer.ToString(), "ok\n");
        var written = c.Kotonoha.GeneratedFunction!.Body!.ToString();
        Assert.True(MinimalEmissionTest.Analyze(written).Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("u8", "256")]
    [InlineData("i32", "2147483648")]
    [InlineData("f32", "3.5e38")]
    public void ReplacementCannotReuseTheOldLiteralsSuccessfulFit(string type, string literal)
    {
        var zero = type == "f32" ? "0.0" : "0";
        var c = MinimalEmissionTest.Analyze($"let value: {type} = {zero}");
        Assert.True(c.Binding.Result.IsComplete);
        var variable = Variable(c);
        var old = variable.InitializerKoto!;
        var donor = MinimalEmissionTest.Analyze("let donor = " + literal);
        Assert.True(KotoHelper.Replace(variable, old, Variable(donor).InitializerKoto!));
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.InvalidLiteral && x.Node.Span == old.Span);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void ReplacementPreservesFloatWritingBeforeAndAfterLazyParsing()
    {
        var c = MinimalEmissionTest.Analyze("let value = 0");
        var variable = Variable(c);
        var parsed = ParseTestHelper.ParseSingleFunction("func donor() => 2.5000000000000000001");
        var number = Assert.IsType<NumberLiteralKoto>(parsed.ExpressionBody);
        Assert.True(KotoHelper.Replace(variable, variable.InitializerKoto!, number));
        Assert.Equal("2.5000000000000000001", number.ToString());
        Assert.True(c.Bind().IsComplete);
        Assert.Equal("2.5000000000000000001", number.ToString());
    }

    [Fact]
    public void WarmRebindingAfterRelocationAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("let value = 0");
        var variable = Variable(c);
        var donor = MinimalEmissionTest.Analyze("let donor = 2");
        Assert.True(KotoHelper.Replace(variable, variable.InitializerKoto!, Variable(donor).InitializerKoto!));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Numeric replacement Binding failed.");
            }
        }));
    }

    private static VariableKoto Variable(Compilation c)
        => c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<VariableKoto>().Single();
}
