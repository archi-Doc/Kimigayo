// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class OwnedPatternVerificationTest
{
    [Fact]
    public void DivergentGuardNeverDestroysOwnedStorageBeforeOrDuringDivergence()
    {
        const string Source = "match \"held\"\n    let text if (loop => ()) => Console.writeLine(text)\n    _ => Console.writeLine(\"fallback\")";
        var c = MinimalEmissionTest.Analyze(Source);
        using var writer = new StringWriter();
        Assert.True(c.Emission.WriteIr(writer, out var error), error);
        // No handle is allowed: any destruction terminates immediately with 121.
        // An exit-only count check could never detect early cleanup before a loop.
        var audited = StringLifetimeAudit.Instrument(writer.ToString(), []);
        ScalarEmissionTest.WriteFixture("VerificationOwnedPatternDivergenceAudit", audited, string.Empty, timeoutMilliseconds: 300);
    }

    [Fact]
    public void DirectAbortGuardDoesNotAcquireBindingsOrUnwindTheSubject()
    {
        const string Source = "match (\"held\", 1)\n    (_, let n) if $abort(\"die\") => ()\n    _ => ()";
        const string Fixture = "VerificationOwnedPatternDirectAbort";
        const string Error = "Hello.kimi:2:19: abort KIMI_E_ABORT: die\n";
        var ir = ScalarEmissionTest.EmitFixture(Fixture, Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture(Fixture, Source, ir, string.Empty, "held=0;die=0", 1, Error);
    }

    [Theory]
    [InlineData("Exit", "do", "exit", "")]
    [InlineData("Yield", "if true", "yield", "\n    else => \"unused\"")]
    public void GuardTransferSecuresResultBeforeLeavingOwnedScopes(string name, string selection, string transfer, string otherwise)
    {
        var source = "func run() -> string\n    let outer = \"outer\"\n    let result: string = target: " + selection +
            "\n        let inner = \"inner\"\n        match (\"subject\", 1)\n            (_, let n) if (" + transfer +
            " to target: \"result\") => $abort(\"selected\")\n            _ => $abort(\"fallback\")" + otherwise +
            "\n    return result@move\nConsole.writeLine(run())";
        var fixture = "VerificationOwnedPattern" + name;
        var ir = ScalarEmissionTest.EmitFixture(fixture, source, "result\n");
        StringEmissionTest.WriteAuditedFixture(fixture, source, ir, "result\n", "outer=1;inner=1;subject=1;result=1", order: [2, 1, 0, 3]);
    }

    [Fact]
    public void OwnedAggregateBindingsMoveAndDestroyInReverseAcquisitionOrder()
    {
        const string Source = """
            enum Packet
                Empty
                Pair(string, string)
            func run(consume: bool)
                match (("first", "second"), Packet.Pair("third", "fourth"), "remaining")
                    (let pair, let packet, _)
                        if consume
                            match packet@move
                                .Pair(let a, let b)
                                    Console.writeLine(a)
                                    Console.writeLine(b)
                                .Empty => ()
                        match pair@move
                            (let a, let b) => ()
            run(true)
            run(false)
            """;
        const string Fixture = "VerificationOwnedPatternAggregateBindings";
        const string Output = "third\nfourth\n";
        var ir = ScalarEmissionTest.EmitFixture(Fixture, Source, Output);
        // writeLine borrows a and b (SPEC 22.4); the arm destroys them in reverse acquisition order.
        StringEmissionTest.WriteAuditedFixture(Fixture, Source, ir, Output, "first=2;second=2;third=2;fourth=2;remaining=2", order: [3, 2, 1, 0, 4, 1, 0, 3, 2, 4]);
    }

    [Fact]
    public void NestedStringPatternsDistinguishByteLengthAndNeverReadInactivePayloads()
    {
        const string Source = """
            enum Item
                Number(i32)
                Text(string)
            func classify(value: Item) -> i32
                return match value@move
                    .Text("a") => 1
                    .Text("a\0") => 2
                    .Text("a\0b") => 3
                    .Text("") => 4
                    .Text(_) => 5
                    .Number(let n) => n
            if classify(.Text("a")) != 1 => $abort("one byte")
            if classify(.Text("a\0")) != 2 => $abort("nul tail")
            if classify(.Text("a\0b")) != 3 => $abort("nul middle")
            if classify(.Text("")) != 4 => $abort("empty")
            if classify(.Text("ab")) != 5 => $abort("prefix")
            if classify(.Number(6)) != 6 => $abort("inactive payload")
            """;
        const string Fixture = "VerificationOwnedPatternLengths";
        var ir = ScalarEmissionTest.EmitFixture(Fixture, Source, string.Empty);
        StringEmissionTest.WriteAuditedFixture(Fixture, Source, ir, string.Empty, "a=1;a\0=1;a\0b=1;=1;ab=1");
    }

    [Theory]
    [InlineData("and", "false", "true", 2)]
    [InlineData("or", "true", "false", 1)]
    public void LogicalGuardsSkipOrDeliverTheTerminalRightOperand(string operation, string skipped, string taken, int normal)
    {
        var source = "func run(value: bool) -> i32\n    return match true\n        let flag if value " + operation +
            " (return 9) => 1\n        _ => 2\nif run(" + skipped + ") != " + normal +
            " => $abort(\"skipped\")\nif run(" + taken + ") != 9 => $abort(\"terminal\")";
        ScalarEmissionTest.EmitFixture("VerificationOwnedPatternLogical" + operation, source, string.Empty);
    }
}
