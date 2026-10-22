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
// only rows whose conclusion can meet a head in its classes; a row whose antecedent Contract can have no fact is not a row. A demand
// is a (Contract declaration, subject class or star, argument classes or star) row; a class demand collects what one class needs for
// its identity, and its classes' kids (upward congruence makes a class depend on its kids' identities). One need-driven rule selects
// every identity row, whichever side mentions the row's subject: a side matching a class makes the other side, under the bindings, a
// promise of that class — a node identified by (class, shape), never by the row or base that produced it (rows and bases producing
// one shape are its members and expand together) — requested only where (a) a pattern needs its head (and kids) at the class or a
// kid, (b) the class meets another class (a demand and the classes of its Contract's facts, a bound variable and its class, an
// identity goal's two classes, same-head nodes' kids: a meeting row per class pair, answered once per round when new or woken), or
// (c) a node of the other side's shape exists (bound expansion); a side meeting a class only through a promise is a chain, a member
// forwarding to that promise. A promise matches as a node, modulo classes: the top by head, every kid by class (different classes
// meet there; a kid that is not a Type yet is decided once built); filters read classes only; two classes' promises join by
// unification through an index, each pair of promises once. Static row indexes (heads per Contract position, which polarities a
// Contract can have) skip impossible expansions and settle impossible polarities. Polarity tags, recomputed every round from the goal
// rows, say which goal polarity a row serves; a polarity
// settles when its evidence is established or none of its rows has work left. A subscription whose conclusion is fixed by its
// bindings retires after the round that found a witness. A selected source in Resource state makes an unsettled answer Resource.
#pragma warning disable
#nullable enable
using System.Diagnostics;

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

    /// <summary>Gets the polarities established: at a limit, those established at the stop (B 3.10).</summary>
    public string Verdict => (this.Positive ? "+" : "") + (this.Negative ? "-" : "") + (!this.Positive && !this.Negative ? "none" : string.Empty);

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
    private readonly Dictionary<LookupKey, (List<Promise> Result, List<int> Visits, List<(int, HeadKey)> Needs, bool Resource, bool Chains)> roundMemo = new();
    private List<int>? visitLog;
    private List<(int Class, HeadKey Need)>? needLog;
    private bool chainSeen;
    private readonly Dictionary<HeadKey, int> headIds = new();
    private readonly Dictionary<int, HashSet<HeadKey>> virtualHeadsOf = new();
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
    private HashSet<(int T, int Class, int Bind, Row Owner, byte Sign)> expansions = new();
    private readonly Dictionary<(Promise Promise, int Pattern, byte Sign), (List<(int, int)> Meets, List<(Template T, int?[] Bind)> Rows)> expanded = new();
    private Dictionary<int, Dictionary<int, Promise>> chains = new();
    private ShapeTable shapes = new();
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

        this.SubjectHeads();
        var positive = new List<EqSide>();
        foreach (var t in model.Templates)
        {
            if (!this.Live(t))
            {
                continue; // rule 4: a row whose antecedent no fact can have is not a row (a static row-index fact)
            }

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

    public int TemplateCount => this.m.Templates.Count;

    public Action<string>? Trace { get; set; }

    /// <summary>Gets the last request's time per phase and its re-examination counts (measurement only; C1-design §7).</summary>
    public PhaseTicks Phases { get; } = new();

    // A static row index (request independent): the heads a fact of K can have at each position (0: the subject, i: the i-th
    // argument), in its identity class, over live rows (rule 4). Refinement and clause rows inherit their antecedent's positions
    // through variables; identity rows connect the heads of their two sides; a variable that no antecedent position fixes admits any
    // head. Expansion skips a promise whose requester needs, at a variable's position, a head no fact of the antecedent Contract can
    // have there.
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
                if (!this.Live(t))
                {
                    continue;
                }

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

    // Rule 4: a row is live when its antecedent Contract can have a fact (some live row, input or conformance concludes `is K`): a
    // joint fixpoint with the position index (inputs and conformances are always live; an over-approximation, never a filter on views).
    private bool Live(Template t) => t.Antecedent is not { } k || this.anyHead.Contains((k, 0)) || this.positionHeads[(k, 0)].Count != 0;

    /// <summary>Entry 1: the available evidence for a bound proposition and for its negation.</summary>
    public Answer Supply(Formula proposition, View view, Limits? limits = null)
    {
        this.Reset(view, limits, includePending: false, exhaustive: false);
        this.goal = new Goal(proposition);
        this.SeedGoal();
        var answer = this.Run();
        (answer.Positive, answer.Negative) = this.Holds(proposition); // at a limit the polarities established at the stop stand (B 3.10, D9)
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
    public List<string> DumpSubs() => this.subs.Select(x => $"#{x.T.Id} st{x.Stage} on {x.On.Decl}({(x.On.Subject >= 0 ? this.g.Show(x.On.Subject) : "*")};{string.Join(",", x.On.Args.Select(a => a >= 0 ? this.g.Show(a) : "*"))}) by {x.Owners.Count} owners beta [{string.Join(",", x.Beta.Select(b => b is int i ? this.g.Show(i) : "*"))}] sign {x.Sign} ants {x.Ants.Length}").OrderBy(x => x, StringComparer.Ordinal).ToList();

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
        this.expanded.Clear();
        this.chains = new();
        this.shapes = new();
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
        this.Phases.Reset();
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

                foreach (var (p, m) in fx.Chains)
                {
                    this.Trace($"  chain at {this.g.Show(p.Class)}: {p.Other} [{string.Join(", ", p.Beta.Select(x => x is int i ? this.g.Show(i) : "*"))}] via {this.g.Show(m.Via.Class)}#{m.Via.Shape} pattern {m.Pattern}");
                }

                foreach (var s in fx.Subs)
                {
                    this.Trace($"  sub {s.Decl}({(s.Subject >= 0 ? this.g.Show(s.Subject) : "*")};{string.Join(",", s.Args.Select(a => a >= 0 ? this.g.Show(a) : "*"))}) for {s.T} by {string.Join("+", s.Requesters.Select(r => r.GetType().Name))} beta [{string.Join(",", s.Beta.Select(b => b is int i ? this.g.Show(i) : "*"))}]");
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
            no = this.factIndex.ContainsKey(new(3, null, Math.Min(a, b), Math.Max(a, b), null)) || this.g.Refuted(a, b); // a negated fact, or B 3.3's structural Refuted
        }

        return p.Kind == FK.Not ? (no, yes) : (yes, no);
    }

    // ---- Rounds ----
    private Effects Compute(byte active)
    {
        var fx = new Effects();
        this.roundMemo.Clear(); // promises and their expansions are a function of the round's starting state
        this.expanded.Clear();
        this.virtualHeadsOf.Clear();
        this.shapes.Forget();
        this.mayGain.Clear();
        this.indexHeads.Clear();
        var t0 = Stopwatch.GetTimestamp();
        foreach (var d in this.demands.ToArray())
        {
            if ((!d.Selected || d.Dirty) && (d.Tags & active) != 0)
            {
                var woken = d.Selected;
                d.Selected = true;
                d.Dirty = false;
                var t = Stopwatch.GetTimestamp();
                this.SelectDemand(d, fx, active);
                this.Phases.Add(woken ? Phase.DemandWoken : Phase.DemandFirst, t);
            }
        }

        t0 = Stopwatch.GetTimestamp();
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

        this.Phases.Add(Phase.ClassDemands, t0);
        t0 = Stopwatch.GetTimestamp();
        foreach (var s in this.subs.ToArray())
        {
            if (!s.Retired && (s.Tags & active) != 0)
            {
                this.Scan(s, fx);
            }
        }

        this.Phases.Add(Phase.Scans, t0);
        t0 = Stopwatch.GetTimestamp();

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

        this.Phases.Add(Phase.Needs, t0);
        t0 = Stopwatch.GetTimestamp();
        this.EagerExpand(fx, active);
        this.Phases.Add(Phase.Eager, t0);

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
        var t0 = Stopwatch.GetTimestamp();
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
            this.Phases.Meetings++;
            this.MeetClasses(row, new MatchCtx(this, row, tags, fx) { Sign = Both, Mode = MatchMode.Expand });
        }

        this.meetQueue.Clear();
        this.Phases.Add(Phase.Meetings, t0);
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
        if (!this.virtualHeadsOf.TryGetValue(c, out var result))
        {
            this.virtualHeadsOf[c] = result = this.RefHead(c) is null ? this.VirtualHeads(this.HeadsOf(c)) : new(); // D5: no row gives a reference class a node
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

    // ---- Promises: the matching component (§1 rules 1–3) ----
    // The promises of c whose index head is the needed head (HeadKey.Any: all) and whose class kids the requester can meet (o0, o1:
    // the heads its pattern's or node's kids 0 and 1 can have; null: any), computed where needed, never stored except chains. One
    // rule for every identity side, whichever side mentions the row's subject:
    // - a direct row: a side whose head is a head of c (or a variable) e-matches c; its other side under the bindings is a promise of c;
    // - a chain row: a chainable side that meets c only through a promise of c (at the top, or at a kid of one of c's nodes) makes its
    //   row's other side a promise of c too, forwarding to that promise (chains forward to records).
    // A promise is identified by (c, shape): every row and base producing one shape at c is a member of one promise, and a stored
    // chain promise found again is the same promise. Candidates are grouped by their side Term, so rows sharing a side cost one match.
    // The matches visit classes for the requester (so their merges wake it) without expanding anything.
    private List<Promise> VirtualsAt(int c, HeadKey need, MatchCtx requester, HashSet<HeadKey>? o0 = null, HashSet<HeadKey>? o1 = null)
    {
        c = this.g.Find(c);
        var result = new Dictionary<int, Promise>();
        var heads = this.HeadsOf(c);

        // Chains are detected for the need and for the needs its candidates meet (two levels; deeper lookups return direct rows
        // and stored chains only) and stored as request rows, so a chain through a chain through a chain appears a round later:
        // bounded work per lookup, and every need a lookup meets gets its own chains (a need row: a tagged effect of the round).
        var chain = this.chainGuard.Count < 2 && this.chainGuard.Add((c, need));
        this.Trace?.Invoke($"  lookup {this.g.Show(c)} {(need.Head is SymHead sh ? sh.Name : "*")}/{need.Arity} level {this.chainGuard.Count - (chain ? 1 : 0)} chains {chain} mode {requester.Mode}");
        if (!chain && this.chainGuard.Count >= 2)
        {
            this.needLog?.Add((c, need));
            if (requester.Fx is { } pending && requester.Owner is { } owner)
            {
                pending.AddNeed(c, need, owner, requester.Tags); // a need row: answered as a top-level lookup next round
            }
        }

        if (this.chains.TryGetValue(c, out var stored))
        {
            foreach (var p in stored.Values)
            {
                if (need.IsAny || p.Key.IsAny || p.Key == need)
                {
                    result[p.Shape] = new Promise(c, p.Shape, p.Other, p.Beta) { ChainSet = new(p.ChainSet) };
                }
            }
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
                // Chain-only candidates: chainable sides with a head c or one of its promises can have.
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

            var groups = new Dictionary<Term, SideMatch>();
            var memo = new Dictionary<LookupKey, List<Promise>>();
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

        // Rule 3: a filter reads classes only. A promise's class kids are compared with the requester's kid filters; a kid that is a
        // shape variable or a shape node (not a Type yet) passes and is decided once built.
        var list = new List<Promise>();
        foreach (var p in result.Values)
        {
            if (p.Shape >= 0 || this.shapes.IsVar(p.Shape) || (this.Passes(p, 0, o0) && this.Passes(p, 1, o1)))
            {
                list.Add(p);
            }
        }

        return list;
    }

    private bool Passes(Promise p, int i, HashSet<HeadKey>? filter)
    {
        var kids = this.shapes.KidsOf(p.Shape);
        return filter is null || kids.Length <= i || kids[i] < 0 || filter.Contains(HeadKey.Any) || filter.Overlaps(this.KidShapes(kids[i]));
    }

    // The heads kid i of a side with head h can have to meet one of c's nodes of that head: those of the nodes' kids and of their
    // promises (null: any).
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

    // The heads a class can have once its promises are expanded: its own and its promises' (HeadKey.Any: any).
    private HashSet<HeadKey> KidShapes(int c)
    {
        var heads = this.HeadsOf(c);
        heads.UnionWith(this.Shapes(c));
        return heads;
    }

    // VirtualsAt for a requester: a lookup outside any other lookup is a function of (class, need, kid filters) and the round's
    // state, so it is answered once per round and replayed (its visits, needs and Resource reads) for later requesters.
    private List<Promise> Virtuals(int c, HeadKey need, MatchCtx requester, HashSet<HeadKey>? o0 = null, HashSet<HeadKey>? o1 = null)
    {
        if (this.chainGuard.Count != 0)
        {
            return this.VirtualsAt(c, need, requester, o0, o1);
        }

        var key = new LookupKey(this.g.Find(c), need, this.SetKey(o0), this.SetKey(o1));
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
                fx.AddNeed(nc, nn, owner, requester.Tags);
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

    // An order-free key for a set of heads (heads are numbered on first sight; the numbers only name them); null: any.
    private IntArrayKey SetKey(HashSet<HeadKey>? heads)
    {
        if (heads is null)
        {
            return new(null);
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
        return new(ids);
    }

    // Rows sharing a side are matched once; a side that meets c neither directly nor through a promise costs one step.
    private void Candidate(Term side, List<EqSide> entries, int c, MatchCtx requester, bool chain, bool direct, Dictionary<Term, SideMatch> groups, Dictionary<LookupKey, List<Promise>> memo, Dictionary<int, Promise> result)
    {
        this.work++;
        if (!groups.TryGetValue(side, out var group))
        {
            var results = new List<int?[]>();
            var ctx = requester.Derive(chain ? MatchMode.Chain : MatchMode.None, memo);
            this.Match(side, c, new int?[entries[0].SideVars], results, ctx);
            groups[side] = group = new SideMatch(Distinct(results).ToList(), ctx.Chain ?? []);
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

    // A matched side's instantiations join the promise at (c, the other side's shape under the bindings) as direct members; a chain hit
    // (the sub-pattern of the side that met a promise, that promise, the bindings the side's match established) joins the promise at
    // (c, the other side's shape under those bindings) as a chain member, a set modulo classes. However many rows or bases produce one
    // shape at c, it is one promise (R4-B1).
    private void Produce(EqSide e, int c, MatchCtx requester, bool direct, SideMatch group, Dictionary<int, Promise> result)
    {
        if (direct)
        {
            foreach (var r in group.Results)
            {
                var beta = new int?[e.T.VarCount];
                Array.Copy(r, beta, r.Length);
                var varOf = new List<int>();
                var shape = this.shapes.Of(e.Other, beta, varOf, this.g);
                this.At(result, c, shape, e.Other, beta).Rows.Add(new Direct(e, beta, varOf.ToArray()));
            }
        }

        foreach (var (via, pattern, beta) in group.Hits)
        {
            // The base may be another instance of this very row (its other side matched c): the chain bootstraps that instance,
            // after which this side matches c directly (`A.(K2).Y is List<i32>` joins T's and U's projections). When matching this
            // side fixes the other one and the hit is the side itself on the promise its own row produced, that instance is the base
            // itself: no chain.
            if (e.Trivial && ReferenceEquals(pattern, e.Side) && via.ChainSet.Count == 0 && via.Rows.All(d => d.E.T == e.T && ReferenceEquals(d.E.Other, e.Side)))
            {
                continue;
            }

            // A chain forwards to a base record's rows, one level: a hit on a promise with rows forwards to it; a hit on a chain
            // promise takes over its members (their bases), as 152dda62's chains forwarded to their base's base.
            var varOf = new List<int>();
            var shape = this.shapes.Of(e.Other, beta, varOf, this.g);
            var p = this.At(result, c, shape, e.Other, beta);
            if (via.Rows.Count != 0 && !(via.Class == c && via.Shape == shape))
            {
                this.Store(p, new Chain(pattern, via, beta), requester);
            }

            foreach (var m in via.Chains)
            {
                if (!(this.g.Find(m.Via.Class) == c && this.shapes.Canon(m.Via.Shape, this.g) == shape))
                {
                    this.Store(p, m, requester);
                }
            }
        }
    }

    // A chain member joins its promise (a set modulo classes); when the stored promise lacks it, it is an effect of the round.
    private void Store(Promise p, Chain member, MatchCtx requester)
    {
        var added = p.AddChain(member, this);
        if (requester.Fx is not { } fx)
        {
            return;
        }

        var known = this.chains.TryGetValue(p.Class, out var stored) && stored.TryGetValue(p.Shape, out var sp) && sp.HasChain(member, this);
        if (added && !known)
        {
            fx.Chains.Add((p, member)); // stored when the round is applied: a later lookup finds chains through it
            fx.ChainTags |= requester.Tags;
            this.chainSeen = true;
        }
    }

    private Promise At(Dictionary<int, Promise> result, int c, int shape, Term other, int?[] beta)
    {
        if (!result.TryGetValue(shape, out var p))
        {
            result[shape] = p = new Promise(c, shape, other, beta);
        }

        return p;
    }

    // The key of a chain member modulo classes: its pattern, its base's identity (class, canonical shape) and its bindings by class.
    private ChainKey KeyOf(Chain m) => new(m.Pattern.Id, this.g.Find(m.Via.Class), this.shapes.Canon(m.Via.Shape, this.g), this.Classes(m.ViaBeta));

    private IntArrayKey Classes(int?[] beta)
    {
        var ids = new int[beta.Length];
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = beta[i] is int b ? this.g.Find(b) : -1;
        }

        return new(ids);
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

            var key = (entry.T.Id, this.g.Find(owner!.Class), this.shapes.Tuple(b, this.g), (Row)owner, Both);
            if (this.expansions.Add(key))
            {
                this.Request(entry.T, b, tags, Both, owner, fx);
            }
        }
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
        if (!Equals(this.RefHead(c), this.RefHead(other)) || (!this.MayGain(c) && !this.MayGain(other)))
        {
            return; // a reference class and a Type class, or references of two declarations (D5), or neither can gain a node: never one
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

    // A meeting row's answer: the nodes of the two classes, real or promised, meet as nodes (§1): each real node of one class probes
    // the other's promises by its head and kid heads and agrees with each found; the two classes' promises join (MeetPromises);
    // same-head real nodes meet at their kids (upward congruence), as kid meetings.
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

        // Promises meet promises at the heads the index can give both classes' promises: one join per class pair, both directions.
        var shared = this.IndexHeads(c);
        shared.IntersectWith(this.IndexHeads(other));
        this.MeetPromises(c, other, shared, ctx);

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

    // The join of two classes' promises at the heads the index gives both (rule 1, R4-B1: each pair of promises once), as unification
    // through an index. The promises of each class at a head come from the lookup without kid filters and are indexed by their class
    // vector (per position: the class, or a wildcard for a shape variable or a shape node); per position, every pair of distinct
    // classes meets; a promise's partners are the other side's promises classed as it is or wild at each of its classed positions
    // (the smallest list is iterated, ties by the lowest position, so the promises examined are a function of the state); per
    // partner, the tuple binding the promise's shape variables (a variable at two positions meeting two classes: one meet, no
    // request) is expanded with the partner's shape-node heads as needs. Equal to the pairwise agreement of every (u, w); bounded by
    // distinct classes² per position, never promises². The join is symmetric in its work: which class is a or b is an accident of
    // class ids, and J is a function of the pair. J counts promises indexed, partners examined, tuples expanded and meets issued.
    // Promises whose other side is a variable meet real nodes only.
    private void MeetPromises(int a, int b, HashSet<HeadKey> heads, MatchCtx ctx)
    {
        foreach (var h in heads)
        {
            var ia = this.IndexBy(this.Virtuals(a, h, ctx), h);
            var ib = this.IndexBy(this.Virtuals(b, h, ctx), h);
            if (ia.All.Count == 0 || ib.All.Count == 0)
            {
                continue;
            }

            for (var i = 0; i < h.Arity; i++)
            {
                foreach (var ca in ia.Classed[i].Keys)
                {
                    foreach (var cb in ib.Classed[i].Keys)
                    {
                        this.work++;
                        if (ca != cb)
                        {
                            this.Meet(ca, cb, ctx);
                        }
                    }
                }
            }

            this.Partners(ia, ib, ctx);
            this.Partners(ib, ia, ctx);
        }
    }

    // The promises of one class at one head, indexed per kid position by class (a shape variable or node is wild there).
    private PromiseIndex IndexBy(List<Promise> promises, HeadKey h)
    {
        var index = new PromiseIndex(h.Arity);
        foreach (var p in promises)
        {
            if (p.Key.IsAny)
            {
                continue; // an alias or a free-variable other side: no kids to join on
            }

            this.work++;
            var vector = new int[h.Arity];
            var kids = p.Shape >= 0 ? ReadOnlySpan<int>.Empty : this.shapes.KidsOf(p.Shape);
            for (var i = 0; i < h.Arity; i++)
            {
                vector[i] = i < kids.Length && kids[i] >= 0 ? kids[i] : -1; // a class-shaped promise: wild everywhere
                if (vector[i] >= 0)
                {
                    if (!index.Classed[i].TryGetValue(vector[i], out var list))
                    {
                        index.Classed[i][vector[i]] = list = new();
                    }

                    list.Add(p);
                }
                else
                {
                    index.Wild[i].Add(p);
                }
            }

            index.All.Add((p, vector));
        }

        return index;
    }

    // Each promise of `from` against its partners in `to`: the promises classed as it is or wild at each of its classed positions
    // (the smallest list is iterated, ties by the lowest position), each pair agreed once (rule 2, at every depth): meets, or the
    // bindings its shape variables take and the heads they met; chains forward once per promise with a partner.
    private void Partners(PromiseIndex from, PromiseIndex to, MatchCtx ctx)
    {
        foreach (var (u, vector) in from.All)
        {
            var best = -1;
            var size = int.MaxValue;
            for (var i = 0; i < vector.Length; i++)
            {
                if (vector[i] >= 0)
                {
                    var n = (to.Classed[i].TryGetValue(vector[i], out var l) ? l.Count : 0) + to.Wild[i].Count;
                    if (n < size)
                    {
                        (best, size) = (i, n);
                    }
                }
            }

            var candidates = best < 0 ? to.All.Select(x => x.Promise) : (to.Classed[best].TryGetValue(vector[best], out var lc) ? lc : []).Concat(to.Wild[best]);
            var any = false;
            foreach (var w in candidates)
            {
                this.work++;
                var bindU = new int?[this.shapes.VarCount(u.Shape)];
                var bindW = new int?[this.shapes.VarCount(w.Shape)];
                var needs = new Dictionary<int, Head>();
                var meets = new List<(int, int)>();
                if (!this.Agree(u.Shape, bindU, w.Shape, bindW, meets, needs))
                {
                    continue;
                }

                if (meets.Count != 0)
                {
                    foreach (var (x, y) in meets)
                    {
                        this.Meet(x, y, ctx);
                    }

                    continue;
                }

                any = true;
                this.RequestRows(u, this.BoundRows(u, bindU, needs), ctx);
            }

            if (any)
            {
                foreach (var m in u.Chains)
                {
                    this.Forward(m, ctx);
                }
            }
        }
    }

    // The heads the index can give c's promises whose other side is not a variable: rows whose side can have a head of c (or is a
    // variable), chainable rows whose side can have a head of c's promises, c's chains. Once per round.
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

            result.UnionWith(this.chains.GetValueOrDefault(c)?.Values.Select(p => p.Key) ?? []);
            result.Remove(HeadKey.Any);
            this.indexHeads[c] = result;
        }

        return new HashSet<HeadKey>(result);
    }

    // An alias: a promise that is an existing class (its other side a variable its side binds, or a ground Type interned already),
    // requested without agreement.
    private bool IsAlias(Promise p) => p.Shape >= 0;

    // A real node of one class meets the promises of the other whose other side has its head (and can meet its kids): the promise agrees
    // with the node as a node (a bare-variable promise, which has no kids, takes the node's class).
    private void MeetOneWay(int withVirtual, int other, MatchCtx ctx)
    {
        var shapes = this.Shapes(withVirtual);
        if (shapes.Count == 0)
        {
            return; // no row can give withVirtual a promise (a static index fact)
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
            foreach (var p in this.Virtuals(withVirtual, new(this.g.HeadOf(node), this.g.KidCount(node)), ctx, kid0, kid1))
            {
                this.Expand(p, p.Shape < 0 && this.shapes.IsVar(p.Shape) ? this.g.Find(other) : this.shapes.OfNode(node, this.g), ctx);
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
            fx.Subs.Add(new(k, beta[0] ?? -1, Enumerable.Range(1, k.Arity).Select(i => beta[i] ?? -1).ToArray(), null, tags, sign, [requester], t, beta, 0, []));
        }
        else if (t.Conditions.Count != 0)
        {
            if (this.Condition(t, 0) is { } c)
            {
                fx.Subs.Add(new(c, -1, [], t.Conditions[0], tags, sign, [requester], t, beta, 0, []));
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
        this.Phases.ScanRescanned += Math.Max(0, Math.Min(s.Peak, list.Count) - from);
        this.Phases.ScanNew += list.Count - Math.Max(from, s.Peak);
        s.Peak = Math.Max(s.Peak, list.Count);
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
                        fx.Subs.Add(new(c, -1, [], s.T.Conditions[following], s.Tags, Pos, s.Owners.ToArray(), s.T, beta, s.Stage + 1, ants));
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

    // The pattern needs nodes of its head in c that only a promise of c can provide: a requester expands them; a chain probe records
    // them (an alias is an existing class, not a node to meet: expanding it where a need meets it merges the classes, after which the
    // side matches directly).
    private void ThroughVirtuals(Term p, int c, int?[] beta, MatchCtx ctx)
    {
        if (ctx.Mode == MatchMode.None || this.Shapes(c) is var shapes && !shapes.Contains(HeadKey.Any) && !shapes.Contains(HeadKey.Of(p)))
        {
            return; // no row can give c a promise of p's head (a static index fact)
        }

        // A chain probe asks the same (class, head, kid filters) for many candidates of one lookup: answered once per lookup.
        var kid0 = p.Kids.Length > 0 ? this.KidHeads(p.Kids[0], beta) : null;
        var kid1 = p.Kids.Length > 1 ? this.KidHeads(p.Kids[1], beta) : null;
        var key = new LookupKey(this.g.Find(c), HeadKey.Of(p), this.SetKey(kid0), this.SetKey(kid1));
        var promises = ctx.Memo is { } memo ? (memo.TryGetValue(key, out var known) ? known : memo[key] = this.VirtualsAt(c, key.Need, ctx, kid0, kid1)) : this.Virtuals(c, key.Need, ctx, kid0, kid1);
        if (ctx.Mode == MatchMode.Chain)
        {
            foreach (var w in promises)
            {
                ctx.Chain!.Add((w, p, beta)); // every promise met is a base to forward to (a class-shaped one too: its members are rows and chains)
            }

            return;
        }

        var pattern = this.shapes.Of(p, beta, new List<int>(), this.g);
        foreach (var w in promises)
        {
            this.Expand(w, pattern, ctx);
        }
    }

    // Rule 3: the heads a pattern's kid can meet, from a class only: those of its class and that class's promises when it resolves to
    // a class; its own head and that head's promises when it names a ground Type not interned yet (its class, once interned, holds
    // exactly that node); anything (null) when it is not a Type yet (a free variable inside it).
    private HashSet<HeadKey>? KidHeads(Term kid, int?[] beta)
    {
        var c = kid.Head is VarHead v ? beta[v.Index] ?? -1 : this.ClassOf(kid, beta);
        if (c >= 0)
        {
            return this.KidShapes(c);
        }

        if (kid.HasVar)
        {
            return null;
        }

        var heads = new HashSet<HeadKey> { HeadKey.Of(kid) };
        if (kid.Head is SymHead { Kind: SymKind.Ref })
        {
            return heads; // D5: a reference's class holds that reference only
        }

        var result = this.VirtualHeads(heads);
        result.UnionWith(heads);
        return result;
    }

    // A pattern needs nodes of a promise's head in its class, or a meeting pairs the promise with a real node: the promise agrees with
    // the pattern as a node, modulo classes (rule 2), its shape variables bound as far as the pattern fixes them; kid classes that differ
    // meet first (their merge wakes the requester, which matches again); otherwise every direct member's row is requested on behalf
    // of the requester. Chain members forward to their base whatever the pattern (the base must fire before the chained side can
    // match), as 152dda62's chains did. An alias is requested without agreement.
    private void Expand(Promise w, int pattern, MatchCtx ctx)
    {
        foreach (var m in w.Chains)
        {
            this.Forward(m, ctx);
        }

        this.ExpandRows(w, pattern, ctx);
    }

    // The agreement and the direct members' requests (a promise's own rows): a function of (promise, pattern, sign) and the round's
    // state, answered once per round and replayed for later requesters (D2 under D1's owned rows): the kid meetings it issues and
    // the rows it requests are the same for every requester, which joins the meeting rows and the subscriptions (once per requester
    // across rounds: expansions). J counts the expansion and its rows once.
    private void ExpandRows(Promise w, int pattern, MatchCtx ctx)
    {
        var key = (w, pattern, ctx.Sign);
        if (!this.expanded.TryGetValue(key, out var entry))
        {
            this.work++;
            var shape = this.shapes.Canon(w.Shape, this.g);
            var bindW = new int?[this.shapes.VarCount(shape)];
            var needs = new Dictionary<int, Head>();
            var meets = new List<(int, int)>();
            var rows = new List<(Template T, int?[] Bind)>();
            if (!this.IsAlias(w) && !this.Agree(shape, bindW, pattern, new int?[this.shapes.VarCount(pattern)], meets, needs))
            {
                meets.Clear();
            }
            else if (meets.Count == 0)
            {
                rows.AddRange(this.BoundRows(w, bindW, needs));
            }

            this.expanded[key] = entry = (meets, rows);
        }

        foreach (var (a, b) in entry.Meets)
        {
            this.Meet(a, b, ctx);
        }

        this.RequestRows(w, entry.Rows, ctx);
    }

    // A chain forwards to its base's rows: the round's promise at the base's (class, shape) (rule 1: chains forward to records; the
    // stored base is the member's identity, never a snapshot of one lookup's members), found by the lookup the base's own shape
    // asks for (its head and class kids; Any for a variable or an existing class), and agreed with the chain's pattern under the
    // bindings the side's match established.
    private void Forward(Chain m, MatchCtx ctx)
    {
        var c = this.g.Find(m.Via.Class);
        var shape = this.shapes.Canon(m.Via.Shape, this.g);
        foreach (var via in this.Virtuals(c, this.HeadOfShape(shape), ctx, this.ShapeKidHeads(shape, 0), this.ShapeKidHeads(shape, 1)))
        {
            if (via.Shape == shape)
            {
                this.ExpandRows(via, this.shapes.Of(m.Pattern, m.ViaBeta, new List<int>(), this.g), ctx);
            }
        }
    }

    // The index head a shape asks for: its head, or Any for a shape variable or an existing class (rule 2).
    private HeadKey HeadOfShape(int shape) => shape >= 0 || this.shapes.IsVar(shape) ? HeadKey.Any : new(this.shapes.HeadOf(shape), this.shapes.KidsOf(shape).Length);

    // Rule 3 for a shape's kid i: a class kid can meet its heads and its promises' heads; a shape variable or node kid anything (null).
    private HashSet<HeadKey>? ShapeKidHeads(int shape, int i)
    {
        if (shape >= 0 || this.shapes.IsVar(shape))
        {
            return null;
        }

        var kids = this.shapes.KidsOf(shape);
        return i < kids.Length && kids[i] >= 0 ? this.KidShapes(kids[i]) : null;
    }

    // An agreed promise's direct members with their bindings: the row's bindings plus the shape variables the agreement bound, unless
    // no fact of its antecedent Contract can have the head a free variable met (the static prune on the position index).
    private IEnumerable<(Template T, int?[] Bind)> BoundRows(Promise w, int?[] bindW, Dictionary<int, Head> needs)
    {
        foreach (var d in w.Rows)
        {
            this.work++;
            var bind = (int?[])d.Beta.Clone();
            for (var n = 0; n < bindW.Length; n++)
            {
                if (bindW[n] is int cls)
                {
                    bind[d.VarOf[n]] = cls;
                }
            }

            if (d.E.T.Antecedent is { } k && needs.Any(x => d.VarOf[x.Key] is var v && bind[v] is null && v <= k.Arity && !this.anyHead.Contains((k, v)) && !this.positionHeads[(k, v)].Contains(x.Value)))
            {
                continue;
            }

            yield return (d.E.T, bind);
        }
    }

    // The rows are requested on behalf of the requester, once per (row, class, bindings, requester, sign).
    private void RequestRows(Promise w, IEnumerable<(Template T, int?[] Bind)> rows, MatchCtx ctx)
    {
        foreach (var (t, bind) in rows)
        {
            var key = (t.Id, this.g.Find(w.Class), this.shapes.Tuple(bind, this.g), ctx.Owner!, ctx.Sign);
            if (this.expansions.Add(key))
            {
                this.Request(t, bind, ctx.Tags, ctx.Sign, ctx.Owner!, ctx.Fx!);
            }
        }
    }

    // Matching modulo classes over the kid encoding (rule 2): a and b are classes (>= 0) or shapes; bindA and bindB the shape-variable
    // bindings being built; meets the kid classes that differ; needsA the heads a's shape variables met (the static prune). Only the
    // top is matched by head: two shape nodes agree when their heads are equal and their kids agree by the kid rule; a class or a
    // shape variable at the top is a kid against the other (a class-shaped promise matches every need at its class; a bare variable
    // takes the class it meets). False when the tops' heads differ or a reference disagrees (D5). Classes compare and bind through
    // Find (a stored chain's base shape is a snapshot).
    private bool Agree(int a, int?[] bindA, int b, int?[] bindB, List<(int, int)> meets, Dictionary<int, Head> needsA)
    {
        if (a >= 0 || b >= 0 || this.shapes.IsVar(a) || this.shapes.IsVar(b))
        {
            return this.Kid(a, bindA, b, bindB, meets, needsA);
        }

        var ka = this.shapes.KidsOf(a);
        var kb = this.shapes.KidsOf(b);
        return this.shapes.HeadOf(a).Equals(this.shapes.HeadOf(b)) && ka.Length == kb.Length && this.Kids(ka, bindA, kb, bindB, meets, needsA);
    }

    // The kid rule: two classes agree when equal and meet otherwise; a shape variable takes a class it meets (a second class: a meet)
    // and records the head of a shape node it meets; anything else that is not a Type yet (a shape node against a class or a node,
    // two shape variables) is deferred: no constraint, decided once built — except a Contract reference (D5: a reference is not a
    // Type; SPEC 8.2.3 relates Types), which agrees only with the same reference head (a class holding it or a node of it), its
    // arguments then by this rule, and never with a Type or another reference.
    private bool Kid(int a, int?[] bindA, int b, int?[] bindB, List<(int, int)> meets, Dictionary<int, Head> needsA)
    {
        if (a >= 0 && b >= 0)
        {
            var (x, y) = (this.g.Find(a), this.g.Find(b));
            if (x == y)
            {
                return true;
            }

            if (!Equals(this.RefHead(x), this.RefHead(y)))
            {
                return false; // D5: a reference class and a Type class, or references of two declarations, are never one
            }

            meets.Add((x, y));
            return true;
        }

        if (a >= 0 || b >= 0)
        {
            var (cls, shape, bind) = a >= 0 ? (a, b, bindB) : (b, a, bindA);
            if (this.shapes.IsVar(shape))
            {
                this.Bind(shape, bind, cls, meets);
                return true;
            }

            var head = this.shapes.HeadOf(shape);
            var refHead = this.RefHead(this.g.Find(cls));
            if (head is not SymHead { Kind: SymKind.Ref })
            {
                return refHead is null; // a Type-headed node: never a reference class (D5); otherwise decided once built
            }

            if (!head.Equals(refHead))
            {
                return false;
            }

            var kids = this.shapes.KidsOf(shape);
            foreach (var node in this.g.NodesOf(cls))
            {
                if (this.g.HeadOf(node).Equals(head) && this.g.KidCount(node) == kids.Length)
                {
                    var nodeKids = new int[kids.Length];
                    for (var i = 0; i < kids.Length; i++)
                    {
                        nodeKids[i] = this.g.KidOf(node, i);
                    }

                    return a >= 0 ? this.Kids(nodeKids, bindA, kids, bindB, meets, needsA) : this.Kids(kids, bindA, nodeKids, bindB, meets, needsA);
                }
            }

            return false;
        }

        if (this.shapes.IsVar(a) || this.shapes.IsVar(b))
        {
            if (this.shapes.IsVar(a) && !this.shapes.IsVar(b))
            {
                needsA[this.shapes.VarIndex(a)] = this.shapes.HeadOf(b);
            }

            return true;
        }

        var (ha, hb) = (this.shapes.HeadOf(a), this.shapes.HeadOf(b));
        if (ha is not SymHead { Kind: SymKind.Ref } && hb is not SymHead { Kind: SymKind.Ref })
        {
            return true; // two Type-headed nodes: decided once built
        }

        var ra = this.shapes.KidsOf(a);
        var rb = this.shapes.KidsOf(b);
        return ha.Equals(hb) && ra.Length == rb.Length && this.Kids(ra, bindA, rb, bindB, meets, needsA);
    }

    private bool Kids(ReadOnlySpan<int> ka, int?[] bindA, ReadOnlySpan<int> kb, int?[] bindB, List<(int, int)> meets, Dictionary<int, Head> needsA)
    {
        for (var i = 0; i < ka.Length; i++)
        {
            if (!this.Kid(ka[i], bindA, kb[i], bindB, meets, needsA))
            {
                return false;
            }
        }

        return true;
    }

    // D5: the Contract reference a class holds (null for a class of Types). A reference is not a Type (SPEC 8.2.3 relates Types): no
    // identity row has a reference as a side, so a reference class holds references of one declaration only, merged by congruence.
    private Head? RefHead(int c)
    {
        foreach (var node in this.g.NodesOf(c))
        {
            return this.g.HeadOf(node) is SymHead { Kind: SymKind.Ref } head ? head : null;
        }

        return null;
    }

    private void Bind(int shape, int?[] bind, int cls, List<(int, int)> meets)
    {
        if (!this.shapes.IsVar(shape))
        {
            return; // a shape node against a class: decided once built
        }

        var n = this.shapes.VarIndex(shape);
        cls = this.g.Find(cls);
        if (bind[n] is int bound)
        {
            bound = this.g.Find(bound);
            if (bound != cls)
            {
                meets.Add((bound, cls));
            }
        }
        else
        {
            bind[n] = cls;
        }
    }

    // ---- Applying a round ----
    private void Apply(Effects fx)
    {
        var t0 = Stopwatch.GetTimestamp();
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
        foreach (var (found, m) in fx.Chains)
        {
            var c = this.g.Find(found.Class);
            if (!this.chains.TryGetValue(c, out var dict))
            {
                this.chains[c] = dict = new();
            }

            if (!dict.TryGetValue(found.Shape, out var p))
            {
                dict[found.Shape] = p = new Promise(c, found.Shape, found.Other, found.Beta);
            }

            if (p.AddChain(m, this))
            {
                woken.Add(c);
            }
        }

        // A new chain member changes what a class can match: rows that visited it are selected again.
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
            var key = new SubKey(s.T.Id, s.Stage, on, s.Sign, this.SubText(s.Beta, s.Ants));
            if (!this.subIndex.TryGetValue(key, out var sub))
            {
                var conditionIndex = s.T.Antecedent is null ? s.Stage : s.Stage - 1;
                sub = new Sub(this.subs.Count, s.T, s.Beta, s.Stage, on, s.Sign, s.Ants)
                {
                    Existential = conditionIndex + 1 >= s.T.Conditions.Count && Unbound(s.T.Conclusion, s.Beta).Count == 0,
                };
                this.subs.Add(sub);
                this.subIndex.Add(key, sub);
            }

            // A subscription is identified by what it subscribes to; a further requester joins its owners (share by meaning).
            foreach (var requester in s.Requesters)
            {
                if (sub.Owners.Add(requester))
                {
                    requester.Requested.Add(sub);
                }
            }
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

        this.Phases.Add(Phase.Apply, t0);
        this.AfterApply();
    }

    private void AfterApply()
    {
        var t0 = Stopwatch.GetTimestamp();
        var merged = this.g.Rebuild();
        this.Phases.Add(Phase.Rebuild, t0);
        t0 = Stopwatch.GetTimestamp();
        this.shapes.Forget();
        this.RekeyChains();
        this.Phases.Add(Phase.ChainKeys, t0);
        t0 = Stopwatch.GetTimestamp();
        if (merged.Count == 0)
        {
            return;
        }

        this.Phases.Merges++;

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
        this.Phases.Add(Phase.Rekey, t0);
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

        // Subscriptions and expansion records of rows that became one are one (otherwise which row survived, an accident of
        // order, would change the counts the limits read); their owners are mapped and united.
        var subs = new List<Sub>();
        this.subIndex = new();
        foreach (var s in this.subs)
        {
            var owners = s.Owners.Select(Owner).ToList();
            s.Owners.Clear();
            s.Owners.UnionWith(owners);
            var key = new SubKey(s.T.Id, s.Stage, s.On, s.Sign, this.SubText(s.Beta, s.Ants));
            if (this.subIndex.TryGetValue(key, out var existing))
            {
                existing.Fired.UnionWith(s.Fired);
                existing.Retired |= s.Retired;
                existing.Owners.UnionWith(s.Owners);
                continue;
            }

            this.subIndex.Add(key, s);
            subs.Add(s);
        }

        this.subs = subs;
        foreach (var row in this.demands.Cast<Row>().Concat(this.idents.Values).Concat(this.compounds.Values).Concat(this.needs.Values).Concat(this.meetings.Values))
        {
            row.Requested.Clear();
        }

        foreach (var s in this.subs)
        {
            foreach (var o in s.Owners)
            {
                o.Requested.Add(s);
            }
        }

        Row Owner(Row r) => r is Demand d && map.TryGetValue(d, out var dm) ? dm : r is ClassDemand c && moved.TryGetValue(c, out var cm) ? cm : r is NeedRow n && needMoved.TryGetValue(n, out var nm) ? nm : r is MeetRow m && meetMoved.TryGetValue(m, out var mm) ? mm : r;
        this.expansions = new(this.expansions.Select(e => (e.T, this.g.Find(e.Class), this.shapes.Canon(e.Bind, this.g), Owner(e.Owner), e.Sign)));

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

    // Stored chain promises are keyed by canonical (class, shape) after every round (a merge or an interned node can make two shapes
    // one): promises that became one are one (members united, a set modulo classes); a member's base identity is re-resolved the
    // same way. The one re-keying mechanism: ShapeTable.Canon.
    private void RekeyChains()
    {
        var chains = new Dictionary<int, Dictionary<int, Promise>>();
        foreach (var (c, dict) in this.chains)
        {
            var root = this.g.Find(c);
            if (!chains.TryGetValue(root, out var into))
            {
                chains[root] = into = new();
            }

            foreach (var p in dict.Values)
            {
                var shape = this.shapes.Canon(p.Shape, this.g);
                if (!into.TryGetValue(shape, out var target))
                {
                    into[shape] = target = new Promise(root, shape, p.Other, p.Beta);
                }

                foreach (var m in p.Chains)
                {
                    target.AddChain(m, this);
                }
            }
        }

        this.chains = chains;
    }

    private IntArrayKey SubText(int?[] beta, int[] ants)
    {
        var ids = new int[beta.Length + ants.Length];
        for (var i = 0; i < beta.Length; i++)
        {
            ids[i] = beta[i] is int b ? this.g.Find(b) : -1;
        }

        for (var i = 0; i < ants.Length; i++)
        {
            ids[beta.Length + i] = this.FactFind(ants[i]);
        }

        return new(ids);
    }

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
    {
        var ids = new int[args.Length];
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = args[i] >= 0 ? this.g.Find(args[i]) : -1;
        }

        return new(decl, subject >= 0 ? this.g.Find(subject) : -1, new(ids));
    }

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
        var seen = new HashSet<IntArrayKey>();
        foreach (var r in results)
        {
            var ids = new int[r.Length];
            for (var i = 0; i < ids.Length; i++)
            {
                ids[i] = r[i] ?? -1;
            }

            if (seen.Add(new(ids)))
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
        public Sub(int id, Template t, int?[] beta, int stage, Demand on, byte sign, int[] ants)
        {
            this.Id = id;
            this.T = t;
            this.Beta = beta;
            this.Stage = stage;
            this.On = on;
            this.Sign = sign;
            this.Ants = ants;
        }

        public int Id { get; }

        public Template T { get; }

        public int?[] Beta { get; }

        public int Stage { get; }

        public Demand On { get; set; }

        public byte Sign { get; }

        /// <summary>Gets the rows on whose behalf the subscription stands (it serves what they serve): identified by what it
        /// subscribes to, never by who asked.</summary>
        public HashSet<Row> Owners { get; } = new();

        public byte Tags { get; set; }

        /// <summary>Gets a value indicating whether the subscription is the template's last stage and its bindings fix the conclusion.</summary>
        public bool Existential { get; init; }

        public bool Retired { get; set; }

        public int[] Ants { get; }

        public HashSet<int> Fired { get; } = new();

        /// <summary>Gets or sets how much of its Contract's fact list the subscription has scanned (reset when classes merge).</summary>
        public int Scanned { get; set; }

        /// <summary>Gets or sets the most it ever scanned (facts below it are re-examinations after a merge; measurement only).</summary>
        public int Peak { get; set; }
    }

    /// <summary>A promise: a node of its class identified by its shape (rule 1), with the rows producing it directly (Rows) and the
    /// chains forwarding to it (Chains, a set modulo classes); Other and Beta are the first (other side, bindings) that produced it
    /// (display only), Key its index head: the shape's head, or Any for an existing class (rule 2: a class-shaped promise has no head
    /// and is found by every need at its class, whichever term produced it first).</summary>
    private sealed class Promise
    {
        public Promise(int c, int shape, Term other, int?[] beta)
        {
            this.Class = c;
            this.Shape = shape;
            this.Other = other;
            this.Beta = beta;
            this.Key = shape >= 0 ? HeadKey.Any : HeadKey.Of(other);
        }

        public int Class { get; }

        public int Shape { get; }

        public Term Other { get; }

        public int?[] Beta { get; }

        public HeadKey Key { get; }

        public List<Direct> Rows { get; } = new();

        public Dictionary<ChainKey, Chain> ChainSet { get; init; } = new();

        public IEnumerable<Chain> Chains => this.ChainSet.Values;

        public bool HasChain(Chain m, Solver s) => this.ChainSet.ContainsKey(s.KeyOf(m));

        /// <summary>Adds a member absent by key (its base is an identity: class and shape, resolved to the round's promise when forwarded).</summary>
        public bool AddChain(Chain m, Solver s) => this.ChainSet.TryAdd(s.KeyOf(m), m);
    }

    /// <summary>A direct member: the matched side, the bindings its match established, and the row variable of each shape variable.</summary>
    private sealed record Direct(EqSide E, int?[] Beta, int[] VarOf);

    /// <summary>A chain member: the side's sub-pattern that met the base promise, under the bindings the side's match established;
    /// forwards to the base's rows (Via names the base: its (class, shape) is the member's identity, modulo classes).</summary>
    private sealed record Chain(Term Pattern, Promise Via, int?[] ViaBeta);

    private readonly record struct ChainKey(int Pattern, int ViaClass, int ViaShape, IntArrayKey ViaBeta);

    /// <summary>A side matched against a class: its distinct bindings and its chain hits.</summary>
    private sealed record SideMatch(List<int?[]> Results, List<(Promise Via, Term Pattern, int?[] Beta)> Hits);

    private readonly record struct LookupKey(int Class, HeadKey Need, IntArrayKey K0, IntArrayKey K1);

    /// <summary>The promises of one class at one head with their class vectors, indexed per kid position (class → promises) with the
    /// wild promises per position.</summary>
    private sealed class PromiseIndex
    {
        public PromiseIndex(int arity)
        {
            this.Classed = new Dictionary<int, List<Promise>>[arity];
            this.Wild = new List<Promise>[arity];
            for (var i = 0; i < arity; i++)
            {
                this.Classed[i] = new();
                this.Wild[i] = new();
            }
        }

        public List<(Promise Promise, int[] Vector)> All { get; } = new();

        public Dictionary<int, List<Promise>>[] Classed { get; }

        public List<Promise>[] Wild { get; }
    }

    /// <summary>An array of ids compared by content (a canonical key); a null array (default) is a filter that admits any head.</summary>
    private readonly struct IntArrayKey(int[]? ids) : IEquatable<IntArrayKey>
    {
        private readonly int[]? ids = ids;

        public bool Equals(IntArrayKey other) => this.ids is null ? other.ids is null : other.ids is not null && this.ids.AsSpan().SequenceEqual(other.ids);

        public override bool Equals(object? obj) => obj is IntArrayKey other && this.Equals(other);

        public override int GetHashCode()
        {
            if (this.ids is null)
            {
                return -1;
            }

            var hash = this.ids.Length;
            foreach (var k in this.ids)
            {
                hash = HashCode.Combine(hash, k);
            }

            return hash;
        }
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

    private readonly record struct DemandKey(ContractDecl Decl, int Subject, IntArrayKey Args);

    private readonly record struct SubKey(int Template, int Stage, Demand On, byte Sign, IntArrayKey Beta);

    private readonly record struct FactKey(byte Kind, ContractDecl? Decl, int A, int B, string? Text);

    /// <summary>A subscription to create: an antecedent (subject and arguments as classes) or a condition (Cond's operands resolved
    /// when the round is applied).</summary>
    private sealed record SubRequest(ContractDecl Decl, int Subject, int[] Args, Formula? Cond, byte Tags, byte Sign, IEnumerable<Row> Requesters, Template T, int?[] Beta, int Stage, int[] Ants);

    private enum MatchMode : byte { None, Expand, Chain }

    private sealed record FireRequest(Template T, int?[] Beta, int[] Ants, byte Tags);

    private sealed class Effects
    {
        public List<SubRequest> Subs { get; } = new();

        public List<FireRequest> Fires { get; } = new();

        public HashSet<int> Idents { get; } = new();

        public List<(Promise Found, Chain Member)> Chains { get; } = new();

        public HashSet<(int Class, HeadKey Need, Row Owner)> Needs { get; } = new();

        public byte IdentTags { get; set; }

        public byte ChainTags { get; set; }

        /// <summary>Gets or sets the tags of the requesters whose needs became need rows this round (a need row is an effect).</summary>
        public byte NeedTags { get; set; }

        public void AddNeed(int c, HeadKey need, Row owner, byte tags)
        {
            this.Needs.Add((c, need, owner));
            this.NeedTags |= tags;
        }

        /// <summary>Gets or sets the meeting requesters or tags added this round.</summary>
        public int Meetings { get; set; }

        public byte MeetTags { get; set; }

        public int Count => this.Subs.Count + this.Fires.Count + this.Idents.Count + this.Chains.Count + this.Needs.Count + this.Meetings;

        public byte Tags => (byte)(this.Subs.Aggregate(0, (a, s) => a | s.Tags) | this.Fires.Aggregate(0, (a, s) => a | s.Tags) | this.IdentTags | this.ChainTags | this.NeedTags | this.MeetTags);
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

        /// <summary>Gets what a match does with a promise of the pattern's head: nothing, expand it, or record it (chains).</summary>
        public MatchMode Mode { get; init; }

        public List<(Promise Via, Term Pattern, int?[] Beta)>? Chain { get; init; }

        public Dictionary<LookupKey, List<Promise>>? Memo { get; init; }

        public MatchCtx Derive(MatchMode mode, Dictionary<LookupKey, List<Promise>>? memo = null) => new(this.solver, this.Owner, this.Tags, this.Fx) { Sign = this.Sign, Mode = mode, Chain = mode == MatchMode.Chain ? new() : null, Memo = memo };

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

public enum Phase : byte { DemandFirst, DemandWoken, ClassDemands, Scans, Needs, Eager, Meetings, Apply, Rebuild, ChainKeys, Rekey }

/// <summary>Time per phase of the last request and the re-examination counts the attribution reads (measurement only).</summary>
public sealed class PhaseTicks
{
    private readonly long[] ticks = new long[11];

    public long ScanRescanned { get; set; }

    public long ScanNew { get; set; }

    public long Meetings { get; set; }

    public long Merges { get; set; }

    public double Ms(Phase phase) => this.ticks[(int)phase] * 1000.0 / Stopwatch.Frequency;

    public double TotalMs => this.ticks.Sum() * 1000.0 / Stopwatch.Frequency;

    public void Add(Phase phase, long since) => this.ticks[(int)phase] += Stopwatch.GetTimestamp() - since;

    public void Reset()
    {
        Array.Clear(this.ticks);
        this.ScanRescanned = this.ScanNew = this.Meetings = this.Merges = 0;
    }

    public override string ToString()
        => string.Join(" ", Enum.GetValues<Phase>().Select(p => $"{p} {this.Ms(p):F1}")) + $" | scans rescanned {this.ScanRescanned} new {this.ScanNew}; meetings answered {this.Meetings}; rounds with merges {this.Merges}";
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

