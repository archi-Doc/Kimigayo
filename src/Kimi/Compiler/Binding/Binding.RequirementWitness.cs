// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The requirement-witness core (SPEC 8.6.1, 8.11.3, 11.9.2, 22.1; IMPL 21.3.1; DECISIONS R5-M22).
// - Responsibility: the one rule from a requirement, a Self that is not abstract, the Contract reference the call selected and a premise
//   scope to what the requirement runs.
// - Inputs: verified conformance paths under the caller's scope; the built-in row of the requirement's own Contract (ImplTable.Builtin),
//   its built-in target, which takes its requirement's signature once per pass after headers (BuiltinTarget, PrepareBuiltinTargets), or
//   its library group (Binding.LibraryWitnesses).
// - Outputs: Builtin; Conformance (the witness, its declaring instance and verified mapping with its base path); Property (the
//   verified path and operation, the implementing Type's instance); LibraryWitness (a group's function and its row); Refused (a
//   base-path witness whose requirement receiver is not `ref`, OCC-X); None.
// - Invariants: a caller forwards an abstract Self before it calls the core (SPEC 8.15) and keeps its own premise scope: the
//   materializer its call's ConstraintScope, Witnesses its definition's EffectPremises, TyCtxt.Witness the library root's, or the
//   reading definition's EffectPremises for a Self with an abstract part.
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
        LibraryWitness,
        Refused,
    }

    // Target: the built-in target, the conformance witness or the group's function; Declaring: a conformance or Property witness's declaring
    // instance; Function: a conformance witness's verified mapping; Path and Property: a Property requirement's verified path and operation;
    // Group: a library witness's row.
    internal readonly record struct RequirementWitness(
        RequirementWitnessKind Kind, BindingSymbol? Target, BoundType? Declaring = null, BoundFunctionWitness? Function = null)
    {
        internal BoundConformancePath? Path { get; init; }

        internal BoundPropertyWitness Property { get; init; }

        internal LibraryWitnessGroup Group { get; init; }
    }

    // SPEC 8.11.3: what a requirement runs on a Self that is not abstract, under the Contract reference the call selected (null when it
    // recorded none) and the caller's premise scope.
    internal RequirementWitness ResolveRequirementWitness(BindingSymbol requirement, BoundType self, BindingSymbol? contract, BindingScope scope, QuerySite site)
    {
        if (requirement.Declaration is FunctionKoto { Accessor: { } accessor })
        {
            return this.PropertyRequirementWitness(accessor, self, contract, scope, site);
        }

        // The built-in row of the requirement's own Contract (ImplTable.Builtin), keyed on it: a built-in Type's conformance has no
        // declaration for a selected reference to name.
        var row = ImplTable.Builtin(self, requirement.Scope.Owner.BoundSymbol);
        if (this.BuiltinTarget(row) is { } builtin)
        {
            return new(RequirementWitnessKind.Builtin, builtin);
        }

        if (row.Kind == ImplTable.BuiltinKind.Library)
        {
            return this.GroupWitness(row.Group, requirement.Name) is { Declaration: FunctionKoto } member
                ? new(RequirementWitnessKind.LibraryWitness, member) { Group = row.Group } : default;
        }

        if (contract is null)
        {
            return default;
        }

        if (this.ConformanceWitness(requirement, self, contract, scope, site, out var witness) is not { } declaring)
        {
            return default;
        }

        var function = witness.Function!;
        return Refused(function.BasePath, witness.Implementation.ReceiverIndex >= 0, function.RequirementReceiver)
            ? new(RequirementWitnessKind.Refused, null) : new(RequirementWitnessKind.Conformance, witness.Implementation, declaring, function);
    }

    // OCC-X: an inherited witness with a receiver lends only its base prefix, which preserves no exclusive or owning requirement receiver.
    private static bool Refused(BoundMemberPath? basePath, bool receives, BoundType? requirementReceiver)
        => basePath is not null && receives && requirementReceiver is not { Semantics: SemanticsKind.Ref };

    // SPEC 8.11.3: the witness a verified conformance of a Self to a Contract maps a requirement to, and the instance of the Type
    // that declares it (a base's for an inherited witness); null where none resolves.
    private BoundType? ConformanceWitness(BindingSymbol requirement, BoundType self, BindingSymbol contract, BindingScope scope, QuerySite site, out BoundWitness witness)
    {
        witness = default;
        return this.ResolveConformance(self, contract, scope, site, out var path) == ConstraintProof.Proven && path is { IsVerified: true } &&
            path.WitnessMap.TryGetValue(new(requirement, contract), out witness) && witness.Function is { ObjectCompatibility: ConstraintProof.Proven } function
            ? this.StoredType(function.DeclaringType, self) : null;
    }

    // SPEC 11.9.2: the verified operation a conformance maps a Property requirement's accessor to, and the instance of the Type that
    // implements it. A standard witness is a member of the conforming Type itself, so only an accessor call can be inherited (OCC-X as above).
    private RequirementWitness PropertyRequirementWitness(BoundAccessor requirement, BoundType self, BindingSymbol? contract, BindingScope scope, QuerySite site)
    {
        if (contract is null || this.ResolveConformance(self, contract, scope, site, out var path) != ConstraintProof.Proven || path is not { IsVerified: true } ||
            !path.PropertyWitnessMap.TryGetValue((new(requirement.Property.Symbol, contract), requirement.Kind), out var witness))
        {
            return default;
        }

        if (witness.ObjectCompatibility != ConstraintProof.Proven || this.StoredType(witness.ImplementationType, self) is not { } declaring)
        {
            return default;
        }

        return witness.Kind == PropertyWitnessKind.AccessorCall && Refused(witness.BasePath, witness.Implementation.Receiver is not null, witness.ReceiverType)
            ? new(RequirementWitnessKind.Refused, null) : new(RequirementWitnessKind.Property, null, declaring) { Path = path, Property = witness };
    }
}
