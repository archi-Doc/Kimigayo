// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class StringResultEmissionTest
{
    private const string Choice = "var c = true\nlet result = if c => \"a\" else => \"b\"\nwriteLine(result)";
    private const string SelfChoice = "var c = true\nvar text = \"old\"\ntext = if c => text else => \"new\"\nwriteLine(text)";
    private const string Deferred = "var i = 0\nloop\n    defer\n        let result = if i == 1 => \"first\" else => \"later\"\n        writeLine(result)\n    i += 1\n    if i == 3 => exit\n    continue";

    public static TheoryData<string, string, string, string> Fixtures => new()
    {
        { "StringResultTrue", Choice, "a\n", "a=1;b=0" },
        { "StringResultFalse", Choice.Replace("true", "false"), "b\n", "a=0;b=1" },
        { "StringResultEmpty", "writeLine(if true => \"\" else => \"unused\")", "\n", "=1;unused=0" },
        { "StringResultDirectCall", "writeLine(if false => \"a\" else => \"b\")", "b\n", "a=0;b=1" },
        { "StringResultNested", "let result = if true => (if false => \"a\" else => \"b\") else => \"c\"\nwriteLine(result)", "b\n", "a=0;b=1;c=0" },
        { "StringResultDo", "let text = work: do\n    exit to work: \"a\"\nwriteLine(text)", "a\n", "a=1" },
        { "StringResultTrailing", "let text = do => \"a\"\nwriteLine(text)", "a\n", "a=1" },
        { "StringResultElseIf", "var n = 2\nlet text = if n == 1 => \"a\" else if n == 2 => \"b\" else => \"c\"\nwriteLine(text)", "b\n", "a=0;b=1;c=0" },
        { "StringResultYield", "let text = choice: if true\n    loop => yield to choice: \"a\"\nelse => \"b\"\nwriteLine(text)", "a\n", "a=1;b=0" },
        { "StringResultLoop", "var i = 0\nlet text = loop\n    i += 1\n    if i < 3 => continue\n    exit \"a\"\nwriteLine(text)", "a\n", "a=1" },
        { "StringResultRepeated", "var i = 0\nwhile i < 3\n    let text = if i == 0 => \"first\" else => \"later\"\n    writeLine(text)\n    i += 1", "first\nlater\nlater\n", "first=1;later=2" },
        { "StringResultSelfTrue", SelfChoice, "old\n", "old=1;new=0" },
        { "StringResultSelfFalse", SelfChoice.Replace("true", "false"), "new\n", "old=1;new=1" },
        { "StringResultSnapshot", "var text = \"old\"\nlet result = work: do\n    defer => text = \"new\"\n    exit to work: text\nwriteLine(result)\nwriteLine(text)", "old\nnew\n", "old=1;new=1" },
        { "StringResultLocalMove", "var text = \"local\"\nlet result = if true => text else => \"other\"\nwriteLine(result)", "local\n", "local=1;other=0" },
        { "StringResultDeferred", Deferred, "first\nlater\nlater\n", "first=1;later=2" },
        { "StringResultDrop", "let text = if true => \"a\" else => \"b\"", string.Empty, "a=1;b=0" },
        { "StringResultDead", "if false\n    let text = if true => \"a\" else => \"b\"\nwriteLine(\"ok\")", "ok\n", "a=0;b=0;ok=1" },
        { "StringResultOutward", "let text = outer: do\n    let result = work: do\n        exit to work: (if false => \"a\" else => exit to outer: \"b\")\n    exit to outer: result\nwriteLine(text)", "b\n", "a=0;b=1" },
        { "StringResultReplacementOrder", "var text = \"old\"\ntext = work: do\n    defer => writeLine(\"cleanup\")\n    exit to work: \"new\"\nwriteLine(text)", "cleanup\nnew\n", "old=1;new=1;cleanup=1" },
        { "StringResultPartialDivergence", "let text = if true => \"a\" else\n    defer => loop => ()\n    yield \"b\"\nwriteLine(text)", "a\n", "a=1;b=0" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ResultOwnershipFollowsNormalArrivals(string name, string source, string stdout, string destructions)
    {
        var ir = ScalarEmissionTest.EmitFixture(name, source, stdout);
        Assert.DoesNotContain("phi %kimi.string", ir);
        Assert.DoesNotContain("load %kimi.string", ir);
        StringEmissionTest.WriteAuditedFixture(name, source, ir, stdout, destructions);
    }

    [Fact]
    public void SecuredResultIsNotDestroyedWhenCleanupAborts()
    {
        const string Source = "var text = \"old\"\ntext = work: do\n    defer\n        var n = 2147483647\n        n += 1\n    exit to work: \"secured\"";
        const string Error = "Hello.kimi:5:9: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture("StringResultAbort", Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("StringResultAbort", Source, ir, string.Empty, "old=0;secured=0", 1, Error);
    }

    [Fact]
    public void AbortBeforeAcquisitionDoesNotInitializeAResult()
    {
        const string Source = "let text = if 2147483647 + 1 > 0 => \"a\" else => \"b\"";
        const string Error = "Hello.kimi:1:15: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture("StringResultAbortBefore", Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("StringResultAbortBefore", Source, ir, string.Empty, "a=0;b=0", 1, Error);
    }

    [Fact]
    public void DivergentCleanupHasNoArrivalOrResultDestruction()
    {
        const string Source = "let text = work: do\n    defer => loop => ()\n    exit to work: \"secured\"";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var body = c.Ownership.Bodies[0];
        var result = Assert.Single(body.StringResults);
        Assert.Equal(0, result.Count);
        Assert.False(body.IsReachable(result.Join));
        Assert.Single(body.ResultWrites);
        Assert.DoesNotContain(module.GetFunction(0).Instructions, x => x.Callee == WindowsLowering.DestroyString);
        ScalarEmissionTest.EmitFixture("StringResultDivergent", Source, string.Empty, timeoutMilliseconds: 300);
    }

    [Fact]
    public void DeferredReplicasShareOnlyStorageAndKeepDistinctPlans()
    {
        var c = MinimalEmissionTest.Analyze(Deferred);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[0];
        Assert.True(body.StringResults.Count > 1);
        var place = Assert.Single(body.StringResults.Select(x => x.Place).Distinct());
        Assert.Single(module.GetFunction(0).Slots, x => x.Place == place);
        Assert.Equal(body.StringResults.Count, body.StringResults.Select(x => x.Declare).Distinct().Count());
        Assert.All(body.StringResults, x => Assert.Equal(-1, body.Operations[x.Join].Place));
        Assert.DoesNotContain(place, module.GetFunction(0).LiveFlags);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NestedDeferredExpansionKeepsOneResultSlotPerSource(int depth)
    {
        var source = new System.Text.StringBuilder("var n = 0\nloop\n");
        for (var i = 0; i < depth; i++)
        {
            source.Append(' ', (i + 1) * 4).Append("defer\n");
            source.Append(' ', (i + 2) * 4).Append("let text").Append(i).Append(" = if true => \"selected\" else => \"unused\"\n");
        }

        for (var i = depth - 1; i >= 0; i--)
        {
            source.Append(' ', (i + 2) * 4).Append("if true => exit\n");
            source.Append(' ', (i + 2) * 4).Append("if false => exit\n");
        }

        source.Append("    n += 1\n    if n == 1 => continue\n    if n == 2 => continue\n    exit");
        var c = MinimalEmissionTest.Analyze(source.ToString());
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var body = c.Ownership.Bodies[0];
        var places = body.StringResults.Select(x => x.Place).Distinct().ToArray();
        Assert.Equal(depth, places.Length);
        Assert.True(body.StringResults.Count > depth);
        Assert.Equal(depth, module.GetFunction(0).Slots.Count(x => places.Contains(x.Place)));
        Assert.All(places, p => Assert.DoesNotContain(p, module.GetFunction(0).LiveFlags));
    }

    [Fact]
    public void LoopBackedgesKeepTheResultUninitializedWithoutFlags()
    {
        var c = MinimalEmissionTest.Analyze("var i = 0\nlet text = loop\n    i += 1\n    if i < 3 => continue\n    exit \"a\"\nwriteLine(text)");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[0];
        var result = Assert.Single(body.StringResults);
        var head = result.Declare + 1;
        Assert.IsType<LoopKoto>(body.Operations[head].Source);
        Assert.Equal(OwnershipOperationKind.Branch, body.Operations[head].Kind);
        Assert.Equal(PlaceState.None, body.GetInputState(head, result.Place));
        Assert.Single(body.Operations, x => x.Kind == OwnershipOperationKind.Declare && x.Place == result.Place);
        Assert.Empty(module.GetFunction(0).LiveFlags);
    }

    [Fact]
    public void AnUnconsumedResultUsesItsCleanupPlan()
    {
        // Model a consumer that discards the already-acquired result. No new source
        // syntax is claimed: later multi-argument calls can also need this cleanup.
        var c = MinimalEmissionTest.Analyze("let text = if true => \"a\" else => \"b\"");
        var body = c.Ownership.Bodies[0];
        var result = Assert.Single(body.StringResults);
        var consumer = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Write && x.Input == result.Place);
        Assert.Equal(body.CleanupStepStorage.Count - 1, body.OperationSteps[consumer]);
        body.CleanupStepStorage.RemoveAt(body.OperationSteps[consumer]);
        body.OperationSteps[consumer] = -1;
        body.OperationStorage[consumer] = body.OperationStorage[consumer] with { Kind = OwnershipOperationKind.Branch, Place = -1, Input = -1 };
        body.Values[consumer] = new(OwnershipValueKind.None, 0, 0);
        body.Solve();
        Assert.Empty(body.Issues);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var function = module.GetFunction(0);
        Assert.Contains(function.Instructions, x => x.Callee == WindowsLowering.DestroyString && function.GetOperands(x)[0].Value == result.Place);
        Assert.Contains(body.CleanupSteps, x => x.Place == result.Place && x.Action == CleanupAction.Destroy);
    }

    [Fact]
    public void ResultWriteEmitsMoveBeforeConditionalReplacement()
    {
        var c = MinimalEmissionTest.Analyze(SelfChoice);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[0];
        var function = module.GetFunction(0);
        foreach (var write in body.ResultWrites)
        {
            Assert.Contains(function.Instructions, x => x.Operation == write.Operation && x.Opcode == EmissionOpcode.MoveString);
        }

        var index = function.Instructions.FindIndex(x => x.Opcode == EmissionOpcode.DestroyStringIfLive);
        Assert.True(index >= 0);
        Assert.Equal(EmissionOpcode.MoveString, function.Instructions[index + 1].Opcode);
        Assert.Equal(Assert.Single(body.StringResults).Place, function.GetOperands(function.Instructions[index + 1])[0].Value);
        Assert.Equal(EmissionOpcode.StoreLiveFlag, function.Instructions[index + 2].Opcode);
        Assert.Equal(1, function.Instructions[index + 2].Constant);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("write")]
    [InlineData("arrival")]
    [InlineData("duplicate")]
    [InlineData("declaration")]
    [InlineData("branch_place")]
    [InlineData("produce")]
    [InlineData("type")]
    [InlineData("dominance")]
    public void MalformedResultPlansAreRejectedAndRecover(string defect)
    {
        var c = MinimalEmissionTest.Analyze(Choice);
        var body = c.Ownership.Bodies[0];
        var result = Assert.Single(body.StringResults);
        switch (defect)
        {
            case "plan": body.StringResults.Clear(); break;
            case "write": body.ResultWrites.RemoveAt(0); break;
            case "arrival": body.ResultArrivals[0] = body.ResultArrivals[0] with { Write = -1 }; break;
            case "duplicate": body.ResultArrivals[1] = body.ResultArrivals[0]; break;
            case "declaration": body.StringResults[0] = result with { Declare = result.Join }; break;
            case "branch_place": body.OperationStorage[result.Join] = body.OperationStorage[result.Join] with { Place = result.Place }; break;
            case "type":
                var scalar = body.PlaceStorage.FindIndex(x => ReferenceEquals(x.Type, BoundType.Boolean));
                var write = body.ResultWrites[0].Operation;
                body.OperationStorage[write] = body.OperationStorage[write] with { Input = scalar };
                break;
            case "dominance": body.ResultArrivals[0] = body.ResultArrivals[0] with { Write = body.ResultArrivals[1].Write }; break;
            case "produce":
                var literal = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Produce && x.Source is StringLiteralKoto);
                body.OperationStorage[literal] = body.OperationStorage[literal] with { Source = body.Places[result.Place].Source };
                break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Theory]
    [InlineData("let text = if true => \"a\"")]
    [InlineData("if false\n    let text = if true => \"a\"")]
    [InlineData("func f() -> string => if true => \"a\" else => \"b\"\nwriteLine(\"ok\")")]
    [InlineData("let text = if true => \"a\" else => \"b\"\nwriteLine(text)\nwriteLine(text)")]
    public void InvalidAndOutOfScopeResultsPublishNoIr(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void WarmResultAnalysisAndWritingAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Deferred);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var valid = true;
        for (var i = 0; i < 128; i++)
        {
            valid &= c.Ownership.Analyze().IsVerified;
            valid &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(valid);
    }
}
