// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class FloatConversionEmissionTest
{
    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "FloatConvertWiden", "func widen(x: f32) -> f64 => x@f64\nif widen(1.5) == 1.5 => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatConvertSubnormal", "var x: f32 = 1e-45\nif x@f64 == 1.401298464324817e-45 => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatConvertMaximum", "var x: f32 = 3.4028234663852886e38\nif x@f64 == 3.4028234663852886e38 => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatConvertZeros", "var x: f32 = -0.0\nvar y: f32 = 0.0\nif 1.0 / x@f64 < 0.0 and 1.0 / y@f64 > 0.0 => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatConvertNonfinite", "func widen(x: f32) -> f64 => x@f64\nvar zero: f32 = 0.0\nvar one: f32 = 1.0\nlet nan = widen(zero / zero)\nlet inf = widen(one / zero)\nlet neg = widen(-one / zero)\nif nan != nan and inf > 1.0 and neg < -1.0 => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatConvertLiteralRoundOnce", "let x = ((1.00000005960464477539062500000000000000000000000000001))@f32\nif x == 1.00000011920928955078125 => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatConvertLiteralTiesEven", "let x = 1.000000059604644775390625@f32\nlet y = 1.000000178813934326171875@f32\nif x == 1.0 and y == 1.0000002384185791015625 => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatConvertLiteralSigns", "let x = (-0.0)@f32\nlet y = +1.25@f64\nif 1.0 / x@f64 < 0.0 and y == 1.25 => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatConvertLiteralUnderflow", "let x = -7e-46@f32\nif x == 0.0 and 1.0 / x@f64 < 0.0 => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatConvertChain", "let x: f32 = 1.25\nif x@f32@f64@f64 == 1.25 => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatConvertPhi", "func widen(x: f32, flag: bool) -> f64\n    var y: f64 = 0.0\n    defer => y = 0.0\n    y = if flag => x@f64 else => (-x)@f64\n    return y@f64\nif widen(1.25, true) == 1.25 and widen(1.25, false) == -1.25 => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatConvertOrder", "func first() -> f32\n    Console.writeLine(\"first\")\n    return 1.25\nfunc second() -> f32\n    Console.writeLine(\"second\")\n    return 2.5\nfunc sum(a: f64, b: f64) -> f64 => a + b\nif sum(first()@f64, second()@f64) == 3.75 => Console.writeLine(\"ok\")", "first\nsecond\nok\n" },
        { "FloatConvertAggregate", "var x: f32 = -0.0\nlet a = (x@f64, \"owned\")\nlet b: [2 of f64] = [x@f64, 1.25@f64]\nlet c = a@move\nConsole.writeLine(\"ok\")", "ok\n" },
        { "FloatConvertAbrupt", "func f() -> f64\n    (return 1.25)@f32\nif f() == 1.25 => Console.writeLine(\"ok\")", "ok\n" },
        { "FloatConvertUnaryAlias", "let x: f32 = -0.0\nif 1.0 / (+x)@f64 < 0.0 => Console.writeLine(\"ok\")", "ok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void WideningIdentityAndLiteralFittingExecute(string name, string source, string expected)
        => ScalarEmissionTest.EmitFixture(name, source, expected);

    [Theory]
    [InlineData("f32")]
    [InlineData("f64")]
    public void IdentityPreservesNonfiniteValuesAndHasNoConversionInstruction(string type)
    {
        var source = $"func id(x: {type}) -> {type} => x@{type}\nvar z: {type} = 0.0\nvar one: {type} = 1.0\nlet nan = id(z / z)\nlet zero = id(-0.0)\nlet inf = id(one / z)\nif nan != nan and 1.0 / zero < 0.0 and inf > 1.0 => Console.writeLine(\"ok\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        for (var i = 0; i < module.FunctionCount; i++)
        {
            Assert.DoesNotContain(module.GetFunction(i).Instructions, x => x.Opcode == EmissionOpcode.Convert);
        }

        ScalarEmissionTest.EmitFixture("FloatConvertIdentity" + type, source, "ok\n");
    }

    [Theory]
    [InlineData("3.5e38@f32")]
    [InlineData("-3.5e38@f32")]
    [InlineData("1e309@f64")]
    [InlineData("if false => 3.5e38@f32")]
    [InlineData("func unused() => 3.5e38@f32\n()")]
    public void LiteralFittingFailuresAreStatic(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    // The Windows profile explicitly excludes typed 128-bit floating conversions.
    [Theory]
    [InlineData("let x: i128 = 1\nx@f32")]
    [InlineData("let x: u128 = 1\nx@f64")]
    [InlineData("let x: f64 = 1.0\nx@u128")]
    [InlineData("let x: f32 = 1.0\nx@i128")]
    public void RemainingConversionsAreNotMistakenForWidening(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("target")]
    public void CorruptWideningPlansFailBeforeWriting(string defect)
    {
        var c = MinimalEmissionTest.Analyze("var x: f32 = 1.25\nlet y = x@f64");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var id = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.Convert);
        var operation = body.Operations[id];
        if (defect == "binding")
        {
            Assert.IsType<ConversionKoto>(operation.Source).ConversionBinding = ConversionBinding.Integer;
        }
        else
        {
            body.PlaceStorage[operation.Place] = body.Places[operation.Place] with { Type = BoundType.Primitives["u64"] };
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        Assert.True(c.Emission.Validate(out error), error);
    }

    [Fact]
    public void LiteralAdaptationSurvivesSerializationWithoutDoubleRounding()
    {
        const string Source = "let x = 1.00000005960464477539062500000000000000000000000000001@f32\nif x@f64 == 1.00000011920928955078125 => Console.writeLine(\"ok\")";
        var c = MinimalEmissionTest.Analyze(Source);
        using var original = new StringWriter();
        Assert.True(c.Emission.WriteIr(original, out var error), error);
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(restored);
        restored.Bind();
        restored.Binding.CheckStartup(OutputKind.Application);
        restored.Ownership.Analyze();
        using var writer = new StringWriter();
        Assert.True(restored.Emission.WriteIr(writer, out error), error);
        Assert.Equal(original.ToString(), writer.ToString());
        ScalarEmissionTest.WriteFixture("FloatConvertRoundTrip", writer.ToString(), "ok\n");
    }

    [Fact]
    public void WarmFloatConversionPlansAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: f32) -> f64 => x@f64\nlet x = 1.25@f32\nlet y = if x > 0.0 => f(x) else => x@f64\nlet z = y@f64");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Ownership.Analyze().IsVerified || !c.Emission.WriteIr(TextWriter.Null, out _))
            {
                throw new InvalidOperationException("Float conversion analysis or emission failed.");
            }
        }));
    }
}
