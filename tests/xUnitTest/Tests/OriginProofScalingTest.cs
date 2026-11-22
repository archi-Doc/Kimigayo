// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Verification;
using Xunit;

namespace XunitTest;

public class OriginProofScalingTest(ITestOutputHelper output)
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

    // SPEC 15.3.4-15.3.7, 15.6.1: a relation the visible premises prove is accepted and one they do not prove is an
    // UnprovenOriginRelation_Kd, one per unproven conjunct, at every size of the family.
    [Theory]
    [MemberData(nameof(Families))]
    public void FamiliesHaveTheirSpecifiedOutcome(string family, string? code)
    {
        foreach (var size in new[] { 1, 2, 8, 16, 32 })
        {
            var output = DiagnosticCorpus.Check(OriginProofWorkloads.Create(family, size));
            Assert.Equal(code?.Split(' ') ?? [], output.Diagnostics.Select(static x => x.Code));
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
    // PLAN G647 (SPEC 15.6.1): x of n nested ref layers returned as its own Type (accepted) and as y's, whose innermost Origin alone
    // differs (c: d, unproven), past the 32 and 64 components the relation walks once counted. The walks' steps (TermWalkWork) grow with
    // the layers where the Types differ, and do not grow where they are one Type, which the fit does not enter. n nested Origins have n(n-1)/2 outlives premises by SPEC, so the premise graph's nodes at most double and its edges at
    // most quadruple (plus two a layer) per doubling; the summed proof work is not this row's (PLAN G647's design section 7).
    // PLAN G778: the same through pair layers (`x < x` under `s is value or valueborrow`, valid; its twin returns x as y's Type, which
    // needs c: d in the borrow cases) and through a call (returning first(x, y), valid; first(y, x) needs d: c). The declaration judge's
    // rows, one per Origin stored below a layer, and a call's well-formedness and inference requests share one premise environment per
    // anchor (a premise session), so the edges written grow with the premises, at most four times plus four a layer per doubling, and the
    // environments built at most double; each request still closes the graph from its target (R7-O1 e).
    [Theory]
    [InlineData("ref", true)]
    [InlineData("ref", false)]
    [InlineData("pair", true)]
    [InlineData("pair", false)]
    [InlineData("call", true)]
    [InlineData("call", false)]
    public void NestedLayerRelationsFollowTheOriginPairs(string form, bool valid)
    {
        var rows = new[] { 33, 65, 129 }.Select(layers =>
        {
            var (c, d) = (TypeDagScalingTest.RefLayers(layers, "i32", "c"), TypeDagScalingTest.RefLayers(layers, "i32", "d"));
            var (pc, pd) = (c.Replace("ref/", "s/", StringComparison.Ordinal), d.Replace("ref/", "s/", StringComparison.Ordinal));
            var source = form switch
            {
                "ref" => $"func f(x: {c}, y: {d}) -> {(valid ? c : d)}\n    return x\n",
                "pair" => $"func f<s/T>(x: {pc}, y: {pd}, m: s/T) -> {(valid ? "bool" : pd)}\n    s is value or valueborrow\n    return {(valid ? "x < x" : "x")}\n",
                _ => $"func first(x: {c}, y: {d}) -> {c}\n    return x\nfunc f(x: {c}, y: {d}) -> {c}\n    return first({(valid ? "x, y" : "y, x")})\n",
            };
            var compilation = Compilation.CreateForTest();
            Assert.True(compilation.Prepare(WindowsProfile.Target));
            compilation.Kotonoha.AddSource(new SourceDocument("nested.kimi", source + "public func main() => ()\n"));
            var metrics = new OriginProofMetrics();
            compilation.Binding.OriginProofMetrics = metrics;
            compilation.Bind();
            compilation.Binding.CheckStartup(OutputKind.Application);
            compilation.Analyze();
            var codes = compilation.Binding.Issues.Select(static x => x.Code.ToString()).Concat(compilation.Problems.Select(static x => x.Code.ToString()));
            Assert.Equal(valid ? [] : ["UnprovenOriginRelation_Kd"], codes.Distinct());
            return (Layers: layers, Work: (long)compilation.Binding.TermWalkWork, metrics.MaxNodes, metrics.MaxEdges, metrics.WrittenEdges, metrics.Environments);
        }).ToArray();
        Assert.True(form != "ref" || ((valid || rows[1].Work > rows[0].Work) && MirBorrowsTest.Grows(rows.Select(static x => x.Work).ToArray(), 2)), string.Join(' ', rows));
        Assert.True(rows[2].MaxNodes <= 2 * rows[1].MaxNodes && rows[2].MaxEdges <= (4 * rows[1].MaxEdges) + (2 * rows[2].Layers), string.Join(' ', rows));
        Assert.True(rows[2].WrittenEdges <= (4 * rows[1].WrittenEdges) + (4 * rows[2].Layers) && rows[2].Environments <= 2 * rows[1].Environments, string.Join(' ', rows));
    }

    // PLAN G492-a (ii), the owner's O-426 (a): a diamond chain of 40 `var` definitions (x{i} and y{i} each bounded by x{i-1} and y{i-1},
    // so each solved value is a meet of the two regions one level up) read through a Closure: A5's split of a failed source (df, invalid:
    // `p outlives q` is Unknown, one record; SPEC 15.6.5) and the retention of a Closure call whose contract names x{N} (dr, valid) read
    // each region once per judgement and per retention computation, and a region's value once per pass, so the check ends at once. Without
    // the visited sets every path through the diamond was walked again (24 levels: 8.5 s -> 83 s). df's split is A5's, so its
    // MirOrigins.Work (each operand Split judges counted) grows linearly from 10 to 40 levels; retention (dr) has no counter.
    [Theory(Timeout = 120000)]
    [InlineData("dr", false)]
    [InlineData("df", true)]
    public async Task ADiamondChainOfVarDefinitionsIsReadOnce(string form, bool invalid)
    {
        var records = await Task.Run(() => CompilationTestHelper.RecordsAtLines(DiamondChain(form, 40)), TestContext.Current.CancellationToken);
        Assert.True(
            invalid ? records.Length == 1 && records[0].StartsWith("UnprovenOriginRelation_Kd", StringComparison.Ordinal) : records.Length == 0,
            $"{form}: [{string.Join(" | ", records)}]");
        if (invalid)
        {
            long[] work = [.. new[] { 10, 20, 40 }.Select(n => OriginWork(DiamondChain(form, n)))];
            output.WriteLine($"{form} N 10, 20, 40: origins {string.Join(", ", work)}");
            Assert.True(MirBorrowsTest.Grows(work, 2.5), $"{form}: origins {string.Join(", ", work)}");
        }
    }

    [Fact]
    public void AFixedReadWalksEachRegionOnce()
    {
        var small = RegionReads(8);
        var large = RegionReads(24);
        Assert.True(large <= (4 * small) + 8, $"region reads {small} at depth 8, {large} at depth 24");
    }

    // SPEC 15.6.5's Closure sentence (R7-O2 A1; the Front's T4): n nested Closures, each capturing the `let` binding of the level above's
    // capture, the innermost returning it at p (chain); and each capturing two, whose values at the next level are both meets of the two
    // above (dag: two carried paths per level, 2^n paths in all; the code review's N5). Each Closure's carried regions are walked once per
    // region-graph version once no body is being bound (BoundClosure.Walk, which the body's oracle reads too) and each body's oracle reads
    // each region once, in turn included: the region reads grow linearly with n (the row asserts them; the time is printed). n is 15, 30, 60 for the chain; the dag's is 5,
    // 10, 20, below the knee of a compile time exponential in its depth that dev has without A1 (PLAN G1209: about 36 s at
    // 30 levels); the unit that fixes G1209 raises the dag's depths to the chain's.
    [Theory]
    [InlineData("chain", 15)]
    [InlineData("dag", 5)]
    public void ACarriedRegionChainReadsEachRegionOnce(string shape, int depth)
    {
        var reads = new long[3];
        var times = new long[3];
        int[] depths = [depth, 2 * depth, 4 * depth];
        for (var i = 0; i < depths.Length; i++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            reads[i] = CarriedMetrics(shape == "dag" ? CarriedDag(depths[i]) : CarriedChain(depths[i])).RegionReads;
            times[i] = watch.ElapsedMilliseconds;
            Assert.NotEqual(0, reads[i]);
        }

        output.WriteLine($"carried {shape} N {string.Join(", ", depths)}: region reads {string.Join(", ", reads)}; ms {string.Join(", ", times)}");
        Assert.True(MirBorrowsTest.Grows(reads, 2.5), $"region reads {string.Join(", ", reads)}");
    }

    // SPEC 15.6.5, 15.4.3 rule 3 (R7-O2 O2-H): n nested Closures, each capturing the `let` binding r of the outer body, the innermost a
    // literal whose header names r's region: while its body is bound the header reads that region as its value through the admission
    // (AdmittedEntry), one step per enclosing Closure's own entry, and at Final each walk admits its entries once: the admissions grow
    // linearly with n (asserted; the time is printed). Each is valid (MissingOriginBinding_Kd at the header before O2-H). n is 15, 30, 60.
    [Fact]
    public void AHeadersCarriedRegionIsAdmittedOnceThroughEachEnclosingClosure()
    {
        var (admissions, times) = (new long[3], new long[3]);
        int[] depths = [15, 30, 60];
        for (var i = 0; i < depths.Length; i++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            admissions[i] = CarriedMetrics(CarriedHeader(depths[i])).CarriedAdmissions;
            times[i] = watch.ElapsedMilliseconds;
            Assert.NotEqual(0, admissions[i]);
        }

        output.WriteLine($"carried header N {string.Join(", ", depths)}: admissions {string.Join(", ", admissions)}; ms {string.Join(", ", times)}");
        Assert.True(MirBorrowsTest.Grows(admissions, 2.5), $"admissions {string.Join(", ", admissions)}");
    }

    // SPEC 15.2.3, 15.3.6, 15.6.5 (R7-O2 A1+A2; the owner's O-471 (a), O-481 (b); the Front's D6): n nested Closures, each declaring a `var`
    // bound by the captured `var` of the level above (chain), or two, each bound by both above (diamond: two carried paths per level), and
    // the erasure of a Closure over the innermost region, whose Owned is read through each candidate in turn to `static` at Final
    // (JudgeErasedOwned): the walks (one per Closure per region-graph version, CarriedWalk) and the region reads in a body (one per
    // region and body per version, ValueIn) grow linearly with n (asserted; the time is printed). Each is valid. n is 15, 30, 60 for the
    // chain and 10, 20, 40 for the diamond.
    [Theory]
    [InlineData("chain", 15)]
    [InlineData("diamond", 10)]
    public void AnErasuresOwnedReadsEachCarriedCandidateOnce(string shape, int depth)
    {
        var (walks, values, times) = (new long[3], new long[3], new long[3]);
        int[] depths = [depth, 2 * depth, 4 * depth];
        for (var i = 0; i < depths.Length; i++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var metrics = CarriedMetrics(ErasedCarried(shape == "diamond", depths[i]));
            (walks[i], values[i], times[i]) = (metrics.CarriedWalks, metrics.ValueInReads, watch.ElapsedMilliseconds);
            Assert.NotEqual(0, metrics.ValueInReads);
        }

        output.WriteLine($"erased {shape} N {string.Join(", ", depths)}: walks {string.Join(", ", walks)}; ValueIn reads {string.Join(", ", values)}; ms {string.Join(", ", times)}");
        Assert.True(MirBorrowsTest.Grows(walks, 2.5), $"walks {string.Join(", ", walks)}");
        Assert.True(MirBorrowsTest.Grows(values, 2.5), $"ValueIn reads {string.Join(", ", values)}");
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

    // PLAN G824 (SPEC 15.6.1, 15.6.5; its design review's B1): rule W reads a slot the body's relations make equal to a fixed Origin as
    // Fixed. N slots reborrow p's referent (each equal to a by uniq's invariance) and each receives a copy of the one before, from a
    // local's borrow (a covariant chain: each slot reaches the chain's start), in a body whose one run covers two classes (a pair binder's
    // masked row). Each size is invalid with one conflict, derived. MirOrigins.Work, with the classes' edges re-formed and indexed for its
    // queries, grows linearly at N = 32 to 256: a walk per slot and a re-index per slot and class made it quadratic.
    [Fact]
    public void RuleWsEqualityIsLinearInTheChainAndItsClasses()
    {
        var rows = new[] { 32, 64, 128, 256 }.Select(static n => MirBorrowsTest.RuleW(
            "func shape<s/T>(p: uniq/(ref/i32 during a) during u, m: s/T)\n    s is owner or ref\n" +
            "    let w: Option<s/(ref/i32 during a) during u> = .None\n    var x = 0\n    var t0 = x@ref\n" +
            string.Concat(Enumerable.Range(0, n).Select(static i => $"    var t{i + 1} = t{i}\n    var r{i} = p@follow@uniq\n    r{i}@follow = t{i + 1}\n")))).ToArray();
        var work = rows.Select(static x => x.Origins).ToArray();
        output.WriteLine($"N 32, 64, 128, 256: origins {string.Join(", ", work)}");
        Assert.All(rows, static x => Assert.Equal((2, true, 1), (x.Classes, x.OneRun, x.Derived)));
        Assert.True(MirBorrowsTest.Grows(work[..3], 2.5) && MirBorrowsTest.Grows(work[1..], 2.5), $"origins {string.Join(", ", work)}");
    }

    // PLAN G824 (its re-review's R1): Fixed's equality costs per class (1 + F') x (E + N), F' the class's Fixed nodes with in- and
    // out-edges, each walked both ways up to the next Fixed node; so it is linear in the body at a fixed signature and in separate cycles,
    // not in F' (P Fixed Origins sharing a chain of 4P copies grow as P x 4P). Here MirOrigins.Work is linear in K, 16 Fixed Origins
    // joined through one covariant chain of K copies (K = 64 to 512), and in P, each of P Fixed Origins on a cycle of its own (P = 4 to
    // 32: past 64 Fixed sources Judge's own fixed pass, components x their words (TyCtxt.OriginChains), takes a second word). Each program
    // is invalid with one conflict, derived: a local's borrow stored through p's reborrowed referent, equal to a0 (SPEC 15.6.1, 15.6.5);
    // the premises z{i} make every a{i} equal, so no other relation fails.
    [Theory]
    [InlineData(new[] { 16, 16, 16, 16 }, new[] { 64, 128, 256, 512 })]
    [InlineData(new[] { 4, 8, 16, 32 }, new[] { 0, 0, 0, 0 })]
    public void RuleWsEqualityIsLinearInTheBodyAndInSeparateCycles(int[] fixedCounts, int[] chains)
    {
        var rows = fixedCounts.Select((p, i) => MirBorrowsTest.RuleW(FixedNodes(p, chains[i]))).ToArray();
        var work = rows.Select(static x => x.Origins).ToArray();
        output.WriteLine($"P {string.Join(", ", fixedCounts)}, K {string.Join(", ", chains)}: origins {string.Join(", ", work)}");
        Assert.All(rows, static x => Assert.Equal((1, true, 1), (x.Classes, x.OneRun, x.Derived)));
        Assert.True(MirBorrowsTest.Grows(work[..3], 2.5) && MirBorrowsTest.Grows(work[1..], 2.5), $"origins {string.Join(", ", work)}");
    }

    // SPEC 15.3.7, 15.6.4 step 3 (R7-O1 d; its design review's C2): a signature part's well-formedness premises over a Type that shares
    // its parts. n locals each pair the one before, a Tuple DAG of 2^n paths over `ref/i32 during t`; `see` takes the last by reference
    // (its instantiated input: the selection's bounds and the selected call's obligations) and `pick` returns it again (a result that
    // carries premises: the inference's bounds and the call's obligations). Each walk visits a distinct part once per query beside the
    // same argument and substituted parts (DECISIONS BU-132), so its visits grow linearly in n (32, 64, 128); a walk per path meets 2^128.
    // Binding is the walk's phase, so the row binds only: the Tuple's 2^n leaves are past every generation limit.
    [Fact]
    public void WellFormedPremisesVisitEachSharedPartOnce()
    {
        var visits = new[] { 32, 64, 128 }.Select(static n => WellFormedParts(n)).ToArray();
        output.WriteLine($"n 32, 64, 128: well-formed parts {string.Join(", ", visits)}");
        Assert.True(visits[1] > visits[0] && MirBorrowsTest.Grows(visits, 2), $"well-formed parts {string.Join(", ", visits)}");
    }

    // P Fixed Origins a{i}, equal by the premises z{i} (SPEC 15.3.3), each with an in- and an out-edge: all joined through one covariant
    // chain of K copies (each q{i} into t0, t{K} into each o{i}.*), or, at K = 0, each through a copy of its own; before them, a local's
    // borrow stored through p's reborrowed referent (a Refuted relation, rule W's query).
    private static string FixedNodes(int fixedCount, int chain)
    {
        var text = new StringBuilder("func shape(p: uniq/(ref/i32 during a0) during u");
        for (var i = 0; i < fixedCount; i++)
        {
            text.Append(CultureInfo.InvariantCulture, $", q{i}: ref/i32 during a{i}, o{i}: uniq/(ref/i32 during a{i}) during w{i}");
            text.Append(CultureInfo.InvariantCulture, $", z{i}: ref/(ref/i32 during a{i}) during a{(i + 1) % fixedCount}");
        }

        text.Append(")\n    var x = 0\n    var r = p@move\n    r@follow = x@ref\n");
        for (var i = 0; i < fixedCount; i++)
        {
            if (chain == 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"    var s{i} = q{i}\n    o{i}@follow = s{i}\n");
            }
            else
            {
                text.Append(CultureInfo.InvariantCulture, $"    {(i == 0 ? "var " : string.Empty)}t0 = q{i}\n");
            }
        }

        for (var k = 0; k < chain; k++)
        {
            text.Append(CultureInfo.InvariantCulture, $"    var t{k + 1} = t{k}\n");
        }

        for (var i = 0; chain > 0 && i < fixedCount; i++)
        {
            text.Append(CultureInfo.InvariantCulture, $"    o{i}@follow = t{chain}\n");
        }

        return text.ToString();
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

    // A5's Origin work over a program's bodies, each body's Judge summed (MirOriginsTest.AShorterMeetsOperandEdgesFormOncePerClass).
    private static long OriginWork(string source)
    {
        var c = MirBuildTest.Compile(source);
        var origins = new MirOrigins(c.Types, new MirCases(c.Types));
        var total = 0L;
        foreach (var body in c.BuildMir().Bodies.ToArray())
        {
            origins.Judge(body, MirCases.Count(body), new MirReport());
            total += origins.Work;
        }

        return total;
    }

    // The reviewer's diamond (`rev5\dc2\gen_diamond.py`): dr returns x{n} through a Closure's call (retention), df relates it to q (A5).
    private static string DiamondChain(string form, int n)
    {
        var source = new StringBuilder(form == "dr" ? "func see(p: ref/i32) => ()\nfunc t(k: bool, p: ref/i32, q: ref/i32)\n" : "func t(k: bool, p: ref/i32, q: ref/i32) -> i32\n");
        source.Append("    var x0 = p\n    var y0 = q\n");
        for (var i = 1; i <= n; i++)
        {
            source.Append($"    var x{i} = x{i - 1}\n    if k\n        x{i} = y{i - 1}\n    var y{i} = y{i - 1}\n    if k\n        y{i} = x{i - 1}\n");
        }

        return source.Append(form == "dr"
            ? $"    let c = func [x{n}] () -> ref/i32 during x{n} => x{n}\n    let v = c()\n    see(v)\n"
            : $"    let c = func [x{n}] () -> ref/i32 during q => x{n}\n    return 0\n").Append("public func main() => ()\n").ToString();
    }

    private static string CarriedChain(int depth)
    {
        var source = new StringBuilder("func see(p: ref/i32) => ()\nfunc f(p: ref/i32)\n    let x0 = p\n");
        var indent = "    ";
        for (var i = 1; i <= depth; i++)
        {
            source.Append($"{indent}let c{i} = func [x{i - 1}] ()\n");
            indent += "    ";
            source.Append($"{indent}let x{i} = x{i - 1}\n");
        }

        source.Append($"{indent}let w: ref/i32 during p = x{depth}\n{indent}see(w)\n");
        return Called(source, indent, depth);
    }

    private static string CarriedDag(int depth)
    {
        var source = new StringBuilder("func see(p: ref/i32) => ()\nfunc f(p: ref/i32, k: bool)\n    let a0 = p\n    let b0 = p\n");
        var indent = "    ";
        for (var i = 1; i <= depth; i++)
        {
            source.Append($"{indent}let c{i} = func [a{i - 1}, b{i - 1}, k] ()\n");
            indent += "    ";
            source.Append($"{indent}let a{i} = if k => a{i - 1} else => b{i - 1}\n{indent}let b{i} = if k => b{i - 1} else => a{i - 1}\n");
        }

        source.Append($"{indent}let w: ref/i32 during p = a{depth}\n{indent}see(w)\n");
        return Called(source, indent, depth);
    }

    // AHeadersCarriedRegionIsAdmittedOnceThroughEachEnclosingClosure's program at `depth` (the h1v twin, n levels deep).
    private static string CarriedHeader(int depth)
    {
        var source = new StringBuilder("struct Box {a}\n    public let v: ref/i32 during a\n    public init(v: ref/i32 during a) => self.v = v\n");
        source.Append("public func main() => ()\nfunc f(p: ref/i32 during static) -> i32\n    let r = p\n");
        var indent = "    ";
        for (var i = 1; i <= depth; i++)
        {
            source.Append($"{indent}let c{i} = func [r] () -> i32\n");
            indent += "    ";
        }

        source.Append($"{indent}let h = func [r] (k: Option<ref/i32 during r>) -> Box => $abort(\"no\")\n{indent}return 1\n");
        for (var i = depth; i >= 1; i--)
        {
            indent = indent[4..];
            source.Append($"{indent}return c{i}()\n");
        }

        return source.ToString();
    }

    // The Front's vco (chain) and dvo (diamond) rows at `depth`: the innermost body erases a Closure over its `var` to `() -> i32`.
    private static string ErasedCarried(bool diamond, int depth)
    {
        var source = new StringBuilder("public func main() => ()\nfunc f(s: ref/i32 during static) -> i32\n    var a0 = s\n");
        source.Append(diamond ? "    var b0 = s\n" : string.Empty);
        var indent = "    ";
        for (var i = 1; i <= depth; i++)
        {
            source.Append(CultureInfo.InvariantCulture, $"{indent}let c{i} = func [a{i - 1}{(diamond ? $", b{i - 1}" : string.Empty)}] () -> i32\n");
            indent += "    ";
            source.Append(CultureInfo.InvariantCulture, $"{indent}var a{i} = a{i - 1}\n");
            if (diamond)
            {
                source.Append(CultureInfo.InvariantCulture, $"{indent}a{i} = b{i - 1}\n{indent}var b{i} = b{i - 1}\n{indent}b{i} = a{i - 1}\n");
            }
        }

        source.Append(CultureInfo.InvariantCulture, $"{indent}let g: () -> i32 = func [a{depth}] () -> i32 => a{depth}@follow\n{indent}return g()\n");
        for (var i = depth; i > 0; i--)
        {
            indent = indent[..^4];
            source.Append(CultureInfo.InvariantCulture, $"{indent}return c{i}()\n");
        }

        return source.ToString();
    }

    // The calls of the nested Closures c1..c{depth}, innermost first, and the entry point.
    private static string Called(StringBuilder source, string indent, int depth)
    {
        for (var i = depth; i > 0; i--)
        {
            indent = indent[..^4];
            source.Append($"{indent}c{i}()\n");
        }

        return source.Append("public func main() => ()\n").ToString();
    }

    private static OriginProofMetrics CarriedMetrics(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("carried.kimi", source));
        var metrics = new OriginProofMetrics();
        c.Binding.OriginProofMetrics = metrics;
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        Assert.Empty(c.Binding.Issues.Select(static x => x.Code).Concat(c.Problems.Select(static x => x.Code)));
        return metrics;
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

    private static long WellFormedParts(int locals)
    {
        var source = new StringBuilder("func see<T>(x: ref/T) -> () => ()\nfunc pick<T>(x: ref/T) -> ref/T => x\nfunc f(t: ref/i32) -> ()\n    let a0 = (t, t)\n");
        for (var i = 1; i < locals; i++)
        {
            source.Append(CultureInfo.InvariantCulture, $"    let a{i} = (a{i - 1}, a{i - 1})\n");
        }

        source.Append(CultureInfo.InvariantCulture, $"    see(a{locals - 1}@ref)\n    let r = pick(a{locals - 1}@ref)\n    return\n");
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("shared.kimi", source.Append("public func main() -> ()\n    let x = 1\n    f(x@ref)\n    return\n").ToString()));
        var metrics = new OriginProofMetrics();
        c.Binding.OriginProofMetrics = metrics;
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
        Assert.NotEqual(0, metrics.WellFormedParts);
        return metrics.WellFormedParts;
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
        Assert.Equal(code?.Split(' ').Distinct() ?? [], codes.Distinct());
        Assert.NotEqual(0, metrics.Closures);
        Assert.Equal(0, metrics.BoundViolations);
        return metrics;
    }
}
