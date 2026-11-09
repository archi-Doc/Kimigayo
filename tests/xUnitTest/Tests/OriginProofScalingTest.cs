// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Verification;
using Xunit;

namespace XunitTest;

public class OriginProofScalingTest
{
    public static TheoryData<string, string?> Families
    {
        get
        {
            var data = new TheoryData<string, string?>();
            foreach (var (family, code) in OriginProofWorkloads.Families)
            {
                data.Add(family, code);
            }

            return data;
        }
    }

    // SPEC 15.3.4-15.3.7, 15.6.1: a relation the visible premises prove is accepted and one they do not prove is one
    // UnprovenOriginRelation_Kd, at every size of the family.
    [Theory]
    [MemberData(nameof(Families))]
    public void FamiliesHaveTheirSpecifiedOutcome(string family, string? code)
    {
        foreach (var size in new[] { 1, 2, 8, 16, 32 })
        {
            var output = DiagnosticCorpus.Check(OriginProofWorkloads.Create(family, size));
            Assert.Equal(code is null ? [] : [code], output.Diagnostics.Select(static x => x.Code));
        }
    }

    // PLAN G74 (proposal §3.7): each closure's work stays within its premise graph, with worklist insertions within its nodes,
    // edge activations within its edges and operand decrements within its meet incidences, and one environment's graph grows
    // with the contracts visible at the use: linearly in a family's Holder inputs or clause links.
    [Theory]
    [MemberData(nameof(Families))]
    public void ClosureWorkStaysWithinThePremiseGraph(string family, string? code)
    {
        var small = Measure(family, 8, code);
        var large = Measure(family, 32, code);
        Assert.True(large.MaxNodes <= (4 * small.MaxNodes) + 8, $"{family}: nodes {small.MaxNodes} at 8, {large.MaxNodes} at 32");
        Assert.True(large.MaxEdges <= (4 * small.MaxEdges) + 8, $"{family}: edges {small.MaxEdges} at 8, {large.MaxEdges} at 32");
    }

    // SPEC 15.3.6 (DECISIONS: G572 U1): nested Closures with two regions per level, each bound by both of the level above, read at a fixed
    // Origin at the bottom. The oracle walks each region once per target (UniversalRelations' memo): without it the reads double per level.
    [Fact]
    public void AFixedReadWalksEachRegionOnce()
    {
        var small = RegionReads(8);
        var large = RegionReads(24);
        Assert.True(large <= (4 * small) + 8, $"region reads {small} at depth 8, {large} at depth 24");
    }

    // SPEC 15.6.5, 23.1.2 (DECISIONS BU-62; PLAN G318): k Semantics binders and k declaration rows b >= x.s, each in one binder's borrow
    // cases, reach the last edge x.s >= a through x.s, which a failed clause leaves Unbound: the declaration judge's chain work grows with
    // the rows, never with the combinations of their conditions. Every size is invalid by the clause (15.3.3) and the chain (15.6.5).
    [Fact]
    public void DeclarationChainWorkGrowsWithTheRows()
    {
        var small = DeclarationChainWork(3);
        var large = DeclarationChainWork(12);
        Assert.True(large <= (4 * small) + 8, $"declaration chain work {small} at 3 binders, {large} at 12");
    }

    private static long DeclarationChainWork(int binders)
    {
        var source = new StringBuilder("struct C {s}\n    public let r: ref/i32 during s\n    public init(r: ref/i32 during s) => self.r = r\n");
        source.Append("struct D<").AppendJoin(", ", Enumerable.Range(1, binders).Select(static i => $"s{i}/T{i}")).Append("> {a, b}\n");
        source.AppendJoin(string.Empty, Enumerable.Range(1, binders).Select(static i => $"    s{i} is owner or ref\n    T{i} is Copy\n"));
        source.Append("    public let f: (C{x}, ").AppendJoin(", ", Enumerable.Range(1, binders).Select(static i => $"ref/(s{i}/T{i} during b) during x.s"));
        source.Append(", ref/(ref/i32 during x.s) during a)\n        origin x.s == zz\npublic func main() => ()\n");
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("chains.kimi", source.ToString()));
        var metrics = new OriginProofMetrics();
        c.Binding.OriginProofMetrics = metrics;
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        var codes = c.Binding.Issues.Select(static x => x.Code.ToString()).Concat(c.Problems.Select(static x => x.Code.ToString()));
        Assert.Equal(["MissingOriginBinding_Kd", "UnprovenOriginRelation_Kd"], codes.Order());
        Assert.NotEqual(0, metrics.ChainWork);
        return metrics.ChainWork;
    }

    private static long RegionReads(int depth)
    {
        var source = new StringBuilder("func see(p: ref/i32) => ()\nfunc f(p: ref/i32)\n    var a0 = p\n    var b0 = p\n");
        var indent = "    ";
        for (var i = 1; i <= depth; i++)
        {
            source.Append($"{indent}let c{i} = func [a{i - 1}, b{i - 1}] ()\n");
            indent += "    ";
            source.Append($"{indent}var a{i} = a{i - 1}\n{indent}a{i} = b{i - 1}\n{indent}var b{i} = b{i - 1}\n{indent}b{i} = a{i - 1}\n");
        }

        source.Append($"{indent}let w: ref/i32 during p = a{depth}\n{indent}see(w)\n");
        for (var i = depth; i > 0; i--)
        {
            indent = indent[..^4];
            source.Append($"{indent}c{i}()\n");
        }

        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("regions.kimi", source.Append("public func main() => ()\n").ToString()));
        var metrics = new OriginProofMetrics();
        c.Binding.OriginProofMetrics = metrics;
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        Assert.Empty(c.Binding.Issues.Select(static x => x.Code).Concat(c.Problems.Select(static x => x.Code)));
        Assert.NotEqual(0, metrics.RegionReads);
        return metrics.RegionReads;
    }

    private static OriginProofMetrics Measure(string family, int size, string? code)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("origin-proof.kimi", OriginProofWorkloads.Create(family, size)));
        var metrics = new OriginProofMetrics();
        c.Binding.OriginProofMetrics = metrics;
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        var codes = c.Binding.Issues.Select(static x => x.Code.ToString()).Concat(c.Problems.Select(static x => x.Code.ToString()));
        Assert.Equal(code is null ? [] : [code], codes.Distinct());
        Assert.NotEqual(0, metrics.Closures);
        Assert.Equal(0, metrics.BoundViolations);
        return metrics;
    }
}
