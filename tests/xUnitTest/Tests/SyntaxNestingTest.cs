// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// DECISIONS BU-112 (PLAN G444 U2): Parsing bounds every published tree at the syntax nesting limit L = 1,024 (IMPL A.4): a node at
// depth L that has a child is cut, one Resource record NestingLimit_Kd per cut, and nothing under a cut is walked, so a program at
// depth L is accepted and one level more is rejected with that record alone. Fail-before: dev accepted each L + 1 program. The depth
// is IMPL A.4's: a directive's marker and the generated function with its Body count no level (G144-25), and a Condition or an
// excluded part stands at its marker's place; restated so, the root shapes keep two levels more than dev did (G144-30).
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

    // G144-32, IMPL A.4 by hand: a root `f` stands at depth 1 (the generated pair counts no level), its Body at 2, `return` at 3 and
    // the `1` inside n parentheses at n + 4 <= L, so n = 1,020 is kept; `f` in `group G` (depth 1) stands at 2, so there n = 1,019.
    [Fact]
    public void ARootFunctionIsKeptOneLevelDeeperThanTheSameFunctionInAGroup()
    {
        (string Kept, string Cut)[] rows =
        [
            (Source("parentheses", 1020), Source("parentheses", 1021)),
            (Under("group G", Source("parentheses", 1019)), Under("group G", Source("parentheses", 1020))),
        ];
        foreach (var (kept, cut) in rows)
        {
            var accepted = this.Compile(kept);
            Assert.Empty(accepted.Records);
            Assert.True(accepted.Accepted);

            var record = Assert.Single(this.Compile(cut).Records);
            Assert.Equal((nameof(DiagnosticCode.NestingLimit_Kd), DiagnosticCategory.Resource), (record.Code, record.Category));
        }
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
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("a.kimi", Source("parentheses", LevelsWithin("parentheses") + 1)));
        c.Kotonoha.AddSource(new SourceDocument("b.kimi", Source("plus", LevelsWithin("plus") + 1).Replace("func f", "func g", StringComparison.Ordinal)));
        var records = c.Diagnostics.Finalize(rejected: true).Diagnostics;
        Assert.Equal([(nameof(DiagnosticCode.NestingLimit_Kd), 0), (nameof(DiagnosticCode.NestingLimit_Kd), 1)], records.Select(static x => (x.Code, x.Source)));
    }

    [Fact]
    public void AGeneratedSourceIsBoundedByItsParse()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
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
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
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

    // A chain the parser reads in a loop, 100,000 prefix operators or Function Type links, is one cut. Fail-before: dev's parser
    // recursed once per link and overflowed the compile stack.
    [Theory]
    [InlineData("prefix")]
    [InlineData("function type")]
    public void AHundredThousandLinkChainIsOneCut(string shape)
    {
        var deep = this.Compile(Source(shape, 100_000));
        Assert.Equal(nameof(DiagnosticCode.NestingLimit_Kd), Assert.Single(deep.Records).Code);
        Assert.True(deep.Depth <= L, $"depth {deep.Depth}");
    }

    // G144-27 N8: a directive inside the excluded syntax a marker owns joins that marker (a stacked prefix, a nested directive Block,
    // a #switch, in a body too), so no marker's Source holds a marker; a list that begins inside excluded syntax keeps its directives'
    // own markers (the last row). Fail-before: each inner directive had its own marker inside the outer one's Source.
    [Theory]
    [InlineData("#if false\n#if true\nfunc g() => ()\n", 1)]
    [InlineData("#if false\n    #if true\n        func g() => ()\n", 1)]
    [InlineData("#if false\n    #switch\n        #case true\n            func h() => ()\n        #case _\n            func h() => ()\n", 1)]
    [InlineData("func f() -> ()\n    #if false\n        #if true\n            let a = 1\n", 1)]
    [InlineData("#if false\nfunc g() -> ()\n    #if true\n        let a = 1\n", 2)]
    public void AMarkersSourceHoldsNoMarker(string source, int markers)
    {
        var all = KotoTree.WalkWithSource(Parsed(source)).OfType<CompileTimeSwitchKoto>().ToArray();
        Assert.Equal(markers, all.Length);
        Assert.DoesNotContain(all, static x => x.ChildrenWithSource().Any(static y => y is CompileTimeSwitchKoto));
    }

    // G144-30 (SPEC 19.4 under IMPL A.4): a part under `#if true` or `#if false` stands where the same syntax without the directive
    // stands, so it is kept at L and cut one level more, at the same syntax. Fail-before: dev cut the kept rows (the generated pair
    // counted two levels) and walked no excluded part.
    [Fact]
    public void AnExcludedPartIsBoundedWhereSelectedSyntaxIs()
    {
        var levels = LevelsWithin("parentheses");
        var plain = Source("parentheses", levels + 1);
        var cut = Assert.Single(this.Compile(plain).Records);
        foreach (var directive in (string[])["#if true", "#if false"])
        {
            var kept = this.Compile(Under(directive, Source("parentheses", levels)));
            Assert.Empty(kept.Records);
            Assert.True(kept.Accepted);

            var source = Under(directive, plain);
            var record = Assert.Single(this.Compile(source).Records);
            Assert.Equal((cut.Code, Text(plain, cut.Span)), (record.Code, Text(source, record.Span)));
        }
    }

    // G144-31 (r1 §5's 2a column): a Condition stands at its marker's place, so one past L is cut at the Condition, the selection
    // unchanged; where the directive's syntax is invalid, the Resource record joins the Language one. Fail-before: dev walked no
    // Condition.
    [Fact]
    public void AConditionPastTheLimitIsCutAtTheCondition()
    {
        var condition = $"#if {string.Join(" or ", Enumerable.Repeat("true", 3000))}\n";
        var valid = this.Compile(condition + "func f() => ()\n");
        var record = Assert.Single(valid.Records);
        Assert.Equal((nameof(DiagnosticCode.NestingLimit_Kd), 4), (record.Code, record.Span.Start));
        Assert.False(valid.Accepted);

        var invalid = this.Compile(condition + "func f() -> i32 => (1 +)\n").Records;
        Assert.Equal([nameof(DiagnosticCode.NestingLimit_Kd), nameof(DiagnosticCode.ExpectedSyntax_Kd)], invalid.Select(static x => x.Code));
        Assert.Equal(4, invalid[0].Span.Start);
    }

    // The depth of a tree as SyntaxNesting counts it (IMPL A.4; G144-25), its root at 0, also KimiLibraryShapeTest's: a directive's
    // marker and the generated function with its Body count no level, and a marker's Source is walked. By an explicit stack: the test
    // thread has the default stack.
    internal static int Depth(Koto root)
    {
        var generated = root.Kotonoha.GeneratedFunction;
        var deepest = 0;
        var stack = new Stack<(Koto Node, int Depth)>([(root, 0)]);
        while (stack.TryPop(out var top))
        {
            deepest = Math.Max(deepest, top.Depth);
            var node = top.Node;
            var level = node is CompileTimeSwitchKoto || (generated is not null && (ReferenceEquals(node, generated) || ReferenceEquals(node, generated.Body))) ? 0 : 1;
            foreach (var child in node.ChildrenWithSource())
            {
                stack.Push((child, top.Depth + level));
            }
        }

        return deepest;
    }

    // The most levels whose tree lies within L.
    internal static int LevelsWithin(string shape)
        => (L - Offset(shape)) / PerLevel(shape);

    private static string Source(string shape, int levels)
        => shape switch
        {
            "parentheses" => CompilationEntryTest.Parentheses(levels),
            "plus" => $"func f(c: bool) -> i32\n    return {Plus(levels)}\n",
            "type" => $"func f(x: {string.Concat(Enumerable.Repeat("Option<", levels))}i32{new string('>', levels)}) -> i32 => 0\n",
            "groups" => Groups(levels),
            "prefix" => $"func f(c: bool) -> i32\n    return {string.Concat(Enumerable.Repeat("- ", levels))}1\n",
            "function type" => $"func f(g: {string.Concat(Enumerable.Repeat("() -> ", levels))}i32) => ()\n",
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

    // source as the block of a header line (a directive or `group G`): each line indented under it.
    private static string Under(string header, string source)
        => $"{header}\n{string.Concat(source.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(static x => $"    {x}\n"))}";

    // The text a span covers.
    private static string Text(string source, SourceSpan span)
        => source.Substring(span.Start, span.Length);

    // A left-deep chain of `levels` additions, which the parser builds in a loop.
    private static string Plus(int levels)
        => "1" + string.Concat(Enumerable.Repeat(" + 1", levels));

    // The nodes one level of the shape adds, and the depth of its tree without levels, from its trees at one and two levels.
    private static int PerLevel(string shape)
        => Depth(Parsed(Source(shape, 2))) - Depth(Parsed(Source(shape, 1)));

    private static int Offset(string shape)
        => Depth(Parsed(Source(shape, 1))) - PerLevel(shape);

    private static Koto Parsed(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("Deep.kimi", source));
        return c.Kotonoha.RootKoto;
    }

    // Parsed, bound, checked and reported as a Library build runs them.
    private (int Depth, DiagnosticEntryView[] Records, bool Accepted) Compile(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
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
