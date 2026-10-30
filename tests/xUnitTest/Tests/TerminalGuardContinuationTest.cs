// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class TerminalGuardContinuationTest
{
    [Theory]
    [InlineData("not 1")]
    [InlineData("(return) and 1")]
    [InlineData("(return) or 1")]
    public void LogicalGuardStillRejectsNonBooleanOperands(string guard)
    {
        var c = MinimalEmissionTest.Analyze(Source("var x = 1", guard, "x = 2", "let y = x"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.TypeMismatch);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Fact]
    public void LogicalGuardTransfersNeverEvaluateTheAbandonedOperand()
    {
        const string Source = """
            func rhs() -> bool => $abort("right side executed")
            func unary() -> i32
                match true
                    let flag if not (return 1) => $abort("unary selected")
                    _ => $abort("unary fallback")
                return 0
            func conjunction() -> i32
                match true
                    let flag if (return 2) and rhs() => $abort("and selected")
                    _ => $abort("and fallback")
                return 0
            func disjunction() -> i32
                match true
                    let flag if (return 3) or rhs() => $abort("or selected")
                    _ => $abort("or fallback")
                return 0
            if unary() != 1 => $abort("unary result")
            if conjunction() != 2 => $abort("and result")
            if disjunction() != 3 => $abort("or result")
            Console.writeLine("ok")
            """;
        ScalarEmissionTest.EmitFixture("TerminalGuardWindowLogical", Source, "ok\n");
    }

    [Fact]
    public void GuardTransfersReleaseExactlyOnce()
    {
        const string Source = "func run(early: bool)\n    let outer = \"outer\"\n    match \"subject\"\n        let candidate if (if early => return else => false) => Console.writeLine(candidate)\n        _ => Console.writeLine(\"fallback\")\n    Console.writeLine(outer)\nrun(true)\nrun(false)";
        const string Name = "TerminalGuardWindowTransfers";
        var ir = ScalarEmissionTest.EmitFixture(Name, Source, "fallback\nouter\n");
        StringEmissionTest.WriteAuditedFixture(Name, ir, "fallback\nouter\n", "outer=2;subject=2;fallback=1");
    }

    [Fact]
    public void AbortingGuardDoesNotAcquireOrDestroySubject()
    {
        const string Source = "func halt(text: ref/string) -> Never => $abort(\"halt\")\nmatch \"held\"\n    let text if halt(text) => Console.writeLine(\"selected\")\n    _ => Console.writeLine(\"fallback\")";
        const string Name = "TerminalGuardWindowAbort";
        var stderr = "Hello.kimi:1:" + (Source.IndexOf("$abort", StringComparison.Ordinal) + 1) + ": abort KIMI_E_ABORT: halt\n";
        var ir = ScalarEmissionTest.EmitFixture(Name, Source, string.Empty, 1, stderr);
        StringEmissionTest.WriteAuditedFixture(Name, ir, string.Empty, "held=0;fallback=0", 1, stderr);
    }

    // A guard that never completes leaves its body and the end of its candidate's borrow to the checks (their MIR: tests/mir/matches.kimi
    // `loopingGuard`).
    [Fact]
    public void DivergentGuardKeepsCleanupUnreachable()
    {
        const string Source = "match \"held\"\n    let text if (loop => ()) => Console.writeLine(\"selected\")\n    _ => Console.writeLine(\"fallback\")";
        ScalarEmissionTest.EmitFixture("TerminalGuardWindowDivergence", Source, string.Empty, observationMilliseconds: 300);
    }

    private static string Source(string declaration, string guard, string body, string tail, string subject = "c")
        => "func stop() -> Never => $abort(\"terminal guard\")\nfunc f(c: bool)\n    " + declaration +
            "\n    label scope: do\n        loop\n            if c => return else => exit\n            match " + subject + "\n                let flag if (" + guard + ") => " + body +
            "\n                _ => ()\n        stop()\n    " + tail + "\nf(true)";
}
