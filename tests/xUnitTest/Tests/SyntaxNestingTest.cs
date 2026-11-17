// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// DECISIONS BU-112 (PLAN G444 U2): Parsing bounds every published tree at the syntax nesting limit L = 1,024 (IMPL A.4): a node at
// depth L that has a child is cut, one Resource record NestingLimit_Kd per cut, and nothing under a cut is walked, so a program at
// depth L is accepted and one level more is rejected with that record alone. Fail-before: dev accepted each L + 1 program.
public class SyntaxNestingTest(ITestOutputHelper output)
{
    private const int L = 1024;

    public static TheoryData<string> Shapes => ["parentheses", "plus", "labeled do"];

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ATreeAtTheLimitIsAcceptedAndOneLevelMoreIsCutOnce(string shape)
    {
        var levels = LevelsWithin(shape);
        var kept = this.Compile(Source(shape, levels));
        Assert.Equal(Offset(shape) + (PerLevel(shape) * levels), kept.Depth);
        Assert.InRange(kept.Depth, L - PerLevel(shape) + 1, L);
        Assert.Empty(kept.Records);
        Assert.True(kept.Accepted);

        var cut = this.Compile(Source(shape, levels + 1));
        var record = Assert.Single(cut.Records);
        Assert.Equal((nameof(DiagnosticCode.NestingLimit_Kd), DiagnosticCategory.Resource), (record.Code, record.Category));
        Assert.Contains("1024", record.Message, StringComparison.Ordinal);
        Assert.True(cut.Depth <= L, $"depth {cut.Depth}");
        Assert.False(cut.Accepted);
    }

    [Fact]
    public void EachCutOfOneBodyIsItsOwnRecord()
    {
        var levels = LevelsWithin("parentheses") + 1;
        var source = $"func f(c: bool) -> i32\n    let a = {new string('(', levels)}1{new string(')', levels)}\n    return {Plus(LevelsWithin("plus") + 1)}\n";
        var records = this.Compile(source).Records;
        Assert.Equal([nameof(DiagnosticCode.NestingLimit_Kd), nameof(DiagnosticCode.NestingLimit_Kd)], records.Select(static x => x.Code));
        Assert.True(records[0].Span.Start < source.IndexOf("return", StringComparison.Ordinal) && records[1].Span.Start > source.IndexOf("return", StringComparison.Ordinal));
    }

