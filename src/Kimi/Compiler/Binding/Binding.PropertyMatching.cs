// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // A non-standard operation's input and Origin binding arrays on its path, kept with the path and reallocated only when a count
    // changes (BoundConformancePath.PropertyWitnessOrigins).
    private static (BoundOrigin[] Inputs, BoundOrigin[] Origins) OperationOrigins(BoundConformancePath path, BoundAccessor requirement, int inputCount, int originCount)
    {
        var entries = path.PropertyWitnessOrigins ??= new();
        for (var i = 0; i < entries.Count; i++)
        {
            var (key, inputs, origins) = entries[i];
            if (ReferenceEquals(key, requirement))
            {
                if (inputs.Length != inputCount || origins.Length != originCount)
                {
                    inputs = inputs.Length == inputCount ? inputs : new BoundOrigin[inputCount];
                    origins = origins.Length == originCount ? origins : new BoundOrigin[originCount];
                    entries[i] = (key, inputs, origins);
                }

                return (inputs, origins);
            }
        }

        var entry = (Requirement: requirement, Inputs: new BoundOrigin[inputCount], Origins: new BoundOrigin[originCount]);
        entries.Add(entry);
        return (entry.Inputs, entry.Origins);
    }

    private ConstraintProof VerifyPropertyRequirement(BoundConformancePath conformance, BoundProperty requirement, BoundType self, BindingScope scope, MemberSelection? retained = null)
    {
        this.BindHeader(requirement.Symbol);
        var selection = retained ?? this.LookupMember(self, requirement.Symbol.Name, scope, LookupRole.Value, self);
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
            (inputOrigins, var origins) = OperationOrigins(conformance, requirement, InputOriginCount(implementation.Binder), implementation.Declaration?.Origins.Count ?? 0);
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
            if (receiver.Kind != BoundTypeKind.Semantics || receiver.Semantics != (getter ? SemanticsKind.Ref : SemanticsKind.Uniq) || !ReferenceEquals(receiver.Components[0], self))
            {
                return ConstraintProof.Refuted;
            }

            if (!getter)
            {
                kind = PropertyWitnessKind.StorageSet;
                proof = input is not null && implementation.Property.Declaration.DeclarationKind == PropertyDeclarationKind.Var &&
                    SignatureEquals(input, field, requirement.Binder, implementation.Binder) &&
                    this.IsSubtypeAt(input, field, requirement.Binder, Bridge()) && ReferenceEquals(result, BoundType.Unit) ?
                    ConstraintProof.Proven : ConstraintProof.Refuted;
            }
            else if (SignatureEquals(field, result, implementation.Binder, requirement.Binder))
            {
                kind = PropertyWitnessKind.StorageCopy;
                proof = this.IsSubtypeAt(field, result, requirement.Binder, Bridge()) ? this.ProveCopy(field, scope) : ConstraintProof.Refuted;
            }
            else if (result is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Origin: { } resultOrigin } && ReferenceEquals(result.Components[0], field) && receiver.Origin is { } receiverOrigin && OriginOutlives(receiverOrigin, resultOrigin))
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

        // SPEC 11.9.1, 8.8.2: a bridge is compared as a function requirement is, in the conformance scope: the Field's Type against
        // the required input or result under that scope's premises and admitted sets (PLAN G801). The Field's Type is well formed
        // wherever its Type is, so a bridge adds no input premise of its own.
        CallableInstance Bridge() => this.RequirementComparison(input is null ? [receiver] : [receiver, input], result, requirement.Binder, scope);
    }
}
