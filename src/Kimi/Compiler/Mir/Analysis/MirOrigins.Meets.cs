// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// MirOrigins' meet law at a shorter end (SPEC 15.3.6: `(a and b) outlives c` exactly when `a outlives c` and `b outlives c`), the last step
// of ClassEdges, so that Judge's chains and Outlives' walks both read its edges.
// - Responsibility: for each non-Fixed meet m = o1 and ... and on and each class edge u -> m whose tail is not one of m's operands, an edge
//   u -> (g1 and ... and gn) at that edge's site and row for each choice of one bound gi per operand, judged as any edge into a Fixed node.
// - Inputs: the class's row and containment edges (classEdges; an Unclassed end's forms none); the C1-F4 view's atoms, a meet's operands
//   resolved by TyCtxt.OriginAtom and a choice's canonical meet by TyCtxt.MeetAtom (a Fixed atom of the body's table).
// - Outputs: the derived edges, appended to classEdges by meet in the order the meets first keep a bound, then by in-edge and bound; and
//   meetWords, the words the choices and derived edges add to the class's arena (D20).
// - Invariants: an operand's bounds are the operand when it is Fixed, else each Fixed node its walk over the class's edges meets (never
//   past a Fixed node, never along an operand's edge into its own meet: `o outlives (o and ...)` bounds nothing) and each bound of another
//   meet the walk enters; each choice's canonical meet is kept once per meet (a per-close stamp; a weaker choice stays, a redundant
//   consequence of the rows, G's ruling (b)); meets close in a worklist, and a meet closes again when a meet one of its walks entered gains
//   a bound; before a close enumerates, its product times m's in-edges that take derived edges is added to meetWords, so over
//   MirDataflow.Within the law stops, no choice skipped, and the body gets OwnershipStorageLimit_Kd; storage is pooled and the view interns
//   each canonical meet once, so a warm run allocates nothing.
// - Cost: a close is its operands' walks (stamped, each node once per walk) plus its choices (the product of its operands' bound counts),
//   each one MeetAtom fold over its operands and one stamp, so it is linear in its choices; a meet closes once, plus once per bound a meet
//   it waits on gains. Work adds each walk step, each choice's fold and stamp, and each kept bound's stamp at a close; the choices and
//   derived edges count in the arena (meetWords).
// - Prohibitions: no cap on the choices; no edge to a non-Fixed node; no record (Join and Chains judge the derived edges).
internal sealed partial class MirOrigins
{
    private int[] lawTails = []; // The class's edges before the law, by edge (-1: an Unclassed end's), indexed by tail and by head.
    private int[] lawHeads = [];
    private int[] lawOutStart = [];
    private int[] lawOut = [];
    private int[] lawInStart = [];
    private int[] lawIn = [];
    private int lawNodes; // The nodes before the law; a canonical meet it adds is Fixed and on no edge it walks.
    private int[] operandAt = []; // Per meet: where its operand nodes start in operandNodes (-1: not resolved).
    private int[] operandNodes = [];
    private int operandCount;
    private int[] boundFirst = []; // Per meet: its kept bounds, a list through boundNext (-1: none) ending at boundLast.
    private int[] boundLast = [];
    private int[] boundNodes = [];
    private int[] boundNext = [];
    private int boundCount;
    private int[] boundMeets = []; // The meets in the order they first keep a bound.
    private int boundMeetCount;
    private int[] waitFirst = []; // Per meet: the meets whose walks entered it, in order, a list through waitNext (-1: none).
    private int[] waitNodes = [];
    private int[] waitNext = [];
    private int waitCount;
    private int[] lawQueue = []; // The worklist, a ring holding each meet once at a time.
    private bool[] lawQueued = [];
    private int[] choices = []; // A close's bounds, operand i's from choiceStart[i], and the digits of its current choice.
    private int[] choiceStart = [];
    private int[] digits = [];
    private long meetWords;

