#:property Nullable=disable
// Unit 2x-P12 (A5 step 2; package 2b-D §3 and §5, DECISIONS R4-M89; PLAN G124, G174): an executable model of the per-declaration component
// summary that Owned and Transferable share (SPEC 15.2.3, 15.2.4, 15.3.5), in its own file. Record: ComponentSummaryPrototype.md (this folder).
//   Types    prim; var (a Type formal or an abstract Type of the context); proj (a base and segments, a segment a word of `(C).A` steps,
//            starred in a family); layer (a concrete Semantics, a pair binder, or outer(X): the outer Semantics of a plain formal or
//            projection X); nom (a declaration with Type and Origin arguments); tuple; [N of T]; fn (a common Function Type); item(...)
//            (a Function Item's bound arguments or a Closure's captures); dyn C (a runtime Contract View Target); target(X) (the pair
//            target of a plain formal X).
//   Summary  §3.2: (ExclT, ExclO, Error, Obl) from one walk of a declaration's storage in its formals; the least fixed point over all
//            declarations from (no, no, no, ∅). A nominal component adds its bits, walks its arguments and maps its Obl through
//            [formals ↦ arguments]. Obl holds Transferable(P) for a projection P (the only kind Owned reads) and Layer(S, X); Layer(s, X)
//            keeps only X's outer core (core:sealed, core:open). The direct walk checks SPEC 8.7.3's Object Target.
//   Decided  the owner's option 2 (2026-10-21): an obligation that would name outer(X) or target(X) (a pair parameter instantiated with
//            the declaration's own plain formal or projection; every peel) is not recorded, and the declaration is the site of a located
//            U for Transferable; a projection family is F5 (a)'s located U for both intrinsics. An argument that D's premises prove
//            ObjectPayload or Sealed splits exactly first (review R2-1). Order P < U < L < R < E: a site's U is raised only when it decides
//            the verdict (review N3-1).
//            Option 1 (the X-2 candidate) keeps such obligations as outer(X)/target(X) with peel templates ∀m. O[$h ↦ target^(j·m)(Y)].
//   Use      Owned(D<A,o>) = ¬ExclO ∧ o = static ∧ Owned(A) ∧ Proj[A]; Transferable(D<A>) = ¬ExclT ∧ Transferable(A) ∧ Obl[A]; Error
//            absorbs. Abstract Types by premises: exact assumption, Contract refinement, associated-Type requirements (SPEC 8.3.2).
//   Columns  SPEC (written by hand from SPEC), Package (§3.2's wording; it shares the core reduction), Option1, Decided (the expected
//            verdict in parentheses where option 2 answers the located U). Each other variant changes one rule of Decided. Reference:
//            SPEC's components by an instantiation BFS, on random declaration graphs; it shares the leaf rules, the substitution and the
//            declarations' Error bit with the model, so it checks the summary and the fixed point, not the leaves.
// Run, in an empty folder outside the repository: dotnet run ComponentSummaryPrototype.cs [-- -random N] [-- -seed S]. Exit code: Decided's
// failures (cases, anchors, conformances, random disagreements).
using System.Text.RegularExpressions;

return Run.Go(args);

// Conjunction is the maximum: Error absorbs, then Refuted, L, Unknown. L: a located Unsupported_Kd for a summary the decided rules leave
// unsupported (F5 (a), option 2), raised only when it decides the verdict: an exact refutation elsewhere wins (review N3-1); its answer is
// Derived, never a Language error. NT: the fixed point did not converge.
enum V { P, U, L, R, E, NT }

static class Vx
{
    public static V And(this V a, V b) => (V)Math.Max((int)a, (int)b);
    public static V All(this IEnumerable<V> vs) => vs.Aggregate(V.P, And);
}

sealed class Seg(string[] w, bool star)
{
    public readonly string[] W = w; public readonly bool Star = star;
    public override string ToString()
    {
        var s = string.Concat(W.Select(x => ".(" + x[..x.IndexOf('.')] + ")" + x[x.IndexOf('.')..]));
        return Star ? "{" + s + "}*" : s;
    }
}

sealed class Sem
{
    public static readonly string[] Nine = ["owner", "ref", "uniq", "obj", "objref", "objuniq", "rc", "arc", "raw"];
    public string N; public bool F; public Ty Of; // F: a pair binder; Of: the outer Semantics of a plain formal or projection
    public static Sem C(string n) => new() { N = n };
    public bool Abstract => F || Of != null;
    public override string ToString() => Of != null ? $"outer({Of})" : N;
}

sealed class Ty
{
    public string K, N; public Ty[] A = []; public string[] O = []; public Sem S; public Seg[] G = [];
    string key;
    public static Ty Prim(string n) => new() { K = "prim", N = n };
    public static Ty Var(string n) => new() { K = "var", N = n };
    public static Ty Nom(string n, Ty[] a, string[] o = null) => new() { K = "nom", N = n, A = a, O = o ?? [] };
    public static Ty Lay(Sem s, Ty x, string o = null) => new() { K = "layer", S = s, A = [x], O = o == null ? [] : [o] };
    public static Ty Proj(Ty b, IEnumerable<Seg> g)
    {
        var segs = new List<Seg>();
        if (b.K == "proj") { segs.AddRange(b.G); b = b.A[0]; }
        foreach (var s in g)
            if (!s.Star && segs.Count > 0 && !segs[^1].Star) segs[^1] = new Seg([.. segs[^1].W, .. s.W], false);
            else segs.Add(s);
        return segs.Count == 0 ? b : new Ty { K = "proj", A = [b], G = [.. segs] };
    }
    public bool Starred => G.Any(g => g.Star);
    int size = -1; // nodes of the term (shared subterms counted again), saturating; bounds the reference walk
    public int Size => size >= 0 ? size : size = (int)Math.Min(1_000_000L, 1L + A.Sum(a => (long)a.Size) + (S?.Of?.Size ?? 0));
    public override bool Equals(object o) => o is Ty t && t.ToString() == ToString();
    public override int GetHashCode() => ToString().GetHashCode();
    public override string ToString() => key ??= K switch
    {
        "prim" or "var" => N,
        "view" => "dyn " + N,
        "core" => $"core:{N}",
        "target" => $"target({A[0]})",
        "fn" => "fn" + Os(),
        "item" => $"item({Js(A)})",
        "tup" => $"({Js(A)})",
        "arr" => $"[{N} of {A[0]}]",
        "layer" => $"{S}/{(A[0].K == "layer" ? $"({A[0]})" : A[0].ToString())}" + (O.Length > 0 ? " during " + O[0] : ""),
        "nom" => N + (A.Length > 0 ? $"<{Js(A)}>" : "") + Os(),
        _ => (A[0].K is "var" or "nom" ? A[0].ToString() : $"({A[0]})") + string.Concat(G.Select(g => g.ToString())),
    };
    string Os() => O.Length > 0 ? "{" + string.Join(", ", O) + "}" : "";
    static string Js(Ty[] a) => string.Join(", ", a.Select(x => x.ToString()));
}

sealed class Parser
{
    readonly Prog p; readonly List<string> t; int i;
    Parser(Prog p, string s) { this.p = p; t = Regex.Matches(s, @"[A-Za-z_][A-Za-z0-9_]*|\d+|\S").Select(m => m.Value).ToList(); }
    public static Ty Parse(Prog p, string s)
    {
        var x = new Parser(p, s); var r = x.Type();
        return x.i == x.t.Count ? r : throw new Exception("trailing input in: " + s);
    }
    string Pk(int k = 0) => i + k < t.Count ? t[i + k] : "";
    void Eat(string s) { if (Pk() != s) throw new Exception($"expected '{s}' at {i} in: {string.Join(" ", t)}"); i++; }
    Ty Type()
    {
        if (Regex.IsMatch(Pk(), "^[A-Za-z_]") && Pk(1) == "/")
        {
            var s = t[i]; i += 2; var x = Type(); string o = null;
            if (Pk() == "during") { i++; o = t[i++]; }
            return Ty.Lay(Sem.Nine.Contains(s) ? Sem.C(s) : new Sem { N = s, F = true }, x, o);
        }
        var a = Atom();
        while (true)
            if (Pk() == "." && Pk(1) == "(") { i += 2; var c = t[i++]; Eat(")"); Eat("."); a = Ty.Proj(a, [new Seg([c + "." + t[i++]], false)]); }
            else if (Pk() == "?") { i++; a = Ty.Nom("Option", [a]); }
            else return a;
    }
    List<Ty> Types(string close) { var r = new List<Ty>(); while (Pk() != close) { r.Add(Type()); if (Pk() == ",") i++; } i++; return r; }
    List<string> Names(string close) { var r = new List<string>(); while (Pk() != close) { r.Add(t[i++]); if (Pk() == ",") i++; } i++; return r; }
    Ty Atom()
    {
        var w = t[i++];
        switch (w)
        {
            case "(": var items = Types(")"); return items.Count == 0 ? Ty.Prim("Unit") : items.Count == 1 ? items[0] : new Ty { K = "tup", A = [.. items] };
            case "[": var n = t[i++]; Eat("of"); var e = Type(); Eat("]"); return new Ty { K = "arr", N = n, A = [e] };
            case "fn": string[] fo = []; if (Pk() == "{") { i++; fo = [.. Names("}")]; } return new Ty { K = "fn", O = fo };
            case "item": Eat("("); return new Ty { K = "item", A = [.. Types(")")] };
            case "dyn": return new Ty { K = "view", N = t[i++] };
        }
        Ty[] args = []; string[] os = [];
        if (Pk() == "<") { i++; args = [.. Types(">")]; }
        if (Pk() == "{") { i++; os = [.. Names("}")]; }
        return Prog.Prims.Contains(w) ? Ty.Prim(w) : p.D.ContainsKey(w) ? Ty.Nom(w, args, os) : Ty.Var(w);
    }
}

// Premises: facts `X is C` / `X is not C` and the admitted sets of pair binders (SPEC 8.3.4).
sealed class Scope
{
    static readonly Dictionary<string, string[]> Cat = new() { ["object"] = ["obj", "rc", "arc"], ["borrow"] = ["ref", "uniq", "objref", "objuniq"] };
    public List<(Ty X, string C, bool Neg)> Facts = new(); public Dictionary<string, string[]> Adm = new(); public string Text = "";
    public static Scope Parse(Prog p, string text)
    {
        var s = new Scope { Text = text ?? "" };
        foreach (var part in s.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = Regex.Match(part, @"^(.*?)\s+is\s+(not\s+)?(.*)$"); var rhs = m.Groups[3].Value.Trim(); var words = rhs.Split('|');
            if (words.All(w => Sem.Nine.Contains(w) || Cat.ContainsKey(w)))
                s.Adm[m.Groups[1].Value.Trim()] = words.SelectMany(w => Cat.TryGetValue(w, out var c) ? c : [w]).Distinct().ToArray();
            else
                foreach (var c in rhs.Split(" and ")) s.Facts.Add((Parser.Parse(p, m.Groups[1].Value), c.Trim(), m.Groups[2].Success));
        }
        return s;
    }
    public Scope Plus(Scope o) => new() { Facts = [.. Facts, .. o.Facts], Adm = Adm.Concat(o.Adm).GroupBy(k => k.Key).ToDictionary(g => g.Key, g => g.Last().Value), Text = Text + "; " + o.Text };
}

sealed class Decl
{
    public string Name, Text; public List<(string S, string T)> Ps = new(); public string[] Slots = []; public Scope Prem = new(); public List<Ty> Store = new();
    public bool Open, Exempt, Internal, OwnedLeaf, Invalid, Claims; // Internal: storage only the standard iterators instantiate
    public int Index(string formal) => Ps.FindIndex(p => p.T == formal);
    public string PairTarget(string s) => Ps.FirstOrDefault(p => p.S == s).T;
}

sealed class Contract { public string Name; public List<string> Parents = new(); public List<(string C, string A, string X)> Sup = new(); }

sealed class Conf { public string Decl, Contract, Text, WhenText; public string[] Ps, AssocText; public Scope When; public Dictionary<string, Ty> Assoc = new(); public bool Valid; }

sealed class Map { public Dictionary<string, Ty> T = new(); public Dictionary<string, Sem> S = new(); public Dictionary<string, string> O = new(); }

