// G170 C1 prototype: the exhaustive bounded reference evaluator. No selection: every SPEC 8.3.2 supply row is applied to every
// fact, forward, until nothing changes; identity is a union-find over the materialized Types closed under congruence. The rows
// are built here from the declarations themselves (direct parents with Self and arguments substituted, conjunct-split clauses,
// verified conformances and their bindings at materialized Types, inputs), not from the templates C reads, and recursive Contracts
// are classified here by their SPEC 8.3.3 definition (a bounded closure from `T0 is C<P̄>`), so agreement tests the rows too. The
// only difference from the unbounded semantics is the bound: a Type deeper than the bound is never materialized, so an answer is
// an oracle only where it is stable under a larger bound (Runner checks bound and bound + 2).
#pragma warning disable
#nullable enable

namespace G170C1;

public sealed class Reference
{
    private readonly ProgramModel m;
    private readonly View view;
    private readonly bool includePending;
    private readonly int bound;
    private readonly HashSet<int>? bindings;
    private readonly int maxTerms;
    private readonly bool rowsOnly;
    private readonly Dictionary<int, int> parent = new();
    private readonly List<Term> universe = new();
    private readonly HashSet<int> inUniverse = new();
    private readonly HashSet<(byte Kind, int A, int B)> atoms = new();
    private readonly List<Formula> compounds = new();
    private readonly HashSet<string> compoundSeen = new();
    private readonly Dictionary<int, List<Term>> members = new();
    private HashSet<ContractDecl> recursive = new();

    public Reference(ProgramModel model, View view, bool includePending, int bound, HashSet<int>? bindings = null, int maxTerms = 40_000)
        : this(model, view, includePending, bound, bindings, maxTerms, rowsOnly: false)
    {
    }

    private Reference(ProgramModel model, View view, bool includePending, int bound, HashSet<int>? bindings, int maxTerms, bool rowsOnly)
    {
        this.m = model;
        this.view = view;
        this.includePending = includePending;
        this.bound = bound;
        this.bindings = bindings;
        this.maxTerms = maxTerms;
        this.rowsOnly = rowsOnly;
    }

    public bool Overflow { get; private set; }

    public int Iterations { get; private set; }

    public int TermCount => this.universe.Count;

    /// <summary>Gets the Contracts this evaluator classified as recursive (SPEC 8.3.3).</summary>
    public IReadOnlyCollection<ContractDecl> Recursive => this.recursive;

    public void Run(IEnumerable<Term> seeds) => this.Run(seeds, this.rowsOnly ? [] : this.m.Inputs);

    public (bool Pos, bool Neg) Answer(Formula p)
    {
        var (pos, neg) = (this.Holds(p), this.Holds(Formula.Negate(p)));
        return (pos, neg);
    }

    public bool Holds(Formula p)
    {
        switch (p.Kind)
        {
            case FK.Is:
                return this.atoms.Contains((0, this.FindTerm(p.A!), this.FindTerm(p.B!))) || this.atoms.Any(x => x.Kind == 0 && this.Find(x.A) == this.FindTerm(p.A!) && this.Find(x.B) == this.FindTerm(p.B!));
            case FK.Eq:
                return this.inUniverse.Contains(p.A!.Id) && this.inUniverse.Contains(p.B!.Id) && this.Find(p.A!.Id) == this.Find(p.B!.Id);
            case FK.Not when p.L!.Kind == FK.Is:
                return this.atoms.Any(x => x.Kind == 1 && this.Find(x.A) == this.FindTerm(p.L.A!) && this.Find(x.B) == this.FindTerm(p.L.B!));
            case FK.Not when p.L!.Kind == FK.Eq:
                {
                    var a = this.FindTerm(p.L.A!);
                    var b = this.FindTerm(p.L.B!);
                    return this.atoms.Any(x => x.Kind == 3 && ((this.Find(x.A) == a && this.Find(x.B) == b) || (this.Find(x.A) == b && this.Find(x.B) == a)));
                }

            default:
                var key = this.Canon(p);
                return this.compounds.Any(c => this.Canon(c) == key);
        }
    }

    /// <summary>Gets the Contract facts on a subject's class (positive or negated, reference).</summary>
    public IEnumerable<(bool Positive, ContractDecl Decl, Term Ref)> FactsOn(Term subject)
    {
        var s = this.FindTerm(subject);
        foreach (var (kind, a, b) in this.atoms)
        {
            if (kind <= 1 && this.Find(a) == s)
            {
                yield return (kind == 0, this.m.DeclOf(this.m.Terms.All[b])!, this.m.Terms.All[b]);
            }
        }
    }

    public bool Same(Term a, Term b) => this.inUniverse.Contains(a.Id) && this.inUniverse.Contains(b.Id) && this.Find(a.Id) == this.Find(b.Id);

