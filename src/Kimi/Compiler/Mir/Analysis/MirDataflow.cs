// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the bit-set dataflow of one MIR body (plan 5.6.3; DECISIONS R4-M20, R4-M15a, R4-M24): forward problems, and the backward one
// (liveness).
// Input: a validated body (Begin) and a transfer, a struct IMirFlow (forward) or IMirBlocks (backward) that interprets blocks on the scratch
// state. Output: the blocks in reverse postorder, the converged state at each flow head per phase, and Replay, which gives each reached
// chain its state once; backward, the converged state at each head's entry (AtEntry), and Exit, a chain's state at its exit.
// Blocks form chains: a block whose only edge is a runtime edge into a block that has no other predecessor and is not the entry continues
// into it, so a chain shares one stored state, held only at its head. The join is union. P1 follows runtime edges from the entry; P2 follows
// runtime and CheckTarget edges and writes only heads P1 did not reach, P1's states frozen (SPEC 14.10.3); backward, a chain P1 reached
// joins its runtime successors and any other chain every successor, so no continuation state reaches a P1 head (SPEC 14.9, 14.10.3
// paragraph 4; DECISIONS R4-M24). States are word-major (the lanes
// of 64 paths per word group), and only the first words, the flow paths', are stored; the scratch holds the rest. The arena's byte size is
// the one Resource limit of the analyses (OwnershipStorageLimit_Kd), forward and backward arenas together. Storage is pooled and reset by
// counts; Index is the analyses' one table builder (keys to values).
// Must not interpret statements, read syntax or HIR, merge a P2 state into a P1 head, or walk all edges in one pass.

#pragma warning disable SA1402 // The transfer's interface belongs to the dataflow.

namespace Kimi.Compiler.Mir;

/// <summary>A problem of <see cref="MirDataflow"/>: how a block changes a state on the scratch that <see cref="MirDataflow.Prepare"/>
/// returned, forward or, for <see cref="MirDataflow.Backward"/>, backward.</summary>
internal interface IMirBlocks
{
    /// <summary>Applies a block's statements and terminator to the scratch, in their order or, backward, in reverse.</summary>
    /// <param name="block">The block.</param>
    void Block(int block);
}

/// <summary>A forward problem of <see cref="MirDataflow"/>: its entry state and how a block changes a state.</summary>
internal interface IMirFlow : IMirBlocks
{
    /// <summary>Writes the entry state of the body into the scratch.</summary>
    void Entry();
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
    private int[] prev = []; // Each block's predecessor in its chain, or -1 at the chain's head.
    private int[] callers = []; // Per head from callerStart, the heads whose chain's tail has an edge to it (Backward's worklist).
    private int[] callerStart = [];
    private int[] edgeHeads = []; // Backward's scratch: each edge between chains, by its target head and its source head.
    private int[] edgeTails = [];
    private int[] head = []; // Each block's flow head.
    private int[] heads = []; // Each head's block, heads numbered in reverse postorder.
    private int[] tails = []; // Each head's chain's last block.
    private int[] queue = []; // The ring of heads to process.
    private byte[] reached = []; // The phase that reached each head first: 0 none, 1 P1, 2 P2.
    private bool[] queued = [];
    private ulong[] arena = []; // The stored state of each head.
    private ulong[] back = []; // The backward state stored at each head's entry.
    private ulong[] scratch = [];
    private MirBody body = null!;
    private int headCount;
    private int stored;
    private int backward; // The words a head stores backward.
    private int width;

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

    /// <summary>Builds a table of values by key, the analyses' one (DECISIONS R4-M24): <paramref name="start"/>[k] to
    /// <paramref name="start"/>[k + 1] index key k's values in <paramref name="items"/>, in their order; a key of -1 adds nothing.</summary>
    /// <param name="keys">Each pair's key, below <paramref name="count"/>, or -1.</param>
    /// <param name="values">Each pair's value.</param>
    /// <param name="count">The number of keys.</param>
    /// <param name="start">The keys' starts, grown to <paramref name="count"/> + 1.</param>
    /// <param name="items">The values by key, grown to the pairs' count.</param>
    internal static void Index(ReadOnlySpan<int> keys, ReadOnlySpan<int> values, int count, ref int[] start, ref int[] items)
    {
        Grow(ref start, count + 1);
        Array.Clear(start, 0, count + 1);
        foreach (var key in keys)
        {
            if (key >= 0)
            {
                start[key]++;
            }
        }

        for (var k = 1; k <= count; k++)
        {
            start[k] += start[k - 1];
        }

        Grow(ref items, start[count]);
        for (var i = keys.Length - 1; i >= 0; i--)
        {
            if (keys[i] >= 0)
            {
                items[--start[keys[i]]] = values[i];
            }
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

    /// <summary>Gets a block's predecessor in its chain.</summary>
    /// <param name="block">A block of the body.</param>
    /// <returns>The predecessor, or -1 for the chain's first block, its head's.</returns>
    internal int Previous(int block) => this.prev[block];

    /// <summary>Gets whether a block is its chain's last, the tail.</summary>
    /// <param name="block">A block of the body.</param>
    /// <returns>Whether the chain ends with it.</returns>
    internal bool Tail(int block) => this.next[block] < 0;

    /// <summary>Gets the backward state <see cref="Backward"/> stored at a head's entry.</summary>
    /// <param name="head">The head.</param>
    /// <returns>Its stored words.</returns>
    internal ReadOnlySpan<ulong> AtEntry(int head) => this.back.AsSpan(head * this.backward, this.backward);

    /// <summary>Orders a validated body's blocks and forms its chains and their heads, once per body.</summary>
    /// <param name="body">The body.</param>
    internal void Begin(MirBody body)
    {
        var blocks = body.Blocks;
        this.body = body;
        Grow(ref this.order, blocks.Length);
        Grow(ref this.next, blocks.Length);
        Grow(ref this.prev, blocks.Length);
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
        Array.Fill(this.prev, -1, 0, blocks.Length);
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
                this.prev[this.next[block]] = block;
            }
        }
    }

