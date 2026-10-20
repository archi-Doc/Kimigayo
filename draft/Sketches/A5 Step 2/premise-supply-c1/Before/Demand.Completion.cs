// Completion roles from prototype-boundary and reviewed support-completion-v2.
// No available application's required Error can be suppressed by another root.
namespace PremiseSupply;

internal sealed partial class Demand
{
    private bool Read(Source source)
    {
        budget.Charge(Counter.Work, "read source " + source.Id);
        facts.Sources.Add(source.Id + ":" + source.State);
        switch (source.State)
        {
            case SourceState.Available: return true;
            case SourceState.Unavailable: return false;
            case SourceState.Pending: facts.Pending.Add("source:" + source.Id); return false;
            case SourceState.Resource: throw new Exhausted(new("Resource", "source:" + source.Id, Counter.Work, 0));
            case SourceState.Unsupported: throw new Exhausted(new("Unsupported", "source:" + source.Id, Counter.Work, 0));
            default: throw new InvalidOperationException("Unmapped source state");
        }
    }

    private void Validate(Validation state, Expr goal, string owner)
    {
        budget.Charge(Counter.Work, "validate " + owner);
        switch (state)
        {
            case Validation.Valid: return;
            case Validation.Invalid: Error(goal); return;
            case Validation.Pending: facts.Pending.Add(owner); return;
            case Validation.Resource: throw new Exhausted(new("Resource", owner, Counter.Work, 0));
            case Validation.Unsupported: throw new Exhausted(new("Unsupported", owner, Counter.Work, 0));
            default: throw new InvalidOperationException("Unmapped validation state");
        }
    }

    private void Judge(Expr goal)
    {
        Validate(inputs.Original.Environment, goal, "environment:" + inputs.Original.Scope);
        if (inputs.Operands.TryGetValue(goal, out var validation)) Validate(validation, goal, "operand:" + goal.Key);
        foreach (var leaf in inputs.Leaves)
        {
            if (leaf.Purpose != query.Purpose || !identity.Equal(leaf.Proposition, goal)) continue;
            Validate(leaf.State, goal, "owner:" + leaf.Id);
            if (leaf.State != Validation.Valid) continue;
            if (leaf.Value == Proof.Error) Error(goal);
            if (leaf.Value is Proof.Proven or Proof.Refuted)
                Support("owner:" + leaf.Id, leaf.Value == Proof.Proven ? goal : arena.Not(goal), "owner", "synthetic", []);
        }
        if (goal.Head == "error") Error(goal);
        if (goal.Head == "unresolved") facts.Pending.Add("operand:" + goal.Key);
        if (goal.Head == "eq" && identity.Equal(goal.Args[0], goal.Args[1]))
            Support("identity:" + goal.Key, goal, "identity-owner", "synthetic", []);
        if (goal.Head is "and" or "or" or "not") Compound(goal);
        if (facts.Error.Contains(arena.Not(goal))) Error(goal);
        if (Has(goal) && Has(arena.Not(goal))) Error(goal);
    }

    private bool Has(Expr expression) => facts.Positive.Any(x => identity.Equal(x, expression));

    private void Compound(Expr goal)
    {
        var key = "logical:" + goal.Key;
        if (!facts.Required.ContainsKey(key))
        {
            budget.Charge(Counter.Supports, "required logical operands");
            facts.Required.Add(key, new(key, goal.Key, "logical-operands", "", goal.Args.Select(x => x.Key).ToArray()));
        }
        if (goal.Args.Any(facts.Error.Contains)) Error(goal);
        var a = goal.Args[0];
        var positive = Has(a); var negative = Has(arena.Not(a));
        if (goal.Head == "not") (positive, negative) = (negative, positive);
        else
        {
            var b = goal.Args[1];
            positive = goal.Head == "and" ? positive && Has(b) : positive || Has(b);
            negative = goal.Head == "and" ? negative || Has(arena.Not(b)) : negative && Has(arena.Not(b));
        }
        if (positive) Support(key + ":positive", goal, "logical", "", goal.Args.Select(x => x.Key).ToArray());
        if (negative) Support(key + ":negative", arena.Not(goal), "logical", "", goal.Args.Select(x => x.Key).ToArray());
    }

    private void Error(Expr expression)
    {
        if (facts.Error.Add(expression)) changed = true;
    }

    private void CloseIdentity()
    {
        foreach (var equation in facts.Positive.Where(x => x.Head == "eq").ToArray())
        {
            Require(equation);
            if (identity.Join(equation.Args[0], equation.Args[1])) changed = true;
        }
        if (identity.Close(arena.All)) changed = true;
        if (facts.Error.Any(x => x.Head == "eq")) foreach (var judgment in judgments.ToArray()) Error(judgment);
    }

    private Answer Result(Completion completion, Failure? failure)
    {
        // A failed computation publishes its stored trace only. Do not continue
        // semantic matching (or allocate new terms) after a budget has failed.
        var positive = target is not null && (completion == Completion.Failed
            ? facts.Positive.Contains(target) : facts.Positive.Any(x => identity.Equal(x, target)));
        var negative = target is not null && (completion == Completion.Failed
            ? facts.Positive.Any(x => x.Head == "not" && ReferenceEquals(x.Args[0], target))
            : facts.Positive.Any(x => identity.Equal(x, arena.Not(target))));
        var errors = facts.Error.Select(x => x.Key).Order(StringComparer.Ordinal).ToArray();
        Proof? value = completion == Completion.Complete && query.Kind == QueryKind.Proof
            ? facts.Error.Contains(target) || positive && negative ? Proof.Error : positive ? Proof.Proven : negative ? Proof.Refuted : Proof.Unknown
            : null;
        var candidates = target is null || completion == Completion.Failed ? [] : facts.Discovered.Where(x => x.Ground && match.Unify(target, x) is not null)
            .Select(x => x.Key).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new(query.Id, completion, value, positive, negative, errors,
            facts.Pending.Order(StringComparer.Ordinal).ToArray(), failure,
            completion == Completion.Complete ? candidates : [], candidates,
            facts.Supports.Values.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            facts.Required.Values.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            facts.Sources.Order(StringComparer.Ordinal).ToArray(), budget.Snapshot(), completion == Completion.Complete);
    }
}
