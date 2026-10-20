// Independent forward reference. It expands the entire actual-instance relation,
// without using demand answers as a universe. A separate ground judgment census
// closes required errors/pending dependencies, including unseeded clause bodies.
// No closed result is returned if either relation keeps growing under its budget.
namespace PremiseSupply;

internal sealed partial class Forward
{
    private readonly Budget budget;
    private readonly Arena arena;
    private readonly ForwardIdentity identity;
    private readonly HashSet<Expr> instances = [];
    private readonly Dictionary<Expr, State> states = [];
    private readonly Dictionary<string, Application> applications = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Edge> supports = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Edge> required = new(StringComparer.Ordinal);
    private readonly HashSet<string> sourceReads = new(StringComparer.Ordinal);
    private Inputs inputs = null!;
    private Query query = null!;
    private Expr target = null!;
    private bool changed;
    private bool equalitySearch;

    private sealed class State
    {
        internal bool Positive;
        internal bool Error;
        internal HashSet<string> Pending { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> Sources { get; } = new(StringComparer.Ordinal);
        internal Failure? Failure;
    }
    private sealed record Application(string Key, Expr Head, Expr Body, Source Source);

    private Forward(Budget budget)
    {
        this.budget = budget;
        arena = new(budget);
        identity = new(arena, budget);
    }

    internal static Answer Run(Input input, Query query, Budget? budget = null)
    {
        var reference = new Forward(budget ?? new());
        try { return reference.Evaluate(input, query); }
        catch (Exhausted e) { return reference.Publish(e.Failure); }
    }

    private Answer Evaluate(Input input, Query requested)
    {
        query = requested;
        budget.Charge(Counter.Preparation, "reference stable snapshot " + input.Scope);
        inputs = new(input, arena);
        target = arena.Parse(query.Proposition);
        equalitySearch = inputs.Roots.Any(x => ContainsEquation(x.Proposition)) || inputs.Rules.Any(x => ContainsEquation(x.Head));
        if (target.Ground) Need(target);
        foreach (var root in inputs.Roots)
        foreach (var (path, head) in OriginalHeads(root.Proposition))
        {
            Need(head);
            ReadSource(root.Source, head);
            if (root.Source.State != SourceState.Unavailable) Candidate(head);
            if (root.Source.State == SourceState.Available) AddSupport("root:" + root.Id + path, head, "root", root.Source.Id, []);
        }
        foreach (var leaf in inputs.Leaves.Where(x => x.Purpose == query.Purpose)) Need(leaf.Proposition);
        do
        {
            changed = false;
            ExpandActualInstances();
            CensusRequiredJudgments();
            foreach (var expression in states.Keys.ToArray()) EvaluateJudgment(expression);
            foreach (var application in applications.Values.ToArray()) Propagate(application);
            if (equalitySearch) CloseIdentity();
        }
        while (changed);
        return Publish(null);
    }

    private static bool ContainsEquation(Expr expression)
        => expression.Head == "eq" || expression.Head == "and" && expression.Args.Any(ContainsEquation);

    private IEnumerable<(string Path, Expr Head)> OriginalHeads(Expr original)
    {
        // Independent positive-conjunction enumeration; not demand's selector.
        var pending = new List<(string Path, Expr Head)> { ("", original) };
        for (var next = 0; next < pending.Count; next++)
        {
            budget.Charge(Counter.Work, "reference exact/conjunction head");
            var current = pending[next];
            yield return current;
            if (current.Head.Head != "and") continue;
            pending.Add((current.Path + ".L", current.Head.Args[0]));
            pending.Add((current.Path + ".R", current.Head.Args[1]));
        }
    }

    private State Need(Expr expression)
    {
        if (states.TryGetValue(expression, out var old)) return old;
        if (!expression.Ground) throw new InvalidOperationException("Ground reference judgment required");
        budget.Charge(Counter.Demands, "reference judgment address " + expression.Key);
        var state = new State();
        states.Add(expression, state);
        changed = true;
        if (expression.Head is "and" or "or" or "not") foreach (var child in expression.Args) Need(child);
        return state;
    }

    private void Candidate(Expr expression)
    {
        if (instances.Add(expression)) changed = true;
        if (expression.Head == "not" && instances.Add(expression.Args[0])) changed = true;
    }

    private void ExpandActualInstances()
    {
        foreach (var rule in inputs.Rules)
        foreach (var actual in instances.ToArray())
        {
            if (rule.Source.State == SourceState.Unavailable) continue;
            var bindings = identity.Bind(rule.Antecedent, actual);
            if (bindings is null) continue;
            foreach (var (path, original) in OriginalHeads(rule.Head))
            {
                var head = arena.Substitute(original, bindings);
                if (!head.Ground) throw new InvalidOperationException("Actual instance did not bind original head");
                AddApplication(rule.Id + path, head, actual, rule.Source);
                Candidate(head);
            }
        }
    }

    private void CensusRequiredJudgments()
    {
        // This does not derive support or seed the instance relation. It records
        // every fully determined required antecedent of the known ground queries.
        // Hidden slots are supplied only by ExpandActualInstances above.
        foreach (var goal in states.Keys.ToArray())
        foreach (var polarity in new[] { goal, arena.Not(goal) })
        foreach (var rule in inputs.Rules)
        foreach (var (path, head) in OriginalHeads(rule.Head))
        {
            budget.Charge(Counter.Work, "reference required-dependency census");
            var binding = identity.Bind(head, polarity);
            if (binding is null) continue;
            var body = arena.Substitute(rule.Antecedent, binding);
            if (!body.Ground) continue;
            AddApplication(rule.Id + path, polarity, body, rule.Source);
        }
    }

    private void AddApplication(string id, Expr head, Expr body, Source source)
    {
        Need(head);
        ReadSource(source, head);
        if (source.State == SourceState.Unavailable) return;
        var key = "rule:" + id + "@" + body.Key + "=>" + head.Key;
        if (applications.ContainsKey(key)) return;
        budget.Charge(Counter.Applications, "reference application " + key);
        applications.Add(key, new(key, head, body, source));
        Need(body);
        changed = true;
        if (source.State == SourceState.Available)
        {
            budget.Charge(Counter.Supports, "reference required antecedent");
            required.Add(key, new(key, head.Key, "clause-antecedent", source.Id, [body.Key]));
        }
    }

    private void AddSupport(string key, Expr output, string operation, string source, string[] dependencies)
    {
        var state = Need(output);
        if (!supports.ContainsKey(key))
        {
            budget.Charge(Counter.Supports, "reference support " + key);
            supports.Add(key, new(key, output.Key, operation, source, dependencies));
            changed = true;
        }
        if (!state.Positive) { state.Positive = true; changed = true; }
        Candidate(output);
    }

    private bool Positive(Expr proposition)
        => states.Any(x => x.Value.Positive && identity.Equal(x.Key, proposition));

    private void Propagate(Application application)
    {
        budget.Charge(Counter.Work, "reference dependency propagation");
        if (application.Source.State != SourceState.Available) return;
        var output = Need(application.Head);
        var body = Need(application.Body);
        if (Positive(application.Body)) AddSupport(application.Key, application.Head, "clause", application.Source.Id, [application.Body.Key]);
        Inherit(output, body);
    }

    private void Inherit(State output, State dependency)
    {
        if (dependency.Error && !output.Error) { output.Error = true; changed = true; }
        foreach (var pending in dependency.Pending) changed |= output.Pending.Add(pending);
        foreach (var source in dependency.Sources) changed |= output.Sources.Add(source);
        if (output.Failure is null && dependency.Failure is not null) { output.Failure = dependency.Failure; changed = true; }
    }
}
