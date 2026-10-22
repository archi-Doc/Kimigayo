// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The authority a raw layer's Origin can keep (SPEC 5.7, 5.2.1 condition 3; DECISIONS BU-24): an over-approximation that the
/// safe analyses read and no diagnostic reads (<see cref="Binding.StepOf"/>, <see cref="Binding.StoredStep"/>,
/// <see cref="Binding.SignatureStep"/>).</summary>
internal enum RawStep : byte
{
    /// <summary>The layer states no Origin: the pointer reaches what its value carries.</summary>
    Unstated,

    /// <summary>`during self` in a stored shape: an own step of the holding value.</summary>
    Own,

    /// <summary>`during static`: static storage.</summary>
    Static,

    /// <summary>A pointer read from the layer has read authority only: a Field's or Case payload's layer stating a slot whose Loan
    /// requirement is `ref`, or a signature's layer whose Origin only shared input borrows of that signature state.</summary>
    Shared,

    /// <summary>Any other Origin: a write through the pointer may be defined.</summary>
    Exclusive,
}

// Raw Origins (SPEC 5.7, 5.2.1 condition 3; DECISIONS BU-24, R4-M105): the Origin a raw layer states, BoundType.RawOrigin. It states which
// storage a pointer reaches and where a call may store through it; it grants no access and decides no access error (the owner,
// 2026-10-22: no access through a raw pointer is a compile-time error; condition 3 bounds it at run time).
// - Responsibility: the written raw Origin in every position, the atom its name binds (WrittenRawOrigin), judged once after the last
//   requirement fixed point (InvalidRawOrigin, from ValidateOriginRequirements: a Phantom slot, also through a written projection; an
//   atom no borrow holds; a pair input admitting a non-borrow case); the step of a raw layer (StepOf, by its Origin's kind; StoredStep,
//   a Field's or Case payload's layer; SignatureStep, a signature's layer) and the borrows its Origin belongs to (RawMembers), which
//   retention and A5 read, each slot keeper walked once, a keeper cycle that changes its Type arguments an incomplete answer
//   (CallRetention.Limit), never a silent stop; the Origin a `during self` layer is read with at a Place (ReadHolder); a layer's
//   OwnedOrigins member (OwnedOrigin); the relations with an unstated end (UnstatedRelation, rulings S4); the allowlist of a `during self`
//   layer's Place reached through no dereference (HolderPlaceFailure, OQ-R1 (a)); the borrows keeping a declared slot (Keepers, D15);
//   and a Type with every raw layer's Origin dropped (WithoutRawOrigins): an instance's stored Types (Instance, design-final §15) and,
//   as the D5 exception (the owner's Q-O3, DECISIONS BU-24), the old analysis's view at its Type intake, removed with it at R6-check.
// - Inputs: bound Types; a declaration's schema and keepers after the requirement fixed point; a signature's input Types; PathOrigin.
// - Outputs: raw Origins, steps, member borrows, relation verdicts and the OriginRelation failure facts of the allowlist.
// - Invariants: Holder only in declared Field and payload Types, which every read replaces, never in an instance's stored Types;
//   Unstated only in relations and their records, never in a Type, so no premise, obligation or row names Holder or Unstated
//   (design-final §13 A2). A formed raw Origin is the Origin it is formed with; every step is total and never Shared by default, and
//   only retention and the TyCtxt views read one, after Binding's last pass.
// - Files: this one (steps, relations, the Types without raw Origins); .Written.cs (the written forms and their judgment); .Holder.cs
//   (`during self` layers read and written at a Place); .Keepers.cs (member borrows and slot keepers).
// - Prohibitions: no region, Loan or retention facts beyond the member borrows, no Place walk of its own (PathOrigin is PlaceAuthority's),
//   no access verdict, no message text.
public sealed partial class Binding
{
    // The member borrows a step or a retention walk is reading (RawMembers), in stack order: each reader truncates to where it started.
    private readonly List<RawMember> rawMembers = new();

    // SPEC 5.7, 5.2.1 condition 3 (DECISIONS BU-24; R4's constraint 2): the authority a raw layer's Origin can keep where no declaration
    // states the layer, by its kind: none Unstated, `self` Own, `static` Static, any other Exclusive, never Shared by default, since a
    // borrow formed anywhere may keep an exclusive Loan of what the Origin covers. Unbound, left by a reported error, is Unstated.
    internal static RawStep StepOf(BoundOrigin? raw) => raw?.Kind switch
    {
        null or OriginKind.Unbound => RawStep.Unstated,
        OriginKind.Holder => RawStep.Own,
        OriginKind.Static => RawStep.Static,
        _ => RawStep.Exclusive,
    };

