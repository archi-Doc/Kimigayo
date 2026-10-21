// G170 C1 prototype of C (PlanB §4.3): request-local demand evaluation over the existing clause templates.
//
// Contract (the design's ContractSolver entry operations, in prototype form):
// - Supply(goal, view): the available evidence for a bound proposition and for its negation; Complete, Provisional or Resource.
// - Enumerate(subject, view): the constraints available on a subject (with discovery: also through pending sources).
// Inputs are the finite model (declared inputs, Contract clause templates, verified conformances and their bindings) and the
// evidence view; nothing writes the model. All rows are request-local: nothing semantic survives the request.
//
// Algorithm: magic-set selection plus semi-naive firing, in Jacobi rounds (each round computes its effects from the previous
// round's state only, so the state after round k, its size and the round count are functions of the request: a Resource outcome
// never depends on query or declaration order). Identity is an e-graph (EGraph); templates are selected by e-matching their
// conclusion against a demanded class. A demand is a (Contract declaration, subject class or star, argument classes or star) row; a
// class demand collects the identity facts with a side in one class. An identity row whose match leaves Self unbound (a closed or
// parameter-only side) is a virtual node of the class, expanded where a pattern needs its head or where the class meets a class
// holding facts of a demanded Contract; static row indexes (heads per Contract position, which polarities a Contract can have)
// skip impossible expansions and settle impossible polarities. A support records its template, antecedent facts, the binding it read
// and the identity facts of its classes. Polarity tags, recomputed every round from the goal rows, say which goal polarity a row
// serves; a polarity settles when its evidence is established (in a frozen view: by a read-free support) or none of its rows has
// work left. A subscription whose conclusion is fixed by its bindings needs one witness (outside frozen views).
#pragma warning disable
#nullable enable

namespace G170C1;

public sealed class Limits
{
    public int Rounds { get; init; } = 4096;

    /// <summary>Gets the materialization limit: rows (demands, class demands, subscriptions), supports and identity nodes.</summary>
    public long Size { get; init; } = 200_000;

    /// <summary>Gets the join-work limit (matching steps, fact scans, congruence repairs) of one request.</summary>
    public long Work { get; init; } = 20_000_000;

    /// <summary>Gets the per-fact cap on read alternatives (a Resource when exceeded).</summary>
    public int Alternatives { get; init; } = 64;
}

public enum Outcome : byte { Complete, Provisional, Resource }

public sealed class Answer
{
    public bool Positive { get; set; }

    public bool Negative { get; set; }

    public Outcome Outcome { get; set; }

    public string Reason { get; set; } = string.Empty;

    public int Rounds { get; set; }

    /// <summary>Gets or sets V: requested rows (demands, class demands, subscriptions).</summary>
    public long V { get; set; }

    /// <summary>Gets or sets E: support edges (one per support plus one per antecedent it names).</summary>
    public long E { get; set; }

    /// <summary>Gets or sets J: join work (matching steps, fact scans, congruence repairs).</summary>
    public long J { get; set; }

    /// <summary>Gets or sets N: identity nodes materialized.</summary>
    public long N { get; set; }

    public long PeakSize { get; set; }

    public List<int[]> PositiveReads { get; set; } = new();

    public List<int[]> NegativeReads { get; set; } = new();

    public List<string> Facts { get; set; } = new();

    public List<string> Members { get; set; } = new();

    public string Detail { get; set; } = string.Empty;

    /// <summary>Gets or sets the goal polarities established (or impossible) when the request stopped (bit 1 positive, bit 2 negated).</summary>
    public byte EstablishedAtStop { get; set; }

    public string Verdict => this.Outcome == Outcome.Resource ? "Resource" : (this.Positive ? "+" : "") + (this.Negative ? "-" : "") + (!this.Positive && !this.Negative ? "none" : string.Empty);

    public override string ToString() => $"{this.Verdict} {this.Outcome}{(this.Reason.Length != 0 ? " (" + this.Reason + ")" : string.Empty)} rounds={this.Rounds} V={this.V} E={this.E} J={this.J} N={this.N}";
}

public sealed class Solver
{
    private const byte Pos = 1;
    private const byte Neg = 2;
    private const byte Both = 3;

    private readonly ProgramModel m;
    private readonly Dictionary<ContractDecl, List<Template>> isTemplates = new();
    private readonly List<Template> eqTemplates = new();
    private readonly List<Template> compoundTemplates = new();
    private readonly List<Template> inputs = new();
    private readonly List<Term> groundSubterms = new();
    private readonly Dictionary<ContractDecl, (Term Subject, Term Ref)> antecedentPattern = new();
    private readonly Dictionary<(ContractDecl Decl, int Position), HashSet<Head>> positionHeads = new();
    private readonly HashSet<(ContractDecl Decl, int Position)> anyHead = new();

    private View view = View.Ordinary;
    private Limits limits = new();
    private bool includePending;
    private bool exhaustive;
    private EGraph g = new();
    private List<Demand> demands = new();
    private Dictionary<DemandKey, Demand> demandIndex = new();
    private Dictionary<int, ClassDemand> idents = new();
    private Dictionary<int, ClassDemand> compounds = new();
    private List<Sub> subs = new();
    private Dictionary<SubKey, Sub> subIndex = new();
    private List<Fact> facts = new();
    private List<int> factAlias = new();
    private Dictionary<FactKey, int> factIndex = new();
    private Dictionary<ContractDecl, List<int>> positiveIs = new();
    private Dictionary<int, List<Virtual>> virtuals = new();
    private HashSet<(int T, int Class, string Bind, Row Owner, byte Sign)> expansions = new();
    private bool provisional;
    private bool mergedSinceScan;
    private long work;
    private Goal goal = null!;

    public Solver(ProgramModel model)
    {
        this.m = model;
        foreach (var decl in model.Decls)
        {
            this.isTemplates[decl] = new();
            this.antecedentPattern[decl] = (model.Terms.Var(0), model.SelfRef(decl));
        }

        foreach (var t in model.Templates)
        {
            if (t.Kind == TemplateKind.Input)
            {
                this.inputs.Add(t);
                continue;
            }

            var c = t.Conclusion;
            if (!c.IsAtom)
            {
                this.compoundTemplates.Add(t);
            }
            else if ((c.Kind == FK.Not ? c.L! : c).Kind == FK.Eq)
            {
                this.eqTemplates.Add(t);
            }
            else if (model.DeclOf((c.Kind == FK.Not ? c.L! : c).B!) is { } decl)
            {
                this.isTemplates[decl].Add(t);
            }

            var all = new List<Term>();
            c.Terms(all);
            foreach (var cond in t.Conditions)
            {
                cond.Terms(all);
            }

            foreach (var term in all)
            {
                CollectGround(term, this.groundSubterms);
            }
        }

        this.SubjectHeads();

        static void CollectGround(Term t, List<Term> into)
        {
            if (!t.HasVar)
            {
                into.Add(t);
                return;
            }

            foreach (var k in t.Kids)
            {
                CollectGround(k, into);
            }
        }
    }

    public int TemplateCount => this.m.Templates.Count;

    public Action<string>? Trace { get; set; }

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

    /// <summary>Entry 1: the available evidence for a bound proposition and for its negation.</summary>
    public Answer Supply(Formula proposition, View view, Limits? limits = null)
    {
        this.Reset(view, limits, includePending: false, exhaustive: false);
        this.goal = new Goal(proposition);
        this.SeedGoal();
        var answer = this.Run();
        if (answer.Outcome != Outcome.Resource)
        {
            (answer.Positive, answer.Negative) = this.Holds(proposition);
            if (view.WantsReads)
            {
                this.Reads(answer);
            }
        }

        return answer;
    }

