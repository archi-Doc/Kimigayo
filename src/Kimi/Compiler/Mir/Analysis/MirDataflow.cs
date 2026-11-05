// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the bit-set dataflow of one MIR body, forward and backward (plan 5.6.3; DECISIONS R4-M20, R4-M15a, R4-M34, R4-M65, R4-M158).
// Input: a validated body (Begin) and a transfer, a struct IMirFlow that interprets blocks on the scratch state and joins states. Output: the
// blocks in reverse postorder, each block's phase, the converged state at each flow head, and Replay and ReplayBackward, which give each
// chain its state once.
// Blocks form chains: a block whose only edge is a runtime edge into a block that has no other predecessor and is not the entry continues
// into it, so a chain shares one stored state, held only at its head. Begin decides each chain's phase: P1 where the runtime edges from the
// entry reach it, else P2 (SPEC 14.10.3). One edge rule serves both directions (Flows): a state flows along any edge into a P2 head and
// along a P1 chain's runtime edges into a P1 head, never otherwise into a P1 head. Forward: P1 follows runtime edges from the entry; P2
// the edges into P2 heads, P1's states frozen. Backward is its transpose: a chain joins the states of the successors its state flows into,
// from the boundary state (R4-M158). The transfer joins (A1: union). A solve's worklist holds every head at first and takes the pending head
// first in its direction's order, reverse postorder forward and postorder backward, so growth along a loop's back edge settles the loop
// before the code after it is applied again (R4-M65). States are word-major (the lanes of 64 paths per word group), and only a problem's
// first words are stored; the scratch holds the rest. One arena stores both problems' words; its byte size is the one Resource limit of the
// analyses (OwnershipStorageLimit_Kd), which StorageLimit records for every arena with its arguments as facts (G213, DECISIONS R4-M132).
// Storage is pooled and reset by counts.
// Must not interpret statements, read syntax or HIR, merge a P2 state into a P1 head, or walk all edges in one pass.

#pragma warning disable SA1402 // The transfer's interface and the arenas of the limit belong to the dataflow.

using System.Numerics;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

/// <summary>An analysis arena the analyses' byte limit admits (R4-M15a, 2a0-D D20), the Table fact of its OwnershipStorageLimit_Kd, which
/// publication renders as the catalog's <c>table</c> (DECISIONS R4-M132).</summary>
internal enum MirStorageTable : byte
{
    Initialization, // A1's states (MirInit).
    Loans, // A5's Loan stratum's states (MirBorrows).
    Origins, // A5's Origin stratum's source bitsets (MirOrigins).
}

/// <summary>A problem of <see cref="MirDataflow"/>: its boundary state, how a block changes a state and how two states join, all on the
/// scratch that <see cref="MirDataflow.Prepare"/> returned. A backward problem's least state is zero.</summary>
internal interface IMirFlow
{
    /// <summary>Writes the boundary state into the scratch: a forward problem's state at the body's entry; a backward problem's after
    /// every chain's last block, which its successors' states then join, so it is the least state and a fact of the exit belongs to the
    /// Return terminator's Block.</summary>
    void Entry();

    /// <summary>Applies a block to the scratch: its statements, then its terminator; a backward problem's terminator, then its statements
    /// last first.</summary>
    /// <param name="block">The block.</param>
    void Block(int block);

    /// <summary>Joins a state into another: a head's stored state, or the scratch.</summary>
    /// <param name="target">The state joined into.</param>
    /// <param name="incoming">The state joined.</param>
    /// <returns>Whether the state grew.</returns>
    bool Join(Span<ulong> target, ReadOnlySpan<ulong> incoming);
}

