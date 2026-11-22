// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>A4's static keys for A5 (SPEC 15.6.4, 16.3.3, 22.4.4; docs/dev/DECISIONS.md R4-M38, R4-M91): the key row and classes
/// <c>MirEffects.Keys</c> gives a Call or a Drop of a body view, from the summaries of the last run.</summary>
public sealed partial class MirEffectsTest
{
    // SPEC 15.6.4, 16.3.3, 22.4.4 (R4-M38, P5): the keys of a Call or a Drop are the mutable static Fields what it runs may access,
    // composed through its callees and destructors, each Field once and written where any access writes it; a call through a common
    // Function value or its destruction is unknown, and a site that runs nothing static has none. A call through an abstract Callable
    // input is unknown, unless its bound is confined; an abstract part's destruction is unknown in its generic body and resolved where
    // its caller's substitution makes it concrete, and the Drop of a moved value runs nothing. Mutually recursive callees share their
    // keys.
    [Fact]
    public void KeysAreTheStaticFieldsASiteRuns()
    {
        var c = MirBuildTest.Compile(
            "group G\n    public var x: i32 = 0\n    public var y: i32 = 0\nfunc writeX() => G.x = 1\nfunc readY() -> i32 => G.y\n" +
            "func both() -> i32\n    writeX()\n    return readY()\nfunc plain() -> i32 => 1\nstruct Noisy\n    public var n: i32 = 0\n" +
            "    drop => G.x = 2\nfunc caller(action: () -> ())\n    writeX()\n    _ = readY()\n    _ = both()\n    _ = plain()\n    action()\n" +
            "    let noisy = Noisy.init()\nfunc anyCall<F>(f: ref/F)\n    F is Callable<() -> ()>\n    f()\nfunc confinedCall<F>(f: ref/F)\n" +
            "    F is Callable<() -> ()>\n        effect confined\n    f()\nfunc absorb<T>(value: T) => ()\nfunc absorbed() => absorb(Noisy.init())\n" +
            "func ping(n: i32)\n    G.x = n\n    if n > 0 => pong(n - 1)\nfunc pong(n: i32)\n    _ = G.y\n    if n > 0 => ping(n - 1)\nfunc pinged() => ping(3)\n");
        var (effects, flow, program) = Analyzed(c);
        Assert.Equal(
            ["writeX:x!", "readY:y", "both:x!,y", "plain:", "value: unknown", "init:", "drop:x!", "drop: unknown"],
            Sites(effects, flow, program, "caller"));
        Assert.Equal(["value: unknown"], Sites(effects, flow, program, "anyCall"));
        Assert.Equal(["value:"], Sites(effects, flow, program, "confinedCall"));
        Assert.Equal(["drop: unknown"], Sites(effects, flow, program, "absorb"));
        Assert.Equal(["init:", "absorb:x!", "drop:"], Sites(effects, flow, program, "absorbed"));
        Assert.Equal(["ping:x!,y"], Sites(effects, flow, program, "pinged"));
        Assert.Equal(["pong:x!,y"], Sites(effects, flow, program, "ping"));
    }

    // SPEC 7.2.3, 8.6.3, 15.6.4 (PLAN G663): the site of a default's copy's requirement call on a closed Self has the keys its witness
    // runs at the call's own Types: none at i32, the static Bump's destructor writes at Bump (fail-before: each, no key and a limit).
    [Theory]
    [InlineData("1", "f:")]
    [InlineData("Bump.init()", "f:n!")]
    public void ADefaultsCopysRequirementCallHasItsWitnessesKeys(string argument, string expected)
    {
        var c = MirBuildTest.Compile(
            "group G\n    public var n: i32 = 0\nstruct Bump\n    public var k: i32 = 0\n    drop => G.n = 1\n" +
            "contract Gen\n    func f<T>(self: ref/Self, x: T) -> bool\nstruct S\n    Self is Gen\n" +
            "    public func f<T>(self: ref/Self, x: T) -> bool => true\n" +
            $"group D\n    public func d<C>(c: ref/C, seen: bool = c.f({argument})) -> i32\n        C is Gen\n        return 1\n" +
            "func use(s: ref/S) -> i32 => D.d(s)\n");
        var (effects, flow, program) = Analyzed(c);
        Assert.Contains(expected, Sites(effects, flow, program, "use"));
    }