    /// <summary>Entry 2: the constraints available on a subject; with discovery, those available through pending sources too.</summary>
    public Answer Enumerate(Term subject, View view, Limits? limits = null, bool discovery = false)
    {
        this.Reset(view, limits, includePending: discovery, exhaustive: true);
        this.goal = new Goal(subject);
        this.SeedGoal();
        var answer = this.Run();
        if (answer.Outcome != Outcome.Resource)
        {
            var s = this.g.Find(this.goal.SubjectClass);
            foreach (var f in this.CanonicalFacts())
            {
                if (f.Kind <= 1 && this.g.Find(f.A) == s)
                {
                    answer.Facts.Add((f.Kind == 0 ? "+" : "-") + this.g.Show(f.B));
                }
            }

            answer.Facts.Sort(StringComparer.Ordinal);
            foreach (var node in this.g.NodesOf(s))
            {
                answer.Members.Add(this.g.HeadOf(node).Show(Enumerable.Range(0, this.g.KidCount(node)).Select(i => this.g.Show(this.g.KidOf(node, i))).ToArray()));
            }

            answer.Members = answer.Members.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        return answer;
    }

    /// <summary>Gets the facts the last request established on a subject's class, as (positive, contract reference) pairs whose
    /// reference is printed through a member of its class.</summary>
    public IEnumerable<(bool Positive, ContractDecl Decl, int RefClass)> FactsOn(Term subject)
    {
        var s = this.g.LookupTerm(subject);
        if (s < 0)
        {
            yield break;
        }

        s = this.g.Find(s);
        foreach (var f in this.CanonicalFacts())
        {
            if (f.Kind <= 1 && this.g.Find(f.A) == s)
            {
                yield return (f.Kind == 0, f.Decl!, this.g.Find(f.B));
            }
        }
    }

    public string ShowClass(int c) => this.g.Show(c);

    /// <summary>Debug: the subscriptions of the last request, as order-free text.</summary>
    public List<string> DumpSubs() => this.subs.Select(x => $"#{x.T.Id} st{x.Stage} on {x.On.Decl}({(x.On.Subject >= 0 ? this.g.Show(x.On.Subject) : "*")};{string.Join(",", x.On.Args.Select(a => a >= 0 ? this.g.Show(a) : "*"))}) by {(x.Requester is Demand d ? "D:" + d.Decl + "(" + (d.Subject >= 0 ? this.g.Show(d.Subject) : "*") + ")" : "C:" + this.g.Show(((ClassDemand)x.Requester).Class))} beta [{string.Join(",", x.Beta.Select(b => b is int i ? this.g.Show(i) : "*"))}] sign {x.Sign} ants {x.Ants.Length}").OrderBy(x => x, StringComparer.Ordinal).ToList();

    private void Reset(View view, Limits? limits, bool includePending, bool exhaustive)
    {
        this.view = view;
        this.limits = limits ?? new();
        this.includePending = includePending;
        this.exhaustive = exhaustive;
        this.g = new();
        this.demands = new();
        this.demandIndex = new();
        this.idents = new();
        this.compounds = new();
        this.subs = new();
        this.subIndex = new();
        this.facts = new();
        this.factAlias = new();
        this.factIndex = new();
        this.positiveIs = new();
        this.virtuals = new();
        this.expansions = new();
        this.provisional = false;
        this.mergedSinceScan = false;
        this.work = 0;
    }

    private void SeedGoal()
    {
        foreach (var t in this.groundSubterms)
        {
            this.g.AddTerm(t);
        }

        // Inputs: finite declared assumptions, seeded as facts (exact compounds stay as written) and identity unions.
        foreach (var t in this.inputs)
        {
            this.Fire(t, [], []);
        }

        var goal = this.goal;
        if (goal.Proposition is { } p)
        {
            var terms = new List<Term>();
            p.Terms(terms);
            foreach (var t in terms)
            {
                this.g.AddTerm(t);
            }

            var atom = p.Kind == FK.Not ? p.L! : p;
            var sign = p.Kind == FK.Not ? Neg : Pos;
            goal.Impossible = 0;
            if (!p.IsAtom)
            {
                goal.SubjectClass = this.g.AddTerm(p.Subject);
                this.EnsureClassDemand(this.compounds, goal.SubjectClass).Root = Both;
                this.EnsureClassDemand(this.idents, goal.SubjectClass).Root = Both;
            }
            else if (atom.Kind == FK.Is)
            {
                goal.SubjectClass = this.g.AddTerm(atom.A!);
                var decl = this.m.DeclOf(atom.B!)!;
                var d = this.EnsureDemand(decl, goal.SubjectClass, atom.B!.Kids.Select(this.g.AddTerm).ToArray());

                // A polarity no row can conclude for this declaration is settled from the start (a static row-index fact).
                var positive = this.Concludes(decl, negated: false);
                var negated = this.Concludes(decl, negated: true);
                d.RootPos = positive ? sign : (byte)0;
                d.RootNeg = negated ? (byte)(Both & ~sign) : (byte)0;
                this.EnsureClassDemand(this.idents, goal.SubjectClass).Root = (byte)(d.RootPos | d.RootNeg);
                goal.Impossible = (byte)(Both & ~(d.RootPos | d.RootNeg));
            }
            else
            {
                goal.SubjectClass = this.g.AddTerm(atom.A!);
                var a = this.EnsureClassDemand(this.idents, goal.SubjectClass);
                var b = this.EnsureClassDemand(this.idents, this.g.AddTerm(atom.B!));
                a.Root = b.Root = Both;
                a.NegatedFor = b.NegatedFor = (byte)(Both & ~sign);
            }
        }
        else
        {
            goal.SubjectClass = this.g.AddTerm(goal.Subject!);
            foreach (var decl in this.m.Decls)
            {
                var d = this.EnsureDemand(decl, goal.SubjectClass, Enumerable.Repeat(-1, decl.Arity).ToArray());
                d.RootPos = d.RootNeg = Both;
            }

            this.EnsureClassDemand(this.idents, goal.SubjectClass).Root = Both;
        }

        this.AfterApply();
    }

    private Answer Run()
    {
        var answer = new Answer();
        var rounds = 0;
        while (true)
        {
            this.Propagate();
            var established = (byte)(this.Established() | this.goal.Impossible);
            answer.EstablishedAtStop = established;
            var active = this.exhaustive ? Both : (byte)(Both & ~established);
            var fx = this.Compute(active);
            if (this.Trace is not null)
            {
                foreach (var f in fx.Fires)
                {
                    this.Trace($"  fire {f.T} with [{string.Join(", ", f.Beta.Select(x => x is int i ? this.g.Show(i) : "*"))}] ants [{string.Join(",", f.Ants)}]");
                }

                foreach (var x in fx.Subs)
                {
                    this.Trace($"  sub {x.T} on {x.Decl}({(x.Subject >= 0 ? this.g.Show(x.Subject) : "*")}; {string.Join(", ", x.Args.Select(a => a >= 0 ? this.g.Show(a) : "*"))}) by {x.Requester.GetType().Name} tags {x.Tags}");
                }
            }

            this.Trace?.Invoke($"round {rounds}: established {established} active {active} effects subs {fx.Subs.Count}({string.Join(",", fx.Subs.Select(x => x.Tags).Distinct())}) fires {fx.Fires.Count}({string.Join(",", fx.Fires.Select(x => x.Tags).Distinct())}) idents {fx.Idents.Count}({fx.IdentTags}) virtuals {fx.Virtuals.Count}({fx.VirtualTags}) rows {this.demands.Count}/{this.idents.Count}/{this.subs.Count} nodes {this.g.Nodes}");
            var settled = this.exhaustive ? fx.Count == 0 : ((established | (byte)(~fx.Tags & Both)) & Both) == Both;
            if (settled || fx.Count == 0)
            {
                answer.Outcome = this.provisional ? Outcome.Provisional : Outcome.Complete;
                break;
            }

            if (++rounds > this.limits.Rounds)
            {
                answer.Outcome = Outcome.Resource;
                answer.Reason = $"rounds>{this.limits.Rounds}";
                break;
            }

            if (this.work + this.g.Work > this.limits.Work)
            {
                answer.Outcome = Outcome.Resource;
                answer.Reason = $"work>{this.limits.Work}";
                break;
            }

            this.Apply(fx);
            var size = this.Size();
            answer.PeakSize = Math.Max(answer.PeakSize, size);
            if (size > this.limits.Size)
            {
                answer.Outcome = Outcome.Resource;
                answer.Reason = $"size {size}>{this.limits.Size}";
                break;
            }
        }

        answer.Rounds = rounds;
        answer.V = this.demands.Count + this.idents.Count + this.compounds.Count + this.subs.Count + this.virtuals.Values.Sum(x => x.Count);
        answer.Detail = $"demands {this.demands.Count} idents {this.idents.Count} compounds {this.compounds.Count} subs {this.subs.Count} virtuals {this.virtuals.Values.Sum(x => x.Count)}";
        answer.E = this.facts.Sum(f => f.Supports.Sum(s => 1 + s.Ants.Length));
        answer.N = this.g.Nodes;
        answer.J = this.work + this.g.Work;
        answer.PeakSize = Math.Max(answer.PeakSize, this.Size());
        return answer;
    }

    private long Size() => this.demands.Count + this.idents.Count + this.compounds.Count + this.subs.Count + this.virtuals.Values.Sum(x => x.Count) + this.facts.Sum(f => f.Supports.Count) + this.g.Nodes;

    // Tags, recomputed from the goal rows every round: a row serves the polarities of the rows that requested it.
    private void Propagate()
    {
        foreach (var d in this.demands)
        {
            d.PosServe = d.RootPos;
            d.NegServe = d.RootNeg;
        }

        foreach (var c in this.idents.Values.Concat(this.compounds.Values))
        {
            c.TagsValue = c.Root;
        }

        foreach (var s in this.subs)
        {
            s.Tags = 0;
        }

        var queue = new Queue<Row>(this.demands.Where(d => d.Tags != 0).Cast<Row>().Concat(this.idents.Values.Concat(this.compounds.Values).Where(c => c.Tags != 0)));
        while (queue.Count != 0)
        {
            var row = queue.Dequeue();
            foreach (var (v, sign) in row.VisitSigns)
            {
                var t = row is Demand vd ? (byte)(((sign & Pos) != 0 ? vd.PosServe : 0) | ((sign & Neg) != 0 ? vd.NegServe : 0)) : row.Tags;
                if (this.idents.TryGetValue(this.g.Find(v), out var ident) && (ident.Tags | t) != ident.Tags)
                {
                    ident.TagsValue |= t;
                    queue.Enqueue(ident);
                }
            }

            foreach (var s in row.Requested)
            {
                // A subscription serves what its requesting match served: the positive or negated facts of a demand, or both
                // (a meeting), or a class demand's tags.
                var t = row is Demand d ? (s.Sign == Neg ? d.NegServe : s.Sign == Pos ? d.PosServe : d.Tags) : ((ClassDemand)row).Tags;
                if (s.Retired || (s.Tags | t) == s.Tags)
                {
                    continue;
                }

                s.Tags |= t;
                var on = s.On;
                if ((on.PosServe | s.Tags) != on.PosServe)
                {
                    on.PosServe |= s.Tags;
                    queue.Enqueue(on);
                }
            }
        }
    }

    private byte Established()
    {
        if (this.goal.Proposition is not { } p)
        {
            return 0;
        }

        var (pos, neg) = this.Holds(p);
        if (this.view.WantsReads && (pos || neg))
        {
            // In a frozen view a polarity is settled only by a support that reads no binding: every other alternative set is a
            // superset of the empty one, so no further support can change the reads.
            var free = this.ReadFree();
            (pos, neg) = this.HoldsReadFree(p, free);
        }

        return (byte)((pos ? Pos : 0) | (neg ? Neg : 0));
    }

    // The facts with a support that reads no binding, transitively (antecedents and the identity facts of its classes).
    private HashSet<int> ReadFree()
    {
        var free = new HashSet<int>();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var f in this.CanonicalFacts())
            {
                if (free.Contains(f.Id))
                {
                    continue;
                }

                foreach (var s in f.Supports)
                {
                    if (s.Binding < 0 && s.Ants.All(a => free.Contains(this.FactFind(a))) && s.Explain.All(e => free.Contains(this.FactFind(e))))
                    {
                        free.Add(f.Id);
                        changed = true;
                        break;
                    }
                }
            }
        }

