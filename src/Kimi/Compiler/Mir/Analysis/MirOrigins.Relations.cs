// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// MirOrigins' publication for A5's call retention (SPEC 15.6.4; DECISIONS R4-M154, Revisions 2 and 4), of the body Judge last judged, read
// after Judge and before the next body's.
// - Responsibility: whether one Origin can outlive another at a site under the class of a view (Outlives): the pairs a call's retention
//   leaves to the call's solved Origins, two Origins of its Type arguments that the callee's premises do not relate. A pair holds when
//   it is satisfiable for every value of the body's Fixed Origins: a declared Origin is universally quantified (SPEC 8.15.1, 8.13.2),
//   a local inference region the body's own solution, which may be any region its bounds admit.
// - Inputs: the class's edges (ClassEdges: its active rows by MeetNode's rule, each a relation the body's problems check, and its
//   containment edges); the C1-F4 view's atoms (TyCtxt.OriginAtom) and the body's fixed-fixed oracle (TyCtxt.UniversalRelations, the
//   layer's for the layer's part) under the class's borrow binders.
// - Outputs: true where the relation, added as an edge, adds no relation the body could not satisfy for every value of its Fixed
//   Origins: each Fixed node at or above the longer end (stopping at Fixed nodes) outlives, by the oracle, each Fixed node at or below
//   the shorter end, and no Finite node above the longer end meets a Fixed one below the shorter end (SPEC 15.6.5); false otherwise;
//   null where an end has no class (Unclassed or Dependent) or the view no class.
// - Invariants: a query reads one class's edges, indexed once until the next class or body; it records no problem and changes no state
//   Judge's problems read; an oracle it opens is disposed before it returns.
// - Prohibitions: no Loan set, liveness or CFG order read; no fact of a body other than the one judged.
internal sealed partial class MirOrigins
{
    private readonly List<int> above = []; // Outlives' Fixed and Finite nodes at or above the longer end.
    private readonly List<int> below = []; // Outlives' Fixed nodes at or below the shorter end.
    private int[] reachStamps = []; // Outlives' visit stamps by node, and its queue.
    private int[] reachQueue = [];
    private int reachStamp;
    private int edgesClass = -1; // The class whose edges tails, heads and their indexes hold for Outlives (-1: none, Judge's own).
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

        this.IndexClass(c);
        this.Bounds(from, this.inStart, this.inEdges, this.tails, this.above);
        this.Bounds(to, this.outStart, this.outEdges, this.heads, this.below);
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

    // The nodes a walk from a node over one direction's edges meets, not walking past a Fixed one: into `found`, the Fixed ones and, above
    // the longer end, the Finite ones (a body-local region, which outlives no Fixed Origin).
    private void Bounds(int start, int[] index, int[] edges, int[] ends, List<int> found)
    {
        var atoms = this.slice.Atoms;
        MirDataflow.Grow(ref this.reachStamps, atoms.Count);
        MirDataflow.Grow(ref this.reachQueue, atoms.Count);
        var (stamp, tail) = (++this.reachStamp, 1);
        (this.reachStamps[start], this.reachQueue[0]) = (stamp, start);
        found.Clear();
        for (var head = 0; head < tail; head++)
        {
            var node = this.reachQueue[head];
            var kind = atoms[node].Kind;
            if (kind == OriginAtomKind.Fixed || (kind == OriginAtomKind.Finite && ReferenceEquals(found, this.above)))
            {
                found.Add(node);
            }

            for (var e = node < this.edgesNodes && kind != OriginAtomKind.Fixed ? index[node] : 0; node < this.edgesNodes && e < index[node + 1]; e++)
            {
                if (ends[edges[e]] is var next && this.reachStamps[next] != stamp)
                {
                    (this.reachStamps[next], this.reachQueue[tail++]) = (stamp, next);
                }
            }
        }
    }

    // A class's edges (ClassEdges, an Unclassed end's none) indexed by tail and by head, once per class.
    private void IndexClass(int c)
    {
        if (this.edgesClass == c)
        {
            return;
        }

        this.edgeCount = 0;
        foreach (var edge in this.ClassEdges(c))
        {
            if (edge.Tail >= 0 && edge.Tail != edge.Head)
            {
                MirDataflow.Grow(ref this.tails, this.edgeCount + 1);
                MirDataflow.Grow(ref this.heads, this.edgeCount + 1);
                (this.tails[this.edgeCount], this.heads[this.edgeCount++]) = (edge.Tail, edge.Head);
            }
        }

        (this.edgesClass, this.edgesNodes) = (c, this.slice.Atoms.Count);
        this.IndexEdges(this.tails, this.edgesNodes, ref this.outStart, ref this.outEdges);
        this.IndexEdges(this.heads, this.edgesNodes, ref this.inStart, ref this.inEdges);
    }
}
