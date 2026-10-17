#:property Nullable=disable
// Executable prototype of the SPEC §15.6 Loan rules (plan §4.1-§4.3) with the S1 kill option (plan §5.2).
// Run: dotnet run proto.cs        (-v prints every error fact per mode)
// Modes: PROV = provenance Loan places, no Loan kill (current SPEC with one static Loan ID per site);
//        S1   = Loans named from their base, killed when the base is replaced (an exchange/swap target counts as
//               replaced); Move of a base is deep (S1 as written);
//        S1P  = S1′, the plan's S1 (e32dc0ef §4.1-§4.3): `ended` only on destroyed_in_place (=, replace, end of storage);
//               base_op = Move, exchange/swap target, exclusive borrow other than as a `replace` target.
using System.Text;
using Fact = (string H, string L);

var verbose = args.Contains("-v");
var modes = new[] { Mode.PROV, Mode.S1, Mode.S1P };
if (args.Length == 2 && args[0] == "-dump") // dotnet run proto.cs -- -dump 8b : per-node carries, liveness and Loan places
{
    foreach (var m in modes.Append(Mode.A)) Console.WriteLine($"== {m}\n" + new Analysis(Cases.All().Single(c => c.Id == args[1]), m).Dump());
    return 0;
}
var table3 = new StringBuilder("| Case | SPEC | S1′ | design A | Class |\n| --- | --- | --- | --- | --- |\n");
string V(Analysis a) => a.Errors.Count == 0 ? "valid" : "error at `" + string.Join("`, `", a.ErrorNodes()) + "`";
string Cls(Case c, Analysis a)
{
    bool ae = a.Errors.Count > 0;
    if (ae == c.Err) return !ae || a.ErrorNodes().ToHashSet().SetEquals(c.At) ? "same" : "same verdict, error moves";
    if (!ae) return "**(c) UNSOUND**";
    return c.S1Err == true ? "(a) S1′ loss" : c.ALoss != null ? "(a) " + c.ALoss : "**(b) unlisted loss**";
}
var table = new StringBuilder();
table.AppendLine("| Case | Program | SPEC | PROV | S1 intended | S1 as written | S1′ (final rules) |");
table.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
int finalFails = 0;
foreach (var c in Cases.All())
{
    var r = modes.ToDictionary(m => m, m => new Analysis(c, m));
    bool s1Err = c.S1Err ?? c.Err; var s1At = c.S1At ?? c.At;
    string Exp(bool e, string[] at) => e ? "error" + (at.Length > 0 ? " at `" + string.Join("`, `", at) + "`" : "") : "valid";
    (string Text, bool Ok) Got(Analysis a, bool e, string[] at)
    {
        var nodes = a.ErrorNodes().ToList();
        var extra = c.Extra?.Invoke(a);
        bool ok = (nodes.Count > 0) == e && (!e || at.Length == 0 || nodes.ToHashSet().SetEquals(at)) && extra == null;
        var tag = ok ? "pass" : e && nodes.Count == 0 ? "**FAIL: UNSOUND**" : "FAIL";
        var v = nodes.Count == 0 ? "valid" : "error at `" + string.Join("`, `", nodes) + "`";
        return ($"{v} ({tag}{(extra != null ? ": " + extra : "")})", ok);
    }
    var prov = Got(r[Mode.PROV], c.Err, c.At); var s1f = Got(r[Mode.S1P], s1Err, s1At);
    var s1 = c.S1POnly ? Got(r[Mode.S1], c.Err, c.At) : Got(r[Mode.S1], s1Err, s1At);
    if (!s1f.Ok) finalFails++;
    var am = new Analysis(c, Mode.A);
    table3.AppendLine($"| {c.Id} | {(c.Err ? "error" : "valid")} | {V(r[Mode.S1P])} | {V(am)} | {Cls(c, am)} |");
    if (verbose) Console.WriteLine($"{c.Id,-4} A   : " + (am.Errors.Count == 0 ? "no errors" : string.Join("; ", am.Errors.Select(e => $"{e.L} live at `{c.Nodes[e.P].Label}` vs {e.What}"))));
    table.AppendLine($"| {c.Id} | {c.Title} | {Exp(c.Err, c.At)} | {prov.Text} | {Exp(s1Err, s1At)}{(c.S1POnly ? " (S1′ only)" : "")} | {s1.Text} | {s1f.Text} |");
    if (verbose)
        foreach (var m in modes)
            Console.WriteLine($"{c.Id,-4} {m,-4}: " + (r[m].Errors.Count == 0 ? "no errors" :
                string.Join("; ", r[m].Errors.Select(e => $"{e.L} live at `{c.Nodes[e.P].Label}` vs {e.What}"))));
}
Console.WriteLine(table);
Console.WriteLine(finalFails == 0 ? "S1′: all cases pass." : $"S1′: {finalFails} case(s) fail.");
Console.WriteLine("\nDesign A (round 3):\n" + table3);

// Ablations: drop one refinement of the final rules (S1′) and list the cases whose verdict changes, judged by the SPEC.
Console.WriteLine("\n| Variant of S1′ | Cases that change |\n| --- | --- |");
var aVar = Cases.All().Select(c => (c, a: new Analysis(c, Mode.A, Abl.AIssueNotEnded), b: new Analysis(c, Mode.A)))
    .Where(x => !x.a.ErrorNodes().SequenceEqual(x.b.ErrorNodes())).Select(x => $"{x.c.Id}: {V(x.a)}");
Console.WriteLine("Design A without the round-3 fix (issued Loan not ended at its base's destruction): " + string.Join("; ", aVar.DefaultIfEmpty("none")));
// Round 4: exclusion by Loan id (old) against exclusion by holder (new), in S1′ and A modes; design A's letter (AText).
Console.WriteLine("\nRound 4 (rows that differ, plus the loop cases):\n| Case | SPEC | S1′ by id | S1′ by holder | A by id | A text | A by holder |\n| --- | --- | --- | --- | --- | --- | --- |");
foreach (var c in Cases.All())
{
    var row = new[] { new Analysis(c, Mode.S1P, Abl.IdExclusion), new Analysis(c, Mode.S1P), new Analysis(c, Mode.A, Abl.IdExclusion), new Analysis(c, Mode.A, Abl.AText), new Analysis(c, Mode.A) };
    string W(Analysis a) => V(a) + (c.Err && a.Errors.Count == 0 ? " **UNSOUND**" : "");
    if (c.Id.StartsWith("20") || !row[0].ErrorNodes().SequenceEqual(row[1].ErrorNodes()) || !row[2].ErrorNodes().SequenceEqual(row[4].ErrorNodes()) || !row[3].ErrorNodes().SequenceEqual(row[4].ErrorNodes()))
        Console.WriteLine($"| {c.Id} | {(c.Err ? "error" : "valid")} | {string.Join(" | ", row.Select(W))} |");
}
var phantomVariants = new[] { Abl.PhantomAlone, Abl.PhantomKeepsReceiver, Abl.PhantomParentsAtCall };
foreach (var ab in new[] { Abl.KillAcrossDeref, Abl.FirstDerefBase, Abl.NoTransit, Abl.NoSharedCut }.Concat(phantomVariants))
{
    var aKill = Cases.All().Select(c => (c, a: new Analysis(c, Mode.A, ab), b: new Analysis(c, Mode.A)))
        .Where(x => !x.a.ErrorNodes().SequenceEqual(x.b.ErrorNodes())).Select(x => $"{x.c.Id}: {V(x.a)}" + (x.c.Err && x.a.Errors.Count == 0 ? " **UNSOUND**" : ""));
    Console.WriteLine($"Design A with {ab}: " + string.Join("; ", aKill.DefaultIfEmpty("no change")));
}
// Round 9 (G113): the F6 cases under design A, its first-Deref variant and each rejected F6 option; class counts.
Console.WriteLine("\nRound 9 (F6 cases):\n| Case | SPEC | A | A FirstDerefBase | " + string.Join(" | ", phantomVariants) + " |\n| --- | --- | --- | --- | --- | --- | --- |");
foreach (var c in Cases.All().Where(c => c.Id.StartsWith("33") || c.Id.StartsWith("36")))
{
    string W(Analysis a) => V(a) + (c.Err && a.Errors.Count == 0 ? " **UNSOUND**" : "");
    var row = new[] { Abl.None, Abl.FirstDerefBase }.Concat(phantomVariants).Select(x => W(new Analysis(c, Mode.A, x)));
    Console.WriteLine($"| {c.Id} | {(c.Err ? "error" : "valid")} | {string.Join(" | ", row)} |");
}
foreach (var ab in new[] { Abl.None, Abl.FirstDerefBase })
    Console.WriteLine($"Design A{(ab == Abl.None ? "" : " with " + ab)}: " + string.Join(", ", Cases.All()
        .GroupBy(c => Cls(c, new Analysis(c, Mode.A, ab)) is var s && s.StartsWith("(a)") ? "listed loss" : s).OrderBy(g => g.Key)
        .Select(g => $"{g.Count()} {g.Key}" + (g.Key.Contains("(b)") || g.Key.Contains("(c)") ? " (" + string.Join(", ", g.Select(c => c.Id)) + ")" : ""))));
foreach (var ab in Enum.GetValues<Abl>().Skip(1).Where(x => x is not (Abl.AIssueNotEnded or Abl.IdExclusion or Abl.AText or Abl.LastDerefBase or Abl.FirstDerefBase) && !phantomVariants.Contains(x)))
{
    string At(Analysis a) => "`" + string.Join("`, `", a.ErrorNodes()) + "`";
    var changed = Cases.All().Select(c => (c, a: new Analysis(c, Mode.S1P, ab), b: new Analysis(c, Mode.S1P)))
        .Where(x => !x.a.ErrorNodes().SequenceEqual(x.b.ErrorNodes()))
        .Select(x => x.c.Id + ": " + (x.a.Errors.Count == 0
            ? x.c.Err ? "**UNSOUND** (accepted)" : "accepted (SPEC-valid, a loss recovered)"
            : x.c.Err ? "error moves to " + At(x.a) : (x.c.S1Err == true ? "rejected (an intended loss) at " : "over-strict: error at ") + At(x.a)));
    Console.WriteLine($"| {ab} | {string.Join("; ", changed.DefaultIfEmpty("none"))} |");
}
return finalFails;

// One refinement removed each. NoMoveBaseOp, NoUniqBaseOp, ExchangeKills and NoReplaceException change one S1′ clause;
// BaseOpMovePathsOnly limits base_op's K to move paths (plan §4.3 read literally). S1ResolvePlaces is the alternative fix:
// no exclusive-borrow base_op, but Loan places resolved at use.
enum Abl { None, KillKeepOnly, NoMaterialize, SyntacticAccess, NoAuthority, NoClosureFeedback, FlatFlow, NoReservation,
    NoMoveBaseOp, NoUniqBaseOp, BaseOpMovePathsOnly, ExchangeKills, NoReplaceException, S1ResolvePlaces, AIssueNotEnded,
    IdExclusion, AText, // round 4: the pre-fix by-id rule (S1′ and A), and design A's letter (A mode)
    KillAcrossDeref,    // round 5: `ended` on the first move path entered (round 2), not only with no Deref between B and K
    NoTransit,          // round 5: the round-4 rule without the in-transit facts
    LastDerefBase,      // round 6: A mode with the plan's B (last Deref) instead of A2's root-most B
    NoSharedCut,        // round 6: an observing destruction reaches exclusively past shared Derefs
    SharedBaseOp,       // round 6: S1′ plus P3 (a shared borrow of the base or an ancestor is a base operation vs an exclusive child)
    P3RootMost,         // round 6: S1′ plus P3 plus the root-most kill (KillAcrossDeref)
    FirstDerefBase,     // round 8: A mode with the first-Deref B; K an exact prefix of B, any index after K (case 23a proposal)
    PhantomAlone,       // round 9: A mode with rounds 7-8's F6 encoding: the result keeps Φ_c without the receiver's Loans
    PhantomKeepsReceiver, // round 9: A mode, the result keeps the receiver's whole value, its own Loan R included
    PhantomParentsAtCall } // round 9: A mode, Φ_c issued at the call with R2's parents of recv(c), not the receiver's value

enum K { Read, Write, Move, SBorrow, UBorrow, Destroy }
enum Mode { PROV, S1, S1P, A }
record Issue(string Id, string Place, bool Uniq, string Into, bool Reserve, bool Param, bool InPlace = false, bool Phantom = false, string Recv = null);
record Acc(K Kind, string Place, string Except, bool InPlace = false); // InPlace: the exclusive borrow is a `replace` target
record Res(HashSet<string> Names, HashSet<string> Leaves, HashSet<string> Auth);

// Holder and Place paths: segments joined by '.', "*" = follow a reference, "[]" = non-path index, digits = constant index.
static class Path
{
    public static string[] Segs(string p) => p.Split('.');
    static bool IsIdx(string s) => s == "[]" || s.All(char.IsDigit);
    static bool SegEq(string a, string b) => a == b || (a == "[]" && IsIdx(b)) || (b == "[]" && IsIdx(a));
    public static bool Within(string k, string h) { var a = Segs(k); var b = Segs(h); return a.Length >= b.Length && b.Select((s, i) => SegEq(a[i], s)).All(x => x); }
    public static bool Overlap(string a, string b) => Within(a, b) || Within(b, a);
    public static string Rest(string k, string h) => string.Join('.', Segs(k).Skip(Segs(h).Length));
    public static bool DerefBelow(string k, string h) => Segs(k).Skip(Segs(h).Length).Contains("*");
    // Access a against Loan place l. Inline parts exclude referents (§15.6.2).
    public static bool Hits(string a, string l) => Within(a, l) || (Within(l, a) && !DerefBelow(l, a));
    public static bool RunsThrough(string l, string h) => Within(l, h) && DerefBelow(l, h);
    public static string Join(string t, string rest) => rest.Length == 0 ? t : t + "." + rest;
    public static string Cap(string p) => string.Join('.', Segs(p).Take(8)); // PROV widening: a prefix overlaps more
    public static string Before(string p, string seg) { var s = Segs(p); int i = Array.IndexOf(s, seg); return i < 0 ? p : string.Join('.', s.Take(i)); }
    public static bool Escaped(string h) => h.StartsWith('^') || Within(h, "$ret");
}

