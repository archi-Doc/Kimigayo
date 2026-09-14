// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AggregateResultEmissionTest
{
    private const string Self = "var c = true\nvar pair = (\"old\", 1)\npair = if c => pair else => (\"new\", 2)";
    private const string Deferred = "var i = 0\nloop\n    defer\n        let pair = if i == 1 => (\"first\", 1) else => (\"later\", 2)\n    i += 1\n    if i == 3 => exit\n    continue";

    public static TheoryData<string, string, string, string> Fixtures => new()
    {
        { "AggregateSelectionTrue", "let pair = if true => (\"a\", 1) else => (\"b\", 2)", string.Empty, "a=1;b=0" },
        { "AggregateSelectionFalse", "let pair = if false => (\"a\", 1) else => (\"b\", 2)", string.Empty, "a=0;b=1" },
        { "AggregateSelectionSelf", Self, string.Empty, "old=1;new=0" },
        { "AggregateSelectionReplace", Self.Replace("true", "false"), string.Empty, "old=1;new=1" },
        { "AggregateSelectionDo", "let pair = work: do\n    exit to work: (\"a\", 1)", string.Empty, "a=1" },
        { "AggregateSelectionTrailing", "let pair = do => (\"a\", 1)", string.Empty, "a=1" },
        { "AggregateSelectionYield", "let pair = choice: if true\n    loop => yield to choice: (\"a\", 1)\nelse => (\"b\", 2)", string.Empty, "a=1;b=0" },
        { "AggregateSelectionLoop", "var i = 0\nlet pair = loop\n    i += 1\n    if i < 3 => continue\n    exit (\"a\", 1)", string.Empty, "a=1" },
        { "AggregateSelectionNested", "let pair = if true => (if false => (\"a\", 1) else => (\"b\", 2)) else => (\"c\", 3)", string.Empty, "a=0;b=1;c=0" },
        { "AggregateSelectionPayload", "let nested = (if true => (\"a\", 1) else => (\"b\", 2), \"outer\")", string.Empty, "a=1;b=0;outer=1" },
        { "AggregateSelectionDeferred", Deferred, string.Empty, "first=1;later=2" },
        { "AggregateSelectionRepeated", "var i = 0\nwhile i < 3\n    let pair = if i == 0 => (\"first\", 1) else => (\"later\", 2)\n    i += 1", string.Empty, "first=1;later=2" },
        { "AggregateSelectionSnapshot", "var pair = (\"old\", 1)\nlet saved = work: do\n    defer => pair = (\"new\", 2)\n    exit to work: pair", string.Empty, "old=1;new=1" },
        { "AggregateSelectionOutward", "let pair = outer: do\n    let inner = work: do\n        exit to work: (if false => (\"a\", 1) else => exit to outer: (\"b\", 2))\n    exit to outer: inner", string.Empty, "a=0;b=1" },
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
            StringEmissionTest.WriteAuditedFixture(name, source, ir, stdout, destruction);
        }
    }

    [Fact]
    public void DeferredReplicasShareResultStorageWithSeparateLifetimes()
    {
        var c = MinimalEmissionTest.Analyze(Deferred);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[0];
        Assert.True(body.SlotResults.Count > 1);
        var place = Assert.Single(body.SlotResults.Select(x => x.Place).Distinct());
        Assert.Single(module.GetFunction(0).Slots, x => x.Place == place);
        Assert.Equal(body.SlotResults.Count, body.SlotResults.Select(x => x.Declare).Distinct().Count());
        Assert.DoesNotContain(place, module.GetFunction(0).LiveFlags);
        Assert.All(body.SlotResults, x => Assert.Equal(-1, body.Operations[x.Join].Place));
    }

    [Fact]
    public void LoopResultRemainsUninitializedOnTheBackedge()
    {
        var c = MinimalEmissionTest.Analyze("var i = 0\nlet pair = loop\n    i += 1\n    if i < 3 => continue\n    exit (\"a\", 1)");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[0];
        var result = Assert.Single(body.SlotResults);
        Assert.IsType<LoopKoto>(body.Operations[result.Declare + 1].Source);
        Assert.Equal(PlaceState.None, body.GetInputState(result.Declare + 1, result.Place));
        Assert.Single(body.Operations, x => x.Kind == OwnershipOperationKind.Declare && x.Place == result.Place);
        Assert.Empty(module.GetFunction(0).LiveFlags);
    }

    [Fact]
    public void ReplacementPublishesInitializationAfterTransfer()
    {
        var c = MinimalEmissionTest.Analyze(Self);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var function = module.GetFunction(0);
        var body = c.Ownership.Bodies[0];
        foreach (var write in body.ResultWrites)
        {
            Assert.Contains(function.Instructions, x => x.Operation == write.Operation && x.Opcode == EmissionOpcode.TransferAggregate);
        }

        var index = function.Instructions.FindIndex(x => x.Opcode == EmissionOpcode.DestroyAggregate && x.Continuation >= 0);
        Assert.True(index >= 0);
        Assert.Equal(EmissionOpcode.TransferAggregate, function.Instructions[index + 1].Opcode);
        Assert.Equal(Assert.Single(body.SlotResults).Place, function.Instructions[index + 1].Constant);
        Assert.Equal(EmissionOpcode.StoreLiveFlag, function.Instructions[index + 2].Opcode);
        Assert.Equal(1, function.Instructions[index + 2].Constant);
    }

    [Fact]
    public void SnapshotKeepsItsValueAcrossDeferredReplacement()
    {
        const string Source = "var pair = (\"old\", 1)\nlet saved = work: do\n    defer => pair = (\"new\", 2)\n    exit to work: pair";
        var ir = ScalarEmissionTest.EmitFixture("AggregateSelectionSnapshotOrder", Source, string.Empty);
        StringEmissionTest.WriteAuditedFixture("AggregateSelectionSnapshotOrder", Source, ir, string.Empty, "old=1;new=1", order: [0, 1]);
    }

    [Fact]
    public void SecuredResultIsNotDestroyedWhenCleanupAborts()
    {
        const string Source = "var pair = (\"old\", 1)\npair = work: do\n    defer\n        var n = 2147483647\n        n += 1\n    exit to work: (\"secured\", 2)";
        const string Error = "Hello.kimi:5:9: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture("AggregateSelectionAbort", Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("AggregateSelectionAbort", Source, ir, string.Empty, "old=0;secured=0", 1, Error);
    }

    [Fact]
    public void NonterminatingCleanupHasNoArrivalOrSubsequentDestruction()
    {
        const string Source = "let pair = work: do\n    defer => loop => ()\n    exit to work: (\"secured\", 2)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var result = Assert.Single(c.Ownership.Bodies[0].SlotResults);
        Assert.Equal(0, result.Count);
        Assert.False(c.Ownership.Bodies[0].IsReachable(result.Join));
        Assert.DoesNotContain(module.GetFunction(0).Instructions, x => x.Opcode == EmissionOpcode.DestroyAggregate);
        var ir = ScalarEmissionTest.EmitFixture("AggregateSelectionDivergent", Source, string.Empty, timeoutMilliseconds: 300);
        Assert.DoesNotContain("mustprogress", ir);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("write")]
    [InlineData("arrival")]
    [InlineData("duplicate")]
    [InlineData("declaration")]
    [InlineData("branch")]
    [InlineData("type")]
    [InlineData("dominance")]
    public void MalformedPlansPublishNoIrAndRecover(string defect)
    {
        var c = MinimalEmissionTest.Analyze("let pair = if true => (\"a\", 1) else => (\"b\", 2)");
        var body = c.Ownership.Bodies[0];
        var result = Assert.Single(body.SlotResults);
        switch (defect)
        {
            case "plan": body.SlotResults.Clear(); break;
            case "write": body.ResultWrites.RemoveAt(0); break;
            case "arrival": body.ResultArrivals[0] = body.ResultArrivals[0] with { Write = -1 }; break;
            case "duplicate": body.ResultArrivals[1] = body.ResultArrivals[0]; break;
            case "declaration": body.SlotResults[0] = result with { Declare = result.Join }; break;
            case "branch": body.OperationStorage[result.Join] = body.OperationStorage[result.Join] with { Place = result.Place }; break;
            case "dominance": body.ResultArrivals[0] = body.ResultArrivals[0] with { Write = body.ResultArrivals[1].Write }; break;
            case "type":
                var write = body.ResultWrites[0].Operation;
                body.OperationStorage[write] = body.OperationStorage[write] with { Input = body.PlaceStorage.FindIndex(x => ReferenceEquals(x.Type, BoundType.Boolean)) };
                break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADeferredConsumerCannotReadASecuredUndeliveredResult(bool text)
    {
        var source = "var original = (\"old\", 1)\nvar target = (\"init\", 1)\nlet saved = work: do\n    defer => target = (\"cleanup\", 1)\n    exit to work: original";
        if (text)
        {
            source = source.Replace("(\"old\", 1)", "\"old\"").Replace("(\"init\", 1)", "\"init\"").Replace("(\"cleanup\", 1)", "\"cleanup\"");
        }

        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var result = Assert.Single(body.SlotResults);
        var deferred = Assert.Single(body.DeferredPlans, x => body.IsReachable(x.Entry));
        var write = body.OperationStorage.FindIndex(deferred.Entry, deferred.End - deferred.Entry, x => x.Kind == OwnershipOperationKind.Write);
        Assert.True(write >= 0);
        body.OperationStorage[write] = body.OperationStorage[write] with { Input = result.Place };
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out error));
        Assert.Contains("normal delivery", error);
    }

    [Fact]
    public void WarmBindingAndResultEmissionAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Deferred);
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

        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(valid);
        Assert.Equal(0, bindingBytes);
        Assert.Equal(0, bytes);
    }
}