sealed class Prog
{
    public static readonly HashSet<string> Prims = ["i32", "isize", "string", "bool", "Unit", "Never"];
    static readonly string[] Intrinsic = ["Transferable", "Owned", "Sealed", "ObjectPayload"];
    public readonly Dictionary<string, Decl> D = new(); public readonly Dictionary<string, Contract> C = new(); public readonly List<Conf> F = new();
    readonly List<(Decl D, string[] Store, string Where)> pending = new();
    static string[] Split(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static Decl Head(string head, out string where)
    {
        var m = Regex.Match(head, @"^((?:open|exempt|internal|ownedleaf|claims|enum)\s+)*(\w+)(?:<([^>]*)>)?(?:\{([^}]*)\})?(?:\s+where\s+(.*))?$");
        var d = new Decl { Name = m.Groups[2].Value, Text = head };
        foreach (Capture c in m.Groups[1].Captures) { var f = c.Value.Trim(); d.Open |= f == "open"; d.Exempt |= f == "exempt"; d.Internal |= f == "internal"; d.OwnedLeaf |= f == "ownedleaf"; d.Claims |= f == "claims"; }
        foreach (var x in Split(m.Groups[3].Value)) d.Ps.Add(x.Contains('/') ? (x[..x.IndexOf('/')], x[(x.IndexOf('/') + 1)..]) : (null, x));
        d.Slots = Split(m.Groups[4].Value); where = m.Groups[5].Value;
        return d;
    }
    public Prog Decl(string head, params string[] store) { var d = Head(head, out var w); D[d.Name] = d; pending.Add((d, store, w)); return this; }
    public Prog Contract(string head, params string[] sup)
    {
        var parts = head.Split(':', 2); var k = new Contract { Name = parts[0].Trim() };
        if (parts.Length > 1) k.Parents.AddRange(Split(parts[1]));
        foreach (var s in sup) { var m = Regex.Match(s, @"^(\w+)\.(\w+) is (.*)$"); foreach (var x in m.Groups[3].Value.Split(" and ")) k.Sup.Add((m.Groups[1].Value, m.Groups[2].Value, x.Trim())); }
        C[k.Name] = k; return this;
    }
    public Prog Conf(string head, string when, params string[] assoc)
    {
        var m = Regex.Match(head, @"^(\w+)(?:<([^>]*)>)?:\s*(\S+)$");
        F.Add(new Conf { Decl = m.Groups[1].Value, Ps = Split(m.Groups[2].Value), Contract = m.Groups[3].Value, WhenText = when, AssocText = assoc, Text = head + (when.Length > 0 ? " when " + when : "") });
        return this;
    }
    public Prog Done()
    {
        foreach (var (d, store, where) in pending)
        {
            d.Store = store.Select(s => Parser.Parse(this, s)).ToList(); d.Prem = Scope.Parse(this, where);
            d.Invalid = d.Store.Any(s => Free(s).Any(v => d.Index(v) < 0)); // an unresolved name in storage: the Field is invalid
        }
        foreach (var f in F) { f.When = Scope.Parse(this, f.WhenText); foreach (var a in f.AssocText) { var kv = a.Split('=', 2); f.Assoc[kv[0].Trim()] = Parser.Parse(this, kv[1]); } }
        // SPEC 8.3.3: a conformance never uses itself as evidence; the least fixed point from "none verified".
        for (var changed = true; changed;) { changed = false; foreach (var f in F.Where(f => !f.Valid)) if (Verify(f)) f.Valid = changed = true; }
        return this;
    }
    public static IEnumerable<string> Free(Ty t) => t.K == "var" ? [t.N] : t.A.SelectMany(Free);
    bool Verify(Conf f)
    {
        foreach (var k in Close([f.Contract]))
            if (C.TryGetValue(k, out var c))
                foreach (var (_, a, x) in c.Sup)
                    if (!f.Assoc.TryGetValue(a, out var b) || !Intrinsic.Contains(x) && Is(b, x, f.When, null, f) != V.P) return false;
        return true;
    }

    // Contract knowledge (SPEC 8.3.2): refinement closure, and the associated-Type requirements a known Contract supplies for a step.
    public HashSet<string> Close(IEnumerable<string> cs)
    {
        var s = new HashSet<string>(); var st = new Stack<string>(cs);
        while (st.Count > 0) { var c = st.Pop(); if (s.Add(c) && C.TryGetValue(c, out var k)) foreach (var x in k.Parents) st.Push(x); }
        return s;
    }
    public HashSet<string> Step(HashSet<string> k, string step)
    {
        var (c, a) = (step[..step.IndexOf('.')], step[(step.IndexOf('.') + 1)..]);
        return k == null || !k.Contains(c) ? null : Close(k.SelectMany(x => C.TryGetValue(x, out var y) ? y.Sup.Where(f => f.C == c && f.A == a).Select(f => f.X) : []));
    }
    public HashSet<string> Word(HashSet<string> k, string[] w) => w.Aggregate(k, Step);
    public HashSet<string> K(Ty x, Scope sc) // x: a var, target(var) or a plain projection of one; null: an invalid projection
    {
        var direct = sc.Facts.Where(f => !f.Neg && f.X.Equals(x)).Select(f => f.C);
        if (x.K == "target") return OwnerKnown(x.A[0], sc) ? K(x.A[0], sc) : Close(direct);
        if (x.K != "proj") return Close(direct);
        var w = x.G[^1].W; var k = Step(K(Ty.Proj(x.A[0], [.. x.G[..^1], .. w.Length > 1 ? new[] { new Seg(w[..^1], false) } : []]), sc), w[^1]);
        return k == null ? null : Close(k.Concat(direct));
    }
    public V Abs(string i, Ty x, Scope sc)
    {
        var k = K(x, sc); if (k == null) return V.E;
        bool pos = k.Contains(i), neg = sc.Facts.Any(f => f.Neg && f.C == i && f.X.Equals(x));
        return pos && neg ? V.E : pos ? V.P : neg ? V.R : V.U;
    }
    public static bool Abstract(Ty x) => x.K is "var" or "target" || x.K == "proj" && Abstract(x.A[0]);
    public bool OwnerKnown(Ty x, Scope sc) => Abstract(x) ? !x.Starred && K(x, sc) is { } k && (k.Contains("ObjectPayload") || k.Contains("Sealed")) : x.K != "layer";
    public V Is(Ty x, string c, Scope sc, Engine e, Conf excl = null)
    {
        if (c is "Transferable" or "Owned") return e?.J(c, x, sc) ?? V.U;
        if (c == "Sealed") return SealedOf(x, sc, e);
        if (Abstract(x)) return Abs(c, x, sc);
        if (c == "ObjectPayload") return x.K == "layer" ? V.R : V.P;
        return x.K == "nom" && F.Any(f => f != excl && f.Valid && f.Decl == x.N && Close([f.Contract]).Contains(c) && Holds(f, x, sc, e)) ? V.P : V.U;
    }
    bool Holds(Conf f, Ty x, Scope sc, Engine e) { var m = Bind(f, x); return f.When.Facts.All(w => Is(Subst(w.X, m), w.C, sc, e) == V.P); }
    static Map Bind(Conf f, Ty x) { var m = new Map(); for (var j = 0; j < f.Ps.Length; j++) m.T[f.Ps[j]] = x.A[j]; return m; }

    // SPEC 8.7.2: owner Semantics and a Core that is valid, not Never and not an open struct; only the outer Core is tested.
    public V SealedOf(Ty x, Scope sc, Engine e) => x.K switch
    {
        "prim" => x.N == "Never" ? V.R : V.P,
        "nom" => D[x.N].Invalid ? V.E : D[x.N].Open ? V.R : V.P,
        "layer" => x.S.N == "owner" ? SealedOf(x.A[0], sc, e) : V.R,
        "view" => V.R,
        "core" => x.N == "sealed" ? V.P : x.N == "open" ? V.R : V.E,
        "target" => OwnerKnown(x.A[0], sc) ? SealedOf(x.A[0], sc, e) : V.U,
        "var" => Abs("Sealed", x, sc),
        "proj" => Abstract(x.A[0]) ? Abs("Sealed", x, sc) : Resolve(x, sc, e) is { } rs ? rs.Select(r => SealedOf(r, sc, e)).All() : V.E,
        _ => V.P,
    };

    // A projection on a concrete base resolves through a verified conformance; a starred segment unrolls (a budget guards the walk).
    public List<Ty> Resolve(Ty x, Scope sc, Engine e, int budget = 6)
    {
        var b = x.A[0]; var g = x.G[0]; var rest = x.G[1..];
        if (g.Star)
        {
            if (budget == 0) return null;
            var skip = Resolve1(Ty.Proj(b, rest), sc, e, budget); var once = Resolve1(Ty.Proj(b, [new Seg(g.W, false), g, .. rest]), sc, e, budget - 1);
            return skip == null || once == null ? null : [.. skip, .. once];
        }
        var next = ResolveStep(b, g.W[0], sc, e);
        return next == null ? null : Resolve1(Ty.Proj(next, [.. g.W.Length > 1 ? new[] { new Seg(g.W[1..], false) } : [], .. rest]), sc, e, budget);
    }
    List<Ty> Resolve1(Ty y, Scope sc, Engine e, int budget) => y.K == "proj" && !Abstract(y.A[0]) ? Resolve(y, sc, e, budget) : [y];
    Ty ResolveStep(Ty b, string step, Scope sc, Engine e)
    {
        if (b.K != "nom") return null;
        var (c, a) = (step[..step.IndexOf('.')], step[(step.IndexOf('.') + 1)..]);
        var f = F.FirstOrDefault(f => f.Valid && f.Decl == b.N && Close([f.Contract]).Contains(c) && f.Assoc.ContainsKey(a) && Holds(f, b, sc, e));
        return f == null ? null : Subst(f.Assoc[a], Bind(f, b));
    }

    // Substitution [formals ↦ arguments]. A pair parameter <s/T> takes one complete Type W: s ↦ W's outer Semantics, T ↦ W's target
    // (SPEC 8.1.1); for a plain formal or projection W both stay symbolic: outer(W), target(W).
    public Map Bind(Decl e, Ty t)
    {
        var m = new Map();
        for (var j = 0; j < e.Ps.Count; j++) { var w = t.A[j]; var (s, x) = e.Ps[j]; if (s == null) m.T[x] = w; else { m.S[s] = Outer(w); m.T[x] = Target(w); } }
        for (var k = 0; k < e.Slots.Length; k++) m.O[e.Slots[k]] = k < t.O.Length ? t.O[k] : e.Slots[k];
        return m;
    }
    // At use a context's abstract Type is never substituted again, so target(target(X)) carries the same knowledge as target(X)
    // (no premise can name either); judgments may identify them. Never applied to a summary, whose formals are still substituted.
    public static Ty Collapse(Ty t)
    {
        if (t.K == "target" && t.A[0].K == "target") return Collapse(t.A[0]);
        var a = t.A.Select(Collapse).ToArray(); var s = t.S is { Of: { } of } ? new Sem { Of = Collapse(of) } : t.S;
        return a.SequenceEqual(t.A) && s == t.S ? t : new Ty { K = t.K, N = t.N, A = a, O = t.O, S = s, G = t.G };
    }
    public static Sem Outer(Ty w) => w.K == "layer" ? w.S : Abstract(w) ? new Sem { Of = w } : Sem.C("owner");
    public static Ty Target(Ty w) => w.K == "layer" ? w.A[0] : Abstract(w) ? new Ty { K = "target", A = [w] } : w;
    public Ty Subst(Ty t, Map m) => t.K switch
    {
        "var" => m.T.TryGetValue(t.N, out var r) ? r : t,
        "proj" => Ty.Proj(Subst(t.A[0], m), t.G),
        "target" => Target(Subst(t.A[0], m)),
        "layer" => Ty.Lay(SubstSem(t.S, m), Subst(t.A[0], m), t.O.Length > 0 ? MapO(t.O[0], m) : null),
        "nom" => Ty.Nom(t.N, [.. t.A.Select(a => Subst(a, m))], [.. t.O.Select(o => MapO(o, m))]),
        "fn" => new Ty { K = "fn", O = [.. t.O.Select(o => MapO(o, m))] },
        _ => t.A.Length == 0 ? t : new Ty { K = t.K, N = t.N, A = [.. t.A.Select(a => Subst(a, m))] },
    };
    public Sem SubstSem(Sem s, Map m) => s.Of != null ? Outer(Subst(s.Of, m)) : s.F && m.S.TryGetValue(s.N, out var r) ? r : s;
    static string MapO(string o, Map m) => m.O.TryGetValue(o, out var r) ? r : o;

    // Family patterns: words with each star unrolled 0..n times; membership of a word.
    public static List<string[]> Words(Seg[] g, int n = 2)
    {
        var r = new List<string[]> { Array.Empty<string>() };
        foreach (var s in g) r = r.SelectMany(w => s.Star ? Enumerable.Range(0, n + 1).Select(k => w.Concat(Enumerable.Repeat(s.W, k).SelectMany(x => x)).ToArray()) : new[] { w.Concat(s.W).ToArray() }).ToList();
        return r;
    }
    public static bool Match(string[] w, Seg[] g, int i = 0, int j = 0)
    {
        if (j == g.Length) return i == w.Length;
        var s = g[j]; var word = i + s.W.Length <= w.Length && s.W.SequenceEqual(w[i..(i + s.W.Length)]);
        return s.Star ? Match(w, g, i, j + 1) || word && Match(w, g, i + s.W.Length, j) : word && Match(w, g, i + s.W.Length, j + 1);
    }
    // b covers a when a's words are among b's (families); when a is b's obligation on the pair target of b's subject (an obligation on
    // target(Y) is implied by the same obligation on Y: the target is a component of Y, and a Sealed Y is owner, so target(Y) = Y); or
    // when b is a peel template ∀k. O[$h ↦ target^k(Y)] and a is one of its instances, or the same template on target^j(Y).
    public bool Covered(Ob a, Ob b)
    {
        if (a.Key == b.Key) return true;
        if (b.Hole != null)
        {
            // Pointwise, with $h shared: a template on a deeper hole whose body b's body covers; a plain obligation one of b's instances
            // covers. Coverage is monotone through the layer rule and the admitted-set combination, so it holds under outer($h) too.
            if (a.Hole != null) return Depth(a.Hole, b.Hole) is var d && d >= 0 && d % b.Step == 0 && a.Step % b.Step == 0 && Covered(Body(a), Body(b));
            var h = b.Hole;
            for (var k = 0; k <= a.Key.Split("target(").Length; k++) { if (Covered(a, Inst(b, h))) return true; var n = Up(h, b.Step); if (n.Equals(h)) break; h = n; }
            return false;
        }
        return a.Hole == null && (a.Same(b) && Words(a.X.G).All(w => Match(w, b.X.G))
            || a.Kind == b.Kind && $"{a.S}" == $"{b.S}" && a.X.K == "target" && (a.X.A[0].Equals(b.X) || Covered(new Ob { Kind = a.Kind, S = a.S, X = a.X.A[0] }, b)));
    }
    static int Depth(Ty y, Ty top) => y.Equals(top) ? 0 : y.K == "target" && Depth(y.A[0], top) is var d && d >= 0 ? d + 1 : -1; // y = target^d(top)
    public static Ty Up(Ty h, int j) { for (var i = 0; i < j; i++) h = Target(h); return h; }
    public Ob Inst(Ob t, Ty h) => Shift(t, "$h", h);
    static Ob Body(Ob t) => new() { Kind = t.Kind, S = t.S, X = t.X };
    public int Level(Ob a, Ob t) // the m at which a is the template t's instance (hole target^(m·step)), or -1
    {
        var h = t.Hole;
        for (var k = 0; k <= a.Key.Split("target(").Length; k++) { if (Inst(t, h).Key == a.Key) return k; var n = Up(h, t.Step); if (n.Equals(h)) break; h = n; }
        return -1;
    }
    public Ob Shift(Ob t, string v, Ty to) { var m = new Map(); m.T[v] = to; return new Ob { Kind = t.Kind, S = t.S == null ? null : SubstSem(t.S, m), X = Subst(t.X, m) }; }
}

sealed class Ob // Transferable(P) for a projection P ("proj"), or Layer(S, X) ("layer"); with Hole, the peel template ∀m. this[$h ↦ target^(m·Step)(Hole)]
{
    public string Kind; public Sem S; public Ty X; public Ty Hole; public int Step = 1;
    public string Body => Kind + "|" + S + "|" + X;
    public string Key => (Hole == null ? "" : $"∀{Hole}/{Step}:") + Body;
    public Ob Map(Prog p, Map m) => new() { Kind = Kind, S = S == null ? null : p.SubstSem(S, m), X = p.Subst(X, m), Hole = Hole == null ? null : p.Subst(Hole, m), Step = Step };
    public bool Same(Ob o) => Hole == null && o.Hole == null && Kind == o.Kind && $"{S}" == $"{o.S}" && X.K == "proj" && o.X.K == "proj" && X.A[0].Equals(o.X.A[0]);
    public override string ToString() => (Hole == null ? "" : "∀m. ") + (Kind == "proj" ? $"T({X})" : $"Layer({S}, {X})") + (Hole == null ? "" : $" with $h = target^{(Step == 1 ? "m" : $"{Step}m")}({Hole})");
}

sealed class Sum
{
    public bool XT, XO, Err, PairOwned; public Dictionary<string, Ob> Obl = new(); public SortedSet<string> FamU = new(), PairU = new();
    public bool Same(Sum o) => XT == o.XT && XO == o.XO && Err == o.Err && PairOwned == o.PairOwned && Obl.Keys.ToHashSet().SetEquals(o.Obl.Keys)
        && FamU.SetEquals(o.FamU) && PairU.SetEquals(o.PairU);
}

// Family: prefix = the package's (Tᵢ, p, q*) "Tᵢ.p.qᵏ", formed when a new path extends a held one; suffix = "Tᵢ.Q*.s", formed when a
// path mapped through an edge of a growing SCC ends with a held pattern; U = A's fallback (a located U at the declaration, answers
// Error); plain = no families (with IgnoreDiv: a bounded enumeration, the oracle Bounded8). Layer: obl (the package's layer
// obligation), A (formals add nothing), B (B's ViewTarget and Layer(s)). Walk: summary, or an instantiation walk (guard, capU, capP).
sealed record Rules(string Name, bool Outer = true, bool AllAdmitted = true, bool TargetAlways = true, string Family = "suffix", string Layer = "obl",
    bool TwoValued = false, string Walk = "summary", int Lim = 4, bool BodyAnchor = false, int Cap = 24, bool IgnoreDiv = false, bool Internal = true,
    bool PairU = false, bool Core = true, string Note = "")
{
    public bool Exempt(Decl d) => d.Exempt || d.Internal && Internal;
}

sealed class Engine
{
    const string T = "Transferable", O = "Owned";
    public readonly Prog P; public readonly Rules R; public readonly Dictionary<string, Sum> S = new();
    public int Rounds; public readonly HashSet<string> Div = new(), FamU = new(); readonly HashSet<string> growing = new(), growingDecl = new(); bool direct;
    public readonly SortedSet<string> Raised = new(); public int Reentries; // FamilyU: the declarations whose located U a judgment raised by reading the family
    public Engine(Prog p, Rules r) { P = p; R = r; Links(); Solve(); }

