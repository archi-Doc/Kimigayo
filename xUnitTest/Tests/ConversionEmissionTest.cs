// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Numerics;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConversionEmissionTest
{
    private const string Reason = "KIMI_E_INT_CONVERSION: Integer conversion out of range";
    private const string Consumers = "func id(x: u32) -> u32 => x\nfunc convert(x: i32) -> u32\n    var y = x\n    defer => y = -1\n    return y@u32\nvar x: i32 = 42\nvar y: u32 = x@u32\nlet z = if true => x@u32 else => 1@u32\ny = x@u32\nif y == z and id(x@u32) == 42 and convert(x) == 42 and (x@u32 > 0) => writeLine(\"ok\")";

    public static IEnumerable<object[]> Pairs()
    {
        string[] types = ["i8", "u8", "i16", "u16", "i32", "u32", "i64", "u64", "isize", "usize"];
        foreach (var source in types)
        {
            foreach (var target in types)
            {
                yield return [source, target];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void EveryPairChecksItsMathematicalRange(string source, string target)
    {
        var (sourceMin, sourceMax) = Range(source);
        var (targetMin, targetMax) = Range(target);
        var low = BigInteger.Max(sourceMin, targetMin).ToString(CultureInfo.InvariantCulture);
        var high = BigInteger.Min(sourceMax, targetMax).ToString(CultureInfo.InvariantCulture);
        var header = $"func convert(x: {source}) -> {target} => x@{target}\n";
        var program = header + $"if convert({low}) == {low} and convert({high}) == {high} => writeLine(\"ok\")";
        var c = MinimalEmissionTest.Analyze(program);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var conversions = Enumerable.Range(0, module.FunctionCount).SelectMany(i => module.GetFunction(i).Instructions).Where(x => x.Opcode == EmissionOpcode.Convert).ToArray();
        var checkedRange = sourceMin < targetMin || sourceMax > targetMax;
        var sourceWidth = ScalarTypes.Width(BoundType.Primitives[source], 64);
        var targetWidth = ScalarTypes.Width(BoundType.Primitives[target], 64);
        if (sourceWidth == targetWidth && !checkedRange)
        {
            Assert.Empty(conversions);
        }
        else
        {
            var conversion = Assert.Single(conversions);
            Assert.Equal(checkedRange ? ArithmeticCheckKind.Conversion : ArithmeticCheckKind.None, conversion.Check);
            Assert.Equal(sourceMin < targetMin ? "slt" : null, conversion.LowerPredicate);
            Assert.Equal(sourceMax > targetMax ? source[0] == 'i' ? "sgt" : "ugt" : null, conversion.UpperPredicate);
            Assert.Equal(sourceWidth == targetWidth ? null : sourceWidth > targetWidth ? "trunc" : source[0] == 'i' ? "sext" : "zext", conversion.ScalarOperator);
        }

        ScalarEmissionTest.EmitFixture($"ConversionPair{source}{target}", program, "ok\n");
        var column = header.IndexOf("x@", StringComparison.Ordinal) + 1;
        if (sourceMin < targetMin)
        {
            Abort($"ConversionLower{source}{target}", header + $"convert({(targetMin - 1).ToString(CultureInfo.InvariantCulture)})\nwriteLine(\"bad\")", 1, column);
        }

        if (sourceMax > targetMax)
        {
            Abort($"ConversionUpper{source}{target}", header + $"convert({(targetMax + 1).ToString(CultureInfo.InvariantCulture)})\nwriteLine(\"bad\")", 1, column);
        }
    }

    [Theory]
    [InlineData("ConversionConsumers", Consumers, "ok\n")]
    [InlineData("ConversionLiteral", "let x = ((-128))@i8\nlet y = 18446744073709551615@u64\nlet z = 255@(u8)\nif x == -128 and y == 18446744073709551615 and z == 255 => writeLine(\"ok\")", "ok\n")]
    [InlineData("ConversionSignedParentheses", "if -(128)@i8 == -128 => writeLine(\"ok\")", "ok\n")]
    [InlineData("ConversionWideNegative", "let x: i8 = -128\nif x@i64 == -128 => writeLine(\"ok\")", "ok\n")]
    [InlineData("ConversionUnsignedHigh", "let x: u8 = 200\nif x@i64 == 200 => writeLine(\"ok\")", "ok\n")]
    [InlineData("ConversionTypedOverload", "func f(x: u8) -> i32 => 1\nfunc f(x: i32) -> i32 => 2\nif f(1@u8) == 1 => writeLine(\"ok\")", "ok\n")]
    [InlineData("ConversionEvaluateOnce", "var x = 1\nlet y = (x++)@u8\nif y == 1 and x == 2 => writeLine(\"ok\")", "ok\n")]
    [InlineData("ConversionSkipped", "var x = -1\nif true or x@u8 == 0 => writeLine(\"ok\")", "ok\n")]
    [InlineData("ConversionExit", "loop\n    (exit)@u8\nwriteLine(\"ok\")", "ok\n")]
    [InlineData("ConversionReturn", "func f() -> i32\n    (return 42)@u8\nif f() == 42 => writeLine(\"ok\")", "ok\n")]
    [InlineData("ConversionNeverEvidence", "func f() -> i32\n    let x = if false => (return 42)@u8 else => 300\n    return x\nif f() == 300 => writeLine(\"ok\")", "ok\n")]
    [InlineData("ConversionLoopEvidence", "let x = if false => (loop => ())@u8 else => 300\nif x == 300 => writeLine(\"ok\")", "ok\n")]
    [InlineData("ConversionNeverCallEvidence", "func stop() -> Never => loop => ()\nlet x = if false => stop()@u8 else => 300\nif x == 300 => writeLine(\"ok\")", "ok\n")]
    [InlineData("ConversionAfterReturn", "func f() -> i32\n    return 42\n    let x = 300\n    x@u8\nif f() == 42 => writeLine(\"ok\")", "ok\n")]
    public void SourceOrderAndConsumersUseTheSecuredValue(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source, stdout);

    [Theory]
    [InlineData("ConversionArithmetic", "(200 + 100)@u8", 1, 1)]
    [InlineData("ConversionParenthesizedSign", "-(128)@u8", 1, 1)]
    [InlineData("ConversionChainFirst", "let x = 65536\nx@u16@u8", 2, 1)]
    [InlineData("ConversionChainSecond", "let x = 256\nx@u16@u8", 2, 1)]
    [InlineData("ConversionBeforeCleanup", "func f() -> u8\n    defer => writeLine(\"bad\")\n    let x = 256\n    return x@u8\nf()", 4, 12)]
    [InlineData("ConversionDuringCleanup", "defer => writeLine(\"bad\")\ndefer\n    let x = -1\n    x@u8", 4, 5)]
    public void InvalidConversionsAbortWithoutCleanup(string name, string source, int line, int column)
        => Abort(name, source, line, column);

    [Theory]
    [InlineData("256@u8", "InvalidLiteral")]
    [InlineData("(-129)@i8", "InvalidLiteral")]
    [InlineData("true@u8", "TypeMismatch")]
    [InlineData("'a'@u32", "TypeMismatch")]
    [InlineData("let x: u8 = 1\n-x@u8", "TypeMismatch")]
    [InlineData("let x = 1\nx@f64", "Unsupported")]
    [InlineData("5000000000@f64", "Unsupported")]
    [InlineData("let x = 1\nx@ref", "Unsupported")]
    [InlineData("let x = 1\nx@owner", "Unsupported")]
    [InlineData("3.9@i32", "Unsupported")]
    [InlineData("let x = 1\nx@i128", "Unsupported")]
    [InlineData("let flag = true\nflag@bool", "Unsupported")]
    [InlineData("let s = \"x\"\ns@string", "Unsupported")]
    [InlineData("let x = 1\nx@owner/u8", "Unsupported")]
    [InlineData("let x = 1\nx@(owner/u8)", "Unsupported")]
    [InlineData("func f(x: i32) => ()\nf(1@u8)", "NoApplicableCandidate")]
    public void InvalidAndUnimplementedAdaptationsHaveDistinctFailures(string source, string failure)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure.ToString() == failure);
        if (failure == "Unsupported")
        {
            Assert.Equal(0, c.Binding.Result.InvalidCount);
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void SameWidthChecksKeepPhiPredecessorsAndDoNotInventValues()
    {
        var c = MinimalEmissionTest.Analyze(Consumers);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        for (var i = 0; i < module.FunctionCount; i++)
        {
            var f = module.GetFunction(i);
            foreach (var conversion in f.Instructions.Where(x => x.Opcode == EmissionOpcode.Convert))
            {
                Assert.Null(conversion.ScalarOperator);
                Assert.DoesNotContain(f.Operands, x => x.Kind == EmissionOperandKind.Value && x.Value == conversion.Operation);
            }
        }

        using var writer = new StringWriter();
        module.WriteIr(writer);
        Assert.Contains("icmp slt i32", writer.ToString());
        Assert.DoesNotContain(" = add i32", writer.ToString());
    }

    [Fact]
    public void WarmConversionAnalysisAndWritingAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Consumers);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var valid = true;
        for (var i = 0; i < 128; i++)
        {
            valid &= c.Ownership.Analyze().IsVerified;
            valid &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(valid);
    }

    [Fact]
    public void OperandInferenceIsIndependentAndRebindingResetsClassification()
    {
        var c = MinimalEmissionTest.Analyze("func id<T>(x: T) -> T => x\nlet a = id(300)@u8\nlet b = (200 + 100)@u8\nlet d = ((-128))@i8");
        Assert.True(c.Binding.Result.IsComplete, string.Join("; ", c.Binding.Issues));
        var nodes = All(c.Kotonoha.RootKoto).OfType<ConversionKoto>().ToArray();
        Assert.Equal(3, nodes.Length);
        Assert.Equal(ConversionBinding.Integer, nodes[0].ConversionBinding);
        Assert.Same(BoundType.I32, nodes[0].Left.BoundType);
        Assert.Same(BoundType.I32, nodes[1].Left.BoundType);
        Assert.Equal(ConversionBinding.Literal, nodes[2].ConversionBinding);
        foreach (var node in nodes)
        {
            node.ConversionBinding = ConversionBinding.Abrupt;
        }

        Assert.True(c.Bind().IsComplete);
        Assert.Equal(ConversionBinding.Integer, nodes[0].ConversionBinding);
        Assert.Equal(ConversionBinding.Integer, nodes[1].ConversionBinding);
        Assert.Equal(ConversionBinding.Literal, nodes[2].ConversionBinding);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var complete = true;
        for (var i = 0; i < 128; i++)
        {
            complete &= c.Bind().IsComplete;
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(complete);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("count")]
    [InlineData("source-type")]
    [InlineData("result-type")]
    [InlineData("unary")]
    [InlineData("classification")]
    public void MalformedConversionPlansNeverWriteIr(string mutation)
    {
        var c = MinimalEmissionTest.Analyze("let flag = true\nlet x: u32 = 1\nlet y = x@i32");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var id = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.Convert);
        var value = body.Values[id];
        switch (mutation)
        {
            case "input": body.ValueOperands[value.Start] = -1; break;
            case "count": body.Values[id] = value with { Count = 0 }; break;
            case "source-type":
                body.ValueOperands[value.Start] = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.Constant && x.Constant == 1);
                break;
            case "result-type":
                var place = body.Operations[id].Place;
                body.PlaceStorage[place] = body.Places[place] with { Type = BoundType.Primitives["u32"] };
                break;
            case "unary": body.Values[id] = value with { Kind = OwnershipValueKind.Unary }; break;
            case "classification": ((ConversionKoto)body.Operations[id].Source).ConversionBinding = ConversionBinding.Literal; break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void NoncompletingOperandsHaveNoConversionValueOrInstruction()
    {
        const string Source = "func stop() -> Never => loop => ()\nstop()@u8";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.All(c.Ownership.Bodies, body => Assert.DoesNotContain(body.Values, x => x.Kind == OwnershipValueKind.Convert));
        for (var i = 0; i < module.FunctionCount; i++)
        {
            Assert.DoesNotContain(module.GetFunction(i).Instructions, x => x.Opcode == EmissionOpcode.Convert);
        }

        ScalarEmissionTest.EmitFixture("ConversionNever", Source, string.Empty, timeoutMilliseconds: 500);
    }

    [Theory]
    [InlineData("stop()")]
    [InlineData("(if true => stop() else => stop())")]
    [InlineData("stop()@u16@i16")]
    public void NestedNoncompletingOperandsDoNotConstrainOtherBranches(string operand)
    {
        var c = MinimalEmissionTest.Analyze($"func stop() -> Never => loop => ()\nlet x = if false => ({operand})@u8 else => 300\nif x == 300 => writeLine(\"ok\")");
        Assert.True(c.Binding.Result.IsComplete, string.Join("; ", c.Binding.Issues));
        Assert.True(c.Emission.Validate(out var error), MinimalEmissionTest.Describe(c, error));
        var field = All(c.Kotonoha.RootKoto).OfType<FieldKoto>().Single(x => x.BoundSymbol?.Name == "x");
        Assert.Same(BoundType.I32, field.BoundSymbol!.Type);
    }

    private static IEnumerable<Koto> All(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var nested in All(child))
            {
                yield return nested;
            }
        }
    }

    private static (BigInteger Min, BigInteger Max) Range(string name)
    {
        var width = ScalarTypes.Width(BoundType.Primitives[name], 64);
        return name[0] == 'i' ? (-(BigInteger.One << (width - 1)), (BigInteger.One << (width - 1)) - 1) : (0, (BigInteger.One << width) - 1);
    }

    private static void Abort(string name, string source, int line, int column)
        => ScalarEmissionTest.EmitFixture(name, source, string.Empty, 1, $"Hello.kimi:{line}:{column}: abort {Reason}\n");
}
