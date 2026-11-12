// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BoundConformancePath Conformance, BoundAccessor Requirement), BoundOrigin[]> propertyWitnessInputs = new();
    private readonly Dictionary<(BoundConformancePath Conformance, BoundAccessor Requirement), BoundOrigin[]> propertyWitnessOrigins = new();

    private ConstraintProof VerifyPropertyRequirement(BoundConformancePath conformance, BoundProperty requirement, BoundType self, BindingScope scope, MemberSelection? retained = null)
    {
        this.BindHeader(requirement.Symbol);
        var selection = retained ?? this.LookupTypeMember(self, requirement.Symbol.Name, scope, self);
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
            var witness = new BoundWitness(new(requirement.Symbol, conformance.Contract), implementation.Symbol);
            conformance.WitnessStorage.Add(witness);
            conformance.WitnessMap.Add(witness.Identity, witness);
        }

        return proof;
    }

    // SPEC 11.9.1, 11.9.2: one operation of a Property requirement against the selected Property's. A written accessor is compared by
    // CompareCallableContracts; standard access by 11.9.2's closed table of bridges (Copy of the Field's Type F, the shared borrow
    // `ref/F during receiver`, the `set` of a `var`). A bridge is an implementation too: SignatureEquals identifies its row (Origins
    // excluded, 8.6.3) and IsSubtypeAt judges its fit in the requirement's comparison (RequirementComparison), as a written witness's.
    private ConstraintProof MatchPropertyOperation(BoundConformancePath conformance, BoundAccessor requirement, BoundAccessor implementation, BoundType self, BindingScope scope, MemberSelection selection)
    {
        if (!implementation.IsPresent || !this.AccessCovers(implementation.Property.Symbol, conformance.Type, conformance.Contract, implementation.Access))
        {
            return ConstraintProof.Refuted;
        }

        if (this.SuppliesNoEvidence(new(requirement), new(implementation)))
        {
            return ConstraintProof.Error;
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
        BoundOrigin[] originBindings = [];
        ConstraintProof proof;
        if (!implementation.IsStandard)
        {
            var key = (conformance, requirement);
            var inputCount = InputOriginCount(implementation.Binder);
            if (!this.propertyWitnessInputs.TryGetValue(key, out inputOrigins!) || inputOrigins.Length != inputCount)
            {
                this.propertyWitnessInputs[key] = inputOrigins = new BoundOrigin[inputCount];
            }

            var originCount = implementation.Declaration?.Origins.Count ?? 0;
            if (!this.propertyWitnessOrigins.TryGetValue(key, out var origins) || origins.Length != originCount)
            {
                this.propertyWitnessOrigins[key] = origins = new BoundOrigin[originCount];
            }

            Array.Clear(origins);
            originBindings = origins;
            Array.Clear(inputOrigins);
            proof = this.CompareCallableContracts(new(requirement), new(implementation), scope, self, selection.DeclaringType, [], origins, inputOrigins, selection.Path);
        }
        else
        {
            if (selection.DeclaringType is null || implementation.Property.Type is not { } storageType ||
                this.PartTypeIn(storageType, selection.DeclaringType, scope) is not { } field)
            {
                return ConstraintProof.Unknown;
            }

            var getter = requirement.Kind == PropertyAccessorKind.Get;
            if (receiver.Kind != BoundTypeKind.Semantics || receiver.Semantics != (getter ? SemanticsKind.Ref : SemanticsKind.Uniq) || !SameType(receiver.Components[0], self))
            {
                return ConstraintProof.Refuted;
            }

            var comparison = this.RequirementComparison(input is null ? [receiver] : [receiver, input], result, requirement.Binder, scope);
            if (!getter)
            {
                // Input direction: the required input P fits the Field, P <: F.
                kind = PropertyWitnessKind.StorageSet;
                proof = input is not null && implementation.Property.Declaration.DeclarationKind == PropertyDeclarationKind.Var &&
                    SignatureEquals(input, field, requirement.Binder, implementation.Binder) && this.IsSubtypeAt(input, field, requirement.Binder, comparison) &&
                    SameType(result, BoundType.Unit) ? ConstraintProof.Proven : ConstraintProof.Refuted;
            }
            else if (SignatureEquals(field, result, implementation.Binder, requirement.Binder))
            {
                kind = PropertyWitnessKind.StorageCopy;
                proof = this.IsSubtypeAt(field, result, requirement.Binder, comparison) ? this.ProveCopy(field, scope) : ConstraintProof.Refuted;
            }
            else if (result is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref } && receiver.Origin is { } receiverOrigin &&
                SignatureEquals(field, result.Components[0], implementation.Binder, requirement.Binder))
            {
                // The bridge's result is the shared borrow of the Field, its Origin the receiver's.
                kind = PropertyWitnessKind.StorageBorrow;
                var borrow = this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [field], origin: receiverOrigin);
                proof = this.IsSubtypeAt(borrow, result, requirement.Binder, comparison) ? ConstraintProof.Proven : ConstraintProof.Refuted;
            }
            else
            {
                proof = ConstraintProof.Refuted;
            }
        }

        if (proof == ConstraintProof.Proven)
        {
            var objectProof = kind == PropertyWitnessKind.AccessorCall && selection.Path is not null ? ProjectedReceiverProof(implementation.Property.Symbol, implementation) : ConstraintProof.Proven;
            var witness = new BoundPropertyWitness(requirement, implementation, kind, receiver, input, result, selection.DeclaringType!, inputOrigins, selection.Path, objectProof) { Origins = originBindings, Contract = conformance.Contract };
            conformance.PropertyWitnessStorage.Add(witness);
            conformance.PropertyWitnessMap.Add((witness.Identity, requirement.Kind), witness);
            if (objectProof == ConstraintProof.Unknown)
            {
                conformance.Unsupported = true; // SPEC 12.7: OCC-X (UnsupportedProof).
                return proof;
            }

            proof = CombineProof(proof, objectProof, true);
        }

        return proof;
    }
}