        return free;
    }

    private (bool Pos, bool Neg) HoldsReadFree(Formula p, HashSet<int> free)
    {
        var atom = p.Kind == FK.Not ? p.L! : p;
        if (!p.IsAtom)
        {
            return (false, false); // compounds: no early settlement in a frozen view
        }

        bool yes;
        bool no;
        if (atom.Kind == FK.Is)
        {
            var decl = this.m.DeclOf(atom.B!)!;
            var s = this.g.Find(this.g.LookupTerm(atom.A!));
            var r = this.g.Find(this.g.LookupTerm(atom.B!));
            // The goal's own Types joined the fact's classes through identity facts: those must be read-free too.
            var joined = this.g.Explain(s).Concat(this.g.Explain(r)).All(e => free.Contains(this.FactFind(e)));
            yes = joined && this.factIndex.TryGetValue(new(0, decl, s, r, null), out var y) && free.Contains(y);
            no = joined && this.factIndex.TryGetValue(new(1, decl, s, r, null), out var n) && free.Contains(n);
        }
        else
        {
            var a = this.g.Find(this.g.LookupTerm(atom.A!));
            var b = this.g.Find(this.g.LookupTerm(atom.B!));
            yes = a == b && this.g.Explain(a).All(e => free.Contains(this.FactFind(e)));
            no = this.factIndex.TryGetValue(new(3, null, Math.Min(a, b), Math.Max(a, b), null), out var n) && free.Contains(n);
        }

        return p.Kind == FK.Not ? (no, yes) : (yes, no);
    }

    private (bool Pos, bool Neg) Holds(Formula p)
    {
        if (!p.IsAtom)
        {
            var key = this.Canon(p);
            var negated = this.Canon(Formula.Negate(p));
            var pos = false;
            var neg = false;
            foreach (var f in this.CanonicalFacts())
            {
                if (f.Kind == 4)
                {
                    var c = this.CanonOf(f);
                    pos |= c == key;
                    neg |= c == negated;
                }
            }

            return (pos, neg);
        }

        var atom = p.Kind == FK.Not ? p.L! : p;
        bool yes;
        bool no;
        if (atom.Kind == FK.Is)
        {
            var decl = this.m.DeclOf(atom.B!)!;
            var s = this.g.Find(this.g.LookupTerm(atom.A!));
            var r = this.g.Find(this.g.LookupTerm(atom.B!));
            yes = this.factIndex.ContainsKey(new(0, decl, s, r, null));
            no = this.factIndex.ContainsKey(new(1, decl, s, r, null));
        }
        else
        {
            var a = this.g.Find(this.g.LookupTerm(atom.A!));
            var b = this.g.Find(this.g.LookupTerm(atom.B!));
            yes = a == b;
            no = this.factIndex.ContainsKey(new(3, null, Math.Min(a, b), Math.Max(a, b), null));
        }

        return p.Kind == FK.Not ? (no, yes) : (yes, no);
    }

    private bool Concludes(ContractDecl decl, bool negated)
    {
        foreach (var t in this.m.Templates)
        {
            var c = t.Conclusion;
            if (c.Kind == FK.Not ? negated && c.L!.Kind == FK.Is && this.m.DeclOf(c.L.B!) == decl : !negated && c.Kind == FK.Is && this.m.DeclOf(c.B!) == decl)
            {
                return true;
            }
        }

        return false;
    }

    // ---- Rounds ----
    private Effects Compute(byte active)
    {
        var fx = new Effects();
        foreach (var d in this.demands.ToArray())
        {
            if ((!d.Selected || d.Dirty) && (d.Tags & active) != 0)
            {
                d.Selected = true;
                d.Dirty = false;
                this.SelectDemand(d, fx, active);
            }
        }

        foreach (var c in this.idents.Values.ToArray())
        {
            if ((!c.Selected || c.Dirty) && (c.Tags & active) != 0)
            {
                c.Selected = true;
                c.Dirty = false;
                this.SelectIdent(c, fx);
            }
        }

        foreach (var c in this.compounds.Values.ToArray())
        {
            if ((!c.Selected || c.Dirty) && (c.Tags & active) != 0)
            {
                c.Selected = true;
                c.Dirty = false;
                this.SelectCompound(c, fx);
            }
        }

        foreach (var s in this.subs.ToArray())
        {
            if (!s.Retired && (s.Tags & active) != 0)
            {
                this.Scan(s, fx);
            }
        }

        this.EagerVirtuals(fx, active);
        return fx;
    }

    // A virtual node expands, fully bound, against every existing node of its shape in another class (a finite expansion: the
    // node's kids bind the virtual's variables); only an unbound enumeration waits for a requester. Nodes are scanned once, and
    // again after merges (a merge can make a node match).
    private void EagerVirtuals(Effects fx, byte active)
    {
        var total = this.g.NodeTotal;
        foreach (var (c, list) in this.virtuals.ToArray())
        {
            if (!this.idents.TryGetValue(this.g.Find(c), out var owner) || (owner.Tags & active) == 0)
            {
                continue;
            }

            var ctx = new MatchCtx(this, owner, (byte)(owner.Tags & active), fx) { Sign = Both };
            var probe = new MatchCtx(this, null, 0, null);
            foreach (var vs in list.ToArray())
            {
                if (vs.Via is not null || vs.Other.Head is VarHead)
                {
                    continue;
                }

                var from = this.mergedSinceScan ? 0 : vs.Scanned;
                for (var node = from; node < total; node++)
                {
                    this.work++;
                    if (!this.g.HeadOf(node).Equals(vs.Other.Head) || this.g.KidCount(node) != vs.Other.Kids.Length || this.g.ClassOfNode(node) == this.g.Find(vs.Class))
                    {
                        continue;
                    }

                    var results = new List<int?[]>();
                    this.Match(vs.Other, this.g.ClassOfNode(node), vs.Beta, results, probe);
                    foreach (var beta in Distinct(results))
                    {
                        if (Unbound(vs.Other, beta).Count != 0)
                        {
                            continue;
                        }

                        var key = (vs.T.Id, this.g.Find(vs.Class), string.Join(",", beta.Select(x => x is int i ? this.g.Find(i).ToString() : "*")), (Row)owner, Both);
                        if (this.expansions.Add(key))
                        {
                            this.Request(vs.T, beta, ctx.Tags, Both, owner, fx);
                        }
                    }
                }

                vs.Scanned = total;
            }
        }

        this.mergedSinceScan = false;
    }

    private void SelectDemand(Demand d, Effects fx, byte active)
    {
        d.Visited.Clear();
        d.VisitSigns.Clear();
        foreach (var t in this.isTemplates[d.Decl])
        {
            var negative = t.Conclusion.Kind == FK.Not;
            var tags = (byte)((negative ? d.NegServe : d.PosServe) & active);
            if (tags == 0 || !this.Usable(t))
            {
                continue;
            }

            var atom = negative ? t.Conclusion.L! : t.Conclusion;
            var ctx = new MatchCtx(this, d, tags, fx) { Sign = negative ? Neg : Pos };
            // Bound arguments first: what they bind (often Self, as in D<Self>) narrows a virtual node's expansion at the subject.
            var results = new List<int?[]> { new int?[t.VarCount] };
            for (var i = 0; i < d.Args.Length && results.Count != 0; i++)
            {
                if (d.Args[i] < 0)
                {
                    continue;
                }

                var next = new List<int?[]>();
                foreach (var b in results)
                {
                    this.Match(atom.B!.Kids[i], d.Args[i], b, next, ctx);
                }

                results = next;
            }

            if (d.Subject >= 0 && results.Count != 0)
            {
                var next = new List<int?[]>();
                foreach (var b in results)
                {
                    this.Match(atom.A!, d.Subject, b, next, ctx);
                }

                results = next;
            }

            foreach (var beta in Distinct(results))
            {
                this.Request(t, beta, tags, negative ? Neg : Pos, d, fx);
            }
        }

        // Facts of this declaration on other classes are relevant when identity connects their class with the subject: meet them
        // (each needs its identity facts; a virtual node on one side expands against a real node, or a virtual node, on the other).
        if ((d.Tags & active) != 0)
        {
            // Bound arguments are identity-saturated, and a fact's arguments meet the demand's: `U is K<i32>` answers
            // `U is K<List<i32>>` when an identity row makes i32 and List<i32> one Type.
            var ctx = new MatchCtx(this, d, (byte)(d.Tags & active), fx) { Sign = Both };
            foreach (var a in d.Args)
            {
                if (a >= 0)
                {
                    ctx.Visit(this.g.Find(a));
                }
            }

            foreach (var f in this.CanonicalFacts().ToArray())
            {
                if (f.Kind > 1 || f.Decl != d.Decl)
                {
                    continue;
                }

                if (d.Subject >= 0 && this.g.Find(f.A) != this.g.Find(d.Subject))
                {
                    this.Meet(this.g.Find(d.Subject), this.g.Find(f.A), ctx);
                }

                if (d.Args.Any(a => a >= 0) && this.RefArgs(f.B, d.Decl) is { } args)
                {
                    for (var i = 0; i < d.Args.Length; i++)
                    {
                        if (d.Args[i] >= 0 && this.g.Find(args[i]) != this.g.Find(d.Args[i]))
                        {
                            this.Meet(this.g.Find(d.Args[i]), this.g.Find(args[i]), ctx);
                        }
                    }
                }
            }
        }

        if (d.Subject < 0 && (d.Tags & active) != 0)
        {
            // A star demand's facts are on classes nobody else visits: each needs its identity facts, which may collapse the
            // enumeration (t.(K).Y is i32 makes t.(K).Y.(K).Y the same Type as i32.(K).Y).
            var ctx = new MatchCtx(this, d, (byte)(d.Tags & active), fx) { Sign = Pos };
            foreach (var f in this.CanonicalFacts().ToArray())
            {
                if (f.Kind == 0 && f.Decl == d.Decl)
                {
                    ctx.Visit(this.g.Find(f.A));
                }
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

    private void Meet(int c, int other, MatchCtx ctx)
    {
        ctx.Visit(other);
        this.MeetOneWay(c, other, ctx);
        this.MeetOneWay(other, c, ctx);
    }

    private void MeetOneWay(int withVirtual, int other, MatchCtx ctx)
    {
        if (!this.virtuals.TryGetValue(this.g.Find(withVirtual), out var list))
        {
            return;
        }

        foreach (var vs in list.ToArray())
        {
            foreach (var node in this.g.NodesOf(other).ToArray())
            {
                this.work++;
                if (vs.Other.Head is VarHead || (vs.Other.Head.Equals(this.g.HeadOf(node)) && vs.Other.Kids.Length == this.g.KidCount(node)))
                {
                    this.ExpandAt(vs, node, ctx);
                }
            }

            if (this.virtuals.TryGetValue(this.g.Find(other), out var others))
            {
                foreach (var w in others.ToArray())
                {
                    if (vs.Other.Head is VarHead || w.Other.Head is VarHead || (vs.Other.Head.Equals(w.Other.Head) && vs.Other.Kids.Length == w.Other.Kids.Length))
                    {
                        this.Expand(vs, w.Other, w.Beta, ctx);
                    }
                }
            }
        }
    }

    // A virtual node meets a real node of the same shape: the real node's kids bind the virtual's variables.
    private void ExpandAt(Virtual vs, int node, MatchCtx ctx)
    {
        if (vs.Via is { } via)
        {
            this.Expand(via, vs.ViaPattern!, vs.ViaBeta!, ctx);
            return;
        }

        var bind = (int?[])vs.Beta.Clone();
        if (vs.Other.Head is not VarHead)
        {
            for (var i = 0; i < vs.Other.Kids.Length; i++)
            {
                var k = vs.Other.Kids[i];
                var kid = this.g.KidOf(node, i);
                if (k.Head is VarHead v)
                {
                    if (bind[v.Index] is int b && this.g.Find(b) != kid)
                    {
                        return;
                    }

                    bind[v.Index] = kid;
                }
                else if (!k.HasVar && this.g.LookupTerm(k) is var gk && (gk < 0 || this.g.Find(gk) != kid))
                {
                    return;
                }
            }
        }

        var key = (vs.T.Id, this.g.Find(vs.Class), string.Join(",", bind.Select(x => x is int i ? this.g.Find(i).ToString() : "*")), ctx.Owner!, ctx.Sign);
        if (this.expansions.Add(key))
        {
            this.Request(vs.T, bind, ctx.Tags, ctx.Sign, ctx.Owner!, ctx.Fx!);
        }
    }

    private void SelectIdent(ClassDemand c, Effects fx)
    {
        c.Visited.Clear();
        c.VisitSigns.Clear();
        foreach (var t in this.eqTemplates)
        {
            var negative = t.Conclusion.Kind == FK.Not;
            if ((negative && c.NegatedFor == 0) || !this.Usable(t))
            {
                continue;
            }

            var tags = negative ? (byte)(c.Tags & c.NegatedFor) : c.Tags;
            var atom = negative ? t.Conclusion.L! : t.Conclusion;
            for (var side = 0; side < 2; side++)
            {
                var pattern = side == 0 ? atom.A! : atom.B!;
                var other = side == 0 ? atom.B! : atom.A!;
                var results = new List<int?[]>();
                var ctx = new MatchCtx(this, c, tags, fx) { Sign = Both, Chain = new() };
                this.Match(pattern, c.Class, new int?[t.VarCount], results, ctx);
                foreach (var (hit, hitPattern, hitBeta) in ctx.Chain)
                {
                    // A chain always forwards to its base virtual (with that base's own pattern), so chains never stack.
                    var via = hit.Via ?? hit;
                    var viaPattern = hit.Via is null ? hitPattern : hit.ViaPattern!;
                    var viaBeta = hit.Via is null ? hitBeta : hit.ViaBeta!;
                    // The side matches only through another template's virtual node: this template's instance is virtual too,
                    // expanded by forwarding to the underlying virtual (whose real nodes this row then matches eagerly).
                    if (!negative && via.T != t && other.HasVar)
                    {
                        fx.Virtuals.Add(new Virtual(t, new int?[t.VarCount], other, c.Class) { Via = via, ViaPattern = viaPattern, ViaBeta = viaBeta });
                        fx.VirtualTags |= tags;
                    }
                }

                foreach (var beta in Distinct(results))
                {
                    var unbound = Unbound(other, beta);
                    if (unbound.Count != 0 && (t.Antecedent is null || beta[0] is null))
                    {
                        // Lazy: the other side depends on a subject this match does not bind (a closed or parameter-only side).
                        // It is a virtual node of the class, expanded only where some pattern needs its head (Expand).
                        if (!negative && (t.Antecedent is not null || t.Kind == TemplateKind.Binding))
                        {
                            fx.Virtuals.Add(new Virtual(t, beta, other, c.Class));
                            fx.VirtualTags |= tags;
                        }

                        continue;
                    }

                    this.Request(t, beta, tags, Pos, c, fx);
                }
            }
        }
    }

    private void SelectCompound(ClassDemand c, Effects fx)
    {
        c.Visited.Clear();
        c.VisitSigns.Clear();
        foreach (var t in this.compoundTemplates)
        {
            if (!this.Usable(t))
            {
                continue;
            }

            var results = new List<int?[]>();
            var ctx = new MatchCtx(this, c, c.Tags, fx) { Sign = Both };
            this.Match(t.Conclusion.Subject, c.Class, new int?[t.VarCount], results, ctx);
            foreach (var beta in Distinct(results))
            {
                this.Request(t, beta, c.Tags, Pos, c, fx);
            }
        }
    }

    private void Request(Template t, int?[] beta, byte tags, byte sign, Row requester, Effects fx)
    {
        if (t.Antecedent is { } k)
        {
            fx.Subs.Add(new(k, beta[0] ?? -1, Enumerable.Range(1, k.Arity).Select(i => beta[i] ?? -1).ToArray(), tags, sign, requester, t, beta, 0, []));
        }
        else if (t.Conditions.Count != 0)
        {
            if (this.Condition(t, 0, beta) is { } c)
            {
                fx.Subs.Add(new(c.Decl, c.Subject, c.Args, tags, sign, requester, t, beta, 0, []));
            }
        }
        else if (Unbound(t.Conclusion, beta).Count == 0)
        {
            fx.Fires.Add(new(t, beta, [], tags));
        }
    }

    private (ContractDecl Decl, int Subject, int[] Args)? Condition(Template t, int index, int?[] beta)
    {
        var cond = t.Conditions[index];
        if (cond.Kind != FK.Is || this.m.DeclOf(cond.B!) is not { } decl)
        {
            return null; // conditions are positive Contract atoms (SPEC 8.8.1)
        }

        return (decl, this.Resolve(cond.A!, beta), cond.B!.Kids.Select(k => this.Resolve(k, beta)).ToArray());
    }

    // A condition operand that is a bound variable or an existing ground Type has a class; anything else is matched at the scan.
    private int Resolve(Term t, int?[] beta)
        => t.Head is VarHead v ? beta[v.Index] ?? -1 : !t.HasVar ? this.g.LookupTerm(t) : -1;

    private void Scan(Sub s, Effects fx)
    {
        var d = s.On;
        if (!this.positiveIs.TryGetValue(d.Decl, out var list))
        {
            return;
        }

        var conditionIndex = s.T.Antecedent is null ? s.Stage : s.Stage - 1;
        var (subjectPattern, refPattern) = conditionIndex < 0 ? this.antecedentPattern[d.Decl] : (s.T.Conditions[conditionIndex].A!, s.T.Conditions[conditionIndex].B!);
        var ctx = new MatchCtx(this, null, s.Tags, null);
        foreach (var id in list.ToArray())
        {
            this.work++;
            var f = this.facts[this.FactFind(id)];
            if (s.Fired.Contains(f.Id) || (d.Subject >= 0 && this.g.Find(f.A) != this.g.Find(d.Subject)))
            {
                continue;
            }

            var results = new List<int?[]>();
            this.Match(subjectPattern, f.A, s.Beta, results, ctx);
            var next = new List<int?[]>();
            foreach (var b in results)
            {
                this.Match(refPattern, f.B, b, next, ctx);
            }

            if (next.Count == 0)
            {
                continue;
            }

            s.Fired.Add(f.Id);
            var ants = s.Ants.Append(f.Id).ToArray();
            foreach (var beta in Distinct(next))
            {
                var following = conditionIndex + 1;
                if (following < s.T.Conditions.Count)
                {
                    if (this.Condition(s.T, following, beta) is { } c)
                    {
                        fx.Subs.Add(new(c.Decl, c.Subject, c.Args, s.Tags, Pos, s.Requester, s.T, beta, s.Stage + 1, ants));
                    }
                }
                else if (Unbound(s.T.Conclusion, beta).Count == 0)
                {
                    fx.Fires.Add(new(s.T, beta, ants, s.Tags));
                }
            }

            if (s.Existential && !this.view.WantsReads)
            {
                s.Retired = true; // the conclusion is fixed by the subscription's bindings: one witness suffices
                break;
            }
        }
    }

    private bool Usable(Template t)
    {
        if (t.BindingId >= 0 && this.view.Inadmissible.Contains(t.BindingId))
        {
            return false; // the inference owner's published admissibility for this view (final for the view)
        }

        if (t.Source == Status.Valid || this.includePending)
        {
            return true;
        }

        if (t.Source == Status.Pending)
        {
            this.provisional = true; // a pending source could still supply: never a cached negative
        }

        return false;
    }

    // ---- Matching ----
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
            else if (ctx.Fx is not null && this.virtuals.TryGetValue(c, out var ground))
            {
                // A ground Type joins c if a virtual node of c has its shape: the expansion is fully bound by the ground Type,
                // so even an identity row expands it (no enumeration).
                foreach (var vs in ground.ToArray())
                {
                    if (vs.Other.Head is VarHead || (vs.Other.Head.Equals(p.Head) && vs.Other.Kids.Length == p.Kids.Length))
                    {
                        this.Expand(vs, p, beta, ctx);
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

        if (ctx.Fx is not null && this.virtuals.TryGetValue(c, out var list))
        {
            foreach (var vs in list)
            {
                if (vs.Other.Head is VarHead || (vs.Other.Head.Equals(p.Head) && vs.Other.Kids.Length == p.Kids.Length))
                {
                    if (ctx.Chain is { } chain)
                    {
                        chain.Add((vs, p, beta));
                    }
                    else
                    {
                        this.Expand(vs, p, beta, ctx);
                    }
                }
            }
        }
    }

    // A pattern needs nodes of a virtual source's head in this class: request the deferred antecedent, bound as far as the
    // requester's pattern resolves (a bound variable or an existing ground Type), on behalf of the requester.
    private void Expand(Virtual vs, Term p, int?[] beta, MatchCtx ctx)
    {
        if (vs.Via is { } via)
        {
            this.Expand(via, vs.ViaPattern!, vs.ViaBeta!, ctx);
            return;
        }

        var bind = (int?[])vs.Beta.Clone();
        var shapes = new Dictionary<int, Head>();
        if (!Unify(vs.Other, p, bind, beta))
        {
            return; // incompatible now; a later merge wakes the requester, which matches (and expands) again
        }

        if (vs.T.Antecedent is { } k)
        {
            foreach (var (v, shape) in shapes)
            {
                if (bind[v] is null && v <= k.Arity && !this.anyHead.Contains((k, v)) && !this.positionHeads[(k, v)].Contains(shape))
                {
                    return; // no fact of k has, at this variable's position, a Type of the shape the requester needs
                }
            }
        }

        var key = (vs.T.Id, this.g.Find(vs.Class), string.Join(",", bind.Select(x => x is int i ? this.g.Find(i).ToString() : "*")), ctx.Owner!, ctx.Sign);
        if (!this.expansions.Add(key))
        {
            return;
        }

        this.Request(vs.T, bind, ctx.Tags, ctx.Sign, ctx.Owner!, ctx.Fx!);

        // Binds the virtual's variables where the requester's pattern resolves to a class; false when they cannot match now.
        bool Unify(Term other, Term pattern, int?[] into, int?[] requester)
        {
            if (other.Head is VarHead ov)
            {
                var resolved = pattern.Head is VarHead pv ? requester[pv.Index] : !pattern.HasVar ? this.g.LookupTerm(pattern) : null;
                if (resolved is int r && r >= 0 && into[ov.Index] is int already && this.g.Find(already) != this.g.Find(r))
                {
                    return false; // both sides bound to different classes
                }

                if (resolved is int r2 && r2 >= 0 && into[ov.Index] is null)
                {
                    into[ov.Index] = this.g.Find(r2);
                }
                else if (pattern.Head is not VarHead)
                {
                    shapes[ov.Index] = pattern.Head;
                }

                return true;
            }

            if (pattern.Head is VarHead rv)
            {
                // A requester variable already bound to a class must agree with a ground side of the virtual.
                if (requester[rv.Index] is int bound && !other.HasVar)
                {
                    var o = this.g.LookupTerm(other);
                    return o >= 0 && this.g.Find(o) == this.g.Find(bound);
                }

                return true;
            }

            if (!other.HasVar && !pattern.HasVar)
            {
                var a = this.g.LookupTerm(other);
                var b = this.g.LookupTerm(pattern);
                return a >= 0 && b >= 0 && this.g.Find(a) == this.g.Find(b);
            }

            if (!other.Head.Equals(pattern.Head) || other.Kids.Length != pattern.Kids.Length)
            {
                return false;
            }

            for (var i = 0; i < other.Kids.Length; i++)
            {
                if (!Unify(other.Kids[i], pattern.Kids[i], into, requester))
                {
                    return false;
                }
            }

            return true;
        }
    }

    // ---- Applying a round ----
    private void Apply(Effects fx)
    {
        foreach (var c in fx.Idents)
        {
            this.EnsureClassDemand(this.idents, c);
        }

        var woken = new HashSet<int>();
        foreach (var vs in fx.Virtuals)
        {
            var c = this.g.Find(vs.Class);
            if (!this.virtuals.TryGetValue(c, out var list))
            {
                this.virtuals[c] = list = new();
            }

            if (!list.Any(x => this.VirtualKey(x) == this.VirtualKey(vs)))
            {
                list.Add(vs);
                woken.Add(c);
            }
        }

        // A new virtual node changes what a class can match: rows that visited it are selected again.
        if (woken.Count != 0)
        {
            foreach (var row in this.demands.Cast<Row>().Concat(this.idents.Values).Concat(this.compounds.Values))
            {
                row.Dirty |= row.Visited.Any(v => woken.Contains(this.g.Find(v)));
            }
        }

        foreach (var s in fx.Subs)
        {
            var on = this.EnsureDemand(s.Decl, s.Subject, s.Args);
            var key = new SubKey(s.T.Id, s.Stage, on, s.Requester, s.Sign, string.Join(",", s.Beta.Select(x => x is int i ? this.g.Find(i) : -1)) + "|" + string.Join(",", s.Ants.Select(this.FactFind)));
            if (this.subIndex.ContainsKey(key))
            {
                continue;
            }

            var sub = new Sub(this.subs.Count, s.T, s.Beta, s.Stage, on, s.Sign, s.Requester, s.Ants)
            {
                Existential = Unbound(s.T.Conclusion, s.Beta).Count == 0,
            };
            this.subs.Add(sub);
            this.subIndex.Add(key, sub);
            s.Requester.Requested.Add(sub);
        }

        var before = this.facts.Count;
        foreach (var f in fx.Fires)
        {
            this.Fire(f.T, f.Beta, f.Ants);
        }

        var gained = new HashSet<ContractDecl>();
        for (var i = before; i < this.facts.Count; i++)
        {
            if (this.facts[i].Decl is { } decl)
            {
                gained.Add(decl);
            }
        }

        if (gained.Count != 0)
        {
            foreach (var d in this.demands)
            {
                d.Dirty |= gained.Contains(d.Decl);
            }
        }

        this.AfterApply();
    }

    private void AfterApply()
    {
        var merged = this.g.Rebuild();
        if (merged.Count == 0)
        {
            return;
        }

        this.mergedSinceScan = true;

        // Wake-ups: every row whose last selection visited a merged class is selected again.
        foreach (var d in this.demands)
        {
            d.Dirty |= d.Visited.Any(merged.Contains) || merged.Contains(d.Subject) || d.Args.Any(merged.Contains);
        }

        foreach (var c in this.idents.Values.Concat(this.compounds.Values))
        {
            c.Dirty |= c.Visited.Any(merged.Contains) || merged.Contains(c.Class);
        }

        this.Rekey();
    }

    private void Rekey()
    {
        // Demands, class demands, virtual sources and facts are keyed by canonical classes; rows that became equal are merged.
        var old = this.demands;
        this.demands = new();
        this.demandIndex = new();
        var map = new Dictionary<Demand, Demand>();
        foreach (var d in old)
        {
            var key = this.KeyOf(d.Decl, d.Subject, d.Args);
            if (this.demandIndex.TryGetValue(key, out var into))
            {
                into.RootPos |= d.RootPos;
                into.RootNeg |= d.RootNeg;
                into.Dirty |= d.Dirty || !d.Selected;
                into.Visited.AddRange(d.Visited);
                foreach (var (k, v) in d.VisitSigns)
                {
                    into.VisitSigns[k] = (byte)(into.VisitSigns.GetValueOrDefault(k) | v);
                }

                into.Requested.AddRange(d.Requested);
                map[d] = into;
                continue;
            }

            d.Subject = d.Subject >= 0 ? this.g.Find(d.Subject) : -1;
            d.Args = d.Args.Select(a => a >= 0 ? this.g.Find(a) : -1).ToArray();
            this.demandIndex.Add(key, d);
            this.demands.Add(d);
            map[d] = d;
        }

        foreach (var s in this.subs)
        {
            s.On = map[s.On];
            if (s.Requester is Demand r)
            {
                s.Requester = map[r];
            }
        }

        var moved = new Dictionary<ClassDemand, ClassDemand>();
        this.idents = RekeyClass(this.idents, moved);
        this.compounds = RekeyClass(this.compounds, moved);
        foreach (var s in this.subs)
        {
            if (s.Requester is ClassDemand c && moved.TryGetValue(c, out var to))
            {
                s.Requester = to;
            }
        }

        // Subscriptions and expansion records of rows that became one are one (otherwise which row survived, an accident of
        // order, would change the counts the limits read).
        var subs = new List<Sub>();
        this.subIndex = new();
        foreach (var s in this.subs)
        {
            var key = new SubKey(s.T.Id, s.Stage, s.On, s.Requester, s.Sign, string.Join(",", s.Beta.Select(x => x is int i ? this.g.Find(i) : -1)) + "|" + string.Join(",", s.Ants.Select(this.FactFind)));
            if (this.subIndex.TryGetValue(key, out var existing))
            {
                existing.Fired.UnionWith(s.Fired);
                existing.Retired |= s.Retired;
                continue;
            }

            this.subIndex.Add(key, s);
            subs.Add(s);
        }

        if (subs.Count != this.subs.Count)
        {
            var alive = new HashSet<Sub>(subs);
            foreach (var row in this.demands.Cast<Row>().Concat(this.idents.Values).Concat(this.compounds.Values))
            {
                row.Requested.RemoveAll(x => !alive.Contains(x));
            }

            this.subs = subs;
        }

        Row Owner(Row r) => r is Demand d && map.TryGetValue(d, out var dm) ? dm : r is ClassDemand c && moved.TryGetValue(c, out var cm) ? cm : r;
        this.expansions = new(this.expansions.Select(e => (e.T, this.g.Find(e.Class), e.Bind, Owner(e.Owner), e.Sign)));

        var virtuals = new Dictionary<int, List<Virtual>>();
        foreach (var (c, list) in this.virtuals)
        {
            var root = this.g.Find(c);
            if (!virtuals.TryGetValue(root, out var into))
            {
                virtuals[root] = into = new();
            }

            foreach (var vs in list)
            {
                if (!into.Any(x => this.VirtualKey(x) == this.VirtualKey(vs)))
                {
                    into.Add(vs);
                }
            }
        }

        this.virtuals = virtuals;
        this.factIndex.Clear();
        this.positiveIs.Clear();
        for (var i = 0; i < this.facts.Count; i++)
        {
            if (this.factAlias[i] != i)
            {
                continue;
            }

            var f = this.facts[i];
            if (f.Kind is 2)
            {
                continue;
            }

            var key = this.KeyOf(f);
            if (this.factIndex.TryGetValue(key, out var existing))
            {
                this.facts[existing].Supports.AddRange(f.Supports);
                this.factAlias[i] = existing;
                continue;
            }

            this.factIndex.Add(key, i);
            if (f.Kind == 0)
            {
                this.Positive(f.Decl!).Add(i);
            }
        }

        Dictionary<int, ClassDemand> RekeyClass(Dictionary<int, ClassDemand> source, Dictionary<ClassDemand, ClassDemand> moved)
        {
            var result = new Dictionary<int, ClassDemand>();
            foreach (var c in source.Values)
            {
                var root = this.g.Find(c.Class);
                if (result.TryGetValue(root, out var into))
                {
                    into.Root |= c.Root;
                    into.NegatedFor |= c.NegatedFor;
                    into.Dirty |= c.Dirty || !c.Selected;
                    into.Visited.AddRange(c.Visited);
                    foreach (var (k, v) in c.VisitSigns)
                    {
                        into.VisitSigns[k] = (byte)(into.VisitSigns.GetValueOrDefault(k) | v);
                    }

                    into.Requested.AddRange(c.Requested);
                    moved[c] = into;
                    continue;
                }

                c.Class = root;
                result.Add(root, c);
            }

            return result;
        }
    }

    // A virtual row's content: its template, other side, canonical bindings and (for a chain) its base's content; equal rows
    // from classes that merged are one row.
    private string VirtualKey(Virtual vs)
        => vs.T.Id + "/" + vs.Other.Id + "/" + string.Join(",", vs.Beta.Select(b => b is int i ? this.g.Find(i) : -1)) +
            (vs.Via is { } via ? "<" + vs.ViaPattern!.Id + "/" + string.Join(",", vs.ViaBeta!.Select(b => b is int i ? this.g.Find(i) : -1)) + "<" + this.VirtualKey(via) : string.Empty);

    private List<int> Positive(ContractDecl decl)
    {
        if (!this.positiveIs.TryGetValue(decl, out var list))
        {
            this.positiveIs[decl] = list = new();
        }

        return list;
    }

    private void Fire(Template t, int?[] beta, int[] ants)
    {
        var c = t.Conclusion;
        var support = new Support(t.Id, ants.Select(this.FactFind).ToArray(), t.BindingId);
        if (!c.IsAtom)
        {
            var subject = this.Inst(c.Subject, beta);
            this.InstAll(c, beta);
            var compound = new Fact(this.facts.Count, 4, null, subject, -1) { Shape = c, Beta = beta.Select(x => x is int i ? (int?)this.g.Find(i) : null).ToArray() };
            this.AddFact(compound, support);
            return;
        }

        var negative = c.Kind == FK.Not;
        var atom = negative ? c.L! : c;
        var a = this.Inst(atom.A!, beta);
        var b = this.Inst(atom.B!, beta);
        support.Explain = this.g.Explain(a).Concat(this.g.Explain(b)).Distinct().ToArray();
        if (atom.Kind == FK.Is)
        {
            this.AddFact(new Fact(this.facts.Count, (byte)(negative ? 1 : 0), this.m.DeclOf(atom.B!), a, b), support);
        }
        else if (negative)
        {
            this.AddFact(new Fact(this.facts.Count, 3, null, Math.Min(this.g.Find(a), this.g.Find(b)), Math.Max(this.g.Find(a), this.g.Find(b))), support);
        }
        else
        {
            // An identity fact: its own row (for reads) and a union of the two classes.
            var f = new Fact(this.facts.Count, 2, null, a, b);
            f.Supports.Add(support);
            this.facts.Add(f);
            this.factAlias.Add(f.Id);
            this.g.Union(a, b, f.Id);
        }
    }

    private void AddFact(Fact f, Support support)
    {
        var key = this.KeyOf(f);
        if (this.factIndex.TryGetValue(key, out var existing))
        {
            var e = this.facts[existing];
            if (!e.Supports.Any(x => x.Template == support.Template && x.Ants.SequenceEqual(support.Ants)))
            {
                e.Supports.Add(support);
            }

            return;
        }

        f.Supports.Add(support);
        this.facts.Add(f);
        this.factAlias.Add(f.Id);
        this.factIndex.Add(key, f.Id);
        if (f.Kind == 0)
        {
            this.Positive(f.Decl!).Add(f.Id);
        }
    }

    private int Inst(Term p, int?[] beta)
    {
        if (p.Head is VarHead v)
        {
            return beta[v.Index] ?? throw new InvalidOperationException("unbound variable in a conclusion");
        }

        var kids = new int[p.Kids.Length];
        for (var i = 0; i < kids.Length; i++)
        {
            kids[i] = this.Inst(p.Kids[i], beta);
        }

        return this.g.Add(p.Head, kids);
    }

    private void InstAll(Formula f, int?[] beta)
    {
        var terms = new List<Term>();
        f.Terms(terms);
        foreach (var t in terms)
        {
            this.Inst(t, beta);
        }
    }

    // ---- Rows ----
    private Demand EnsureDemand(ContractDecl decl, int subject, int[] args)
    {
        var key = this.KeyOf(decl, subject, args);
        if (this.demandIndex.TryGetValue(key, out var d))
        {
            return d;
        }

        d = new Demand(this.demands.Count, decl, subject >= 0 ? this.g.Find(subject) : -1, args.Select(a => a >= 0 ? this.g.Find(a) : -1).ToArray());
        this.demands.Add(d);
        this.demandIndex.Add(key, d);
        return d;
    }

    private ClassDemand EnsureClassDemand(Dictionary<int, ClassDemand> table, int c)
    {
        c = this.g.Find(c);
        if (!table.TryGetValue(c, out var d))
        {
            d = new ClassDemand(c);
            table.Add(c, d);
        }

        return d;
    }

    private DemandKey KeyOf(ContractDecl decl, int subject, int[] args)
        => new(decl, subject >= 0 ? this.g.Find(subject) : -1, string.Join(",", args.Select(a => a >= 0 ? this.g.Find(a) : -1)));

    private FactKey KeyOf(Fact f) => f.Kind switch
    {
        0 or 1 => new(f.Kind, f.Decl, this.g.Find(f.A), this.g.Find(f.B), null),
        3 => new(3, null, Math.Min(this.g.Find(f.A), this.g.Find(f.B)), Math.Max(this.g.Find(f.A), this.g.Find(f.B)), null),
        _ => new(4, null, this.g.Find(f.A), 0, this.CanonOf(f)),
    };

    private int FactFind(int id)
    {
        while (this.factAlias[id] != id)
        {
            id = this.factAlias[id];
        }

        return id;
    }

    private IEnumerable<Fact> CanonicalFacts()
    {
        for (var i = 0; i < this.facts.Count; i++)
        {
            if (this.factAlias[i] == i)
            {
                yield return this.facts[i];
            }
        }
    }

    private string CanonOf(Fact f) => this.Canon(f.Shape!, f.Beta!);

    private string Canon(Formula f, int?[]? beta = null) => f.Kind switch
    {
        FK.Is => $"Is({this.ClassOf(f.A!, beta)},{this.ClassOf(f.B!, beta)})",
        FK.Eq => $"Eq({this.ClassOf(f.A!, beta)},{this.ClassOf(f.B!, beta)})",
        FK.Not => $"Not({this.Canon(f.L!, beta)})",
        _ => $"{f.Kind}({this.Canon(f.L!, beta)},{this.Canon(f.R!, beta)})",
    };

    private int ClassOf(Term t, int?[]? beta)
    {
        if (t.Head is VarHead v)
        {
            return beta?[v.Index] is int b ? this.g.Find(b) : -1;
        }

        var kids = t.Kids.Select(k => this.ClassOf(k, beta)).ToArray();
        if (kids.Any(k => k < 0))
        {
            return -1;
        }

        var c = this.g.Lookup(t.Head, kids);
        return c < 0 ? -1 : this.g.Find(c);
    }

    // ---- Reads (inference views): every admissible support, as the antichain of minimal binding-read sets ----
    private void Reads(Answer answer)
    {
        var reads = new Dictionary<int, List<int[]>>();
        var changed = true;
        var guard = 0;
        while (changed && guard++ < 10_000)
        {
            changed = false;
            foreach (var f in this.CanonicalFacts().ToArray())
            {
                var alternatives = new List<int[]>();
                foreach (var s in f.Supports)
                {
                    var combos = new List<int[]> { s.Binding >= 0 ? [s.Binding] : [] };
                    foreach (var a in s.Ants.Concat(s.Explain))
                    {
                        if (!reads.TryGetValue(this.FactFind(a), out var ra) || ra.Count == 0)
                        {
                            combos.Clear();
                            break;
                        }

                        combos = Minimize(combos.SelectMany(x => ra.Select(y => x.Union(y).OrderBy(z => z).ToArray())).ToList());
                    }

                    alternatives.AddRange(combos);
                }

                alternatives = Minimize(alternatives);
                if (alternatives.Count > this.limits.Alternatives)
                {
                    answer.Outcome = Outcome.Resource;
                    answer.Reason = "read alternatives";
                    return;
                }

                if (!reads.TryGetValue(f.Id, out var previous) || !SameAntichain(previous, alternatives))
                {
                    reads[f.Id] = alternatives;
                    changed = true;
                }
            }
        }

        var p = this.goal.Proposition!;
        var atom = p.Kind == FK.Not ? p.L! : p;
        if (!p.IsAtom)
        {
            return;
        }

        if (atom.Kind == FK.Is)
        {
            var decl = this.m.DeclOf(atom.B!)!;
            var s = this.g.Find(this.g.LookupTerm(atom.A!));
            var r = this.g.Find(this.g.LookupTerm(atom.B!));
            // The goal's Types joined the fact's classes through the identity facts of those classes: their reads count too.
            List<int[]> Joined(List<int[]> factReads)
            {
                var combos = factReads;
                foreach (var e in this.g.Explain(s).Concat(this.g.Explain(r)).Distinct())
                {
                    if (reads.TryGetValue(this.FactFind(e), out var re) && re.Count != 0)
                    {
                        combos = Minimize(combos.SelectMany(x => re.Select(y => x.Union(y).OrderBy(z => z).ToArray())).ToList());
                    }
                }

                return combos;
            }

            if (this.factIndex.TryGetValue(new(0, decl, s, r, null), out var pf) && reads.TryGetValue(pf, out var pr))
            {
                (p.Kind == FK.Not ? answer.NegativeReads : answer.PositiveReads).AddRange(Joined(pr));
            }

            if (this.factIndex.TryGetValue(new(1, decl, s, r, null), out var nf) && reads.TryGetValue(nf, out var nr))
            {
                (p.Kind == FK.Not ? answer.PositiveReads : answer.NegativeReads).AddRange(Joined(nr));
            }
        }
        else
        {
            // Identity goal: the identity facts of the merged class.
            var a = this.g.Find(this.g.LookupTerm(atom.A!));
            if (a == this.g.Find(this.g.LookupTerm(atom.B!)))
            {
                var combos = new List<int[]> { Array.Empty<int>() };
                foreach (var e in this.g.Explain(a))
                {
                    if (reads.TryGetValue(this.FactFind(e), out var re) && re.Count != 0)
                    {
                        combos = Minimize(combos.SelectMany(x => re.Select(y => x.Union(y).OrderBy(z => z).ToArray())).ToList());
                    }
                }

                answer.PositiveReads.AddRange(combos);
            }
        }
    }

    private static List<int[]> Minimize(List<int[]> sets)
    {
        var result = new List<int[]>();
        foreach (var s in sets.Distinct(new SetComparer()).OrderBy(x => x.Length))
        {
            if (!result.Any(r => r.All(s.Contains)))
            {
                result.Add(s);
            }
        }

        return result;
    }

    private static bool SameAntichain(List<int[]> a, List<int[]> b)
        => a.Count == b.Count && a.All(x => b.Any(y => x.SequenceEqual(y)));

    private static List<int> Unbound(Formula f, int?[] beta)
    {
        var terms = new List<Term>();
        f.Terms(terms);
        var vars = new HashSet<int>();
        foreach (var t in terms)
        {
            t.Vars(vars);
        }

        return vars.Where(v => beta[v] is null).ToList();
    }

    private static List<int> Unbound(Term t, int?[] beta)
    {
        var vars = new HashSet<int>();
        t.Vars(vars);
        return vars.Where(v => beta[v] is null).ToList();
    }

    private static IEnumerable<int?[]> Distinct(List<int?[]> results)
    {
        var seen = new HashSet<string>();
        foreach (var r in results)
        {
            if (seen.Add(string.Join(",", r.Select(x => x?.ToString() ?? "*"))))
            {
                yield return r;
            }
        }
    }

    private sealed class SetComparer : IEqualityComparer<int[]>
    {
        public bool Equals(int[]? x, int[]? y) => x!.SequenceEqual(y!);

        public int GetHashCode(int[] obj) => obj.Aggregate(17, HashCode.Combine);
    }

    private sealed class Goal
    {
        public Goal(Formula p) => this.Proposition = p;

        public Goal(Term subject) => this.Subject = subject;

        public Formula? Proposition { get; }

        public Term? Subject { get; }

        public int SubjectClass { get; set; }

        /// <summary>Gets or sets the goal polarities no row can supply (settled without search).</summary>
        public byte Impossible { get; set; }
    }

    private abstract class Row
    {
        public abstract byte Tags { get; }

        public bool Selected { get; set; }

        public bool Dirty { get; set; }

        public List<int> Visited { get; } = new();

        public Dictionary<int, byte> VisitSigns { get; } = new();

        public List<Sub> Requested { get; } = new();
    }

    private sealed class Demand : Row
    {
        public Demand(int id, ContractDecl decl, int subject, int[] args)
        {
            this.Id = id;
            this.Decl = decl;
            this.Subject = subject;
            this.Args = args;
        }

        public int Id { get; }

        public ContractDecl Decl { get; }

        public int Subject { get; set; }

        public int[] Args { get; set; }

        /// <summary>Gets or sets the goal polarities positive facts of this row serve at the goal (or the requested antecedents).</summary>
        public byte RootPos { get; set; }

        /// <summary>Gets or sets the goal polarities negated facts of this row serve; only goal rows want negated facts.</summary>
        public byte RootNeg { get; set; }

        public byte PosServe { get; set; }

        public byte NegServe { get; set; }

        public override byte Tags => (byte)(this.PosServe | this.NegServe);
    }

    private sealed class ClassDemand : Row
    {
        public ClassDemand(int c) => this.Class = c;

        public int Class { get; set; }

        public byte Root { get; set; }

        public byte NegatedFor { get; set; }

        public byte TagsValue { get; set; }

        public override byte Tags => this.TagsValue;
    }

    private sealed class Sub
    {
        public Sub(int id, Template t, int?[] beta, int stage, Demand on, byte sign, Row requester, int[] ants)
        {
            this.Id = id;
            this.T = t;
            this.Beta = beta;
            this.Stage = stage;
            this.On = on;
            this.Sign = sign;
            this.Requester = requester;
            this.Ants = ants;
        }

        public int Id { get; }

        public Template T { get; }

        public int?[] Beta { get; }

        public int Stage { get; }

        public Demand On { get; set; }

        public byte Sign { get; }

        public Row Requester { get; set; }

        public byte Tags { get; set; }

        public bool Existential { get; init; }

        public bool Retired { get; set; }

        public int[] Ants { get; }

        public HashSet<int> Fired { get; } = new();
    }

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

        public int Scanned { get; set; }

        public Term? ViaPattern { get; init; }

        public int?[]? ViaBeta { get; init; }
    }

    private sealed class Support
    {
        public Support(int template, int[] ants, int binding)
        {
            this.Template = template;
            this.Ants = ants;
            this.Binding = binding;
        }

        public int Template { get; }

        public int[] Ants { get; }

        public int Binding { get; }

        public int[] Explain { get; set; } = [];
    }

    private sealed class Fact
    {
        public Fact(int id, byte kind, ContractDecl? decl, int a, int b)
        {
            this.Id = id;
            this.Kind = kind;
            this.Decl = decl;
            this.A = a;
            this.B = b;
        }

        public int Id { get; }

        /// <summary>Gets the row kind: 0 Is, 1 not Is, 2 identity, 3 not identity, 4 exact compound.</summary>
        public byte Kind { get; }

        public ContractDecl? Decl { get; }

        public int A { get; }

        public int B { get; }

        public Formula? Shape { get; init; }

        public int?[]? Beta { get; init; }

        public List<Support> Supports { get; } = new();
    }

    private readonly record struct DemandKey(ContractDecl Decl, int Subject, string Args);

    private readonly record struct SubKey(int Template, int Stage, Demand On, Row Requester, byte Sign, string Beta);

    private readonly record struct FactKey(byte Kind, ContractDecl? Decl, int A, int B, string? Text);

    private sealed record SubRequest(ContractDecl Decl, int Subject, int[] Args, byte Tags, byte Sign, Row Requester, Template T, int?[] Beta, int Stage, int[] Ants);

    private sealed record FireRequest(Template T, int?[] Beta, int[] Ants, byte Tags);

    private sealed class Effects
    {
        public List<SubRequest> Subs { get; } = new();

        public List<FireRequest> Fires { get; } = new();

        public List<int> Idents { get; } = new();

        public List<Virtual> Virtuals { get; } = new();

        public byte IdentTags { get; set; }

        public byte VirtualTags { get; set; }

        public int Count => this.Subs.Count + this.Fires.Count + this.Idents.Count + this.Virtuals.Count;

        public byte Tags => (byte)(this.Subs.Aggregate(0, (a, s) => a | s.Tags) | this.Fires.Aggregate(0, (a, s) => a | s.Tags) | this.IdentTags | this.VirtualTags);
    }

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

        public List<(Virtual Via, Term Pattern, int?[] Beta)>? Chain { get; init; }

        // Every class a selection visits needs its identity facts: a class demand, which inherits the visitor's tags.
        public void Visit(int c)
        {
            if (this.Owner is null || this.Fx is null)
            {
                return;
            }

            this.Owner.Visited.Add(c);
            this.Owner.VisitSigns[c] = (byte)(this.Owner.VisitSigns.GetValueOrDefault(c) | this.Sign);
            if (!this.solver.idents.ContainsKey(c))
            {
                this.Fx.Idents.Add(c);
                this.Fx.IdentTags |= this.Tags;
            }
        }
    }
}

internal static class SetExtensions
{
    public static bool UnionWithChanged<T>(this HashSet<T> set, IEnumerable<T> other)
    {
        var before = set.Count;
        set.UnionWith(other);
        return set.Count != before;
    }
}
