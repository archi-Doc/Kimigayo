// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 15.3.7, 15.6.1: a signature's result-Type well-formedness (every Origin stored below a borrow layer outlives that layer's
// Origin) is a premise of its definition and an obligation at each use. A relation that the callee's inputs and declared relations
// already prove needs no premise; only the others, which the definition would otherwise fail, are carried to the uses: each call
// solves its Origins with them and proves them for its substituted result, and a function reference or a conformance proves them
// from the required contract.
public sealed partial class Binding
{
    // The callee whose result premise is set aside while a use asks whether its definition proves a relation from its other premises.
    private FunctionKoto? resultPremiseExcluded;

    // The comparison an Instance visit judges the premises under, and the first premise it fails (longer, shorter).
    private CallableInstance resultPremiseInstance;
    private (BoundOrigin Longer, BoundOrigin Shorter)? failedResultPremise;

    private enum ResultPremiseAction : byte
    {
        // Whether any relation is carried to the uses at all.
        Detect,

        // Judged under a Callable comparison's instantiation, for the record of a failed conversion.
        Instance,

        // An obligation of the selected call for the substituted result, judged under SPEC 15.6.5 as a Type occurrence.
        Require,

        // Proven at the use from its premises alone (a whole-contract comparison).
        Prove,

        // A relation of the call's Origin inference, so the call's Origins satisfy it where any solution does.
        Bound,

        // Removed from the call's Origin inference again.
        Unbound,
    }

