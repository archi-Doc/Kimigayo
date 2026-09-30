// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class ContextualLabelTest
{
    public static TheoryData<string, string, string, string> DiagnosticCases => new()
    {
        { "let x = label work: do => exit to work: 1", "let x = label work: do => exit to work 1", "TransferOperandExpected_Kd", ":" },
        { "let x = label work: do => exit to work(1)", "let x = label work: do => exit to work (1)", "TransferOperandSeparation_Kd", "(" },
        { "label work: do => exit to", "label work: do => exit to work", "TransferTargetExpected_Kd", string.Empty },
        { "let x = label work: (do => 1)", "let x = label work: do => 1", "LabelTargetExpected_Kd", "(" },
    };

    [Theory]
    [InlineData("let label = 1\nfunc label(value: i32) => ()\nobject.label\nconsume(label: value)")]
    [InlineData("label label: do => exit to label label")]
    [InlineData("label to: loop => exit to to to")]
    [InlineData("let x = 1 + label a: do => 2")]
    [InlineData("let x = -label a: do => 2")]
    [InlineData("let x = not label a: if ready => true else => false")]
    [InlineData("let x = try label a: do => make()")]
    [InlineData("consume(label a: do => 1, value: label b: do => 2)")]
    [InlineData("[label key: do => 1: label item: do => 2]")]
    [InlineData("label a: do => exit to a label b: do => 1")]
    [InlineData("return label a: do => 1")]
    [InlineData("return (label a: if ready => 1 else => 2) + 1")]
    [InlineData("label a: for item in items => continue to a")]
    [InlineData("label a: while ready => exit to a")]
    [InlineData("label a: match value\n    _ => yield to a 1")]
    [InlineData("label/* name */ a /* colon */:/* target */do => ()")]
    [InlineData("let unsafe = 1\nlet label = 2\nlet values = [unsafe: label]")]
    [InlineData("func f(value: () = return ! named: i32) => ()")]
    public void PrimaryPositionsAndOrdinaryNamesRoundTrip(string source)
    {
        var c = Parse(source);
        Assert.Empty(TestDiagnostics.Of(c));
        var written = ParseTestHelper.Unparse(c.Kotonoha);
        Assert.Empty(TestDiagnostics.Of(Parse(written)));
        var restored = TinyhandSerializer.Deserialize<Kotonoha>(TinyhandSerializer.Serialize(c.Kotonoha))!;
        restored.OnDeserialized(Compilation.CreateForTest());
        Assert.Equal(written, ParseTestHelper.Unparse(restored));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("(x)")]
    [InlineData("()")]
    [InlineData("[1, 2]")]
    [InlineData(".Some(1)")]
    [InlineData("..end")]
    [InlineData("x + 1")]
    [InlineData("label inner: do => 1")]
    public void NamedTransferConsumesACompleteOperand(string operand)
    {
        foreach (var keyword in new[] { "exit", "yield" })
        {
            foreach (var separator in new[] { " ", "/**/" })
            {
                var c = Parse(keyword + " to target" + separator + operand);
                Assert.Empty(TestDiagnostics.Of(c));
                var jump = Assert.IsAssignableFrom<JumpKoto>(Assert.Single(ParseTestHelper.GetChildren(c.Kotonoha.RootKoto)));
                Assert.Equal("target", jump.Label);
                Assert.NotNull(jump.Expression);
                Assert.Empty(TestDiagnostics.Of(Parse(jump.ToString())));
            }
        }
    }

    [Theory]
    [InlineData("return: x", "TransferOperandExpected_Kd", ":")]
    [InlineData("exit: x", "TransferOperandExpected_Kd", ":")]
    [InlineData("yield: x", "TransferOperandExpected_Kd", ":")]
    [InlineData("[exit to work: x]", "TransferOperandExpected_Kd", ":")]
    [InlineData("[yield to work: x]", "TransferOperandExpected_Kd", ":")]
    [InlineData("exit to work and ok", "TransferOperandExpected_Kd", "and")]
    [InlineData("exit to work(x)", "TransferOperandSeparation_Kd", "(")]
    [InlineData("exit to work.value", "TransferOperandSeparation_Kd", ".")]
    [InlineData("exit to work[0]", "TransferOperandSeparation_Kd", "[")]
    [InlineData("exit to work..end", "TransferOperandSeparation_Kd", "..")]
    [InlineData("yield to work-1", "TransferOperandSeparation_Kd", "-")]
    [InlineData("label work: 1", "LabelTargetExpected_Kd", "1")]
    [InlineData("label work: unsafe => ()", "LabelTargetExpected_Kd", "unsafe")]
    [InlineData("label work: (do => ())", "LabelTargetExpected_Kd", "(")]
    [InlineData("label work: label other: do => ()", "LabelTargetExpected_Kd", "label")]
    public void InvalidBoundariesExplainTheOffendingToken(string source, string code, string token)
    {
        var c = Parse(source);
        var diagnostic = Assert.Single(TestDiagnostics.Of(c), x => x.Code == code);
        Assert.Equal(token, diagnostic.Text);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message));
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Label));
        var shifted = Assert.Single(TestDiagnostics.Of(Parse("\n" + source)), x => x.Code == code);
        Assert.Equal(diagnostic.Message, shifted.Message);
        Assert.Equal(diagnostic.Span.Start + 1, shifted.Span.Start);
    }

    [Theory]
    [InlineData("[exit to work (): x]")]
    [InlineData("[(exit to work): x]")]
    [InlineData("[exit to work x: y]")]
    [InlineData("[return (): x]")]
    [InlineData("[yield to work (): x]")]
    [InlineData("if ready => exit to work else => ()")]
    [InlineData("(exit to work)\nnext()")]
    public void ExplicitValuesAndEnclosingSeparatorsRemainValid(string source)
        => Assert.Empty(TestDiagnostics.Of(Parse(source)));

    [Fact]
    public void MissingSameLineTargetDoesNotConsumeTheNextItem()
    {
        var c = Parse("exit to\nnext()");
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "TransferTargetExpected_Kd");
        var jump = Assert.IsType<ExitKoto>(ParseTestHelper.GetChildren(c.Kotonoha.RootKoto)[0]);
        Assert.Equal(string.Empty, jump.Label);
        Assert.StartsWith("exit to", jump.ToString(), StringComparison.Ordinal);
        Assert.IsType<InvocationKoto>(ParseTestHelper.GetChildren(c.Kotonoha.RootKoto).Last());
    }

    [Theory]
    [InlineData("return")]
    [InlineData("exit")]
    [InlineData("yield")]
    [InlineData("exit to work")]
    [InlineData("yield to work")]
    public void OmissionUsesPhysicalLinesEvenInsideParentheses(string transfer)
    {
        var c = Parse(transfer + "\nnext()");
        Assert.Empty(TestDiagnostics.Of(c));
        Assert.Null(Assert.IsAssignableFrom<JumpKoto>(ParseTestHelper.GetChildren(c.Kotonoha.RootKoto)[0]).Expression);
        var nextLine = Parse("(" + transfer + "\n    1)");
        Assert.NotEmpty(TestDiagnostics.Of(nextLine));
        var group = Assert.IsType<ParenthesizedKoto>(ParseTestHelper.GetChildren(nextLine.Kotonoha.RootKoto)[0]);
        Assert.Null(Assert.IsAssignableFrom<JumpKoto>(group.Operand).Expression);
        Assert.Empty(TestDiagnostics.Of(Parse("(" + transfer + " (\n        1\n))")));
    }

    [Theory]
    [InlineData("work: do => ()")]
    [InlineData("label\nwork: do => ()")]
    [InlineData("label work\n: do => ()")]
    [InlineData("label work:\ndo => ()")]
    [InlineData("label work: require true else => return")]
    [InlineData("label work: defer => ()")]
    [InlineData("label work: let x = 1")]
    [InlineData("continue to work 1")]
    [InlineData("continue to work: 1")]
    [InlineData("exit to work +")]
    public void IncompleteAndUnsupportedFormsAreRejected(string source)
        => Assert.NotEmpty(TestDiagnostics.Of(Parse(source)));

    [Theory]
    [MemberData(nameof(DiagnosticCases))]
    public void RecoveryKeepsIndependentProblemsAndTheValidCounterpart(string source, string valid, string code, string token)
    {
        var errors = Publish(source);
        var error = Assert.Single(errors);
        Assert.Equal(code, error.Code);
        Assert.Equal(token, error.Text);
        Assert.NotEmpty(error.Label!);
        Assert.NotEmpty(error.Advice!);
        Assert.Empty(Publish(valid));
        var independent = Publish(source + "\nfunc unrelated() -> i32 => true");
        Assert.Contains(error, independent);
        Assert.Equal("TypeMismatch_Kd", Assert.Single(independent, x => x != error).Code);
    }

    [Fact]
    public void GroupingOperandsAndCleanupSurviveRoundTripAndEmission()
    {
        const string Source = """
            struct Marker
                drop => Console.writeLine("drop")
            func choose(ready: bool) -> i32
                return (label choice: if ready => 10 else => 20) + 1
            func add(value: i32) -> i32 => value + 1
            func run() -> i32
                return label work: do
                    let marker = Marker.init()
                    var value = 1
                    defer => Console.writeLine("defer \(value)")
                    value = 2
                    exit to work label choice: if true => yield to choice 42 else => 0
            require choose(true) == 11 and choose(false) == 21 else => $abort("grouping")
            let sum = 1 + label work: do => exit to work 2
            let negative = -label work: do => 2
            let inverted = not label choice: if true => false else => true
            let argument = add(value: label work: do => exit to work 4)
            require sum == 3 and negative == -2 and inverted and argument == 5 else => $abort("operands")
            require run() == 42 else => $abort("result")
            Console.writeLine("ok")
            """;
        ScalarEmissionTest.EmitFixture("ContextualLabels", Source, "defer 2\ndrop\nok\n");
        var written = ParseTestHelper.Unparse(Parse(Source).Kotonoha);
        ScalarEmissionTest.EmitFixture("ContextualLabelsRoundTrip", written, "defer 2\ndrop\nok\n");
    }

    [Theory]
    [InlineData("label work: do => exit to missing: 1", "InvalidJumpTarget_Kd", "TransferOperandExpected_Kd")]
    [InlineData("label work: do => exit to missing(1)", "InvalidJumpTarget_Kd", "TransferOperandSeparation_Kd")]
    [InlineData("label work: do => yield to work: 1", "InvalidJumpTarget_Kd", "TransferOperandExpected_Kd")]
    [InlineData("if true => yield: ()", "UnlabeledYieldTarget_Kd", "TransferOperandExpected_Kd")]
    public void OperandRecoveryDoesNotHideAnIndependentTargetProblem(string source, string targetCode, string operandCode)
        => Assert.Equal(new[] { targetCode, operandCode }.Order(), Publish(source).Select(x => x.Code).Order());

    [Fact]
    public void DictionaryKeysAndValuesBindWithUngroupedLabels()
    {
        // Runtime nonempty Dictionary literals remain the independent P31 limitation.
        var c = Parse("let values: Dictionary<i32, i32> = [label key: do => 1: label value: do => 2]");
        Assert.Empty(TestDiagnostics.Of(c));
        Assert.True(c.Bind().IsComplete);
    }

    [Fact]
    public void DropIsReservedAndDeinitIsNoLongerADeclaration()
    {
        Assert.Equal("drop", TokenHelper.GetKeywordOrIdentifierKind("drop").ToText());
        Assert.NotEqual(TokenKind.Identifier, TokenHelper.GetKeywordOrIdentifierKind("drop"));
        Assert.Equal(TokenKind.Identifier, TokenHelper.GetKeywordOrIdentifierKind("deinit"));
        Assert.Empty(TestDiagnostics.Of(Parse("struct Owner\n    drop => ()")));
        Assert.NotEmpty(TestDiagnostics.Of(Parse("struct Owner\n    deinit => ()")));
        Assert.Empty(TestDiagnostics.Of(Parse("func deinit() => ()\nlet x = deinit()")));
    }

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        return c;
    }

    private static TestDiagnostic[] Publish(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        c.Binding.ReportStartupDiagnostics();
        c.Ownership.ControlFlow!.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        return TestDiagnostics.Of(c, "Hello.kimi").Where(x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error).ToArray();
    }
}
