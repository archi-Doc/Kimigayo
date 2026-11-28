// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>PLAN G647: a walk of one Type's parts (a layer walk, or a recursive walk of its components) ends where the Type ends and has
/// no count of its own, so a program is judged alike at any number of layers up to IMPL A.4's Type height H = 8,192. This file owns the
/// layered forms the acceptance rows of the owning classes share (<see cref="LayeredForm"/>), the walks' scaling rows and the stack row.
/// A named Origin on each of n nested layers costs Origin-proof work that grows faster than the layers (PLAN G647's design section 7: the
/// premise environment and the proof's closure, not a layer walk), which keeps the written sizes small.</summary>
public partial class TypeDagScalingTest
{
    private const string LayerTypes =
        "struct Node\n    public var weight: i32\n    public init(weight: i32) => self.weight = weight\n    public func load(self: ref/Self) -> i32 => self.weight\n" +
        "struct Point\n    Self is Equatable\n    public var x: i32\n    public init(x: i32) => self.x = x\n" +
        "    public func equals(self: ref/Self, other: ref/Self) -> bool => self.x == other.x\n" +
        "struct Q\n    public var v: i32\n    public init(v: i32) => self.v = v\n" +
        "func g(v: ref/i32) -> i32 => 0\nfunc h(v: i32) -> i32 => v\n";

    private const string LayerMain = "public func main()\n    Console.writeLine(\"p\")\n";

    // SPEC 3.4.3, 13.5.5: a Field read, a receiver call, a shared-reference argument, arithmetic, comparisons, a `for`, a `match` and a
    // range key through n layers. Each walk's work grows with the layers: Binding's layer walks and the component walks of the fits
    // (TermWalkWork), the layers HIR publishes (HirPlace.Layers: one step of its loop each) and the MIR builder's layer walks
    // (MirProgram.LayerWalkWork: their steps, a targeted walk's agreement tests and its premising), the counted walk (the Field read, the
    // `for` Subject, the `match` Subject), the targeted walk (the receiver's lent referent, the argument's referent, the terminals `==`
    // compares on Point) and the walk to the value (the range key, through ref layers in both rows). The sizes pass every count the walks had (8, 16, 32, 64) where the
    // proof cost of the named Origins allows.
    [Theory]
    [InlineData(false, 20)]
    [InlineData(true, 10)]
    public void LayerWalksAreLinearInTheLayers(bool pair, int size)
    {
        var work = new[] { size, 2 * size, 4 * size }.Select(n => Work(pair, n)).ToArray();
        foreach (var measure in new Func<(long Terms, long Layers, long Walks), long>[] { static x => x.Terms, static x => x.Layers, static x => x.Walks })
        {
            var steps = work.Select(measure).ToArray();
            Assert.True(steps[1] > steps[0] && MirBorrowsTest.Grows(steps, 2), string.Join(' ', work));
        }

        static (long Terms, long Layers, long Walks) Work(bool pair, int layers)
        {
            var layered = pair ? (Func<int, string, string>)PairLayers : (n, terminal) => RefLayers(n, terminal);
            var c = MirBuildTest.Compile(LayerTypes +
                $"func f{(pair ? "<s/T>" : string.Empty)}(x: {layered(layers, "Node")}, y: {layered(layers, "Array<i32>")}, z: {layered(layers, "i32")}, " +
                $"p: {layered(layers, "Point")}, o: {layered(layers, "Option<i32>")}, v: ref/Array<i32>, r: {RefLayers(layers, "Range<isize, End>")}{(pair ? ", m: s/T" : string.Empty)}) -> i32\n" +
                (pair ? "    s is value or valueborrow\n" : string.Empty) +
                "    var total: i32 = x.weight + x.load() + g(z) + (z + 1)\n    for e in y\n        total += e\n    let n = v[r].length\n    let less = z < z\n" +
                "    let same = p == p\n    match o\n        .Some(let w) => return w\n        .None => return if same => total else => total + 1\n" + LayerMain);
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
            var terms = c.Binding.TermWalkWork;
            var program = c.BuildMir();
            Assert.Empty(program.Findings);
            Assert.Empty(program.Records(MirRecordClass.Construction));
            return (terms, HirColumnsTest.Reachable(c.Kotonoha).Sum(static x => (long)HirTables.PlaceOf(x).Layers), program.LayerWalkWork);
        }
    }

