// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    /// <summary>Compares interned complete types without erasing their Origin contracts.</summary>
    /// <param name="left">The first canonical type.</param>
    /// <param name="right">The second canonical type from the same compilation.</param>
    /// <returns>Whether both references identify the same complete type.</returns>
    public static bool SameType(BoundType left, BoundType right) => ReferenceEquals(left, right);

    /// <summary>Proves the implemented structural subtype rules without performing a value operation.</summary>
    /// <param name="actual">The supplied complete type.</param>
    /// <param name="expected">The required complete type.</param>
    /// <returns>Whether the implemented rules prove the relation.</returns>
    public static bool FitsType(BoundType actual, BoundType expected)
    {
        if (ReferenceEquals(actual, expected) || ReferenceEquals(actual, BoundType.Never))
        {
            return true;
        }

        if (actual.Kind == BoundTypeKind.Primitive || expected.Kind == BoundTypeKind.Primitive)
        {
            return false;
        }

        if (actual.Kind != expected.Kind || actual.Semantics != expected.Semantics || !ReferenceEquals(actual.Symbol, expected.Symbol) || actual.Length != expected.Length || !ReferenceEquals(actual.LengthExpression, expected.LengthExpression) || actual.Components.Count != expected.Components.Count || actual.OriginArguments.Count != expected.OriginArguments.Count)
        {
            return false;
        }

        if (!ReferenceEquals(actual.Origin, expected.Origin) && (actual.Origin is null || expected.Origin is null || !OriginOutlives(actual.Origin, expected.Origin)))
        {
            return false;
        }

        for (var i = 0; i < actual.OriginArguments.Count; i++)
        {
            var a = actual.OriginArguments[i];
            var b = expected.OriginArguments[i];
            if (ReferenceEquals(a, b))
            {
                continue;
            }

            var variance = actual.Symbol?.Schema?.Origins[i].Variance ?? OriginVariance.Invariant;
            if (variance == OriginVariance.Covariant ? !OriginOutlives(a, b) : variance == OriginVariance.Contravariant ? !OriginOutlives(b, a) : true)
            {
                return false;
            }
        }

        for (var i = 0; i < actual.Components.Count; i++)
        {
            var a = actual.Components[i];
            var b = expected.Components[i];
            if (actual.Kind == BoundTypeKind.Constructed && actual.Symbol?.Schema is { } schema)
            {
                var variance = schema.GenericSlots[i].OriginVariance;
                if (variance == OriginVariance.Covariant ? !FitsType(a, b) : variance == OriginVariance.Contravariant ? !FitsType(b, a) : !ReferenceEquals(a, b))
                {
                    return false;
                }

                continue;
            }

            if (actual.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq or SemanticsKind.Unsafe)
            {
                if (!ReferenceEquals(a, b))
                {
                    return false;
                }
            }
            else if (actual.Kind == BoundTypeKind.Function && i == 0)
            {
                if (!FitsType(b, a))
                {
                    return false;
                }
            }
            else if (!FitsType(a, b))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasDeclaredOrigins(BoundType type)
    {
        if (type.Origin is not null || type.OriginArguments.Count != 0)
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (HasDeclaredOrigins(type.Components[i]))
            {
                return true;
            }
        }

        return false;
    }

    private bool CheckTypeUse(BoundType actual, BoundType expected, Koto use)
    {
        if (FitsType(actual, expected))
        {
            return true;
        }

        if (actual.Kind != expected.Kind || actual.Semantics != expected.Semantics || actual.Symbol != expected.Symbol || actual.Length != expected.Length || !ReferenceEquals(actual.LengthExpression, expected.LengthExpression) || actual.Components.Count != expected.Components.Count || actual.OriginArguments.Count != expected.OriginArguments.Count || actual.Kind == BoundTypeKind.Primitive)
        {
            return false;
        }

        if (!ReferenceEquals(actual.Origin, expected.Origin))
        {
            if (actual.Origin is null || expected.Origin is null)
            {
                return false;
            }

            this.AddObligation(new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, expected, actual.Origin, expected.Origin));
        }

        for (var i = 0; i < actual.OriginArguments.Count; i++)
        {
            if (ReferenceEquals(actual.OriginArguments[i], expected.OriginArguments[i]))
            {
                continue;
            }

            this.AddObligation(new(BindingObligationKind.TypeFormation, use, BindingDeadline.BodyOrigins, expected, actual.OriginArguments[i], expected.OriginArguments[i]));
        }

        for (var i = 0; i < actual.Components.Count; i++)
        {
            if (actual.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq or SemanticsKind.Unsafe)
            {
                if (!ReferenceEquals(actual.Components[i], expected.Components[i]))
                {
                    return false;
                }
            }
            else if (!this.CheckTypeUse(actual.Components[i], expected.Components[i], use))
            {
                return false;
            }
        }

        return true;
    }

    private BoundType DirectTarget(BoundType whole)
    {
        if (whole.Kind is BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication)
        {
            return whole.Components[0];
        }

        if (whole.Kind == BoundTypeKind.Parameter)
        {
            return whole.Symbol!.Kind == BindingSymbolKind.SemanticsTarget ? whole.Symbol.Type! : this.InternType(BoundTypeKind.TargetProjection, whole.Symbol, SemanticsKind.Owner, []);
        }

        return whole;
    }

    private BoundType? SubstituteType(BoundType type, Koto binder, ReadOnlySpan<BoundType?> arguments)
    {
        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection && type.Symbol!.Scope.Owner == binder)
        {
            var whole = arguments[type.Symbol.Slot];
            if (whole is null)
            {
                return null;
            }

            var projected = type.Kind == BoundTypeKind.TargetProjection ? this.DirectTarget(whole) : whole;
            if (type.Origin is { } replacement)
            {
                if (!IsBorrow(projected.Semantics))
                {
                    return null;
                }

                projected = this.WithOrigins(projected, replacement, (BoundOrigin[])projected.OriginArguments);
            }

            return projected;
        }

        if (type.Components.Count == 0)
        {
            return type;
        }

        var scratch = this.RentTypes(type.Components.Count);
        try
        {
            var changed = false;
            for (var i = 0; i < type.Components.Count; i++)
            {
                var substituted = this.SubstituteType(type.Components[i], binder, arguments);
                if (substituted is null)
                {
                    return null;
                }

                scratch[i] = substituted;
                changed |= !ReferenceEquals(substituted, type.Components[i]);
            }

            if (type.Kind == BoundTypeKind.SemanticsApplication && type.Symbol!.Scope.Owner == binder)
            {
                var whole = arguments[type.Symbol.Slot];
                if (whole is null)
                {
                    return null;
                }

                if (whole.Kind == BoundTypeKind.Parameter)
                {
                    return this.InternType(BoundTypeKind.SemanticsApplication, whole.Symbol, SemanticsKind.Parameter, scratch.AsSpan(0, 1), origin: type.Origin);
                }

                if (whole.Semantics == SemanticsKind.Owner)
                {
                    return scratch[0];
                }

                // s/U applies only the kind; it never inherits the original pair's outer Origin.
                return this.InternType(BoundTypeKind.Semantics, null, whole.Semantics, scratch.AsSpan(0, 1), origin: type.Origin);
            }

            return changed ? this.InternType(type.Kind, type.Symbol, type.Semantics, scratch.AsSpan(0, type.Components.Count), type.Length, type.Origin, (BoundOrigin[])type.OriginArguments, type.LengthExpression) : type;
        }
        finally
        {
            this.typeScratch.Return(scratch, clearArray: true);
        }
    }
}