class Node
{
    public readonly string Label; public readonly int Ix;
    public readonly List<Issue> Issues = new(); public readonly List<(string From, string To)> Flows = new();
    public readonly List<string> Defs = new(), Uses = new(), Activates = new(), Assumed = new(), Exchanges = new(), Disturbs = new(), Excuses = new();
    // F6 (S2, A3 §5): Φ_c, a phantom uniq Loan on the receiver's referent carried by the result; a disturbing point
    // turns Φ_c into Φ_c^dist in every set; a later preserves call on the same receiver excuses an undisturbed Φ_c.
    // recv: the receiver's reservation Loan R. Round 9 (G113): the result keeps the receiver's value with R replaced
    // by Φ_c, so Φ_c's values carry its parent Loans, as a child's do.
    public Node Phantom(string id, string place, string into, string recv) { Issues.Add(new(id, place, true, into, false, false, false, true, recv)); return this; }
    public Node Disturb(string id) { Disturbs.Add(id); return this; }
    public Node Excuse(string id) { Excuses.Add(id); return this; }
    public readonly List<(string Place, string Src)> Throughs = new(); public readonly List<Acc> Accs = new();
    public List<string> Succ;
    public Node(string label, int ix) { Label = label; Ix = ix; }
    public Node Use(params string[] h) { Uses.AddRange(h.Select(p => Path.Before(p, "*"))); return this; }
    public Node Def(params string[] h) { Defs.AddRange(h); return this; }                        // replaced(H, P)
    public Node Flow(string from, string to) { Flows.Add((from, to)); return Use(from); }          // flow(H1, H2, P)
    public Node Borrow(string id, string place, bool uniq, string into, bool reserve = false, bool inPlace = false) // issued(L, place, mode, P, H)
    { Issues.Add(new(id, place, uniq, into, reserve, false, inPlace)); return place.Contains('*') ? Use(place) : this; }
    public Node Param(string id, string h) { Issues.Add(new(id, "^" + h, true, h, false, true)); return Def(h); } // escaped referent
    public Node A(K kind, string place) { Accs.Add(new(kind, place, null)); return place.Contains('*') ? Use(place) : this; }
    public Node Assign(string target) { Def(target); return A(K.Write, target); }               // whole write of a move path
    public Node Through(string place, string src) { Throughs.Add((place, src)); A(K.Write, place); return Use(src); } // written_through
    public Node Activate(string id) { Activates.Add(id); return this; }                          // reserved(L, _, P) ends here
    public Node Exchange(string h) { Exchanges.Add(h); return this; } // exchange/swap target: wholly replaced, old value moved out
    public Node Drop(string h) => A(K.Destroy, h);                    // destroying h's old value runs a user drop that may reach through it
    public Node Goto(params string[] labels) { Succ = labels.ToList(); return this; }
    public Node Assume(string label) { Assumed.Add(label); return this; }                        // assumed edge (§14.10.3)
}

class Case
{
    public readonly string Id, Title; public readonly List<Node> Nodes = new();
    public bool Err; public string[] At = []; public bool? S1Err; public string[] S1At; public bool S1POnly; public Func<Analysis, string> Extra;
    public string ALoss; // the listed design-A loss that explains an extra rejection (round 3)
    public Case(string id, string title) { Id = id; Title = title; }
    public Node N(string label) { var n = new Node(label, Nodes.Count); Nodes.Add(n); return n; }
    public Case Error(params string[] at) { Err = true; At = at; return this; }
    public Case Valid() { Err = false; return this; }
    public Case S1(bool err, params string[] at) { S1Err = err; S1At = at; return this; }
}

class Analysis
{
    readonly Mode M; readonly Abl X; readonly List<Node> ns; readonly int N;
    readonly Dictionary<string, Issue> issue = new(); readonly Dictionary<string, int> issueAt = new(), activeAt = new();
    readonly Dictionary<string, HashSet<string>> places = new();
    HashSet<Fact>[] In; HashSet<string>[] liveIn; List<int>[] succ;
    public readonly List<(string L, int P, string What)> Errors = new();

    public Analysis(Case c, Mode m, Abl x = Abl.None)
    {
        M = m; X = x; ns = c.Nodes; N = ns.Count;
        foreach (var n in ns)
        {
            foreach (var s0 in n.Issues)
            {
                var s = x == Abl.NoReservation ? s0 with { Reserve = false } : s0;
                issue[s.Id] = s; issueAt[s.Id] = n.Ix; places[s.Id] = new(); if (s.Param || m != Mode.PROV) places[s.Id].Add(s.Place);
            }
            if (x != Abl.NoReservation) foreach (var a in n.Activates) activeAt[a] = n.Ix;
        }
        foreach (var d in ns.SelectMany(n => n.Disturbs).Distinct()) // Φ_c^dist: the same phantom, no longer excusable
        { issue[d + "^dist"] = issue[d] with { Id = d + "^dist" }; issueAt[d + "^dist"] = issueAt[d]; places[d + "^dist"] = new(places[d]); }
        BuildEdges(); Carries();
        if (m == Mode.A) { LivenessA(); CheckA(); } else { Liveness(); Check(); }
    }

    int IxOf(string l) { int i = ns.FindIndex(n => n.Label == l); return i >= 0 ? i : throw new Exception("no label " + l); }

    // edge(P, Q): runtime edges, plus assumed edges into nodes no runtime path reaches (§14.10.3, DEC R4-M20).
    void BuildEdges()
    {
        succ = ns.Select(n => n.Succ?.Select(IxOf).ToList() ?? (n.Ix + 1 < N ? [n.Ix + 1] : new List<int>())).ToArray();
        var reach = new bool[N]; var st = new Stack<int>(); st.Push(0);
        while (st.Count > 0) { var i = st.Pop(); if (reach[i]) continue; reach[i] = true; foreach (var q in succ[i]) st.Push(q); }
        foreach (var n in ns) foreach (var l in n.Assumed) if (!reach[IxOf(l)]) succ[n.Ix].Add(IxOf(l));
    }

