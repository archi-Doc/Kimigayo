// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class DivisionEmissionTest
{
    private const string ZeroReason = "KIMI_E_INT_DIV_ZERO: Integer division or remainder by zero";
    private const string OverflowReason = "KIMI_E_INT_OVERFLOW: Integer overflow";
    private const string Snapshot = "var x = 21\nlet y = label work: do\n    defer => x /= 3\n    exit to work x / 2\nif y == 10 and x == 7 => Console.writeLine(\"ok\")";

    public static TheoryData<string, string> Fixtures => new()
    {
        { "DivisionSigns", "if 7 / 3 == 2 and -7 / 3 == -2 and 7 / -3 == -2 and -7 / -3 == 2 => Console.writeLine(\"ok\")\nif 7 % 3 == 1 and -7 % 3 == -1 and 7 % -3 == 1 and -7 % -3 == -1 => Console.writeLine(\"ok\")" },
        { "DivisionLimits", "var x = -2147483647 - 1\nvar d = -2147483648\nif x / 1 == -2147483648 and x % 1 == 0 and x / d == 1 and x % d == 0 => Console.writeLine(\"ok\")\nif 0 / -1 == 0 and 0 % -1 == 0 and 2147483647 / 1 == 2147483647 and 2147483647 % 1 == 0 => Console.writeLine(\"ok\")" },
        { "DivisionCompound", "var x = -21\nvar d = 2\nlet unit = (x /= d++)\nif x == -10 and d == 3 => Console.writeLine(\"ok\")\nx %= d++\nif x == -1 and d == 4 => Console.writeLine(\"ok\")" },
        { "DivisionOrder", "var x = 7\nlet y = x++ / x++\nif x == 9 and y == 0 => Console.writeLine(\"ok\")\nlet z = x++ % x++\nif x == 11 and z == 9 => Console.writeLine(\"ok\")" },
        { "DivisionShortCircuit", "var divisor = 0\nif divisor != 0 and (100 / divisor > 1) => Console.writeLine(\"bad\")\ndivisor = 10\nif divisor != 0 and (100 / divisor > 1) => Console.writeLine(\"ok\")\nif true or 1 % 0 == 0 => Console.writeLine(\"ok\")" },
        { "DivisionSkipped", "if false => 1 / 0\nif false => -2147483648 % -1\nif false and 1 / 0 == 0 => Console.writeLine(\"bad\")\nConsole.writeLine(\"ok\")\nloop\n    exit\n    let x = 1 % 0\nConsole.writeLine(\"ok\")" },
        { "DivisionPhi", "var c = true\nlet x = if c => (21 / 2) % 3 else => 3 / 0\nif x == 1 => Console.writeLine(\"ok\")\nlet y = label work: do\n    defer => c = false\n    exit to work (if c => 7 / 3 else => 1 % 0)\nif y == 2 and not c => Console.writeLine(\"ok\")" },
        { "DivisionSnapshot", Snapshot + "\nConsole.writeLine(\"ok\")" },
        { "DivisionDeferLoop", "var x = 64\nloop\n    defer => x /= 2\n    if x == 2 => exit\n    continue\nif x == 1 => Console.writeLine(\"ok\")\nConsole.writeLine(\"ok\")" },
        { "DivisionOperandTransfer", "var x = 12\nlet y = label outer: do\n    defer => Console.writeLine(\"ok\")\n    x /= (if true => exit to outer 9 else => 0)\n    exit to outer 0\nif x == 12 and y == 9 => Console.writeLine(\"ok\")" },
        { "DivisionRemainderDuringCleanup", "var x = -2147483648\ndefer\n    x %= -1\n    if x == 0 => Console.writeLine(\"ok\")\nConsole.writeLine(\"ok\")" },
    };

    // SPEC 13.3.1: a % -1 is 0 for every dividend, including the minimum, with a literal or a dynamic divisor.
    [Theory]
    [InlineData("%", false)]
    [InlineData("%=", false)]
    [InlineData("%", true)]
    [InlineData("%=", true)]
    public void RemainderOfTheMinimumByMinusOneIsZero(string op, bool variable)
    {
        var compound = op.Length == 2;
        var divisor = variable ? "d" : "-1";
        var prefix = variable ? "var d = -1\n" : string.Empty;
        var source = compound
            ? $"{prefix}var x = -2147483648\nx %= {divisor}\nif x == 0 => Console.writeLine(\"ok\")"
            : $"{prefix}var x = -2147483648\nif x % {divisor} == 0 and -2147483648 % {divisor} == 0 => Console.writeLine(\"ok\")";
        var name = $"DivisionRemainderMinimum{(compound ? "Assign" : "Binary")}{(variable ? "Variable" : "Literal")}";
        var ir = ScalarEmissionTest.EmitFixture(name, source, "ok\n");
        // A dynamic divisor is substituted through %safe; a literal -1 is written as 1, so srem never sees -1.
        Assert.Equal(variable, ir.Contains("%safe", StringComparison.Ordinal));
        Assert.DoesNotMatch(@"= srem i32 [^\n]*, -1\n", ir);
    }

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
    [InlineData("/=", true, false)]
    [InlineData("/", false, true)]
    [InlineData("%", false, true)]
    [InlineData("/=", false, true)]
    [InlineData("%=", false, true)]
    [InlineData("/", true, true)]
    [InlineData("/=", true, true)]
    public void ExceptionalInputsAbortAtTheOperator(string op, bool overflow, bool variable)
    {
        var compound = op.Length == 2;
        var left = overflow ? "-2147483648" : "1";
        var right = overflow ? "-1" : "0";
        var prefix = variable ? $"var x = {(overflow ? "-2147483647 - 1" : left)}\nvar d = {right}\n" : compound ? $"var x = {left}\n" : string.Empty;
        var expression = $"{(variable || compound ? "x" : left)} {op} {(variable ? "d" : right)}";
        var source = prefix + expression + "\nConsole.writeLine(\"bad\")";
        var line = variable ? 3 : compound ? 2 : 1;
        var name = $"DivisionFail{(op[0] == '/' ? "Quotient" : "Remainder")}{(compound ? "Assign" : "Binary")}{(overflow ? "Overflow" : "Zero")}{(variable ? "Variable" : "Literal")}";
        ScalarEmissionTest.EmitFixture(name, source, string.Empty, 1, $"Hello.kimi:{line}:1: abort {(overflow ? OverflowReason : ZeroReason)}\n");
    }

    [Theory]
    [InlineData("DivisionInnerFailure", "var x = -2147483648\nvar a = 1\nvar b = 0\nx /= a / b", "", 4, 6, false)]
    [InlineData("QuotientLiteralMinusOneMinimum", "var x = -2147483648\nlet y = x / -1", "", 2, 9, true)] // IMPL 21.3.3: the minimum check stays.
    [InlineData("DivisionOuterFailure", "var x = -2147483648\nvar a = 1\nvar b = -1\nx /= a / b", "", 4, 1, true)]
    [InlineData("DivisionBeforeCleanup", "let y = label work: do\n    defer => Console.writeLine(\"bad\")\n    exit to work 1 / 0\nConsole.writeLine(\"bad\")", "", 3, 18, false)]
    [InlineData("DivisionDuringCleanup", "defer => Console.writeLine(\"bad\")\ndefer\n    Console.writeLine(\"begin\")\n    1 / 0\n    Console.writeLine(\"bad\")", "begin\n", 4, 5, false)]
    [InlineData("QuotientDuringCleanup", "var x = -2147483648\ndefer => Console.writeLine(\"bad\")\ndefer\n    Console.writeLine(\"begin\")\n    x /= -1\n    Console.writeLine(\"bad\")", "begin\n", 5, 5, true)]
    public void FailureStopsAtTheActualEvaluation(string name, string source, string stdout, int line, int column, bool overflow)
        => ScalarEmissionTest.EmitFixture(name, source, stdout, 1, $"Hello.kimi:{line}:{column}: abort {(overflow ? OverflowReason : ZeroReason)}\n");

    // IMPL 21.3.3: a literal nonzero divisor needs no zero check even at O0, a literal other than -1 needs no minimum check,
    // and a literal -1 keeps only the minimum check of an integer quotient; every signed quotient by -1 is a negation.
    [Fact]
    public void LiteralDivisorsOmitTheirChecks()
    {
        var ir = ScalarEmissionTest.EmitFixture("DivisionLiteralDivisors", "var x: i32 = 7\nvar u: u32 = 7\nvar w: Wrapping<i32> = -2147483648\nlet a = x / 2\nlet b = x % 3\nlet c = u / 4\nlet d = u % 5\nlet e = x / -1\nlet f = x % -1\nlet g = w / -1\nlet h = w % -1\nx /= -2\nif a == 3 and b == 1 and c == 1 and d == 2 and e == -7 and f == 0 and g == -2147483648 and h == 0 and x == -3 => Console.writeLine(\"ok\")", "ok\n");
        var module = GeneratedIr.Parse(ir);
        var abort = module.Function("__kimi_abort");
        var body = module.Startup;
        Assert.DoesNotContain(body.Instructions, static x => x.Opcode == "select");
        var check = Assert.Single(body.Blocks, x => x.Successors.Any(s => s.Instructions.Any(i => i.Callee == abort))).Terminator;
        Assert.Equal("icmp eq i32 %, -2147483648", check.Operands[0].Shape);
        Assert.Equal(2, body.Instructions.Count(static x => x.Shape.StartsWith("sub i32 0, ", StringComparison.Ordinal)));
        Assert.Equal(2, body.Instructions.Count(static x => x.Shape == "srem i32 %, 1"));
        Assert.Equal(2, body.Instructions.Count(static x => x.Shape.StartsWith("sdiv i32 ", StringComparison.Ordinal)));
        Assert.DoesNotContain(body.Instructions, static x => x.Shape == "sdiv i32 %, -1");
        Assert.Contains(body.Instructions, static x => x.Shape.StartsWith("udiv i32 ", StringComparison.Ordinal));
        Assert.Contains(body.Instructions, static x => x.Shape.StartsWith("urem i32 ", StringComparison.Ordinal));
    }

    [Fact]
    public void ChecksHaveOneSuccessLabelAndDivisionIsOnlyInThatBlock()
    {
        var c = MinimalEmissionTest.Analyze("var x = 21\nvar d = 2\nlet y = if true => x / d else => x % d\nx /= y");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var function = module.GetFunction(0);
        using var writer = new StringWriter();
        module.WriteIr(writer);
        var ir = writer.ToString();
        var divisions = function.Instructions.Where(x => x.Check is ArithmeticCheckKind.Division or ArithmeticCheckKind.DivisionZero).ToArray();
        Assert.Equal(3, divisions.Length);
        Assert.Equal(2, divisions.Count(x => x.Check == ArithmeticCheckKind.Division));
        foreach (var instruction in divisions)
        {
            Assert.True(instruction.Constant >= 0);
            var quotient = instruction.Check == ArithmeticCheckKind.Division;
            Assert.Equal(quotient ? "sdiv" : "srem", instruction.ScalarOperator);
            Assert.Contains($"br i1 {(quotient ? "%invalid" : "%zero")}{instruction.Operation}, label %abort{instruction.Operation}, label %b{instruction.Place}\n", ir);
            // The instruction lies in the success block, after the -1 substitution of a dynamic remainder divisor (SPEC 13.3.1).
            var block = ir.IndexOf($"b{instruction.Place}:\n", StringComparison.Ordinal);
            var division = ir.IndexOf($"\n  %v{instruction.Operation} = {instruction.ScalarOperator} i32 ", block, StringComparison.Ordinal);
            var next = ir.IndexOf("\nb", block + 1, StringComparison.Ordinal);
            Assert.True(block >= 0 && division > block && (next < 0 || next > division));
            Assert.Contains(quotient ? $"call void @__kimi_abort(i32 %v{instruction.Place}," : $"call void @__kimi_abort(i32 {AbortReasons.IntegerDivisionZero},", ir);
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
        GeneratedIr.Parse(ir).AssertNoTerminationClaim();
    }

    [Theory]
    [InlineData("let x = " + MinimalEmissionTest.FloatExpression)]
    [InlineData("var x = " + MinimalEmissionTest.FloatExpression)]
    [InlineData("if false => 1.0 / 0.0")]
    [InlineData(MinimalEmissionTest.FloatExpression)]
    public void FloatingArithmeticUsesItsOwnExecutionRules(string source, bool emitted = true)
    {
        var c = MinimalEmissionTest.Analyze(source);
        FloatEmissionTest.AssertEmissionSupport(c, emitted);
    }

    [Fact]
    public void AbortTableHasStableIndicesAndDerivedLengths()
    {
        var c = MinimalEmissionTest.Analyze("1 / 1");
        var ir = CompilationTestHelper.WriteIr(c);
        Assert.Equal(18, AbortReasons.All.Length);
        var count = AbortReasons.All.Length;
        Assert.Equal(2, Regex.Matches(ir, $@"\[{count} x \{{ ptr, i64 \}}\]").Count);
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reason in AbortReasons.All)
        {
            Assert.True(codes.Add(reason.Text.Split(':', 2)[0]), "Abort codes must have one canonical catalog entry.");
            Assert.Contains($"{{ ptr, i64 }} {{ ptr @__kimi_{reason.Name}_reason, i64 {reason.Text.Length} }}", ir);
            Assert.Contains($"@__kimi_{reason.Name}_reason = private constant [{reason.Text.Length} x i8] c\"{reason.Text}\"", ir);
        }

        Assert.Equal(OverflowReason, AbortReasons.All[AbortReasons.IntegerOverflow].Text);
        Assert.Equal(ZeroReason, AbortReasons.All[AbortReasons.IntegerDivisionZero].Text);
        Assert.Equal("KIMI_E_INT_SHIFT_COUNT: Shift count out of range", AbortReasons.All[AbortReasons.IntegerShiftCount].Text);
        Assert.Equal("KIMI_E_INT_CONVERSION: Integer conversion out of range", AbortReasons.All[AbortReasons.IntegerConversion].Text);
        Assert.Equal("KIMI_E_FLOAT_CONVERSION: Floating conversion out of range", AbortReasons.All[AbortReasons.FloatingConversion].Text);
        Assert.Equal("KIMI_E_ARG_RANGE: Argument out of range", AbortReasons.All[AbortReasons.ArgumentRange].Text);
        Assert.Equal("KIMI_E_FORMAT: Formatting failed", AbortReasons.All[AbortReasons.Format].Text);
        Assert.Equal(14, AbortReasons.MissingKey);
        Assert.Equal("KIMI_E_MISSING_KEY: Dictionary key was not found", AbortReasons.All[AbortReasons.MissingKey].Text);
        Assert.Equal(15, AbortReasons.DuplicateKey);
        Assert.Equal("KIMI_E_DUPLICATE_KEY: Dictionary literal contains an equivalent key", AbortReasons.All[AbortReasons.DuplicateKey].Text);
        Assert.Equal(16, AbortReasons.ReferenceCount);
        Assert.Equal("KIMI_E_REF_COUNT: Reference count limit exceeded", AbortReasons.All[AbortReasons.ReferenceCount].Text);
        Assert.Equal(17, AbortReasons.StaticCycle);
        Assert.Equal("KIMI_E_STATIC_CYCLE: Static initialization cycle", AbortReasons.All[AbortReasons.StaticCycle].Text);
        Assert.Contains($"%known = icmp ult i32 %reason, {count}", ir);
        Assert.DoesNotContain("{{", ir);
    }

    [Theory]
    [InlineData("1 / 0", false)]
    [InlineData("1 % 0", false)]
    [InlineData("(-9223372036854775807 - 1) / -1", false)]
    [InlineData("(-9223372036854775807 - 1) % -1", true)]
    [InlineData("7 / -3 + 3", true)]
    [InlineData("7 % -3", true)]
    [InlineData("(-9223372036854775807 - 1) % 1", true)]
    public void RequiredConstantLengthsRejectExceptionalInputs(string expression, bool valid)
    {
        var c = MinimalEmissionTest.Analyze($"func f(x: [({expression}) of i32]) => ()");
        Assert.Equal(valid, c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("/", "-1", false)]
    [InlineData("/", "1", true)]
    [InlineData("%", "-1", true)]
    [InlineData("%", "0", false)]
    public void ConstantLengthChecksUseTheTargetWidth(string op, string divisor, bool valid)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("i686-unknown-linux-gnu"));
        var expression = $"(-2147483647 - 1) {op} {divisor}";
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

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmDivisionAnalysisAndWritingAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Snapshot);
        Assert.True(c.Analyze().IsVerified);
        Assert.True(c.WriteIr(TextWriter.Null, out var error), error);
        var success = true;
        var bytes = AllocationMeasurement.Measure(() =>
        {
            success &= c.Analyze().IsVerified;
            success &= c.WriteIr(TextWriter.Null, out _);
        });
        Assert.Equal(0, bytes);
        Assert.True(success);
    }
}
