// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// MirOrigins' publication for the Loan stratum's rule W (SPEC 15.6.5; DECISIONS R4-M132), of the body Judge last judged, read after Judge
// and before the next body's.
// - Responsibility: per Ref statement, the recorded Refuted problems (UnsatisfiedOriginRelation_Kd) whose finite source, their Reason's
//   longer end, is that Ref's own Origin (Causes), counted by the atom's index once per Judge; whether the body has any (Refutes, the Loan
//   stratum's gate); and whether a receiving Type holds an Origin and every Origin it holds is Fixed at a site under a run's views
//   (Fixed): its atom is Fixed, or is Inferred and every class of those views has one Fixed node that the class's edges reach from the
//   atom and that reaches the atom, neither path through another Fixed node, so the body's relations make it equal to that fixed Origin
//   (SPEC 15.6.1, 15.6.5; PLAN G824).
// - Inputs: Judge's Refs and its problems with the indices MirReport.Add returned; the C1-F4 view's atoms (TyCtxt.OriginAtom); a class's
//   edges, re-formed and indexed after Judge (IndexClass).
// - Outputs: Causes, indices in MirReport.All; Refutes; Fixed.
// - Invariants: two Refs of one root share an atom and so share causes; a body judged with no view or over the arena limit has none;
//   Fixed walks each distinct part of a Type once and fails closed on a raw layer, an abstract part (BoundType.IsAbstract) or a Callable;
//   an Inferred atom's equality reads its classes' marks (Pin), formed once per class and Judge from the class's Fixed nodes, two walks
//   each, so a query costs the classes of its views per Origin; no view, a view with no class, or a class over the meet law's storage
//   limit leaves an Inferred atom not Fixed.
// - Prohibitions: no Loan set read; no fact of a body other than the one judged.
internal sealed partial class MirOrigins
{
    private readonly List<int> recorded = []; // Per pending problem: its index in MirReport.All (-1: dropped).
    private readonly Dictionary<BoundOrigin, int> owns = new(ReferenceEqualityComparer.Instance); // Each Ref's own Origin's atom index.
    private readonly HashSet<BoundType> parts = new(ReferenceEqualityComparer.Instance); // Fixed's walk.
    private int[] causeStart = []; // By atom index below anchorCount: its causes in causeList, from causeStart[a] to causeStart[a + 1].
    private int[] causeList = [];
    private int[] cursors = [];
    private ulong[] pinned = []; // Pin's: by node below pinnedNodes, the classes whose edges make it equal to a Fixed node.
    private int[] pinMarks = []; // Pin's: by node, the mark of the last walk over a Fixed node's in-edges that met it.
    private int pinMark;
    private int pinnedNodes;
    private ulong pinnedClasses; // The classes Pin formed since Judge.
    private int causeCount;

    /// <summary>Gets the recorded Refuted problems whose finite source is the own Origin of the Ref at a statement of the body Judge last
    /// judged (none for another statement), as indices in MirReport.All (SPEC 15.6.5: the causes of a conflict on its Loan).</summary>
    /// <param name="statement">The statement.</param>
    /// <returns>The problems.</returns>
    internal ReadOnlySpan<int> Causes(int statement)
    {
        var (low, high) = (0, this.refCount - 1);
        while (low <= high)
        {
            var mid = (low + high) >> 1;
            var at = this.refs[mid].Statement;
            if (at == statement)
            {
                var own = this.refs[mid].Own;
                return this.causeList.AsSpan(this.causeStart[own], this.causeStart[own + 1] - this.causeStart[own]);
            }

            (low, high) = at < statement ? (mid + 1, high) : (low, mid - 1);
        }

        return default;
    }

    /// <summary>Gets whether a body is the one Judge last judged and has a Refuted problem from a Ref's own Origin: where it has none, rule W
    /// derives nothing, so the Loan stratum sets nothing up for it.</summary>
    /// <param name="body">The body.</param>
    /// <returns>Whether it has causes.</returns>
    internal bool Refutes(MirBody body) => ReferenceEquals(body, this.body) && this.causeCount > 0;

    /// <summary>Gets whether a receiving Type of the body Judge last judged holds an Origin and every Origin it holds, its own, its Origin
    /// arguments' and its components', is Fixed at a site under a run's views (SPEC 15.6.5: a part with a fixed Origin, every part that can
    /// hold the value): a Fixed atom, or an Inferred one every class of the views makes equal to a Fixed atom.</summary>
    /// <param name="type">The case Type, or null.</param>
    /// <param name="site">The site the Origins are resolved at.</param>
    /// <param name="views">The run's views.</param>
    /// <returns>Whether every Origin is Fixed.</returns>
    internal bool Fixed(BoundType? type, SourceRef site, ulong views)
    {
        this.parts.Clear();
        return this.FixedOrigins(type, site, views) > 0;
    }

    // The Origins a part holds, each Fixed, or -1 where one is not or the part is one the body does not know: a raw layer, a Type an
    // instance decides (BoundType.IsAbstract; in a case Type a Type parameter or a projection, its pair layers being replaced) or a
    // Callable (a Function, Closure or Function Item Type); a part met before counts none.
    private int FixedOrigins(BoundType? type, SourceRef site, ulong views)
    {
        var callable = type?.Kind is BoundTypeKind.Function or BoundTypeKind.Closure or BoundTypeKind.FunctionItem;
        if (type is null || type.Semantics == SemanticsKind.Raw || type.IsAbstract || callable)
        {
            return -1;
        }

        if (!this.parts.Add(type))
        {
            return 0;
        }

        var count = 0;
        if (type.Origin is { } origin)
        {
            count = this.IsFixed(origin, site, views) ? 1 : -1;
        }

        for (var i = 0; count >= 0 && i < type.OriginArguments.Count; i++)
        {
            count = this.IsFixed(type.OriginArguments[i], site, views) ? count + 1 : -1;
        }

        for (var i = 0; count >= 0 && i < type.Components.Count; i++)
        {
            var inner = this.FixedOrigins(type.Components[i], site, views);
            count = inner < 0 ? -1 : count + inner;
        }

        return count;
    }

