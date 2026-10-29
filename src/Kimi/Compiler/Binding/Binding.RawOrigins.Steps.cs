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
    /// requirement is `ref` in every case its holder selects, or a signature's layer whose Origin only shared input borrows of that
    /// signature state.</summary>
    Shared,

    /// <summary>Any other Origin: a write through the pointer may be defined.</summary>
    Exclusive,
}

// Raw Origins, steps (SPEC 5.7, 5.2.1 condition 3; DECISIONS BU-24, BU-41, BU-46; contract in Binding.RawOrigins.cs): the authority a
// raw layer's Origin can keep, which retention and the TyCtxt views read after Binding: by its kind (StepOf), a signature's layer
// (SignatureStep) and a layer an instance stores (StoredStep and its members StoredMembers, both over StoredLayer, which reads the
// instance's kept part and, by WrittenAt, the Type its declaration writes there; the step from the layer its declaration states,
// DeclaredStep, which StoredStep and the hold walk read).
public sealed partial class Binding
{
    // SPEC 5.7, 5.2.1 condition 3 (DECISIONS BU-24; R4's constraint 2): the authority a raw layer's Origin can keep where no declaration
    // states the layer, by its kind: none Unstated, `self` Own, `static` Static, any other Exclusive, never Shared by default, since a
    // borrow formed anywhere may keep an exclusive Loan of what the Origin covers. Unbound, left by a reported error, is Unstated, as is
    // Unstated, a kept part's layer its declaration writes without `during` (DECISIONS BU-46).
    internal static RawStep StepOf(BoundOrigin? raw) => raw?.Kind switch
    {
        null or OriginKind.Unbound or OriginKind.Unstated => RawStep.Unstated,
        OriginKind.Holder => RawStep.Own,
        OriginKind.Static => RawStep.Static,
        _ => RawStep.Exclusive,
    };

    // SPEC 5.7 Calls ("for self, the holding value's Type"; the owner's Q2 (a)): what a `during self` layer of a stored part admits past
    // it besides its pointee, the one reading of it retention's storage walk and fits (Binding.Retention.Storage.cs) and A5's holding walk
    // (MirTypes.Holding, through TyCtxt) read: the holding instance's Type arguments by their actual Types (`arguments`), or, where the
    // instance has an Origin argument, the write fact instead (true): any value at each of them.
    internal static bool SelfAdmitted(BoundType? holder, out IReadOnlyList<BoundType> arguments)
    {
        var write = holder is { OriginArguments.Count: > 0 };
        arguments = write || holder is null ? [] : holder.Components;
        return write;
    }

    // SPEC 5.2.1 condition 3 (the owner's 「シグネチャ版も入れる (推奨)」): the step of a raw layer written in a signature whose input
    // Types are `inputs` (a parameter's or the result's, under a call's substitution or not; none for a Function Type's): Shared when some
    // input states its Origin and every statement of it in the inputs keeps only shared Loans (Statements: the outer Origin of a `ref`,
    // `objref` or Slice layer or of a pair layer admitting only those, an argument of a slot `ref` in every case its Type selects), since a
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

    // SPEC 5.2.1 condition 3: the step of the raw layer an instance stores at a part and inline positions (StoredLayer), by DeclaredStep.
    internal RawStep StoredStep(BoundType instance, int part, ReadOnlySpan<int> positions)
        => this.DeclaredStep(this.StoredLayer(instance, part, positions, out var declared), declared, instance);

    // RawMembers of the raw layer an instance stores (StoredLayer): its Origin under the instance's substitution, within the instance, so
    // its members are the keepers of the slots the instance binds it to; `self`, `static` and an unstated layer have none. False when the
    // walk is incomplete (RawMembers), and for a layer a Type slot's argument supplies whose Origin steps Exclusive: that Origin belongs to
    // the borrows of the scope that wrote the Type (an input's, a body local's), not to the instance's, and a call may store through it.
    internal bool StoredMembers(BoundType instance, int part, ReadOnlySpan<int> positions, List<RawMember> members)
    {
        var layer = this.StoredLayer(instance, part, positions, out var declared);
        return layer.RawOrigin is not { } origin || StepOf(origin) != RawStep.Exclusive ||
            (declared is not null && this.RawMembers(origin, new ReadOnlySpan<BoundType>(in instance), members));
    }

    // The declared slot a raw Origin names: an Input atom's target slot (a projection `x.s` whose slot argument is omitted) with the
    // Type of the input it projects (`instance`), a declaration's own slot (no instance: its declaration's cases); null for any other.
    private static OriginParameter? NamedSlot(BoundOrigin? atom, out BoundType? instance)
    {
        instance = atom?.Kind == OriginKind.Input ? atom.Occurrence?.BoundType : null;
        return atom switch
        {
            { Kind: OriginKind.Input, Occurrence.BoundType.Symbol.Schema: { } schema } when (uint)atom.TargetSlot < (uint)schema.Origins.Count
                => schema.Origins[atom.TargetSlot],
            { Kind: OriginKind.Parameter, Slot: >= 0, Binder: DeclarationContainerKoto { BoundSymbol.Schema: { } schema } }
                when atom.Slot < schema.Origins.Count => schema.Origins[atom.Slot],
            _ => null,
        };
    }