    // PLAN G348, DECISIONS BU-134: the recursive walks of a Type's components recurse once per level, so at most H deep, inside the
    // compile stack's margin. D13<i32>.Item has height H = 8,192 (TypeHeightLimitTest's doubling declarations) and D12<i32>.Item half
    // of it, which a borrow or a Function Type of it stays under: an annotated local (AddTypeClauseObligations), calls that take one by
    // value and by reference (CollectCallRelations), a generic call that infers it and returns it (MentionsSlots, the fit's
    // FailedOriginRelation) and a function reference with it written (SameExceptOmittedOrigins, SolveOmittedOrigins, InputOrigins) are
    // judged with no stack failure, and the counted walks went the Type's height; one doubling higher is the height record at each Type
    // over the limit, and p's body, published beside its failed result, keeps its own generation limit record.
    [Fact]
    public void TheComponentWalksReachTheHeightLimitWithoutAStackFailure()
    {
        const string Uses = "func id<T>(v: T) -> T => v@move\nfunc take(v: {0}) -> {0} => v@move\nfunc near(v: ref/{1}) -> i32 => 0\n" +
            "func p(x: {0}, w: {1}) -> {0}\n    let y: {0} = x@move\n    let n = near(w@ref)\n    let h = id<{1}>\n    return id(take(y@move))\n";
        var c = MinimalEmissionTest.Analyze(TypeHeightLimitTest.Doubling(13) + string.Format(Uses, "D13<i32>.Item", "D12<i32>.Item") + LayerMain);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Binding.TermWalkWork >= 2 * 8192, $"{c.Binding.TermWalkWork}");

        var source = TypeHeightLimitTest.Doubling(14) + string.Format(Uses, "D14<i32>.Item", "D13<i32>.Item") + LayerMain;
        var over = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.NotEmpty(over);