    private void Run(IEnumerable<Term> seeds, IEnumerable<Formula> inputs)
    {
        if (!this.rowsOnly)
        {
            this.recursive = this.Classify();
        }

        foreach (var t in seeds)
        {
            this.AddTerm(t);
        }

        foreach (var input in inputs)
        {
            foreach (var f in Split(input))
            {
                this.AddFormula(f);
            }
        }

        var changed = true;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (changed && !this.Overflow)
        {
            this.Iterations++;
            if (clock.Elapsed.TotalSeconds > 5)
            {
                this.Overflow = true; // a time guard for the oracle only; such a model is excluded, never counted as agreement
                break;
            }

            changed = this.Congruence();
            this.members.Clear();
            foreach (var t in this.universe)
            {
                var root = this.Find(t.Id);
                if (!this.members.TryGetValue(root, out var list))
                {
                    this.members[root] = list = new();
                }

                list.Add(t);
            }

            // Refinement: `s is K<ā>` supplies each direct parent of K and each conjunct of K's clauses, with s for Self and ā for
            // K's parameters, at every member of s's class (the transitive ancestors follow by iteration).
            var snapshot = this.atoms.Where(a => a.Kind == 0).Select(a => (this.Find(a.A), a.B)).Distinct().ToArray();
            foreach (var (s, r) in snapshot)
            {
                var reference = this.m.Terms.All[r];
                if (this.m.DeclOf(reference) is not { } k || !this.UsableState(k.Status))
                {
                    continue;
                }

                foreach (var subject in this.Members(s))
                {
                    var beta = new Term?[k.Arity + 1];
                    beta[0] = subject;
                    for (var i = 0; i < k.Arity; i++)
                    {
                        beta[i + 1] = reference.Kids[i];
                    }

                    foreach (var p in k.Parents)
                    {
                        changed |= this.Conclude(new Formula(FK.Is, this.m.Terms.Var(0), p), beta);
                    }

                    foreach (var clause in k.Clauses)
                    {
                        foreach (var f in Split(clause))
                        {
                            changed |= this.Conclude(f, beta);
                        }
                    }
                }
            }

            // Verified conformances (never to a recursive Contract), and their bindings, at materialized Types only.
            foreach (var c in this.rowsOnly ? [] : this.m.Conformances)
            {
                if (this.recursive.Contains(this.m.DeclOf(c.Ref)!) || this.view.UnderCheck.Contains(c.Id) || !this.UsableState(c.Status))
                {
                    continue;
                }

                var matches = new List<Term?[]>();
                if (!c.Type.HasVar)
                {
                    matches.Add(new Term?[c.VarCount]);
                }
                else
                {
                    foreach (var u in this.universe.ToArray())
                    {
                        var beta = new Term?[c.VarCount];
                        if (Syntactic(c.Type, u, beta))
                        {
                            matches.Add(beta);
                        }
                    }
                }

                foreach (var beta in matches)
                {
                    if (!c.Conditions.All(x => this.Holds(x.Subst(this.m.Terms, beta))))
                    {
                        continue;
                    }

                    changed |= this.Conclude(new Formula(FK.Is, c.Type, c.Ref), beta);
                    foreach (var b in c.Bindings)
                    {
                        if (this.UsableState(b.Status) && !this.view.Inadmissible.Contains(b.Id) && (this.bindings is null || this.bindings.Contains(b.Id)))
                        {
                            changed |= this.Conclude(new Formula(FK.Eq, b.Projection, b.Value), beta);
                        }
                    }
                }
            }

            foreach (var (kind, a, b) in this.atoms.ToArray())
            {
                if (kind == 2 && this.Find(a) != this.Find(b))
                {
                    this.Union(a, b);
                    changed = true;
                }
            }
        }
    }

    // SPEC 8.3.3, read literally: C is recursive when `T0 is C<P̄>` (fresh T0, P̄) supplies, through refinement and clause rows
    // alone, a conformance of a projection of T0 to C or to a Contract refining C. Bounded by this evaluator's bound.
    private HashSet<ContractDecl> Classify()
    {
        var result = new HashSet<ContractDecl>();
        foreach (var c in this.m.Decls)
        {
            var t0 = this.m.Terms.Make(new SymHead(SymKind.Opaque, "T0"));
            var args = Enumerable.Range(0, c.Arity).Select(i => this.m.Terms.Make(new SymHead(SymKind.Opaque, "P" + i))).ToArray();
            var probe = new Reference(this.m, View.Ordinary, includePending: true, Math.Min(this.bound, 5), null, 4_000, rowsOnly: true);
            probe.Run([t0], [new Formula(FK.Is, t0, this.m.RefOf(c, args))]);
            foreach (var (kind, a, b) in probe.atoms)
            {
                var d = this.m.DeclOf(this.m.Terms.All[b]);
                if (kind == 0 && d is not null && (d == c || this.Refines(d, c, new())) && probe.universe.Any(u => probe.Find(u.Id) == probe.Find(a) && RootedProjection(u, t0)))
                {
                    result.Add(c);
                    break;
                }
            }
        }

        return result;

        static bool RootedProjection(Term u, Term t0) => u.Head is SymHead { Kind: SymKind.Proj } && (ReferenceEquals(u.Kids[0], t0) || RootedProjection(u.Kids[0], t0));
    }

