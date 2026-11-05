// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 13.2, 13.3.1: %, the bitwise and shift operators, increment and decrement take integer or wrapping integer operands, and a
// shift count is an integer Type, never a wrapping one. Binding owns both checks and names the operand Type and the operator;
// the operands of one Type are never reported as a mismatch of that Type with itself (SPEC 23.1).
public sealed class NonIntegerOperandDiagnosticTest(ITestOutputHelper output)
{
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
        Assert.Equal("operator %= requires an integer operand", error.Label);
        Assert.Equal("x %= 2.0", Source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal(["operand", "operator"], error.Reason!.Select(static x => x.Name));
        Assert.Equal(["f64", "%="], error.Reason!.Select(static x => x.Value));
        Assert.Null(error.Note);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Empty(c.Problems);
    }

    // Every integer-only operator and form on a floating-point operand: plain, compound, prefix and postfix, through a reference
    // parameter and a Tuple element, with a literal-only operation, and whatever the other operand is.
    [Theory]
    [InlineData("let v = 1.5 % 2.0", "f64", "%", "1.5 % 2.0")]
    [InlineData("let v = 1.0 & 2.0", "f64", "&", "1.0 & 2.0")]
    [InlineData("let x: f32 = 1.0\nlet v = x | x", "f32", "|", "x | x")]
    [InlineData("let x = 1.0\nlet v = x ^ x", "f64", "^", "x ^ x")]
    [InlineData("let x = 1.0\nlet v = x << 1", "f64", "<<", "x << 1")]
    [InlineData("let x: f32 = 1.0\nlet v = x >> 2", "f32", ">>", "x >> 2")]
    [InlineData("var x = 1.0\nx &= 2.0", "f64", "&=", "x &= 2.0")]
    [InlineData("var x = 1.0\nx |= 2.0", "f64", "|=", "x |= 2.0")]
    [InlineData("var x = 1.0\nx ^= 2.0", "f64", "^=", "x ^= 2.0")]
    [InlineData("var x = 1.0\nx <<= 1", "f64", "<<=", "x <<= 1")]
    [InlineData("var x = 1.0\nx >>= 1", "f64", ">>=", "x >>= 1")]
    [InlineData("var x = 1.0\nx++", "f64", "++", "x++")]
    [InlineData("var x = 1.0\nx--", "f64", "--", "x--")]
    [InlineData("var x: f32 = 1.0\n++x", "f32", "++", "++x")]
    [InlineData("var x = 1.0\n--x", "f64", "--", "--x")]
    [InlineData("let x = 1.0\nlet n: i32 = 2\nlet v = x % n", "f64", "%", "x % n")]
    [InlineData("var t = (1, 2.0)\nt.1 %= 2.0", "f64", "%=", "t.1 %= 2.0")]
    [InlineData("func rem(r: ref/f64) -> f64 => r % 2.0\npublic func main() => ()", "f64", "%", "r % 2.0")]
    [InlineData("func f() -> f64\n    return 1.0\n    1.0 & 2.0\npublic func main() => ()", "f64", "&", "1.0 & 2.0")]
    public void EveryIntegerOnlyOperatorOnAFloatNamesItsTypeAndOperator(string source, string type, string op, string text)
    {
        var error = Assert.Single(Errors(Analyze(source)));
        Assert.Equal(nameof(DiagnosticCode.NonIntegerOperand_Kd), error.Code);
        Assert.Equal($"The operator {op} requires integer operands, but this operand has Type {type}", error.Message);
        Assert.Equal($"operator {op} requires an integer operand", error.Label);
        Assert.Equal(text, error.Text);
        Assert.Null(error.Note);
    }