    // The meet law over the class's edges formed so far (ClassEdges); false when its words are over the limit (D20).
    private bool MeetLaw()
    {
        var (nodes, count) = (this.slice.Atoms.Count, this.classEdgeCount);
        (this.lawNodes, this.meetWords) = (nodes, 0);
        var any = false;
        for (var v = 0; v < nodes && !any; v++)
        {
            any = this.IsMeet(v);
        }

        if (!any)
        {
            return true;
        }

        MirDataflow.Grow(ref this.lawTails, count);
        MirDataflow.Grow(ref this.lawHeads, count);
        for (var e = 0; e < count; e++)
        {
            (this.lawTails[e], this.lawHeads[e]) = (this.classEdges[e].Tail, this.classEdges[e].Head);
        }

        TyCtxt.OriginChains.Index(this.lawTails.AsSpan(0, count), nodes, ref this.lawOutStart, ref this.lawOut, ref this.cursors);
        TyCtxt.OriginChains.Index(this.lawHeads.AsSpan(0, count), nodes, ref this.lawInStart, ref this.lawIn, ref this.cursors);
        this.ResetLaw(nodes);
        var (head, queued) = (0, 0);
        for (var m = 0; m < nodes; m++)
        {
            if (this.IsMeet(m) && this.DerivedIns(m, out _) > 0)
            {
                (this.lawQueue[queued++], this.lawQueued[m]) = (m, true);
            }
        }

        while (queued > 0)
        {
            var m = this.lawQueue[head];
            (head, queued, this.lawQueued[m]) = ((head + 1) % nodes, queued - 1, false);
            if (!this.Close(m, out var gained))
            {
                return false;
            }

            for (var w = gained ? this.waitFirst[m] : -1; w >= 0; w = this.waitNext[w])
            {
                if (this.waitNodes[w] is var waiting && !this.lawQueued[waiting])
                {
                    (this.lawQueue[(head + queued++) % nodes], this.lawQueued[waiting]) = (waiting, true);
                }
            }
        }

        for (var i = 0; i < this.boundMeetCount; i++)
        {
            var m = this.boundMeets[i];
            for (var j = this.lawInStart[m]; j < this.lawInStart[m + 1]; j++)
            {
                var edge = this.classEdges[this.lawIn[j]];
                for (var b = this.OperandEdge(edge) ? -1 : this.boundFirst[m]; b >= 0; b = this.boundNext[b])
                {
                    this.Related(edge.Tail, this.boundNodes[b], edge.Site, edge.Row);
                }
            }
        }

        return true;
    }

    private void ResetLaw(int nodes)
    {
        MirDataflow.Grow(ref this.operandAt, nodes);
        MirDataflow.Grow(ref this.boundFirst, nodes);
        MirDataflow.Grow(ref this.boundLast, nodes);
        MirDataflow.Grow(ref this.waitFirst, nodes);
        MirDataflow.Grow(ref this.lawQueue, nodes);
        MirDataflow.Grow(ref this.lawQueued, nodes);
        this.operandAt.AsSpan(0, nodes).Fill(-1);
        this.boundFirst.AsSpan(0, nodes).Fill(-1);
        this.waitFirst.AsSpan(0, nodes).Fill(-1);
        this.lawQueued.AsSpan(0, nodes).Clear();
        (this.operandCount, this.boundCount, this.boundMeetCount, this.waitCount) = (0, 0, 0, 0);
    }

    // One close of meet m: every operand's bounds, then each choice of one bound per operand, whose canonical meet is kept once; none while
    // an operand has no bound; false when the choices' words are over the limit (D20).
    private bool Close(int m, out bool gained)
    {
        gained = false;
        var ins = this.DerivedIns(m, out var site);
        var (at, operands) = (this.Operands(m, site), this.slice.Atoms[m].Atom.Operands.Count);
        MirDataflow.Grow(ref this.choiceStart, operands + 1);
        MirDataflow.Grow(ref this.digits, operands);
        var (found, product) = (0, 1L);
        for (var i = 0; i < operands; i++)
        {
            this.choiceStart[i] = found;
            found = this.Walk(this.operandNodes[at + i], m, found);
            product = Math.Min(product * (found - this.choiceStart[i]), 1L << 32);
        }

        this.choiceStart[operands] = found;
        if (product == 0)
        {
            return true;
        }

        this.meetWords += product * ins; // C-49 N1: counted before the choices are formed, so none is skipped.
        if (!MirDataflow.Within(this.meetWords))
        {
            return false;
        }

        var kept = ++this.reachStamp; // The walks are done: this close's stamp marks m's kept bounds and each choice it keeps.
        for (var b = this.boundFirst[m]; b >= 0; b = this.boundNext[b])
        {
            this.reachStamps[this.boundNodes[b]] = kept;
            this.work++;
        }

        this.digits.AsSpan(0, operands).Clear();
        for (var k = 0; k < operands;)
        {
            var choice = this.choices[this.choiceStart[0] + this.digits[0]];
            for (var i = 1; i < operands; i++)
            {
                choice = types.MeetAtom(this.key, choice, this.choices[this.choiceStart[i] + this.digits[i]], site);
            }

            this.work += operands; // The fold (operands - 1 MeetAtom calls) and the stamp.
            gained |= this.Keep(m, choice, kept);
            for (k = 0; k < operands && ++this.digits[k] == this.choiceStart[k + 1] - this.choiceStart[k]; k++)
            {
                this.digits[k] = 0;
            }
        }

        return true;
    }

