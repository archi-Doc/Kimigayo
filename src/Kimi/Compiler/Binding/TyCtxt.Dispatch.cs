// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the requirement dispatch facts in MIR terms (DECISIONS R5-M61, R5-M70; R5 package F 4.1, 4.5, S2-S4 X1, X16, X28): what a
// requirement call runs under a closed substitution (Witness), from the requirement-witness core (Binding.ResolveRequirementWitness, R5-M22)
// under the library root's premises and the Contract reference the call selected as its Self conforms to it (Binding.InstanceContract,
// shared with the CallPlan materializer), and a built-in target's Contract by its lang item (ShimContract), the one read of what a shim
// runs, for Binding's comparison plans and the Backend's shim readers. ResetDispatch discards the dispatch views' memos together at an edit: Witness's, Comparison's
// (TyCtxt.Comparison.cs) and Applies' (TyCtxt.Virtuals.cs).
// Input: a requirement, the Contract reference its call recorded (MirCallee.Contract; null for none) and the call's closed substitution
// (TyCtxt.Compose with a representative), whose Self is the conforming Type and whose declaring Type holds the instantiated bound
// reference (TyCtxt.SubstOf); or, for a compiler function's call of a user witness (K package 4.5 rows 2 and 6, a comparison's leaf) and a
// requirement's function item (rows 5, 12 and 13, Witness(item)), the requirement, the Contract reference and its call on a Self
// (Synthesized); OwnRequirement names a library Contract's requirement for such a call.
// Output: RequirementTarget(Kind, Target, Subst, Access, Cause), one per (requirement, contract, subst), kept across passes with no edit and
// discarded at an edit (ResetViews). Body: a function with a MIR body under its instance: a conformance witness (inherited ones included)
// under its declaring instance with the call's Types, lengths and its Origins in the call's terms (Binding.WitnessOrigin), an accessor-call
// witness's executing accessor under its implementing Type's instance, a library group's function under its own slots.
// Builtin: a compiler function under the call's substitution, whose bound reference instantiates the requirement's signature. Storage: a
// standard property witness's implementing Field under the call's substitution, Access its operation (Copy, Borrow or Set; AccessorCall for
// every other kind). Refused: OCC-X. None: no answer, also for an abstract Self, which for a closed accepted Self is a broken contract that
// its reader records as InternalInvariant_Kd. What a call runs does not depend on its Self's storage, so Witness reads no storage verdict:
// a Self whose storage rests on a part Binding rejected or nests past Binding's bound has its witness's target, and the instance the
// target needs is its reader's to admit (DECISIONS BU-38, R7-M54 c4).
// Cause: the resolution's formation (TyCtxt.Formation), Formed but for a resolution that formed a Type past IMPL A.4's height inside the
// collector's answer frame (another conformance's formal, SPEC 8.6.1, or a conditional conformance's premise, 8.8.2): HeightLimit with no
// target, which the caller records at its requesting site (InstanceCollector.Formed).
// Invariants: an answer is the CallPlan materializer's (InstantiateRequirementCall) for the same call in the same instance, but for a
// resolution refused in the collector's frame, which the materializer, outside it, records itself (InstanceCollectorTest's K 4.5 rows,
// TyCtxtTest's direct Witness assertions, the native fixtures); no receiver projection is returned,
// since a base subobject is a layout prefix (Layout.Of, X1).
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
}

/// <summary>What a requirement call runs: <see cref="Target"/> under <see cref="Subst"/>, the target's instance for Body and the call's own
/// substitution, which binds the conforming Self, for Builtin and Storage; <see cref="Access"/> a Storage witness's operation;
/// <see cref="Cause"/> the resolution's formation (TyCtxt.Formation): HeightLimit, with no target, where the resolution formed a Type past
/// IMPL A.4's height in the collector's answer frame (Witness).</summary>
internal readonly record struct RequirementTarget(
    RequirementTargetKind Kind, BindingSymbol? Target, Subst? Subst, PropertyWitnessKind Access, TyCtxt.Formation Cause = TyCtxt.Formation.Formed);

internal sealed partial class TyCtxt
{
    private readonly Dictionary<(BindingSymbol Requirement, BindingSymbol? Contract, Subst Subst), RequirementTarget> witnesses = new();

    // A witness instance's Origins before interning, reused.
    private BoundOrigin[] witnessOrigins = [];

    // SPEC 8.6.1: the Contract that declares a requirement, also of a built-in target, which keeps its requirement's declaration and scope
    // (Binding.PrepareBuiltinTargets): the Contract whose mapping a comparison shim and whose operation an arithmetic shim runs, and whose
    // requirement a compiler function's call of a user witness names (DECISIONS R5-M126; PLAN G157 (16)).
    internal static BindingSymbol Contract(BindingSymbol requirement) => requirement.Scope.Owner is ContractKoto { BoundSymbol: { } contract }
        ? contract : throw new InvalidOperationException($"{requirement.Name} is not a requirement.");