/// <summary>The bit-set dataflow of the MIR analyses (plan 5.6.3, DECISIONS R4-M20): <see cref="Begin"/> orders a body's blocks and decides
/// their phases once, <see cref="Prepare"/> sizes the states of a backward and a forward problem, <see cref="Forward"/> solves a forward one
/// in the two phases of SPEC 14.10.3 and <see cref="Backward"/> a backward one, and <see cref="Replay"/> and <see cref="ReplayBackward"/>
/// give each chain its converged state, where the checks run.</summary>
/// <param name="reach">The one reachability walk, whose post-order over every edge orders the blocks and whose runtime walk decides P1.</param>
internal sealed class MirDataflow(MirReach reach)
{
    // R4-M15a: the byte size of an analysis arena above which a body is not analyzed (OwnershipStorageLimit_Kd), the old path's limit
    // value until R6 states it in IMPL 21.3.2.
    private const long ByteLimit = 64L * 1024 * 1024;
    private int[] order = []; // The blocks in reverse postorder.
    private int[] next = []; // Each block's successor in its chain, or -1 at the chain's tail.
    private int[] previous = []; // Each block's predecessor in its chain, or -1 at its head.
    private int[] head = []; // Each block's flow head.
    private int[] heads = []; // Each head's block, heads numbered in reverse postorder.
    private int[] tails = []; // Each head's chain's last block.
    private int[] starts = []; // Each block's first predecessor edge in predecessors, and the end after the last block's.
    private int[] predecessors = []; // The source block of each edge, grouped by its target block.
    private byte[] phases = []; // Each head's phase: 1 where the runtime edges from the entry reach it, else 2.
    private bool[] reached = []; // Whether the forward solve reached each head in its phase.
    private ulong[] pending = []; // The worklist: a bit per pending head at its place in the solve's order (Next).
    private ulong[] arena = []; // The stored states of each head: the backward problem's words, then the forward problem's.
    private ulong[] scratch = [];
    private MirBody body = null!;
    private int headCount;
    private int low; // The first word of the worklist that may hold a bit.
    private int backward; // The words a head stores for the backward problem.
    private int forward; // The words a head stores for the forward problem.

    /// <summary>Gets the blocks of the body begun in reverse postorder over runtime and check edges, so a chain's blocks are in order.</summary>
    internal ReadOnlySpan<int> Order => this.order.AsSpan(0, this.body.Blocks.Length);

    // The words a head stores, the backward problem's first.
    private int Row => this.backward + this.forward;

    /// <summary>Sets and clears lanes of the paths [first, end) of a word-major state.</summary>
    /// <param name="state">The state.</param>
    /// <param name="lanes">The lanes per word group.</param>
    /// <param name="first">The first path.</param>
    /// <param name="end">The path after the last.</param>
    /// <param name="set">The lanes set, bit l for lane l.</param>
    /// <param name="clear">The lanes cleared.</param>
    internal static void Apply(ulong[] state, int lanes, int first, int end, int set, int clear)
    {
        for (var group = first >> 6; group << 6 < end; group++)
        {
            var mask = Mask(group, first, end);
            for (var lane = 0; lane < lanes; lane++)
            {
                if ((set & (1 << lane)) != 0)
                {
                    state[(group * lanes) + lane] |= mask;
                }
                else if ((clear & (1 << lane)) != 0)
                {
                    state[(group * lanes) + lane] &= ~mask;
                }
            }
        }
    }

