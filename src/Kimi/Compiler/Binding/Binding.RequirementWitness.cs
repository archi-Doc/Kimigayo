// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The requirement-witness core (SPEC 8.6.1, 8.11.4, 11.9.2, 22.1; IMPL 21.3.1; DECISIONS R5-M22).
// - Responsibility: the one rule from a requirement, a Self that is not abstract, the Contract reference the call selected and a premise
//   scope to what the requirement runs.
// - Inputs: verified conformance paths under the caller's scope; the built-in targets, which take their requirements' signatures once
//   per pass after headers (PrepareBuiltinTargets); the Kimi fixed-array and integer Position members.
// - Outputs: Builtin; Conformance (the witness, its declaring instance and verified mapping with its base path); Property (the
//   verified path and operation); FixedArrayEntry; IntegerPosition; Refused (a base-path witness whose requirement receiver is not
//   `ref`, OCC-X); None.
// - Invariants: a caller forwards an abstract Self before it calls the core (SPEC 8.15) and keeps its own premise scope: the
//   materializer its call's ConstraintScope, Witnesses its definition's EffectPremises.
// - Prohibitions: no member lookup, CallPlan, Origin translation, receiver projection, bridge or diagnostic; no written state beyond
//   interning, proof caches and instance storage preparation.
public sealed partial class Binding
{
    // What the core selects for a requirement on a Self (ResolveRequirementWitness).
    internal enum RequirementWitnessKind : byte
    {
        None,
        Builtin,
        Conformance,
        Property,
        FixedArrayEntry,
        IntegerPosition,
        Refused,
    }

    // Target: the built-in target, the conformance witness or the Kimi member; Declaring and Function: a conformance witness's declaring
    // instance and verified mapping; Path and Property: a Property requirement's verified path and operation.
    internal readonly record struct RequirementWitness(
        RequirementWitnessKind Kind, BindingSymbol? Target, BoundType? Declaring = null, BoundFunctionWitness? Function = null)
    {
        internal BoundConformancePath? Path { get; init; }

        internal BoundPropertyWitness Property { get; init; }
    }

    // SPEC 8.11.4: what a requirement runs on a Self that is not abstract, under the Contract reference the call selected (null when it
    // recorded none) and the caller's premise scope.
    internal RequirementWitness ResolveRequirementWitness(BindingSymbol requirement, BoundType self, BindingSymbol? contract, BindingScope scope)
    {
        if (requirement.Declaration is FunctionKoto { Accessor: { } accessor })
        {
            return this.PropertyRequirementWitness(accessor, self, contract, scope);
        }

        var builtin = this.CompilerRequirementTarget(requirement, self);
        if (builtin.CompilerFunction != CompilerFunctionKind.None)
        {
            return new(RequirementWitnessKind.Builtin, builtin);
        }

        if (contract is null)
        {
            return default;
        }

        if (IsFixedArrayEntry(self, contract))
        {
            // PLAN G32.
            return this.FixedArrayWitness(contract) is { Declaration: FunctionKoto } entry ? new(RequirementWitnessKind.FixedArrayEntry, entry) : default;
        }

        if (IsIntegerPosition(self, contract))
        {
            // SPEC 4.6.1.
            return this.IntegerPositionWitness() is { Declaration: FunctionKoto } position ? new(RequirementWitnessKind.IntegerPosition, position) : default;
        }

        if (this.ConformanceWitness(requirement, self, contract, scope, out var witness) is not { } declaring)
        {
            return default;
        }

        // OCC-X: an inherited witness lends only its receiver's base prefix, which preserves no exclusive or owning receiver.
        var function = witness.Function!;
        var refused = function.BasePath is not null && witness.Implementation.ReceiverIndex >= 0 &&
            function.RequirementReceiver is not { Semantics: SemanticsKind.Ref };
        return refused ? new(RequirementWitnessKind.Refused, null) : new(RequirementWitnessKind.Conformance, witness.Implementation, declaring, function);
    }

    // SPEC 8.11.4: the witness a verified conformance of a Self to a Contract maps a requirement to, and the instance of the Type
    // that declares it (a base's for an inherited witness); null where none resolves.
    private BoundType? ConformanceWitness(BindingSymbol requirement, BoundType self, BindingSymbol contract, BindingScope scope, out BoundWitness witness)
    {
        witness = default;
        return this.ResolveConformance(self, contract, scope, out var path) == ConstraintProof.Proven && path is { IsVerified: true } &&
            path.WitnessMap.TryGetValue(new(requirement, contract), out witness) && witness.Function is { ObjectCompatibility: ConstraintProof.Proven } function
            ? this.StoredType(function.DeclaringType, self) : null;
    }

    // SPEC 11.9.2: the verified operation a conformance maps a Property requirement's accessor to. A standard witness is a member of the
    // conforming Type itself, so only an accessor call can be inherited (OCC-X as above).
    private RequirementWitness PropertyRequirementWitness(BoundAccessor requirement, BoundType self, BindingSymbol? contract, BindingScope scope)
    {
        if (contract is null || this.ResolveConformance(self, contract, scope, out var path) != ConstraintProof.Proven || path is not { IsVerified: true } ||
            !path.PropertyWitnessMap.TryGetValue((new(requirement.Property.Symbol, contract), requirement.Kind), out var witness))
        {
            return default;
        }

        if (witness.ObjectCompatibility != ConstraintProof.Proven)
        {
            return default;
        }

        var refused = witness.Kind == PropertyWitnessKind.AccessorCall && witness.BasePath is not null && witness.Implementation.Receiver is not null &&
            witness.ReceiverType.Semantics != SemanticsKind.Ref;
        return refused ? new(RequirementWitnessKind.Refused, null) : new(RequirementWitnessKind.Property, null) { Path = path, Property = witness };
    }
}
