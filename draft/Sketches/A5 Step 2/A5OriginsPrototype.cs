#:property Nullable=disable
// Unit 2x-P4 (A5 step 2 plan, PD3): an executable model of A5-F4's Origin stratum, design A3 §7 with errata E4, E7 and E12 and the
// plan's PD11, in its own file (not a round of `draft/Sketches/A5 Step 0/A5RulesPrototype.cs`). Record: origins.md (this folder).
// One body per case; one Semantics case per view; views with equal edges and premises form one class, run once.
//   atoms    Fixed: universal, parameter and receiver Origins, projections, static, a meet of fixed Origins. FiniteBody: a Borrow reached
//            from a body root by Field, element and own steps only. FiniteStatic: a Borrow of mutable static storage (P6). FiniteGuard:
//            a guard candidate's guard-local Origin (SPEC 14.8.3). Inferred: body variables and call regions, a Borrow through a borrow
//            (c1), a raw Place's anchor, a Borrow of immutable static storage (SPEC 15.6.5, 15.2.3, 11.8, 5.2.2). Per view: a pair
//            layer absent in a case adds no borrow step there.
//   rows     (site, longer, shorter, Equality, case mask, use, return fit). Equality gives both edges. A meet at the longer end is split
//            into its operands; an all-fixed meet at the shorter end is one fixed node, any other meet there an inferred node with an
//            edge from each operand (SPEC 15.3.6's solver rules).
//   E4       a Ref's containment, its layers counted outward from the Borrow: an exclusive layer adds `layer → new` and continues, a
//            shared one adds it and stops, a pair layer by its case (absent: skipped), an own step is no dereference, a raw dereference
//            stops (no owner or Loan is recovered, SPEC 5.2.2). SPEC 15.6.3.
//   Refuted  A3 §7: one multi-source BFS from the finite nodes through inner nodes (inferred or finite), one witness per node; an edge
//            from a reached node into a fixed node gives Unsatisfied (S) at that edge's site. Variant AllSources: finite-source bitsets.
//   Unknown  Tarjan over the inner nodes, fixed-source bitsets (64 per word) in topological order; an edge into fixed g carrying fixed f
//            with not entails(f, g) gives Unproven (P) at that edge's site. entails is the premise oracle of the class (reflexive and
//            transitive premise closure, `static`, `==`, the meet laws), one reverse closure per fixed target.
//   merge    PD11: a Loan problem of kind destroy (D) on a Loan `_0` carries is dropped when the stratum refuted its Borrow's Origin at a
//            return fit; a guard candidate's err(ended) (E) is dropped when its γ reaches `_0` and its guard-local Origin was refuted at a
//            return fit; nothing else is dropped. Loan problems are inputs here; -loans computes them with A5RulesPrototype.cs's design.
// Reference: every chain by SPEC 15.6.5's definition (a DFS from each end through inner nodes), on every case and on random graphs.
// A case states SPEC's failures, each with the reports SPEC and DIAGNOSTICS admit for it, and the optional reports a failure caused
// only by another may add (SPEC 23.1.2). A run is UNSOUND (SPEC error, no report), OVERSTRICT (SPEC valid, a report), LOST (a failure
// without a report), EXTRA (a report no failure admits) or DOUBLE (a redundant report whose failures another report covers).
// Run, in a folder outside the repository: dotnet run A5OriginsPrototype.cs [-- -v] [-- -random N]. Exit code: the design's UNSOUND
// and OVERSTRICT cases plus the reference's disagreements.
// -loans, in an empty folder outside the repository with both files copied there:
//   sed '1,/^\/\/ ==== end of entry/d; /^#if F4LOANS$/d; /^#endif \/\/ F4LOANS$/d' A5OriginsPrototype.cs > origins.cs
//   sed '/^var verbose = /a if (args.Contains("-f4loans")) return F4Loans.Run();' A5RulesPrototype.cs > proto.cs
//   cat origins.cs >> proto.cs && dotnet run proto.cs -- -f4loans
using System.Text;

return F4.Run(args);
// ==== end of entry (the -loans recipe drops the lines above) ====

static class F4
{
    public enum Kind { Fixed, FiniteBody, FiniteStatic, FiniteGuard, Inferred }

    static bool IsFinite(Kind k) => k is Kind.FiniteBody or Kind.FiniteStatic or Kind.FiniteGuard;

    public record Row(string Site, string Longer, string Shorter, bool Eq, ulong Mask, bool Use, bool Ret);

    // A dereference step of a Ref's Place, counted outward from the Borrow: Mode "ref", "uniq", "raw", "own", or "pair" with its mode
    // per view ("absent", "ref" or "uniq").
    public record Layer(string Origin, string Mode, string[] Pair = null);

    // A Borrow (a MIR Ref): its Origin New; Root "local" (a local, parameter or temporary), "mstatic", "istatic" or "guard" (a guard
    // candidate, whose Origin is guard-local).
    public record Ref(string Id, string New, string Root, string Site, Layer[] Layers);

    public record Prem(string A, string B, bool Eq, ulong Mask);

    // A Loan-stratum problem: Kind D (a destroy access at a storage end), W (a write) or E (a guard candidate's err(ended)); Loan: the
    // Borrow's Ref id (the candidate's for E); Zero: `_0` carries the Loan at the access (D, W), or γ reaches `_0` (E).
    public record Lp(char Kind, string Loan, string Site, bool Zero)
    {
        public override string ToString() => $"{Kind} {Site} {Loan}{(Zero ? " (_0)" : "")}";
    }

    // A report: S Unsatisfied (Refuted), P Unproven (Unknown), C ComparisonLoanConflict (a Loan problem kept), N no candidate.
    public record Rep(char Code, string Site, string A, string B)
    {
        public override string ToString() => Code switch { 'C' => $"C {Site} {A}", 'N' => $"N {A}", _ => $"{Code} {Site} {A}>{B}" };
    }

    public static Layer Sh(string o) => new(o, "ref");
    public static Layer Ex(string o) => new(o, "uniq");
    public static Layer Raw() => new(null, "raw");
    public static Layer Own() => new(null, "own");
    public static Layer PairL(string o, params string[] perView) => new(o, "pair", perView);

    public sealed class Body(string id, string group, string program, string clause)
    {
        public readonly string Id = id, Group = group, Program = program, Clause = clause;
        public string[] Views = ["*"];
        public readonly Dictionary<string, Kind> Atoms = new() { ["static"] = Kind.Fixed };
        public readonly Dictionary<string, char> Vars = new();
        public readonly List<Row> Rows = new();
        public readonly List<Ref> Refs = new();
        public readonly List<Prem> Prems = new();
        public readonly List<Lp> Loans = new();
        public readonly List<string[]> Fails = new();
        public readonly List<string> Opts = new();
        public string Note;

        public bool Err => Fails.Count > 0;

        public Body In(params string[] views) { Views = views; return this; }

        public Body Fixed(params string[] a) { foreach (var x in a) Atoms[x] = Kind.Fixed; return this; }

        public Body Var(string v, char variance = '+') { Atoms[v] = Kind.Inferred; Vars[v] = variance; return this; }

        public Body Regions(params string[] vs) { foreach (var v in vs) Var(v); return this; }

        ulong M(string views) => views == null ? ~0UL : views.Split(',').Aggregate(0UL, (m, v) => m | 1UL << Array.IndexOf(Views, v));

        public Body R(string site, string longer, string shorter, bool use = false, string views = null)
        { Rows.Add(new(site, longer, shorter, false, M(views), use, false)); return this; }

        public Body Ret(string site, string longer, string shorter) { Rows.Add(new(site, longer, shorter, false, ~0UL, true, true)); return this; }

        public Body Eq(string site, string a, string b) { Rows.Add(new(site, a, b, true, ~0UL, false, false)); return this; }

        public Body Borrow(string id, string origin, string root, string site, params Layer[] layers)
        { Refs.Add(new(id, origin, root, site, layers)); return this; }

        public Body P(string a, string b, string views = null) { Prems.Add(new(a, b, false, M(views))); return this; }

        public Body PEq(string a, string b) { Prems.Add(new(a, b, true, ~0UL)); return this; }

        public Body Loan(char kind, string loan, string site, bool zero) { Loans.Add(new(kind, loan, site, zero)); return this; }

        public Body Fail(params string[] alternatives) { Fails.Add(alternatives); return this; }

        public Body Opt(string report) { Opts.Add(report); return this; }

        public Body Says(string note) { Note = note; return this; }
    }

    public sealed record Cfg(string Name)
    {
        public bool RootCut, NoContain, RawContain, OwnAsBorrow, AnchorTop, AnchorFinite, GuardInferred, AllSources, Cand, CandDecl, NoCandErr;
        public string Merge = "PD11";
    }

    static readonly Cfg Design = new("design");
    static long W; // the work counter: node visits, edge steps, bitset words, bit tests and premise-closure steps

    // ---------------------------------------------------------------------------------------------------------------- the graph
    sealed class Graph
    {
        public readonly List<string> Name = new();
        public readonly List<Kind> Kinds = new();
        public readonly Dictionary<string, int> Ix = new();
        public readonly List<(int A, int B, string Site, bool Use)> E = new();
        public readonly List<(string A, string B)> Prems = new();

