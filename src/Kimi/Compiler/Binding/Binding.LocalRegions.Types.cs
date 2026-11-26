// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The Types of locals over the local-region graph (SPEC 15.4.4): a region owner's Type opened into region slots, a local's, `let` or
// `var`, a var Pattern binding's and a var for slot's alike (LocalBorrowType), a result's own body's regions resolved to their sources
// (ResolveLocalResultOrigins), and the queries on the regions of a Type's parts (HasLocalRegion, which also counts a pending atom,
// HasRegionBounds, CarrierOf).
public sealed partial class Binding
{
    // Whether an Origin of the Type's parts, a raw layer's included, is a local region or a pending atom; a Function or Function Item part
    // keeps its own, so no Origin summary answers it, and each distinct part is visited once per query (DECISIONS BU-19, BU-63).
    internal bool HasLocalRegion(BoundType type) => this.LocalRegionPart(type, this.visitedParts.Begin(this.storageVersion));

    // SPEC 15.6, 15.6.5: the Origin, read at a use, through which a value of a Type carries `origin`, judged without flow: one the Type
    // names (a meet operand included) or a local region with `origin` among its sources; null when the value does not carry it. Each
    // distinct part is visited once per query (DECISIONS BU-19, BU-63): a part met again carried nothing, since a carrier ends the walk.
    internal BoundOrigin? CarrierOf(BoundType type, BoundOrigin origin, Koto use) => this.CarrierPart(type, origin, use, this.visitedParts.Begin(this.storageVersion));

    // Whether an Origin of the Type's parts has region bounds; each distinct part is visited once per query (DECISIONS BU-19).
    internal bool HasRegionBounds(BoundType type) => this.RegionBoundsPart(type, this.visitedParts.Begin(this.storageVersion));

    // SPEC 15.3.6, 15.4.4, 10.8 (PLAN G492-a (ii) revision 2): a Type as a selection reads it at `use`, an argument's Type its evidence,
    // with each `let` region of the use's own body read as its value (RegionValueAtUse); one call is one query of rewrittenParts, each
    // distinct part rebuilt once. A Function or Function Item part is a contract and keeps its own Origins.
    internal BoundType RegionValuesAt(BoundType type, Koto use)
        => this.regionValues == 0 || !this.HasLocalRegion(type) ? type : this.RegionValuesPart(type, use, this.rewrittenParts.Begin(this.storageVersion));

    // SPEC 15.4.4, 15.6.5 (PLAN G492-a (ii) revision 2): whether a local region of a body takes part in a fit at `use`, each Type read
    // there (RegionValuesAt), so a `let` region of the use's own body takes part as its value: the fit then leaves its Origin positions
    // as rows at the use (FitsValueAt, a control-flow join), and otherwise is judged where it is formed.
    internal bool RegionalAt(BoundType actual, BoundType expected, Koto use)
        => this.HasLocalRegion(this.RegionValuesAt(actual, use)) || this.HasLocalRegion(this.RegionValuesAt(expected, use));

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

