// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.RegularExpressions;
using Kimi.Compiler.Backend;
using Xunit;

namespace XunitTest;

public class StringEmissionTest
{
    // writeLine borrows its argument (SPEC 22.1); the conditional Moves are written as transfers of a temporary.
    private const string Conditional = "var c = true\nvar text = \"old\"\nif c => Console.writeLine(text@move)\ntext = \"new\"\nConsole.writeLine(text)";
    private const string Loop = "var text = \"outer\"\nvar i = 0\nwhile i < 3\n    if i == 1 => Console.writeLine(text@move)\n    text = \"next\"\n    i += 1\nConsole.writeLine(text)";

    public static TheoryData<string, string, string, string> Fixtures => new()
    {
        { "StringLocal", "let text = \"hello\"\nConsole.writeLine(text)", "hello\n", "hello=1" },
        { "StringMove", "let text = \"hello\"\nlet other = text@move\nConsole.writeLine(other)", "hello\n", "hello=1" },
        { "StringDrop", "let text = \"drop\"", string.Empty, "drop=1" },
        { "StringDiscardMove", "let text = \"drop\"\ntext@move", string.Empty, "drop=1" },
        { "StringEmpty", "var text = \"\"\ntext = text@move\nConsole.writeLine(text)", "\n", "=1" },
        { "StringUnicode", "let text = \"日本語\\0\"\nConsole.writeLine(text)", "日本語\0\n", "日本語\0=1" },
        { "StringReplace", "var text = \"old\"\ntext = \"new\"\nConsole.writeLine(text)", "new\n", "old=1;new=1" },
        { "StringSelf", "var text = \"self\"\ntext = text@move\nConsole.writeLine(text)", "self\n", "self=1" },
        { "StringBorrowTwice", "let text = \"twice\"\nConsole.writeLine(text)\nConsole.writeLine(text)", "twice\ntwice\n", "twice=1" },
        { "StringReinitialize", "var text = \"old\"\nConsole.writeLine(text)\ntext = \"new\"", "old\n", "old=1;new=1" },
        { "StringConditionalTrue", Conditional, "old\nnew\n", "old=1;new=1" },
        { "StringConditionalFalse", Conditional.Replace("true", "false"), "new\n", "old=1;new=1" },
        { "StringPartialTrue", "var c = true\nvar text: string\nif c => text = \"partial\"", string.Empty, "partial=1" },
        { "StringPartialFalse", "var c = false\nvar text: string\nif c => text = \"partial\"", string.Empty, "partial=0" },
        { "StringLoop", Loop, "next\nnext\n", "outer=1;next=3" },
        { "StringLoopDeclare", "var i = 0\nwhile i < 3\n    var text: string\n    if i == 0 => text = \"once\"\n    i += 1\n    continue", string.Empty, "once=1" },
        { "StringLoopExit", "var i = 0\nloop\n    var text = \"iteration\"\n    if i == 0 => Console.writeLine(text)\n    i += 1\n    if i == 3 => exit\n    continue", "iteration\n", "iteration=3" },
        { "StringDefer", "var text = \"old\"\ndefer => Console.writeLine(text)\ntext = \"new\"", "new\n", "old=1;new=1" },
        { "StringDeferLocal", "var i = 0\nloop\n    defer\n        var text = \"deferred\"\n        if i == 1 => Console.writeLine(text)\n    i += 1\n    if i == 3 => exit\n    continue", "deferred\n", "deferred=3" },
        { "StringReturn", "func f(c: bool) -> i32\n    var text = \"local\"\n    if c => Console.writeLine(text)\n    return 42\nif f(true) == 42 and f(false) == 42 => Console.writeLine(\"ok\")", "local\nok\n", "local=2;ok=1" },
        { "StringPhi", "var c = true\nlet result = label work: do\n    var text = \"local\"\n    if c => Console.writeLine(text)\n    exit to work 42\nif result == 42 => Console.writeLine(\"ok\")", "local\nok\n", "local=1;ok=1" },
        { "StringExitRhs", "loop\n    var text = \"old\"\n    text = (exit)", string.Empty, "old=1" },
        { "StringReturnRhs", "func f() -> i32\n    var text = \"old\"\n    text = (return 42)\nif f() == 42 => Console.writeLine(\"ok\")", "ok\n", "old=1;ok=1" },
        { "StringSkipped", "if false\n    var text = \"skipped\"\n    text = text@move\nConsole.writeLine(\"ok\")", "ok\n", "skipped=0;ok=1" },
        { "StringPhiCleanup", "func choose(c: bool, move: bool) -> i32\n    let n = if c\n        var text = \"join\"\n        if move => Console.writeLine(text)\n        yield 40 + 2\n    else => 7\n    return n\nif choose(true, true) == 42 and choose(true, false) == 42 and choose(false, true) == 7 => Console.writeLine(\"ok\")", "join\nok\n", "join=2;ok=1" },
        { "StringDiscardSelection", "if true => (if true => \"a\" else => \"b\")", string.Empty, "a=1;b=0" },
        { "StringExplicitMain", "public func main()\n    var text = \"main\"\n    text = text@move\n    Console.writeLine(text)", "main\n", "main=1" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void TransfersAndCleanupExecuteOnce(string name, string source, string stdout, string destructions)
    {
        var ir = ScalarEmissionTest.EmitFixture(name, source, stdout);
        Assert.DoesNotContain("llvm.memcpy", ir);
        WriteAuditedFixture(name, ir, stdout, destructions);
    }

    [Theory]
    [InlineData("StringAbortRhs", "func fail() -> Never\n    var x = 2147483647\n    x += 1\n    loop => ()\nvar text = \"old\"\ntext = fail()", 3, 5, "old=0")]
    [InlineData("StringAbortDefer", "var text = \"old\"\ndefer\n    var other = \"inner\"\n    var x = 2147483647\n    x += 1", 5, 5, "old=0;inner=0")]
    public void AbortDoesNotContinueDestruction(string name, string source, int line, int column, string destructions)
    {
        var stderr = $"Hello.kimi:{line}:{column}: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture(name, source, string.Empty, 1, stderr);
        WriteAuditedFixture(name, ir, string.Empty, destructions, 1, stderr);
    }

    [Theory]
    [InlineData("func unused() -> string => " + MinimalEmissionTest.FloatExpression + "\nConsole.writeLine(\"ok\")")]
    [InlineData("let text = \"a\"\ntext@string\nConsole.writeLine(text)")]
    [InlineData("let text = \"a\"\nlet taken = text@move\nConsole.writeLine(text)")]
    [InlineData("let text = \"a\"\nlet taken = text\nConsole.writeLine(text)")]
    [InlineData("let text = \"a\"\nConsole.writeLine(text@uniq)")]
    [InlineData("var text: string\nvar c = true\nif c => text = \"a\"\nConsole.writeLine(text)")]
    public void UnsupportedResultsAndInvalidMovesPublishNoIr(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.Generate(writer));
        Assert.Empty(writer.ToString());
    }

    // A right-hand side that never completes writes nothing (its MIR: tests/mir/updates.kimi `noncompletingWrite`).
    [Fact]
    public void NoncompletingRhsHasNoStringWrite()
    {
        var c = MinimalEmissionTest.Analyze("loop\n    var text = \"old\"\n    text = (exit)");
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    // A defer that never completes leaves the string's destruction and the delivery to the checks (their MIR: tests/mir/scopes.kimi
    // `divergentDeferHeld`).
    [Fact]
    public void DivergentDeferPreventsStringDestructionAndDelivery()
    {
        const string Source = "var text = \"held\"\ndefer => loop => ()";
        var ir = ScalarEmissionTest.EmitFixture("StringDivergentDefer", Source, string.Empty, observationMilliseconds: 300);
        GeneratedIr.Parse(ir).AssertNoTerminationClaim();
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmStringAnalysisAndWritingAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Loop);
        var valid = true;
        var allocated = AllocationMeasurement.Measure(() =>
        {
            valid &= c.Analyze().IsVerified;
            valid &= c.Generate(TextWriter.Null);
        });
        Assert.True(valid);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void AuditDetectsDoubleDestructionAndMissingDestruction()
    {
        const string Source = "let text = \"single\"\nConsole.writeLine(text)";
        var ir = ScalarEmissionTest.EmitFixture("StringAuditControl", Source, "single\n");
        var duplicate = StringLifetimeAudit.Instrument(ir, [("__kimi_text", 6, 1)], duplicate: true);
        ScalarEmissionTest.WriteFixture("StringAuditDuplicate", duplicate, "single\n", 120, string.Empty);
        var missing = StringLifetimeAudit.Instrument(ir, [("__kimi_text", 6, 2)]);
        ScalarEmissionTest.WriteFixture("StringAuditMissing", missing, "single\n", 120, string.Empty);
    }

    // Audits the destructions of the module's string constants (count, and optionally order, per text). A constant is found by its bytes
    // among the definitions of the audited text itself, which code generation writes through LlvmConstantPool; a text the module
    // defines no constant for (its code is never generated) cannot be destroyed, so it matches no handle.
    internal static void WriteAuditedFixture(string name, string ir, string stdout, string destructions, int exit = 0, string? stderr = null, int[]? order = null)
    {
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match definition in Regex.Matches(ir, @"(?m)^@([\w.]+) = private unnamed_addr constant \[\d+ x i8\] c""((?:\\[0-9A-F]{2})*)"", align 1$"))
        {
            var hex = definition.Groups[2].Value.Replace("\\", string.Empty, StringComparison.Ordinal);
            constants.Add(Encoding.UTF8.GetString(Convert.FromHexString(hex)), definition.Groups[1].Value);
        }

        var entries = destructions.Split(';').Select(item =>
        {
            var separator = item.LastIndexOf('=');
            var text = item[..separator];
            var count = int.Parse(item.AsSpan(separator + 1), System.Globalization.CultureInfo.InvariantCulture);
            return (Symbol: constants.GetValueOrDefault(text), Length: Encoding.UTF8.GetByteCount(text), Count: count);
        }).ToArray();
        ScalarEmissionTest.WriteFixture(name + "Audit", StringLifetimeAudit.Instrument(ir, entries, order: order), stdout, exit, stderr);
    }
}
