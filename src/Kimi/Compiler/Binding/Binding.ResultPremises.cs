// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 15.3.7, 15.6.1, 15.6.4 step 3: a signature's intrinsic well-formedness (every Origin stored below a borrow layer outlives that
// layer's Origin, and for an input its Types' own clauses hold) is a premise of its definition and an obligation at each use. For the
// result, a relation that the callee's inputs and declared relations already prove needs no premise; only the others, which the
// definition would otherwise fail, are carried to the uses: each call solves its Origins with them and proves them for its substituted
// result, and a function reference or a conformance proves them from the required contract. Every input relation is a premise, so each
// is carried: each call shortens its solution with them and judges them at each input whose instantiation replaced an Origin, and a
// whole-contract comparison proves them where its solve replaced a required Origin. One visitor serves both.
public sealed partial class Binding
{
    // The callee whose result premise is set aside while a use asks whether its definition proves a relation from its other premises.
    private FunctionKoto? resultPremiseExcluded;

    // The comparison an Instance visit judges the premises under, and the first premise it fails (longer, shorter).
    private CallableInstance resultPremiseInstance;
    private (BoundOrigin Longer, BoundOrigin Shorter)? failedResultPremise;

    private enum WellFormedAction : byte
    {
        // Whether any relation is carried to the uses at all.
        Detect,

        // Judged under a Callable comparison's instantiation, for the record of a failed conversion.
        Instance,

        // An obligation of the selected call for the substituted result, judged under SPEC 15.6.5 as a Type occurrence, or for an
        // instantiated input, judged at that input.
        Require,

        // Proven at the use from its premises alone (a whole-contract comparison).
        Prove,

        // A relation of the call's Origin inference, so the call's Origins satisfy it where any solution does.
        Bound,

        // Removed from the call's Origin inference again.
        Unbound,
    }

    // The callee whose result premises are visited, or null for an input's, which no other premise of the definition replaces; the
    // use; and for Bound and Unbound the call's inference and, for a result, the receiver Type whose arguments a member's container
    // Origins project to (an input's pattern is already projected where the call collects it).
    private readonly record struct WellFormedVisit(FunctionKoto? Function, Koto Use, WellFormedAction Action, OriginInference? Inference = null, BoundType? Owner = null);

    // SPEC 15.3.7, 10.7: instantiate all of an Item's call Origins together, including named and result-only universals, against the
    // required contract. Repeated invariant occurrences and declared conditions constrain the same solution. An open evidence region
    // still uses the known-signature comparison until other evidence fixes it (SPEC 10.8).
    internal bool ItemContractFits(BoundType item, BoundType signature, BoundType required, Koto use)
    {
        if (item.Kind != BoundTypeKind.FunctionItem || item.Symbol is not { Declaration: FunctionKoto function } symbol)
        {
            return false;
        }

        signature = this.ContractType(signature, this.ConstraintScope(use));
        if (function.Origins.Count == 0 || HasOpenOrigin(required, evidenceOnly: true))
        {
            if (!CallableSignatureFits(signature, required, SignatureOwner(item)))
            {
                return false;
            }

            if (!this.HasOriginConditions(function))
            {
                return true;
            }
        }

        if (required is not { Kind: BoundTypeKind.Function, Components.Count: 2 } ||
            (ReferenceEquals(required.Components[0], BoundType.Unit) ? 0 : required.Components[0].Components.Count) != function.Parameters.Count)
        {
            return false;
        }

        var origins = this.originScratch.Rent(function.Origins.Count);
        var inputCount = InputOriginCount(function);
        var inputs = this.originScratch.Rent(inputCount);
        Array.Clear(origins, 0, function.Origins.Count);
        Array.Clear(inputs, 0, inputCount);
        try
        {
            return this.ContractFits(use, symbol, function, required, null, item, null, default, origins, inputs);
        }
        finally
        {
            this.originScratch.Return(origins, clearArray: true);
            this.originScratch.Return(inputs, clearArray: true);
        }
    }

