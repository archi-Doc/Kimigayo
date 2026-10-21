// G170 C1 matching owner: select existing clause instances modulo request-local identity classes.
// Inputs: immutable template indexes, published round state, requested patterns/classes and evidence view.
// Outputs: binding alternatives and staged subscription, class, chain, need and meeting effects.
// Invariants: matching supplies no evidence; only firing supported existing templates does. Identity is never decomposed.
// Prohibitions: no production Binding writes, guessed Types, cross-request semantic cache or separate scheduler.
#pragma warning disable
#nullable enable

namespace G170C1;

public sealed partial class Solver
{
    // One index over every positive identity side, whichever side mentions the row's subject: (other side's head, side's head),
    // then the heads of (other kid0, other kid1, side kid0, side kid1), rows grouped by side Term. `chainable` holds the sides a
    // virtual node can meet (a chain); `byShape` the bound-expansion entries; negated rows serve only an identity goal's negation.
    private readonly Dictionary<(HeadKey Other, HeadKey Side), SideTrie> sides = new();
    private readonly Dictionary<HeadKey, HashSet<HeadKey>> otherHeads = new();
    private readonly Dictionary<(HeadKey Other, HeadKey Side), SideTrie> chainable = new();
    private readonly Dictionary<HeadKey, HashSet<HeadKey>> chainOtherHeads = new();
    private readonly Dictionary<(HeadKey Head, HeadKey Kid0, HeadKey Kid1), List<ShapeEntry>> byShape = new();
    private readonly Dictionary<(HeadKey Side, HeadKey Kid0), List<EqSide>> negatedSides = new();
    private readonly Dictionary<HeadKey, HashSet<HeadKey>> reach = new();
    private readonly HashSet<HeadKey> aliasHeads = new();
    private readonly HashSet<HeadKey> chainableAliasHeads = new();
    private readonly HashSet<HeadKey> anyVirtualHeads = new();
    private readonly Dictionary<int, bool> mayGain = new();
    private readonly Dictionary<int, HashSet<HeadKey>> indexHeads = new();
    private readonly HashSet<(int Class, HeadKey Need)> chainGuard = new();
    private readonly Dictionary<(int, HeadKey, string), (List<Virtual> Result, List<int> Visits, List<(int, HeadKey)> Needs, bool Resource, bool Chains)> roundMemo = new();
    private List<int>? visitLog;
    private List<(int Class, HeadKey Need)>? needLog;
    private bool chainSeen;
    private readonly Dictionary<HeadKey, int> headIds = new();
    private readonly Dictionary<int, HashSet<HeadKey>> shapes = new();
    private bool resourceSeen;
    private HashSet<(int T, int Class, string Bind, Row Owner, byte Sign)> expansions = new();
    private Dictionary<int, List<Virtual>> chains = new();
    private readonly Dictionary<Virtual, string> keys = new();
    private readonly Dictionary<int, HashSet<string>> storedKeys = new();
    private static void AddTo(Dictionary<HeadKey, HashSet<HeadKey>> index, HeadKey key, HeadKey value)
    {
        if (!index.TryGetValue(key, out var set))
        {
            index[key] = set = new();
        }

        set.Add(value);
    }

    private static SideTrie Trie(Dictionary<(HeadKey Other, HeadKey Side), SideTrie> index, Dictionary<HeadKey, HashSet<HeadKey>> heads, EqSide e)
    {
        var key = (HeadKey.Of(e.Other), HeadKey.Of(e.Side));
        if (!index.TryGetValue(key, out var trie))
        {
            index[key] = trie = new();
        }

        AddTo(heads, key.Item2, key.Item1);
        return trie;
    }

    // Whether two row terms can denote one Type: a variable of either row matches anything (rows are renamed apart).
    private static bool StaticUnify(Term a, Term b)
        => a.Head is VarHead || b.Head is VarHead || (a.Head.Equals(b.Head) && a.Kids.Length == b.Kids.Length && a.Kids.Zip(b.Kids).All(k => StaticUnify(k.First, k.Second)));

