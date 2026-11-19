// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 10.8, 15.3.6: a known call signature as evidence for an outer candidate's slots. Binder quantifies the signature's own Origins
// (the Function Item or closure declaration, or the Function Type's own syntax); Source is the argument that supplied it.
internal readonly record struct SignatureEvidence(Koto? Binder, Koto Source);

public sealed partial class Binding
{
    // The key of a call's own open region for the callee's i-th Origin, apart from the keys of argument evidence.
    private const int UnsolvedKey = int.MinValue / 2;

    // The top of the keys of a call's regions for its Type slots' Origins (SlotRegion), below UnsolvedKey / 2.
    private const int SlotRegionKey = (UnsolvedKey / 2) - 1;

    // J2: the polarity of a position inside a Function Type (SlotComponentPolarity), beside 1, -1 and 0.
    private const int ContractPolarity = 2;

    // One open region per argument and per Origin the argument's known call signature quantifies (Input slot, or -1 - Parameter slot).
    private readonly OpenOriginRows openOrigins;

    // SPEC 15.3.6: a selected call whose slot only an argument's own per-call Origin would satisfy, with that argument and the slot.
    private Dictionary<Koto, PerCallOriginFact>? perCallOrigins;

    // The argument whose own per-call Origin alone would satisfy a slot of the selected callee, and that callee's first Type parameter
    // whose solution holds the argument's open region (-1 when none does, displayed as the Type argument).
    private readonly record struct PerCallOriginFact(Koto Argument, FunctionKoto Selected, int Slot);

    // SPEC 10.8: an Origin that the evidence's own binder quantifies, such as a per-call input, never becomes the solution of an Origin
    // inside a slot. The slot keeps the structure and Semantics, and each such Origin is replaced by an open region of the call (SPEC
    // 15.3.6): a local region that holds no Loans, is never displayed, and that other evidence for the slot fills.
    internal static bool HasOpenOrigin(BoundType type, bool evidenceOnly = false)
    {
        if (!type.CarriesOrigin)
        {
            return false;
        }

        if (type.Origin is { } origin && IsOpen(origin))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (IsOpen(type.OriginArguments[i]))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (HasOpenOrigin(type.Components[i], evidenceOnly))
            {
                return true;
            }
        }

        return false;

