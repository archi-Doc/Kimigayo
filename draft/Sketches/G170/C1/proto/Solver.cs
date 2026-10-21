// G170 C1 prototype of C (PlanB §4.3): request-local demand evaluation over the existing clause templates.
//
// Contract (the design's ContractSolver entry operations, in prototype form):
// - Supply(goal, view): the available evidence for a bound proposition and for its negation; Complete, Provisional or Resource.
// - Enumerate(subject, view): the constraints available on a subject (with discovery: also through pending sources).
// Inputs are the finite model (declared inputs, Contract clause templates, verified conformances and their bindings, each with
// its published source state) and the evidence view; nothing writes the model. All rows are request-local: nothing semantic
// survives the request.
//
// Algorithm: magic-set selection plus semi-naive firing, in Jacobi rounds (each round computes its effects from the previous
// round's state only, so the state after round k, its size, its work and the round count are functions of the request: a
// Resource outcome never depends on query or declaration order). Identity is an e-graph (EGraph); templates are selected by
// e-matching their conclusion against a demanded class, through static indexes keyed by conclusion heads, so a request examines
// only rows whose conclusion can meet a head in its classes. A demand is a (Contract declaration, subject class or star, argument
// classes or star) row; a class demand collects the identity facts with a side in one class, and its classes' kids (upward
// congruence makes a class depend on its kids' identities). An identity row matched only through its closed side (a side without
// the row's subject: `i32` in `Self.(It).Value is i32`, the value of a binding) is lazy: its other side is a virtual node of the
// class, never materialized per row, found through the closed-side index where a pattern needs its head, where the class meets a
// class holding facts of a demanded Contract, or where an existing node has its shape (bound expansion). Static row indexes (heads
// per Contract position, which polarities a Contract can have) skip impossible expansions and settle impossible polarities. Polarity
// tags, recomputed every round from the goal rows, say which goal polarity a row serves; a polarity settles when its evidence is
// established or none of its rows has work left. A subscription whose conclusion is fixed by its bindings retires after the round
// that found a witness. A selected source in Resource state makes an unsettled answer Resource.
#pragma warning disable
#nullable enable

namespace G170C1;

public sealed class Limits
{
    /// <summary>Gets the round limit (SPEC 8.3.3's proof-search depth).</summary>
    public int Rounds { get; init; } = 4096;

    /// <summary>Gets the item limit: rows (demands, class demands, subscriptions, chain rows), supports and identity nodes.</summary>
    public long Items { get; init; } = 65_536;
}

public enum Outcome : byte { Complete, Provisional, Resource }

public sealed class Answer
{
    public bool Positive { get; set; }

    public bool Negative { get; set; }

    public Outcome Outcome { get; set; }

    public string Reason { get; set; } = string.Empty;

    public int Rounds { get; set; }

    /// <summary>Gets or sets V: requested rows (demands, class demands, subscriptions, chain rows).</summary>
    public long V { get; set; }

    /// <summary>Gets or sets E: support edges (one per support plus one per antecedent it names).</summary>
    public long E { get; set; }

    /// <summary>Gets or sets J: join work (matching steps, index entries and facts examined, congruence repairs), a function of each
    /// round's starting state.</summary>
    public long J { get; set; }

    /// <summary>Gets or sets N: identity nodes materialized.</summary>
    public long N { get; set; }

    public long PeakItems { get; set; }

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
    private readonly List<Template> inputs = new();
    private readonly Dictionary<ContractDecl, List<Template>> isAll = new();
    private readonly Dictionary<ContractDecl, List<Template>> isAnySubject = new();
    private readonly Dictionary<(ContractDecl Decl, HeadKey Head), List<Template>> isBySubject = new();
    private readonly Dictionary<HeadKey, List<Template>> compoundBySubject = new();
    private readonly Dictionary<(HeadKey Side, HeadKey Kid0), List<EqSide>> openSides = new();
    private readonly Dictionary<(HeadKey Other, HeadKey Side, HeadKey Kid0), List<EqSide>> openIndex = new();
    private readonly Dictionary<HeadKey, HashSet<HeadKey>> otherHeadsByOpen = new();
    private readonly Dictionary<(HeadKey Other, HeadKey Closed), Dictionary<HeadKey, Dictionary<Term, List<EqSide>>>> closedIndex = new();
    private readonly Dictionary<HeadKey, HashSet<HeadKey>> otherHeadsByClosed = new();
    private readonly Dictionary<(HeadKey Other, HeadKey Closed), HashSet<HeadKey>> closedKids = new();
    private readonly Dictionary<HeadKey, HashSet<HeadKey>> reach = new();
    private readonly Dictionary<(HeadKey Other, HeadKey Kid0), List<EqSide>> closedByPath = new();
    private readonly HashSet<(int Class, HeadKey Need)> chainGuard = new();
    private readonly Dictionary<(int, HeadKey, string), (List<Virtual> Result, List<int> Visits, List<(int, HeadKey)> Needs, bool Resource, bool Chains)> roundMemo = new();
    private List<int>? visitLog;
    private List<(int Class, HeadKey Need)>? needLog;
    private bool chainSeen;
    private readonly Dictionary<HeadKey, int> headIds = new();
    private readonly Dictionary<int, HashSet<HeadKey>> shapes = new();
    private bool resourceSeen;
    private readonly HashSet<ContractDecl> concludesPositive = new();
    private readonly HashSet<ContractDecl> concludesNegated = new();
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
    private Dictionary<ContractDecl, List<int>> factsOf = new();
    private HashSet<(int T, int Class, string Bind, Row Owner, byte Sign)> expansions = new();
    private Dictionary<int, List<Virtual>> chains = new();
    private Dictionary<(int Class, HeadKey Need), NeedRow> needs = new();
    private bool provisional;
    private byte resourceTags;
    private string resourceSource = string.Empty;
    private bool mergedSinceScan;
    private int eagerScanned;
    private long work;
    private Goal goal = null!;

