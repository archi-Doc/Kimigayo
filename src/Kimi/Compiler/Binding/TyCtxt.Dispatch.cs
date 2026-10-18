// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the dispatch facts in MIR terms (DECISIONS R5-M61, R5-M62, R5-M68; R5 package F 4.1, 4.3, 4.5, 4.6, S2-S4 X1, X16, X28): what a
// requirement call runs under a closed substitution (Witness), from the requirement-witness core (Binding.ResolveRequirementWitness, R5-M22)
// under the library root's premises and the Contract reference the call selected as its Self conforms to it (Binding.InstanceContract,
// shared with the CallPlan materializer); how a comparison runs on a Type (Comparison), by Binding's one composition rule
// (Binding.ComparisonKindOf, shared with the legacy plan) with its closed leaves from Witness; whether a virtual slot exists for a declaring
// instance (Applies), beside its struct's VTableDef.
// Comparison: given Equatable or Comparable, a Type and the mode (source operators, IEEE for floats, or the Contract's mapping, whose
// equals is NaN-reflexive), a ComparisonNode tree: Composite over its parts, Builtin, Witness (a closed leaf: Witness's Body under its
// declaring instance, Synthesized), Requirement (a leaf with an abstract part: the Contract's own requirement on the leaf in every instance,
// Synthesized on a Self) or None (no answer, a broken contract for a closed accepted Self that its reader records as InternalInvariant_Kd);
// null for a Limit (Witness's). One node per (contract, self, operators), discarded at an edit.
// Applies: given an original and the layer of a representative that declares it, true or false as Binding.VirtualApplicability proves it
// under the public premises, else null, which for a closed declaring Type is a broken contract its reader records as InternalInvariant_Kd;
// false makes the slot's entry null (VTableDef's consumer rule). One answer per (original, declaring), discarded at an edit.
// Input: a requirement, the Contract reference its call recorded (MirCallee.Contract; null for none) and the call's closed substitution
// (TyCtxt.Compose with a representative), whose Self is the conforming Type and whose declaring Type holds the instantiated bound
// reference (TyCtxt.SubstOf); or, for a compiler function's call of a user witness (K package 4.5 rows 2 and 6, a comparison's leaf) and a
// requirement's function item (rows 5, 12 and 13, Witness(item)), the requirement, the Contract reference and its call on a Self
// (Synthesized); OwnRequirement names a library Contract's requirement for such a call.
// Output: RequirementTarget(Kind, Target, Subst, Access), one per (requirement, contract, subst), kept across passes with no edit and
// discarded at an edit (ResetViews). Body: a function with a MIR body under its instance: a conformance witness (inherited ones included)
// under its declaring instance with the call's Types, lengths and its Origins in the call's terms (Binding.WitnessOrigin), an accessor-call
// witness's executing accessor under its implementing Type's instance, a fixed-array or integer Position member under its own slots.
// Builtin: a compiler function under the call's substitution, whose bound reference instantiates the requirement's signature. Storage: a
// standard property witness's implementing Field under the call's substitution, Access its operation (Copy, Borrow or Set; AccessorCall for
// every other kind). Refused: OCC-X. Limit: the conforming Self's storage does not complete (PrepareTypeStorage: nested past its depth
// bound). None: no answer, also for an abstract Self, which for a closed accepted Self is a broken contract that its reader records as
// InternalInvariant_Kd.
// Invariants: an answer is the CallPlan materializer's (InstantiateRequirementCall) for the same call in the same instance (TyCtxtTest's
// twin); a comparison of a closed Type has the legacy plan's leaves wherever that plan exists, which it does not for an inherited witness
// (ComparisonCompositionTest's twin); no receiver projection is returned, since a base subobject is a layout prefix (Layout.Of, X1).
// Must not: forward an abstract Self (the materializer's SPEC 8.15 rule), create a bridge or a CallPlan, write a diagnostic, HIR or pass
// state, or prove on an erased value.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>What a requirement call runs under a closed substitution (<see cref="TyCtxt.Witness(BindingSymbol, BindingSymbol?, Subst)"/>).</summary>
internal enum RequirementTargetKind : byte
{
    None,
    Body,
    Builtin,
    Storage,
    Refused,
    Limit,
}

/// <summary>What a requirement call runs: <see cref="Target"/> under <see cref="Subst"/>, the target's instance for Body and the call's own
/// substitution, which binds the conforming Self, for Builtin and Storage; <see cref="Access"/> a Storage witness's operation.</summary>
internal readonly record struct RequirementTarget(RequirementTargetKind Kind, BindingSymbol? Target, Subst? Subst, PropertyWitnessKind Access);

/// <summary>How a comparison composes a Type (<see cref="TyCtxt.Comparison"/>, <see cref="Binding.ComparisonKindOf"/>).</summary>
internal enum ComparisonKind : byte
{
    None,
    Composite,
    Builtin,
    Witness,
    Requirement,
}

