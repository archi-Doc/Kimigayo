// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class AggregateResultEmissionTest
{
    private const string Self = "var c = true\nvar pair = (\"old\", 1)\npair = if c => pair@move else => (\"new\", 2)";
    private const string Deferred = "var i = 0\nloop\n    defer\n        let pair = if i == 1 => (\"first\", 1) else => (\"later\", 2)\n    i += 1\n    if i == 3 => exit\n    continue";

    public static TheoryData<string, string, string, string> Fixtures => new()
    {
        { "AggregateSelectionTrue", "let pair = if true => (\"a\", 1) else => (\"b\", 2)", string.Empty, "a=1;b=0" },
        { "AggregateSelectionFalse", "let pair = if false => (\"a\", 1) else => (\"b\", 2)", string.Empty, "a=0;b=1" },
        { "AggregateSelectionSelf", Self, string.Empty, "old=1;new=0" },
        { "AggregateSelectionReplace", Self.Replace("true", "false"), string.Empty, "old=1;new=1" },
        { "AggregateSelectionDo", "let pair = label work: do\n    exit to work (\"a\", 1)", string.Empty, "a=1" },
        { "AggregateSelectionTrailing", "let pair = do => (\"a\", 1)", string.Empty, "a=1" },
        { "AggregateSelectionYield", "let pair = label choice: if true\n    loop => yield to choice (\"a\", 1)\nelse => (\"b\", 2)", string.Empty, "a=1;b=0" },
        { "AggregateSelectionLoop", "var i = 0\nlet pair = loop\n    i += 1\n    if i < 3 => continue\n    exit (\"a\", 1)", string.Empty, "a=1" },
        { "AggregateSelectionNested", "let pair = if true => (if false => (\"a\", 1) else => (\"b\", 2)) else => (\"c\", 3)", string.Empty, "a=0;b=1;c=0" },
        { "AggregateSelectionPayload", "let nested = (if true => (\"a\", 1) else => (\"b\", 2), \"outer\")", string.Empty, "a=1;b=0;outer=1" },
        { "AggregateSelectionDeferred", Deferred, string.Empty, "first=1;later=2" },
        { "AggregateSelectionRepeated", "var i = 0\nwhile i < 3\n    let pair = if i == 0 => (\"first\", 1) else => (\"later\", 2)\n    i += 1", string.Empty, "first=1;later=2" },
        { "AggregateSelectionSnapshot", "var pair = (\"old\", 1)\nlet saved = label work: do\n    defer => pair = (\"new\", 2)\n    exit to work pair@move", string.Empty, "old=1;new=1" },
        { "AggregateSelectionOutward", "let pair = label outer: do\n    let inner = label work: do\n        exit to work (if false => (\"a\", 1) else => exit to outer (\"b\", 2))\n    exit to outer inner@move", string.Empty, "a=0;b=1" },
        { "AggregateSelectionArray", "let values: [2 of string] = if true => [\"a\", \"b\"] else => [\"c\", \"d\"]", string.Empty, "a=1;b=1;c=0;d=0" },
        { "AggregateSelectionArrayLoop", "let values: [2 of string] = loop => exit [\"a\", \"b\"]", string.Empty, "a=1;b=1" },
        { "AggregateSelectionZero", "let values: [0 of string] = if true => [] else => []", string.Empty, string.Empty },
        { "AggregateSelectionUnit", "let values: [2 of ()] = if true => [(), ()] else => [(), ()]", string.Empty, string.Empty },
        { "AggregateSelectionCopy", "let values: (u8, i128, f64, char) = if true => (200, 42, 2.5, 'a') else => (100, 43, 3.5, 'b')\nlet a = values\nlet b = values", string.Empty, string.Empty },
        { "AggregateSelectionMatch", "let pair = match true\n    true => (\"a\", 1)\n    false => (\"b\", 2)", string.Empty, "a=1;b=0" },
        { "AggregateSelectionCovered", "let pair = match true\n    _ => (\"a\", 1)\n    true => (\"b\", 2)", string.Empty, "a=1;b=0" },
        { "AggregateSelectionGuard", "let pair = match 1\n    let n if n == 0 => (\"a\", 1)\n    _ => (\"b\", 2)", string.Empty, "a=0;b=1" },
        { "AggregateSelectionDrop", "if true => (\"a\", 1) else => (\"b\", 2)", string.Empty, "a=1;b=0" },
        { "AggregateSelectionPartial", "let pair = if true => (\"a\", 1) else\n    defer => loop => ()\n    yield (\"b\", 2)", string.Empty, "a=1;b=0" },
        { "AggregateSelectionFunctionLocal", "func f() -> ()\n    let pair = if true => (\"a\", 1) else => (\"b\", 2)\nf()", string.Empty, "a=1;b=0" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ResultsExecuteWithExistingCleanup(string name, string source, string stdout, string destruction)
    {
        var ir = ScalarEmissionTest.EmitFixture(name, source, stdout);
        if (destruction.Length != 0)
        {
            StringEmissionTest.WriteAuditedFixture(name, ir, stdout, destruction);
        }
    }

    [Fact]
    public void SnapshotKeepsItsValueAcrossDeferredReplacement()
    {
        const string Source = "var pair = (\"old\", 1)\nlet saved = label work: do\n    defer => pair = (\"new\", 2)\n    exit to work pair@move";
        var ir = ScalarEmissionTest.EmitFixture("AggregateSelectionSnapshotOrder", Source, string.Empty);
        StringEmissionTest.WriteAuditedFixture("AggregateSelectionSnapshotOrder", ir, string.Empty, "old=1;new=1", order: [0, 1]);
    }

    [Fact]
    public void SecuredResultIsNotDestroyedWhenCleanupAborts()
    {
        const string Source = "var pair = (\"old\", 1)\npair = label work: do\n    defer\n        var n = 2147483647\n        n += 1\n    exit to work (\"secured\", 2)";
        const string Error = "Hello.kimi:5:9: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture("AggregateSelectionAbort", Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("AggregateSelectionAbort", ir, string.Empty, "old=0;secured=0", 1, Error);
    }

    [Fact]
    public void NonterminatingCleanupHasNoArrivalOrSubsequentDestruction()
    {
        const string Source = "let pair = label work: do\n    defer => loop => ()\n    exit to work (\"secured\", 2)";
        var ir = ScalarEmissionTest.EmitFixture("AggregateSelectionDivergent", Source, string.Empty, observationMilliseconds: 300);
        GeneratedIr.Parse(ir).AssertNoTerminationClaim();
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBindingAndResultEmissionAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Deferred);
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Analyze().IsVerified);
        Assert.True(c.WriteIr(TextWriter.Null, out var error), error);
        var valid = true;
        var bindingBytes = AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        var bytes = AllocationMeasurement.Measure(() =>
        {
            valid &= c.Analyze().IsVerified;
            valid &= c.WriteIr(TextWriter.Null, out _);
        });
        Assert.True(valid);
        Assert.Equal(0, bindingBytes);
        Assert.Equal(0, bytes);
    }
}
