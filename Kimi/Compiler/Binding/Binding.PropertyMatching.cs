// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BoundConformance Conformance, BoundAccessor Requirement), BoundOrigin[]> propertyWitnessInputs = new();

    private ConstraintProof VerifyPropertyRequirement(BoundConformance conformance, BoundProperty requirement, BoundType self, BindingScope scope)
    {
        this.BindHeader(requirement.Symbol);
        var selection = this.LookupTypeMember(self, requirement.Symbol.Name);
        if (selection.Pending)
        {
            return ConstraintProof.Unknown;
        }

        if (selection.Ambiguous || selection.Member?.Property is not { } implementation)
        {
            return ConstraintProof.Refuted;
        }

        this.BindHeader(implementation.Symbol);
        var proof = this.ValidateAccessor(requirement.Getter);
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

        if (proof == ConstraintProof.Proven)
        {
            conformance.WitnessStorage.Add(new(requirement.Symbol, implementation.Symbol));
            conformance.WitnessMap.Add(requirement.Symbol, implementation.Symbol);
        }

        return proof;
    }

    private ConstraintProof MatchPropertyOperation(BoundConformance conformance, BoundAccessor requirement, BoundAccessor implementation, BoundType self, BindingScope scope, MemberSelection selection)
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
            // A projected inherited accessor still has Self = Base. Until its receiver
            // correspondence/ObjectCompatible proof is available, do not invent a conversion.
            if (selection.Path is not null)
            {
                return ConstraintProof.Unknown;
            }

            var key = (conformance, requirement);
            if (!this.propertyWitnessInputs.TryGetValue(key, out inputOrigins!))
            {
                this.propertyWitnessInputs.Add(key, inputOrigins = new BoundOrigin[2]);
            }

            Array.Clear(inputOrigins);
            proof = this.CompareCallableContracts(new(requirement), new(implementation), scope, self, selection.DeclaringType, [], [], inputOrigins);
        }
        else
        {
            if (selection.DeclaringType is null || implementation.Property.Type is not { } storageType || this.StoredType(storageType, selection.DeclaringType) is not { } field)
            {
                return ConstraintProof.Unknown;
            }

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
                proof = FitsType(field, result) ? this.ProveCopy(field, conformance.Type.Declaration) : ConstraintProof.Refuted;
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
            var witness = new BoundPropertyWitness(requirement, implementation, kind, receiver, input, result, selection.DeclaringType!, inputOrigins, selection.Path);
            conformance.PropertyWitnessStorage.Add(witness);
            conformance.PropertyWitnessMap.Add((requirement.Property.Symbol, requirement.Kind), witness);
        }

        return proof;
    }
}
