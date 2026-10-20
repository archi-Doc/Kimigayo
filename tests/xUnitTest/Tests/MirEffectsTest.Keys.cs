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

    // R4-M91 (R4-M38's Open, R4-M74 OQ6): a site's classes stay apart, so A5 can tell an unknown effect (a call through a common Function
    // value or its destruction) from a limit (a call through an input with several Callable signatures) and from a dependent of an error
    // (a callee with an Opaque site), whose own keys stay. A plain write and read are their Field's two bits; a static that is not
    // mutable has no number.
    [Fact]
    public void KeysKeepTheClassesApart()
    {
        var c = MirBuildTest.Compile(
            "group G\n    public var x: i32 = 0\n    public var y: i32 = 0\n    public let z: i32 = 0\nfunc writeX() => G.x = 1\n" +
            "func readY() -> i32 => G.y\nfunc readZ() -> i32 => G.z\nfunc opaque()\n    _ = missing\n    G.y = 2\n" +
            "func classes<F>(action: () -> (), f: ref/F)\n    F is Callable<() -> ()>\n        effect confined\n    F is Callable<(i32) -> ()>\n" +
            "        effect confined\n    action()\n    f()\n    opaque()\n    writeX()\n    _ = readY()\n    _ = readZ()\n");
        var (effects, flow, program) = Analyzed(c);
        Assert.Equal(
            ["value: unknown", "value: limit", "opaque:y! derived", "writeX:x!", "readY:y", "readZ:", "drop: unknown"],
            Sites(effects, flow, program, "classes"));
        var statics = Statics(program);
        Assert.Equal(-1, effects.Field(statics["z"]));
        Assert.Equal([0, 1], new[] { effects.Field(statics["x"]), effects.Field(statics["y"]) }.Order());
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
    // as A5 reads them, allocates nothing once warm.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void KeyQueriesAllocateNothing()
    {
        var (effects, flow, program) = Analyzed(MirBuildTest.Compile(StaticChain(64)));
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
                            _ = effects.Keys(body, view, block, out _);
                        }
                    }
                }
            }
        }));
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
        var program = c.BuildMir();
        var (cases, report) = (new MirCases(c.Types), new MirReport());
        var flow = new MirDataflow(new MirReach());
        var effects = new MirEffects(c.Types, cases, flow);
        Analyze(effects, flow, cases, program, report);
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
    // number: the callee's name ("value" for a callable value, "drop"), the Fields in order, a written one marked !, then its classes.
    private static List<string> Sites(MirEffects effects, MirDataflow flow, MirProgram program, string function, long view = 0)
    {
        var body = program.Bodies.ToArray().Single(x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == function);
        var statics = Statics(program);
        flow.Begin(body);
        var sites = new List<string>();
        for (var block = 0; block < body.Blocks.Length; block++)
        {
            var terminator = body.Blocks[block].Terminator;
            if (terminator.Kind == MirTerminatorKind.Drop || terminator is { Kind: MirTerminatorKind.Call, Callee.Kind: not MirCalleeKind.Intrinsic })
            {
                var row = effects.Keys(body, view, block, out var classes);
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
