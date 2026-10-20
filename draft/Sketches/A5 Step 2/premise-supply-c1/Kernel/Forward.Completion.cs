namespace PremiseSupply;

internal sealed partial class Forward
{
    private void ReadSource(Source source, Expr goal)
    {
        budget.Charge(Counter.Work, "reference source " + source.Id);
        var state = Need(goal);
        sourceReads.Add(source.Id + ":" + source.State);
        state.Sources.Add(source.Id + ":" + source.State);
        switch (source.State)
        {
            case SourceState.Available:
            case SourceState.Unavailable: break;
            case SourceState.Pending: changed |= state.Pending.Add("source:" + source.Id); break;
            case SourceState.Resource:
            case SourceState.Unsupported:
                if (state.Failure is null)
                {
                    state.Failure = new(source.State.ToString(), "source:" + source.Id, Counter.Work, 0);
                    changed = true;
                }
                break;
            default: throw new InvalidOperationException("Unmapped reference source");
        }
    }

    private void ValidationResult(Validation validation, State state, string location)
    {
        budget.Charge(Counter.Work, "reference owner state");
        if (validation == Validation.Invalid && !state.Error) { state.Error = true; changed = true; }
        if (validation == Validation.Pending) changed |= state.Pending.Add(location);
        if (validation is Validation.Resource or Validation.Unsupported && state.Failure is null)
        {
            state.Failure = new(validation.ToString(), location, Counter.Work, 0);
            changed = true;
        }
    }

    private void EvaluateJudgment(Expr goal)
    {
        var state = Need(goal);
        Inherit(state, entry);
        if (inputs.Operands.TryGetValue(goal, out var own)) ValidationResult(own, state, "operand:" + goal.Key);
        foreach (var leaf in inputs.Leaves)
        {
            if (leaf.Purpose != query.Purpose || !identity.Equal(leaf.Proposition, goal)) continue;
            ValidationResult(leaf.State, state, "owner:" + leaf.Id);
            if (leaf.State != Validation.Valid) continue;
            if (leaf.Value == Proof.Error && !state.Error) { state.Error = true; changed = true; }
            if (leaf.Value is Proof.Proven or Proof.Refuted)
                AddSupport("owner:" + leaf.Id, leaf.Value == Proof.Proven ? goal : arena.Not(goal), "owner", "synthetic", []);
        }
        if (goal.Head == "error" && !state.Error) { state.Error = true; changed = true; }
        if (goal.Head == "unresolved") changed |= state.Pending.Add("operand:" + goal.Key);
        if (goal.Head == "eq" && identity.Equal(goal.Args[0], goal.Args[1]))
            AddSupport("identity:" + goal.Key, goal, "identity-owner", "synthetic", []);
        if (goal.Head is "and" or "or" or "not") Logical(goal);
        foreach (var equivalent in states.ToArray())
            if (!ReferenceEquals(equivalent.Key, goal) && identity.Equal(equivalent.Key, goal))
            {
                var key = "equivalent:" + goal.Key + "<-" + equivalent.Key.Key;
                if (!required.ContainsKey(key))
                {
                    budget.Charge(Counter.Supports, "reference equivalent judgment dependency");
                    required.Add(key, new(key, goal.Key, "equivalent-judgment", "", [equivalent.Key.Key]));
                    changed = true;
                }
                Inherit(state, equivalent.Value);
            }
        var opposite = arena.Not(goal);
        if (states.TryGetValue(opposite, out var negativeWork)) Inherit(state, negativeWork);
        if (Positive(goal) && Positive(opposite) && !state.Error) { state.Error = true; changed = true; }
    }

    private void Logical(Expr goal)
    {
        var key = "logical:" + goal.Key;
        if (!required.ContainsKey(key))
        {
            budget.Charge(Counter.Supports, "reference required logical children");
            required.Add(key, new(key, goal.Key, "logical-operands", "", goal.Args.Select(x => x.Key).ToArray()));
        }
        var state = Need(goal);
        foreach (var child in goal.Args) Inherit(state, Need(child));
        bool positive;
        bool negative;
        if (goal.Head == "not")
        {
            positive = Positive(arena.Not(goal.Args[0]));
            negative = Positive(goal.Args[0]);
        }
        else if (goal.Head == "and")
        {
            positive = goal.Args.All(Positive);
            negative = goal.Args.Any(x => Positive(arena.Not(x)));
        }
        else
        {
            positive = goal.Args.Any(Positive);
            negative = goal.Args.All(x => Positive(arena.Not(x)));
        }
        var children = goal.Args.Select(x => x.Key).ToArray();
        if (positive) AddSupport(key + ":positive", goal, "logical", "", children);
        if (negative) AddSupport(key + ":negative", arena.Not(goal), "logical", "", children);
    }

    private void CloseIdentity()
    {
        changed |= identity.Close(states.Where(x => x.Value.Positive && x.Key.Head == "eq").Select(x => x.Key).ToArray());
        var badIdentity = states.Any(x => x.Key.Head == "eq" && x.Value.Error);
        if (badIdentity) foreach (var state in states.Values) if (!state.Error) { state.Error = true; changed = true; }
        foreach (var identityWork in states.Where(x => x.Key.Head == "eq").Select(x => x.Value).ToArray())
            foreach (var state in states.Values.ToArray()) Inherit(state, identityWork);
    }

    private Answer Publish(Failure? budgetFailure)
    {
        var selected = new List<Expr>();
        if (budgetFailure is null)
        {
            foreach (var candidate in instances)
                if (identity.Bind(target, candidate) is not null) selected.Add(candidate);
        }
        var state = target is not null && states.TryGetValue(target, out var actual) ? actual : new State();
        Inherit(state, entry);
        if (query.Kind == QueryKind.Candidates)
            foreach (var item in selected) Inherit(state, states[item]);
        var failure = budgetFailure ?? state.Failure;
        var completion = failure is not null ? Completion.Failed : state.Pending.Count != 0 ? Completion.Pending : Completion.Complete;
        var positive = target is not null && (failure is not null ? state.Positive : Positive(target));
        var negative = target is not null && failure is null && Positive(arena.Not(target));
        Proof? value = completion != Completion.Complete ? null : state.Error || positive && negative ? Proof.Error
            : query.Kind == QueryKind.Proof ? positive ? Proof.Proven : negative ? Proof.Refuted : Proof.Unknown : null;
        var candidates = selected.Select(x => x.Key).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new(query.Id, completion, value, positive, negative,
            states.Where(x => x.Value.Error).Select(x => x.Key.Key)
                .Concat(entry.Error ? ["environment:" + inputs.Original.Scope] : []).Order(StringComparer.Ordinal).ToArray(),
            state.Pending.Order(StringComparer.Ordinal).ToArray(), failure,
            completion == Completion.Complete && value != Proof.Error ? candidates : [], candidates,
            supports.Values.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            required.Values.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            sourceReads.Order(StringComparer.Ordinal).ToArray(), budget.Snapshot(), completion == Completion.Complete);
    }
}
