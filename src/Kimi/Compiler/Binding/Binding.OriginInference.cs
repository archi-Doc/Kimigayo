// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(Koto Use, Koto Binder), OriginInference> originInferences = new();

    // G1128 (B2; BU-55): the per-query tables of a slot's skeleton walk (SkeletonOf: the bindings' sub-Type tuples and the skeleton per
    // tuple and polarity) and of its bindings' collection (slotCollectMemo while OpenSlotVariables collects), pooled and cleared per slot.
    private readonly Dictionary<(int Prefix, BoundType Part), int> slotTupleIds = new();
    private readonly Dictionary<(int Tuple, int Polarity), BoundType> slotSkeletons = new();
    private readonly HashSet<(BoundType, BoundType, int)> slotCollectTable = new();
    private HashSet<(BoundType, BoundType, int)>? slotCollectMemo;
    private int slotWalks;
    private int slotVariables;

    /// <summary>Gets the steps G1128's slot walks took in the latest pass: a SkeletonOf node entered past its (tuple, polarity) table, a
    /// CollectOriginInference query under the slot collection's table and a SlotValuesOf node; read by the scaling tests (Q20).</summary>
    internal int SlotWalkWork => this.slotWalks;

    /// <summary>Gets the slot variables the latest pass's calls took (NewSlotVariable); read by the scaling tests.</summary>
    internal int SlotVariableWork => this.slotVariables;

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

    // The solve state of `binder`'s candidacy at `use`, retained for every run of the candidate; a candidate that solves no Origin of
    // its own reads and writes only its openings there (OriginInference.Opened), without beginning a solve.
    private OriginInference InferenceOf(Koto use, Koto binder)
    {
        if (!this.originInferences.TryGetValue((use, binder), out var inference))
        {
            this.originInferences.Add((use, binder), inference = new(use, binder));
        }

        return inference;
    }

    private OriginInference BeginOriginInference(Koto use, Koto binder)
    {
        var inference = this.InferenceOf(use, binder);
        inference.Bounds.Clear();
        inference.Variables.Clear();
        inference.WellFormed.Clear();
        inference.PremiseSolved.Clear();
        inference.WaitingNamed.Clear();
        inference.Quantifiers.Clear();
        inference.Comparison = default;
        inference.Conditional = false;
        inference.Source = default;
        inference.Marked.Clear();
        inference.SlotVariableCount = 0;
        return inference;
    }

    // SPEC 15.3.5: the polarity (1, -1 or 0) of component `index` of `type` at `polarity`, the one composition of the walks over an
    // Origin solve's positions (CollectOriginInference, Infer, SlotComponentPolarity): a Constructed Type's slot by its schema's
    // variance, a component of an invariant layer 0, a Function Type's parameters reversed.
    private int ComponentPolarity(BoundType type, int index, int polarity, in CallableInstance comparison)
        => type.Kind == BoundTypeKind.Constructed && type.Symbol?.Schema is { } schema && index < schema.GenericSlots.Count ? ComposePolarity(polarity, schema.GenericSlots[index].OriginVariance)
            : IsInvariantLayer(type, this, null, comparison) ? 0 : type.Kind == BoundTypeKind.Function && index == 0 ? -polarity : polarity;

    private void CollectOriginInference(BoundType pattern, BoundType actual, OriginInference inference, int polarity = 1, bool result = false, Koto? known = null)
    {
        // SPEC 8.1.2 (PLAN G758): the evidence's pair layer's slot is its OuterOrigin, the original's implicit `o` included, an Origin
        // fixed in the evidence's body (G147 ruling 43: a slot's bound like any other); the pattern's own `o` is bound by its Type
        // argument (SPEC 8.1.1), so the pattern's Origins are read as written.
        if (!pattern.CarriesOrigin || !(actual.CarriesOrigin || actual.ContainsPairLayer))
        {
            return;
        }

        // G1128 (B2): BU-55's per-query table while a slot's bindings are collected: a repeated (pattern, actual, polarity) adds the
        // same bounds again. Scoped to that collection, whose queries share `result`, `known`, the comparison and the quantifiers.
        if (this.slotCollectMemo is { } memo)
        {
            if (!memo.Add((pattern, actual, polarity)))
            {
                return;
            }

            this.slotWalks++;
        }

        if (pattern.Origin is { } p && this.OuterOrigin(actual) is { } a && !this.InactiveOuterOrigin(pattern, this, null, inference.Comparison))
        {
            // SPEC 15.6.5: a pair layer's slot exists only in its binder's borrow cases, as the walk's (SlotCondition).
            Add(p, a, polarity, SlotCondition(actual, this, inference.Use, inference.Comparison));
        }

        for (var i = 0; i < Math.Min(pattern.OriginArguments.Count, actual.OriginArguments.Count); i++)
        {
            var variance = this.OriginArgumentVariance(pattern, i, this);
            Add(pattern.OriginArguments[i], actual.OriginArguments[i], ComposePolarity(polarity, variance), 0);
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
            this.CollectOriginInference(pattern.Components[i], actual.Components[i], inference, this.ComponentPolarity(pattern, i, polarity, inference.Comparison), result, known);
        }

        inference.Quantifiers.RemoveRange(enclosing, inference.Quantifiers.Count - enclosing);

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
    // is never verified here. The trials' requests share a premise session (PLAN G778).
    // G1128 R1.3 (e): `slotValues`, the values of the call's slot variables, a span of their count its caller rents for this solve (empty
    // when the solve has none).
    private bool SolveOriginInference(
        OriginInference inference, Span<BoundOrigin> origins, Span<BoundOrigin> inputs, Span<BoundOrigin> slotValues, BoundType? declaringType = null, bool select = false)
    {
        var session = this.OpenPremiseSession();
        try
        {
            return this.SolveOriginInferenceInSession(inference, origins, inputs, slotValues, declaringType, select);
        }
        finally
        {
            session?.Close();
        }
    }

    // The local functions that read or write the solution take its spans as parameters: a local function cannot capture a span.
    private bool SolveOriginInferenceInSession(
        OriginInference inference, Span<BoundOrigin> origins, Span<BoundOrigin> inputs, Span<BoundOrigin> slotValues, BoundType? declaringType, bool select)
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
            Set(variable.Origin, null, origins, inputs, slotValues);
        }

        // A variable without a principal solution takes a representative, so that its relations are judged at their own sources: an
        // invariant one its fixed equality partner, else the meet of its upper bounds, else its lower bound proven to outlive every
        // other (SPEC 15.3.6's covariant and contravariant candidates; the owner's O-350 (a)), each read from the solution reached, so
        // neither the order of the bounds nor that of the variables decides it. The solve holds each representative it takes and runs
        // the fixed point again over the other variables until a round takes no new one (PLAN G960's fixed point, widened to every
        // bound; G1128 R1.3 (i)), so a variable bounded through a held one, such as a per-call Origin under an input's premise (G1115),
        // reads its value. For one open variable the meet satisfies every bound whenever any Origin does; a bound between two open
        // variables is not read, since each is read where the other is still open (PLAN: two variables without a principal solution
        // related by a clause). A variable with neither stays open, in a call a region of the body (SPEC 15.3.6). A call's selection
        // judges no Origin relation (SPEC 15.6.1); a comparison judges every bound against the representative, and its walk every
        // member, so a variable at both covariant and contravariant positions whose bounds admit a choice is accepted (SPEC 15.3.7:
        // compatibility admits every call the requirement allows; the owner's O-348 (a)).
        var count = inference.Variables.Count;
        inference.Marked.Clear();
        var held = this.flagScratch.Rent(count);
        try
        {
            Array.Clear(held, 0, count);
            for (var round = 0; round <= count; round++)
            {
                if (!Converge(held, origins, inputs, slotValues) && !select)
                {
                    return false;
                }

                if (!Hold(held, round == 0, origins, inputs, slotValues))
                {
                    break;
                }
            }
        }
        finally
        {
            this.flagScratch.Return(held);
        }

        // G1128 R1.3 (i) (G147 rulings 23, 26): the solve's published fact (OriginInference.Marked) grows from the variables whose own
        // equality failed to every variable whose bounds or premises read a marked one, a direct read inside the solve.
        for (var grown = inference.Marked.Count != 0; grown;)
        {
            grown = false;
            foreach (var variable in inference.Variables)
            {
                if (!inference.ReadsMarked(variable.Origin) && ReadsMarkedBound(variable.Origin))
                {
                    inference.Marked.Add(variable.Origin);
                    grown = true;
                }
            }
        }

        // J2 (SPEC 15.3.6, 10.5.1; the owner's O-362 (a)): a waited variable's solution is the covariant candidate written out, the meet
        // of its evidence candidate E, solved above as any covariant variable's (so a variable its clauses bound through it is solved
        // as without the waiting part), and the call's region (CallRegion), which stands for the bounds the waiting part gives after
        // selection. E is kept beside the region at its opening (OriginInference.Open); a later run of the candidate, where the
        // completed waiting part is evidence and the solved value holds the region, writes the kept E's meet again, so the variable
        // has one spelling in every run (SPEC 10.5.4). Without an evidence candidate it is the region alone.
        foreach (var variable in inference.Variables)
        {
            if (Waited(variable))
            {
                var region = this.CallRegion(use, variable.Origin.Slot);
                Set(variable.Origin, Get(variable.Origin, origins, inputs, slotValues) is { } solved ? this.Meet(inference.Open(region, solved), region) : region, origins, inputs, slotValues);
            }
        }

        if (select)
        {
            return true;
        }

        foreach (var bound in inference.Bounds)
        {
            var longer = Resolve(bound.Longer, bound.LongerVariable, origins, inputs, slotValues);
            var shorter = Resolve(bound.Shorter, bound.ShorterVariable, origins, inputs, slotValues);
            // In a comparison the solve judges no bound, only that it is solved: each bound is a member position, a clause or a
            // well-formedness premise that the comparison's owning check judges after the solve, once (ContractFits,
            // CompareCallableContracts). Outside a comparison (a call) the solve's check is the judge.
            if (longer is null || shorter is null || (!InComparison(inference.Comparison) && !this.ProvesOriginOutlives(longer, shorter, use, inference.Comparison, bound.Condition) &&
                !this.HoldsResting(longer, shorter, bound.Condition, new ProofLeaf(this, use, inference.Comparison))))
            {
                // An end left open holds a variable without a representative, itself or as an operand of a meet: no upper bound, and
                // lower bounds none of which outlives the others, whose first such pair fails here; or no bound that reached a fixed
                // Origin, even through a held representative, the located limit (SPEC 15.3.6: "If none is reached, the variable has no
                // candidate").
                var failure = longer is not null && shorter is not null
                    ? BoundFailure(inference.Comparison, bound.Member, bound.Clause, longer, shorter, bound.Equality)
                    : Limits(Open(longer is null ? bound.Longer : bound.Shorter, origins, inputs, slotValues), false, origins, inputs, slotValues).Conflict;
                inference.Failure = failure.Kind == ComparisonFailureKind.None ? new(ComparisonFailureKind.Limit, null, null, false, inference.Comparison) : failure;
                return false;
            }
        }

        return true;

        // SPEC 15.3.6, from the solution reached so far: the meet of a variable's upper bounds, its lower bound proven to outlive every
        // other (none when no lower bound does), and the failure of its first pair of lower bounds of which neither outlives the other.
        // `shortens`: an input's well-formedness may shorten the meet (not for an invariant variable).
        (BoundOrigin? Upper, BoundOrigin? Lower, ComparisonFailure Conflict) Limits(BoundOrigin variable, bool shortens, Span<BoundOrigin> origins, Span<BoundOrigin> inputs, Span<BoundOrigin> slotValues)
        {
            BoundOrigin? upper = null;
            BoundOrigin? lower = null;
            ComparisonFailure conflict = default;
            var incomparable = false;
            foreach (var bound in inference.Bounds)
            {
                if (bound.Condition == 0 && bound.ShorterVariable && ReferenceEquals(bound.Shorter, variable) && Bounding(bound.Longer, bound.LongerVariable, origins, inputs, slotValues) is { } limit)
                {
                    upper = upper is null ? limit : this.Meet(upper, limit);
                }

                if (bound.LongerVariable && ReferenceEquals(bound.Longer, variable) && Bounding(bound.Shorter, bound.ShorterVariable, origins, inputs, slotValues) is { } floor)
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
            if (incomparable && Strongest(variable, origins, inputs, slotValues) is { } strongest)
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
                if (bound.Condition != 0 && bound.ShorterVariable && ReferenceEquals(bound.Shorter, variable) && Bounding(bound.Longer, bound.LongerVariable, origins, inputs, slotValues) is { } limit &&
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
                if (ReferenceEquals(inference.WellFormed[i].Shorter, variable) && Bounding(inference.WellFormed[i].Longer, true, origins, inputs, slotValues) is { } stored &&
                    (upper is null || !this.ProvesOriginOutlives(stored, upper, use, inference.Comparison)))
                {
                    upper = upper is null ? stored : this.Meet(upper, stored);
                }
            }

            return (upper, incomparable ? null : lower, conflict);
        }

        // The lower bound of `variable` proven to outlive every other, if any.
        BoundOrigin? Strongest(BoundOrigin variable, Span<BoundOrigin> origins, Span<BoundOrigin> inputs, Span<BoundOrigin> slotValues)
        {
            foreach (var bound in inference.Bounds)
            {
                if (bound.LongerVariable && ReferenceEquals(bound.Longer, variable) && Bounding(bound.Shorter, bound.ShorterVariable, origins, inputs, slotValues) is { } floor &&
                    OutlivesEveryFloor(variable, floor, origins, inputs, slotValues))
                {
                    return floor;
                }
            }

            return null;
        }

        bool OutlivesEveryFloor(BoundOrigin variable, BoundOrigin candidate, Span<BoundOrigin> origins, Span<BoundOrigin> inputs, Span<BoundOrigin> slotValues)
        {
            foreach (var bound in inference.Bounds)
            {
                if (bound.LongerVariable && ReferenceEquals(bound.Longer, variable) && Bounding(bound.Shorter, bound.ShorterVariable, origins, inputs, slotValues) is { } floor &&
                    !this.ProvesOriginOutlives(candidate, floor, use, inference.Comparison))
                {
                    return false;
                }
            }

            return true;
        }

        // The fixed point over the variables not held: each takes its candidate from the solution reached (Limits) until a pass changes
        // none; false when it does not converge.
        bool Converge(bool[] isHeld, Span<BoundOrigin> origins, Span<BoundOrigin> inputs, Span<BoundOrigin> slotValues)
        {
            for (var pass = 0; pass <= count * count; pass++)
            {
                var changed = false;
                for (var v = 0; v < count; v++)
                {
                    var variable = inference.Variables[v];
                    if (isHeld[v])
                    {
                        continue;
                    }

                    var (upper, lower, _) = Limits(variable.Origin, variable.Polarity != 3, origins, inputs, slotValues);
                    var candidate = variable.Polarity == 2 ? lower : upper ?? lower;
                    if (variable.Polarity == 3 && (upper is null || lower is null ||
                        !this.ProvesOriginOutlives(upper, lower, use, inference.Comparison) || !this.ProvesOriginOutlives(lower, upper, use, inference.Comparison)))
                    {
                        candidate = null;
                    }

                    if (!ReferenceEquals(Get(variable.Origin, origins, inputs, slotValues), candidate))
                    {
                        Set(variable.Origin, candidate, origins, inputs, slotValues);
                        changed = true;
                    }
                }

                if (!changed)
                {
                    return true;
                }
            }

            return false;
        }

        // The representatives of the variables still without a solution, taken at once from the solution reached and held; in the
        // first round an invariant one whose own equality fails is marked (OriginInference.Marked). False when none is taken.
        bool Hold(bool[] isHeld, bool first, Span<BoundOrigin> origins, Span<BoundOrigin> inputs, Span<BoundOrigin> slotValues)
        {
            var taken = false;
            var representatives = this.originScratch.Rent(count);
            try
            {
                for (var i = 0; i < count; i++)
                {
                    representatives[i] = (Get(inference.Variables[i].Origin, origins, inputs, slotValues) is null ? Representative(inference.Variables[i], origins, inputs, slotValues) : null)!;
                    if (first && representatives[i] is not null && inference.Variables[i].Polarity == 3 && EqualityFails(inference.Variables[i].Origin))
                    {
                        inference.Marked.Add(inference.Variables[i].Origin);
                    }
                }

                for (var i = 0; i < count; i++)
                {
                    if (representatives[i] is { } representative)
                    {
                        Set(inference.Variables[i].Origin, representative, origins, inputs, slotValues);
                        isHeld[i] = taken = true;
                    }
                }
            }
            finally
            {
                this.originScratch.Return(representatives, clearArray: true);
            }

            return taken;
        }

        // Whether an invariant variable's own equality fails: two of its fixed equality partners (the pairs of bounds Representative
        // reads) are not proven equal. Its other bounds (a clause, a covariant binding, a premise) are checks of their own.
        bool EqualityFails(BoundOrigin variable)
        {
            BoundOrigin? first = null;
            foreach (var bound in inference.Bounds)
            {
                if (bound.LongerVariable && !bound.ShorterVariable && ReferenceEquals(bound.Longer, variable) &&
                    inference.IndexOf((Longer: bound.Shorter, Shorter: variable, LongerVariable: false, ShorterVariable: true)) >= 0)
                {
                    if (first is null)
                    {
                        first = bound.Shorter;
                    }
                    else if (!this.ProvesOriginOutlives(first, bound.Shorter, use, inference.Comparison) || !this.ProvesOriginOutlives(bound.Shorter, first, use, inference.Comparison))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // Whether a bound or an input's premise of `variable` names a marked variable at its other end, itself or as an operand of a meet.
        bool ReadsMarkedBound(BoundOrigin variable)
        {
            foreach (var bound in inference.Bounds)
            {
                if ((ReferenceEquals(bound.Shorter, variable) && inference.ReadsMarked(bound.Longer)) || (ReferenceEquals(bound.Longer, variable) && inference.ReadsMarked(bound.Shorter)))
                {
                    return true;
                }
            }

            foreach (var premise in inference.WellFormed)
            {
                if (ReferenceEquals(premise.Shorter, variable) && inference.ReadsMarked(premise.Longer))
                {
                    return true;
                }
            }

            return false;
        }

        // The representative of a variable without a principal solution (above): an invariant one's fixed equality partner, an Origin
        // that bounds it from both sides; else the meet of its upper bounds, else its strongest lower bound.
        BoundOrigin? Representative((BoundOrigin Origin, int Polarity) variable, Span<BoundOrigin> origins, Span<BoundOrigin> inputs, Span<BoundOrigin> slotValues)
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

            var (upper, lower, _) = Limits(variable.Origin, variable.Polarity != 3, origins, inputs, slotValues);
            return upper ?? lower;
        }

        // J2 (SPEC 15.3.6; the owner's O-362 (a)): a covariant variable that an evidence position and a position without evidence both
        // name (WaitingNamed) is a variable the waiting part's check bounds too, which the solve does not see: its solution is its
        // evidence candidate met with the region of the call, judged after selection by each fit at its source.
        bool Waited((BoundOrigin Origin, int Polarity) variable)
            => variable is { Polarity: 1, Origin.Kind: OriginKind.Parameter } && ReferenceEquals(variable.Origin.Binder, binder) && inference.WaitingNamed.Contains(variable.Origin.Slot);

        BoundOrigin Project(BoundOrigin origin) => declaringType?.Symbol is { } owner ?
            this.SubstituteStoredOrigin(origin, owner.Declaration, (BoundOrigin[])declaringType.OriginArguments) : origin;

        // The first variable without a solution that leaves `expression` unresolved: the expression itself or an operand of its meet.
        BoundOrigin Open(BoundOrigin expression, Span<BoundOrigin> origins, Span<BoundOrigin> inputs, Span<BoundOrigin> slotValues)
        {
            for (var i = 0; i < expression.Operands.Count; i++)
            {
                if (Resolve(expression.Operands[i], true, origins, inputs, slotValues) is null)
                {
                    return Open(expression.Operands[i], origins, inputs, slotValues);
                }
            }

            return expression;
        }

        // A call's slot variable is the solve's third kind of variable, valued in the per-solve span (R1.3 (e) 4: one index space).
        BoundOrigin? Get(BoundOrigin origin, Span<BoundOrigin> origins, Span<BoundOrigin> inputs, Span<BoundOrigin> slotValues)
        {
            if (IsSlotVariable(origin))
            {
                return slotValues[SlotVariableIndex(origin, slotValues)];
            }

            var values = origin.Kind == OriginKind.Parameter ? origins : inputs;
            return (uint)origin.Slot < (uint)values.Length ? values[origin.Slot] : null;
        }

        void Set(BoundOrigin origin, BoundOrigin? value, Span<BoundOrigin> origins, Span<BoundOrigin> inputs, Span<BoundOrigin> slotValues)
        {
            if (IsSlotVariable(origin))
            {
                slotValues[SlotVariableIndex(origin, slotValues)] = value!;
                return;
            }

            var values = origin.Kind == OriginKind.Parameter ? origins : inputs;
            if ((uint)origin.Slot < (uint)values.Length)
            {
                values[origin.Slot] = value!;
            }
        }

        // A bound's other end as a bound of a variable: none when it reads a marked variable (G1128 R1.3 (i), G147 ruling 43 (4)): a
        // marked variable's held value is a stand-in that no check reads, so it bounds no other variable either (rule W inside the
        // solve), and a variable bounded through it takes its candidate from its other bounds.
        BoundOrigin? Bounding(BoundOrigin end, bool substitute, Span<BoundOrigin> origins, Span<BoundOrigin> inputs, Span<BoundOrigin> slotValues)
            => inference.ReadsMarked(end) ? null : Resolve(end, substitute, origins, inputs, slotValues);

        BoundOrigin? Resolve(BoundOrigin expression, bool substitute, Span<BoundOrigin> origins, Span<BoundOrigin> inputs, Span<BoundOrigin> slotValues)
        {
            if (!substitute)
            {
                return expression;
            }

            if ((ReferenceEquals(expression.Binder, binder) && expression.Kind is OriginKind.Parameter or OriginKind.Input) ||
                inference.SolvesSlotVariable(expression))
            {
                return Get(expression, origins, inputs, slotValues);
            }

            if (expression.Kind != OriginKind.Intersection)
            {
                return expression;
            }

            var meet = BoundOrigin.Static;
            for (var i = 0; i < expression.Operands.Count; i++)
            {
                if (Resolve(expression.Operands[i], true, origins, inputs, slotValues) is not { } operand)
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

        // G1128 R1.3 (i) (G147 rulings 23, 26, 29): the solve's published fact, written by each solve: the invariant variables whose own
        // equality failed (two fixed equality partners not proven equal; its record is the partners' `==`, formed by the fits) and every
        // variable whose bounds or premises read a marked one. The held value of a marked variable is read by no check: each check of the
        // selected call or construction that reads one rests on it (rule W, SPEC 23.1.2), its clauses (JudgeCallClauses) and its inputs'
        // premises (RequireInputPremises). The result's fit at its destination and a construction Type's clause proof read the substituted
        // value instead (PLAN: a dependent record of a failed equality outside the solve's readers), as does a value call, whose solve
        // its solution does not carry.
        internal List<BoundOrigin> Marked { get; } = new();

        // J2 (SPEC 15.3.6; the owner's O-362 (a); PLAN G936): the slots of the callee's own Origin parameters that an evidence position
        // and a position without evidence both name (SlotSolution.OpenWaited, the one writer, a candidate's late solve only): the solve
        // writes a covariant one as its evidence candidate met with the call's region (CallRegion), which the waiting part's check
        // bounds as the evidence's fit does; empty for any other solve.
        internal HashSet<int> WaitingNamed { get; } = new();

        // J2 (SPEC 15.3.6; PLAN G936): the candidate's openings: each region its late solve opened (a Type slot's SlotRegion,
        // OpenSlotOrigins; a waited Origin's CallRegion, the solve) with the evidence candidate E it stands beside, so the variable's
        // solution is `(E and region)`. Written at the opening (Open), cleared at the candidate's first solve (OpenWaited) and kept for
        // its later runs; the selected candidate's are the region graph's records (RecordOpenings). E is never read back from a meet.
        internal List<(BoundOrigin Region, BoundOrigin Bound)> Opened { get; } = new();

        // SPEC 15.3.4: the per-call binders (BoundType.Binder) of the Function Types the collection is inside, on either side
        // (CollectOriginInference, the one writer); empty between collections.
        internal List<Koto> Quantifiers { get; } = new();

        // The Callable comparison whose implementation the solve instantiates (ContractFits), whose premises its proofs assume; default
        // for a call's solve.
        internal CallableInstance Comparison { get; set; }

        // The source of the bounds being added, set by their producer around its collection (ContractFits, SolveOriginInference).
        internal (byte Member, Koto? Clause, bool Equality) Source { get; set; }

        // The first bound a failed solve could not satisfy, as the comparison's failure (ContractFits), or the located limit when a
        // variable left open had no bound that reached a fixed Origin, even through a held representative (SolveOriginInference); None
        // when no bound failed.
        internal ComparisonFailure Failure { get; set; }

        // Whether a bound was added with a condition, so a solve folds conditional limits (SolveOriginInference).
        internal bool Conditional { get; set; }

        // The evidence candidate of `region`'s variable: `solved`, kept at its opening; the kept one where `solved` already holds the
        // region, a later run's value that the completed waiting part has entered. Where such a value has no kept candidate, the
        // variable had none at its opening (the first run wrote the region alone), so there is no E to read back: `solved` is that
        // run's own solve over the region and is returned as it is.
        internal BoundOrigin Open(BoundOrigin region, BoundOrigin solved)
        {
            var later = HoldsOrigin(solved, region);
            for (var i = 0; i < this.Opened.Count; i++)
            {
                if (ReferenceEquals(this.Opened[i].Region, region))
                {
                    if (!later)
                    {
                        this.Opened[i] = (region, solved);
                    }

                    return this.Opened[i].Bound;
                }
            }

            if (!later)
            {
                this.Opened.Add((region, solved));
            }

            return solved;
        }

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

        // Whether `origin` or an operand of its meet is a marked variable (Marked): the variable itself or a pattern's spelling of it,
        // the same kind, binder and slot.
        internal bool ReadsMarked(BoundOrigin origin)
        {
            if (this.Marked.Count == 0)
            {
                return false;
            }

            foreach (var marked in this.Marked)
            {
                if (ReferenceEquals(marked, origin) || (origin.Kind is OriginKind.Parameter or OriginKind.Input && marked.Kind == origin.Kind &&
                    ReferenceEquals(marked.Binder, origin.Binder) && marked.Slot == origin.Slot))
                {
                    return true;
                }
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (this.ReadsMarked(origin.Operands[i]))
                {
                    return true;
                }
            }

            return false;
        }

        // G1128 (R1.3 (b), (e)): the count of the call's slot variables, measured once at the slot step (OpenSlotVariables), the width
        // of the span their solve's values take; reset by BeginOriginInference and when the solve's values settle them
        // (SettleSlotVariables), after which a slot variable left without a value is the call's region, no variable.
        internal int SlotVariableCount { get; set; }

        // Whether `origin` is one of the call's slot variables this solve values (SlotVariableCount): the one test of the solve's third
        // kind of variable (Discover, the solver's Resolve, SlotValue).
        internal bool SolvesSlotVariable(BoundOrigin origin)
            => IsSlotVariable(origin) && ReferenceEquals(origin.Binder, this.Use) && SlotVariableIndex(origin) < this.SlotVariableCount;

        internal void Discover(BoundOrigin expression, int polarity)
        {
            if ((ReferenceEquals(expression.Binder, this.Binder) && expression.Kind is OriginKind.Parameter or OriginKind.Input) ||
                this.SolvesSlotVariable(expression))
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
