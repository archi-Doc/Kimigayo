// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class ContinuationVerificationTest
{
    [Fact]
    public void PreparedTupleCopiesPreserveNamedAndReturnedArgumentSnapshots()
    {
        const string Source = "func pair(n: i32) -> (i32, bool) => (n, true)\n" +
            "func choose(a: (i32, bool), b: (i32, bool), x: i32 = (match a\n    let saved => saved.0 + b.0\n), y: i32 = (match (b, x)@move\n    ((let n, _), let value) if n > 0 => n + value\n    _ => x\n)) -> i32 => y\n" +
            "var value = (3, true)\nif choose(b: pair(4), a: value) != 11 or value.0 != 3 => $abort(\"snapshot\")\n" +
            "if choose(pair(2), pair(5), y: 19) != 19 => $abort(\"supplied\")\n" +
            "var i = 0\nvar sum = 0\nwhile i < 4\n    sum += choose(pair(i), pair(2))\n    i += 1\nif sum != 22 => $abort(\"repeated\")\nConsole.writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("VerificationContinuation" + Configuration + "TupleCalls", Source, "ok\n");
    }

    [Fact]
    public void ScopedFalseGuardCleansTemporariesBeforeSubjectAcquisition()
    {
        const string Source = "func same(a: ref/string, b: ref/string) -> bool => a == b\n" +
            "match \"subject\"\n    let text if (label guard: do\n        let local = \"guard\"\n        exit to guard same(text, \"other\")\n    ) => ()\n    let text if same(text, \"subject\") => Console.writeLine(text)\n    _ => ()";
        var name = "VerificationContinuation" + Configuration + "GuardCleanup";
        var ir = ScalarEmissionTest.EmitFixture(name, Source, "subject\n");
        StringEmissionTest.WriteAuditedFixture(name, Source, ir, "subject\n", "subject=2;guard=1;other=1", order: [2, 1, 0, 0]);
    }

    [Theory]
    [InlineData("Literal", "(3, true)")]
    [InlineData("Local", "value")]
    [InlineData("Returned", "pair(3)")]
    [InlineData("Selected", "if true => value else => pair(3)")]
    [InlineData("SelectedFalse", "if false => value else => pair(3)")]
    public void TupleDefaultReadsEveryPreparedStorageSource(string name, string argument)
    {
        var source = "func pair(n: i32) -> (i32, bool) => (n, true)\n" +
            "func read(a: (i32, bool), b: (i32, bool), result: i32 = (match a\n    let saved => saved.0 + b.0 + a.0\n)) -> i32 => result\n" +
            "func forward(value: (i32, bool)) -> i32 => read(b: pair(4), a: value)\n" +
            "var value = (3, true)\nif read(b: pair(4), a: (" + argument + ")) != 10 or forward(value) != 10 or value.0 != 3 => $abort(\"prepared tuple\")";
        ScalarEmissionTest.EmitFixture("VerificationContinuation" + Configuration + name, source, string.Empty);
    }

    [Fact]
    public void PreparedSelectionCopyDoesNotRequireElementProjections()
    {
        const string Source = "func f(pair: (i32, bool), result: i32 = (match pair\n    (let n, _) => n\n)) -> i32 => result\n" +
            "if f(if true => (7, true) else => (2, false)) != 7 => $abort(\"selection copy\")";
        ScalarEmissionTest.EmitFixture("VerificationContinuation" + Configuration + "SelectionCopy", Source, string.Empty);
    }

    [Theory]
    [InlineData("Array", "[2 of i32]", "[2, 3]", "value[1]", "")]
    [InlineData("OwnedTuple", "(i32, string)", "(3, \"payload\")", "value.0", "payload=1")]
    [InlineData("OwnedArray", "[2 of (i32, string)]", "[(2, \"first\"), (3, \"last\")]", "value[1].0", "first=1;last=1")]
    public void DefaultsInspectAcquiredAggregateArguments(string name, string type, string value, string read, string drops)
    {
        // A Copy aggregate is acquired bare; an owned one needs @move (SPEC 15.1.5).
        var transfer = drops.Length == 0 ? string.Empty : "@move";
        var source = "func inspect(value: " + type + ", result: i32 = " + read + ") -> i32 => result\n" +
            "func forward(value: " + type + ") -> i32 => inspect(value" + transfer + ")\n" +
            "let value: " + type + " = " + value + "\nif forward(value" + transfer + ") != 3 => $abort(\"prepared aggregate\")";
        var fixture = "VerificationContinuation" + Configuration + "Acquired" + name;
        var ir = ScalarEmissionTest.EmitFixture(fixture, source, string.Empty);
        if (drops.Length != 0)
        {
            StringEmissionTest.WriteAuditedFixture(fixture, source, ir, string.Empty, drops);
        }
    }

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