    private bool IsFixed(BoundOrigin origin, SourceRef site, ulong views)
        => types.OriginAtom(this.key, origin, site) is var atom && this.slice.Atoms[atom].Kind switch
        {
            OriginAtomKind.Fixed => true,
            OriginAtomKind.Inferred => this.Equal(atom, views),
            _ => false,
        };

    // Whether an Inferred atom is equal to a Fixed one in every class of the views: Pin has marked it in each; no view, or a view no class
    // covers, fails.
    private bool Equal(int atom, ulong views)
    {
        var (covered, equal) = (0UL, views != 0);
        for (var c = 0; equal && c < this.classCount; c++)
        {
            if ((this.classes[c].Views & views) != 0)
            {
                this.Pin(c);
                covered |= this.classes[c].Views;
                equal = atom < this.pinnedNodes && (this.pinned[atom] & (1UL << c)) != 0;
            }
        }

        return equal && (covered & views) == views;
    }

    // Once per class and Judge: the nodes its edges make equal to a Fixed node F, on a cycle through F and no other Fixed node (SPEC 15.6.1,
    // 15.6.5), each marked with the class's bit: for each F with in- and out-edges, the nodes a walk over its in-edges meets (they reach F)
    // that a walk over its out-edges meets too (F reaches them), neither walking past another Fixed node (Reach). None in a class whose
    // meet law is over the storage limit.
    private void Pin(int c)
    {
        if ((this.pinnedClasses & (1UL << c)) != 0)
        {
            return;
        }

        this.pinnedClasses |= 1UL << c;
        if (!this.IndexClass(c))
        {
            return;
        }

        var (atoms, nodes) = (this.slice.Atoms, this.edgesNodes);
        MirDataflow.Grow(ref this.pinned, nodes);
        MirDataflow.Grow(ref this.pinMarks, nodes);
        this.pinned.AsSpan(this.pinnedNodes, Math.Max(nodes - this.pinnedNodes, 0)).Clear();
        (this.pinnedNodes, this.work) = (Math.Max(this.pinnedNodes, nodes), this.work + nodes);
        for (var f = 0; f < nodes; f++)
        {
            if (atoms[f].Kind != OriginAtomKind.Fixed || this.queryInStart[f] == this.queryInStart[f + 1] || this.queryOutStart[f] == this.queryOutStart[f + 1])
            {
                continue;
            }

            var (mark, up) = (++this.pinMark, this.Reach(f, this.queryInStart, this.queryIn, this.queryTails));
            foreach (var node in this.reachQueue.AsSpan(0, up))
            {
                this.pinMarks[node] = mark;
            }

            var down = this.Reach(f, this.queryOutStart, this.queryOut, this.queryHeads);
            foreach (var node in this.reachQueue.AsSpan(0, down))
            {
                this.pinned[node] |= this.pinMarks[node] == mark ? 1UL << c : 0;
            }
        }
    }

    // After Judge records: the recorded Refuted problems by the atom of their finite source that is some Ref's own (counting sort); nothing
    // where Judge found none, as Refutes is then false and Causes unread.
    private void IndexCauses()
    {
        var refuted = false;
        for (var p = 0; !refuted && p < this.pending.Count; p++)
        {
            refuted = this.pending[p].Code == DiagnosticCode.UnsatisfiedOriginRelation_Kd;
        }

        if (!refuted)
        {
            return;
        }

        this.owns.Clear();
        for (var r = 0; r < this.refCount; r++)
        {
            this.owns.TryAdd(this.slice.Atoms[this.refs[r].Own].Atom, this.refs[r].Own);
        }

        var atoms = this.anchorCount;
        MirDataflow.Grow(ref this.causeStart, atoms + 1);
        MirDataflow.Grow(ref this.cursors, atoms);
        this.causeStart.AsSpan(0, atoms + 1).Clear();
        this.causeCount = 0;
        for (var p = 0; p < this.pending.Count; p++)
        {
            if (this.Source(p) is var own and >= 0)
            {
                this.causeStart[own + 1]++;
                this.causeCount++;
            }
        }

        for (var a = 0; a < atoms; a++)
        {
            this.causeStart[a + 1] += this.causeStart[a];
        }

        this.causeStart.AsSpan(0, atoms).CopyTo(this.cursors);
        MirDataflow.Grow(ref this.causeList, this.causeCount);
        for (var p = 0; p < this.pending.Count; p++)
        {
            if (this.Source(p) is var own and >= 0)
            {
                this.causeList[this.cursors[own]++] = this.recorded[p];
            }
        }
    }

    // A recorded Refuted problem's finite source, where it is a Ref's own atom; -1 otherwise.
    private int Source(int problem)
        => this.pending[problem] is { Code: DiagnosticCode.UnsatisfiedOriginRelation_Kd, Reason.Longer: { } longer } && this.recorded[problem] >= 0 &&
            this.owns.TryGetValue(longer, out var own) ? own : -1;
}
