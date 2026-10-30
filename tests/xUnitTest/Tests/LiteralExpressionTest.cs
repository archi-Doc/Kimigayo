// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 12.3: a literal-only expression is fitted to candidates, expected Types, comparison operands and branch
/// results as one literal; its operators then evaluate in the fitted Type at run time, and a shift count is typed
/// independently of the shifted operand.</summary>
public class LiteralExpressionTest
{
    private const string Fitting =
        "func widen(value: i64) -> i64 => value\nfunc pick(value: u8) -> u8 => value\n" +
        "func take(value: i32) -> string => \"value\"\nfunc take(value: ref/i64) -> string => \"borrow\"\n" +
        "let x: i64 = 7\n" +
        "require widen(2 * 5) == 10 and widen(-(3) + 1) == -2 else => $abort(\"candidate\")\n" +
        "require pick(200 + 55) == 255 and pick(1 << 7) == 128 else => $abort(\"u8\")\n" +
        "require x == 2 * 5 + -3 and 2 * 5 - 3 == x and (3 << 2) + x == 19 else => $abort(\"comparison\")\n" +
        "let wide: i64 = 1 << 40\nrequire wide == 1099511627776 else => $abort(\"shift\")\n" +
        "let r = if x > 0 => 2 * 3 else => x\nrequire r == 6 else => $abort(\"branch\")\n" +
        "require take(1 + 1) == \"value\" else => $abort(\"class\")\n" +
        "let n = 4 * 4\nlet m: i64 = n@i64 + (1 << 2)\nrequire m == 20 else => $abort(\"default\")\n" +
        "let buffer: [(2 * 3) of u8] = [(2 * 3) of 0]\nrequire buffer.length == 6 else => $abort(\"length\")\n" +
        "let pairs = [(x, x), (x, 2000000000 + 2000000000)]\nrequire pairs[1].1 == 4000000000 else => $abort(\"element\")\n" +
        "Console.writeLine(\"ok\")";

    [Fact]
    public void FitsLiteralOnlyExpressionsAsLiterals()
        => ScalarEmissionTest.EmitFixture("LiteralExpressionFitting", Fitting, "ok\n");

    [Fact]
    public void OperatorsEvaluateInTheFittedTypeAtRunTime()
        => ScalarEmissionTest.EmitFixture(
            "LiteralExpressionOverflow",
            "Console.writeLine(\"before\")\nlet a: u8 = 200 + 100\nConsole.writeLine(\"after\")",
            "before\n",
            1,
            "Hello.kimi:2:13: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");

    [Fact]
    public void ShiftCountIsTypedIndependently()
        => ScalarEmissionTest.EmitFixture(
            "LiteralExpressionShiftCount",
            "Console.writeLine(\"before\")\nlet bad: u8 = 1 << 256\nConsole.writeLine(\"after\")",
            "before\n",
            1,
            "Hello.kimi:2:15: abort KIMI_E_INT_SHIFT_COUNT: Shift count out of range\n");

