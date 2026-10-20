// Demand-directed original-head selection. Instance patterns bind only original
// template slots; answers come from actual root/rule instances, not a Type domain.
// Supports and required logical edges have finite addresses, never proof trees.
namespace PremiseSupply;

internal sealed partial class Demand
{
    private readonly Budget budget;
    private readonly Arena arena;
    private readonly DemandIdentity identity;
    private readonly DemandMatch match;
    private readonly Facts facts = new();
    private readonly Dictionary<string, Expr> demands = new(StringComparer.Ordinal);
    private readonly HashSet<Expr> judgments = [];
    private readonly Dictionary<string, (Expr Head, Expr Body, Source Source)> applications = new(StringComparer.Ordinal);
    private Inputs inputs = null!;
    private Query query = null!;
    private Expr target = null!;
    private bool changed;
    private bool equalitySearch;
    private bool environmentError;

    private Demand(Budget budget)
    {
        this.budget = budget;
        arena = new(budget);
        identity = new(budget);
        match = new(arena, budget, identity);
    }

    internal static Answer Run(Input input, Query query, Budget? budget = null)
    {
        var request = new Demand(budget ?? new());
        try { return request.Evaluate(input, query); }
        catch (Exhausted e) { return request.Result(Completion.Failed, e.Failure); }
    }

    private Answer Evaluate(Input input, Query requested)
    {
        query = requested;
        budget.Charge(Counter.Preparation, "begin stable synthetic snapshot " + input.Scope);
        inputs = new(input, arena);
        target = arena.Parse(query.Proposition);
        if (query.Kind == QueryKind.Proof && !target.Ground) throw new InvalidOperationException("A proof query must be ground");
        environmentError = Validate(input.Environment, "environment:" + input.Scope);
        foreach (var root in inputs.Roots.Where(x => x.Source.State != SourceState.Unavailable))
            foreach (var (_, head) in arena.Heads(root.Proposition)) Discover(head);
        foreach (var leaf in inputs.Leaves)
            if (leaf.Purpose == query.Purpose && leaf.Value is Proof.Proven or Proof.Refuted) Discover(leaf.Proposition);
        equalitySearch = inputs.Roots.Any(x => HasEquality(x.Proposition)) || inputs.Rules.Any(x => HasEquality(x.Head));
        Open(target);
        if (query.Kind == QueryKind.Proof) Require(target);
        if (equalitySearch) Open(arena.Parse("eq($left,$right)"));
        do
        {
            changed = false;
            foreach (var pattern in demands.Values.ToArray()) Visit(pattern);
            foreach (var goal in judgments.ToArray()) Judge(goal);
            foreach (var application in applications.ToArray()) Apply(application.Key, application.Value);
            if (equalitySearch) CloseIdentity();
        }
        while (changed);
        return Result(facts.Pending.Count == 0 ? Completion.Complete : Completion.Pending, null);
    }

    private static bool HasEquality(Expr value)
        => value.Head == "eq" || value.Head == "and" && value.Args.Any(HasEquality);

    private void Open(Expr pattern)
    {
        pattern = arena.CanonicalPattern(pattern);
        if (demands.ContainsKey(pattern.Key)) return;
        budget.Charge(Counter.Demands, "open instance/proposition " + pattern.Key);
        demands.Add(pattern.Key, pattern);
        changed = true;
    }

    private void Require(Expr proposition)
    {
        if (!proposition.Ground) throw new InvalidOperationException("Ungrounded judgment");
        if (judgments.Contains(proposition)) return;
        budget.Charge(Counter.Demands, "require judgment " + proposition.Key);
        judgments.Add(proposition);
        var environment = "environment:" + inputs.Original.Scope;
        var dependency = "environment:" + proposition.Key;
        budget.Charge(Counter.Supports, "required environment " + proposition.Key);
        facts.Required.Add(dependency, new(dependency, proposition.Key, "premise-environment", "", [environment]));
        changed = true;
        Open(proposition);
        Open(arena.Not(proposition));
        if (proposition.Head is "and" or "or" or "not") foreach (var child in proposition.Args) Require(child);
    }

