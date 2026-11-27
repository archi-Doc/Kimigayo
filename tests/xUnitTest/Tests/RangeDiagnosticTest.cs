// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Arc.Unit;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class RangeDiagnosticTest(ITestOutputHelper output)
{
    // SPEC 4.6.4, 14.6.2, 23.3 (R7-E3 E3a-3): the record shows the range, its selected entry and the boundary Types, and relates the entry
    // declaration (role "entry"); the constant Note went by code (M3).
    [Theory]
    [InlineData("for i in 1..^1 => ()", "1..^1", "IntoIterable", "Kimi.Range<i32, Kimi.FromEnd<i32>>")]
    [InlineData("for i in ..3 => ()", "..3", "IntoIterable", "Kimi.Range<Kimi.Start, i32>")]
    [InlineData("for i in 1@i32..3@i64 => ()", "1@i32..3@i64", "IntoIterable", "Kimi.Range<i32, i64>")]
    [InlineData("let r = ..=3\nfor i in r => ()", "r", "Iterable", "Kimi.ClosedRange<Kimi.Start, i32>")]
    [InlineData("var r = 1..^1\nfor i in r@uniq => ()", "r@uniq", "UniqIterable", "Kimi.Range<i32, Kimi.FromEnd<i32>>")]
    [InlineData("let r = 1@i32..=3@i64\nfor i in r@move => ()", "r@move", "IntoIterable", "Kimi.ClosedRange<i32, i64>")]
    public void NonIterableRangesExplainTheSelectedEntryAndRepair(string source, string text, string entry, string type)
    {
        var c = Analyze(source);
        var error = Assert.Single(Errors(c));
        Assert.Equal(nameof(DiagnosticCode.UnsatisfiedConstraint_Kd), error.Code);
        Assert.Equal(text, error.Text);
        Assert.Contains(type, error.Label, StringComparison.Ordinal);
        Assert.Contains(entry, error.Label, StringComparison.Ordinal);
        Assert.Equal([("entry", entry)], error.Related!.Select(static x => (x.Role, x.Label)));
        var record = Assert.Single(c.Diagnostics.Finalize().Diagnostics, static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Contains(record.Reason!, x => x.Name == "entry" && x.Value == entry);
        Assert.DoesNotContain(c.Diagnostics.Finalize().Diagnostics, static x => x.Code == nameof(DiagnosticCode.PrerequisiteUnavailable_Kd));
    }

    // SPEC 4.6.4, 8.2: boundary Types not proven to be one integer Type in a generic context are reported once, at the
    // Subject, with the requirement and a conditional repair, relating the entry declaration (R7-E3 E3a-3; the Note went by code, M3).
    [Theory]
    [InlineData("func f<A, B>(p: A, q: B)\n    A is PrimitiveInteger\n    B is PrimitiveInteger\n    for x in p..q => ()", "p..q")]
    [InlineData("func f<P>(p: P, q: P)\n    P is Position\n    for x in p..q => ()", "p..q")]
    public void UnprovenRangeIterationIsExplainedOnce(string source, string text)
    {
        var c = Analyze(source);
        var error = Assert.Single(Errors(c));
        Assert.Equal(nameof(DiagnosticCode.UnprovenConstraint_Kd), error.Code);
        Assert.Equal(text, error.Text);
        Assert.Equal([("entry", "IntoIterable")], error.Related!.Select(static x => (x.Role, x.Label)));
    }

    [Theory]
    [InlineData("func f<A>(p: A, q: A)\n    A is PrimitiveInteger\n    for x in p..q => ()")]
    [InlineData("for i in (1..^1).resolve(5) => ()")]
    [InlineData("for i in (..3).resolve(5) => ()")]
    [InlineData("for i in 1@i64..3@i64 => ()")]
    [InlineData("var r = 1..=3\nfor i in r@uniq => ()")]
    public void RepairedRangesRemainValid(string source)
    {
        var c = Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Empty(Errors(c));
    }

    [Theory]
    [InlineData("struct Start\nlet r = ..3\nlet p: Start = r.start", "r.start", "Kimi.Start", "Start")]
    [InlineData("struct End\nlet r = 1..\nlet p: End = r.end", "r.end", "Kimi.End", "End")]
    [InlineData("struct FromEnd<T>\nlet p: FromEnd<i32> = ^1", "^1", "Kimi.FromEnd<i32>", "FromEnd<i32>")]
    [InlineData("let r: Range<i32, i32> = 1..=3", "1..=3", "Kimi.ClosedRange<i32, i32>", "Kimi.Range<i32, i32>")]

    // SPEC 4.6.2 (PLAN G924, K's revision): a boundary that is not literal-only is checked against the E of the range's expected Type,
    // so the record is the boundary's; the range rests on it and adds none (it was the range's own, `1..n` against
    // Kimi.Range<i32, i32>, before G924).
    [InlineData("let n: i64 = 3\nlet r: Range<i32, i32> = 1..n", "n", "i64", "i32")]
    public void MismatchKeepsCompleteTypesAndTheValueLocation(string source, string text, string actual, string expected)
    {
        var error = Assert.Single(Errors(Analyze(source)));
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), error.Code);
        Assert.Equal(text, error.Text);
        Assert.Equal($"expected {expected}, found {actual}", error.Label);
    }

    [Theory]
    [InlineData("for i in missing..3 => ()", "UnresolvedBinding_Kd")]
    [InlineData("for i in 1..^1 => ()\nlet wrong: i32 = true", "UnsatisfiedConstraint_Kd,TypeMismatch_Kd")]

    // SPEC 4.6.2, 23.1.2 (PLAN G924): each boundary that misfits the expected S or E is its own record; the range adds none.
    [InlineData("let n: i64 = 3\nlet m: i64 = 4\nlet r: Range<i32, i32> = m..n", "TypeMismatch_Kd,TypeMismatch_Kd")]

    // SPEC 4.6.2, 23.1.2 (PLAN G924): a literal-only boundary takes the S or E of the range's expected Type, so its misfit is its own
    // record beside the other boundary's, in either order.
    [InlineData("let n: i64 = 3\nlet r: Range<i32, i32> = n..3000000000", "InvalidNumericLiteral_Kd,TypeMismatch_Kd")]
    [InlineData("let n: i64 = 3\nlet r: Range<i32, i32> = 3000000000..n", "InvalidNumericLiteral_Kd,TypeMismatch_Kd")]
    public void PrerequisitesAndIndependentErrorsRemainExplained(string source, string codes)
        => Assert.Equal(codes.Split(',').Order(StringComparer.Ordinal), Errors(Analyze(source)).Select(static x => x.Code).Order(StringComparer.Ordinal));

    [Theory]
    [InlineData("func take(r: Range<i32, i32>) => ()\ntake(1..=3)")]
    [InlineData("func take<T>(r: Range<T, T>)\n    T is PrimitiveInteger\n    ()\ntake(1..=3)")]
    [InlineData("func take(n: i32 ! r: Range<i32, i32>) => ()\ntake(0, r: 1..=3)")]
    [InlineData("func take(r: Range<i32, i32>) => ()\nlet r = 1..=3\ntake(r)")]
    public void ShapeMismatchNamesTheRejectedSignature(string source)
    {
        var c = Analyze(source);
        var error = Assert.Single(Errors(c));
        Assert.Equal(nameof(DiagnosticCode.NoApplicableOverload_Kd), error.Code);
        var record = Assert.Single(c.Diagnostics.Finalize().Diagnostics, static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Contains(record.Related!, x => x.Label!.Contains("argument has Kimi.ClosedRange<i32, i32>", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("for i in 1..^1 => ()", "UnsatisfiedConstraint_Kd")]
    [InlineData("struct Start\nlet r = ..3\nlet p: Start = r.start", "TypeMismatch_Kd")]
    [InlineData("func take(r: Range<i32, i32>) => ()\ntake(1..=3)", "NoApplicableOverload_Kd")]
    [InlineData("let values: Array<i32> = [1, 2, 3]\nlet p = values.tryGet<i64>(1 << 63)", "PositionAlwaysFails_Kd")]
    [InlineData("let values: Array<i32> = [1, 2, 3]\nlet p = values.tryGet<i8>(127 << 1)", "PositionAlwaysFails_Kd")]
    public void CliAndLanguageServerKeepTheExplanationAndLocation(string source, string code)
    {
        var path = Path.GetFullPath("RangeDiagnostic.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(code, record.Code);
        var console = new DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(record.Code, console.Text, StringComparison.Ordinal);
        Assert.Contains(record.Label ?? record.Message, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        var check = new CheckOutput(CheckOutcome.Completed, c.Binding.Result.IsComplete, TestPresence.No, result);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, related)[identity]);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Equal(record.Code, sent.Code);
            Assert.Contains(record.Label ?? record.Message, sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }

        output.WriteLine(console.Text);
    }

    [Fact]
    public void RebindingAndUnrelatedErrorsPreserveRangeFacts()
    {
        const string source = "for i in 1..^1 => ()";
        var c = Analyze(source);
        var first = Assert.Single(Errors(c));
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(first, Assert.Single(Errors(c)));
        var moved = Assert.Single(Errors(Analyze("\n" + source + "\nlet wrong: i32 = true")), static x => x.Code == nameof(DiagnosticCode.UnsatisfiedConstraint_Kd));
        Assert.Equal(first.Label, moved.Label);
        Assert.Equal(first.Related!.Select(static x => (x.Role, x.Label)), moved.Related!.Select(static x => (x.Role, x.Label)));
        Assert.Equal(first.Span.Start + 1, moved.Span.Start);
    }

    [Fact]
    public void BoundedMismatchKeepsTheDifferentBoundaryTypes()
    {
        var longName = "Container" + new string('x', 120);
        var source = $"struct {longName}<T>\nfunc f(x: {longName}<Range<i32, i64>>)\n    let y: {longName}<Range<i32, i32>> = x";
        var c = Analyze(source);
        var error = Assert.Single(Errors(c));
        Assert.Equal("x", error.Text);
        var record = Assert.Single(c.Diagnostics.Finalize().Diagnostics, static x => x.Severity == DiagnosticSeverity.Error);
        Assert.All(record.Reason!, static x => Assert.True(x.Elided));
        Assert.Contains("i64", record.Reason![0].Value, StringComparison.Ordinal);
        Assert.Contains("i32", record.Reason![1].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuralDisplayDoesNotInventAConcreteArrayLength()
    {
        var error = Assert.Single(Errors(Analyze("func f<length N>(x: ref/[(N + 1) of i32], expected: ref/[(N + 2) of i32])\n    let y: ref/[(N + 2) of i32] = x")));
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), error.Code);
        Assert.Contains("N", error.Label, StringComparison.Ordinal);
        Assert.Contains("1", error.Label, StringComparison.Ordinal);
        Assert.Contains("2", error.Label, StringComparison.Ordinal);
        Assert.DoesNotContain("[0 of", error.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void ShapeExplanationSurvivesTheRelatedCandidateLimit()
    {
        var source = string.Join('\n', new[] { "i8", "u8", "i16", "u16", "i32", "u32", "i64", "u64", "i128" }.Select(static x => $"func take(r: {x}) => ()")) +
            "\nfunc take(r: Range<i32, i32>) => ()\ntake(1..=3)";
        var c = Analyze(source);
        var error = Assert.Single(Errors(c));
        Assert.Contains("Kimi.ClosedRange<i32, i32>", error.Note, StringComparison.Ordinal);
        Assert.Contains("Kimi.Range<i32, i32>", error.Note, StringComparison.Ordinal);
        var record = Assert.Single(c.Diagnostics.Finalize().Diagnostics, static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(8, record.Related!.Length);
        Assert.Contains(record.Omissions!, static x => x.Part == "related locations" && x.Count == 2);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmRangeFailureRecordingAllocatesNothing()
    {
        var c = CompilationTestHelper.ParseSuccess("struct Start\nlet r = ..3\nlet p: Start = r.start\nfor i in 1..^1 => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    private static Compilation Analyze(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        return c;
    }

    private static TestDiagnostic[] Errors(Compilation c)
        => [.. TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error)];

    private sealed class DiagnosticConsole : IConsoleService
    {
        private readonly StringBuilder text = new();

        public string Text => this.text.ToString();

        public bool KeyAvailable => false;

        public bool EnableColor { get; set; }

        public void Write(string? message, ConsoleColor color = ConsoleColor.Gray) => this.text.Append(message);

        public void Write(ReadOnlySpan<char> message, ConsoleColor color = ConsoleColor.Gray) => this.text.Append(message);

        public void WriteLine(string? message, ConsoleColor color = ConsoleColor.Gray) => this.text.Append(message).Append('\n');

        public void WriteLine(ReadOnlySpan<char> message, ConsoleColor color = ConsoleColor.Gray) => this.text.Append(message).Append('\n');

        public Task<InputResult> ReadLineAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public ConsoleKeyInfo ReadKey(bool intercept) => throw new NotSupportedException();
    }
}