    // R4-M91 (R4-M38's Open, R4-M74 OQ6): a site's classes stay apart, so A5 can tell an unknown effect (a call through a common Function
    // value or its destruction) from a limit (a chain growing through a destructor, GrowingChain) and from a dependent of an error (a
    // callee with an Opaque site), whose own keys stay; a call through an input with several Callable signatures has its selected one's
    // bound (Z3: the limit until then). A plain write and read are their Field's two bits; a static that is not mutable has no number.
    [Fact]
    public void KeysKeepTheClassesApart()
    {
        var c = MirBuildTest.Compile(
            GrowingChain + "group S\n    public var x: i32 = 0\n    public var y: i32 = 0\n    public let z: i32 = 0\nfunc writeX() => S.x = 1\n" +
            "func readY() -> i32 => S.y\nfunc readZ() -> i32 => S.z\nfunc opaque()\n    _ = missing\n    S.y = 2\n" +
            "func classes<F>(action: () -> (), f: ref/F)\n    F is Callable<() -> ()>\n        effect confined\n    F is Callable<(i32) -> ()>\n" +
            "        effect confined\n    action()\n    f()\n    _ = G<i32>.init()\n    opaque()\n    writeX()\n    _ = readY()\n    _ = readZ()\n");
        var (effects, flow, program) = Analyzed(c);
        Assert.Equal(
            ["value: unknown", "value:", "init:", "drop: limit", "opaque:y! derived", "writeX:x!", "readY:y", "readZ:", "drop: unknown"],
            Sites(effects, flow, program, "classes"));
        var statics = Statics(program);
        Assert.Equal(-1, effects.Field(statics["z"]));
        Assert.Equal([0, 1], new[] { effects.Field(statics["x"]), effects.Field(statics["y"]) }.Order());
    }

