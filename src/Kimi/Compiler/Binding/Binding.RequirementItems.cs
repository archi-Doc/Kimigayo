// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Requirement Items retain the requirement declaration, the conforming Self and its bound declaring Contract.
    // These are type-level bindings, not a captured receiver or an implementation chosen by a later name lookup.
    private RequirementGroup? ReferenceRequirements(Koto use)
        => ReferenceName(use) is MemberAccessKoto member && this.requirementGroups.TryGetValue(member, out var group) && group.Active ? group : null;

    private BoundType? BindRequirementItem(Koto use, RequirementGroup group, BindingScope scope, BoundType? required = null, bool erase = false)
    {
        if (!group.TypeAccess)
        {
            return this.Fail(use, BindingFailure.BoundMethodValue);
        }

        BoundType? selected = null;
        var count = group.Members.Count;
        var signatures = this.typeScratch.Rent(count);
        var applicable = this.flagScratch.Rent(count);
        Array.Clear(applicable, 0, count);
        try
        {
            var pending = false;
            var matches = 0;
            for (var i = 0; i < count; i++)
            {
                var member = group.Members[i];
                if (member.Declaration is not FunctionKoto { GenericArguments.Count: 0 } function || function.Origins.Count != 0)
                {
                    return this.Fail(use, BindingFailure.Unsupported);
                }

                var contract = group.Contracts[i];
                var item = this.InternType(BoundTypeKind.FunctionItem, member, SemanticsKind.Owner, [group.Self, contract.Type!]);
                var signature = this.FunctionItemSignature(item);
                signatures[i] = signature is null ? null : this.ContractType(signature, scope);
                if (required is not null && (signatures[i] is not { } bound || !this.ItemContractFits(item, bound, required, use)))
                {
                    continue;
                }

                var proof = this.ProveConformance(group.Self, contract, scope);
                if (proof == ConstraintProof.Error)
                {
                    return this.Fail(use, BindingFailure.InvalidConstraint);
                }

                pending |= proof == ConstraintProof.Unknown;
                if (proof == ConstraintProof.Proven && this.Accessible(member, scope))
                {
                    selected = item;
                    applicable[i] = true;
                    matches++;
                }
            }

            if (pending || matches != 1)
            {
                var rejected = new RejectedCandidate[matches == 0 ? count : matches];
                var next = 0;
                for (var i = 0; i < count; i++)
                {
                    if (matches == 0 || applicable[i])
                    {
                        rejected[next++] = new((FunctionKoto)group.Members[i].Declaration, signatures[i], required, CallableSignature: required is not null, ReferenceSignature: required is not null, UnfixedReference: required is null);
                    }
                }

                (this.rejectedCandidates ??= new(ReferenceEqualityComparer.Instance))[use] = rejected;
                return this.Fail(use, pending ? BindingFailure.UnprovenConstraint : matches == 0 ? BindingFailure.NoApplicableCandidate : BindingFailure.Ambiguous);
            }
        }
        finally
        {
            this.typeScratch.Return(signatures, clearArray: true);
            this.flagScratch.Return(applicable, clearArray: true);
        }

        if ((((FunctionKoto)selected!.Symbol!.Declaration).Modifier & ModifierKind.Unsafe) != 0)
        {
            return this.Fail(use, BindingFailure.UnsafeFunctionValue);
        }

        this.CompleteFunctionItem(use, selected);
        if (erase && required is not null)
        {
            if (!this.ErasesToFunction(use, selected, required))
            {
                return this.FailMismatch(use, use, selected, required);
            }

            use.ErasedFunctionType = required;
            return required;
        }

        return selected;
    }

    private BoundCall? RequirementItemContext(BoundType item, FunctionKoto function, BoundType result)
    {
        if (item.Components.Count != 2)
        {
            return null;
        }

        var self = item.Components[0];
        var contract = this.BoundContractReference(item.Components[1]);
        var inputs = this.originScratch.Rent(InputOriginCount(function));
        try
        {
            for (var i = 0; i < InputOriginCount(function); i++)
            {
                inputs[i] = i < function.Parameters.Count ? this.OriginAtom(function, OriginKind.Input, i) : item.Symbol!.AggregateInputOrigins![i - function.Parameters.Count];
            }

            var operations = new BoundArgumentOperation[function.Parameters.Count];
            for (var i = 0; i < operations.Length; i++)
            {
                var parameter = this.ItemType(function.Parameters[i].Type.BoundType!, item, function)!;
                operations[i] = new(function.Parameters[i].Type, parameter, parameter, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, ParameterIndex: i);
            }

            var context = new BoundCall();
            context.Set(item.Symbol!, result, null, [], [], conformingType: self, inputOrigins: inputs.AsSpan(0, InputOriginCount(function)), operations: operations);
            context.RequirementContract = contract;
            return item.ContainsParameter ? context : this.InstantiateRequirementCall(context, context);
        }
        finally
        {
            this.originScratch.Return(inputs, clearArray: true);
        }
    }
}
