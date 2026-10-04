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
    public static bool FitsType(BoundType actual, BoundType expected) => FitsTypeCore(actual, expected, null, null);

    internal bool FitsTypeAt(BoundType actual, BoundType expected, Koto use) => FitsTypeCore(actual, expected, this, use);

    // Only Origin restriction is inferred here. No Core conversion or common base search is
    // introduced; every invariant position stays identical and both inputs must fit the result.
    internal BoundType? CommonOriginType(BoundType left, BoundType right)
    {
        if (ReferenceEquals(left, right))
        {
            return left;
        }

        if (left.Kind == BoundTypeKind.Primitive || right.Kind == BoundTypeKind.Primitive || left.Kind != right.Kind || left.Symbol != right.Symbol || left.Semantics != right.Semantics || left.Length != right.Length ||
            !ReferenceEquals(left.LengthExpression, right.LengthExpression) || !ReferenceEquals(left.ClosureContext, right.ClosureContext) || left.Components.Count != right.Components.Count ||
            left.OriginArguments.Count != right.OriginArguments.Count || (left.Origin is null) != (right.Origin is null))
        {
            return null;
        }

        var components = this.RentTypes(left.Components.Count);
        var origins = this.originScratch.Rent(left.OriginArguments.Count);
        try
        {
            for (var i = 0; i < left.Components.Count; i++)
            {
                var covariant = left.Semantics is not (SemanticsKind.Uniq or SemanticsKind.ObjUniq or SemanticsKind.Raw) &&
                    !(left.Kind == BoundTypeKind.Function && i == 0) &&
                    (left.Kind != BoundTypeKind.Constructed || left.Symbol?.Schema?.GenericSlots[i].OriginVariance == OriginVariance.Covariant);
                if ((covariant ? this.CommonOriginType(left.Components[i], right.Components[i]) : ReferenceEquals(left.Components[i], right.Components[i]) ? left.Components[i] : null) is not { } part)
                {
                    return null;
                }

                components[i] = part;
            }

            for (var i = 0; i < left.OriginArguments.Count; i++)
            {
                if (!ReferenceEquals(left.OriginArguments[i], right.OriginArguments[i]) && left.Symbol?.Schema?.Origins[i].Variance != OriginVariance.Covariant)
                {
                    return null;
                }

                origins[i] = this.Meet(left.OriginArguments[i], right.OriginArguments[i]);
            }

            var result = this.InternType(left.Kind, left.Symbol, left.Semantics, components.AsSpan(0, left.Components.Count), left.Length, left.Origin is { } a ? this.Meet(a, right.Origin!) : null, origins.AsSpan(0, left.OriginArguments.Count), left.LengthExpression, left.ClosureContext);
            return FitsType(left, result) && FitsType(right, result) ? result : null;
        }
        finally
        {
            this.originScratch.Return(origins, clearArray: true);
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    private static bool FitsTypeCore(BoundType actual, BoundType expected, Binding? binding, Koto? use, bool invariant = false, bool renameInput = false, Koto? actualInputs = null, Koto? expectedInputs = null)
    {
        if (ReferenceEquals(actual, expected) || ReferenceEquals(actual, BoundType.Never))
        {
            return true;
        }

        if (actual.Kind == BoundTypeKind.Primitive || expected.Kind == BoundTypeKind.Primitive)
        {
            return false;
        }

        if (actual.Kind != expected.Kind || actual.Semantics != expected.Semantics || !ReferenceEquals(actual.Symbol, expected.Symbol) || !ReferenceEquals(actual.ClosureContext, expected.ClosureContext) || actual.Length != expected.Length || !ReferenceEquals(actual.LengthExpression, expected.LengthExpression) || actual.Components.Count != expected.Components.Count || actual.OriginArguments.Count != expected.OriginArguments.Count)
        {
            return false;
        }

        if (!(renameInput && actual.Origin?.Kind == OriginKind.Input && expected.Origin?.Kind == OriginKind.Input) &&
            !ReferenceEquals(actual.Origin, expected.Origin) && (actual.Origin is null || expected.Origin is null ||
            !OriginFits(actual.Origin, expected.Origin) || (invariant && !OriginFits(expected.Origin, actual.Origin))))
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
            if (invariant || variance is OriginVariance.Invariant or OriginVariance.Unused ?
                !OriginFits(a, b) || !OriginFits(b, a) : variance == OriginVariance.Covariant ? !OriginFits(a, b) : !OriginFits(b, a))
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
                if (invariant || variance is OriginVariance.Invariant or OriginVariance.Unused ?
                    !FitsTypeCore(a, b, binding, use, true, actualInputs: actualInputs, expectedInputs: expectedInputs) : variance == OriginVariance.Covariant ? !FitsTypeCore(a, b, binding, use, actualInputs: actualInputs, expectedInputs: expectedInputs) : !FitsTypeCore(b, a, binding, use, actualInputs: expectedInputs, expectedInputs: actualInputs))
                {
                    return false;
                }

                continue;
            }

            if (actual.Kind == BoundTypeKind.Function && i == 0 && PerCallShape(actual) && PerCallShape(expected))
            {
                // Fresh input binders are local quantifiers of the Function Type, not fixed external Origins.
                // Rename only the outer input layer; referent Types and their own Origins remain rigid.
                if (a.Components.Count != b.Components.Count)
                {
                    return false;
                }

                for (var input = 0; input < a.Components.Count; input++)
                {
                    if (!FitsTypeCore(b.Components[input], a.Components[input], binding, use, invariant, renameInput: true))
                    {
                        return false;
                    }
                }
            }
            else if (actual.Kind == BoundTypeKind.Function && i == 1 && InputDependentBinder(actual) is { } actualBinder && InputDependentBinder(expected) is { } expectedBinder)
            {
                // SPEC 10.7, 15.6.4: a result over the per-call inputs names them through its own Function Type's binders;
                // the two results are compared with each input of one standing for the same input of the other.
                if (!FitsTypeCore(a, b, binding, use, invariant, actualInputs: actualBinder, expectedInputs: expectedBinder))
                {
                    return false;
                }
            }
            else if (invariant || actual.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq or SemanticsKind.Raw)
            {
                if (!FitsTypeCore(a, b, binding, use, true, actualInputs: actualInputs, expectedInputs: expectedInputs))
                {
                    return false;
                }
            }
            else if (actual.Kind == BoundTypeKind.Function && i == 0)
            {
                if (!FitsTypeCore(b, a, binding, use))
                {
                    return false;
                }
            }
            else if (!FitsTypeCore(a, b, binding, use, actualInputs: actualInputs, expectedInputs: expectedInputs))
            {
                return false;
            }
        }

        return true;

        bool OriginFits(BoundOrigin a, BoundOrigin b) => actualInputs is not null && RenamedInputsOutlive(a, b, actualInputs, expectedInputs!) is { } renamed ? renamed :
            binding is null ? OriginOutlives(a, b) : binding.ProvesOriginOutlives(a, b, use!);
    }

    // A signature whose inputs are fresh per-call borrows and whose result has no Origin or one over those inputs alone.
    private static bool PerCallShape(BoundType signature) => PerCallSignature(signature) || InputDependentBinder(signature) is not null;

    // Within two results over per-call inputs, input slot i of one stands for input slot i of the other. An Origin over a set of
    // inputs outlives one over a superset, the static Origin outlives every such Origin; null when no renamed input occurs.
    private static bool? RenamedInputsOutlive(BoundOrigin actual, BoundOrigin expected, Koto actualBinder, Koto expectedBinder)
    {
        var actualKnown = InputSlots(actual, actualBinder, out var actualSlots, out var actualRenamed);
        var expectedKnown = InputSlots(expected, expectedBinder, out var expectedSlots, out var expectedRenamed);
        if (!actualRenamed && !expectedRenamed)
        {
            return null;
        }

        return actualKnown && expectedKnown && (actualSlots & ~expectedSlots) == 0;

        static bool InputSlots(BoundOrigin origin, Koto binder, out ulong slots, out bool renamed)
        {
            slots = 0;
            renamed = false;
            if (origin.Kind == OriginKind.Static)
            {
                return true;
            }

            if (origin.Kind == OriginKind.Input && ReferenceEquals(origin.Binder, binder) && origin.Slot is >= 0 and < 64)
            {
                slots = 1UL << origin.Slot;
                renamed = true;
                return true;
            }

            if (origin.Kind != OriginKind.Intersection)
            {
                return false;
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (!InputSlots(origin.Operands[i], binder, out var part, out var partRenamed))
                {
                    renamed |= partRenamed;
                    return false;
                }

                slots |= part;
                renamed |= partRenamed;
            }

            return true;
        }
    }

    private bool CheckTypeUse(BoundType actual, BoundType expected, Koto use)
    {
        if (this.FitsTypeAt(actual, expected, use))
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
            if (actual.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq or SemanticsKind.Raw)
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

    private BoundType? SubstituteType(BoundType type, Koto binder, ReadOnlySpan<BoundType?> arguments, ReadOnlySpan<BoundLength?> lengths = default)
    {
        var slot = type.Symbol is { } parameter ? ContainerSlot(binder, parameter) : -1;
        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection && slot >= 0)
        {
            if ((uint)slot >= (uint)arguments.Length)
            {
                return null;
            }

            var whole = arguments[slot];
            if (whole is null)
            {
                return null;
            }

            var projected = type.Kind == BoundTypeKind.TargetProjection ? this.DirectTarget(whole) : whole;
            if (type.Origin is { } replacement && (IsBorrow(projected.Semantics) || projected.Kind is BoundTypeKind.Parameter or BoundTypeKind.SemanticsApplication))
            {
                // SPEC 8.1.2: the annotation binds the outer-Origin slot of a borrow binding; a value binding keeps no slot,
                // and an abstract binding keeps it conditional.
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
            var length = type.Length;
            var expression = type.LengthExpression;
            if (!lengths.IsEmpty && expression is not null)
            {
                expression = this.SubstituteLength(expression, binder, lengths);
                if (expression is null || (expression.IsConstant && !this.ValidLength(expression.Value)))
                {
                    return null;
                }

                if (expression.IsConstant)
                {
                    length = expression.Value;
                    expression = null;
                }
            }

            var changed = length != type.Length || !ReferenceEquals(expression, type.LengthExpression);
            for (var i = 0; i < type.Components.Count; i++)
            {
                var substituted = this.SubstituteType(type.Components[i], binder, arguments, lengths);
                if (substituted is null)
                {
                    return null;
                }

                scratch[i] = substituted;
                changed |= !ReferenceEquals(substituted, type.Components[i]);
            }

            if (type.Kind == BoundTypeKind.SemanticsApplication && slot >= 0)
            {
                if ((uint)slot >= (uint)arguments.Length)
                {
                    return null;
                }

                var whole = arguments[slot];
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
                return this.InternType(BoundTypeKind.Semantics, null, whole.Semantics, scratch.AsSpan(0, 1), origin: IsBorrow(whole.Semantics) ? type.Origin : null);
            }

            return changed ? this.InternType(type.Kind, type.Symbol, type.Semantics, scratch.AsSpan(0, type.Components.Count), length, type.Origin, (BoundOrigin[])type.OriginArguments, expression, type.ClosureContext) : type;
        }
        finally
        {
            this.typeScratch.Return(scratch, clearArray: true);
        }
    }
}
