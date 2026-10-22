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
// - Responsibility: the raw Origin formed through a borrow (Formed: the label, or its shared view Shared(L) when the borrow is shared and
//   the label's own step is not), the step of a raw Origin (StepOf), the region one names (RegionOf), a substituted one (Restep, G1:
//   it keeps the step of the Origin it replaces), the Origin a `during self` layer is read with at a Place (ReadHolder), a layer's
//   OwnedOrigins member (OwnedOrigin), the relations with an unstated end (UnstatedRelation, rulings S4), the allowlist of a
//   `during self` layer's Place reached through no dereference (HolderPlaceFailure, OQ-R1 (a)), the borrows keeping a declared slot
//   (Keepers, D15), the step published to retention and A5 (PublishedStep, TyCtxt.RawStep), and a Type with every raw layer's Origin
//   dropped (WithoutRawOrigins): an instance's stored Types (Instance, design-final §15) and, as the D5 exception (the owner's
//   Q-O3, DECISIONS BU-24), the old analysis's view at its Type intake, removed with it at R6-check.
// - Inputs: bound Types; a signature's input Types and a declaration's schema after the requirement fixed point (StepOf); PathOrigin.
// - Outputs: raw Origins, steps, relation verdicts and the OriginRelation failure facts of the allowlist.
// - Invariants: Shared(L) is interned per L and occurs only as a raw layer's Origin; Holder only in declared Field and payload Types,
//   which every read replaces, never in an instance's stored Types; Unstated only in relations and their records, never in a Type;
//   RegionOf maps Shared(L) to L before any relation is formed, so no premise, obligation or row names Shared, Holder or Unstated
//   (design-final §13 A2). A published raw Origin always
//   has a step: a declaration's Origin StepOf cannot classify is rejected once the slots' requirements exist and its layer published
//   unstated (RejectRawOrigins), and a mixed pair follow states its shared view (FollowStep). PublishedStep's throw is unreachable,
//   and Reached records InternalInvariant_Kd at the dereference.
// - Prohibitions: no region, Loan or retention facts, no Place walk of its own (PathOrigin is PlaceAuthority's), no message text.
public sealed partial class Binding
{
    private readonly Dictionary<BoundOrigin, BoundOrigin> sharedOrigins = new(ReferenceEqualityComparer.Instance);

    // SPEC 5.7 (D15): the borrows keeping each declared Origin slot, per declaration and pass (Keepers).
    private readonly Dictionary<OriginParameter, KeeperList> keepers = new(ReferenceEqualityComparer.Instance);

    // SPEC 5.7: the Phantom slot a raw layer states, recorded only when the check fails.
    private Dictionary<Koto, OriginParameter>? phantomRawOrigins;

    // The region a raw Origin names: L for Shared(L), the Origin itself otherwise.
    internal static BoundOrigin? RegionOf(BoundOrigin? origin) => origin?.Kind == OriginKind.Shared ? origin.Operands[0] : origin;

    // SPEC 5.7, design-final §4 (DECISIONS BU-24, R4-M105): the step of a raw Origin, read by the atom's binder. An input atom of a
    // signature or a universal name joins the outer layers of the inputs it is the Origin of and the requirements of the aggregate
    // slots it binds in them (SlotsStep; Shared iff all are shared), and a projected slot gives its requirement; a declared slot gives
    // its requirement (ref Shared, uniq Exclusive) and a pair's `o` its
    // admitted cases; any other label, a meet included, is Exclusive, since every formation through a shared borrow wraps it in
    // Shared. Null for an atom no input borrow or slot holds, or one whose cases mix modes (U1), which formation rejects.
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
            default:
                return RawStep.Exclusive;
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
        var stored = AdtDef.IsStruct(instance) ? this.StoredType(adt!.Fields[part], instance)
            : adt?.Cases[part] is { } selected && positions.Length > 0 ? this.StoredType(selected.Payload[positions[0]], instance) : null;
        for (var i = AdtDef.IsStruct(instance) ? 0 : 1; i < positions.Length && stored is not null; i++)
        {
            stored = stored.Kind == BoundTypeKind.Tuple ? stored.Components[positions[i]] : stored.Kind == BoundTypeKind.FixedArray ? stored.Components[0] : null;
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

    // Whether a Type holds a `during self` layer in its inline storage.
    private static bool HoldsHolder(BoundType type)
    {
        if (type.RawOrigin?.Kind == OriginKind.Holder)
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count && type.CarriesRawOrigin && type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray; i++)
        {
            if (HoldsHolder(type.Components[i]))
            {
                return true;
            }
        }

        return false;
    }

