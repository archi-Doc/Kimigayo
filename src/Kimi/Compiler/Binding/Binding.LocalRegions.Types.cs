// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The Types of locals over the local-region graph (SPEC 15.4.4): a mutable local's Type opened into region slots (LocalBorrowType), a
// result's local regions resolved to their sources (ResolveLocalResultOrigins), and the queries on the regions of a Type's parts
// (HasLocalRegion, which also counts a pending atom, HasRegionBounds, CarrierOf, CheckLocalTypeUse).
public sealed partial class Binding
{
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

    // `slot` is the local's next region slot (BindVariableCore).
    private BoundType LocalBorrowType(BoundType type, VariableKoto owner, Koto value, int slot)
    {
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

    private BoundType ResolveLocalResultOrigins(BoundType type)
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
                components[i] = this.ResolveLocalResultOrigins(type.Components[i]);
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

            if (!IsLocalRegion(origin))
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

    private bool CheckLocalTypeUse(BoundType actual, BoundType expected, Koto use)
        => (this.HasLocalRegion(actual) || this.HasLocalRegion(expected)) && this.CheckTypeUse(actual, expected, use, out _);
}