/// <summary>A comparison of <see cref="Type"/>: elementwise over <see cref="Parts"/> (Composite), natively (Builtin), by the witness
/// <see cref="Target"/> under its declaring instance <see cref="Subst"/> (Witness) or by the Contract's requirement <see cref="Target"/> on
/// the Self <see cref="Subst"/> binds (Requirement); None has no answer.</summary>
internal sealed record ComparisonNode(BoundType Type, ComparisonKind Kind, BindingSymbol? Target, Subst? Subst, ComparisonNode[] Parts);

internal sealed partial class TyCtxt
{
    private readonly Dictionary<(BindingSymbol Requirement, BindingSymbol? Contract, Subst Subst), RequirementTarget> witnesses = new();
    private readonly Dictionary<(BindingSymbol Original, BoundType Declaring), bool?> applicability = new();
    private readonly Dictionary<(BindingSymbol Contract, BoundType Self, bool Operators), ComparisonNode?> comparisons = new();

    // A witness instance's Types and Origins before interning, reused.
    private BoundType?[] witnessTypes = [];
    private BoundOrigin[] witnessOrigins = [];

    // SPEC 8.6.1, 8.11.4, 11.9.2, 22.1; IMPL 21.3.1: what a requirement call with the recorded Contract reference runs under a closed
    // substitution.
    internal RequirementTarget Witness(BindingSymbol requirement, BindingSymbol? contract, Subst subst)
    {
        if (!this.witnesses.TryGetValue((requirement, contract, subst), out var target))
        {
            target = this.Resolve(requirement, contract, subst);
            this.witnesses.Add((requirement, contract, subst), target);
        }

        return target;
    }

    // SPEC 7.3, 8.11.4: what a closed requirement's function item runs: Witness of the requirement's call on the Item's Self through the
    // Contract reference it records (its Components), which InstanceContract matches as a call's; null for another function's Item.
    internal RequirementTarget? Witness(BoundType item)
        => item is { Kind: BoundTypeKind.FunctionItem, Symbol: { } requirement, Components: [var self, var reference] } &&
            CallPlan.KindOf(requirement) == CalleeKind.Requirement
            ? this.Witness(requirement, reference.Symbol, this.Synthesized(requirement, self, reference)) : null;

    // SPEC 6.4.3: whether an original's slot exists for the declaring instance, the layer of the representative that declares the original
    // (Binding.VirtualApplicability under the public premises): Proven is true and Refuted false.
    internal bool? Applies(BindingSymbol original, BoundType declaring)
    {
        if (!this.applicability.TryGetValue((original, declaring), out var applies))
        {
            applies = binding.VirtualApplicability(new((FunctionKoto)original.Declaration, declaring)) switch
            {
                ConstraintProof.Proven => true,
                ConstraintProof.Refuted => false,
                _ => null,
            };
            this.applicability.Add((original, declaring), applies);
        }

        return applies;
    }

    // SPEC 13.4, 13.4.1, 12.4.3: how a comparison of Self under Equatable or Comparable runs, as source operators or the Contract's mapping
    // compare it.
    internal ComparisonNode? Comparison(BindingSymbol contract, BoundType self, bool operators)
    {
        if (!this.comparisons.TryGetValue((contract, self, operators), out var node))
        {
            node = this.Compose(contract, self, operators);
            this.comparisons.Add((contract, self, operators), node);
        }

        return node;
    }

    // SPEC 22.10.1, 8.6.1: the one requirement a library Contract declares, which a compiler function runs on a user Type (BufferWriter's
    // reserve for an erased writer's destination); null for a Contract that declares none.
    internal BindingSymbol? OwnRequirement(KimiDeclarationId contract) => library.GetSymbol(contract)?.Contract?.OwnRequirement();

    private static RequirementTarget Body(BindingSymbol target, Subst subst) => new(RequirementTargetKind.Body, target, subst, default);

    private void ResetDispatch()
    {
        this.witnesses.Clear();
        this.applicability.Clear();
        this.comparisons.Clear();
    }

    // Binding's composition rule over the leaves: a closed one runs what the Contract's requirement on it runs (Witness's Body), one with an
    // abstract part that requirement on it in every instance.
    private ComparisonNode? Compose(BindingSymbol contract, BoundType self, bool operators)
    {
        var kind = Binding.ComparisonKindOf(self, contract.LibraryDeclaration, operators);
        if (kind == ComparisonKind.Composite)
        {
            var parts = new ComparisonNode[self.Components.Count];
            for (var i = 0; i < parts.Length; i++)
            {
                if (this.Comparison(contract, self.Components[i], operators) is not { } part)
                {
                    return null;
                }

                parts[i] = part;
            }

            return new(self, kind, null, null, parts);
        }

        if (kind == ComparisonKind.Builtin)
        {
            return new(self, kind, null, null, []);
        }

        var requirement = contract.Contract!.OwnRequirement()!; // Equatable's equals, Comparable's compare.
        var subst = this.Synthesized(requirement, self, contract.Type!);
        if (kind == ComparisonKind.Requirement)
        {
            return new(self, kind, requirement, subst, []);
        }

        return this.Witness(requirement, contract, subst) switch
        {
            { Kind: RequirementTargetKind.Limit } => null,
            { Kind: RequirementTargetKind.Body, Target: { } witness, Subst: { } instance }
                => new(self, kind, witness, this.Synthesized(witness, instance.Declaring, []), []),
            _ => new(self, ComparisonKind.None, null, null, []),
        };
    }

