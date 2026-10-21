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
// classes or star) row; a class demand collects what one class needs for its identity, and its classes' kids (upward congruence
// makes a class depend on its kids' identities). One need-driven rule selects every identity row, whichever side mentions the
// row's subject: a side matching a class makes the other side a virtual node of that class, never materialized per row, requested
// only where (a) a pattern needs its head (and kids) at the class or a kid, (b) the class meets another class (a demand and the
// classes of its Contract's facts, a bound variable and its class, an identity goal's two classes, same-head nodes' kids: a meeting
// row per class pair, answered once per round when new or woken), or (c) a node of the other side's shape exists (bound
// expansion); a side meeting a class only through a virtual node is a chain row. A virtual node matches as a node, modulo classes:
// a subterm instantiated to a class matches by class, different classes meet there; two classes' nodes join through the index.
// Static row indexes (heads per Contract position, which polarities a Contract can have) skip impossible expansions and settle
// impossible polarities. Polarity tags, recomputed every round from the goal rows, say which goal polarity a row serves; a polarity
// settles when its evidence is established or none of its rows has work left. A subscription whose conclusion is fixed by its
// bindings retires after the round that found a witness. A selected source in Resource state makes an unsettled answer Resource.
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

public sealed partial class Solver
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
    private Dictionary<(int Class, HeadKey Need), NeedRow> needs = new();
    private Dictionary<(int A, int B), MeetRow> meetings = new();
    private List<MeetRow> meetQueue = new();
    private Dictionary<HeadKey, List<(ShapeEntry Entry, int?[] Beta, int Scanned)>> waiting = new();
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

        var positive = new List<EqSide>();
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
            else if (atom.Kind == FK.Eq && c.Kind == FK.Not)
            {
                for (var side = 0; side < 2; side++)
                {
                    var e = new EqSide(t, side);
                    Add(this.negatedSides, (HeadKey.Of(e.Side), HeadKey.KidOf(e.Side)), e);
                }
            }
            else if (atom.Kind == FK.Eq)
            {
                for (var side = 0; side < 2; side++)
                {
                    var e = new EqSide(t, side);
                    positive.Add(e);
                    if (e.Alias)
                    {
                        this.aliasHeads.Add(HeadKey.Of(e.Side)); // its virtual node is the class its variable binds: VirtualHeads
                    }
                    else
                    {
                        AddTo(this.reach, HeadKey.Of(e.Side), HeadKey.Of(e.Other));
                    }
                    Trie(this.sides, this.otherHeads, e).Add(e); // rows sharing a side (every binding whose value is i32) are one match
                }

                this.AddShapeEntries(t, atom.A!, atom.B!);
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

        // A side is chainable when it can meet a virtual node: some row's other side unifies with it (variables of either row match
        // anything), unless that other side is a variable its own side binds (an alias of an existing class, expanded where any
        // need meets it) or is this row's other side and fixes nothing new (vars(other) ⊆ vars(side): the base instance itself).
        var bases = positive.Where(b => !b.Alias).ToLookup(b => HeadKey.Of(b.Other));
        foreach (var s in positive)
        {
            if (s.Side.Head is not VarHead && bases[HeadKey.Of(s.Side)].Concat(bases[HeadKey.Any]).Any(b => StaticUnify(b.Other, s.Side) && !(b.T == s.T && b.Index != s.Index && s.Trivial)))
            {
                Trie(this.chainable, this.chainOtherHeads, s).Add(s);
                if (s.Alias)
                {
                    this.chainableAliasHeads.Add(HeadKey.Of(s.Side));
                }
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

        // The heads any virtual node can have: a class holding none of them, with no virtual node of its own and no kid that can
        // change, never gains a node (§1 (b): same-head kids meet only where one of them can change).
        foreach (var heads in this.reach.Values)
        {
            this.anyVirtualHeads.UnionWith(heads);
        }

        if (this.aliasHeads.Count != 0)
        {
            this.anyVirtualHeads.Add(HeadKey.Any);
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
        this.keys.Clear();
        this.storedKeys.Clear();
        this.needs = new();
        this.meetings = new();
        this.meetQueue = new();
        this.waiting = new();
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

                foreach (var vs in fx.Chains)
                {
                    this.Trace($"  chain at {this.g.Show(vs.Class)}: {vs.T} other {vs.Other} via {vs.Via!.T} pattern {vs.ViaPattern}");
                }

                foreach (var s in fx.Subs)
                {
                    this.Trace($"  sub {s.Decl}({(s.Subject >= 0 ? this.g.Show(s.Subject) : "*")}) for {s.T} by {s.Requester.GetType().Name}");
                }
            }

            if (this.exhaustive && fx.Count == 0)
            {
                fx = this.Canonicalize();
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
        answer.V = this.demands.Count + this.idents.Count + this.compounds.Count + this.subs.Count + this.chains.Values.Sum(x => x.Count) + this.needs.Count + this.meetings.Count;
        answer.Detail = $"demands {this.demands.Count} idents {this.idents.Count} compounds {this.compounds.Count} subs {this.subs.Count} chains {this.chains.Values.Sum(x => x.Count)} meetings {this.meetings.Count}";
        answer.E = this.facts.Sum(f => f.Supports.Sum(s => 1 + s.Ants.Length));
        answer.N = this.g.Nodes;
        answer.J = this.work + this.g.Work;
        answer.PeakItems = Math.Max(answer.PeakItems, this.Items());
        return answer;
    }

    // Enumerate lists facts modulo identity (SPEC 8.6.1: one instance per class): once no row has work, the facts of each
    // declaration on the subject's class meet pairwise at their arguments, as an identity goal's two classes do (same polarity:
    // `S1 is K3<S1.(K4<i64>).Y>` and `S1 is K3<i32>` with `S1.(K4<i64>).Y is i32` are one fact); what that requests re-enters the
    // fixpoint. An unbounded enumeration never settles, so it never pays for the pairs.
    private Effects Canonicalize()
    {
        var fx = new Effects();
        var s = this.g.Find(this.goal.SubjectClass);
        foreach (var d in this.demands.ToArray())
        {
            if (d.Subject != s || (d.RootPos | d.RootNeg) == 0)
            {
                continue;
            }

            var ctx = new MatchCtx(this, d, Both, fx) { Sign = Both, Mode = MatchMode.Expand };
            var onSubject = this.FactsOf(d.Decl).Select(this.FactFind).Distinct().Select(id => this.facts[id])
                .Where(f => this.g.Find(f.A) == s).Select(f => (f.Kind, Args: this.RefArgs(f.B, d.Decl)!)).ToList();
            for (var i = 0; i < onSubject.Count; i++)
            {
                for (var j = i + 1; j < onSubject.Count; j++)
                {
                    for (var k = 0; k < d.Decl.Arity && onSubject[i].Kind == onSubject[j].Kind; k++)
                    {
                        this.work++;
                        this.Meet(onSubject[i].Args[k], onSubject[j].Args[k], ctx);
                    }
                }
            }
        }

        this.ProcessMeetings(fx, Both);
        fx.Needs.RemoveWhere(n => this.needs.TryGetValue((this.g.Find(n.Class), n.Need), out var row) && row.Owners.Contains(n.Owner));
        return fx;
    }

    private long Items() => this.demands.Count + this.idents.Count + this.compounds.Count + this.subs.Count + this.chains.Values.Sum(x => x.Count) + this.needs.Count + this.meetings.Count + this.facts.Sum(f => f.Supports.Count) + this.g.Nodes;

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

        foreach (var n in this.needs.Values.Cast<OwnedRow>().Concat(this.meetings.Values))
        {
            n.TagsValue = 0;
        }

        var queue = new Queue<Row>(this.demands.Where(d => d.Tags != 0).Cast<Row>().Concat(this.idents.Values.Concat(this.compounds.Values).Where(c => c.Tags != 0)));
        while (true)
        {
            this.Drain(queue);

            // A need or meeting row serves what its requesters serve (requesters may be such rows: to a fixed point).
            foreach (var n in this.needs.Values.Cast<OwnedRow>().Concat(this.meetings.Values))
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
        this.mayGain.Clear();
        this.indexHeads.Clear();
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

        // Meeting rows requested this round or woken since their last answer, each answered once (the queue grows as meetings
        // request their kids' meetings).
        foreach (var m in this.meetings.Values)
        {
            if ((!m.Selected || m.Dirty) && !m.Queued && (m.Tags & active) != 0)
            {
                m.Queued = true;
                this.meetQueue.Add(m);
            }
        }

        this.ProcessMeetings(fx, active);
        return fx;
    }

    private void ProcessMeetings(Effects fx, byte active)
    {
        for (var i = 0; i < this.meetQueue.Count; i++)
        {
            var row = this.meetQueue[i];
            row.Queued = false;
            var tags = (byte)(row.Tags & active);
            if (tags == 0)
            {
                continue;
            }

            row.Selected = true;
            row.Dirty = false;
            row.Visited.Clear();
            row.VisitSigns.Clear();
            this.MeetClasses(row, new MatchCtx(this, row, tags, fx) { Sign = Both, Mode = MatchMode.Expand });
        }

        this.meetQueue.Clear();
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

                // A fact's arguments matter once its subject is the demand's (a merge of the subject wakes the demand).
                if ((d.Subject < 0 || this.g.Find(f.A) == this.g.Find(d.Subject)) && d.Args.Any(a => a >= 0) && this.RefArgs(f.B, d.Decl) is { } args)
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

        // Positive identity rows are never requested here: VirtualsAt finds them where a pattern needs a head (§1 (a)), classes
        // meet (b) or a node of the other side's shape exists (c, EagerExpand). Negated identity rows serve only an identity goal's
        // negation: matched against the goal pair, by the side's head and its first kid's head.
        var negated = (byte)(c.Tags & c.NegatedFor);
        if (negated == 0)
        {
            return;
        }

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
            foreach (var e in this.negatedSides.GetValueOrDefault(key) ?? [])
            {
                this.work++;
                if (!this.Usable(e.T, negated))
                {
                    continue;
                }

                var results = new List<int?[]>();
                this.Match(e.Side, c.Class, new int?[e.T.VarCount], results, new MatchCtx(this, c, negated, fx) { Sign = Both });
                foreach (var beta in Distinct(results))
                {
                    if (Unbound(e.Other, beta).Count == 0)
                    {
                        this.Request(e.T, beta, negated, Pos, c, fx);
                    }
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

            if (this.StoredKeys(c, list).Add(this.VirtualKey(vs)))
            {
                list.Add(vs);
                woken.Add(c);
            }
        }

        // A new chain row changes what a class can match: rows that visited it are selected again.
        if (woken.Count != 0)
        {
            foreach (var row in this.demands.Cast<Row>().Concat(this.idents.Values).Concat(this.compounds.Values).Concat(this.needs.Values).Concat(this.meetings.Values))
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

        this.keys.Clear();
        this.storedKeys.Clear();

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

        // A meeting reads its classes' kids too: a merge below either class wakes it.
        var below = this.meetings.Count == 0 ? new HashSet<int>() : this.g.Upward(merged);
        foreach (var m in this.meetings.Values)
        {
            m.Dirty |= m.Visited.Any(merged.Contains) || below.Contains(this.g.Find(m.A)) || below.Contains(this.g.Find(m.B));
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

        // Meeting rows of pairs that became one pair are one row.
        var meetMoved = new Dictionary<MeetRow, MeetRow>();
        var meetings = new Dictionary<(int A, int B), MeetRow>();
        foreach (var m in this.meetings.Values)
        {
            var (a, b) = (this.g.Find(m.A), this.g.Find(m.B));
            var key = (Math.Min(a, b), Math.Max(a, b));
            if (meetings.TryGetValue(key, out var into))
            {
                into.Owners.UnionWith(m.Owners);
                into.Dirty |= m.Dirty || !m.Selected;
                into.Visited.UnionWith(m.Visited);
                foreach (var (k, v) in m.VisitSigns)
                {
                    into.VisitSigns[k] = (byte)(into.VisitSigns.GetValueOrDefault(k) | v);
                }

                into.Requested.AddRange(m.Requested);
                meetMoved[m] = into;
                continue;
            }

            (m.A, m.B) = key;
            meetings.Add(key, m);
        }

        this.meetings = meetings;
        foreach (var n in this.needs.Values.Cast<OwnedRow>().Concat(this.meetings.Values))
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
            foreach (var row in this.demands.Cast<Row>().Concat(this.idents.Values).Concat(this.compounds.Values).Concat(this.needs.Values).Concat(this.meetings.Values))
            {
                row.Requested.RemoveAll(x => !alive.Contains(x));
            }

            this.subs = subs;
        }

        Row Owner(Row r) => r is Demand d && map.TryGetValue(d, out var dm) ? dm : r is ClassDemand c && moved.TryGetValue(c, out var cm) ? cm : r is NeedRow n && needMoved.TryGetValue(n, out var nm) ? nm : r is MeetRow m && meetMoved.TryGetValue(m, out var mm) ? mm : r;
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
                if (this.StoredKeys(root, into).Add(this.VirtualKey(vs)))
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

    /// <summary>A row answered on its requesters' behalf: it serves what they serve.</summary>
    private abstract class OwnedRow : Row
    {
        public HashSet<Row> Owners { get; } = new();

        public byte TagsValue { get; set; }

        public override byte Tags => this.TagsValue;
    }

    /// <summary>A need met beyond the chain-detection depth: (class, head), answered as a top-level lookup on its requesters'
    /// behalf.</summary>
    private sealed class NeedRow : OwnedRow
    {
        public NeedRow(int c, HeadKey need)
        {
            this.Class = c;
            this.Need = need;
        }

        public int Class { get; set; }

        public HeadKey Need { get; }
    }

    /// <summary>Two classes that must meet (§1 (b)), answered once per round when new or woken.</summary>
    private sealed class MeetRow : OwnedRow
    {
        public MeetRow(int a, int b)
        {
            this.A = a;
            this.B = b;
        }

        public int A { get; set; }

        public int B { get; set; }

        public bool Queued { get; set; }
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

        /// <summary>Gets or sets the meeting requesters or tags added this round.</summary>
        public int Meetings { get; set; }

        public byte MeetTags { get; set; }

        public int Count => this.Subs.Count + this.Fires.Count + this.Idents.Count + this.Chains.Count + this.Needs.Count + this.Meetings;

        public byte Tags => (byte)(this.Subs.Aggregate(0, (a, s) => a | s.Tags) | this.Fires.Aggregate(0, (a, s) => a | s.Tags) | this.IdentTags | this.ChainTags | this.MeetTags);
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
