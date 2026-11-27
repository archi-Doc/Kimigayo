// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// PLAN G951 U1b (the Back's C-378 §3) and G337 U2 (DECISIONS R9-Bx): the collector's round rule. In a build whose root range is the
// program's, a run stops before a body the analyses have not stepped (one a lookup built since the last step) and commits nothing;
// Compilation.Collect steps the bodies built since and runs it again, resuming there, until a run reaches its end, and the version's runs
// equal the one run over the eager build, its work included.
public sealed partial class InstanceCollectorTest
{
    private const string Hold = "public group Api\n    public func hold<T>(value: T) -> () => ()\n"; // Lib's generic function the rows erase.

    // X3 with a layout limit and A3b's static-storage gap: labels as AStoppedRunCommitsNothingAndTheNextResumes states.
    private const string LimitAction = "func a(x: [2147483648 of u8]) -> i32 => 1\ngroup G\n    public var count: i32 = 0\n" + MirBuildTest.StopwatchAction;

    // C-col and X3 (R9-Bx). Labels (SPEC): X3 valid (10.5.3, an erasure at a declared Function Type; 7.7.4); Hello valid (22.7);
    // Utf8Formatting and Milestone31 valid; Generic 1 and 32 valid (CompilerPipelineBenchmark's Generic shape: Array, Dictionary, Option, a
    // generic struct and two generic functions under a Contract bound per item Type; 8.6.1, 13). The lazy build's checked set (each item in
    // order with its key, view, site and parent, so its generated names, which are item indices; its targets; its body's A2 table), its
    // collection records in order, its generated module and its Work equal the eager build's one run; the lazy collection takes `runs`
    // runs, one more than its stops (X3's Stopwatch.start first demanded by its erasure's adapter; the storage bodies by their items, in
    // waves; P1's counts). Fail-before (957e3cf45's rule: a missed run replayed the FIFO again): the lazy Work exceeds the eager run's, and
    // 2 runs where these take 3 to 5.
    [Theory]
    [InlineData("X3", 2)]
    [InlineData("docs/examples/Hello/Hello.kimi", 1)]
    [InlineData("docs/examples/Utf8Formatting/Utf8Formatting.kimi", 4)]
    [InlineData("tests/milestones/Milestone31.kimi", 5)]
    [InlineData("Generic1", 3)]
    [InlineData("Generic32", 3)]
    public void ALazyCollectionEqualsTheEagerOne(string name, int runs)
    {
        var source = name switch
        {
            "X3" => MirBuildTest.StopwatchAction,
            "Generic1" => Generic(1),
            "Generic32" => Generic(32),
            _ => Repository.ReadText(name.Split('/')),
        };
        var eager = Collected(source, static _ => SyntaxTable.LibraryModule + 1);
        var lazy = Collected(source, static c => c.SourceModules.Length);
        Assert.Equal(eager.Items, lazy.Items);
        Assert.Equal(eager.Records, lazy.Records);
        Assert.Equal(eager.Module, lazy.Module);
        Assert.Equal((1, runs, eager.Work), (eager.Runs, lazy.Runs, lazy.Work));
    }

    // g2 (PLAN G949 Stage 2 U3; C-381 §2): MIR stays per compilation over the shared frozen library. Milestone31's checked set, records and
    // module are the same before and after Hello's compilation built, analyzed and collected over the same library: the analysis and the
    // collector key their library state on the program, so each takes the library step for its own (labels as above).
    [Fact]
    public void AProgramsCollectionIsTheSameAfterAnotherCompilationsOverOneLibrary()
    {
        var milestone = Repository.ReadText("tests", "milestones", "Milestone31.kimi");
        var before = Collected(milestone, static c => c.SourceModules.Length);
        _ = Collected(Repository.ReadText("docs", "examples", "Hello", "Hello.kimi"), static c => c.SourceModules.Length);
        var after = Collected(milestone, static c => c.SourceModules.Length);
        Assert.Equal(before.Items, after.Items);
        Assert.Equal(before.Records, after.Records);
        Assert.Equal(before.Module, after.Module);
    }