    // SPEC 10.5.1, 14.9.2: a control-flow argument whose result sources are all literals is a Literal: each candidate's Type propagates to
    // every source, at a position of a Tuple literal too and shared-borrowed at `ref/T`, and only a candidate every source fits applies.
    [Theory]
    [InlineData("func take(value: i64) -> () => ()\ntake(if c => 1 else => 2)")]
    [InlineData("func pick(value: u8) -> () => ()\npick(if c => 255 else => 0)")]
    [InlineData("func read(value: ref/i32) -> () => ()\nread(if c => 1 else => 2)")]
    [InlineData("func take(value: i64) -> () => ()\ntake(if c => (if d => 1 else => 2) else => 3)")]
    [InlineData("func take(value: i64) -> () => ()\ntake(match c\n    true => 1\n    false => 2\n)")]
    [InlineData("func take(value: i64) -> () => ()\ntake(loop => exit 1)")]
    [InlineData("func g(x: (i64, i64)) => ()\nlet a: i64 = 2\ng((if c => 1 else => 2, a))")]
    [InlineData("func f(value: i8) -> () => ()\nfunc f(value: i64) -> () => ()\nf(if c => 1 else => 200)")]
    public void AControlFlowArgumentOfLiteralsIsALiteral(string source)
    {
        var c = MinimalEmissionTest.Analyze("let c = true\nlet d = false\n" + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    // SPEC 14.9.2 step 3, 4.3, 12.3: a literal source of a join, or a literal leaf of a Tuple, array or dictionary literal source or of an
    // array or dictionary literal's element, is not fixed before its position's Type: it waits for the Type the other sources or elements
    // supply, which no survey typed before binding them, nested too, is bound once at it and generates in it.
    [Theory]
    [InlineData("let t = if c => p.x else => 1", "1", "i64")]
    [InlineData("let t = if c => p.x + n else => 1", "1", "i64")]
    [InlineData("let t = if c => p.x else => 2 * 3", "2 * 3", "i64")]
    [InlineData("let t = if c => p.x else => (if d => 1 else => 2)", "2", "i64")]
    [InlineData("let t = if c => (n, n) else => (1, 2)", "(1, 2)", "(i64, i64)")]
    [InlineData("let t = if c => (n, n) else => (n, 1)", "(n, 1)", "(i64, i64)")]
    [InlineData("let x = [(n, 1), (n, n)]", "(n, 1)", "(i64, i64)")]
    [InlineData("let x = if c => [1, 2] else => [p.x]", "[1, 2]", "Array<i64>")]
    [InlineData("let t = match n\n        1 => p.x\n        _ => 4000000000", "4000000000", "i64")]
    [InlineData("let t = loop\n        if c => exit 4000000000\n        exit p.x", "4000000000", "i64")]
    [InlineData("let g = func [] (e: bool, q: P)\n        if e => return q.x\n        return 4000000000", "4000000000", "i64")]
    [InlineData("let t = if c => (p.x, p.x) else => (p.x, if d => 4000000000 else => 2)", "2", "i64")]
    [InlineData("let t = if c => p.x else => (if d => (if c => 4000000000 else => 1) else => -2)", "-2", "i64")]
    [InlineData("let g = func [] (e: bool)\n        return func [] ()\n            return 7", "7", "i32")]
    [InlineData("let g = func [] (e: bool, q: P) => if e => (return q.x) else => 4000000000", "4000000000", "i64")]
    [InlineData("let t = if c => p.x else => (label inner: if d => 4000000000 else => 3)", "3", "i64")]
    [InlineData("let t = if c => p.x else => (4000000000)", "4000000000", "i64")]
    [InlineData("let t = if c => (n, (return 1)) else => (n, 5)", "(n, (return 1))", "(i64, i32)")]
    [InlineData("let t = if c => (n, (return 1)) else => (5, n)", "(5, n)", "(i64, i64)")]
    [InlineData("let t = if c => (n, 5) else => (6, n)", "(n, 5)", "(i64, i64)")]
    [InlineData("let t = if c => (n, 5) else => (6, 7)", "(6, 7)", "(i64, i32)")]
    [InlineData("let t = if c => [2 of 5] else => [2 of n]", "[2 of 5]", "[2 of i64]")]
    [InlineData("let x: i32 = 1\n    let r = x@ref\n    let t = if c => r else => (if d => 6 else => (return 1))", "6", "i32")]
    [InlineData("let x = [[2 of 5], [2 of n]]", "[2 of 5]", "[2 of i64]")]
    [InlineData("let g = func [] (e: bool, a: i64)\n        if e => return (1, 2)\n        return (a, a)", "(1, 2)", "(i64, i64)")]
    [InlineData("let t = if c => [\"k\": p.x] else => [\"k\": 4000000000]", "[\"k\": 4000000000]", "Dictionary<string, i64>")]
    [InlineData("let t = if c => [p.x: \"x\"] else => [4000000000: \"y\"]", "[4000000000: \"y\"]", "Dictionary<i64, string>")]
    [InlineData("let x = [(n, n), (n, 2000000000 + 2000000000)]", "2000000000 + 2000000000", "i64")]
    [InlineData("let x = [(n, 4000000000), (n, n)]", "4000000000", "i64")]
    [InlineData("let x = [[n], [4000000000]]", "4000000000", "i64")]
    [InlineData("let x = [\"k\": (n, n), \"j\": (n, 4000000000)]", "4000000000", "i64")]
    [InlineData("let x = [n, if c => 1 else => 4000000000]", "4000000000", "i64")]
    public void ALiteralSourceTakesItsJoinsType(string statement, string literal, string type)
    {
        var c = MinimalEmissionTest.Analyze("struct P\n    public var x: i64 = 5\nfunc f(c: bool, d: bool, n: i64, p: P) -> i64\n    " + statement + "\n    return 0\n" +
            "let r = f(true, false, 1, P.init())");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).Where(x => x.ToString() == literal).ToArray();
        Assert.NotEmpty(nodes);
        Assert.All(nodes, x => Assert.Equal(type, TyCtxt.Name(x.TypeOf()!)));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    // SPEC 14.9.2 step 4, 12.3: a literal source is checked once, at its join's Type or, with none decided, at its default, so one that
    // does not fit that Type is the literal's own problem, on which the join rests; a source bound at its default is decided. SPEC 12.3.1,
    // 13.5.2 (the owner, 2026-10-19): an @move or @copy operand takes no Type from its position, so its literal takes its default.
    [Theory]
    [InlineData("let t = if c => p.y else => 300", nameof(DiagnosticCode.InvalidNumericLiteral_Kd), "300")]
    [InlineData("let t = if c => p.b else => 1", nameof(DiagnosticCode.TypeMismatch_Kd), "1")]
    [InlineData("let t = if c => (p.y, p.y) else => (p.y, 300)", nameof(DiagnosticCode.InvalidNumericLiteral_Kd), "300")]
    [InlineData("let t = if c => [p.y] else => [300]", nameof(DiagnosticCode.InvalidNumericLiteral_Kd), "300")]
    [InlineData("let t = if c => [2 of p.y] else => [2 of 300]", nameof(DiagnosticCode.InvalidNumericLiteral_Kd), "300")]
    [InlineData("let t = if c => [1, 4000000000] else => [2]", nameof(DiagnosticCode.InvalidNumericLiteral_Kd), "4000000000")]
    [InlineData("let t = if c => [[1, 4000000000]] else => [[2]]", nameof(DiagnosticCode.InvalidNumericLiteral_Kd), "4000000000")]
    [InlineData("let t = if c => [1, 4000000000] else => [(return)]", nameof(DiagnosticCode.InvalidNumericLiteral_Kd), "4000000000")]
    [InlineData("let t = if c => p.x else => (4000000000)@copy", nameof(DiagnosticCode.InvalidNumericLiteral_Kd), "4000000000")]
    [InlineData("let t = if c => p.x else => (2)@copy", nameof(DiagnosticCode.TypeMismatch_Kd), "if c => p.x else => (2)@copy")]
    public void ALiteralSourceThatDoesNotFitItsJoinsTypeIsItsOwnProblem(string statement, string code, string at)
    {
        var source = "struct P\n    public var y: i8 = 1\n    public var b: bool = true\n    public var x: i64 = 5\nfunc f(c: bool, p: P)\n    " + statement +
            "\n    _ = t\npublic func main() => ()\n";
        var diagnostic = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((code, at), (diagnostic.Code, source.Substring(diagnostic.Span!.Value.Start, diagnostic.Span.Value.Length)));
    }

    [Theory]
    [InlineData("struct P\n    public var y: i8 = 1\nfunc f(c: bool, p: P) -> i8\n    let t = if c => p.y else => 300\n    return t")]
    [InlineData("func choose(value: i32) -> () => ()\nfunc choose(value: i64) -> () => ()\nchoose(2 * 5)")]
    [InlineData("let a: u8 = -(1)")]
    [InlineData("let a: u8 = 256 + 0")]
    [InlineData("let a: f64 = 1 + 2")]
    [InlineData("let invalid: [(4 / 0) of u8] = []")]
    [InlineData("let x = 10\nmatch x\n    2 * 5 => ()\n    _ => ()")]
    public void RejectsWhatLiteralFittingDoesNotAdmit(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && TestDiagnostics.Of(c, "Hello.kimi").Length == 0, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void NumericConversionTypesItsOperandIndependently()
        => ScalarEmissionTest.EmitFixture(
            "LiteralExpressionConversion",
            "Console.writeLine(\"before\")\nlet a = (200 + 100)@u8\nConsole.writeLine(\"after\")",
            "before\n",
            1,
            "Hello.kimi:2:9: abort KIMI_E_INT_CONVERSION: Integer conversion out of range\n");
}
