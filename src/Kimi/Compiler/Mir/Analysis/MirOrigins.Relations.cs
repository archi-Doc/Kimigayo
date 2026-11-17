// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// MirOrigins' publication for A5's call retention (SPEC 15.6.4; DECISIONS R4-M154, Revisions 2 and 4), of the body Judge last judged, read
// after Judge and before the next body's.
// - Responsibility: whether one Origin can outlive another at a site under the class of a view (Outlives): the pairs a call's retention
//   leaves to the call's solved Origins, two Origins of its Type arguments that the callee's premises do not relate. A pair holds when
//   it is satisfiable for every value of the body's Fixed Origins: a declared Origin is universally quantified (SPEC 8.15.1, 8.13.2),
//   a local inference region the body's own solution, which may be any region its bounds admit; an enclosing body's region, Fixed here,
//   reads as its solved value (UniversalRelations.Outlives).
// - Inputs: the class's edges (ClassEdges: its active rows by MeetNode's rule, each a relation the body's problems check, and its
//   containment edges); the C1-F4 view's atoms (TyCtxt.OriginAtom) and the body's fixed-fixed oracle (TyCtxt.UniversalRelations, the
//   layer's for the layer's part) under the class's borrow binders.
// - Outputs: true where the relation, added as an edge, adds no relation the body could not satisfy for every value of its Fixed
//   Origins: each Fixed node at or above the longer end (stopping at Fixed nodes) outlives, by the oracle, each Fixed node at or below
//   the shorter end, and no Finite node above the longer end meets a Fixed one below the shorter end (SPEC 15.6.5); false otherwise;
//   null where an end has no class (Unclassed or Dependent), the view no class, or the class's meet law is over the storage limit (D20).
// - Invariants: a query reads one class's edges, indexed once until the next class or body; it records no problem and changes no state
//   Judge's problems read; an oracle it opens is disposed before it returns; Fixed's equality (Pin, MirOrigins.Causes.cs) reads the same
//   index and walk (IndexClass, Walk).
// - Prohibitions: no Loan set, liveness or CFG order read; no fact of a body other than the one judged.
internal sealed partial class MirOrigins
{
    private readonly List<int> above = []; // Outlives' Fixed and Finite nodes at or above the longer end.
    private readonly List<int> below = []; // Outlives' Fixed nodes at or below the shorter end.
    private int[] reachStamps = []; // Outlives' and the meet law's stamps by node, and the walks' queue (the law's stack).
    private int[] reachQueue = [];
    private int reachStamp;
    private int[] queryTails = []; // Outlives' class edges (every one with two ends, Fixed ones included), by tail and by head.
    private int[] queryHeads = [];
    private int[] queryOutStart = [];
    private int[] queryOut = [];
    private int[] queryInStart = [];
    private int[] queryIn = [];
    private int queryCount;
    private int edgesClass = -1; // The class whose edges the query arrays hold for Outlives (-1: none; Judge re-forms classEdges).
    private int edgesNodes; // The node count they were indexed for.

