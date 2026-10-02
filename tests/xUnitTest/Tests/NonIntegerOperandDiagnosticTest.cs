// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 13.2, 13.3: %, the bitwise and shift operators, increment and decrement take integer or wrapping integer operands, and a
// shift count is an integer Type, never a wrapping one. Binding owns both checks and names the operand Type and the operator;
// the operands of one Type are never reported as a mismatch of that Type with itself (SPEC 23.3.6).
public sealed class NonIntegerOperandDiagnosticTest(ITestOutputHelper output)
{
    private const string Bits64 = "If the bit pattern is meant, reinterpret each f64 operand with @bits<u64> before applying {0}; @bits<f64> turns resulting bits back into an f64";

    [Fact]
    public void FloatRemainderIsRejectedAtBindingWithItsTypeAndOperator()
    {
        const string Source = "var x = 1.0\nx %= 2.0";
        var c = Analyze(Source);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.NonIntegerOperand_Kd), error.Code);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal("The operator %= requires integer operands, but this operand has Type f64", error.Message);
        Assert.Equal("f64 has no %=", error.Label);
        Assert.Equal("x %= 2.0", Source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal(["operand", "operator"], error.Reason!.Select(static x => x.Name));
        Assert.Equal(["f64", "%="], error.Reason!.Select(static x => x.Value));
        Assert.Null(error.Note);
        Assert.Null(error.Advice);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Empty(c.Ownership.Issues);
    }

    // Every integer-only operator and form on a floating-point operand: plain, compound, prefix and postfix, through a reference
    // parameter and a Tuple element, with a literal-only operation, and whatever the other operand is. The operator in the
    // Advice is the one applied to the bits, without the assignment of a compound form.
    [Theory]
    [InlineData("let v = 1.5 % 2.0", "f64", "%", "1.5 % 2.0", null)]
    [InlineData("let v = 1.0 & 2.0", "f64", "&", "1.0 & 2.0", "&")]
    [InlineData("let x: f32 = 1.0\nlet v = x | x", "f32", "|", "x | x", "|")]
    [InlineData("let x = 1.0\nlet v = x ^ x", "f64", "^", "x ^ x", "^")]
    [InlineData("let x = 1.0\nlet v = x << 1", "f64", "<<", "x << 1", "<<")]
    [InlineData("let x: f32 = 1.0\nlet v = x >> 2", "f32", ">>", "x >> 2", ">>")]
    [InlineData("var x = 1.0\nx &= 2.0", "f64", "&=", "x &= 2.0", "&")]
    [InlineData("var x = 1.0\nx |= 2.0", "f64", "|=", "x |= 2.0", "|")]
    [InlineData("var x = 1.0\nx ^= 2.0", "f64", "^=", "x ^= 2.0", "^")]
    [InlineData("var x = 1.0\nx <<= 1", "f64", "<<=", "x <<= 1", "<<")]
    [InlineData("var x = 1.0\nx >>= 1", "f64", ">>=", "x >>= 1", ">>")]
    [InlineData("var x = 1.0\nx++", "f64", "++", "x++", null)]
    [InlineData("var x = 1.0\nx--", "f64", "--", "x--", null)]
    [InlineData("var x: f32 = 1.0\n++x", "f32", "++", "++x", null)]
    [InlineData("var x = 1.0\n--x", "f64", "--", "--x", null)]
    [InlineData("let x = 1.0\nlet n: i32 = 2\nlet v = x % n", "f64", "%", "x % n", null)]
    [InlineData("var t = (1, 2.0)\nt.1 %= 2.0", "f64", "%=", "t.1 %= 2.0", null)]
    [InlineData("func rem(r: ref/f64) -> f64 => r % 2.0\npublic func main() => ()", "f64", "%", "r % 2.0", null)]
    [InlineData("func f() -> f64\n    return 1.0\n    1.0 & 2.0\npublic func main() => ()", "f64", "&", "1.0 & 2.0", "&")]
    public void EveryIntegerOnlyOperatorOnAFloatNamesItsTypeAndOperator(string source, string type, string op, string text, string? bits)
    {
        var error = Assert.Single(Errors(Analyze(source)));
        Assert.Equal(nameof(DiagnosticCode.NonIntegerOperand_Kd), error.Code);
        Assert.Equal($"The operator {op} requires integer operands, but this operand has Type {type}", error.Message);
        Assert.Equal($"{type} has no {op}", error.Label);
        Assert.Equal(text, error.Text);
        Assert.Null(error.Note);
        Assert.Equal(bits is null ? null : type == "f64" ? string.Format(Bits64, bits) : $"If the bit pattern is meant, reinterpret each f32 operand with @bits<u32> before applying {bits}; @bits<f32> turns resulting bits back into an f32", error.Advice);
    }

    // A shift count is located at the count; only a wrapping count has a repair of its own.
    [Theory]
    [InlineData("let x: i32 = 1\nlet v = x << 1.0", "f64", "<<", "1.0")]
    [InlineData("let x: i32 = 1\nlet c: f32 = 1.0\nlet v = x >> c", "f32", ">>", "c")]
    [InlineData("var x: i32 = 1\nx <<= true", "bool", "<<=", "true")]
    [InlineData("let v = 1 << 'a'", "char", "<<", "'a'")]
    [InlineData("let x: u64 = 1\nlet c = \"a\"\nlet v = x >> c", "string", ">>", "c")]
    public void AShiftCountWithoutAnIntegerTypeIsReportedAtTheCount(string source, string type, string op, string text)
    {
        var error = Assert.Single(Errors(Analyze(source)));
        Assert.Equal(nameof(DiagnosticCode.InvalidShiftCount_Kd), error.Code);
        Assert.Equal($"The operator {op} requires an integer count, but this count has Type {type}", error.Message);
        Assert.Equal($"{type} is not an integer Type", error.Label);
        Assert.Equal(text, error.Text);
        Assert.Null(error.Note);
        Assert.Null(error.Advice);
    }

    [Theory]
    [InlineData("func f(x: i32, w: Wrapping<i32>) -> i32 => x << w\npublic func main() => ()", "Wrapping<i32>", "i32")]
    [InlineData("func f(x: Wrapping<u8>, w: Wrapping<u16>) -> Wrapping<u8> => x >> w\npublic func main() => ()", "Wrapping<u16>", "u16")]
    public void AWrappingCountIsToldToLeaveWrappingThroughItsArgument(string source, string type, string integer)
    {
        var error = Assert.Single(Errors(Analyze(source)));
        Assert.Equal(nameof(DiagnosticCode.InvalidShiftCount_Kd), error.Code);
        Assert.Equal($"{type} is not an integer Type", error.Label);
        Assert.Equal("w", error.Text);
        Assert.Equal("A wrapping integer Type is never a shift count (SPEC 13.3)", error.Note);
        Assert.Equal($"Convert the count to {integer} with @{integer}", error.Advice);
    }

    // The shifted operand is judged before the count, so a floating-point operand with a floating-point count is one record.
    [Fact]
    public void TheShiftedOperandIsJudgedBeforeTheCount()
    {
        var error = Assert.Single(Errors(Analyze("let x = 1.0\nlet v = x << 1.0")));
        Assert.Equal(nameof(DiagnosticCode.NonIntegerOperand_Kd), error.Code);
        Assert.Equal("x << 1.0", error.Text);
    }

    // An integer left operand with a floating-point right operand is a mismatch of two Types, and that is the fact.
    [Theory]
    [InlineData("let x: i32 = 7\nlet v = x % 2.5", "expected i32, found floating-point literal")]
    [InlineData("let x: i32 = 7\nlet y = 2.5\nlet v = x & y", "expected i32, found f64")]
    [InlineData("let n: i32 = 2\nlet v = 1.5 % n", "expected i32, found floating-point literal")]
    public void AnIntegerOperandWithAFloatKeepsTheMismatch(string source, string label)
    {
        var error = Assert.Single(Errors(Analyze(source)));
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), error.Code);
        Assert.Equal(label, error.Label);
    }

    // Integer and wrapping integer operands keep every operator, a floating-point operand keeps the arithmetic and sign
    // operators, and an integer count of any width shifts.
    [Fact]
    public void IntegerAndFloatingOperandsKeepTheirOperators()
    {
        const string Source = """
            func ops(w: Wrapping<u8>, n: u8, f: f64) -> f64
                var a: i32 = 7
                a %= 3
                a &= 1
                a |= 4
                a ^= 2
                a <<= 1
                a >>= n
                a++
                --a
                let m = w % w
                let s = w << n
                let t = (m ^ s)@u8
                var g = f
                g += 1.0
                g = -g
                g = +g / 2.0
                return g
            public func main() => ()
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void IndependentErrorsSurviveAndRebindingRepeatsNothing()
    {
        var compilation = CompilationTestHelper.ParseSuccess("var x = 1.0\nx %= 2.0\nlet n: i32 = 1\nlet v = n << 1.0\nlet y: i32 = true");
        Assert.False(compilation.Bind().IsComplete);
        Assert.Equal([DiagnosticCode.NonIntegerOperand_Kd, DiagnosticCode.InvalidShiftCount_Kd, DiagnosticCode.TypeMismatch_Kd], compilation.Binding.Issues.Select(static x => x.Code));
        Assert.False(compilation.Bind().IsComplete);
        Assert.Equal(3, compilation.Binding.Issues.Count);
        compilation.Binding.ReportDiagnostics();
        Assert.Equal(3, Errors(compilation).Length);
    }

    // The repairs the Advice names are accepted, verified and executed: the bit pattern through @bits and a wrapping count
    // converted to its integer argument.
    [Fact]
    public void TheAdvisedRepairsAreAcceptedAndExecute()
    {
        const string Source = """
            let x = -2.5
            let magnitude = (x@bits<u64> & 9223372036854775807)@bits<f64>
            let w: Wrapping<i32> = 3
            let shifted = 1 << w@i32
            if magnitude == 2.5 => Console.writeLine("magnitude") else => Console.writeLine("sign kept")
            Console.writeLine("\(shifted)")
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("IntegerOperandRepairs", Source, "magnitude\n8\n");
    }

    [Fact]
    public void CliAndLspCarryTheLabelsAndAdvice()
    {
        var path = Path.GetFullPath("Hello.kimi");
        var c = Analyze("func bits(x: f64) -> f64 => x & x\nfunc f(n: i32, w: Wrapping<i32>) -> i32 => n << w\npublic func main() => ()", path);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal([nameof(DiagnosticCode.NonIntegerOperand_Kd), nameof(DiagnosticCode.InvalidShiftCount_Kd)], result.Diagnostics.Select(static x => x.Code));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("Hello.kimi:1:29", console.Text, StringComparison.Ordinal);
        Assert.Contains("^^^^^ f64 has no &", console.Text, StringComparison.Ordinal);
        Assert.Contains(string.Format(Bits64, "&"), console.Text, StringComparison.Ordinal);
        Assert.Contains("Hello.kimi:2:49", console.Text, StringComparison.Ordinal);
        Assert.Contains("^ Wrapping<i32> is not an integer Type", console.Text, StringComparison.Ordinal);
        Assert.Contains("Convert the count to i32 with @i32", console.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("TypeMismatch", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var capability in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, capability)[identity];
            Assert.Equal(2, sent.Length);
            for (var i = 0; i < sent.Length; i++)
            {
                Assert.Equal(result.Diagnostics[i].Display!.Range, sent[i].Range);
                Assert.Equal(result.Diagnostics[i].Code, sent[i].Code);
                Assert.Contains(result.Diagnostics[i].Message, sent[i].Message, StringComparison.Ordinal);
            }

            Assert.Contains("@bits<u64>", sent[0].Message, StringComparison.Ordinal);
            Assert.Contains("Convert the count to i32 with @i32", sent[1].Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    private static TestDiagnostic[] Errors(Compilation c)
        => [.. TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error)];

    private static Compilation Analyze(string source, string path = "Hello.kimi")
    {
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        c.Binding.ReportStartupDiagnostics();
        c.Ownership.ControlFlow?.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        return c;
    }
}
