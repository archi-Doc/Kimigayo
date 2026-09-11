// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BoundConformancePath Conformance, BoundAccessor Requirement), BoundOrigin[]> propertyWitnessInputs = new();

    private ConstraintProof VerifyPropertyRequirement(BoundConformancePath conformance, BoundProperty requirement, BoundType self, BindingScope scope)
    {
        this.BindHeader(requirement.Symbol);
        var selection = this.LookupTypeMember(self, requirement.Symbol.Name, scope, self);
        if (selection.Pending)
        {
            return ConstraintProof.Unknown;
        }

        if (selection.Ambiguous || selection.Member?.Property is not { } implementation)
        {
            return ConstraintProof.Refuted;
        }

        this.BindHeader(implementation.Symbol);
        var proof = this.ProveMemberConditions(implementation.Symbol, selection.DeclaringType, scope);
        proof = CombineProof(proof, this.ValidateAccessor(requirement.Getter), true);
        proof = CombineProof(proof, this.ValidateAccessor(requirement.Setter), true);
        proof = CombineProof(proof, this.ValidateAccessor(implementation.Getter), true);
        proof = CombineProof(proof, this.ValidateAccessor(implementation.Setter), true);
        if (proof != ConstraintProof.Proven)
        {
            return proof;
        }

        proof = this.MatchPropertyOperation(conformance, requirement.Getter, implementation.Getter, self, scope, selection);
        if (requirement.Setter.IsPresent)
        {
            proof = CombineProof(proof, this.MatchPropertyOperation(conformance, requirement.Setter, implementation.Setter, self, scope, selection), true);
        }

        if (proof is ConstraintProof.Proven or ConstraintProof.Unknown)
        {
            var witness = new BoundWitness(requirement.Symbol, implementation.Symbol);
            conformance.WitnessStorage.Add(witness);
            conformance.WitnessMap.Add(requirement.Symbol, witness);
        }

        return proof;
    }

    private ConstraintProof MatchPropertyOperation(BoundConformancePath conformance, BoundAccessor requirement, BoundAccessor implementation, BoundType self, BindingScope scope, MemberSelection selection)
    {
        if (!implementation.IsPresent || !AccessCovers(implementation.Property.Symbol, conformance.Type, conformance.Contract, implementation.Access))
        {
            return ConstraintProof.Refuted;
        }

        if (requirement.Receiver is not { } receiver || requirement.Result is not { } result)
        {
            return ConstraintProof.Unknown;
        }

        receiver = this.ContractType(receiver, scope, self);
        result = this.ContractType(result, scope, self);
        var input = requirement.Input is { } requiredInput ? this.ContractType(requiredInput, scope, self) : null;
        var kind = PropertyWitnessKind.AccessorCall;
        BoundOrigin[] inputOrigins = [];
        ConstraintProof proof;
        if (!implementation.IsStandard)
        {
            var key = (conformance, requirement);
            if (!this.propertyWitnessInputs.TryGetValue(key, out inputOrigins!))
            {
                this.propertyWitnessInputs.Add(key, inputOrigins = new BoundOrigin[2]);
            }

            Array.Clear(inputOrigins);
            proof = this.CompareCallableContracts(new(requirement), new(implementation), scope, self, selection.DeclaringType, [], [], inputOrigins, selection.Path);
        }
        else
        {
            if (selection.DeclaringType is null || implementation.Property.Type is not { } storageType || this.StoredType(storageType, selection.DeclaringType) is not { } field)
            {
                return ConstraintProof.Unknown;
            }

            field = this.ContractType(field, scope);

            var getter = requirement.Kind == PropertyAccessorKind.Get;
            if (receiver.Kind != BoundTypeKind.Semantics || receiver.Semantics != (getter ? SemanticsKind.Ref : SemanticsKind.Uniq) || !SameType(receiver.Components[0], self))
            {
                return ConstraintProof.Refuted;
            }

            if (!getter)
            {
                kind = PropertyWitnessKind.StorageSet;
                proof = input is not null && implementation.Property.Declaration.DeclarationKind == PropertyDeclarationKind.Var && SignatureEquals(input, field, requirement.Binder, implementation.Binder) && FitsType(input, field) && SameType(result, BoundType.Unit) ? ConstraintProof.Proven : ConstraintProof.Refuted;
            }
            else if (SignatureEquals(field, result, implementation.Binder, requirement.Binder))
            {
                kind = PropertyWitnessKind.StorageCopy;
                proof = FitsType(field, result) ? this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, field, contract: this.Core.Copy)), scope) : ConstraintProof.Refuted;
            }
            else if (result is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Origin: { } resultOrigin } && SameType(result.Components[0], field) && receiver.Origin is { } receiverOrigin && OriginOutlives(receiverOrigin, resultOrigin))
            {
                kind = PropertyWitnessKind.StorageBorrow;
                proof = ConstraintProof.Proven;
            }
            else
            {
                proof = ConstraintProof.Refuted;
            }
        }

        if (proof == ConstraintProof.Proven)
        {
            var objectProof = kind == PropertyWitnessKind.AccessorCall && selection.Path is not null ? ProjectedReceiverProof(implementation.Property.Symbol) : ConstraintProof.Proven;
            var witness = new BoundPropertyWitness(requirement, implementation, kind, receiver, input, result, selection.DeclaringType!, inputOrigins, selection.Path, objectProof);
            conformance.PropertyWitnessStorage.Add(witness);
            conformance.PropertyWitnessMap.Add((requirement.Property.Symbol, requirement.Kind), witness);
            proof = CombineProof(proof, objectProof, true);
        }

        return proof;
    }
}