    private BoundOrigin? CarrierPart(BoundType type, BoundOrigin origin, Koto use, uint query)
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
            if (this.FirstVisit(type.Components[i], query) && this.CarrierPart(type.Components[i], origin, use, query) is { } part)
            {
                return part;
            }
        }

        return null;
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

    // `slot` is the local's next region slot (BindVariableCore; a Pattern binding's or for slot's first, BindingTypeOf). SPEC 15.4.2,
    // 15.6.5, 15.3.4 (PLAN G492-a (ii)): a region owner's unwritten slots are its regions, `let` and `var` alike, opened at its declaration
    // (15.4.4), one per stored part and place: each path of a reassignable binding's storage is a place a store writes apart
    // (IsMutableDeclaration), so its paths are opened apart; a `let` local is one place, so a stored part met on several of its paths is
    // opened once, in one query of rewrittenParts. The fit records each region's bound from the value (CheckTypeUse). Each slot's row is
    // its position's variance in the local's Type, composed as RelationWalk composes it; a `let` local's part met again keeps its first
    // row, which no reader reads (its value is published), and its regions, which merge its paths (DECISIONS R8-M56), are marked so
    // (MarkMerged; PLAN G922 U1).
    private BoundType LocalBorrowType(BoundType type, Koto owner, Koto value, int slot)
    {
        if (!IsRegionOwner(owner) || !type.StoresOrigin)
        {
            return type;
        }

        var query = IsMutableDeclaration(owner) ? 0u : this.rewrittenParts.Begin(this.storageVersion);
        var merged = 0u; // MarkMerged's visitedParts query, begun at the first part met again.
        var result = Open(type, OriginVariance.Covariant);
        _ = this.CheckTypeUse(type, result, value, out _);
        return result;

        BoundType Open(BoundType current, OriginVariance variance)
        {
            if (query == 0)
            {
                return OpenPart(current, variance);
            }

            if (!this.rewrittenParts.TryGet(current, query, out var opened))
            {
                opened = OpenPart(current, variance);
                this.rewrittenParts.Set(current, query, opened);
            }
            else
            {
                merged = merged == 0 ? this.visitedParts.Begin(this.storageVersion) : merged;
                if (this.FirstVisit(opened!, merged))
                {
                    this.MarkMerged(opened!, owner, merged);
                }
            }

            return opened!;
        }

        BoundType OpenPart(BoundType current, OriginVariance variance)
        {
            // SPEC 15.6.5, 8.1.2 (PLAN G758): a pair layer's outer-Origin slot, the original's implicit `o` included (OuterOrigin), is a
            // slot of the local's Type as a concrete layer's Origin is; its target stays the binder's.
            if (current.Kind == BoundTypeKind.Parameter && this.OuterOrigin(current) is { } outer)
            {
                return this.types.Rebuild(current, (BoundType[])current.Components, Region(outer, variance), (BoundOrigin[])current.OriginArguments);
            }

            // A complete generic binding and a callable's quantified signature are contracts, not local storage slots.
            if (!current.StoresOrigin || current.Kind is BoundTypeKind.Function or BoundTypeKind.FunctionItem or BoundTypeKind.Parameter)
            {
                return current;
            }

            var components = this.RentTypes(current.Components.Count);
            var arguments = this.originScratch.Rent(current.OriginArguments.Count);
            try
            {
                var origin = current.Origin is { } own ? Region(own, variance) : null;
                for (var i = 0; i < current.OriginArguments.Count; i++)
                {
                    arguments[i] = Region(current.OriginArguments[i], ComposeVariance(variance, this.OriginArgumentVariance(current, i, this)));
                }

                for (var i = 0; i < current.Components.Count; i++)
                {
                    components[i] = Open(current.Components[i], ComposeVariance(variance, ComponentVariance(current, i, this)));
                }

                return this.types.Rebuild(current, components.AsSpan(0, current.Components.Count), origin, arguments.AsSpan(0, current.OriginArguments.Count));
            }
            finally
            {
                this.typeScratch.Return(components, clearArray: true);
                this.originScratch.Return(arguments, clearArray: true);
            }
        }

        // A `let` region's value is the initializer's Origin at its slot (SPEC 15.3.6, 15.4.4: its one bound, fixed at the declaration).
        BoundOrigin Region(BoundOrigin initial, OriginVariance row)
        {
            var region = this.LocalRegionSlot(owner, slot--, row);
            if (query != 0)
            {
                this.PublishRegionValue(region, initial, value);
            }

            return region;
        }
    }

    // DECISIONS R8-M56 (PLAN G922 U1, the owner's O-534 (a) interim): the regions of `owner`'s opened part that its opening met again on
    // another path merge those paths, each marked Merged for the name limit (UnmergedSelection); each distinct part is walked once per
    // opening (visitedParts). A contract part keeps its own Origins, which are no regions of `owner`.
    private void MarkMerged(BoundType part, Koto owner, uint query)
    {
        if (!part.StoresOrigin || part.Kind is BoundTypeKind.Function or BoundTypeKind.FunctionItem)
        {
            return;
        }

        Mark(part.Origin);
        for (var i = 0; i < part.OriginArguments.Count; i++)
        {
            Mark(part.OriginArguments[i]);
        }

        for (var i = 0; i < part.Components.Count; i++)
        {
            if (this.FirstVisit(part.Components[i], query))
            {
                this.MarkMerged(part.Components[i], owner, query);
            }
        }

        void Mark(BoundOrigin? origin)
        {
            if (origin is not null && SlotRegion(origin) && ReferenceEquals(origin.Binder, owner) && this.Region(origin) is { } row)
            {
                row.Merged = true;
            }
        }
    }

    private BoundType RegionValuesPart(BoundType type, Koto use, uint query)
    {
        if (this.rewrittenParts.TryGet(type, query, out var read))
        {
            return read!;
        }

        read = type;
        if (type.CarriesAnyOrigin && type.Kind is not (BoundTypeKind.Function or BoundTypeKind.FunctionItem))
        {
            var components = this.RentTypes(type.Components.Count);
            var arguments = this.originScratch.Rent(type.OriginArguments.Count);
            try
            {
                for (var i = 0; i < type.Components.Count; i++)
                {
                    components[i] = this.RegionValuesPart(type.Components[i], use, query);
                }

                for (var i = 0; i < type.OriginArguments.Count; i++)
                {
                    arguments[i] = this.RegionValueAtUse(type.OriginArguments[i], use);
                }

                var origin = type.Origin is { } own ? this.RegionValueAtUse(own, use) : null;
                read = this.types.Rebuild(type, components.AsSpan(0, type.Components.Count), origin, arguments.AsSpan(0, type.OriginArguments.Count));
            }
            finally
            {
                this.typeScratch.Return(components, clearArray: true);
                this.originScratch.Return(arguments, clearArray: true);
            }
        }

        this.rewrittenParts.Set(type, query, read);
        return read;
    }

    // SPEC 15.3.6, 15.8.2 (DECISIONS: G570): a Closure's result with the regions of its own body resolved: a local region as its value
    // (RegionValue, R7-O2 A1), a call's open region as its sources' meet (it has no local value; retention's AddRegionMeet reads it so),
    // each in turn until no region of the body is left. An enclosing region is fixed in `body` (BU-28), so it stays: the result and the
    // environment name one Origin, solved over all its bounds. One call is one query of
    // rewrittenParts (DECISIONS BU-19), as PublishedResult is: a part's resolution is a function of the part and `body`, so each distinct
    // part is resolved once (PLAN G492-a (ii)).
    private BoundType ResolveLocalResultOrigins(BoundType type, ExecutableBody body)
        => this.ResolveLocalResultOrigins(type, body, this.rewrittenParts.Begin(this.storageVersion));

    private BoundType ResolveLocalResultOrigins(BoundType type, ExecutableBody body, uint query)
    {
        if (!this.rewrittenParts.TryGet(type, query, out var resolved))
        {
            resolved = this.ResolveLocalResultPart(type, body, query);
            this.rewrittenParts.Set(type, query, resolved);
        }

        return resolved!;
    }

    private BoundType ResolveLocalResultPart(BoundType type, ExecutableBody body, uint query)
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
                components[i] = this.ResolveLocalResultOrigins(type.Components[i], body, query);
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

            return this.types.Intern(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, type.Components.Count), type.Length, origin, arguments.AsSpan(0, type.OriginArguments.Count), type.LengthExpression, type.InstanceContext, type.LengthArguments, type.Binder);
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

            if (SlotRegion(origin))
            {
                return this.RegionValue(origin) is { } value ? Resolve(value) : origin;
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