    // The projection-link graph: (D, i) -r-> (E, j) when D's storage names E with argument j = formal i extended by projection steps r,
    // or with argument j = the pair target i passed as a whole (a peel). An SCC holding a projection step grows only through a recursive
    // Contract (SPEC 8.3.3); one holding a peel peels a layer per level.
    void Links()
    {
        var edges = new List<(string From, string To, string Grows, string D, string E)>(); // Grows: "proj", "peel" or ""
        void Scan(Decl d, Ty t)
        {
            if (t.K == "nom" && P.D.TryGetValue(t.N, out var e))
                for (var j = 0; j < t.A.Length; j++)
                {
                    var a = t.A[j].K == "layer" ? t.A[j].A[0] : t.A[j]; var b = a.K == "proj" ? a.A[0] : a;
                    var peel = a == t.A[j] && d.Ps.Any(x => x.S != null && x.T == b.N); // the pair target T of <s/T> as a whole argument: one layer peeled
                    if (b.K == "var" && d.Index(b.N) >= 0) edges.Add(($"{d.Name}#{d.Index(b.N)}", $"{e.Name}#{j}", a.K == "proj" ? "proj" : peel ? "peel" : "", d.Name, e.Name));
                }
            foreach (var c in t.A) Scan(d, c);
        }
        foreach (var d in P.D.Values) foreach (var t in d.Store) Scan(d, t);
        var reach = new Dictionary<string, HashSet<string>>();
        foreach (var n in edges.SelectMany(e => new[] { e.From, e.To }).Distinct())
        {
            var seen = new HashSet<string>(); var st = new Stack<string>([n]);
            while (st.Count > 0) { var cur = st.Pop(); foreach (var x in edges.Where(x => x.From == cur)) if (seen.Add(x.To)) st.Push(x.To); }
            reach[n] = seen;
        }
        bool Same(string a, string b) => reach[a].Contains(b) && reach[b].Contains(a);
        foreach (var g in edges.Where(g => Same(g.From, g.To) && edges.Any(x => x.Grows != "" && Same(x.From, g.From) && Same(x.To, g.From))))
        {
            growing.Add(g.D + ">" + g.E); growingDecl.Add(g.E);
            if (R.Family == "U" && edges.Any(x => x.Grows == "proj" && Same(x.From, g.From) && Same(x.To, g.From))) { FamU.Add(g.D); FamU.Add(g.E); }
        }
    }

    void Solve()
    {
        foreach (var d in P.D.Values) S[d.Name] = new Sum();
        for (Rounds = 1; ; Rounds++)
        {
            var changed = new List<string>();
            foreach (var d in P.D.Values) { var n = Compute(d); if (!n.Same(S[d.Name])) { S[d.Name] = n; changed.Add(d.Name); } }
            if (changed.Count == 0) break;
            if (Rounds == R.Cap) { Div.UnionWith(changed); break; }
        }
        // A declaration whose summary reads a non-converged one answers from a truncated summary: it is not converged either.
        static IEnumerable<string> Names(Ty t) => (t.K == "nom" ? [t.N] : Array.Empty<string>()).Concat(t.A.SelectMany(Names));
        for (var grew = Div.Count > 0; grew; )
            grew = P.D.Values.Where(d => !Div.Contains(d.Name) && d.Store.SelectMany(Names).Any(Div.Contains)).Select(d => Div.Add(d.Name)).ToList().Count > 0;
    }

    Sum Compute(Decl d)
    {
        var prev = S[d.Name]; var a = new Sum { XT = prev.XT, XO = prev.XO || d.OwnedLeaf, Err = prev.Err || d.Invalid, PairOwned = prev.PairOwned };
        a.FamU.UnionWith(prev.FamU); if (FamU.Contains(d.Name)) a.FamU.Add(d.Name); a.PairU.UnionWith(prev.PairU);
        foreach (var o in prev.Obl) a.Obl[o.Key] = o.Value; // seeding with the previous round keeps the iteration monotone
        var q = new List<(Ob O, bool C)>(); var seen = new HashSet<string>();
        direct = true; // D's own storage text: its object forms are checked for an Object Target
        foreach (var t in d.Store) Walk(d, t, R.Exempt(d), a, q, false);
        direct = false;
        for (var work = 0; q.Count > 0; )
        {
            if ((work += q.Count) > 2000) { Div.Add(d.Name); break; } // a walk that keeps growing inside one step: reported, never capped silently
            var batch = q.Where(x => seen.Add(x.O.Key + x.C)).OrderBy(x => x.O.Hole != null).ThenBy(x => x.O.X.Starred).ThenBy(x => x.O.Key.Length).ToList(); q.Clear();
            foreach (var (o, c) in batch)
                if (o.Hole != null) Template(d, o, a, q, c);
                else if (o.Kind == "proj") Walk(d, o.X, false, a, q, c); else Layer(d, o.S, o.X, false, a, q, c);
        }
        foreach (var o in a.Obl.Values.ToList())
            if (a.Obl.Values.Any(b => b != o && P.Covered(o, b) && !(P.Covered(b, o) && string.CompareOrdinal(b.Key, o.Key) > 0))) a.Obl.Remove(o.Key);
        return a;
    }

    // A peel template mapped onto a hole with known layers: its instances while the hole has layers, then the template on the rest.
    void Template(Decl d, Ob o, Sum a, List<(Ob, bool)> q, bool cyc)
    {
        for (var h = o.Hole; ; )
        {
            if (Prog.Abstract(h)) { Add(a, new Ob { Kind = o.Kind, S = o.S, X = o.S != null && o.S.Abstract && R.Core ? Core(d, o.X) : o.X, Hole = h, Step = o.Step }, cyc); return; }
            var i = P.Inst(o, h);
            if (i.Kind == "proj") Walk(d, i.X, false, a, q, cyc); else Layer(d, i.S, i.X, false, a, q, cyc);
            var n = Prog.Up(h, o.Step); if (n.Equals(h)) return; h = n;
        }
    }

    // Layer(s, X) = Layer(s, core(X)) ∧ Transferable(X) when X's outer core is known: the layer rule reads X only through its Sealed proof
    // (the outer core alone, SPEC 8.7.2) and its Transferable verdict, which the walk requires as a component (of the layer, or of the
    // nominal's arguments for a mapped obligation). Keeping only the core stops composite Types growing inside obligations.
    Ty Core(Decl d, Ty x) => Prog.Abstract(x) || x.K is "view" or "core" ? x : P.SealedOf(x, d.Prem, this) switch
    {
        V.P => new Ty { K = "core", N = "sealed" }, V.R => new Ty { K = "core", N = "open" }, V.E => new Ty { K = "core", N = "error" }, _ => x,
    };

    static string[] Adm(Decl d, string s) => d.Prem.Adm.TryGetValue(s, out var a) ? a : Sem.Nine;
    static bool ObjectFamily(string n) => n is "obj" or "objref" or "objuniq" or "rc" or "arc";
    // SPEC 8.7.3's Object Target: ObjectPayload proven, a runtime Contract View Target, or a pair target with pair evidence.
    bool ObjectTarget(Decl d, Ty x)
    {
        if (x.K == "view") return true;
        if (!Prog.Abstract(x)) return x.K != "layer" && !(x.K == "prim" && x.N == "Never");
        return P.Is(x, "ObjectPayload", d.Prem, this) == V.P || x.K == "var" && d.Ps.Any(p => p.S != null && p.T == x.N && Adm(d, p.S).All(ObjectFamily));
    }

    void Walk(Decl d, Ty t, bool ex, Sum a, List<(Ob, bool)> q, bool cyc)
    {
        switch (t.K)
        {
            case "prim": case "target": return;
            case "var": if (d.Index(t.N) < 0) a.Err = true; return; // a formal adds nothing: it is required as a Type argument
            case "fn": case "view": a.XT = true; return;
            case "proj":
                if (Prog.Abstract(t.A[0])) Add(a, new Ob { Kind = "proj", X = t }, cyc);
                else if (P.Resolve(t, d.Prem, this) is { } rs) foreach (var r in rs) Walk(d, r, ex, a, q, cyc);
                else a.Err = true;
                return;
            case "layer": Layer(d, t.S, t.A[0], ex, a, q, cyc); return;
            case "nom":
                var e = P.D[t.N]; var s = S[e.Name];
                a.Err |= s.Err; a.XT |= s.XT; a.XO |= s.XO; a.FamU.UnionWith(s.FamU); a.PairU.UnionWith(s.PairU); a.PairOwned |= s.PairOwned;
                foreach (var x in t.A) Walk(d, x, false, a, q, cyc);
                if (FamU.Contains(e.Name)) return; // F5 (a): a projection family is unsupported, read on demand; its obligations are not mapped
                // On a growing path when the edge grows, or when E itself lies on a growing cycle (E's obligations, mapped through a target
                // that names E again, deepen by one step per walk).
                var m = P.Bind(e, t); var c = growing.Contains(d.Name + ">" + e.Name) || growingDecl.Contains(e.Name);
                // R2-1: an abstract argument that D's premises prove ObjectPayload or Sealed is an owner Type (8.7.3, 8.7.2), so a pair
                // parameter splits it exactly: s ↦ owner, T ↦ W. Only a split no premise decides is left to option 2's check.
                for (var j = 0; j < e.Ps.Count && j < t.A.Length; j++)
                    if (e.Ps[j].S != null && Prog.Abstract(t.A[j]) && P.OwnerKnown(t.A[j], d.Prem)) { m.S[e.Ps[j].S] = Sem.C("owner"); m.T[e.Ps[j].T] = t.A[j]; }
                foreach (var o in s.Obl.Values)
                {
                    var u = o.Map(P, m);
                    // Option 2 (owner, 2026-10-21): an obligation that would name outer(X) or target(X) (E's pair parameter instantiated with
                    // D's own plain formal or projection, which every peel does) is not recorded; D's summary records itself as the site
                    // of a located U for Transferable (and for Owned too if the obligation is a projection's, which Owned reads).
                    if (R.PairU && (u.Key.Contains("outer(") || u.Key.Contains("target(")))
                    { a.PairU.Add(d.Name); a.PairOwned |= u.Kind == "proj"; continue; }
                    q.Add((u, c));
                }
                return;
            default: foreach (var x in t.A) Walk(d, x, ex, a, q, cyc); return; // tuple, array, item
        }
    }

