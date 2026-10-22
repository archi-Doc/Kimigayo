// G170 C1 Solver Candidates concern: request-local matching over the previous round's classes and rows.
// Inputs: immutable declaration indexes and the current round snapshot. Outputs: candidate matches and staged needs/effects.
// Invariants: no row fires here; EGraph identity is never decomposed; semantic changes are published by the round owner.
#pragma warning disable
#nullable enable

namespace G170C1;

public sealed partial class Solver
{
    // VirtualHeads of a class's own heads, once per round (the classes do not change within a round).
    private HashSet<HeadKey> Shapes(int c)
    {
        c = this.g.Find(c);
        if (!this.shapes.TryGetValue(c, out var result))
        {
            this.shapes[c] = result = this.VirtualHeads(this.HeadsOf(c));
        }

        return result;
    }

    // The heads a virtual node of c can have (a static over-approximation from c's heads); HeadKey.Any means any head. An alias
    // side (its other side a variable the side binds) that can match c, directly or through a virtual node, makes c possibly the
    // class it binds: any head.
    private HashSet<HeadKey> VirtualHeads(HashSet<HeadKey> heads)
    {
        var result = new HashSet<HeadKey>();
        foreach (var h in heads.Append(HeadKey.Any))
        {
            if (this.reach.TryGetValue(h, out var others))
            {
                result.UnionWith(others);
            }
        }

        if (heads.Overlaps(this.aliasHeads) || result.Overlaps(this.chainableAliasHeads))
        {
            result.Add(HeadKey.Any);
        }

        return result;
    }

    // The virtual nodes of c whose other side can have the needed head (HeadKey.Any: all) and kids the requester can meet (o0, o1:
    // the heads its pattern's or node's kids 0 and 1 can have; null: any), computed where needed, never stored. One rule for every
    // identity side, whichever side mentions the row's subject:
    // - a direct row: a side whose head is a head of c (or a variable) e-matches c; its other side is a virtual node of c;
    // - a chain row: a chainable side that meets c only through a virtual node of c (at the top, or at a kid of one of c's nodes)
    //   makes its row's other side a virtual node of c too, forwarding to that node's row (chains forward to their base).
    // Candidates are grouped by their side Term, so rows sharing a side (every binding whose value is i32) cost one match. The
    // matches visit classes for the requester (so their merges wake it) without expanding anything.
    private List<Virtual> VirtualsAt(int c, HeadKey need, MatchCtx requester, HashSet<HeadKey>? o0 = null, HashSet<HeadKey>? o1 = null)
    {
        c = this.g.Find(c);
        var result = new List<Virtual>();
        var heads = this.HeadsOf(c);

        // Chains are detected for the need and for the needs its candidates meet (two levels; deeper lookups return direct rows
        // and stored chains only) and stored as request rows, so a chain through a chain through a chain appears a round later:
        // bounded work per lookup, and every need a lookup meets gets its own chains.
        var chain = this.chainGuard.Count < 2 && this.chainGuard.Add((c, need));
        if (!chain && this.chainGuard.Count >= 2)
        {
            this.needLog?.Add((c, need));
            if (requester.Fx is { } pending && requester.Owner is { } owner)
            {
                pending.Needs.Add((c, need, owner)); // a need row: answered as a top-level lookup next round
            }
        }

        if (this.chains.TryGetValue(c, out var stored))
        {
            result.AddRange(stored.Where(vs => (need.IsAny || vs.Other.Head is VarHead || HeadKey.Of(vs.Other) == need) && Passes(KidAt(vs.Other, 0), o0) && Passes(KidAt(vs.Other, 1), o1)));
        }

        try
        {
            var found = new Dictionary<Term, (List<EqSide> Entries, bool Direct)>();
            foreach (var h in heads.Append(HeadKey.Any))
            {
                // A side with a head of c meets one of c's nodes of that head: its kids can have those nodes' kids' heads.
                if (this.otherHeads.TryGetValue(h, out var others))
                {
                    HashSet<HeadKey>?[] filters = [o0, o1, this.SideKids(c, h, 0), this.SideKids(c, h, 1)];
                    foreach (var other in others)
                    {
                        if (need.IsAny || other == need || other.IsAny)
                        {
                            this.sides[(other, h)].Find(filters, (side, entries) => Found(side, entries, true));
                        }
                    }
                }
            }

            if (chain)
            {
                // Chain-only candidates: chainable sides with a head c or one of its virtual nodes can have.
                HashSet<HeadKey>?[] filters = [o0, o1, null, null];
                foreach (var h in heads.Append(HeadKey.Any).Concat(this.VirtualHeads(heads)).ToHashSet())
                {
                    if (this.chainOtherHeads.TryGetValue(h, out var others))
                    {
                        foreach (var other in others)
                        {
                            if (need.IsAny || other == need || other.IsAny)
                            {
                                this.chainable[(other, h)].Find(filters, (side, entries) => Found(side, entries, false));
                            }
                        }
                    }
                }
            }

            var groups = new Dictionary<Term, (List<int?[]> Results, List<(Virtual Via, Term Pattern, int?[] Beta)> Hits)>();
            var memo = new Dictionary<(int, HeadKey, string), List<Virtual>>();
            foreach (var (side, (entries, direct)) in found)
            {
                this.Candidate(side, entries, c, requester, chain, direct, groups, memo, result);
            }

            // Each side lies in one leaf per (other head, path): finds under several keys add up. A direct find of a side already
            // holds its chainable entries (the same filters bar the same other sides).
            void Found(Term side, List<EqSide> entries, bool direct)
            {
                if (!found.TryGetValue(side, out var known))
                {
                    found[side] = (new List<EqSide>(entries), direct);
                }
                else if (known.Direct == direct)
                {
                    known.Entries.AddRange(entries);
                }
            }
        }
        finally
        {
            if (chain)
            {
                this.chainGuard.Remove((c, need));
            }
        }

        var seen = new HashSet<string>();
        return result.Where(vs => seen.Add(this.VirtualKey(vs))).ToList(); // a set: stored and newly found chains are one
    }