        public int Node(string n, Kind k)
        {
            if (Ix.TryGetValue(n, out var i)) return i;
            Ix[n] = Name.Count; Name.Add(n); Kinds.Add(k);
            return Name.Count - 1;
        }

        public bool Inner(int i) => Kinds[i] != Kind.Fixed;

        public string Key() => string.Join(";", E.Select(e => $"{Name[e.A]}>{Name[e.B]}@{e.Site}").Distinct().OrderBy(x => x, StringComparer.Ordinal))
            + "|" + string.Join(";", Prems.Select(p => $"{p.A}>{p.B}").Distinct().OrderBy(x => x, StringComparer.Ordinal));
    }

    // OriginAtom.Of for a Ref's Origin in view v, and the Origins its E4 containment adds edges from.
    static (Kind Kind, List<string> Contain, bool Anchor) Classify(Ref r, int v, Cfg cfg)
    {
        var steps = r.Layers.Select(l => l.Mode == "pair" ? new Layer(l.Origin, l.Pair[v]) : l).Where(l => l.Mode != "absent").ToList();
        bool anchor = steps.Count > 0 && steps[0].Mode == "raw";
        bool through = steps.Any(l => l.Mode is "ref" or "uniq" || l.Mode == "own" && cfg.OwnAsBorrow);
        var contain = new List<string>();
        if (!cfg.NoContain)
        {
            var order = cfg.RootCut ? Enumerable.Reverse(steps) : steps; // RootCut: A3 §7 before E4, "the same cut as obs" from the root
            foreach (var l in order)
            {
                if (l.Mode == "raw") { if (cfg.RawContain) continue; break; }
                if (l.Mode is "ref" or "uniq") { contain.Add(l.Origin); if (l.Mode == "ref") break; }
            }
        }

        var kind = r.Root == "guard" ? (cfg.GuardInferred ? Kind.Inferred : Kind.FiniteGuard)
            : anchor ? (cfg.AnchorFinite ? Kind.FiniteBody : Kind.Inferred)
            : through ? Kind.Inferred
            : r.Root switch { "local" => Kind.FiniteBody, "mstatic" => Kind.FiniteStatic, _ => Kind.Inferred };
        return (kind, contain, anchor);
    }

    static string Sub(Dictionary<string, string> s, string n) => s != null && s.TryGetValue(n, out var c) ? c : n;

    static string Canon(string n) => string.Join('&', n.Split('&').Distinct().OrderBy(x => x, StringComparer.Ordinal));

    static Graph Build(Body b, int v, Cfg cfg, Dictionary<string, string> subst)
    {
        var g = new Graph();
        var kinds = new Dictionary<string, Kind>(b.Atoms);
        var rename = new Dictionary<string, string>();
        var edges = new List<(string A, string B, string Site, bool Use)>();
        foreach (var r in b.Refs)
        {
            var (kind, contain, anchor) = Classify(r, v, cfg);
            if (anchor && cfg.AnchorTop) rename[r.New] = "static"; // Binding's reading: a raw anchor outlives everything
            else kinds[r.New] = kind;
            foreach (var o in contain) edges.Add((o, r.New, r.Site, false));
        }

        foreach (var row in b.Rows.Where(row => (row.Mask >> v & 1) != 0))
        {
            edges.Add((row.Longer, row.Shorter, row.Site, row.Use));
            if (row.Eq) edges.Add((row.Shorter, row.Longer, row.Site, row.Use));
        }

        string Map(string n) => Canon(string.Join('&', n.Split('&').Select(x => Sub(subst, Sub(rename, x)))));
        Kind KindOf(string op) => kinds.TryGetValue(op, out var k) ? k : throw new InvalidOperationException($"{b.Id}: unknown atom {op}");
        int NodeOf(string n)
        {
            var ops = n.Split('&');
            if (ops.Length == 1) return g.Node(n, KindOf(n));
            if (ops.All(o => KindOf(o) == Kind.Fixed)) return g.Node(n, Kind.Fixed);
            if (g.Ix.TryGetValue("m(" + n + ")", out var known)) return known;
            var m = g.Node("m(" + n + ")", Kind.Inferred);
            foreach (var o in ops) g.E.Add((NodeOf(o), m, "meet", false));
            return m;
        }

        foreach (var (a, c, site, use) in edges)
        {
            var target = NodeOf(Map(c));
            foreach (var op in Map(a).Split('&')) g.E.Add((NodeOf(op), target, site, use)); // a meet at the longer end is split
        }

        foreach (var p in b.Prems.Where(p => (p.Mask >> v & 1) != 0))
        {
            g.Prems.Add((p.A, p.B));
            if (p.Eq) g.Prems.Add((p.B, p.A));
        }

        foreach (var name in b.Atoms.Keys.Where(k => b.Atoms[k] == Kind.Fixed)) g.Node(name, Kind.Fixed);
        return g;
    }

    // ---------------------------------------------------------------------------------------------------------------- the premise oracle
    sealed class Oracle((string A, string B)[] prems)
    {
        readonly Dictionary<string, HashSet<string>> rev = new();
        readonly ILookup<string, string> longer = prems.ToLookup(p => p.B, p => p.A);

        HashSet<string> Rev(string t) // the atoms a premise chain proves to outlive t
        {
            if (rev.TryGetValue(t, out var s)) return s;
            s = new() { t };
            for (var work = new Queue<string>([t]); work.Count > 0;)
                foreach (var a in longer[work.Dequeue()]) { W++; if (s.Add(a)) work.Enqueue(a); }
            return rev[t] = s;
        }

        public bool Entails(string f, string t)
        {
            if (f == t || f == "static") return true;
            if (f.Contains('&')) return f.Split('&').All(o => Entails(o, t));   // (a and b) outlives c exactly when both do
            if (t.Contains('&')) return t.Split('&').Any(o => Entails(f, o));   // x outlives a proves x outlives (a and b)
            return Rev(t).Contains(f) || Rev("static").Contains(f);             // a outlives static proves a equal to static
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- the stratum
    static (int[] Comp, int Count, List<int>[] Members) Scc(Graph g, List<int>[] adj)
    {
        int n = g.Name.Count, counter = 0, count = 0;
        var comp = Enumerable.Repeat(-1, n).ToArray();
        var index = Enumerable.Repeat(-1, n).ToArray();
        var low = new int[n];
        var on = new bool[n];
        var stack = new Stack<int>();
        void Visit(int u)
        {
            W++; index[u] = low[u] = counter++; stack.Push(u); on[u] = true;
            foreach (var x in adj[u])
            {
                if (!g.Inner(x)) continue;
                W++;
                if (index[x] < 0) { Visit(x); low[u] = Math.Min(low[u], low[x]); }
                else if (on[x]) low[u] = Math.Min(low[u], index[x]);
            }

            if (low[u] != index[u]) return;
            for (int x = -1; x != u;) { x = stack.Pop(); on[x] = false; comp[x] = count; }
            count++;
        }

        for (int u = 0; u < n; u++) if (g.Inner(u) && index[u] < 0) Visit(u);
        var members = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
        for (int u = 0; u < n; u++) if (comp[u] >= 0) members[comp[u]].Add(u);
        return (comp, count, members);
    }

    // Bitsets over the sources, propagated through the inner nodes' components in topological order (Tarjan numbers sinks first).
    static ulong[][] Propagate(Graph g, List<int>[] adj, (int[] Comp, int Count, List<int>[] Members) s, int words, Action<ulong[][]> seed)
    {
        var bits = Enumerable.Range(0, s.Count).Select(_ => new ulong[words]).ToArray();
        seed(bits);
        for (int c = s.Count - 1; c >= 0; c--)
            foreach (var u in s.Members[c])
                foreach (var x in adj[u])
                    if (g.Inner(x) && s.Comp[x] != c) { W += words; for (int w = 0; w < words; w++) bits[s.Comp[x]][w] |= bits[c][w]; }
        return bits;
    }

    static List<Rep> Judge(Graph g, Cfg cfg)
    {
        int n = g.Name.Count;
        var reps = new List<Rep>();
        var adj = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();
        var outEdges = Enumerable.Range(0, n).Select(_ => new List<(int B, string Site)>()).ToArray();
        foreach (var e in g.E) { adj[e.A].Add(e.B); outEdges[e.A].Add((e.B, e.Site)); }
        W += n + g.E.Count;
        var oracle = new Oracle(g.Prems.ToArray());
        var s = Scc(g, adj);

        // Refuted.
        var finite = Enumerable.Range(0, n).Where(i => IsFinite(g.Kinds[i])).ToList();
        if (cfg.AllSources)
        {
            int words = (finite.Count + 63) / 64;
            var bits = Propagate(g, adj, s, words, b => { for (int k = 0; k < finite.Count; k++) b[s.Comp[finite[k]]][k / 64] |= 1UL << k % 64; });
            for (int u = 0; u < n; u++)
                if (g.Inner(u))
                    foreach (var (t, site) in outEdges[u].Where(x => !g.Inner(x.B)))
                        for (int k = 0; k < finite.Count; k++) { W++; if ((bits[s.Comp[u]][k / 64] >> k % 64 & 1) != 0) reps.Add(new('S', site, g.Name[finite[k]], g.Name[t])); }
        }
        else
        {
            var witness = Enumerable.Repeat(-1, n).ToArray();
            var queue = new Queue<int>();
            foreach (var f in finite) { witness[f] = f; queue.Enqueue(f); }
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                foreach (var (x, site) in outEdges[u])
                {
                    W++;
                    if (!g.Inner(x)) reps.Add(new('S', site, g.Name[witness[u]], g.Name[x]));
                    else if (witness[x] < 0) { witness[x] = witness[u]; queue.Enqueue(x); }
                }
            }
        }

        // Unknown.
        var sources = Enumerable.Range(0, n).Where(i => !g.Inner(i) && adj[i].Any(g.Inner)).ToList();
        int fw = (sources.Count + 63) / 64;
        var fbits = Propagate(g, adj, s, fw, b =>
        {
            for (int k = 0; k < sources.Count; k++) foreach (var x in adj[sources[k]].Where(g.Inner)) { W++; b[s.Comp[x]][k / 64] |= 1UL << k % 64; }
        });
        for (int u = 0; u < n; u++)
            foreach (var (t, site) in outEdges[u].Where(x => !g.Inner(x.B)))
            {
                if (!g.Inner(u)) { if (!oracle.Entails(g.Name[u], g.Name[t])) reps.Add(new('P', site, g.Name[u], g.Name[t])); continue; }
                for (int k = 0; k < sources.Count; k++)
                {
                    W++;
                    if ((fbits[s.Comp[u]][k / 64] >> k % 64 & 1) != 0 && !oracle.Entails(g.Name[sources[k]], g.Name[t]))
                        reps.Add(new('P', site, g.Name[sources[k]], g.Name[t]));
                }
            }

        return reps.Distinct().ToList();
    }

