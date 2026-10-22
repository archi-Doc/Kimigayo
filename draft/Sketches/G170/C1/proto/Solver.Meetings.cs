// G170 C1 Solver Meetings concern: request-local matching over the previous round's classes and rows.
// Inputs: immutable declaration indexes and the current round snapshot. Outputs: candidate matches and staged needs/effects.
// Invariants: no row fires here; EGraph identity is never decomposed; semantic changes are published by the round owner.
#pragma warning disable
#nullable enable

namespace G170C1;

public sealed partial class Solver
{
    // Two classes meet (§1 (b)) through a meeting row per class pair: the requester visits both classes (their merge wakes it) and
    // joins the row's requesters, whose tags the row serves; the row is answered once per round when new or woken. A new requester
    // or tag is work of the round. Two classes neither of which can gain a node need no row.
    private void Meet(int c, int other, MatchCtx ctx)
    {
        c = this.g.Find(c);
        other = this.g.Find(other);
        if (c == other || ctx.Owner is not { } owner || ctx.Fx is not { } fx)
        {
            return;
        }

        ctx.Visit(c);
        ctx.Visit(other);
        if (!this.MayGain(c) && !this.MayGain(other))
        {
            return; // neither class can gain a node: they never become one class
        }

        var key = (Math.Min(c, other), Math.Max(c, other));
        if (!this.meetings.TryGetValue(key, out var row))
        {
            this.meetings[key] = row = new MeetRow(key.Item1, key.Item2);
        }

        if (row == owner)
        {
            return; // a pair that needs the meeting itself is that meeting's own row
        }

        if (row.Owners.Add(owner) | (row.TagsValue | ctx.Tags) != row.TagsValue)
        {
            row.TagsValue |= ctx.Tags;
            fx.Meetings++;
            fx.MeetTags |= ctx.Tags;
        }

        if (!row.Selected && !row.Queued)
        {
            row.Queued = true;
            this.meetQueue.Add(row);
        }
    }

    // A meeting row's answer: the nodes of the two classes, real or virtual, meet as nodes (§1): each node of one class probes the
    // other's virtual nodes through the index by its head and kid heads and agrees with each found (Agree: one Type, or its kids
    // meet); same-head real nodes meet at their kids (upward congruence), as kid meetings.
    private void MeetClasses(MeetRow row, MatchCtx ctx)
    {
        var c = this.g.Find(row.A);
        var other = this.g.Find(row.B);
        if (c == other)
        {
            return;
        }

        ctx.Visit(c);
        ctx.Visit(other);
        this.MeetOneWay(c, other, ctx);
        this.MeetOneWay(other, c, ctx);

        // Virtual nodes meet virtual nodes at the heads the index can give both classes' virtual nodes, in both directions.
        var shared = this.IndexHeads(c);
        shared.IntersectWith(this.IndexHeads(other));
        this.MeetVirtuals(c, other, shared, ctx);
        this.MeetVirtuals(other, c, shared, ctx);

        foreach (var n in this.g.NodesOf(c).ToArray())
        {
            foreach (var o in this.g.NodesOf(other).ToArray())
            {
                if (this.g.KidCount(n) != 0 && this.g.KidCount(n) == this.g.KidCount(o) && this.g.HeadOf(n).Equals(this.g.HeadOf(o)))
                {
                    for (var i = 0; i < this.g.KidCount(n); i++)
                    {
                        this.work++;
                        this.Meet(this.g.KidOf(n, i), this.g.KidOf(o, i), ctx);
                    }
                }
            }
        }
    }

    // Whether class c can gain a node in this request: it can have a virtual node, a virtual node can have one of its heads, or a
    // kid of one of its nodes can gain one (upward congruence). Two classes neither of which can gain one never become one class.
    // Once per round; a class reached again while in progress counts as changing (a cycle is never pruned).
    private bool MayGain(int c)
    {
        c = this.g.Find(c);
        if (this.mayGain.TryGetValue(c, out var known))
        {
            return known;
        }

        this.mayGain[c] = true;
        var heads = this.HeadsOf(c);
        var result = this.Shapes(c).Count != 0 || this.anyVirtualHeads.Contains(HeadKey.Any) || heads.Overlaps(this.anyVirtualHeads);
        foreach (var node in this.g.NodesOf(c))
        {
            for (var i = 0; !result && i < this.g.KidCount(node); i++)
            {
                result = this.MayGain(this.g.KidOf(node, i));
            }
        }

        return this.mayGain[c] = result;
    }