    // C-378 §3's limit row through a stopped run (DECISIONS R5-M171, R9-Bx). Labels: valid (SPEC 22.4.1, 7.7.4); IMPL 21.3.2: the 2 GiB
    // parameter Type is over the layout limit, one GenerationResourceLimit_Kd per declaration at its least use; DECISIONS R5-M82: the
    // settable static is A3b's static-storage gap at its declaration. X3 with both, built lazily: the first run reaches both uses and stops
    // before Stopwatch.start's body; it commits nothing (no record, no end-of-run record, a set of no version) and keeps the items it
    // admitted; after the step the next run resumes there, its records are the eager compilation's, once each, and Work is one run's, a
    // fresh collector's run over the stepped program. Fail-before (957e3cf45's rule): the missed run kept no item, and Work summed the
    // missed run's and a clean run's.
    [Fact]
    public void AStoppedRunCommitsNothingAndTheNextResumes()
    {
        var c = MirBuildTest.Compile(LimitAction);
        var analysis = new MirAnalysis(c.Types);
        var program = analysis.Run(new MirBuilder(c).Build(c.SourceModules.Length), c.SourceModules.Length);
        var collector = new InstanceCollector(c.Types, c.Layout);
        var stopped = collector.Run(program, c.SourceModules.Length);
        Assert.Equal((0UL, 1), (stopped.Version, collector.RunCount));
        Assert.NotEqual(0, stopped.Items.Length);
        Assert.Empty(program.Records(MirRecordClass.Collection));
        analysis.Step(program, c.SourceModules.Length);
        Assert.Equal(program.Version, collector.Run(program, c.SourceModules.Length).Version);
        Assert.Equal(2, collector.RunCount);
        string[] records = ["GenerationResourceLimit_Kd [2147483648 of u8]", "Unsupported_Kd public var count: i32 = 0"];
        Assert.Equal(records, Records(c, program).Order(StringComparer.Ordinal));
        var eager = MirBuildTest.Compile(LimitAction);
        Assert.Equal(Records(eager, eager.BuildMir()), Records(c, program));
        var fresh = new InstanceCollector(c.Types, c.Layout);
        Assert.Equal(program.Version, fresh.Run(program, c.SourceModules.Length).Version);
        Assert.Equal((fresh.Work, 1), (collector.Work, fresh.RunCount));
    }

    // The Middle's C-536 N2 and N3 (R9-Bx): a resumed run with another root range is a broken contract, and a run that throws leaves
    // nothing to resume: the version's next run drops the round it left open and starts the version again. The program above (labels
    // there), built lazily: the first run stops; after the step a run over the library's range throws; the next run, over the first range,
    // starts again and ends with the eager compilation's records, once each, its Work a fresh run's after the first run's.
    // Fail-before (957e3cf45's rule, every run starting the version): no throw.
    [Fact]
    public void ARunAfterAThrowStartsTheVersionAgain()
    {
        var c = MirBuildTest.Compile(LimitAction);
        var analysis = new MirAnalysis(c.Types);
        var program = analysis.Run(new MirBuilder(c).Build(c.SourceModules.Length), c.SourceModules.Length);
        var collector = new InstanceCollector(c.Types, c.Layout);
        Assert.Equal(0UL, collector.Run(program, c.SourceModules.Length).Version);
        var stopped = collector.Work;
        analysis.Step(program, c.SourceModules.Length);
        Assert.Throws<InvalidOperationException>(() => collector.Run(program, SyntaxTable.LibraryModule + 1));
        Assert.Equal(program.Version, collector.Run(program, c.SourceModules.Length).Version);
        var eager = MirBuildTest.Compile(LimitAction);
        Assert.Equal(Records(eager, eager.BuildMir()), Records(c, program));
        var fresh = new InstanceCollector(c.Types, c.Layout);
        Assert.Equal(program.Version, fresh.Run(program, c.SourceModules.Length).Version);
        Assert.Equal((stopped + fresh.Work, 3), (collector.Work, collector.RunCount));
    }