    // SPEC 8.6.1: a shim's Contract (Binding.PrepareBuiltinTargets) by its lang item, which names what the shim runs: Equatable's or
    // Comparable's comparison, or an arithmetic Contract's operation (KimiLibraryCatalog.IsArithmeticContract); null for a function of
    // another disposition. Its readers are Binding's comparison plans (Binding.RequirementInstances' Witnesses) and the Backend's shim
    // targets (InstanceCollector.Witnesses' Shim), capability gaps (InstanceCollector.Capabilities) and bodies (FunctionCodegen.Shims).
    internal static KimiDeclarationId? ShimContract(BindingSymbol function)
        => function.Definition.Kind == DispositionKind.Shim ? Contract(function).LibraryDeclaration : null;

    // SPEC 8.6.1, 8.11.3, 11.9.2, 22.1; IMPL 21.3.1: what a requirement call with the recorded Contract reference runs under a closed
    // substitution.
    internal RequirementTarget Witness(BindingSymbol requirement, BindingSymbol? contract, Subst subst)
    {
        if (!this.witnesses.TryGetValue((requirement, contract, subst), out var target))
        {
            // R7-T1 c4: the resolution is a formation in the collector's answer frame (Binding.OpenFormation): it forms the conformances
            // of Self's Type to the Contract in declaration order up to the one naming the reference (SPEC 8.6.1; the only one by 8.9.1)
            // and the premises of the conformance it selects (8.8.2), so it can form a Type no site formed (another conformance's formal
            // or a conditional conformance's premise), whose refusal is the call's HeightLimit, recorded by its reader at the requesting
            // site; outside the frame it owns nothing.
            var formation = binding.OpenFormation();
            target = this.Resolve(requirement, contract, subst);
            if (binding.CloseAnswer(formation))
            {
                target = new(RequirementTargetKind.None, null, null, default, Formation.HeightLimit);
            }

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

    // The library declaration a compiler component reaches by its catalog identity (InstanceCollector: the destroyArray instance).
    internal BindingSymbol? Declaration(KimiDeclarationId id) => library.GetSymbol(id);

    // SPEC 17.4: the compiler-only $abort (KimiLibrary.Abort), which is no library declaration, so no MIR callee names it as one (MirValidator).
    internal BindingSymbol Abort => library.Abort;

    // A library declaration's own Type, each of its parameters standing for itself (Layout: a sequence handle's component, IMPL 21.1.3).
    internal BoundType SelfType(KimiDeclarationId id) => binding.SelfType(library.GetSymbol(id)!);

    // SPEC 21.3.1: a library group's witness at the Self's arguments (InstantiateLibraryWitness), the instance of the view's Body and of
    // A4's target (Binding.Witnesses), under the given Origins; null where the function's slots do not resolve.
    internal Subst? LibraryInstance(in Binding.RequirementWitness witness, BoundType self, ReadOnlySpan<BoundOrigin> origins, ReadOnlySpan<BoundOrigin> inputs)
    {
        var target = witness.Target!;
        if (!binding.RentWitnessArguments(witness.Group, target, self, out var types, out var lengths, out var count))
        {
            return null;
        }

        try
        {
            return this.Intern(target, null, null, types.AsSpan(0, count), lengths.AsSpan(0, count), origins, inputs);
        }
        finally
        {
            binding.ReturnWitnessArguments(types, lengths);
        }
    }

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

        var instance = binding.InstanceContract(contract, self, subst.Declaring, Binding.QuerySite.AfterPass);
        var witness = binding.ResolveRequirementWitness(requirement, self, instance, binding.EffectPremises(null), Binding.QuerySite.AfterPass);
        return witness.Kind switch
        {
            Binding.RequirementWitnessKind.Builtin => new(RequirementTargetKind.Builtin, witness.Target, subst, default),
            Binding.RequirementWitnessKind.Conformance => this.Conformance(requirement, witness, subst),
            Binding.RequirementWitnessKind.Property => this.Property(requirement, witness, subst),
            Binding.RequirementWitnessKind.LibraryWitness => this.LibraryWitness(witness, self, subst),
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

    // SPEC 21.3.1: a library group's witness at the Self's arguments (LibraryInstance).
    private RequirementTarget LibraryWitness(in Binding.RequirementWitness witness, BoundType self, Subst subst)
        => this.LibraryInstance(witness, self, subst.Origins, subst.Inputs) is { } instance ? Body(witness.Target!, instance) : default;
}