    // carries(H, L, P) on entry to P; naive forward fixpoint, union at joins (may-facts).
    void Carries()
    {
        In = Enumerable.Range(0, N).Select(_ => new HashSet<Fact>()).ToArray();
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int i = 0; i < N; i++) { var o = M == Mode.A ? TransferA(i) : Transfer(i); foreach (var q in succ[i]) foreach (var t in o) changed |= In[q].Add(t); }
            if (M == Mode.PROV) // provenance: a Loan's place is the set of targets its borrow path resolves to
                foreach (var (id, at) in issueAt) if (!issue[id].Param)
                    foreach (var t in Resolve(issue[id].Place, at).Leaves) changed |= places[id].Add(Path.Cap(t));
        }
    }

    HashSet<Fact> Transfer(int i)
    {
        var n = ns[i]; var o = new HashSet<Fact>();
        bool KillH(string h) => n.Defs.Concat(n.Exchanges).Any(r => Path.Within(h, r));                      // killed(K, P)
        // destroyed_in_place(K, P): =, replace, end of storage (Defs). S1 as written also counts exchange/swap targets.
        IEnumerable<string> dip = M == Mode.S1 || X == Abl.ExchangeKills ? n.Defs.Concat(n.Exchanges) : M == Mode.S1P ? n.Defs : [];
        Func<string, bool> Ended = l => EndedBy(l, dip);                                                     // ended(L, P)
        foreach (var t in In[i]) if (!KillH(t.H) && !Ended(t.L)) o.Add(t);                                    // keep
        if (X == Abl.KillKeepOnly) Ended = _ => false;      // only `keep` sees `ended`
        var inn = In[i].Where(t => !Ended(t.L)).ToList();   // an ended Loan propagates through no rule at P
        foreach (var (from, to) in n.Flows)                                                                  // part-preserving flow
            foreach (var s in !from.Contains('*') ? [from] : X == Abl.FlatFlow ? [Path.Before(from, "*")] : Resolve(from, i).Leaves)
                foreach (var t in inn)
                    if (Path.Within(t.H, s)) o.Add((Path.Join(to, Path.Rest(t.H, s)), t.L));
                    else if (Path.Within(s, t.H)) o.Add((to, t.L));                                          // ancestor's whole-value Loans
        foreach (var (place, src) in n.Throughs)                                                             // written_through + reaches
        {
            var reach = Resolve(place, i).Leaves.Select(p => Path.Before(p, "[]")).ToList();
            foreach (var t in inn) if (Path.Overlap(t.H, src)) foreach (var r in reach) o.Add((r, t.L));
        }
        foreach (var s in n.Issues)                                                                          // issue + child+ materialization
        {
            o.Add((s.Into, s.Id));
            if (!s.Param && !s.Phantom && X != Abl.NoMaterialize) foreach (var l in Resolve(s.Place, i).Auth) if (!Ended(l)) o.Add((s.Into, l));
            if (s.Phantom) // round 9 (G113): the receiver's value, R replaced by Φ_c
                foreach (var t in inn) if (t.L != s.Recv && Path.Overlap(t.H, issue[s.Recv].Into)) o.Add((s.Into, t.L));
        }
        return Disturbed(n, o);
    }

    // base(L, B) (plan fe1a5b34): the Place storing the reference the borrow follows last, i.e. the prefix before the
    // last Deref step (null for a plain Loan). r.* → r; rr.*.* → rr.*; cursor.*.children.[] → cursor.
    string Base(string l)
    {
        var s = Path.Segs(issue[l].Place); int j = Array.LastIndexOf(s, "*");
        return j < 0 ? null : string.Join('.', s.Take(j));
    }

    // ended(L, P) :- base(L, B), destroyed_in_place(K, P), within(B, K), on move paths only: no Deref step between B and
    // K. KillAcrossDeref restores round 2's rule (the first move path entered may be destroyed).
    bool EndedBy(string l, IEnumerable<string> dip)
    {
        var b = Base(l);
        if (b == null) return false;
        // Both modes use the plan's kill (round 6 correction): B is a move path, K is B or an ancestor with no Deref
        // between, and Index steps only for identical constant indexes. KillAcrossDeref is the root-most kill with the
        // may-overlap Within, which lets a constant index kill a dynamic one (unsound, 34a/34b). FirstDerefBase takes the
        // same B with the definite matcher: K is a move path and an exact prefix of B, whose later steps may be any index
        // (destroying arr destroys arr[i], 34c).
        if (X is Abl.KillAcrossDeref or Abl.P3RootMost) { b = Path.Before(b, "*"); return dip.Any(k => Path.Within(b, k)); }
        if (X == Abl.FirstDerefBase)
        {
            var fs = Path.Segs(Path.Before(b, "*"));
            return dip.Select(Path.Segs).Any(ks => !ks.Contains("[]") && fs.Length >= ks.Length && fs.Take(ks.Length).SequenceEqual(ks));
        }
        if (b.Contains('*')) return false;
        var bs = Path.Segs(b);
        return dip.Select(Path.Segs).Any(ks => bs.Length >= ks.Length && bs.Take(ks.Length).SequenceEqual(ks) && !bs.Skip(ks.Length).Contains("[]"));
    }

    // Round 6 (review r2, finding 3): an observing destruction of K reaches exclusively only down to the first shared
    // Deref. The Deref below K on Loan place p is shared when every Loan its holder carries is a shared Loan.
    bool SharedCut(string k, string p, int i)
    {
        if (X == Abl.NoSharedCut) return false;
        var s = Path.Segs(p); int from = Path.Segs(k).Length;
        int j = from <= s.Length ? Array.IndexOf(s, "*", from) : -1;
        if (j < 0) return false;
        var prefix = string.Join('.', s.Take(j));
        var holders = M == Mode.A ? [Root(prefix)] : Resolve(prefix, i).Leaves;
        var f = In[i].Where(t => holders.Any(h => M == Mode.A ? t.H == h : Path.Overlap(t.H, h))).ToList();
        return f.Count > 0 && f.All(t => !issue[t.L].Uniq && !issue[t.L].Param);
    }

    // Provenance resolution of a Place through the references it follows, at P. Names: every name of the Place
    // (base-relative and resolved); Leaves: targets with no further Loan; Auth: Loans of the references followed.
    Res Resolve(string path, int i, int depth = 0)
    {
        var r = new Res(new() { path }, new(), new());
        var segs = Path.Segs(path); int j = Array.IndexOf(segs, "*");
        var loans = j < 0 ? new List<string>() :
            In[i].Where(t => Path.Overlap(t.H, string.Join('.', segs.Take(j)))).Select(t => t.L).Distinct().ToList();
        if (loans.Count == 0 || depth > 10) { r.Leaves.Add(path); return r; }
        var rest = string.Join('.', segs.Skip(j + 1));
        foreach (var l in loans)
        {
            r.Auth.Add(l);
            foreach (var t in places[l].ToList())
            { var s = Resolve(Path.Join(t, rest), i, depth + 1); r.Names.UnionWith(s.Names); r.Leaves.UnionWith(s.Leaves); r.Auth.UnionWith(s.Auth); }
        }
        return r;
    }

    IEnumerable<string> Names(string l, int i) => M == Mode.PROV ? places[l] : places[l].SelectMany(p => Resolve(p, i).Leaves);

    // live(H, P) on entry to P: backward from uses, closed through live references: live(T) :- live(R), carries(R, L), names(L, T).
    void Liveness()
    {
        liveIn = Enumerable.Range(0, N).Select(_ => new HashSet<string>()).ToArray();
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int i = N - 1; i >= 0; i--)
            {
                var n = ns[i];
                var s = succ[i].SelectMany(q => liveIn[q]).Where(h => !n.Defs.Concat(n.Exchanges).Any(r => Path.Within(h, r))).ToHashSet();
                s.UnionWith(n.Uses);
                if (X != Abl.NoClosureFeedback) Close(s, i);
                foreach (var x in s) changed |= liveIn[i].Add(x);
            }
        }
        if (X == Abl.NoClosureFeedback) for (int i = 0; i < N; i++) Close(liveIn[i], i); // closure per point only
    }

    void Close(HashSet<string> s, int i)
    {
        var work = new Queue<string>(s);
        while (work.Count > 0)
        {
            var r = work.Dequeue();
            foreach (var t in In[i]) if (Path.Overlap(t.H, r)) foreach (var x in Names(t.L, i)) if (s.Add(x)) work.Enqueue(x);
        }
    }

    // error(L, P) :- live_loan(L, P), access(P, place, kind), overlap, conflicts(kind, mode(L, P)).
    void Check()
    {
        for (int i = 0; i < N; i++)
        {
            var n = ns[i]; var accs = n.Accs.ToList();
            accs.AddRange(n.Issues.Where(s => !s.Param && !s.Phantom).Select(s => new Acc(s.Uniq && !s.Reserve ? K.UBorrow : K.SBorrow, s.Place, null, s.InPlace)));
            accs.AddRange(n.Activates.Select(id => new Acc(K.UBorrow, issue[id].Place, id, issue[id].InPlace)));
            if (M == Mode.S1P) accs.AddRange(n.Exchanges.Select(h => new Acc(K.Move, h, null))); // exchanged/swapped: the old value moves out
            var after = succ[i].SelectMany(q => liveIn[q]).ToHashSet();
            bool LiveIn(string h) => Path.Escaped(h) || liveIn[i].Any(x => Path.Overlap(h, x));
            bool LiveAfter(string h) => Path.Escaped(h) || after.Any(x => Path.Overlap(h, x));
            bool Dest(string h) => n.Defs.Concat(n.Exchanges).Any(d => Path.Within(h, d));
            // Round 5: a value P stores (a flow into a holder live after P, or a write through a reference) is in transit
            // during P, so its source's entry facts count even if the source dies at P (24c).
            var transit = X == Abl.NoTransit ? [] : n.Flows.Where(f => LiveAfter(f.To))
                .SelectMany(f => f.From.Contains('*') ? Resolve(f.From, i).Leaves : [f.From])
                .Concat(n.Throughs.Where(t => Resolve(t.Place, i).Leaves.Any(r => LiveAfter(Path.Before(r, "[]")))).Select(t => t.Src)).ToList();
            foreach (var a in accs)
            {
                var res = X == Abl.SyntacticAccess ? new Res([a.Place], [a.Place], []) : Resolve(a.Place, i);
                if (X == Abl.NoAuthority) res.Auth.Clear();
                // base_op(P, K): a Move (S1, S1′), or an exclusive borrow other than as a `replace` target (S1′).
                // (H) plan: destroying an old value whose drop may reach the base is an exclusive use (K.Destroy, from Drop).
                bool baseOp = M != Mode.PROV && (a.Kind == K.Move ? X != Abl.NoMoveBaseOp
                    : a.Kind == K.Destroy ? M == Mode.S1P
                    : a.Kind == K.UBorrow && M == Mode.S1P && X is not (Abl.NoUniqBaseOp or Abl.S1ResolvePlaces) && (!a.InPlace || X == Abl.NoReplaceException));
                // Round 4, by holder: entry facts over holders live after P minus the destination's old value; an activation
                // over holders live into P (the call protects its arguments) minus the reservation's own holder.
                var facts = X == Abl.IdExclusion ? In[i].Where(t => LiveIn(t.H))
                    : a.Except != null ? In[i].Where(t => LiveIn(t.H) && !Path.Within(t.H, issue[a.Except].Into))
                    : In[i].Where(t => LiveAfter(t.H) && !Dest(t.H) || transit.Any(s => Path.Overlap(t.H, s)));
                foreach (var (h, l) in facts)
                {
                    if ((X == Abl.IdExclusion && l == a.Except) || res.Auth.Contains(l) || issue[l].Param || n.Excuses.Contains(l)) continue;
                    bool uniq = issue[l].Uniq && !Reserved(h, l, i);
                    // SharedBaseOp: P3 for S1′, a shared borrow of the base or an ancestor while an exclusive child is live.
                    bool op = baseOp || ((X is Abl.SharedBaseOp or Abl.P3RootMost) && M == Mode.S1P && a.Kind == K.SBorrow && !a.InPlace && uniq);
                    // error(L, P) :- live_loan(L, P), base(L, B), base_op(P, K), runs_through(B, K): K is B or a prefix of B
                    // across Deref steps, syntactically; an Index step matches unless both are distinct constants.
                    if (op && Base(l) is { } bb && res.Names.Any(k => Path.Within(bb, k) && (X != Abl.BaseOpMovePathsOnly || !k.Contains('*'))
                            && !(a.Kind == K.Destroy && SharedCut(k, issue[l].Place, i))))
                    { Err(l, i, $"base_op {a.Kind} {a.Place}"); continue; }
                    if (!uniq && a.Kind is K.Read or K.SBorrow) continue;
                    var lp = X == Abl.S1ResolvePlaces ? places[l].SelectMany(p => Resolve(p, i).Names).ToHashSet() : places[l];
                    if (res.Names.Any(x => lp.Any(p => Path.Hits(x, p)))) Err(l, i, $"{a.Kind} {a.Place}");
                }
            }
        }
    }

    // ---- Design A (design-A.md §3, §5): root holders + E (P1), deep shared borrows (P3), syntactic access Places,
    // layer-blind following (P10), content = holders named by direct Loans (+ E), check-time liveness closure.
    static string Root(string h) => h.StartsWith('^') ? "E" : Path.Segs(h)[0];
    static int Steps(string p) => Path.Segs(p).Count(s => s == "*");
    bool Direct(string l) => !issue[l].Param && !issue[l].Place.Contains('*');
    static HashSet<string> Sigma(IEnumerable<Fact> s, string h) => s.Where(t => t.H == h).Select(t => t.L).ToHashSet();
    HashSet<string> Refs(HashSet<string> S)
    {
        var r = S.Where(Direct).Select(l => Root(issue[l].Place)).ToHashSet();
        if (S.Any(l => issue[l].Param) || !S.Any(Direct)) r.Add("E"); // Φ_E, or no direct Loan
        return r;
    }
    HashSet<string> Content(IEnumerable<Fact> s, HashSet<string> S) => Refs(S).SelectMany(t => Sigma(s, t)).ToHashSet();

    HashSet<Fact> TransferA(int i)
    {
        var n = ns[i]; var o = new HashSet<Fact>();
        var inn = In[i].Where(t => !EndedBy(t.L, n.Defs)).ToList(); // ended (plan fe1a5b34), on every carrying rule
        var whole = n.Defs.Concat(n.Exchanges).Concat(n.Accs.Where(a => a.Kind == K.Move).Select(a => a.Place)).Where(h => !h.Contains('.')).ToHashSet();
        foreach (var t in inn) if (!whole.Contains(t.H)) o.Add(t);   // P1: only a whole-root write, Move or storage end ends carrying
        HashSet<string> Val(string x) { var S = Sigma(inn, Root(x)); for (int k = Steps(x); k > 0; k--) S = Content(inn, S); return S; }
        foreach (var (from, to) in n.Flows) foreach (var l in Val(from)) o.Add((Root(to), l));
        foreach (var (place, src) in n.Throughs)                    // P10: every holder the carried direct Loans name
        {
            var S = Sigma(inn, Root(place)); for (int k = Steps(place); k > 1; k--) S = Content(inn, S);
            var targets = Steps(place) == 0 ? [Root(place)] : Refs(S);
            foreach (var l in Sigma(inn, Root(src))) foreach (var t in targets) o.Add((t, l));
        }
        foreach (var s in n.Issues)                                 // issue + parents of every followed layer
        {
            // Fix found in round 3: a child Loan whose base this point destroys in place is ended here too (19a).
            if (X != Abl.AIssueNotEnded && EndedBy(s.Id, n.Defs)) { } else o.Add((Root(s.Into), s.Id));
            // Round 9 (G113): the result keeps the receiver's value with its own Loan R replaced by Φ_c, so Φ_c's
            // values carry its parent Loans (the receiver Ref's R2 parents), as a child's do.
            if (s.Phantom && X is not (Abl.PhantomAlone or Abl.PhantomParentsAtCall))
                foreach (var l in Sigma(inn, Root(issue[s.Recv].Into))) if (l != s.Recv || X == Abl.PhantomKeepsReceiver) o.Add((Root(s.Into), l));
            if (s.Param || (s.Phantom && X != Abl.PhantomParentsAtCall)) continue;
            var S = Sigma(inn, Root(s.Place));
            for (int j = 0; j < Steps(s.Place); j++) { foreach (var l in S) o.Add((Root(s.Into), l)); S = Content(inn, S); }
        }
        return Disturbed(n, o);
    }

    void LivenessA() // backward over root holders; borrowing T uses T; no feedback from the closure
    {
        liveIn = Enumerable.Range(0, N).Select(_ => new HashSet<string>()).ToArray();
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int i = N - 1; i >= 0; i--)
            {
                var n = ns[i];
                var kill = n.Defs.Concat(n.Accs.Where(a => a.Kind == K.Move).Select(a => a.Place)).Where(h => !h.Contains('.')).ToHashSet();
                var s = succ[i].SelectMany(q => liveIn[q]).Where(h => !kill.Contains(h)).ToHashSet();
                s.UnionWith(n.Uses.Select(Root));
                s.UnionWith(n.Issues.Where(x => !x.Param).Select(x => Root(x.Place)));
                s.UnionWith(n.Accs.Where(a => a.Kind != K.Write).Select(a => Root(a.Place)));
                foreach (var x in s) changed |= liveIn[i].Add(x);
            }
        }
    }

    void CheckA()
    {
        for (int i = 0; i < N; i++)
        {
            var n = ns[i]; var accs = n.Accs.ToList();
            // A2 keeps the `replace` exemption: a destroying Ref is a shallow read, and its activation is not deep.
            accs.AddRange(n.Issues.Where(s => !s.Param && !s.Phantom).Select(s => new Acc(s.InPlace ? K.Read : s.Uniq && !s.Reserve ? K.UBorrow : K.SBorrow, s.Place, null, s.InPlace)));
            accs.AddRange(n.Activates.Select(id => new Acc(K.UBorrow, issue[id].Place, id, issue[id].InPlace)));
            accs.AddRange(n.Exchanges.Select(h => new Acc(K.Move, h, null)));
            var after = succ[i].SelectMany(q => liveIn[q]).ToHashSet();
            var dest = n.Defs.Concat(n.Exchanges).Where(h => !h.Contains('.')).ToHashSet();
            var outSet = X == Abl.AText ? TransferA(i) : null;
            static bool Esc(string h) => h is "E" or "$ret";
            HashSet<string> SrcHolders(string x) // the holders a flow's value is read from (k reference layers)
            {
                if (Steps(x) == 0) return [Root(x)];
                var S = Sigma(In[i], Root(x)); for (int k = Steps(x); k > 1; k--) S = Content(In[i], S);
                return Refs(S);
            }
            HashSet<string> Targets(string place) // the holders a write through `place` reaches (P10)
            {
                if (Steps(place) == 0) return [Root(place)];
                var S = Sigma(In[i], Root(place)); for (int k = Steps(place); k > 1; k--) S = Content(In[i], S);
                return Refs(S);
            }
            // Transit clause (owner decision): σ_in of every value ℓ stores into a holder live after ℓ, into E, or through
            // a reference with a live destination.
            var transit = X == Abl.NoTransit ? [] : n.Flows.Where(f => after.Contains(Root(f.To)) || Esc(Root(f.To)))
                .SelectMany(f => SrcHolders(f.From))
                .Concat(n.Throughs.Where(t => Targets(t.Place).Any(h => after.Contains(h) || Esc(h))).Select(t => Root(t.Src))).ToHashSet();
            foreach (var a in accs)
            {
                // LS facts (see Check). IdExclusion: the old by-id rule. AText: design A's letter (σ after ℓ over live_out,
                // "L not issued at ℓ", activation L ≠ R), both by Loan id.
                HashSet<Fact> F;
                if (X == Abl.IdExclusion) F = In[i].Where(t => Esc(t.H) || liveIn[i].Contains(t.H)).ToHashSet();
                else if (a.Except != null) F = In[i].Where(t => Esc(t.H) || liveIn[i].Contains(t.H) && (X == Abl.AText || t.H != issue[a.Except].Into)).ToHashSet();
                else if (X == Abl.AText) F = outSet.Where(t => Esc(t.H) || after.Contains(t.H)).ToHashSet();
                else F = In[i].Where(t => Esc(t.H) || after.Contains(t.H) && !dest.Contains(t.H) || transit.Contains(t.H)).ToHashSet();
                IEnumerable<Fact> basis = X == Abl.AText && a.Except == null ? outSet : In[i];
                for (var work = new Queue<Fact>(F); work.Count > 0;) // a live direct Loan naming T makes σ(T) live
                {
                    var l0 = work.Dequeue().L; var T = issue[l0].Param ? "E" : Direct(l0) ? Root(issue[l0].Place) : null;
                    if (T != null) foreach (var t in basis.Where(t => t.H == T)) if (F.Add(t)) work.Enqueue(t);
                }
                bool deep = (a.Kind is K.Move or K.SBorrow or K.UBorrow or K.Destroy) && !a.InPlace; // S1′ + P3; an observing Drop is deep; not a destroying Ref
                bool byId = X is Abl.IdExclusion or Abl.AText;
                foreach (var (h, l) in F)
                {
                    if ((byId && l == a.Except) || issue[l].Param || n.Excuses.Contains(l)) continue;
                    if (X == Abl.AText && a.Except == null && n.Issues.Any(s => s.Id == l)) continue; // "L not issued at ℓ"
                    bool uniq = issue[l].Uniq && !Reserved(h, l, i);
                    if (!uniq && a.Kind is K.Read or K.SBorrow) continue;
                    var p = issue[l].Place; // syntactic: no resolution, no authority
                    bool reach = deep && !(a.Kind == K.Destroy && SharedCut(a.Place, p, i));
                    if (Path.Within(a.Place, p) || (Path.Within(p, a.Place) && (reach || !Path.DerefBelow(p, a.Place)))) Err(l, i, $"{a.Kind} {a.Place}");
                }
            }
        }
    }

    void Err(string l, int i, string what) { if (!Errors.Contains((l, i, what))) Errors.Add((l, i, what)); }

    // disturbed at ℓ ⇒ Φ_c is replaced by Φ_c^dist in every σ (by holder, like ended).
    static HashSet<Fact> Disturbed(Node n, HashSet<Fact> o) =>
        n.Disturbs.Count == 0 ? o : o.Select(t => n.Disturbs.Contains(t.L) ? (t.H, t.L + "^dist") : t).ToHashSet();

    // A reserved Loan acts as shared until activation. Round 4: only in its own reservation holder, inside the call's
    // window; any other holder carries an earlier, activated incarnation of the site (loops). By id: by position only.
    bool Reserved(string h, string l, int i) => activeAt.TryGetValue(l, out var at) &&
        (X is Abl.IdExclusion or Abl.AText ? i <= at : h == issue[l].Into && issueAt[l] < i && i <= at);

    public IEnumerable<string> ErrorNodes() => Errors.Select(e => ns[e.P].Label).Distinct();
    public bool CarriesAt(string label, string h, string l) => In[IxOf(label)].Contains((h, l));
    public string Dump() => string.Join("\n", ns.Select(n => $"{n.Label,-44} carries {{{string.Join(" ", In[n.Ix].OrderBy(t => t.H).Select(t => t.H + ":" + t.L))}}} live {{{string.Join(" ", liveIn[n.Ix].Order())}}}"))
        + "\nplaces: " + string.Join("; ", places.Select(p => p.Key + " = {" + string.Join(", ", p.Value) + "}"));
}

// Hand-encoded facts. Each node is one program point; labels are the source lines; `$t` names a temporary.
static class Cases
{
    const bool Ref = false, Uniq = true;

