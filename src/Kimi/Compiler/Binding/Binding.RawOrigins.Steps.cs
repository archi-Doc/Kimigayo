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

// Raw Origins, steps (SPEC 5.7, 5.2.1 condition 3; DECISIONS BU-24; contract in Binding.RawOrigins.cs): the authority a raw layer's
// Origin can keep, which retention and the TyCtxt views read after Binding: by its kind (StepOf), a signature's layer (SignatureStep,
// SignatureLayerStep) and a layer an instance stores (StoredStep, its members StoredMembers, both over StoredLayer).
public sealed partial class Binding
{
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
    // Types are `inputs` (a parameter's or the result's, under a call's substitution or not; none for a Function Type's): Shared when some
    // input states its Origin and every statement of it in the inputs keeps only shared Loans (Statements: the outer Origin of a `ref`,
    // `objref` or Slice layer or of a pair layer admitting only those, an argument of a slot whose Loan requirement is `ref`), since a
    // pointer read from the layer then has no write authority; raw layers state no borrow, and borrows formed in a body never count. Any
    // other is its kind (StepOf).
    internal RawStep SignatureStep(BoundOrigin? raw, ReadOnlySpan<BoundType> inputs)
    {
        var kept = RawStep.Unstated;
        for (var i = 0; i < inputs.Length && kept != RawStep.Exclusive && raw is { Kind: OriginKind.Input or OriginKind.Parameter }; i++)
        {
            kept = this.Statements(inputs[i], raw, kept);
        }

        return kept == RawStep.Shared ? RawStep.Shared : StepOf(raw);
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
        return layer.RawOrigin is not { } origin || StepOf(origin) != RawStep.Exclusive ||
            this.RawMembers(origin, new ReadOnlySpan<BoundType>(in instance), members);
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

    // SignatureStep's reading of an input Type: the authority the statements of `origin` in it can keep, joined with `kept` (RawStep's order:
    // Unstated while none states it, Shared while each keeps only shared Loans, else Exclusive). A statement is a layer's outer Origin or a
    // slot argument; a raw layer's Origin states no borrow.
    private RawStep Statements(BoundType type, BoundOrigin origin, RawStep kept)
    {
        if (!type.CarriesOriginOrSlot || kept == RawStep.Exclusive)
        {
            return kept;
        }

        var pair = TryPairLayer(type, out _, out _);
        if (Names(pair ? this.OuterOrigin(type) : type.Origin, origin))
        {
            var shared = type.Kind == BoundTypeKind.Slice || type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.ObjRef } ||
                (pair && this.LayerAdmitted(type) is not SemanticsMask.None and var admitted && (admitted & ~(SemanticsMask.Ref | SemanticsMask.ObjRef)) == 0);
            kept = Max(kept, shared ? RawStep.Shared : RawStep.Exclusive);
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (Names(type.OriginArguments[i], origin))
            {
                var shared = type.Symbol?.Schema is { } schema && i < schema.Origins.Count && schema.Origins[i].LoanRequirement == LoanRequirement.Ref;
                kept = Max(kept, shared ? RawStep.Shared : RawStep.Exclusive);
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            kept = this.Statements(type.Components[i], origin, kept);
        }

        return kept;

        static RawStep Max(RawStep a, RawStep b) => a < b ? b : a;
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
        return ReferenceTypes.IsPointer(declared) && ReferenceTypes.IsPointer(stored)
            ? declared! : throw new InvalidOperationException($"No raw layer is stored at part {part} of `{instance}` (InternalInvariant).");
    }
}
