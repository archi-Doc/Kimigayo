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
    private const string Hold = "public group Api\n    public func hold<T>(value: T) -> () => ()\n"; // Lib's generic function the rows erase.

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
    // clean run's records are the eager compilation's, once each, and Work sums both runs: the missed run's and one clean run's, which a
    // fresh collector's run over the stepped program measures (the Back's review C2; fail-before: a per-run reset).
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
        Assert.Equal(2, collector.RunCount);
        string[] records = ["GenerationResourceLimit_Kd [2147483648 of u8]", "Unsupported_Kd public var count: i32 = 0"];
        Assert.Equal(records, Records(c, program).Order(StringComparer.Ordinal));
        var eager = MirBuildTest.Compile(source);
        Assert.Equal(Records(eager, eager.BuildMir()), Records(c, program));
        var fresh = new InstanceCollector(c.Types, c.Layout);
        Assert.Equal(program.Version, fresh.Run(program, c.SourceModules.Length).Version);
        Assert.Equal((work + fresh.Work, 1), (collector.Work, fresh.RunCount));
    }

    // The Middle's review N6: Work and RunCount sum one version's runs, so a version whose missed run had no clean one (as a fault in the
    // driver's loop leaves it) carries none of its runs into the next version. X3 (valid, above), built lazily in two Binding passes of
    // one compilation: the new version's first run misses as the first version's did, with the same work, and counts as its first run.
    // Fail-before: RunCount 2 and the work of both versions' runs.
    [Fact]
    public void ANewVersionCountsOnlyItsOwnRuns()
    {
        var c = MirBuildTest.Compile(MirBuildTest.StopwatchAction);
        var (builder, analysis, collector) = (new MirBuilder(c), new MirAnalysis(c.Types), new InstanceCollector(c.Types, c.Layout));
        var program = analysis.Run(builder.Build(c.SourceModules.Length), c.SourceModules.Length);
        Assert.Equal(0UL, collector.Run(program, c.SourceModules.Length).Version);
        var (first, work) = (program.Version, collector.Work);
        c.Binding.Bind(BindingMode.Final);
        program = analysis.Run(builder.Build(c.SourceModules.Length), c.SourceModules.Length);
        Assert.NotEqual(first, program.Version);
        Assert.Equal((0UL, 1, work), (collector.Run(program, c.SourceModules.Length).Version, collector.RunCount, collector.Work));
        analysis.Step(program, c.SourceModules.Length);
        Assert.Equal((program.Version, 2), (collector.Run(program, c.SourceModules.Length).Version, collector.RunCount));
    }

    // C-378 §3's limit row in its least-use form (the Middle's review N1, its RV-T). Labels (SPEC): valid (10.5.3: hold's T is bound from the
    // declared Function Type, then erased, 7.7.4; 7.2.3: the callee destroys its parameter; 16.3: destroying a Holder runs its drop);
    // IMPL 21.3.2 with DECISIONS R5-M171: Gs.g's erased Option<[2147483648 of u8]> is over the layout limit, one GenerationResourceLimit_Kd
    // for the declaration at its least use, a, which only g<[2147483648 of u8]> reaches, through the drop of hold's parameter. Built
    // lazily, the first run misses at hold (built by the erasure's adapter; the targets of its Drop wait for A2) with b as g's least use, and
    // commits nothing; the clean run records a once, as the eager build, and as Compilation's own check, whose root range holds Lib (L1,
    // PLAN G951 U2). Fail-before (a missed run committing): records at b and at a.
    [Fact]
    public void ALeastUseReachedAfterAMissIsTheOneRecord()
    {
        const string root = "struct Holder<T>\n    public var n: i32\n    public init()\n        self.n = 0\n    drop => _ = Gs.g<T>()\n" +
            "group Gs\n    public func g<T>() -> i32\n        let a: Option<T> = .None\n        let b: Option<[2147483648 of u8]> = .None\n        return 1\n" +
            "public func main() -> ()\n    _ = Gs.g<i32>()\n    let action: (Holder<[2147483648 of u8]>) -> () = Lib.Api.hold\n" +
            "    action(Holder<[2147483648 of u8]>.init())\n    return\n";
        var eager = TwoModule(root, Hold, SyntaxTable.LibraryModule + 1);
        var lazy = TwoModule(root, Hold, 1);
        Assert.Equal((0UL, 2), (lazy.First, lazy.Works.Length));
        Assert.Empty(lazy.FirstRows);
        Assert.Equal(["collection GenerationResourceLimit_Kd let a: Option<T> = .None"], eager.Rows);
        Assert.Equal(eager.Rows, lazy.Rows);
        Assert.Equal(eager.Items, lazy.Items);
        var c = ModuleBindingTest.Create(root, Hold);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(eager.Rows, Rows(c, c.BuildMir()));
    }

    // The commit rule's other half (the Middle's review N2, its RV-C and RV-A): a construction record of a body built during a round that
    // misses is added at once and stays. Labels (SPEC): valid (7.2.3: the default fits its parameter Type for every binding; 7.7.4; RV-A
    // 8.11.2, 8.11.4: g follows its input and allocates only, so the confined bound holds); the records are implementation limits, equal in
    // both builds: the construction Unsupported_Kd at Lib's f(b) (a default copy the call does not instantiate past the storage depth,
    // DECISIONS R4-M127), the collection records of the deep erased Type at main's site (K 4.9) and A4's limit records (the copy is an
    // Opaque value). Lib's g is first demanded inside a round that misses: the collector's first run (an erasure's adapter) or A4's
    // first round (an obligation's argument). Fail-before (every problem held while a round is open): the construction record is lost.
    [Theory]
    [InlineData("collector", 2)]
    [InlineData("a4", 1)]
    public void ARecordMadeDuringAMissedRoundStays(string round, int runs)
    {
        var root = round == "collector" ? $"public func main() -> ()\n    let action: (ref/{Deep("Lib.Bx")}) -> () = Lib.Api.g\n    return\n"
            : $"func apply<F>(action: ref/F) -> ()\n    F is Callable<(ref/{Deep("Lib.Bx")}) -> ()>\n        effect confined\n    return\n" +
                "public func main() -> ()\n    apply(Lib.Api.g)\n    return\n";
        var lib = "public struct Bx<T>\n    public var v: T\npublic group Api\n    public func f<T>(x: ref/T, k: Option<T> = .None) => ()\n" +
            $"    public func g(b: ref/{Deep("Bx")}) -> ()\n        f(b)\n";
        var eager = TwoModule(root, lib, SyntaxTable.LibraryModule + 1);
        var lazy = TwoModule(root, lib, 1);
        Assert.Equal(runs, lazy.Works.Length);
        Assert.Contains("construction Unsupported_Kd f(b)", lazy.FirstRows);
        Assert.Equal(eager.Rows, lazy.Rows);
        Assert.Equal(eager.Items, lazy.Items);

        static string Deep(string type) => string.Concat(Enumerable.Repeat(type + "<", 65)) + "i32" + new string('>', 65); // Past the storage depth.
    }

    // A missed run does not make every demand (the Back's review N3): a Drop site of a body A2 does not hold requests nothing, so a chain
    // through it takes a third run. Labels (SPEC): valid (10.5.3 and 7.7.4: hold's T bound from the declared Function Type, then erased;
    // 7.2.3 and 16.3: hold destroys its parameter, running Lib's Holder drop); no record. Built lazily, the first run misses at hold (built
    // by the erasure's adapter), the second at Holder's drop (first demanded by hold's Drop), and the third is clean and equals the eager
    // build's one run; Work sums the three, the missed runs' and one clean run's.
    [Fact]
    public void AChainThroughAMissedRunsDropTakesAThirdRun()
    {
        const string root = "public func main() -> ()\n    let action: (Lib.Holder) -> () = Lib.Api.hold\n    action(Lib.Holder.init())\n    return\n";
        const string lib = "public struct Holder\n    public var n: i32\n    public init()\n        self.n = 0\n    drop => ()\n" + Hold;
        var eager = TwoModule(root, lib, SyntaxTable.LibraryModule + 1);
        var lazy = TwoModule(root, lib, 1);
        Assert.Equal((1, 3), (eager.Works.Length, lazy.Works.Length));
        Assert.Equal((eager.Fresh, lazy.Works[1] + lazy.Fresh), (eager.Works[0], lazy.Works[2]));
        Assert.Empty(lazy.Rows);
        Assert.Equal(eager.Items, lazy.Items);
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

    // A two-module compilation (main's module 0 and Lib) built with a root range (1: Lib's bodies on demand), analyzed and collected with
    // main's module as the roots by a copy of Compilation.Collect's loop, which must stay equal to it (the C-col theory drives the loop
    // itself), copied to observe the first run: the records of both classes and the analyses' problems (Rows), the checked set (Items),
    // the collector's Work after each run, the first run's set version and rows, and the work of a fresh collector's run over the stepped
    // program (one clean run's).
    private static (string[] Rows, string[] Items, int[] Works, ulong First, string[] FirstRows, int Fresh) TwoModule(string root, string lib, int range)
    {
        var c = ModuleBindingTest.Create(root, lib);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        var analysis = new MirAnalysis(c.Types);
        var program = analysis.Run(new MirBuilder(c).Build(range), 1);
        var collector = new InstanceCollector(c.Types, c.Layout);
        var set = collector.Run(program, 1);
        var (first, firstRows, works) = (set.Version, Rows(c, program), new List<int> { collector.Work });
        for (; set.Version != program.Version; set = collector.Run(program, 1), works.Add(collector.Work))
        {
            analysis.Step(program, 1);
        }

        var items = Enumerable.Range(0, set.Items.Length).Select(x => $"{x} {Label(program, set.Items[x])} view {set.Items[x].View} from " +
            $"{set.Items[x].Parent}: {string.Join(", ", set.Targets(x).ToArray().Select(static t => $"{t.Item} {t.At} {t.Runtime}"))}").ToArray();
        var rows = Rows(c, program);
        var fresh = new InstanceCollector(c.Types, c.Layout);
        Assert.Equal(program.Version, fresh.Run(program, 1).Version);
        return (rows, items, [.. works], first, firstRows, fresh.Work);
    }

    // A program's records of both classes and its analysis problems, each with its subject's first line.
    private static string[] Rows(Compilation c, MirProgram program)
        => [.. program.Records(MirRecordClass.Construction).Select(x => $"construction {x.Code} {Line(c, x.Subject)}"),
            .. program.Records(MirRecordClass.Collection).Select(x => $"collection {x.Code} {Line(c, x.Subject)}"),
            .. program.Problems.All.ToArray().Where(static x => !x.IsRecord).Select(x => $"problem {x.Code} {x.Requirement} {Line(c, x.Subject)}")];

    private static string Line(Compilation c, SourceRef subject) => c.SyntaxOf(subject).ToString().Split('\n')[0];
}