    // §3.2's leaf rule in the walk; Layer(S, X) for a Semantics formal (or outer(...)) and for an object layer over a formal or projection.
    void Layer(Decl d, Sem s, Ty x, bool ex, Sum a, List<(Ob, bool)> q, bool cyc)
    {
        var own = s.F && x.K == "var" && d.PairTarget(s.N) == x.N; // s/T for the pair's own T is the argument W itself
        if (direct && (!s.Abstract && ObjectFamily(s.N) || s.F && !own && Adm(d, s.N).Any(ObjectFamily)) && !ObjectTarget(d, x)) a.Err = true; // SPEC 8.7.3, 8.1.1
        if (s.Abstract)
        {
            if (!own && R.Layer != "A" && (R.Outer || s.Of == null)) Add(a, new Ob { Kind = "layer", S = s, X = R.Core ? Core(d, x) : x }, cyc);
        }
        else
            switch (s.N)
            {
                case "rc": a.XT = true; break;
                case "raw": if (!ex) a.XT = true; break;
                case "obj": case "objref": case "objuniq": case "arc":
                    if (x.K == "view") { a.XT = true; return; }
                    if (R.Layer == "B" && s.N == "arc") break;
                    if (Prog.Abstract(x)) { if (R.Layer != "A") Add(a, new Ob { Kind = "layer", S = s, X = x }, cyc); break; }
                    var sealedV = P.SealedOf(x, d.Prem, this); a.XT |= sealedV == V.R; a.Err |= sealedV == V.E;
                    break;
            }
        // The target stays a component (OwnedOrigins: referent, payload or View Target). An instance k ≥ 1 of a held peel template needs no
        // walk: its target is the k-shift of instance 0's, and mapping commutes with the shift, so it yields only further instances.
        if (!a.Obl.Values.Any(b => b.Hole != null && P.Level(new Ob { Kind = "layer", S = s, X = x }, b) >= 1)) Walk(d, x, ex, a, q, cyc);
    }

    void Add(Sum a, Ob o, bool cyc)
    {
        if (a.Obl.Values.Any(b => P.Covered(o, b))) return;
        if (cyc && o.Hole == null && R.Family != "plain")
            // A peel cycle (a pair target passed back as a whole argument): when o is a held b with one formal v shifted to target^j(v)
            // (j peels per round trip), keep the template ∀m. b[v ↦ target^(j·m)(v)], which covers b, o and every later level.
            foreach (var b in a.Obl.Values.Where(b => b.Hole == null).ToList())
            {
                var hit = Prog.Free(b.X).Concat(b.S?.Of != null ? Prog.Free(b.S.Of) : []).Distinct().Where(v => v != "$h")
                    .SelectMany(v => Enumerable.Range(1, 4).Select(j => (v, j))).FirstOrDefault(x => P.Shift(b, x.v, Prog.Up(Ty.Var(x.v), x.j)).Key == o.Key);
                if (hit.v == null) continue;
                a.Obl.Remove(b.Key); var t = P.Shift(b, hit.v, Ty.Var("$h")); o = new Ob { Kind = t.Kind, S = t.S, X = t.X, Hole = Ty.Var(hit.v), Step = hit.j };
                break;
            }
        if (cyc && o.X.K == "proj")
        {
            static List<string> Tok(Seg[] g) => g.SelectMany(s => s.Star ? new[] { s.ToString() } : s.W).ToList();
            var ta = Tok(o.X.G);
            foreach (var b in a.Obl.Values.Where(b => b.Same(o)))
            {
                var tb = Tok(b.X.G);
                if (R.Family == "prefix" && !o.X.Starred && !b.X.Starred && tb.Count < ta.Count && ta.Take(tb.Count).SequenceEqual(tb))
                { o = new Ob { Kind = o.Kind, S = o.S, X = Ty.Proj(o.X.A[0], [new Seg([.. tb], false), new Seg([.. ta.Skip(tb.Count)], true)]) }; break; }
                if (R.Family == "suffix" && tb.Count < ta.Count && ta.Skip(ta.Count - tb.Count).SequenceEqual(tb) && ta.Take(ta.Count - tb.Count).All(x => !x.StartsWith('{')))
                { o = new Ob { Kind = o.Kind, S = o.S, X = Ty.Proj(o.X.A[0], [new Seg([.. ta.Take(ta.Count - tb.Count)], true), .. b.X.G]) }; break; }
            }
        }
        a.Obl[o.Key] = o;
    }

    // ---- Use: the verdicts at a complete Type under a scope's premises.
    static V Orig(string o) => o == null || o == "static" ? V.P : V.U; // an unproven Origin gives Unknown (SPEC 15.2.3)
    readonly SortedSet<string> pending = new();
    public V Eval(string i, Ty t, Scope sc)
    {
        pending.Clear();
        var v = R.Walk == "summary" ? J(i, t, sc) : Expand(i, t, sc, R.Walk, R.Lim, out _);
        if (v == V.L) Raised.UnionWith(pending); // the sites whose U decided this verdict
        return v;
    }
    public V J(string i, Ty t, Scope sc)
    {
        var tr = i == T;
        switch (t.K)
        {
            case "prim": return V.P;
            case "fn": return tr ? V.R : t.O.Select(Orig).All();
            case "view": return tr ? V.R : V.P;
            case "core": return V.P; // the outer core of a Type whose components the summary already requires
            case "var": return P.Abs(i, t, sc);
            case "target": var v = J(i, t.A[0], sc); return P.OwnerKnown(t.A[0], sc) || v == V.P ? v : V.U; // a component of the formal's argument
            case "proj":
                if (Prog.Abstract(t.A[0])) return t.Starred ? Pattern([i], t, sc) : P.Abs(i, t, sc);
                return P.Resolve(t, sc, this) is { } rs ? rs.Select(r => J(i, r, sc)).All() : V.E;
            case "layer": return tr ? Leaf(t.S, t.A[0], sc) : Orig(t.O.FirstOrDefault()).And(t.A[0].K == "view" ? V.P : J(i, t.A[0], sc));
            case "nom": return Nom(i, t, sc);
            default: return t.A.Select(a => J(i, a, sc)).All();
        }
    }

    // A use-site judgment that meets itself again (a pair binder applied to a Type built from its own declaration) contributes no
    // exclusion there: the least fixed point from "no exclusion" (SPEC 15.3.5), with nothing cached from inside the cycle.
    readonly HashSet<string> judging = new();
    V Nom(string i, Ty t, Scope sc)
    {
        t = Prog.Collapse(t); var key = $"{i}|{t}|{sc.Text}";
        if (!judging.Add(key)) { Reentries++; return V.P; }
        try { return NomOnce(i, t, sc); } finally { judging.Remove(key); }
    }

    V NomOnce(string i, Ty t, Scope sc)
    {
        var tr = i == T; var e = P.D[t.N]; var s = S[e.Name];
        if (Div.Contains(e.Name) && !R.IgnoreDiv) return V.NT;
        var v = s.Err ? V.E : (tr ? s.XT : s.XO) ? V.R : V.P;
        if (!tr) v = v.And(t.O.Select(Orig).All());
        var m = P.Bind(e, t);
        foreach (var a in t.A) v = v.And(J(i, a, sc));
        if (s.FamU.Count > 0) { pending.UnionWith(s.FamU); v = v.And(V.L); } // F5 (a): raised by the judgment it decides, once per declaration
        if (tr ? s.PairU.Count > 0 : s.PairOwned) { pending.UnionWith(s.PairU); v = v.And(V.L); } // option 2: Transferable only (R2-1's argument)
        foreach (var o in s.Obl.Values)
        {
            if (!tr && o.Kind != "proj") continue;
            foreach (var u in Instances(o.Map(P, m)))
                v = v.And(u.Kind == "proj" ? J(i, u.X, sc) : R.Layer == "B" && o.S.Abstract ? (u.S.N is "rc" or "raw" ? V.R : V.P) : Leaf(u.S, u.X, sc));
        }
        return v;
    }

    // A template's instances at use: target^k of the hole until it stops changing (a concrete W has finitely many layers), or two steps
    // past the first abstract hole (target^k(Y) for k ≥ 1 carries no premise of its own: SPEC has no syntax for one).
    IEnumerable<Ob> Instances(Ob o)
    {
        if (o.Hole == null) { yield return o; yield break; }
        var h = o.Hole; var abstractSteps = 0;
        for (var k = 0; k < 64; k++)
        {
            yield return P.Inst(o, h);
            if (Prog.Abstract(h) && ++abstractSteps > 2) yield break;
            var n = Prog.Up(h, o.Step); if (n.Equals(h)) yield break; h = n;
        }
    }

    // Transferable of s/X (SPEC 15.2.4, 3.3.1, 8.7.3): rc and raw refute; an object layer refutes over a non-Sealed target or a runtime
    // Contract View and requires the target; a Semantics formal is judged on its admitted set (SPEC 8.3.4).
    public V Leaf(Sem s, Ty x, Scope sc)
    {
        if (s.Abstract)
        {
            var set = s.F ? (sc.Adm.TryGetValue(s.N, out var a) ? a : Sem.Nine) : P.OwnerKnown(s.Of, sc) ? ["owner"] : Sem.Nine;
            var vs = set.Select(n => Leaf(Sem.C(n), x, sc)).ToList();
            if (vs.Contains(V.NT)) return V.NT;
            if (vs.Contains(V.E)) return V.E;
            if (vs.Contains(V.L) && !(vs.Contains(V.U) || vs.Contains(V.P) && vs.Contains(V.R))) return V.L; // the known cases do not decide
            if (vs.All(v => v is V.R or V.L)) return V.R;
            return R.AllAdmitted ? (vs.All(v => v == V.P) ? V.P : V.U) : (vs.Contains(V.R) ? V.U : V.P); // the package: "Proven when none does"
        }
        switch (s.N)
        {
            case "owner": case "ref": case "uniq": return J(T, x, sc);
            case "rc": case "raw": return V.R;
        }
        if (x.K == "view") return V.R;
        if (R.Layer == "B") return s.N == "arc" ? J(T, x, sc) : P.SealedOf(x, sc, this) == V.P ? J(T, x, sc) : V.R;
        if (x.K == "proj" && x.Starred && Prog.Abstract(x.A[0])) return Pattern(["Sealed", T], x, sc);
        var sealedV = P.SealedOf(x, sc, this);
        if (R.TwoValued && sealedV == V.U) sealedV = V.R;
        return R.TargetAlways ? sealedV.And(J(T, x, sc)) : sealedV == V.P ? J(T, x, sc) : sealedV;
    }

    // A family at an abstract base. prefix: B's inductive rule for (Tᵢ, p, q*): some C' known for Tᵢ.p refines the requirements and
    // supplies Self.q is C'. Otherwise the Contract-knowledge states reachable through the segments (finite: sets of Contracts).
    V Pattern(string[] reqs, Ty x, Scope sc)
    {
        var b = x.A[0];
        if (R.Family == "prefix" && x.G.Length == 2 && !x.G[0].Star && x.G[1].Star)
        {
            var kp = P.K(Ty.Proj(b, [x.G[0]]), sc); if (kp == null) return V.E;
            return kp.Any(c => reqs.All(r => P.Close([c]).Contains(r)) && (P.Word(P.Close([c]), x.G[1].W)?.Contains(c) ?? false)) ? V.P : V.U;
        }
        var states = new List<(HashSet<string> K, Ty Exact)> { (P.K(b, sc), b) };
        foreach (var g in x.G)
        {
            if (!g.Star) states = states.Select(st => st.Exact != null ? (P.K(Ty.Proj(st.Exact, [g]), sc), Ty.Proj(st.Exact, [g])) : (P.Word(st.K, g.W), (Ty)null)).ToList();
            else
            {
                var all = states.ToList(); var keys = new HashSet<string>(all.Select(st => st.Exact + "|" + (st.K == null ? "!" : string.Join(",", st.K.Order()))));
                for (var i = 0; i < all.Count; i++)
                {
                    if (all[i].K == null) continue;
                    var n = (P.Word(all[i].K, g.W), (Ty)null);
                    if (keys.Add("|" + (n.Item1 == null ? "!" : string.Join(",", n.Item1.Order())))) all.Add(n);
                }
                states = all;
            }
            if (states.Any(st => st.K == null)) return V.E;
        }
        return states.Select(st => reqs.All(r => st.K.Contains(r)) ? V.P : V.U).All();
    }