    // SPEC 15.6.5's definition, path by path: from each end (fixed or finite) through inner nodes to every end.
    static List<Rep> Reference(Graph g)
    {
        var oracle = new Oracle(g.Prems.ToArray());
        var reps = new List<Rep>();
        int n = g.Name.Count;
        var outEdges = g.E.ToLookup(e => e.A);
        bool End(int i) => g.Kinds[i] == Kind.Fixed || IsFinite(g.Kinds[i]);
        for (int a = 0; a < n; a++)
        {
            if (!End(a)) continue;
            var seen = new HashSet<int> { a };
            for (var stack = new Stack<int>([a]); stack.Count > 0;)
            {
                var x = stack.Pop();
                foreach (var e in outEdges[x])
                {
                    if (End(e.B))
                    {
                        if (IsFinite(g.Kinds[a]) && g.Kinds[e.B] == Kind.Fixed) reps.Add(new('S', e.Site, g.Name[a], g.Name[e.B]));
                        if (g.Kinds[a] == Kind.Fixed && g.Kinds[e.B] == Kind.Fixed && !oracle.Entails(g.Name[a], g.Name[e.B])) reps.Add(new('P', e.Site, g.Name[a], g.Name[e.B]));
                    }

                    if (g.Inner(e.B) && seen.Add(e.B)) stack.Push(e.B);
                }
            }
        }

        return reps.Distinct().ToList();
    }

    // ---------------------------------------------------------------------------------------------------------------- inference (OJ5)
    // SPEC 15.3.6 with S3: a variable's bounds are followed through other (inferred) nodes to fixed or finite Origins; a covariant
    // class takes the meet of its upper bounds, a contravariant one the lower bound that outlives every other, an invariant one an end
    // it is equal to; otherwise it has no candidate and stays a local region. CandDecl: the declaration's bounds only (no later use).
    static Dictionary<string, string> Candidates(Body b, Graph g, Cfg cfg, List<string> noCandidate)
    {
        var oracle = new Oracle(g.Prems.ToArray());
        var es = g.E.Where(e => !(cfg.CandDecl && e.Use)).ToList();
        var vars = b.Vars.Keys.Where(g.Ix.ContainsKey).ToList();
        noCandidate.AddRange(b.Vars.Keys.Where(v => !g.Ix.ContainsKey(v))); // no bound at all
        var subst = new Dictionary<string, string>();
        bool End(int i) => g.Kinds[i] == Kind.Fixed || IsFinite(g.Kinds[i]);
        HashSet<int> Reach(IEnumerable<int> from, bool forward)
        {
            var ends = new HashSet<int>(); var seen = new HashSet<int>(from);
            for (var work = new Stack<int>(seen); work.Count > 0;)
            {
                var x = work.Pop();
                foreach (var e in es)
                {
                    int src = forward ? e.A : e.B, dst = forward ? e.B : e.A;
                    if (src != x) continue;
                    if (End(dst)) ends.Add(dst);
                    else if (seen.Add(dst)) work.Push(dst);
                }
            }

            return ends;
        }

        HashSet<int> VarReach(int from) // the variables a variable reaches through variables
        {
            var seen = new HashSet<int> { from };
            for (var work = new Stack<int>(seen); work.Count > 0;)
            {
                var x = work.Pop();
                foreach (var e in es.Where(e => e.A == x && b.Vars.ContainsKey(g.Name[e.B]))) if (seen.Add(e.B)) work.Push(e.B);
            }

            return seen;
        }

        bool Outlives(int l, int m) => l == m || g.Kinds[l] == Kind.Fixed && IsFinite(g.Kinds[m])
            || g.Kinds[l] == Kind.Fixed && g.Kinds[m] == Kind.Fixed && oracle.Entails(g.Name[l], g.Name[m]);
        var done = new HashSet<string>();
        foreach (var v in vars)
        {
            if (done.Contains(v)) continue;
            int iv = g.Ix[v];
            var cls = VarReach(iv).Where(x => VarReach(x).Contains(iv)).Select(x => g.Name[x]).ToList(); // a cycle is solved as one
            done.UnionWith(cls);
            var vs = cls.Select(x => b.Vars[x]).Distinct().ToList();
            var variance = vs.Count == 1 ? vs[0] : '=';
            var idx = cls.Select(x => g.Ix[x]).ToList();
            var up = Reach(idx, forward: false).ToList();
            var lo = Reach(idx, forward: true).ToList();
            string cand = variance switch
            {
                '+' => up.Count > 0 ? Canon(string.Join('&', up.Select(x => g.Name[x]))) : null,
                '-' => lo.Where(l => lo.All(m => Outlives(l, m))).Select(l => g.Name[l]).OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault(),
                _ => up.Intersect(lo).Select(x => g.Name[x]).OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault(),
            };
            foreach (var x in cls) { if (cand != null) subst[x] = cand; else noCandidate.Add(x); }
        }

        return subst;
    }

    // ---------------------------------------------------------------------------------------------------------------- one body
    public sealed class Outcome
    {
        public readonly List<Rep> Origin = new();
        public readonly Dictionary<string, List<string>> ViewsOf = new();
        public List<Rep> Final;
        public int Classes;
        public long Work;
        public string RefDisagreement;
    }

    public static Outcome Analyze(Body b, Cfg cfg)
    {
        var o = new Outcome();
        W = 0;
        var classes = new Dictionary<string, (Graph G, List<int> Views)>();
        var noCand = new HashSet<string>();
        for (int v = 0; v < b.Views.Length; v++)
        {
            var g = Build(b, v, cfg, null);
            if (cfg.Cand)
            {
                var none = new List<string>();
                g = Build(b, v, cfg, Candidates(b, g, cfg, none));
                noCand.UnionWith(none);
            }

            var key = g.Key();
            if (!classes.TryGetValue(key, out var cl)) classes[key] = cl = (g, new());
            cl.Views.Add(v);
        }

        o.Classes = classes.Count;
        foreach (var (g, views) in classes.Values)
        {
            foreach (var r in Judge(g, cfg))
            {
                if (!o.ViewsOf.TryGetValue(r.ToString(), out var vs)) { o.Origin.Add(r); o.ViewsOf[r.ToString()] = vs = new(); }
                vs.AddRange(views.Select(v => b.Views[v]));
            }

            if (cfg != Design) continue;
            long keep = W;
            var all = Judge(g, cfg with { AllSources = true }).Select(r => r.ToString()).ToHashSet();
            var refr = Reference(g).Select(r => r.ToString()).ToHashSet();
            var bfs1 = Judge(g, cfg).Where(r => r.Code == 'S').Select(r => r.ToString()).ToHashSet();
            var refS = refr.Where(r => r[0] == 'S').ToHashSet();
            if (!all.SetEquals(refr)) o.RefDisagreement = $"design(AllSources) {{{string.Join(", ", all.Except(refr))}}} vs reference {{{string.Join(", ", refr.Except(all))}}}";
            else if (!bfs1.IsSubsetOf(refS) || bfs1.Count == 0 != (refS.Count == 0)) o.RefDisagreement = "BFS1 is not a nonempty subset of the reference's Refuted chains";
            W = keep;
        }

        if (cfg.NoCandErr) foreach (var x in noCand.OrderBy(x => x, StringComparer.Ordinal)) o.Origin.Add(new('N', "-", x, null));
        o.Work = W;
        o.Final = Merge(b, o.Origin, cfg);
        return o;
    }

