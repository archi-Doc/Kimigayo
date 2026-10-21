// G170 C1 prototype: the exhaustive bounded reference evaluator. No selection: every rule instance over every fact is applied,
// forward, until nothing changes; identity is a union-find over the materialized Types closed under congruence. The only
// difference from the unbounded semantics is the bound: a Type deeper than the bound is never materialized, so an answer is an
// oracle only where it is stable under a larger bound (Runner checks bound and bound + 2).
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
    private readonly Dictionary<int, int> parent = new();
    private readonly List<Term> universe = new();
    private readonly HashSet<int> inUniverse = new();
    private readonly HashSet<(byte Kind, int A, int B)> atoms = new();
    private readonly List<Formula> compounds = new();
    private readonly HashSet<string> compoundSeen = new();
    private readonly Dictionary<int, List<Term>> members = new();

    public Reference(ProgramModel model, View view, bool includePending, int bound, HashSet<int>? bindings = null, int maxTerms = 40_000)
    {
        this.m = model;
        this.view = view;
        this.includePending = includePending;
        this.bound = bound;
        this.bindings = bindings;
        this.maxTerms = maxTerms;
    }

    public bool Overflow { get; private set; }

    public int Iterations { get; private set; }

    public int TermCount => this.universe.Count;

    public void Run(IEnumerable<Term> seeds)
    {
        foreach (var t in seeds)
        {
            this.AddTerm(t);
        }

        foreach (var t in this.m.Templates.Where(x => x.Kind == TemplateKind.Input))
        {
            this.AddFormula(t.Conclusion);
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

            var snapshot = this.atoms.Where(a => a.Kind == 0).Select(a => (a.Kind, this.Find(a.A), a.B)).Distinct().ToArray();
            foreach (var t in this.m.Templates)
            {
                if (t.Kind == TemplateKind.Input || !this.Usable(t))
                {
                    continue;
                }

                if (t.Antecedent is { } k)
                {
                    foreach (var (_, s, r) in snapshot)
                    {
                        var reference = this.m.Terms.All[r];
                        if (this.m.DeclOf(reference) != k)
                        {
                            continue;
                        }

                        // Every member of the subject's class within the bound, the reference's own arguments.
                        foreach (var subject in this.Members(s))
                        {
                            var beta = new Term?[t.VarCount];
                            beta[0] = subject;
                            for (var i = 0; i < k.Arity; i++)
                            {
                                beta[i + 1] = reference.Kids[i];
                            }

                            changed |= this.Conclude(t, beta);
                        }
                    }
                }
                else
                {
                    // Verified conformances: a ground one always applies; a generic one only at Types already materialized (no Type
                    // is invented).
                    if (!t.Conclusion.A!.HasVar && !t.Conclusion.B!.HasVar)
                    {
                        if (t.Conditions.All(c => this.Holds(c)))
                        {
                            changed |= this.Conclude(t, new Term?[t.VarCount]);
                        }

                        continue;
                    }

                    var pattern = t.Conclusion.Kind == FK.Is ? t.Conclusion.A! : FindTypePattern(t.Conclusion.A!);
                    foreach (var u in this.universe.ToArray())
                    {
                        var beta = new Term?[t.VarCount];
                        if (!Syntactic(pattern, u, beta))
                        {
                            continue;
                        }

                        if (t.Conditions.All(c => this.Holds(c.Subst(this.m.Terms, beta))))
                        {
                            changed |= this.Conclude(t, beta);
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

    private static Term FindTypePattern(Term projection) => projection.Head is SymHead { Kind: SymKind.Proj } ? projection.Kids[0] : projection;

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

    private bool Usable(Template t)
    {
        if (t.BindingId >= 0 && (this.view.Inadmissible.Contains(t.BindingId) || (this.bindings is not null && !this.bindings.Contains(t.BindingId))))
        {
            return false;
        }

        return t.Source == Status.Valid || this.includePending;
    }

    private IEnumerable<Term> Members(int c) => this.members.TryGetValue(this.Find(c), out var list) ? list.ToArray() : [this.m.Terms.All[c]];

    private bool Conclude(Template t, Term?[] beta)
    {
        if (beta.Any(x => x is null) && t.VarCount != 0)
        {
            var vars = new HashSet<int>();
            var terms = new List<Term>();
            t.Conclusion.Terms(terms);
            foreach (var x in terms)
            {
                x.Vars(vars);
            }

            if (vars.Any(v => beta[v] is null))
            {
                return false;
            }
        }

        var f = t.Conclusion.Subst(this.m.Terms, beta);
        var all = new List<Term>();
        f.Terms(all);
        if (all.Any(x => x.Depth > this.bound))
        {
            return false;
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
