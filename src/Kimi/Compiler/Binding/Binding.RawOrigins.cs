// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>What reaching storage through a raw layer's Origin is (SPEC 5.7; DECISIONS BU-24): the step <see cref="Binding.StepOf"/>
/// gives a raw Origin.</summary>
internal enum RawStep : byte
{
    /// <summary>The layer states no Origin: the pointer reaches what its value carries.</summary>
    Unstated,

    /// <summary>`during self` in a stored shape: an own step of the holding value.</summary>
    Own,

    /// <summary>`during static`: static storage.</summary>
    Static,

    /// <summary>A label reached through a shared borrow, or a slot whose Loan requirement is ref: no write.</summary>
    Shared,

    /// <summary>A label reached through an exclusive borrow, the special receiver, or any other label (a Place or local region, a meet).</summary>
    Exclusive,
}

// Raw Origins (SPEC 5.7; design-final §4, §14; DECISIONS BU-24, R4-M105): the Origin a raw layer states, BoundType.RawOrigin.
// - Responsibility: the written raw Origin in every position, formed with the step its written form gives and judged where the slots'
//   requirements exist (WrittenRawOrigin, InvalidRawOrigin; a declaration's by RejectRawOrigins), the raw Origin formed through a
//   borrow (Formed: the label, or its shared view Shared(L) when the borrow is shared and
//   the label's own step is not), the step of a raw Origin (StepOf), the region one names (RegionOf), a substituted one (Restep, G1:
//   it keeps the step of the Origin it replaces), the Origin a `during self` layer is read with at a Place (ReadHolder), a layer's
//   OwnedOrigins member (OwnedOrigin), the relations with an unstated end (UnstatedRelation, rulings S4), the allowlist of a
//   `during self` layer's Place reached through no dereference (HolderPlaceFailure, OQ-R1 (a)), the borrows keeping a declared slot
//   (Keepers, D15) and the limit on their cycles (RejectPolymorphicKeeperCycles), the step published to retention and A5
//   (PublishedStep, TyCtxt.RawStep; StoredStep for a stored layer), and a Type with every raw layer's Origin
//   dropped (WithoutRawOrigins): an instance's stored Types (Instance, design-final §15) and, as the D5 exception (the owner's
//   Q-O3, DECISIONS BU-24), the old analysis's view at its Type intake, removed with it at R6-check.
// - Inputs: bound Types; a signature's input Types and a declaration's schema after the requirement fixed point (StepOf); PathOrigin.
// - Outputs: raw Origins, steps, relation verdicts and the OriginRelation failure facts of the allowlist.
// - Invariants: Shared(L) is interned per L and occurs only as a raw layer's Origin; Holder only in declared Field and payload Types,
//   which every read replaces, never in an instance's stored Types; Unstated only in relations and their records, never in a Type;
//   RegionOf maps Shared(L) to L before any relation is formed, so no premise, obligation or row names Shared, Holder or Unstated
//   (design-final §13 A2). A published raw Origin always has a step: a written Origin StepOf cannot classify is rejected and its layer
//   published unstated, a declaration's once the slots' requirements exist (RejectRawOrigins), a body's at formation, after which no
//   requirement changes (AccumulateOrigin's check); a mixed pair follow states its shared view (FollowStep). PublishedStep's throw
//   is unreachable, and Reached records InternalInvariant_Kd at the dereference.
// - Files: this one (steps, formation through a borrow, substitution, regions, relations, the published steps); .Written.cs (the written
//   forms and their judgment); .Holder.cs (`during self` layers read and written at a Place); .Keepers.cs (slot keepers, cycle limit).
// - Prohibitions: no region, Loan or retention facts, no Place walk of its own (PathOrigin is PlaceAuthority's), no message text.
public sealed partial class Binding
{
    private readonly Dictionary<BoundOrigin, BoundOrigin> sharedOrigins = new(ReferenceEqualityComparer.Instance);

    // The region a raw Origin names: L for Shared(L), the Origin itself otherwise.
    internal static BoundOrigin? RegionOf(BoundOrigin? origin) => origin?.Kind == OriginKind.Shared ? origin.Operands[0] : origin;