    // PD11, and the variants: None, Membership (E12's first partition row: by `_0`'s set), ByBorrow (every Loan problem of a refuted
    // Borrow), GuardReverse (PD11 for destroy; for a guard candidate with an err(ended), its guard-local Origin's Refuted reports go).
    static List<Rep> Merge(Body b, List<Rep> origin, Cfg cfg)
    {
        var retSites = b.Rows.Where(r => r.Ret).Select(r => r.Site).ToHashSet();
        var refuted = origin.Where(r => r.Code == 'S').ToList();
        var atReturn = refuted.Where(r => retSites.Contains(r.Site)).Select(r => r.A).ToHashSet();
        var any = refuted.Select(r => r.A).ToHashSet();
        var originOf = b.Refs.ToDictionary(r => r.Id, r => r.New);
        var final = new List<Rep>(origin);
        if (cfg.Merge == "GuardReverse")
        {
            var ended = b.Loans.Where(l => l.Kind == 'E').Select(l => originOf[l.Loan]).ToHashSet();
            final.RemoveAll(r => r.Code == 'S' && ended.Contains(r.A));
        }

        foreach (var l in b.Loans)
        {
            var o = originOf[l.Loan];
            bool drop = cfg.Merge switch
            {
                "None" => false,
                "Membership" => l.Zero && l.Kind is 'D' or 'E',
                "ByBorrow" => any.Contains(o),
                "GuardReverse" => l.Kind == 'D' && l.Zero && atReturn.Contains(o),
                _ => l.Kind is 'D' or 'E' && l.Zero && atReturn.Contains(o),
            };
            if (!drop) final.Add(new('C', l.Site, l.Loan, null));
        }

        return final.Distinct().ToList();
    }

    public static string Verdict(Body b, List<Rep> reps)
    {
        var got = reps.Select(r => r.ToString()).ToHashSet();
        if (b.Err && got.Count == 0) return "UNSOUND";
        if (!b.Err) return got.Count > 0 ? "OVERSTRICT" : "ok";
        var tags = new List<string>();
        if (b.Fails.Any(f => !f.Any(got.Contains))) tags.Add("LOST");
        var allowed = b.Fails.SelectMany(f => f).Concat(b.Opts).ToHashSet();
        if (got.Any(r => !allowed.Contains(r))) tags.Add("EXTRA");
        if (got.Where(r => !b.Opts.Contains(r)).Any(r => b.Fails.Where(f => f.Contains(r)).ToList() is { Count: > 0 } mine && mine.All(f => f.Any(x => x != r && got.Contains(x)))))
            tags.Add("DOUBLE");
        return tags.Count == 0 ? "ok" : string.Join("+", tags);
    }

    static string Spec(Body b) => (b.Err ? b.Fails.Count > 6 ? $"error, {b.Fails.Count} failures" : "error: " + string.Join("; ", b.Fails.Select(f => string.Join(" or ", f)))
        + (b.Opts.Count > 0 ? " (optional: " + string.Join(", ", b.Opts) + ")" : "") : "valid") + $" (SPEC {b.Clause})";