    public Solver(ProgramModel model)
    {
        this.m = model;
        foreach (var decl in model.Decls)
        {
            this.isAll[decl] = new();
            this.isAnySubject[decl] = new();
            this.antecedentPattern[decl] = (model.Terms.Var(0), model.SelfRef(decl));
        }

        foreach (var t in model.Templates)
        {
            if (t.Kind == TemplateKind.Input)
            {
                // An input fact can reach the goal through identity: its Contract and polarity can be concluded.
                this.inputs.Add(t);
                if (t.Conclusion.IsAtom && (t.Conclusion.Kind == FK.Not ? t.Conclusion.L! : t.Conclusion) is { Kind: FK.Is } input && model.DeclOf(input.B!) is { } concluded)
                {
                    (t.Conclusion.Kind == FK.Not ? this.concludesNegated : this.concludesPositive).Add(concluded);
                }

                continue;
            }

            var c = t.Conclusion;
            var atom = c.Kind == FK.Not ? c.L! : c;
            if (!c.IsAtom)
            {
                Add(this.compoundBySubject, HeadKey.Of(c.Subject), t);
            }
            else if (atom.Kind == FK.Eq)
            {
                for (var side = 0; side < 2; side++)
                {
                    var e = new EqSide(t, side);
                    var closed = HeadKey.Of(e.Side);
                    var other = HeadKey.Of(e.Other);
                    if (!this.reach.TryGetValue(closed, out var next))
                    {
                        this.reach[closed] = next = new();
                    }

                    next.Add(other);
                    if (!e.Closed)
                    {
                        Add(this.openSides, (closed, HeadKey.KidOf(e.Side)), e);
                        if (!e.Negative)
                        {
                            Add(this.openIndex, (other, closed, e.Side.Kids.Length == 0 ? HeadKey.Any : HeadKey.Of(e.Side.Kids[0])), e);
                            if (!this.otherHeadsByOpen.TryGetValue(closed, out var opens))
                            {
                                this.otherHeadsByOpen[closed] = opens = new();
                            }

                            opens.Add(other);
                        }

                        continue;
                    }

                    if (!this.closedIndex.TryGetValue((other, closed), out var byKid))
                    {
                        this.closedIndex[(other, closed)] = byKid = new();
                    }

                    if (!byKid.TryGetValue(HeadKey.KidOf(e.Other), out var bySide))
                    {
                        byKid[HeadKey.KidOf(e.Other)] = bySide = new();
                    }

                    Add(bySide, e.Side, e); // rows sharing a closed side (every binding whose value is i32) are one match
                    if (e.Side.HasVar || e.Other.HasVar)
                    {
                        // The first kids a virtual node of this row can have. A ground row's virtual node is its own other side,
                        // which only that row's open side equals (one binding per projection): it can start no chain.
                        if (!this.closedKids.TryGetValue((other, closed), out var kids))
                        {
                            this.closedKids[(other, closed)] = kids = new();
                        }

                        kids.Add(HeadKey.KidOf(e.Other));
                    }
                    if (!this.otherHeadsByClosed.TryGetValue(closed, out var heads))
                    {
                        this.otherHeadsByClosed[closed] = heads = new();
                    }

                    heads.Add(other);
                    if (e.Other.Head is not VarHead)
                    {
                        Add(this.closedByPath, (other, e.Other.Kids.Length == 0 ? HeadKey.Any : HeadKey.Of(e.Other.Kids[0])), e);
                    }
                }
            }
            else if (model.DeclOf(atom.B!) is { } decl)
            {
                this.isAll[decl].Add(t);
                if (atom.A!.Head is VarHead)
                {
                    this.isAnySubject[decl].Add(t);
                }
                else
                {
                    Add(this.isBySubject, (decl, HeadKey.Of(atom.A!)), t);
                }

                (c.Kind == FK.Not ? this.concludesNegated : this.concludesPositive).Add(decl);
            }
        }

        // The heads a virtual node can have, from a head a class holds: the other sides of identity rows whose side has that head
        // (or is a variable), transitively (a chain row's other side, found through a virtual node, can be another row's side).
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (head, heads) in this.reach)
            {
                foreach (var h in heads.Append(HeadKey.Any).ToArray())
                {
                    if (this.reach.TryGetValue(h, out var further) && h != head)
                    {
                        changed |= heads.UnionWithChanged(further);
                    }
                }
            }
        }

        this.SubjectHeads();

        static void Add<TKey, TValue>(Dictionary<TKey, List<TValue>> index, TKey key, TValue value)
            where TKey : notnull
        {
            if (!index.TryGetValue(key, out var list))
            {
                index[key] = list = new();
            }

            list.Add(value);
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
        this.factsOf = new();
        this.expansions = new();
        this.chains = new();
        this.needs = new();
        this.provisional = false;
        this.resourceTags = 0;
        this.resourceSource = string.Empty;
        this.mergedSinceScan = false;
        this.eagerScanned = 0;
        this.work = 0;
    }

    private void SeedGoal()
    {
        // Inputs: finite declared assumptions, seeded as facts (exact compounds stay as written) and identity unions. Nothing else
        // is interned up front: a Type enters the request when a goal, an input, a fired row or a condition names it.
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
                d.RootPos = this.concludesPositive.Contains(decl) ? sign : (byte)0;
                d.RootNeg = this.concludesNegated.Contains(decl) ? (byte)(Both & ~sign) : (byte)0;
                this.EnsureClassDemand(this.idents, goal.SubjectClass).Root = (byte)(d.RootPos | d.RootNeg);
                goal.Impossible = (byte)(Both & ~(d.RootPos | d.RootNeg));
            }
            else
            {
                goal.SubjectClass = this.g.AddTerm(atom.A!);
                goal.OtherClass = this.g.AddTerm(atom.B!);
                var a = this.EnsureClassDemand(this.idents, goal.SubjectClass);
                var b = this.EnsureClassDemand(this.idents, goal.OtherClass);
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
        var answer = new Answer { PeakItems = this.Items() };
        var rounds = 0;
        if (answer.PeakItems > this.limits.Items)
        {
            answer.Outcome = Outcome.Resource; // the seeded state alone exceeds the item limit
            answer.Reason = $"items {answer.PeakItems}>{this.limits.Items}";
            return answer;
        }

        while (true)
        {
            this.Propagate();
            var established = (byte)(this.Established() | this.goal.Impossible);
            answer.EstablishedAtStop = established;
            var active = this.exhaustive ? Both : (byte)(Both & ~established);
            var fx = this.Compute(active);
            this.Trace?.Invoke($"round {rounds}: established {established} active {active} effects subs {fx.Subs.Count} fires {fx.Fires.Count} idents {fx.Idents.Count} chains {fx.Chains.Count} needs {fx.Needs.Count} rows {this.demands.Count}/{this.idents.Count}/{this.subs.Count} nodes {this.g.Nodes}");
            if (this.Trace is not null)
            {
                foreach (var f in fx.Fires)
                {
                    this.Trace($"  fire {f.T} with [{string.Join(", ", f.Beta.Select(x => x is int i ? this.g.Show(i) : "*"))}]");
                }
            }

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

            this.Apply(fx);
            var items = this.Items();
            answer.PeakItems = Math.Max(answer.PeakItems, items);
            if (items > this.limits.Items)
            {
                answer.Outcome = Outcome.Resource;
                answer.Reason = $"items {items}>{this.limits.Items}";
                break;
            }
        }

        // A selected source whose published state is Resource could still supply a polarity the request did not establish: the
        // answer is Resource (the source's own record stands; this request adds none).
        if (answer.Outcome != Outcome.Resource && (this.resourceTags & ~(this.exhaustive ? 0 : this.Established() | this.goal.Impossible) & Both) != 0)
        {
            answer.Outcome = Outcome.Resource;
            answer.Reason = "source state Resource: " + this.resourceSource;
        }

        answer.Rounds = rounds;
        answer.V = this.demands.Count + this.idents.Count + this.compounds.Count + this.subs.Count + this.chains.Values.Sum(x => x.Count) + this.needs.Count;
        answer.Detail = $"demands {this.demands.Count} idents {this.idents.Count} compounds {this.compounds.Count} subs {this.subs.Count} chains {this.chains.Values.Sum(x => x.Count)}";
        answer.E = this.facts.Sum(f => f.Supports.Sum(s => 1 + s.Ants.Length));
        answer.N = this.g.Nodes;
        answer.J = this.work + this.g.Work;
        answer.PeakItems = Math.Max(answer.PeakItems, this.Items());
        return answer;
    }

    private long Items() => this.demands.Count + this.idents.Count + this.compounds.Count + this.subs.Count + this.chains.Values.Sum(x => x.Count) + this.needs.Count + this.facts.Sum(f => f.Supports.Count) + this.g.Nodes;

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

        foreach (var n in this.needs.Values)
        {
            n.TagsValue = 0;
        }

        var queue = new Queue<Row>(this.demands.Where(d => d.Tags != 0).Cast<Row>().Concat(this.idents.Values.Concat(this.compounds.Values).Where(c => c.Tags != 0)));
        while (true)
        {
            this.Drain(queue);

            // A need row serves what its requesters serve (requesters may be need rows: to a fixed point).
            foreach (var n in this.needs.Values)
            {
                var t = (byte)n.Owners.Aggregate(0, (a, o) => a | o.Tags);
                if ((n.TagsValue | t) != n.TagsValue)
                {
                    n.TagsValue |= t;
                    queue.Enqueue(n);
                }
            }

            if (queue.Count == 0)
            {
                return;
            }
        }
    }

    private void Drain(Queue<Row> queue)
    {
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
                // (a meeting), or a class demand's or need row's tags.
                var t = row is Demand d ? (s.Sign == Neg ? d.NegServe : s.Sign == Pos ? d.PosServe : d.Tags) : row.Tags;
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
        return (byte)((pos ? Pos : 0) | (neg ? Neg : 0));
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

    // ---- Rounds ----
    private Effects Compute(byte active)
    {
        var fx = new Effects();
        this.roundMemo.Clear(); // virtual nodes are a function of the round's starting state
        this.shapes.Clear();
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

        // Need rows (a need met beyond the chain-detection depth) are answered as top-level lookups, which detect and store
        // their chains: every need gets its chains, one level per round.
        foreach (var n in this.needs.Values.ToArray())
        {
            if ((!n.Selected || n.Dirty) && (n.Tags & active) != 0)
            {
                n.Selected = true;
                n.Dirty = false;
                n.Visited.Clear();
                n.VisitSigns.Clear();
                this.Virtuals(n.Class, n.Need, new MatchCtx(this, n, (byte)(n.Tags & active), fx) { Sign = Both, Mode = MatchMode.Expand });
            }
        }

        this.EagerExpand(fx, active);
        return fx;
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

    // The heads a virtual node of c can have (a static over-approximation from c's heads); HeadKey.Any means any head.
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

        return result;
    }

    // The virtual nodes of c whose other side can have the needed head (HeadKey.Any: all), computed where needed, never stored:
    // - a closed-side row (found through the index by the needed head and a head of c) whose closed side e-matches c;
    // - a chain row: a row side that meets c only through a virtual node of c (at the top, or at a kid of one of c's nodes) makes
    //   the row's other side a virtual node of c too, forwarding to that node's row (chains forward to their base: never stack).
    // Candidates are grouped by their side Term, so rows sharing a side (every binding whose value is i32) cost one match. The
    // matches visit classes for the requester (so their merges wake it) without expanding anything.
    private List<Virtual> VirtualsAt(int c, HeadKey need, MatchCtx requester, HashSet<HeadKey>? kid0 = null)
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
            result.AddRange(stored.Where(vs => need.IsAny || vs.Other.Head is VarHead || HeadKey.Of(vs.Other) == need));
        }

        try
        {
            var groups = new Dictionary<Term, (List<int?[]> Results, List<(Virtual Via, Term Pattern, int?[] Beta)> Hits)>();
            var memo = new Dictionary<(int, HeadKey, string), List<Virtual>>();
            var direct = heads.Append(HeadKey.Any).ToHashSet();
            var throughVirtuals = chain ? this.VirtualHeads(heads) : [];
            throughVirtuals.ExceptWith(direct);
            foreach (var (closed, isDirect) in direct.Select(h => (h, true)).Concat(throughVirtuals.Select(h => (h, false))))
            {
                if (!this.otherHeadsByClosed.TryGetValue(closed, out var others))
                {
                    continue;
                }

                foreach (var other in others)
                {
                    if (need.IsAny || other == need || other.IsAny)
                    {
                        // The other side's first kid must be able to meet the requester's (when it names one).
                        var byKid = this.closedIndex[(other, closed)];
                        foreach (var bySide in kid0 is null ? byKid.Values : kid0.Append(HeadKey.Any).Distinct().Select(k => byKid.GetValueOrDefault(k)).OfType<Dictionary<Term, List<EqSide>>>())
                        {
                            foreach (var (side, entries) in bySide)
                            {
                                this.Candidate(side, entries, c, requester, chain, isDirect, groups, memo, result);
                            }
                        }
                    }
                }
            }

            if (!chain)
            {
                return result; // a lookup made while detecting chains: direct rows and stored chains only
            }

            // Open sides by (other head, side head, side's first kid head): a side with a head of c meets it through the first kid
            // of one of c's nodes of that head, real or virtual; a side with only a virtual head of c, through any first kid.
            foreach (var side in direct.Concat(this.VirtualHeads(heads)).ToHashSet())
            {
                if (!this.otherHeadsByOpen.TryGetValue(side, out var others))
                {
                    continue;
                }

                // The first kids c's nodes of that head can have: its real nodes' kids, the first kids of non-ground closed rows
                // that can give c such a virtual node, and its stored chains'. Any among them admits every side (no filter);
                // sides whose own first kid is a variable are always candidates.
                var contributed = new HashSet<HeadKey>();
                if (side.Arity != 0)
                {
                    foreach (var node in this.g.NodesOf(c))
                    {
                        if (new HeadKey(this.g.HeadOf(node), this.g.KidCount(node)) == side)
                        {
                            var kidHeads = this.HeadsOf(this.g.KidOf(node, 0));
                            contributed.UnionWith(kidHeads);
                            contributed.UnionWith(this.VirtualHeads(kidHeads));
                        }
                    }

                    foreach (var closed in direct.Concat(this.VirtualHeads(heads)))
                    {
                        contributed.UnionWith(this.closedKids.GetValueOrDefault((side, closed)) ?? []);
                        contributed.UnionWith(this.closedKids.GetValueOrDefault((HeadKey.Any, closed)) ?? []);
                    }

                    foreach (var vs in stored ?? [])
                    {
                        contributed.Add(vs.Other.Head is VarHead ? HeadKey.Any : HeadKey.KidOf(vs.Other));
                    }
                }

                var kids = side.Arity == 0 || contributed.Contains(HeadKey.Any) ? null : contributed.Append(HeadKey.Any).ToHashSet();

                foreach (var other in others)
                {
                    if (!need.IsAny && other != need && !other.IsAny)
                    {
                        continue;
                    }

                    var buckets = kids is null
                        ? this.openIndex.Where(x => x.Key.Other == other && x.Key.Side == side).Select(x => x.Value)
                        : kids.Select(k => this.openIndex.GetValueOrDefault((other, side, k)) ?? new List<EqSide>());
                    foreach (var entries in buckets)
                    {
                        foreach (var e in entries)
                        {
                            this.Candidate(e.Side, [e], c, requester, chain, direct: false, groups, memo, result);
                        }
                    }
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

        return result;
    }

    // VirtualsAt for a requester: a lookup outside any other lookup is a function of (class, need, first-kid heads) and the
    // round's state, so it is answered once per round and replayed (its visits and Resource reads) for later requesters.
    private List<Virtual> Virtuals(int c, HeadKey need, MatchCtx requester, HashSet<HeadKey>? kid0 = null)
    {
        if (this.chainGuard.Count != 0)
        {
            return this.VirtualsAt(c, need, requester, kid0);
        }

        var key = (this.g.Find(c), need, this.SetKey(kid0));
        if (!this.roundMemo.TryGetValue(key, out var entry))
        {
            this.visitLog = new();
            this.needLog = new();
            this.resourceSeen = false;
            this.chainSeen = false;
            var result = this.VirtualsAt(c, need, requester, kid0);
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
            // The base may be another instance of this very row (its other closed side matched c): the chain bootstraps that
            // instance, after which this side matches c directly (`A.(K2).Y is List<i32>` joins T's and U's projections).
            var via = hit.Via ?? hit;
            var vs = new Virtual(e.T, new int?[e.T.VarCount], e.Other, c) { Via = via, ViaPattern = hit.Via is null ? pattern : hit.ViaPattern!, ViaBeta = hit.Via is null ? beta : hit.ViaBeta! };
            result.Add(vs);
            if (requester.Fx is { } fx && !(this.chains.TryGetValue(c, out var stored) && stored.Any(x => this.VirtualKey(x) == this.VirtualKey(vs))))
            {
                fx.Chains.Add(vs); // stored when the round is applied: a later lookup finds chains through it
                fx.ChainTags |= requester.Tags;
                this.chainSeen = true;
            }
        }
    }

    // A chain row's content: its template, other side, canonical bindings and its base's content; equal rows from classes that
    // merged are one row.
    private string VirtualKey(Virtual vs)
        => vs.T.Id + "/" + vs.Other.Id + "/" + string.Join(",", vs.Beta.Select(b => b is int i ? this.g.Find(i) : -1)) +
            (vs.Via is { } via ? "<" + vs.ViaPattern!.Id + "/" + string.Join(",", vs.ViaBeta!.Select(b => b is int i ? this.g.Find(i) : -1)) + "<" + this.VirtualKey(via) : string.Empty);

    // Bound expansion: an existing node with the shape of a closed-side row's other side binds that side; the row's closed side,
    // instantiated, names a class with an active class demand: request the row on its behalf (finite: no enumeration). Found
    // through the index by (other side's head, its first kid's head). New nodes are scanned each round, all nodes after a merge or
    // when the active class demands change.
    private void EagerExpand(Effects fx, byte active)
    {
        var changed = this.mergedSinceScan;
        foreach (var c in this.idents.Values)
        {
            var now = (byte)(c.Tags & active);
            changed |= now != c.ScannedTags;
            c.ScannedTags = now;
        }

        var total = this.g.NodeTotal;
        var probe = new MatchCtx(this, null, 0, null);
        for (var node = changed ? 0 : this.eagerScanned; node < total; node++)
        {
            var nodeClass = this.g.ClassOfNode(node);
            var head = new HeadKey(this.g.HeadOf(node), this.g.KidCount(node));
            var kid0 = this.g.KidCount(node) == 0 ? new HashSet<HeadKey>() : this.HeadsOf(this.g.KidOf(node, 0));
            foreach (var k in kid0.Append(HeadKey.Any))
            {
                if (!this.closedByPath.TryGetValue((head, k), out var entries))
                {
                    continue;
                }

                foreach (var e in entries)
                {
                    this.work++;
                    var results = new List<int?[]>();
                    this.Match(e.Other, nodeClass, new int?[e.T.VarCount], results, probe);
                    foreach (var beta in Distinct(results))
                    {
                        if (Unbound(e.Other, beta).Count != 0)
                        {
                            continue;
                        }

                        // The closed side names its class, or (with variables the other side leaves free) is matched against each
                        // active class demand.
                        var owners = new List<(ClassDemand Owner, int?[] Beta)>();
                        if (Unbound(e.Side, beta).Count == 0)
                        {
                            var cc = this.ClassOf(e.Side, beta);
                            if (cc >= 0 && this.idents.TryGetValue(cc, out var owner))
                            {
                                owners.Add((owner, beta));
                            }
                        }
                        else
                        {
                            foreach (var owner in this.idents.Values)
                            {
                                if ((owner.Tags & active) == 0)
                                {
                                    continue;
                                }

                                this.work++;
                                var bound = new List<int?[]>();
                                this.Match(e.Side, owner.Class, beta, bound, probe);
                                owners.AddRange(Distinct(bound).Select(b => (owner, b)));
                            }
                        }

                        foreach (var (owner, b) in owners)
                        {
                            var tags = (byte)(owner.Tags & active);
                            if (tags == 0 || this.g.Find(owner.Class) == nodeClass || !this.Usable(e.T, tags))
                            {
                                continue;
                            }

                            var key = (e.T.Id, this.g.Find(owner.Class), BindKey(b), (Row)owner, Both);
                            if (this.expansions.Add(key))
                            {
                                this.Request(e.T, b, tags, Both, owner, fx);
                            }
                        }
                    }
                }
            }
        }

        this.eagerScanned = total;
        this.mergedSinceScan = false;
    }

    private void SelectDemand(Demand d, Effects fx, byte active)
    {
        d.Visited.Clear();
        d.VisitSigns.Clear();
        IEnumerable<Template> candidates;
        if (d.Subject < 0)
        {
            candidates = this.isAll[d.Decl];
        }
        else
        {
            var heads = this.HeadsOf(d.Subject);
            var virtualHeads = this.VirtualHeads(heads);
            candidates = virtualHeads.Contains(HeadKey.Any)
                ? this.isAll[d.Decl]
                : this.isAnySubject[d.Decl].Concat(heads.Concat(virtualHeads).Distinct().SelectMany(h => this.isBySubject.TryGetValue((d.Decl, h), out var list) ? list : []));
        }

        foreach (var t in candidates)
        {
            this.work++;
            var negative = t.Conclusion.Kind == FK.Not;
            var tags = (byte)((negative ? d.NegServe : d.PosServe) & active);
            if (tags == 0 || !this.Usable(t, tags))
            {
                continue;
            }

            var atom = negative ? t.Conclusion.L! : t.Conclusion;
            var ctx = new MatchCtx(this, d, tags, fx) { Sign = negative ? Neg : Pos, Mode = MatchMode.Expand };
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
            var ctx = new MatchCtx(this, d, (byte)(d.Tags & active), fx) { Sign = Both, Mode = MatchMode.Expand };
            foreach (var a in d.Args)
            {
                if (a >= 0)
                {
                    ctx.Visit(this.g.Find(a));
                }
            }

            foreach (var id in this.FactsOf(d.Decl).ToArray())
            {
                this.work++;
                var f = this.facts[this.FactFind(id)];
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

                if (d.Subject < 0 && f.Kind == 0)
                {
                    // A star demand's facts are on classes nobody else visits: each needs its identity facts, which may collapse
                    // the enumeration (t.(K).Y is i32 makes t.(K).Y.(K).Y the same Type as i32.(K).Y).
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

        // Virtual nodes on both sides meet where their shapes can agree (heads both classes' virtual nodes can have).
        var mine = this.Shapes(c);
        var theirs = this.Shapes(other);
        if (mine.Count == 0 || theirs.Count == 0)
        {
            return;
        }

        var shared = mine.Contains(HeadKey.Any) || theirs.Contains(HeadKey.Any) ? mine.Concat(theirs).ToHashSet() : mine.Intersect(theirs).ToHashSet();
        foreach (var head in shared)
        {
            var left = this.Virtuals(c, head, ctx);
            if (left.Count == 0)
            {
                continue;
            }

            foreach (var w in this.Virtuals(other, head, ctx))
            {
                foreach (var vs in left)
                {
                    this.Expand(vs, w.Other, w.Beta, ctx);
                    this.Expand(w, vs.Other, vs.Beta, ctx);
                }
            }
        }
    }

    // A real node of one class meets the virtual nodes of the other whose other side has its head (and can meet its first kid).
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
            HashSet<HeadKey>? kid0 = null;
            if (this.g.KidCount(node) != 0)
            {
                kid0 = this.HeadsOf(this.g.KidOf(node, 0));
                kid0.UnionWith(this.VirtualHeads(kid0));
            }

            foreach (var vs in this.Virtuals(withVirtual, new(this.g.HeadOf(node), this.g.KidCount(node)), ctx, kid0))
            {
                this.ExpandAt(vs, node, ctx);
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

        var key = (vs.T.Id, this.g.Find(vs.Class), BindKey(bind), ctx.Owner!, ctx.Sign);
        if (this.expansions.Add(key))
        {
            this.Request(vs.T, bind, ctx.Tags, ctx.Sign, ctx.Owner!, ctx.Fx!);
        }
    }

    private void SelectIdent(ClassDemand c, Effects fx)
    {
        c.Visited.Clear();
        c.VisitSigns.Clear();

        // Upward congruence makes c's members depend on its nodes' kids: their identity facts are needed too.
        var down = new MatchCtx(this, c, c.Tags, fx) { Sign = Both };
        foreach (var node in this.g.NodesOf(c.Class).ToArray())
        {
            for (var i = 0; i < this.g.KidCount(node); i++)
            {
                this.work++;
                down.Visit(this.g.KidOf(node, i));
            }
        }

        // An identity goal's two classes meet (their virtual nodes expand against each other), as a demand meets the classes
        // holding its Contract's facts.
        if (this.goal.Proposition is { } p && p.IsAtom && (p.Kind == FK.Not ? p.L! : p).Kind == FK.Eq)
        {
            var other = this.g.Find(c.Class) == this.g.Find(this.goal.SubjectClass) ? this.goal.OtherClass : this.g.Find(c.Class) == this.g.Find(this.goal.OtherClass) ? this.goal.SubjectClass : -1;
            if (other >= 0 && this.g.Find(other) != this.g.Find(c.Class))
            {
                this.Meet(this.g.Find(c.Class), this.g.Find(other), new MatchCtx(this, c, c.Tags, fx) { Sign = Both, Mode = MatchMode.Expand });
            }
        }

        // Open sides (those mentioning the row's subject) and negated rows, matched against c's own nodes and requested eagerly.
        // Closed sides are lazy and a side meeting c only through a virtual node is a chain row: VirtualsAt finds both where
        // needed.
        // Indexed by the side's head and its first kid's head, against c's nodes and their first kids' classes.
        var keys = new HashSet<(HeadKey, HeadKey)> { (HeadKey.Any, HeadKey.Any) };
        foreach (var node in this.g.NodesOf(c.Class))
        {
            var head = new HeadKey(this.g.HeadOf(node), this.g.KidCount(node));
            keys.Add((head, HeadKey.Any));
            if (this.g.KidCount(node) != 0)
            {
                foreach (var kid in this.HeadsOf(this.g.KidOf(node, 0)))
                {
                    keys.Add((head, kid));
                }
            }
        }

        foreach (var key in keys)
        {
            if (!this.openSides.TryGetValue(key, out var list))
            {
                continue;
            }

            foreach (var e in list)
            {
                this.work++;
                var tags = e.Negative ? (byte)(c.Tags & c.NegatedFor) : c.Tags;
                if (tags == 0 || !this.Usable(e.T, tags))
                {
                    continue;
                }

                var results = new List<int?[]>();
                this.Match(e.Side, c.Class, new int?[e.T.VarCount], results, new MatchCtx(this, c, tags, fx) { Sign = Both });
                foreach (var beta in Distinct(results))
                {
                    if (e.Negative && Unbound(e.Other, beta).Count != 0)
                    {
                        continue;
                    }

                    this.Request(e.T, beta, tags, Pos, c, fx);
                }
            }
        }
    }

    private void SelectCompound(ClassDemand c, Effects fx)
    {
        c.Visited.Clear();
        c.VisitSigns.Clear();
        var heads = this.HeadsOf(c.Class);
        var virtualHeads = this.VirtualHeads(heads);
        var keys = virtualHeads.Contains(HeadKey.Any) ? this.compoundBySubject.Keys.ToHashSet() : heads.Concat(virtualHeads).Append(HeadKey.Any).ToHashSet();
        foreach (var key in keys)
        {
            if (!this.compoundBySubject.TryGetValue(key, out var list))
            {
                continue;
            }

            foreach (var t in list)
            {
                this.work++;
                if (!this.Usable(t, c.Tags))
                {
                    continue;
                }

                var results = new List<int?[]>();
                var ctx = new MatchCtx(this, c, c.Tags, fx) { Sign = Both, Mode = MatchMode.Expand };
                this.Match(t.Conclusion.Subject, c.Class, new int?[t.VarCount], results, ctx);
                foreach (var beta in Distinct(results))
                {
                    this.Request(t, beta, c.Tags, Pos, c, fx);
                }
            }
        }
    }

    private void Request(Template t, int?[] beta, byte tags, byte sign, Row requester, Effects fx)
    {
        if (t.Antecedent is { } k)
        {
            fx.Subs.Add(new(k, beta[0] ?? -1, Enumerable.Range(1, k.Arity).Select(i => beta[i] ?? -1).ToArray(), null, tags, sign, requester, t, beta, 0, []));
        }
        else if (t.Conditions.Count != 0)
        {
            if (this.Condition(t, 0) is { } c)
            {
                fx.Subs.Add(new(c, -1, [], t.Conditions[0], tags, sign, requester, t, beta, 0, []));
            }
        }
        else if (Unbound(t.Conclusion, beta).Count == 0)
        {
            fx.Fires.Add(new(t, beta, [], tags));
        }
    }

    // A condition is a positive Contract atom (SPEC 8.8.1); its operands are resolved (and interned) when the round is applied.
    private ContractDecl? Condition(Template t, int index)
        => t.Conditions[index] is { Kind: FK.Is } cond ? this.m.DeclOf(cond.B!) : null;

    // A condition operand: a bound variable's class, an instantiated Type when every variable in it is bound, or a star.
    private int Operand(Term t, int?[] beta)
        => t.Head is VarHead v ? beta[v.Index] ?? -1 : Unbound(t, beta).Count == 0 ? this.Inst(t, beta) : -1;

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
        var witnessed = false;
        var from = s.Scanned;
        s.Scanned = list.Count;
        foreach (var id in list.Skip(from).ToArray())
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

            witnessed = true;
            s.Fired.Add(f.Id);
            var ants = s.Ants.Append(f.Id).ToArray();
            foreach (var beta in Distinct(next))
            {
                var following = conditionIndex + 1;
                if (following < s.T.Conditions.Count)
                {
                    if (this.Condition(s.T, following) is { } c)
                    {
                        fx.Subs.Add(new(c, -1, [], s.T.Conditions[following], s.Tags, Pos, s.Requester, s.T, beta, s.Stage + 1, ants));
                    }
                }
                else if (Unbound(s.T.Conclusion, beta).Count == 0)
                {
                    fx.Fires.Add(new(s.T, beta, ants, s.Tags));
                }
            }
        }

        // Semi-naive: the facts appended since the last scan (all of them after a merge). Every such candidate is examined (the
        // work is a function of the round's state); a subscription whose conclusion its bindings fix needs no further witness
        // once a round found one.
        if (witnessed && s.Existential)
        {
            s.Retired = true;
        }
    }

    private bool Usable(Template t, byte tags)
    {
        if (t.BindingId >= 0 && this.view.Inadmissible.Contains(t.BindingId))
        {
            return false; // the inference owner's published admissibility for this view (final for the view)
        }

        if (t.Kind == TemplateKind.Conformance && this.view.UnderCheck.Contains(t.ConformanceId))
        {
            return false; // SPEC 8.3.3: the conformance under its own check supplies no evidence to it (Complete, not Pending)
        }

        switch (t.Source)
        {
            case Status.Valid:
                return true;
            case Status.Pending:
                if (this.includePending)
                {
                    return true;
                }

                this.provisional = true; // a pending source could still supply: never a cached negative
                return false;
            case Status.Resource:
                this.resourceTags |= tags; // the source's own check ended at a limit: what it could supply is unknown
                this.resourceSeen = true;
                if (this.resourceSource.Length == 0)
                {
                    this.resourceSource = t.Origin;
                }

                return false;
            default:
                return false; // Invalid: supplies nothing
        }
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

        // A chain probe asks the same (class, head) for many candidates of one lookup: answered once per lookup.
        var kid0 = p.Kids.Length == 0 ? null : this.KidHeads(p.Kids[0], beta);
        var key = (this.g.Find(c), HeadKey.Of(p), this.SetKey(kid0));
        var virtuals = ctx.Memo is { } memo ? (memo.TryGetValue(key, out var known) ? known : memo[key] = this.VirtualsAt(c, key.Item2, ctx, kid0)) : this.Virtuals(c, key.Item2, ctx, kid0);
        foreach (var vs in virtuals)
        {
            if (ctx.Mode == MatchMode.Chain)
            {
                if (this.MayUnify(vs.Other, p))
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

    // A pattern needs nodes of a virtual source's head in this class: request the deferred row, bound as far as the requester's
    // pattern resolves (a bound variable or an existing ground Type), on behalf of the requester.
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

        var key = (vs.T.Id, this.g.Find(vs.Class), BindKey(bind), ctx.Owner!, ctx.Sign);
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
                if (ReferenceEquals(other, pattern))
                {
                    return true;
                }

                // Two ground Types: one class when both are present; an absent one has no identity facts, so it equals another only
                // by its head and kids (congruence).
                var a = this.g.LookupTerm(other);
                var b = this.g.LookupTerm(pattern);
                if (a >= 0 && b >= 0)
                {
                    return this.g.Find(a) == this.g.Find(b);
                }
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

        foreach (var (nc, nn, owner) in fx.Needs)
        {
            var key = (this.g.Find(nc), nn);
            if (!this.needs.TryGetValue(key, out var row))
            {
                this.needs[key] = row = new NeedRow(key.Item1, nn);
            }

            row.Owners.Add(owner);
        }

        var woken = new HashSet<int>();
        foreach (var vs in fx.Chains)
        {
            var c = this.g.Find(vs.Class);
            if (!this.chains.TryGetValue(c, out var list))
            {
                this.chains[c] = list = new();
            }

            if (!list.Any(x => this.VirtualKey(x) == this.VirtualKey(vs)))
            {
                list.Add(vs);
                woken.Add(c);
            }
        }

        // A new chain row changes what a class can match: rows that visited it are selected again.
        if (woken.Count != 0)
        {
            foreach (var row in this.demands.Cast<Row>().Concat(this.idents.Values).Concat(this.compounds.Values).Concat(this.needs.Values))
            {
                row.Dirty |= row.Visited.Any(v => woken.Contains(this.g.Find(v)));
            }
        }

        foreach (var s in fx.Subs)
        {
            var subject = s.Cond is { } cond ? this.Operand(cond.A!, s.Beta) : s.Subject;
            var args = s.Cond is { } c2 ? c2.B!.Kids.Select(k => this.Operand(k, s.Beta)).ToArray() : s.Args;
            var on = this.EnsureDemand(s.Decl, subject, args);
            var key = new SubKey(s.T.Id, s.Stage, on, s.Requester, s.Sign, this.SubText(s.Beta, s.Ants));
            if (this.subIndex.ContainsKey(key))
            {
                continue;
            }

            var conditionIndex = s.T.Antecedent is null ? s.Stage : s.Stage - 1;
            var sub = new Sub(this.subs.Count, s.T, s.Beta, s.Stage, on, s.Sign, s.Requester, s.Ants)
            {
                Existential = conditionIndex + 1 >= s.T.Conditions.Count && Unbound(s.T.Conclusion, s.Beta).Count == 0,
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

        foreach (var n in this.needs.Values)
        {
            n.Dirty |= n.Visited.Any(merged.Contains) || merged.Contains(n.Class);
        }

        this.Rekey();
    }

    private void Rekey()
    {
        // Demands, class demands, chain rows and facts are keyed by canonical classes; rows that became equal are merged.
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
                into.Visited.UnionWith(d.Visited);
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

        // Need rows of classes that became one are one row (their requesters, visits and subscriptions joined).
        var needMoved = new Dictionary<NeedRow, NeedRow>();
        var needs = new Dictionary<(int Class, HeadKey Need), NeedRow>();
        foreach (var n in this.needs.Values)
        {
            var key = (this.g.Find(n.Class), n.Need);
            if (needs.TryGetValue(key, out var into))
            {
                into.Owners.UnionWith(n.Owners);
                into.Dirty |= n.Dirty || !n.Selected;
                into.Visited.UnionWith(n.Visited);
                foreach (var (k, v) in n.VisitSigns)
                {
                    into.VisitSigns[k] = (byte)(into.VisitSigns.GetValueOrDefault(k) | v);
                }

                into.Requested.AddRange(n.Requested);
                needMoved[n] = into;
                continue;
            }

            n.Class = key.Item1;
            needs.Add(key, n);
        }

        this.needs = needs;
        foreach (var n in this.needs.Values)
        {
            var owners = n.Owners.Select(Owner).ToList();
            n.Owners.Clear();
            n.Owners.UnionWith(owners);
        }

        foreach (var s in this.subs)
        {
            s.Requester = Owner(s.Requester);
        }

        // Subscriptions and expansion records of rows that became one are one (otherwise which row survived, an accident of
        // order, would change the counts the limits read).
        var subs = new List<Sub>();
        this.subIndex = new();
        foreach (var s in this.subs)
        {
            var key = new SubKey(s.T.Id, s.Stage, s.On, s.Requester, s.Sign, this.SubText(s.Beta, s.Ants));
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
            foreach (var row in this.demands.Cast<Row>().Concat(this.idents.Values).Concat(this.compounds.Values).Concat(this.needs.Values))
            {
                row.Requested.RemoveAll(x => !alive.Contains(x));
            }

            this.subs = subs;
        }

        Row Owner(Row r) => r is Demand d && map.TryGetValue(d, out var dm) ? dm : r is ClassDemand c && moved.TryGetValue(c, out var cm) ? cm : r is NeedRow n && needMoved.TryGetValue(n, out var nm) ? nm : r;
        this.expansions = new(this.expansions.Select(e => (e.T, this.g.Find(e.Class), e.Bind, Owner(e.Owner), e.Sign)));

        var chains = new Dictionary<int, List<Virtual>>();
        foreach (var (c, list) in this.chains)
        {
            var root = this.g.Find(c);
            if (!chains.TryGetValue(root, out var into))
            {
                chains[root] = into = new();
            }

            foreach (var vs in list)
            {
                if (!into.Any(x => this.VirtualKey(x) == this.VirtualKey(vs)))
                {
                    into.Add(vs);
                }
            }
        }

        this.chains = chains;

        this.factIndex.Clear();
        this.positiveIs.Clear();
        this.factsOf.Clear();
        foreach (var s in this.subs)
        {
            s.Scanned = 0; // merged classes can make an earlier fact match
        }

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
            this.Index(f);
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
                    into.Visited.UnionWith(c.Visited);
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

    private string SubText(int?[] beta, int[] ants)
        => string.Join(",", beta.Select(x => x is int i ? this.g.Find(i) : -1)) + "|" + string.Join(",", ants.Select(this.FactFind));

    private string BindKey(int?[] bind) => string.Join(",", bind.Select(x => x is int i ? this.g.Find(i).ToString() : "*"));

    private List<int> FactsOf(ContractDecl decl) => this.factsOf.TryGetValue(decl, out var list) ? list : [];

    private void Index(Fact f)
    {
        if (f.Kind > 1)
        {
            return;
        }

        if (!this.factsOf.TryGetValue(f.Decl!, out var all))
        {
            this.factsOf[f.Decl!] = all = new();
        }

        all.Add(f.Id);
        if (f.Kind == 0)
        {
            if (!this.positiveIs.TryGetValue(f.Decl!, out var list))
            {
                this.positiveIs[f.Decl!] = list = new();
            }

            list.Add(f.Id);
        }
    }

    private void Fire(Template t, int?[] beta, int[] ants)
    {
        var c = t.Conclusion;
        var support = new Support(t.Id, ants.Select(this.FactFind).ToArray());
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
            // An identity fact: its own row (a support) and a union of the two classes.
            var f = new Fact(this.facts.Count, 2, null, a, b);
            f.Supports.Add(support);
            this.facts.Add(f);
            this.factAlias.Add(f.Id);
            this.g.Union(a, b);
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
        this.Index(f);
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

    /// <summary>A conclusion head for the static indexes: a symbol head with its arity, or Any for a variable.</summary>
    private readonly record struct HeadKey(Head? Head, int Arity)
    {
        public static readonly HeadKey Any = new(null, -1);

        public bool IsAny => this.Head is null;

        public static HeadKey Of(Term t) => t.Head is VarHead ? Any : new(t.Head, t.Kids.Length);

        public static HeadKey KidOf(Term t) => t.Kids.Length == 0 ? Any : Of(t.Kids[0]);
    }

    /// <summary>One side of an identity row. A side is closed when it does not mention the row's subject: for a Contract row the
    /// side without Self, for a binding the value side. Negated identity rows are never lazy.</summary>
    private sealed class EqSide
    {
        public EqSide(Template t, int side)
        {
            this.T = t;
            this.Negative = t.Conclusion.Kind == FK.Not;
            var atom = this.Negative ? t.Conclusion.L! : t.Conclusion;
            this.Side = side == 0 ? atom.A! : atom.B!;
            this.Other = side == 0 ? atom.B! : atom.A!;
            this.Closed = !this.Negative && (t.Kind == TemplateKind.Binding ? side == 1 : !Mentions(this.Side, 0));
            var vars = new HashSet<int>();
            this.Side.Vars(vars);
            this.SideVars = vars.Count == 0 ? 0 : vars.Max() + 1;
        }

        public int SideVars { get; }

        public Template T { get; }

        public Term Side { get; }

        public Term Other { get; }

        public bool Negative { get; }

        public bool Closed { get; }

        private static bool Mentions(Term t, int variable) => (t.Head is VarHead v && v.Index == variable) || t.Kids.Any(k => Mentions(k, variable));
    }

    private sealed class Goal
    {
        public Goal(Formula p) => this.Proposition = p;

        public Goal(Term subject) => this.Subject = subject;

        public Formula? Proposition { get; }

        public Term? Subject { get; }

        public int SubjectClass { get; set; }

        /// <summary>Gets or sets an identity goal's other side.</summary>
        public int OtherClass { get; set; } = -1;

        /// <summary>Gets or sets the goal polarities no row can supply (settled without search).</summary>
        public byte Impossible { get; set; }
    }

    private abstract class Row
    {
        public abstract byte Tags { get; }

        public bool Selected { get; set; }

        public bool Dirty { get; set; }

        public HashSet<int> Visited { get; } = new();

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

    /// <summary>A need met beyond the chain-detection depth: (class, head), answered as a top-level lookup on its requesters'
    /// behalf.</summary>
    private sealed class NeedRow : Row
    {
        public NeedRow(int c, HeadKey need)
        {
            this.Class = c;
            this.Need = need;
        }

        public int Class { get; set; }

        public HeadKey Need { get; }

        public HashSet<Row> Owners { get; } = new();

        public byte TagsValue { get; set; }

        public override byte Tags => this.TagsValue;
    }

    private sealed class ClassDemand : Row
    {
        public ClassDemand(int c) => this.Class = c;

        public int Class { get; set; }

        public byte Root { get; set; }

        public byte NegatedFor { get; set; }

        public byte TagsValue { get; set; }

        public byte ScannedTags { get; set; }

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

        /// <summary>Gets a value indicating whether the subscription is the template's last stage and its bindings fix the conclusion.</summary>
        public bool Existential { get; init; }

        public bool Retired { get; set; }

        public int[] Ants { get; }

        public HashSet<int> Fired { get; } = new();

        /// <summary>Gets or sets how much of its Contract's fact list the subscription has scanned (reset when classes merge).</summary>
        public int Scanned { get; set; }
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

        public Term? ViaPattern { get; init; }

        public int?[]? ViaBeta { get; init; }
    }

    private sealed class Support
    {
        public Support(int template, int[] ants)
        {
            this.Template = template;
            this.Ants = ants;
        }

        public int Template { get; }

        public int[] Ants { get; }
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

    /// <summary>A subscription to create: an antecedent (subject and arguments as classes) or a condition (Cond's operands resolved
    /// when the round is applied).</summary>
    private sealed record SubRequest(ContractDecl Decl, int Subject, int[] Args, Formula? Cond, byte Tags, byte Sign, Row Requester, Template T, int?[] Beta, int Stage, int[] Ants);

    private enum MatchMode : byte { None, Expand, Chain }

    private sealed record FireRequest(Template T, int?[] Beta, int[] Ants, byte Tags);

    private sealed class Effects
    {
        public List<SubRequest> Subs { get; } = new();

        public List<FireRequest> Fires { get; } = new();

        public HashSet<int> Idents { get; } = new();

        public List<Virtual> Chains { get; } = new();

        public HashSet<(int Class, HeadKey Need, Row Owner)> Needs { get; } = new();

        public byte IdentTags { get; set; }

        public byte ChainTags { get; set; }

        public int Count => this.Subs.Count + this.Fires.Count + this.Idents.Count + this.Chains.Count + this.Needs.Count;

        public byte Tags => (byte)(this.Subs.Aggregate(0, (a, s) => a | s.Tags) | this.Fires.Aggregate(0, (a, s) => a | s.Tags) | this.IdentTags | this.ChainTags);
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

internal static class SetExtensions
{
    public static bool UnionWithChanged<T>(this HashSet<T> set, IEnumerable<T> other)
    {
        var before = set.Count;
        set.UnionWith(other);
        return set.Count != before;
    }
}