    // SPEC 5.7, design-final §4 (DECISIONS BU-24, R4-M105): the step of a raw Origin, read by the atom's binder. An input atom of a
    // signature or a universal name joins the outer layers of the inputs it is the Origin of and the requirements of the aggregate
    // slots it binds in them (SlotsStep; Shared iff all are shared), and a projected slot gives its requirement; a declared slot gives
    // its requirement (ref Shared, uniq Exclusive) and a pair's `o` its admitted cases. An inferred, meet, anchor or projection label
    // is Exclusive by Formed's invariant: every formation with a Shared step wraps its label in Shared (WrittenRawOrigin checks it for
    // written ones). Unbound, left by a reported error, is Unstated. Null for an atom no input borrow or slot holds, or one whose cases
    // mix modes (U1), which formation rejects; any other kind is a broken contract.
    internal RawStep? StepOf(BoundOrigin? origin)
    {
        switch (origin?.Kind)
        {
            case null:
                return RawStep.Unstated;
            case OriginKind.Holder:
                return RawStep.Own;
            case OriginKind.Static:
                return RawStep.Static;
            case OriginKind.Shared:
                return RawStep.Shared;
            case OriginKind.Input when origin.Occurrence?.BoundType?.Symbol?.Schema is { } schema && (uint)origin.TargetSlot < (uint)schema.Origins.Count:
                return RequirementStep(schema.Origins[origin.TargetSlot].LoanRequirement);
            case OriginKind.Parameter when origin.Occurrence is GenericParameterKoto { BoundSymbol: { WholeType: { } whole } target }:
                return CasesStep(this.AdmittedSemantics(whole, target.Scope));
            case OriginKind.Parameter when origin.Binder?.BoundSymbol?.Schema is { } declared && origin.Binder is DeclarationContainerKoto &&
                (uint)origin.Slot < (uint)declared.Origins.Count:
                return RequirementStep(declared.Origins[origin.Slot].LoanRequirement);
            case OriginKind.Input or OriginKind.Parameter when origin.Binder is { } binder:
                RawStep? joined = null;
                for (var i = 0; i < InputCount(binder); i++)
                {
                    if (this.BoundInputType(binder, i) is not { } input)
                    {
                        continue;
                    }

                    if (ReferenceEquals(this.OuterOrigin(input), origin))
                    {
                        if (this.LayerStep(input) is not { } step)
                        {
                            return null;
                        }

                        joined = joined == RawStep.Exclusive ? joined : step;
                    }

                    joined = SlotsStep(input, origin, joined);
                }

                return joined;
            case OriginKind.Inference or OriginKind.Intersection or OriginKind.Anchor or OriginKind.Projection:
                return RawStep.Exclusive;
            case OriginKind.Unbound:
                return RawStep.Unstated;
            case OriginKind.Input or OriginKind.Parameter:
                return null;
            default:
                throw new InvalidOperationException($"A raw layer states `{origin}`, an Origin of kind {origin.Kind} (InternalInvariant).");
        }
    }

    // SPEC 5.4, 5.7: whether a conversion stating `claimed` is a fit: the premises prove that its operand's Origin, none for an
    // unstated pointer or an address, outlives the claimed one.
    internal bool ClaimFits(ConversionKoto claim, BoundOrigin claimed)
        => this.ProvesOriginOutlives(RegionOf(claim.Left.BoundType?.RawOrigin) ?? BoundOrigin.Unstated, RegionOf(claimed)!, claim);

