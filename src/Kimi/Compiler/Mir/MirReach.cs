// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the one reachability walk over a MIR body's blocks (plan 5.6.3).
// Input: a body and the blocks walks start from. Output: the blocks reached along runtime edges (MirUses.Successor) and, when asked,
// CheckTarget edges; reached blocks accumulate until Clear, so each block is visited once however many walks reach it.
// Must not decide what reaching a block means or allocate once its arrays hold the widest body.

namespace Kimi.Compiler.Mir;

/// <summary>The blocks of a body that walks from given blocks reach (plan 5.6.3): along runtime edges, and along CheckTarget edges when
/// a walk asks. The validator walks every edge from the entry; A3 walks runtime edges from failure bodies' entries.</summary>
internal sealed class MirReach
{
    private bool[] reached = [];
    private int[] pending = [];

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
            this.pending = new int[this.reached.Length];
        }

        Array.Clear(this.reached, 0, blocks);
    }

    /// <summary>Reaches a block and every block its successors reach, stopping at blocks already reached.</summary>
    /// <param name="body">The body, whose successors are in range.</param>
    /// <param name="block">The block the walk starts from.</param>
    /// <param name="checks">Whether CheckTarget edges are followed too.</param>
    internal void Walk(MirBody body, int block, bool checks)
    {
        if (this.reached[block])
        {
            return;
        }

        var count = 0;
        this.reached[block] = true;
        this.pending[count++] = block;
        while (count > 0)
        {
            var terminator = body.Blocks[this.pending[--count]].Terminator;
            for (var i = checks ? -1 : 0; i < MirUses.Successors(terminator); i++)
            {
                var next = i < 0 ? terminator.CheckTarget : MirUses.Successor(body, terminator, i);
                if (next >= 0 && !this.reached[next])
                {
                    this.reached[next] = true;
                    this.pending[count++] = next;
                }
            }
        }
    }
}