    // An operand's bounds, appended to choices from `found`, each once: the operand when it is Fixed, else each Fixed node its walk meets and
    // each bound of another meet it enters, m waiting on that meet; returns their end.
    private int Walk(int start, int m, int found)
    {
        var atoms = this.slice.Atoms;
        MirDataflow.Grow(ref this.reachStamps, atoms.Count);
        MirDataflow.Grow(ref this.reachQueue, this.lawNodes);
        var (stamp, depth) = (++this.reachStamp, 0);
        if (atoms[start].Kind == OriginAtomKind.Fixed)
        {
            return this.Found(start, stamp, found);
        }

        (this.reachStamps[start], this.reachQueue[depth++]) = (stamp, start);
        while (depth > 0)
        {
            var v = this.reachQueue[--depth];
            for (var i = v < this.lawNodes ? this.lawOutStart[v] : 0; v < this.lawNodes && i < this.lawOutStart[v + 1]; i++)
            {
                var edge = this.classEdges[this.lawOut[i]];
                this.work++;
                if (this.OperandEdge(edge))
                {
                    continue;
                }

                if (atoms[edge.Head].Kind == OriginAtomKind.Fixed)
                {
                    found = this.Found(edge.Head, stamp, found);
                    continue;
                }

                if (this.reachStamps[edge.Head] == stamp)
                {
                    continue;
                }

                this.reachStamps[edge.Head] = stamp;
                if (edge.Head != m && this.IsMeet(edge.Head))
                {
                    this.Wait(edge.Head, m);
                    for (var b = this.boundFirst[edge.Head]; b >= 0; b = this.boundNext[b])
                    {
                        found = this.Found(this.boundNodes[b], stamp, found);
                    }
                }

                this.reachQueue[depth++] = edge.Head;
            }
        }

        return found;
    }

    // A bound of the walk stamped `stamp`, appended once (a Fixed node is never walked past, so its stamp marks it found).
    private int Found(int node, int stamp, int found)
    {
        if (this.reachStamps[node] == stamp)
        {
            return found;
        }

        this.reachStamps[node] = stamp;
        MirDataflow.Grow(ref this.choices, found + 1);
        this.choices[found] = node;
        return found + 1;
    }

    // Keeps a choice as m's bound unless the close's stamp marks it kept (the same interned canonical meet); true when kept.
    private bool Keep(int m, int choice, int stamp)
    {
        MirDataflow.Grow(ref this.reachStamps, choice + 1);
        if (this.reachStamps[choice] == stamp)
        {
            return false;
        }

        this.reachStamps[choice] = stamp;
        var n = this.boundCount++;
        MirDataflow.Grow(ref this.boundNodes, this.boundCount);
        MirDataflow.Grow(ref this.boundNext, this.boundCount);
        (this.boundNodes[n], this.boundNext[n]) = (choice, -1);
        if (this.boundFirst[m] < 0)
        {
            this.boundFirst[m] = n;
            MirDataflow.Grow(ref this.boundMeets, this.boundMeetCount + 1);
            this.boundMeets[this.boundMeetCount++] = m;
        }
        else
        {
            this.boundNext[this.boundLast[m]] = n;
        }

        this.boundLast[m] = n;
        return true;
    }

    // m waits on meet h: a bound h gains closes m again; each waiter once, in order.
    private void Wait(int h, int m)
    {
        var last = -1;
        for (var w = this.waitFirst[h]; w >= 0; (last, w) = (w, this.waitNext[w]))
        {
            if (this.waitNodes[w] == m)
            {
                return;
            }
        }

        var n = this.waitCount++;
        MirDataflow.Grow(ref this.waitNodes, this.waitCount);
        MirDataflow.Grow(ref this.waitNext, this.waitCount);
        (this.waitNodes[n], this.waitNext[n]) = (m, -1);
        if (last < 0)
        {
            this.waitFirst[h] = n;
        }
        else
        {
            this.waitNext[last] = n;
        }
    }

    // m's in-edges that take derived edges (their tails not its operands), and the first one's site.
    private int DerivedIns(int m, out SourceRef site)
    {
        site = default;
        var count = 0;
        for (var j = this.lawInStart[m]; j < this.lawInStart[m + 1]; j++)
        {
            var edge = this.classEdges[this.lawIn[j]];
            if (!this.OperandEdge(edge))
            {
                site = count++ == 0 ? edge.Site : site;
            }
        }

        return count;
    }

    // Whether an edge is an operand's into its own meet (`o outlives (o and ...)`, a fact that bounds nothing).
    private bool OperandEdge(in ClassEdge edge)
    {
        if (!this.IsMeet(edge.Head))
        {
            return false;
        }

        var at = this.Operands(edge.Head, edge.Site);
        return this.operandNodes.AsSpan(at, this.slice.Atoms[edge.Head].Atom.Operands.Count).Contains(edge.Tail);
    }

    // A meet's operand nodes, resolved once at the first site asked; where they start in operandNodes.
    private int Operands(int m, SourceRef site)
    {
        if (this.operandAt[m] < 0)
        {
            var operands = this.slice.Atoms[m].Atom.Operands;
            this.operandAt[m] = this.operandCount;
            MirDataflow.Grow(ref this.operandNodes, this.operandCount + operands.Count);
            for (var i = 0; i < operands.Count; i++)
            {
                this.operandNodes[this.operandCount++] = types.OriginAtom(this.key, operands[i], site);
            }
        }

        return this.operandAt[m];
    }

    // A non-Fixed meet the law closes: an Inferred or Dependent meet node before the law.
    private bool IsMeet(int v)
        => v < this.lawNodes && this.slice.Atoms[v] is { Atom.Kind: OriginKind.Intersection, Kind: not (OriginAtomKind.Fixed or OriginAtomKind.Unclassed) };
}
