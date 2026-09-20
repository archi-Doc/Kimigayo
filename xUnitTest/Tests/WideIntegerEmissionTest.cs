// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class WideIntegerEmissionTest
{
    private const string Min = "-170141183460469231731687303715884105728";
    private const string Max = "170141183460469231731687303715884105727";
    private const string UnsignedMax = "340282366920938463463374607431768211455";
    private const string High = "18446744073709551616";
    private const string Overflow = "KIMI_E_INT_OVERFLOW: Integer overflow";
    private const string Shift = "KIMI_E_INT_SHIFT_COUNT: Shift count out of range";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "WideLiteral", "let x: i128 = 1", string.Empty },
        { "WideFalse", "if false\n    let x: u128 = 1", string.Empty },
        { "WideUnusedSignature", "func unused(x?: i128) -> i128 => x\n()", string.Empty },
        { "WideUnusedBody", "func unused() -> ()\n    let x: i128 = 1\n()", string.Empty },
        { "WideOldConversion", "let x = 1\nx@i128", string.Empty },
        { "WideEmptyArray", "let value: [0 of i128] = []", string.Empty },
        { "WideSignedZero", "let x: u128 = -0\nif x == 0 => Console.writeLine(\"ok\")", "ok\n" },
        { "WideMatch", $"func f(x?: u128) -> i128\n    return match x\n        {UnsignedMax} => {Min}\n        {High} => {Max}\n        _ => 0\nif f({UnsignedMax}) == {Min} and f({High}) == {Max} => Console.writeLine(\"ok\")", "ok\n" },
        { "WideGuard", $"let x: u128 = {UnsignedMax}\nmatch x\n    let y if y == {High} => Console.writeLine(\"bad\")\n    let z if z == {UnsignedMax} => Console.writeLine(\"ok\")\n    _ => Console.writeLine(\"bad\")", "ok\n" },
        { "WideTuple", $"var x = (1@u128, \"old\", ({Min}@i128, true))\nlet y = x\nx = y\nConsole.writeLine(\"ok\")", "ok\n" },
        { "WideArray", $"let x: [2 of u128] = [{High}, {UnsignedMax}]\nlet y = x\nConsole.writeLine(\"ok\")", "ok\n" },
        { "WideMixedAbi", $"func f(a?: u8, b?: i128, c?: f64, d?: u128, e?: i128, f?: string) -> u128\n    Console.writeLine(f)\n    if a == 255 and b == {Min} and c == 1.5 and e == {Max} => return d\n    return 0\nif f(255, {Min}, 1.5, {UnsignedMax}, {Max}, \"call\") == {UnsignedMax} => Console.writeLine(\"ok\")", "call\nok\n" },
        { "WideOrder", "func left() -> u128\n    Console.writeLine(\"left\")\n    return 3\nfunc right() -> u128\n    Console.writeLine(\"right\")\n    return 7\nif left() * right() == 21 => Console.writeLine(\"ok\")", "left\nright\nok\n" },
        { "WideSkipped", $"var x: i128 = {Max}\nif true or x + 1 > 0 => Console.writeLine(\"ok\")", "ok\n" },
        { "WideCountOriginal", "var x: u8 = 1\nlet n: u128 = 7\nif (x << n) == 128 => Console.writeLine(\"ok\")", "ok\n" },
        { "WideSignedMultiplyEdge", $"var x: i128 = {Min}\nvar y: i128 = 1\nif x * y == {Min} and x * 0 == 0 and (x + 1) * -1 == {Max} => Console.writeLine(\"ok\")", "ok\n" },
        { "WideMultiplyCrossLimb", $"var x: u128 = {High}\nvar y: u128 = 18446744073709551615\nif x * y == 340282366920938463444927863358058659840 and y * y == 340282366920938463426481119284349108225 => Console.writeLine(\"ok\")", "ok\n" },
        { "WideHex", "let x: u128 = 0xFFFF_FFFF_FFFF_FFFF_FFFF_FFFF_FFFF_FFFF\nlet y: i128 = -0x80000000000000000000000000000000\nif x >> 127 == 1 and y >> 127 == -1 => Console.writeLine(\"ok\")", "ok\n" },
    };

    public static IEnumerable<object[]> ForbiddenOperations()
    {
        foreach (var type in new[] { "i128", "u128" })
        {
            foreach (var op in new[] { "/", "%", "/=", "%=" })
            {
                yield return [$"var x: {type} = 1\nx {op} 1"];
                yield return [$"if false\n    var x: {type} = 1\n    x {op} 1"];
                yield return [$"func unused()\n    var x: {type} = 1\n    x {op} 1\n()"];
            }

            foreach (var floating in new[] { "f32", "f64" })
            {
                yield return [$"func unused(x?: {type}) => x@{floating}\n()"];
                yield return [$"func unused(x?: {floating}) => x@{type}\n()"];
                yield return [$"if false\n    let x: {type} = 1\n    x@{floating}"];
                yield return [$"if false\n    let x: {floating} = 1.0\n    x@{type}"];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ValuesAndExistingConsumersExecute(string name, string source, string expected)
        => ScalarEmissionTest.EmitFixture(name, source, expected);

    [Theory]
    [InlineData("i128", Min, Max)]
    [InlineData("u128", "0", UnsignedMax)]
    public void AllSupportedOperationsAndResultStorageExecute(string type, string minimum, string maximum)
    {
        var signed = type == "i128";
        var source = $"func snapshot(x?: {type}) -> {type}\n    var result = x\n    defer => result = 0\n    return result\n" +
            $"var x: {type} = 9\nx += 3\nx -= 2\nx *= 2\nx |= 4\nx &= 6\nx ^= 3\nx <<= 1\nx >>= 1\nlet old = x++\n++x\nlet current = --x\nx--\n" +
            $"let low: {type} = {minimum}\nlet high: {type} = {maximum}\nlet top: {type} = 1 << 127\nvar choice = true\nlet phi = if choice => snapshot(high) else => low\n" +
            $"if x == 7 and old == 7 and current == 8 and low < high and high > low and high >= high and low <= low and (top >> 127) == {(signed ? "-1" : "1")} and phi == high and snapshot(low) == low => Console.writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("WideOperations" + type, source, "ok\n");
    }

    [Theory]
    [InlineData("SignedAdd", "i128", Max, "x += 1")]
    [InlineData("UnsignedAdd", "u128", UnsignedMax, "++x")]
    [InlineData("SignedSubtract", "i128", Min, "x -= 1")]
    [InlineData("UnsignedSubtract", "u128", "0", "x--")]
    [InlineData("SignedMultiply", "i128", Max, "x *= 2")]
    [InlineData("UnsignedMultiply", "u128", UnsignedMax, "x *= 2")]
    [InlineData("SignedNegate", "i128", Min, "-x")]
    [InlineData("SignedMinMultiply", "i128", Min, "x *= -1")]
    [InlineData("UnsignedCrossMultiply", "u128", High, "x *= " + High)]
    [InlineData("SignedIncrement", "i128", Max, "x++")]
    [InlineData("SignedDecrement", "i128", Min, "--x")]
    public void OverflowAbortsBeforeCommit(string name, string type, string initial, string expression)
        => ScalarEmissionTest.EmitFixture("WideOverflow" + name, $"var x: {type} = {initial}\n{expression}\nConsole.writeLine(\"bad\")", string.Empty, 1, $"Hello.kimi:2:1: abort {Overflow}\n");

    [Theory]
    [InlineData("u128", "u8", "128")]
    [InlineData("i128", "i128", Min)]
    [InlineData("u128", "u128", UnsignedMax)]
    [InlineData("u8", "u128", High)]
    [InlineData("u8", "i128", "-1")]
    public void CountsAreCheckedAtOriginalWidth(string type, string countType, string count)
        => ScalarEmissionTest.EmitFixture($"WideShiftFail{type}{countType}{count}", $"var x: {type} = 1\nlet n: {countType} = {count}\nx <<= n", string.Empty, 1, $"Hello.kimi:3:1: abort {Shift}\n");

    [Theory]
    [MemberData(nameof(ForbiddenOperations))]
    public void ProfileExclusionsAreDiagnosedBeforeOptimization(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Unsupported);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("i128", "170141183460469231731687303715884105728")]
    [InlineData("i128", "-170141183460469231731687303715884105729")]
    [InlineData("u128", "340282366920938463463374607431768211456")]
    [InlineData("u128", "-1")]
    public void LiteralOverflowIsAStaticError(string type, string value)
    {
        var c = MinimalEmissionTest.Analyze($"let x: {type} = {value}");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("i128")]
    [InlineData("u128")]
    public void CorruptHighConstantBitsAreRejected(string type)
    {
        var c = MinimalEmissionTest.Analyze($"let x: {type} = 1");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var id = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.Constant);
        body.Values[id] = body.Values[id] with { Constant = ((Int128)1 << 64) + 1 };
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        c.Ownership.Analyze();
        Assert.True(c.Emission.Validate(out error), error);
    }

    [Fact]
    public void CorruptParameterHighBitsCannotAliasAValidIndex()
    {
        var c = MinimalEmissionTest.Analyze("func f(x?: i128) -> i128 => x\nf(1)");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "f");
        var id = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.Parameter);
        Assert.True(id >= 0);
        body.Values[id] = body.Values[id] with { Constant = (Int128)1 << 64 };
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void SerializationRebuildsBothHalvesOfWideValues()
    {
        var c = MinimalEmissionTest.Analyze($"let x: u128 = {UnsignedMax}\nlet y: i128 = {Min}\nif x >> 127 == 1 and y >> 127 == -1 => Console.writeLine(\"ok\")");
        using var original = new StringWriter();
        Assert.True(c.Emission.WriteIr(original, out var error), error);
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        Assert.Same(restored.Kotonoha, kotonoha);
        kotonoha.OnDeserialized(restored);
        restored.Bind();
        restored.Binding.CheckStartup(OutputKind.Application);
        restored.Ownership.Analyze();
        using var roundTrip = new StringWriter();
        Assert.True(restored.Emission.WriteIr(roundTrip, out error), error);
        Assert.Equal(original.ToString(), roundTrip.ToString());
        ScalarEmissionTest.WriteFixture("WideRoundTrip", roundTrip.ToString(), "ok\n");
    }

    [Fact]
    public void CorruptWideDivisionCannotBypassProfileValidation()
    {
        var c = MinimalEmissionTest.Analyze("var x: i128 = 1\nlet y = x + 1");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var id = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.Binary);
        body.Values[id] = body.Values[id] with { Operator = KotoKind.Slash };
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void WarmWidePlansAndFormattingAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze($"func f(x?: u128) -> u128 => x * 1\nvar x: u128 = {UnsignedMax}\nlet y = if true => f(x) else => 0\nlet z = y@i128");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Ownership.Analyze().IsVerified || !c.Emission.WriteIr(TextWriter.Null, out _))
            {
                throw new InvalidOperationException("Wide integer analysis or emission failed.");
            }
        }));
    }
}
