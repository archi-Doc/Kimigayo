// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class BitwiseEmissionTest
{
    private const string Reason = "KIMI_E_INT_SHIFT_COUNT: Shift count out of range";
    private const string Snapshot = "func result() -> i32\n    var x = 3\n    defer => x <<= 2\n    return x << 1\nif result() == 6 => writeLine(\"ok\")";

    public static TheoryData<string, string> Fixtures => new()
    {
        { "BitwiseMasks", "if (10 & 6) == 2 and (10 | 6) == 14 and (10 ^ 6) == 12 => writeLine(\"ok\")" },
        { "BitwiseSigned", "if (-1 & -2147483648) == -2147483648 and (-2147483648 | 2147483647) == -1 and (-1 ^ 2147483647) == -2147483648 => writeLine(\"ok\")" },
        { "BitwiseShiftLimits", "var x = -2147483648\nif (1 << 31) == x and (x << 1) == 0 and (-1 << 1) == -2 and (2147483647 << 1) == -2 and (x << 0) == x and (x >> 31) == -1 and (2147483647 >> 31) == 0 and (-3 >> 1) == -2 and (x >> 0) == x => writeLine(\"ok\")" },
        { "BitwiseCompound", "var x = 10\nlet unit = (x &= 6)\nx |= 8\nx ^= 6\nx <<= 2\nx >>= 3\nif x == 6 => writeLine(\"ok\")" },
        { "BitwiseOrder", "var x = 1\nlet y = x++ << x++\nlet z = x++ | x++\nlet a = x++ ^ x++\nlet b = x++ & x++\nif y == 4 and z == 7 and a == 3 and b == 0 and x == 9 => writeLine(\"ok\")" },
        { "BitwiseCompoundOrder", "var x = 2\nx <<= x++\nvar y = 7\ny &= y++\nvar z = 1\nz |= z++\nvar a = 5\na ^= a++\nvar b = 8\nb >>= b++ - 6\nif x == 8 and y == 7 and z == 1 and a == 0 and b == 2 => writeLine(\"ok\")" },
        { "BitwiseShortCircuit", "var n = 32\nif n < 32 and (1 << n) == 0 => writeLine(\"bad\")\nif true or (1 >> -1) == 0 => ()\nif false => 1 << 32\nloop\n    exit\n    1 >> -2147483648\nwriteLine(\"ok\")" },
        { "BitwisePhi", "var c = true\nvar x = 3\nlet y = if c => x << 2 else => x >> 1\nlet z = if c\n    yield y | 1\nelse\n    yield 0\nif y == 12 and z == 13 => writeLine(\"ok\")" },
        { "BitwiseSnapshot", Snapshot },
        { "BitwiseDeferredLoop", "var x = 1\nloop\n    defer => x <<= 1\n    if x == 8 => exit\n    continue\nif x == 16 => writeLine(\"ok\")" },
        { "BitwiseOperandTransfer", "var x = 3\nlet y = outer: do\n    x <<= (if true => exit to outer: 9 else => 32)\n    exit to outer: 0\nif x == 3 and y == 9 => writeLine(\"ok\")" },
        { "BitwiseFunctions", "func left() -> i32\n    writeLine(\"ok\")\n    return 3\nfunc count() -> i32\n    writeLine(\"bad\")\n    return 1\npublic func main() -> ()\n    let n = left()\n    if n == 3 or count() == 1 => ()\n    if (n & 1) == 0 and count() == 1 => ()" },
        { "BitwiseCallOrder", "func left() -> i32\n    writeLine(\"left\")\n    return 3\nfunc count() -> i32\n    writeLine(\"count\")\n    return 1\nif (left() << count()) == 6 => writeLine(\"ok\")" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EmitsBitwiseFixtures(string name, string source)
        => ScalarEmissionTest.EmitFixture(name, source, name == "BitwiseCallOrder" ? "left\ncount\nok\n" : "ok\n");

    public static TheoryData<string, int, bool> InvalidCounts
    {
        get
        {
            var data = new TheoryData<string, int, bool>();
            foreach (var op in new[] { "<<", ">>", "<<=", ">>=" })
            {
                foreach (var count in new[] { -1, 32, int.MinValue, int.MaxValue })
                {
                    data.Add(op, count, false);
                    data.Add(op, count, true);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(InvalidCounts))]
    public void InvalidCountsAbortWithoutMasking(string op, int count, bool variable)
    {
        var compound = op.Length == 3;
        var number = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var prefix = variable ? $"var x = 1\nvar n = {number}\n" : compound ? "var x = 1\n" : string.Empty;
        var source = prefix + $"{(variable || compound ? "x" : "1")} {op} {(variable ? "n" : number)}\nwriteLine(\"bad\")";
        var name = $"BitwiseInvalid{(op[0] == '<' ? "Left" : "Right")}{(compound ? "Assign" : "Binary")}{number}{(variable ? "Variable" : "Literal")}";
        ScalarEmissionTest.EmitFixture(name, source, string.Empty, 1, $"Hello.kimi:{(variable ? 3 : compound ? 2 : 1)}:1: abort {Reason}\n");
    }

    [Theory]
    [InlineData("BitwiseOperandOverflow", "var n = 2147483647\ndefer => writeLine(\"bad\")\n1 << (n + 1)", "", 3, 7, true)]
    [InlineData("BitwiseInnerFailure", "var x = 3\nx <<= 1 << 32", "", 2, 7, false)]
    [InlineData("BitwiseOuterFailure", "var x = 3\nx <<= 1 << 5", "", 2, 1, false)]
    [InlineData("BitwiseBeforeCleanup", "func f() -> i32\n    defer => writeLine(\"bad\")\n    return 1 << -1\nf()", "", 3, 12, false)]
    [InlineData("BitwiseDuringCleanup", "defer => writeLine(\"bad\")\ndefer\n    writeLine(\"begin\")\n    1 >> 32\n    writeLine(\"bad\")", "begin\n", 4, 5, false)]
    public void FailureStopsEvaluationAndCleanup(string name, string source, string stdout, int line, int column, bool overflow)
        => ScalarEmissionTest.EmitFixture(name, source, stdout, 1, $"Hello.kimi:{line}:{column}: abort {(overflow ? "KIMI_E_INT_OVERFLOW: Integer overflow" : Reason)}\n");

    [Fact]
    public void ShiftChecksKeepPhysicalPhiPredecessorsAndNeedNoExtraSlots()
    {
        var c = MinimalEmissionTest.Analyze("var x = 3\nlet y = if true => x << 2 else => x >> 1\nx ^= y");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var f = module.GetFunction(0);
        using var writer = new StringWriter();
        module.WriteIr(writer);
        var ir = writer.ToString();
        var shifts = f.Instructions.Where(x => x.Check == ArithmeticCheckKind.Shift).ToArray();
        Assert.Equal(2, shifts.Length);
        foreach (var instruction in shifts)
        {
            Assert.True(instruction.Constant >= 0);
            Assert.Contains($"%invalid{instruction.Operation} = icmp uge i32 ", ir);
            Assert.Contains($"br i1 %invalid{instruction.Operation}, label %abort{instruction.Operation}, label %b{instruction.Place}\n", ir);
            Assert.Contains($"b{instruction.Place}:\n  %v{instruction.Operation} = {instruction.ScalarOperator} i32 ", ir);
            Assert.False(instruction.IsComparison);
        }

        var phi = Assert.Single(f.Instructions, x => x.Opcode == EmissionOpcode.Phi);
        var inputs = f.GetOperands(phi);
        for (var i = 0; i < inputs.Length; i += 2)
        {
            var producer = inputs[i].Value;
            Assert.Equal(Assert.Single(shifts, x => x.Operation == producer).Place, inputs[i + 1].Value);
        }

        Assert.All(f.Slots, x => Assert.Equal(OwnershipPlaceKind.Local, c.Ownership.Bodies[0].Places[x.Place].Kind));
        Assert.DoesNotContain(" nsw ", ir);
        Assert.DoesNotContain(" nuw ", ir);
        Assert.DoesNotContain(" exact ", ir);
        Assert.DoesNotContain("mustprogress", ir);
        Assert.DoesNotContain("willreturn", ir);
    }

    [Theory]
    [InlineData(MinimalEmissionTest.UnsupportedExpression, true)]
    [InlineData("if false => " + MinimalEmissionTest.UnsupportedExpression, true)]
    [InlineData("if false => true & false", false)]
    [InlineData("true | false", false)]
    [InlineData("true ^ false", false)]
    [InlineData("1 << true", false)]
    [InlineData("1.0 >> 1", false)]
    public void UnsupportedTypesNeverProduceIr(string source, bool bound)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(bound, c.Binding.Result.IsComplete);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData(KotoKind.LessThanLessThan, false)]
    [InlineData(KotoKind.LessThanLessThan, true)]
    [InlineData(KotoKind.Ampersand, true)]
    [InlineData(KotoKind.Caret, true)]
    [InlineData(KotoKind.And, false)]
    [InlineData(KotoKind.Or, false)]
    public void InvalidOperatorPlansFailBeforeWritingAndRecover(KotoKind op, bool boolean)
    {
        var c = MinimalEmissionTest.Analyze("let b = true\nlet x = 1 << 2");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var id = body.Values.FindIndex(x => x.Operator == KotoKind.LessThanLessThan);
        body.Values[id] = body.Values[id] with { Operator = op };
        if (boolean)
        {
            var value = Enumerable.Range(0, id).First(i => body.Values[i].Kind == OwnershipValueKind.Constant && body.Places[body.Operations[i].Place].Type == BoundType.Boolean);
            body.ValueOperands[body.Values[id].Start + 1] = value;
        }
        else if (op == KotoKind.LessThanLessThan)
        {
            body.Values[id] = body.Values[id] with { Kind = OwnershipValueKind.Unary, Count = 1 };
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        c.Ownership.Analyze();
        Assert.True(c.Emission.Validate(out error), error);
    }

    [Fact]
    public void WarmBitwiseAnalysisAndWritingAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Snapshot);
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
}
