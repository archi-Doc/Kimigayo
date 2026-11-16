// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 10.8, 8.3.4: while a call's evidence is judged (EnterEvidence), the scope of its solve's node, whose premises judge an abstract
    // evidence Type (PremiseScope); null otherwise.
    private BindingScope? premiseUse;

    // DECISIONS D3: the formations so far that formed no Type (Unformed); a check compares it with its mark (UnformedSince).
    private int unformedWrites;

    // DECISIONS D3: the case of the split layer a consumer judges in (EnterSplitCase), and the one binding judged in it; null otherwise.
    private SemanticsKind? splitCase;

    private BoundType? splitWhole;

    // A finite Type family stores complete case Types in SemanticsOrder, packed by its Length mask.
    // Only the selecting binder is a dependency; no source expression or operation plan is retained.
    private enum FamilyTransform : byte
    {
        Target,
        Origin,
        Apply,
    }

    /// <summary>Gets the pair selector of a layer or finite result-Type family.</summary>
    /// <param name="type">The complete Type.</param>
    /// <param name="whole">The selector's whole Type.</param>
    /// <returns>Whether a selector remains.</returns>
    internal static bool TryAdaptationSelector(BoundType type, out BoundType whole)
    {
        if (TryPairLayer(type, out whole, out _))
        {
            return true;
        }

        if (type.Kind == BoundTypeKind.SemanticsAdaptation && type.Symbol?.WholeType is { } selected)
        {
            whole = selected;
            return true;
        }

        whole = null!;
        return false;
    }

    /// <summary>Forms the finite family of complete result Types of a short adaptation.</summary>
    /// <param name="pair">The selector's pair target Symbol.</param>
    /// <param name="source">The independently fixed input Type.</param>
    /// <param name="origin">The Origin of a newly produced borrow.</param>
    /// <param name="scope">The adaptation's scope, whose premises give each binder's admitted cases (SPEC 8.3.4).</param>
    /// <returns>An interned family, collapsed when every case is identical.</returns>
    internal BoundType SemanticsAdaptation(BindingSymbol pair, BoundType source, BoundOrigin? origin, BindingScope scope)
        => this.AdaptationFamily(pair, source, origin, scope);

    /// <summary>Gets the possible outer Semantics of a complete Type or finite result-Type family.</summary>
    /// <param name="type">The complete Type.</param>
    /// <param name="scope">The available premises.</param>
    /// <returns>A conservative set of outer Semantics.</returns>
    internal SemanticsMask ResultSemantics(BoundType type, BindingScope scope)
    {
        if (type.Kind != BoundTypeKind.SemanticsAdaptation)
        {
            if (TryPairLayer(type, out var whole, out var target))
            {
                var admitted = this.AdmittedSemantics(whole, scope);
                // Applying owner is transparent; its result can itself be a borrow or handle.
                return type.Kind == BoundTypeKind.SemanticsApplication && (admitted & SemanticsMask.Owner) != 0
                    ? (admitted & ~SemanticsMask.Owner) | this.ResultSemantics(target, scope) : admitted;
            }

            if (type.IsAbstract)
            {
                return this.TargetCases(type, scope, default) == SemanticsMask.Owner ||
                    this.RequestCapability(type, this.Library.ObjectPayload, scope) == ConstraintProof.Proven ||
                    this.ProveSealed(type, scope) == ConstraintProof.Proven ||
                    this.RequestCapability(type, this.Library.PrimitiveInteger, scope) == ConstraintProof.Proven
                    ? SemanticsMask.Owner : this.AdmittedSemantics(type, scope);
            }

            return type.Semantics.ToMask();
        }

        var result = SemanticsMask.None;
        for (var i = 0; i < type.Components.Count; i++)
        {
            result |= this.ResultSemantics(type.Components[i], scope);
        }

        return result;
    }

    /// <summary>Forms a short adaptation's result after its selected Semantics is known.</summary>
    /// <param name="semantics">The selected Semantics.</param>
    /// <param name="source">The complete input Type.</param>
    /// <param name="origin">The Origin of a newly produced borrow.</param>
    /// <param name="scope">The adaptation's scope, whose premises give each binder's admitted cases (SPEC 8.3.4).</param>
    /// <returns>The normalized ordinary Type or a finite family over an independent source selector.</returns>
    internal BoundType AdaptedType(SemanticsKind semantics, BoundType source, BoundOrigin? origin, BindingScope scope)
    {
        if (ReferenceEquals(source, BoundType.Never) || semantics == SemanticsKind.Owner)
        {
            return source;
        }

        if (IsObjectSemantics(semantics))
        {
            if (TryAdaptationSelector(source, out var whole))
            {
                return this.AdaptationFamily(whole.Symbol!, source, origin, scope, semantics);
            }

            if (semantics == SemanticsKind.ObjRef && source.Semantics == SemanticsKind.ObjRef)
            {
                return source;
            }

            if (IsBorrow(semantics) && source.IsObjectBorrow)
            {
                origin = source.Origin ?? origin;
            }

            if (IsObjectSemantics(source.Semantics) && source.Components.Count == 1)
            {
                source = source.Components[0];
            }
        }

        // Value borrows and addresses wrap the written slot; object targets select its View Target.
        return this.Form(semantics, source, origin);
    }

    private static BoundType? FamilyCase(BoundType family, SemanticsKind mode)
    {
        var mask = (uint)family.Length;
        var bit = (uint)mode.ToMask();
        return (mask & bit) == 0 ? null : family.Components[BitOperations.PopCount(mask & (bit - 1))];
    }

    // SPEC 8.1.2: whether an abstract binding is its own selector, the symbol its applications and families over other targets name: a
    // pair's original or application names its pair, a Type parameter itself. A pair target alone or an unresolved projection has none, so
    // no Type over another target is formed under it (ApplySemantics, SubstituteFamily; PLAN G886: its representation is the remainder).
    private static bool HasOwnSelector(BoundType binding) => binding.Kind == BoundTypeKind.Parameter || binding.Semantics == SemanticsKind.Parameter;

    // SPEC 7.5, 8.3.4, 8.15.1, 10.8: the scope whose premises judge an abstract Type, by the Type's one meaning where it is read. A source's
    // outer Semantics at a call (its evidence, while EnterEvidence holds the solve's node) is judged under the premises at that node, a
    // conditional member's leading region and an enclosing `when` condition included, wherever the Type is in scope there. Any other
    // reading, a pair slot's stored binding above all, is the Type it is in every phase (Binding's substitution, MIR's TyCtxt.Signature),
    // so it reads what the Type carries: its root's declaration, a projection's receiver's (`T` of `T.Element`, so the caller's
    // `T.Element is ObjectPayload` and a refinement through its `T is Holder` are read), where the generic declaring it states its
    // Constraints; a receiver without a declaration leaves the projection's own. A binding that only the use's premises make owner is the
    // remainder of PLAN G886's representation (the call's substitution records no scope).
    private BindingScope PremiseScope(BoundType type)
    {
        while (type is { Kind: BoundTypeKind.AssociatedProjection, Components: [{ Symbol: not null } receiver, ..] })
        {
            type = receiver;
        }

        var declaring = type.Symbol!.Scope;
        for (var scope = this.premiseUse; scope is not null; scope = scope.Parent)
        {
            if (ReferenceEquals(scope, declaring))
            {
                return this.premiseUse!;
            }
        }

        return declaring;
    }

    // SPEC 10.8, 8.3.4: opens the judgment of a call's evidence at the solve's node (PremiseScope) and returns the scope it replaces, which
    // the caller restores.
    private BindingScope? EnterEvidence(SlotFrame frame)
    {
        var saved = this.premiseUse;
        this.premiseUse = this.ConstraintScope(frame.Node);
        return saved;
    }

    // SPEC 8.1.2, 8.2.3, 8.7.3: whether a pair slot's binding is abstract, its outer Semantics not yet known: a pair's original or
    // occurrence, an application (Semantics Parameter), or an abstract Type (a Type parameter, a pair target alone, an unresolved
    // projection: its Semantics field is the constructor default, never an outer Semantics) unless its premises (PremiseScope) prove it
    // owner (ResultSemantics; the owner's O-311 (a)), when it binds as an owner Type. ApplySemantics, the substitution of a family and
    // HasOuterSlot read it.
    private bool IsAbstractBinding(BoundType binding) => binding.IsAbstractType
        ? this.ResultSemantics(binding, this.PremiseScope(binding)) != SemanticsMask.Owner
        : HasOwnSelector(binding);

    // SPEC 8.1.1, 8.2.3 (Rd3): the cases a pair target alone takes from its pair, the selected case or else its binder's admitted set; in
    // the owner case it is `W` itself, an owner Type (`t is owner` alone makes U owner). All for any other Type, whose premises alone fix
    // its outer Semantics. ResultSemantics, CanSelectIdentity and TransformFamily's owner case read it.
    private SemanticsMask TargetCases(BoundType type, BindingScope scope, ReadOnlySpan<PairCase> cases)
        => type is { Kind: BoundTypeKind.TargetProjection, Symbol: { Kind: BindingSymbolKind.SemanticsTarget, WholeType: { } whole } pair }
            ? CaseOf(cases, pair)?.ToMask() ?? this.AdmittedSemantics(whole, scope) : SemanticsMask.All;

    // SPEC 8.1.2: whether a Type is formed under this binding, its split represented: a value Type, an owner-proven abstract Type, one
    // with a selector of its own, a family case by case. A selector-less abstract binding (a pair target alone or an unresolved
    // projection no premise proves owner) forms none (Unformed; PLAN G886).
    private bool FormsUnder(BoundType binding) => !this.IsAbstractBinding(binding) || HasOwnSelector(binding);

    // DECISIONS D3 (temporary; removed with the representation of a selector-less binding's split, PLAN G886): a formation under a binding
    // that forms none (FormsUnder) forms nothing (null) and counts one write (unformedWrites), the one record of it; its readers compare
    // the count with a mark (UnformedSince). The split is an Unknown in one candidate's fit (rule W, BU-79): a part whose own pattern
    // forms no Type is unknown (UnformedPart), and a candidate trial rests on it only where every other check passed, which its verdict
    // publishes to the selection (TryCandidate: PendingJudgmentKind.UnformedSplit), its call then one located Unsupported_Kd. A value
    // call's callee signature and a reference's candidate have no part to leave unknown (their binder is fixed before any evidence), so
    // they judge the same check in each case of the split layer (EnterSplitCase): one that fails in every case fails by itself, its
    // Language record kept, and any other rests on the split. The writers:
    // ApplySemantics, SubstituteFamily, DirectTarget, HasOuterSlot (the inline `IsAbstractBinding` and not `HasOwnSelector` there is
    // FormsUnder's body where the first is already decided); IsSource's comparison reads FormsUnder and publishes nothing.
    private BoundType? Unformed()
    {
        this.unformedWrites++;
        return null;
    }

    // DECISIONS D3 (SPEC 8.1.1's table, 8.15.2: a split layer is judged case by case): opens case `mode` of the split layer for one check
    // of a consumer. Until LeaveSplitCase, the first binding that forms no Type forms `s/target` as `mode`'s layer over the target
    // (SplitLayer) and keeps an outer slot as that layer does; every other formation that forms none stays Unformed (a second binding's
    // cases are not paired with the first's; a direct target and a family are unrepresented in any case). Every Semantics is a case, a
    // superset of the cases the binding's premises admit, so a check that fails in each fails in every admitted one. Returns what
    // LeaveSplitCase restores.
    private (int Mark, SemanticsKind? Case, BoundType? Whole) EnterSplitCase(SemanticsKind mode)
    {
        var entered = (this.unformedWrites, this.splitCase, this.splitWhole);
        this.splitCase = mode;
        this.splitWhole = null;
        return entered;
    }

    // DECISIONS D3: closes a case of the split layer and returns whether its check, which `failed`, failed by itself: no formation in the
    // case formed none. The case's own writes are dropped (the consumer's first run left the split's).
    private bool LeaveSplitCase((int Mark, SemanticsKind? Case, BoundType? Whole) entered, bool failed)
    {
        var own = failed && !this.UnformedSince(entered.Mark);
        this.unformedWrites = entered.Mark;
        this.splitCase = entered.Case;
        this.splitWhole = entered.Whole;
        return own;
    }

    // DECISIONS D3: whether the binding `whole`, which forms no Type, is the one judged in an open case of the split layer (EnterSplitCase):
    // the first such binding a case meets.
    private bool InSplitCase(BoundType whole, out SemanticsKind mode)
    {
        mode = this.splitCase ?? default;
        return this.splitCase is not null && ReferenceEquals(this.splitWhole ??= whole, whole);
    }

    // DECISIONS D3 with rule W (BU-79): whether a formation since `mark` (an earlier unformedWrites) formed no Type, so that a check that
    // failed since then failed through the split: an Unknown, not a failure of its own.
    private bool UnformedSince(int mark) => this.unformedWrites != mark;

    // SPEC 8.1.2: whether a pair layer formed under this binding (an annotated original, or an application `s/U`, ApplySemantics) keeps
    // its outer-Origin slot: a borrow binding does, an abstract binding or a family conditionally; a value binding keeps none. For a
    // binding that forms no Type it is unrepresented (Unformed, D3): the slot is kept and the candidate rests on the split.
    private bool HasOuterSlot(BoundType binding)
    {
        var isAbstract = this.IsAbstractBinding(binding);
        if (isAbstract && !HasOwnSelector(binding))
        {
            if (this.InSplitCase(binding, out var mode))
            {
                return IsBorrow(mode);
            }

            _ = this.Unformed();
        }

        return isAbstract || IsBorrow(binding.Semantics) || binding.Kind == BoundTypeKind.SemanticsAdaptation;
    }

    // Short adaptations select the output mode with the input case; a fixed mode maps every input case. The cases are the binder's
    // admitted set at the adaptation (SPEC 8.3.4, 8.15.2: a conditional member's or block's premises included), not at its declaration,
    // within a family source's own cases. A family never reaches a scope wider than where it was formed (result and parameter Types are
    // declared, SPEC 7.1, and a nested scope only adds premises), so the cases any reader selects lie within its mask.
    private BoundType AdaptationFamily(BindingSymbol pair, BoundType source, BoundOrigin? origin, BindingScope scope, SemanticsKind? fixedMode = null)
    {
        var admitted = this.AdmittedSemantics(pair.WholeType!, scope) &
            (source.Kind == BoundTypeKind.SemanticsAdaptation && ReferenceEquals(source.Symbol, pair) ? (SemanticsMask)source.Length : SemanticsMask.All);
        var children = this.RentTypes(BitOperations.PopCount((uint)admitted));
        var cases = this.adaptationCaseScratch.Rent(1);
        try
        {
            var count = 0;
            foreach (var mode in SemanticsOrder)
            {
                if (admitted.Contains(mode))
                {
                    cases[0] = new(pair, mode);
                    children[count++] = this.AdaptedType(fixedMode ?? mode, this.CaseType(source, cases.AsSpan(0, 1)), origin, scope);
                }
            }

            return this.TypeFamily(pair, admitted, children.AsSpan(0, count));
        }
        finally
        {
            this.typeScratch.Return(children, clearArray: true);
            this.adaptationCaseScratch.Return(cases, clearArray: true);
        }
    }

    // A placeholder's Owner tag is not mode evidence. A pair's owner case does establish its target's owner form.
    private bool CanSelectIdentity(BoundType type, BindingScope scope, ReadOnlySpan<PairCase> cases = default)
        => !type.IsAbstract || this.TargetCases(type, scope, cases) == SemanticsMask.Owner || this.ResultSemantics(type, scope) == SemanticsMask.Owner;

    // A family is interned only when no pair application has its cases: SPEC 13.5.1's `p@s` abbreviates `p@s/U`, whose Type is the
    // application `s/U` (8.2.3: one Type, one representation), so a case list that one application's cases (CaseType) give is that
    // application, as a list of identical cases is that case (TyCtxt.Intern).
    private BoundType TypeFamily(BindingSymbol pair, SemanticsMask admitted, ReadOnlySpan<BoundType> children)
        => this.FamilyApplication(pair, admitted, children) ??
            this.types.Intern(BoundTypeKind.SemanticsAdaptation, pair, SemanticsKind.Parameter, children, length: (long)admitted);

    // The pair application `s/U` (with the outer Origin its borrow cases state) whose case Types over the family's mask are `children` in
    // SemanticsOrder, or null. `U` and the Origin are read off a layer case; the owner case is `U` itself. The mask is the binder's
    // admitted set where the family is formed, and no reader of it is in a wider scope (DECISIONS BU-172), so only the cases compare.
    private BoundType? FamilyApplication(BindingSymbol pair, SemanticsMask admitted, ReadOnlySpan<BoundType> children)
    {
        // One case stays that case (TyCtxt.Intern), the concrete Type its application's determined head forms (SPEC 8.2.3).
        if (children.Length < 2 || pair.WholeType is not { } whole)
        {
            return null;
        }

        BoundType? target = null;
        BoundOrigin? origin = null;
        var count = 0;
        foreach (var mode in SemanticsOrder)
        {
            if (admitted.Contains(mode))
            {
                var child = children[count++];
                if (mode == SemanticsKind.Owner)
                {
                    target ??= child;
                }
                else if (child is { Kind: BoundTypeKind.Semantics, Components: [var below] } && child.Semantics == mode)
                {
                    target ??= below;
                    origin ??= IsBorrow(mode) ? child.Origin : null;
                }
            }
        }

        if (target is null)
        {
            return null;
        }

        var application = this.PairApplication(pair, target, origin);
        count = 0;
        foreach (var mode in SemanticsOrder)
        {
            if (admitted.Contains(mode))
            {
                ReadOnlySpan<PairCase> selected = [new(pair, mode)];
                if (!ReferenceEquals(this.CaseType(application, selected), children[count++]))
                {
                    return null;
                }
            }
        }

        return application;
    }

    // Only Apply forms nothing (null), for a case whose binding forms no Type; Target and Origin map every case.
    private BoundType? TransformFamily(BoundType family, FamilyTransform operation, BoundOrigin? origin = null, BoundType? target = null)
    {
        var children = this.RentTypes(family.Components.Count);
        var cases = this.adaptationCaseScratch.Rent(1);
        try
        {
            var count = 0;
            foreach (var mode in SemanticsOrder)
            {
                if (((SemanticsMask)family.Length).Contains(mode))
                {
                    var child = family.Components[count];
                    cases[0] = new(family.Symbol!, mode);
                    var formed = operation switch
                    {
                        FamilyTransform.Target => this.DirectTarget(child),
                        FamilyTransform.Origin => IsBorrow(child.Semantics) || child.IsAbstract ? this.WithOrigins(child, origin, (BoundOrigin[])child.OriginArguments) : child,
                        // A pair target alone whose case is owner is `W` itself (TargetCases): the case forms the target itself.
                        _ when child.IsAbstractType && this.TargetCases(child, this.PremiseScope(child), cases.AsSpan(0, 1)) == SemanticsMask.Owner
                            => this.CaseType(target!, cases.AsSpan(0, 1)),
                        _ => this.ApplySemantics(child, this.CaseType(target!, cases.AsSpan(0, 1)), origin),
                    };
                    if (formed is null)
                    {
                        return null; // A case whose binding forms no Type (ApplySemantics): the application forms none either.
                    }

                    children[count++] = formed;
                }
            }

            return this.TypeFamily(family.Symbol!, (SemanticsMask)family.Length, children.AsSpan(0, count));
        }
        finally
        {
            this.typeScratch.Return(children, clearArray: true);
            this.adaptationCaseScratch.Return(cases, clearArray: true);
        }
    }

    // SPEC 8.1.2: `s/U`, a pair binder's binding `whole` applied to `target`: a family case by case, an abstract binding's application, and
    // a layer the binding's Semantics forms over the target, read through TyCtxt.PairBinding (DECISIONS BU-46): never its Origin, but a
    // borrow states `origin`, the annotation's, and a raw layer what the binding states (none, or a declaration's Unstated); an owner
    // binding gives the target. An abstract binding without a selector of its own (HasOwnSelector) forms none (Unformed), or the layer
    // of the open case of its split (InSplitCase).
    private BoundType? ApplySemantics(BoundType whole, BoundType target, BoundOrigin? origin) => whole switch
    {
        { Kind: BoundTypeKind.SemanticsAdaptation } => this.TransformFamily(whole, FamilyTransform.Apply, origin, target: target),
        _ when this.IsAbstractBinding(whole) => HasOwnSelector(whole) ? this.PairApplication(whole.Symbol!, target, origin)
            : this.InSplitCase(whole, out var mode) ? this.Form(mode, target, origin) : this.Unformed(),
        { Kind: BoundTypeKind.Semantics } => this.types.Rebuild(this.types.PairBinding(whole), [target], IsBorrow(whole.Semantics) ? origin : null, []),
        _ => this.Form(whole.Semantics, target, origin),
    };

    // SPEC 8.1.2: a pair's `s/U` for an abstract binding: the pair's own target gives its original, with the occurrence's slot where it
    // is not the pair's `o` (AnnotatedOriginal); any other target an application, a plain type parameter's binding (no whole) included.
    // ApplySemantics and FamilyApplication read it.
    private BoundType PairApplication(BindingSymbol pair, BoundType target, BoundOrigin? origin)
        => pair.WholeType is { } original && ReferenceEquals(target, pair.Type)
            ? this.AnnotatedOriginal(original, ReferenceEquals(origin, this.PairOuterOrigin(pair)) ? null : origin)
            : this.types.Intern(BoundTypeKind.SemanticsApplication, pair, SemanticsKind.Parameter, [target], origin: origin);

    // SPEC 8.1.2: an occurrence of a pair's original under its binding, annotated with `origin` (`s/T during b`): the binding with that
    // outer-Origin slot where it keeps one (HasOuterSlot); a value binding keeps none. Substitution and PairApplication read it.
    private BoundType AnnotatedOriginal(BoundType binding, BoundOrigin? origin)
        => origin is not null && this.HasOuterSlot(binding) ? this.WithOrigins(binding, origin, (BoundOrigin[])binding.OriginArguments) : binding;

    private BoundType? SubstituteFamilyArgument(BoundType family, in CallSubstitution substitution, int slot, BoundType whole)
    {
        // A slot the slot owner's column does not bind is the declaring Type's (CallSubstitution.Slot), whose occurrence then takes each case in turn.
        var declaring = substitution.OwnsSlot(ContainerSlot(substitution.SlotOwner, family.Symbol!)) ? null : substitution.Declaring;
        var slots = declaring is null ? substitution.Types : (BoundType[])declaring.Components;
        var children = this.RentTypes(whole.Components.Count);
        var caseArguments = this.typeScratch.Rent(slots.Length);
        var cases = this.adaptationCaseScratch.Rent(1);
        slots.CopyTo(caseArguments);
        try
        {
            var count = 0;
            foreach (var mode in SemanticsOrder)
            {
                if (((SemanticsMask)whole.Length).Contains(mode))
                {
                    cases[0] = new(whole.Symbol!, mode);
                    caseArguments[slot] = whole.Components[count];
                    var caseSlots = caseArguments.AsSpan(0, slots.Length);
                    var caseSubstitution = declaring is null ? substitution with { Types = caseSlots }
                        : substitution with { Declaring = this.types.Rebuild(declaring, caseSlots!, declaring.Origin, (BoundOrigin[])declaring.OriginArguments) };
                    if (this.Substitute(family, caseSubstitution) is not { } child)
                    {
                        return null;
                    }

                    children[count++] = this.CaseType(child, cases.AsSpan(0, 1));
                }
            }

            return this.TypeFamily(whole.Symbol!, (SemanticsMask)whole.Length, children.AsSpan(0, count));
        }
        finally
        {
            this.adaptationCaseScratch.Return(cases, clearArray: true);
            this.typeScratch.Return(caseArguments, clearArray: true);
            this.typeScratch.Return(children, clearArray: true);
        }
    }

    private BoundType? SubstituteFamily(BoundType family, BoundType whole)
    {
        if (this.IsAbstractBinding(whole))
        {
            if (!HasOwnSelector(whole))
            {
                return this.Unformed();
            }

            if (whole.Symbol is not { } selector)
            {
                return null;
            }

            var admitted = this.ResultSemantics(whole, this.PremiseScope(whole)) & (SemanticsMask)family.Length;
            var children = this.RentTypes(BitOperations.PopCount((uint)admitted));
            try
            {
                var count = 0;
                foreach (var mode in SemanticsOrder)
                {
                    if (admitted.Contains(mode))
                    {
                        children[count++] = FamilyCase(family, mode)!;
                    }
                }

                return this.TypeFamily(selector, admitted, children.AsSpan(0, count));
            }
            finally
            {
                this.typeScratch.Return(children, clearArray: true);
            }
        }

        return FamilyCase(family, whole.Semantics);
    }
}
