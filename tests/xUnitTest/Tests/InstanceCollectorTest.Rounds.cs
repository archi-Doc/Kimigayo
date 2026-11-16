// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// PLAN G951 U1b (the Back's C-378 §3): the collector's round rule. In a build whose root range is the program's, a run that replays a body
// the analyses have not stepped (one a lookup built during the run) misses and commits nothing; Compilation.Collect steps the bodies built
// since and runs again until a run is clean, and the clean run equals the one run over the eager build.
public sealed partial class InstanceCollectorTest
{
    // C-col and X3. Labels (SPEC): X3 valid (10.5.3, an erasure at a declared Function Type; 7.7.4); Hello valid (22.7); Utf8Formatting and
    // Milestone31 valid. The lazy build's checked set (each item in order with its key, view, site and parent, so its generated names, which
    // are item indices; its targets; its body's A2 table), its collection records in order and its generated module equal the eager
    // build's; the lazy collection takes `runs` runs, the eager one one: X3's Stopwatch.start is first demanded by its erasure's adapter,
    // Utf8Formatting's and Milestone31's storage bodies by their items (design §R2.2).
    [Theory]
    [InlineData("X3", 2)]
    [InlineData("docs/examples/Hello/Hello.kimi", 1)]
    [InlineData("docs/examples/Utf8Formatting/Utf8Formatting.kimi", 2)]
    [InlineData("tests/milestones/Milestone31.kimi", 2)]
    public void ALazyCollectionEqualsTheEagerOne(string name, int runs)
    {
        var source = name == "X3" ? MirBuildTest.StopwatchAction : Repository.ReadText(name.Split('/'));
        var eager = Collected(source, static _ => SyntaxTable.LibraryModule + 1);
        var lazy = Collected(source, static c => c.SourceModules.Length);
        Assert.Equal(eager.Items, lazy.Items);
        Assert.Equal(eager.Records, lazy.Records);
        Assert.Equal(eager.Module, lazy.Module);
        Assert.Equal((1, runs), (eager.Runs, lazy.Runs));
    }

    // C-378 §3's limit row through a missed run (DECISIONS R5-M171). Labels: valid (SPEC 22.4.1, 7.7.4); IMPL 21.3.2: the 2 GiB parameter
    // Type is over the layout limit, one GenerationResourceLimit_Kd per declaration at its least use; DECISIONS R5-M82: the settable static
    // is A3b's static-storage gap at its declaration. X3 with both, built lazily: the first run reaches both uses and misses at
    // Stopwatch.start; it commits nothing (no record, no end-of-run record, a set of no version) and its work counts; after the step the
    // clean run's records are the eager compilation's, once each, and Work sums both runs.
    [Fact]
    public void AMissedRunCommitsNothing()
    {
        const string source = "func a(x: [2147483648 of u8]) -> i32 => 1\ngroup G\n    public var count: i32 = 0\n" + MirBuildTest.StopwatchAction;
        var c = MirBuildTest.Compile(source);
        var analysis = new MirAnalysis(c.Types);
        var program = analysis.Run(new MirBuilder(c).Build(c.SourceModules.Length), c.SourceModules.Length);
        var collector = new InstanceCollector(c.Types, c.Layout);
        var missed = collector.Run(program, c.SourceModules.Length);
        Assert.Equal((0UL, 0, 1), (missed.Version, missed.Items.Length, collector.RunCount));
        Assert.Empty(program.Records(MirRecordClass.Collection));
        var work = collector.Work;
        analysis.Step(program, c.SourceModules.Length);
        Assert.Equal(program.Version, collector.Run(program, c.SourceModules.Length).Version);
        Assert.True(collector.Work > work, $"{collector.Work} after {work}");
        Assert.Equal(2, collector.RunCount);
        string[] records = ["GenerationResourceLimit_Kd [2147483648 of u8]", "Unsupported_Kd public var count: i32 = 0"];
        Assert.Equal(records, Records(c, program).Order(StringComparer.Ordinal));
        var eager = MirBuildTest.Compile(source);
        Assert.Equal(Records(eager, eager.BuildMir()), Records(c, program));
    }

    // A compilation of a source built with a root range, analyzed and collected with its source modules as roots by Compilation's driver
    // (Collect), then sealed as BuildMir seals it: its checked set, its collection records and its module generated with every root an
    // output root, and the collector's runs.
    private static (string[] Items, string[] Records, string Module, int Runs) Collected(string source, Func<Compilation, int> range)
    {
        var c = MirBuildTest.Compile(source);
        var analysis = new MirAnalysis(c.Types);
        var program = analysis.Run(new MirBuilder(c).Build(range(c)), c.SourceModules.Length);
        var collector = new InstanceCollector(c.Types, c.Layout);
        var set = Compilation.Collect(analysis, collector, program, c.SourceModules.Length);
        program.Enter(MirProgram.Phase.Sealed);
        var items = Enumerable.Range(0, set.Items.Length).Select(x => $"{x} {Label(program, set.Items[x])} view {set.Items[x].View} at {set.Items[x].Site} from " +
            $"{set.Items[x].Parent}: {string.Join(", ", set.Targets(x).ToArray().Select(static t => $"{t.Item} {t.At} {t.Runtime}"))}" +
            (set.Items[x].BodyOrdinal >= 0 ? " | " + MirAnalysisTest.Table(program, program.Body(set.Items[x].BodyOrdinal)) : string.Empty)).ToArray();
        int[] roots = [.. Enumerable.Range(0, set.RootCount)];
        set.Mark(roots);
        using var writer = new StringWriter();
        Assert.True(new ModuleCodegen(c.Layout, c.Locations, c.Types, c.Binding.LibraryImports, c.Tests).Write(program, collector, c.Binding.Startup, roots, writer));
        return (items, Records(c, program), writer.ToString(), collector.RunCount);
    }

    // A program's collection records in order, each its code and its subject's first line.
    private static string[] Records(Compilation c, MirProgram program)
        => [.. program.Records(MirRecordClass.Collection).Select(x => $"{x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0]}")];
}