    // A shift count is located at the count; only a wrapping count has a Note of its own.
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
    }

    [Theory]
    [InlineData("func f(x: i32, w: Wrapping<i32>) -> i32 => x << w\npublic func main() => ()", "Wrapping<i32>")]
    [InlineData("func f(x: Wrapping<u8>, w: Wrapping<u16>) -> Wrapping<u8> => x >> w\npublic func main() => ()", "Wrapping<u16>")]
    public void AWrappingCountIsNeverAShiftCount(string source, string type)
    {
        var error = Assert.Single(Errors(Analyze(source)));
        Assert.Equal(nameof(DiagnosticCode.InvalidShiftCount_Kd), error.Code);
        Assert.Equal($"{type} is not an integer Type", error.Label);
        Assert.Equal("w", error.Text);
        Assert.Equal("A wrapping integer Type is never a shift count (SPEC 13.3.1)", error.Note);
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void ARefutedIntegerRequirementPublishesItsActualQuery()
    {
        const string Source = "func bits<T>(value: T)\n    T is not PrimitiveInteger\n    return value & 2\npublic func main() => ()";
        var compilation = Analyze(Source);
        var error = Assert.Single(compilation.Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.NonIntegerOperand_Kd), error.Code);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal(["operand", "operator", "subject", "member", "constraint"], error.Reason!.Select(static x => x.Name));
        Assert.Equal(["T", "&", "T", "&", "T is PrimitiveInteger"], error.Reason!.Select(static x => x.Value));
        Assert.Equal("Required condition is not satisfied: T is PrimitiveInteger", error.Note);
    }

    [Theory]
    [InlineData("func f<T>(x: T) -> T\n    T is PrimitiveInteger\n    return 1 + x\npublic func main() => ()")]
    [InlineData("func f<T>(x: Wrapping<T>) -> Wrapping<T>\n    T is PrimitiveInteger\n    return 1 + x\npublic func main() => ()")]
    [InlineData("func f(x: Wrapping<i32>) -> Wrapping<i32> => 1 + x\npublic func main() => ()")]
    public void AProvenRightIntegerSuppliesTheLiteralExpectation(string source)
    {
        var compilation = CompilationTestHelper.ParseSuccess(source);
        Assert.True(compilation.Bind().IsComplete, MinimalEmissionTest.Describe(compilation));
        Assert.Empty(compilation.Binding.Issues);
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

    // The written repairs are accepted, verified and executed: the bit pattern through @bits and a wrapping count
    // converted to its integer argument.
    [Fact]
    public void TheWrittenRepairsAreAcceptedAndExecute()
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        ScalarEmissionTest.EmitFixture("IntegerOperandRepairs", Source, "magnitude\n8\n");
    }

    [Fact]
    public void CliAndLspCarryTheLabels()
    {
        var path = Path.GetFullPath("Hello.kimi");
        var c = Analyze("func bits(x: f64) -> f64 => x & x\nfunc f(n: i32, w: Wrapping<i32>) -> i32 => n << w\npublic func main() => ()", path);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal([nameof(DiagnosticCode.NonIntegerOperand_Kd), nameof(DiagnosticCode.InvalidShiftCount_Kd)], result.Diagnostics.Select(static x => x.Code));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("Hello.kimi:1:29", console.Text, StringComparison.Ordinal);
        Assert.Contains("^^^^^ operator & requires an integer operand", console.Text, StringComparison.Ordinal);
        Assert.Contains("Hello.kimi:2:49", console.Text, StringComparison.Ordinal);
        Assert.Contains("^ Wrapping<i32> is not an integer Type", console.Text, StringComparison.Ordinal);
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

            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongOperandRequirementsSurviveCliAndLspRendering(bool refuted)
    {
        var type = "Operand" + new string('X', 140) + "Tail";
        var condition = refuted ? $"\n    {type} is not PrimitiveInteger" : string.Empty;
        var source = $"func bits<{type}>(value: {type}){condition}\n    return value & 2\npublic func main() => ()";
        var path = Path.GetFullPath("operator.kimi");
        var compilation = Analyze(source, path);
        compilation.Diagnostics.AddInput(compilation.Diagnostics.FindDocument(path)!, compilation.Kotonoha);
        var result = compilation.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(refuted ? nameof(DiagnosticCode.NonIntegerOperand_Kd) : nameof(DiagnosticCode.UnprovenConstraint_Kd), error.Code);
        var facts = refuted ? error.Reason!.Skip(2).ToArray() : error.Reason!.ToArray();
        Assert.Equal(["subject", "member", "constraint"], facts.Select(static x => x.Name));
        var subject = facts[0].Value;
        Assert.True(subject.Length <= 96);
        Assert.StartsWith("Operand", subject, StringComparison.Ordinal);
        Assert.EndsWith("Tail", subject, StringComparison.Ordinal);
        Assert.Equal("&", facts[1].Value);
        Assert.EndsWith(" is PrimitiveInteger", facts[2].Value, StringComparison.Ordinal);
        var verdict = refuted ? "not satisfied" : "not proven";
        Assert.Equal($"Required condition is {verdict}: {subject} is PrimitiveInteger", error.Note);
        var function = KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "bits");
        Assert.Equal(type, function.Parameters[0].Type.TypeOf()!.Name);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("PrimitiveInteger", console.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("TypeMismatch", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var placed = WorkspaceCheck.Place(
                new(CheckOutcome.Completed, false, TestPresence.No, result),
                [identity],
                identity,
                related);
            var sent = Assert.Single(placed[identity]);
            Assert.Equal(error.Code, sent.Code);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains("PrimitiveInteger", sent.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EditingAnOperatorReplacesItsTypedFailureFacts()
    {
        var compilation = CompilationTestHelper.ParseSuccess("func f(value: u32) => -value\npublic func main() => ()");
        Assert.False(compilation.Bind().IsComplete);
        compilation.Binding.ReportDiagnostics();
        var original = Assert.Single(compilation.Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.OperatorNotDefined_Kd), original.Code);
        Assert.Equal(["u32", "-"], original.Reason!.Select(static x => x.Value));
        ReplaceFunction(compilation, "func f(value: i32) => -value\npublic func main() => ()");
        Assert.True(compilation.Bind().IsComplete);
        compilation.Binding.ReportDiagnostics();
        Assert.Empty(compilation.Diagnostics.Finalize(rejected: false).Diagnostics);
        ReplaceFunction(compilation, "func f(value: bool) => value < false\npublic func main() => ()");
        Assert.False(compilation.Bind().IsComplete);
        compilation.Binding.ReportDiagnostics();
        var replaced = Assert.Single(compilation.Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.OperatorNotDefined_Kd), replaced.Code);
        Assert.Equal(["bool", "<"], replaced.Reason!.Select(static x => x.Value));

        static void ReplaceFunction(Compilation compilation, string source)
        {
            var root = compilation.Kotonoha.RootKoto;
            var current = KotoTree.Walk(root).OfType<FunctionKoto>().Single(static x => x.Name == "f");
            var donor = CompilationTestHelper.ParseSuccess(source);
            var replacement = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f");
            ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
            Assert.True(KotoHelper.Replace(current.Parent!, current, replacement));
        }
    }

    [Theory]
    [InlineData(
        "func f(x: u32) => -x\npublic func main() => ()",
        "OperatorNotDefined_Kd",
        "u32",
        "-",
        "-x",
        null)]
    [InlineData(
        "func f(x: ref/u32) => -x\npublic func main() => ()",
        "OperatorNotDefined_Kd",
        "u32",
        "-",
        "-x",
        null)]
    [InlineData(
        "let value = true < false",
        "OperatorNotDefined_Kd",
        "bool",
        "<",
        "true < false",
        null)]
    [InlineData(
        "let value = () >= ()",
        "OperatorNotDefined_Kd",
        "()",
        ">=",
        "() >= ()",
        null)]
    [InlineData(
        "let p: raw/i32 = null\nlet q: raw/i32 = null\nlet r = p < q",
        "OperatorNotDefined_Kd",
        "raw/i32",
        "<",
        "p < q",
        null)]
    [InlineData(
        "func f(p: raw/i32) => p * 2\npublic func main() => ()",
        "OperatorNotDefined_Kd",
        "raw/i32",
        "*",
        "p * 2",
        null)]
    [InlineData(
        "func f(p: raw/i32) => 2 * p\npublic func main() => ()",
        "OperatorNotDefined_Kd",
        "raw/i32",
        "*",
        "2 * p",
        null)]
    [InlineData(
        "func f(p: raw/i32) => 2 + p\npublic func main() => ()",
        "OperatorNotDefined_Kd",
        "raw/i32",
        "+",
        "2 + p",
        null)]
    [InlineData(
        "func f(p: raw/i32) => 2 - p\npublic func main() => ()",
        "OperatorNotDefined_Kd",
        "raw/i32",
        "-",
        "2 - p",
        null)]
    [InlineData(
        "func f(p: raw/i32)\n    var q = p\n    q *= 2\npublic func main() => ()",
        "OperatorNotDefined_Kd",
        "raw/i32",
        "*=",
        "q *= 2",
        null)]
    [InlineData(
        "func f(p: raw/i32)\n    var q = p\n    q <<= 1\npublic func main() => ()",
        "NonIntegerOperand_Kd",
        "raw/i32",
        "<<=",
        "q <<= 1",
        null)]
    [InlineData(
        "func f(p: raw/i32)\n    var q = p\n    q++\npublic func main() => ()",
        "NonIntegerOperand_Kd",
        "raw/i32",
        "++",
        "q++",
        null)]
    [InlineData(
        "struct S\nfunc f(x: S) => x << 1\npublic func main() => ()",
        "NonIntegerOperand_Kd",
        "S",
        "<<",
        "x << 1",
        null)]
    [InlineData(
        "struct S\nfunc f(x: S) => x & 2\npublic func main() => ()",
        "NonIntegerOperand_Kd",
        "S",
        "&",
        "x & 2",
        null)]
    [InlineData(
        "func f(p: raw/i32) => p & 2\npublic func main() => ()",
        "NonIntegerOperand_Kd",
        "raw/i32",
        "&",
        "p & 2",
        null)]
    [InlineData(
        "func f<T>(x: T) => x << 1\npublic func main() => ()",
        "UnprovenConstraint_Kd",
        "T",
        "<<",
        "x << 1",
        "PrimitiveInteger")]
    [InlineData(
        "func f<T>(x: T) => x & 2\npublic func main() => ()",
        "UnprovenConstraint_Kd",
        "T",
        "&",
        "x & 2",
        "PrimitiveInteger")]
    [InlineData(
        "func f<T>(x: T) => -x\npublic func main() => ()",
        "UnprovenConstraint_Kd",
        "T",
        "-",
        "-x",
        "Negatable")]
    [InlineData(
        "func f<T>(x: T)\n    T is PrimitiveInteger\n    return -x\npublic func main() => ()",
        "UnprovenConstraint_Kd",
        "T",
        "-",
        "-x",
        "Negatable")]
    [InlineData(
        "func f<T>(x: T) => 1 << x\npublic func main() => ()",
        "UnprovenConstraint_Kd",
        "T",
        "<<",
        "x",
        "PrimitiveInteger")]
    public void ApplicabilityFailuresRetainFactsBeforeCounterpartFitting(
        string source, string code, string type, string op, string at, string? contract)
    {
        var compilation = Analyze(source);
        var error = Assert.Single(Errors(compilation));
        Assert.Equal(code, error.Code);
        Assert.Equal(at, error.Text);
        var record = Assert.Single(compilation.Diagnostics.Finalize().Diagnostics);
        Assert.Equal(code == "UnprovenConstraint_Kd" ? DiagnosticCategory.Proof : DiagnosticCategory.Language, record.Category);
        var names = contract is null ? new[] { "operand", "operator" } : ["subject", "member", "constraint"];
        Assert.Equal(names, record.Reason!.Select(static x => x.Name));
        Assert.Equal(type, record.Reason![0].Value);
        Assert.Equal(op, record.Reason[1].Value);
        if (contract is not null)
        {
            Assert.Contains(contract, record.Reason[2].Value, StringComparison.Ordinal);
        }

        Assert.False(compilation.Binding.Result.IsComplete);
        Assert.Empty(compilation.Problems);
        using var output = new StringWriter();
        Assert.False(compilation.Generate(output));
        Assert.Empty(output.ToString());
    }

    private static TestDiagnostic[] Errors(Compilation c)
        => [.. TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error)];

    private static Compilation Analyze(string source, string path = "Hello.kimi")
    {
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        c.Binding.ReportStartupDiagnostics();
        c.ReportAnalysis();
        return c;
    }
}
