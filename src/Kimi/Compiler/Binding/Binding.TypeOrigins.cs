// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly HashSet<BindingSymbol> borrowVisiting = new(ReferenceEqualityComparer.Instance);

    internal BoundType SelfType(BindingSymbol symbol)
    {
        // A Self symbol carries its Type (SPEC 9.1: `S<Slot(0), ...>` or the Contract's Self parameter); no Type is headed by it.
        if (symbol.SelfOf is not null)
        {
            return symbol.Type!;
        }

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

    // The node whose Type a transparent wrapper returns: a parenthesized Type's, or a Semantics layer's that adds no Semantics; null for
    // any other node.
    private static Koto? TransparentInner(Koto node) => node switch
    {
        ParenthesizedTypeKoto p => p.Type,
        TypeSemanticsKoto s when s.Type is not null && (s.IsTransparentWrapper || (s.SemanticsParameter is null && s.SemanticsKind == SemanticsKind.Owner)) => s.Type,
        _ => null,
    };

    // SPEC 3.3.2, 15.3.1, 8.5.1: whether a slot binding has a target in the denoted Type: an unbound Slice, or an owner Type whose schema has
    // one Origin, not yet bound. No other Type (a borrow, raw, pair or object layer, a Type parameter, a Tuple, a Function) has one.
    private static bool HasOpenSlot(BoundType type) => type.Kind == BoundTypeKind.Slice ? type.Origin is null
        : type is { Semantics: SemanticsKind.Owner, Symbol.Schema.Origins.Count: 1 } && (type.OriginArguments.Count == 0 || type.OriginArguments[0] is null);

    // SPEC 15.3.1, 15.6.1: whether `type` is a layer whose outer Origin is its own: a borrow, a Slice or a SemanticsApplication. The one
    // kind test of the Origin branch (CompleteOrigins) and of a layer's rows (RetainLayerOutlives).
    private static bool IsOriginLayer(BoundType type) => IsBorrow(type.Semantics) || type.Kind is BoundTypeKind.Slice or BoundTypeKind.SemanticsApplication;

    private BoundType WithOrigins(BoundType type, BoundOrigin? origin, ReadOnlySpan<BoundOrigin> arguments)
        => type.Kind == BoundTypeKind.SemanticsAdaptation ? this.TransformFamily(type, FamilyTransform.Origin, origin)!
            : this.types.Rebuild(type, (BoundType[])type.Components, origin, arguments);

    private BoundType? CompleteOrigins(BoundType type, TypeSemanticsKoto? annotation, Koto use, BindingScope scope, TypeBindingContext context)
    {
        var bindingSet = annotation?.BindingSetName is not null;
        var written = annotation is { HasOrigin: true } && !bindingSet;
        if (bindingSet)
        {
            if (type.Symbol?.Schema is not { Origins.Count: > 0 })
            {
                return this.Record(use, BindingFailure.InvalidOrigin, []);
            }

            annotation!.OriginExpression!.BindingState = BindingState.Resolved;
            context = context with { SuppressOuter = false };
        }

        // SPEC 3.3.2, 15.3.1, 8.5.1: a slot binding targets the one open schema slot of the Type its name denotes; any other Type gives it no
        // target. Its written atom is bound first, its failure being its own (SPEC 23.1.2).
        if (written && annotation!.IsSlotBinding && !HasOpenSlot(type))
        {
            _ = this.WrittenOrigin(annotation, use, scope);
            return this.Record(use, BindingFailure.InvalidOrigin, []);
        }

        // A written Type parameter here is a pair binder's layer or carries Origin arguments: a slot binding's target is decided above.
        if (IsOriginLayer(type) || (type.Kind == BoundTypeKind.Parameter && written))
        {
            if (annotation?.OriginArguments is not null)
            {
                return this.Record(use, BindingFailure.InvalidOrigin, []);
            }

            var origin = written ? this.WrittenOrigin(annotation!, use, scope) : type.Origin;
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
                return this.Record(use, BindingFailure.InvalidOrigin, []);
            }

            var supplied = !ReferenceEquals(origin, type.Origin);
            if (supplied)
            {
                type = this.WithOrigins(type, origin, (BoundOrigin[])type.OriginArguments);
            }

            // A conditional pair slot exists only in admitted borrow cases. Its payload keeps its own Origins
            // in every case, but value/object cases impose no outlives relation to an inactive outer slot. A nested signature's input (an
            // anonymous function's or a Function Type's parameter) forms none: its well-formedness is a premise of that signature (R2) and
            // an obligation of each call (RequireInputPremises), never a relation of the body it is written in (SPEC 15.3.4, 15.6.1).
            // A transparent wrapper returns its inner node's Type, whose well-formedness that node formed: it forms the relation only
            // when it supplies the outer Origin itself (SPEC 23.1.2: one requirement, one record). A layer of a local's annotation forms
            // none here: its declaration forms it over the Type it resolves (RetainLocalWellFormedness; R7-O2 A4-L).
            if ((supplied || TransparentInner(use) is null) && !(context is { Position: TypePosition.Local, Owner: VariableKoto { TypeKoto: { } local } } && IsWithin(use, local)) &&
                context is not { Position: TypePosition.Parameter, Owner: FunctionKoto { IsAnonymous: true } or FunctionTypeKoto })
            {
                // SPEC 8.1.2, 15.6.4: a direct borrowed input of a struct, or a pair layer whose outer slot is active only for a borrow
                // binding, guarantees its own stored Origins outlive the borrow: each call judges them as the instantiated input's
                // well-formedness (RequireInputPremises), whether its argument is a Borrow formed there or an existing reference. A layer
                // of a definition's assumed result Type walks as AssumedResult (a held Function Type below it forms HeldResult rows).
                var discharge = context is { Position: TypePosition.Parameter, Direct: true, Owner: FunctionKoto function } &&
                    origin is { Kind: OriginKind.Input } && ReferenceEquals(origin.Binder, function) && origin.Slot == context.Slot &&
                    (AdtDef.IsStruct(type.SafeReferent) || TryPairLayer(type, out _, out _)) ? OriginDischarge.CallBorrows
                    : context.Position == TypePosition.Result && AssumesResult(context.Owner) ? OriginDischarge.AssumedResult : OriginDischarge.Judgment;
                this.RetainLayerOutlives(type, use, discharge);
            }
        }
        else if (type is { IsPointer: true })
        {
            // SPEC 5.7: a raw layer states the Origin its `during` writes (WrittenRawOrigin) or none, and forms no borrow Origin.
            if (written)
            {
                if (this.WrittenRawOrigin(annotation!, use, scope) is not { } raw)
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

            // SPEC 15.3.1: braces never apply Origins. A trailing during binds the only slot of a one-slot schema (the target decided
            // above); it adds no borrow layer.
            if (written && annotation!.OriginArguments is not null)
            {
                return this.Record(use, BindingFailure.InvalidOrigin, []);
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
                        var slot = this.WrittenOrigin(annotation!, use, scope);
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
        for (var node = use; TransparentInner(node) is { } inner; node = inner)
        {
            Complete(inner, type);
        }

        return type;
    }

    // SPEC 15.6.1: the well-formedness of the completed layer `type` at `use`, a borrow, Slice or SemanticsApplication layer with an outer
    // Origin and a component, its outer slot active (InactiveOuterOrigin): each Origin its component stores outlives that Origin
    // (RetainInnerOutlives), in the cases its pair slot exists (PairSlotCondition), discharged as the caller's position takes it. The one
    // formation of a layer's rows: at the layer's binding (CompleteOrigins), or for a local's annotation at its declaration
    // (RetainLocalWellFormedness).
    private void RetainLayerOutlives(BoundType type, Koto use, OriginDischarge discharge)
    {
        if (IsOriginLayer(type) && type.Origin is { } origin &&
            type.Components.Count != 0 && !this.InactiveOuterOrigin(type, this, use, use, default))
        {
            // A written Type's own layer at its use, in no comparison (default), as its retention below.
            var formation = new UseScope(use);
            this.RetainInnerOutlives(type.Components[0], origin, ref formation, discharge, RequiredCondition(this.PairSlotCondition(type, ref formation, default)));
        }
    }

    // SPEC 15.6.1, 15.4.4 (R7-O2 A4-L): a local's annotation's layer rows, formed by its declaration (BindVariableCore, after
    // InferLocalOrigins and before FixDeclaredRegions, which reads them as bounds of its region slots), each at a layer node its binding
    // formed (FormedTypeRows) over that node's Type as the declaration left it (`node.BoundType`, an omitted slot the local's region
    // slot), Judgment rows as every Local layer's; a transparent wrapper's are its inner node's. A layer that formed keeps its rows when
    // the annotation as a whole fails (SPEC 23.1.2). A Function Type's layers are its signature's, formed at their own positions
    // (CompleteOrigins), so the walk does not enter it.
    private void RetainLocalWellFormedness(Koto node)
    {
        if (node is FunctionTypeKoto)
        {
            return;
        }

        if (FormedTypeRows.Contains(node) && TransparentInner(node) is null && node.BoundType is { } type)
        {
            this.RetainLayerOutlives(type, node, OriginDischarge.Judgment);
        }

        for (var i = 0; i < node.TypeComponentCount; i++)
        {
            this.RetainLocalWellFormedness(node.TypeComponent(i));
        }
    }

    // The one binding of a written Origin atom: its expression, or the name it was written as. Null when the atom failed (its own record).
    private BoundOrigin? WrittenOrigin(TypeSemanticsKoto annotation, Koto use, BindingScope scope)
        => annotation.OriginExpression is { } expression ? this.BindOrigin(expression, scope) : this.BindOriginName(annotation.OriginName!, use, scope);

    // SPEC 15.6.1: each relation is the well-formedness of the Type occurrence `use` (WellFormed), never a fit of a value: every Origin
    // of OwnedOrigins(inner) (SPEC 15.2.3) outlives `outer`. A held Function Type's part is its call signature with its own per-call
    // Origins closed (DECISIONS BU-73, StoredSignature): its fixed Origins relate, its per-call Origins never do, as they cannot escape
    // their binder (SPEC 15.3.4). `discharge` applies to the Origins stored directly in `inner`; those of its components are judged,
    // except below a definition's assumed result layer (AssumedResult, the walk's state, whose own rows are Judgment rows): there a held
    // Function Type's Origins form HeldResult rows (DECISIONS BU-73's amendment, F's ruling O3-24). `condition` is the Semantics case in
    // which `outer` stores `inner`; a pair layer's own slot, the implicit `o` of an original `s/T` included, is stored only in its binder's
    // borrow cases (SPEC 15.6.5). The layers below one written layer share its use's scope.
    private void RetainInnerOutlives(BoundType inner, BoundOrigin outer, ref UseScope use, OriginDischarge discharge = OriginDischarge.Judgment, ulong condition = 0)
    {
        if (!inner.StoresOrigin)
        {
            return;
        }

        var below = discharge is OriginDischarge.AssumedResult or OriginDischarge.HeldResult
            ? inner.Kind == BoundTypeKind.Function ? OriginDischarge.HeldResult : discharge
            : OriginDischarge.Judgment;
        var row = discharge == OriginDischarge.AssumedResult ? below == OriginDischarge.HeldResult ? below : OriginDischarge.Judgment : discharge;
        inner = this.StoredSignature(inner);

        // A written Type's retention at its use, in no comparison (default).
        if (this.StoredSlot(inner, ref use, out var slot, default) is { } origin && !OriginOutlives(origin, outer))
        {
            this.AddObligation(new(BindingObligationKind.OriginOutlives, use.Use, BindingDeadline.BodyOrigins, inner, origin, outer, WellFormed: true, Discharge: row, Condition: condition | RequiredCondition(slot)));
        }

        for (var i = 0; i < inner.OriginArguments.Count; i++)
        {
            var argument = inner.OriginArguments[i];
            if (!OriginOutlives(argument, outer))
            {
                this.AddObligation(new(BindingObligationKind.OriginOutlives, use.Use, BindingDeadline.BodyOrigins, inner, argument, outer, WellFormed: true, Discharge: row, Condition: condition));
            }
        }

        for (var i = 0; i < inner.Components.Count; i++)
        {
            this.RetainInnerOutlives(inner.Components[i], outer, ref use, below, condition);
        }
    }

    // One query of the requirement fixed point's visit of a declared Type: each distinct part once per polarity and position (visitedParts'
    // state, one bit per polarity and StoragePosition). A part met again in a state repeats its joins and keepers, duplicates only; a join
    // a revisit would have made after this visit changed a slot the part reads is made when the declaration is visited again (its
    // self-edge, CollectRequirementEdges, queues it as its own dependent), so the least fixed point is the same (DECISIONS BU-19, PLAN G309).
    private void AccumulateRequirements(BoundType type, DeclarationSchema schema, int polarity, ref bool changed)
        => this.RequirementPart(type, schema, polarity, StoragePosition.Stored, ref changed, this.visitedParts.Begin(this.storageVersion));

    private void RequirementPart(BoundType type, DeclarationSchema schema, int polarity, StoragePosition position, ref bool changed, uint query)
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
            var use = position == StoragePosition.Signature ? default : conditional ? this.ConditionalUse(schema, whole) : new SlotRequirement(loan, default);
            this.AccumulateOrigin(origin, schema, polarity, use, position, ref changed);
            if (use != default)
            {
                this.Keep(schema, origin, conditional ? target : this.BorrowReferent(type, out _, out _), null, use.Binders);
            }
        }

        if (type.RawOrigin is { Kind: not OriginKind.Holder } raw)
        {
            // SPEC 5.7: a raw layer's Origin is a covariant position, no use of a slot for Loan requirements or Phantom status. So is
            // every layer of a stored Function Type's signature, which stores nothing (SPEC 15.2.4, 15.3.5; StoragePosition.Signature).
            this.AccumulateOrigin(raw, schema, polarity, default, position, ref changed);
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

            // SPEC 15.3.5: the chain through a nested Type's slot bound to the argument reaches a stored signature where the slot's does.
            var sign = source.Variance == OriginVariance.Invariant ? 0 : source.Variance == OriginVariance.Contravariant ? -polarity : polarity;
            var use = position == StoragePosition.Signature ? default : this.NestedUse(source.Requirement, type, schema);
            this.AccumulateOrigin(type.OriginArguments[i], schema, sign, use, source.InStoredSignature ? StoragePosition.Signature : position, ref changed);
            if (use != default)
            {
                this.Keep(schema, type.OriginArguments[i], type, source, default);
            }
        }

        // A Function Type's parameters and result are its signature (StoragePosition.Signature).
        var below = type.Kind == BoundTypeKind.Function ? StoragePosition.Signature : position;
        for (var i = 0; i < type.Components.Count; i++)
        {
            var sign = IsInvariantLayer(type, this, null, default) ? 0 : type.Kind == BoundTypeKind.Function && i == 0 ? -polarity : polarity;
            if (type.Kind == BoundTypeKind.Constructed && declared is { } target && i < target.GenericSlots.Count)
            {
                var variance = target.GenericSlots[i].OriginVariance;
                if (variance == OriginVariance.Unused)
                {
                    continue;
                }

                sign = variance == OriginVariance.Invariant ? 0 : variance == OriginVariance.Contravariant ? -polarity : polarity;
            }

            if (this.FirstVisit(type.Components[i], query, (byte)(2 << (sign + 1 + (below == StoragePosition.Signature ? 3 : 0)))))
            {
                this.RequirementPart(type.Components[i], schema, sign, below, ref changed, query);
            }
        }
    }

    private void AccumulateOrigin(BoundOrigin origin, DeclarationSchema schema, int polarity, SlotRequirement use, StoragePosition position, ref bool changed)
    {
        if (origin.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                this.AccumulateOrigin(origin.Operands[i], schema, polarity, use, position, ref changed);
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

            if (position == StoragePosition.Signature && !parameter.InStoredSignature)
            {
                parameter.InStoredSignature = true;
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

            // SPEC 15.3.2 (DECISIONS BU-100): storage holds only public Origins; a written Origin there that is a Constraint set's
            // unrelated slot is a hidden Origin, judged once every declaration completed.
            if (node is TypeSemanticsKoto { OriginExpression: { } expression, BoundType.Origin.Kind: OriginKind.ConstraintSet } && StorageType(node) is not null)
            {
                this.Record(expression, BindingFailure.InvalidOrigin, []);
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
                        this.RecordFailingCase(site, schema.Origins[i], type, LoanRequirement.Uniq);
                        if (!ReferenceEquals(site, node))
                        {
                            this.CompleteDependent(node, site);
                        }

                        // Transitional, R7-E5 (E4c, N1; PLAN's second independent failure at an occupied Origin key): a site's first
                        // failing uniq slot only. A further slot reads the same Type at the same site, not a failed input, but its
                        // record would be a second InvalidOrigin under the site's key (a Defect), until RecordNumbered per failure.
                        invalid = true;
                        break;
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

        if (type.IsPointer || type.Kind == BoundTypeKind.Function)
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
