// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class StringGuardEmissionTest
{
    private const string Same = "func same(a: ref/string, b: ref/string) -> bool => a == b\n";

    [Theory]
    [InlineData("Candidate", "match \"a\"\n    let s if same(s, s) => Console.writeLine(s)\n    _ => ()", "a\n")]
    [InlineData("Direct", "match \"a\"\n    let s if s == s => Console.writeLine(s)\n    _ => ()", "a\n")]
    [InlineData("DirectLiteral", "match \"a\"\n    let s if s == \"a\" => Console.writeLine(s)\n    _ => ()", "a\n")]
    [InlineData("LiteralBorrow", "match \"a\"\n    let s if same(s, \"a\") => Console.writeLine(s)\n    _ => ()", "a\n")]
    [InlineData("False", "match \"a\"\n    let s if same(s, \"b\") => Console.writeLine(\"bad\")\n    let s if same(s, \"a\") => Console.writeLine(s)\n    _ => ()", "a\n")]
    [InlineData("Wildcard", "match \"a\"\n    _ if false => ()\n    _ if true => Console.writeLine(\"ok\")\n    _ => ()", "ok\n")]
    [InlineData("Mismatch", "func bad() -> bool\n    Console.writeLine(\"bad\")\n    return true\nmatch \"a\"\n    \"b\" if bad() => ()\n    \"a\" if true => Console.writeLine(\"ok\")\n    _ => ()", "ok\n")]
    [InlineData("BodyVar", "match \"a\"\n    var s if same(s, \"a\")\n        s = \"b\"\n        Console.writeLine(s)\n    _ => ()", "b\n")]
    [InlineData("Return", "func choose(text: string, wanted: ref/string) -> string\n    return match text@move\n        let s if same(s, wanted) => s@move\n        _ => \"other\"\nConsole.writeLine(choose(\"a\", \"a\"))", "a\n")]
    [InlineData("CallSubject", "func echo(s: string) -> string => s@move\nmatch echo(\"a\")\n    let s if same(s, \"a\") => Console.writeLine(s)\n    _ => ()", "a\n")]
    [InlineData("Covered", "match \"a\"\n    _ => Console.writeLine(\"ok\")\n    let s if same(s, s) => Console.writeLine(s)", "ok\n")]
    [InlineData("ShortCircuit", "match \"a\"\n    let s if false and same(s, s) => ()\n    let s if true or same(s, s) => Console.writeLine(s)\n    _ => ()", "a\n")]
    [InlineData("Deferred", "defer\n    match \"a\"\n        let s if same(s, \"a\") => Console.writeLine(s)\n        _ => ()\nConsole.writeLine(\"ok\")", "ok\na\n")]
    [InlineData("Cleanup", "var flag = true\nmatch \"a\"\n    let s if (check: do\n        defer => flag = false\n        exit to check: same(s, s)\n    ) => if not flag => Console.writeLine(s)\n    _ => ()", "a\n")]
    [InlineData("Transfer", "func run() -> string\n    match \"a\"\n        let s if (return \"ok\") => s@move\n        _ => \"other\"\n    return \"bad\"\nConsole.writeLine(run())", "ok\n")]
    [InlineData("Loop", "var n = 0\nwhile n < 3\n    n += 1\n    match \"a\"\n        let s if same(s, \"a\") => Console.writeLine(s)\n        _ => ()", "a\na\na\n")]
    [InlineData("Nested", "match \"a\"\n    let outer if (match \"a\"\n        let inner if same(outer, inner) => true\n        _ => false\n    ) => Console.writeLine(outer)\n    _ => ()", "a\n")]
    [InlineData("OuterLoan", "let text = \"a\"\nif text == (match \"a\"\n    let s if same(s, \"a\") => \"a\"\n    _ => \"b\"\n) => Console.writeLine(text)", "a\n")]
    [InlineData("CheckingRead", "func run() -> string\n    match \"a\"\n        let s if (check: do\n            return \"ok\"\n            same(s, s)\n            exit to check: true\n        ) => s@move\n        _ => \"other\"\n    return \"bad\"\nConsole.writeLine(run())", "ok\n")]
    public void Execute(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("StringGuard" + name, Same + source, stdout);

    [Theory]
    [InlineData("func forever() -> bool\n    loop => ()\nmatch \"held\"\n    let s if forever() => Console.writeLine(s)\n    _ => ()")]
    [InlineData("func forever(a: ref/string) -> bool\n    loop => ()\nmatch \"held\"\n    let s if forever(s) => Console.writeLine(s)\n    _ => ()")]
    public void DivergentGuardHasNoAcquisitionOrCleanup(string source)
    {
        var suffix = source.Contains("forever(s)", StringComparison.Ordinal) ? "Borrow" : "Plain";
        var ir = ScalarEmissionTest.EmitFixture("StringGuardDivergent" + suffix, source, string.Empty, timeoutMilliseconds: 300);
        Assert.DoesNotContain("mustprogress", ir);
        Assert.DoesNotContain("willreturn", ir);
    }

    [Theory]
    [InlineData("match 1\n    let s if (work: do\n        let saved = s@move\n        exit to work: true\n    ) => ()\n    _ => ()")]
    [InlineData("func take(s: string) => ()\nmatch \"a\"\n    let s if (work: do\n        take(s@move)\n        exit to work: true\n    ) => ()\n    _ => ()")]
    [InlineData("match \"a\"\n    var s if (work: do\n        s = \"b\"\n        exit to work: true\n    ) => ()\n    _ => ()")]
    [InlineData("match \"a\"\n    let s if (work: do\n        let saved = s@move\n        exit to work: true\n    ) => ()\n    _ => ()")]
    public void InvalidOrUnsupportedCandidateUsePublishesNothing(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void FalseGuardCleanupPrecedesAcquisitionAndDestroysEachOwnerOnce()
    {
        const string Source = Same + "match \"subject\"\n    let s if same(s, \"other\") => ()\n    let s if same(s, \"subject\") => Console.writeLine(s)\n    _ => ()";
        var ir = ScalarEmissionTest.EmitFixture("StringGuardLifetime", Source, "subject\n");
        StringEmissionTest.WriteAuditedFixture("StringGuardLifetime", Source, ir, "subject\n", "subject=2;other=1", order: [1, 0, 0]);
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[0];
        var pattern = body.Matches[0].Binding.Positions[0];
        Assert.True(ReferenceTypes.IsString(pattern.CandidateSymbol!.Type));
        Assert.Same(BoundType.String, pattern.BodySymbol!.Type);
        Assert.NotSame(pattern.CandidateSymbol, pattern.BodySymbol);
        Assert.Same(pattern.CandidateSymbol.Declaration, pattern.BodySymbol.Declaration);
        Assert.DoesNotContain(module.GetFunction(0).Slots, x => ReferenceTypes.IsString(body.Places[x.Place].Type));
        Assert.Empty(module.GetFunction(0).LiveFlags);
        foreach (var arm in body.MatchArms.Where(x => x.GuardLoan >= 0))
        {
            Assert.False(body.HasComparisonLoan(arm.GuardBranch, arm.GuardLoan));
            Assert.Equal(OwnershipOperationKind.EndComparisonLoans, body.Operations[arm.GuardBranch - 1].Kind);
            Assert.True(body.HasComparisonLoan(arm.GuardBranch - 1, arm.GuardLoan));
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("anchor")]
    [InlineData("early")]
    [InlineData("read")]
    [InlineData("origin")]
    [InlineData("guard_entry")]
    [InlineData("guard_match")]
    [InlineData("read_move")]
    public void InvalidLoanPlansAreRejected(string defect)
    {
        var c = MinimalEmissionTest.Analyze(Same + "match \"a\"\n    let s if same(s, s) => Console.writeLine(s)\n    _ => ()");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var arm = body.MatchArms[0];
        var read = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.Borrow);
        switch (defect)
        {
            case "missing": body.MatchArmStorage[0] = arm with { GuardLoan = -1 }; break;
            case "anchor": body.ComparisonLoans[arm.GuardLoan] = body.ComparisonLoans[arm.GuardLoan] with { Place = 0 }; break;
            case "early": body.LoanInputs[read] = -1; break;
            case "read": body.OperationSteps[read] = 1; break;
            case "origin": body.OperationStorage[read] = body.Operations[read] with { Source = body.Operations[arm.GuardEntry].Source }; break;
            case "guard_entry": body.MatchArmStorage[0] = arm with { GuardEntry = int.MaxValue }; break;
            case "guard_match": body.MatchArmStorage[0] = arm with { Match = int.MaxValue }; break;
            case "read_move": body.OperationStorage[read] = body.Operations[read] with { Acquisition = AcquisitionKind.Move }; break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void WarmCandidateBindingAndEmissionAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Same + "match \"a\"\n    let s if same(s, \"a\") => Console.writeLine(s)\n    _ => ()");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var valid = true;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++)
        {
            valid &= c.Bind().IsComplete;
        }

        var bindingBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        c.Binding.CheckStartup(OutputKind.Application);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++)
        {
            valid &= c.Ownership.Analyze().IsVerified;
            valid &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        var emissionBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(valid);
        Assert.Equal(0, bindingBytes);
        Assert.Equal(0, emissionBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbortDoesNotUnwindGuardOrBorrowedTemporary(bool temporary)
    {
        var source = temporary
            ? "func fail(a: ref/string) -> bool\n    let n: i32 = 2147483647 + 1\n    return true\nfail(\"held\")"
            : "func fail() -> bool\n    let n: i32 = 2147483647 + 1\n    return true\nmatch \"held\"\n    let s if fail() => Console.writeLine(s)\n    _ => ()";
        const string Error = "Hello.kimi:2:18: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var name = temporary ? "StringGuardTemporaryAbort" : "StringGuardAbort";
        var ir = ScalarEmissionTest.EmitFixture(name, source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture(name, source, ir, string.Empty, "held=0", 1, Error);
    }
}