    // ---------------------------------------------------------------------------------------------------------------- the cases
    public static IEnumerable<Body> AllCases()
    {
        // audit-origins §3.1 (row 7): fits by variance, declared and substituted clauses, well-formedness (SPEC 15.6.1, 15.3.3, 15.6.4).
        yield return new Body("r7-get", "row 7", "`func get(b: objref/Holder<ref/i32 during c> during o) -> ref/i32 during o => b.value`", "15.6.1")
            .Fixed("c", "o").Ret("ret", "c", "o").P("c", "o");
        yield return new Body("r7-identity", "row 7", "`func identity<T>(value: View<T>) -> View<T>{result}`, `origin result.source == value.source`, `return value@move`", "15.3.1")
            .Fixed("value.source", "result.source").Ret("ret", "value.source", "result.source").PEq("result.source", "value.source");
        yield return new Body("r7-useView", "row 7", "`func useView<T>(x: View<T>, callback: (View<T>{c}) -> ())`, `origin c.source == x.source`, `callback(x@move)`", "15.3.4")
            .Fixed("x.source", "c.source").R("call", "x.source", "c.source").PEq("c.source", "x.source");
        yield return new Body("r7-inv", "row 7", "`func f(x: uniq/(ref/i32 during a), y: uniq/(ref/i32 during b))`, `origin b outlives a`, `let z: uniq/(ref/i32 during a) = y`", "15.6.1 (== at an invariant position), 15.6.5")
            .Fixed("a", "b", "y").Var("v_z").R("init", "y", "v_z").Eq("init", "b", "a").P("b", "a").Fail("P init a>b");
        yield return new Body("r7-inv-eq", "row 7", "r7-inv with `origin a == b`", "15.6.1")
            .Fixed("a", "b", "y").Var("v_z").R("init", "y", "v_z").Eq("init", "b", "a").PEq("a", "b");
        yield return new Body("r7-subst", "row 7", "`keep(x: uniq/(ref/i32 during a), y: ref/i32 during b)` with `origin b outlives a`; `func use(p: uniq/(ref/i32 during c), q: ref/i32 during d) => keep(p, q)`", "15.6.4 step 3")
            .Fixed("c", "d", "p").Var("v_x").Var("a'", '=').Var("b'").R("call", "p", "v_x").Eq("call", "c", "a'").R("call", "d", "b'").R("call", "b'", "a'").Fail("P call d>c");
        yield return new Body("r7-subst-ok", "row 7", "r7-subst with `origin d outlives c`", "15.6.4 step 3")
            .Fixed("c", "d", "p").Var("v_x").Var("a'", '=').Var("b'").R("call", "p", "v_x").Eq("call", "c", "a'").R("call", "d", "b'").R("call", "b'", "a'").P("d", "c");

        // audit-origins §3.2 (row 9c) and SPEC 15.5: containment up to and including the first shared layer (E4).
        yield return new Body("c-get", "15.5", "View: `func get(self: ref/Self) -> ref/T during self.source => self.data` (a Copy of the stored reference)", "15.5")
            .Fixed("self.source", "self").Ret("ret", "self.source", "self.source").P("self.source", "self");
        yield return new Body("c-getUniq", "15.5", "UniqView: `func getUniq(self: uniq/Self) -> uniq/T during self => self.data` (a Reborrow through self)", "15.5, 15.6.3")
            .Fixed("self.source", "self").Borrow("Ld", "b_data", "local", "ret", Ex("self.source"), Ex("self")).Ret("ret", "b_data", "self").P("self.source", "self");
        yield return new Body("c-intoUniq", "15.5", "UniqView: `func intoUniq(self: Self) -> uniq/T during self.source => self.data@move`", "15.5")
            .Fixed("self.source").Ret("ret", "self.source", "self.source");
        yield return new Body("c-detach", "15.5", "UniqView: `func detach(self: uniq/Self) -> uniq/T during self.source => self.data` (spec case 15.5/0)", "15.5, 15.6.3")
            .Fixed("self.source", "self").Borrow("Ld", "b_data", "local", "ret", Ex("self.source"), Ex("self")).Ret("ret", "b_data", "self.source").P("self.source", "self")
            .Fail("P ret self>self.source");
        yield return new Body("c-box", "row 9c", "`func f(b: uniq/Box<uniq/i32 during a> during c) -> uniq/i32 during a => b.item` (SETTLED)", "15.6.3")
            .Fixed("a", "c").Borrow("Li", "b_item", "local", "ret", Ex("a"), Ex("c")).Ret("ret", "b_item", "a").P("a", "c").Fail("P ret c>a");
        yield return new Body("c-peek", "row 9c", "`func peek(r: ref/(ref/i32 during a) during b) -> ref/i32 during a => r@follow@follow@ref`", "15.6.3")
            .Fixed("a", "b").Borrow("Lr", "b_r", "local", "ret", Sh("a"), Sh("b")).Ret("ret", "b_r", "a").P("a", "b");
        yield return new Body("c-peekU", "row 9c", "`func peekU(r: ref/(uniq/i32 during a) during b) -> ref/i32 during a => r@follow@follow@ref`", "15.6.3")
            .Fixed("a", "b").Borrow("Lr", "b_r", "local", "ret", Ex("a"), Sh("b")).Ret("ret", "b_r", "a").P("a", "b").Fail("P ret b>a");
        yield return new Body("c-cut", "row 9c", "E4's cut: `func cut(r: uniq/(ref/(uniq/i32 during a) during b) during c) -> ref/i32 during b => r@follow@follow@follow@ref`", "15.6.3")
            .Fixed("a", "b", "c").Borrow("Lr", "b_r", "local", "ret", Ex("a"), Sh("b"), Ex("c")).Ret("ret", "b_r", "b").P("a", "b").P("b", "c").P("a", "c")
            .Says("rustc accepts the twin `fn cut<'a, 'b, 'c>(r: &'c mut &'b &'a mut i32) -> &'b i32 { &***r }`");
        yield return new Body("c-own", "row 9c", "`func own(input: ref/i32) -> ref/i32 during input`, `let h: obj/Cell = Cell.init(5)`, `return h.value@ref` (an own step)", "15.6.2, 15.6.5")
            .Fixed("input").Borrow("Lh", "b_h", "local", "ret", Own()).Ret("ret", "b_h", "input").Fail("S ret b_h>input");
        yield return new Body("c-pair", "row 9c", "`func pd<s/T>(p: s/(uniq/i32 during a) during b) -> uniq/i32 during a`, `s is owner or uniq`, returns a Reborrow of the referent of p's `uniq/i32`", "15.6.3, 15.6.5 (per case)")
            .In("owner", "uniq").Fixed("a", "b").Borrow("Lp", "b_p", "local", "ret", Ex("a"), PairL("b", "absent", "uniq")).Ret("ret", "b_p", "a").P("a", "b", "uniq")
            .Fail("P ret b>a");

        // audit-origins §3.3 (row 13) and SPEC 15.6.5: judging chains; P6's anchors (SPEC 15.6.4, 11.8).
        yield return new Body("j-bad", "15.6.5", "`func bad(x: ref/i32) -> ref/i32 during x`, `let local: i32 = 1`, `return local@ref`", "15.6.5")
            .Fixed("x").Borrow("Ll", "b_local", "local", "ret").Ret("ret", "b_local", "x").Loan('D', "Ll", "end", true).Fail("S ret b_local>x");
        yield return new Body("j-f", "15.6.5", "`func f(x: ref/i32, y: ref/i32) -> ref/i32 during y`, `let r = x`, `return r`", "15.6.5")
            .Fixed("x", "y").Var("v_r").R("init", "x", "v_r").Ret("ret", "v_r", "y").Fail("P ret x>y");
        yield return new Body("j-pick", "15.6.5", "`func pick<s/T>(b: ref/Box<s/T during a> during c, x: ref/i32 during a) -> ref/i32 during c`, `s is owner or ref`, `return x`", "15.6.5")
            .In("owner", "ref").Fixed("a", "c").Ret("ret", "a", "c").P("a", "c", "ref").Fail("P ret a>c");
        yield return new Body("j-choose", "15.3.6", "`func choose<T>(x: ref/T, y: ref/T) -> ref/T` (during `(x and y)`), `return x`", "15.3.6")
            .Fixed("x", "y").Ret("ret", "x", "x&y");
        yield return new Body("j-region", "row 13", "`func bad(input: ref/i32) -> ref/i32 during input`, `var view = input`, `let local = 5`, `view = local@ref`, `return view` (LocalRegionInferenceTest)", "15.6.5")
            .Fixed("input").Var("v_view").Borrow("Ll", "b_local", "local", "asg").R("init", "input", "v_view").R("asg", "b_local", "v_view", use: true).Ret("ret", "v_view", "input")
            .Loan('D', "Ll", "end", true).Fail("S ret b_local>input");
        yield return new Body("j-region-loop", "row 13", "j-region with `loop => ()` before `return view` (Origins only)", "15.6.5 (neither liveness nor reachability)")
            .Fixed("input").Var("v_view").Borrow("Ll", "b_local", "local", "asg").R("init", "input", "v_view").R("asg", "b_local", "v_view", use: true).Ret("ret", "v_view", "input")
            .Fail("S ret b_local>input");
        yield return new Body("j-region-proof", "row 13", "`func bad(a: ref/i32, b: ref/i32) -> ref/i32 during a`, `var view = a`, `view = b`, `return view`", "15.6.5")
            .Fixed("a", "b").Var("v_view").R("init", "a", "v_view").R("asg", "b", "v_view", use: true).Ret("ret", "v_view", "a").Fail("P ret b>a");
        yield return new Body("j-region-ok", "row 13", "j-region-proof with the result during `(a and b)`", "15.3.6, 15.6.5")
            .Fixed("a", "b").Var("v_view").R("init", "a", "v_view").R("asg", "b", "v_view", use: true).Ret("ret", "v_view", "a&b");
        yield return new Body("j-unreach", "row 13", "`func u(x: ref/i32) -> ref/i32 during x`, `return x`, then `let t = 1`, `return t@ref`", "15.6.5, 14.9")
            .Fixed("x").Borrow("Lt", "b_t", "local", "ret2").Ret("ret1", "x", "x").Ret("ret2", "b_t", "x").Fail("S ret2 b_t>x");
        yield return new Body("j-current", "P6", "`func current(anchor: ref/i32) -> ref/string during anchor => State.text@ref` (mutable static)", "15.6.4, 15.6.5")
            .Fixed("anchor").Borrow("Ls", "b_text", "mstatic", "ret").Ret("ret", "b_text", "anchor").Fail("S ret b_text>anchor");
        yield return new Body("j-leak", "P6", "`func leak(anchor: ref/i32, target: uniq/(ref/string during anchor))`, `target@follow = State.text@ref`", "15.6.4, 15.6.5")
            .Fixed("anchor", "target").Borrow("Ls", "b_text", "mstatic", "store").R("store", "b_text", "anchor").Fail("S store b_text>anchor");
        yield return new Body("j-limitRef", "P6", "`func limitRef() -> ref/i32 during static => Config.limit@ref` (immutable static)", "11.8")
            .Borrow("Ll", "b_limit", "istatic", "ret").Ret("ret", "b_limit", "static");
        yield return new Body("j-nameRef", "P6", "`func nameRef() -> ref/string during static => Config.name@ref` (mutable static)", "11.8, 15.6.5")
            .Borrow("Ln", "b_name", "mstatic", "ret").Ret("ret", "b_name", "static").Fail("S ret b_name>static");
        yield return new Body("j-copyStatic", "P6", "`group G { var slot: ref/i32 during static }`, `func g() -> ref/i32 during static => G.slot` (a Copy, no Borrow)", "11.8")
            .Ret("ret", "static", "static");
        yield return new Body("j-local", "row 13", "`var a = 1`, `let r = a@ref`, `var s = r`, `inspect(s)` (finite to finite only)", "15.6.5")
            .Regions("v_r", "v_s", "p'").Borrow("La", "b_a", "local", "init").R("init", "b_a", "v_r").R("init2", "v_r", "v_s").R("call", "v_s", "p'");
        yield return new Body("j-two", "row 13", "`func two(x: ref/i32, c: bool) -> ref/i32 during x`, `let l1 = 1`, `let l2 = 2`, `var r = x`, `if c => r = l1@ref else => r = l2@ref`, `return r`", "15.6.5, 23.1.2; DIAGNOSTICS 4.3 (each chain a problem)")
            .Fixed("x").Var("v_r").Borrow("L1", "b_l1", "local", "a1").Borrow("L2", "b_l2", "local", "a2")
            .R("init", "x", "v_r").R("a1", "b_l1", "v_r", use: true).R("a2", "b_l2", "v_r", use: true).Ret("ret", "v_r", "x")
            .Loan('D', "L1", "end1", true).Loan('D', "L2", "end2", true).Fail("S ret b_l1>x").Fail("S ret b_l2>x");
        yield return new Body("j-bits", "row 13", "70 parameters `p0` … `p69`, `var v = p0`, `v = p1` … `v = p69`, `return v` during `p0` (two bitset words)", "15.6.5")
            .Fixed(Enumerable.Range(0, 70).Select(i => "p" + i).ToArray()).Var("v").R("init", "p0", "v").Ret("ret", "v", "p0")
            .Each(1, 70, (body, i) => body.R("asg" + i, "p" + i, "v", use: true).Fail($"P ret p{i}>p0"));

        // audit-origins §3.4 (row 19), SPEC 15.3.6 with S3 (OQ12 (a), being integrated by claude/r4-spec-1019).
        yield return new Body("s-demo", "S3", "audit-origins §5: `func demo<T>(x: View<T>, anchor: ref/i32 during s, sink: (View<T> during s) -> ()) -> View<T> during x.source`, `origin x.source outlives s`, `let pair: (View<T>{w}, (View<T>{k}) -> ()) = (x@move, sink@move)` with `origin k.source outlives w.source`, `return pair.0@move`", "15.3.6 (with S3), 15.4.4, 15.6.5")
            .Fixed("x.source", "s").Var("w").Var("k", '-').R("init", "x.source", "w").R("init", "k", "s").R("clause", "k", "w").Ret("ret", "w", "x.source").P("x.source", "s")
            .Says("valid: uses constrain a body variable (15.3.6 'constrained by variance, relations and uses', 15.4.4 'later uses constrain them'); w reaches x.source, k reaches s and, through w's return bound, x.source, so both take x.source; the only chain x.source → w → x.source is Proven. With the declaration's bounds only (CANDDECL, the audit's reading) k takes s and the clause is Unknown");
        yield return new Body("s-none", "S3", "c8c1f3f534b0: `let refs = Collection<ref/Node>.init([])` (an omitted Origin with no bound)", "15.3.6")
            .Var("v_e");
        yield return new Body("s-vars", "S3", "a variable bounded only by variables: `let a: Option<ref/i32> = .None`, `let b: Option<ref/i32> = a`, `inspect(b)`", "15.3.6 (with S3: none is reached, no candidate, a local region)")
            .Regions("v_a", "v_b", "p'").R("init", "v_a", "v_b").R("call", "v_b", "p'");
        yield return new Body("s-vars-fixed", "S3", "s-vars with `let a: Option<ref/i32> = .Some(x)` and `return b` during `y`", "15.3.6 (with S3: x is reached through a), 15.6.5")
            .Fixed("x", "y").Regions("v_a", "v_b").R("init", "x", "v_a").R("init2", "v_a", "v_b").Ret("ret", "v_b", "y").Fail("P ret x>y");
        yield return new Body("s-noprincipal", "S3", "no principal candidate: `let f: (View<i32>{k}) -> () = if c => sink1 else => sink2` (sinks during s1, s2), `f(x@move)` (x during a), `origin a outlives s1`, `origin a outlives s2`", "15.3.6 (no candidate: a local region), 15.6.5")
            .Fixed("s1", "s2", "a").Var("k", '-').R("init", "k", "s1").R("init", "k", "s2").R("call", "a", "k", use: true).P("a", "s1").P("a", "s2");
        yield return new Body("s-noprincipal-err", "S3", "s-noprincipal without `origin a outlives s2`", "15.3.6, 15.6.5")
            .Fixed("s1", "s2", "a").Var("k", '-').R("init", "k", "s1").R("init", "k", "s2").R("call", "a", "k", use: true).P("a", "s1").Fail("P init a>s2");

        // audit-origins §3.5 (row 20), SPEC 15.3.4: a nested function's signature names fixed Origins of its enclosing function.
        yield return new Body("n-use", "15.3.4", "`func use(source: uniq/i32, bound: ref/i32) -> i32` with `origin source outlives bound`, nested `func pass(value: uniq/i32 during bound) -> uniq/i32 during bound => value`, `let child = pass(source)`", "15.3.4")
            .Fixed("source", "bound").Ret("pass", "bound", "bound").R("call", "source", "bound").P("source", "bound");
        yield return new Body("n-pass", "15.3.4", "nested in `use`: `func pass(value: uniq/i32 during bound) -> uniq/i32 during source => value`", "15.3.4, 15.6.5")
            .Fixed("source", "bound").Ret("ret", "bound", "source").P("source", "bound").Fail("P ret bound>source");
        yield return new Body("n-inner", "15.3.4", "nested in `use`: `func inner() -> ref/i32 during bound`, `let t = 1`, `return t@ref`", "15.3.4, 15.6.5")
            .Fixed("source", "bound").Borrow("Lt", "b_t", "local", "ret").Ret("ret", "b_t", "bound").P("source", "bound").Fail("S ret b_t>bound");

        // Raw anchors (SPEC 5.2.2, 15.6.5; 2a0-0's finding 4).
        yield return new Body("a-split", "raw", "Storage.splitFirst: `func splitFirst<E>(state: uniq/UniqRemainder<E>) -> Option<uniq/E during state.source>`, `return .Some(state.storage[position]@uniq)`", "5.2.2, 15.6.3, 15.6.5")
            .Fixed("state", "state.source").Borrow("Le", "b_elem", "local", "ret", Raw(), Ex("state")).Ret("ret", "b_elem", "state.source").P("state.source", "state");
        yield return new Body("a-wf", "raw", "`func viaAnchor(p: raw/(ref/i32 during a), anchor: ref/i32 during r) -> ref/(ref/i32 during a) during r`, `unsafe => return (*p)@ref` (a chain a → anchor → r)", "5.2.2, 15.6.1, 15.6.5")
            .Fixed("a", "r").Borrow("Lp", "b_anchor", "local", "ret", Raw()).R("wf", "a", "b_anchor").Ret("ret", "b_anchor", "r").Ret("ret", "a", "a").P("a", "r");

        // Guards (SPEC 14.8.3; A3 E7) and the E12/E7 merge (PD11).
        yield return new Body("g-return", "merge", "p12 guardReturn: `func guardReturn(packet: ref/Packet, fallback: ref/Data) -> ref/Data during packet`, `var saved = fallback`, `match packet@follow`, `.Data(let d) if keep(saved@uniq, d) => ()`, `return saved` (keep: `slot: uniq/(ref/Data during s), value: ref/Data during s`)", "14.8.3, 15.6.5")
            .Fixed("packet", "fallback").Var("v_saved").Var("s'", '=').Var("o'").Borrow("Ld", "g_d", "guard", "cand", Sh("packet")).Borrow("La", "b_saved", "local", "call")
            .R("init", "fallback", "v_saved").R("call", "b_saved", "o'").Eq("call", "v_saved", "s'").R("call", "g_d", "s'").Ret("ret", "v_saved", "packet")
            .Loan('E', "Ld", "cleanup", true).Fail("S ret g_d>packet", "C cleanup Ld").Fail("P ret fallback>packet");
        yield return new Body("g-local", "merge", "SPEC 14.8.3's example (p03 guard1): `var saved = fallback@ref` (a local), `match packet`, `.Data(let d) if keep(saved@uniq, d) => ()`, `use(saved)`", "14.8.3")
            .Regions("v_saved", "o'", "u'").Var("s'", '=').Borrow("Lf", "b_f", "local", "init").Borrow("Ld", "g_d", "guard", "cand").Borrow("La", "b_saved", "local", "call")
            .R("init", "b_f", "v_saved").R("call", "b_saved", "o'").Eq("call", "v_saved", "s'").R("call", "g_d", "s'").R("use", "v_saved", "u'", use: true)
            .Loan('E', "Ld", "cleanup", false).Fail("C cleanup Ld");
        yield return new Body("g-two", "merge", "g-return with `var other = fallback`, the guard `keep(saved@uniq, d) and keep(other@uniq, d)` and `use(other)` after the match", "14.8.3, 15.6.5, 23.1.2")
            .Fixed("packet", "fallback").Regions("v_saved", "v_other", "o'", "o2'", "u'").Var("s'", '=').Var("s2'", '=')
            .Borrow("Ld", "g_d", "guard", "cand", Sh("packet")).Borrow("La", "b_saved", "local", "call").Borrow("Lb", "b_other", "local", "call2")
            .R("init", "fallback", "v_saved").R("init2", "fallback", "v_other").R("call", "b_saved", "o'").Eq("call", "v_saved", "s'").R("call", "g_d", "s'")
            .R("call2", "b_other", "o2'").Eq("call2", "v_other", "s2'").R("call2", "g_d", "s2'").R("use", "v_other", "u'", use: true).Ret("ret", "v_saved", "packet")
            .Loan('E', "Ld", "cleanup", true).Fail("S ret g_d>packet", "C cleanup Ld").Fail("C cleanup Ld").Fail("P ret fallback>packet")
            .Says("other keeps d's guard-local Origin past the cleanup and is used below (SPEC 14.8.3's example): an independent failure only err(ended) reports");
        yield return new Body("m-36f", "merge", "36f without its write: `func f<S>(src: uniq/S) -> Option<S.Item>`, `S is Take`, `var h = H(inner: src, other: 0)`, `var r = h@uniq`, `let first = r.inner.take()`, `return first@move`", "15.6.3 (P12), 15.6.5, 15.6.6")
            .Fixed("src").Regions("v_h", "v_r", "self'").Borrow("Lh", "b_h", "local", "borrow").Borrow("Lr1", "b_recv", "local", "recv", Ex("v_h"), Ex("v_r"))
            .R("init", "src", "v_h").R("borrow", "b_h", "v_r").R("recv", "b_recv", "self'").Loan('D', "Lh", "end", true).Fail("C end Lh");
        yield return new Body("m-write", "merge", "`func w(p: ref/i32) -> ref/i32 during p`, `var x = 1`, `let r = x@ref`, `x = 1`, `return r`", "15.6.2, 15.6.5, 23.1.2")
            .Fixed("p").Var("v_r").Borrow("Lx", "b_x", "local", "borrow").R("borrow", "b_x", "v_r").Ret("ret", "v_r", "p")
            .Loan('W', "Lx", "asg", false).Fail("C asg Lx").Fail("S ret b_x>p");
        yield return new Body("m-pair", "merge", "P1's other Borrow: `func pr(p: ref/i32) -> ref/i32 during p`, `var x = 1`, `var y = 2`, `let pair = (x@ref, y@ref)`, `return pair.0`", "15.6 (holders), 15.6.5, 15.6.6")
            .Fixed("p").Regions("v1", "v2").Borrow("Lx", "b_x", "local", "init").Borrow("Ly", "b_y", "local", "init").R("init", "b_x", "v1").R("init", "b_y", "v2").Ret("ret", "v1", "p")
            .Loan('D', "Lx", "endx", true).Loan('D', "Ly", "endy", true).Fail("S ret b_x>p").Fail("C endy Ly");
        yield return new Body("m-scope", "merge", "`func f(x: ref/i32) -> ref/i32 during x`, `var r = x`, `do { let local = 1; r = local@ref }`, `return r`", "15.6.5, 23.1.2")
            .Fixed("x").Var("v_r").Borrow("Ll", "b_local", "local", "asg").R("init", "x", "v_r").R("asg", "b_local", "v_r", use: true).Ret("ret", "v_r", "x")
            .Loan('D', "Ll", "scope", false).Fail("S ret b_local>x").Opt("C scope Ll")
            .Says("the scope end conflict is caused only by the return (SPEC 23.1.2 leaves it to the implementation); PD11 keeps it, `_0` not yet carrying the Loan");
        yield return new Body("m-scope-use", "merge", "m-scope with `inspect(r)` after the `do`", "15.6.5, 23.1.2")
            .Fixed("x").Regions("v_r", "u'").Borrow("Ll", "b_local", "local", "asg").R("init", "x", "v_r").R("asg", "b_local", "v_r", use: true).R("use", "v_r", "u'", use: true).Ret("ret", "v_r", "x")
            .Loan('D', "Ll", "scope", false).Fail("S ret b_local>x").Fail("C scope Ll");
        yield return new Body("m-store", "merge", "`func leakL(target: uniq/(ref/i32 during a))`, `let local = 1`, `target@follow = local@ref` (storage the body does not own, not `_0`)", "15.6, 15.6.5, 23.1.2")
            .Fixed("a", "target").Borrow("Ll", "b_local", "local", "store").R("store", "b_local", "a").Loan('D', "Ll", "end", false).Fail("S store b_local>a").Opt("C end Ll");

        // Classes: 64 views of 6 pair binders `s_i is owner or ref`; only s_0's case changes a premise, so two classes run.
        var views = Enumerable.Range(0, 64).Select(i => Convert.ToString(i, 2).PadLeft(6, '0')).ToArray();
        yield return new Body("k-views", "classes", "`func k<s0/T0, …, s5/T5>(b: ref/Box<s0/T0 during a> during c, x: ref/i32 during a, …) -> ref/i32 during c`, each `s_i is owner or ref`, `return x`", "15.6.5 (per case), 23.1.2 (one failure in several cases)")
            .In(views).Fixed("a", "c").Ret("ret", "a", "c").P("a", "c", string.Join(',', views.Where(v => v[5] == '1'))).Fail("P ret a>c");
    }