    public static List<Case> All() => [
        Replacement(), Join(), Slot("3", copy: true, early: false), SlotReplaced(), Slot("4", copy: false, early: false), Slot("4b", copy: false, early: true),
        Index("5", nonPath: true, rewrite: false), Index("5b", nonPath: false, rewrite: true), Index("5c", nonPath: true, rewrite: true),
        Reborrow("6", write: true), Reborrow("6b", write: false), Partial("7", inspectP: false), Partial("7b", inspectP: true),
        Deepest("8a", clear: true, check: false), Deepest("8b", clear: false, check: false), Deepest("8c", clear: false, check: true),
        CopyChild(), StoreChild(), TwoBases(), Loop("9d", "x = 3"), Loop("9d'", "y = 3"), FieldBase("9e", partial: false), FieldBase("9e'", partial: true),
        ThroughCall(), Exchange(), ExchangeThenReplace(), ExchangeReplaces(), SwapBase(), StealThroughW(), SharedBase(), Nested(), MoveBase(), UniqBase(),
        Replace(), AppendRemove(), Insert(), Swap(), Unreachable(), UnreachableJoin(),
        MoveBan("12a", rewrite: true), MoveBan("12b", rewrite: false),
        RefBase("13a", shared: false, "outer"), RefBase("13b", shared: false, "inner"), RefBase("13c", shared: false, "through"),
        RefBase("13d", shared: true, "outer"), RefBase("13e", shared: true, "inner"),
        MoveAncestor(), ReplaceBase("15a", write: false), ReplaceBase("15b", write: true),
        Alias("16a", "reborrow"), Alias("16b", "copy"), Alias("16c", "aggregate"), Alias("16d", "stored"), Alias("16e", "inner"),
        DeadHolder("17a", chain: true), DeadHolder("17b", chain: false), PartialMove("18a", restore: true), PartialMove("18b", restore: false),
        InPlace("19a", parent: false), InPlace("19b", parent: true),
        LoopSite("20a", "uniq"), LoopSite("20b", "reserved"), LoopSite("20c", "refkept"), LoopSite("20d", "uniqkept"),
        LoopSite("20e", "child"), LoopSite("20f", "window"),
        TempStep("21a", clear: false), TempStep("21b", clear: true), IndexBan("22a", constant: false), IndexBan("22b", constant: true),
        OuterThenWrite(), ParamOuter(), DropBase("24a", userDrop: true), DropBase("24b", userDrop: false), DropMerged(),
        SelfStore("25a", "live"), SelfStore("25b", "dead"), SelfStore("25c", "enum"), DestroyingCall("26a", clear: false), DestroyingCall("26b", clear: true),
        SharedField(), Args("28a", "uniqref"), Args("28b", "refref"), Args("28c", "fields"), Args("28d", "stored"),
        CrossingKill(), EscapedTransit(), PartTransit(), LinkedList("30a", "obj"), LinkedList("30b", "uniq"), LinkedList("30c", "conflict"), LinkedList("30d", "peek"),
        SwapParams("31a", caller: false), SwapParams("31b", caller: true), CopyThrough("32a", childLive: false), CopyThrough("32b", childLive: true),
        PreservesResults("33a", "two"), PreservesResults("33b", "across"), PreservesResults("33c", "callonly"),
        IndexKill("34a", deref2: false), IndexKill("34b", deref2: true), IndexKill("34c", deref2: false, whole: true),
        Walk("35a", loop: false), Walk("35b", loop: true),
        PhantomParents("36a", direct: false, retain: false), PhantomParents("36b", direct: false, retain: true),
        PhantomParents("36c", direct: true, retain: false), PhantomParents("36d", direct: true, retain: true), SplitOuter(),
        PhantomRoot("36e"), PhantomRoot("36f"), PhantomRoot("36g"), PhantomRoot("36h")];

    static Case Replacement()
    {
        var c = new Case("1", "§15.6 example: `r = b@ref` ends the Loan on a");
        c.N("var a = 1; var b = 2").Def("a", "b");
        c.N("var r = a@ref").Def("r").Borrow("L1", "a", Ref, "r");
        c.N("r = b@ref").Assign("r").Borrow("L2", "b", Ref, "r");
        c.N("a = 3").Assign("a");
        c.N("inspect(r)").Use("r");
        return c.Valid();
    }

    static Case Join()
    {
        var c = new Case("2", "§14.9.2 example: a joined result carries both Loans");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("if ready").Goto("  => x@ref", "  => y@ref");
        c.N("  => x@ref").Def("r").Borrow("Lx", "x", Ref, "r").Goto("x = 3");
        c.N("  => y@ref").Def("r").Borrow("Ly", "y", Ref, "r");
        c.N("x = 3").Assign("x");
        c.N("use(r)").Use("r");
        return c.Error("x = 3");
    }

    // 3: copy of the referent's reference; 4: read through two layers; 4b: the Move precedes the borrow of the slot.
    static Case Slot(string id, bool copy, bool early)
    {
        var c = new Case(id, copy ? "`let copy = r@follow` takes slot's Loan, not r's" : early ? "4 with `consume` before `let r = slot@ref`" : "`inspect(r@follow@follow)`: liveness through references");
        c.N("var data = makeData()").Def("data");
        c.N("var slot: ref/Data = data@ref").Def("slot").Borrow("Ld", "data", Ref, "slot");
        if (early) c.N("consume(data@move)").A(K.Move, "data");
        c.N("let r = slot@ref").Def("r").Borrow("Ls", "slot", Ref, "r");
        if (copy) c.N("let copy = r@follow").Def("copy").A(K.Read, "r.*").Flow("r.*", "copy");
        if (!early) c.N("consume(data@move)").A(K.Move, "data");
        if (copy) c.N("inspect(copy)").Use("copy"); else c.N("inspect(r@follow@follow)").A(K.Read, "r.*.*");
        return c.Error("consume(data@move)");
    }

    // §15.7.3: "a Copy of a stored ref/Data keeps only its external Loan", so replacing slot is valid.
    static Case SlotReplaced()
    {
        var c = new Case("3b", "`let copy = r@follow; slot = other@ref; inspect(copy)`");
        c.N("var data = makeData(); var other = makeData()").Def("data", "other");
        c.N("var slot: ref/Data = data@ref").Def("slot").Borrow("Ld", "data", Ref, "slot");
        c.N("let r = slot@ref").Def("r").Borrow("Ls", "slot", Ref, "r");
        c.N("let copy = r@follow").Def("copy").A(K.Read, "r.*").Flow("r.*", "copy");
        c.N("slot = other@ref").Assign("slot").Borrow("Lo", "other", Ref, "slot");
        c.N("inspect(copy)").Use("copy");
        return c.Valid();
    }

    static Case Index(string id, bool nonPath, bool rewrite)
    {
        var target = nonPath ? "refs.[]" : "refs.0"; var spelled = nonPath ? "refs[n]" : "refs[0]";
        var c = new Case(id, $"`{spelled} = a@ref`" + (rewrite ? $"; `{spelled} = z@ref`" : "") + "; `a = 3`; `inspect(refs)`");
        c.N("var z = 0; var a = 1; let n: isize = pick()").Def("z", "a", "n");
        c.N("var refs: [2 of ref/i32] = [z@ref, z@ref]").Def("refs").Borrow("Lz0", "z", Ref, "refs.0").Borrow("Lz1", "z", Ref, "refs.1");
        foreach (var (src, loan) in rewrite ? new[] { ("a", "La"), ("z", "Lz2") } : [("a", "La")])
            if (nonPath) { c.N($"$t = {src}@ref ({loan})").Def("$t").Borrow(loan, src, Ref, "$t"); c.N($"{spelled} = $t ({loan})").Through(target, "$t"); }
            else c.N($"{spelled} = {src}@ref").Assign(target).Borrow(loan, src, Ref, target);
        c.N("a = 3").Assign("a");
        c.N("inspect(refs)").Use("refs");
        c.ALoss = "P1 (listed in A §2: prototype 5b)"; return nonPath ? c.Error("a = 3") : c.Valid();
    }

    static Case Reborrow(string id, bool write)
    {
        var c = new Case(id, "reborrow `c = r@follow@uniq`; `r = y@uniq`" + (write ? "; `x = 3`" : "") + "; `inspect(c)`");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r");
        if (write) c.N("x = 3").Assign("x");
        c.N("inspect(c)").Use("c");
        return write ? c.Error("x = 3") : c.Valid();
    }

    // struct Pair {left: ref/i32, right: ref/i32} with `Self is Copy` (§3.5.2).
    static Case Partial(string id, bool inspectP)
    {
        var c = new Case(id, $"`var q = p; q.left = c@ref; a = 3; inspect({(inspectP ? "p" : "q")})`");
        c.N("var a = 1; var b = 2; var c = 3").Def("a", "b", "c");
        c.N("var p = Pair{left: a@ref, right: b@ref}").Def("p").Borrow("La", "a", Ref, "p.left").Borrow("Lb", "b", Ref, "p.right");
        c.N("var q = p").Def("q").Flow("p", "q");
        c.N("q.left = c@ref").Assign("q.left").Borrow("Lc", "c", Ref, "q.left");
        c.N("a = 3").Assign("a");
        c.N(inspectP ? "inspect(p)" : "inspect(q)").Use(inspectP ? "p" : "q");
        c.ALoss = "P1 (listed in A §2: prototype 7)"; return inspectP ? c.Error("a = 3") : c.Valid();
    }

    // Plan §5.2. `cursor = next` is a bare acquisition of a uniq Place, so it Reborrows (§3.5).
    static Case Deepest(string id, bool clear, bool check)
    {
        var c = new Case(id, check ? "8b: the returned cursor still carries root's Loan L0" : clear ? "deepest with `cursor.children.clear()`" : "deepest as written");
        c.N("func deepest(root: uniq/Tree)").Param("Lroot", "root");
        c.N("var cursor = root").Def("cursor").Borrow("L0", "root.*", Uniq, "cursor");
        c.N("while cursor.children.length > 0").A(K.Read, "cursor.*.children").Goto("  let next = cursor.children[0]@uniq", "return cursor");
        c.N("  let next = cursor.children[0]@uniq").Def("next").Borrow("Ln", "cursor.*.children.[]", Uniq, "next");
        if (clear) c.N("  cursor.children.clear()").A(K.UBorrow, "cursor.*.children");
        c.N("  cursor = next").Assign("cursor").Borrow("L5", "next.*", Uniq, "cursor");
        c.N("  (end of body: StorageDead next)").Def("next").Goto("while cursor.children.length > 0");
        c.N("return cursor").Flow("cursor", "$ret");
        c.N("exit").Goto();
        if (check) c.Extra = a => a.CarriesAt("exit", "$ret", "L0") ? null : "$ret lacks L0";
        return clear ? c.Error("  cursor.children.clear()") : c.Valid();
    }

    // ---- 9: adversarial cases for S1's soundness hypothesis ----
    static Case CopyChild()
    {
        var c = new Case("9a", "copy a shared child, then replace the base");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@ref").Def("c").Borrow("L2", "r.*", Ref, "c");
        c.N("let d = c").Def("d").Flow("c", "d");
        c.N("r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r");
        c.N("x = 3").Assign("x");
        c.N("inspect(d)").Use("d");
        return c.Error("x = 3");
    }

    static Case StoreChild()
    {
        var c = new Case("9b", "store the child in an aggregate, then replace the base");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("var box = Box{item: c@move}").Def("box").A(K.Move, "c").Flow("c", "box.item");
        c.N("r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r");
        c.N("x = 3").Assign("x");
        c.N("inspect(box)").Use("box");
        return c.Error("x = 3");
    }

    static Case TwoBases()
    {
        var c = new Case("9c", "reborrow through r into s, through s into d; replace s, then r");
        c.N("var x = 1; var y = 2; var z = 4").Def("x", "y", "z");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("var s = r@follow@uniq").Def("s").Borrow("L2", "r.*", Uniq, "s");
        c.N("let d = s@follow@uniq").Def("d").Borrow("L3", "s.*", Uniq, "d");
        c.N("s = z@uniq").Assign("s").Borrow("Lz", "z", Uniq, "s");
        c.N("r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r");
        c.N("x = 3").Assign("x");
        c.N("inspect(d)").Use("d");
        return c.Error("x = 3");
    }

