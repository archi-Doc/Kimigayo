// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>A4's static keys for A5 (SPEC 15.6.4, 16.3.3, 22.4.4; docs/dev/DECISIONS.md R4-M38): what <c>MirEffects.Keys</c> gives a
/// Call or a Drop of a body view, from the summaries of the last run.</summary>
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
        var program = c.BuildMir();
        var (cases, report) = (new MirCases(c.Types), new MirReport());
        var flow = new MirDataflow(new MirReach());
        var effects = new MirEffects(c.Types, cases, flow);
        Analyze(effects, flow, cases, program, report);
        Assert.Equal(["writeX:x!", "readY:y", "both:x!,y", "plain:", "value:?", "init:", "drop:x!", "drop:?"], Sites(effects, flow, program, "caller"));
        Assert.Equal(["value:?"], Sites(effects, flow, program, "anyCall"));
        Assert.Equal(["value:"], Sites(effects, flow, program, "confinedCall"));
        Assert.Equal(["drop:?"], Sites(effects, flow, program, "absorb"));
        Assert.Equal(["init:", "absorb:x!", "drop:"], Sites(effects, flow, program, "absorbed"));
        Assert.Equal(["ping:x!,y"], Sites(effects, flow, program, "pinged"));
        Assert.Equal(["pong:x!,y"], Sites(effects, flow, program, "ping"));
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
            var (statics, members) = (new StringBuilder("group G\n"), new StringBuilder("group H\n"));
            for (var n = 0; n < sizes[i]; n++)
            {
                statics.Append(System.Globalization.CultureInfo.InvariantCulture, $"    public var v{n}: i32 = 0\n");
                var next = n + 1 == sizes[i] ? "()" : $"H.step{n + 1}()";
                members.Append(System.Globalization.CultureInfo.InvariantCulture, $"    public func step{n}()\n        G.v{n} = 1\n        {next}\n");
            }

            var c = MirBuildTest.Compile(statics.Append(members).Append("func chainHead() => H.step0()\n").ToString());
            var program = c.BuildMir();
            var (cases, report) = (new MirCases(c.Types), new MirReport());
            var flow = new MirDataflow(new MirReach());
            var effects = new MirEffects(c.Types, cases, flow);
            Analyze(effects, flow, cases, program, report);
            evaluations[i] = effects.Evaluations;
            var keys = Assert.Single(Sites(effects, flow, program, "chainHead"));
            Assert.Equal($"step0:{string.Join(",", Enumerable.Range(0, sizes[i]).Select(static n => $"v{n}!").Order(StringComparer.Ordinal))}", keys);
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: evaluations {string.Join(", ", evaluations)}");
        Assert.True(evaluations[2] - evaluations[1] <= 2 * (evaluations[1] - evaluations[0]), $"evaluations {string.Join(", ", evaluations)}");
    }

    // The keys of each Call (other than an intrinsic) and Drop of a function's body: the callee's name ("value" for a callable value,
    // "drop"), its keys in order, a written one marked !, and ? where it is unknown.
    private static List<string> Sites(MirEffects effects, MirDataflow flow, MirProgram program, string function)
    {
        var body = program.Bodies.ToArray().Single(x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == function);
        flow.Begin(body);
        var sites = new List<string>();
        for (var block = 0; block < body.Blocks.Length; block++)
        {
            var terminator = body.Blocks[block].Terminator;
            if (terminator.Kind == MirTerminatorKind.Drop || terminator is { Kind: MirTerminatorKind.Call, Callee.Kind: not MirCalleeKind.Intrinsic })
            {
                var keys = effects.Keys(body, 0, block, out var unknown).ToArray().Select(static x => x.Field.Name + (x.Write ? "!" : string.Empty));
                var name = terminator.Kind == MirTerminatorKind.Drop ? "drop" : terminator.Callee.Symbol?.Name ?? "value";
                sites.Add($"{name}:{string.Join(",", keys.Order(StringComparer.Ordinal))}{(unknown ? "?" : string.Empty)}");
            }
        }

        return sites;
    }
}