    // A Type with every raw layer's Origin dropped: an instance's stored Types (Instance; design-final §15), and the D5 exception (the
    // owner's Q-O3, 2026-10-22; DECISIONS BU-24): the old analysis and Emission compare Types by reference, so they read each Type
    // through this view at their Type intake (OwnershipAnalysis.Place, OwnershipBody.Resolve); removed with them at R6-check.
    internal BoundType? WithoutRawOrigins(BoundType? type)
    {
        if (type is null || !type.CarriesRawOrigin)
        {
            return type;
        }

        var components = this.RentTypes(type.Components.Count);
        try
        {
            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.WithoutRawOrigins(type.Components[i])!;
            }

            var parts = components.AsSpan(0, type.Components.Count);
            return type.Kind == BoundTypeKind.Semantics && type.Semantics == SemanticsKind.Raw
                ? this.types.Pointer(parts[0]) : this.types.Rebuild(type, parts, type.Origin, (BoundOrigin[])type.OriginArguments);
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    // The step of a published raw Origin (design-final §7.3): what a rewriter's image keeps, what the retention walk follows and what
    // A5 reads (TyCtxt.RawStep). Total by the contract above; a raw Origin without one is a broken contract that ends the analysis,
    // since neither a rewriter nor a view has a node to locate it at.
    internal RawStep PublishedStep(BoundOrigin? raw)
        => this.StepOf(raw) ?? throw new InvalidOperationException($"The published raw Origin `{raw}` has no step (InternalInvariant).");

    // SPEC 5.7 (design-final §15): the step of a raw layer an instance stores, read from its declaration, since an instance's stored Types
    // state none: a struct's Field `part` or an enum's Case `part` (whose first position selects the payload's component) under the
    // instance's substitution (StoredType, which re-steps a slot and keeps `self`, the holder's own step), then the inline positions (a
    // Tuple's component, a fixed array's element) down to the layer. An Origin without a step (an Unbound slot argument) gives Unstated;
    // a path that ends at no raw layer is a broken contract.
    internal RawStep StoredStep(BoundType instance, int part, ReadOnlySpan<int> positions)
    {
        var adt = instance.Symbol?.Adt;
        var structure = AdtDef.IsStruct(instance);
        var stored = structure ? (uint)part < (uint)adt!.Fields.Length ? this.StoredType(adt.Fields[part], instance) : null
            : adt is not null && (uint)part < (uint)adt.Cases.Length && adt.Cases[part] is { } selected && positions.Length > 0 &&
                (uint)positions[0] < (uint)selected.Payload.Length ? this.StoredType(selected.Payload[positions[0]], instance) : null;
        for (var i = structure ? 0 : 1; i < positions.Length && stored is not null; i++)
        {
            stored = stored.Kind == BoundTypeKind.Tuple && (uint)positions[i] < (uint)stored.Components.Count ? stored.Components[positions[i]]
                : stored.Kind == BoundTypeKind.FixedArray ? stored.Components[0] : null;
        }

        return stored is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw } layer ? this.StepOf(layer.RawOrigin) ?? RawStep.Unstated
            : throw new InvalidOperationException($"No raw layer is stored at part {part} of `{instance}` (InternalInvariant).");
    }

    // SPEC 15.2.3, 5.7: a layer's member of OwnedOrigins: its Origin, or its raw Origin's region other than `self`.
    private static BoundOrigin? OwnedOrigin(BoundType type) => type.Origin ?? (type.RawOrigin is { Kind: not OriginKind.Holder } raw ? RegionOf(raw) : null);

    // SPEC 5.7 (rulings S4): a raw layer without `during` counts as stating an Origin every Origin outlives: `x outlives Unstated`
    // holds and `Unstated outlives x`, x another Origin, is Refuted; null for a relation without an Unstated end.
    private static bool? UnstatedRelation(BoundOrigin longer, BoundOrigin shorter) => (longer.Kind, shorter.Kind) switch
    {
        (_, OriginKind.Unstated) => true,
        (OriginKind.Unstated, _) => false,
        _ => null,
    };

    // Whether two Types are one Type but for the Origins their raw layers state, which a contract compares by subtyping (SPEC 15.3.7).
    private static bool SameExceptRawOrigins(BoundType a, BoundType b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Semantics == SemanticsKind.Raw && a.Kind == BoundTypeKind.Semantics
            ? b is not { Semantics: SemanticsKind.Raw, Kind: BoundTypeKind.Semantics }
            : !a.SameExceptOrigins(b) || !ReferenceEquals(a.Origin, b.Origin) || !a.OriginArguments.SequenceEqual(b.OriginArguments))
        {
            return false;
        }

        for (var i = 0; i < a.Components.Count; i++)
        {
            if (!SameExceptRawOrigins(a.Components[i], b.Components[i]))
            {
                return false;
            }
        }