    private static bool Passes(HeadKey kid, HashSet<HeadKey>? filter) => filter is null || kid.IsAny || filter.Contains(kid) || filter.Contains(HeadKey.Any);

    // The heads kid i of a side with head h can have to meet one of c's nodes of that head: those of the nodes' kids and of their
    // virtual nodes (null: any).
    private HashSet<HeadKey>? SideKids(int c, HeadKey h, int i)
    {
        if (h.IsAny || h.Arity <= i)
        {
            return null;
        }

        var result = new HashSet<HeadKey>();
        foreach (var node in this.g.NodesOf(c))
        {
            if (this.g.KidCount(node) == h.Arity && this.g.HeadOf(node).Equals(h.Head))
            {
                result.UnionWith(this.KidShapes(this.g.KidOf(node, i)));
            }
        }

        return result.Contains(HeadKey.Any) ? null : result;
    }

    // The heads a class can have once its virtual nodes are expanded: its own and its virtual nodes' (HeadKey.Any: any).
    private HashSet<HeadKey> KidShapes(int c)
    {
        var heads = this.HeadsOf(c);
        heads.UnionWith(this.VirtualHeads(heads));
        return heads;
    }

    // VirtualsAt for a requester: a lookup outside any other lookup is a function of (class, need, kid filters) and the round's
    // state, so it is answered once per round and replayed (its visits and Resource reads) for later requesters.
    private List<Virtual> Virtuals(int c, HeadKey need, MatchCtx requester, HashSet<HeadKey>? o0 = null, HashSet<HeadKey>? o1 = null)
    {
        if (this.chainGuard.Count != 0)
        {
            return this.VirtualsAt(c, need, requester, o0, o1);
        }

        var key = (this.g.Find(c), need, this.SetKey(o0) + "|" + this.SetKey(o1));
        if (!this.roundMemo.TryGetValue(key, out var entry))
        {
            this.visitLog = new();
            this.needLog = new();
            this.resourceSeen = false;
            this.chainSeen = false;
            var result = this.VirtualsAt(c, need, requester, o0, o1);
            entry = (result, this.visitLog, this.needLog, this.resourceSeen, this.chainSeen);
            this.roundMemo[key] = entry;
            this.visitLog = null;
            this.needLog = null;
            return result;
        }

        // Replayed for a later requester: what the lookup did on the first requester's behalf is done on this one's too.
        foreach (var v in entry.Visits)
        {
            requester.Visit(v);
        }

        if (requester.Fx is { } fx && requester.Owner is { } owner)
        {
            foreach (var (nc, nn) in entry.Needs)
            {
                fx.Needs.Add((nc, nn, owner));
            }

            if (entry.Chains)
            {
                fx.ChainTags |= requester.Tags;
            }
        }

        if (entry.Resource)
        {
            this.resourceTags |= requester.Tags;
        }

        return entry.Result;
    }