    // A slot's Loan requirement as a step: ref Shared, uniq Exclusive; none for a Phantom slot.
    private static RawStep? RequirementStep(LoanRequirement requirement)
        => requirement switch { LoanRequirement.Ref => RawStep.Shared, LoanRequirement.Uniq => RawStep.Exclusive, _ => null };

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

    // SPEC 5.1, 5.7 (R4-M105, OQ-R1 (a)): what a write of `source` puts in a `during self` layer that `stored` is or holds inline, other
    // than the null literal: `source` itself at the layer, or the first such element at a layer's position of a Tuple or array literal,
    // recursively; any other source reaches the layers as a whole. Null when every layer receives null.
    private static Koto? NonNullAtLayers(BoundType stored, Koto source)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (stored.RawOrigin?.Kind == OriginKind.Holder)
        {
            return source is NullLiteralKoto ? null : source;
        }

        var elements = (stored.Kind, source) switch
        {
            (BoundTypeKind.Tuple, TupleLiteralKoto tuple) => tuple.Elements,
            (BoundTypeKind.FixedArray, ArrayLiteralKoto array) => array.Elements,
            _ => null,
        };
        for (var i = 0; elements is not null && i < elements.Count; i++)
        {
            var part = stored.Components[stored.Kind == BoundTypeKind.FixedArray ? 0 : i];
            if (HoldsHolder(part) && NonNullAtLayers(part, elements[i]) is { } written)
            {
                return written;
            }
        }