    // b's virtual nodes meet a's as nodes: each probes a's virtual nodes through the index by its head and kid heads (an index join,
    // never a loop over both lists) and agrees with each found, both rows expanding when they are one Type. A virtual node whose other
    // side is a variable (an alias: an existing class; a free variable: any Type) meets real nodes only (MeetOneWay).
    private void MeetVirtuals(int a, int b, HashSet<HeadKey> heads, MatchCtx ctx)
    {
        foreach (var w in heads.SelectMany(h => this.Virtuals(b, h, ctx)))
        {
            if (w.Other.Head is VarHead)
            {
                continue;
            }

            this.work++;
            var k0 = w.Other.Kids.Length > 0 ? this.KidHeads(w.Other.Kids[0], w.Beta) : null;
            var k1 = w.Other.Kids.Length > 1 ? this.KidHeads(w.Other.Kids[1], w.Beta) : null;
            foreach (var u in this.Virtuals(a, HeadKey.Of(w.Other), ctx, k0, k1))
            {
                if (u.Other.Head is not VarHead)
                {
                    this.Expand(u, w.Other, w.Beta, ctx);
                    this.Expand(w, u.Other, u.Beta, ctx);
                }
            }
        }
    }

    // The heads the index can give c's virtual nodes whose other side is not a variable: rows whose side can have a head of c (or is
    // a variable), chainable rows whose side can have a head of c's virtual nodes, c's chains. Once per round.
    private HashSet<HeadKey> IndexHeads(int c)
    {
        c = this.g.Find(c);
        if (!this.indexHeads.TryGetValue(c, out var result))
        {
            var heads = this.HeadsOf(c);
            result = new HashSet<HeadKey>();
            foreach (var h in heads.Append(HeadKey.Any))
            {
                result.UnionWith(this.otherHeads.GetValueOrDefault(h) ?? []);
            }

            foreach (var h in heads.Append(HeadKey.Any).Concat(this.VirtualHeads(heads)))
            {
                result.UnionWith(this.chainOtherHeads.GetValueOrDefault(h) ?? []);
            }

            result.UnionWith(this.chains.GetValueOrDefault(c)?.Select(vs => HeadKey.Of(vs.Other)) ?? []);
            result.Remove(HeadKey.Any);
            this.indexHeads[c] = result;
        }

        return new HashSet<HeadKey>(result);
    }

    private static bool IsAlias(Virtual vs) => vs.Via is null && vs.Other.Head is VarHead v && vs.Beta[v.Index] is not null;

    // A real node of one class meets the virtual nodes of the other whose other side has its head (and can meet its kids).
    private void MeetOneWay(int withVirtual, int other, MatchCtx ctx)
    {
        var shapes = this.Shapes(withVirtual);
        if (shapes.Count == 0)
        {
            return; // no row can give withVirtual a virtual node (a static index fact)
        }

        foreach (var node in this.g.NodesOf(other).ToArray())
        {
            if (!shapes.Contains(HeadKey.Any) && !shapes.Contains(new(this.g.HeadOf(node), this.g.KidCount(node))))
            {
                continue;
            }

            this.work++;
            var kid0 = this.g.KidCount(node) > 0 ? this.KidShapes(this.g.KidOf(node, 0)) : null;
            var kid1 = this.g.KidCount(node) > 1 ? this.KidShapes(this.g.KidOf(node, 1)) : null;
            foreach (var vs in this.Virtuals(withVirtual, new(this.g.HeadOf(node), this.g.KidCount(node)), ctx, kid0, kid1))
            {
                this.ExpandAt(vs, node, ctx);
            }
        }
    }

    // A virtual node meets a real node of its head as a node: it agrees with the node's kid classes (a free variable other side takes
    // the node's class; an alias expands).
    private void ExpandAt(Virtual vs, int node, MatchCtx ctx)
    {
        this.work++;
        if (vs.Via is { } via)
        {
            this.Expand(via, vs.ViaPattern!, vs.ViaBeta!, ctx);
            return;
        }

        var bind = (int?[])vs.Beta.Clone();
        var shapes = new Dictionary<int, Head>();
        var meets = new List<(int, int)>();
        if (vs.Other.Head is VarHead && !IsAlias(vs))
        {
            this.AgreeClass(vs.Other, bind, this.g.ClassOfNode(node), meets);
        }
        else if (!IsAlias(vs))
        {
            for (var i = 0; i < vs.Other.Kids.Length; i++)
            {
                this.AgreeClass(vs.Other.Kids[i], bind, this.g.KidOf(node, i), meets);
            }
        }

        this.Settle(vs, bind, shapes, meets, ctx);
    }

}