    /// <summary>Gets whether one Origin can outlive another at a site under the class of a view of the body Judge last judged (SPEC
    /// 15.6.4; DECISIONS R4-M154, Revisions 2 and 4: a call retention's pair).</summary>
    /// <param name="body">The body Judge last judged.</param>
    /// <param name="view">The view.</param>
    /// <param name="longer">The Origin that would outlive.</param>
    /// <param name="shorter">The Origin it would outlive.</param>
    /// <param name="site">The site the Origins are resolved at.</param>
    /// <returns>True where the relation adds none the body could not satisfy; false where it does; null where an end or the view has no
    /// class.</returns>
    internal bool? Outlives(MirBody body, long view, BoundOrigin longer, BoundOrigin shorter, SourceRef site)
    {
        if (!ReferenceEquals(body, this.body))
        {
            throw new InvalidOperationException("MirOrigins.Outlives reads the body Judge last judged.");
        }

        var c = 0;
        while (c < this.classCount && (this.classes[c].Views & (1UL << (int)view)) == 0)
        {
            c++;
        }

        if (c == this.classCount)
        {
            return null;
        }

        var (from, to) = (types.OriginAtom(this.key, longer, site), types.OriginAtom(this.key, shorter, site));
        var atoms = this.slice.Atoms;
        if (atoms[from].Kind is OriginAtomKind.Unclassed or OriginAtomKind.Dependent || atoms[to].Kind is OriginAtomKind.Unclassed or OriginAtomKind.Dependent)
        {
            return null;
        }

        if (from == to || longer.Kind == OriginKind.Static)
        {
            return true;
        }

        if (!this.IndexClass(c))
        {
            return null;
        }

        this.Bounds(from, this.queryInStart, this.queryIn, this.queryTails, this.above);
        this.Bounds(to, this.queryOutStart, this.queryOut, this.queryHeads, this.below);
        var part = this.slice.InLayer(from) || this.slice.InLayer(to) ? this.slice.Layer : this.key;
        using var oracle = this.below.Count == 0 ? null : types.UniversalRelations(part);
        foreach (var lower in this.below)
        {
            oracle!.Close(atoms[lower].Atom, this.classes[c].Borrows);
            foreach (var upper in this.above)
            {
                if (upper != lower && (atoms[upper].Kind != OriginAtomKind.Fixed || !oracle.Outlives(atoms[upper].Atom)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // The nodes a walk from a node over one direction's edges meets (Walk): into `found`, the Fixed ones and, above the longer end, the
    // Finite ones (a body-local region, which outlives no Fixed Origin).
    private void Bounds(int start, int[] index, int[] edges, int[] ends, List<int> found)
    {
        var atoms = this.slice.Atoms;
        MirDataflow.Grow(ref this.reachStamps, atoms.Count);
        MirDataflow.Grow(ref this.reachQueue, atoms.Count);
        (this.reachStamps[start], this.reachQueue[0]) = (++this.reachStamp, start);
        found.Clear();
        foreach (var node in this.reachQueue.AsSpan(0, this.Walk(1, index, edges, ends)))
        {
            var kind = atoms[node].Kind;
            if (kind == OriginAtomKind.Fixed || (kind == OriginAtomKind.Finite && ReferenceEquals(found, this.above)))
            {
                found.Add(node);
            }
        }
    }

    // The nodes a walk from a Fixed node's edges in one direction meets (Walk), never the node itself, which is stamped before the walk:
    // Pin's walks (MirOrigins.Causes.cs). Returns their count, reachQueue's first.
    private int Reach(int start, int[] index, int[] edges, int[] ends)
    {
        MirDataflow.Grow(ref this.reachStamps, this.slice.Atoms.Count);
        MirDataflow.Grow(ref this.reachQueue, this.slice.Atoms.Count);
        this.reachStamps[start] = ++this.reachStamp;
        return this.Walk(this.Expand(start, 0, index, edges, ends), index, edges, ends);
    }

    // A walk over one direction's edges from the nodes queued below `tail` (stamped reachStamp), not walking past a Fixed node: it walks
    // the edges of each node it meets that is not Fixed. Returns the count of nodes met, reachQueue's first; its node visits and edge steps
    // count in Work.
    private int Walk(int tail, int[] index, int[] edges, int[] ends)
    {
        for (var head = 0; head < tail; head++)
        {
            var node = this.reachQueue[head];
            tail = this.slice.Atoms[node].Kind == OriginAtomKind.Fixed ? tail : this.Expand(node, tail, index, edges, ends);
        }

        this.work += tail;
        return tail;
    }

    // A node's edges in one direction (none at or past the indexed nodes): each end the walk has not met joins its queue at `tail`.
    private int Expand(int node, int tail, int[] index, int[] edges, int[] ends)
    {
        var (first, end) = node < this.edgesNodes ? (index[node], index[node + 1]) : (0, 0);
        this.work += end - first;
        for (var e = first; e < end; e++)
        {
            if (ends[edges[e]] is var next && this.reachStamps[next] != this.reachStamp)
            {
                (this.reachStamps[next], this.reachQueue[tail++]) = (this.reachStamp, next);
            }
        }

        return tail;
    }

    // A class's edges (ClassEdges, an Unclassed end's none) indexed by tail and by head, once until another class's or the next body's,
    // their rows, edges and nodes counted in Work; false when its meet law is over the storage limit (D20).
    private bool IndexClass(int c)
    {
        if (this.edgesClass == c)
        {
            return true;
        }

        (this.queryCount, this.edgesClass) = (0, -1);
        var formed = this.ClassEdges(c);
        this.work += this.slice.Rows.Count + this.classEdgeCount + this.slice.Atoms.Count;
        if (!formed)
        {
            return false;
        }

        foreach (var edge in this.classEdges.AsSpan(0, this.classEdgeCount))
        {
            if (edge.Tail >= 0 && edge.Tail != edge.Head)
            {
                MirDataflow.Grow(ref this.queryTails, this.queryCount + 1);
                MirDataflow.Grow(ref this.queryHeads, this.queryCount + 1);
                (this.queryTails[this.queryCount], this.queryHeads[this.queryCount++]) = (edge.Tail, edge.Head);
            }
        }

        (this.edgesClass, this.edgesNodes) = (c, this.slice.Atoms.Count);
        TyCtxt.OriginChains.Index(this.queryTails.AsSpan(0, this.queryCount), this.edgesNodes, ref this.queryOutStart, ref this.queryOut, ref this.cursors);
        TyCtxt.OriginChains.Index(this.queryHeads.AsSpan(0, this.queryCount), this.edgesNodes, ref this.queryInStart, ref this.queryIn, ref this.cursors);
        return true;
    }
}
