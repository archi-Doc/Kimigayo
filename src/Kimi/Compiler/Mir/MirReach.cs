// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the one reachability walk over a MIR body's blocks (plan 5.6.3; DECISIONS R4-M20).
// Input: a body and the blocks walks start from. Output: the blocks reached along runtime edges (MirUses.Successor) and, when asked,
// CheckTarget edges, and the order the depth-first walks finish them (a post-order); reached blocks accumulate until Clear, so each block
// is visited once however many walks reach it.
// Must not decide what reaching a block means or allocate once its arrays hold the widest body.

namespace Kimi.Compiler.Mir;

/// <summary>The blocks of a body that walks from given blocks reach (plan 5.6.3): along runtime edges, and along CheckTarget edges when
/// a walk asks, depth first. The validator walks every edge from the entry; A3 walks runtime edges from failure bodies' entries; the
/// dataflow orders a body's blocks by the reverse of <see cref="Postorder"/>.</summary>
internal sealed class MirReach
{
    private bool[] reached = [];
    private int[] stack = []; // The blocks on the walk's path.
    private int[] edges = []; // Each stacked block's next edge, from -1 (its CheckTarget).
    private int[] order = []; // The blocks in the order the walks finished them.
    private int finished;

    /// <summary>Gets the blocks the walks since the last <see cref="Clear"/> reached, in the order they finished them: a post-order of
    /// each walk's depth-first tree, in which a block follows every block it alone leads to.</summary>
    internal ReadOnlySpan<int> Postorder => this.order.AsSpan(0, this.finished);

    /// <summary>Gets whether a walk since the last <see cref="Clear"/> reached a block.</summary>
    /// <param name="block">The block.</param>
    internal bool this[int block] => this.reached[block];

    /// <summary>Starts over for a body: no block reached.</summary>
    /// <param name="blocks">The number of the body's blocks.</param>
    internal void Clear(int blocks)
    {
        if (this.reached.Length < blocks)
        {
            this.reached = new bool[Math.Max(blocks, this.reached.Length * 2)];
            (this.stack, this.edges, this.order) = (new int[this.reached.Length], new int[this.reached.Length], new int[this.reached.Length]);
        }

        Array.Clear(this.reached, 0, blocks);
        this.finished = 0;
    }

    /// <summary>Reaches a block and every block its successors reach, depth first, stopping at blocks already reached.</summary>
    /// <param name="body">The body, whose successors are in range.</param>
    /// <param name="block">The block the walk starts from.</param>
    /// <param name="checks">Whether CheckTarget edges are followed too.</param>
    internal void Walk(MirBody body, int block, bool checks)
    {
        if (this.reached[block])
        {
            return;
        }

        var first = checks ? -1 : 0;
        (this.stack[0], this.edges[0], this.reached[block]) = (block, first, true);
        for (var depth = 1; depth > 0;)
        {
            var terminator = body.Blocks[this.stack[depth - 1]].Terminator;
            if (this.edges[depth - 1] >= MirUses.Successors(terminator))
            {
                this.order[this.finished++] = this.stack[--depth];
            }
            else if (MirUses.Successor(body, terminator, this.edges[depth - 1]++) is >= 0 and var next && !this.reached[next])
            {
                (this.stack[depth], this.edges[depth], this.reached[next]) = (next, first, true);
                depth++;
            }
        }
    }
}
