// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the requirement dispatch facts in MIR terms (DECISIONS R5-M61, R5-M70; R5 package F 4.1, 4.5, S2-S4 X1, X16, X28): what a
// requirement call runs under a closed substitution (Witness), from the requirement-witness core (Binding.ResolveRequirementWitness, R5-M22)
// under the library root's premises and the Contract reference the call selected as its Self conforms to it (Binding.InstanceContract,
// shared with the CallPlan materializer). ResetDispatch discards the dispatch views' memos together at an edit: Witness's, Comparison's
// (TyCtxt.Comparison.cs) and Applies' (TyCtxt.Virtuals.cs).
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
// twin); no receiver projection is returned, since a base subobject is a layout prefix (Layout.Of, X1).
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

internal sealed partial class TyCtxt
{
    private readonly Dictionary<(BindingSymbol Requirement, BindingSymbol? Contract, Subst Subst), RequirementTarget> witnesses = new();

    // A witness instance's Types and Origins before interning, reused.
    private BoundType?[] witnessTypes = [];
    private BoundOrigin[] witnessOrigins = [];

    // SPEC 8.6.1, 8.11.3, 11.9.2, 22.1; IMPL 21.3.1: what a requirement call with the recorded Contract reference runs under a closed
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

    // SPEC 7.3, 8.11.3: what a closed requirement's function item runs: Witness of the requirement's call on the Item's Self through the
    // Contract reference it records (its Components), which InstanceContract matches as a call's; null for another function's Item.
    internal RequirementTarget? Witness(BoundType item)
        => item is { Kind: BoundTypeKind.FunctionItem, Symbol: { } requirement, Components: [var self, var reference] } &&
            CallPlan.KindOf(requirement) == CalleeKind.Requirement
            ? this.Witness(requirement, reference.Symbol, this.Synthesized(requirement, self, reference)) : null;

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
