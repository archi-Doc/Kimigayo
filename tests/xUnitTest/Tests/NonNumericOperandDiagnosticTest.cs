// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 13.2, 13.3: arithmetic, bitwise, shift, sign, increment and decrement operators take numeric operands, so bool, Unit,
// char and string have none, and an interpolated literal is the one form that joins strings. Binding owns the check and names
// the repair (SPEC 23.3.6).
public sealed class NonNumericOperandDiagnosticTest(ITestOutputHelper output)
{
    private const string Strings = "let first = \"Hello, \"\nlet second = \"world\"\n";
    private const string Note = "string has no arithmetic operators; an interpolated literal creates an owning string and borrows the values it embeds (SPEC 12.3.3, 13.3)";
    private const string Building = "; to build text in steps, write to a Text.HeapBuffer through Text.writer and $tryWrite, then intoString";

    [Fact]
    public void StringPlusIsRejectedAtBindingWithTheInterpolatedLiteral()
    {
        const string Source = Strings + "let joined = first + second";
        var c = Analyze(Source);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.NonNumericOperand_Kd), error.Code);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal("The operator + requires numeric operands, but this operand has Type string", error.Message);
        Assert.Equal("string has no +", error.Label);
        Assert.Equal("first + second", Source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal(["operand", "operator"], error.Reason!.Select(static x => x.Name));
        Assert.Equal(["string", "+"], error.Reason!.Select(static x => x.Value));
        Assert.Equal(Note, error.Note);
        Assert.Equal("If the strings are to be joined, write the interpolated literal \"\\(first)\\(second)\" in place of first + second" + Building, error.Advice);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Empty(c.Ownership.Issues);
    }

    // The operand Type and the operator are the facts; only a string operand is told about interpolation.
    [Theory]
    [InlineData("let v = true + true", "bool", "+")]
    [InlineData("let v = () + ()", "()", "+")]
    [InlineData("let v = 'a' * 'b'", "char", "*")]
    [InlineData("let v = \"a\" - \"b\"", "string", "-")]
    [InlineData("let v = true & false", "bool", "&")]
    [InlineData("var v = true\nv |= false", "bool", "|=")]
    [InlineData("var v = 'a'\nv %= 'b'", "char", "%=")]
    [InlineData("let v = \"a\" + 1", "string", "+")]
    [InlineData("func f() -> i32\n    return 1\n    true + false\npublic func main() => ()", "bool", "+")]
    [InlineData("let v = true << 1", "bool", "<<")]
    [InlineData("let v = \"a\" >> 1", "string", ">>")]
    [InlineData("var v = 'a'\nv <<= 1", "char", "<<=")]
    [InlineData("let v = -true", "bool", "-")]
    [InlineData("let v = +()", "()", "+")]
    [InlineData("let v = -\"a\"", "string", "-")]
    [InlineData("func f() -> i32\n    return 1\n    -\"text\"\npublic func main() => ()", "string", "-")]
    [InlineData("var v = true\nv++", "bool", "++")]
    [InlineData("var v = 'a'\n--v", "char", "--")]
    public void EveryNonNumericPrimitiveNamesItsTypeAndOperator(string source, string type, string op)
    {
        var error = Assert.Single(Errors(Analyze(source)));
        Assert.Equal(nameof(DiagnosticCode.NonNumericOperand_Kd), error.Code);
        Assert.Equal($"{type} has no {op}", error.Label);
        Assert.Equal(type == "string" ? Note : null, error.Note);
        Assert.Null(error.Advice);
    }

    [Theory]
    [InlineData("var log = \"start\"\nlog += \"!\"", "log += \"!\"", "log = \"\\(log)!\"")]
    [InlineData("var a = (\"a\", true)\na.0 += \"b\"", "a.0 += \"b\"", "a.0 = \"\\(a.0)b\"")]
    [InlineData("func append(p: raw/string)\n    unsafe => *p += \"x\"\npublic func main() => ()", "*p += \"x\"", "*p = \"\\(*p)x\"")]
    [InlineData("var log = \"start\"\nlog += \"\\(1)!\"", "log += \"\\(1)!\"", "log = \"\\(log)\\(1)!\"")]
    [InlineData("var log = \"start\"\nlet tail = \"!\"\nlog += tail", "log += tail", "log = \"\\(log)\\(tail)\"")]
    public void CompoundUpdatesAdviseTheReplacementAssignment(string source, string text, string replacement)
    {
        var error = Assert.Single(Errors(Analyze(source)));
        Assert.Equal(nameof(DiagnosticCode.NonNumericOperand_Kd), error.Code);
        Assert.Equal("string has no +=", error.Label);
        Assert.Equal(text, error.Text);
        Assert.Equal($"If text is to be appended to {text[..text.IndexOf(" +=", StringComparison.Ordinal)]}, assign a new string: {replacement}{Building}", error.Advice);
    }

    // SPEC 13.4 reads a string comparison through its layers; the operator rule judges a string operand the same way, and two
    // references with distinct Origins are not a mismatch of one Type with itself.
    [Theory]
    [InlineData("func join(a: ref/string, b: ref/string) -> string => a + b\npublic func main() => ()", "ref/string")]
    [InlineData("func join(a: uniq/string, b: ref/string) -> string => a + b\npublic func main() => ()", "uniq/string")]
    public void StringReferencesAreJudgedThroughTheirLayers(string source, string type)
    {
        var error = Assert.Single(Errors(Analyze(source)));
        Assert.Equal(nameof(DiagnosticCode.NonNumericOperand_Kd), error.Code);
        Assert.Equal($"{type} has no +", error.Label);
        Assert.Contains("\"\\(a)\\(b)\" in place of a + b", error.Advice, StringComparison.Ordinal);
    }

    // A chain reports once, at its innermost join, and the Advice is the one literal that joins every operand in order:
    // escaped and interpolated literals contribute their content, every other operand an interpolation.
    [Theory]
    [InlineData("let v = first + \" and \" + second + \"!\"", "first + \" and \"", "\"\\(first) and \\(second)!\" in place of first + \" and \" + second + \"!\"")]
    [InlineData("let v = first + (second + \"!\")", "second + \"!\"", "\"\\(first)\\(second)!\" in place of first + (second + \"!\")")]
    [InlineData("let v = \"x\\(first)\" + second", "\"x\\(first)\" + second", "\"x\\(first)\\(second)\" in place of \"x\\(first)\" + second")]
    [InlineData("let v = \"\"\"raw\"\"\" + second", "\"\"\"raw\"\"\" + second", "\"\\(\"\"\"raw\"\"\")\\(second)\" in place of \"\"\"raw\"\"\" + second")]
    [InlineData("let v = \"a\" + \"b\"", "\"a\" + \"b\"", "\"ab\" in place of \"a\" + \"b\"")]
    [InlineData("var log = \"start\"\nlog += first + second", "first + second", "assign a new string: log = \"\\(log)\\(first)\\(second)\"")]
    [InlineData("var flag = true\nloop\n    defer => first + second\n    if flag => exit\n    continue", "first + second", "\"\\(first)\\(second)\" in place of first + second")]
    public void ChainsReportOnceAndAdviseOneLiteral(string source, string text, string advice)
    {
        var error = Assert.Single(Errors(Analyze(Strings + source)));
        Assert.Equal(nameof(DiagnosticCode.NonNumericOperand_Kd), error.Code);
        Assert.Equal(text, error.Text);
        Assert.Contains(advice, error.Advice, StringComparison.Ordinal);
    }

    // A numeric left operand keeps the operand mismatch: the Types differ, and that is the fact.
    [Theory]
    [InlineData("let v = 1 + \"a\"", "expected string, found integer literal")]
    [InlineData("let n: i32 = 1\nlet v = n + \"a\"", "expected i32, found string")]
    public void ANumericOperandWithAStringKeepsTheMismatch(string source, string label)
    {
        var error = Assert.Single(Errors(Analyze(source)));
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), error.Code);
        Assert.Equal(label, error.Label);
    }

    [Fact]
    public void IndependentErrorsSurviveAndRebindingRepeatsNothing()
    {
        var compilation = CompilationTestHelper.ParseSuccess(Strings + "let v = first + second\nlet x: i32 = true");
        Assert.False(compilation.Bind().IsComplete);
        Assert.Equal([DiagnosticCode.NonNumericOperand_Kd, DiagnosticCode.TypeMismatch_Kd], compilation.Binding.Issues.Select(static x => x.Code));
        Assert.False(compilation.Bind().IsComplete);
        Assert.Equal(2, compilation.Binding.Issues.Count);
        compilation.Binding.ReportDiagnostics();
        Assert.Equal(2, Errors(compilation).Length);
    }

    // The repairs the Advice names are accepted, verified and executed: the interpolated literal, the replacement assignment
    // and text built in a HeapBuffer.
    [Fact]
    public void TheAdvisedRepairsAreAcceptedAndExecute()
    {
        const string Join = Strings + "let joined = \"\\(first)\\(second)\"\nvar log = \"start\"\nlog = \"\\(log)!\"\nConsole.writeLine(joined)\nConsole.writeLine(log)\nConsole.writeLine(first)";
        const string Build = """
            var buffer = Text.heap(0)
            var writer = Text.writer(buffer@uniq)
            match $tryWrite(writer@uniq, "start")
                .Ok(_) => ()
                .Err(_) => $abort("full")
            match $tryWrite(writer@uniq, "!")
                .Ok(_) => ()
                .Err(_) => $abort("full")
            match (buffer@move).intoString()@move
                .Ok(let text) => Console.writeLine(text)
                .Err(_) => $abort("utf8")
            """;
        foreach (var source in new[] { Join, Build })
        {
            var c = MinimalEmissionTest.Analyze(source);
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
            Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        }

        ScalarEmissionTest.EmitFixture("StringJoinInterpolation", Join, "Hello, world\nstart!\nHello, \n");
        ScalarEmissionTest.EmitFixture("StringJoinBuffer", Build, "start!\n");
    }

    [Fact]
    public void CliAndLspCarryTheAdvice()
    {
        var path = Path.GetFullPath("Hello.kimi");
        var c = Analyze(Strings + "let joined = first + second", path);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("Hello.kimi:3:14", console.Text, StringComparison.Ordinal);
        Assert.Contains("^^^^^^^^^^^^^^ string has no +", console.Text, StringComparison.Ordinal);
        Assert.Contains("\"\\(first)\\(second)\" in place of first + second", console.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("UnsupportedOwnership", console.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("TypeMismatch", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var capability in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, capability)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Equal(error.Code, sent.Code);
            Assert.Contains(error.Message, sent.Message, StringComparison.Ordinal);
            Assert.Contains("in place of first + second", sent.Message, StringComparison.Ordinal);
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
