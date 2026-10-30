// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class StringGuardEmissionTest
{
    private const string Same = "func same(a: ref/string, b: ref/string) -> bool => a == b\n";

    [Theory]
    [InlineData("Candidate", "match \"a\"@move\n    let s if same(s, s) => Console.writeLine(s)\n    _ => ()", "a\n")]
    [InlineData("Direct", "match \"a\"@move\n    let s if s == s => Console.writeLine(s)\n    _ => ()", "a\n")]
    [InlineData("DirectLiteral", "match \"a\"@move\n    let s if s == \"a\" => Console.writeLine(s)\n    _ => ()", "a\n")]
    [InlineData("LiteralBorrow", "match \"a\"@move\n    let s if same(s, \"a\") => Console.writeLine(s)\n    _ => ()", "a\n")]
    [InlineData("False", "match \"a\"@move\n    let s if same(s, \"b\") => Console.writeLine(\"bad\")\n    let s if same(s, \"a\") => Console.writeLine(s)\n    _ => ()", "a\n")]
    [InlineData("Wildcard", "match \"a\"@move\n    _ if false => ()\n    _ if true => Console.writeLine(\"ok\")\n    _ => ()", "ok\n")]
    [InlineData("Mismatch", "func bad() -> bool\n    Console.writeLine(\"bad\")\n    return true\nmatch \"a\"@move\n    \"b\" if bad() => ()\n    \"a\" if true => Console.writeLine(\"ok\")\n    _ => ()", "ok\n")]
    [InlineData("BodyVar", "match \"a\"@move\n    var s if same(s, \"a\")\n        s = \"b\"\n        Console.writeLine(s)\n    _ => ()", "b\n")]
    [InlineData("Return", "func choose(text: string, wanted: ref/string) -> string\n    return match text@move\n        let s if same(s, wanted) => s@move\n        _ => \"other\"\nConsole.writeLine(choose(\"a\", \"a\"))", "a\n")]
    [InlineData("CallSubject", "func echo(s: string) -> string => s@move\nmatch echo(\"a\")\n    let s if same(s, \"a\") => Console.writeLine(s)\n    _ => ()", "a\n")]
    [InlineData("Covered", "match \"a\"@move\n    _ => Console.writeLine(\"ok\")\n    let s if same(s, s) => Console.writeLine(s)", "ok\n")]
    [InlineData("ShortCircuit", "match \"a\"@move\n    let s if false and same(s, s) => ()\n    let s if true or same(s, s) => Console.writeLine(s)\n    _ => ()", "a\n")]
    [InlineData("Deferred", "defer\n    match \"a\"@move\n        let s if same(s, \"a\") => Console.writeLine(s)\n        _ => ()\nConsole.writeLine(\"ok\")", "ok\na\n")]
    [InlineData("Cleanup", "var flag = true\nmatch \"a\"@move\n    let s if (label check: do\n        defer => flag = false\n        exit to check same(s, s)\n    ) => if not flag => Console.writeLine(s)\n    _ => ()", "a\n")]
    [InlineData("Transfer", "func run() -> string\n    match \"a\"@move\n        let s if (return \"ok\") => \"unused\"\n        _ => \"other\"\n    return \"bad\"\nConsole.writeLine(run())", "ok\n")]
    [InlineData("Loop", "var n = 0\nwhile n < 3\n    n += 1\n    match \"a\"@move\n        let s if same(s, \"a\") => Console.writeLine(s)\n        _ => ()", "a\na\na\n")]
    [InlineData("Nested", "match \"a\"@move\n    let outer if (match \"a\"@move\n        let inner if same(outer, inner) => true\n        _ => false\n    ) => Console.writeLine(outer)\n    _ => ()", "a\n")]
    [InlineData("OuterLoan", "let text = \"a\"\nif text == (match \"a\"@move\n    let s if same(s, \"a\") => \"a\"\n    _ => \"b\"\n) => Console.writeLine(text)", "a\n")]
    public void Execute(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("StringGuard" + name, Same + source, stdout);

    [Theory]
    [InlineData("func forever() -> bool\n    loop => ()\nmatch \"held\"@move\n    let s if forever() => Console.writeLine(s)\n    _ => ()")]
    [InlineData("func forever(a: ref/string) -> bool\n    loop => ()\nmatch \"held\"@move\n    let s if forever(s) => Console.writeLine(s)\n    _ => ()")]
    public void DivergentGuardHasNoAcquisitionOrCleanup(string source)
    {
        var suffix = source.Contains("forever(s)", StringComparison.Ordinal) ? "Borrow" : "Plain";
        var ir = ScalarEmissionTest.EmitFixture("StringGuardDivergent" + suffix, source, string.Empty, observationMilliseconds: 300);
        GeneratedIr.Parse(ir).AssertNoTerminationClaim();
    }

    [Theory]
    [InlineData("match 1\n    let s if (label work: do\n        let saved = s@move\n        exit to work true\n    ) => ()\n    _ => ()")]
    [InlineData("func take(s: string) => ()\nmatch \"a\"@move\n    let s if (label work: do\n        take(s@move)\n        exit to work true\n    ) => ()\n    _ => ()")]
    [InlineData("match \"a\"@move\n    var s if (label work: do\n        s = \"b\"\n        exit to work true\n    ) => ()\n    _ => ()")]
    [InlineData("match \"a\"@move\n    let s if (label work: do\n        let saved = s@move\n        exit to work true\n    ) => ()\n    _ => ()")]
    public void InvalidOrUnsupportedCandidateUsePublishesNothing(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void FalseGuardCleanupPrecedesAcquisitionAndDestroysEachOwnerOnce()
    {
        const string Source = Same + "match \"subject\"@move\n    let s if same(s, \"other\") => ()\n    let s if same(s, \"subject\") => Console.writeLine(s)\n    _ => ()";
        var ir = ScalarEmissionTest.EmitFixture("StringGuardLifetime", Source, "subject\n");
        StringEmissionTest.WriteAuditedFixture("StringGuardLifetime", ir, "subject\n", "subject=2;other=1", order: [1, 0, 0]);
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

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmCandidateBindingAndEmissionAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Same + "match \"a\"@move\n    let s if same(s, \"a\") => Console.writeLine(s)\n    _ => ()");
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Analyze().IsVerified);
        Assert.True(c.WriteIr(TextWriter.Null, out var error), error);

        var valid = true;
        var bindingBytes = AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        var emissionBytes = AllocationMeasurement.Measure(() =>
        {
            valid &= c.Analyze().IsVerified;
            valid &= c.WriteIr(TextWriter.Null, out _);
        });
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
            : "func fail() -> bool\n    let n: i32 = 2147483647 + 1\n    return true\nmatch \"held\"@move\n    let s if fail() => Console.writeLine(s)\n    _ => ()";
        const string Error = "Hello.kimi:2:18: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var name = temporary ? "StringGuardTemporaryAbort" : "StringGuardAbort";
        var ir = ScalarEmissionTest.EmitFixture(name, source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture(name, ir, string.Empty, "held=0", 1, Error);
    }
}
