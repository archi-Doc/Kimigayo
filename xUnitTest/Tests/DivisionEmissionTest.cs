// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class DivisionEmissionTest
{
    private const string ZeroReason = "KIMI_E_INT_DIV_ZERO: Integer division or remainder by zero";
    private const string OverflowReason = "KIMI_E_INT_OVERFLOW: Integer overflow";
    private const string Snapshot = "var x = 21\nlet y = work: do\n    defer => x /= 3\n    exit to work: x / 2\nif y == 10 and x == 7 => writeLine(\"ok\")";

    public static TheoryData<string, string> Fixtures => new()
    {
        { "DivisionSigns", "if 7 / 3 == 2 and -7 / 3 == -2 and 7 / -3 == -2 and -7 / -3 == 2 => writeLine(\"ok\")\nif 7 % 3 == 1 and -7 % 3 == -1 and 7 % -3 == 1 and -7 % -3 == -1 => writeLine(\"ok\")" },
        { "DivisionLimits", "var x = -2147483647 - 1\nvar d = -2147483648\nif x / 1 == -2147483648 and x % 1 == 0 and x / d == 1 and x % d == 0 => writeLine(\"ok\")\nif 0 / -1 == 0 and 0 % -1 == 0 and 2147483647 / 1 == 2147483647 and 2147483647 % 1 == 0 => writeLine(\"ok\")" },
        { "DivisionCompound", "var x = -21\nvar d = 2\nlet unit = (x /= d++)\nif x == -10 and d == 3 => writeLine(\"ok\")\nx %= d++\nif x == -1 and d == 4 => writeLine(\"ok\")" },
        { "DivisionOrder", "var x = 7\nlet y = x++ / x++\nif x == 9 and y == 0 => writeLine(\"ok\")\nlet z = x++ % x++\nif x == 11 and z == 9 => writeLine(\"ok\")" },
        { "DivisionShortCircuit", "var divisor = 0\nif divisor != 0 and (100 / divisor > 1) => writeLine(\"bad\")\ndivisor = 10\nif divisor != 0 and (100 / divisor > 1) => writeLine(\"ok\")\nif true or 1 % 0 == 0 => writeLine(\"ok\")" },
        { "DivisionSkipped", "if false => 1 / 0\nif false => -2147483648 % -1\nif false and 1 / 0 == 0 => writeLine(\"bad\")\nwriteLine(\"ok\")\nloop\n    exit\n    let x = 1 % 0\nwriteLine(\"ok\")" },
        { "DivisionPhi", "var c = true\nlet x = if c => (21 / 2) % 3 else => 3 / 0\nif x == 1 => writeLine(\"ok\")\nlet y = work: do\n    defer => c = false\n    exit to work: (if c => 7 / 3 else => 1 % 0)\nif y == 2 and not c => writeLine(\"ok\")" },
        { "DivisionSnapshot", Snapshot + "\nwriteLine(\"ok\")" },
        { "DivisionDeferLoop", "var x = 64\nloop\n    defer => x /= 2\n    if x == 2 => exit\n    continue\nif x == 1 => writeLine(\"ok\")\nwriteLine(\"ok\")" },
        { "DivisionOperandTransfer", "var x = 12\nlet y = outer: do\n    defer => writeLine(\"ok\")\n    x /= (if true => exit to outer: 9 else => 0)\n    exit to outer: 0\nif x == 12 and y == 9 => writeLine(\"ok\")" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EmitsDivisionFixtures(string name, string source)
        => ScalarEmissionTest.EmitFixture(name, source, "ok\nok\n");

    [Theory]
    [InlineData("/", false, false)]
    [InlineData("%", false, false)]
    [InlineData("/=", false, false)]
    [InlineData("%=", false, false)]
    [InlineData("/", true, false)]
    [InlineData("%", true, false)]
    [InlineData("/=", true, false)]
    [InlineData("%=", true, false)]
    [InlineData("/", false, true)]
    [InlineData("%", false, true)]
    [InlineData("/=", false, true)]
    [InlineData("%=", false, true)]
    [InlineData("/", true, true)]
    [InlineData("%", true, true)]
    [InlineData("/=", true, true)]
    [InlineData("%=", true, true)]
    public void ExceptionalInputsAbortAtTheOperator(string op, bool overflow, bool variable)
    {
        var compound = op.Length == 2;
        var left = overflow ? "-2147483648" : "1";
        var right = overflow ? "-1" : "0";
        var prefix = variable ? $"var x = {(overflow ? "-2147483647 - 1" : left)}\nvar d = {right}\n" : compound ? $"var x = {left}\n" : string.Empty;
        var expression = $"{(variable || compound ? "x" : left)} {op} {(variable ? "d" : right)}";
        var source = prefix + expression + "\nwriteLine(\"bad\")";
        var line = variable ? 3 : compound ? 2 : 1;
        var name = $"DivisionFail{(op[0] == '/' ? "Quotient" : "Remainder")}{(compound ? "Assign" : "Binary")}{(overflow ? "Overflow" : "Zero")}{(variable ? "Variable" : "Literal")}";
        ScalarEmissionTest.EmitFixture(name, source, string.Empty, 1, $"Hello.kimi:{line}:1: abort {(overflow ? OverflowReason : ZeroReason)}\n");
    }

    [Theory]
    [InlineData("DivisionInnerFailure", "var x = -2147483648\nvar a = 1\nvar b = 0\nx /= a / b", "", 4, 6, false)]
    [InlineData("DivisionOuterFailure", "var x = -2147483648\nvar a = 1\nvar b = -1\nx /= a / b", "", 4, 1, true)]
    [InlineData("DivisionBeforeCleanup", "let y = work: do\n    defer => writeLine(\"bad\")\n    exit to work: 1 / 0\nwriteLine(\"bad\")", "", 3, 19, false)]
    [InlineData("DivisionDuringCleanup", "defer => writeLine(\"bad\")\ndefer\n    writeLine(\"begin\")\n    1 / 0\n    writeLine(\"bad\")", "begin\n", 4, 5, false)]
    [InlineData("RemainderDuringCleanup", "var x = -2147483648\ndefer => writeLine(\"bad\")\ndefer\n    writeLine(\"begin\")\n    x %= -1\n    writeLine(\"bad\")", "begin\n", 5, 5, true)]
    public void FailureStopsAtTheActualEvaluation(string name, string source, string stdout, int line, int column, bool overflow)
        => ScalarEmissionTest.EmitFixture(name, source, stdout, 1, $"Hello.kimi:{line}:{column}: abort {(overflow ? OverflowReason : ZeroReason)}\n");

    [Fact]
    public void ChecksHaveOneSuccessLabelAndDivisionIsOnlyInThatBlock()
    {
        var c = MinimalEmissionTest.Analyze("var x = 21\nlet y = if true => x / 2 else => x % 2\nx /= y");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var function = module.GetFunction(0);
        using var writer = new StringWriter();
        module.WriteIr(writer);
        var ir = writer.ToString();
        var divisions = function.Instructions.Where(x => x.Check == ArithmeticCheckKind.Division).ToArray();
        Assert.Equal(3, divisions.Length);
        foreach (var instruction in divisions)
        {
            Assert.True(instruction.Constant >= 0);
            Assert.Contains($"br i1 %invalid{instruction.Operation}, label %abort{instruction.Operation}, label %b{instruction.Place}\n", ir);
            Assert.Contains($"b{instruction.Place}:\n  %v{instruction.Operation} = {instruction.ScalarOperator} i32 ", ir);
            Assert.Contains($"call void @__kimi_abort(i32 %v{instruction.Place},", ir);
        }

        var phi = Assert.Single(function.Instructions, x => x.Opcode == EmissionOpcode.Phi);
        var inputs = function.GetOperands(phi);
        for (var i = 0; i < inputs.Length; i += 2)
        {
            var producer = inputs[i].Value;
            Assert.Equal(Assert.Single(divisions, x => x.Operation == producer).Place, inputs[i + 1].Value);
        }

        Assert.All(function.Slots, x => Assert.Equal(OwnershipPlaceKind.Local, c.Ownership.Bodies[0].Places[x.Place].Kind));
        Assert.DoesNotContain(" exact ", ir);
        Assert.DoesNotContain("mustprogress", ir);
        Assert.DoesNotContain("willreturn", ir);
    }

    [Theory]
    [InlineData("let x = " + MinimalEmissionTest.UnsupportedExpression)]
    [InlineData("var x = " + MinimalEmissionTest.UnsupportedExpression)]
    [InlineData("if false => 1.0 / 0.0")]
    [InlineData(MinimalEmissionTest.UnsupportedExpression)]
    public void OtherTypesAndOperatorsStillFailBeforeWriting(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MismatchedDivisionPlansFailBeforeWriting(bool wrongKind)
    {
        var c = MinimalEmissionTest.Analyze("let b = true\nvar x = 7\nlet y = x / 2");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var id = body.Values.FindIndex(x => x.Operator == KotoKind.Slash);
        if (wrongKind)
        {
            body.Values[id] = body.Values[id] with { Kind = OwnershipValueKind.Unary, Count = 1 };
        }
        else
        {
            var boolean = Enumerable.Range(0, body.Values.Count).First(i => body.Values[i].Kind == OwnershipValueKind.Constant && body.Places[body.Operations[i].Place].Type == BoundType.Boolean);
            body.ValueOperands[body.Values[id].Start + 1] = boolean;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void AbortTableHasStableIndicesAndDerivedLengths()
    {
        var c = MinimalEmissionTest.Analyze("1 / 1");
        using var writer = new StringWriter();
        Assert.True(c.Emission.WriteIr(writer, out var error), error);
        var ir = writer.ToString();
        Assert.Equal(9, WindowsLowering.AbortReasons.Length);
        var count = WindowsLowering.AbortReasons.Length;
        Assert.Equal(2, Regex.Matches(ir, $@"\[{count} x \{{ ptr, i64 \}}\]").Count);
        foreach (var reason in WindowsLowering.AbortReasons)
        {
            Assert.Contains($"{{ ptr, i64 }} {{ ptr @__kimi_{reason.Name}_reason, i64 {reason.Text.Length} }}", ir);
            Assert.Contains($"@__kimi_{reason.Name}_reason = private constant [{reason.Text.Length} x i8] c\"{reason.Text}\"", ir);
        }

        Assert.Equal(OverflowReason, WindowsLowering.AbortReasons[WindowsLowering.IntegerOverflowReason].Text);
        Assert.Equal(ZeroReason, WindowsLowering.AbortReasons[WindowsLowering.IntegerDivisionZeroReason].Text);
        Assert.Equal("KIMI_E_INT_SHIFT_COUNT: Shift count out of range", WindowsLowering.AbortReasons[WindowsLowering.IntegerShiftCountReason].Text);
        Assert.Contains($"%known = icmp ult i32 %reason, {count}", ir);
        Assert.DoesNotContain("{{", ir);
    }

    [Theory]
    [InlineData("1 / 0", false)]
    [InlineData("1 % 0", false)]
    [InlineData("(-9223372036854775807 - 1) / -1", false)]
    [InlineData("(-9223372036854775807 - 1) % -1", false)]
    [InlineData("7 / -3 + 3", true)]
    [InlineData("7 % -3", true)]
    [InlineData("(-9223372036854775807 - 1) % 1", true)]
    public void RequiredConstantLengthsRejectExceptionalInputs(string expression, bool valid)
    {
        var c = MinimalEmissionTest.Analyze($"func f(x: [({expression}) of i32]) => ()");
        Assert.Equal(valid, c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("/", false)]
    [InlineData("%", false)]
    [InlineData("/", true)]
    [InlineData("%", true)]
    public void ConstantLengthChecksUseTheTargetWidth(string op, bool valid)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("i686-unknown-linux-gnu"));
        var expression = $"(-2147483647 - 1) {op} {(valid ? "1" : "-1")}";
        if (op == "/" && valid)
        {
            expression = $"({expression}) + 2147483647 + 1";
        }

        c.Kotonoha.AddSource(new SourceDocument("Length.kimi", $"func f(x: [({expression}) of i32]) => ()"));
        Assert.Equal(valid, c.Bind().IsComplete);
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.InvalidTypeFormation_Kd);
        }
    }

    [Fact]
    public void CompoundMappingHasNoUnknownFallback()
    {
        Assert.Equal(KotoKind.Slash, KotoHelper.CompoundOperation(KotoKind.SlashEquals));
        Assert.Equal(KotoKind.Percent, KotoHelper.CompoundOperation(KotoKind.PercentEquals));
        Assert.Equal(KotoKind.Invalid, KotoHelper.CompoundOperation(KotoKind.Equals));
        Assert.Equal(KotoKind.Invalid, KotoHelper.CompoundOperation(KotoKind.Slash));
    }

    [Fact]
    public void WarmDivisionAnalysisAndWritingAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Snapshot);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var success = true;
        for (var i = 0; i < 128; i++)
        {
            success &= c.Ownership.Analyze().IsVerified;
            success &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(success);
    }
}
