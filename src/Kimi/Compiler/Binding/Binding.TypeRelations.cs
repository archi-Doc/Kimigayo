// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The Type relation (SPEC 3.8, 15.3.5, 15.6.1; DECISIONS BU-4): subtyping, identity up to Origin variance, read by Type-to-Type
// comparisons (identity tests, slot unification, ranking, contract and layer matching) through IsSubtype, IsStructuralSubtype,
// IsSubtypeAt and FitsStructuralPart, and by the value fits (Binding.ValueFits.cs) below their root. Never relates only to itself: a
// Type holding Never is an ordinary Type, and Never fitting is a value judgment (SPEC 3.8), as 10.7's result compatibility is a
// callable one (CallableSignatureFits). Each entry is one query of the relation's one walk (RelationWalk, Binding.TypeRelations.Walk.cs).
public sealed partial class Binding
{
    /// <summary>Compares interned complete types without erasing their Origin contracts.</summary>
    /// <param name="left">The first canonical type.</param>
    /// <param name="right">The second canonical type from the same compilation.</param>
    /// <returns>Whether both references identify the same complete type.</returns>
    public static bool SameType(BoundType left, BoundType right) => ReferenceEquals(left, right);

    // SPEC 3.8: whether `actual` is a subtype of `expected`; `own` is the declaration whose per-call inputs `actual` names.
    internal static bool IsSubtype(BoundType actual, BoundType expected, Koto? own = null)
        => new RelationWalk(null, null, OriginPart.Judged).Fits(actual, expected, own: own);

    // SPEC 15.6.1: the structural part of the relation, every Origin binding outside Function Types treated as equal.
    internal static bool IsStructuralSubtype(BoundType actual, BoundType expected)
        => new RelationWalk(null, null, OriginPart.Structural).Fits(actual, expected);

    // SPEC 10.4 step 2, 15.6.1: the structural part with every Origin binding treated as equal, inside Function Types too, since Origin
    // bindings never rank candidates.
    internal static bool FitsStructuralPart(BoundType actual, BoundType expected)
        => new RelationWalk(null, null, OriginPart.Everywhere).Fits(actual, expected);

    // SPEC 10.7, 15.6.1: the part of callable compatibility that applicability reads: subtyping against the required signature, its
    // result Never when the implementation's is (CompatibleResult), judged without recording a bound; `own` is the declaration whose
    // per-call inputs the implementation's signature names. A Function Item's conditions are ItemContractFits'.
    internal bool CallableSubtype(BoundType actual, BoundType expected, Koto? own) => IsSubtype(actual, this.CompatibleSignature(actual, expected), own);

    // SPEC 10.7, 15.3.7: callable compatibility of a Closure's or a Function value's signature with a required one: CallableSubtype and
    // the implementation's conditions, its own well-formedness proven from the required signature's and the premises at `use` under
    // the instantiation in position (the owner's (a); FailedSignatureWellFormedness, the one judgment FunctionFits makes too).
    internal bool CallableSignatureFits(BoundType actual, BoundType expected, Koto? own, Koto use)
        => this.CallableSubtype(actual, expected, own) && (actual is not { Kind: BoundTypeKind.Function, Components.Count: 2 } ||
            this.FailedSignatureWellFormedness(actual, Comparison(actual, own, this.CompatibleSignature(actual, expected), null), use) is null);

    // SPEC 3.8 at a use whose premises and Origins the relation reads.
    internal bool IsSubtypeAt(BoundType actual, BoundType expected, Koto use) => new RelationWalk(this, use, OriginPart.Judged).Fits(actual, expected);

    // SPEC 10.7: an implementation's result R fits a required result Q when R <: Q or R is Never, at the result's top level only: the
    // required result it is compared against.
    private static BoundType CompatibleResult(BoundType result, BoundType required) => ReferenceEquals(result, BoundType.Never) ? BoundType.Never : required;

    // SPEC 15.3.5: the layers invariant in their target: an exclusive borrow, a writable object handle, a raw pointer, and a pair
    // layer whose binder admits one of them (InvariantAdmitted), since a relation between pair layers must hold for every admitted
    // Semantics. A comparison without a Binding sees only the concrete layers.
    private static bool IsInvariantLayer(BoundType type, Binding? binding)
        => type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq or SemanticsKind.Raw || (binding is not null && binding.InvariantAdmitted(type));

    // The Callable comparison of `actual`, whose per-call inputs `own` binds (its own syntax when null), with `expected`, nested in the
    // comparisons `outer` names.
    private static CallableInstance Comparison(BoundType actual, Koto? own, BoundType expected, ComparisonPremises? outer)
        => new(actual, own ?? FunctionTypeBinder(actual), expected, FunctionTypeBinder(expected), outer);