    // ---- Instantiation walks: a BFS over instantiated components (SPEC's definition, least fixed point from "no exclusion" as
    // reachability). ref: cut at nominal depth lim (the reference); capU / capP: a depth cap answering U / P; guard: today's growth
    // guard (Binding.Capabilities.cs RequestCapability: Unknown at the second growth step of one declaration along the consumer chain).
    sealed class Node(Ty t, bool ex, int depth, Node par) { public Ty T = t; public bool Ex = ex; public int Depth = depth; public Node Par = par; }
    static int Depth(Ty t) => t.K == "proj" ? Depth(t.A[0]) + t.G.Sum(g => g.W.Length) : 1 + t.A.Select(Depth).DefaultIfEmpty(0).Max(); // a projection step nests
    static bool Grows(Node n)
    {
        var t = n.T; var growingNow = false; var depth = 0;
        for (var a = n.Par; a != null; a = a.Par)
            if (a.T.K == "nom" && a.T.N == t.N && !a.T.Equals(t))
            {
                depth = depth == 0 ? Depth(t) : depth; var prev = Depth(a.T);
                if (depth > prev) { if (growingNow) return true; growingNow = true; depth = prev; }
            }
        return false;
    }
    public V Expand(string i, Ty root, Scope sc, string mode, int lim, out bool cut)
    {
        var tr = i == T; var acc = V.P; cut = false;
        var seen = new HashSet<string>(); var q = new Queue<Node>();
        var tooBig = false;
        void Push(Ty t, bool ex, int depth, Node par) { if (t.Size > 2000) tooBig = true; else if (seen.Add(t + "|" + ex)) q.Enqueue(new Node(t, ex, depth, par)); }
        Push(root, false, 0, null);
        while (q.Count > 0)
        {
            if (seen.Count > 4000 || tooBig) return V.NT; // a walk that keeps instantiating, or an instance too large to print
            var n = q.Dequeue(); var t = n.T;
            switch (t.K)
            {
                case "prim": break;
                case "fn": acc = acc.And(tr ? V.R : t.O.Select(Orig).All()); break;
                case "view": acc = acc.And(tr ? V.R : V.P); break;
                case "var": case "target": acc = acc.And(J(i, t, sc)); break;
                case "proj":
                    if (Prog.Abstract(t.A[0])) { acc = acc.And(J(i, t, sc)); break; }
                    if (P.Resolve(t, sc, this) is { } rs) foreach (var r in rs) Push(r, n.Ex, n.Depth, n); else acc = acc.And(V.E);
                    break;
                case "layer":
                    var x = t.A[0];
                    if (t.S.Abstract) { acc = acc.And(J(i, t, sc)); break; }
                    if (!tr) { acc = acc.And(Orig(t.O.FirstOrDefault())); if (x.K != "view") Push(x, n.Ex, n.Depth, n); break; }
                    switch (t.S.N)
                    {
                        case "rc": acc = acc.And(V.R); Push(x, n.Ex, n.Depth, n); break; // the target stays a component: Error absorbs
                        case "raw": if (!n.Ex) acc = acc.And(V.R); Push(x, n.Ex, n.Depth, n); break;
                        case "owner": case "ref": case "uniq": Push(x, n.Ex, n.Depth, n); break;
                        default: if (x.K == "view") acc = acc.And(V.R); else { acc = acc.And(P.SealedOf(x, sc, this)); Push(x, n.Ex, n.Depth, n); } break;
                    }
                    break;
                case "nom":
                    var e = P.D[t.N];
                    if (e.Invalid || S[e.Name].Err) { acc = acc.And(V.E); break; } // an invalid or ill-formed declaration (8.7.3) is Error, as Binding reports it
                    if (!tr) { if (e.OwnedLeaf) acc = acc.And(V.R); acc = acc.And(t.O.Select(Orig).All()); }
                    var d = n.Depth + 1;
                    if (mode == "ref" && d > lim) { cut = true; break; }
                    if (mode is "capU" or "capP" && d > lim) { acc = acc.And(mode == "capU" ? V.U : V.P); break; }
                    if (mode == "guard" && Grows(n)) { acc = acc.And(V.U); break; }
                    var m = P.Bind(e, t);
                    foreach (var a in t.A) Push(a, false, d, n);
                    foreach (var s in e.Store) Push(P.Subst(s, m), R.Exempt(e), d, n);
                    break;
                default: foreach (var a in t.A) Push(a, n.Ex, n.Depth, n); break;
            }
        }
        return acc;
    }
}

static class Cases
{
    public static Prog Prelude(Prog p) => p
        .Decl("enum Option<T>", "T")
        .Decl("exempt Array<T>", "raw/T", "isize")
        .Decl("exempt Dictionary<K, V>", "raw/(K, V)")
        .Decl("exempt Slice<T>{source}", "ref/T during source", "raw/T")
        .Decl("internal IterCursor<T>", "raw/T", "isize") // library storage that only the standard iterators instantiate
        .Decl("exempt ArrayIterator<T>", "IterCursor<T>")
        .Decl("Weak<s/T> where s is rc|arc", "s/T") // counts as storing an S (SPEC 3.2.2, 15.3.5)
        .Decl("Channel<T>", "Option<T>")
        .Decl("Sender<T>", "raw/Channel<T>") // Async.Sender's internal state: not exempt
        .Decl("ownedleaf FixedBuffer", "raw/i32") // verified raw storage: TryLeafCapability refutes Owned
        .Decl("Item", "i32")
        .Decl("open Base", "i32")
        .Decl("Box<T>", "T")
        .Decl("open OBox<T>", "T");

    public static Prog Program() => Prelude(new Prog())
        .Decl("Holder", "rc/Item").Decl("RawHolder", "raw/i32").Decl("Phantom<T>").Decl("Inner<T>", "i32").Decl("enum Msg", "i32", "rc/Item")
        .Decl("Hook", "Option<obj/Hook>", "fn").Decl("ChainS", "Option<obj/ChainS>", "i32").Decl("claims Claim", "rc/Item")
        .Decl("Broken", "Missing").Decl("UsesBroken<T>", "Broken", "T")
        .Decl("Node<T>{a}", "ref/T during a", "Option<rc/Node<T>{a}>")
        .Decl("Linked<T>", "T", "Option<rc/Linked<T>>")
        .Decl("Nest<T>", "T", "Array<Nest<(T, T)>>")
        .Decl("Nest15<T>", "T", "Option<rc/Nest15<(T, T)>>")
        .Decl("Chain<T>", "obj/Chain<(T, T)>")
        .Decl("NodeFix<T>", "obj/NodeFix<(i32, i32)>")
        .Decl("Ping<T>", "Option<obj/Pong<(T, i32)>>").Decl("Pong<T>", "Option<obj/Ping<Option<T>>>", "T")
        .Decl("D1<T>", "D2<(T, T)>").Decl("D2<T>", "D3<(T, T)>").Decl("D3<T>", "D4<(T, T)>").Decl("D4<T>", "D5<(T, T)>").Decl("D5<T>", "D6<(T, T)>").Decl("D6<T>", "T", "rc/Item")
        .Decl("open Opn<T>", "T").Decl("Grow<T> where T is ObjectPayload", "obj/T", "Option<obj/Grow<Opn<T>>>")
        .Decl("HoldObj<T> where T is ObjectPayload", "obj/T")
        .Decl("HoldRef<T>{a} where T is ObjectPayload", "objref/T during a")
        .Decl("HoldUniq<T>{a} where T is ObjectPayload", "objuniq/T during a")
        .Decl("HoldArc<T> where T is ObjectPayload", "arc/T")
        .Decl("HoldP<s/T> where s is object", "arc/T")
        .Decl("SemHold<s/T>{a} where s is ref|obj", "s/Base during a")
        .Decl("SemAny<s/T>{a}", "s/Base during a")
        .Decl("Fwd<T>{a}", "SemAny<T>{a}")
        .Decl("Peel<s/T>{a}", "s/Base during a", "Option<obj/Peel<T>{a}>")
        .Decl("Fwd2<T>{a} where T is Has", "SemAny<T.(Has).Out>{a}") // the review's k1-k8
        .Decl("PA<s/T>{a}", "s/Base during a", "Option<obj/PB<T>{a}>").Decl("PB<U>{a}", "PA<U>{a}")
        .Decl("Peel3<s/T>{a}", "s/OBox<T> during a", "Option<obj/Peel3<T>{a}>")
        .Decl("Q0<s/T>{a}", "s/Base during a", "Option<obj/Q1<T>{a}>").Decl("Q1<s/T>{a}", "Option<obj/Q2<T>{a}>").Decl("Q2<s/T>{a}", "Option<obj/Q3<T>{a}>") // review round 2
        .Decl("Q3<s/T>{a}", "Option<obj/Q4<T>{a}>").Decl("Q4<s/T>{a}", "Option<obj/Q0<T>{a}>")
        .Decl("IX<s/T, r/U>{a}", "s/Base during a", "Option<obj/IX<U, T>{a}>")
        .Decl("RB<s/T>{a}", "Array<s/OBox<RB<T>{a}> during a>")
        .Decl("PS<s/T>{a}", "s/SemAny<T>{a} during a", "Option<obj/PS<T>{a}>")
        .Decl("G2<s/T, U>{a}", "s/OBox<U> during a", "Option<obj/G2<s/T, Box<U>>{a}>") // a pair binder over a composite that grows: no outer, no peel
        .Decl("EP<s/T>{a} where T is Has", "T.(Has).Out").Decl("DP<X>{a} where X is Has and ObjectPayload", "EP<X>{a}") // review round 3
        .Decl("TB<s/T, r/U>{a} where U is ObjectPayload", "s/U during a", "Option<obj/TB<s/T, r/Box<U>>{a}>")
        .Decl("Cfg", "i32").Decl("Leaf", "i32").Decl("Wrap<X>", "X").Decl("Pair2<X>", "X", "X")
        .Decl("PHold<T> where T is Has", "T.(Has).Out")
        .Decl("TreeNode<T> where T is Tree", "T", "Array<TreeNode<T.(Tree).Child>>")
        .Decl("LFam<T> where T is OTree", "obj/T.(Tree).Child", "Option<obj/LFam<T.(Tree).Child>>")
        .Decl("LFamBad<T> where T is Tree", "obj/T.(Tree).Child", "Option<obj/LFamBad<T.(Tree).Child>>")
        .Decl("Walk<T> where T is Seq", "T.(Seq).Elem", "Option<obj/Walk<T.(Seq).Next>>")
        .Decl("Even<T> where T is Tree", "Option<obj/Odd<T.(Tree).Child>>").Decl("Odd<T> where T is Tree", "Option<obj/Even<T.(Tree).Child>>", "T")
        .Decl("Fold<T> where T is Has", "T.(Has).Out", "Option<obj/Fold<Pair2<T>>>")
        .Decl("Two<T> where T is Seq", "T.(Seq).Elem", "T.(Seq).Next.(Seq).Elem")
        .Contract("Tree", "Tree.Child is Tree") // SPEC 8.3.3's recursive Contract
        .Contract("TTree: Tree, Transferable, Owned", "Tree.Child is TTree")
        .Contract("OTree: Tree, ObjectPayload", "Tree.Child is OTree")
        .Contract("STree: TTree, OTree, Sealed", "Tree.Child is STree")
        .Contract("Seq", "Seq.Next is Seq")
        .Contract("TSeq: Seq", "Seq.Next is TSeq and Transferable and Owned", "Seq.Elem is Transferable and Owned")
        .Contract("Has").Contract("THas: Has", "Has.Out is Transferable and Owned").Contract("Portable: Transferable")
        .Conf("Leaf: Tree", "", "Child = Leaf").Conf("Wrap<X>: Tree", "X is Tree", "Child = Wrap<Wrap<X>>")
        .Conf("Item: Has", "", "Out = rc/Item").Conf("Cfg: Has", "", "Out = i32").Conf("Pair2<X>: Has", "X is Has", "Out = X.(Has).Out")
        .Conf("Holder: Portable", "").Conf("Item: Portable", "")
        .Done();

