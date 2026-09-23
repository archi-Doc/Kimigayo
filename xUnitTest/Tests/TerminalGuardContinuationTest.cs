// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class TerminalGuardContinuationTest
{
    [Theory]
    [InlineData("return")]
    [InlineData("exit")]
    [InlineData("exit to scope")]
    [InlineData("if c => return else => false")]
    [InlineData("not (return)")]
    [InlineData("(return) and c")]
    [InlineData("(return) or c")]
    public void GuardTransfersPreserveMixedSourceExtents(string guard)
    {
        var c = MinimalEmissionTest.Analyze(Source("var x = 1", guard, "x = 2", "let y = x"));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        foreach (var body in c.Ownership.Bodies)
        {
            foreach (var region in body.CheckingRegions.Skip(1))
            {
                if (region.Entry >= 0)
                {
                    Assert.False(body.IsReachable(region.Entry));
                }
            }
        }
    }

    [Theory]
    [InlineData("not 1")]
    [InlineData("(return) and 1")]
    [InlineData("(return) or 1")]
    public void LogicalGuardStillRejectsNonBooleanOperands(string guard)
    {
        var c = MinimalEmissionTest.Analyze(Source("var x = 1", guard, "x = 2", "let y = x"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.TypeMismatch);
        Assert.False(c.Emission.Validate(out _));
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
    public void GuardYieldTargetsAnEnclosingSelection()
    {
        var source = Source("var x = 1", "yield to scope", "x = 2", "let y = x")
            .Replace("scope: do", "scope: if true", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Theory]
    [InlineData("return")]
    [InlineData("if c => return else => false")]
    [InlineData("guard: do\n                    return\n                    flag == flag\n                    exit to guard: true\n                ")]
    public void AbandonedStringProtectionDoesNotEscapeIntoBodyAcquisition(string guard)
    {
        var c = MinimalEmissionTest.Analyze(Source("var x = 1", guard, "Console.writeLine(flag)", "let y = x", "\"subject\""));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.All(c.Ownership.Bodies, body => Assert.True(body.ValidateComparisonLoans()));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Theory]
    [InlineData("stop()", "c", "()")]
    [InlineData("$abort(\"direct\")", "\"subject\"", "Console.writeLine(flag)")]
    [InlineData("halt(flag)", "\"subject\"", "Console.writeLine(flag)")]
    [InlineData("loop => ()", "\"subject\"", "Console.writeLine(flag)")]
    [InlineData("if c => stop() else => false", "\"subject\"", "Console.writeLine(flag)")]
    public void NoncompletingGuardsKeepCheckingWithoutRuntimeSelection(string guard, string subject, string body)
    {
        var c = MinimalEmissionTest.Analyze("func halt(text: ref/string) -> Never => $abort(\"halt\")\n" +
            Source("var x = 1", guard, body, "let y = x", subject));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.All(c.Ownership.Bodies, body => Assert.True(body.ValidateComparisonLoans()));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Fact]
    public void GuardTransfersReleaseExactlyOnceAndDoNotExecuteCheckingBodies()
    {
        var source = "func run(early: bool)\n    let outer = \"outer\"\n    match \"subject\"\n        let candidate if (if early => return else => false) => Console.writeLine(candidate)\n        _ => Console.writeLine(\"fallback\")\n    Console.writeLine(outer)\nrun(true)\nrun(false)\n" +
            Source("var x = 1", "return", "Console.writeLine(flag)", "let y = x", "\"unselected\"");
        const string Name = "TerminalGuardWindowTransfers";
        var ir = ScalarEmissionTest.EmitFixture(Name, source, "fallback\nouter\n");
        StringEmissionTest.WriteAuditedFixture(Name, source, ir, "fallback\nouter\n", "outer=2;subject=2;fallback=1;unselected=0");
    }

    [Fact]
    public void AbortingGuardDoesNotAcquireOrDestroySubject()
    {
        const string Source = "func halt(text: ref/string) -> Never => $abort(\"halt\")\nmatch \"held\"\n    let text if halt(text) => Console.writeLine(text)\n    _ => Console.writeLine(\"fallback\")";
        const string Name = "TerminalGuardWindowAbort";
        var stderr = "Hello.kimi:1:" + (Source.IndexOf("$abort", StringComparison.Ordinal) + 1) + ": abort KIMI_E_ABORT: halt\n";
        var ir = ScalarEmissionTest.EmitFixture(Name, Source, string.Empty, 1, stderr);
        StringEmissionTest.WriteAuditedFixture(Name, Source, ir, string.Empty, "held=0;fallback=0", 1, stderr);
    }

    [Fact]
    public void DivergentGuardKeepsCleanupUnreachable()
    {
        const string Source = "match \"held\"\n    let text if (loop => ()) => Console.writeLine(text)\n    _ => Console.writeLine(\"fallback\")";
        ScalarEmissionTest.EmitFixture("TerminalGuardWindowDivergence", Source, string.Empty, timeoutMilliseconds: 300);
        var c = MinimalEmissionTest.Analyze(Source);
        var body = Assert.Single(c.Ownership.Bodies, x => x.Matches.Count != 0);
        Assert.False(body.IsReachable(body.MatchArms[0].BodyEntry));
        // Ownership conservatively retains Pattern failure; lowering proves the
        // catch-all always enters this guard. Its protection end is checking-only.
        // The writeLine calls end their own argument borrows (SPEC 22.4) and are excluded.
        Assert.All(
            body.Operations.Select((op, id) => (op, id)).Where(x => x.op.Kind == OwnershipOperationKind.EndComparisonLoans && x.op.Source is not InvocationKoto),
            x => Assert.False(body.IsReachable(x.id)));
    }

    [Theory]
    [InlineData("let s = \"s\"", "return", "_ = s@move", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "if c => return else => false", "x = 2", "x = 3", OwnershipFailure.ReassignedLet)]
    public void UnselectedBodiesStillCheckEffects(string declaration, string guard, string body, string tail, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, guard, body, tail));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Empty(c.Ownership.ControlFlow!.Issues);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    private static string Source(string declaration, string guard, string body, string tail, string subject = "c")
        => "func stop() -> Never => $abort(\"terminal guard\")\nfunc f(c: bool)\n    " + declaration +
            "\n    scope: do\n        loop\n            if c => return else => exit\n            match " + subject + "\n                let flag if (" + guard + ") => " + body +
            "\n                _ => ()\n        stop()\n    " + tail + "\nf(true)";
}