    // SPEC 15.3.7, 10.7: instantiate all of an Item's call Origins together, including named and result-only universals, against the
    // required contract. Repeated invariant occurrences and declared conditions constrain the same solution. An open evidence region
    // still uses the known-signature comparison until other evidence fixes it (SPEC 10.8).
    internal bool ItemContractFits(BoundType item, BoundType signature, BoundType required, Koto use)
    {
        if (item.Kind != BoundTypeKind.FunctionItem || item.Symbol is not { Declaration: FunctionKoto function } symbol)
        {
            return false;
        }

        if (function.Origins.Count == 0 || HasOpenOrigin(required))
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

    private static bool KnownToInference(OriginInference inference, BoundOrigin origin)
    {
        if (origin is { Kind: OriginKind.Input, Binder: FunctionTypeKoto })
        {
            return false; // A nested Function Type's per-call input is bound by that Type, never by this call (SPEC 15.3.4).
        }

        if (ReferenceEquals(origin.Binder, inference.Binder) && origin.Kind is OriginKind.Parameter or OriginKind.Input)
        {
            if (origin.Kind == OriginKind.Parameter)
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
            if (!KnownToInference(inference, origin.Operands[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CarriesResultPremises(FunctionKoto function)
        => !function.IsConstructor && !function.IsAnonymous && function.BoundSymbol?.Type is { CarriesOrigin: true };

    // SPEC 15.6.1 well-formedness as a premise: every Origin stored below a borrow layer of the result outlives that layer's Origin.
    // A nested Function Type ends the premise: its per-call Origins cannot leave their binder (SPEC 15.3.4), so the well-formedness
    // inside it stays an obligation of the definition.
    private bool ProvesWellFormedPremise(BoundType type, BoundOrigin longer, BoundOrigin shorter, Koto use)
    {
        if (!type.CarriesOrigin || type.Kind == BoundTypeKind.Function)
        {
            return false;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            var inner = type.Components[i];
            if ((IsBorrow(type.Semantics) || type.Kind == BoundTypeKind.Slice) && type.Origin is { } outer &&
                this.ProvesOriginOutlives(outer, shorter, use) && this.ProvesStoredResultPremise(inner, longer, use))
            {
                return true;
            }

            if (this.ProvesWellFormedPremise(inner, longer, shorter, use))
            {
                return true;
            }
        }

        return false;
    }

    // Whether `longer` outlives an Origin stored in `type` outside any nested Function Type.
    private bool ProvesStoredResultPremise(BoundType type, BoundOrigin longer, Koto use)
    {
        if (type.Kind == BoundTypeKind.Function)
        {
            return false;
        }

        if (type.Origin is { } origin && this.ProvesOriginOutlives(longer, origin, use))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (this.ProvesOriginOutlives(longer, type.OriginArguments[i], use))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (this.ProvesStoredResultPremise(type.Components[i], longer, use))
            {
                return true;
            }
        }

        return false;
    }

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
        this.VisitResultPremises(function, result, result, use, ResultPremiseAction.Unbound, inference, declaringType); // Left by an earlier pass.
        var count = inference.Bounds.Count;
        var variables = inference.Variables.Count;
        this.VisitResultPremises(function, result, result, use, ResultPremiseAction.Bound, inference, declaringType);
        if (inference.Bounds.Count != count)
        {
            if (this.SolveOriginInference(inference, origins, inputs, use, declaringType))
            {
                return true;
            }

            this.VisitResultPremises(function, result, result, use, ResultPremiseAction.Unbound, inference, declaringType);
            inference.Variables.RemoveRange(variables, inference.Variables.Count - variables); // The result-only Origins they discovered.
        }

        return this.SolveOriginInference(inference, origins, inputs, use, declaringType, select);
    }

    // SPEC 15.3.7: the selected call's obligations for its substituted result.
    private void RequireResultPremises(FunctionKoto function, BoundType result, Koto use)
    {
        if (CarriesResultPremises(function))
        {
            this.VisitResultPremises(function, function.BoundSymbol!.Type!, result, use, ResultPremiseAction.Require, null, null);
        }
    }

    // SPEC 15.3.7: a whole-contract comparison proves the implementation's result premises for its substituted result from the
    // required contract's premises at `use`.
    private bool ProvesResultPremises(FunctionKoto function, BoundType result, Koto use)
        => !CarriesResultPremises(function) || this.VisitResultPremises(function, function.BoundSymbol!.Type!, result, use, ResultPremiseAction.Prove, null, null);

    // SPEC 15.3.7, 15.6.4 step 3: whether a use of a named function must prove Origin conditions beyond its inputs' Types: a declared
    // relation, or a result premise that its inputs and clauses do not prove.
    private bool HasOriginConditions(FunctionKoto function)
        => this.originDeclarations.GetValueOrDefault(function)?.Relations.Count > 0 ||
            (CarriesResultPremises(function) && !this.VisitResultPremises(function, function.BoundSymbol!.Type!, function.BoundSymbol!.Type!, function, ResultPremiseAction.Detect, null, null));

    // A failed Item comparison explains the same joint instantiation as acceptance. A representative is chosen only to locate
    // the refuted/Unknown relation; it never admits the contract. In particular, repeated invariant occurrences name the two
    // required Origins whose equality cannot be proven, rather than an uninstantiated universal of the implementation.
    private OriginContractFact? ItemContractFailure(BoundType item, BoundType signature, BoundType required, Koto at, Koto use)
    {
        if (item.Symbol is not { Declaration: FunctionKoto function } symbol)
        {
            return null;
        }

        if (function.Origins.Count == 0 || HasOpenOrigin(required))
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
                    return this.ConditionFact(at, $"the clause '{relation.Syntax}'", longer, shorter, relation.Equality, expected, instance, use);
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
            return !this.VisitResultPremises(function, result, actual.Components[1], use, ResultPremiseAction.Instance, null, null) && this.failedResultPremise is { } failed
                ? this.ConditionFact(at, "the result's well-formedness", failed.Longer, failed.Shorter, false, expected, instance, use) : null;
        }
        finally
        {
            this.resultPremiseInstance = saved;
            this.failedResultPremise = null;
        }
    }

    private OriginContractFact ConditionFact(Koto at, string member, BoundOrigin longer, BoundOrigin shorter, bool equality, BoundType expected, in CallableInstance instance, Koto use)
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
        string? input = null;
        var inputs = expected.Components[0];
        for (var i = 0; i < inputs.Components.Count && !equality && !instance.IsRequiredSlot(b); i++)
        {
            if (ReferenceEquals(inputs.Components[i].Origin, a) && inputs.Components[i] is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq })
            {
                input = $"the {Ordinal(i + 1)} parameter";
                break;
            }
        }

        return new(at, member, a, b, equality, input);

        static string Ordinal(int n) => n + (n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
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
        }

        if (binder is FunctionKoto function)
        {
            if (!this.SolveCallOriginInference(function, inference, origins, inputs, use, null, select: true))
            {
                return false;
            }

            this.OpenResultOnlyOrigins(use, function, origins);
            return this.CheckCallOriginRelations(function, origins, inputs, use, null);
        }

        return this.SolveOriginInference(inference, origins, inputs, use, select: true);
    }

    // Visits each borrow layer of the callee's result pattern beside the same layer of `result`, the pattern itself or its
    // substitution at a use; false only when Prove fails. A member's container Origins are projected through `owner` for inference.
    // The visit stops at a nested Function Type, as the premise does (ProvesWellFormedPremise).
    private bool VisitResultPremises(FunctionKoto function, BoundType pattern, BoundType result, Koto use, ResultPremiseAction action, OriginInference? inference, BoundType? owner)
    {
        if (!pattern.CarriesOrigin || pattern.Kind == BoundTypeKind.Function || pattern.Kind != result.Kind || pattern.Components.Count != result.Components.Count)
        {
            return true;
        }

        if ((IsBorrow(pattern.Semantics) || pattern.Kind == BoundTypeKind.Slice) && pattern.Origin is { } outer && result.Origin is { } substituted &&
            pattern.Components.Count != 0 && !this.VisitStoredPremises(function, pattern.Components[0], result.Components[0], outer, substituted, use, action, inference, owner))
        {
            return false;
        }

        for (var i = 0; i < pattern.Components.Count; i++)
        {
            if (!this.VisitResultPremises(function, pattern.Components[i], result.Components[i], use, action, inference, owner))
            {
                return false;
            }
        }

        return true;
    }

    private bool VisitStoredPremises(FunctionKoto function, BoundType pattern, BoundType result, BoundOrigin outer, BoundOrigin substituted, Koto use, ResultPremiseAction action, OriginInference? inference, BoundType? owner)
    {
        if (!pattern.CarriesOrigin || pattern.Kind == BoundTypeKind.Function || pattern.Kind != result.Kind || pattern.Components.Count != result.Components.Count)
        {
            return true;
        }

        if (pattern.Origin is { } origin && result.Origin is { } value &&
            !this.VisitResultPremise(function, origin, outer, result, value, substituted, use, action, inference, owner))
        {
            return false;
        }

        for (var i = 0; i < Math.Min(pattern.OriginArguments.Count, result.OriginArguments.Count); i++)
        {
            if (!this.VisitResultPremise(function, pattern.OriginArguments[i], outer, result, result.OriginArguments[i], substituted, use, action, inference, owner))
            {
                return false;
            }
        }

        for (var i = 0; i < pattern.Components.Count; i++)
        {
            if (!this.VisitStoredPremises(function, pattern.Components[i], result.Components[i], outer, substituted, use, action, inference, owner))
            {
                return false;
            }
        }

        return true;
    }

    private bool VisitResultPremise(FunctionKoto function, BoundOrigin longer, BoundOrigin outer, BoundType result, BoundOrigin value, BoundOrigin substituted, Koto use, ResultPremiseAction action, OriginInference? inference, BoundType? owner)
    {
        if (OriginOutlives(longer, outer) || OriginOutlives(value, substituted))
        {
            return true;
        }

        var excluded = this.resultPremiseExcluded;
        this.resultPremiseExcluded = function;
        try
        {
            if (this.ProvesOriginOutlives(longer, outer, function))
            {
                return true; // The definition proves it without the premise.
            }
        }
        finally
        {
            this.resultPremiseExcluded = excluded;
        }

        if ((action is ResultPremiseAction.Bound or ResultPremiseAction.Unbound) && owner?.Symbol is { } container)
        {
            // As the call's declared relations are: the container's stored Origins are the receiver Type's arguments.
            longer = this.SubstituteStoredOrigin(longer, container.Declaration, (BoundOrigin[])owner.OriginArguments);
            outer = this.SubstituteStoredOrigin(outer, container.Declaration, (BoundOrigin[])owner.OriginArguments);
        }

        switch (action)
        {
            case ResultPremiseAction.Detect:
                return false;
            case ResultPremiseAction.Instance:
                if (InstanceOutlives(value, substituted, this.resultPremiseInstance, this, use, 0))
                {
                    return true;
                }

                this.failedResultPremise = (value, substituted);
                return false;
            case ResultPremiseAction.Bound:
                if (KnownToInference(inference!, longer) && KnownToInference(inference!, outer))
                {
                    // As SolveOriginInference does for a declared relation: a result-only signature Origin becomes a variable.
                    inference!.Discover(longer, 0);
                    inference.Discover(outer, 0);
                    inference.Add(longer, outer);
                }

                return true;
            case ResultPremiseAction.Unbound:
                inference!.Bounds.Remove((longer, outer, true, true));
                return true;
            case ResultPremiseAction.Prove:
                return this.ProvesOriginOutlives(value, substituted, use);
            default:
                this.RequireResultOutlives(result, value, substituted, use);
                return true;
        }
    }

    // A meet outlives an Origin exactly when each operand does, so each failing operand is its own chain (SPEC 15.6.1 Identity).
    private void RequireResultOutlives(BoundType result, BoundOrigin value, BoundOrigin substituted, Koto use)
    {
        if (value.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < value.Operands.Count; i++)
            {
                this.RequireResultOutlives(result, value.Operands[i], substituted, use);
            }
        }
        else if (!OriginOutlives(value, substituted))
        {
            this.AddObligation(new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, result, value, substituted, WellFormed: true));
        }
    }
}
