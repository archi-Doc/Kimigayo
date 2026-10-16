// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>The dataflow's own contract (docs/dev/DECISIONS.md R4-M20, R4-M34) through a backward problem, the liveness of whole locals, which
/// <see cref="MirDataflow.Backward"/> solves and <see cref="MirDataflow.ReplayBackward"/> reads.</summary>
public sealed class MirDataflowTest
{
    // R4-M34: over every body of the MIR goldens' sources, each block's entry state after the backward solve is the least fixed point of
    // the blocks themselves, a P1 block joining its runtime successors' states and any other block every successor's (SPEC 14.9, 14.10.3).
    [Fact]
    public void BackwardSolvesTheLeastFixedPoint()
    {
        var bodies = 0;
        foreach (var path in Directory.GetFiles(DiagnosticCorpus.RepositoryPath("tests", "mir"), "*.kimi"))
        {
            foreach (var body in MirBuildTest.Compile(File.ReadAllText(path)).BuildMir().Bodies)
            {
                var (_, entries) = Solve(body);
                Assert.True(entries.SequenceEqual(FixedPoint(body)), $"{Path.GetFileName(path)}: {body.Key}");
                bodies++;
            }
        }

        Assert.True(bodies > 100, $"{bodies} bodies");
    }

    // SPEC 14.10.3, 14.9: a use only the checks reach keeps nothing live on a runtime path. x, read after a return, is live in that check-only
    // code but not at the entry; read after a conditional return, it is live at the entry.
    [Fact]
    public void CheckOnlyUsesKeepNothingLiveOnRuntimePaths()
    {
        var program = MirBuildTest.Compile("func use(n: i32) => ()\nfunc after(x: i32) -> i32\n    return 1\n    use(x)\n    return 2\n" +
            "func branch(x: i32, flag: bool) -> i32\n    if flag => return 1\n    use(x)\n    return 2\n").BuildMir();
        foreach (var (name, entry) in new[] { ("after", false), ("branch", true) })
        {
            var body = program.Bodies.ToArray().Single(x => x.Key.Decl?.Name == name);
            var (flow, entries) = Solve(body);
            var x = body.Locals.ToArray().ToList().FindIndex(static local => local.Name == "x");
            var words = Words(body);
            Assert.Equal(entry, Live(entries, words, 0, x));
            Assert.Contains(Enumerable.Range(0, body.Blocks.Length), b => !flow.Runtime(b) && Live(entries, words, b, x));
        }
    }

    private static int Words(MirBody body) => (body.Locals.Length + 63) >> 6;

    private static bool Live(ulong[] entries, int words, int block, int local) => (entries[(block * words) + (local >> 6)] & (1UL << local)) != 0;

    // A body's liveness by the dataflow: each block's entry state, recorded as ReplayBackward applies it.
    private static (MirDataflow Flow, ulong[] Entries) Solve(MirBody body)
    {
        var flow = new MirDataflow(new MirReach());
        flow.Begin(body);
        var words = Words(body);
        var live = new Liveness(body, flow.Prepare(words, 0, words)!, new ulong[body.Blocks.Length * words]);
        flow.Backward(ref live);
        flow.ReplayBackward(ref live);
        return (flow, live.Entries);
    }

    // The same liveness as a fixed point of every block, iterated over all blocks until no entry state grows, a block's phase from its own
    // runtime walk.
    private static ulong[] FixedPoint(MirBody body)
    {
        var words = Words(body);
        var runtime = new MirReach();
        runtime.Clear(body.Blocks.Length);
        runtime.Walk(body, 0, checks: false);
        var (state, entries) = (new ulong[words], new ulong[body.Blocks.Length * words]);
        var live = new Liveness(body, state, new ulong[entries.Length]);
        for (var grew = true; grew;)
        {
            grew = false;
            for (var b = body.Blocks.Length - 1; b >= 0; b--)
            {
                live.Entry();
                var terminator = body.Blocks[b].Terminator;
                for (var i = runtime[b] ? 0 : -1; i < MirUses.Successors(terminator); i++)
                {
                    if (MirUses.Successor(body, terminator, i) is >= 0 and var successor)
                    {
                        MirDataflow.Union(state, entries.AsSpan(successor * words, words));
                    }
                }

                live.Block(b);
                grew |= MirDataflow.Union(entries.AsSpan(b * words, words), state);
            }
        }

        return entries;
    }

    // Whole locals live before each location: a whole definition or a storage marker kills its local, any other use of a Place uses its
    // root; the terminator, then the statements last first.
    private struct Liveness(MirBody body, ulong[] state, ulong[] entries) : IMirFlow, IMirUses
    {
        private readonly List<int> killed = [];
        private readonly List<int> used = [];

        internal readonly ulong[] Entries => entries;

        public readonly void Entry() => state.AsSpan(0, Words(body)).Clear();

        public void Block(int block)
        {
            var words = Words(body);
            for (var index = body.Blocks[block].Count; index >= 0; index--)
            {
                this.killed.Clear();
                this.used.Clear();
                MirUses.ForEach(body, new(block, index), ref this);
                foreach (var local in this.killed)
                {
                    state[local >> 6] &= ~(1UL << local);
                }

                foreach (var local in this.used)
                {
                    state[local >> 6] |= 1UL << local;
                }
            }

            state.AsSpan(0, words).CopyTo(entries.AsSpan(block * words, words));
        }

        public readonly bool Join(Span<ulong> target, ReadOnlySpan<ulong> incoming) => MirDataflow.Union(target, incoming);

        public readonly void Operand(in MirOperand operand)
        {
        }

        public readonly void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            if (place.IsStatic)
            {
                return;
            }

            var kills = place.Projection == 0 && (access & (MirAccess.Defines | MirAccess.Kills)) != 0;
            (kills ? this.killed : this.used).Add(place.Root);
        }

        public readonly void Effect(MirEffect effects, int site)
        {
        }
    }
}