    static Body Each(this Body b, int from, int to, Action<Body, int> add) { for (int i = from; i < to; i++) add(b, i); return b; }

    // ---------------------------------------------------------------------------------------------------------------- the run
    static readonly Cfg AllSourcesCfg = new("AllSources") { AllSources = true };
    static readonly Cfg[] StratumCfgs =
    [
        Design, AllSourcesCfg, new("RootCut") { RootCut = true }, new("NoContain") { NoContain = true }, new("RawContain") { RawContain = true },
        new("OwnAsBorrow") { OwnAsBorrow = true }, new("AnchorTop") { AnchorTop = true }, new("AnchorFinite") { AnchorFinite = true },
        new("GuardInferred") { GuardInferred = true }, new("CAND") { Cand = true },
    ];

    static readonly Cfg[] MergeCfgs =
    [
        Design, new("NoMerge") { Merge = "None" }, new("Membership") { Merge = "Membership" }, new("ByBorrow") { Merge = "ByBorrow" },
        new("GuardReverse") { Merge = "GuardReverse" }, new("AllSources+GuardReverse") { AllSources = true, Merge = "GuardReverse" },
    ];

    static readonly Cfg[] InferenceCfgs = [Design, new("CAND") { Cand = true }, new("CANDDECL") { Cand = true, CandDecl = true }, new("NoCandErr") { Cand = true, NoCandErr = true }];