    private RequirementTarget Resolve(BindingSymbol requirement, BindingSymbol? contract, Subst subst)
    {
        if (subst.Self is not { HasAbstractPart: false } self)
        {
            return default;
        }

        if (!binding.PrepareTypeStorage(self))
        {
            return new(RequirementTargetKind.Limit, null, null, default);
        }

        var instance = binding.InstanceContract(contract, self, subst.Declaring);
        var witness = binding.ResolveRequirementWitness(requirement, self, instance, binding.EffectPremises(null));
        return witness.Kind switch
        {
            Binding.RequirementWitnessKind.Builtin => new(RequirementTargetKind.Builtin, witness.Target, subst, default),
            Binding.RequirementWitnessKind.Conformance => this.Conformance(requirement, witness, subst),
            Binding.RequirementWitnessKind.Property => this.Property(requirement, witness, subst),
            Binding.RequirementWitnessKind.FixedArrayEntry => this.FixedArrayEntry(witness.Target!, self, subst),
            Binding.RequirementWitnessKind.IntegerPosition => this.IntegerPosition(witness.Target!, self, subst),
            Binding.RequirementWitnessKind.Refused => new(RequirementTargetKind.Refused, null, null, default),
            _ => default,
        };
    }

    // SPEC 21.3.1: the witness at the call's Types and lengths under its declaring instance, its Origins in the call's terms
    // (InstantiateConformanceCall).
    private RequirementTarget Conformance(BindingSymbol requirement, in Binding.RequirementWitness witness, Subst subst)
    {
        var function = witness.Function!;
        var origins = Fit(ref this.witnessOrigins, function.Origins.Length + function.InputOrigins.Length);
        for (var i = 0; i < origins.Length; i++)
        {
            var origin = i < function.Origins.Length ? function.Origins[i] : function.InputOrigins[i - function.Origins.Length];
            origins[i] = binding.WitnessOrigin(origin, requirement.Declaration, subst.Origins, subst.Inputs);
        }

        var at = function.Origins.Length;
        return Body(witness.Target!, this.Intern(witness.Target!, null, witness.Declaring, subst.Types, subst.Lengths, origins[..at], origins[at..]));
    }

    // SPEC 11.9.2: an accessor-call witness's executing accessor under its implementing Type's instance, its Origins in the call's terms;
    // a standard witness's implementing Field (InstantiatePropertyRequirementCall).
    private RequirementTarget Property(BindingSymbol requirement, in Binding.RequirementWitness witness, Subst subst)
    {
        var property = witness.Property;
        if (property.Kind != PropertyWitnessKind.AccessorCall)
        {
            return new(RequirementTargetKind.Storage, property.Implementation.Property.Symbol, subst, property.Kind);
        }

        var binder = ((FunctionKoto)requirement.Declaration).Accessor!.Binder;
        var origins = Fit(ref this.witnessOrigins, property.Origins.Count + property.InputOrigins.Count);
        for (var i = 0; i < origins.Length; i++)
        {
            var origin = i < property.Origins.Count ? property.Origins[i] : property.InputOrigins[i - property.Origins.Count];
            origins[i] = binding.WitnessOrigin(origin, binder, subst.Origins, subst.Inputs);
        }

        var accessor = binding.AccessorFunction(property.Implementation).BoundSymbol!;
        var at = property.Origins.Count;
        return Body(accessor, this.Intern(accessor, null, witness.Declaring, [], [], origins[..at], origins[at..]));
    }

    // SPEC 21.3.1: the fixed array's entry member at the array's element Type and length (InstantiateFixedArrayEntry).
    private RequirementTarget FixedArrayEntry(BindingSymbol target, BoundType self, Subst subst)
    {
        if (!binding.RentFixedArrayArguments((FunctionKoto)target.Declaration, self, out var types, out var lengths, out var count))
        {
            return default;
        }

        try
        {
            return Body(target, this.Intern(target, null, null, types.AsSpan(0, count), lengths.AsSpan(0, count), subst.Origins, subst.Inputs));
        }
        finally
        {
            binding.ReturnFixedArrayArguments(types, lengths);
        }
    }

    // SPEC 4.6.1, 21.3.1: the integer Position witness at the integer Type (InstantiateIntegerPosition).
    private RequirementTarget IntegerPosition(BindingSymbol target, BoundType self, Subst subst)
    {
        if (binding.IntegerPositionSlot(target) is not (>= 0 and var slot))
        {
            return default;
        }

        var types = Fit(ref this.witnessTypes, slot + 1);
        types.Clear();
        types[slot] = self;
        return Body(target, this.Intern(target, null, null, types, [], subst.Origins, subst.Inputs));
    }
}
