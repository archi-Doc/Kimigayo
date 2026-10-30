// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler.Mir;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The dataflow's own contract (docs/dev/DECISIONS.md R4-M20, R4-M34, R4-M158) through a backward problem, the liveness of whole
/// locals, which <see cref="MirDataflow.Backward"/> solves and <see cref="MirDataflow.ReplayBackward"/> reads.</summary>
public sealed class MirDataflowTest
{
    // R4-M158: over every body of the MIR goldens' sources, each block's entry state after the backward solve is the least fixed point of
    // the blocks themselves, a block joining the state of each successor its state flows into forward: a P1 block its runtime successors'
    // and a successor's only the checks reach, any other block its successors' only the checks reach (SPEC 14.10.3).
    [Fact]
    public void BackwardSolvesTheLeastFixedPoint()
    {
        var bodies = 0;
        foreach (var path in Directory.GetFiles(DiagnosticCorpus.RepositoryPath("tests", "mir"), "*.kimi"))
        {
            foreach (var body in MirDumpTest.Compile(Path.GetFileNameWithoutExtension(path)).BuildMir().Bodies)
            {
                var (_, entries) = Solve(body);
                Assert.True(entries.SequenceEqual(FixedPoint(body)), $"{Path.GetFileName(path)}: {body.Key}");
                bodies++;
            }
        }

        Assert.True(bodies > 100, $"{bodies} bodies");
    }

    // SPEC 14.10.3, 15.6 (PLAN G305, R4-M158): a Never form's continuation, which only the checks reach, continues from the form's state, so
    // x, read after a return, is live in that continuation and back across the return, at the entry. A P1 join takes no assumed state:
    // x, read after a conditional return, is live at the entry but in no block only the checks reach (the return's continuation flows
    // into the join).
    [Fact]
    public void ValuesStayLiveIntoACheckOnlyContinuation()
    {
        var program = MirBuildTest.Compile("func use(n: i32) => ()\nfunc after(x: i32) -> i32\n    return 1\n    use(x)\n    return 2\n" +
            "func branch(x: i32, flag: bool) -> i32\n    if flag => return 1\n    use(x)\n    return 2\n").BuildMir();
        foreach (var (name, checkOnly) in new[] { ("after", true), ("branch", false) })
        {
            var body = program.Bodies.ToArray().Single(x => x.Key.Decl?.Name == name);
            var (flow, entries) = Solve(body);
            var x = body.Locals.ToArray().ToList().FindIndex(static local => local.Name == "x");
            var words = Words(body);
            Assert.True(Live(entries, words, 0, x), name);
            Assert.Contains(Enumerable.Range(0, body.Blocks.Length), b => !flow.Runtime(b));
            Assert.Equal(checkOnly, Enumerable.Range(0, body.Blocks.Length).Any(b => !flow.Runtime(b) && Live(entries, words, b, x)));
        }
    }

    // G213 (DECISIONS R4-M132, R4-M15a): the one record of an arena over the analyses' byte limit is OwnershipStorageLimit_Kd at the body in
    // the views it stops, with its arguments as facts at the body: Required the arena's bytes, Limit the byte limit and Table the arena,
    // the catalog's requiredBytes, limitBytes and table; it returns the problem's index.
    [Fact]
    public void TheStorageLimitCarriesItsArguments()
    {
        var body = MirBuildTest.Compile("func f() => ()\n").BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f");
        var report = new MirReport();
        report.Add(new(DiagnosticCode.Unsupported_Kd, DiagnosticRequirement.Ownership(OwnershipRequirement.Unsupported), body.Source, body.Ordinal, 1));
        var words = (8L * 1024 * 1024) + 1;
        Assert.False(MirDataflow.Within(words));
        Assert.Equal(1, MirDataflow.StorageLimit(report, body, 0b11, MirStorageTable.Origins, words));
        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.StorageLimit);
        Assert.Equal(new MirProblem(DiagnosticCode.OwnershipStorageLimit_Kd, requirement, body.Source, body.Ordinal, 0b11), report.All[1]);
        Assert.Equal(
            [
                new MirFact(MirFactKind.Required, body.Source, words * 8), new(MirFactKind.Limit, body.Source, 64L * 1024 * 1024),
                new(MirFactKind.Table, body.Source, (long)MirStorageTable.Origins),
            ],
            report.Facts(1).ToArray());
        Assert.Equal("Origins over", Limit(report, 1));
    }

    // A storage limit's arguments as a test reads them: its arena and whether the arena's bytes exceed the byte limit.
    internal static string Limit(MirReport report, int problem)
    {
        var facts = report.Facts(problem).ToArray();
        var (required, limit) = (facts.Single(static x => x.Kind == MirFactKind.Required).Value, facts.Single(static x => x.Kind == MirFactKind.Limit).Value);
        return $"{(MirStorageTable)facts.Single(static x => x.Kind == MirFactKind.Table).Value} {(required > limit ? "over" : "within")}";
    }

    private static int Words(MirBody body) => (body.Locals.Length + 63) >> 6;

    private static bool Live(ulong[] entries, int words, int block, int local) => (entries[(block * words) + (local >> 6)] & (1UL << local)) != 0;

    // A body's liveness by the dataflow: each block's entry state, recorded as ReplayBackward applies it.
    private static (MirDataflow Flow, ulong[] Entries) Solve(MirBody body)
    {
        var flow = new MirDataflow(new MirReach());
        flow.Begin(body);
        var words = Words(body);
        var live = new Liveness(body, flow.Prepare(words, 0, words, out _)!, new ulong[body.Blocks.Length * words]);
        flow.Backward(ref live);
        flow.ReplayBackward(ref live);
        return (flow, live.Entries);
    }

    // The same liveness as a fixed point of every block, iterated over all blocks until no entry state grows, a block's phase from its own
    // runtime walk; a successor only the checks reach takes any edge, a runtime one only a P1 block's runtime edges.
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
                for (var i = -1; i < MirUses.Successors(terminator); i++)
                {
                    if (MirUses.Successor(body, terminator, i) is >= 0 and var successor && (!runtime[successor] || (runtime[b] && i >= 0)))
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