    public static int Run(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        bool verbose = args.Contains("-v");
        int ri = Array.IndexOf(args, "-random");
        int randomCount = ri >= 0 ? int.Parse(args[ri + 1]) : 3000;
        var cases = AllCases().ToList();
        int failures = 0, findings = 0;

        Console.WriteLine($"## Cases under the design ({cases.Count})\n\n| Case | Program | SPEC | design | Reports |\n| --- | --- | --- | --- | --- |");
        foreach (var c in cases)
        {
            var o = Analyze(c, Design);
            var v = Verdict(c, o.Final);
            if (v is "UNSOUND" or "OVERSTRICT") failures++;
            else if (v != "ok") findings++;
            if (o.RefDisagreement != null) { failures++; v += " REFERENCE: " + o.RefDisagreement; }
            var shown = o.Final.Count > 6 ? string.Join("; ", o.Final.Take(3)) + $"; … ({o.Final.Count})" : string.Join("; ", o.Final);
            Console.WriteLine($"| {c.Id} | {c.Program} | {Spec(c)} | {v} | {(shown.Length == 0 ? "none" : shown)} |");
            if (verbose && c.Note != null) Console.WriteLine($"|   | {c.Note} | | | |");
        }

        void Table(string title, IEnumerable<Body> rows, Cfg[] cfgs)
        {
            Console.WriteLine($"\n## {title}\n\n| Case | {string.Join(" | ", cfgs.Select(x => x.Name))} |\n|{string.Concat(Enumerable.Repeat(" --- |", cfgs.Length + 1))}");
            foreach (var c in rows)
            {
                var cells = cfgs.Select(x => Analyze(c, x)).Select(o => Verdict(c, o.Final)).ToList();
                Console.WriteLine($"| {c.Id} | {string.Join(" | ", cells)} |");
                if (verbose)
                    foreach (var x in cfgs) Console.WriteLine($"|   {x.Name}: {string.Join("; ", Analyze(c, x).Final)} |");
            }
        }

        Table("Stratum variants", cases, StratumCfgs);
        Table("Merge variants (cases with Loan problems)", cases.Where(c => c.Loans.Count > 0), MergeCfgs);
        Table("Inference (OJ5's candidates substituted into the rows; S3)", cases.Where(c => c.Vars.Count > 0 && c.Group is "S3" or "row 7" or "row 13"), InferenceCfgs);

        Console.WriteLine("\n## Classes and cost\n\n| Case | Views | Classes | Work |\n| --- | ---: | ---: | ---: |");
        foreach (var c in cases.Where(c => c.Views.Length > 1)) { var o = Analyze(c, Design); Console.WriteLine($"| {c.Id} | {c.Views.Length} | {o.Classes} | {o.Work} |"); }

        Console.WriteLine("\n## Scaling (V16 shape (9): N chained locals and N signature Origins; and N finite Borrows into one chain)\n");
        Console.WriteLine("| N | Nodes N_o | Edges E_o | B = N_o + E_o·⌈F/64⌉ | design work | /B | AllSources work | /(B + E_o·⌈Fin/64⌉) | design S | AllSources S |\n| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var n in new[] { 64, 128, 256, 512 })
        {
            var b = new Body("shape9", "scaling", "", "").Fixed("r");
            for (int i = 0; i < n; i++) b.Fixed("a" + i).Var("v" + i).R("in" + i, "a" + i, "v0").P("a" + i, "r").Borrow("L" + i, "l" + i, "local", "b" + i).R("b" + i, "l" + i, "v0");
            for (int i = 0; i + 1 < n; i++) b.R("c" + i, "v" + i, "v" + (i + 1));
            b.Ret("ret", "v" + (n - 1), "r");
            var d = Analyze(b, Design); var a = Analyze(b, AllSourcesCfg);
            var g = Build(b, 0, Design, null);
            long bound = g.Name.Count + (long)g.E.Count * ((n + 63) / 64), extra = (long)g.E.Count * ((n + 63) / 64); // F = Fin = N here
            Console.WriteLine($"| {n} | {g.Name.Count} | {g.E.Count} | {bound} | {d.Work} | {(double)d.Work / bound:F2} | {a.Work} | {(double)a.Work / (bound + extra):F2} | {d.Origin.Count(r => r.Code == 'S')} | {a.Origin.Count(r => r.Code == 'S')} |");
        }

        // Random graphs: the design against SPEC 15.6.5's definition.
        var rnd = new Random(20261019);
        int bad = 0;
        for (int t = 0; t < randomCount; t++)
        {
            var b = new Body("rnd" + t, "random", "", "");
            int n = rnd.Next(2, 13);
            var names = new List<string>();
            for (int i = 0; i < n; i++)
            {
                var name = "n" + i; names.Add(name);
                switch (rnd.Next(10))
                {
                    case < 4: b.Fixed(name); break;
                    case < 6: b.Borrow("L" + i, name, rnd.Next(3) switch { 0 => "local", 1 => "mstatic", _ => "guard" }, "b" + i); break;
                    default: b.Var(name); break;
                }
            }

            for (int e = rnd.Next(0, 2 * n + 1); e > 0; e--)
                if (rnd.Next(5) == 0) b.Eq("e" + e, names[rnd.Next(n)], names[rnd.Next(n)]);
                else b.R("e" + e, names[rnd.Next(n)], names[rnd.Next(n)]);
            var fixeds = names.Where(x => b.Atoms.TryGetValue(x, out var k) && k == Kind.Fixed).Append("static").ToList();
            for (int p = rnd.Next(0, 4); p > 0; p--) b.P(fixeds[rnd.Next(fixeds.Count)], fixeds[rnd.Next(fixeds.Count)]);
            var o = Analyze(b, Design);
            if (o.RefDisagreement != null) { bad++; if (bad <= 5) Console.WriteLine($"random {t}: {o.RefDisagreement}"); }
        }

        Console.WriteLine($"\nRandom graphs: {randomCount}, reference disagreements {bad}.");
        failures += bad;
        Console.WriteLine($"Design: {failures} failures (UNSOUND, OVERSTRICT or reference), {findings} report findings (LOST, EXTRA, DOUBLE).");
        return failures;
    }
}

#if F4LOANS
// -loans: the Loan problems F4's merge cases take as inputs, computed by A5RulesPrototype.cs's design (design A with E8's ending, rounds
// 13 and 14: new Analysis(c, Mode.A, Abl.None, prefixKill: true)). Each node label starts with its site, "[site] …".
static class F4Loans
{
    const bool Shared = false, Uniq = true;