    // The component a Tuple's or fixed array's inline position selects; null for any other Type or none.
    private static BoundType? InlinePart(BoundType? type, int position)
        => type is { Kind: BoundTypeKind.Tuple } && (uint)position < (uint)type.Components.Count ? type.Components[position]
            : type is { Kind: BoundTypeKind.FixedArray } ? type.Components[0] : null;

    // The Type an instance's declaration writes (WrittenType) at a struct's Field `part`, or at the payload an enum's Case `part` selects
    // by the first position, then at the inline positions below it, followed through the Tuples and fixed arrays it writes only
    // (InlinePart): null past any other Type (a Type parameter, a pair layer), and where the declaration writes no such part (`writes`
    // false). It reads the declaration only, never an instance's kept part (StoredLayer reads both).
    private static BoundType? WrittenAt(BoundType instance, int part, ReadOnlySpan<int> positions, out bool writes)
    {
        var adt = instance.Symbol?.Adt;
        var structure = AdtDef.IsStruct(instance);
        var written = WrittenType(structure ? (uint)part < (uint)adt!.Fields.Length ? adt.Fields[part] : null
            : adt is not null && (uint)part < (uint)adt.Cases.Length && adt.Cases[part] is { } selected && positions.Length > 0 &&
                (uint)positions[0] < (uint)selected.Payload.Length ? selected.Payload[positions[0]] : null);
        writes = written is not null;
        for (var i = structure ? 0 : 1; i < positions.Length && written is not null; i++)
        {
            written = InlinePart(written, positions[i]);
        }

        return written;
    }

    // Whether an Origin is `origin` or a meet with it as an operand.
    private static bool Names(BoundOrigin? stated, BoundOrigin origin)
        => ReferenceEquals(stated, origin) || (stated?.Kind == OriginKind.Intersection && stated.Operands.Contains(origin));

    // SPEC 5.2.1 condition 3 (the owner's 「Loan 要求で上限 (推奨)」; design-final §15): the step of a raw layer an instance stores (`layer`,
    // in its kept part), capped by the layer its declaration states there (`declared`), whose slot the substitution replaces: Shared when
    // the declaration states the layer with a slot whose Loan requirement is `ref` in every case the holding instance (`holder`) selects
    // (SPEC 15.3.5, DECISIONS BU-44), since a pointer read from it has read authority only; any other is the kind of its Origin under the
    // instance's substitution (`self` the holder's own step, an Unbound slot argument Unstated). A layer a Type argument supplies
    // (`declared` null) is no layer the declaration states, so no cap reads it: it has the kind of the Origin the argument states, as any
    // layer no declaration states (a pair slot's argument states none, TyCtxt.PairBinding). StoredStep and the hold walk (Held) read this
    // one rule.
    private RawStep DeclaredStep(BoundType layer, BoundType? declared, BoundType? holder)
        => NamedSlot(declared?.RawOrigin, out _) is { } slot && this.RequirementAt(slot, holder).Strongest == LoanRequirement.Ref
            ? RawStep.Shared : StepOf(layer.RawOrigin);

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
                var shared = type.Symbol?.Schema is { } schema && i < schema.Origins.Count &&
                    this.RequirementAt(schema.Origins[i], type).Strongest == LoanRequirement.Ref;
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
    // component, then the inline positions (a Tuple's component, a fixed array's element), in the instance's kept part (Instance: its
    // substitution with the raw Origins it gives) where its declaration writes the part, and as `declared` the raw layer its declaration
    // states there (WrittenAt): null where a Type argument supplies the layer, the declaration stating another Type (a Type parameter, a
    // pair layer) at the path or above it. A path whose kept part is no raw layer, a Case's that did not substitute (null; DECISIONS
    // BU-37) included, is a broken contract.
    private BoundType StoredLayer(BoundType instance, int part, ReadOnlySpan<int> positions, out BoundType? declared)
    {
        var written = WrittenAt(instance, part, positions, out var writes);
        var stored = writes ? this.Instance(instance).Parts[part] : null;
        for (var i = 0; i < positions.Length && stored is not null; i++)
        {
            stored = InlinePart(stored, positions[i]);
        }

        declared = ReferenceTypes.IsPointer(written) ? written : null;
        return ReferenceTypes.IsPointer(stored)
            ? stored! : throw new InvalidOperationException($"No raw layer is stored at part {part} of `{instance}` (InternalInvariant).");
    }
}
