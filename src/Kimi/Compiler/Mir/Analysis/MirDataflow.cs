// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the forward bit-set dataflow of one MIR body (plan 5.6.3; DECISIONS R4-M20, R4-M15a).
// Input: a validated body (Begin) and a transfer, a struct IMirFlow that interprets blocks on the scratch state. Output: the blocks in
// reverse postorder, the converged state at each flow head per phase, and Replay, which gives each reached chain its state once.
// Blocks form chains: a block whose only edge is a runtime edge into a block that has no other predecessor and is not the entry continues
// into it, so a chain shares one stored state, held only at its head. The join is union. P1 follows runtime edges from the entry; P2 follows
// runtime and CheckTarget edges and writes only heads P1 did not reach, P1's states frozen (SPEC 14.10.3). States are word-major (the lanes
// of 64 paths per word group), and only the first words, the flow paths', are stored; the scratch holds the rest. The arena's byte size is
// the one Resource limit of the analyses (OwnershipStorageLimit_Kd). Storage is pooled and reset by counts.
// Must not interpret statements, read syntax or HIR, merge a P2 state into a P1 head, or walk all edges in one pass.

#pragma warning disable SA1402 // The transfer's interface belongs to the dataflow.

namespace Kimi.Compiler.Mir;

/// <summary>A forward problem of <see cref="MirDataflow"/>: its entry state and how a block changes a state, both on the scratch that
/// <see cref="MirDataflow.Prepare"/> returned.</summary>
internal interface IMirFlow
{
    /// <summary>Writes the entry state of the body into the scratch.</summary>
    void Entry();

    /// <summary>Applies a block's statements and terminator to the scratch.</summary>
    /// <param name="block">The block.</param>
    void Block(int block);
}

/// <summary>The forward bit-set dataflow of the MIR analyses (plan 5.6.3, DECISIONS R4-M20): <see cref="Begin"/> orders a body's blocks
/// once, <see cref="Prepare"/> sizes the states of a problem, <see cref="Forward"/> solves it in the two phases of SPEC 14.10.3 and
/// <see cref="Replay"/> gives each reached chain its converged state, where the checks run.</summary>
/// <param name="reach">The one reachability walk, whose post-order over every edge orders the blocks.</param>
internal sealed class MirDataflow(MirReach reach)
{
    // R4-M15a: the byte size of an analysis arena above which a body is not analyzed (OwnershipStorageLimit_Kd), the old path's limit
    // value until R6 states it in IMPL 21.3.2.
    private const long ByteLimit = 64L * 1024 * 1024;
    private int[] order = []; // The blocks in reverse postorder.
    private int[] next = []; // Each block's successor in its chain, or -1 at the chain's tail.
    private int[] head = []; // Each block's flow head.
    private int[] heads = []; // Each head's block, heads numbered in reverse postorder.
    private int[] tails = []; // Each head's chain's last block.
    private int[] queue = []; // The ring of heads to process.
    private byte[] reached = []; // The phase that reached each head first: 0 none, 1 P1, 2 P2.
    private bool[] queued = [];
    private ulong[] arena = []; // The stored state of each head.
    private ulong[] scratch = [];
    private MirBody body = null!;
    private int headCount;
    private int stored;

    /// <summary>Gets the blocks of the body begun in reverse postorder over runtime and check edges, so a chain's blocks are in order.</summary>
    internal ReadOnlySpan<int> Order => this.order.AsSpan(0, this.body.Blocks.Length);

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

    /// <summary>Gets whether the runtime edges from the entry reach a block (P1); every other block only the checks reach (P2).</summary>
    /// <param name="block">A block of the body.</param>
    /// <returns>Whether P1 reached it.</returns>
    internal bool Runtime(int block) => this.reached[this.head[block]] == 1;

    /// <summary>Gets the flow head of a block's chain, numbered in reverse postorder.</summary>
    /// <param name="block">A block of the body.</param>
    /// <returns>The head.</returns>
    internal int Head(int block) => this.head[block];

    /// <summary>Orders a validated body's blocks and forms its chains and their heads, once per body.</summary>
    /// <param name="body">The body.</param>
    internal void Begin(MirBody body)
    {
        var blocks = body.Blocks;
        this.body = body;
        Grow(ref this.order, blocks.Length);
        Grow(ref this.next, blocks.Length);
        Grow(ref this.head, blocks.Length);
        Grow(ref this.heads, blocks.Length);
        Grow(ref this.tails, blocks.Length);
        reach.Clear(blocks.Length);
        reach.Walk(body, 0, checks: true); // A validated body's blocks are all reached.
        for (var i = 0; i < blocks.Length; i++)
        {
            this.order[i] = reach.Postorder[blocks.Length - 1 - i];
        }

        Array.Clear(this.head, 0, blocks.Length); // Predecessor counts first.
        foreach (ref readonly var block in blocks)
        {
            for (var i = -1; i < MirUses.Successors(block.Terminator); i++)
            {
                if (MirUses.Successor(body, block.Terminator, i) is >= 0 and var successor)
                {
                    this.head[successor]++;
                }
            }
        }

        for (var b = 0; b < blocks.Length; b++)
        {
            var terminator = blocks[b].Terminator;
            var successor = MirUses.Successors(terminator) == 1 && terminator.CheckTarget < 0 ? MirUses.Successor(body, terminator, 0) : 0;
            this.next[b] = successor > 0 && this.head[successor] == 1 ? successor : -1;
        }

        this.headCount = 0;
        Array.Fill(this.head, -1, 0, blocks.Length);
        foreach (var block in this.Order)
        {
            if (this.head[block] < 0)
            {
                this.heads[this.headCount] = block;
                this.head[block] = this.headCount++;
            }

            this.tails[this.head[block]] = block;
            if (this.next[block] >= 0)
            {
                this.head[this.next[block]] = this.head[block];
            }
        }
    }

