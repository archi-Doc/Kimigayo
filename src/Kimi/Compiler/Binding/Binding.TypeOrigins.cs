// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private Dictionary<Koto, (OriginParameter Slot, LoanRequirement Failing, PairCase[] Cases)>? invalidStaticOriginSlots;

    // Complete stored Origin identity, including declared slots and intersections; shared by fitting and diagnostics.
    internal static bool ContainsOrigin(BoundType type, BoundOrigin origin)
    {
        if (type.Origin is { } own && ContainsOrigin(own, origin))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (ContainsOrigin(type.OriginArguments[i], origin))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (ContainsOrigin(type.Components[i], origin))
            {
                return true;
            }
        }

        return false;
    }

    internal BoundType SelfType(BindingSymbol symbol)
    {
        var schema = symbol.Schema!;
        var types = this.RentTypes(schema.GenericSlots.Count);
        var origins = this.originScratch.Rent(schema.Origins.Count);
        try
        {
            for (var i = 0; i < schema.GenericSlots.Count; i++)
            {
                var slot = schema.GenericSlots[i];
                // An invalid nominal length header is diagnosed by BindSchemas; its recovery Type must still be complete.
                types[i] = slot.Kind == GenericSlotKind.Length ? BoundType.Never : slot.Symbol.WholeType!;
            }

            for (var i = 0; i < schema.Origins.Count; i++)
            {
                origins[i] = schema.Origins[i].Origin;
            }

            if (schema.GenericSlots.Count == 0 && schema.Origins.Count == 0)
            {
                return symbol.Type!;
            }

            return this.types.Intern(schema.GenericSlots.Count == 0 ? BoundTypeKind.Nominal : BoundTypeKind.Constructed, symbol, SemanticsKind.Owner, types.AsSpan(0, schema.GenericSlots.Count), originArguments: origins.AsSpan(0, schema.Origins.Count));
        }
        finally
        {
            this.typeScratch.Return(types, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
    }

    // Whether an Origin expression names an atom, itself or through a meet's operands.
    private static bool ContainsOrigin(BoundOrigin expression, BoundOrigin atom)
    {
        if (ReferenceEquals(expression, atom))
        {
            return true;
        }

        for (var i = 0; i < expression.Operands.Count; i++)
        {
            if (ContainsOrigin(expression.Operands[i], atom))
            {
                return true;
            }
        }

        return false;
    }

    private static Koto OriginFormationSite(Koto node, BoundType type)
    {
        var declaration = node is VariableKoto ? node : node.BoundSymbol?.Declaration;
        Koto? annotation = declaration switch
        {
            VariableKoto variable => variable.TypeKoto,
            FunctionKoto function => function.ReturnType,
            _ => null,
        };
        var site = annotation is not null && ReferenceEquals(annotation.BoundType, type) ? annotation : node;
        while (site.Parent is TypeKoto parent && ReferenceEquals(parent.BoundType, type))
        {
            site = parent;
        }

        return site;
    }

    private BoundType WithOrigins(BoundType type, BoundOrigin? origin, ReadOnlySpan<BoundOrigin> arguments)
        => type.Kind == BoundTypeKind.SemanticsAdaptation ? this.TransformFamily(type, FamilyTransform.Origin, origin)
            : this.types.Rebuild(type, (BoundType[])type.Components, origin, arguments);

    private BoundType? CompleteOrigins(BoundType type, TypeSemanticsKoto? annotation, Koto use, BindingScope scope, TypeBindingContext context)
    {
        var bindingSet = annotation?.BindingSetName is not null;
        var written = annotation is { HasOrigin: true } && !bindingSet;
        if (bindingSet)
        {
            if (type.Symbol?.Schema is not { Origins.Count: > 0 })
            {
                return this.Fail(use, BindingFailure.InvalidOrigin);
            }

            annotation!.OriginExpression!.BindingState = BindingState.Resolved;
            context = context with { SuppressOuter = false };
        }

        if (written && type.Kind == BoundTypeKind.Parameter && annotation?.SemanticsParameter is null)
        {
            return this.Fail(use, BindingFailure.InvalidOrigin);
        }

        if (IsBorrow(type.Semantics) || type.Kind is BoundTypeKind.Slice or BoundTypeKind.SemanticsApplication || (type.Kind == BoundTypeKind.Parameter && written))
        {
            if (annotation?.OriginArguments is not null)
            {
                return this.Fail(use, BindingFailure.InvalidOrigin);
            }

            var origin = written ? annotation!.OriginExpression is { } expression ? this.BindOrigin(expression, scope) : this.BindOriginName(annotation.OriginName!, use, scope) : type.Origin;
            if (written && type.Origin is not null && !ReferenceEquals(type.Origin, origin) && annotation?.SemanticsParameter is null)
            {
                return this.Fail(use, BindingFailure.InvalidOrigin);
            }

            if (written && type.Kind == BoundTypeKind.Parameter)
            {
                this.AddObligation(new(BindingObligationKind.TypeFormation, use, BindingDeadline.Definition, type));
            }

            if (origin is null && !written && !context.SuppressOuter)
            {
                if (type.Kind == BoundTypeKind.SemanticsApplication && !context.Direct && context.Position is not (TypePosition.Local or TypePosition.Result))
                {
                    // Definition proof must show this position needs no borrow Origin.
                    return type;
                }

                var condition = type.Kind == BoundTypeKind.SemanticsApplication ? type.Symbol : null;
                var mayBeExclusive = IsExclusive(type.Semantics) || (condition?.WholeType is { } whole &&
                    !this.HasSemanticsRole(whole, SemanticsMask.Owner | SemanticsMask.Ref | SemanticsMask.ObjRef | SemanticsMask.Obj | SemanticsMask.Rc | SemanticsMask.Arc | SemanticsMask.Raw, scope));
                origin = this.OmittedOrigin(use, scope, type.Kind == BoundTypeKind.Slice ? context with { Direct = false } : context, mayBeExclusive ? LoanRequirement.Uniq : LoanRequirement.Ref, type.Kind == BoundTypeKind.Slice ? 0 : -1, condition);
                if (type.Kind == BoundTypeKind.SemanticsApplication && origin?.Kind == OriginKind.Input && context.Direct)
                {
                    origin.BorrowCondition = type.Symbol;
                }
            }

            if (origin is null && (written || !context.SuppressOuter))
            {
                return null;
            }

            if (origin?.Kind == OriginKind.Static && IsExclusive(type.Semantics))
            {
                return this.Fail(use, BindingFailure.InvalidOrigin);
            }

            if (!ReferenceEquals(origin, type.Origin))
            {
                type = this.WithOrigins(type, origin, (BoundOrigin[])type.OriginArguments);
            }

            // A conditional pair slot exists only in admitted borrow cases. Its payload keeps its own Origins
            // in every case, but value/object cases impose no outlives relation to an inactive outer slot. A nested signature's input (an
            // anonymous function's or a Function Type's parameter) forms none: its well-formedness is a premise of that signature (R2) and
            // an obligation of each call (RequireInputPremises), never a relation of the body it is written in (SPEC 15.3.4, 15.6.1).
            if (origin is not null && type.Components.Count != 0 && !InactiveOuterOrigin(type, this, use) &&
                context is not { Position: TypePosition.Parameter, Owner: FunctionKoto { IsAnonymous: true } or FunctionTypeKoto })
            {
                // SPEC 8.1.2, 15.6.4: a direct borrowed input of a struct, or a pair layer whose outer slot is active only for a borrow
                // binding, guarantees its own stored Origins outlive the borrow: each call judges them as the instantiated input's
                // well-formedness (RequireInputPremises), whether its argument is a Borrow formed there or an existing reference.
                var discharge = context is { Position: TypePosition.Parameter, Direct: true, Owner: FunctionKoto function } &&
                    origin is { Kind: OriginKind.Input } && ReferenceEquals(origin.Binder, function) && origin.Slot == context.Slot &&
                    (ReferenceTypes.IsStruct(type) || TryPairLayer(type, out _, out _)) ? OriginDischarge.CallBorrows : OriginDischarge.Judgment;
                this.RetainInnerOutlives(type.Components[0], origin, use, discharge, RequiredCondition(this.PairSlotCondition(type, use)));
            }
        }
        else if (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw })
        {
            // SPEC 5.7: a raw layer states the Origin its `during` writes (WrittenRawOrigin) or none, and forms no borrow Origin.
            if (written)
            {
                if (this.WrittenRawOrigin(annotation!, use, scope, context.Position) is not { } raw)
                {
                    return null;
                }

                type = this.types.Pointer(type.Components[0], raw);
            }
        }
        else
        {
            var target = type;
            var objectLayer = type.Kind == BoundTypeKind.Semantics && type.Semantics is SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc;
            if (objectLayer)
            {
                target = type.Components[0];
            }

            var schema = target.Symbol?.Schema;
            var count = schema?.Origins.Count ?? 0;

            // SPEC 15.3.1: a trailing during binds the only slot of a one-slot schema. It adds no borrow layer and never
            // rebinds a complete Type; several slots need a binding set and relations.
            if (written && (objectLayer || count != 1 || annotation!.OriginArguments is not null ||
                (target.OriginArguments.Count == 1 && target.OriginArguments[0] is not null)))
            {
                return this.Fail(use, BindingFailure.InvalidOrigin);
            }

            if (count != 0 && (written || (!context.SuppressOuter && (target.OriginArguments.Count != count || target.OriginArguments.Contains(null!)))))
            {
                var arguments = this.originScratch.Rent(count);
                Array.Clear(arguments, 0, count);
                try
                {
                    for (var i = 0; i < target.OriginArguments.Count; i++)
                    {
                        arguments[i] = target.OriginArguments[i];
                    }

                    if (written)
                    {
                        var slot = annotation!.OriginExpression is { } expression ? this.BindOrigin(expression, scope) : this.BindOriginName(annotation.OriginName!, use, scope);
                        if (slot is null)
                        {
                            return null;
                        }

                        arguments[0] = slot;
                    }

                    for (var i = 0; i < count; i++)
                    {
                        if (arguments[i] is not null)
                        {
                            continue;
                        }

                        var requirement = this.RequirementAt(schema!.Origins[i], target).Strongest;
                        var bound = this.OmittedOrigin(use, scope, context with { Direct = false }, requirement, i);
                        if (bound is null)
                        {
                            return null;
                        }

                        arguments[i] = bound;
                    }

                    target = this.WithOrigins(target, target.Origin, arguments.AsSpan(0, count));
                    type = objectLayer ? this.types.Rebuild(type, [target], type.Origin, []) : target;
                }
                finally
                {
                    this.originScratch.Return(arguments, clearArray: true);
                }
            }
        }

        // Transparent syntax must expose the same completed type as its normalized owner.
        for (var node = use; ;)
        {
            Koto? inner = node switch
            {
                ParenthesizedTypeKoto p => p.Type,
                TypeSemanticsKoto s when s.Type is not null && (s.IsTransparentWrapper || (s.SemanticsParameter is null && s.SemanticsKind == SemanticsKind.Owner)) => s.Type,
                _ => null,
            };
            if (inner is null)
            {
                break;
            }

            Complete(inner, type);
            node = inner;
        }

        return type;
    }

    // `discharge` applies to the Origins stored directly in `inner`; those of its components are judged. `condition` is the Semantics
    // case in which `outer` stores `inner`; a pair layer's own slot, the implicit `o` of an original `s/T` included, is stored only in
    // its binder's borrow cases (SPEC 15.6.5).
    private void RetainInnerOutlives(BoundType inner, BoundOrigin outer, Koto use, OriginDischarge discharge = OriginDischarge.Judgment, ulong condition = 0)
    {
        if (!inner.StoresOrigin)
        {
            return;
        }

        if (this.StoredSlot(inner, use, out var slot) is { } origin && !OriginOutlives(origin, outer))
        {
            this.AddObligation(new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, inner, origin, outer, Discharge: discharge, Condition: condition | RequiredCondition(slot)));
        }

        for (var i = 0; i < inner.OriginArguments.Count; i++)
        {
            var argument = inner.OriginArguments[i];
            if (!OriginOutlives(argument, outer))
            {
                this.AddObligation(new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, inner, argument, outer, Discharge: discharge, Condition: condition));
            }
        }

        for (var i = 0; i < inner.Components.Count; i++)
        {
            this.RetainInnerOutlives(inner.Components[i], outer, use, condition: condition);
        }
    }

    private void AccumulateRequirements(BoundType type, DeclarationSchema schema, int polarity, ref bool changed)
    {
        // Only Origins and Type Parameter slots contribute; other subtrees cannot change a summary.
        if (!type.CarriesOriginOrSlot)
        {
            return;
        }

        if (type.Kind == BoundTypeKind.Parameter)
        {
            for (var i = 0; i < schema.GenericSlots.Count; i++)
            {
                var slot = schema.GenericSlots[i];
                if (!ReferenceEquals(slot.Symbol, type.Symbol))
                {
                    continue;
                }

                var variance = polarity == 0 ? OriginVariance.Invariant : polarity > 0 ? OriginVariance.Covariant : OriginVariance.Contravariant;
                var combined = slot.OriginVariance == OriginVariance.Unused ? variance : slot.OriginVariance == variance ? variance : OriginVariance.Invariant;
                if (combined != slot.OriginVariance)
                {
                    slot.OriginVariance = combined;
                    changed = true;
                }
            }
        }

        if (type.Origin is { } origin)
        {
            // SPEC 15.3.5 (DECISIONS BU-44): a pair layer's slot is a conditional use (ConditionalUse), kept by its target in those cases;
            // a use of none still counts toward variance.
            var conditional = TryPairLayer(type, out var whole, out var target);
            var loan = IsExclusive(type.Semantics) ? LoanRequirement.Uniq : LoanRequirement.Ref;
            var use = conditional ? this.ConditionalUse(schema, whole) : new SlotRequirement(loan, default);
            this.AccumulateOrigin(origin, schema, polarity, use, ref changed);
            if (use != default)
            {
                this.Keep(schema, origin, conditional ? target : this.BorrowReferent(type, out _, out _), null, use.Binders);
            }
        }

        if (type.RawOrigin is { Kind: not OriginKind.Holder } raw)
        {
            // SPEC 5.7: a raw layer's Origin is a covariant position, no use of a slot for Loan requirements or Phantom status.
            this.AccumulateOrigin(raw, schema, polarity, default, ref changed);
        }

        // A Type may carry fewer or more arguments than its declaration while a pass is
        // still completing; only the slots the declaration actually owns carry requirements.
        var declared = type.Symbol?.Schema;
        for (var i = 0; i < type.OriginArguments.Count && i < (declared?.Origins.Count ?? 0); i++)
        {
            var source = declared!.Origins[i];
            if (source.Variance == OriginVariance.Unused)
            {
                continue;
            }

            var sign = source.Variance == OriginVariance.Invariant ? 0 : source.Variance == OriginVariance.Contravariant ? -polarity : polarity;
            var use = this.NestedUse(source.Requirement, type, schema);
            this.AccumulateOrigin(type.OriginArguments[i], schema, sign, use, ref changed);
            if (use != default)
            {
                this.Keep(schema, type.OriginArguments[i], type, source, default);
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            var sign = IsInvariantLayer(type, this) ? 0 : type.Kind == BoundTypeKind.Function && i == 0 ? -polarity : polarity;
            if (type.Kind == BoundTypeKind.Constructed && declared is { } target && i < target.GenericSlots.Count)
            {
                var variance = target.GenericSlots[i].OriginVariance;
                if (variance == OriginVariance.Unused)
                {
                    continue;
                }

                sign = variance == OriginVariance.Invariant ? 0 : variance == OriginVariance.Contravariant ? -polarity : polarity;
            }

            this.AccumulateRequirements(type.Components[i], schema, sign, ref changed);
        }
    }

    private void AccumulateOrigin(BoundOrigin origin, DeclarationSchema schema, int polarity, SlotRequirement use, ref bool changed)
    {
        if (origin.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                this.AccumulateOrigin(origin.Operands[i], schema, polarity, use, ref changed);
            }

            return;
        }

        for (var i = 0; i < schema.Origins.Count; i++)
        {
            var parameter = schema.Origins[i];
            if (!ReferenceEquals(parameter.Origin, origin))
            {
                continue;
            }

            var variance = polarity == 0 ? OriginVariance.Invariant : polarity > 0 ? OriginVariance.Covariant : OriginVariance.Contravariant;
            var combined = parameter.Variance == OriginVariance.Unused ? variance : parameter.Variance == variance ? variance : OriginVariance.Invariant;
            if (combined != parameter.Variance)
            {
                parameter.Variance = combined;
                changed = true;
            }

            changed |= parameter.Absorb(use);
        }
    }

    private void ValidateOriginRequirements()
    {
        for (var n = 0; n < this.nodes.Count; n++)
        {
            var node = this.nodes[n];
            if (node.BoundType is not { } type)
            {
                continue;
            }

            if (node is TypeSemanticsKoto { BoundType.RawOrigin: { } raw } written)
            {
                this.JudgeRawOrigin(written, raw); // SPEC 5.7: every written raw Origin, after the last fixed point.
            }

            var invalid = false;
            if (type.Symbol?.Schema is { } schema)
            {
                for (var i = 0; i < type.OriginArguments.Count && i < schema.Origins.Count; i++)
                {
                    if (type.OriginArguments[i].Kind == OriginKind.Static &&
                        this.RequirementAt(schema.Origins[i], type).Strongest == LoanRequirement.Uniq)
                    {
                        // A late storage summary may establish uniq after this Type was completed. The written
                        // annotation owns formation; its declaration and names only repeat the already invalid Type.
                        // SPEC 15.3.5: uniq in some case the Type selects, the record naming those cases (DECISIONS BU-44).
                        var site = OriginFormationSite(node, type);
                        var slot = schema.Origins[i];
                        var cases = this.FailingCase(slot, type, LoanRequirement.Uniq);
                        this.FailExplained(ref this.invalidStaticOriginSlots, site, BindingFailure.InvalidOrigin, (slot, LoanRequirement.Uniq, cases));
                        if (!ReferenceEquals(site, node))
                        {
                            this.CompleteDependent(node, site);
                        }

                        invalid = true;
                    }
                }
            }

            if (invalid)
            {
                continue;
            }

            if (node is PropertyKoto property && IsStoredVariable(property) && this.symbols[property].Scope.Owner is GroupKoto)
            {
                if (property.TypeKoto is { BindingState: BindingState.Invalid } annotation)
                {
                    this.CompleteDependent(property, annotation);
                    continue;
                }

                // Check the stored value under the inherited premises, not the enclosing Type. A shared static borrow
                // may be Owned; carrying a borrow is not itself a language error (SPEC 11.8, 15.4.2).
                var owned = this.ProveOwned(type, property);
                this.RequireConstraint(property, owned, this.capabilityMode);

                this.borrowVisiting.Clear();
                if (owned == ConstraintProof.Proven && this.RetainsBorrow(type, this.borrowVisiting))
                {
                    this.Fail(node, BindingFailure.Unsupported, true);
                }
            }
        }
    }

    private bool RetainsBorrow(BoundType type, HashSet<BindingSymbol> visiting)
    {
        if (IsBorrow(type.Semantics))
        {
            return true;
        }

        if (type.Semantics == SemanticsKind.Raw || type.Kind == BoundTypeKind.Function)
        {
            return false;
        }

        if (type.Symbol?.Declaration is DeclarationContainerKoto container)
        {
            if (!visiting.Add(type.Symbol))
            {
                return false;
            }

            try
            {
                if (container.BoundSymbol?.Adt is not { } shape)
                {
                    return false;
                }

                for (var i = 0; i < shape.Types.Count; i++)
                {
                    var actual = this.StoredType(shape.Types[i], type);
                    if (actual is not null && this.RetainsBorrow(actual, visiting))
                    {
                        return true;
                    }
                }
            }
            finally
            {
                visiting.Remove(type.Symbol);
            }

            return false;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (this.RetainsBorrow(type.Components[i], visiting))
            {
                return true;
            }
        }

        return false;
    }
}