    // An order-free text key for a set of heads (heads are numbered on first sight; the numbers only name them).
    private string SetKey(HashSet<HeadKey>? heads)
    {
        if (heads is null)
        {
            return "*";
        }

        var ids = new int[heads.Count];
        var i = 0;
        foreach (var h in heads)
        {
            if (!this.headIds.TryGetValue(h, out var id))
            {
                this.headIds[h] = id = this.headIds.Count;
            }

            ids[i++] = id;
        }

        Array.Sort(ids);
        return string.Join(",", ids);
    }

    // Rows sharing a side are matched once; a side that meets c neither directly nor through a virtual node costs one step.
    private void Candidate(Term side, List<EqSide> entries, int c, MatchCtx requester, bool chain, bool direct, Dictionary<Term, (List<int?[]> Results, List<(Virtual Via, Term Pattern, int?[] Beta)> Hits)> groups, Dictionary<(int, HeadKey, string), List<Virtual>> memo, List<Virtual> result)
    {
        this.work++;
        if (!groups.TryGetValue(side, out var group))
        {
            var results = new List<int?[]>();
            var ctx = requester.Derive(chain ? MatchMode.Chain : MatchMode.None, memo);
            this.Match(side, c, new int?[entries[0].SideVars], results, ctx);
            groups[side] = group = (Distinct(results).ToList(), ctx.Chain ?? []);
        }

        if ((!direct || group.Results.Count == 0) && group.Hits.Count == 0)
        {
            return;
        }

        foreach (var e in entries)
        {
            this.work++;
            if (this.Usable(e.T, requester.Tags))
            {
                this.Produce(e, c, requester, direct, group, result);
            }
        }
    }

    private void Produce(EqSide e, int c, MatchCtx requester, bool direct, (List<int?[]> Results, List<(Virtual Via, Term Pattern, int?[] Beta)> Hits) group, List<Virtual> result)
    {
        if (direct)
        {
            foreach (var r in group.Results)
            {
                var beta = new int?[e.T.VarCount];
                Array.Copy(r, beta, r.Length);
                result.Add(new Virtual(e.T, beta, e.Other, c));
            }
        }

        foreach (var (hit, pattern, beta) in group.Hits)
        {
            // The base may be another instance of this very row (its other side matched c): the chain bootstraps that instance,
            // after which this side matches c directly (`A.(K2).Y is List<i32>` joins T's and U's projections). When matching this
            // side fixes the other one, that instance is the base itself: no chain.
            if (e.Trivial && hit.Via is null && hit.T == e.T && ReferenceEquals(hit.Other, e.Side) && ReferenceEquals(pattern, e.Side))
            {
                continue;
            }

            var via = hit.Via ?? hit;
            var vs = new Virtual(e.T, new int?[e.T.VarCount], e.Other, c) { Via = via, ViaPattern = hit.Via is null ? pattern : hit.ViaPattern!, ViaBeta = hit.Via is null ? beta : hit.ViaBeta! };
            result.Add(vs);
            if (requester.Fx is { } fx && !(this.chains.TryGetValue(c, out var stored) && this.StoredKeys(c, stored).Contains(this.VirtualKey(vs))))
            {
                fx.Chains.Add(vs); // stored when the round is applied: a later lookup finds chains through it
                fx.ChainTags |= requester.Tags;
                this.chainSeen = true;
            }
        }
    }

    // A chain row's content: its template, other side, canonical bindings and its base's content; equal rows from classes that
    // merged are one row. Keys are remembered until a merge changes the canonical classes (AfterApply).
    private string VirtualKey(Virtual vs)
    {
        if (!this.keys.TryGetValue(vs, out var key))
        {
            this.keys[vs] = key = this.VirtualKeyOf(vs);
        }

        return key;
    }

    private string VirtualKeyOf(Virtual vs)
        => vs.T.Id + "/" + vs.Other.Id + "/" + string.Join(",", vs.Beta.Select(b => b is int i ? this.g.Find(i) : -1)) +
            (vs.Via is { } via ? "<" + vs.ViaPattern!.Id + "/" + string.Join(",", vs.ViaBeta!.Select(b => b is int i ? this.g.Find(i) : -1)) + "<" + this.VirtualKey(via) : string.Empty);

    // The keys of a class's stored chains (kept with the chains until a merge).
    private HashSet<string> StoredKeys(int c, List<Virtual> stored)
    {
        if (!this.storedKeys.TryGetValue(c, out var set))
        {
            this.storedKeys[c] = set = stored.Select(this.VirtualKey).ToHashSet();
        }

        return set;
    }

}