    // (id, group, query, context premises, SPEC Owned/Transferable written by hand, the SPEC reason)
    public static readonly (string Id, string Group, string Q, string Ctx, string Spec, string Why)[] All = [
        ("c1", "7.2", "i32", "", "P/P", "primitive"),
        ("c2", "7.2", "string", "", "P/P", "string is a primitive"),
        ("c3", "7.2", "rc/Item", "", "P/R", "15.2.4: rc refutes"),
        ("c4", "7.2", "ref/(rc/Item) during static", "", "P/R", "rc behind ref"),
        ("c5", "7.2", "ref/i32 during static", "", "P/P", "valid neighbour of c4"),
        ("c6", "7.2", "ref/i32 during local", "", "U/P", "15.2.3: an unproven Origin; 15.2.4 requires no Owned"),
        ("c7", "7.2", "arc/Holder", "", "P/R", "rc behind arc (Holder stores rc/Item)"),
        ("c8", "7.2", "arc/Item", "", "P/P", "Sealed payload"),
        ("c9", "7.2", "Weak<rc/Item>", "", "P/R", "Weak of rc"),
        ("c10", "7.2", "ref/(Weak<rc/Item>) during static", "", "P/R", "Weak of rc behind ref"),
        ("c11", "7.2", "Weak<arc/Item>", "", "P/P", "Weak counts as storing arc/Item; only Weak of rc is listed"),
        ("c12", "7.2", "raw/i32", "", "P/R", "a raw pointer outside the exemption; Owned visits the pointee"),
        ("c13", "7.2", "arc/RawHolder", "", "P/R", "raw behind arc"),
        ("c14", "7.2", "fn", "", "P/R", "common Function Type (g3)"),
        ("c15", "7.2", "item()", "", "P/P", "a Function Item stores nothing"),
        ("c16", "7.2", "item(i32)", "", "P/P", "a concrete Closure's captures"),
        ("c17", "7.2", "item(rc/Item)", "", "P/R", "a capture refutes"),
        ("c18", "7.2", "obj/Base", "", "P/R", "base-object view (g1)"),
        ("c19", "7.2", "obj/dyn Shape", "", "P/R", "runtime-Contract view (g2)"),
        ("c20", "7.2", "obj/Item", "", "P/P", "concrete payload"),
        ("c21", "7.2", "obj/Holder", "", "P/R", "the concrete payload is visited"),
        ("c22", "7.2", "Array<i32>", "", "P/P", "the collections' exemption (g5)"),
        ("c23", "7.2", "Array<rc/Item>", "", "P/R", "an element refutes (g8)"),
        ("c24", "7.2", "Dictionary<i32, rc/Item>", "", "P/R", "a value Type refutes"),
        ("c25", "7.2", "Slice<i32>{static}", "", "P/P", "exempt; a static source"),
        ("c26", "7.2", "Slice<i32>{local}", "", "U/P", "borrows of caller storage qualify"),
        ("c27", "7.2", "ArrayIterator<i32>", "", "P/P", "a standard iterator and its internal storage"),
        ("c28", "7.2", "Sender<i32>", "", "P/R", "Async.Sender's internal state is not exempt"),
        ("c29", "7.2", "[0 of rc/Item]", "", "P/R", "a zero-length array keeps its component"),
        ("c30", "7.2", "Phantom<rc/Item>", "", "P/R", "an unused argument (15.2.3)"),
        ("c31", "7.2", "Phantom<ref/i32 during local>", "", "U/P", "an unused argument's Origin"),
        ("c32", "7.2", "Inner<rc/Item>", "", "P/R", "an inherited argument (6.1.3)"),
        ("c33", "7.2", "Msg", "", "P/R", "an inactive Case payload"),
        ("c34", "7.2", "Hook", "", "P/R", "a cycle hides nothing (g10)"),
        ("c35", "7.2", "ChainS", "", "P/P", "a cycle ends (g11)"),
        ("c36", "7.2", "Claim", "", "P/R", "8.7.1: `Self is Transferable` is an error at the clause; structure decides"),
        ("c37", "7.2", "Broken", "", "E/E", "an invalid Field"),
        ("c38", "7.2", "UsesBroken<i32>", "", "E/E", "Error absorbs (8.3.2)"),
        ("c39", "7.2", "FixedBuffer", "", "R/R", "verified raw storage (Owned leaf); raw"),
        ("c40", "7.2", "Box<FixedBuffer>", "", "R/R", "through a Type argument"),
        ("c41", "15.3.5", "Node<i32>{static}", "", "P/R", "SPEC 15.3.5's Node: recursion adds nothing; rc"),
        ("c42", "15.3.5", "Node<i32>{local}", "", "U/R", "an unproven Origin argument"),
        ("u1", "abstract", "U", "", "U/U", "abstract Types need premises (g4)"),
        ("u2", "abstract", "U", "U is Transferable", "U/P", "exact assumption (g9)"),
        ("u3", "abstract", "U", "U is Transferable and Owned", "P/P", ""),
        ("u4", "abstract", "U", "U is not Transferable", "U/R", "an available not P refutes"),
        ("u5", "abstract", "U", "U is my.Transferable", "U/U", "a user Contract named Transferable gains nothing (8.7.1)"),
        ("u6", "abstract", "U", "U is Portable", "U/P", "Contract refinement (contract Portable: Transferable)"),
        ("r1", "recursion", "Nest<i32>", "", "P/P", "the Transferable plan's Nest: non-regular, Proven"),
        ("r2", "recursion", "Nest<rc/Item>", "", "P/R", ""),
        ("r3", "recursion", "Nest15<i32>", "", "P/R", "SPEC 15.3.5's own field rc/Nest<(T, T)>?"),
        ("r4", "recursion", "Linked<i32>", "", "P/R", "rc refutes for every T"),
        ("r5", "recursion", "Chain<i32>", "", "P/P", "G174: SPEC 15.3.5 gives Proven"),
        ("r6", "recursion", "Chain<rc/Item>", "", "P/R", ""),
        ("r7", "recursion", "NodeFix<i32>", "", "P/P", "IntrinsicCapabilityTest's fixed instance"),
        ("r8", "recursion", "Box<Box<i32>>", "", "P/P", "finite nesting"),
        ("r9", "recursion", "Ping<i32>", "", "P/P", "mutual non-regular recursion"),
        ("r10", "recursion", "Ping<rc/Item>", "", "P/R", ""),
        ("r11", "recursion", "D1<i32>", "", "P/R", "rc six declarations deep"),
        ("r12", "recursion", "Grow<Item>", "", "P/R", "Grow<Opn<Item>> stores obj/Opn<Item>, a base-object view"),
        ("l1", "layer", "HoldObj<Base>", "", "P/R", "A-1: obj/Base"),
        ("l2", "layer", "HoldObj<Item>", "", "P/P", ""),
        ("l3", "layer", "HoldRef<Base>{static}", "", "P/R", "A-1: objref/Base"),
        ("l4", "layer", "HoldRef<Item>{static}", "", "P/P", ""),
        ("l5", "layer", "HoldUniq<Base>{static}", "", "P/R", "A-1: objuniq/Base"),
        ("l6", "layer", "HoldUniq<Item>{static}", "", "P/P", ""),
        ("l7", "layer", "HoldArc<Base>", "", "P/R", "B-1: arc/T at an open base"),
        ("l8", "layer", "HoldArc<Item>", "", "P/P", ""),
        ("l9", "layer", "HoldP<obj/dyn Shape>", "", "P/R", "arc/T at a runtime Contract (the argument refutes too)"),
        ("l10", "layer", "HoldP<obj/Item>", "", "P/P", ""),
        ("l11", "layer", "HoldP<obj/Base>", "", "P/R", ""),
        ("l12", "layer", "arc/Base", "", "P/R", "B-1: arc/OpenBase"),
        ("l13", "layer", "arc/dyn Shape", "", "P/R", "B-1: arc over a runtime Contract"),
        ("l14", "layer", "SemHold<ref/Item during static>{static}", "", "P/P", "s/Base with s = ref"),
        ("l15", "layer", "SemHold<obj/Item>{static}", "", "P/R", "B-1: s/U under a formal binder, s = obj"),
        ("l16", "layer", "Fwd<i32>{static}", "", "P/P", "SemAny<i32>: owner/Base"),
        ("l17", "layer", "Fwd<obj/Item>{static}", "", "P/R", "SemAny<obj/Item> stores obj/Base: a pair binder bound to a plain formal"),
        ("l18", "layer", "HoldObj<U>", "U is ObjectPayload and Transferable and Owned", "P/U", "P-2: obj/U without a Sealed premise"),
        ("l19", "layer", "HoldObj<U>", "U is ObjectPayload and Transferable and Owned and Sealed", "P/P", "P-2: with one"),
        ("l20", "layer", "Option<s/U during static>", "s is ref|uniq; U is Transferable and Owned", "P/P", "every admitted case proves"),
        ("l21", "layer", "Option<s/U during static>", "s is ref|obj; U is Transferable and Owned and ObjectPayload", "P/U", "ref proves, obj is unknown (U not Sealed)"),
        ("l22", "layer", "Option<s/U during static>", "s is rc|raw; U is Owned", "P/R", "every admitted case refutes"),
        ("l23", "layer", "Fwd<U>{static}", "U is Transferable and Owned", "P/U", "outer(U) admits all nine"),
        ("l25", "peeling", "Peel<ref/Item during static>{static}", "", "P/P", "Peel<Item> is owner/Base"),
        ("l26", "peeling", "Peel<ref/(obj/Item) during static>{static}", "", "P/R", "the nested Peel<obj/Item> stores obj/Base"),
        ("l27", "peeling", "Peel<U>{static}", "U is Transferable and Owned", "P/U", "every layer of U applied to Base"),
        ("l24", "layer", "obj/U", "U is ObjectPayload and Owned; U is not Transferable", "P/R", "the View Target is a component whatever its Sealed proof"),
        ("p1", "projection", "PHold<U>", "U is Has and Transferable and Owned", "U/U", "U.(Has).Out unproven"),
        ("p2", "projection", "PHold<U>", "U is THas and Transferable and Owned", "P/P", "THas supplies Out's proofs"),
        ("p3", "projection", "PHold<U>", "U is Has and Transferable and Owned; U.(Has).Out is Transferable and Owned", "P/P", "a premise on the projection"),
        ("p4", "projection", "PHold<Item>", "", "P/R", "Item: Has binds Out = rc/Item"),
        ("p5", "projection", "PHold<Cfg>", "", "P/P", "Cfg: Has binds Out = i32"),
        ("p6", "projection", "Fold<U>", "U is THas and Transferable and Owned", "P/P", "Fold<Pair2<T>>: the conditional conformance maps Out back to T.Out"),
        ("p7", "projection", "Fold<Cfg>", "", "P/P", ""),
        ("p8", "projection", "Fold<Item>", "", "P/R", ""),
        ("p9", "projection", "Two<U>", "U is Seq and Transferable and Owned; U.(Seq).Elem is Transferable and Owned; U.(Seq).Next.(Seq).Elem is Transferable and Owned", "P/P", "two direct projections, no cycle"),
        ("f1", "family", "TreeNode<U>", "U is TTree", "P/P", "U.Childᵏ: TTree refines Transferable and Owned and supplies Child is TTree"),
        ("f2", "family", "TreeNode<U>", "U is Tree and Transferable and Owned", "U/U", "U.Child unproven"),
        ("f3", "family", "TreeNode<Leaf>", "", "E/E", "Leaf: Tree is invalid (8.3.3), so the Type is invalid"),
        ("f4", "family", "TreeNode<Wrap<i32>>", "", "E/E", "concrete growth: Wrap<X>: Tree would need itself (8.3.3)"),
        ("f5", "family", "Walk<U>", "U is TSeq and Transferable and Owned", "P/P", "U.Nextᵏ and U.Nextᵏ.Elem all proven"),
        ("f6", "family", "Walk<U>", "U is Seq and Transferable and Owned", "U/U", "U.Elem unproven"),
        ("f7", "family", "Even<U>", "U is TTree", "P/P", "a two-declaration cycle"),
        ("f8", "family", "Even<U>", "U is Tree and Transferable and Owned", "U/U", ""),
        ("f9", "family", "LFam<U>", "U is STree", "P/P", "obj/U.Childᵏ: Sealed and Transferable at every k"),
        ("f10", "family", "LFam<U>", "U is OTree and TTree", "P/U", "U.Child's Sealed proof is unknown"),
        ("f11", "family", "LFamBad<U>", "U is STree", "E/E", "8.7.3: obj/T.(Tree).Child needs an Object Target; Tree supplies no ObjectPayload"),
        ("k1", "review", "Fwd2<Item>{static}", "", "P/R", "SemAny<rc/Item>: a pair binder bound to a projection, s = rc"),
        ("k2", "review", "Fwd2<Cfg>{static}", "", "P/P", "SemAny<i32>: owner/Base"),
        ("k3", "review", "Fwd2<U>{static}", "U is THas and Transferable and Owned", "P/U", "outer(U.Out) unknown"),
        ("k4", "review", "PA<ref/(ref/(obj/Item) during static) during static>{static}", "", "P/R", "a peel through PB: PA<obj/Item> stores obj/Base"),
        ("k5", "review", "PA<ref/(ref/Item during static) during static>{static}", "", "P/P", "every level owner or ref"),
        ("k6", "review", "Peel3<ref/(obj/Item) during static>{static}", "", "P/R", "Peel3<obj/Item> stores obj/OBox<Item>, OBox open"),
        ("k7", "review", "Peel3<ref/(ref/i32 during static) during static>{static}", "", "P/P", "every level ref or owner over a sealed i32"),
        ("k8", "review", "Peel3<U>{static}", "U is Transferable and Owned", "P/U", "every layer of U applied to OBox<...>"),
        ("j1", "review2", "Q0<ref/(ref/(ref/(ref/(ref/(obj/Item) during static) during static) during static) during static) during static>{static}", "", "P/R", "five peels per round trip: Q0 at target^5 is obj/Item"),
        ("j2", "review2", "Q0<ref/(ref/(ref/(ref/(Item) during static) during static) during static) during static>{static}", "", "P/P", "five peels, owner at target^5"),
        ("j3", "review2", "Q0<U>{static}", "U is Transferable and Owned", "P/U", ""),
        ("j4", "review2", "Q0<ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(obj/Item) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static>{static}", "", "P/R", "Q0 at target^30 is obj/Item (R1-1)"),
        ("d1", "review2", "Peel<ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(obj/Item) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static>{static}", "", "P/R", "Peel at depth 70: obj/Base (R1-2)"),
        ("x1", "review2", "IX<ref/Item during static, ref/(ref/(ref/(obj/Item) during static) during static) during static>{static}", "", "P/R", "swapped peels: s at level 3 is obj"),
        ("x2", "review2", "IX<ref/Item during static, ref/(ref/(ref/(Item) during static) during static) during static>{static}", "", "P/P", ""),
        ("x3", "review2", "IX<U, V>{static}", "U is Transferable and Owned; V is Transferable and Owned", "P/U", ""),
        ("b1", "review2", "RB<obj/Item>{static}", "", "P/R", "obj/OBox<RB<Item>>: OBox open"),
        ("b2", "review2", "RB<ref/(ref/(i32) during static) during static>{static}", "", "P/P", "self-reference through Array"),
        ("b3", "review2", "RB<ref/(obj/Item) during static>{static}", "", "P/R", "level 1 is obj"),
        ("s1", "review2", "PS<ref/(obj/Item) during static>{static}", "", "P/R", "level 0's SemAny<obj/Item> stores obj/Base"),
        ("s2", "review2", "PS<ref/(ref/(obj/Item) during static) during static>{static}", "", "P/R", "a walk product at level 1"),
        ("s3", "review2", "PS<ref/(ref/(ref/(obj/Item) during static) during static) during static>{static}", "", "P/R", "a walk product at level 2"),
        ("s4", "review2", "PS<ref/(ref/(ref/(Item) during static) during static) during static>{static}", "", "P/P", ""),
        ("s5", "review2", "PS<U>{static}", "U is Transferable and Owned", "P/U", ""),
        ("s6", "review2", "PS<U>{static}", "U is Transferable and Owned and ObjectPayload", "P/P", "U owner: every level owner/Base"),
        ("q1", "review3", "DP<Cfg>{static}", "", "P/P", "EP<Cfg>: owner split of an ObjectPayload argument; Cfg.Out = i32"),
        ("q2", "review3", "DP<Item>{static}", "", "P/R", "Item.Out = rc/Item"),
        ("q3", "review3", "Fwd<rc/Item>{static}", "", "P/R", "the argument refutes, independent of the pair site"),
        ("q4", "review3", "TB<ref/i32 during static, obj/Item>{static}", "", "P/P", "s/U with U r's target: ref/Item"),
        ("q5", "review3", "TB<obj/Item, obj/Base>{static}", "", "P/R", "the argument obj/Base refutes"),
        ("q6", "review3", "TB<obj/Item, obj/OBox<i32>>{static}", "", "P/R", "the argument obj/OBox<i32> refutes (open)"),
        ("g1", "review2", "G2<ref/Item during static, i32>{static}", "", "P/P", "ref/OBox<Box^k<i32>> at every level"),
        ("g2", "review2", "G2<obj/Item, i32>{static}", "", "P/R", "obj/OBox<i32>: OBox open"),
    ];