    static Case Loop(string id, string probe)
    {
        var c = new Case(id, $"replace the base inside a loop; `{probe}` after it");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("var c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("while cond").Goto("  inspect(c)", probe);
        c.N("  inspect(c)").Use("c");
        c.N("  r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r").Goto("while cond");
        c.N(probe).Assign(probe[..1]);
        c.N("inspect(c)").Use("c");
        return probe == "x = 3" ? c.Error(probe) : c.Valid();
    }

    // struct H {link: uniq/i32, n: i32}
    static Case FieldBase(string id, bool partial)
    {
        var c = new Case(id, partial ? "reborrow through `h.link`; `h.n = 5`; write through `h.link`" : "reborrow through `h.link`, then replace the ancestor `h`");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var h = H{link: x@uniq, n: 0}").Def("h").Borrow("L1", "x", Uniq, "h.link");
        c.N("let c = h.link@follow@uniq").Def("c").Borrow("L2", "h.link.*", Uniq, "c");
        if (partial) { c.N("h.n = 5").Assign("h.n"); c.N("h.link@follow = 7").A(K.Write, "h.link.*"); }
        else { c.N("h = H{link: y@uniq, n: 1}").Assign("h").Borrow("Ly", "y", Uniq, "h.link"); c.N("x = 3").Assign("x"); }
        c.N("inspect(c)").Use("c");
        return c.Error(partial ? "h.link@follow = 7" : "x = 3");
    }

    // func pass(v: uniq/i32) -> uniq/i32 during v: the result retains v (a call flow).
    static Case ThroughCall()
    {
        var c = new Case("9f", "pass the child through a retaining call, then replace the base");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("$a = c (Reborrow at a uniq parameter, reserved)").Def("$a").Borrow("L3", "c.*", Uniq, "$a", reserve: true);
        c.N("let d = pass($a)").Activate("L3").Def("d").Flow("$a", "d");
        c.N("r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r");
        c.N("x = 3").Assign("x");
        c.N("inspect(d)").Use("d");
        return c.Error("x = 3");
    }

    // exchange<T>(target: uniq/T ! with: T) -> T: the result is the old contents, the target receives `with`.
    static Case Exchange()
    {
        var c = new Case("9g", "`exchange(r@uniq, with: y@uniq)` while the child is live; write through the old value");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("$t = r@uniq (reserved)").Def("$t").Borrow("Lr", "r", Uniq, "$t", reserve: true);
        c.N("$u = y@uniq (reserved)").Def("$u").Borrow("Ly", "y", Uniq, "$u", reserve: true);
        c.N("let old = exchange($t, with: $u)").Activate("Lr").Activate("Ly").Def("old").Flow("$t.*", "old").Through("$t.*", "$u");
        c.N("old@follow = 7").A(K.Write, "old.*");
        c.N("inspect(c)").Use("c");
        return c.Error("old@follow = 7").S1(true, "let old = exchange($t, with: $u)");
    }

    // Coordinator's case: exchange lowered as a complete replacement of the move path r whose old value moves to `old`.
    static Case ExchangeReplaces()
    {
        var c = new Case("9n", "`old = exchange(r@uniq, with: y@uniq)` as a complete replacement of r; `old@follow = 3; c@follow = 4`");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("$t = r@uniq (reserved)").Def("$t").Borrow("Lr", "r", Uniq, "$t", reserve: true);
        c.N("$u = y@uniq (reserved)").Def("$u").Borrow("Ly", "y", Uniq, "$u", reserve: true);
        c.N("let old = exchange($t, with: $u)").Activate("Lr").Activate("Ly").Def("old").Flow("r", "old").Exchange("r").Flow("$u", "r");
        c.N("old@follow = 3").A(K.Write, "old.*");
        c.N("c@follow = 4").A(K.Write, "c.*");
        return c.Error("old@follow = 3").S1(true, "let old = exchange($t, with: $u)");
    }

    // swap(first: uniq/T, second: uniq/T): both targets wholly replaced, old values exchanged.
    static Case SwapBase()
    {
        var c = new Case("9o", "`swap(r@uniq, s@uniq)` while c is live; `s@follow = 3; c@follow = 4`");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("var s = y@uniq").Def("s").Borrow("Ls", "y", Uniq, "s");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("$a = r@uniq (reserved)").Def("$a").Borrow("La", "r", Uniq, "$a", reserve: true);
        c.N("$b = s@uniq (reserved)").Def("$b").Borrow("Lb", "s", Uniq, "$b", reserve: true);
        c.N("swap($a, $b)").Activate("La").Activate("Lb").Flow("r", "s").Flow("s", "r").Exchange("r").Exchange("s");
        c.N("s@follow = 3").A(K.Write, "s.*");
        c.N("c@follow = 4").A(K.Write, "c.*");
        return c.Error("s@follow = 3").S1(true, "swap($a, $b)");
    }

    // 9g, then a real replacement of the base kills L2 before the old value is used.
    static Case ExchangeThenReplace()
    {
        var c = new Case("9m", "9g, then `r = z@uniq` before writing through the old value");
        c.N("var x = 1; var y = 2; var z = 3").Def("x", "y", "z");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("$t = r@uniq (reserved)").Def("$t").Borrow("Lr", "r", Uniq, "$t", reserve: true);
        c.N("$u = y@uniq (reserved)").Def("$u").Borrow("Ly", "y", Uniq, "$u", reserve: true);
        c.N("let old = exchange($t, with: $u)").Activate("Lr").Activate("Ly").Def("old").Flow("$t.*", "old").Through("$t.*", "$u");
        c.N("r = z@uniq").Assign("r").Borrow("Lz", "z", Uniq, "r");
        c.N("old@follow = 7").A(K.Write, "old.*");
        c.N("inspect(c)").Use("c");
        return c.Error("old@follow = 7").S1(true, "let old = exchange($t, with: $u)");
    }

    // func steal(slot: uniq/(uniq/i32 during a), with: uniq/i32 during a) -> uniq/i32 during a => exchange(slot, with: with)
    static Case StealThroughW()
    {
        var c = new Case("9h", "child through `w = r@uniq`; a user call exchanges `w@follow` and returns the old value");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let w = r@uniq").Def("w").Borrow("Lw", "r", Uniq, "w");
        c.N("let c = w@follow@follow@uniq").Def("c").Borrow("L2", "w.*.*", Uniq, "c");
        c.N("$t = w (Reborrow at a uniq parameter, reserved)").Def("$t").Borrow("Lt", "w.*", Uniq, "$t", reserve: true);
        c.N("$u = y@uniq (reserved)").Def("$u").Borrow("Ly", "y", Uniq, "$u", reserve: true);
        c.N("let old = steal($t, with: $u)").Activate("Lt").Activate("Ly").Def("old").Flow("$t.*", "old").Flow("$u", "old").Through("$t.*", "$u");
        c.N("old@follow = 7").A(K.Write, "old.*");
        c.N("inspect(c)").Use("c");
        return c.Error("old@follow = 7").S1(true, "let old = steal($t, with: $u)");
    }

    static Case SharedBase()
    {
        var c = new Case("9i", "shared borrow of the base; read through it while the child is live");
        c.N("var x = 1").Def("x");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("let z = r@ref").Def("z").Borrow("Lz", "r", Ref, "z");
        c.N("inspect(z@follow@follow)").A(K.Read, "z.*.*");
        c.N("inspect(c)").Use("c");
        return c.Error("inspect(z@follow@follow)");
    }

    static Case Nested()
    {
        var c = new Case("9j", "reborrow through two references `r@follow@follow@uniq`; replace the outer base");
        c.N("var x = 1; var o = 5").Def("x", "o");
        c.N("var slot = x@uniq").Def("slot").Borrow("L1", "x", Uniq, "slot");
        c.N("var r = slot@uniq").Def("r").Borrow("Lr", "slot", Uniq, "r");
        c.N("let c = r@follow@follow@uniq").Def("c").Borrow("L2", "r.*.*", Uniq, "c");
        c.N("r = o@uniq").Assign("r").Borrow("Lo", "o", Uniq, "r");
        c.N("x = 3").Assign("x");
        c.N("inspect(c)").Use("c");
        return c.Error("x = 3");
    }

    static Case MoveBase()
    {
        var c = new Case("9k", "Move the base while its child is live (the loss S1 lists)");
        c.N("var x = 1").Def("x");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("let q = r@move").Def("q").A(K.Move, "r").Flow("r", "q");
        c.N("inspect(c)").Use("c");
        c.N("inspect(q)").Use("q");
        return c.Valid().S1(true, "let q = r@move");
    }

    // Valid by the current SPEC: the write replaces r's reference value, not x (§15.6.3, §15.7.3). S1′ rejects it.
    static Case UniqBase()
    {
        var c = new Case("9l", "`let w = r@uniq; w@follow = y@uniq` while the child is live (the loss the fix adds)");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("let w = r@uniq").Def("w").Borrow("Lw", "r", Uniq, "w");
        c.N("$t = y@uniq").Def("$t").Borrow("Ly", "y", Uniq, "$t");
        c.N("w@follow = $t").Through("w.*", "$t");
        c.N("inspect(c)").Use("c");
        var k = c.Valid().S1(true, "let w = r@uniq"); k.S1POnly = true; return k;
    }

    // ---- Round 2 ----
    // stash(v: uniq/i32) keeps v in storage the body does not own (^stash); a uniq parameter lets the callee use v
    // exclusively during the call (§15.6.4: a uniq requirement creates an exclusive Loan).
    static Case MoveBan(string id, bool rewrite)
    {
        var after = rewrite ? "r = y@uniq" : "c@follow = 1";
        var c = new Case(id, "`if cond => stash(r@move)`" + (rewrite ? "; `r = y@uniq`" : "") + "; `c@follow = 1`");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("if cond").Goto("  stash(r@move): Move r", after);
        c.N("  stash(r@move): Move r").Def("$v").A(K.Move, "r").Flow("r", "$v");
        c.N("  stash(r@move): callee uses v, keeps it").A(K.UBorrow, "$v.*").Flow("$v", "^stash").Goto(after);
        if (rewrite) c.N("r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r");
        c.N("c@follow = 1").A(K.Write, "c.*");
        return c.Error("  stash(r@move): callee uses v, keeps it").S1(true, "  stash(r@move): Move r");
    }

    // The parent is reached through another reference: c = o@follow@follow@m, base o (the first move path entered).
    static Case RefBase(string id, bool shared, string kind)
    {
        var o = shared ? "rs" : "rr"; var m = shared ? "ref" : "uniq";
        var write = kind == "outer" ? $"{o} = slot2@{m}" : kind == "inner" ? "slot = y@uniq" : $"{o}@follow = y@uniq";
        var c = new Case(id, $"`var {o} = slot@{m}; let c = {o}@follow@follow@{m}`; `{write}`; use c");
        c.N("var x = 1; var y = 2; var z = 3").Def("x", "y", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N($"var {o} = slot@{m}").Def(o).Borrow("Lo", "slot", !shared, o);
        c.N($"let c = {o}@follow@follow@{m}").Def("c").Borrow("L2", o + ".*.*", !shared, "c");
        if (kind == "outer") c.N(write).Assign(o).Borrow("Lo2", "slot2", !shared, o);
        else if (kind == "inner") c.N(write).Assign("slot").Borrow("Ly", "y", Uniq, "slot");
        else { c.N("$t = y@uniq").Def("$t").Borrow("Ly", "y", Uniq, "$t"); c.N(write).Through(o + ".*", "$t"); }
        c.N(shared ? "inspect(c@follow)" : "c@follow = 1").A(shared ? K.Read : K.Write, "c.*");
        return kind == "inner" ? c.Error(write) : c.Valid();
    }

    static Case MoveAncestor()
    {
        var c = new Case("14", "`let g = h@move` (an ancestor of base `h.link`) while c is live");
        c.N("var x = 1").Def("x");
        c.N("var h = H{link: x@uniq, n: 0}").Def("h").Borrow("L1", "x", Uniq, "h.link");
        c.N("let c = h.link@follow@uniq").Def("c").Borrow("L2", "h.link.*", Uniq, "c");
        c.N("let g = h@move").Def("g").A(K.Move, "h").Flow("h", "g");
        c.N("c@follow = 1").A(K.Write, "c.*");
        c.N("inspect(g)").Use("g");
        return c.Valid().S1(true, "let g = h@move");
    }

    // replace<T>(target: uniq/T ! with: T): the old value is destroyed in place, so `ended` applies and no base_op.
    static Case ReplaceBase(string id, bool write)
    {
        var c = new Case(id, "`Kimi.Intrinsics.replace(r@uniq, with: y@uniq)` while c is live" + (write ? "; `x = 3`" : "") + "; `c@follow = 1`");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("$t = r@uniq (reserved, replace target)").Def("$t").Borrow("Lr", "r", Uniq, "$t", reserve: true, inPlace: true);
        c.N("$u = y@uniq (reserved)").Def("$u").Borrow("Ly", "y", Uniq, "$u", reserve: true);
        c.N("replace($t, with: $u)").Activate("Lr").Activate("Ly").Def("r").Flow("$u", "r");
        if (write) c.N("x = 3").Assign("x");
        c.N("c@follow = 1").A(K.Write, "c.*");
        c.ALoss = "A §2 counts a replace target as an exclusive borrow; plan e32dc0ef exempts it"; return write ? c.Error("x = 3") : c.Valid();
    }

    // ---- Round 3: adversarial cases for design A ----
    // Syntactic access without authority: aliases formed through a reborrow, a Copy, an aggregate, a write through a reference.
    static Case Alias(string id, string how)
    {
        var c = new Case(id, how switch
        {
            "reborrow" => "`let a = r` (Reborrow); `let c = a@follow@uniq`; `r@follow = 5`; `inspect(c)`",
            "copy" => "`let z = r@follow@ref; let z2 = z`; `let c = r@follow@uniq`; `inspect(z2)`",
            "aggregate" => "`let c = r@follow@uniq; var h = H{link: c}`; `r@follow = 5`; `inspect(h)`",
            "stored" => "`let w = s@uniq; w@follow = r` (stores a Reborrow of r in s); `r@follow = 5`; `inspect(s@follow)`",
            _ => "`var rr = slot@uniq; let c = rr@follow@follow@uniq`; `slot@follow = 5`; `inspect(c)`",
        });
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        var probe = "r@follow = 5";
        switch (how)
        {
            case "reborrow":
                c.N("let a = r").Def("a").Borrow("La", "r.*", Uniq, "a");
                c.N("let c = a@follow@uniq").Def("c").Borrow("L2", "a.*", Uniq, "c");
                c.N(probe).A(K.Write, "r.*"); c.N("inspect(c)").Use("c"); break;
            case "copy":
                c.N("let z = r@follow@ref").Def("z").Borrow("Lz", "r.*", Ref, "z");
                c.N("let z2 = z").Def("z2").Flow("z", "z2");
                probe = "let c = r@follow@uniq"; c.N(probe).Def("c").Borrow("L2", "r.*", Uniq, "c");
                c.N("inspect(z2)").Use("z2"); c.N("inspect(c)").Use("c"); break;
            case "aggregate":
                c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
                c.N("var h = H{link: c}").Def("h").Borrow("Lh", "c.*", Uniq, "h.link");
                c.N(probe).A(K.Write, "r.*"); c.N("inspect(h)").Use("h"); break;
            case "stored":
                c.N("var s = y@uniq").Def("s").Borrow("Ly", "y", Uniq, "s");
                c.N("let w = s@uniq").Def("w").Borrow("Lw", "s", Uniq, "w");
                c.N("$t = r (Reborrow)").Def("$t").Borrow("La", "r.*", Uniq, "$t");
                c.N("w@follow = $t").Through("w.*", "$t");
                c.N(probe).A(K.Write, "r.*"); c.N("inspect(s@follow)").A(K.Read, "s.*"); break;
            default:
                c.N("var slot = x@uniq").Def("slot").Borrow("Ls", "x", Uniq, "slot");
                c.N("var rr = slot@uniq").Def("rr").Borrow("Lo", "slot", Uniq, "rr");
                c.N("let c = rr@follow@follow@uniq").Def("c").Borrow("L2", "rr.*.*", Uniq, "c");
                probe = "slot@follow = 5"; c.N(probe).A(K.Write, "slot.*"); c.N("inspect(c)").Use("c"); break;
        }
        return c.Error(probe);
    }

    // The closure needs no feedback: references held by holders that are dead at the conflicting access.
    static Case DeadHolder(string id, bool chain)
    {
        var c = new Case(id, chain ? "`mid = slot@ref; top = mid@ref`; `consume(data@move)`; read through `top` (slot and mid otherwise dead)"
                                   : "`w = slot@uniq`; `consume(data@move)`; `t = w@follow@ref` formed after it; read through `t`");
        c.N("var data = makeData()").Def("data");
        c.N("var slot: ref/Data = data@ref").Def("slot").Borrow("Ld", "data", Ref, "slot");
        if (chain)
        {
            c.N("var mid = slot@ref").Def("mid").Borrow("Ls", "slot", Ref, "mid");
            c.N("var top = mid@ref").Def("top").Borrow("Lm", "mid", Ref, "top");
            c.N("consume(data@move)").A(K.Move, "data");
            c.N("inspect(top@follow@follow@follow)").A(K.Read, "top.*.*.*");
        }
        else
        {
            c.N("var w = slot@uniq").Def("w").Borrow("Lw", "slot", Uniq, "w");
            c.N("consume(data@move)").A(K.Move, "data");
            c.N("let t = w@follow@ref").Def("t").Borrow("Lt", "w.*", Ref, "t");
            c.N("inspect(t@follow@follow)").A(K.Read, "t.*.*");
        }
        return c.Error("consume(data@move)");
    }

    // P1 and a partial Move: struct P2 {left: ref/i32, right: ref/i32}, not Copy.
    static Case PartialMove(string id, bool restore)
    {
        var c = new Case(id, restore ? "`let l = p.left@move; p.left = c@ref; a = 3; inspect(p)`" : "`let l = p.left@move; a = 3; inspect(l)`");
        c.N("var a = 1; var b = 2; var c = 3").Def("a", "b", "c");
        c.N("var p = P2{left: a@ref, right: b@ref}").Def("p").Borrow("La", "a", Ref, "p.left").Borrow("Lb", "b", Ref, "p.right");
        c.N("let l = p.left@move").Def("l").A(K.Move, "p.left").Flow("p.left", "l");
        if (restore) c.N("p.left = c@ref").Assign("p.left").Borrow("Lc", "c", Ref, "p.left");
        c.N("a = 3").Assign("a");
        c.N(restore ? "inspect(p)" : "inspect(l)").Use(restore ? "p" : "l");
        if (restore) { c.ALoss = "P1 (a partial write ends no Loan)"; return c.Valid(); }
        return c.Error("a = 3");
    }

    // A Reborrow stored back into its own base (the in-place traversal); 19b also writes through the parent root.
    static Case InPlace(string id, bool parent)
    {
        var c = new Case(id, "`cursor = cursor.children[0]@uniq` in a loop" + (parent ? "; `root.value = 2`" : "") + "; `cursor.value = 1`");
        c.N("func descend(root: uniq/Tree)").Param("Lroot", "root");
        c.N("var cursor = root").Def("cursor").Borrow("L0", "root.*", Uniq, "cursor");
        c.N("while cursor.children.length > 0").A(K.Read, "cursor.*.children").Goto("  cursor = cursor.children[0]@uniq", parent ? "root.value = 2" : "cursor.value = 1");
        c.N("  cursor = cursor.children[0]@uniq").Assign("cursor").Borrow("Ln", "cursor.*.children.[]", Uniq, "cursor").Goto("while cursor.children.length > 0");
        if (parent) c.N("root.value = 2").A(K.Write, "root.*.value");
        c.N("cursor.value = 1").A(K.Write, "cursor.*.value");
        return parent ? c.Error("root.value = 2") : c.Valid();
    }

    // ---- Round 4: one Loan id covers every iteration of a borrow site ----
    static Case LoopSite(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "uniq" => "loop: `let cur = v@uniq; inspect(prev); prev = cur`",
            "child" => "loop: `let cur = r@follow@uniq; inspect(prev); prev = cur`",
            "reserved" => "loop: `let k2 = f(v@uniq)` (reserved; f retains); `inspect(keep); keep = k2`",
            "window" => "loop: reserved `$t = v@uniq`, then `$n = v + 0` in the window; `keep = f($t, $n)`",
            "refkept" => "loop: `let u = v@uniq; u@follow = 1; inspect(keep); let a = v@ref; keep = a`",
            _ => "loop: `let s = v@ref; inspect(s); inspect(keep); let a = v@uniq; keep = a`",
        });
        c.N("var v = 1; var w = 2; var z = 0").Def("v", "w", "z");
        var head = "loop";
        switch (kind)
        {
            case "uniq": case "child":
                if (kind == "child") c.N("var x = 1; var r = x@uniq").Def("x", "r").Borrow("L1", "x", Uniq, "r");
                c.N("var prev = w@uniq").Def("prev").Borrow("Lw", "w", Uniq, "prev");
                c.N(head);
                var site = kind == "uniq" ? "  let cur = v@uniq" : "  let cur = r@follow@uniq";
                c.N(site).Def("cur").Borrow("Lc", kind == "uniq" ? "v" : "r.*", Uniq, "cur");
                c.N("  inspect(prev)").Use("prev");
                c.N("  prev = cur").Assign("prev").Flow("cur", "prev").Goto(head);
                return c.Error(site);
            case "reserved": case "window":
                c.N("var keep = w@uniq").Def("keep").Borrow("Lw", "w", Uniq, "keep");
                c.N(head);
                c.N("  $t = v@uniq (reserved)").Def("$t").Borrow("Lt", "v", Uniq, "$t", reserve: true);
                if (kind == "window") c.N("  $n = v + 0").Def("$n").A(K.Read, "v");
                c.N("  let k2 = f($t)").Activate("Lt").Def("k2").Flow("$t", "k2");
                c.N("  inspect(keep)").Use("keep");
                c.N("  keep = k2").Assign("keep").Flow("k2", "keep").Goto(head);
                return c.Error();
            case "refkept":
                c.N("var keep: ref/i32 = z@ref").Def("keep").Borrow("Lz", "z", Ref, "keep");
                c.N(head);
                c.N("  let u = v@uniq").Def("u").Borrow("Lu", "v", Uniq, "u");
                c.N("  u@follow = 1").A(K.Write, "u.*");
                c.N("  inspect(keep)").Use("keep");
                c.N("  let a = v@ref").Def("a").Borrow("La", "v", Ref, "a");
                c.N("  keep = a").Assign("keep").Flow("a", "keep").Goto(head);
                return c.Error(); // `let u` and the write through u both conflict with the earlier shared Loan
            default:
                c.N("var keep = w@uniq").Def("keep").Borrow("Lw", "w", Uniq, "keep");
                c.N(head);
                c.N("  let s = v@ref").Def("s").Borrow("Ls", "v", Ref, "s");
                c.N("  inspect(s)").Use("s");
                c.N("  inspect(keep)").Use("keep");
                c.N("  let a = v@uniq").Def("a").Borrow("La", "v", Uniq, "a");
                c.N("  keep = a").Assign("keep").Flow("a", "keep").Goto(head);
                return c.Error("  let s = v@ref");
        }
    }

