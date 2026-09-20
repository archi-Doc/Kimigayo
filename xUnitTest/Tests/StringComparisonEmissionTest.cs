// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class StringComparisonEmissionTest
{
    private const string Echo = "func echo(text?: string) -> string => text\n";

    public static TheoryData<string, string, string, string> Fixtures => new()
    {
        { "Same", "let text = \"a\"\nif text == text => Console.writeLine(\"ok\")\nConsole.writeLine(text)", "ok\na\n", "a=1;ok=1" },
        { "Nested", "let text = \"a\"\nif text == (if text == \"a\" => \"a\" else => \"b\") => Console.writeLine(\"ok\")\nConsole.writeLine(text)", "ok\na\n", "a=3;b=0;ok=1" },
        { "Temporary", Echo + "if echo(\"a\") == \"a\" => Console.writeLine(\"ok\")", "ok\n", "a=2;ok=1" },
        { "Ordering", "if \"a\" < \"ab\" and \"ab\" > \"a\" and \"a\" <= \"a\" and \"z\" >= \"a\" and \"a\" != \"b\" => Console.writeLine(\"ok\")", "ok\n", "a=6;ab=2;z=1;b=1;ok=1" },
        { "Empty", "if \"\" == \"\" and \"\" < \"a\" and \"a\" > \"\" and \"\" != \"a\" => Console.writeLine(\"ok\")", "ok\n", "=5;a=3;ok=1" },
        { "Unicode", "if \"z\" < \"日本語\" and \"a\\0b\" < \"a\\0c\" and \"a\\0\" != \"a\" => Console.writeLine(\"ok\")", "ok\n", "z=1;日本語=1;a\0b=1;a\0c=1;a\0=1;a=1;ok=1" },
        { "Parameter", "func equal(a?: string, b?: string) -> bool => a == b\nif equal(\"a\", \"a\") => Console.writeLine(\"ok\")", "ok\n", "a=2;ok=1" },
        { "Loop", "let text = \"a\"\nvar n = 0\nwhile n < 3\n    if text == \"a\" => n += 1\nConsole.writeLine(text)", "a\n", "a=4" },
        { "ReturnLiteral", "func f() -> string\n    let text = \"a\"\n    text == (return \"x\")\n    return \"bad\"\nConsole.writeLine(f())", "x\n", "a=1;x=1;bad=0" },
        { "Defer", "var text = \"a\"\ndefer\n    if text == \"a\" => Console.writeLine(\"ok\")", "ok\n", "a=2;ok=1" },
        { "BranchTransfer", "func f(c?: bool) -> string\n    let text = \"a\"\n    if text == (if c => (return \"x\") else => \"a\") => return text\n    return \"bad\"\nConsole.writeLine(f(false))\nConsole.writeLine(f(true))", "a\nx\n", "a=3;x=1;bad=0" },
        { "Skipped", Echo + "if false and echo(\"skip\") == \"skip\" => Console.writeLine(\"bad\")\nif true or echo(\"skip\") == \"skip\" => Console.writeLine(\"ok\")", "ok\n", "skip=0;bad=0;ok=1" },
        { "RepeatedConditional", Echo + "var n = 0\nwhile n < 4\n    if n == 1 and echo(\"once\") == \"once\" => Console.writeLine(\"ok\")\n    n += 1", "ok\n", "once=2;ok=1" },
        { "LoopExit", "let text = \"a\"\nif text == (loop => exit \"a\") => Console.writeLine(text)", "a\n", "a=2" },
        { "AbandonedCleanup", "func f() -> string\n    var text = \"a\"\n    defer => text = \"b\"\n    text == (return \"x\")\n    return \"bad\"\nConsole.writeLine(f())", "x\n", "a=1;b=1;x=1;bad=0" },
        { "CheckingContinuation", Echo + "func f() -> string\n    let text = \"a\"\n    text == (work: do\n        return \"x\"\n        echo(text)\n        exit to work: \"a\"\n    )\n    return \"bad\"\nConsole.writeLine(f())", "x\n", "a=1;x=1;bad=0" },
        { "TwoReturns", "func f(c?: bool) -> string\n    let text = \"a\"\n    text == (if c => (return \"x\") else => (return \"y\"))\n    return \"bad\"\nConsole.writeLine(f(true))\nConsole.writeLine(f(false))", "x\ny\n", "a=2;x=1;y=1;bad=0" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ComparisonsPreserveOwnership(string name, string source, string stdout, string destructions)
    {
        name = "StringComparison" + name;
        var checkedCompilation = MinimalEmissionTest.Analyze(source);
        foreach (var body in checkedCompilation.Ownership.Bodies)
        {
            Assert.True(body.ValidateComparisonLoans(), string.Join("; ", body.Edges.Where(e => body.LoanInputs.Count > 0 && e.Kind != OwnershipEdgeKind.Abort && body.LoanStates[e.From] != body.LoanInputs[e.To]).Select(e => $"{e.From}:{body.Operations[e.From].Kind}({body.IsReachable(e.From)}) -> {e.To}:{body.Operations[e.To].Kind}({body.IsReachable(e.To)})")));
        }

        var ir = ScalarEmissionTest.EmitFixture(name, source, stdout);
        StringEmissionTest.WriteAuditedFixture(name, source, ir, stdout, destructions);
    }

    [Theory]
    [InlineData("func f() -> string\n    let text = \"a\"\n    text == (return text)\n    return \"x\"")]
    [InlineData(Echo + "let text = \"a\"\ntext == echo(text)")]
    [InlineData("var text = \"a\"\ntext == do\n    defer => text = \"b\"\n    exit \"a\"")]
    [InlineData("var text = \"a\"\ntext == (if false\n    text = \"b\"\n    yield \"a\"\nelse => \"a\")")]
    public void ConflictingOperationsAreLanguageErrors(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong_read")]
    [InlineData("early_end")]
    [InlineData("plan")]
    [InlineData("type")]
    public void MissingLoanIsRejectedAndReanalysisRecovers(string defect)
    {
        var c = MinimalEmissionTest.Analyze("let text = \"a\"\ntext == text");
        var body = c.Ownership.Bodies[0];
        Assert.Equal(2, body.ComparisonLoans.Count);
        Assert.True(c.Emission.Validate(out var error), error);
        var plan = body.StringComparisons[0];
        switch (defect)
        {
            case "missing": body.StringComparisons[0] = plan with { LeftLoan = -1 }; break;
            case "wrong_read": body.StringComparisons[0] = plan with { LeftLoan = plan.RightLoan }; break;
            case "early_end": body.LoanInputs[plan.Operation] = -1; break;
            case "plan": body.StringComparisons.Clear(); break;
            case "type": body.StringComparisons[0] = plan with { Left = body.Operations[plan.Operation].Place }; break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void ConditionSecuresBoolBeforeDestructionAndBranching()
    {
        const string Source = Echo + "if echo(\"condition\") == \"literal\" => Console.writeLine(\"bad\")\nConsole.writeLine(\"after\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var function = module.GetFunction(0);
        var comparison = function.Instructions.FindIndex(x => x.Opcode == EmissionOpcode.StringEquals);
        var destroy = function.Instructions.FindIndex(comparison + 1, x => x.Callee == WindowsLowering.DestroyString);
        var branch = function.Instructions.FindIndex(comparison + 1, x => x.Opcode == EmissionOpcode.ConditionalBranch);
        Assert.True(comparison >= 0 && comparison < destroy && destroy < branch);
        var ir = ScalarEmissionTest.EmitFixture("StringComparisonConditionOrder", Source, "after\n");
        StringEmissionTest.WriteAuditedFixture("StringComparisonConditionOrder", Source, ir, "after\n", "condition=1;literal=1;bad=0;after=1", order: [1, 0, 3]);
    }

    [Theory]
    [InlineData("Operand", Echo + "func fail() -> string\n    var n = 2147483647\n    n += 1\n    return \"bad\"\nlet text = \"held\"\ntext == fail()", 4, 5, "held=0;bad=0")]
    [InlineData("Cleanup", "func f() -> string\n    var text = \"held\"\n    defer\n        var n = 2147483647\n        n += 1\n    text == (return \"secured\")\n    return \"bad\"\nConsole.writeLine(f())", 5, 9, "held=0;secured=0;bad=0")]
    public void AbortStopsComparisonAndCleanup(string name, string source, int line, int column, string destructions)
    {
        var stderr = $"Hello.kimi:{line}:{column}: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture("StringComparisonAbort" + name, source, string.Empty, 1, stderr);
        StringEmissionTest.WriteAuditedFixture("StringComparisonAbort" + name, source, ir, string.Empty, destructions, 1, stderr);
    }

    [Fact]
    public void DivergentOperandKeepsTheProgramNonterminating()
    {
        const string Source = "func forever() -> string\n    loop => ()\nlet text = \"held\"\ntext == forever()";
        var ir = ScalarEmissionTest.EmitFixture("StringComparisonDivergent", Source, string.Empty, timeoutMilliseconds: 300);
        Assert.DoesNotContain("mustprogress", ir);
        Assert.DoesNotContain("willreturn", ir);
    }

    [Fact]
    public void ConditionalTemporaryFlagsAreInitializedAndValidated()
    {
        var c = MinimalEmissionTest.Analyze("var c = false\nif c and \"a\" == \"a\" => ()");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[0];
        var function = module.GetFunction(0);
        Assert.Empty(body.ComparisonLoans);
        Assert.Equal(2, function.LiveFlags.Count);
        var scratch = new int[body.Places.Count + body.Operations.Count];
        Assert.True(BodyLowering.ValidateStringFlags(body, function, scratch));
        var index = function.Instructions.FindIndex(x => x.Opcode == EmissionOpcode.InitializeLiveFlag);
        var instruction = function.Instructions[index];
        function.Instructions.RemoveAt(index);
        Assert.False(BodyLowering.ValidateStringFlags(body, function, scratch));
        function.Instructions.Insert(index, instruction);
        function.Instructions.Insert(index, instruction);
        Assert.False(BodyLowering.ValidateStringFlags(body, function, scratch));
        function.Instructions.RemoveAt(index);
        Assert.True(BodyLowering.ValidateStringFlags(body, function, scratch));
    }

    [Fact]
    public void HelpersCompareDistinctBuffersAndSkipUnneededReads()
    {
        const string Source = "if \"a\" != \"b\" => Console.writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture("StringComparisonHelperSource", Source, "ok\n");
        const string Entry = "define void @__kimi_start() noreturn #0 {\nentry:\n";
        Assert.Contains(Entry, ir);
        // A separate helper fixture, not a claim that source syntax constructs Heap strings.
        // External linkage also keeps unknown-input helper bodies observable after O2.
        ir = ir.Replace("define internal i32 @__kimi_string_", "define i32 @__kimi_string_", StringComparison.Ordinal)
            .Replace(Entry, Entry + "  call void @__test_string_comparisons()\n", StringComparison.Ordinal);
        ir += """

            @test_a = private constant [3 x i8] c"a\00b"
            @test_b = private constant [3 x i8] c"a\00b"
            @test_c = private constant [3 x i8] c"a\00c"
            define internal void @__test_string_comparisons() {
            entry:
              %same = call i32 @__kimi_string_equal(ptr @test_a, i64 3, ptr @test_b, i64 3)
              %order = call i32 @__kimi_string_compare(ptr @test_a, i64 3, ptr @test_c, i64 3)
              %differentLength = call i32 @__kimi_string_equal(ptr inttoptr (i64 1 to ptr), i64 1, ptr inttoptr (i64 2 to ptr), i64 2)
              %empty = call i32 @__kimi_string_compare(ptr null, i64 0, ptr inttoptr (i64 1 to ptr), i64 3)
              %a = icmp eq i32 %same, 0
              %b = icmp slt i32 %order, 0
              %c = icmp ne i32 %differentLength, 0
              %d = icmp slt i32 %empty, 0
              %ab = and i1 %a, %b
              %cd = and i1 %c, %d
              %ok = and i1 %ab, %cd
              br i1 %ok, label %success, label %failure
            success:
              ret void
            failure:
              call void @ExitProcess(i32 123)
              unreachable
            }
            """ + "\n";
        ScalarEmissionTest.WriteFixture("StringComparisonHelpers", ir, "ok\n", 0, string.Empty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmComparisonAnalysisAndWritingAllocateNothing(bool conditional)
    {
        var c = MinimalEmissionTest.Analyze(conditional ? "var n = 0\nwhile n < 3\n    if n == 1 and \"a\" == \"a\" => Console.writeLine(\"ok\")\n    n += 1" : "func equal(a?: string, b?: string) -> bool => a == b\nlet text = \"a\"\nif text == (if text == \"a\" => \"a\" else => \"b\") => Console.writeLine(text)");
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
