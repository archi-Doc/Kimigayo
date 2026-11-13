// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The Type relation (SPEC 3.8, 15.3.5, 15.6.1; DECISIONS BU-4): subtyping, identity up to Origin variance, read by Type-to-Type
// comparisons (identity tests, slot unification, ranking, contract and layer matching) through IsSubtype, IsSubtypeAt and
// FitsStructuralPart, and by the value fits (Binding.ValueFits.cs) below their root. Never relates only to itself: a
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

    // IsSubtype with the walk's first failure, for a comparison that records its mismatch (FailMismatch).
    internal static bool IsSubtype(BoundType actual, BoundType expected, out ComparisonFailure failure)
    {
        var walk = new RelationWalk(null, null, OriginPart.Judged);
        var fits = walk.Fits(actual, expected);
        failure = walk.Failure;
        return fits;
    }

    // SPEC 10.4 step 2, 15.6.1: the structural part with every Origin binding treated as equal, inside Function Types too, since Origin
    // bindings never rank candidates.
    internal static bool FitsStructuralPart(BoundType actual, BoundType expected)
        => new RelationWalk(null, null, OriginPart.Everywhere).Fits(actual, expected);

    // SPEC 10.7, 15.6.1: the part of callable compatibility that applicability reads: subtyping against the required signature, its
    // result Never when the implementation's is (CompatibleResult), judged without recording a bound and without premises; `own` is the
    // declaration whose per-call inputs the implementation's signature names. A Function Item's conditions are ItemContractFits'.
    internal bool CallableSubtype(BoundType actual, BoundType expected, Koto? own, out ComparisonFailure failure)
    {
        var walk = new RelationWalk(null, null, OriginPart.Contract);
        var fits = walk.Fits(actual, this.CompatibleSignature(actual, expected), own: own);
        failure = walk.Failure;
        return fits;
    }

    // SPEC 10.7, 15.3.7, 15.6.1: callable compatibility of a Closure's or a Function value's signature with a required one at `use`, a
    // whole-contract comparison: its member Origins and the implementation's conditions, its own well-formedness among them (the
    // owner's (a); FunctionFits), are proven from the required signature's premises and those of `use` alone (DECISIONS BU-93).
    internal bool CallableSignatureFits(BoundType actual, BoundType expected, Koto? own, Koto use, out ComparisonFailure failure)
    {
        var walk = new RelationWalk(this, use, OriginPart.Contract);
        var fits = walk.Fits(actual, this.CompatibleSignature(actual, expected), own: own);
        failure = walk.Failure;
        return fits;
    }

    // SPEC 3.8 at a use whose premises and Origins the relation reads, in the whole-contract comparison `comparison` (SPEC 15.3.7), whose
    // required signature's own well-formedness its proofs assume.
    internal bool IsSubtypeAt(BoundType actual, BoundType expected, Koto use, in CallableInstance comparison)
        => new RelationWalk(this, use, OriginPart.Judged).Fits(actual, expected, instance: comparison);

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

    // The images of an occurrence's declared Origin slots: its Origin arguments, or a Slice's one slot, its layer Origin, written into the
    // caller's `single` so that no Slice occurrence allocates.
    private static ReadOnlySpan<BoundOrigin> OccurrenceOrigins(BoundType owner, Span<BoundOrigin> single)
    {
        if (owner.Kind == BoundTypeKind.Slice && owner.Origin is { } origin)
        {
            single[0] = origin;
            return single;
        }

        return (BoundOrigin[])owner.OriginArguments;
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

    // A Type with its binder's slots, lengths and pair Origins and the images of its Origin binder's declared slots and per-call atoms
    // substituted in one pass, so an argument's own Origins are never mapped again (PLAN G425, G457). The Origin binder is `binder`, or a
    // constructor whose Type slots its structure owns (CallSlotOwner) or an accessor's binder (OriginBinder). One outermost call is one query
    // of the substitution table, its binder, arguments, lengths and images fixed, so a part shared within the Type is substituted once
    // (DECISIONS BU-19).
    private BoundType? SubstituteType(
        BoundType type,
        Koto binder,
        ReadOnlySpan<BoundType?> arguments,
        ReadOnlySpan<BoundLength?> lengths = default,
        ReadOnlySpan<BoundOrigin> origins = default,
        PerCallImages images = default,
        Koto? originBinder = null)
        => this.Substitute(type, new CallSubstitution(originBinder ?? binder, arguments, lengths, origins) { SlotOwner = binder, Images = images });

    // An Origin of a declaration-terms Type under a substitution, each atom mapped once: its SlotImage under the Origin binder's images, else
    // a pair's `o` through its Type argument (PairOuterImage); a meet is rebuilt from its operands' images only when one of them changes.
    private BoundOrigin SubstituteSlotOrigin(BoundOrigin origin, in CallSubstitution substitution)
    {
        if (origin.Kind != OriginKind.Intersection)
        {
            return this.AtomImage(origin, substitution) ?? origin;
        }

        var changed = false;
        for (var i = 0; i < origin.Operands.Count && !changed; i++)
        {
            changed = this.AtomImage(origin.Operands[i], substitution) is { } image && !ReferenceEquals(image, origin.Operands[i]);
        }

        if (!changed)
        {
            return origin;
        }

        var result = this.AtomImage(origin.Operands[0], substitution) ?? origin.Operands[0];
        for (var i = 1; i < origin.Operands.Count; i++)
        {
            result = this.Meet(result, this.AtomImage(origin.Operands[i], substitution) ?? origin.Operands[i]);
        }

        return result;
    }

    // Without images no atom has a SlotImage, so the common substitution of Type slots alone reads the pair arm only.
    private BoundOrigin? AtomImage(BoundOrigin origin, in CallSubstitution substitution)
        => (substitution.Origins.IsEmpty && substitution.Images.IsEmpty ? null : this.SlotImage(origin, substitution.Binder, substitution.Origins, substitution.Images, default)) ??
            this.PairOuterImage(origin, substitution.SlotOwner, substitution.Types) ??
            (substitution.Declaring is null ? null : this.DeclaringImage(origin, substitution.SlotOwner, substitution.Declaring));

    // SPEC 15.6.4 (PLAN G457, G707): an atom's image under the occurrence of the Type declaring the binder, the second binder of the one
    // pass: its Origin arguments' (OccurrenceOrigins) and, where the binder does not own its Type slots (a constructor's structure does),
    // a pair's `o` through its Type argument.
    private BoundOrigin? DeclaringImage(BoundOrigin origin, Koto binder, BoundType declaring)
    {
        Span<BoundOrigin> single = [BoundOrigin.Static];
        var outer = declaring.Symbol!.Declaration;
        var origins = OccurrenceOrigins(declaring, single);
        return (origins.IsEmpty ? null : this.SlotImage(origin, outer, origins, default, default)) ??
            (ReferenceEquals(outer, binder) ? null : this.PairOuterImage(origin, outer, (BoundType[])declaring.Components));
    }

    // A part of a substitution query. Without length arguments a part with neither an abstract part nor an Origin is its own image: every
    // arm that changes a part substitutes a slot of an abstract kind, a length, an Origin or an interpolation-bound term's value Types (an
    // abstract part, BoundLength.HasAbstractPart).
    private BoundType? SubstitutePart(BoundType type, in CallSubstitution substitution, uint query)
    {
        if (substitution.Lengths.IsEmpty && !type.HasAbstractPart && !type.CarriesAnyOrigin)
        {
            return type;
        }

        if (type.Components.Count == 0)
        {
            return this.SubstituteTypeCore(type, substitution, query);
        }

        if (!this.substitutedParts.TryGet(type, query, out var image))
        {
            image = this.SubstituteTypeCore(type, substitution, query);
            this.substitutedParts.Set(type, query, image);
        }

        return image;
    }

    private BoundType? SubstituteTypeCore(BoundType type, in CallSubstitution substitution, uint query)
    {
        var slot = substitution.Slot(type.Symbol, out var slotArguments);
        if (type.Kind == BoundTypeKind.SemanticsAdaptation && slot >= 0 && slot < slotArguments.Length && slotArguments[slot] is { } selection)
        {
            if (selection.Kind == BoundTypeKind.SemanticsAdaptation)
            {
                return this.SubstituteFamilyArgument(type, substitution, slot, selection);
            }

            if (!IsAbstractBinding(selection))
            {
                return FamilyCase(type, selection.Semantics) is { } child ? this.SubstitutePart(child, substitution, query) : null;
            }
        }

        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection && slot >= 0)
        {
            if ((uint)slot >= (uint)slotArguments.Length)
            {
                return null;
            }

            var whole = slotArguments[slot];
            if (whole is null)
            {
                return null;
            }

            // SPEC 8.1.2: an original `s/T` denotes the pair's binding (TyCtxt.PairBinding), its projection `T` the direct target.
            var projected = type.Kind == BoundTypeKind.TargetProjection ? this.DirectTarget(whole)
                : type.Symbol is { Kind: BindingSymbolKind.SemanticsTarget } ? this.types.PairBinding(whole) : whole;
            return type.Origin is { } replacement && HasOuterSlot(projected)
                ? this.WithOrigins(projected, this.SubstituteSlotOrigin(replacement, substitution), (BoundOrigin[])projected.OriginArguments) : projected;
        }

        // DECISIONS BU-60: the Type instance a closure's context holds is a part of the Closure Type.
        var context = type.ClosureContext;
        if (TyCtxt.HeldType(type) is { } held)
        {
            if (this.SubstitutePart(held, substitution, query) is not { } image)
            {
                return null;
            }

            context = ReferenceEquals(image, held) ? context : this.types.HeldContext(type.Symbol!, image);
        }

        if (type.Components.Count == 0 && type.LengthArguments.Length == 0 && !type.CarriesOrigin)
        {
            return this.WithContext(type, context);
        }

        var scratch = this.RentTypes(type.Components.Count);
        var itemLengths = this.lengthScratch.Rent(type.LengthArguments.Length);
        BoundOrigin[]? rebuilt = null;
        try
        {
            var length = type.Length;
            var expression = type.LengthExpression;
            if (expression is not null && (!substitution.Lengths.IsEmpty || expression.HasAbstractPart))
            {
                expression = this.SubstituteLength(expression, substitution);
                if (expression is null || (expression.IsConstant && !ValidLength(expression.Value)))
                {
                    return null;
                }

                if (expression.IsConstant)
                {
                    length = expression.Value;
                    expression = null;
                }
            }

            var changed = length != type.Length || !ReferenceEquals(expression, type.LengthExpression) || !ReferenceEquals(context, type.ClosureContext);
            for (var i = 0; i < type.LengthArguments.Length; i++)
            {
                var argument = type.LengthArguments[i];
                itemLengths[i] = argument is null || substitution.Lengths.IsEmpty ? argument : this.SubstituteLength(argument, substitution);
                if (argument is not null && (itemLengths[i] is null || (itemLengths[i]!.IsConstant && !ValidLength(itemLengths[i]!.Value))))
                {
                    return null;
                }

                changed |= !ReferenceEquals(argument, itemLengths[i]);
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                var substituted = this.SubstitutePart(type.Components[i], substitution, query);
                if (substituted is null)
                {
                    return null;
                }

                scratch[i] = substituted;
                changed |= !ReferenceEquals(substituted, type.Components[i]);
            }

            if (type.Kind is BoundTypeKind.SemanticsApplication or BoundTypeKind.SemanticsAdaptation && slot >= 0)
            {
                if ((uint)slot >= (uint)slotArguments.Length)
                {
                    return null;
                }

                var whole = slotArguments[slot];
                if (whole is null)
                {
                    return null;
                }

                if (type.Kind == BoundTypeKind.SemanticsAdaptation)
                {
                    // The re-formed cases may be the binder's application (TypeFamily), which the argument applies as any application.
                    var family = this.TypeFamily(type.Symbol!, (SemanticsMask)type.Length, scratch.AsSpan(0, type.Components.Count));
                    return family.Kind == BoundTypeKind.SemanticsAdaptation ? this.SubstituteFamily(family, whole)
                        : TryPairLayer(family, out var formed, out var target) && ReferenceEquals(formed, type.Symbol!.WholeType)
                        ? this.ApplySemantics(whole, target, family.Origin) : family;
                }

                return this.ApplySemantics(whole, scratch[0], type.Origin is { } slotOrigin ? this.SubstituteSlotOrigin(slotOrigin, substitution) : null);
            }

            // SPEC 8.1.1, 8.1.2: every `o` of the binder's pairs, as an outer Origin, a meet operand or an Origin argument, is bound
            // through its Type argument.
            var origin = type.Origin is { } outer ? this.SubstituteSlotOrigin(outer, substitution) : null;
            if (type.RawOrigin is { } raw)
            {
                origin = this.SubstituteSlotOrigin(raw, substitution);
            }

            changed |= !ReferenceEquals(origin, type.Origin ?? type.RawOrigin);
            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                var argument = this.SubstituteSlotOrigin(type.OriginArguments[i], substitution);
                if (rebuilt is null && !ReferenceEquals(argument, type.OriginArguments[i]))
                {
                    rebuilt = this.originScratch.Rent(type.OriginArguments.Count);
                    for (var j = 0; j < i; j++)
                    {
                        rebuilt[j] = type.OriginArguments[j];
                    }
                }

                if (rebuilt is not null)
                {
                    rebuilt[i] = argument;
                }
            }

            changed |= rebuilt is not null;
            return changed
                ? this.types.Intern(type.Kind, type.Symbol, type.Semantics, scratch.AsSpan(0, type.Components.Count), length, origin, rebuilt is null ? (BoundOrigin[])type.OriginArguments : rebuilt.AsSpan(0, type.OriginArguments.Count), expression, context, itemLengths.AsSpan(0, type.LengthArguments.Length))
                : type;
        }
        finally
        {
            if (rebuilt is not null)
            {
                this.originScratch.Return(rebuilt, clearArray: true);
            }

            this.typeScratch.Return(scratch, clearArray: true);
            this.lengthScratch.Return(itemLengths, clearArray: true);
        }
    }
}