    // Generic statics: (id, container, static Type, reading body's extra premises, instance read, SPEC: the confined read is an error)
    public static readonly (string Id, string Container, string Type, string Body, string Args, bool Err, string Why)[] Anchors = [
        ("a1", "Shared", "i32", "", "", false, "8.11.2's Shared.limit"),
        ("a2", "Shared", "rc/Item", "", "", true, "8.11.2's Shared.node"),
        ("a3", "Shared", "obj/Base", "", "", true, "g1"),
        ("a4", "Shared", "obj/dyn Shape", "", "", true, "g2"),
        ("a5", "Shared", "fn", "", "", true, "g3"),
        ("a6", "Shared", "Array<i32>", "", "", false, "g5"),
        ("a7", "Shared", "Array<rc/Item>", "", "", true, "g8"),
        ("a8", "Shared", "Hook", "", "", true, "g10"),
        ("a9", "Shared", "ChainS", "", "", false, "g11"),
        ("a10", "Gen<T> where T is Owned", "T", "", "i32", false, "g4, a valid instance"),
        ("a11", "Gen<T> where T is Owned", "T", "", "rc/Item", true, "g4"),
        ("a12", "Cache<T> where T is Owned", "Option<T>", "T is Transferable", "rc/Item", true, "A-2: a conditional member reads Cache<rc/Item>.S.x"),
        ("a13", "Cache<T> where T is Owned", "Option<T>", "T is Transferable", "T", false, "its own instance under its premise"),
        ("a14", "Cache<T> where T is Owned", "Option<T>", "", "i32", false, ""),
        ("a15", "TCache<T> where T is Owned and Transferable", "Option<T>", "", "i32", false, "a container premise"),
        ("a16", "Reg<T> where T is ObjectPayload and Owned and Transferable", "Option<obj/T>", "", "Item", false, "P-2"),
        ("a17", "Reg<T> where T is ObjectPayload and Owned and Transferable", "Option<obj/T>", "", "Base", true, "P-2"),
        ("a18", "SReg<T> where T is ObjectPayload and Owned and Transferable and Sealed", "Option<obj/T>", "", "Item", false, "P-2 with a Sealed premise"),
        ("a19", "PG<s/T> where s is ref|obj; T is Owned and Transferable and ObjectPayload and Sealed", "Option<s/T during static>", "", "obj/Item", false, "a pair binder over its admitted set"),
        ("a20", "PGr<s/T> where s is ref|rc; T is Owned and Transferable and ObjectPayload and Sealed", "Option<s/T during static>", "", "rc/Item", true, ""),
        ("a21", "PGr<s/T> where s is ref|rc; T is Owned and Transferable and ObjectPayload and Sealed", "Option<s/T during static>", "", "ref/Item during static", false, ""),
    ];

    // Conformances (SPEC 8.3.3, 8.7.1: an ancestor conformance to Transferable is decided by the judgment), with the hand verdict.
    public static readonly (string Text, bool Valid, string Why)[] Confs = [
        ("Leaf: Tree", false, "8.3.3: proving Leaf is Tree needs Leaf is Tree"),
        ("Wrap<X>: Tree when X is Tree", false, "Child = Wrap<Wrap<X>> needs the conformance itself (concrete growth)"),
        ("Item: Has", true, ""), ("Cfg: Has", true, ""), ("Pair2<X>: Has when X is Has", true, ""),
        ("Holder: Portable", false, "Portable: Transferable; Holder stores rc/Item"),
        ("Item: Portable", true, ""),
    ];
}

static class Run
{
    // The decided rules: the owner's option 2 (2026-10-21) with F3, F4, F5 (a), F6 and the core reduction.
    public static readonly Rules Decided = new("Decided", Family: "U", PairU: true, Note: "option 2 with F3, F4, F5 (a), F6 and the core reduction");
    static readonly Rules[] Variants = [
        new("Option1", Note: "option 1, the X-2 candidate: outer(X)/target(X), the core reduction, peel templates (step j ≤ 4 and 64 instances at use: R1-1, R1-2 open), the use-site rule, Q*.s families"),
        new("Package", Outer: false, AllAdmitted: false, TargetAlways: false, Family: "prefix", Note: "§3.2 as written (sharing the model's core reduction and coverage): no outer(...), \"Proven when none does\", the target only under a proven Sealed, Tᵢ.p.qᵏ"),
        Decided with { Name = "PairBinderOnly", PairU = false, Outer = false, Note = "option 2 without its U: an obligation that would name outer(X) or target(X) is dropped" },
        Decided with { Name = "NoCore", Core = false, Note = "option 2 without the core reduction" },
        Decided with { Name = "A", Layer = "A", Note = "design A's summary: a formal adds nothing, no layer obligation" },
        Decided with { Name = "B", Layer = "B", Note = "design B's ViewTarget (obj, objref, objuniq; two-valued) and Layer(s) (rc, raw); arc continues" },
        Decided with { Name = "TwoValued", TwoValued = true, Note = "the pre-P-2 wording: an unproven Sealed refutes" },
        Decided with { Name = "NoneRefutes", AllAdmitted = false, Note = "§3.2's \"Proven when none does\" for an admitted set" },
        Decided with { Name = "TargetIfSealed", TargetAlways = false, Note = "§3.2's object layer: the target only when Sealed is Proven" },
        Decided with { Name = "FamilyPrefix", Family = "prefix", Note = "§3.2's family (Tᵢ, p, q*) = Tᵢ.p.qᵏ with B's inductive rule" },
        Decided with { Name = "FamilyQs", Family = "suffix", Note = "F5 (b): Tᵢ.Q*.s families with the knowledge-state judgment" },
        Decided with { Name = "NoInternal", Internal = false, Note = "the exemption without the iterators' internal storage declarations" },
        Decided with { Name = "Bounded8", Family = "plain", Cap = 8, IgnoreDiv = true, Note = "oracle for families: obligations enumerated for 8 rounds, each judged by 8.3.2" },
        Decided with { Name = "Guard", Walk = "guard", Note = "instantiation walk with today's growth guard (Binding.Capabilities.cs:228-245)" },
        Decided with { Name = "Cap4U", Walk = "capU", Note = "instantiation walk, depth cap 4 answering U" },
        Decided with { Name = "Cap4P", Walk = "capP", Note = "instantiation walk, depth cap 4 answering P" },
        Decided with { Name = "BodyAnchor", BodyAnchor = true, Note = "anchor: the reading body's premises (the Transferable plan §5.3)" },
    ];
    // Option 2's located U, listed by hand: Transferable of a Type whose summary would need outer(X) or target(X) (a pair parameter
    // instantiated with the declaration's own plain formal or projection, which every peel is), and both intrinsics under a projection
    // family (F5 (a)). Every other verdict must equal SPEC's.
    // k1 (Fwd2<Item>: its recorded T(T.(Has).Out) is rc/Item), b1 (RB<obj/Item>: its recorded level-0 Layer(s, core:open)) and q3 (the
    // argument rc/Item) refute exactly through what the summary keeps, so under N3-1's order they answer SPEC's R, not L.
    static readonly HashSet<string> PairCases = ["l16", "l17", "l23", "l25", "l26", "l27", "k2", "k3", "k4", "k5", "k6", "k7", "k8", "j1", "j2", "j3", "j4", "d1",
        "x1", "x2", "x3", "b2", "b3", "s1", "s2", "s3", "s4", "s5", "s6"];
    static readonly HashSet<string> FamilyCases = ["f1", "f2", "f5", "f6", "f7", "f8", "f9", "f10"];
    static (V O, V T) Expected(string id, V o, V t) => (FamilyCases.Contains(id) ? V.L : o, FamilyCases.Contains(id) || PairCases.Contains(id) ? V.L : t);
    static string S2(V o, V t) => $"{o}/{t}";
    static V Pv(char c) => c switch { 'P' => V.P, 'U' => V.U, 'R' => V.R, _ => V.E };
    public static string Cls(V spec, V got) => got == spec ? "" : got == V.L ? "located U" : got == V.NT ? "NONTERM" : spec != V.P && got == V.P ? "UNSOUND"
        : spec == V.P && got == V.R ? "over-strict" : spec == V.E ? "misses Error" : $"{got} for {spec}";