    // The Middle's review N6: Work and RunCount sum one version's runs, so a version whose stopped run had no last one (as a fault in the
    // driver's loop leaves it) carries none of its runs into the next version. X3 (valid, above), built lazily in two Binding passes of
    // one compilation: the new version's first run stops as the first version's did, with the same work, and counts as its first run.
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
    // lazily, the first run stops before hold (built by the erasure's adapter; its Drop's targets follow the step) with b as g's least use,
    // and commits nothing; the version's last run records a once, as the eager build, and as Compilation's own check, whose root range
    // holds Lib (L1, PLAN G951 U2). Fail-before (a run committing): records at b and at a.
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
        Assert.Equal(eager.Rows, Rows(c, c.BuildMir())); // One clean run (Lib is a source module); Compilation.Collect's stop path is C-col's (ALazyCollectionEqualsTheEagerOne).
    }

    // Its mirror across a stop (R9-Bx §4 row 4 (a)). Labels as above, g with two Type parameters: main's g<[2147483648 of u8], i32> reaches
    // a, the least use, before the first run stops at hold; the drop of hold's parameter's g<i32, [2147483648 of u8]> reaches b, a greater
    // use, after it. The version's last run records a once, as the eager build. Fail-before (a mutant dropping the round at a resume, so
    // the uses before the stop): the record at b.
    [Fact]
    public void ALeastUseReachedBeforeAStopIsTheOneRecord()
    {
        const string root = "struct Holder<T>\n    public var n: i32\n    public init()\n        self.n = 0\n    drop => _ = Gs.g<i32, T>()\n" +
            "group Gs\n    public func g<T, U>() -> i32\n        let a: Option<T> = .None\n        let b: Option<U> = .None\n        return 1\n" +
            "public func main() -> ()\n    _ = Gs.g<[2147483648 of u8], i32>()\n    let action: (Holder<[2147483648 of u8]>) -> () = Lib.Api.hold\n" +
            "    action(Holder<[2147483648 of u8]>.init())\n    return\n";
        var eager = TwoModule(root, Hold, SyntaxTable.LibraryModule + 1);
        var lazy = TwoModule(root, Hold, 1);
        Assert.Equal((0UL, 2), (lazy.First, lazy.Works.Length));
        Assert.Empty(lazy.FirstRows);
        Assert.Equal(["collection GenerationResourceLimit_Kd let a: Option<T> = .None"], eager.Rows);
        Assert.Equal(eager.Rows, lazy.Rows);
        Assert.Equal(eager.Items, lazy.Items);
    }

    // A growth chain across a stop (R9-Bx §4 row 4 (b); DECISIONS R5-M170). Labels (SPEC): valid (8.6.1: Lib's grow instantiated at
    // Box^n<i32>; 10.5.3 and 7.7.4: each instance erases Lib's sink to a declared Function Type); IMPL 21.3.2 with R5-M170: grow's
    // 17th generation on the chain is GenerationResourceLimit_Kd, once. Built lazily, sink is first demanded by grow<i32>'s erasure's
    // adapter, so the first run stops before sink after it admitted grow's first generations, and the version's runs count the chain's
    // generations once: one growth record and the eager items. Fail-before (a mutant clearing the admission state at a resume): the chain's
    // count restarts after the stop, so it admits more of grow's items than the eager build.
    [Fact]
    public void AGrowthChainAcrossAStopIsOneRecord()
    {
        const string root = "public func main() -> ()\n    _ = Lib.Api.grow<i32>(1, 20)\n    return\n";
        const string lib = "struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value@move\n" +
            "public group Api\n    public func sink(value: i32) -> () => ()\n    public func grow<T>(value: T, n: i32) -> i32\n" +
            "        let f: (i32) -> () = Api.sink\n        if n == 0 => return 0\n        return grow<Box<T>>(Box<T>.init(value@move), n - 1)\n";
        var eager = TwoModule(root, lib, SyntaxTable.LibraryModule + 1);
        var lazy = TwoModule(root, lib, 1);
        Assert.Equal(0UL, lazy.First);
        Assert.True(lazy.Works.Length >= 2, $"{lazy.Works.Length} runs");
        Assert.Empty(lazy.FirstRows);
        _ = Assert.Single(eager.Rows, static x => x.StartsWith("collection GenerationResourceLimit_Kd ", StringComparison.Ordinal));
        Assert.Equal(eager.Rows, lazy.Rows);
        Assert.Equal(eager.Items, lazy.Items);
        Assert.Equal(eager.Works[0], lazy.Works[^1]);
    }

    // The commit rule's other half (the Middle's review N2, its RV-C and RV-A): a construction record of a body built during a round that
    // the collector's run stops in or A4's misses is added at once and stays. Labels (SPEC): valid (7.2.3: each default fits its parameter
    // Type; 7.7.4; RV-A 8.11.2, 8.11.4: g and the defaults call and add only, so the confined bound holds); the records are implementation
    // limits, equal in both builds: the body budget's ExpansionLimit_Kd (IMPL 21.3.2) at g's second `f12()`, whose copy of f12's default
    // would take g's own body past the budget (each template of Lib's f-chain fits; f13's would not, MirBuildTest.DefaultExpansionStopsAtTheLimit),
    // and in the a4 form A4's OwnershipDepthLimit_Kd at the obligation `apply(Lib.Api.g)` (its argument's deep Type, R4-M153); the collector
    // records none (the deep erased Type is an instance, R7-M54). Lib's g is first demanded and built inside an open round: the collector's
    // first run (an erasure's adapter) or A4's first round (an obligation's argument). The record lies in g's own body: a default
    // template's is made before any round (MirBuilder.Build), and MirBuildTest.Unbuilt's selection, a generic body's, cannot stand in g
    // (a non-generic function item), while in a generic body g calls it is built outside A4's missed round. Before PLAN G396 (3)'s B3
    // the record was B3's Unsupported_Kd at g's `f(b)`, a default over Bx^65, which is now built. Fail-before (every problem held while a
    // round is open): the construction record is lost.
    [Theory]
    [InlineData("collector", 2)]
    [InlineData("a4", 1)]
    public void ARecordMadeDuringAMissedRoundStays(string round, int runs)
    {
        var root = round == "collector" ? $"public func main() -> ()\n    let action: (ref/{Deep("Lib.Bx")}) -> () = Lib.Api.g\n    return\n"
            : $"func apply<F>(action: ref/F) -> ()\n    F is Callable<(ref/{Deep("Lib.Bx")}) -> ()>\n        effect confined\n    return\n" +
                "public func main() -> ()\n    apply(Lib.Api.g)\n    return\n";
        var lib = "public struct Bx<T>\n    public var v: T\npublic group Api\n    public func f0(x: i32 = 1) -> i32 => x\n" +
            string.Concat(Enumerable.Range(1, 12).Select(static i => $"    public func f{i}(x: i32 = Api.f{i - 1}() + Api.f{i - 1}()) -> i32 => x\n")) +
            $"    public func g(b: ref/{Deep("Bx")}) -> ()\n        _ = f12()\n        _ = f12()\n";
        var eager = TwoModule(root, lib, SyntaxTable.LibraryModule + 1);
        var lazy = TwoModule(root, lib, 1);
        Assert.Equal(runs, lazy.Works.Length);
        Assert.Contains("construction ExpansionLimit_Kd f12()", lazy.FirstRows);
        Assert.Equal(eager.Rows, lazy.Rows);
        Assert.Equal(eager.Items, lazy.Items);

        static string Deep(string type) => string.Concat(Enumerable.Repeat(type + "<", 65)) + "i32" + new string('>', 65); // Past the storage depth.
    }

    // PLAN G651 U2b R8 Rev 2 (rounds; the Front's c495-nest built lazily; IMPL A.4, 21.3.2): a body first built in a collector round
    // forms a refused instance after Binding's run, and its record survives the stopped run. Labels (SPEC): valid (7.2.3: takeh's default
    // fits its parameter Type; 10.5.3 and 7.7.4: Lib's g erased to a declared Function Type); IMPL A.4: the default's copy at g's omitting
    // call nested in count(..) forms Option<W^192<W^8000<i32>>> past H: exactly one TypeHeightLimit_Kd at takeh(p40), in both builds (its
    // single-module form is MirBuildTest's nest192). Built lazily, g is first built by the erasure's adapter in the collector's first run,
    // which stops before it and commits nothing; g's site added its use at once, and the version's last run flushes it once, as the eager
    // build's one run (fail-before on dev: B3's Unsupported_Kd at the omitting call and InternalInvariant_Kd at the project).
    [Fact]
    public void ARefusedInstanceOfABodyBuiltInAMissedRoundIsItsRecord()
    {
        const string root = "public func main() -> ()\n    let action: (raw/i32) -> () = Lib.Api.g\n    return\n";
        var lib = "public struct W<T>\n    public var v: T\npublic group Api\n" + $"    public func lift<T>(p: raw/T) -> raw/{W(200)} => null\n" +
            $"    public func count<U>(u: U) -> i32 => 0\n    public func takeh<T>(p: raw/T, k: i32 = count(Option<{W(192)}>.None)) => ()\n" +
            "    public func g(p0: raw/i32) -> ()\n" + string.Concat(Enumerable.Range(1, 40).Select(static i => $"        let p{i} = lift(p{i - 1})\n")) +
            "        _ = count(takeh(p40))\n        return\n";
        var eager = TwoModule(root, lib, SyntaxTable.LibraryModule + 1);
        var lazy = TwoModule(root, lib, 1);
        Assert.Equal((1, 2), (eager.Works.Length, lazy.Works.Length));
        Assert.Empty(lazy.FirstRows);
        Assert.Equal(["collection TypeHeightLimit_Kd takeh(p40)"], eager.Rows);
        Assert.Equal(eager.Rows, lazy.Rows);
        Assert.Equal(eager.Items, lazy.Items);

        static string W(int n) => string.Concat(Enumerable.Repeat("W<", n)) + "T" + new string('>', n);
    }

    // A Drop site's demand waits for its body's step (the Back's review N3, R9-Bx): a run stops before a body A2 does not hold, so a chain
    // through its Drop takes a third run. Labels (SPEC): valid (10.5.3 and 7.7.4: hold's T bound from the declared Function Type, then
    // erased; 7.2.3 and 16.3: hold destroys its parameter, running Lib's Holder drop); no record. Built lazily, the first run stops before
    // hold (built by the erasure's adapter), the second, resuming there, before Holder's drop (first demanded by hold's Drop), and the third
    // reaches the end and equals the eager build's one run, its Work included: the three runs' Work sums one run's. Fail-before (957e3cf45's
    // rule): Work summed the missed runs' and a clean run's (28 where one run's is 14).
    [Fact]
    public void AChainThroughAStoppedBodysDropTakesAThirdRun()
    {
        const string root = "public func main() -> ()\n    let action: (Lib.Holder) -> () = Lib.Api.hold\n    action(Lib.Holder.init())\n    return\n";
        const string lib = "public struct Holder\n    public var n: i32\n    public init()\n        self.n = 0\n    drop => ()\n" + Hold;
        var eager = TwoModule(root, lib, SyntaxTable.LibraryModule + 1);
        var lazy = TwoModule(root, lib, 1);
        Assert.Equal((1, 3), (eager.Works.Length, lazy.Works.Length));
        Assert.Equal((eager.Works[0], eager.Works[0], eager.Works[0]), (eager.Fresh, lazy.Fresh, lazy.Works[2]));
        Assert.Empty(lazy.Rows);
        Assert.Equal(eager.Items, lazy.Items);
    }

    // A compilation of a source built with a root range, analyzed and collected with its source modules as roots by Compilation's driver
    // (Collect), then sealed as BuildMir seals it: its checked set, its collection records and its module generated with every root an
    // output root, and the collector's runs and Work.
    private static (string[] Items, string[] Records, string Module, int Runs, int Work) Collected(string source, Func<Compilation, int> range)
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
        Assert.True(new ModuleCodegen(c.Layout, c.Locations, c.Types, c.Binding.LibraryImports, c.Tests, TargetProfile.Windows).Write(program, collector, c.Binding.Startup, roots, writer));
        return (items, Records(c, program), writer.ToString(), collector.RunCount, collector.Work);
    }

    // CompilerPipelineBenchmark's Generic shape at a count of item Types (its GenericSource, restated: the tests reference no benchmark):
    // each Type instantiates Array, Dictionary, Option, a two-parameter generic struct and two generic functions under a Contract bound.
    private static string Generic(int types)
        => "contract Weighted\n    func weight(self: ref/Self) -> i64\n\nstruct Pair<A, B>\n    public let first: A\n    public let second: B\n" +
            "    public init(first: A, second: B)\n        self.first = first@move\n        self.second = second@move\n\n" +
            "func total<T>(items: ref/Array<T>) -> i64\n    T is Weighted\n    var sum: i64 = 0\n    for item in items\n        sum += item.weight()\n" +
            "    return sum\n\nfunc pairWeight<A, B>(pair: ref/Pair<A, B>) -> i64\n    A is Weighted\n    B is Weighted\n" +
            "    return pair.first.weight() + pair.second.weight()\n\n" +
            string.Concat(Enumerable.Range(0, types).Select(i => $"struct Item{i}\n    Self is Weighted\n    public let value: i64\n" +
                $"    public init(value: i64) => self.value = value\n    public func weight(self: ref/Self) -> i64 => self.value + {i}\n\n" +
                $"func use{i}() -> i64\n    var items: Array<Item{i}> = []\n    items.append(Item{i}.init(1))\n" +
                $"    var lookup: Dictionary<i64, Item{i}> = [:]\n    _ = lookup.tryInsert(1, Item{i}.init(2))\n" +
                $"    let pair = Pair<Item{i}, Item{(i + 1) % types}>.init(Item{i}.init(3), Item{(i + 1) % types}.init(4))\n" +
                "    var sum = total(items@ref) + pairWeight(pair@ref)\n    match lookup.tryGet(1)\n" +
                "        .Some(let found) => sum += found.weight()\n        .None => ()\n    return sum\n\n")) +
            "public func main() -> ()\n    var result: i64 = 0\n" + string.Concat(Enumerable.Range(0, types).Select(static i => $"    result += use{i}()\n")) +
            "    return\n";

    // A program's collection records in order, each its code and its subject's first line.
    private static string[] Records(Compilation c, MirProgram program)
        => [.. program.Records(MirRecordClass.Collection).Select(x => $"{x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0]}")];

    // A two-module compilation (main's module 0 and Lib) built with a root range (1: Lib's bodies on demand), analyzed and collected with
    // main's module as the roots by a copy of Compilation.Collect's loop, which must stay equal to it (the C-col theory drives the loop
    // itself), copied to observe the first run: the records of both classes and the analyses' problems (Rows), the checked set (Items),
    // the collector's Work after each run, the first run's set version and rows, and the work of a fresh collector's run over the stepped
    // program (one run's).
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