    // ---- Round 5 (plan fe1a5b34) ----
    // MIR's Temp shape of `cursor = cursor.children[0]@uniq`: the child is issued into _t, then moved into cursor.
    static Case TempStep(string id, bool clear)
    {
        var c = new Case(id, "Temp shape: `_t = Ref(cursor.children[0])`" + (clear ? "; `cursor.children.clear()`" : "") + "; `cursor = move _t`");
        c.N("func descend(root: uniq/Tree)").Param("Lroot", "root");
        c.N("var cursor = root").Def("cursor").Borrow("L0", "root.*", Uniq, "cursor");
        c.N("while cursor.children.length > 0").A(K.Read, "cursor.*.children").Goto("  _t = Ref(cursor.children[0])", "cursor.value = 1");
        c.N("  _t = Ref(cursor.children[0])").Def("_t").Borrow("Ln", "cursor.*.children.[]", Uniq, "_t");
        if (clear) c.N("  cursor.children.clear()").A(K.UBorrow, "cursor.*.children");
        c.N("  cursor = move _t").Assign("cursor").A(K.Move, "_t").Flow("_t", "cursor").Goto("while cursor.children.length > 0");
        c.N("cursor.value = 1").A(K.Write, "cursor.*.value");
        return clear ? c.Error("  cursor.children.clear()") : c.Valid();
    }

    // var arr: [2 of uniq/i32] = [x@uniq, y@uniq]; a child through arr[i], an exchange of arr[j] (§4.1, §15.1.3).
    static Case IndexBan(string id, bool constant)
    {
        var (ci, cj) = constant ? ("0", "1") : ("[]", "[]");
        var (si, sj) = constant ? ("0", "1") : ("i", "j");
        var c = new Case(id, $"`c = arr[{si}]@follow@uniq; old = exchange(arr[{sj}]@uniq, with: z@uniq); old@follow = 7; inspect(c)`");
        c.N("var x = 1; var y = 2; var z = 3; let i: isize = pick(); let j: isize = pick()").Def("x", "y", "z", "i", "j");
        c.N("var arr: [2 of uniq/i32] = [x@uniq, y@uniq]").Def("arr").Borrow("Lx", "x", Uniq, "arr.0").Borrow("Ly", "y", Uniq, "arr.1");
        c.N($"let c = arr[{si}]@follow@uniq").Def("c").Borrow("L2", $"arr.{ci}.*", Uniq, "c");
        c.N($"$t = arr[{sj}]@uniq (reserved)").Def("$t").Borrow("Lt", $"arr.{cj}", Uniq, "$t", reserve: true);
        c.N("$u = z@uniq (reserved)").Def("$u").Borrow("Lz", "z", Uniq, "$u", reserve: true);
        c.N("let old = exchange($t, with: $u)").Activate("Lt").Activate("Lz").Def("old").Flow("$t.*", "old").Through("$t.*", "$u");
        c.N("old@follow = 7").A(K.Write, "old.*");
        c.N("inspect(c)").Use("c");
        return constant ? c.Valid() : c.Error("old@follow = 7").S1(true, "let old = exchange($t, with: $u)");
    }