    // Bound expansion entries (§1 (c)): an identity row is requested on behalf of a class demand matching one side when a node of the
    // other, non-variable side exists. A row whose sides' variables are nested (one side's variables include the other's) is
    // indexed once, by the shape of the side that fixes every variable (the more selective one when both do): scanning one of its
    // nodes instantiates the other side, and the row serves the demands of both classes. Otherwise each non-variable side is scanned
    // and the other side is matched against the active class demands.
    private void AddShapeEntries(Template t, Term p, Term q)
    {
        var vp = new HashSet<int>();
        var vq = new HashSet<int>();
        p.Vars(vp);
        q.Vars(vq);
        var pKey = p.Head is not VarHead && vq.IsSubsetOf(vp);
        var qKey = q.Head is not VarHead && vp.IsSubsetOf(vq);
        if (pKey && qKey)
        {
            pKey = Selectivity(p) >= Selectivity(q);
            qKey = !pKey;
        }

        if (pKey || qKey)
        {
            var (scan, side) = pKey ? (p, q) : (q, p);
            Add(scan, new ShapeEntry(t, scan, side, true));
            return;
        }

        if (q.Head is not VarHead)
        {
            Add(q, new ShapeEntry(t, q, p, false));
        }

        if (p.Head is not VarHead)
        {
            Add(p, new ShapeEntry(t, p, q, false));
        }

        void Add(Term scan, ShapeEntry entry)
        {
            var key = (HeadKey.Of(scan), KidAt(scan, 0), KidAt(scan, 1));
            if (!this.byShape.TryGetValue(key, out var list))
            {
                this.byShape[key] = list = new();
            }

            list.Add(entry);
        }

        static int Selectivity(Term t) => (KidAt(t, 0).IsAny ? 0 : 1) + (KidAt(t, 1).IsAny ? 0 : 1);
    }

    private static HeadKey KidAt(Term t, int i) => t.Kids.Length > i ? HeadKey.Of(t.Kids[i]) : HeadKey.Any;

