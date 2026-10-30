// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class StringComparisonEmissionTest
{
    private const string Echo = "func echo(text: string) -> string => text@move\n";

    public static TheoryData<string, string, string, string> Fixtures => new()
    {
        { "Same", "let text = \"a\"\nif text == text => Console.writeLine(\"ok\")\nConsole.writeLine(text)", "ok\na\n", "a=1;ok=1" },
        { "Nested", "let text = \"a\"\nif text == (if text == \"a\" => \"a\" else => \"b\") => Console.writeLine(\"ok\")\nConsole.writeLine(text)", "ok\na\n", "a=2;b=0;ok=1" },
        { "Temporary", Echo + "if echo(\"a\") == \"a\" => Console.writeLine(\"ok\")", "ok\n", "a=1;ok=1" },
        { "Ordering", "if \"a\" < \"ab\" and \"ab\" > \"a\" and \"a\" <= \"a\" and \"z\" >= \"a\" and \"a\" != \"b\" => Console.writeLine(\"ok\")", "ok\n", "a=0;ab=0;z=0;b=0;ok=1" },
        { "Empty", "if \"\" == \"\" and \"\" < \"a\" and \"a\" > \"\" and \"\" != \"a\" => Console.writeLine(\"ok\")", "ok\n", "=0;a=0;ok=1" },
        { "Unicode", "if \"z\" < \"日本語\" and \"a\\0b\" < \"a\\0c\" and \"a\\0\" != \"a\" => Console.writeLine(\"ok\")", "ok\n", "z=0;日本語=0;a\0b=0;a\0c=0;a\0=0;a=0;ok=1" },
        { "Parameter", "func equal(a: string, b: string) -> bool => a == b\nif equal(\"a\", \"a\") => Console.writeLine(\"ok\")", "ok\n", "a=2;ok=1" },
        { "Loop", "let text = \"a\"\nvar n = 0\nwhile n < 3\n    if text == \"a\" => n += 1\nConsole.writeLine(text)", "a\n", "a=1" },
        { "ReturnLiteral", "func f() -> string\n    let text = \"a\"\n    text == (return \"x\")\n    return \"bad\"\nConsole.writeLine(f())", "x\n", "a=1;x=1;bad=0" },
        { "Defer", "var text = \"a\"\ndefer\n    if text == \"a\" => Console.writeLine(\"ok\")", "ok\n", "a=1;ok=1" },
        { "BranchTransfer", "func f(c: bool) -> string\n    let text = \"a\"\n    if text == (if c => (return \"x\") else => \"a\") => return text@move\n    return \"bad\"\nConsole.writeLine(f(false))\nConsole.writeLine(f(true))", "a\nx\n", "a=3;x=1;bad=0" },
        { "Skipped", Echo + "if false and echo(\"skip\") == \"skip\" => Console.writeLine(\"bad\")\nif true or echo(\"skip\") == \"skip\" => Console.writeLine(\"ok\")", "ok\n", "skip=0;bad=0;ok=1" },
        { "RepeatedConditional", Echo + "var n = 0\nwhile n < 4\n    if n == 1 and echo(\"once\") == \"once\" => Console.writeLine(\"ok\")\n    n += 1", "ok\n", "once=1;ok=1" },
        { "LoopExit", "let text = \"a\"\nif text == (loop => exit \"a\") => Console.writeLine(text)", "a\n", "a=2" },
        { "AbandonedCleanup", "func f() -> string\n    var text = \"a\"\n    defer => text = \"b\"\n    text == (return \"x\")\n    return \"bad\"\nConsole.writeLine(f())", "x\n", "a=1;b=1;x=1;bad=0" },
        { "TwoReturns", "func f(c: bool) -> string\n    let text = \"a\"\n    text == (if c => (return \"x\") else => (return \"y\"))\n    return \"bad\"\nConsole.writeLine(f(true))\nConsole.writeLine(f(false))", "x\ny\n", "a=2;x=1;y=1;bad=0" },
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
        StringEmissionTest.WriteAuditedFixture(name, ir, stdout, destructions);
    }

    [Theory]
    [InlineData("func f() -> string\n    let text = \"a\"\n    text == (return text@move)\n    return \"x\"")]
    [InlineData(Echo + "let text = \"a\"\ntext == echo(text@move)")]
    [InlineData("var text = \"a\"\ntext == label work: do\n    defer => text = \"b\"\n    exit to work \"a\"")]
    [InlineData("var text = \"a\"\ntext == (if false\n    text = \"b\"\n    yield \"a\"\nelse => \"a\")")]
    public void ConflictingOperationsAreLanguageErrors(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict());
        using var writer = new StringWriter();
        Assert.False(c.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void ConditionSecuresBoolBeforeDestructionAndBranching()
    {
        const string Source = Echo + "if echo(\"condition\") == \"literal\" => Console.writeLine(\"bad\")\nConsole.writeLine(\"after\")";
        var ir = ScalarEmissionTest.EmitFixture("StringComparisonConditionOrder", Source, "after\n");
        StringEmissionTest.WriteAuditedFixture("StringComparisonConditionOrder", ir, "after\n", "condition=1;literal=0;bad=0;after=1", order: [0, 3]);
    }

    [Theory]
    [InlineData("Operand", Echo + "func fail() -> string\n    var n = 2147483647\n    n += 1\n    return \"bad\"\nlet text = \"held\"\ntext == fail()", 4, 5, "held=0;bad=0")]
    [InlineData("Cleanup", "func f() -> string\n    var text = \"held\"\n    defer\n        var n = 2147483647\n        n += 1\n    text == (return \"secured\")\n    return \"bad\"\nConsole.writeLine(f())", 5, 9, "held=0;secured=0;bad=0")]
    public void AbortStopsComparisonAndCleanup(string name, string source, int line, int column, string destructions)
    {
        var stderr = $"Hello.kimi:{line}:{column}: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture("StringComparisonAbort" + name, source, string.Empty, 1, stderr);
        StringEmissionTest.WriteAuditedFixture("StringComparisonAbort" + name, ir, string.Empty, destructions, 1, stderr);
    }

    [Fact]
    public void DivergentOperandKeepsTheProgramNonterminating()
    {
        const string Source = "func forever() -> string\n    loop => ()\nlet text = \"held\"\ntext == forever()";
        var ir = ScalarEmissionTest.EmitFixture("StringComparisonDivergent", Source, string.Empty, observationMilliseconds: 300);
        GeneratedIr.Parse(ir).AssertNoTerminationClaim();
    }

    [Fact]
    public void HelpersCompareDistinctBuffersAndSkipUnneededReads()
    {
        const string Source = "if \"a\" != \"b\" => Console.writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture("StringComparisonHelperSource", Source, "ok\n");
        const string Entry = "define void @__kimi_start() noreturn #0 {\nentry:\n";
        // A separate helper fixture, not a claim that source syntax constructs Heap strings.
        // External linkage also keeps unknown-input helper bodies observable after O2.
        ir = NativeAllocationAudit.Instrument(ir, "define internal i32 @__kimi_string_equal(", "define i32 @__kimi_string_equal(");
        ir = NativeAllocationAudit.Instrument(ir, "define internal i32 @__kimi_string_compare(", "define i32 @__kimi_string_compare(");
        ir = NativeAllocationAudit.Instrument(ir, Entry, Entry + "  call void @__test_string_comparisons()\n");
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

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmComparisonAnalysisAndWritingAllocateNothing(bool conditional)
    {
        var c = MinimalEmissionTest.Analyze(conditional ? "var n = 0\nwhile n < 3\n    if n == 1 and \"a\" == \"a\" => Console.writeLine(\"ok\")\n    n += 1" : "func equal(a: string, b: string) -> bool => a == b\nlet text = \"a\"\nif text == (if text == \"a\" => \"a\" else => \"b\") => Console.writeLine(text)");
        var valid = true;
        var allocated = AllocationMeasurement.Measure(() =>
        {
            valid &= c.Analyze().IsVerified;
            valid &= c.WriteIr(TextWriter.Null, out _);
        });

        Assert.Equal(0, allocated);
        Assert.True(valid);
    }
}
