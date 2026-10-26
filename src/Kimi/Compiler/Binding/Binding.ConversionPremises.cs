// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 10.7, 15.3.7, 15.6.1 (DECISIONS R4-M144): a Callable comparison against a Function Type assumes the target's own Types, whose
// well-formedness each call through the target proves, and proves the implementation's from them. Inputs: the target and source
// Function Types, the comparison's CallableInstance and the use. Users: a Function value's conversion (FunctionFits, a call argument's
// RequireArgumentConversion) proves its source's signature Types under that instantiation, its record ConversionContractFailure's; a
// named function's whole-contract comparison (ContractFits) proves its premises, its record ConditionContractFailure's. Invariants: the
// target premises are a stack scoped to the comparison in progress, each operand of a meet at the longer end its own relation; the
// visit is Binding.ResultPremises' (WellFormedAction Collect, Convert, Instance and Prove). Never a Binding failure of its own: a
// failure is the caller's conversion record.
public sealed partial class Binding
{
    // A conversion's target premises (ProvesConversionPremises), in stack order: each conversion appends its own and removes them.
    private readonly List<(BoundOrigin Longer, BoundOrigin Shorter)> conversionPremises = [];

    // SPEC 15.6.1, 15.3.7, 10.7 (DECISIONS R4-M144): a conversion of a Function value to another Function Type proves, at `use`, the
    // source's signature Types (its inputs and result), which its implementation assumes (an anonymous function's result included), from
    // the target's, which each call through the target proves, under the comparison's per-call instantiation (`instance`, as
    // ConversionContractFailure's): a source relation that is a target relation once instantiated is covered, one the conversion proves
    // holds, and any other fails the conversion (failedResultPremise names it for the caller's record).
    internal bool ProvesConversionPremises(BoundType source, BoundType target, in CallableInstance instance, Koto use)
    {
        var (previous, first) = (this.resultPremiseInstance, this.CollectTargetPremises(target, use));
        this.resultPremiseInstance = instance;
        try
        {
            var inputs = source.Components[0];
            var count = Math.Min(inputs.Components.Count, target.Components[0].Components.Count);
            this.failedResultPremise = null;
            for (var i = 0; i < count; i++)
            {
                if (!this.VisitWellFormedPremises(inputs.Components[i], inputs.Components[i], null, new(null, use, WellFormedAction.Convert)))
                {
                    return false;
                }
            }

            return this.VisitWellFormedPremises(source.Components[1], source.Components[1], null, new(null, use, WellFormedAction.Convert, Result: true));
        }
        finally
        {
            this.conversionPremises.RemoveRange(first, this.conversionPremises.Count - first);
            this.resultPremiseInstance = previous;
        }
    }

    // SPEC 15.6.1, 10.7, 15.3.7 (DECISIONS R4-M144): a Callable comparison against a Function Type assumes the target's own Types, its
    // inputs' and result's well-formedness, which each call through the target proves: collected for the comparison in progress, which
    // removes them from the returned mark: a Function value's conversion (ProvesConversionPremises), a named function's whole-contract
    // comparison (ContractFits) and its record (ConditionContractFailure).
    private int CollectTargetPremises(BoundType target, Koto use)
    {
        var first = this.conversionPremises.Count;
        if (target is { Kind: BoundTypeKind.Function, Components.Count: 2 })
        {
            var inputs = target.Components[0];
            for (var i = 0; i < inputs.Components.Count; i++)
            {
                this.VisitWellFormedPremises(inputs.Components[i], inputs.Components[i], null, new(null, use, WellFormedAction.Collect));
            }

            this.VisitWellFormedPremises(target.Components[1], target.Components[1], null, new(null, use, WellFormedAction.Collect, Result: true));
        }

        return first;
    }

    // SPEC 15.6.1, 10.7 (DECISIONS R4-M144): an argument the call converts to a Function-typed parameter, whose position its fit leaves to
    // applicability (CollectCallRelations), proves its source's signature Types at the argument (ProvesConversionPremises), its failure the
    // conversion's contract record there, UnprovenOriginContract_Kd, as a value fit's is (RecordMismatch). A Function Item proves its own
    // (ContractFits); an argument whose binding failed is no instance of the parameter.
    private void RequireArgumentConversion(in BoundArgumentOperation operation)
    {
        if (operation is { Source: { BindingFailure: BindingFailure.None } at, SourceType: { Kind: BoundTypeKind.Function or BoundTypeKind.Closure } source } &&
            operation.ParameterType is { Kind: BoundTypeKind.Function, Components.Count: 2 } parameter &&
            this.ConversionSignature(source, at, out var signature, out var own) && !ReferenceEquals(signature, parameter) &&
            !this.ProvesConversionPremises(signature, parameter, ConversionInstance(signature, own, parameter), at) &&
            this.ConversionContractFailure(signature, parameter, own, at, at) is { } contract)
        {
            this.FailExplained(ref this.originContracts, at, BindingFailure.OriginContract, contract);
        }
    }

    // A conversion target's relation (ProvesConversionPremises): a meet outlives an Origin exactly when each operand does (SPEC 15.6.1
    // Identity), so each operand is its own relation, as in a source's (Converted).
    private void CollectConversionPremise(BoundOrigin longer, BoundOrigin shorter)
    {
        if (longer.Kind != OriginKind.Intersection)
        {
            this.conversionPremises.Add((longer, shorter));
            return;
        }

        for (var i = 0; i < longer.Operands.Count; i++)
        {
            this.CollectConversionPremise(longer.Operands[i], shorter);
        }
    }

    // A whole-contract comparison's relation (Prove): one a Function Type target's own Types give (CollectTargetPremises), one per operand
    // of a meet at its longer end, or one the premises at `use` prove.
    private bool ProvesFromPremises(BoundOrigin longer, BoundOrigin shorter, Koto use, ulong condition = 0)
    {
        if (this.conversionPremises.Count == 0 || longer.Kind != OriginKind.Intersection)
        {
            return this.conversionPremises.Contains((longer, shorter)) || this.ProvesOriginOutlives(longer, shorter, use, condition);
        }

        for (var i = 0; i < longer.Operands.Count; i++)
        {
            if (!this.ProvesFromPremises(longer.Operands[i], shorter, use, condition))
            {
                return false;
            }
        }

        return true;
    }

    // A Callable comparison's relation (Convert, Instance), instantiated, one per operand of a meet at its longer end: covered by a
    // target relation or proven at the conversion; the first that is neither is the failed premise its record names.
    private bool Converted(BoundOrigin longer, BoundOrigin shorter, Koto use)
    {
        if (longer.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < longer.Operands.Count; i++)
            {
                if (!this.Converted(longer.Operands[i], shorter, use))
                {
                    return false;
                }
            }

            return true;
        }

        var (a, b) = (this.resultPremiseInstance.Instantiate(longer), this.Instantiated(shorter, this.resultPremiseInstance));
        if (this.conversionPremises.Contains((a, b)) || InstanceOutlives(longer, shorter, this.resultPremiseInstance, this, use, 0))
        {
            return true;
        }

        this.failedResultPremise = (a, b);
        return false;
    }
}