    /// <summary>Sizes the states of a problem over the heads of the body begun, and of a backward one beside it, keeping the backward
    /// states stored.</summary>
    /// <param name="stored">The words a head stores, the first words of a state.</param>
    /// <param name="width">The words of a whole state, forward or backward.</param>
    /// <param name="backward">The words a head stores backward.</param>
    /// <returns>The scratch, or null when the arenas together would exceed the analyses' byte limit (R4-M15a).</returns>
    internal ulong[]? Prepare(int stored, int width, int backward)
    {
        if ((long)this.headCount * (stored + backward) * sizeof(ulong) > ByteLimit)
        {
            return null;
        }

        (this.stored, this.backward, this.width) = (stored, backward, width);
        Grow(ref this.arena, this.headCount * stored);
        Grow(ref this.back, this.headCount * backward);
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

    /// <summary>Solves a backward problem (liveness, DECISIONS R4-M24) over the edges <see cref="Exit"/> joins: each head's state at its
    /// entry, from its chain's state at its exit through its blocks from the tail. A ring worklist is seeded with
    /// every head in postorder, so acyclic code converges in one sweep, and a head whose state grows queues the heads with an edge to it.
    /// </summary>
    /// <typeparam name="TFlow">The transfer's struct type.</typeparam>
    /// <param name="flow">The transfer.</param>
    internal void Backward<TFlow>(ref TFlow flow)
        where TFlow : struct, IMirBlocks
    {
        this.Callers();
        Array.Clear(this.back, 0, this.headCount * this.backward);
        for (var h = 0; h < this.headCount; h++)
        {
            (this.queue[h], this.queued[h]) = (this.headCount - 1 - h, true);
        }

        var (first, count) = (0, this.headCount);
        while (count-- > 0)
        {
            var h = this.queue[first];
            (first, this.queued[h]) = (first + 1 == this.headCount ? 0 : first + 1, false);
            for (var block = this.Exit(h); block >= 0; block = this.prev[block])
            {
                flow.Block(block);
            }

            if (!Union(this.back.AsSpan(h * this.backward, this.backward), this.scratch))
            {
                continue;
            }

            for (var c = this.callerStart[h]; c < this.callerStart[h + 1]; c++)
            {
                if (!this.queued[this.callers[c]])
                {
                    this.queued[this.callers[c]] = true;
                    this.queue[(first + count++) % this.headCount] = this.callers[c];
                }
            }
        }
    }

    /// <summary>Writes into the scratch a chain's backward state at its exit: the union of the states stored at the entries of the heads
    /// its tail has an edge to, a chain P1 reached its runtime successors only (SPEC 14.9: continuations add no execution paths).
    /// </summary>
    /// <param name="head">The chain's head.</param>
    /// <returns>The chain's tail.</returns>
    internal int Exit(int head)
    {
        Array.Clear(this.scratch, 0, this.width);
        var tail = this.tails[head];
        var terminator = this.body.Blocks[tail].Terminator;
        for (var i = this.reached[head] == 1 ? 0 : -1; i < MirUses.Successors(terminator); i++)
        {
            if (MirUses.Successor(this.body, terminator, i) is >= 0 and var successor)
            {
                _ = Union(this.scratch.AsSpan(0, this.backward), this.back.AsSpan(this.head[successor] * this.backward, this.backward));
            }
        }

        return tail;
    }

    // Joins a span into another; returns whether it grew.
    private static bool Union(Span<ulong> into, ReadOnlySpan<ulong> from)
    {
        var grew = 0UL;
        for (var i = 0; i < into.Length; i++)
        {
            grew |= from[i] & ~into[i];
            into[i] |= from[i];
        }

        return grew != 0;
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

    // The heads whose chain's exit joins each head's entry (Exit's edges), as a table (Index).
    private void Callers()
    {
        var count = 0;
        for (var h = 0; h < this.headCount; h++)
        {
            var terminator = this.body.Blocks[this.tails[h]].Terminator;
            for (var i = this.reached[h] == 1 ? 0 : -1; i < MirUses.Successors(terminator); i++)
            {
                if (MirUses.Successor(this.body, terminator, i) is >= 0 and var successor)
                {
                    Grow(ref this.edgeHeads, count + 1);
                    Grow(ref this.edgeTails, count + 1);
                    (this.edgeHeads[count], this.edgeTails[count++]) = (this.head[successor], h);
                }
            }
        }

        Index(this.edgeHeads.AsSpan(0, count), this.edgeTails.AsSpan(0, count), this.headCount, ref this.callerStart, ref this.callers);
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

        return Union(state, this.scratch);
    }
}