        return elements is null ? source : null;
    }

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

    // SPEC 5.7, Reading a Field: a `during self` layer read at `place` (a Field, or a Subject a payload is selected from in place)
    // states the Origin of the last borrow or raw pointer the path dereferences (PathOrigin), formed with that dereference's mode, the
    // special receiver's in a constructor or drop, or none through no dereference; null after a located Unsupported_Kd at `node` (U2).
    private BoundType? ReadHolder(BoundType type, Koto place, Koto node)
    {
        if (!type.CarriesRawOrigin || !HoldsHolder(type))
        {
            return type;
        }

        var path = this.PathOrigin(place);
        if (path.Dereferences is null)
        {
            this.Fail(node, BindingFailure.Unsupported);
            return null;
        }

        return this.ReplaceHolder(type, path.Origin is { } origin ? this.Formed(origin, path.Mode) : null);
    }

    private BoundType ReplaceHolder(BoundType type, BoundOrigin? image)
    {
        if (type.RawOrigin?.Kind == OriginKind.Holder)
        {
            return this.types.Pointer(type.Components[0], image);
        }

        if (!type.CarriesRawOrigin || type.Kind is not (BoundTypeKind.Tuple or BoundTypeKind.FixedArray))
        {
            return type;
        }

        var components = this.RentTypes(type.Components.Count);
        try
        {
            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.ReplaceHolder(type.Components[i], image);
            }

            return this.types.Rebuild(type, components.AsSpan(0, type.Components.Count), type.Origin, (BoundOrigin[])type.OriginArguments);
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    // SPEC 5.7 (DECISIONS R4-M105, OQ-R1 (a)): through no dereference, except through `self` in the constructors and drop of the Type
    // whose inline storage holds the layer, a `during self` layer's Place admits reads, shared borrows and writes of null only; an
    // enclosing Tuple or fixed-array Place's write is one of the layer's Place. Whether `access` to `place`, writing `source`, failed
    // at `at` (HolderWriteFailure); PathOrigin publishes the stored Field and the Place's stored Type.
    private bool HolderPlaceFailure(Koto place, HolderAccess access, Koto? source, Koto at)
        => this.PathOrigin(place) is { Dereferences: false, Field: { } field, Stored: { } stored } && HoldsHolder(stored) && Receiver(field.Left) is null &&
            this.HolderWriteFailure(stored, access, source, at);

    // `access` to storage whose Type `stored` is, or encloses, a `during self` layer reached through no dereference. A write whose value
    // puts null in every layer is admitted (NonNullAtLayers). Any other write, an update and an exclusive borrow of the layer's Place
    // record an Origin relation failure at `at` and form no relation: the holder's storage, which no Origin expression names, outlives
    // no Origin. Its longer end is the Origin of the value reaching the layer, none for an update or a borrow, which admit the Place's
    // read Type. A Move of the layer's Place, and an exclusive borrow of an enclosing one, are located Unsupported_Kd (PLAN G212).
    private bool HolderWriteFailure(BoundType stored, HolderAccess access, Koto? source, Koto at)
    {
        var written = access == HolderAccess.Write && source is not null ? NonNullAtLayers(stored, source) : null;
        if (access == HolderAccess.Write && written is null)
        {
            return false;
        }

        if (access == HolderAccess.Move || (access == HolderAccess.ExclusiveBorrow && stored.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray))
        {
            this.Fail(at, BindingFailure.Unsupported);
            return true;
        }

        var longer = RegionOf(written?.BoundType?.RawOrigin) ?? BoundOrigin.Unstated;
        var relation = new OriginRelationFact(at, longer, BoundOrigin.Holder, Equality: false, stored, Refuted: true);
        this.FailExplained(ref this.originRelations, at, BindingFailure.OriginRelation, relation);
        return true;
    }

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

    // SPEC 5.7, 15.3.5 (D15; design-final §4): during the requirement fixed point's visit of a declaration's Types (AccumulateRequirements),
    // a borrow layer `type` whose Origin is a slot of `schema` keeps that slot with its referent, and an Origin argument bound to such a
    // slot keeps it with the nested Type's slot `via`, whose keepers count as the slot's, substituted by `type` (Keepers). Kept per pass.
    private void Keep(DeclarationSchema schema, BoundOrigin origin, BoundType type, OriginParameter? via)
    {
        for (var i = 0; i < origin.Operands.Count && origin.Kind == OriginKind.Intersection; i++)
        {
            this.Keep(schema, origin.Operands[i], type, via);
        }

        var kept = via is null ? this.BorrowReferent(type, out _, out _) : type;
        for (var i = 0; i < schema.Origins.Count && kept is not null; i++)
        {
            if (!ReferenceEquals(schema.Origins[i].Origin, origin))
            {
                continue;
            }

            if (!this.keepers.TryGetValue(schema.Origins[i], out var list))
            {
                this.keepers.Add(schema.Origins[i], list = new());
            }

            if (list.Pass != this.originRequirementPass)
            {
                (list.Pass, list.Count) = (this.originRequirementPass, 0);
            }

            list.Add(new(kept, via));
        }
    }

    // The keepers of a declared slot in this pass (Keep): a borrow's referent, or a nested Type with the nested slot keeping it.
    private ReadOnlySpan<Keeper> Keepers(OriginParameter slot)
        => this.keepers.TryGetValue(slot, out var list) && list.Pass == this.originRequirementPass ? list.Items : default;

    // SPEC 5.7 (D17, D18; design-final §5): the Origin a raw layer's `during` writes, one atom: never a meet or Origin arguments.
    // `self` names the holding value (Holder) on a layer in inline storage, a Field's or Case payload's Type or a Tuple or fixed-array
    // element of one, and nothing elsewhere in storage; any other name binds as on any layer (BindOriginName, the special receiver
    // included), and a borrow's name is formed through that borrow. Whether it belongs to a borrow is judged once the signature's
    // requirements are complete (InvalidRawOrigin).
    private BoundOrigin? WrittenRawOrigin(TypeSemanticsKoto annotation, Koto use, BindingScope scope, TypePosition position)
    {
        var name = annotation.OriginExpression is null ? annotation.OriginName : (annotation.OriginExpression as IdentifierNameKoto)?.IdentifierName;
        if (name == "self" && (position == TypePosition.InstanceStorage || OriginOwner(annotation) is { Akind: KotoKind.EnumCase }))
        {
            if (!InlineStorage(annotation))
            {
                return this.FailOrigin(use);
            }

            if (annotation.OriginExpression is { } self)
            {
                self.BindingState = BindingState.Resolved;
                self.BoundOrigin = BoundOrigin.Holder;
            }

            return BoundOrigin.Holder;
        }

        if (annotation.OriginArguments is not null)
        {
            return this.FailOrigin(use);
        }

        var origin = annotation.OriginExpression is { } expression ? this.BindOrigin(expression, scope) : this.BindOriginName(name!, use, scope);
        if (origin is null || origin.Kind == OriginKind.Intersection)
        {
            return origin is null ? null : this.FailOrigin(use);
        }

        // D11: a borrow's name is formed with that borrow's mode (BindOriginName accepts no other carrier); a slot, a universal name
        // and static are their own steps.
        return name is not null && this.OriginCandidate(name, use, scope, out _, out var carrier, out _) && this.LayerStep(carrier) is { } mode
            ? this.Formed(origin, mode) : origin;

        static bool InlineStorage(Koto node)
        {
            while (node.Parent is ParenthesizedTypeKoto or TupleTypeKoto ||
                (node.Parent is FixedArrayTypeKoto array && ReferenceEquals(array.ElementType, node)))
            {
                node = node.Parent;
            }

            var field = node.Parent is VariableKoto variable && ReferenceEquals(variable.TypeKoto, node);
            return node.Parent is SyntaxFormKoto { Akind: KotoKind.EnumCase } || field;
        }
    }

    // SPEC 5.7 (design-final §7.3): right after the slots' requirements exist, a declaration's written raw Origin that StepOf cannot
    // classify (a Phantom slot, U1's case-dependent pair input, an atom no borrow holds) is rejected at its annotation (InvalidRawOrigin)
    // and never published: its layer is unstated in every Type written around it, up to the declared Type its symbol holds (Unpublish),
    // so no substitution or dereference re-steps it. A body's annotations are judged with the declarations' (ValidateOriginRequirements).
    private void RejectRawOrigins()
    {
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is TypeSemanticsKoto { BoundType: { RawOrigin: { } raw } layer } annotation && this.InvalidRawOrigin(annotation, raw))
            {
                this.Unpublish(annotation, layer, this.types.Pointer(layer.Components[0]));
            }
        }
    }

    // Replaces `from`, the Type of `node`, with `to` there and in each Type written around it (TyCtxt.Rebuild), up to the declaration
    // (a Field, parameter, function result or Case payload) whose symbol holds the declared Type.
    private void Unpublish(Koto node, BoundType from, BoundType to)
    {
        for (Koto? at = node; at is not null; at = at.Parent)
        {
            if (at.BoundSymbol is { } symbol && ReferenceEquals(symbol.Declaration, at) && ReferenceEquals(symbol.Type, from))
            {
                symbol.Type = to;
            }

            if (ReferenceEquals(at.BoundType, from))
            {
                at.BoundType = to;
            }
            else if (at.BoundType is { } enclosing && this.Replaced(enclosing, from, to) is { } rebuilt)
            {
                at.BoundType = rebuilt;
                (from, to) = (enclosing, rebuilt);
            }

            if (at is not (TypeKoto or GenericsKoto))
            {
                return;
            }
        }
    }

    // `type` with each component that is `from` replaced by `to` (TyCtxt.Rebuild); null when none is.
    private BoundType? Replaced(BoundType type, BoundType from, BoundType to)
    {
        var count = type.Components.Count;
        if (!type.Components.Contains(from))
        {
            return null;
        }

        var components = this.RentTypes(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                components[i] = ReferenceEquals(type.Components[i], from) ? to : type.Components[i];
            }

            return this.types.Rebuild(type, components.AsSpan(0, count), type.Origin, (BoundOrigin[])type.OriginArguments);
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    private BoundOrigin? FailOrigin(Koto use)
    {
        this.Fail(use, BindingFailure.InvalidOrigin);
        return null;
    }

    // SPEC 5.7 (design-final §5, U1): once the requirement fixed point is complete, a written raw Origin belongs to a borrow. A slot of a
    // Type it states must not be Phantom (InvalidOrigin with the slot); an Origin without a step, one that no input borrow or slot
    // holds, is invalid, and one whose admitted pair cases mix modes is a located Unsupported_Kd. Whether `node` failed.
    private bool InvalidRawOrigin(Koto node, BoundOrigin raw)
    {
        if (RegionOf(raw) is { Kind: OriginKind.Parameter, Slot: >= 0, Binder: DeclarationContainerKoto { BoundSymbol.Schema: { } schema } } slot &&
            slot.Slot < schema.Origins.Count && schema.Origins[slot.Slot].LoanRequirement == LoanRequirement.None)
        {
            this.FailExplained(ref this.phantomRawOrigins, node, BindingFailure.InvalidOrigin, schema.Origins[slot.Slot]);
            return true;
        }

        if (this.StepOf(raw) is not null)
        {
            return false;
        }

        var pair = raw is { Kind: OriginKind.Parameter, Slot: < 0 };
        for (var i = 0; raw.Binder is { } binder && i < InputCount(binder) && !pair; i++)
        {
            pair = this.BoundInputType(binder, i) is { } input && TryPairLayer(input, out _, out _) && ReferenceEquals(this.OuterOrigin(input), raw);
        }

        this.Fail(node, pair ? BindingFailure.Unsupported : BindingFailure.InvalidOrigin);
        return true;
    }

    // A slot's keeper: the referent of a borrow keeping it, or (Via) a nested Type whose slot Via keeps it.
    private readonly record struct Keeper(BoundType Type, OriginParameter? Via);

    // The accesses of a `during self` layer's Place the allowlist decides (HolderPlaceFailure).
    private enum HolderAccess : byte
    {
        Write,
        Update,
        ExclusiveBorrow,
        Move,
    }

    // A slot's keepers in one pass, without duplicates; reused across passes (a warm pass allocates nothing).
    private sealed class KeeperList
    {
        private Keeper[] items = new Keeper[2];

        internal uint Pass { get; set; }

        internal int Count { get; set; }

        internal ReadOnlySpan<Keeper> Items => this.items.AsSpan(0, this.Count);

        internal void Add(Keeper keeper)
        {
            if (this.Items.Contains(keeper))
            {
                return;
            }

            if (this.Count == this.items.Length)
            {
                Array.Resize(ref this.items, this.Count * 2);
            }

            this.items[this.Count++] = keeper;
        }
    }
}