    // A signature whose inputs are fresh per-call borrows and whose result has no Origin or one over those inputs alone.
    private static bool PerCallShape(BoundType signature, Koto? own, bool any = false)
        => PerCallSignature(signature, own, any) || InputDependentBinder(signature, own, any) is not null;

    // SPEC 10.7: within one Callable comparison, `a` outlives `b` after the implementation's call-time Origins are instantiated. The
    // required per-call Origins are rigid: one outlives another only at the same position or by the own well-formedness of the
    // comparison's required signatures (the owner's (a)), and a fixed Origin outlives one only when it is static, since the call that
    // binds it may come after every point of the enclosing body. A meet outlives an Origin when each operand does, and an Origin
    // outlives a meet when it outlives one operand.
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
            // A required per-call Origin relates to another only through the required signatures' own well-formedness.
            return binding is not null && binding.ProvesOriginOutlives(a, b, use!, instance);
        }

        if (b is { Kind: OriginKind.Inference, Open: true } && instance.IsResultOnlyOrigin(a))
        {
            return true;
        }

        return binding is null ? OriginOutlives(a, b) : binding.FitOrigin(a, b, use!, instance);
    }

    // The required signature an implementation's is compared against (CompatibleResult): the required one, its result replaced.
    private BoundType CompatibleSignature(BoundType implementation, BoundType required)
    {
        if (implementation.Kind != BoundTypeKind.Function || required.Kind != BoundTypeKind.Function ||
            implementation.Components.Count != 2 || required.Components.Count != 2)
        {
            return required;
        }

        return ReferenceEquals(CompatibleResult(implementation.Components[1], required.Components[1]), required.Components[1]) ? required
            : this.types.Rebuild(required, [required.Components[0], BoundType.Never], required.Origin, (BoundOrigin[])required.OriginArguments);
    }

    // SPEC 10.7, 15.6.1, 23.1: the first member of a conversion whose Origin part fails under the call-time instantiation: the n-th
    // parameter, whose required input must outlive the implementation's, or the result, which must outlive the required one. Both ends
    // are instantiated, so neither is a call-time Origin of the implementation.
    private OriginContractFact? ConversionContractFailure(BoundType actual, BoundType expected, Koto? own, Koto at, Koto use)
    {
        if (actual.Components.Count != 2 || expected.Components.Count != 2)
        {
            return null;
        }

        var instance = Comparison(actual, own, expected, null);
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

        if (this.FailedInstancePart(actual.Components[1], expected.Components[1], false, instance, use) is { } result)
        {
            return new(at, "the result", result.Longer, result.Shorter, result.Equality);
        }

        // The owner's (a): the implementation's own well-formedness, proven from the required signature's (the one judgment).
        if (this.FailedSignatureWellFormedness(actual, instance, use) is not { } formed)
        {
            return null;
        }

        var member = formed.Part < inputs.Components.Count ? $"the {Ordinal(formed.Part + 1)} parameter's well-formedness" : "the result's well-formedness";
        return new(at, member, this.Instantiated(formed.Longer, instance), this.Instantiated(formed.Shorter, instance), false);

        static string Ordinal(int n) => n + (n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    }

    // The first Origin position, in the order of FailedOriginRelation, at which `longer` does not fit `shorter` under the instantiation.
    private (BoundOrigin Longer, BoundOrigin Shorter, bool Equality)? FailedInstancePart(BoundType longer, BoundType shorter, bool invariant, in CallableInstance instance, Koto use, int depth = 0)
    {
        if (depth > 64 || longer.Components.Count != shorter.Components.Count || longer.OriginArguments.Count != shorter.OriginArguments.Count)
        {
            return null;
        }

        if (FitOuterOrigins(longer, shorter, this, use, out var a, out var b) == OuterOriginFit.Relation &&
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
            return whole.Symbol!.Kind == BindingSymbolKind.SemanticsTarget ? whole.Symbol.Type! : this.types.Intern(BoundTypeKind.TargetProjection, whole.Symbol, SemanticsKind.Owner, []);
        }

        return whole;
    }

    // A Type with its binder's slots, lengths and pair Origins substituted. One outermost call is one query of the substitution table, its
    // binder, arguments and lengths fixed, so a part shared within the Type is substituted once (DECISIONS BU-19).
    private BoundType? SubstituteType(BoundType type, Koto binder, ReadOnlySpan<BoundType?> arguments, ReadOnlySpan<BoundLength?> lengths = default)
        => this.SubstitutePart(type, binder, arguments, lengths, this.substitutedParts.Begin(this.storageVersion));

    // A part of a substitution query. Without length arguments a part with neither an abstract part nor an Origin is its own image: every
    // arm that changes a part substitutes a slot of an abstract kind, a length or an Origin.
    private BoundType? SubstitutePart(BoundType type, Koto binder, ReadOnlySpan<BoundType?> arguments, ReadOnlySpan<BoundLength?> lengths, uint query)
    {
        if (lengths.IsEmpty && !type.HasAbstractPart && !type.CarriesAnyOrigin)
        {
            return type;
        }

        if (type.Components.Count == 0)
        {
            return this.SubstituteTypeCore(type, binder, arguments, lengths, query);
        }

        if (!this.substitutedParts.TryGet(type, query, out var image))
        {
            image = this.SubstituteTypeCore(type, binder, arguments, lengths, query);
            this.substitutedParts.Set(type, query, image);
        }

        return image;
    }

    private BoundType? SubstituteTypeCore(BoundType type, Koto binder, ReadOnlySpan<BoundType?> arguments, ReadOnlySpan<BoundLength?> lengths, uint query)
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
                return FamilyCase(type, selection.Semantics) is { } child ? this.SubstitutePart(child, binder, arguments, lengths, query) : null;
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

            // SPEC 8.1.2: an original `s/T` denotes the pair's binding (TyCtxt.PairBinding), its projection `T` the direct target.
            var projected = type.Kind == BoundTypeKind.TargetProjection ? this.DirectTarget(whole)
                : type.Symbol is { Kind: BindingSymbolKind.SemanticsTarget } ? this.types.PairBinding(whole) : whole;
            if (type.Origin is { } replacement && (IsBorrow(projected.Semantics) || projected.Kind is BoundTypeKind.Parameter or BoundTypeKind.SemanticsApplication or BoundTypeKind.SemanticsAdaptation))
            {
                // SPEC 8.1.2: the annotation binds the outer-Origin slot of a borrow binding; a value binding keeps no slot,
                // and an abstract binding keeps it conditional.
                projected = this.WithOrigins(projected, this.SubstitutePairOrigins(replacement, binder, arguments), (BoundOrigin[])projected.OriginArguments);
            }

            return projected;
        }

        if (type.Components.Count == 0 && type.LengthArguments.Length == 0 && !type.CarriesOrigin)
        {
            return type;
        }

        var scratch = this.RentTypes(type.Components.Count);
        var itemLengths = this.lengthScratch.Rent(type.LengthArguments.Length);
        BoundOrigin[]? origins = null;
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
                var substituted = this.SubstitutePart(type.Components[i], binder, arguments, lengths, query);
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

                return this.ApplySemantics(whole, scratch[0], type.Origin is { } slotOrigin ? this.SubstitutePairOrigins(slotOrigin, binder, arguments) : null);
            }

            // SPEC 8.1.1, 8.1.2: every `o` of the binder's pairs, as an outer Origin, a meet operand or an Origin argument, is bound
            // through its Type argument.
            var origin = type.Origin is { } outer ? this.SubstitutePairOrigins(outer, binder, arguments) : null;
            if (type.RawOrigin is { } raw)
            {
                origin = this.SubstitutePairOrigins(raw, binder, arguments);
            }

            changed |= !ReferenceEquals(origin, type.Origin ?? type.RawOrigin);
            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                var argument = this.SubstitutePairOrigins(type.OriginArguments[i], binder, arguments);
                if (origins is null && !ReferenceEquals(argument, type.OriginArguments[i]))
                {
                    origins = this.originScratch.Rent(type.OriginArguments.Count);
                    for (var j = 0; j < i; j++)
                    {
                        origins[j] = type.OriginArguments[j];
                    }
                }

                if (origins is not null)
                {
                    origins[i] = argument;
                }
            }

            changed |= origins is not null;
            return changed
                ? this.types.Intern(type.Kind, type.Symbol, type.Semantics, scratch.AsSpan(0, type.Components.Count), length, origin, origins is null ? (BoundOrigin[])type.OriginArguments : origins.AsSpan(0, type.OriginArguments.Count), expression, type.ClosureContext, itemLengths.AsSpan(0, type.LengthArguments.Length))
                : type;
        }
        finally
        {
            if (origins is not null)
            {
                this.originScratch.Return(origins, clearArray: true);
            }

            this.typeScratch.Return(scratch, clearArray: true);
            this.lengthScratch.Return(itemLengths, clearArray: true);
        }
    }
}