    // The outer holder is replaced; the child's base is rr.*, which `ended` (no Deref between B and K) never ends.
    static Case OuterThenWrite()
    {
        var c = new Case("23a", "`c = rr@follow@follow@uniq; rr = slot2@uniq; rr@follow@follow = 7; c@follow = 1`");
        c.N("var x = 1; var z = 3").Def("x", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var rr = slot@uniq").Def("rr").Borrow("Lo", "slot", Uniq, "rr");
        c.N("let c = rr@follow@follow@uniq").Def("c").Borrow("L2", "rr.*.*", Uniq, "c");
        c.N("rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr");
        c.N("rr@follow@follow = 7").A(K.Write, "rr.*.*");
        c.N("c@follow = 1").A(K.Write, "c.*");
        return c.Valid().S1(true, "rr@follow@follow = 7"); // listed loss of the plan's kill (round 6 correction)
    }

    // Round 6 correction (plan review): a kill across a Deref ends both children of q. S {var p: uniq/i32}.
    static Case CrossingKill()
    {
        var c = new Case("29a", "(coordinator's 25a) `q = s@uniq; c = q.p@follow@uniq; w = q@follow@ref; q = o@uniq; a = w.p@follow; c@follow = 5; inspect(w.p@follow)`");
        c.N("var t = 1; var u = 2").Def("t", "u");
        c.N("var s = S(p: t@uniq); var o = S(p: u@uniq)").Def("s", "o").Borrow("Lt", "t", Uniq, "s.p").Borrow("Lu", "u", Uniq, "o.p");
        c.N("var q = s@uniq").Def("q").Borrow("Lq", "s", Uniq, "q");
        c.N("let c = q.p@follow@uniq").Def("c").Borrow("Lc", "q.*.p.*", Uniq, "c");
        c.N("let w = q@follow@ref").Def("w").Borrow("Lw", "q.*", Ref, "w");
        c.N("q = o@uniq").Assign("q").Borrow("Lo", "o", Uniq, "q");
        c.N("let a = w.p@follow").Def("a").A(K.Read, "w.*.p.*");
        c.N("c@follow = 5").A(K.Write, "c.*");
        c.N("inspect(w.p@follow)").A(K.Read, "w.*.p.*");
        return c.Error();
    }

    // Transit into an escaped destination: p: uniq/S; S's drop writes through self.r; drop and store at one point.
    static Case EscapedTransit()
    {
        var c = new Case("29b", "`p: uniq/S`; `p@follow = S(r: p.r@follow@uniq)`, S's drop writes through self.r (one point)");
        c.N("func f(p: uniq/S)").Param("Lp", "p");
        c.N("$t = p.r@follow@uniq").Def("$t").Borrow("L2", "p.*.r.*", Uniq, "$t");
        c.N("$v = S(r: $t)").Def("$v").Flow("$t", "$v.r");
        c.N("p@follow = move $v (drops old *p)").Through("p.*", "$v").Drop("p.*").A(K.Write, "p.*.r.*").A(K.Move, "$v");
        return c.Error("p@follow = move $v (drops old *p)");
    }

    // Linked-list traversal (Temp shape). struct Node {var value: i32; var next: Option<L>}; `.some` is the payload
    // a match selects. ll1: L = obj/Node, whose payload is an owned step (part of the handle's storage, A2 §3), so the
    // place has one borrow Deref and B = node. ll2: L = uniq/Node, so B = node.*.next.some, and `node = …` ends nothing.
    static Case LinkedList(string id, string kind)
    {
        bool obj = kind == "obj"; bool conflict = kind == "conflict";
        var c = new Case(id, (obj ? "ll1 (obj links)" : conflict ? "ll3 (uniq links, write through the old link)" : kind == "peek" ? "ll2 + read through the new node's link" : "ll2 (uniq links)")
            + ": loop `_t = node.next.some@follow@uniq; node = move _t`" + (conflict ? " with `node.next@follow.value = 1` while _t is live" : ""));
        c.N("func last(head: uniq/Node)").Param("Lh", "head");
        c.N("var node = head").Def("node").Borrow("L0", "head.*", Uniq, "node");
        c.N("while node.next is .Some").A(K.Read, "node.*.next").Goto("  _t = node.next.some@follow@uniq", "node.value = 1");
        c.N("  _t = node.next.some@follow@uniq").Def("_t").Borrow("Ln", obj ? "node.*.next.some.payload" : "node.*.next.some.*", Uniq, "_t");
        if (conflict) c.N("  node.next@follow.value = 1").A(K.Write, "node.*.next.some.*.value");
        var step = c.N("  node = move _t").Assign("node").A(K.Move, "_t").Flow("_t", "node");
        if (kind == "peek") c.N("  inspect(node.next@follow.value)").A(K.Read, "node.*.next.some.*.value").Goto("while node.next is .Some");
        else step.Goto("while node.next is .Some");
        c.N("node.value = 1").A(K.Write, "node.*.value");
        if (kind == "peek") c.ALoss = "23a family: the plan's within kill (stale base name through a Deref)";
        return conflict ? c.Error("  node.next@follow.value = 1") : c.Valid();
    }

    // ---- Round 7 (design A3 unit 0b) ----
    // swap with two reference parameters: the caller's activation keeps them disjoint (S15:614). 31b: the caller.
    static Case SwapParams(string id, bool caller)
    {
        var c = new Case(id, caller ? "caller of `f(a, b) => swap(a, b)`: `f(x@uniq, x@uniq)`" : "`func f(a: uniq/T, b: uniq/T) => Kimi.Intrinsics.swap(a, b)`");
        if (caller)
        {
            c.N("var x = make()").Def("x");
            c.N("$1 = x@uniq (reserved)").Def("$1").Borrow("L1", "x", Uniq, "$1", reserve: true);
            c.N("$2 = x@uniq (reserved)").Def("$2").Borrow("L2", "x", Uniq, "$2", reserve: true);
            c.N("f($1, $2)").Activate("L1").Activate("L2").Use("$1", "$2");
            return c.Error("f($1, $2)");
        }
        c.N("func f(a: uniq/T, b: uniq/T)").Param("La", "a").Param("Lb", "b");
        c.N("$1 = a (Reborrow at a uniq parameter, reserved)").Def("$1").Borrow("L1", "a.*", Uniq, "$1", reserve: true);
        c.N("$2 = b (Reborrow at a uniq parameter, reserved)").Def("$2").Borrow("L2", "b.*", Uniq, "$2", reserve: true);
        c.N("swap($1, $2)").Activate("L1").Activate("L2").Use("$1", "$2");
        return c.Valid();
    }

    // A Copy read through a reference stored into a holder (BinaryOp: the value carries no Loan).
    static Case CopyThrough(string id, bool childLive)
    {
        var c = new Case(id, childLive ? "`var h = x@uniq; let c = h@follow@uniq; y = h@follow + 1; c@follow = 2`" : "`let h = x@ref; y = h@follow + 1; x = 3; inspect(y)`");
        c.N("var x = 1; var y = 0").Def("x", "y");
        if (childLive)
        {
            c.N("var h = x@uniq").Def("h").Borrow("Lh", "x", Uniq, "h");
            c.N("let c = h@follow@uniq").Def("c").Borrow("Lc", "h.*", Uniq, "c");
            c.N("$v = h@follow").Def("$v").A(K.Read, "h.*").Flow("h.*", "$v");
            c.N("y = $v + 1").Assign("y").Use("$v");
            c.N("c@follow = 2").A(K.Write, "c.*");
            return c.Error("$v = h@follow");
        }
        c.N("let h = x@ref").Def("h").Borrow("Lh", "x", Ref, "h");
        c.N("$v = h@follow").Def("$v").A(K.Read, "h.*").Flow("h.*", "$v");
        c.N("y = $v + 1").Assign("y").Use("$v");
        c.N("x = 3").Assign("x");
        c.N("inspect(y)").Use("y");
        return c.Valid();
    }

    // §8.11.3 takeTwo (valid) and the plan's takeAcross (error). `take` is abstract and its result descends from the
    // receiver, so it carries Φ_c on `source.*`. 33c: the excuse only at the Call, not at the receiver's reservation.
    static Case PreservesResults(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "across" => "`takeAcross`: `first = source.take(); source@follow = fresh@move; second = source.take()`",
            "callonly" => "`takeTwo` with the excuse at the Call only (A3's letter)",
            _ => "`takeTwo<S: StableTake>`: `first = source.take(); second = source.take()` (§8.11.3)",
        });
        c.N("func f(source: uniq/S, fresh: S)").Param("Ls", "source").Def("fresh");
        c.N("$r1 = source (receiver, reserved)").Def("$r1").Borrow("Lr1", "source.*", Uniq, "$r1", reserve: true);
        c.N("let first = source.take()").Activate("Lr1").Def("first").Phantom("Phi1", "source.*", "first", "Lr1");
        if (kind == "across") c.N("source@follow = fresh@move").A(K.Move, "fresh").Through("source.*", "fresh").Disturb("Phi1");
        var r2 = c.N("$r2 = source (receiver, reserved)").Def("$r2").Borrow("Lr2", "source.*", Uniq, "$r2", reserve: true);
        if (kind != "callonly") r2.Excuse("Phi1");
        c.N("let second = source.take()").Activate("Lr2").Def("second").Phantom("Phi2", "source.*", "second", "Lr2").Excuse("Phi1");
        c.N("return (first@move, second@move)").Flow("first", "$ret").Flow("second", "$ret");
        return kind == "across" ? c.Error() : c.Valid();
    }

    // ---- Round 8 (case 23a proposal review) ----
    // A child through arr[i] (one or two borrow Derefs); arr[0] (34a/34b) or the whole arr (34c) is replaced; a write
    // through arr[i], the old route unless the whole array was replaced.
    static Case IndexKill(string id, bool deref2, bool whole = false)
    {
        var f = deref2 ? "@follow@follow" : "@follow";
        var c = new Case(id, $"`c = arr[i]{f}@uniq; {(whole ? "arr = [z@uniq, w@uniq]" : "arr[0] = …")}; arr[i]{f} = 7; c@follow = 1`");
        c.N("var x = 1; var y = 2; var z = 3; var w = 4; let i: isize = pick()").Def("x", "y", "z", "w", "i");
        if (deref2)
        {
            c.N("var s1 = x@uniq; var s2 = y@uniq; var s3 = z@uniq").Def("s1", "s2", "s3").Borrow("Lx", "x", Uniq, "s1").Borrow("Ly", "y", Uniq, "s2").Borrow("Lz", "z", Uniq, "s3");
            c.N("var arr: [2 of uniq/uniq/i32] = [s1@uniq, s2@uniq]").Def("arr").Borrow("L0", "s1", Uniq, "arr.0").Borrow("L1", "s2", Uniq, "arr.1");
            c.N("let c = arr[i]@follow@follow@uniq").Def("c").Borrow("L2", "arr.[].*.*", Uniq, "c");
            c.N("arr[0] = s3@uniq").Assign("arr.0").Borrow("L3", "s3", Uniq, "arr.0");
        }
        else
        {
            c.N("var arr: [2 of uniq/i32] = [x@uniq, y@uniq]").Def("arr").Borrow("Lx", "x", Uniq, "arr.0").Borrow("Ly", "y", Uniq, "arr.1");
            c.N("let c = arr[i]@follow@uniq").Def("c").Borrow("L2", "arr.[].*", Uniq, "c");
            if (whole) c.N("arr = [z@uniq, w@uniq]").Assign("arr").Borrow("Lz", "z", Uniq, "arr.0").Borrow("Lw", "w", Uniq, "arr.1");
            else c.N("arr[0] = z@uniq").Assign("arr.0").Borrow("Lz", "z", Uniq, "arr.0");
        }
        c.N($"arr[i]{f} = 7").A(K.Write, deref2 ? "arr.[].*.*" : "arr.[].*");
        c.N("c@follow = 1").A(K.Write, "c.*");
        if (!whole) return c.Error($"arr[i]{f} = 7");
        c.ALoss = "the plan's within: no end across a non-path index after K (narrower than S1's text)";
        return c.Valid();
    }

    // 23a split through a named intermediate: t's Loan has base rr, so rr = slot2@uniq ends it under every base rule.
    static Case SplitOuter()
    {
        var c = new Case("37", "23a through a named intermediate: `t = rr@follow@uniq; c = t@follow@uniq; rr = slot2@uniq; rr@follow@follow = 7; c@follow = 1`");
        c.N("var x = 1; var z = 3").Def("x", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var rr = slot@uniq").Def("rr").Borrow("Lo", "slot", Uniq, "rr");
        c.N("let t = rr@follow@uniq").Def("t").Borrow("Lt", "rr.*", Uniq, "t");
        c.N("let c = t@follow@uniq").Def("c").Borrow("L2", "t.*", Uniq, "c");
        c.N("rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr");
        c.N("rr@follow@follow = 7").A(K.Write, "rr.*.*");
        c.N("c@follow = 1").A(K.Write, "c.*");
        return c.Valid();
    }

    // uniq-linked walk (ll2): a Borrow of the new node's Field outlives the next step. 35a is straight-line; 35b keeps
    // each iteration's Borrow in one holder used after the loop.
    static Case Walk(string id, bool loop)
    {
        var c = new Case(id, loop ? "ll2 loop: `keep = node.value@uniq; node = node.next…@uniq`, keep used after the loop"
            : "`node = node.next…@uniq; b = node.value@uniq; node = node.next…@uniq; b@follow = 1`");
        c.N("func f(head: uniq/Node)").Param("Lh", "head");
        c.N("var node = head").Def("node").Borrow("L0", "head.*", Uniq, "node");
        if (loop)
        {
            c.N("var keep = none").Def("keep");
            c.N("while node.next is .Some").A(K.Read, "node.*.next").Goto("  keep = node.value@uniq", "keep@follow = 1");
            c.N("  keep = node.value@uniq").Assign("keep").Borrow("Lb", "node.*.value", Uniq, "keep");
            c.N("  _t = node.next.some@follow@uniq").Def("_t").Borrow("Ln", "node.*.next.some.*", Uniq, "_t");
            c.N("  node = move _t").Assign("node").A(K.Move, "_t").Flow("_t", "node").Goto("while node.next is .Some");
            c.N("keep@follow = 1").A(K.Write, "keep.*");
        }
        else
        {
            c.N("_t = node.next.some@follow@uniq").Def("_t").Borrow("Ln1", "node.*.next.some.*", Uniq, "_t");
            c.N("node = move _t").Assign("node").A(K.Move, "_t").Flow("_t", "node");
            c.N("let b = node.value@uniq").Def("b").Borrow("Lb", "node.*.value", Uniq, "b");
            c.N("_t2 = node.next.some@follow@uniq").Def("_t2").Borrow("Ln2", "node.*.next.some.*", Uniq, "_t2");
            c.N("node = move _t2").Assign("node").A(K.Move, "_t2").Flow("_t2", "node");
            c.N("b@follow = 1").A(K.Write, "b.*");
        }
        c.ALoss = "23a family: the plan's within kill (a Borrow through the new node while a kept value carries the stale Loan)";
        return c.Valid();
    }

    // F6 phantom through a replaced holder. H {var inner: uniq/S}; Φ_c on cur.*.inner.* is carried by first. cur moves to
    // b; then 36a/36b clear a's iterator through cur = a again, 36c/36d through a itself. retain: first also keeps the
    // receiver Loan Lr1 (A3 `out` with Value retention); otherwise first keeps Φ_c and, from round 9, its parent Loans.
    static Case PhantomParents(string id, bool direct, bool retain)
    {
        var c = new Case(id, "F6: `first = cur.inner.take(); cur = b; " + (direct ? "a.inner.clear()" : "cur = a; cur.inner.clear()")
            + "; return first`" + (retain ? ", first retains the receiver" : ""));
        c.N("func f(a: uniq/H, b: uniq/H)").Param("La", "a").Param("Lb", "b");
        c.N("var cur = a").Def("cur").Borrow("L0", "a.*", Uniq, "cur");
        c.N("$r1 = cur.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "cur.*.inner.*", Uniq, "$r1", reserve: true);
        var call = c.N("let first = cur.inner.take()").Activate("Lr1").Def("first");
        if (retain) call.Flow("$r1", "first");
        call.Phantom("Phi1", "cur.*.inner.*", "first", "Lr1");
        c.N("cur = b").Assign("cur").Borrow("Lb2", "b.*", Uniq, "cur").Disturb("Phi1");
        if (!direct) c.N("cur = a").Assign("cur").Borrow("La2", "a.*", Uniq, "cur");
        var recv = direct ? "a.*.inner.*" : "cur.*.inner.*";
        c.N("$r2 = " + (direct ? "a" : "cur") + ".inner (receiver of clear)").Def("$r2").Borrow("Lr2", recv, Uniq, "$r2");
        c.N("clear($r2)").Use("$r2");
        c.N("return first").Flow("first", "$ret");
        return c.Error();
    }

    // Round 9 (G113), from the review. 36e/36h: a `do` argument assigns cur during the receiver's reservation, which
    // SPEC 15.6.7 rejects and A3's shallow write misses (36h, a gap outside G113); 36e then shows that Φ_c's parents
    // come from the receiver's value (a's), not from recv(c) at the call (b's). 36f: the cost P12, a write to another
    // part of the parent referent while the result is live (SPEC and rustc accept). 36g: plain Take; `it = a` ends Φ_c
    // under the plan base, so only the parent Loans reject the reborrow.
    static Case PhantomRoot(string id)
    {
        var c = new Case(id, "F6: " + id switch
        {
            "36e" => "`first = cur.inner.takeWith(do { cur = b; 0 }); a.inner.clear(); return first`",
            "36f" => "`var h = H(inner: src, other: 0); var r = h@uniq; first = r.inner.take(); h.other = 5; return first`",
            "36g" => "`var it = a; first = it.take(); it = a; second = it.take()` (Take only)",
            _ => "`first = cur.inner.takeWith(do { cur = b; 0 }); return first`",
        });
        if (id == "36f")
        {
            c.N("func f(src: uniq/S)").Param("Ls", "src");
            c.N("var h = H(inner: src, other: 0)").Def("h").Borrow("Lsrc", "src.*", Uniq, "h.inner");
            c.N("var r = h@uniq").Def("r").Borrow("Lh", "h", Uniq, "r");
            c.N("$r1 = r.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "r.*.inner.*", Uniq, "$r1", reserve: true);
            c.N("let first = r.inner.take()").Activate("Lr1").Def("first").Phantom("Phi1", "r.*.inner.*", "first", "Lr1");
            c.N("h.other = 5").Assign("h.other");
            c.N("return first").Flow("first", "$ret");
            c.ALoss = "P12 (G113): a descending abstract-call result keeps the receiver's parent Loans";
            return c.Valid();
        }
        if (id == "36g")
        {
            c.N("func f(a: uniq/S)").Param("La", "a");
            c.N("var it = a").Def("it").Borrow("L0", "a.*", Uniq, "it");
            c.N("$r1 = it (receiver, reserved)").Def("$r1").Borrow("Lr1", "it.*", Uniq, "$r1", reserve: true);
            c.N("let first = it.take()").Activate("Lr1").Def("first").Phantom("Phi1", "it.*", "first", "Lr1");
            c.N("it = a").Assign("it").Borrow("L1", "a.*", Uniq, "it").Disturb("Phi1");
            c.N("$r2 = it (receiver, reserved)").Def("$r2").Borrow("Lr2", "it.*", Uniq, "$r2", reserve: true);
            c.N("let second = it.take()").Activate("Lr2").Def("second").Phantom("Phi2", "it.*", "second", "Lr2");
            c.N("return (first, second)").Flow("first", "$ret").Flow("second", "$ret");
            return c.Error();
        }
        c.N("func f(a: uniq/H, b: uniq/H)").Param("La", "a").Param("Lb", "b");
        c.N("var cur = a").Def("cur").Borrow("L0", "a.*", Uniq, "cur");
        c.N("$r1 = cur.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "cur.*.inner.*", Uniq, "$r1", reserve: true);
        c.N("do { cur = b; 0 } (argument)").Assign("cur").Borrow("Lb2", "b.*", Uniq, "cur");
        c.N("let first = takeWith($r1, …)").Activate("Lr1").Def("first").Phantom("Phi1", "cur.*.inner.*", "first", "Lr1");
        if (id == "36e")
        {
            c.N("$r2 = a.inner (receiver of clear)").Def("$r2").Borrow("Lr2", "a.*.inner.*", Uniq, "$r2");
            c.N("clear($r2)").Use("$r2");
        }
        c.N("return first").Flow("first", "$ret");
        return id == "36h" ? c.Error("do { cur = b; 0 } (argument)") : c.Error(); // 36e: SPEC errs first at the write, as in 36h
    }

    // Path-exact transit: the store goes to s.inner, while only s is used later.
    static Case PartTransit()
    {
        var c = new Case("29c", "`s.inner = f(s.inner@ref)` (retains); only `s` used later");
        c.N("var s = S(inner: make(), other: 0)").Def("s");
        c.N("$a = s.inner@ref").Def("$a").Borrow("Li", "s.inner", Ref, "$a");
        c.N("$r = f($a)").Def("$r").Flow("$a", "$r");
        c.N("s.inner = move $r").Assign("s.inner").A(K.Move, "$r").Flow("$r", "s.inner");
        c.N("inspect(s)").Use("s");
        return c.Error("s.inner = move $r");
    }

    // Soundness probe for a kill across a Deref: the outer holder is a reborrow of a parameter's referent.
    static Case ParamOuter()
    {
        var c = new Case("23b", "`q: uniq/(uniq/i32)`; `var q2 = q; c = q2@follow@follow@uniq; q2 = s2@uniq; q@follow@follow = 5; inspect(c)`");
        c.N("func f(q: uniq/(uniq/i32))").Param("Lq", "q");
        c.N("var o = 5; var s2 = o@uniq").Def("o", "s2").Borrow("Ls2", "o", Uniq, "s2");
        c.N("var q2 = q").Def("q2").Borrow("Lq2", "q.*", Uniq, "q2");
        c.N("let c = q2@follow@follow@uniq").Def("c").Borrow("L2", "q2.*.*", Uniq, "c");
        c.N("q2 = s2@uniq").Assign("q2").Borrow("Lq3", "s2", Uniq, "q2");
        c.N("q@follow@follow = 5").A(K.Write, "q.*.*");
        c.N("inspect(c)").Use("c");
        return c.Error("q@follow@follow = 5");
    }

    // struct S {r: uniq/i32}; 24a: S has a `drop` writing through self.r. `s = S(r: s.r@follow@uniq)` in MIR order:
    // the right side into Temps, the old value destroyed (its drop runs), then the store.
    static Case DropBase(string id, bool userDrop)
    {
        var c = new Case(id, "`s = S(r: s.r@follow@uniq)`" + (userDrop ? ", S's drop writes through self.r" : ", S has no drop"));
        c.N("var x = 1").Def("x");
        c.N("var s = S(r: x@uniq)").Def("s").Borrow("L1", "x", Uniq, "s.r");
        c.N("$t = s.r@follow@uniq").Def("$t").Borrow("L2", "s.r.*", Uniq, "$t");
        c.N("$v = S(r: $t)").Def("$v").Flow("$t", "$v.r");
        var d = c.N("destroy old s" + (userDrop ? " (drop writes self.r@follow)" : "")).Def("s");
        if (userDrop) d.Drop("s").A(K.Write, "s.r.*");
        c.N("s = move $v").Assign("s").A(K.Move, "$v").Flow("$v", "s");
        c.N("inspect(s)").Use("s");
        return userDrop ? c.Error("destroy old s (drop writes self.r@follow)") : c.Valid();
    }

    // 24a with the destruction and the store at one point: the new value is in transit from the dying $v.
    static Case DropMerged()
    {
        var c = new Case("24c", "24a with the old value's drop and the store at one point");
        c.N("var x = 1").Def("x");
        c.N("var s = S(r: x@uniq)").Def("s").Borrow("L1", "x", Uniq, "s.r");
        c.N("$t = s.r@follow@uniq").Def("$t").Borrow("L2", "s.r.*", Uniq, "$t");
        c.N("$v = S(r: $t)").Def("$v").Flow("$t", "$v.r");
        c.N("s = move $v (drops old s)").Assign("s").Drop("s").A(K.Write, "s.r.*").A(K.Move, "$v").Flow("$v", "s");
        c.N("inspect(s)").Use("s");
        return c.Error("s = move $v (drops old s)");
    }

    // ---- Round 6 (review r2) ----
    // f1: `x = f(x@ref)`, f retains its input in its result; x's Type needs no drop (no Drop statement in MIR).
    static Case SelfStore(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "dead" => "f1 dead store: `x = f(x@ref)`, x unused afterwards",
            "enum" => "f1 enum: `var x: E = .A(n: 42); x = pin(x@ref); match x .B(let r) => inspect(r@follow)`",
            _ => "f1: `x = f(x@ref)` (retains; no drop); `inspect(x)`",
        });
        c.N(kind == "enum" ? "var x: E = .A(n: 42)" : "var x = make()").Def("x");
        c.N("$a = x@ref").Def("$a").Borrow("Lx", "x", Ref, "$a");
        c.N(kind == "enum" ? "$r = pin($a)" : "$r = f($a)").Def("$r").Flow("$a", "$r");
        c.N("x = move $r").Assign("x").A(K.Move, "$r").Flow("$r", "x");
        if (kind == "enum") { c.N("match x .B(let r)").Def("r").A(K.Read, "x").Flow("x", "r"); c.N("inspect(r@follow)").A(K.Read, "r.*"); }
        else if (kind != "dead") c.N("inspect(x)").Use("x");
        return kind == "dead" ? c.Valid() : c.Error("x = move $r");
    }

    // f2: S {r: uniq/Array<i32>} whose drop clears self.r. 26a: `replace(s@uniq, with: …)`; 26b: `arr.clear()` on Array<S>.
    // The destroying call is a destroy access of its target, deep and exclusive (the Type observes), before `ended`.
    static Case DestroyingCall(string id, bool clear)
    {
        var c = new Case(id, clear ? "f2: `c = arr[0].r@follow@uniq; arr.clear()` (Array<S>, S's drop writes self.r); `inspect(c@follow)`"
                                   : "f2: `c = s.r@follow[0]@uniq; replace(s@uniq, with: S(r: y@uniq))` (old s's drop clears self.r); `inspect(c@follow)`");
        c.N("var x = [1, 2]; var y = [3]").Def("x", "y");
        if (clear)
        {
            c.N("var arr: Array<S> = [S(r: x@uniq)]").Def("arr").Borrow("L1", "x", Uniq, "arr");
            c.N("let c = arr[0].r@follow@uniq").Def("c").Borrow("L2", "arr.[].r.*", Uniq, "c");
            c.N("$t = arr (clear receiver, reserved; destroying)").Def("$t").Borrow("Lt", "arr", Uniq, "$t", reserve: true, inPlace: true);
            c.N("arr.clear()").Activate("Lt").Drop("arr.[]").A(K.Write, "arr.[].r.*");
            c.N("inspect(c@follow)").A(K.Read, "c.*");
            return c.Error("arr.clear()");
        }
        c.N("var s = S(r: x@uniq)").Def("s").Borrow("L1", "x", Uniq, "s.r");
        c.N("let c = s.r@follow[0]@uniq").Def("c").Borrow("L2", "s.r.*.[]", Uniq, "c");
        c.N("$t = s@uniq (reserved, replace target)").Def("$t").Borrow("Lt", "s", Uniq, "$t", reserve: true, inPlace: true);
        c.N("$v = S(r: y@uniq)").Def("$v").Borrow("Ly", "y", Uniq, "$v.r");
        c.N("replace($t, with: $v)").Activate("Lt").Def("s").Drop("s").A(K.Write, "s.r.*").Flow("$v", "s");
        c.N("inspect(c@follow)").A(K.Read, "c.*");
        return c.Error("replace($t, with: $v)");
    }

    // f3 (valid): S {r: ref/i32} has a drop; a shared reborrow through the shared field outlives s.
    static Case SharedField()
    {
        var c = new Case("27", "f3: `var v = other@ref; do: let s = S(r: x@ref); v = s.r@follow@ref` (S has a drop); `inspect(v)`");
        c.N("var x = 1; var other = 2").Def("x", "other");
        c.N("var v = other@ref").Def("v").Borrow("Lo", "other", Ref, "v");
        c.N("  let s = S(r: x@ref)").Def("s").Borrow("Lx", "x", Ref, "s.r");
        c.N("  v = s.r@follow@ref").Assign("v").Borrow("L2", "s.r.*", Ref, "v");
        c.N("  (end of do: drop s)").Drop("s").Def("s");
        c.N("inspect(v)").Use("v");
        return c.Valid();
    }

    // f9: arguments of one call. An exclusive argument borrow is a reservation (§15.6.7); 28d is a stored borrow.
    static Case Args(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "uniqref" => "f9: `g(a@uniq, a@ref)`",
            "refref" => "f9: `g(a@ref, a@ref)`",
            "fields" => "f9: `g(a.x@uniq, a.y@ref)`",
            _ => "f9: `let r = a@uniq; g(r, a@ref)` (r stored, not a reservation)",
        });
        c.N("var a = A(x: 1, y: 2)").Def("a");
        switch (kind)
        {
            case "uniqref":
                c.N("$1 = a@uniq (reserved)").Def("$1").Borrow("L1", "a", Uniq, "$1", reserve: true);
                c.N("$2 = a@ref").Def("$2").Borrow("L2", "a", Ref, "$2");
                c.N("g($1, $2)").Activate("L1").Use("$1", "$2");
                return c.Error("g($1, $2)");
            case "refref":
                c.N("$1 = a@ref").Def("$1").Borrow("L1", "a", Ref, "$1");
                c.N("$2 = a@ref").Def("$2").Borrow("L2", "a", Ref, "$2");
                c.N("g($1, $2)").Use("$1", "$2");
                return c.Valid();
            case "fields":
                c.N("$1 = a.x@uniq (reserved)").Def("$1").Borrow("L1", "a.x", Uniq, "$1", reserve: true);
                c.N("$2 = a.y@ref").Def("$2").Borrow("L2", "a.y", Ref, "$2");
                c.N("g($1, $2)").Activate("L1").Use("$1", "$2");
                return c.Valid();
            default:
                c.N("let r = a@uniq").Def("r").Borrow("Lr", "a", Uniq, "r");
                c.N("$2 = a@ref").Def("$2").Borrow("L2", "a", Ref, "$2");
                c.N("g(r, $2)").Use("r", "$2");
                return c.Error("$2 = a@ref");
        }
    }

    // ---- 10: call reservations (§15.6.7) ----
    static Case Replace()
    {
        var c = new Case("10a", "`Kimi.Intrinsics.replace(p@uniq, with: p + 1)`");
        c.N("var p: i32 = 1").Def("p");
        c.N("$t = p@uniq (reserved)").Def("$t").Borrow("Lp", "p", Uniq, "$t", reserve: true);
        c.N("$w = p + 1").Def("$w").A(K.Read, "p");
        c.N("replace($t, with: $w)").Activate("Lp").Use("$t").Through("$t.*", "$w");
        return c.Valid();
    }

    static Case AppendRemove()
    {
        var c = new Case("10b", "`tasks.append(tasks.remove(0))`");
        c.N("var tasks: Array<Task> = []").Def("tasks");
        c.N("$ra = tasks (append receiver, reserved)").Def("$ra").Borrow("La", "tasks", Uniq, "$ra", reserve: true);
        c.N("$rr = tasks (remove receiver, reserved)").Def("$rr").Borrow("Lr", "tasks", Uniq, "$rr", reserve: true);
        c.N("$v = remove($rr, 0)").Activate("Lr").Use("$rr").Def("$v");
        c.N("append($ra, $v)").Activate("La").Use("$ra", "$v");
        return c.Error("$v = remove($rr, 0)");
    }

    static Case Insert()
    {
        var c = new Case("10c", "`tasks.insert(tasks.length, Task.init(9))`");
        c.N("var tasks: Array<Task> = []").Def("tasks");
        c.N("$r = tasks (insert receiver, reserved)").Def("$r").Borrow("Li", "tasks", Uniq, "$r", reserve: true);
        c.N("$n = tasks.length").Def("$n").A(K.Read, "tasks");
        c.N("$task = Task.init(9)").Def("$task");
        c.N("insert($r, $n, $task)").Activate("Li").Use("$r", "$n", "$task");
        return c.Valid();
    }

    static Case Swap()
    {
        var c = new Case("10d", "`Kimi.Intrinsics.swap(p@uniq, p@uniq)` (§15.7.1)");
        c.N("var p: i32 = 0").Def("p");
        c.N("$a = p@uniq (reserved)").Def("$a").Borrow("La", "p", Uniq, "$a", reserve: true);
        c.N("$b = p@uniq (reserved)").Def("$b").Borrow("Lb", "p", Uniq, "$b", reserve: true);
        c.N("swap($a, $b)").Activate("La").Activate("Lb").Use("$a", "$b");
        return c.Error("swap($a, $b)");
    }

    // ---- 11: unreachable code (§14.10.3) ----
    static Case Unreachable()
    {
        var c = new Case("11", "Loan conflict after `return` (assumed path)");
        c.N("var x = 1").Def("x");
        c.N("let r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("return 0").Goto("exit").Assume("x = 3");
        c.N("x = 3").Assign("x");
        c.N("inspect(r)").Use("r").Goto("exit");
        c.N("exit").Goto();
        return c.Error("x = 3");
    }

    static Case UnreachableJoin()
    {
        var c = new Case("11b", "two assumed predecessors disagree (union reading; intersection would accept)");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = y@uniq").Def("r").Borrow("Ly", "y", Uniq, "r");
        c.N("if c").Goto("  r = x@uniq", "  r = y@uniq");
        c.N("  r = x@uniq").Assign("r").Borrow("Lx", "x", Uniq, "r");
        c.N("  return 0").Goto("exit").Assume("x = 3");
        c.N("  r = y@uniq").Assign("r").Borrow("Ly2", "y", Uniq, "r");
        c.N("  return 1").Goto("exit").Assume("x = 3");
        c.N("x = 3").Assign("x");
        c.N("inspect(r)").Use("r").Goto("exit");
        c.N("exit").Goto();
        return c.Error("x = 3");
    }
}
