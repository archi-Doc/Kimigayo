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

    // Declaration containers nested past L: no slot above a nested container takes an ErrorKoto, so the container at depth L is emptied
    // where it stands and recorded once, also when a later document's parse walks the chain again. Fail-before: dev kept the chain's
    // depth and recorded it at the outermost container.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContainersNestedPastTheLimitAreEmptiedOnceAtTheLimit(bool secondDocument)
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
        var limit = Groups(L).IndexOf($"group G{L - 1}\n", StringComparison.Ordinal);
        Assert.Equal((nameof(DiagnosticCode.NestingLimit_Kd), 0, limit), (record.Code, record.Source, record.Span?.Start));
        Assert.Equal(L, Depth(c.Kotonoha.RootKoto));
    }

    // Ten times L: one cut, linear (the parser loops on a chain, reads past L without nodes, and the walk stops at L), and every phase
    // after parsing walks at most L levels.
    [Theory]
    [InlineData("parentheses")]
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

    // PLAN G444 U2b's twins (design artifacts/units/g144 R1.5; runner D's dev rows, F's G144-3): each program's records by code. The
    // parser reads past L without nodes (TokenReader.Depth), so a record past the cut is never made (deepsyn) and a record above it
    // stays (ch-pre-y's y, deeptype's TypeMismatch_Kd). Fail-before: dev overflowed on parentheses, calls, ch-pre-y at 100,000 in a
    // Debug host, or and interp; deepsyn kept its ExpectedSyntax_Kd below the cut.
    [Theory]
    [InlineData("parentheses-100k", "NestingLimit_Kd")]
    [InlineData("calls-100k", "NestingLimit_Kd")]
    [InlineData("ch-pre-y-100k", "NestingLimit_Kd UnresolvedBinding_Kd")]
    [InlineData("deepsyn-1500-I", "NestingLimit_Kd")]
    [InlineData("deeptype-1500-I", "NestingLimit_Kd TypeMismatch_Kd")]
    [InlineData("or-100k-V", "NestingLimit_Kd")]
    [InlineData("interp-120x1000-V", "NestingLimit_Kd")]
    [InlineData("arrow-100k-V", "NestingLimit_Kd")]
    [InlineData("dc-1100", "NestingLimit_Kd")]
    [InlineData("oR2-missing-I", "UnresolvedBinding_Kd")]
    [InlineData("oR2-syntax-I", "MissingSyntax_Kd UnresolvedBinding_Kd")]
    [InlineData("missing-qualifier-I", "UnresolvedBinding_Kd")]
    public void EachTwinKeepsItsRecords(string twin, string codes)
        => Assert.Equal(codes.Split(' ').Order(), this.Compile(Twin(twin)).Records.Select(static x => x.Code).Order());

    // G144-5 (O-R2, SPEC 18.2.1, 9.6, 23.1.2): a qualified use reached through an opening alias that names a member of the container
    // emptied at L reads that container, a recovery, so its failure is derived (rule W): one record, at G1023. Its five-level twin is
    // accepted; a missing member of an ordinary container (oR2-missing-I), with a syntax error in that container's body too
    // (oR2-syntax-I), and a missing qualifier outside any recovery (missing-qualifier-I, G144-8) keep their records. Fail-before:
    // commit 2's head added UnresolvedBinding_Kd at the callee `G600...G1099.m`.
    [Fact]
    public void AUseBelowTheEmptiedContainerRestsOnItsRecord()
    {
        Assert.True(this.Compile(Aliased(5, 2)).Accepted);
        var source = Aliased(1100, 600);
        var record = Assert.Single(this.Compile(source).Records);
        Assert.Equal((nameof(DiagnosticCode.NestingLimit_Kd), source.IndexOf("group G1023\n", StringComparison.Ordinal)), (record.Code, record.Span.Start));

        var value = Aliased(L, 600); // oR2-value-1100-V: `m` declared in G1023 itself, a value member the emptied container lost.
        var only = Assert.Single(this.Compile(value).Records);
        Assert.Equal((nameof(DiagnosticCode.NestingLimit_Kd), value.IndexOf("group G1023\n", StringComparison.Ordinal)), (only.Code, only.Span.Start));
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
            "calls" => CompilationEntryTest.Calls(levels),
            "or" => $"#if {string.Join(" or ", Enumerable.Repeat("true", levels))}\nfunc f() => ()\n",
            "arrow" => $"func f()\n    let g = {string.Concat(Enumerable.Repeat("func [] () => ", levels))}1\n",
            "interpolations" => $"func f() -> string\n    return {Interpolations(levels / L, L)}\n", // levels / L interpolations of L parentheses.
            _ => CompilationEntryTest.LabeledDos(levels),
        };

    private static string Twin(string name)
        => name switch
        {
            "parentheses-100k" => Source("parentheses", 100_000),
            "calls-100k" => Source("calls", 100_000),
            "ch-pre-y-100k" => $"func f(x: i32) -> i32\n    return {string.Concat(Enumerable.Repeat("- ", 100_000))}x + y\n",
            "deepsyn-1500-I" => $"func f(c: bool) -> i32\n    return {new string('(', 1499)}(1 +){new string(')', 1499)}\n",
            "deeptype-1500-I" => $"func f(c: bool) -> i32\n    return {new string('(', 1500)}g(){new string(')', 1500)}\nfunc g() -> i32\n    let s: string = 1\n    return 1\n",
            "or-100k-V" => Source("or", 100_000),
            "interp-120x1000-V" => $"func f() -> string\n    return {Interpolations(120, 1000)}\n",
            "oR2-missing-I" => "alias G0\ngroup G0\n    internal group G1\n        internal func m() -> i32 => 1\n\nfunc f() -> i32 => G1.n()\n",
            "oR2-syntax-I" => "alias G0\ngroup G0\n    internal group G1\n        internal func k() -> i32 => 1 +\n        internal func m() -> i32 => 1\n\nfunc f() -> i32 => G1.n()\n",
            "missing-qualifier-I" => "func f() -> i32 => Missing.m()\n",
            "arrow-100k-V" => Source("arrow", 100_000),
            _ => string.Concat(Enumerable.Range(0, 1100).Select(static i => $"{new string(' ', 4 * i)}struct C{i}\n")) + $"{new string(' ', 4 * 1100)}func m() -> i32 => 1\n",
        };

    // `levels` interpolations, each around `parentheses` parentheses: design R1.4's interp twin.
    private static string Interpolations(int levels, int parentheses)
    {
        var expression = "1";
        for (var i = 0; i < levels; i++)
        {
            expression = $"\"\\({new string('(', parentheses)}{expression}{new string(')', parentheses)})\"";
        }

        return expression;
    }

    // `alias G0...G{opened - 1}`, `levels` nested groups around `m`, and a use of `m` qualified from G{opened} (gen3-oR2.py's twin).
    private static string Aliased(int levels, int opened)
    {
        var source = new System.Text.StringBuilder("alias ").AppendJoin('.', Enumerable.Range(0, opened).Select(static i => $"G{i}")).Append('\n');
        for (var i = 0; i < levels; i++)
        {
            source.Append(' ', 4 * i).Append(i == 0 ? "group" : "internal group").Append($" G{i}\n");
        }

        source.Append(' ', 4 * levels).Append("internal func m() -> i32 => 1\n\nfunc f() -> i32 => ");
        return source.AppendJoin('.', Enumerable.Range(opened, levels - opened).Select(static i => $"G{i}")).Append(".m()\n").ToString();
    }

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

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests(ITestOutputHelper output)
    {
        // The parse-work row (design artifacts/units/g144 R1.5 C6, F's G144-4): past L the parser reads a construct without nodes
        // (TokenReader.Depth) and a chain in a loop, so a document's parse allocates linearly in its levels at 4, 8 and 16 x L on each
        // deep row that opens no generics (a Type argument's `<` costs the tokenizer's forward scan, quadratic on dev too: PLAN's row).
        [Theory]
        [InlineData("parentheses")]
        [InlineData("calls")]
        [InlineData("prefix")]
        [InlineData("function type")]
        [InlineData("or")]
        [InlineData("arrow")]
        [InlineData("interpolations")]
        public void AParsePastTheLimitAllocatesLinearly(string shape)
        {
            var bytes = AllocationMeasurement.MeasureSeries(3, i =>
            {
                var c = Compilation.CreateForTest();
                Assert.True(c.Prepare(TargetProfile.Windows.Target));
                var document = new SourceDocument("Deep.kimi", Source(shape, (4 << i) * L));
                return () => c.Kotonoha.AddSource(document);
            });
            output.WriteLine($"{shape} at 4, 8 and 16 x L: bytes {string.Join(", ", bytes)}");
            Assert.True(bytes[0] < bytes[1] && MirBorrowsTest.Grows(bytes, 3), $"bytes {string.Join(", ", bytes)}");
        }
    }
}