    // With `discover`, a signature Origin that no bound mentions yet is known too, and becomes a variable (BoundOrigins).
    private static bool KnownToInference(OriginInference inference, BoundOrigin origin, bool discover = true)
    {
        if (origin is { Kind: OriginKind.Input, Binder: FunctionTypeKoto })
        {
            return false; // A nested Function Type's per-call input is bound by that Type, never by this call (SPEC 15.3.4).
        }

        if (origin is { Kind: OriginKind.Parameter, Slot: < 0 })
        {
            return false; // A pair's outer Origin `o` is bound by its Type argument, never solved by a call (SPEC 8.1.1).
        }

        if (ReferenceEquals(origin.Binder, inference.Binder) && origin.Kind is OriginKind.Parameter or OriginKind.Input)
        {
            if (origin.Kind == OriginKind.Parameter && discover)
            {
                return true; // A signature Origin, also a result-only one, is solved as under a declared relation (Discover).
            }

            for (var i = 0; i < inference.Variables.Count; i++)
            {
                if (ReferenceEquals(inference.Variables[i].Origin, origin))
                {
                    return true;
                }
            }

            return false;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (!KnownToInference(inference, origin.Operands[i], discover))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CarriesResultPremises(FunctionKoto function)
        => !function.IsConstructor && !function.IsAnonymous && function.BoundSymbol?.Type is { StoresOrigin: true };

    // As SolveOriginInference does for a declared relation, a result premise's relation is a bound, and a signature Origin becomes a
    // variable, also a result-only one. An input's relation shortens only Origins that the call already solves (OriginInference.WellFormed).
    // A relation over an Origin that this inference does not solve is no bound.
    private static void BoundOrigins(OriginInference inference, BoundOrigin longer, BoundOrigin shorter, bool result)
    {
        if (OriginOutlives(longer, shorter) || !KnownToInference(inference, longer, result) || !KnownToInference(inference, shorter, result))
        {
            return;
        }

        if (!result)
        {
            if (!inference.WellFormed.Contains((longer, shorter)))
            {
                inference.WellFormed.Add((longer, shorter));
            }

            return;
        }

        inference.Discover(longer, 0);
        inference.Discover(shorter, 0);
        inference.Add(longer, shorter);
    }

    private static bool SameLayer(BoundType actual, BoundType result)
        => actual.Kind == result.Kind && actual.Components.Count == result.Components.Count && actual.OriginArguments.Count == result.OriginArguments.Count;

    // SPEC 15.6.4 steps 1-4: a call's fresh Origins satisfy its callee's result premises where a solution does, so a result over
    // body-local borrows is bounded by every Origin it holds. Otherwise the call is solved without them and the obligation at the
    // selected call reports the relation; a premise never decides applicability (SPEC 15.6.1). With `select`, candidate applicability,
    // the solve without them judges no bound either (SolveOriginInference).
    private bool SolveCallOriginInference(FunctionKoto function, OriginInference inference, BoundOrigin[] origins, BoundOrigin[] inputs, Koto use, BoundType? declaringType, bool select = false)
    {
        if (!CarriesResultPremises(function))
        {
            return this.SolveOriginInference(inference, origins, inputs, use, declaringType, select);
        }

        var result = function.BoundSymbol!.Type!;
        this.VisitWellFormedPremises(result, result, null, new(function, use, WellFormedAction.Unbound, inference, declaringType)); // Left by an earlier pass.
        var count = inference.Bounds.Count;
        var variables = inference.Variables.Count;
        this.VisitWellFormedPremises(result, result, null, new(function, use, WellFormedAction.Bound, inference, declaringType));
        if (inference.Bounds.Count != count)
        {
            if (this.SolveOriginInference(inference, origins, inputs, use, declaringType))
            {
                return true;
            }

            this.VisitWellFormedPremises(result, result, null, new(function, use, WellFormedAction.Unbound, inference, declaringType));
            inference.Variables.RemoveRange(variables, inference.Variables.Count - variables); // The result-only Origins they discovered.
        }

        return this.SolveOriginInference(inference, origins, inputs, use, declaringType, select);
    }

    // SPEC 15.3.7: the selected call's obligations for its substituted result.
    private void RequireResultPremises(FunctionKoto function, BoundType result, Koto use)
    {
        if (CarriesResultPremises(function))
        {
            this.VisitWellFormedPremises(function.BoundSymbol!.Type!, result, null, new(function, use, WellFormedAction.Require));
        }
    }

    // SPEC 15.3.7, 15.6.4 step 3: an input's well-formedness, over the parameter pattern that a call collects for that input, shortens
    // the call's solution where the arguments alone would leave an instantiated input ill formed (SolveOriginInference). Only a call
    // collects it: a whole-contract comparison proves it instead (ProvesInputPremises), since the required contract's input premises
    // are no premises at its use.
    private void CollectInputPremises(BoundType pattern, OriginInference inference, Koto use)
    {
        if (pattern.StoresOrigin)
        {
            this.VisitWellFormedPremises(pattern, pattern, null, new(null, use, WellFormedAction.Bound, inference));
        }
    }

    // SPEC 15.3.7, 15.6.4 step 3: the selected call's obligations for an input's instantiated parameter Type, judged at the input. The
    // argument's own Type is well formed, so only a position that the fit replaced, such as an Origin shared with another input, can fail.
    // As for the fit, an argument that carries no Origin, such as one that transfers control, supplies none; and an input whose fit
    // already failed is no instance of its parameter, so its well-formedness would only restate that failure.
    private void RequireInputPremises(in BoundArgumentOperation operation)
    {
        if (operation is { Source: { } at, AdaptedType: { StoresOrigin: true } adapted, ParameterType: { StoresOrigin: true } parameter } && !ReferenceEquals(adapted, parameter) &&
            this.originRelations?.ContainsKey(at) != true && !this.HasCallRelation(at))
        {
            this.VisitWellFormedPremises(parameter, parameter, adapted, new(null, at, WellFormedAction.Require));
        }
    }

    // SPEC 15.3.7: a whole-contract comparison proves the implementation's instantiated input well formed from the premises at `use`
    // wherever its solve replaced an Origin of the required input, whose own well-formedness covers every position it kept.
    private bool ProvesInputPremises(BoundType implementation, BoundType required, Koto use)
        => ReferenceEquals(implementation, required) || !implementation.StoresOrigin ||
            this.VisitWellFormedPremises(implementation, implementation, required, new(null, use, WellFormedAction.Prove));

    // SPEC 15.3.7: a whole-contract comparison proves the implementation's result premises for its substituted result from the
    // required contract's premises at `use`.
    private bool ProvesResultPremises(FunctionKoto function, BoundType result, Koto use)
        => !CarriesResultPremises(function) || this.VisitWellFormedPremises(function.BoundSymbol!.Type!, result, null, new(function, use, WellFormedAction.Prove));

    // SPEC 15.3.7, 15.6.4 step 3: whether a use of a named function must prove Origin conditions beyond its inputs' Types: a declared
    // relation, or a result premise that its inputs and clauses do not prove.
    private bool HasOriginConditions(FunctionKoto function)
        => this.originDeclarations.GetValueOrDefault(function)?.Relations.Count > 0 ||
            (CarriesResultPremises(function) && !this.VisitWellFormedPremises(function.BoundSymbol!.Type!, function.BoundSymbol!.Type!, null, new(function, function, WellFormedAction.Detect)));

    // A failed Item comparison explains the same joint instantiation as acceptance. A representative is chosen only to locate
    // the refuted/Unknown relation; it never admits the contract. In particular, repeated invariant occurrences name the two
    // required Origins whose equality cannot be proven, rather than an uninstantiated universal of the implementation.
    private OriginContractFact? ItemContractFailure(BoundType item, BoundType signature, BoundType required, Koto at, Koto use)
    {
        if (item.Symbol is not { Declaration: FunctionKoto function } symbol)
        {
            return null;
        }

        if (function.Origins.Count == 0 || HasOpenOrigin(required, evidenceOnly: true))
        {
            return this.ConversionContractFailure(signature, required, function, at, use) ?? this.ConditionContractFailure(function, signature, required, at, use);
        }

        var origins = this.originScratch.Rent(function.Origins.Count);
        var inputCount = InputOriginCount(function);
        var inputs = this.originScratch.Rent(inputCount);
        Array.Clear(origins, 0, function.Origins.Count);
        Array.Clear(inputs, 0, inputCount);
        try
        {
            this.ContractFits(use, symbol, function, required, null, item, null, default, origins, inputs, explain: true);
            var substituted = this.SubstituteStoredOrigins(signature, function, origins.AsSpan(0, function.Origins.Count), inputs.AsSpan(0, inputCount));
            return this.ConversionContractFailure(substituted, required, function, at, use) ??
                this.ConditionContractFailure(function, substituted, required, at, use, origins, inputs);
        }
        finally
        {
            this.originScratch.Return(origins, clearArray: true);
            this.originScratch.Return(inputs, clearArray: true);
        }
    }

    // SPEC 10.7, 15.6.1, 23.3.6.5: the record of a conversion whose structural comparison holds but whose implementation conditions do not,
    // judged under the comparison's instantiation: the first declared relation, else the first result premise, that is not proven. A meet at
    // the longer end names its first failing operand (SPEC 15.3.6), and a repair writes the required input that supplies it, if any.
    private OriginContractFact? ConditionContractFailure(FunctionKoto function, BoundType actual, BoundType expected, Koto at, Koto use, BoundOrigin[]? origins = null, BoundOrigin[]? inputs = null)
    {
        if (actual.Components.Count != 2 || expected.Components.Count != 2)
        {
            return null;
        }

        var instance = new CallableInstance(actual, function, expected, FunctionTypeBinder(expected));
        if (this.originDeclarations.GetValueOrDefault(function) is { } declaration)
        {
            foreach (var relation in declaration.Relations)
            {
                var longer = origins is null ? relation.Longer : this.SubstituteStoredOrigin(relation.Longer, function, origins.AsSpan(0, function.Origins.Count), inputs.AsSpan(0, InputOriginCount(function)));
                var shorter = origins is null ? relation.Shorter : this.SubstituteStoredOrigin(relation.Shorter, function, origins.AsSpan(0, function.Origins.Count), inputs.AsSpan(0, InputOriginCount(function)));
                if (!InstanceOutlives(longer, shorter, instance, this, use, 0) ||
                    (relation.Equality && !InstanceOutlives(shorter, longer, instance, this, use, 0)))
                {
                    return this.ConditionFact(at, $"the clause '{relation.Syntax}'", longer, shorter, relation.Equality, instance, use);
                }
            }
        }

        if (!CarriesResultPremises(function))
        {
            return null;
        }

        var saved = this.resultPremiseInstance;
        this.resultPremiseInstance = instance;
        this.failedResultPremise = null;
        try
        {
            var result = function.BoundSymbol!.Type!;
            return !this.VisitWellFormedPremises(result, actual.Components[1], null, new(function, use, WellFormedAction.Instance)) && this.failedResultPremise is { } failed
                ? this.ConditionFact(at, "the result's well-formedness", failed.Longer, failed.Shorter, false, instance, use) : null;
        }
        finally
        {
            this.resultPremiseInstance = saved;
            this.failedResultPremise = null;
        }
    }

    private OriginContractFact ConditionFact(Koto at, string member, BoundOrigin longer, BoundOrigin shorter, bool equality, in CallableInstance instance, Koto use)
    {
        if (!equality && longer.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < longer.Operands.Count; i++)
            {
                if (!InstanceOutlives(longer.Operands[i], shorter, instance, this, use, 0))
                {
                    longer = longer.Operands[i];
                    break;
                }
            }
        }

        var a = this.Instantiated(longer, instance);
        var b = this.Instantiated(shorter, instance);
        return new(at, member, a, b, equality);
    }

    // An Origin as the comparison sees it: every per-call input of the implementation replaced by the required input at its position.
    private BoundOrigin Instantiated(BoundOrigin origin, in CallableInstance instance)
    {
        if (origin.Kind != OriginKind.Intersection)
        {
            return instance.Instantiate(origin);
        }

        var meet = BoundOrigin.Static;
        for (var i = 0; i < origin.Operands.Count; i++)
        {
            meet = this.Meet(meet, this.Instantiated(origin.Operands[i], instance));
        }

        return meet;
    }

    // A value call instantiates the same nested input/signature Origins and result premises as an ordinary call.
    // The selected contract supplies structure; Origin relations are checked after solving, at their supplying values.
    private bool SolveValueCallOrigins(Koto binder, BoundType parameters, ReadOnlySpan<BoundType> actuals, BoundOrigin[] origins, BoundOrigin[] inputs, Koto use)
    {
        var inference = this.BeginOriginInference(use, binder);
        for (var i = 0; i < actuals.Length; i++)
        {
            this.CollectOriginInference(parameters.Components[i], actuals[i], inference);
            this.CollectInputPremises(parameters.Components[i], inference, use);
        }

        if (binder is FunctionKoto function)
        {
            if (!this.SolveCallOriginInference(function, inference, origins, inputs, use, null, select: true))
            {
                return false;
            }

            this.OpenResultOnlyOrigins(use, function, origins);
            return true; // Selected-call clauses are judged at their supplying arguments, never as applicability.
        }

        return this.SolveOriginInference(inference, origins, inputs, use, select: true);
    }

    // Visits each borrow layer of a well-formed Type pattern beside the same layer of `result`, the pattern itself or its substitution
    // at a use; false only when Prove fails. The visit stops at a nested Function Type, as the premise does (AddResultPremises). An
    // input's visit also covers its Types' own clauses (SPEC 15.3.3), as the input premise does (AddTypePremises); beside `actual`, the
    // argument's own Type, which is well formed, it covers only the positions that the fit replaced.
    private bool VisitWellFormedPremises(BoundType pattern, BoundType result, BoundType? actual, in WellFormedVisit visit)
    {
        if (!pattern.StoresOrigin || pattern.Kind == BoundTypeKind.Function || pattern.Kind != result.Kind || pattern.Components.Count != result.Components.Count)
        {
            return true;
        }

        if (actual is not null && !SameLayer(actual, result))
        {
            actual = null; // Every position is covered when the shapes differ.
        }

        if (visit.Function is null && result.Symbol is { } symbol && result.OriginArguments.Count != 0 && !SameOriginArguments(result, actual) &&
            this.originDeclarations.TryGetValue(symbol.Declaration, out var declaration) && declaration.State == 3)
        {
            foreach (var relation in declaration.Relations)
            {
                var longer = this.SubstituteStoredOrigin(relation.Longer, symbol.Declaration, (BoundOrigin[])result.OriginArguments);
                var shorter = this.SubstituteStoredOrigin(relation.Shorter, symbol.Declaration, (BoundOrigin[])result.OriginArguments);
                if (!this.VisitTypeClause(longer, shorter, relation.Equality, relation.Syntax, visit))
                {
                    return false;
                }
            }
        }

        if ((IsBorrow(pattern.Semantics) || pattern.Kind == BoundTypeKind.Slice) && pattern.Origin is { } outer && result.Origin is { } substituted &&
            pattern.Components.Count != 0 && !this.VisitStoredPremises(pattern.Components[0], result.Components[0], actual?.Components[0], outer, substituted, visit))
        {
            return false;
        }

        for (var i = 0; i < pattern.Components.Count; i++)
        {
            if (!this.VisitWellFormedPremises(pattern.Components[i], result.Components[i], actual?.Components[i], visit))
            {
                return false;
            }
        }

        return true;

        static bool SameOriginArguments(BoundType result, BoundType? actual)
        {
            if (actual is null)
            {
                return false;
            }

            for (var i = 0; i < result.OriginArguments.Count; i++)
            {
                if (!ReferenceEquals(result.OriginArguments[i], actual.OriginArguments[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private bool VisitStoredPremises(BoundType pattern, BoundType result, BoundType? actual, BoundOrigin outer, BoundOrigin substituted, in WellFormedVisit visit)
    {
        // A pair layer of the pattern stores its slot whatever its substitution is; any other layer only beside the same shape.
        var shaped = pattern.Kind == result.Kind && pattern.Components.Count == result.Components.Count;
        if (!pattern.StoresOrigin || pattern.Kind == BoundTypeKind.Function || (!shaped && !TryPairLayer(pattern, out _, out _)))
        {
            return true;
        }

        if (actual is not null && !SameLayer(actual, result))
        {
            actual = null;
        }

        // SPEC 8.1.1, 8.1.2, 15.6.5: a pair layer's slot, the implicit `o` of an original `s/T` included, is stored only in its binder's
        // borrow cases. At a use it is the slot its substitution stores: a borrow's Origin, a caller's pair slot in that pair's borrow
        // cases, and none for a value binding, which leaves the relation vacuous.
        if (this.OuterOrigin(pattern) is { } origin && this.StoredSlot(result, visit.Use, out var slot) is { } value &&
            !this.VisitWellFormedPremise(origin, outer, result, value, actual is null ? null : this.OuterOrigin(actual), substituted, visit, RequiredCondition(slot)))
        {
            return false;
        }

        if (!shaped)
        {
            return true;
        }

        for (var i = 0; i < Math.Min(pattern.OriginArguments.Count, result.OriginArguments.Count); i++)
        {
            if (!this.VisitWellFormedPremise(pattern.OriginArguments[i], outer, result, result.OriginArguments[i], actual?.OriginArguments[i], substituted, visit))
            {
                return false;
            }
        }

        for (var i = 0; i < pattern.Components.Count; i++)
        {
            if (!this.VisitStoredPremises(pattern.Components[i], result.Components[i], actual?.Components[i], outer, substituted, visit))
            {
                return false;
            }
        }

        return true;
    }

    // `kept` is the argument's Origin at the same position, which satisfies the relation already when the fit kept it: the argument's
    // Type is well formed, and the fit, judged on its own, makes its outer Origin outlive the substituted one.
    private bool VisitWellFormedPremise(BoundOrigin longer, BoundOrigin outer, BoundType result, BoundOrigin value, BoundOrigin? kept, BoundOrigin substituted, in WellFormedVisit visit, ulong condition = 0)
    {
        if (OriginOutlives(longer, outer) || OriginOutlives(value, substituted) || ReferenceEquals(value, kept))
        {
            return true;
        }

        if (visit.Function is { } function)
        {
            var excluded = this.resultPremiseExcluded;
            this.resultPremiseExcluded = function;
            try
            {
                if (this.ProvesOriginOutlives(longer, outer, function, condition))
                {
                    return true; // The definition proves it without the premise.
                }
            }
            finally
            {
                this.resultPremiseExcluded = excluded;
            }
        }

        if ((visit.Action is WellFormedAction.Bound or WellFormedAction.Unbound) && visit.Owner?.Symbol is { } container)
        {
            // As the call's declared relations are: the container's stored Origins are the receiver Type's arguments, and a pair's `o`
            // its Type argument's slot.
            longer = this.SubstituteStoredOrigin(longer, container.Declaration, (BoundOrigin[])visit.Owner.OriginArguments, types: (BoundType[])visit.Owner.Components);
            outer = this.SubstituteStoredOrigin(outer, container.Declaration, (BoundOrigin[])visit.Owner.OriginArguments, types: (BoundType[])visit.Owner.Components);
        }

        switch (visit.Action)
        {
            case WellFormedAction.Detect:
                return false;
            case WellFormedAction.Instance:
                if (InstanceOutlives(value, substituted, this.resultPremiseInstance, this, visit.Use, 0))
                {
                    return true;
                }

                this.failedResultPremise = (value, substituted);
                return false;
            case WellFormedAction.Bound:
                BoundOrigins(visit.Inference!, longer, outer, visit.Function is not null);
                return true;
            case WellFormedAction.Unbound:
                visit.Inference!.Bounds.Remove((longer, outer, true, true));
                return true;
            case WellFormedAction.Prove:
                return this.ProvesOriginOutlives(value, substituted, visit.Use, condition);
            default:
                if (visit.Function is null)
                {
                    this.JudgeCallPosition(visit.Use, value, substituted, false, null, null, wellFormed: true, condition: condition);
                }
                else
                {
                    this.RequireResultOutlives(result, value, substituted, visit.Use, condition);
                }

                return true;
        }
    }

    // An input Type's own clause, substituted with the Origins of its occurrence: a bound of the call's inference, at a selected call a
    // `declared` relation at the input, or proven by a whole-contract comparison.
    private bool VisitTypeClause(BoundOrigin longer, BoundOrigin shorter, bool equality, Koto clause, in WellFormedVisit visit)
    {
        if (visit.Action == WellFormedAction.Require)
        {
            this.JudgeCallPosition(visit.Use, longer, shorter, equality, null, clause, declared: true);
            return true;
        }

        if (visit.Action == WellFormedAction.Prove)
        {
            return this.ProvesOriginOutlives(longer, shorter, visit.Use) && (!equality || this.ProvesOriginOutlives(shorter, longer, visit.Use));
        }

        if (visit.Action == WellFormedAction.Bound)
        {
            BoundOrigins(visit.Inference!, longer, shorter, false);
            if (equality)
            {
                BoundOrigins(visit.Inference!, shorter, longer, false);
            }
        }

        return true;
    }

    // A meet outlives an Origin exactly when each operand does, so each failing operand is its own chain (SPEC 15.6.1 Identity).
    private void RequireResultOutlives(BoundType result, BoundOrigin value, BoundOrigin substituted, Koto use, ulong condition)
    {
        if (value.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < value.Operands.Count; i++)
            {
                this.RequireResultOutlives(result, value.Operands[i], substituted, use, condition);
            }
        }
        else if (!OriginOutlives(value, substituted))
        {
            this.AddObligation(new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, result, value, substituted, WellFormed: true, Condition: condition));
        }
    }
}