    private void Visit(Expr pattern)
    {
        foreach (var root in inputs.Roots)
        foreach (var (path, head) in arena.Heads(root.Proposition))
        {
            if (match.Unify(pattern, head) is null) continue;
            if (Read(root.Source)) Support("root:" + root.Id + path, head, "root", root.Source.Id, []);
        }
        foreach (var rule in inputs.Rules)
        foreach (var (path, rawHead) in arena.Heads(rule.Head))
        {
            budget.Charge(Counter.Work, "exhaustive original head scan");
            var rename = rule.Antecedent.Slots.ToDictionary(x => x, x => arena.Make("$rule_" + rule.Id + "_" + x[1..]), StringComparer.Ordinal);
            var head = arena.Substitute(rawHead, rename);
            var body = arena.Substitute(rule.Antecedent, rename);
            var binding = match.Unify(head, pattern);
            if (binding is not null) Plan(rule, path, arena.Substitute(head, binding), arena.Substitute(body, binding), pattern);
            // A possible equality prevents a structural mismatch from closing the
            // instance stream. Enumerate actual antecedents; never invert equality.
            if (equalitySearch && rawHead.Head == pattern.Head && !rawHead.Ground)
                Plan(rule, path, head, body, pattern);
        }
        foreach (var candidate in facts.Discovered.ToArray())
            if (candidate.Ground && match.Unify(pattern, candidate) is not null) Require(candidate);
    }

    private void Plan(ParsedRule rule, string path, Expr head, Expr body, Expr requested)
    {
        if (rule.Source.State == SourceState.Unavailable) { Read(rule.Source); return; }
        Open(body);
        if (body.Ground && match.Unify(head, requested) is not null) Form(rule, path, head, body);
        foreach (var actual in facts.Discovered.ToArray())
        {
            if (actual.Head != body.Head || !actual.Ground) continue;
            if (match.Unify(body, actual) is null) continue;
            // Read complete bindings from the original antecedent instance. A
            // head-constrained equality must not replace its actual hidden slot.
            var binding = match.Unify(rule.Antecedent, actual);
            if (binding is null) throw new InvalidOperationException("Selected instance lost its original template");
            var originalHead = arena.Heads(rule.Head).Single(x => x.Path == path).Head;
            var result = arena.Substitute(originalHead, binding);
            if (!result.Ground) throw new InvalidOperationException("Actual antecedent did not bind all declared slots");
            if (match.Unify(result, requested) is not null) Form(rule, path, result, actual);
        }
    }

    private void Form(ParsedRule rule, string path, Expr head, Expr body)
    {
        if (!head.Ground || !body.Ground) return;
        Read(rule.Source);
        var key = "rule:" + rule.Id + path + "@" + body.Key + "=>" + head.Key;
        if (applications.ContainsKey(key)) return;
        budget.Charge(Counter.Applications, "form " + key);
        applications.Add(key, (head, body, rule.Source));
        changed = true;
        Require(body);
        Require(head);
        if (rule.Source.State == SourceState.Available)
        {
            budget.Charge(Counter.Supports, "required antecedent " + key);
            facts.Required.Add(key, new(key, head.Key, "clause-antecedent", rule.Source.Id, [body.Key]));
        }
    }

    private void Apply(string key, (Expr Head, Expr Body, Source Source) application)
    {
        budget.Charge(Counter.Work, "wake clause dependency");
        if (facts.Discovered.Contains(application.Body)) Discover(application.Head);
        if (application.Source.State != SourceState.Available) return;
        if (facts.Positive.Any(x => identity.Equal(x, application.Body)))
        {
            Support(key, application.Head, "clause", application.Source.Id, [application.Body.Key]);
        }
        if (facts.Error.Contains(application.Body)) Error(application.Head);
    }

    private void Discover(Expr proposition)
    {
        if (facts.Discovered.Add(proposition)) changed = true;
        if (proposition.Head == "not" && facts.Discovered.Add(proposition.Args[0])) changed = true;
    }

    private void Support(string key, Expr output, string operation, string source, string[] dependencies)
    {
        if (!facts.Supports.ContainsKey(key))
        {
            budget.Charge(Counter.Supports, "support " + key);
            facts.Supports.Add(key, new(key, output.Key, operation, source, dependencies));
            changed = true;
        }
        if (facts.Positive.Add(output)) changed = true;
        Discover(output);
    }
}