    private bool Refines(ContractDecl d, ContractDecl c, HashSet<ContractDecl> seen)
        => seen.Add(d) && d.Parents.Any(p => this.m.DeclOf(p) is { } q && (q == c || this.Refines(q, c, seen)));

    private static IEnumerable<Formula> Split(Formula f)
    {
        yield return f; // a conjunction stays available as written
        if (f.Kind == FK.And)
        {
            foreach (var x in Split(f.L!).Concat(Split(f.R!)))
            {
                yield return x;
            }
        }
    }

    private bool UsableState(Status s) => s == Status.Valid || (s == Status.Pending && this.includePending);

    private static bool Syntactic(Term p, Term u, Term?[] beta)
    {
        if (p.Head is VarHead v)
        {
            if (beta[v.Index] is { } b)
            {
                return ReferenceEquals(b, u);
            }

            beta[v.Index] = u;
            return true;
        }

        if (!p.Head.Equals(u.Head) || p.Kids.Length != u.Kids.Length)
        {
            return false;
        }

        for (var i = 0; i < p.Kids.Length; i++)
        {
            if (!Syntactic(p.Kids[i], u.Kids[i], beta))
            {
                return false;
            }
        }

        return true;
    }

    private IEnumerable<Term> Members(int c) => this.members.TryGetValue(this.Find(c), out var list) ? list.ToArray() : [this.m.Terms.All[c]];

    private bool Conclude(Formula conclusion, Term?[] beta)
    {
        var f = conclusion.Subst(this.m.Terms, beta);
        var all = new List<Term>();
        f.Terms(all);
        if (all.Any(x => x.HasVar || x.Depth > this.bound))
        {
            return false; // a row instance with a free variable invents nothing; a deeper Type is outside the bound
        }

        return this.AddFormula(f);
    }

    private bool AddFormula(Formula f)
    {
        var all = new List<Term>();
        f.Terms(all);
        foreach (var t in all)
        {
            this.AddTerm(t);
        }

        switch (f.Kind)
        {
            case FK.Is:
                return this.atoms.Add((0, f.A!.Id, f.B!.Id));
            case FK.Eq:
                return this.atoms.Add((2, f.A!.Id, f.B!.Id));
            case FK.Not when f.L!.Kind == FK.Is:
                return this.atoms.Add((1, f.L.A!.Id, f.L.B!.Id));
            case FK.Not when f.L!.Kind == FK.Eq:
                return this.atoms.Add((3, f.L.A!.Id, f.L.B!.Id));
            default:
                if (!this.compoundSeen.Add(f.ToString()))
                {
                    return false;
                }

                this.compounds.Add(f);
                return true;
        }
    }

    private void AddTerm(Term t)
    {
        if (this.inUniverse.Contains(t.Id))
        {
            return;
        }

        foreach (var k in t.Kids)
        {
            this.AddTerm(k);
        }

        this.inUniverse.Add(t.Id);
        this.universe.Add(t);
        this.parent[t.Id] = t.Id;
        if (this.universe.Count > this.maxTerms)
        {
            this.Overflow = true;
        }
    }

    private int FindTerm(Term t)
    {
        this.AddTerm(t);
        return this.Find(t.Id);
    }

    private int Find(int x)
    {
        while (this.parent[x] != x)
        {
            this.parent[x] = this.parent[this.parent[x]];
            x = this.parent[x];
        }

        return x;
    }

    private void Union(int a, int b)
    {
        a = this.Find(a);
        b = this.Find(b);
        if (a != b)
        {
            this.parent[b] = a;
        }
    }

    // Upward congruence closure over the materialized Types.
    private bool Congruence()
    {
        var any = false;
        var changed = true;
        while (changed)
        {
            changed = false;
            var seen = new Dictionary<(Head, int, int, int, int), int>();
            foreach (var t in this.universe)
            {
                if (t.Kids.Length == 0)
                {
                    continue;
                }

                if (t.Kids.Length > 4)
                {
                    throw new NotSupportedException("arity");
                }

                var key = (t.Head, this.Find(t.Kids[0].Id), t.Kids.Length > 1 ? this.Find(t.Kids[1].Id) : -1, t.Kids.Length > 2 ? this.Find(t.Kids[2].Id) : -1, t.Kids.Length > 3 ? this.Find(t.Kids[3].Id) : -1);
                if (seen.TryGetValue(key, out var other))
                {
                    if (this.Find(other) != this.Find(t.Id))
                    {
                        this.Union(other, t.Id);
                        changed = any = true;
                    }
                }
                else
                {
                    seen[key] = t.Id;
                }
            }
        }

        return any;
    }

    private string Canon(Formula f) => f.Kind switch
    {
        FK.Is or FK.Eq => $"{f.Kind}({this.FindTerm(f.A!)},{this.FindTerm(f.B!)})",
        FK.Not => $"Not({this.Canon(f.L!)})",
        _ => $"{f.Kind}({this.Canon(f.L!)},{this.Canon(f.R!)})",
    };
}