        return true;
    }

    // A slot's Loan requirement as a step: ref Shared, uniq Exclusive; none for a Phantom slot.
    private static RawStep? RequirementStep(LoanRequirement requirement)
        => requirement switch { LoanRequirement.Ref => RawStep.Shared, LoanRequirement.Uniq => RawStep.Exclusive, _ => null };

    // The declared slot whose requirement steps an atom (StepOf's rows): an Input atom's target slot, a declaration's own slot.
    private static OriginParameter? SteppingSlot(BoundOrigin? atom) => atom switch
    {
        { Kind: OriginKind.Input, Occurrence.BoundType.Symbol.Schema: { } schema } when (uint)atom.TargetSlot < (uint)schema.Origins.Count
            => schema.Origins[atom.TargetSlot],
        { Kind: OriginKind.Parameter, Slot: >= 0, Binder: DeclarationContainerKoto { BoundSymbol.Schema: { } schema } } when atom.Slot < schema.Origins.Count
            => schema.Origins[atom.Slot],
        _ => null,
    };

    // `joined` with the requirement of each slot that `origin` binds as an Origin argument of a nominal Type within `type` (design-final
    // §4: an aggregate slot an input atom binds contributes its requirement; a Phantom slot contributes none).
    private static RawStep? SlotsStep(BoundType type, BoundOrigin origin, RawStep? joined)
    {
        for (var i = 0; i < type.Components.Count; i++)
        {
            joined = SlotsStep(type.Components[i], origin, joined);
        }

        for (var i = 0; type.Symbol?.Schema is { } schema && i < type.OriginArguments.Count && i < schema.Origins.Count; i++)
        {
            if (ReferenceEquals(type.OriginArguments[i], origin) && RequirementStep(schema.Origins[i].LoanRequirement) is { } step)
            {
                joined = joined == RawStep.Exclusive ? joined : step;
            }
        }

        return joined;
    }

    // The step of admitted Semantics cases that are all borrows of one mode; null when they mix modes or admit a non-borrow (U1).
    private static RawStep? CasesStep(SemanticsMask admitted) => admitted switch
    {
        SemanticsMask.None => null,
        _ when (admitted & ~(SemanticsMask.Ref | SemanticsMask.ObjRef)) == 0 => RawStep.Shared,
        _ when (admitted & ~(SemanticsMask.Uniq | SemanticsMask.ObjUniq)) == 0 => RawStep.Exclusive,
        _ => null,
    };

    // The mode a path is reached with through a pair layer whose admitted cases are all borrows (design-final §4): the weakest case's,
    // Shared when any case is a shared borrow; null when a case is no borrow (U2).
    private static RawStep? FollowStep(SemanticsMask admitted) => admitted switch
    {
        _ when CasesStep(admitted) is { } step => step,
        _ when (admitted & ~(SemanticsMask.Ref | SemanticsMask.ObjRef | SemanticsMask.Uniq | SemanticsMask.ObjUniq)) == 0 => RawStep.Shared,
        _ => null,
    };

    // The step of a borrow or pair layer (SPEC 5.7, D11): shared for ref and objref, exclusive for uniq and objuniq, and a pair layer's
    // when its admitted cases are borrows of one mode; null for any other layer.
    private RawStep? LayerStep(BoundType? layer) => layer switch
    {
        { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.ObjRef } => RawStep.Shared,
        { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq } => RawStep.Exclusive,
        _ when TryPairLayer(layer, out _, out _) => CasesStep(this.LayerAdmitted(layer!)),
        _ => null,
    };

    // SPEC 5.7 (D11; DECISIONS R4-M105): the raw Origin formed through a borrow of `mode` whose Origin is `label`: the label, or its
    // shared view when the borrow is shared and the label's own step is not. A meet is formed like any label.
    private BoundOrigin Formed(BoundOrigin label, RawStep mode)
    {
        if (mode != RawStep.Shared || label.Kind == OriginKind.Shared || this.StepOf(label) == RawStep.Shared)
        {
            return label;
        }

        if (!this.sharedOrigins.TryGetValue(label, out var shared))
        {
            this.sharedOrigins.Add(label, shared = new(OriginKind.Shared, name: "shared", operands: [label]));
        }

        return shared;
    }

    // G1 (design-final §4): a rewriter's image of a raw Origin, `image` being its own map of RegionOf(raw), keeps the step of the Origin
    // it replaces; `self` and `static` are unchanged.
    private BoundOrigin Restep(BoundOrigin raw, BoundOrigin image)
        => raw.Kind is OriginKind.Holder or OriginKind.Static || ReferenceEquals(image, RegionOf(raw)) ? raw : this.Formed(image, this.PublishedStep(raw));

    // SPEC 5.6, 5.7 (D14): the region the argument of a callee's Within parameter (KimiLibraryCatalog.Within) states before its fit to
    // the parameter, with that argument; null when the callee has no such parameter or the argument states none.
    private BoundOrigin? StatedWithin(FunctionKoto selected, ReadOnlySpan<BoundArgumentOperation> operations, out Koto argument)
    {
        argument = null!;
        var within = KimiLibraryCatalog.Within(selected.BoundSymbol);
        for (var i = 0; i < operations.Length && within >= 0; i++)
        {
            if (operations[i] is { ParameterIndex: var parameter, Source: { } source, AdaptedType.RawOrigin: { } stated } && parameter == within)
            {
                argument = source;
                return RegionOf(stated);
            }
        }

        return null;
    }
}