    // SPEC 15.6.4, 22.4.3, 22.4.4 (Z7, R4-M103): a staticEnsure is its static's first-access initialization. One of a static with one slot
    // is a summary entry its reader composes: the call of readY has the write of log that y's initializer makes. A static with several
    // slots keeps the edge into its initializer, so each call's substitution resolves the initializer's atoms: get keeps x's Drop(T),
    // and user's call makes it Drop(i32), which does nothing, so user's summary has no Unknown (fail-before an entry form for every
    // static: Unknown, the atom composed unsubstituted at the reader).
    [Fact]
    public void AStaticsInitializationComposesThroughItsSlots()
    {
        var c = MirBuildTest.Compile(
            "struct Counter\n    public var n: i32 = 0\n    public init() => ()\nstruct Cache<T>\n    public group S\n" +
            "        public let x: Counter = S.makeX()\n        public func makeX() -> Counter\n            let o: Option<T> = .None\n" +
            "            return Counter.init()\n        public func get() -> i32 => x.n\nfunc user() -> i32 => Cache<i32>.S.get()\n" +
            "group G\n    public var log: i32 = 0\n    public let y: i32 = G.makeY()\n    public func makeY() -> i32\n        G.log = 1\n" +
            "        return 2\nfunc readY() -> i32 => G.y\nfunc caller() -> i32 => readY()\n");
        var (effects, flow, program) = Analyzed(c);
        Assert.Equal(["readY:log!"], Sites(effects, flow, program, "caller"));
        var get = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "get");
        Assert.Contains(effects.Items(get, 0, out _).ToArray(), static x => x.Kind == MirEffects.Drop && x.Type.Name == "T");
        var user = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "user");
        Assert.Empty(effects.Items(user, 0, out var classes).ToArray());
        Assert.Equal(0, classes & MirEffects.Unknown);
        var readY = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "readY");
        _ = effects.Items(readY, 0, out var initialized);
        Assert.NotEqual(0, initialized); // N1: its summary has the write y's initializer makes, readY's own none.
    }

    // SPEC 15.6.4, 22.4.3 (Z7 Revision 4, the review's D1 and N3): a query that leaves a static's initialization out reads the same read
    // graph as the row with every initialization, without that entry's edges only, so its row keeps every body the site runs: a
    // destructor its callee runs (dropsW), a Function Item its callee calls (callsItem), the witness its callee's inline default copy runs
    // (pings: a requirement call on a closed Self, SPEC 7.2.3; the requirement has no default body; on an abstract Self it would be
    // Unknown), a body both the site and x's initializer call (both: s), and a callee that y's initializer
    // calls back (cyc, a cycle through y's entry); only what runs through x's or y's initialization is left out (dropsX's destructor's
    // ensure of x, makeX's k, makeY's ky). Fail-before (090b599bb, Reach over the local calls and entries): dropsW, callsItem and pings lose
    // log's write.
    [Fact]
    public void AnExcludedRowHoldsWhatEachBodyRuns()
    {
        var c = MirBuildTest.Compile("""
            group G
                public var log: i32 = 0
                public var k: i32 = 0
                public var s: i32 = 0
                public var ky: i32 = 0
                public var cy: i32 = 0
                public let x: i32 = G.makeX()
                public let y: i32 = G.makeY()
                public func makeX() -> i32
                    G.k = 1
                    H.shared()
                    return 2
                public func makeY() -> i32
                    G.ky = 1
                    return H.cyc()
            struct HW
                public var n: i32 = 0
                drop => G.log = 1
            struct XR
                public var n: i32 = 0
                drop => _ = G.x
            contract Pinged
                func ping(self: ref/Self) -> bool
            struct Noisy
                Self is Pinged
                public var n: i32 = 0
                public func ping(self: ref/Self) -> bool
                    G.log = 4
                    return true
            group H
                public func writesLog() -> i32
                    G.log = 3
                    return 3
                public func shared() => G.s = 1
                public func cyc() -> i32
                    G.cy = 1
                    return G.y
                public func pingAll<C>(c: ref/C, seen: bool = c.ping()) -> i32
                    C is Pinged
                    return 1
            func dropsW() => _ = HW.init()
            func callsItem() -> i32
                let f = H.writesLog
                return f()
            func pings() -> i32
                let n = Noisy.init()
                return H.pingAll(n@ref)
            func dropsX() => _ = XR.init()
            func both() -> i32
                H.shared()
                return G.x
            func user()
                dropsW()
                _ = callsItem()
                _ = pings()
                dropsX()
                _ = both()
                _ = H.cyc()

            """);
        var (effects, flow, program) = Analyzed(c);
        Assert.Equal(
            ["dropsW:log!", "callsItem:log!", "pings:log!", "dropsX:k!,s!", "both:k!,s!", "cyc:cy!,ky!"], Sites(effects, flow, program, "user"));
        Assert.Equal(
            ["dropsW:log!", "callsItem:log!", "pings:log!", "dropsX:", "both:s!", "cyc:cy!"], Sites(effects, flow, program, "user", excluded: ["x", "y"]));
    }

    // V16 (Z7 Revision 4, D3): a query that leaves an initialization out walks each node it reaches once, so N sites each reaching a chain
    // of D members, whose tail ensures v (its initializer is one node more) and z (left out), walk N × (D + 1) nodes, linear in N and in D
    // (the guard per site; the total over K conflicting locations is an open issue).
    [Fact]
    public void AnExcludedQueryWalksItsReachOnce()
    {
        foreach (var (n, d) in new[] { (8, 16), (16, 16), (16, 32) })
        {
            var text = new StringBuilder("group G\n    public var v: i32 = 0\n    public let z: i32 = 0\ngroup H\n");
            for (var i = 0; i + 1 < d; i++)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"    public func c{i}() => H.c{i + 1}()\n");
            }

            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"    public func c{d - 1}()\n        G.v = 1\n        _ = G.z\nfunc sites()\n");
            var (effects, flow, program) = Analyzed(MirBuildTest.Compile(text.Append(string.Concat(Enumerable.Repeat("    H.c0()\n", n))).ToString()));
            Assert.Equal(Enumerable.Repeat("c0:v!", n), Sites(effects, flow, program, "sites", excluded: ["z"]));
            Assert.Equal(n * (d + 1), effects.Walked);
        }
    }

    // R4-M11, R4-M91: a query answers the view it is given: each view of a caller selects the callee view its substitution names (SPEC
    // 8.15), so the owner case destroys the Noisy value its callee takes and the reference case does not.
    [Fact]
    public void KeysAreQueriedPerView()
    {
        var c = MirBuildTest.Compile(
            "group G\n    public var x: i32 = 0\nstruct Noisy\n    public var n: i32 = 0\n    drop => G.x = 2\nfunc hold<s/T>(value: s/T)\n" +
            "    s is owner or ref\n    ()\nfunc forward<s/T>(value: s/Noisy, marker: s/T)\n    s is owner or ref\n    hold(value@move)\n");
        var (effects, flow, program) = Analyzed(c);
        Assert.Equal(["hold:x!", "drop:", "drop: unknown", "drop:"], Sites(effects, flow, program, "forward"));
        Assert.Equal(["hold:", "drop:", "drop:", "drop:"], Sites(effects, flow, program, "forward", view: 1));
    }

    // V16, R4-M38: keys join once per component of the read graph after the fixed point, so a chain whose callers come first, each
    // writing its own static, keeps its linear evaluations, and the head's call has every member's key.
    [Fact]
    public void KeysKeepTheEvaluationsLinear()
    {
        int[] sizes = [64, 128, 256];
        var evaluations = new int[sizes.Length];
        for (var i = 0; i < sizes.Length; i++)
        {
            var (effects, flow, program) = Analyzed(MirBuildTest.Compile(StaticChain(sizes[i])));
            evaluations[i] = effects.Evaluations;
            var keys = Assert.Single(Sites(effects, flow, program, "chainHead"));
            Assert.Equal($"step0:{string.Join(",", Enumerable.Range(0, sizes[i]).Select(static n => $"v{n}!").Order(StringComparer.Ordinal))}", keys);
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: evaluations {string.Join(", ", evaluations)}");
        Assert.True(evaluations[2] - evaluations[1] <= 2 * (evaluations[1] - evaluations[0]), $"evaluations {string.Join(", ", evaluations)}");
    }

    // R4-M91: a query writes A4's pooled row and reads the last run's storage, so querying every Call and Drop of every view A2 completed,
    // as A5 reads them, allocates nothing once warm, also with an initialization left out (Z7: the read graph's walk, its stamp and stack).
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeyQueriesAllocateNothing(bool excluding)
    {
        var (effects, flow, program) = Analyzed(MirBuildTest.Compile(StaticChain(64)));
        ulong[] excluded = excluding ? [1UL] : [];
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            foreach (var body in program.Bodies)
            {
                flow.Begin(body);
                for (var view = 0L; view < program.Drops.ViewsOf(body.Ordinal); view++)
                {
                    for (var block = 0; block < body.Blocks.Length; block++)
                    {
                        if (body.Blocks[block].Terminator.Kind is MirTerminatorKind.Call or MirTerminatorKind.Drop)
                        {
                            _ = effects.Keys(body, view, block, excluded, out _);
                        }
                    }
                }
            }
        }));
        Assert.Equal(excluding, effects.Walked > 0);
    }

    // A chain of N members of group H, each writing its own static of group G and calling the next, its head called by chainHead.
    private static string StaticChain(int size)
    {
        var (statics, members) = (new StringBuilder("group G\n"), new StringBuilder("group H\n"));
        for (var n = 0; n < size; n++)
        {
            statics.Append(System.Globalization.CultureInfo.InvariantCulture, $"    public var v{n}: i32 = 0\n");
            var next = n + 1 == size ? "()" : $"H.step{n + 1}()";
            members.Append(System.Globalization.CultureInfo.InvariantCulture, $"    public func step{n}()\n        G.v{n} = 1\n        {next}\n");
        }

        return statics.Append(members).Append("func chainHead() => H.step0()\n").ToString();
    }

    // A fresh A4 over a compilation's program after the driver's A4 rows (Analyze).
    private static (MirEffects Effects, MirDataflow Flow, MirProgram Program) Analyzed(Compilation c)
    {
        var program = MirBuildTest.LibraryRange(c); // The library's bodies too (PLAN G951 U2-T).
        var (cases, report) = (new MirCases(c.Types), new MirReport());
        var flow = new MirDataflow(new MirReach(), cases);
        var effects = new MirEffects(c.Types, cases, flow);
        Analyze(effects, flow, cases, program, report, All);
        return (effects, flow, program);
    }

    // The statics the program's bodies access, by name.
    private static SortedDictionary<string, BindingSymbol> Statics(MirProgram program)
    {
        var statics = new SortedDictionary<string, BindingSymbol>(StringComparer.Ordinal);
        foreach (var body in program.Bodies)
        {
            foreach (var item in body.Statics)
            {
                statics[item.Symbol.Name] = item.Symbol;
            }
        }

        return statics;
    }

    // The key row of each Call (other than an intrinsic) and Drop of a function's body in a view, read through each static's Field
    // number, with the initializations of the excluded statics left out: the callee's name ("value" for a callable value, "drop"), the
    // Fields in order, a written one marked !, then its classes.
    private static List<string> Sites(MirEffects effects, MirDataflow flow, MirProgram program, string function, long view = 0, string[]? excluded = null)
    {
        var body = program.Bodies.ToArray().Single(x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == function);
        var statics = Statics(program);
        var ids = (excluded ?? []).Select(x => effects.Initialization(statics[x])).ToArray();
        Assert.All(ids, static x => Assert.True(x >= 0));
        var exclusion = new ulong[ids.Length == 0 ? 0 : (ids.Max() >> 6) + 1];
        foreach (var id in ids)
        {
            exclusion[id >> 6] |= 1UL << id;
        }

        flow.Begin(body);
        var sites = new List<string>();
        for (var block = 0; block < body.Blocks.Length; block++)
        {
            var terminator = body.Blocks[block].Terminator;
            if (terminator.Kind == MirTerminatorKind.Drop || terminator is { Kind: MirTerminatorKind.Call, Callee.Kind: not MirCalleeKind.Intrinsic })
            {
                var row = effects.Keys(body, view, block, exclusion, out var classes);
                var keys = new List<string>();
                foreach (var (name, symbol) in statics)
                {
                    var bit = 2 * effects.Field(symbol);
                    if (bit >= 0 && (Bit(row, bit + 1) || Bit(row, bit)))
                    {
                        keys.Add(Bit(row, bit + 1) ? name + "!" : name);
                    }
                }

                var site = terminator.Kind == MirTerminatorKind.Drop ? "drop" : terminator.Callee.Symbol?.Name ?? "value";
                sites.Add($"{site}:{string.Join(",", keys)}{((classes & MirEffects.Unknown) != 0 ? " unknown" : string.Empty)}" +
                    $"{((classes & MirEffects.Limit) != 0 ? " limit" : string.Empty)}{((classes & MirEffects.Derived) != 0 ? " derived" : string.Empty)}");
            }
        }

        return sites;

        static bool Bit(ReadOnlySpan<ulong> row, int bit) => (row[bit >> 6] & (1UL << bit)) != 0;
    }
}