    /// <summary>Sizes the states of a problem over the heads of the body begun.</summary>
    /// <param name="stored">The words a head stores, the first words of a state.</param>
    /// <param name="width">The words of a whole state.</param>
    /// <returns>The scratch, or null when the arena would exceed the analyses' byte limit (R4-M15a).</returns>
    internal ulong[]? Prepare(int stored, int width)
    {
        if ((long)this.headCount * stored * sizeof(ulong) > ByteLimit)
        {
            return null;
        }

        this.stored = stored;
        Grow(ref this.arena, this.headCount * stored);
        Grow(ref this.scratch, width);
        Grow(ref this.reached, this.headCount);
        Grow(ref this.queued, this.headCount);
        Grow(ref this.queue, this.headCount);
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
        this.scratch.AsSpan(0, this.stored).CopyTo(this.arena);
        this.reached[0] = 1;
        this.Solve(ref flow, phase: 1);
        this.Solve(ref flow, phase: 2);
    }

    /// <summary>Applies each reached chain once, in reverse postorder, from its head's converged state.</summary>
    /// <typeparam name="TFlow">The transfer's struct type.</typeparam>
    /// <param name="flow">The transfer.</param>
    internal void Replay<TFlow>(ref TFlow flow)
        where TFlow : struct, IMirFlow
    {
        for (var h = 0; h < this.headCount; h++)
        {
            if (this.reached[h] != 0)
            {
                this.Chain(ref flow, h);
            }
        }
    }

    // The bits of the paths [first, end) in a word group.
    private static ulong Mask(int group, int first, int end)
    {
        var low = Math.Max(first - (group << 6), 0);
        var high = end - (group << 6);
        return (high >= 64 ? ulong.MaxValue : (1UL << high) - 1) & ~((1UL << low) - 1);
    }

    // The heads reached in a phase until their states stop growing: P1 from the entry over runtime edges; P2 over runtime and CheckTarget
    // edges into the heads P1 did not reach, a P1 head passing its frozen state once along its CheckTarget. The ring is seeded with every
    // head in reverse postorder, so acyclic code converges in one sweep, and a head whose state grows is queued again at the tail.
    private void Solve<TFlow>(ref TFlow flow, byte phase)
        where TFlow : struct, IMirFlow
    {
        for (var h = 0; h < this.headCount; h++)
        {
            (this.queue[h], this.queued[h]) = (h, true);
        }

        var (first, count) = (0, this.headCount);
        while (count-- > 0)
        {
            var h = this.queue[first];
            (first, this.queued[h]) = (first + 1 == this.headCount ? 0 : first + 1, false);
            var frozen = this.reached[h] == 1 && phase == 2;
            var terminator = this.body.Blocks[this.tails[h]].Terminator;
            if (frozen ? terminator.CheckTarget < 0 || this.reached[this.head[terminator.CheckTarget]] == 1 : this.reached[h] != phase)
            {
                continue;
            }

            this.Chain(ref flow, h);
            for (var i = phase == 2 ? -1 : 0; i < (frozen ? 0 : MirUses.Successors(terminator)); i++)
            {
                var target = MirUses.Successor(this.body, terminator, i) is >= 0 and var successor ? this.head[successor] : -1;
                if (target >= 0 && (phase == 1 || this.reached[target] != 1) && this.Join(target, phase) && !this.queued[target])
                {
                    this.queued[target] = true;
                    this.queue[(first + count++) % this.headCount] = target;
                }
            }
        }
    }

    // Loads a head's state into the scratch and applies its chain.
    private void Chain<TFlow>(ref TFlow flow, int h)
        where TFlow : struct, IMirFlow
    {
        this.arena.AsSpan(h * this.stored, this.stored).CopyTo(this.scratch);
        for (var block = this.heads[h]; block >= 0; block = this.next[block])
        {
            flow.Block(block);
        }
    }

    // Joins the scratch into a head's stored state, the first arrival in the phase copying it; returns whether the state grew.
    private bool Join(int target, byte phase)
    {
        var state = this.arena.AsSpan(target * this.stored, this.stored);
        if (this.reached[target] != phase)
        {
            this.reached[target] = phase;
            this.scratch.AsSpan(0, this.stored).CopyTo(state);
            return true;
        }

        var grew = 0UL;
        for (var i = 0; i < state.Length; i++)
        {
            grew |= this.scratch[i] & ~state[i];
            state[i] |= this.scratch[i];
        }

        return grew != 0;
    }
}
