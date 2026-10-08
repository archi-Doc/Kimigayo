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

    // SPEC 15.6.1: the structural part of a fit, with all Origin bindings treated as equal; only its failure is a Type mismatch. The
    // Origin part of a Function Type, a comparison of whole contracts, is still judged here (SPEC 10.7).
    internal static bool FitsStructurally(BoundType actual, BoundType expected) => FitsTypeCore(actual, expected, null, null, structural: true);

    // SPEC 10.4 step 2, 15.6.1: the structural part with every Origin binding treated as equal, inside Function Types too, since Origin
    // bindings never rank candidates.
    internal static bool FitsStructuralPart(BoundType actual, BoundType expected) => FitsTypeCore(actual, expected, null, null, structural: true, everywhere: true);

    internal bool FitsTypeAt(BoundType actual, BoundType expected, Koto use) => FitsTypeCore(actual, expected, this, use);

    // Lowering rechecks the already verified contract without adding constraints or reopening inference.
    internal bool FitsVerifiedTypeAt(BoundType actual, BoundType expected, Koto use)
        => this.FitsTypeAt(actual, expected, use) || ((actual.CarriesOrigin || expected.CarriesOrigin) &&
            this.FitsStructurallyAt(actual, expected, use) && this.FailedOriginRelation(actual, expected, use, OriginVariance.Covariant, verified: true) is null);

    internal bool FitsStructurallyAt(BoundType actual, BoundType expected, Koto use) => FitsTypeCore(actual, expected, this, use, structural: true);

    // Only Origin restriction is inferred here. No Core conversion or common base search is
    // introduced; every invariant position stays identical and both inputs must fit the result.
    internal BoundType? CommonOriginType(BoundType left, BoundType right)
    {
        if (ReferenceEquals(left, right))
        {
            return left;
        }

        if (left.Kind == BoundTypeKind.Primitive || right.Kind == BoundTypeKind.Primitive || left.Kind != right.Kind || left.ResultMode != right.ResultMode || left.Symbol != right.Symbol || left.Semantics != right.Semantics || left.Length != right.Length ||
            !ReferenceEquals(left.LengthExpression, right.LengthExpression) || !ReferenceEquals(left.ClosureContext, right.ClosureContext) || !left.LengthArguments.AsSpan().SequenceEqual(right.LengthArguments) || left.Components.Count != right.Components.Count ||
            left.OriginArguments.Count != right.OriginArguments.Count || ((left.Origin is null) != (right.Origin is null) && !InactiveOuterOrigin(left, this, null)))
        {
            return null;
        }

        var components = this.RentTypes(left.Components.Count);
        var origins = this.originScratch.Rent(left.OriginArguments.Count);
        try
        {
            for (var i = 0; i < left.Components.Count; i++)
            {
                var covariant = !IsInvariantLayer(left, this) &&
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

            var result = this.InternType(left.Kind, left.Symbol, left.Semantics, components.AsSpan(0, left.Components.Count), left.Length, !InactiveOuterOrigin(left, this, null) && left.Origin is { } a ? this.Meet(a, right.Origin!) : null, origins.AsSpan(0, left.OriginArguments.Count), left.LengthExpression, left.ClosureContext, left.LengthArguments, left.ResultMode);
            return FitsType(left, result) && FitsType(right, result) ? result : null;
        }
        finally
        {
            this.originScratch.Return(origins, clearArray: true);
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    // `own` is the declaration whose own per-call inputs the root actual signature names (SPEC 8.6); a nested Function Type's own
    // syntax binds its inputs. `skipOrigin` leaves the outer Origin to an instantiation that already fixed it; `instance` is the
    // SPEC 15.3.5: the layers invariant in their target: an exclusive borrow, a writable object handle, a raw pointer, and a pair
    // layer whose binder admits one of them (InvariantAdmitted), since a relation between pair layers must hold for every admitted
    // Semantics. A comparison without a Binding sees only the concrete layers.
    private static bool IsInvariantLayer(BoundType type, Binding? binding)
        => type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq or SemanticsKind.Raw || (binding is not null && binding.InvariantAdmitted(type));

    // SPEC 8.1.2: a pair's conditional outer Origin participates only in admitted borrow cases. Internal Origins remain checked.
    private static bool InactiveOuterOrigin(BoundType type, Binding? binding, Koto? use)
    {
        if (!TryAdaptationSelector(type, out var whole) || whole.Symbol is not { } selector)
        {
            return false;
        }

        binding ??= selector.Declaration.CodeContext.Compilation.Binding;
        var scope = use is null ? selector.Scope : binding.ConstraintScope(use);
        // An owner application preserves the target's borrow Origin, not the application's conditional slot.
        var semantics = type.Kind == BoundTypeKind.SemanticsAdaptation ? binding.ResultSemantics(type, scope)
            : binding.AdmittedSemantics(whole, scope);
        return (semantics & SemanticsMask.Borrow) == 0;
    }

    // Callable comparison whose per-call Origins the parts are compared under; `structural` treats Origin bindings as equal outside
    // Function Types, and `everywhere` inside them too.
    private static bool FitsTypeCore(BoundType actual, BoundType expected, Binding? binding, Koto? use, bool invariant = false, bool skipOrigin = false, CallableInstance instance = default, Koto? own = null, bool structural = false, bool everywhere = false)
    {
        if (ReferenceEquals(actual, expected) || ReferenceEquals(actual, BoundType.Never))
        {
            return true;
        }

        if (actual.Kind == BoundTypeKind.Primitive || expected.Kind == BoundTypeKind.Primitive)
        {
            return false;
        }

        var inferredLength = expected.Kind == BoundTypeKind.FixedArray && expected.Length < 0;
        if (actual.Kind != expected.Kind || actual.ResultMode != expected.ResultMode || actual.Semantics != expected.Semantics || !ReferenceEquals(actual.Symbol, expected.Symbol) || !ReferenceEquals(actual.ClosureContext, expected.ClosureContext) || !actual.LengthArguments.AsSpan().SequenceEqual(expected.LengthArguments) || (!inferredLength && (actual.Length != expected.Length || !ReferenceEquals(actual.LengthExpression, expected.LengthExpression))) || actual.Components.Count != expected.Components.Count || actual.OriginArguments.Count != expected.OriginArguments.Count)
        {
            return false;
        }

        if (!skipOrigin && !ReferenceEquals(actual.Origin, expected.Origin) && !InactiveOuterOrigin(actual, binding, use) && (actual.Origin is null || expected.Origin is null ||
            (!structural && (!OriginFits(actual.Origin, expected.Origin) || (invariant && !OriginFits(expected.Origin, actual.Origin))))))
        {
            return false;
        }

        for (var i = 0; i < actual.OriginArguments.Count; i++)
        {
            var a = actual.OriginArguments[i];
            var b = expected.OriginArguments[i];
            if (ReferenceEquals(a, b) || structural)
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

        if (actual.Kind == BoundTypeKind.Function && actual.Components.Count == 2)
        {
            return FunctionFits(actual, expected, binding, use, invariant, new(actual, own ?? FunctionTypeBinder(actual), expected, FunctionTypeBinder(expected)), everywhere);
        }

        for (var i = 0; i < actual.Components.Count; i++)
        {
            var a = actual.Components[i];
            var b = expected.Components[i];
            if (actual.Kind == BoundTypeKind.Constructed && actual.Symbol?.Schema is { } schema)
            {
                var variance = schema.GenericSlots[i].OriginVariance;
                if (invariant || variance is OriginVariance.Invariant or OriginVariance.Unused ?
                    !FitsTypeCore(a, b, binding, use, true, instance: instance, structural: structural, everywhere: everywhere) : variance == OriginVariance.Covariant ? !FitsTypeCore(a, b, binding, use, instance: instance, structural: structural, everywhere: everywhere) : !FitsTypeCore(b, a, binding, use, instance: instance, structural: structural, everywhere: everywhere))
                {
                    return false;
                }

                continue;
            }

            if (invariant || IsInvariantLayer(actual, binding))
            {
                if (!FitsTypeCore(a, b, binding, use, true, instance: instance, structural: structural, everywhere: everywhere))
                {
                    return false;
                }
            }
            else if (!FitsTypeCore(a, b, binding, use, instance: instance, structural: structural, everywhere: everywhere))
            {
                return false;
            }
        }

        return true;

        bool OriginFits(BoundOrigin a, BoundOrigin b) => instance.Actual is not null ? InstanceOutlives(a, b, instance, binding, use, 0) :
            binding is null ? OriginOutlives(a, b) : binding.FitOriginOutlives(a, b, use!);
    }

    // A signature whose inputs are fresh per-call borrows and whose result has no Origin or one over those inputs alone.
    private static bool PerCallShape(BoundType signature, Koto? own, bool any = false)
        => PerCallSignature(signature, own, any) || InputDependentBinder(signature, own, any) is not null;

    // SPEC 10.7, 15.3.7: an implementation fits a required signature when its inputs accept the required inputs and its result fits
    // the required result. Each input the implementation binds per call is instantiated at the required input in the same position,
    // per call or fixed, so only its referent is compared; every other input is compared as written, contravariantly.
    private static bool FunctionFits(BoundType actual, BoundType expected, Binding? binding, Koto? use, bool invariant, CallableInstance instance, bool everywhere = false)
    {
        var actualInputs = actual.Components[0];
        var expectedInputs = expected.Components[0];
        if (!ReferenceEquals(actualInputs, expectedInputs))
        {
            if (actualInputs.Kind != expectedInputs.Kind || actualInputs.Components.Count != expectedInputs.Components.Count)
            {
                return false;
            }

            for (var i = 0; i < actualInputs.Components.Count; i++)
            {
                var input = actualInputs.Components[i];
                var required = expectedInputs.Components[i];
                if (!FitsTypeCore(required, input, binding, use, invariant, skipOrigin: instance.IsQuantifiedInput(input, i), instance: instance, structural: everywhere, everywhere: everywhere))
                {
                    return false;
                }
            }
        }

        return FitsTypeCore(actual.Components[1], expected.Components[1], binding, use, invariant, instance: instance, structural: everywhere, everywhere: everywhere);
    }

    // SPEC 10.7: within one Callable comparison, `a` outlives `b` after the implementation's call-time Origins are instantiated. The
    // required per-call Origins are rigid: one outlives another only at the same position, and a fixed Origin outlives one only when
    // it is static, since the call that binds it may come after every point of the enclosing body. A meet outlives an Origin when
    // each operand does, and an Origin outlives a meet when it outlives one operand.
    private static bool InstanceOutlives(BoundOrigin a, BoundOrigin b, in CallableInstance instance, Binding? binding, Koto? use, int depth)
    {
        if (depth > 8)
        {
            return false;
        }

        a = instance.Instantiate(a);
        b = instance.Instantiate(b);
        if (ReferenceEquals(a, b) || a.Kind == OriginKind.Static)
        {
            return true;
        }

        if (a.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < a.Operands.Count; i++)
            {
                if (!InstanceOutlives(a.Operands[i], b, instance, binding, use, depth + 1))
                {
                    return false;
                }
            }

            return true;
        }

        if (b.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < b.Operands.Count; i++)
            {
                if (InstanceOutlives(a, b.Operands[i], instance, binding, use, depth + 1))
                {
                    return true;
                }
            }

            return false;
        }

        if (instance.IsRequiredSlot(a) || instance.IsRequiredSlot(b))
        {
            return false;
        }

        if (b is { Kind: OriginKind.Inference, Open: true } && instance.IsResultOnlyOrigin(a))
        {
            return true;
        }

        return binding is null ? OriginOutlives(a, b) : binding.FitOriginOutlives(a, b, use!);
    }

    // SPEC 10.7, 15.6.1, 23.3.6.5: the first member of a conversion whose Origin part fails under the call-time instantiation: the n-th
    // parameter, whose required input must outlive the implementation's, or the result, which must outlive the required one. Both ends
    // are instantiated, so neither is a call-time Origin of the implementation.
    private OriginContractFact? ConversionContractFailure(BoundType actual, BoundType expected, Koto? own, Koto at, Koto use)
    {
        if (actual.Components.Count != 2 || expected.Components.Count != 2)
        {
            return null;
        }

        var instance = new CallableInstance(actual, own ?? FunctionTypeBinder(actual), expected, FunctionTypeBinder(expected));
        var inputs = actual.Components[0];
        var required = expected.Components[0];
        for (var i = 0; i < inputs.Components.Count && i < required.Components.Count; i++)
        {
            var input = inputs.Components[i];
            var part = instance.IsQuantifiedInput(input, i)
                ? this.FailedInstancePart(required.Components[i].Components[0], input.Components[0], input.Semantics == SemanticsKind.Uniq, instance, use)
                : this.FailedInstancePart(required.Components[i], input, false, instance, use);
            if (part is { } failed)
            {
                return new(at, $"the {Ordinal(i + 1)} parameter", failed.Longer, failed.Shorter, failed.Equality);
            }
        }

        return this.FailedInstancePart(actual.Components[1], expected.Components[1], false, instance, use) is { } result
            ? new(at, "the result", result.Longer, result.Shorter, result.Equality) : null;

        static string Ordinal(int n) => n + (n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    }

    // The first Origin position, in the order of FailedOriginRelation, at which `longer` does not fit `shorter` under the instantiation.
    private (BoundOrigin Longer, BoundOrigin Shorter, bool Equality)? FailedInstancePart(BoundType longer, BoundType shorter, bool invariant, in CallableInstance instance, Koto use, int depth = 0)
    {
        if (depth > 64 || longer.Components.Count != shorter.Components.Count || longer.OriginArguments.Count != shorter.OriginArguments.Count)
        {
            return null;
        }

        if (longer.Origin is { } a && shorter.Origin is { } b && !ReferenceEquals(a, b) &&
            (!InstanceOutlives(a, b, instance, this, use, 0) || (invariant && !InstanceOutlives(b, a, instance, this, use, 0))))
        {
            return (instance.Instantiate(a), instance.Instantiate(b), invariant);
        }

        var exclusive = longer.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq || this.InvariantAdmitted(longer);
        for (var i = 0; i < longer.Components.Count && longer.Kind is BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication; i++)
        {
            if (this.FailedInstancePart(longer.Components[i], shorter.Components[i], invariant || exclusive, instance, use, depth + 1) is { } target)
            {
                return target;
            }
        }

        for (var i = 0; i < longer.OriginArguments.Count; i++)
        {
            var a2 = longer.OriginArguments[i];
            var b2 = shorter.OriginArguments[i];
            var variance = longer.Symbol?.Schema?.Origins[i].Variance ?? OriginVariance.Invariant;
            var both = invariant || variance is OriginVariance.Invariant or OriginVariance.Unused;
            if (!ReferenceEquals(a2, b2) && (both ? !InstanceOutlives(a2, b2, instance, this, use, 0) || !InstanceOutlives(b2, a2, instance, this, use, 0)
                : variance == OriginVariance.Covariant ? !InstanceOutlives(a2, b2, instance, this, use, 0) : !InstanceOutlives(b2, a2, instance, this, use, 0)))
            {
                return (instance.Instantiate(a2), instance.Instantiate(b2), both);
            }
        }

        for (var i = 0; i < longer.Components.Count && longer.Kind is not (BoundTypeKind.Semantics or BoundTypeKind.Function); i++)
        {
            if (this.FailedInstancePart(longer.Components[i], shorter.Components[i], invariant, instance, use, depth + 1) is { } argument)
            {
                return argument;
            }
        }

        return null;
    }

    // SPEC 15.6.1: a fit whose structure holds leaves each differing Origin position as an obligation that ownership judges and reports
    // with facts at the value. `polarity` is the variance of the compared position within the fitted Type (SPEC 15.3.5), composed as
    // FitsTypeCore composes it: a contravariant position reverses the relation, and an invariant or unused one makes it `==`, also for
    // every position nested in it, such as the slots of a Type argument stored through `uniq/T` or read by `(T) -> i32`.
    private bool CheckTypeUse(BoundType actual, BoundType expected, Koto use, OriginVariance polarity = OriginVariance.Covariant)
    {
        if (!HasLocalRegion(actual) && !HasLocalRegion(expected) &&
            (polarity == OriginVariance.Contravariant ? this.FitsTypeAt(expected, actual, use) : FitsTypeCore(actual, expected, this, use, polarity == OriginVariance.Invariant)))
        {
            return true;
        }

        if (actual.Kind != expected.Kind || actual.ResultMode != expected.ResultMode || actual.Semantics != expected.Semantics || actual.Symbol != expected.Symbol || actual.Length != expected.Length || !ReferenceEquals(actual.LengthExpression, expected.LengthExpression) || !actual.LengthArguments.AsSpan().SequenceEqual(expected.LengthArguments) || actual.Components.Count != expected.Components.Count || actual.OriginArguments.Count != expected.OriginArguments.Count || actual.Kind == BoundTypeKind.Primitive)
        {
            return false;
        }

        if (actual.Kind == BoundTypeKind.Function)
        {
            return false; // SPEC 10.7, 15.6.1: a conversion compares whole contracts, so its failure is one record (RecordMismatch).
        }

        if (!ReferenceEquals(actual.Origin, expected.Origin) && !InactiveOuterOrigin(actual, this, use))
        {
            if (actual.Origin is null || expected.Origin is null)
            {
                return false;
            }

            this.AddOriginFit(actual.Origin, expected.Origin, expected, use, polarity);
        }

        for (var i = 0; i < actual.OriginArguments.Count; i++)
        {
            var a = actual.OriginArguments[i];
            var b = expected.OriginArguments[i];
            if (!ReferenceEquals(a, b))
            {
                this.AddOriginFit(a, b, expected, use, ComposeVariance(polarity, expected.Symbol?.Schema?.Origins[i].Variance ?? OriginVariance.Invariant));
            }
        }

        for (var i = 0; i < actual.Components.Count; i++)
        {
            if (IsInvariantLayer(actual, this))
            {
                if (!this.CheckTypeUse(actual.Components[i], expected.Components[i], use, OriginVariance.Invariant))
                {
                    return false;
                }

                continue;
            }

            var position = actual.Kind == BoundTypeKind.Constructed && actual.Symbol?.Schema is { } schema && i < schema.GenericSlots.Count
                ? schema.GenericSlots[i].OriginVariance : OriginVariance.Covariant;
            if (!this.CheckTypeUse(actual.Components[i], expected.Components[i], use, ComposeVariance(polarity, position)))
            {
                return false;
            }
        }

        return true;
    }

    // One Origin position of a fit by its variance: `actual` outlives `expected` at a covariant position, the reverse at a contravariant
    // one, and `==` otherwise; `type` is the expected Type at that position, the record's destination.
    private void AddOriginFit(BoundOrigin actual, BoundOrigin expected, BoundType type, Koto use, OriginVariance variance)
        => this.AddObligation(variance == OriginVariance.Contravariant
            ? new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, type, expected, actual)
            : new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, type, actual, expected, Equality: variance != OriginVariance.Covariant));

    private BoundType DirectTarget(BoundType whole)
    {
        if (whole.Kind == BoundTypeKind.SemanticsAdaptation)
        {
            return this.TransformFamily(whole, FamilyTransform.Target);
        }

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
        if (type.Kind == BoundTypeKind.SemanticsAdaptation && slot >= 0 && slot < arguments.Length && arguments[slot] is { } selection)
        {
            if (selection.Kind == BoundTypeKind.SemanticsAdaptation)
            {
                return this.SubstituteFamilyArgument(type, binder, arguments, lengths, slot, selection);
            }

            if (selection.Kind != BoundTypeKind.Parameter && selection.Semantics != SemanticsKind.Parameter)
            {
                return FamilyCase(type, selection.Semantics) is { } child ? this.SubstituteType(child, binder, arguments, lengths) : null;
            }
        }

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
            if (type.Origin is { } replacement && (IsBorrow(projected.Semantics) || projected.Kind is BoundTypeKind.Parameter or BoundTypeKind.SemanticsApplication or BoundTypeKind.SemanticsAdaptation))
            {
                // SPEC 8.1.2: the annotation binds the outer-Origin slot of a borrow binding; a value binding keeps no slot,
                // and an abstract binding keeps it conditional.
                projected = this.WithOrigins(projected, replacement, (BoundOrigin[])projected.OriginArguments);
            }

            return projected;
        }

        if (type.Components.Count == 0 && type.LengthArguments.Length == 0)
        {
            return type;
        }

        var scratch = this.RentTypes(type.Components.Count);
        var itemLengths = this.lengthScratch.Rent(type.LengthArguments.Length);
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
            for (var i = 0; i < type.LengthArguments.Length; i++)
            {
                var argument = type.LengthArguments[i];
                itemLengths[i] = argument is null || lengths.IsEmpty ? argument : this.SubstituteLength(argument, binder, lengths);
                if (argument is not null && (itemLengths[i] is null || (itemLengths[i]!.IsConstant && !this.ValidLength(itemLengths[i]!.Value))))
                {
                    return null;
                }

                changed |= !ReferenceEquals(argument, itemLengths[i]);
            }

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

            if (type.Kind is BoundTypeKind.SemanticsApplication or BoundTypeKind.SemanticsAdaptation && slot >= 0)
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

                if (type.Kind == BoundTypeKind.SemanticsAdaptation)
                {
                    var family = this.TypeFamily(type.Symbol!, (SemanticsMask)type.Length, scratch.AsSpan(0, type.Components.Count));
                    return family.Kind == BoundTypeKind.SemanticsAdaptation ? this.SubstituteFamily(family, whole) : family;
                }

                return this.ApplySemantics(whole, scratch[0], type.Origin);
            }

            var origin = type.Origin;
            if (origin is { Kind: OriginKind.Parameter, Occurrence: GenericParameterKoto declaration } && ReferenceEquals(origin.Binder, binder) &&
                declaration.BoundSymbol is { } target && ContainerSlot(binder, target) is >= 0 and var pair && pair < arguments.Length && arguments[pair] is { } bound)
            {
                // SPEC 8.1.1, 8.1.2: the pair's outer Origin `o` is bound through its Type argument: the outer Origin of a borrow
                // binding, nothing for a value binding (static, which no exclusive slot accepts); an abstract binding keeps its own `o`.
                origin = IsBorrow(bound.Semantics) ? bound.Origin ?? BoundOrigin.Static : this.OuterOrigin(bound) ?? BoundOrigin.Static;
                changed |= !ReferenceEquals(origin, type.Origin);
            }

            return changed ? this.InternType(type.Kind, type.Symbol, type.Semantics, scratch.AsSpan(0, type.Components.Count), length, origin, (BoundOrigin[])type.OriginArguments, expression, type.ClosureContext, itemLengths.AsSpan(0, type.LengthArguments.Length), type.ResultMode) : type;
        }
        finally
        {
            this.typeScratch.Return(scratch, clearArray: true);
            this.lengthScratch.Return(itemLengths, clearArray: true);
        }
    }
}