    // SPEC 5.2.1 condition 3 (the owner's 「シグネチャ版も入れる (推奨)」): the step of a raw layer written in a signature whose input
    // Types are `inputs` (a parameter's or the result's, under a call's substitution or not): Shared when some input states its Origin and
    // every statement of it in the inputs keeps only shared Loans (SharedStatements: the outer Origin of a `ref`, `objref` or Slice layer
    // or of a pair layer admitting only those, an argument of a slot whose Loan requirement is `ref`), since a pointer read from the layer
    // then has read authority only; raw layers state no borrow, and borrows formed in a body never count. Any other is its kind (StepOf).
    internal RawStep SignatureStep(BoundOrigin? raw, ReadOnlySpan<BoundType> inputs)
    {
        var (stated, shared) = (false, true);
        for (var i = 0; i < inputs.Length && shared && raw is { Kind: OriginKind.Input or OriginKind.Parameter }; i++)
        {
            this.SharedStatements(inputs[i], raw, ref stated, ref shared);
        }

        return stated && shared ? RawStep.Shared : StepOf(raw);
    }

    // SignatureStep of the raw layer a function's or accessor's signature states at `input` (Retention.Result for its result) and the
    // component `positions` below it, as it declares them (TyCtxt.Signature); a path that ends at no raw layer is a broken contract.
    internal RawStep SignatureLayerStep(BindingSymbol function, int input, ReadOnlySpan<int> positions)
    {
        _ = TyCtxt.Signature(function, null, 0, out var count);
        var inputs = this.RentTypes(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                inputs[i] = TyCtxt.Signature(function, null, i, out _)!;
            }

            var layer = TyCtxt.Signature(function, null, input, out _);
            for (var i = 0; i < positions.Length && layer is not null; i++)
            {
                layer = (uint)positions[i] < (uint)layer.Components.Count ? layer.Components[positions[i]] : null;
            }

            return layer is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw } ? this.SignatureStep(layer.RawOrigin, inputs.AsSpan(0, count))
                : throw new InvalidOperationException($"No raw layer is declared at input {input} of `{function.Name}` (InternalInvariant).");
        }
        finally
        {
            this.typeScratch.Return(inputs, clearArray: true);
        }
    }

    // SPEC 5.2.1 condition 3 (the owner's 「Loan 要求で上限 (推奨)」; design-final §15): the step of a raw layer an instance stores, read from
    // its declaration, since an instance's stored Types state none: Shared when the declaration states the layer with a slot whose Loan
    // requirement is `ref`, since a pointer read from it has read authority only; any other is the kind of its Origin under the instance's
    // substitution (`self` the holder's own step, an Unbound slot argument Unstated).
    internal RawStep StoredStep(BoundType instance, int part, ReadOnlySpan<int> positions)
    {
        var declared = this.StoredLayer(instance, part, positions, out var layer);
        return declared.RawOrigin is { Kind: OriginKind.Parameter } && NamedSlot(declared.RawOrigin) is { LoanRequirement: LoanRequirement.Ref }
            ? RawStep.Shared : StepOf(layer.RawOrigin);
    }

    // RawMembers of the raw layer an instance stores (StoredLayer): its Origin under the instance's substitution, within the instance, so
    // its members are the keepers of the slots the instance binds it to; `self`, `static` and an unstated layer have none. False when the
    // walk is incomplete (RawMembers).
    internal bool StoredMembers(BoundType instance, int part, ReadOnlySpan<int> positions, List<RawMember> members)
    {
        _ = this.StoredLayer(instance, part, positions, out var layer);
        return layer.RawOrigin is not { Kind: OriginKind.Input or OriginKind.Parameter or OriginKind.Inference or OriginKind.Intersection or OriginKind.Projection or OriginKind.Anchor } origin ||
            this.RawMembers(origin, new ReadOnlySpan<BoundType>(in instance), members);
    }

    // SPEC 5.4, 5.7: whether a conversion stating `claimed` is a fit: the premises prove that its operand's Origin, none for an
    // unstated pointer or an address, outlives the claimed one.
    internal bool ClaimFits(ConversionKoto claim, BoundOrigin claimed)
        => this.ProvesOriginOutlives(claim.Left.BoundType?.RawOrigin ?? BoundOrigin.Unstated, claimed, claim);

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

    // SPEC 15.2.3, 5.7: a layer's member of OwnedOrigins: its Origin, or its raw Origin other than `self`.
    private static BoundOrigin? OwnedOrigin(BoundType type) => type.Origin ?? (type.RawOrigin is { Kind: not OriginKind.Holder } raw ? raw : null);

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

    // The declared slot a raw Origin names: an Input atom's target slot (a projection `x.s` whose slot argument is omitted), a
    // declaration's own slot; null for any other Origin.
    private static OriginParameter? NamedSlot(BoundOrigin? atom) => atom switch
    {
        { Kind: OriginKind.Input, Occurrence.BoundType.Symbol.Schema: { } schema } when (uint)atom.TargetSlot < (uint)schema.Origins.Count
            => schema.Origins[atom.TargetSlot],
        { Kind: OriginKind.Parameter, Slot: >= 0, Binder: DeclarationContainerKoto { BoundSymbol.Schema: { } schema } } when atom.Slot < schema.Origins.Count
            => schema.Origins[atom.Slot],
        _ => null,
    };

    // The component a Tuple's or fixed array's inline position selects; null for any other Type.
    private static BoundType? InlinePart(BoundType type, int position)
        => type.Kind == BoundTypeKind.Tuple && (uint)position < (uint)type.Components.Count ? type.Components[position]
            : type.Kind == BoundTypeKind.FixedArray ? type.Components[0] : null;

    // Whether an Origin is `origin` or a meet with it as an operand.
    private static bool Names(BoundOrigin? stated, BoundOrigin origin)
        => ReferenceEquals(stated, origin) || (stated?.Kind == OriginKind.Intersection && stated.Operands.Contains(origin));

    // SignatureStep's reading of an input Type: whether it states `origin` as a layer's outer Origin or as a slot argument (a raw layer's
    // Origin states no borrow), and whether each such statement keeps only shared Loans.
    private void SharedStatements(BoundType type, BoundOrigin origin, ref bool stated, ref bool shared)
    {
        if (!type.CarriesOriginOrSlot)
        {
            return;
        }

        var pair = TryPairLayer(type, out _, out _);
        if (Names(pair ? this.OuterOrigin(type) : type.Origin, origin))
        {
            stated = true;
            shared &= type.Kind == BoundTypeKind.Slice || (type.Kind == BoundTypeKind.Semantics && type.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef) ||
                (pair && this.LayerAdmitted(type) is not SemanticsMask.None and var admitted && (admitted & ~(SemanticsMask.Ref | SemanticsMask.ObjRef)) == 0);
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (Names(type.OriginArguments[i], origin))
            {
                stated = true;
                shared &= type.Symbol?.Schema is { } schema && i < schema.Origins.Count && schema.Origins[i].LoanRequirement == LoanRequirement.Ref;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            this.SharedStatements(type.Components[i], origin, ref stated, ref shared);
        }
    }

    // The raw layer an instance stores at a struct's Field `part`, or an enum's Case `part` whose first position selects the payload's
    // component, then the inline positions (a Tuple's component, a fixed array's element): as its declaration states it, and as `layer`
    // under the instance's substitution (StoredType). A path that ends at no raw layer is a broken contract.
    private BoundType StoredLayer(BoundType instance, int part, ReadOnlySpan<int> positions, out BoundType layer)
    {
        var adt = instance.Symbol?.Adt;
        var structure = AdtDef.IsStruct(instance);
        Koto? declaration = structure ? (uint)part < (uint)adt!.Fields.Length ? adt.Fields[part] : null
            : adt is not null && (uint)part < (uint)adt.Cases.Length && adt.Cases[part] is { } selected && positions.Length > 0 &&
                (uint)positions[0] < (uint)selected.Payload.Length ? selected.Payload[positions[0]] : null;
        var declared = declaration?.BoundType ?? (declaration as PropertyKoto)?.BoundSymbol?.Type;
        var stored = declared is null ? null : this.StoredType(declared, instance);
        for (var i = structure ? 0 : 1; i < positions.Length && declared is not null && stored is not null; i++)
        {
            (declared, stored) = (InlinePart(declared, positions[i]), InlinePart(stored, positions[i]));
        }

        layer = stored!;
        return declared is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw } && stored is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw }
            ? declared : throw new InvalidOperationException($"No raw layer is stored at part {part} of `{instance}` (InternalInvariant).");
    }

    // SPEC 5.6, 5.7 (D14): the raw Origin the argument of a callee's Within parameter (KimiLibraryCatalog.Within) states before its fit to
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
                return stated;
            }
        }

        return null;
    }
}