    public static int Run()
    {
        var bodies = F4.AllCases().ToDictionary(b => b.Id);
        var field = typeof(Analysis).GetField("In", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        int fails = 0;
        Console.WriteLine("| F4 case | Loan problems (A5RulesPrototype design) | F4's inputs | |\n| --- | --- | --- | --- |");
        foreach (var c in Encodings())
        {
            var a = new Analysis(c, Mode.A, Abl.None, prefixKill: true);
            var state = (HashSet<(string H, string L)>[])field.GetValue(a);
            static string Site(string label) => label[1..label.IndexOf(']')];
            var got = a.Errors.Select(e =>
            {
                char kind = e.What.StartsWith("Destroy") ? 'D' : e.What.StartsWith("Write") ? 'W' : e.What.StartsWith("ended") ? 'E' : '?';
                bool zero = kind == 'E' ? state.Any(s => s.Contains(("$ret", e.L))) : state[e.P].Contains(("$ret", e.L));
                return new F4.Lp(kind, e.L.StartsWith('γ') ? "L" + e.L[1..] : e.L, Site(a.LabelAt(e.P)), zero);
            }).ToHashSet();
            var want = bodies[c.Id].Loans.ToHashSet();
            bool ok = got.SetEquals(want);
            if (!ok) fails++;
            Console.WriteLine($"| {c.Id} | {string.Join("; ", got)} | {string.Join("; ", want)} | {(ok ? "equal" : "DIFFERENT")} |");
        }

        return fails;
    }

    static IEnumerable<Case> Encodings()
    {
        Case c;
        c = new("j-bad", "");
        c.N("[entry] func bad(x: ref/i32)").Param("Lx", "x");
        c.N("[decl] let local: i32 = 1").Def("local");
        c.N("[ret] _0 = local@ref").Borrow("Ll", "local", Shared, "$ret");
        c.N("[end] StorageDead local").A(K.Destroy, "local").Def("local");
        c.N("[return] return").Use("$ret");
        yield return c;

        c = new("j-region", "");
        c.N("[entry] func bad(input: ref/i32)").Param("Li", "input");
        c.N("[init] var view = input").Def("view").Flow("input", "view");
        c.N("[decl] let local = 5").Def("local");
        c.N("[asg] view = local@ref").Assign("view").Borrow("Ll", "local", Shared, "view");
        c.N("[ret] _0 = view").Flow("view", "$ret");
        c.N("[end] StorageDead local").A(K.Destroy, "local").Def("local");
        c.N("[return] return").Use("$ret");
        yield return c;

        c = new("j-two", "");
        c.N("[entry] func two(x: ref/i32, c: bool)").Param("Lx", "x");
        c.N("[decl] let l1 = 1; let l2 = 2").Def("l1", "l2");
        c.N("[init] var r = x").Def("r").Flow("x", "r");
        c.N("[if] if c").Goto("[a1] r = l1@ref", "[a2] r = l2@ref");
        c.N("[a1] r = l1@ref").Assign("r").Borrow("L1", "l1", Shared, "r").Goto("[ret] _0 = r");
        c.N("[a2] r = l2@ref").Assign("r").Borrow("L2", "l2", Shared, "r");
        c.N("[ret] _0 = r").Flow("r", "$ret");
        c.N("[end1] StorageDead l1").A(K.Destroy, "l1").Def("l1");
        c.N("[end2] StorageDead l2").A(K.Destroy, "l2").Def("l2");
        c.N("[return] return").Use("$ret");
        yield return c;

        foreach (var id in new[] { "g-return", "g-local", "g-two" })
        {
            c = new(id, "");
            if (id == "g-local")
            {
                c.N("[decl] let fallback = Data.init(); let packet: Packet = .Data(Data.init())").Def("fallback", "packet");
                c.N("[init] var saved = fallback@ref").Def("saved").Borrow("Lf", "fallback", Shared, "saved");
                c.N("[subject] $s = packet@ref (Subject, shared)").Def("$s").Borrow("Lsub", "packet", Shared, "$s");
            }
            else
            {
                c.N("[entry] func guardReturn(packet: ref/Packet, fallback: ref/Data)").Param("Lp", "packet").Param("Lf", "fallback");
                c.N("[init] var saved = fallback").Def("saved").Flow("fallback", "saved");
                if (id == "g-two") c.N("[init2] var other = fallback").Def("other").Flow("fallback", "other");
                c.N("[subject] $s = packet@follow (Subject, shared)").Def("$s").Borrow("Lsub", "packet.*", Shared, "$s", sharedAt: 1);
            }

            c.N("[cand] d = $s.data (candidate)").Def("d").Borrow("Ld", "$s.*.data", Shared, "d", sharedAt: 1);
            c.N("[arg] $a = saved@uniq (keep's argument, reserved)").Def("$a").Borrow("La", "saved", Uniq, "$a", reserve: true);
            c.N("[call] keep($a, d)").Activate("La").Through("$a.*", "d");
            if (id == "g-two")
            {
                c.N("[arg2] $b = other@uniq (keep's argument, reserved)").Def("$b").Borrow("Lb", "other", Uniq, "$b", reserve: true);
                c.N("[call2] keep($b, d)").Activate("Lb").Through("$b.*", "d");
            }

            c.N("[cleanup] guard cleanup: EndBorrow d").EndBorrow("d");
            if (id == "g-local") { c.N("[use] use(saved)").Use("saved"); yield return c; continue; }
            if (id == "g-two") c.N("[use] use(other)").Use("other");
            c.N("[ret] _0 = saved").Flow("saved", "$ret");
            c.N("[return] return").Use("$ret");
            yield return c;
        }

        c = new("m-36f", "");
        c.N("[entry] func f(src: uniq/S)").Param("Ls", "src");
        c.N("[init] var h = H(inner: src, other: 0)").Def("h").Borrow("Lsrc", "src.*", Uniq, "h.inner");
        c.N("[borrow] var r = h@uniq").Def("r").Borrow("Lh", "h", Uniq, "r");
        c.N("[recv] $r1 = r.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "r.*.inner.*", Uniq, "$r1", reserve: true);
        c.N("[call] let first = r.inner.take()").Activate("Lr1").Def("first").Phantom("Phi1", "r.*.inner.*", "first", "Lr1");
        c.N("[ret] _0 = first").Flow("first", "$ret");
        c.N("[end] StorageDead h").A(K.Destroy, "h").Def("h");
        c.N("[return] return").Use("$ret");
        yield return c;

        c = new("m-write", "");
        c.N("[entry] func w(p: ref/i32)").Param("Lp", "p");
        c.N("[decl] var x = 1").Def("x");
        c.N("[borrow] let r = x@ref").Def("r").Borrow("Lx", "x", Shared, "r");
        c.N("[asg] x = 1").Assign("x");
        c.N("[ret] _0 = r").Flow("r", "$ret");
        c.N("[end] StorageDead x").A(K.Destroy, "x").Def("x");
        c.N("[return] return").Use("$ret");
        yield return c;

        c = new("m-pair", "");
        c.N("[entry] func pr(p: ref/i32)").Param("Lp", "p");
        c.N("[decl] var x = 1; var y = 2").Def("x", "y");
        c.N("[init] let pair = (x@ref, y@ref)").Def("pair").Borrow("Lx", "x", Shared, "pair.0").Borrow("Ly", "y", Shared, "pair.1");
        c.N("[ret] _0 = pair.0").Flow("pair.0", "$ret");
        c.N("[endx] StorageDead x").A(K.Destroy, "x").Def("x");
        c.N("[endy] StorageDead y").A(K.Destroy, "y").Def("y");
        c.N("[return] return").Use("$ret");
        yield return c;

        foreach (var id in new[] { "m-scope", "m-scope-use" })
        {
            c = new(id, "");
            c.N("[entry] func f(x: ref/i32)").Param("Lx", "x");
            c.N("[init] var r = x").Def("r").Flow("x", "r");
            c.N("[decl] let local = 1 (in the do)").Def("local");
            c.N("[asg] r = local@ref").Assign("r").Borrow("Ll", "local", Shared, "r");
            c.N("[scope] StorageDead local (end of the do)").A(K.Destroy, "local").Def("local");
            if (id == "m-scope-use") c.N("[use] inspect(r)").Use("r");
            c.N("[ret] _0 = r").Flow("r", "$ret");
            c.N("[return] return").Use("$ret");
            yield return c;
        }

        c = new("m-store", "");
        c.N("[entry] func leakL(target: uniq/(ref/i32 during a))").Param("Lt", "target");
        c.N("[decl] let local = 1").Def("local");
        c.N("[borrow] $v = local@ref").Def("$v").Borrow("Ll", "local", Shared, "$v");
        c.N("[store] target@follow = $v").Through("target.*", "$v");
        c.N("[end] StorageDead local").A(K.Destroy, "local").Def("local");
        c.N("[return] return");
        yield return c;
    }
}
#endif // F4LOANS