    public static int Go(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var p = Cases.Program();
        var engines = new[] { Decided }.Concat(Variants).ToDictionary(r => r.Name, r => new Engine(p, r));
        var dec = engines["Decided"]; var fails = 0;

        Console.WriteLine($"# 2x-P12 component summary prototype\n\nDeclarations {p.D.Count}, Contracts {p.C.Count}, conformances {p.F.Count}, cases {Cases.All.Length}, anchors {Cases.Anchors.Length}.");
        Console.WriteLine("\n## Fixed point (rounds until no summary changes; cap 24, Bounded8 8; not converged includes every reader of one)\n\n| Rules | rounds | not converged | located U: families | located U: pair sites | max obligations | templates |\n| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var (name, e) in engines)
            Console.WriteLine($"| {name} | {e.Rounds} | {(e.Div.Count == 0 ? "—" : string.Join(", ", e.Div.Order()))} | {(e.FamU.Count == 0 ? "—" : string.Join(", ", e.FamU.Order()))} | "
                + $"{string.Join(", ", e.S.Where(x => x.Value.PairU.Contains(x.Key)).Select(x => x.Key).DefaultIfEmpty("—"))} | {e.S.Values.Max(s => s.Obl.Count)} | {e.S.Values.Sum(s => s.Obl.Values.Count(o => o.Hole != null))} |");
        Console.WriteLine("\nDecided's summaries with obligations or a located-U site:\n");
        foreach (var (name, s) in dec.S.Where(x => x.Value.Obl.Count > 0 || x.Value.PairU.Count > 0 || x.Value.FamU.Count > 0))
            Console.WriteLine($"- {name}: ExclT {(s.XT ? "yes" : "no")}, ExclO {(s.XO ? "yes" : "no")}, Error {(s.Err ? "yes" : "no")}, Obl {{{string.Join(", ", s.Obl.Values.Select(o => o.ToString()))}}}"
                + (s.FamU.Count > 0 ? $", family U {{{string.Join(", ", s.FamU)}}}" : "") + (s.PairU.Count > 0 ? $", pair U {{{string.Join(", ", s.PairU)}}}{(s.PairOwned ? " (Owned too)" : "")}" : ""));
        Console.WriteLine($"- Exempt declarations (the KimiLibraryShapeTest condition: their own storage reaches no rc, Function or view leaf): {string.Join(", ", p.D.Values.Where(d => d.Exempt || d.Internal).Select(d => $"{d.Name} ExclT {(dec.S[d.Name].XT ? "**yes**" : "no")}"))}");
        var walk = engines["Package"].S["Walk"];
        var longest = walk.Obl.Values.MaxBy(o => o.Key.Length);
        Console.WriteLine($"- Package's Walk at its cap: {walk.Obl.Count} obligations, still growing; the longest is T(T.(Seq).Next^{longest.X.G[0].W.Length}{longest.X.G[^1]}).");

        Console.WriteLine("\n## Cases (Owned/Transferable; L: a located Unsupported_Kd)\n\n| Case | Query | Premises | SPEC | why | Package | Option1 | Decided (expected) | meets |\n| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        var results = new Dictionary<string, Dictionary<string, (V O, V T)>>();
        int located = 0, exactOwned = 0;
        foreach (var c in Cases.All)
        {
            var q = Parser.Parse(p, c.Q); var sc = Scope.Parse(p, c.Ctx); var so = Pv(c.Spec[0]); var st = Pv(c.Spec[2]);
            var r = results[c.Id] = engines.ToDictionary(e => e.Key, e => (e.Value.Eval("Owned", q, sc), e.Value.Eval("Transferable", q, sc)));
            string Mark(string n) { var (o, t) = r[n]; var k = string.Join(", ", new[] { Cls(so, o), Cls(st, t) }.Where(x => x.Length > 0).Distinct()); return S2(o, t) + (k.Length > 0 ? $" **{k}**" : ""); }
            var expected = Expected(c.Id, so, st); var ok = r["Decided"] == expected; fails += ok ? 0 : 1;
            located += r["Decided"].T == V.L ? 1 : 0; exactOwned += r["Decided"].O == so ? 1 : 0;
            Console.WriteLine($"| {c.Id} | `{c.Q}` | {(c.Ctx.Length > 0 ? $"`{c.Ctx}`" : "")} | {c.Spec} | {c.Why} | {Mark("Package")} | {Mark("Option1")} | {S2(r["Decided"].O, r["Decided"].T)}"
                + $"{(expected == (so, st) ? "" : $" ({S2(expected.O, expected.T)})")} | {(ok ? "yes" : "**NO**")} |");
        }
        Console.WriteLine($"\nDecided: Transferable answers the located U on {located} cases (the pair cases and the families listed by hand), Owned equals SPEC on {exactOwned} of "
            + $"{Cases.All.Length} (the others: F5 (a)'s families), every other verdict equals SPEC; use-site re-entries {dec.Reentries}; peel templates "
            + $"{dec.S.Values.Sum(s => s.Obl.Values.Count(o => o.Hole != null))}; located U raised at {string.Join(", ", dec.Raised)}.");

        Console.WriteLine("\n## Generic statics (§3.5's anchor; A4: Proven nothing, Refuted Environment, Unknown Limit = located U, Error Derived)\n");
        Console.WriteLine("| Read | Container | Static's Type | Body premises | Instance | SPEC (instance's verdict) | Decided | BodyAnchor | TwoValued | Package |\n| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var a in Cases.Anchors)
        {
            var d = Prog.Head(a.Container, out var w); d.Prem = Scope.Parse(p, w);
            var ty = Parser.Parse(p, a.Type); var body = Scope.Parse(p, a.Body);
            var inst = a.Args.Length == 0 ? ty : p.Subst(ty, p.Bind(d, Ty.Nom(d.Name, [Parser.Parse(p, a.Args)])));
            var iv = dec.J("Transferable", inst, body); var consistent = iv != V.P == a.Err; fails += consistent ? 0 : 1;
            string Out(string rn)
            {
                var e = engines[rn]; var v = e.Eval("Transferable", ty, e.R.BodyAnchor ? d.Prem.Plus(body) : d.Prem);
                var o = v switch { V.P => "valid", V.R => "error", V.U => "U", V.L => "U (located)", V.E => "derived", _ => "NT" };
                var bad = a.Err && o == "valid" ? " **UNSOUND**" : !a.Err && o == "error" ? " **OVERSTRICT**" : d.Ps.Count == 0 && o == "U" ? " **U at a closed Type**" : "";
                if (rn == "Decided" && bad.Length > 0) fails++;
                return $"{v} → {o}{bad}";
            }
            Console.WriteLine($"| {a.Id} | `{d.Name}` | `{a.Type}` | {a.Body} | {(a.Args.Length > 0 ? $"`{d.Name}<{a.Args}>`" : "")} | {(a.Err ? "error" : "valid")} ({iv}{(consistent ? "" : " **NO**")}) | {Out("Decided")} | {Out("BodyAnchor")} | {Out("TwoValued")} | {Out("Package")} |");
        }

        Console.WriteLine("\n## Conformances (verified without themselves, SPEC 8.3.3; an intrinsic parent by the judgment, 8.7.1)\n\n| Conformance | SPEC | model |\n| --- | --- | --- |");
        foreach (var (text, valid, why) in Cases.Confs)
        {
            var f = p.F.First(f => f.Text == text);
            var got = f.Valid && p.Close([f.Contract]).Where(c => c is "Transferable" or "Owned").All(c => dec.J(c, Ty.Nom(f.Decl, [.. f.Ps.Select(Ty.Var)]), f.When) == V.P);
            fails += got == valid ? 0 : 1;
            Console.WriteLine($"| `{text}` | {(valid ? "accepted" : "rejected")}{(why.Length > 0 ? ": " + why : "")} | {(got ? "accepted" : "rejected")}{(got == valid ? "" : " **NO**")} |");
        }
        Console.WriteLine("| `struct Claim` with `Self is Transferable` | rejected at the clause (8.7.1) | the summary ignores the clause; c36 is Refuted |");

        Console.WriteLine("\n## Variants (cases where a variant differs from Decided; its verdict, then its class against SPEC)\n");
        foreach (var v in Variants)
        {
            var diffs = Cases.All.Where(c => results[c.Id][v.Name] != results[c.Id]["Decided"]).Select(c =>
            {
                var (o, t) = results[c.Id][v.Name]; var k = string.Join(", ", new[] { Cls(Pv(c.Spec[0]), o), Cls(Pv(c.Spec[2]), t) }.Where(x => x.Length > 0).Distinct());
                return $"{c.Id} {S2(o, t)}{(k.Length > 0 ? $" ({k})" : " (SPEC)")}";
            }).ToList();
            Console.WriteLine($"- **{v.Name}** ({v.Note}): {(diffs.Count == 0 ? "as Decided on every case" : string.Join("; ", diffs))}");
        }

        var graphs = args.SkipWhile(a => a != "-random").Skip(1).Select(int.Parse).DefaultIfEmpty(2000).First();
        fails += Rand.Check(graphs, args.SkipWhile(a => a != "-seed").Skip(1).Select(int.Parse).DefaultIfEmpty(20261021).First());
        Console.WriteLine($"\nDecided failures: {fails}");
        return fails;
    }
}

// Random declaration graphs (no projections): Decided's summary against the instantiation reference (a BFS cut at nominal depth 9,
// deepened to 100 when cut; still cut, over 4,000 nodes or an instance over 2,000 terms: inconclusive). Closed queries, so every answer
// is P, R, U (an Origin), E, or Decided's located U (Transferable only). Graphs: 2–4 declarations, or a ring of 5–8 peeling pair
// declarations (a quarter of the graphs). Pair binders apply to Base, to Types built from the pair target and to other formals; half the
// pair declarations peel. A fifth of the query arguments carry 2–80 ref layers.
static class Rand
{
    static readonly string[] Args = ["i32", "Item", "Base", "rc/Item", "obj/Base", "obj/Item", "ref/i32 during static", "ref/i32 during local", "(i32, i32)", "arc/Item", "raw/i32", "Option<Item>", "Box<Base>", "Weak<rc/Item>", "fn"];
    public static int Check(int graphs, int seed)
    {
        var rnd = new Random(seed); var cls = new SortedDictionary<string, int>(); var first = new SortedDictionary<string, string>(); var locatedBy = new SortedDictionary<string, int>();
        int queries = 0, inconclusive = 0, deepened = 0, maxRounds = 0, decDiv = 0, opt1Rounds = 0, opt1Div = 0, templates = 0, reentries = 0, pairGraphs = 0, ownedSites = 0, rings = 0, deep = 0, bad = 0;
        for (var g = 0; g < graphs; g++)
        {
            var p = Cases.Prelude(new Prog()); var ring = rnd.Next(4) == 0; var nd = ring ? rnd.Next(5, 9) : rnd.Next(2, 5); rings += ring ? 1 : 0;
            var heads = Enumerable.Range(0, nd).Select(k => (Name: "R" + k, Pair: ring || rnd.Next(3) == 0, Arity: rnd.Next(1, 3), Slot: rnd.Next(3) == 0)).ToArray();
            string Apply(int j, string slot, Func<int, string> arg) { var h = heads[j]; return h.Name + "<" + string.Join(", ", Enumerable.Range(0, h.Arity).Select(arg)) + ">" + (h.Slot ? "{" + (slot ?? "static") + "}" : ""); }
            string Ref(int depth, string[] formals, string slot, HashSet<string> objT, string first = null)
                => Apply(rnd.Next(nd), slot, j => j == 0 && first != null ? first : Gen(depth - 1, formals, null, slot, objT));
            string Gen(int depth, string[] formals, string pair, string slot, HashSet<string> objT)
            {
                var o = slot ?? "static"; string X() => Gen(depth - 1, formals, pair, slot, objT);
                string Formal() => formals[rnd.Next(formals.Length)];
                string Over(string f) { objT.Add(f); return f; } // an object form over a formal needs its ObjectPayload premise (SPEC 8.7.3)
                string Obj() => rnd.Next(4) switch { 0 => "Item", 1 => "Base", 2 => Over(Formal()), _ => Ref(depth, formals, slot, objT) };
                string PairOver() => rnd.Next(5) switch // the pair binder over Base, over a Type built from the pair target, or over another formal
                {
                    0 => "Base", 1 => new[] { "Box", "OBox", "Array", "Option" }[rnd.Next(4)] + "<T0>", 2 => Ref(depth, formals, slot, objT, "T0"),
                    3 => formals.Length > 1 ? Over("T1") : "OBox<T0>", _ => $"({Ref(depth, formals, slot, objT, "T0")})",
                };
                if (depth <= 0) return rnd.Next(2) == 0 ? "i32" : Formal();
                return rnd.Next(16) switch
                {
                    0 => "i32", 1 => Formal(), 2 => "rc/" + Obj(), 3 => $"raw/({X()})", 4 => "obj/" + Obj(), 5 => "arc/" + Obj(),
                    6 => $"objref/{Obj()} during {o}", 7 => $"ref/({X()}) during {o}", 8 => pair != null ? $"{pair}/{PairOver()} during {o}" : "Base",
                    9 => $"({X()}, {X()})", 10 => $"Option<{X()}>", 11 or 12 => Ref(depth, formals, slot, objT), 13 => "fn", 14 => $"Array<{X()}>", _ => $"Box<{X()}>",
                };
            }
            for (var k = 0; k < nd; k++)
            {
                var h = heads[k];
                var ps = Enumerable.Range(0, h.Arity).Select(j => j == 0 && h.Pair ? "s/T0" : "T" + j).ToArray(); var formals = Enumerable.Range(0, h.Arity).Select(j => "T" + j).ToArray();
                var flags = (rnd.Next(5) == 0 ? "open " : "") + (rnd.Next(10) == 0 ? "exempt " : "") + (rnd.Next(20) == 0 ? "ownedleaf " : "");
                var objT = new HashSet<string>(); var slot = h.Slot ? "{a}" : ""; var sl = h.Slot ? "a" : null;
                var store = rnd.Next(20) == 0 ? new List<string> { "Missing" } : Enumerable.Range(0, rnd.Next(1, ring ? 3 : 4)).Select(_ => Gen(ring ? 2 : 3, formals, h.Pair ? "s" : null, sl, objT)).ToList();
                if (ring) store.Add($"Option<obj/{Apply((k + 1) % nd, sl, j => j == 0 ? "T0" : formals[j % formals.Length])}>"); // the ring: the pair target passed on whole
                else if (h.Pair && rnd.Next(2) == 0) store.Add($"Option<obj/{h.Name}<{string.Join(", ", formals)}>{slot}>"); // a self peel
                p.Decl($"{flags}{h.Name}<{string.Join(", ", ps)}>{slot}" + (objT.Count > 0 ? " where " + string.Join("; ", objT.Order().Select(f => f + " is ObjectPayload")) : ""), [.. store]);
            }
            p.Done();
            var dec = new Engine(p, Run.Decided); var opt1 = new Engine(p, new Rules("Option1")); var pkg = new Engine(p, new Rules("Package", Outer: false, AllAdmitted: false, TargetAlways: false, Family: "prefix"));
            var grd = new Engine(p, Run.Decided with { Name = "Guard", Walk = "guard" });
            maxRounds = Math.Max(maxRounds, dec.Rounds); decDiv += dec.Div.Count > 0 ? 1 : 0; opt1Rounds = Math.Max(opt1Rounds, opt1.Rounds); opt1Div += opt1.Div.Count > 0 ? 1 : 0;
            templates += dec.S.Values.Sum(x => x.Obl.Values.Count(o => o.Hole != null)); pairGraphs += dec.S.Values.Any(x => x.PairU.Count > 0) ? 1 : 0; ownedSites += dec.S.Values.Any(x => x.PairOwned) ? 1 : 0;
            string Arg() { var a = Args[rnd.Next(Args.Length)]; if (rnd.Next(5) != 0) return a; deep++; var n = rnd.Next(2, 81); for (var j = 0; j < n; j++) a = $"ref/({a}) during static"; return a; }
            for (var k = 0; k < nd; k++)
                for (var r = 0; r < (ring ? 1 : 3); r++)
                {
                    var h = heads[k];
                    var q = Parser.Parse(p, h.Name + "<" + string.Join(", ", Enumerable.Range(0, h.Arity).Select(_ => Arg())) + ">" + (h.Slot ? (rnd.Next(2) == 0 ? "{static}" : "{local}") : ""));
                    foreach (var i in new[] { "Owned", "Transferable" })
                    {
                        // A walk cut at depth 9 treats its unexplored part as "no exclusion": deepen it to 100, and count it inconclusive when it
                        // is still cut (unless it found Error, which nothing deeper changes) or exceeds its node or term limits.
                        var reference = dec.Expand(i, q, new Scope(), "ref", 9, out var cut);
                        if (cut && reference != V.E) { deepened++; reference = dec.Expand(i, q, new Scope(), "ref", 100, out cut); }
                        if (reference == V.NT || cut && reference != V.E) { inconclusive++; continue; }
                        queries++;
                        var c = dec.Eval(i, q, new Scope());
                        if (c == V.L && i == "Transferable") { var key = reference.ToString(); locatedBy[key] = locatedBy.GetValueOrDefault(key) + 1; }
                        else if (c != reference)
                        {
                            bad++;
                            if (bad <= 5) Console.WriteLine($"\nRandom disagreement ({i} of `{q}`): reference {reference}, Decided {c}\n" + string.Join("\n", p.D.Values.Where(d => d.Name.StartsWith('R')).Select(d => $"  {d.Text} = {string.Join(", ", d.Store)}")));
                        }
                        void Count(string name, V v)
                        {
                            if (v == reference) return;
                            var key = $"{name}: {i} {Run.Cls(reference, v)}"; cls[key] = cls.GetValueOrDefault(key) + 1;
                            first.TryAdd(key, $"`{(q.ToString().Length > 160 ? q.ToString()[..160] + "…" : q)}` with " + string.Join("; ", p.D.Values.Where(d => d.Name.StartsWith('R')).Select(d => $"`{d.Text} = {string.Join(", ", d.Store)}`")));
                        }
                        Count("Option1", opt1.J(i, q, new Scope())); Count("Package", pkg.J(i, q, new Scope())); Count("Guard", grd.Eval(i, q, new Scope()));
                    }
                }
            reentries += dec.Reentries;
        }
        Console.WriteLine($"\n## Random declaration graphs\n\n{graphs} graphs (seed {seed}; {rings} rings of 5–8 peeling declarations; {deep} query arguments with 2–80 ref layers): {queries} closed queries compared "
            + $"({deepened} reference walks cut at depth 9 deepened to 100); {inconclusive} inconclusive (still cut, or over the node or term limits). Decided: disagreements {bad}; "
            + $"Transferable's located U {locatedBy.Values.Sum()} times (reference {string.Join(", ", locatedBy.Select(x => $"{x.Key} ×{x.Value}"))}); most fixed-point rounds {maxRounds}, "
            + $"graphs not converged {decDiv}, graphs with a pair site {pairGraphs} (with an Owned site {ownedSites}), peel templates {templates}, use-site re-entries {reentries}. Option1: most rounds {opt1Rounds}, graphs not converged {opt1Div}.\n"
            + $"Other rules against the reference: {(cls.Count == 0 ? "none differ" : string.Join("; ", cls.Select(x => $"{x.Key} ×{x.Value}")))}.");
        foreach (var (k, v) in first) Console.WriteLine($"- first {k}: {v}");
        return bad;
    }
}
