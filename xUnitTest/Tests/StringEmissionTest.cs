// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class StringEmissionTest
{
    private const string Conditional = "var c = true\nvar text = \"old\"\nif c => writeLine(text)\ntext = \"new\"\nwriteLine(text)";
    private const string Loop = "var text = \"outer\"\nvar i = 0\nwhile i < 3\n    if i == 1 => writeLine(text)\n    text = \"next\"\n    i += 1\nwriteLine(text)";

    public static TheoryData<string, string, string, string> Fixtures => new()
    {
        { "StringLocal", "let text = \"hello\"\nwriteLine(text)", "hello\n", "hello=1" },
        { "StringMove", "let text = \"hello\"\nlet other = text\nwriteLine(other)", "hello\n", "hello=1" },
        { "StringDrop", "let text = \"drop\"", string.Empty, "drop=1" },
        { "StringDiscardMove", "let text = \"drop\"\ntext", string.Empty, "drop=1" },
        { "StringEmpty", "var text = \"\"\ntext = text\nwriteLine(text)", "\n", "=1" },
        { "StringUnicode", "let text = \"日本語\\0\"\nwriteLine(text)", "日本語\0\n", "日本語\0=1" },
        { "StringReplace", "var text = \"old\"\ntext = \"new\"\nwriteLine(text)", "new\n", "old=1;new=1" },
        { "StringSelf", "var text = \"self\"\ntext = text\nwriteLine(text)", "self\n", "self=1" },
        { "StringReinitialize", "var text = \"old\"\nwriteLine(text)\ntext = \"new\"", "old\n", "old=1;new=1" },
        { "StringConditionalTrue", Conditional, "old\nnew\n", "old=1;new=1" },
        { "StringConditionalFalse", Conditional.Replace("true", "false"), "new\n", "old=1;new=1" },
        { "StringPartialTrue", "var c = true\nvar text: string\nif c => text = \"partial\"", string.Empty, "partial=1" },
        { "StringPartialFalse", "var c = false\nvar text: string\nif c => text = \"partial\"", string.Empty, "partial=0" },
        { "StringLoop", Loop, "next\nnext\n", "outer=1;next=3" },
        { "StringLoopDeclare", "var i = 0\nwhile i < 3\n    var text: string\n    if i == 0 => text = \"once\"\n    i += 1\n    continue", string.Empty, "once=1" },
        { "StringLoopExit", "var i = 0\nloop\n    var text = \"iteration\"\n    if i == 0 => writeLine(text)\n    i += 1\n    if i == 3 => exit\n    continue", "iteration\n", "iteration=3" },
        { "StringDefer", "var text = \"old\"\ndefer => writeLine(text)\ntext = \"new\"", "new\n", "old=1;new=1" },
        { "StringDeferLocal", "var i = 0\nloop\n    defer\n        var text = \"deferred\"\n        if i == 1 => writeLine(text)\n    i += 1\n    if i == 3 => exit\n    continue", "deferred\n", "deferred=3" },
        { "StringReturn", "func f(c: bool) -> i32\n    var text = \"local\"\n    if c => writeLine(text)\n    return 42\nif f(true) == 42 and f(false) == 42 => writeLine(\"ok\")", "local\nok\n", "local=2;ok=1" },
        { "StringPhi", "var c = true\nlet result = work: do\n    var text = \"local\"\n    if c => writeLine(text)\n    exit to work: 42\nif result == 42 => writeLine(\"ok\")", "local\nok\n", "local=1;ok=1" },
        { "StringExitRhs", "loop\n    var text = \"old\"\n    text = (exit)", string.Empty, "old=1" },
        { "StringReturnRhs", "func f() -> i32\n    var text = \"old\"\n    text = (return 42)\nif f() == 42 => writeLine(\"ok\")", "ok\n", "old=1;ok=1" },
        { "StringSkipped", "if false\n    var text = \"skipped\"\n    text = text\nwriteLine(\"ok\")", "ok\n", "skipped=0;ok=1" },
        { "StringPhiCleanup", "func choose(c: bool, move: bool) -> i32\n    let n = if c\n        var text = \"join\"\n        if move => writeLine(text)\n        yield 40 + 2\n    else => 7\n    return n\nif choose(true, true) == 42 and choose(true, false) == 42 and choose(false, true) == 7 => writeLine(\"ok\")", "join\nok\n", "join=2;ok=1" },
        { "StringDiscardSelection", "if true => (if true => \"a\" else => \"b\")", string.Empty, "a=1;b=0" },
        { "StringExplicitMain", "public func main()\n    var text = \"main\"\n    text = text\n    writeLine(text)", "main\n", "main=1" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void TransfersAndCleanupExecuteOnce(string name, string source, string stdout, string destructions)
    {
        var ir = ScalarEmissionTest.EmitFixture(name, source, stdout);
        Assert.DoesNotContain("load %kimi.string", ir);
        Assert.DoesNotContain("llvm.memcpy", ir);
        WriteAuditedFixture(name, source, ir, stdout, destructions);
    }

    [Theory]
    [InlineData("StringAbortRhs", "func fail() -> Never\n    var x = 2147483647\n    x += 1\n    loop => ()\nvar text = \"old\"\ntext = fail()", 3, 5, "old=0")]
    [InlineData("StringAbortDefer", "var text = \"old\"\ndefer\n    var other = \"inner\"\n    var x = 2147483647\n    x += 1", 5, 5, "old=0;inner=0")]
    public void AbortDoesNotContinueDestruction(string name, string source, int line, int column, string destructions)
    {
        var stderr = $"Hello.kimi:{line}:{column}: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture(name, source, string.Empty, 1, stderr);
        WriteAuditedFixture(name, source, ir, string.Empty, destructions, 1, stderr);
    }

    [Theory]
    [InlineData("func unused() -> string => \"a\"\nwriteLine(\"ok\")")]
    [InlineData("func unused(text: string) => ()\nwriteLine(\"ok\")")]
    [InlineData("let text = \"a\"\ntext@string")]
    [InlineData("let text = \"a\"\nwriteLine(text)\nwriteLine(text)")]
    [InlineData("var text: string\nvar c = true\nif c => text = \"a\"\nwriteLine(text)")]
    public void UnsupportedResultsAndInvalidMovesPublishNoIr(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void ReplacementOrdersDestructionThenMoveThenFlag()
    {
        var c = MinimalEmissionTest.Analyze(Conditional);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var function = module.GetFunction(0);
        var index = function.Instructions.FindIndex(x => x.Opcode == EmissionOpcode.DestroyStringIfLive);
        Assert.True(index >= 0);
        Assert.Equal(EmissionOpcode.MoveString, function.Instructions[index + 1].Opcode);
        Assert.Equal(EmissionOpcode.StoreLiveFlag, function.Instructions[index + 2].Opcode);
        Assert.Equal(1, function.Instructions[index + 2].Constant);
        using var writer = new StringWriter();
        module.WriteIr(writer);
        var ir = writer.ToString();
        var id = function.Instructions[index].Operation;
        var destroy = ir.IndexOf($"destroy{id}:\n", StringComparison.Ordinal);
        var store = ir.IndexOf($"store ptr %strValue{id}_0", StringComparison.Ordinal);
        var flag = ir.IndexOf("store i8 1, ptr %liveSlot", store, StringComparison.Ordinal);
        Assert.True(destroy >= 0 && destroy < store && store < flag);
    }

    [Fact]
    public void MissingOrDuplicateFlagUpdateIsRejected()
    {
        var c = MinimalEmissionTest.Analyze(Conditional);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[0];
        var function = module.GetFunction(0);
        var scratch = new int[body.Places.Count + body.Operations.Count];
        Assert.True(BodyLowering.ValidateStringFlags(body, function, scratch));
        for (var index = 0; index < function.Instructions.Count; index++)
        {
            var instruction = function.Instructions[index];
            if (instruction.Opcode != EmissionOpcode.StoreLiveFlag)
            {
                continue;
            }

            function.Instructions.RemoveAt(index);
            Assert.False(BodyLowering.ValidateStringFlags(body, function, scratch));
            function.Instructions.Insert(index, instruction);
            function.Instructions.Insert(index, instruction);
            Assert.False(BodyLowering.ValidateStringFlags(body, function, scratch));
            function.Instructions.RemoveAt(index);
            function.Instructions[index] = instruction with { Constant = 1 - instruction.Constant };
            Assert.False(BodyLowering.ValidateStringFlags(body, function, scratch));
            function.Instructions[index] = instruction;
            Assert.True(BodyLowering.ValidateStringFlags(body, function, scratch));
        }
    }

    [Fact]
    public void SelfAssignmentKeepsBothMovesAndNoOldValueDestruction()
    {
        var c = MinimalEmissionTest.Analyze("var text = \"self\"\ntext = text\nwriteLine(text)");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var function = module.GetFunction(0);
        Assert.Equal(4, function.Instructions.Count(x => x.Opcode == EmissionOpcode.MoveString));
        Assert.DoesNotContain(function.Instructions, x => x.Callee == WindowsLowering.DestroyString);
        Assert.Empty(function.LiveFlags);
    }

    [Theory]
    [InlineData("temporary")]
    [InlineData("action")]
    [InlineData("input")]
    public void InvalidStringPlansFailBeforeWritingAndRecover(string defect)
    {
        var c = MinimalEmissionTest.Analyze("var text = \"old\"\ntext = \"new\"\nwriteLine(text)");
        var body = c.Ownership.Bodies[0];
        if (defect == "input")
        {
            var write = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Write && x.Input >= 0 && ReferenceEquals(body.Places[x.Place].Type, BoundType.String));
            body.OperationStorage[write] = body.OperationStorage[write] with { Input = -1 };
        }
        else
        {
            var index = body.CleanupStepStorage.FindIndex(x => ReferenceEquals(body.Places[x.Place].Type, BoundType.String) &&
                (defect == "temporary" ? body.Places[x.Place].Kind == OwnershipPlaceKind.Temporary : body.IsReachable(x.Operation) && x.Action == CleanupAction.Destroy));
            Assert.True(index >= 0);
            body.CleanupStepStorage[index] = body.CleanupStepStorage[index] with { Action = defect == "temporary" ? CleanupAction.Conditional : CleanupAction.Skip };
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Fact]
    public void NoncompletingRhsHasNoStringWrite()
    {
        var c = MinimalEmissionTest.Analyze("loop\n    var text = \"old\"\n    text = (exit)");
        Assert.True(c.Emission.Validate(out var error), error);
        Assert.DoesNotContain(c.Ownership.Bodies[0].Operations, x => x.Kind == OwnershipOperationKind.Write && x.Place >= 0 && ReferenceEquals(c.Ownership.Bodies[0].Places[x.Place].Type, BoundType.String) && x.Input < 0);
    }

    [Fact]
    public void DivergentDeferPreventsStringDestructionAndDelivery()
    {
        const string Source = "var text = \"held\"\ndefer => loop => ()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var function = module.GetFunction(0);
        Assert.DoesNotContain(function.Instructions, x => x.Callee == WindowsLowering.DestroyString || x.Opcode is EmissionOpcode.ReturnVoid or EmissionOpcode.ReturnScalar);
        var body = c.Ownership.Bodies[0];
        Assert.All(body.Operations.Select((x, id) => (x, id)).Where(x => x.x.Kind == OwnershipOperationKind.Deliver), x => Assert.False(body.IsReachable(x.id)));
        var ir = ScalarEmissionTest.EmitFixture("StringDivergentDefer", Source, string.Empty, timeoutMilliseconds: 300);
        Assert.DoesNotContain("mustprogress", ir);
        Assert.DoesNotContain("willreturn", ir);
    }

    [Fact]
    public void WarmStringAnalysisAndWritingAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Loop);
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

    [Fact]
    public void AuditDetectsDoubleDestructionAndMissingDestruction()
    {
        const string Source = "let text = \"single\"\nwriteLine(text)";
        var ir = ScalarEmissionTest.EmitFixture("StringAuditControl", Source, "single\n");
        var duplicate = StringLifetimeAudit.Instrument(ir, [("__kimi_text", 6, 1)], duplicate: true);
        ScalarEmissionTest.WriteFixture("StringAuditDuplicate", duplicate, "single\n", 120, string.Empty);
        var missing = StringLifetimeAudit.Instrument(ir, [("__kimi_text", 6, 2)]);
        ScalarEmissionTest.WriteFixture("StringAuditMissing", missing, "single\n", 120, string.Empty);
    }

    internal static void WriteAuditedFixture(string name, string source, string ir, string stdout, string destructions, int exit = 0, string? stderr = null)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var entries = destructions.Split(';').Select(item =>
        {
            var separator = item.LastIndexOf('=');
            var text = item[..separator];
            var constant = text.Length == 0 ? null : Enumerable.Range(0, module.Constants.Count).Select(i => module.Constants[i]).Single(x => x.Value == text);
            return (Symbol: constant?.Name, Length: constant?.ByteLength ?? 0, Count: int.Parse(item.AsSpan(separator + 1), System.Globalization.CultureInfo.InvariantCulture));
        }).ToArray();
        ScalarEmissionTest.WriteFixture(name + "Audit", StringLifetimeAudit.Instrument(ir, entries), stdout, exit, stderr);
    }
}
