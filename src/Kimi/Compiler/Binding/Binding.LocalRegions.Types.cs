// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The Types of locals over the local-region graph (SPEC 15.4.4): a mutable local's Type opened into region slots (LocalBorrowType), a
// result's own body's regions resolved to their sources (ResolveLocalResultOrigins), and the queries on the regions of a Type's parts
// (HasLocalRegion, which also counts a pending atom, HasRegionBounds, CarrierOf).
// - Interim (PLAN G758; the owner's O-168 (a), 2026-11-11; removal: G758 opens the slot as a region): a mutable unannotated local's pair
//   layer of Parameter kind (the original `s/T`, annotated or not) keeps the outer-Origin slot its initializer gave it, which SPEC 15.4.2
//   makes an inferred region, so a relation that fails on that slot where it was formed against that local itself is the analysis limit,
//   one located Unsupported_Kd at the relation's `At`, never its Language record: an assignment whose target is the local's own Place
//   (UnregionedPairSlot, read where every Origin relation record takes its node's failure, FailExplained), or a call relation whose
//   shorter end the local's own input fixed (the input EqualitySource or SlotEqualitySource names; JudgeCallPosition, which publishes
//   that input as FixedBy for an equality only). The predicate of both is NotedLocal. Notes are per pass.
public sealed partial class Binding
{
    // The interim's notes (PLAN G758): each mutable local and an unopened pair slot of its Type; per pass (ResetLocalRegions).
    private readonly List<(VariableKoto Local, BoundOrigin Slot)> unregionedPairSlots = new();

    // Whether an Origin of the Type's parts, a raw layer's included, is a local region or a pending atom; a Function or Function Item part
    // keeps its own, so no Origin summary answers it, and each distinct part is visited once per query (DECISIONS BU-19, BU-63).
    internal bool HasLocalRegion(BoundType type) => this.LocalRegionPart(type, this.visitedParts.Begin(this.storageVersion));

    // SPEC 15.6, 15.6.5: the Origin, read at a use, through which a value of a Type carries `origin`, judged without flow: one the Type
    // names (a meet operand included) or a local region with `origin` among its sources; null when the value does not carry it.
    internal BoundOrigin? CarrierOf(BoundType type, BoundOrigin origin, Koto use)
    {
        if (this.RegionCarrier(type.Origin, origin, use) is { } own)
        {
            return own;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (this.RegionCarrier(type.OriginArguments[i], origin, use) is { } argument)
            {
                return argument;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (this.CarrierOf(type.Components[i], origin, use) is { } part)
            {
                return part;
            }
        }

        return null;
    }

    // Whether an Origin of the Type's parts has region bounds; each distinct part is visited once per query (DECISIONS BU-19).
    internal bool HasRegionBounds(BoundType type) => this.RegionBoundsPart(type, this.visitedParts.Begin(this.storageVersion));

    // A pending atom (SPEC 15.6.5; DECISIONS R4-M126 §2.5) is fitted as a local region is: the fit checks the structure now and leaves
    // its Origin positions as obligations, judged once the Closure is classified.
    private static bool HasLocalRegion(BoundOrigin origin)
    {
        if (IsLocalRegion(origin) || PendingAtom(origin))
        {
            return true;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (HasLocalRegion(origin.Operands[i]))
            {
                return true;
            }
        }

        return false;
    }

    // An Origin of a Type read at a use, when it carries `origin` (CarrierOf).
    private BoundOrigin? RegionCarrier(BoundOrigin? expression, BoundOrigin origin, Koto use)
        => expression is not null && this.OriginAtUse(expression, use) is var read && this.RegionContains(read, origin) ? read : null;

    private bool LocalRegionPart(BoundType type, uint query)
    {
        if (!type.CarriesAnyOrigin || type.Kind is BoundTypeKind.Function or BoundTypeKind.FunctionItem)
        {
            return false;
        }

        if ((type.Origin ?? type.RawOrigin) is { } origin && HasLocalRegion(origin))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (HasLocalRegion(type.OriginArguments[i]))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (this.FirstVisit(type.Components[i], query) && this.LocalRegionPart(type.Components[i], query))
            {
                return true;
            }
        }

        return false;
    }

    private bool RegionBoundsPart(BoundType type, uint query)
    {
        if (!type.CarriesOrigin || type.Kind is BoundTypeKind.Function or BoundTypeKind.FunctionItem)
        {
            return false;
        }

        if (type.Origin is { } origin && this.HasRegionBounds(origin))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (this.HasRegionBounds(type.OriginArguments[i]))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (this.FirstVisit(type.Components[i], query) && this.RegionBoundsPart(type.Components[i], query))
            {
                return true;
            }
        }