        bool IsOpen(BoundOrigin origin)
        {
            if (origin.Open && (!evidenceOnly || !origin.IsLocalInput))
            {
                return true;
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (origin.Operands[i].Open && (!evidenceOnly || !origin.Operands[i].IsLocalInput))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // An Origin that a known call signature's binder quantifies, or that a nested Function Type's own syntax binds per call.
    private static bool QuantifiedBy(BoundOrigin origin, Koto binder)
    {
        if (origin.Kind is OriginKind.Input or OriginKind.Parameter)
        {
            return ReferenceEquals(origin.Binder, binder) || origin.Binder is FunctionTypeKoto;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (QuantifiedBy(origin.Operands[i], binder))
            {
                return true;
            }
        }

        return false;
    }

    // Whether `type` holds an open region that the known call signature of `source` supplied.
    private static bool HasOpenOriginFrom(BoundType type, Koto source)
    {
        if (!type.CarriesOrigin)
        {
            return false;
        }

        if (From(type.Origin, source))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (From(type.OriginArguments[i], source))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (HasOpenOriginFrom(type.Components[i], source))
            {
                return true;
            }
        }

        return false;

        static bool From(BoundOrigin? origin, Koto source)
        {
            if (origin is null)
            {
                return false;
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (From(origin.Operands[i], source))
                {
                    return true;
                }
            }

            return origin.Open && ReferenceEquals(origin.Binder, source);
        }
    }

    // Whether an Origin is a call's own region (OpenUnsolvedOrigins), keyed from UnsolvedKey, far from every argument evidence key.
    private static bool IsCallRegion(BoundOrigin origin) => origin is { Kind: OriginKind.Inference, Open: true } && origin.Slot < UnsolvedKey / 2;

    // J2: whether an Origin is a call's region for a Type slot's Origin (SlotRegion's key range, the 2^28 keys down from
    // SlotRegionKey): the region of a solved variable, never an open position that later evidence fills (FillOpenOrigins). A call's
    // unsolved region (CallRegion), keyed from UnsolvedKey below that range, is not one: later evidence fills it (DECISIONS BU-104).
    private static bool IsSlotRegion(BoundOrigin origin) => IsCallRegion(origin) && origin.Slot is <= SlotRegionKey and > SlotRegionKey - (1 << 28);

    // DECISIONS BU-104 (twelfth amendment), SPEC 15.6.1: whether a declared Origin relation is a row of a call: an end is a call's
    // region, or a meet holding one. "Every relation of the call over [its region] is a body row: Binding neither judges nor drops
    // it", so the formation walk leaves it (CheckTypeOriginRelations) to the row the call's step 3 forms for the Type the call
    // instantiates: an input's (RequireInputPremises, JudgePositionRows) or its result's, a constructor's construction Type included
    // (VisitTypeClause, the other caller: RequireResultPremises, JudgeCallClauses). A Type derived from these holds the call's region
    // only through them and rests on their rows. Every other declared relation keeps the formation proof.
    private static bool CallRowRelation(BoundOrigin longer, BoundOrigin shorter) => HoldsCallRegion(longer) || HoldsCallRegion(shorter);

    private static bool HoldsCallRegion(BoundOrigin origin)
    {
        if (IsCallRegion(origin))
        {
            return true;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (HoldsCallRegion(origin.Operands[i]))
            {
                return true;
            }
        }

        return false;
    }

    // Whether `origin` is `part` or a meet holding it.
    private static bool HoldsOrigin(BoundOrigin origin, BoundOrigin part)
    {
        if (ReferenceEquals(origin, part))
        {
            return true;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (HoldsOrigin(origin.Operands[i], part))
            {
                return true;
            }
        }

        return false;
    }

    // Whether `source`, a bound of the solution `(E and region)`, is a bound of `region` of its own: neither an operand of the
    // solution (E outlives the meet by SPEC 15.3.6's law, which bounds nothing) nor an Origin holding the region.
    private static bool BoundsSolved(BoundOrigin source, BoundOrigin solution, BoundOrigin region)
    {
        for (var i = 0; i < solution.Operands.Count; i++)
        {
            if (ReferenceEquals(source, solution.Operands[i]))
            {
                return false;
            }
        }

        return !HoldsOrigin(source, region);
    }

    // J2: the polarity of Origin argument `index` of `type` at `polarity` (SlotComponentPolarity's), by its variance.
    private static int OriginArgumentPolarity(BoundType type, int index, int polarity, Binding binding)
        => polarity == ContractPolarity ? ContractPolarity : ComposePolarity(polarity, binding.OriginArgumentVariance(type, index, binding));

    // The polarity of a position of `variance` at `polarity` (1, -1 or 0), the one composition of an Origin solve's walks.
    private static int ComposePolarity(int polarity, OriginVariance variance)
        => variance == OriginVariance.Covariant ? polarity : variance == OriginVariance.Contravariant ? -polarity : 0;

    // Whether OpenSlotOrigins opens `origin`: a fixed Origin the evidence supplied (static, a parameter's or an input's, a local's
    // region, a borrow's), or a meet of such Origins.
    private static bool OpensAsSlotRegion(BoundOrigin origin)
    {
        switch (origin.Kind)
        {
            case OriginKind.Static or OriginKind.Parameter or OriginKind.Input or OriginKind.Inference or OriginKind.Projection:
                return !origin.Open;
            case OriginKind.Intersection:
                for (var i = 0; i < origin.Operands.Count; i++)
                {
                    if (!OpensAsSlotRegion(origin.Operands[i]))
                    {
                        return false;
                    }
                }

                return origin.Operands.Count != 0;
            default:
                return false;
        }
    }

    // Whether `type` names the Origin parameter `slot` of `binder` (EvidenceNames): a node of its Origin summary, a meet's operand included.
    private bool MentionsOriginSlot(BoundType type, Koto binder, int slot)
    {
        foreach (var origin in this.SummaryOf(type).Nodes)
        {
            if (origin.Kind == OriginKind.Parameter && ReferenceEquals(origin.Binder, binder) && origin.Slot == slot)
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 15.3.6, 15.6.4 steps 1, 3 (DECISIONS BU-104, superseding BU-51's region clause): after a candidate's solve, every Origin of
    // the callee without a candidate (no evidence, no equality, no result and no representative), whatever names it, a supplied
    // parameter's premise included, is a local region of the call, solved with the body's other local regions by its uses; the
    // premises the call's judgment visits over it are that region's bounds (a result-only `s` of `constant() -> ref/i32 during s`, one
    // only a default names, the Origins only a join's or an aggregate's `.None` leaves name). It needs no annotation and is never
    // replaced by static. It is the call's open region (CallRegion); the input Origins are opened too (an elided parameter Origin a
    // Never argument leaves without one, SPEC 10.8; PLAN G410). `origins` and `inputs` are the callee binder's slots, a Function
    // Type's none and its per-call inputs.
    private void OpenUnsolvedOrigins(Koto call, Span<BoundOrigin> origins, Span<BoundOrigin> inputs)
    {
        for (var i = 0; i < origins.Length; i++)
        {
            origins[i] ??= this.CallRegion(call, i);
        }

        for (var i = 0; i < inputs.Length; i++)
        {
            inputs[i] ??= this.CallRegion(call, -1 - i);
        }
    }

    // SPEC 15.3.6, 15.6.4 step 1: the region of `call` for a callee Origin no evidence supplies: Origin parameter `index` (from 0) or
    // input Origin `-1 - index`, or a Never `$tryWrite` writer's Origin argument `index` (BindTryWrite), or, keyed by the callee node of
    // a construction rather than its call, the constructed Type's own slot `index` (ConstructorType). Keyed apart from argument
    // evidence by UnsolvedKey, parameters up and inputs down, all below UnsolvedKey / 2, so IsCallRegion holds for every one.
    private BoundOrigin CallRegion(Koto call, int index) => this.OpenOrigin(call, UnsolvedKey + index);

    // SPEC 8.1.2, 10.8: the open region of a solve's node (a call, a function reference's use) for the outer Origin that an annotated
    // occurrence of pair slot `slot`'s original leaves to other evidence; keyed apart from argument evidence and from CallRegion.
    private BoundOrigin AnnotatedOuterRegion(Koto source, int slot) => this.OpenOrigin(source, (UnsolvedKey / 4) - slot);

    // J2 (SPEC 10.8, 15.3.6; the owner's O-358 (a); PLAN G936): the region of `call` for the `ordinal`-th Origin position of its callee's
    // Type slot `slot`'s solution (OpenSlotOrigins), keyed down from SlotRegionKey, between the CallRegion keys and UnsolvedKey / 2, so
    // IsCallRegion holds for every one; slots and positions past 2^14 share a region, which only joins their bounds.
    private BoundOrigin SlotRegion(Koto call, int slot, int ordinal) => this.OpenOrigin(call, SlotRegionKey - (((slot & 0x3FFF) << 14) | (ordinal & 0x3FFF)));

    // J2 (SlotSolution.OpenWaited): a Type slot's solution with each Origin E at a covariant position (`polarity` 1 at the root;
    // SlotComponentPolarity) that its evidence supplied written as SPEC 15.3.6's covariant candidate, the meet `(E and region)` of the
    // evidence's bound and the call's region for that position (SlotRegion), which stands for the bounds the waiting part gives after
    // selection; in pre-order (a Type's own Origin, its Origin arguments, then each component's), so every run of the candidate opens
    // the same regions, and E is kept beside its region in the candidate's openings (`opened`, OriginInference.Open). The meet is
    // the fixpoint of SPEC 10.8's covariant binding: a later run's evidence and its completed waiting part give it back, and it is
    // kept, as every meet holding an open region. A contravariant Origin's candidate is a lower bound and an invariant one's the
    // equality, which keep the evidence's binding. Kept also: an open region (already a region the body's solve decides: a call's,
    // an argument's per-call stand-in), an Origin that no substitution replaces (a raw anchor, a Constraint set's slot) and a meet
    // holding one, and a raw layer's Origin.
    private BoundType OpenSlotOrigins(BoundType type, Koto call, int slot, int polarity, ref int ordinal, OriginInference opened)
    {
        if (!type.CarriesOrigin || polarity != 1)
        {
            return type;
        }

        var origin = type.Origin is { } outer && OpensAsSlotRegion(outer) ? this.OpenSlotOrigin(outer, this.SlotRegion(call, slot, ordinal++), opened) : type.Origin;
        var components = this.RentTypes(type.Components.Count);
        var origins = this.originScratch.Rent(type.OriginArguments.Count);
        try
        {
            var changed = !ReferenceEquals(origin, type.Origin);
            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                var covariant = OriginArgumentPolarity(type, i, polarity, this) == 1;
                origins[i] = covariant && OpensAsSlotRegion(type.OriginArguments[i])
                    ? this.OpenSlotOrigin(type.OriginArguments[i], this.SlotRegion(call, slot, ordinal++), opened) : type.OriginArguments[i];
                changed |= !ReferenceEquals(origins[i], type.OriginArguments[i]);
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.OpenSlotOrigins(type.Components[i], call, slot, this.SlotComponentPolarity(type, i, polarity), ref ordinal, opened);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            return changed ? this.types.Rebuild(type, components.AsSpan(0, type.Components.Count), origin, origins.AsSpan(0, type.OriginArguments.Count)) : type;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
    }

    // An opened slot Origin: the evidence's bound, kept in the candidate's openings, met with its region.
    private BoundOrigin OpenSlotOrigin(BoundOrigin bound, BoundOrigin region, OriginInference opened) => this.Meet(opened.Open(region, bound), region);

    // J2: the polarity of component `index` of `type` at `polarity` (1 covariant, -1 contravariant, 0 invariant), as an Origin solve
    // collects it (CollectOriginInference: an invariant layer 0, a Constructed Type's slot by its schema), except that inside a Function
    // Type it is ContractPolarity: a fit compares its Origins as a contract (SPEC 15.3.7: the required Origins are rigid), never by
    // 15.3.6's rows. Shared by the solution's walk (OpenSlotOrigins) and the positions' (CollectPositionNames).
    private int SlotComponentPolarity(BoundType type, int index, int polarity)
    {
        if (polarity == ContractPolarity || type.Kind == BoundTypeKind.Function)
        {
            return ContractPolarity;
        }

        if (IsInvariantLayer(type, this, null, default))
        {
            return 0;
        }

        return type.Kind == BoundTypeKind.Constructed && type.Symbol?.Schema is { } schema ? ComposePolarity(polarity, schema.GenericSlots[index].OriginVariance) : polarity;
    }

    private BoundOrigin OpenOrigin(Koto source, int key, bool localInput = false)
    {
        if (!this.openOrigins.TryGetValue((source, key), out var origin))
        {
            origin = new(OriginKind.Inference, source, key) { Open = true, InputIndex = -1, IsLocalInput = localInput, Body = this.formingBody };
            this.openOrigins.Add((source, key), origin);
        }

        return origin;
    }

    // The evidence Type with each Origin that `binder` quantifies replaced by the open region of `source`.
    private BoundType OpenKnownOrigins(BoundType type, Koto binder, Koto source)
    {
        if (!type.CarriesOrigin)
        {
            return type;
        }

        var origin = type.Origin is { } outer ? this.OpenKnownOrigin(outer, binder, source) : null;
        var components = this.RentTypes(type.Components.Count);
        var origins = this.originScratch.Rent(type.OriginArguments.Count);
        try
        {
            var changed = !ReferenceEquals(origin, type.Origin);
            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.OpenKnownOrigins(type.Components[i], binder, source);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                origins[i] = this.OpenKnownOrigin(type.OriginArguments[i], binder, source);
                changed |= !ReferenceEquals(origins[i], type.OriginArguments[i]);
            }

            return changed ? this.types.Rebuild(type, components.AsSpan(0, type.Components.Count), origin, origins.AsSpan(0, type.OriginArguments.Count)) : type;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
    }

    private BoundOrigin OpenKnownOrigin(BoundOrigin origin, Koto binder, Koto source)
    {
        if (origin.Kind is OriginKind.Input or OriginKind.Parameter && ReferenceEquals(origin.Binder, binder))
        {
            return this.OpenOrigin(source, origin.Kind == OriginKind.Input ? origin.Slot : -1 - origin.Slot);
        }

        if (origin.Kind == OriginKind.Intersection && origin.Operands.Count != 0)
        {
            var result = this.OpenKnownOrigin(origin.Operands[0], binder, source);
            for (var i = 1; i < origin.Operands.Count; i++)
            {
                result = this.Meet(result, this.OpenKnownOrigin(origin.Operands[i], binder, source));
            }

            return result;
        }

        return origin;
    }

    // SPEC 15.3.6, 10.8: after selection, a slot that only the argument's own per-call Origin would satisfy is MissingOriginBinding_Kd
    // at the call, whose Reason names the argument and the slot (ReportPerCallOrigin), with the argument related.
    private BoundType? FailPerCallOrigin(InvocationKoto call, FunctionKoto selected, ReadOnlySpan<BoundType?> slots, Koto argument)
    {
        var slot = -1;
        for (var g = 0; g < selected.GenericArguments.Count; g++)
        {
            if (slots[g] is { } solution && HasOpenOriginFrom(solution, argument))
            {
                slot = g;
                break;
            }
        }

        return this.FailExplained(ref this.perCallOrigins, call, BindingFailure.MissingOrigin, new PerCallOriginFact(argument, selected, slot));
    }

    // SPEC 15.3.6, 10.8: `type` with each open region that the known call signature of `source` put in place of its k-th per-call input
    // replaced by the outer Origin of the k-th input of `signature`, the required call signature at that parameter: the per-call Origin
    // that alone would satisfy the slot. Null when `type` holds no such open region. With `fixedOnly`, only a required input over a
    // fixed Origin, such as the callee's `x` of `(ref/i32 during x) -> T`, replaces its open region: SPEC 10.7 instantiates the
    // argument's per-call input to that Origin, which is then a solution of the slot, not a per-call Origin.
    private BoundType? PerCallStandIn(BoundType type, Koto source, BoundType signature, bool fixedOnly = false)
    {
        var replaced = this.ReplaceOpenOrigins(type, source, signature, fixedOnly);
        return ReferenceEquals(replaced, type) ? null : replaced;
    }

    // SPEC 10.7, 10.8: the slot solutions with each open region of `source` at a required input over a fixed Origin instantiated to that
    // Origin, once the fit holds with it (PerCallStandIn with `fixedOnly`).
    private void InstantiateOpenOrigins(SlotFrame frame, int count, Koto source, BoundType signature)
    {
        for (var g = 0; g < count; g++)
        {
            if (frame.Types[g] is { } solution)
            {
                frame.SetType(g, this.ReplaceOpenOrigins(solution, source, signature, true));
            }
        }
    }

    private BoundType ReplaceOpenOrigins(BoundType type, Koto source, BoundType signature, bool fixedOnly = false)
    {
        if (!type.CarriesOrigin)
        {
            return type;
        }

        var origin = type.Origin is { } outer ? this.ReplaceOpenOrigin(outer, source, signature, fixedOnly) : null;
        var components = this.RentTypes(type.Components.Count);
        var origins = this.originScratch.Rent(type.OriginArguments.Count);
        try
        {
            var changed = !ReferenceEquals(origin, type.Origin);
            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.ReplaceOpenOrigins(type.Components[i], source, signature, fixedOnly);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                origins[i] = this.ReplaceOpenOrigin(type.OriginArguments[i], source, signature, fixedOnly);
                changed |= !ReferenceEquals(origins[i], type.OriginArguments[i]);
            }

            return changed ? this.types.Rebuild(type, components.AsSpan(0, type.Components.Count), origin, origins.AsSpan(0, type.OriginArguments.Count)) : type;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
    }

    private BoundOrigin ReplaceOpenOrigin(BoundOrigin origin, Koto source, BoundType signature, bool fixedOnly)
    {
        if (origin.Open && ReferenceEquals(origin.Binder, source))
        {
            // A required input's own per-call Origin is the outer Origin of a direct borrow that the signature's binder
            // (BoundType.Binder) quantifies at that position (SPEC 8.10); any other Origin there is fixed.
            var inputs = signature.Components[0];
            return origin.Slot >= 0 && origin.Slot < inputs.Components.Count && inputs.Components[origin.Slot].Origin is { Open: false } perCall &&
                !(fixedOnly && perCall is { Kind: OriginKind.Input, Occurrence: null } && ReferenceEquals(perCall.Binder, signature.Binder) && perCall.Slot == origin.Slot) ? perCall : origin;
        }

        if (origin.Kind == OriginKind.Intersection && origin.Operands.Count != 0)
        {
            var result = this.ReplaceOpenOrigin(origin.Operands[0], source, signature, fixedOnly);
            for (var i = 1; i < origin.Operands.Count; i++)
            {
                result = this.Meet(result, this.ReplaceOpenOrigin(origin.Operands[i], source, signature, fixedOnly));
            }

            return result;
        }

        return origin;
    }

    // SPEC 10.8 ("another argument's Origin at an open position solves it"): the open regions that later evidence fills are an
    // argument's per-call stand-in and a call's unsolved region (CallRegion); a slot region (IsSlotRegion) stands for a solved
    // variable and is neither (J2).
    // `type` with each open region replaced by the Origin that `other` has at the same position, when that one is an ordinary Origin,
    // or also an open region of `other` (`takeOpen`), so that two open positions keep the earlier region. Unequal structure is left to
    // the caller's ordinary comparison.
    private BoundType FillOpenOrigins(BoundType type, BoundType other, bool takeOpen)
    {
        if (ReferenceEquals(type, other) || !type.CarriesOrigin || !other.CarriesOrigin || type.Kind != other.Kind || type.Components.Count != other.Components.Count ||
            type.OriginArguments.Count != other.OriginArguments.Count)
        {
            return type;
        }

        var origin = Fill(type.Origin, other.Origin, takeOpen);
        var components = this.RentTypes(type.Components.Count);
        var origins = this.originScratch.Rent(type.OriginArguments.Count);
        try
        {
            var changed = !ReferenceEquals(origin, type.Origin);
            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.FillOpenOrigins(type.Components[i], other.Components[i], takeOpen);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                origins[i] = Fill(type.OriginArguments[i], other.OriginArguments[i], takeOpen)!;
                changed |= !ReferenceEquals(origins[i], type.OriginArguments[i]);
            }

            return changed ? this.types.Rebuild(type, components.AsSpan(0, type.Components.Count), origin, origins.AsSpan(0, type.OriginArguments.Count)) : type;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }

        static BoundOrigin? Fill(BoundOrigin? origin, BoundOrigin? supplied, bool takeOpen)
            => origin is { Open: true } && !IsSlotRegion(origin) && supplied is not null && (supplied.Kind != OriginKind.Inference || (takeOpen && supplied.Open)) ? supplied : origin;
    }

    /// <summary>The `openOrigins` family's one accessor (DECISIONS BU-220): a kept interner, a source's open regions (Inference atoms whose
    /// slot is the key) in its module's NodeColumns at its SyntaxId, one KeyedRow of them, never reset; a renumbering moves the row with
    /// its source. Its members keep the names and the tuple key its readers use.</summary>
    private readonly struct OpenOriginRows(Binding binding)
    {
        internal bool TryGetValue((Koto Source, int Key) key, [NotNullWhen(true)] out BoundOrigin? origin)
            => (origin = key.Source.SyntaxOwner is { } table ? KeyedRow.Find(table.Hir.Families.OpenOrigins.Get(key.Source.SyntaxId), (OriginKind.Inference, key.Key), AtomKey, ref binding.originRowProbes) : null) is not null;

        internal void Add((Koto Source, int Key) key, BoundOrigin origin)
        {
            if (this.TryGetValue(key, out _))
            {
                throw new ArgumentException($"An open region is already interned at {key}.", nameof(key));
            }

            KeyedRow.Put(ref HirTables.Of(key.Source).Families.OpenOrigins.Ref(key.Source.SyntaxId), Interned(origin, key.Source, (OriginKind.Inference, key.Key)), AtomKey, ref binding.originRowProbes);
        }
    }
}