    /// <summary>Gets whether a lane has a bit among the paths [first, end) of a word-major state.</summary>
    /// <param name="state">The state.</param>
    /// <param name="lanes">The lanes per word group.</param>
    /// <param name="lane">The lane.</param>
    /// <param name="first">The first path.</param>
    /// <param name="end">The path after the last.</param>
    /// <returns>Whether a bit is set.</returns>
    internal static bool Any(ulong[] state, int lanes, int lane, int first, int end)
    {
        for (var group = first >> 6; group << 6 < end; group++)
        {
            if ((state[(group * lanes) + lane] & Mask(group, first, end)) != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The union join of bit-set states.</summary>
    /// <param name="target">The state joined into.</param>
    /// <param name="incoming">The state joined.</param>
    /// <returns>Whether the state grew.</returns>
    internal static bool Union(Span<ulong> target, ReadOnlySpan<ulong> incoming)
    {
        var grew = 0UL;
        for (var i = 0; i < target.Length; i++)
        {
            grew |= incoming[i] & ~target[i];
            target[i] |= incoming[i];
        }

        return grew != 0;
    }

    /// <summary>Grows a pooled array of the analyses to hold a count, keeping its items; a warm body of the same size allocates nothing.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The array.</param>
    /// <param name="count">The count it must hold.</param>
    internal static void Grow<T>(ref T[] items, int count)
    {
        if (items.Length < count)
        {
            Array.Resize(ref items, Math.Max(count, items.Length * 2));
        }
    }

    /// <summary>Gets whether an analysis arena of a count of words is within the analyses' byte limit (R4-M15a), the one decision that
    /// <see cref="Prepare"/> and MirOrigins share (2a0-D D20); above it the body is not analyzed (OwnershipStorageLimit_Kd).</summary>
    /// <param name="words">The arena's words.</param>
    /// <returns>Whether the arena is within the limit.</returns>
    internal static bool Within(long words) => words <= ByteLimit / sizeof(ulong);

    /// <summary>Records the one problem of an arena over the analyses' byte limit (R4-M15a): OwnershipStorageLimit_Kd
    /// (Ownership.StorageLimit) at the body in the views it stops, with the facts Required (the arena's bytes), Limit (the byte limit) and
    /// Table (the arena), each at the body, the catalog's requiredBytes, limitBytes and table (G213, DECISIONS R4-M132).</summary>
    /// <param name="report">The program's problems.</param>
    /// <param name="body">The body.</param>
    /// <param name="views">The views the limit stops.</param>
    /// <param name="table">The arena.</param>
    /// <param name="words">The words the arena needs, which <see cref="Within"/> refused.</param>
    /// <returns>The problem's index in the report.</returns>
    internal static int StorageLimit(MirReport report, MirBody body, ulong views, MirStorageTable table, long words)
    {
        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.StorageLimit);
        ReadOnlySpan<MirFact> facts =
        [
            new(MirFactKind.Required, body.Source, words * sizeof(ulong)),
            new(MirFactKind.Limit, body.Source, ByteLimit),
            new(MirFactKind.Table, body.Source, (long)table),
        ];
        return report.Add(new(DiagnosticCode.OwnershipStorageLimit_Kd, requirement, body.Source, body.Ordinal, views), facts);
    }

    /// <summary>Gets whether the runtime edges from the entry reach a block (P1); every other block only the checks reach (P2).</summary>
    /// <param name="block">A block of the body.</param>
    /// <returns>Whether it is in P1.</returns>
    internal bool Runtime(int block) => this.phases[this.head[block]] == 1;

    /// <summary>Gets a head's stored backward state, the state before its chain, which <see cref="Backward"/> solved.</summary>
    /// <param name="head">The head.</param>
    /// <returns>Its words.</returns>
    internal ReadOnlySpan<ulong> Before(int head) => this.arena.AsSpan(head * this.Row, this.backward);

    /// <summary>Gets the flow head of a block's chain, numbered in reverse postorder.</summary>
    /// <param name="block">A block of the body.</param>
    /// <returns>The head.</returns>
    internal int Head(int block) => this.head[block];

    /// <summary>Orders a validated body's blocks, forms its chains and their heads and decides their phases, once per body.</summary>
    /// <param name="body">The body.</param>
    internal void Begin(MirBody body)
    {
        var blocks = body.Blocks;
        this.body = body;
        Grow(ref this.order, blocks.Length);
        Grow(ref this.next, blocks.Length);
        Grow(ref this.previous, blocks.Length);
        Grow(ref this.head, blocks.Length);
        Grow(ref this.heads, blocks.Length);
        Grow(ref this.tails, blocks.Length);
        Grow(ref this.phases, blocks.Length);
        Grow(ref this.starts, blocks.Length + 1);
        reach.Clear(blocks.Length);
        reach.Walk(body, 0, checks: true); // A validated body's blocks are all reached.
        for (var i = 0; i < blocks.Length; i++)
        {
            this.order[i] = reach.Postorder[blocks.Length - 1 - i];
        }

        this.Predecessors();
        Array.Fill(this.previous, -1, 0, blocks.Length);
        for (var b = 0; b < blocks.Length; b++)
        {
            var terminator = blocks[b].Terminator;
            var successor = MirUses.Successors(terminator) == 1 && terminator.CheckTarget < 0 ? MirUses.Successor(body, terminator, 0) : 0;
            this.next[b] = successor > 0 && this.starts[successor + 1] - this.starts[successor] == 1 ? successor : -1;
            if (this.next[b] >= 0)
            {
                this.previous[successor] = b;
            }
        }

        reach.Clear(blocks.Length);
        reach.Walk(body, 0, checks: false); // P1: the runtime edges from the entry.
        this.headCount = 0;
        Array.Fill(this.head, -1, 0, blocks.Length);
        foreach (var block in this.Order)
        {
            if (this.head[block] < 0)
            {
                this.heads[this.headCount] = block;
                this.phases[this.headCount] = reach[block] ? (byte)1 : (byte)2;
                this.head[block] = this.headCount++;
            }

            this.tails[this.head[block]] = block;
            if (this.next[block] >= 0)
            {
                this.head[this.next[block]] = this.head[block];
            }
        }
    }

    /// <summary>Sizes the states of a backward and a forward problem over the heads of the body begun, both read together.</summary>
    /// <param name="backward">The words a head stores for the backward problem, the first words of its state.</param>
    /// <param name="forward">The words a head stores for the forward problem, the first words of its state.</param>
    /// <param name="width">The words of the scratch, the widest whole state.</param>
    /// <param name="words">The arena's words, which <see cref="StorageLimit"/> records when they are over the limit.</param>
    /// <returns>The scratch, or null when the arena would exceed the analyses' byte limit (R4-M15a).</returns>
    internal ulong[]? Prepare(int backward, int forward, int width, out long words)
    {
        words = (long)this.headCount * (backward + forward);
        if (!Within(words))
        {
            return null;
        }

        (this.backward, this.forward) = (backward, forward);
        Grow(ref this.arena, this.headCount * this.Row);
        Grow(ref this.scratch, width);
        Grow(ref this.reached, this.headCount);
        Grow(ref this.pending, (this.headCount + 63) >> 6);
        return this.scratch;
    }

    /// <summary>Solves a forward problem from the body's entry: P1 over runtime edges, then P2 over runtime and CheckTarget edges into the
    /// heads P1 did not reach (SPEC 14.10.3).</summary>
    /// <typeparam name="TFlow">The transfer's struct type.</typeparam>
    /// <param name="flow">The transfer.</param>
    internal void Forward<TFlow>(ref TFlow flow)
        where TFlow : struct, IMirFlow
    {
        Array.Clear(this.reached, 0, this.headCount);
        flow.Entry();
        this.scratch.AsSpan(0, this.forward).CopyTo(this.arena.AsSpan(this.backward));
        this.reached[0] = true;
        this.Solve(ref flow, phase: 1);
        this.Solve(ref flow, phase: 2);
    }

    /// <summary>Applies each reached chain once, in reverse postorder, from its head's converged forward state.</summary>
    /// <typeparam name="TFlow">The transfer's struct type.</typeparam>
    /// <param name="flow">The transfer.</param>
    internal void Replay<TFlow>(ref TFlow flow)
        where TFlow : struct, IMirFlow
    {
        for (var h = 0; h < this.headCount; h++)
        {
            if (this.reached[h])
            {
                this.Chain(ref flow, h);
            }
        }
    }

    /// <summary>Solves a backward problem to its least fixed point: a chain's state before its head is its blocks applied, last first, to the
    /// boundary state joined with the states of the successors its state flows into forward (SPEC 14.10.3, DECISIONS R4-M158). The worklist takes
    /// heads in postorder, its index i being head headCount − 1 − i, so acyclic code converges in one sweep, and a head whose state grows
    /// makes its predecessors' heads pending (R4-M65).</summary>
    /// <typeparam name="TFlow">The transfer's struct type.</typeparam>
    /// <param name="flow">The transfer.</param>
    internal void Backward<TFlow>(ref TFlow flow)
        where TFlow : struct, IMirFlow
    {
        for (var h = 0; h < this.headCount; h++)
        {
            this.arena.AsSpan(h * this.Row, this.backward).Clear();
        }

        this.Seed();
        for (var next = this.Next(); next >= 0; next = this.Next())
        {
            var h = this.headCount - 1 - next;
            this.Unwind(ref flow, h);
            if (!flow.Join(this.arena.AsSpan(h * this.Row, this.backward), this.scratch.AsSpan(0, this.backward)))
            {
                continue;
            }

            for (var i = this.starts[this.heads[h]]; i < this.starts[this.heads[h] + 1]; i++)
            {
                this.Queue(this.headCount - 1 - this.head[this.predecessors[i]]);
            }
        }
    }

    /// <summary>Applies each chain once, last block first, from the converged backward state after it.</summary>
    /// <typeparam name="TFlow">The transfer's struct type.</typeparam>
    /// <param name="flow">The transfer.</param>
    internal void ReplayBackward<TFlow>(ref TFlow flow)
        where TFlow : struct, IMirFlow
    {
        for (var h = this.headCount - 1; h >= 0; h--)
        {
            this.Unwind(ref flow, h);
        }
    }

    // The bits of the paths [first, end) in a word group.
    private static ulong Mask(int group, int first, int end)
    {
        var low = Math.Max(first - (group << 6), 0);
        var high = end - (group << 6);
        return (high >= 64 ? ulong.MaxValue : (1UL << high) - 1) & ~((1UL << low) - 1);
    }

    // Each block's predecessors, one per edge (a CheckTarget's included), grouped by block: each group's end, as a running count, then each
    // source placed down from it, so that starts[b] ends at the group's start.
    private void Predecessors()
    {
        var blocks = this.body.Blocks;
        Array.Clear(this.starts, 0, blocks.Length + 1);
        foreach (ref readonly var block in blocks)
        {
            for (var i = -1; i < MirUses.Successors(block.Terminator); i++)
            {
                if (MirUses.Successor(this.body, block.Terminator, i) is >= 0 and var successor)
                {
                    this.starts[successor]++;
                }
            }
        }

        for (var b = 1; b <= blocks.Length; b++)
        {
            this.starts[b] += this.starts[b - 1];
        }

        Grow(ref this.predecessors, this.starts[blocks.Length]);
        for (var b = 0; b < blocks.Length; b++)
        {
            for (var i = -1; i < MirUses.Successors(blocks[b].Terminator); i++)
            {
                if (MirUses.Successor(this.body, blocks[b].Terminator, i) is >= 0 and var successor)
                {
                    this.predecessors[--this.starts[successor]] = b;
                }
            }
        }
    }

    // Whether a state flows from head h along its chain's edge i (-1 the CheckTarget) into head s, the one edge rule of both directions
    // (R4-M158): into a P2 head along any edge, into a P1 head only along a P1 chain's runtime edge (SPEC 14.10.3: an assumed continuation
    // continues from its Never form's state; an item with a real incoming path uses only real paths).
    private bool Flows(int h, int i, int s) => this.phases[s] == 2 || (this.phases[h] == 1 && i >= 0);

    // The heads reached in a phase until their states stop growing, along the edges Flows admits into the phase's heads: P1 from the entry
    // over runtime edges; P2 into P2 heads, a P1 head passing its frozen state once along its CheckTarget. The worklist takes heads in
    // reverse postorder, so acyclic code converges in one sweep, and a head whose state grows is pending again (R4-M65).
    private void Solve<TFlow>(ref TFlow flow, byte phase)
        where TFlow : struct, IMirFlow
    {
        this.Seed();
        for (var h = this.Next(); h >= 0; h = this.Next())
        {
            var frozen = this.phases[h] == 1 && phase == 2;
            var terminator = this.body.Blocks[this.tails[h]].Terminator;
            if (frozen ? terminator.CheckTarget < 0 || this.phases[this.head[terminator.CheckTarget]] == 1 : this.phases[h] != phase || !this.reached[h])
            {
                continue;
            }

            this.Chain(ref flow, h);
            for (var i = -1; i < (frozen ? 0 : MirUses.Successors(terminator)); i++)
            {
                var target = MirUses.Successor(this.body, terminator, i) is >= 0 and var successor ? this.head[successor] : -1;
                if (target >= 0 && this.phases[target] == phase && this.Flows(h, i, target) && this.Join(ref flow, target))
                {
                    this.Queue(target);
                }
            }
        }
    }

    // Makes every head pending.
    private void Seed()
    {
        var words = (this.headCount + 63) >> 6;
        this.pending.AsSpan(0, words).Fill(ulong.MaxValue);
        this.pending[words - 1] >>= -this.headCount & 63;
        this.low = 0;
    }

    // Makes a head pending at its index in the solve's order.
    private void Queue(int index)
    {
        this.pending[index >> 6] |= 1UL << index;
        this.low = Math.Min(this.low, index >> 6);
    }

    // Takes the pending head first in the solve's order, or -1 when none is (R4-M65).
    private int Next()
    {
        for (; this.low < (this.headCount + 63) >> 6; this.low++)
        {
            if (this.pending[this.low] is var bits and not 0)
            {
                this.pending[this.low] = bits & (bits - 1);
                return (this.low << 6) + BitOperations.TrailingZeroCount(bits);
            }
        }

        return -1;
    }

    // Loads a head's forward state into the scratch and applies its chain.
    private void Chain<TFlow>(ref TFlow flow, int h)
        where TFlow : struct, IMirFlow
    {
        this.arena.AsSpan((h * this.Row) + this.backward, this.forward).CopyTo(this.scratch);
        for (var block = this.heads[h]; block >= 0; block = this.next[block])
        {
            flow.Block(block);
        }
    }

    // Joins the scratch into a head's stored forward state, the first arrival copying it; returns whether the state grew.
    private bool Join<TFlow>(ref TFlow flow, int target)
        where TFlow : struct, IMirFlow
    {
        var state = this.arena.AsSpan((target * this.Row) + this.backward, this.forward);
        if (!this.reached[target])
        {
            this.reached[target] = true;
            this.scratch.AsSpan(0, this.forward).CopyTo(state);
            return true;
        }

        return flow.Join(state, this.scratch.AsSpan(0, this.forward));
    }

    // Writes into the scratch a chain's backward state after its last block, the boundary state joined with the stored states of the
    // successors its state flows into (Flows), then applies its blocks, last first.
    private void Unwind<TFlow>(ref TFlow flow, int h)
        where TFlow : struct, IMirFlow
    {
        flow.Entry();
        var terminator = this.body.Blocks[this.tails[h]].Terminator;
        for (var i = -1; i < MirUses.Successors(terminator); i++)
        {
            if (MirUses.Successor(this.body, terminator, i) is >= 0 and var successor && this.Flows(h, i, this.head[successor]))
            {
                _ = flow.Join(this.scratch.AsSpan(0, this.backward), this.arena.AsSpan(this.head[successor] * this.Row, this.backward));
            }
        }

        for (var block = this.tails[h]; block >= 0; block = this.previous[block])
        {
            flow.Block(block);
        }
    }
}