        return false;
    }

    // Interim (PLAN G758; file contract): whether a failed relation is an assignment's whose target is a noted local's own Place and whose
    // shorter end is that local's slot (not a Place reached through it: `local@follow = v` relates the referent's slot, which the layer's
    // invariance fixes). The slot's atom alone decides nothing (design reviews 2 B4, 3 B7); a call's case is JudgeCallPosition's.
    private bool UnregionedPairSlot(Koto use, in OriginRelationFact recorded)
        => (use as BinaryKoto ?? Grouped(use).Parent as BinaryKoto) is { Akind: KotoKind.Equals } assignment &&
            this.NotedLocal(assignment.Left, recorded.Shorter);

    // Interim (PLAN G758): whether a node is a noted local's own Place, or an explicit borrow of it, whose slot is `slot`.
    private bool NotedLocal(Koto place, BoundOrigin slot)
    {
        if (this.unregionedPairSlots.Count == 0)
        {
            return false;
        }

        var own = KotoHelper.UnwrapParentheses(place);
        own = own is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } borrow ? KotoHelper.UnwrapParentheses(borrow.Left) : own;
        if (own is not IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local, Declaration: VariableKoto local } })
        {
            return false;
        }

        foreach (var noted in this.unregionedPairSlots)
        {
            if (ReferenceEquals(noted.Local, local) && ReferenceEquals(noted.Slot, slot))
            {
                return true;
            }
        }

        return false;
    }

    // Interim (PLAN G758): notes each pair layer of Parameter kind in a mutable local's Type, whose slot LocalBorrowType leaves unopened.
    private void NoteUnregionedPairSlots(BoundType type, VariableKoto owner)
    {
        if (!type.ContainsPairLayer)
        {
            return;
        }

        if (type.Kind == BoundTypeKind.Parameter)
        {
            if (TryPairLayer(type, out _, out _) && this.OuterOrigin(type) is { } slot)
            {
                this.unregionedPairSlots.Add((owner, slot));
            }

            return;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            this.NoteUnregionedPairSlots(type.Components[i], owner);
        }
    }

    // `slot` is the local's next region slot (BindVariableCore).
    private BoundType LocalBorrowType(BoundType type, VariableKoto owner, Koto value, int slot)
    {
        if (IsMutableDeclaration(owner))
        {
            this.NoteUnregionedPairSlots(type, owner);
        }

        if (!IsMutableDeclaration(owner) || !type.CarriesOrigin)
        {
            return type;
        }

        var result = Open(type);
        _ = this.CheckTypeUse(type, result, value, out _);
        return result;

        BoundType Open(BoundType current)
        {
            // A complete generic binding and a callable's quantified signature are contracts, not local storage slots.
            if (!current.CarriesOrigin || current.Kind is BoundTypeKind.Function or BoundTypeKind.FunctionItem or BoundTypeKind.Parameter)
            {
                return current;
            }

            var components = this.RentTypes(current.Components.Count);
            var arguments = this.originScratch.Rent(current.OriginArguments.Count);
            try
            {
                var origin = current.Origin is null ? null : this.LocalRegionSlot(owner, slot--);
                for (var i = 0; i < current.OriginArguments.Count; i++)
                {
                    arguments[i] = this.LocalRegionSlot(owner, slot--);
                }

                for (var i = 0; i < current.Components.Count; i++)
                {
                    components[i] = Open(current.Components[i]);
                }

                return this.types.Rebuild(current, components.AsSpan(0, current.Components.Count), origin, arguments.AsSpan(0, current.OriginArguments.Count));
            }
            finally
            {
                this.typeScratch.Return(components, clearArray: true);
                this.originScratch.Return(arguments, clearArray: true);
            }
        }
    }

    // SPEC 15.3.6, 15.8.2 (DECISIONS: G570): a Closure's result with the regions of its own body resolved. An enclosing region is fixed in
    // `body` (BU-28), so it stays: the result and the environment name one Origin, solved over all its bounds.
    private BoundType ResolveLocalResultOrigins(BoundType type, ExecutableBody body)
    {
        if (!this.HasLocalRegion(type))
        {
            return type;
        }

        var components = this.RentTypes(type.Components.Count);
        var arguments = this.originScratch.Rent(type.OriginArguments.Count);
        try
        {
            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.ResolveLocalResultOrigins(type.Components[i], body);
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                arguments[i] = Resolve(type.OriginArguments[i]);
            }

            var origin = type.Origin is { } outer ? Resolve(outer) : null;
            if (type.RawOrigin is { } raw)
            {
                origin = Resolve(raw);
            }

            return this.types.Intern(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, type.Components.Count), type.Length, origin, arguments.AsSpan(0, type.OriginArguments.Count), type.LengthExpression, type.ClosureContext, type.LengthArguments);
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
            this.originScratch.Return(arguments, clearArray: true);
        }

        BoundOrigin Resolve(BoundOrigin origin)
        {
            if (origin.Kind == OriginKind.Intersection)
            {
                var resolved = BoundOrigin.Static;
                for (var i = 0; i < origin.Operands.Count; i++)
                {
                    resolved = this.Meet(resolved, Resolve(origin.Operands[i]));
                }

                return resolved;
            }

            if (!RegionIn(origin, body))
            {
                return origin;
            }

            var sources = this.LocalRegionSources(origin);
            if (sources.IsEmpty)
            {
                return origin;
            }

            var result = sources[0];
            for (var i = 1; i < sources.Length; i++)
            {
                result = this.Meet(result, sources[i]);
            }

            return result;
        }
    }
}