    // A later document's parse walks only its own nodes and the module's merge points, so an earlier cut is recorded once.
    [Fact]
    public void EachDocumentIsBoundedByItsOwnParse()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("a.kimi", Source("parentheses", LevelsWithin("parentheses") + 1)));
        c.Kotonoha.AddSource(new SourceDocument("b.kimi", Source("plus", LevelsWithin("plus") + 1).Replace("func f", "func g", StringComparison.Ordinal)));
        var records = c.Diagnostics.Finalize(rejected: true).Diagnostics;
        Assert.Equal([(nameof(DiagnosticCode.NestingLimit_Kd), 0), (nameof(DiagnosticCode.NestingLimit_Kd), 1)], records.Select(static x => (x.Code, x.Source)));
    }

    [Fact]
    public void AGeneratedSourceIsBoundedByItsParse()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, Source("parentheses", LevelsWithin("parentheses") + 1));
        Assert.Equal(nameof(DiagnosticCode.NestingLimit_Kd), Assert.Single(c.Diagnostics.Finalize(rejected: true).Diagnostics).Code);
        Assert.True(Depth(c.Kotonoha.RootKoto) <= L);
    }

    // A written Type adds two nodes per Type argument level: at L nothing is cut, one level more is one cut (PLAN G348's written-Type
    // half). The Type's own Generic generation limit may record beside it; only the nesting records are asserted.
    [Fact]
    public void AWrittenTypeAtTheLimitIsKeptAndOneLevelMoreIsCutOnce()
    {
        var levels = LevelsWithin("type");
        var kept = this.Compile(Source("type", levels));
        Assert.InRange(kept.Depth, L - PerLevel("type") + 1, L);
        Assert.DoesNotContain(kept.Records, static x => x.Code == nameof(DiagnosticCode.NestingLimit_Kd));

        var cut = this.Compile(Source("type", levels + 1));
        Assert.Single(cut.Records, static x => x.Code == nameof(DiagnosticCode.NestingLimit_Kd));
        Assert.True(cut.Depth <= L, $"depth {cut.Depth}");
        Assert.False(cut.Accepted);
    }

    // Declaration containers nested past L: no slot above a nested container takes an ErrorKoto, so the chain keeps its depth and is
    // recorded once, at the outermost container, also when a later document's parse walks the chain again (PLAN G444 U2b bounds it).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContainersNestedPastTheLimitAreRecordedOnceAtTheOutermost(bool secondDocument)
    {
        var levels = LevelsWithin("groups");
        Assert.DoesNotContain(this.Compile(Source("groups", levels)).Records, static x => x.Code == nameof(DiagnosticCode.NestingLimit_Kd));

        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("a.kimi", Groups(L))); // Group G1023 lies at depth L and has a child.
        if (secondDocument)
        {
            c.Kotonoha.AddSource(new SourceDocument("b.kimi", "func g() -> i32 => 1\n"));
        }

        var record = Assert.Single(c.Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.NestingLimit_Kd), 0, 0), (record.Code, record.Source, record.Span?.Start));
        Assert.True(Depth(c.Kotonoha.RootKoto) > L);
    }

    // Ten times L: one cut, linear (the parser loops on a chain and the walk stops at L), and every phase after parsing walks at most
    // L levels. Parentheses join when the parser's own recursion is bounded (PLAN G444 U2b).
    [Theory]
    [InlineData("plus")]
    [InlineData("labeled do")]
    public void TenTimesTheLimitIsOneCut(string shape)
    {
        var deep = this.Compile(Source(shape, 10 * L / PerLevel(shape)));
        Assert.Equal(nameof(DiagnosticCode.NestingLimit_Kd), Assert.Single(deep.Records).Code);
        Assert.True(deep.Depth <= L, $"depth {deep.Depth}");
    }

    // The depth of a tree, its root at 0 (SyntaxNesting's measure, also KimiLibraryShapeTest's), by an explicit stack: the test thread has the default stack.
    internal static int Depth(Koto root)
    {
        var deepest = 0;
        var stack = new Stack<(Koto Node, int Depth)>([(root, 0)]);
        while (stack.TryPop(out var top))
        {
            deepest = Math.Max(deepest, top.Depth);
            foreach (var child in top.Node.Children())
            {
                stack.Push((child, top.Depth + 1));
            }
        }

        return deepest;
    }

    private static string Source(string shape, int levels)
        => shape switch
        {
            "parentheses" => CompilationEntryTest.Parentheses(levels),
            "plus" => $"func f(c: bool) -> i32\n    return {Plus(levels)}\n",
            "type" => $"func f(x: {string.Concat(Enumerable.Repeat("Option<", levels))}i32{new string('>', levels)}) -> i32 => 0\n",
            "groups" => Groups(levels),
            _ => CompilationEntryTest.LabeledDos(levels),
        };

    // `levels` nested groups around one static.
    private static string Groups(int levels)
    {
        var source = new System.Text.StringBuilder();
        for (var i = 0; i < levels; i++)
        {
            source.Append(' ', 4 * i).Append($"group G{i}\n");
        }

        return source.Append(' ', 4 * levels).Append("let v: i32 = 1\n").ToString();
    }

    // A left-deep chain of `levels` additions, which the parser builds in a loop.
    private static string Plus(int levels)
        => "1" + string.Concat(Enumerable.Repeat(" + 1", levels));

    // The nodes one level of the shape adds, and the depth of its tree without levels, from its trees at one and two levels.
    private static int PerLevel(string shape)
        => Depth(Parsed(Source(shape, 2))) - Depth(Parsed(Source(shape, 1)));

    private static int Offset(string shape)
        => Depth(Parsed(Source(shape, 1))) - PerLevel(shape);

    // The most levels whose tree lies within L.
    private static int LevelsWithin(string shape)
        => (L - Offset(shape)) / PerLevel(shape);

    private static Koto Parsed(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Deep.kimi", source));
        return c.Kotonoha.RootKoto;
    }

    // Parsed, bound, checked and reported as a Library build runs them.
    private (int Depth, DiagnosticEntryView[] Records, bool Accepted) Compile(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Deep.kimi", source));
        var depth = Depth(c.Kotonoha.RootKoto);
        var checkedAll = c.Bind().IsComplete && c.Binding.CheckStartup(OutputKind.Library).IsComplete && c.Analyze().IsVerified;
        c.Binding.ReportDiagnostics();
        c.ReportAnalysis();
        var accepted = checkedAll && c.Accepts;
        var records = c.Diagnostics.Finalize(rejected: !accepted).Diagnostics.Select(static x => new DiagnosticEntryView(x.Code, x.Category, x.Message, x.Span ?? default)).ToArray();
        output.WriteLine($"depth {depth}: {string.Join(", ", records.Select(static x => $"{x.Code} {x.Span}"))}");
        return (depth, records, accepted);
    }

    private readonly record struct DiagnosticEntryView(string Code, DiagnosticCategory Category, string Message, SourceSpan Span);
}