    // A static row index (request independent): the heads a fact of K can have at each position (0: the subject, i: the i-th
    // argument), in its identity class. Refinement and clause rows inherit their antecedent's positions through variables; identity
    // rows connect the heads of their two sides; a variable that no antecedent position fixes admits any head. Expansion skips a
    // virtual node whose requester needs, at a variable's position, a head no fact of the antecedent Contract can have there.
    private void SubjectHeads()
    {
        foreach (var d in this.m.Decls)
        {
            for (var i = 0; i <= d.Arity; i++)
            {
                this.positionHeads[(d, i)] = new();
            }
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var t in this.m.Templates)
            {
                var c = t.Conclusion;
                if (c.Kind == FK.Is && this.m.DeclOf(c.B!) is { } k)
                {
                    for (var position = 0; position <= k.Arity; position++)
                    {
                        var pattern = position == 0 ? c.A! : c.B!.Kids[position - 1];
                        var key = (k, position);
                        if (pattern.Head is VarHead v)
                        {
                            if (t.Antecedent is { } a && v.Index <= a.Arity)
                            {
                                changed |= this.positionHeads[key].UnionWithChanged(this.positionHeads[(a, v.Index)]);
                                if (this.anyHead.Contains((a, v.Index)))
                                {
                                    changed |= this.anyHead.Add(key);
                                }
                            }
                            else
                            {
                                changed |= this.anyHead.Add(key);
                            }
                        }
                        else
                        {
                            changed |= this.positionHeads[key].Add(pattern.Head);
                        }
                    }
                }
                else if (c.Kind == FK.Eq)
                {
                    var l = c.A!.Head is VarHead ? null : c.A!.Head;
                    var r = c.B!.Head is VarHead ? null : c.B!.Head;
                    foreach (var (key, heads) in this.positionHeads)
                    {
                        var any = this.anyHead.Contains(key);
                        if (l is null || r is null)
                        {
                            if (any || (l is not null && heads.Contains(l)) || (r is not null && heads.Contains(r)))
                            {
                                changed |= this.anyHead.Add(key);
                            }
                        }
                        else
                        {
                            if (heads.Contains(l) || any)
                            {
                                changed |= heads.Add(r);
                            }

                            if (heads.Contains(r) || any)
                            {
                                changed |= heads.Add(l);
                            }
                        }
                    }
                }
            }
        }
    }

    private HashSet<HeadKey> HeadsOf(int c)
    {
        var heads = new HashSet<HeadKey>();
        foreach (var node in this.g.NodesOf(c))
        {
            heads.Add(new(this.g.HeadOf(node), this.g.KidCount(node)));
        }

        return heads;
    }

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

    // Bound expansion (§1 (c)): an existing node with the shape of an identity row's non-variable side binds the row; a side whose
    // class has an active class demand gets the row requested on its behalf when the other side's node exists (finite: no
    // enumeration). Found through the index by (head, kid0 head, kid1 head). New nodes are scanned each round, all nodes after a merge
    // or when the active class demands change; an instance whose other side has no node yet waits for a node of that side's head.
    private void EagerExpand(Effects fx, byte active)
    {
        var changed = this.mergedSinceScan;
        foreach (var c in this.idents.Values)
        {
            var now = (byte)(c.Tags & active);
            changed |= now != c.ScannedTags;
            c.ScannedTags = now;
        }

        if (changed)
        {
            this.waiting.Clear();
        }

        var total = this.g.NodeTotal;
        var probe = new MatchCtx(this, null, 0, null);
        for (var node = changed ? 0 : this.eagerScanned; node < total; node++)
        {
            var nodeClass = this.g.ClassOfNode(node);
            var head = new HeadKey(this.g.HeadOf(node), this.g.KidCount(node));
            if (this.waiting.Remove(head, out var waiters))
            {
                foreach (var (entry, beta, scanned) in waiters)
                {
                    this.work++;
                    this.BindShape(entry, beta, scanned, fx, active, probe);
                }
            }

            var kid0 = this.g.KidCount(node) > 0 ? this.HeadsOf(this.g.KidOf(node, 0)) : [];
            var kid1 = this.g.KidCount(node) > 1 ? this.HeadsOf(this.g.KidOf(node, 1)) : [];
            foreach (var k0 in kid0.Append(HeadKey.Any))
            {
                foreach (var k1 in kid1.Append(HeadKey.Any))
                {
                    if (!this.byShape.TryGetValue((head, k0, k1), out var entries))
                    {
                        continue;
                    }

                    foreach (var entry in entries)
                    {
                        // The scanned node itself binds the side (its class's other nodes are scanned in turn).
                        this.work++;
                        var results = new List<int?[]> { new int?[entry.T.VarCount] };
                        for (var i = 0; i < entry.Scan.Kids.Length && results.Count != 0; i++)
                        {
                            var next = new List<int?[]>();
                            foreach (var b in results)
                            {
                                this.Match(entry.Scan.Kids[i], this.g.KidOf(node, i), b, next, probe);
                            }

                            results = next;
                        }

                        foreach (var beta in Distinct(results))
                        {
                            this.BindShape(entry, beta, nodeClass, fx, active, probe);
                        }
                    }
                }
            }
        }

        this.eagerScanned = total;
        this.mergedSinceScan = false;
    }

    // A bound-expansion instance: the scanned side's node exists. Both: the other side is fully bound; when its node exists, the row
    // serves its class demand, and the scanned class's when that side is not a variable. Otherwise the other side is matched against
    // each active class demand.
    private void BindShape(ShapeEntry entry, int?[] beta, int scanned, Effects fx, byte active, MatchCtx probe)
    {
        scanned = this.g.Find(scanned);
        if (entry.Both)
        {
            var other = entry.Side.Head is VarHead v ? beta[v.Index] ?? -1 : this.ClassOf(entry.Side, beta);
            if (other < 0)
            {
                var key = HeadKey.Of(entry.Side);
                if (!this.waiting.TryGetValue(key, out var list))
                {
                    this.waiting[key] = list = new();
                }

                list.Add((entry, beta, scanned));
                return;
            }

            other = this.g.Find(other);
            if (other != scanned)
            {
                Offer(this.idents.GetValueOrDefault(other), beta);
                if (entry.Side.Head is not VarHead)
                {
                    Offer(this.idents.GetValueOrDefault(scanned), beta);
                }
            }

            return;
        }

        foreach (var owner in this.idents.Values)
        {
            if ((owner.Tags & active) == 0 || this.g.Find(owner.Class) == scanned)
            {
                continue;
            }

            this.work++;
            var bound = new List<int?[]>();
            this.Match(entry.Side, owner.Class, beta, bound, probe);
            foreach (var b in Distinct(bound))
            {
                Offer(owner, b);
            }
        }

        void Offer(ClassDemand? owner, int?[] b)
        {
            var tags = (byte)((owner?.Tags ?? 0) & active);
            if (tags == 0 || !this.Usable(entry.T, tags))
            {
                return;
            }

            var key = (entry.T.Id, this.g.Find(owner!.Class), BindKey(b), (Row)owner, Both);
            if (this.expansions.Add(key))
            {
                this.Request(entry.T, b, tags, Both, owner, fx);
            }
        }
    }

    // The argument classes of a Contract reference class (its node with the declaration's head).
    private int[]? RefArgs(int refClass, ContractDecl decl)
    {
        foreach (var node in this.g.NodesOf(refClass))
        {
            if (this.g.HeadOf(node).Equals(decl.RefHead) && this.g.KidCount(node) == decl.Arity)
            {
                return Enumerable.Range(0, decl.Arity).Select(i => this.g.KidOf(node, i)).ToArray();
            }
        }

        return null;
    }

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

    private void Match(Term p, int c, int?[] beta, List<int?[]> results, MatchCtx ctx)
    {
        this.work++;
        c = this.g.Find(c);
        ctx.Visit(c);
        if (p.Head is VarHead v)
        {
            if (beta[v.Index] is int bound)
            {
                if (this.g.Find(bound) == c)
                {
                    results.Add(beta);
                }
                else if (ctx.Mode == MatchMode.Expand)
                {
                    // A bound variable needs its class's members in c: the two classes meet, as an identity goal's do.
                    this.Meet(c, this.g.Find(bound), ctx);
                }
            }
            else
            {
                var nb = (int?[])beta.Clone();
                nb[v.Index] = c;
                results.Add(nb);
            }

            return;
        }

        if (!p.HasVar)
        {
            var gc = this.g.LookupTerm(p);
            if (gc >= 0 && this.g.Find(gc) == c)
            {
                results.Add(beta);
            }
            else
            {
                // A ground Type joins c if a virtual node of c has its shape (the expansion is fully bound by the ground Type), or
                // by upward congruence if a node of c with its head has kids that each join its kids: their needs go down.
                this.ThroughVirtuals(p, c, beta, ctx);
                if (ctx.Mode != MatchMode.None)
                {
                    foreach (var node in this.g.NodesOf(c).ToArray())
                    {
                        if (this.g.KidCount(node) == p.Kids.Length && p.Kids.Length != 0 && this.g.HeadOf(node).Equals(p.Head))
                        {
                            this.work++;
                            for (var i = 0; i < p.Kids.Length; i++)
                            {
                                this.Match(p.Kids[i], this.g.KidOf(node, i), beta, new List<int?[]>(), ctx);
                            }
                        }
                    }
                }
            }

            return;
        }

        foreach (var node in this.g.NodesOf(c).ToArray())
        {
            this.work++;
            if (this.g.KidCount(node) != p.Kids.Length || !this.g.HeadOf(node).Equals(p.Head))
            {
                continue;
            }

            var partial = new List<int?[]> { beta };
            for (var i = 0; i < p.Kids.Length && partial.Count != 0; i++)
            {
                var next = new List<int?[]>();
                foreach (var b in partial)
                {
                    this.Match(p.Kids[i], this.g.KidOf(node, i), b, next, ctx);
                }

                partial = next;
            }

            results.AddRange(partial);
        }

        this.ThroughVirtuals(p, c, beta, ctx);
    }

    // The pattern needs nodes of its head in c that only a virtual node of c can provide: a requester expands them; a chain
    // probe records them.
    private void ThroughVirtuals(Term p, int c, int?[] beta, MatchCtx ctx)
    {
        if (ctx.Mode == MatchMode.None || this.Shapes(c) is var shapes && !shapes.Contains(HeadKey.Any) && !shapes.Contains(HeadKey.Of(p)))
        {
            return; // no row can give c a virtual node of p's head (a static index fact)
        }

        // A chain probe asks the same (class, head, kid filters) for many candidates of one lookup: answered once per lookup.
        var kid0 = p.Kids.Length > 0 ? this.KidHeads(p.Kids[0], beta) : null;
        var kid1 = p.Kids.Length > 1 ? this.KidHeads(p.Kids[1], beta) : null;
        var key = (this.g.Find(c), HeadKey.Of(p), this.SetKey(kid0) + "|" + this.SetKey(kid1));
        var virtuals = ctx.Memo is { } memo ? (memo.TryGetValue(key, out var known) ? known : memo[key] = this.VirtualsAt(c, key.Item2, ctx, kid0, kid1)) : this.Virtuals(c, key.Item2, ctx, kid0, kid1);
        foreach (var vs in virtuals)
        {
            if (ctx.Mode == MatchMode.Chain)
            {
                // An alias (a variable other side its side binds) is an existing class, not a node to meet: expanding it where a
                // need meets it merges the classes, after which the side matches directly.
                if (this.MayUnify(vs.Other, p) && !(vs.Via is null && vs.Other.Head is VarHead a && vs.Beta[a.Index] is not null))
                {
                    ctx.Chain!.Add((vs, p, beta));
                }
            }
            else
            {
                this.Expand(vs, p, beta, ctx);
            }
        }
    }

    // The heads a pattern's first kid can meet: those of its class and that class's virtual nodes when it resolves to a class;
    // its own head when it names an absent Type; anything when it is an unbound variable (null).
    private HashSet<HeadKey>? KidHeads(Term kid, int?[] beta)
    {
        var c = kid.Head is VarHead v ? beta[v.Index] ?? -1 : this.ClassOf(kid, beta);
        if (c >= 0)
        {
            var heads = this.HeadsOf(c);
            heads.UnionWith(this.VirtualHeads(heads));
            return heads;
        }

        return kid.Head is VarHead ? null : new() { HeadKey.Of(kid) };
    }

    // Whether two sides can denote one Type: variables match anything; ground Types compare by class when both are present.
    private bool MayUnify(Term a, Term b)
    {
        if (a.Head is VarHead || b.Head is VarHead || ReferenceEquals(a, b))
        {
            return true;
        }

        if (!a.HasVar && !b.HasVar && this.g.LookupTerm(a) is var x and >= 0 && this.g.LookupTerm(b) is var y and >= 0)
        {
            return this.g.Find(x) == this.g.Find(y);
        }

        return a.Head.Equals(b.Head) && a.Kids.Length == b.Kids.Length && a.Kids.Zip(b.Kids).All(k => this.MayUnify(k.First, k.Second));
    }

    // A pattern needs nodes of a virtual node's head in this class (or a meeting pairs two virtual nodes): the virtual node agrees with
    // the pattern as a node (§1), its variables bound as far as the pattern resolves; agreement requests the deferred row on behalf of
    // the requester, kid classes that differ meet first (their merge wakes the requester, which matches again).
    private void Expand(Virtual vs, Term p, int?[] beta, MatchCtx ctx)
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
        if (IsAlias(vs) || this.Agree(vs.Other, bind, p, beta, shapes, meets))
        {
            this.Settle(vs, bind, shapes, meets, ctx);
        }
    }

    // Matching modulo classes (§1): a virtual node's term o (variables bound in into) and a pattern p (bound in bp) can be one Type.
    // A subterm instantiated to a class matches by its class (AgreeClass); a free variable on either side matches anything (a free
    // variable of o records the head p needs there); otherwise heads and kids, a compound kid with a free variable by shape only.
    // False only when heads differ.
    private bool Agree(Term o, int?[] into, Term p, int?[] bp, Dictionary<int, Head> shapes, List<(int, int)> meets)
    {
        var cp = p.Head is VarHead pv ? bp[pv.Index] ?? -1 : this.ClassOf(p, bp);
        if (cp >= 0)
        {
            return this.AgreeClass(o, into, cp, meets);
        }

        if (p.Head is VarHead)
        {
            return true;
        }

        if (o.Head is VarHead ov)
        {
            if (into[ov.Index] is null)
            {
                shapes[ov.Index] = p.Head;
            }

            return true;
        }

        if (this.ClassOf(o, into) >= 0)
        {
            return true; // an existing Type and one not built yet: decided by matching once the row is built
        }

        if (!o.Head.Equals(p.Head) || o.Kids.Length != p.Kids.Length)
        {
            return false;
        }

        // A compound kid with a free variable is not one Type yet (its row's antecedent binds the variable): it agrees by shape and
        // is decided once built; other kids agree as above.
        for (var i = 0; i < o.Kids.Length; i++)
        {
            var (ko, kp) = (o.Kids[i], p.Kids[i]);
            if ((ko.Head is not VarHead && HasFree(ko, into)) || (kp.Head is not VarHead && HasFree(kp, bp)) ? !SameShape(ko, kp) : !this.Agree(ko, into, kp, bp, shapes, meets))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasFree(Term t, int?[] beta) => t.Head is VarHead v ? beta[v.Index] is null : t.HasVar && t.Kids.Any(k => HasFree(k, beta));

    private static bool SameShape(Term a, Term b)
        => a.Head is VarHead || b.Head is VarHead || (a.Head.Equals(b.Head) && a.Kids.Length == b.Kids.Length && a.Kids.Zip(b.Kids).All(k => SameShape(k.First, k.Second)));

    // o and a class c: a bound variable or a subterm instantiated to a class matches by class (different classes meet there); a free
    // variable takes c; a subterm not built yet is decided once built.
    private bool AgreeClass(Term o, int?[] into, int c, List<(int, int)> meets)
    {
        c = this.g.Find(c);
        var co = o.Head is VarHead ov ? (into[ov.Index] is int bound ? this.g.Find(bound) : -1) : this.ClassOf(o, into);
        if (o.Head is VarHead fv && into[fv.Index] is null)
        {
            into[fv.Index] = c;
        }
        else if (co >= 0 && co != c)
        {
            meets.Add((co, c));
        }

        return true;
    }

    // An agreed virtual node: kid classes that differ meet first; otherwise its row is requested on behalf of the requester (unless no
    // fact of its antecedent Contract can have the shape the requester needs at a free variable's position).
    private void Settle(Virtual vs, int?[] bind, Dictionary<int, Head> shapes, List<(int, int)> meets, MatchCtx ctx)
    {
        if (meets.Count != 0)
        {
            foreach (var (a, b) in meets)
            {
                this.Meet(a, b, ctx);
            }

            return;
        }

        if (vs.T.Antecedent is { } k)
        {
            foreach (var (v, shape) in shapes)
            {
                if (bind[v] is null && v <= k.Arity && !this.anyHead.Contains((k, v)) && !this.positionHeads[(k, v)].Contains(shape))
                {
                    return;
                }
            }
        }

        var key = (vs.T.Id, this.g.Find(vs.Class), BindKey(bind), ctx.Owner!, ctx.Sign);
        if (this.expansions.Add(key))
        {
            this.Request(vs.T, bind, ctx.Tags, ctx.Sign, ctx.Owner!, ctx.Fx!);
        }
    }

    /// <summary>A conclusion head for the static indexes: a symbol head with its arity, or Any for a variable.</summary>
    private readonly record struct HeadKey(Head? Head, int Arity)
    {
        public static readonly HeadKey Any = new(null, -1);

        public bool IsAny => this.Head is null;

        public static HeadKey Of(Term t) => t.Head is VarHead ? Any : new(t.Head, t.Kids.Length);

        public static HeadKey KidOf(Term t) => t.Kids.Length == 0 ? Any : Of(t.Kids[0]);
    }

    /// <summary>One side of an identity row: the side a class is matched against and the other side, its virtual node there.</summary>
    private sealed class EqSide
    {
        public EqSide(Template t, int side)
        {
            this.T = t;
            this.Index = side;
            var atom = t.Conclusion.Kind == FK.Not ? t.Conclusion.L! : t.Conclusion;
            this.Side = side == 0 ? atom.A! : atom.B!;
            this.Other = side == 0 ? atom.B! : atom.A!;
            var vars = new HashSet<int>();
            this.Side.Vars(vars);
            this.SideVars = vars.Count == 0 ? 0 : vars.Max() + 1;
            this.Alias = this.Other.Head is VarHead v && vars.Contains(v.Index);
            var others = new HashSet<int>();
            this.Other.Vars(others);
            this.Trivial = others.IsSubsetOf(vars);
            this.Path = [KidAt(this.Other, 0), KidAt(this.Other, 1), KidAt(this.Side, 0), KidAt(this.Side, 1)];
        }

        public int SideVars { get; }

        public Template T { get; }

        public int Index { get; }

        public Term Side { get; }

        public Term Other { get; }

        /// <summary>Gets a value indicating whether the other side is a variable the side binds: its virtual node is an existing class.</summary>
        public bool Alias { get; }

        /// <summary>Gets a value indicating whether matching the side fixes the other side (vars(other) ⊆ vars(side)).</summary>
        public bool Trivial { get; }

        /// <summary>Gets the index path: heads of the other side's kids 0 and 1, then the side's (Any for a variable or none).</summary>
        public HeadKey[] Path { get; }
    }

    /// <summary>Identity sides by (other kid0, other kid1, side kid0, side kid1) heads, then by side Term (rows sharing a side are one
    /// match). A lookup takes a filter per level (null: all); Any (a variable or no kid) is always included.</summary>
    private sealed class SideTrie
    {
        private readonly Dictionary<HeadKey, SideTrie> next = new();
        private readonly Dictionary<Term, List<EqSide>> leaf = new();

        public int Count { get; private set; }

        public void Add(EqSide e)
        {
            this.Count++;
            this.Add(e, 0);
        }

        public void Find(HashSet<HeadKey>?[] filters, Action<Term, List<EqSide>> found, int level = 0)
        {
            if (level == filters.Length)
            {
                foreach (var (side, entries) in this.leaf)
                {
                    found(side, entries);
                }

                return;
            }

            var filter = filters[level];
            if (filter is null || filter.Contains(HeadKey.Any))
            {
                foreach (var child in this.next.Values)
                {
                    child.Find(filters, found, level + 1);
                }

                return;
            }

            foreach (var h in filter.Append(HeadKey.Any))
            {
                if (this.next.TryGetValue(h, out var child))
                {
                    child.Find(filters, found, level + 1);
                }
            }
        }

        private void Add(EqSide e, int level)
        {
            if (level == e.Path.Length)
            {
                if (!this.leaf.TryGetValue(e.Side, out var list))
                {
                    this.leaf[e.Side] = list = new();
                }

                list.Add(e);
                return;
            }

            if (!this.next.TryGetValue(e.Path[level], out var child))
            {
                this.next[e.Path[level]] = child = new();
            }

            child.Add(e, level + 1);
        }
    }

    /// <summary>A bound-expansion entry: nodes of Scan's shape bind the row; Both: Side is then fully bound and the row serves the
    /// class demands of both sides; otherwise Side is matched against the active class demands.</summary>
    private sealed record ShapeEntry(Template T, Term Scan, Term Side, bool Both);

    private sealed class Virtual
    {
        public Virtual(Template t, int?[] beta, Term other, int c)
        {
            this.T = t;
            this.Beta = beta;
            this.Other = other;
            this.Class = c;
        }

        public Template T { get; }

        public int?[] Beta { get; }

        public Term Other { get; }

        public int Class { get; }

        public Virtual? Via { get; init; }

        public Term? ViaPattern { get; init; }

        public int?[]? ViaBeta { get; init; }
    }

    private enum MatchMode : byte { None, Expand, Chain }

    private sealed class MatchCtx
    {
        private readonly Solver solver;

        public MatchCtx(Solver solver, Row? owner, byte tags, Effects? fx)
        {
            this.solver = solver;
            this.Owner = owner;
            this.Tags = tags;
            this.Fx = fx;
        }

        public Row? Owner { get; }

        public byte Tags { get; }

        public Effects? Fx { get; }

        public byte Sign { get; init; } = Both;

        /// <summary>Gets what a match does with a virtual node of the pattern's head: nothing, expand it, or record it (chains).</summary>
        public MatchMode Mode { get; init; }

        public List<(Virtual Via, Term Pattern, int?[] Beta)>? Chain { get; init; }

        public Dictionary<(int, HeadKey, string), List<Virtual>>? Memo { get; init; }

        public MatchCtx Derive(MatchMode mode, Dictionary<(int, HeadKey, string), List<Virtual>>? memo = null) => new(this.solver, this.Owner, this.Tags, this.Fx) { Sign = this.Sign, Mode = mode, Chain = mode == MatchMode.Chain ? new() : null, Memo = memo };

        // Every class a selection visits needs its identity facts: a class demand, which inherits the visitor's tags.
        public void Visit(int c)
        {
            if (this.Owner is null || this.Fx is null)
            {
                return;
            }

            this.Owner.Visited.Add(c);
            this.solver.visitLog?.Add(c);
            this.Owner.VisitSigns[c] = (byte)(this.Owner.VisitSigns.GetValueOrDefault(c) | this.Sign);
            if (!this.solver.idents.ContainsKey(c))
            {
                this.Fx.Idents.Add(c);
                this.Fx.IdentTags |= this.Tags;
            }
        }
    }
}