        // p's body is published beside its failed result (a failed slot, SPEC 23.1.2), so its parameter w of D13<i32>.Item reaches the
        // generation limit, as p's w does within the limit; every other record is the height record.
        var generation = Assert.Single(over, static x => x.Code != nameof(DiagnosticCode.TypeHeightLimit_Kd));
        Assert.Equal(nameof(DiagnosticCode.GenerationResourceLimit_Kd), generation.Code);
        var at = source.IndexOf("func p(x: D14<i32>.Item, w: ", StringComparison.Ordinal) + "func p(x: D14<i32>.Item, w: ".Length;
        var span = Assert.NotNull(generation.Span);
        Assert.Equal(at, span.Start);
        Assert.Equal("D13<i32>.Item", source.Substring(span.Start, span.Length));
    }

    // SPEC 15.6.4, PLAN G408 (G408B-9 R1, R2): the fits asked at the positions a callee's output walk names (Holds, Borrows) walk the
    // storage's Type with the output walk's bound, which is none, entering each state once, so one call's contract is decided at any number
    // of levels up to H. make's result is a Tuple chain over `ref/i32 during q` (D_n's chains: a written one is past the syntax depth),
    // each level a position whose fit may walk down to the leaf. Holds keeps its answers for one output's positions (G408B-10), so each of
    // the call's two hold walks enters each level once: at most 2n + 4 steps (the call's TermWalkWork, AssertOneCallsWork) at n = 0, 1,024
    // and 8,190 levels (4, 2,052, 16,384; with the leaf's ref and i32, H = 8,192), with no stack failure. A fit walking the levels below
    // each position anew reads (n + 1)(n + 2) + 2, 67.1 million at 8,190.
    [Fact]
    public void TheHoldFitsOfOneCallAreLinearInTheLevels()
        => AssertOneCallsWork(
            static (levels, call) => TypeHeightLimitTest.Doubling(12) +
                $"func make(x: ref/i32 during q) -> {TypeHeightLimitTest.Chain(levels, "ref/i32 during q")} => $abort(\"x\")\n" +
                "func g()\n    var x = 1\n" + call + LayerMain,
            "    var d = make(x@ref)\n",
            2,
            4);

    // SPEC 15.6.4, PLAN G408 (G408B-11, G408B-12): the same chain where x's own Type, `uniq/(i64, i32) during q`, fits no level, so each
    // position's fit asks Borrows whether it is a borrow formed through x's layer: down to the leaf `ref/i32 during q`, whose referent i32
    // is a part (i64, i32) holds by value. Borrows keeps its answers as Holds does, so each output's two walks enter each level once (Holds'
    // no also the tuples' i32), with HoldsFrom's three entries in the referent and the two other entries: at most 4n + 14 steps (the call's,
    // AssertOneCallsWork) at n = 0, 1,024 and 8,190 (14, 4,110, 32,774). A Borrows walk anew at each position reads n^2 + 5n + 12
    // (67,117,062 at 8,190).
    [Fact]
    public void TheBorrowFitsOfOneCallAreLinearInTheLevels()
        => AssertOneCallsWork(
            static (levels, call) => TypeHeightLimitTest.Doubling(12) +
                $"func make(x: uniq/(i64, i32) during q) -> {TypeHeightLimitTest.Chain(levels, "ref/i32 during q")} => $abort(\"x\")\n" +
                "func g(v: (i64, i32))\n    var x = v@move\n" + call + LayerMain,
            "    var d = make(x@uniq)\n",
            4,
            14);

    // SPEC 15.6.4, PLAN G408 (G408B-17; design section 4's s1, s2): a call's retention asks once, over all the subjects its walks left past
    // their bounds, whether one can carry a Loan or an address (one MayCarry query, one visited set), so the question is linear in the Types
    // reachable from them. f's input d is an n-level Tuple chain past StorageBound and HandedBound, beside r, whose Loan the result may
    // hold: over i32 (s1) the query walks the n levels and answers no; over `ref/i32 during q` (s2) it answers yes at level n and the
    // call's contract is the Limit. At n = 64, 128, 256 and 512 the Binding's work (TermWalkWork: the hold walks; MayCarry has no counter)
    // grows linearly; the time, the query's measure, is printed.
    [Theory]
    [InlineData("i32")]
    [InlineData("ref/i32 during q")]
    public void ARetentionSubjectIsAskedOnceAcrossTheLevels(string leaf)
    {
        int[] levels = [64, 128, 256, 512];
        var (steps, times) = (new long[levels.Length], new long[levels.Length]);
        for (var i = 0; i < levels.Length; i++)
        {
            var chain = TypeHeightLimitTest.Chain(levels[i], leaf);
            (steps[i], times[i]) = RetentionWork(TypeHeightLimitTest.Doubling(9) +
                $"func make(x: ref/i32 during q) -> {chain} => $abort(\"x\")\nfunc f(d: {chain}, r: ref/i32 during q) -> ref/i32 during q => $abort(\"x\")\n" +
                "func g()\n    var x = 1\n    var d = make(x@ref)\n    var k = f(d@move, x@ref)\n" + LayerMain);
        }

        var text = $"leaf {leaf} n {string.Join(", ", levels)}: work {string.Join(", ", steps)}; ms {string.Join(", ", times)}";
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        Assert.True(MirBorrowsTest.Grows(steps[..3], 2.5) && MirBorrowsTest.Grows(steps[1..], 2.5), text);
    }

    // SPEC 15.6.4, PLAN G408 (G408B-17; design section 4's s3): a Handed frontier. f's input h refers to k structs S_j, each storing a
    // 33-level Tuple chain over its own `[j of i32]` (distinct Types, so no part is shared), whose last levels lie past HandedBound: the
    // walk publishes them as the call's subjects, and the one query walks each once. At k = 16, 32, 64 and 128 the Binding's work grows
    // linearly; the time is printed.
    [Fact]
    public void AHandedFrontierIsAskedOnceAcrossItsParts()
    {
        int[] counts = [16, 32, 64, 128];
        var (steps, times) = (new long[counts.Length], new long[counts.Length]);
        for (var i = 0; i < counts.Length; i++)
        {
            var structs = new System.Text.StringBuilder();
            for (var j = 1; j <= counts[i]; j++)
            {
                structs.Append($"struct S{j}\n    public var a: {string.Concat(Enumerable.Repeat("(i32, ", 33))}[{j} of i32]{new string(')', 33)}\n");
            }

            var tuple = "(" + string.Join(", ", Enumerable.Range(1, counts[i]).Select(static j => $"S{j}")) + ")";
            (steps[i], times[i]) = RetentionWork(structs.ToString() +
                $"func f(h: uniq/{tuple} during q, r: ref/i32 during q) -> ref/i32 during q => $abort(\"x\")\n" +
                $"func g(v: {tuple})\n    var x = 1\n    var t = v@move\n    var k = f(t@uniq, x@ref)\n" + LayerMain);
        }

        var text = $"k {string.Join(", ", counts)}: work {string.Join(", ", steps)}; ms {string.Join(", ", times)}";
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        Assert.True(MirBorrowsTest.Grows(steps[..3], 2.5) && MirBorrowsTest.Grows(steps[1..], 2.5), text);
    }

    // PLAN G348, R7-D3: a Closure's call signature is an edge BoundType.Height does not count (BU-71), so the walk of the enclosing
    // anonymous functions' atoms at each Closure's formation (FixedAtoms) does not descend it. In a chain of closures each taking the
    // previous (G348-d's `sclo`), the walks grow linearly in the links; descending each signature walked the whole chain at every link.
    [Fact]
    public void AClosureChainsFormationWalksAreLinearInTheLinks()
    {
        var steps = new[] { 100, 200, 400 }.Select(static n => Work(n)).ToArray();
        Assert.True(steps[1] > steps[0] && MirBorrowsTest.Grows(steps, 2), string.Join(' ', steps));

        static long Work(int links)
        {
            var source = new System.Text.StringBuilder("func pass<F, G>(f: ref/F, g: G) -> G\n    G is Callable<(ref/F) -> i32>\n    return g@move\n");
            source.Append("public func main()\n    let c0 = func [] () -> i32 => 1\n");
            for (var i = 1; i <= links; i++)
            {
                source.Append($"    let c{i} = pass(c{i - 1}@ref, func [] (x) -> i32 => 1)\n");
            }

            var c = MirBuildTest.Compile(source.ToString());
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
            return c.Binding.TermWalkWork;
        }
    }

    // SPEC 8.15.2, 15.6.3 (G254b, G254c): each use of `h@follow` whose owner case Reborrows a nested layer beside an invariant case plans
    // its owner case and walks its reach under that case, then is judged again at its selected input (each owner-case level counted), so
    // a body's work grows linearly in its uses; each use is one located limit beside its Resolved Place.
    [Fact]
    public void OwnerCaseReborrowsAreLinearInTheUses()
    {
        var steps = new[] { 128, 256, 512 }.Select(static n => Work(n)).ToArray();
        Assert.True(steps[1] > steps[0] && MirBorrowsTest.Grows(steps, 2), string.Join(' ', steps));

        static long Work(int uses)
        {
            var source = new System.Text.StringBuilder("func take<V>(x: V) -> () => ()\n");
            source.Append("func f<s/T, U>(m: s/T, h: uniq/(s/(uniq/U during b) during a) during c) -> ()\n    s is owner or uniq\n");
            for (var i = 0; i < uses; i++)
            {
                source.Append("    take(h@follow)\n");
            }

            var c = MirBuildTest.Compile(source.Append(LayerMain).ToString());
            Assert.Equal((0, 0, uses), (c.Binding.Result.UnresolvedCount, c.Binding.Result.InvalidCount, c.Binding.Result.LimitedCount));
            return c.Binding.TermWalkWork;
        }
    }

    // SPEC 15.4.2: a borrow layer nested in an input names its Origin, so each layer below the outermost one names a_k, the innermost one
    // `innermost` where given.
    internal static string RefLayers(int layers, string terminal, string? innermost = null)
    {
        var inner = terminal;
        for (var k = layers; k > 1; k--)
        {
            inner = $"ref/{Operand(inner)} during {(k == layers && innermost is not null ? innermost : "a" + k)}";
        }

        return "ref/" + Operand(inner);
    }

    // SPEC 13.5.5: n qualifying pair layers of one selector s (`s is value or valueborrow`).
    internal static string PairLayers(int layers, string terminal)
    {
        var inner = terminal;
        for (var k = layers; k > 1; k--)
        {
            inner = $"s/{Operand(inner)} during a{k}";
        }

        return "s/" + Operand(inner);
    }

    // One operation on x through `layers` ref or qualifying pair layers, valid or its invalid twin, whose terminal lacks the operation.
    // The forms and their SPEC rules: a Field read and a receiver below the layers (3.4.3, 7.3; Q has neither); a `for` Subject (14.6.2,
    // 15.1.6; i32 is not iterable); built-in arithmetic and comparison of the terminals (3.5.3, 13.1, 13.4; bool is not numeric or
    // ordered, Q not Equatable); a Case Pattern (14.8.1; i32 has no Case); the shared-reference row at an argument (10.2: layers ending
    // in i32 give one ref/i32; bool does not); a string comparison through safe reference layers (13.4; i32 is no string); a member
    // Borrow below the layers (15.6.3, PLAN G813: the containment walk meets every layer, one recursion each; Q has no Field).
    internal static string LayeredForm(string form, bool pair, int layers, bool valid)
    {
        var (good, bad, result, body) = form switch
        {
            "field" => ("Node", "Q", "i32", "    return x.weight\n"),
            "recv" => ("Node", "Q", "i32", "    return x.load()\n"),
            "borrow" => ("Node", "Q", "i32", "    let b = x.weight@ref\n    return b + 0\n"),
            "for" => ("Array<i32>", "i32", "i32", "    var total: i32 = 0\n    for v in x\n        total += v\n    return total\n"),
            "arith" => ("i32", "bool", "i32", "    return x + 1\n"),
            "match" => ("Option<i32>", "i32", "i32", "    match x\n        .Some(let v) => return v\n        .None => return 0\n"),
            "eq" => ("Point", "Q", "bool", "    return x == x\n"),
            "lt" => ("i32", "bool", "bool", "    return x < x\n"),
            "argref" => ("i32", "bool", "i32", "    return g(x)\n"),
            "let" => ("i32", "bool", "i32", "    let v: i32 = x\n    return v\n"),
            "argval" => ("i32", "bool", "i32", "    return h(x)\n"),
            "streq" => ("string", "i32", "bool", "    return x == \"a\"\n"),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };
        var terminal = valid ? good : bad;
        return LayerTypes + (pair ? $"func f<s/T>(x: {PairLayers(layers, terminal)}, m: s/T) -> {result}\n    s is value or valueborrow\n"
            : $"func f(x: {RefLayers(layers, terminal)}) -> {result}\n") + body + LayerMain;
    }

    // The acceptance rows' one assertion: the valid form has no record, and its invalid twin the records it has through 2 layers.
    internal static void AssertLayeredTwins(string form, bool pair, int layers)
    {
        Assert.Equal("clean", DiagnosticCorpus.RecordProfile(LayeredForm(form, pair, layers, true), "Application"));
        var shallow = DiagnosticCorpus.RecordProfile(LayeredForm(form, pair, 2, false), "Application");
        Assert.NotEqual("clean", shallow);
        Assert.Equal(shallow, DiagnosticCorpus.RecordProfile(LayeredForm(form, pair, layers, false), "Application"));
    }

    private static string Operand(string type) => type.Contains('/') || type.Contains(' ') ? $"({type})" : type;

    // The Binding's work (TermWalkWork) and the milliseconds of one compilation and its analysis, which records no internal failure.
    private static (long Work, long Ms) RetentionWork(string source)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var c = MirBuildTest.Compile(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        c.Analyze();
        Assert.DoesNotContain(c.Problems, static x => x.Code == DiagnosticCode.InternalInvariant_Kd);
        return (c.Binding.TermWalkWork, watch.ElapsedMilliseconds);
    }

    // PLAN G408 (G408B-21): one call's steps are its program's TermWalkWork less its twin's, the same program without the call's
    // statement, at n = 0, 1,024 and 8,190 levels. The twin holds the rest of the run, which is not the call's: the prelude's
    // declarations, among them the declaration judge's clause walk at each written Type's root (Q-823: 6 + 12 x 10 entries over
    // Doubling(12)). The twin's work is equal at every n, so no work that grows with n hides in it; the call's grows from n = 0 to 1,024
    // and is at most perLevel * n + fixedSteps.
    private static void AssertOneCallsWork(Func<int, string, string> program, string call, long perLevel, long fixedSteps)
    {
        int[] levels = [0, 1024, 8190];
        var twins = levels.Select(n => RetentionWork(program(n, string.Empty)).Work).ToArray();
        var steps = levels.Select((n, i) => RetentionWork(program(n, call)).Work - twins[i]).ToArray();
        var text = $"{string.Join(' ', steps)}; twin {string.Join(' ', twins)}";
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        Assert.True(twins.All(x => x == twins[0]), text);
        Assert.True(steps[1] > steps[0] && steps.Zip(levels).All(x => x.First <= (perLevel * x.Second) + fixedSteps), text);
    }
}
