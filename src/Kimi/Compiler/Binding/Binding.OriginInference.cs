// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(Koto Use, Koto Binder), OriginInference> originInferences = new();

    // SPEC 15.3.6: `value` is a covariant input bound of the callee's Origin `parameter` (CollectOriginInference's covariant row), in
    // the cases `condition` names (OriginInference.Bounds).
    private static void AddInputBound(OriginInference inference, BoundOrigin parameter, BoundOrigin value, ulong condition = 0)
    {
        inference.Discover(parameter, 1);
        inference.Add(value, parameter, false, true, condition);
    }

    // The failure of a solve's bound under its source (OriginInference.Bounds): a declared clause, else a member's position, else a result
    // premise.
    private static ComparisonFailure BoundFailure(
        in CallableInstance comparison, byte member, Koto? clause, BoundOrigin longer, BoundOrigin shorter, bool equality)
    {
        var kind = ComparisonFailureKind.ResultPremise;
        if (clause is not null)
        {
            kind = ComparisonFailureKind.Clause;
        }
        else if (member != 0)
        {
            kind = ComparisonFailureKind.Origin;
        }

        return new(kind, longer, shorter, equality, comparison) { Clause = clause, Path = member, Depth = (byte)(member == 0 ? 0 : 1) };
    }

    private OriginInference BeginOriginInference(Koto use, Koto binder)
    {
        if (!this.originInferences.TryGetValue((use, binder), out var inference))
        {
            this.originInferences.Add((use, binder), inference = new(use, binder));
        }

        inference.Bounds.Clear();
        inference.Variables.Clear();
        inference.WellFormed.Clear();
        inference.PremiseSolved.Clear();
        inference.Quantifiers.Clear();
        inference.Comparison = default;
        inference.Conditional = false;
        inference.Source = default;
        return inference;
    }

    private void CollectOriginInference(BoundType pattern, BoundType actual, OriginInference inference, int polarity = 1, bool result = false, Koto? known = null)
    {
        if (!pattern.CarriesOrigin || !actual.CarriesOrigin)
        {
            return;
        }

        if (pattern.Origin is { } p && actual.Origin is { } a && !InactiveOuterOrigin(pattern, this, null, inference.Comparison))
        {
            // SPEC 15.6.5: a pair layer's slot exists only in its binder's borrow cases, as the walk's (SlotCondition).
            Add(p, a, polarity, SlotCondition(actual, this, inference.Use, inference.Comparison));
        }

        for (var i = 0; i < Math.Min(pattern.OriginArguments.Count, actual.OriginArguments.Count); i++)
        {
            var variance = OriginArgumentVariance(pattern, i, this);
            Add(pattern.OriginArguments[i], actual.OriginArguments[i], Compose(polarity, variance), 0);
        }

        // SPEC 15.3.4, 10.7: inside a Function Type pair, each side's own per-call Origins (an Input Origin of its BoundType.Binder) are
        // that Type's quantification, which the Type relation corresponds position by position; they are never bounds of this call's
        // Origins, at any slot or depth.
        var enclosing = inference.Quantifiers.Count;
        if (pattern.Kind == BoundTypeKind.Function && actual.Kind == BoundTypeKind.Function)
        {
            inference.Enter(pattern.Binder);
            inference.Enter(actual.Binder);
        }

        for (var i = 0; i < Math.Min(pattern.Components.Count, actual.Components.Count); i++)
        {
            var sign = IsInvariantLayer(pattern, this, null, inference.Comparison) ? 0 :
                pattern.Kind == BoundTypeKind.Function && i == 0 ? -polarity : polarity;
            if (pattern.Kind == BoundTypeKind.Constructed && pattern.Symbol?.Schema is { } schema)
            {
                sign = Compose(polarity, schema.GenericSlots[i].OriginVariance);
            }

            this.CollectOriginInference(pattern.Components[i], actual.Components[i], inference, sign, result, known);
        }

        inference.Quantifiers.RemoveRange(enclosing, inference.Quantifiers.Count - enclosing);

        static int Compose(int sign, OriginVariance variance) => variance == OriginVariance.Covariant ? sign : variance == OriginVariance.Contravariant ? -sign : 0;

        void Add(BoundOrigin parameter, BoundOrigin value, int sign, ulong condition)
        {
            if (known is not null && QuantifiedBy(value, known))
            {
                return; // SPEC 10.8: a known call signature's own Origin is no bound for the callee's Origins.
            }

            if (inference.Quantifies(parameter) || inference.Quantifies(value))
            {
                return;
            }

            if (sign > 0 && !result)
            {
                AddInputBound(inference, parameter, value, condition); // The covariant input row, one copy (a borrowed temporary's too).
                return;
            }

            inference.Discover(parameter, sign == 0 ? 3 : sign > 0 ? 1 : 2);
            if ((sign >= 0) != result || sign == 0)
            {
                inference.Add(value, parameter, false, true, condition);
            }

            if ((sign <= 0) != result || sign == 0)
            {
                inference.Add(parameter, value, true, false, condition);
            }
        }
    }

    // With `select`, candidate applicability: the bounds are not judged, since the Origin relations of a fit never change a selection
    // (SPEC 15.6.1, 10.4); the selected call judges them after selection. A call's inputs' well-formedness (SPEC 15.6.4 step 3) also
    // shortens the solution (OriginInference.WellFormed), and the selected call judges it at each input (RequireInputPremises), so it
    // is never verified here.
    private bool SolveOriginInference(OriginInference inference, BoundOrigin[] origins, BoundOrigin[] inputs, BoundType? declaringType = null, bool select = false)
    {
        var binder = inference.Binder;
        var use = inference.Use;
        inference.Failure = default;
        if (this.originDeclarations.TryGetValue(binder, out var declaration))
        {
            foreach (var relation in declaration.Relations)
            {
                inference.Source = (0, relation.Syntax, relation.Equality);
                var longer = Project(relation.Longer);
                var shorter = Project(relation.Shorter);
                inference.Discover(longer, 0);
                inference.Discover(shorter, 0);
                inference.Add(longer, shorter);
                if (relation.Equality)
                {
                    inference.Add(shorter, longer);
                }
            }

            inference.Source = default;
        }

        // Preliminary Type inference may have proposed values. Recompute every mentioned
        // Origin from its bounds, keeping contextual inputs that have no Origin equation.
        foreach (var variable in inference.Variables)
        {
            Set(variable.Origin, null);
        }

        var count = inference.Variables.Count;
        var converged = false;
        for (var pass = 0; pass <= count * count; pass++)
        {
            var changed = false;
            foreach (var variable in inference.Variables)
            {
                var (upper, lower, _) = Limits(variable.Origin, variable.Polarity != 3);
                var candidate = variable.Polarity == 2 ? lower : upper ?? lower;
                if (variable.Polarity == 3 && (upper is null || lower is null ||
                    !this.ProvesOriginOutlives(upper, lower, use, inference.Comparison) || !this.ProvesOriginOutlives(lower, upper, use, inference.Comparison)))
                {
                    candidate = null;
                }

                if (!ReferenceEquals(Get(variable.Origin), candidate))
                {
                    Set(variable.Origin, candidate);
                    changed = true;
                }
            }

            if (!changed)
            {
                converged = true;
                break;
            }
        }

        if (!converged && !select)
        {
            return false;
        }

        // A variable without a principal solution takes a representative, so that its relations are judged at their own sources: an
        // invariant one its fixed equality partner, else the meet of its upper bounds, else its lower bound proven to outlive every
        // other (SPEC 15.3.6's covariant and contravariant candidates; the owner's O-350 (a)), each read from the solution reached, so
        // neither the order of the bounds nor that of the variables decides it. For one open variable the meet satisfies every bound
        // whenever any Origin does; a bound between two open variables is not read, since each is read where the other is still open
        // (PLAN: two variables without a principal solution related by a clause). A variable with neither stays open, in a call a
        // region of the body (SPEC 15.3.6). A call's selection judges no Origin relation (SPEC 15.6.1); a comparison judges every bound
        // against the representative, and its walk every member, so a variable at both covariant and contravariant positions whose
        // bounds admit a choice is accepted (SPEC 15.3.7: compatibility admits every call the requirement allows; the owner's O-348 (a)).
        var open = 0;
        foreach (var variable in inference.Variables)
        {
            open += Get(variable.Origin) is null ? 1 : 0;
        }

        if (open > 0)
        {
            var representatives = this.originScratch.Rent(count);
            try
            {
                for (var i = 0; i < count; i++)
                {
                    representatives[i] = (Get(inference.Variables[i].Origin) is null ? Representative(inference.Variables[i]) : null)!;
                }

                for (var i = 0; i < count; i++)
                {
                    if (representatives[i] is { } representative)
                    {
                        Set(inference.Variables[i].Origin, representative);
                    }
                }
            }
            finally
            {
                this.originScratch.Return(representatives, clearArray: true);
            }
        }

        if (select)
        {
            return true;
        }

        foreach (var bound in inference.Bounds)
        {
            var longer = Resolve(bound.Longer, bound.LongerVariable);
            var shorter = Resolve(bound.Shorter, bound.ShorterVariable);
            // In a comparison the solve judges no bound, only that it is solved: each bound is a member position, a clause or a
            // well-formedness premise that the comparison's owning check judges after the solve, once (ContractFits,
            // CompareCallableContracts). Outside a comparison (a call) the solve's check is the judge.
            if (longer is null || shorter is null || (!InComparison(inference.Comparison) && !this.ProvesOriginOutlives(longer, shorter, use, inference.Comparison, bound.Condition) &&
                !this.HoldsResting(longer, shorter, bound.Condition, new ProofLeaf(this, use, inference.Comparison))))
            {
                // An end left open holds a variable without a representative, itself or as an operand of a meet: no upper bound, and
                // lower bounds none of which outlives the others, whose first such pair fails here; or no bound that reached a fixed
                // Origin when the representatives were taken, the located limit (SPEC 15.3.6: a local region in an executable body; or a
                // bound only through another variable without a principal solution, PLAN G960).
                var failure = longer is not null && shorter is not null
                    ? BoundFailure(inference.Comparison, bound.Member, bound.Clause, longer, shorter, bound.Equality)
                    : Limits(Open(longer is null ? bound.Longer : bound.Shorter), false).Conflict;
                inference.Failure = failure.Kind == ComparisonFailureKind.None ? new(ComparisonFailureKind.Limit, null, null, false, inference.Comparison) : failure;
                return false;
            }
        }

        return true;

        // SPEC 15.3.6, from the solution reached so far: the meet of a variable's upper bounds, its lower bound proven to outlive every
        // other (none when no lower bound does), and the failure of its first pair of lower bounds of which neither outlives the other.
        // `shortens`: an input's well-formedness may shorten the meet (not for an invariant variable).
        (BoundOrigin? Upper, BoundOrigin? Lower, ComparisonFailure Conflict) Limits(BoundOrigin variable, bool shortens)
        {
            BoundOrigin? upper = null;
            BoundOrigin? lower = null;
            ComparisonFailure conflict = default;
            var incomparable = false;
            foreach (var bound in inference.Bounds)
            {
                if (bound.Condition == 0 && bound.ShorterVariable && ReferenceEquals(bound.Shorter, variable) && Resolve(bound.Longer, bound.LongerVariable) is { } limit)
                {
                    upper = upper is null ? limit : this.Meet(upper, limit);
                }

                if (bound.LongerVariable && ReferenceEquals(bound.Longer, variable) && Resolve(bound.Shorter, bound.ShorterVariable) is { } floor)
                {
                    if (lower is null || this.ProvesOriginOutlives(floor, lower, use, inference.Comparison))
                    {
                        lower = floor;
                    }
                    else if (!incomparable && !this.ProvesOriginOutlives(lower, floor, use, inference.Comparison))
                    {
                        incomparable = true;
                        conflict = BoundFailure(inference.Comparison, bound.Member, bound.Clause, lower, floor, false);
                    }
                }
            }

            // A later lower bound can outlive an incomparable pair: the strongest lower bound is then that one, whatever the order.
            if (incomparable && Strongest(variable) is { } strongest)
            {
                (lower, incomparable) = (strongest, false);
            }

            // SPEC 15.6.5 (PLAN G809): a limit required only in some Semantics cases joins the meet unless, in those cases, it
            // outlives the meet of the limits every case requires, and every such limit joins when no limit is required in every
            // case; each is judged against that meet alone, so the candidate does not depend on the order of the bounds. For a
            // contract comparison (SPEC 15.3.7) this is SPEC 15.3.6's rule, the owner's O-233 (C). For a call in a generic body,
            // where SPEC 8.15.2 judges each case, this one solve for every case accepts only what that accepts, not all of it: the
            // open issue "a call in a generic body is solved once for all Semantics cases".
            var unconditional = upper;
            for (var i = 0; inference.Conditional && i < inference.Bounds.Count; i++)
            {
                var bound = inference.Bounds[i];
                if (bound.Condition != 0 && bound.ShorterVariable && ReferenceEquals(bound.Shorter, variable) && Resolve(bound.Longer, bound.LongerVariable) is { } limit &&
                    (unconditional is null || !this.ProvesOriginOutlives(limit, unconditional, use, inference.Comparison, bound.Condition)))
                {
                    upper = upper is null ? limit : this.Meet(upper, limit);
                }
            }

            // An input's well-formedness only shortens the meet of a covariant variable that other bounds already limit, where they do
            // not prove it: an Origin that no argument bounds, such as a receiver Borrow formed later, stays open, and a redundant
            // operand would make the solution name an Origin it does not depend on. An Origin that only positions without evidence
            // name (PremiseSolved; SPEC 15.3.6, DECISIONS BU-99) takes the meet of its premises' longer ends as its upper bound, which
            // its other bounds are then checked against.
            for (var i = 0; (upper is not null || inference.PremiseSolved.Contains(variable)) && shortens && i < inference.WellFormed.Count; i++)
            {
                if (ReferenceEquals(inference.WellFormed[i].Shorter, variable) && Resolve(inference.WellFormed[i].Longer, true) is { } stored &&
                    (upper is null || !this.ProvesOriginOutlives(stored, upper, use, inference.Comparison)))
                {
                    upper = upper is null ? stored : this.Meet(upper, stored);
                }
            }

            return (upper, incomparable ? null : lower, conflict);
        }

        // The lower bound of `variable` proven to outlive every other, if any.
        BoundOrigin? Strongest(BoundOrigin variable)
        {
            foreach (var bound in inference.Bounds)
            {
                if (bound.LongerVariable && ReferenceEquals(bound.Longer, variable) && Resolve(bound.Shorter, bound.ShorterVariable) is { } floor &&
                    OutlivesEveryFloor(variable, floor))
                {
                    return floor;
                }
            }

            return null;
        }

        bool OutlivesEveryFloor(BoundOrigin variable, BoundOrigin candidate)
        {
            foreach (var bound in inference.Bounds)
            {
                if (bound.LongerVariable && ReferenceEquals(bound.Longer, variable) && Resolve(bound.Shorter, bound.ShorterVariable) is { } floor &&
                    !this.ProvesOriginOutlives(candidate, floor, use, inference.Comparison))
                {
                    return false;
                }
            }

            return true;
        }

        // The representative of a variable without a principal solution (above): an invariant one's fixed equality partner, an Origin
        // that bounds it from both sides; else the meet of its upper bounds, else its strongest lower bound.
        BoundOrigin? Representative((BoundOrigin Origin, int Polarity) variable)
        {
            for (var i = 0; variable.Polarity == 3 && i < inference.Bounds.Count; i++)
            {
                var bound = inference.Bounds[i];
                if (bound.LongerVariable && !bound.ShorterVariable && ReferenceEquals(bound.Longer, variable.Origin) &&
                    inference.IndexOf((Longer: bound.Shorter, Shorter: variable.Origin, LongerVariable: false, ShorterVariable: true)) >= 0)
                {
                    return bound.Shorter;
                }
            }

            var (upper, lower, _) = Limits(variable.Origin, variable.Polarity != 3);
            return upper ?? lower;
        }

        BoundOrigin Project(BoundOrigin origin) => declaringType?.Symbol is { } owner ?
            this.SubstituteStoredOrigin(origin, owner.Declaration, (BoundOrigin[])declaringType.OriginArguments) : origin;

        // The first variable without a solution that leaves `expression` unresolved: the expression itself or an operand of its meet.
        BoundOrigin Open(BoundOrigin expression)
        {
            for (var i = 0; i < expression.Operands.Count; i++)
            {
                if (Resolve(expression.Operands[i], true) is null)
                {
                    return Open(expression.Operands[i]);
                }
            }

            return expression;
        }

        BoundOrigin? Get(BoundOrigin origin)
        {
            var values = origin.Kind == OriginKind.Parameter ? origins : inputs;
            return (uint)origin.Slot < (uint)values.Length ? values[origin.Slot] : null;
        }

        void Set(BoundOrigin origin, BoundOrigin? value)
        {
            var values = origin.Kind == OriginKind.Parameter ? origins : inputs;
            if ((uint)origin.Slot < (uint)values.Length)
            {
                values[origin.Slot] = value!;
            }
        }

        BoundOrigin? Resolve(BoundOrigin expression, bool substitute)
        {
            if (!substitute)
            {
                return expression;
            }

            if (ReferenceEquals(expression.Binder, binder) && expression.Kind is OriginKind.Parameter or OriginKind.Input)
            {
                return Get(expression);
            }

            if (expression.Kind != OriginKind.Intersection)
            {
                return expression;
            }

            var meet = BoundOrigin.Static;
            for (var i = 0; i < expression.Operands.Count; i++)
            {
                if (Resolve(expression.Operands[i], true) is not { } operand)
                {
                    return null;
                }

                meet = this.Meet(meet, operand);
            }

            return meet;
        }
    }

    private sealed class OriginInference(Koto use, Koto binder)
    {
        // The use whose premises and pair binders the solve's proofs read (the first part of its key), its one source of the use.
        internal Koto Use { get; } = use;

        internal Koto Binder { get; } = binder;

        // Each bound with its source (SPEC 15.6.1): the comparison's member whose position collected it (a parameter n + 1, the result
        // ComparisonFailure.ResultStep), else the declared clause; neither, a result premise or a call's bound. Its Condition is the pair
        // binders that are borrows in every case of the entry (SPEC 15.6.5, PairSlotCondition), 0 for every case. A bound's cases are
        // the union of its entries' (Add), so it exists in every case exactly when one entry has Condition 0.
        internal List<(BoundOrigin Longer, BoundOrigin Shorter, bool LongerVariable, bool ShorterVariable,
            byte Member, Koto? Clause, bool Equality, ulong Condition)> Bounds { get; } = new();

        internal List<(BoundOrigin Origin, int Polarity)> Variables { get; } = new();

        // SPEC 15.6.4 step 3: the relations of the call's inputs' well-formedness, each an upper limit of its shorter end only.
        internal List<(BoundOrigin Longer, BoundOrigin Shorter)> WellFormed { get; } = new();

        // SPEC 15.3.6 (R2', DECISIONS BU-99): the callee's own Origins that a position without evidence (a Signature leaf row, a used
        // default) names and that no evidence position's pattern names, an Evidence root row's or the receiver's (EvidenceNames; an
        // Evidence child row's is not read, which is inert: an evidence-named covariant variable has an upper bound), which their
        // premises solve (DiscoverPremiseOrigins, the one writer); empty for a solve without such positions. The first published "which
        // position fixes this Origin" fact: U4's Decided column takes it over (its removal condition).
        internal List<BoundOrigin> PremiseSolved { get; } = new();

        // SPEC 15.3.4: the per-call binders (BoundType.Binder) of the Function Types the collection is inside, on either side
        // (CollectOriginInference, the one writer); empty between collections.
        internal List<Koto> Quantifiers { get; } = new();

        // The Callable comparison whose implementation the solve instantiates (ContractFits), whose premises its proofs assume; default
        // for a call's solve.
        internal CallableInstance Comparison { get; set; }

        // The source of the bounds being added, set by their producer around its collection (ContractFits, SolveOriginInference).
        internal (byte Member, Koto? Clause, bool Equality) Source { get; set; }

        // The first bound a failed solve could not satisfy, as the comparison's failure (ContractFits), or the located limit when a
        // variable left open had no bound that reached a fixed Origin (SolveOriginInference); None when no bound failed.
        internal ComparisonFailure Failure { get; set; }

        // Whether a bound was added with a condition, so a solve folds conditional limits (SolveOriginInference).
        internal bool Conditional { get; set; }

        internal void Add(BoundOrigin longer, BoundOrigin shorter, bool longerVariable = true, bool shorterVariable = true, ulong condition = 0)
        {
            if (ReferenceEquals(longer, shorter) && longerVariable == shorterVariable)
            {
                return;
            }

            // SPEC 15.6.5: the same bound required again exists in the union of the two case sets. An entry whose binders are among the
            // new ones already covers its cases; one whose binders include them widens to them; any other pair stays two entries, as no
            // condition names the union of two binders' cases.
            for (var i = 0; i < this.Bounds.Count; i++)
            {
                var known = this.Bounds[i];
                if (!ReferenceEquals(known.Longer, longer) || !ReferenceEquals(known.Shorter, shorter) ||
                    known.LongerVariable != longerVariable || known.ShorterVariable != shorterVariable)
                {
                    continue;
                }

                if ((known.Condition & ~condition) == 0)
                {
                    return;
                }

                if ((condition & ~known.Condition) == 0)
                {
                    known.Condition = condition;
                    this.Bounds[i] = known;
                    return;
                }
            }

            this.Bounds.Add((longer, shorter, longerVariable, shorterVariable, this.Source.Member, this.Source.Clause, this.Source.Equality, condition));
            this.Conditional |= condition != 0;
        }

        internal void Enter(Koto? binder)
        {
            if (binder is not null)
            {
                this.Quantifiers.Add(binder);
            }
        }

        // An Origin that, or an operand of which, a Function Type the collection is inside quantifies per call (Quantifiers).
        internal bool Quantifies(BoundOrigin origin)
        {
            for (var i = 0; origin.Kind == OriginKind.Input && i < this.Quantifiers.Count; i++)
            {
                if (ReferenceEquals(origin.Binder, this.Quantifiers[i]))
                {
                    return true;
                }
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (this.Quantifies(origin.Operands[i]))
                {
                    return true;
                }
            }

            return false;
        }

        // The index of the bound with these ends (its source aside), or -1.
        internal int IndexOf((BoundOrigin Longer, BoundOrigin Shorter, bool LongerVariable, bool ShorterVariable) ends)
        {
            for (var i = 0; i < this.Bounds.Count; i++)
            {
                var bound = this.Bounds[i];
                if (ReferenceEquals(bound.Longer, ends.Longer) && ReferenceEquals(bound.Shorter, ends.Shorter) &&
                    bound.LongerVariable == ends.LongerVariable && bound.ShorterVariable == ends.ShorterVariable)
                {
                    return i;
                }
            }

            return -1;
        }

        internal void Discover(BoundOrigin expression, int polarity)
        {
            if (ReferenceEquals(expression.Binder, this.Binder) && expression.Kind is OriginKind.Parameter or OriginKind.Input)
            {
                for (var i = 0; i < this.Variables.Count; i++)
                {
                    if (ReferenceEquals(this.Variables[i].Origin, expression))
                    {
                        this.Variables[i] = (expression, this.Variables[i].Polarity | polarity);
                        return;
                    }
                }

                this.Variables.Add((expression, polarity));
            }

            for (var i = 0; i < expression.Operands.Count; i++)
            {
                this.Discover(expression.Operands[i], polarity);
            }
        }
    }
}
