// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The pending relations (DECISIONS R4-M126 §2.5, D-2): each obligation formed naming a pending atom, once per binding, with the
    // outermost unclassified Closure whose atoms it names, which judges it when it is classified, and its record's destination (the
    // obligation's Type, a fit's whole destination Type, CheckTypeUse; DECISIONS R4-M133); reused across passes.
    private readonly List<(FunctionKoto Owner, BindingObligation Obligation, BoundType? Destination)> pendingRelations = [];

    // Every change to inference, obligation or Origin declaration state; a pure proof request leaves it unchanged.
    private int originStateVersion;

    // The candidate evaluations in progress (EvaluateCallCandidates): the Origin part of a fit there is the pure answer, since only the
    // selected candidate's bounds are recorded, after selection (FitsOriginOutlives).
    private int candidateTrials;

    // Set while a selected call's fits are collected (CollectFitBounds): the Origin part of a fit is then the pure answer, and the
    // bounds of Origins omitted in open initializers are recorded for that call (FitsOriginOutlives).
    private bool collectingFits;

    // SPEC 15.6.1, 23.1: whether an Origin obligation is a fit, reported at the value with the source `fit`, rather than the
    // well-formedness of a Type occurrence (RetainInnerOutlives), whose use is that occurrence and whose Type is the inner one, or of a
    // callee's result at its call (RequireResultPremises), or a Type's clause substituted at its occurrence (AddTypeClauseObligations),
    // a declared relation.
    internal static bool IsFitObligation(in BindingObligation obligation)
        => obligation.Clause is null && !obligation.WellFormed && (obligation.Type is null || ReferenceEquals(obligation.Type.Origin, obligation.Shorter) || obligation.Use is not TypeKoto);

    internal bool IsVerifiedOriginObligation(in BindingObligation obligation)
    {
        if (obligation.Kind == BindingObligationKind.OriginInference && obligation.Longer is { } pending)
        {
            // SPEC 15.3.6: an Origin resolved to an open region is that local region; it needs no annotation.
            var resolved = this.OriginAtUse(pending, obligation.Use);
            return resolved.Kind is not (OriginKind.Inference or OriginKind.Unbound) || IsLocalRegion(resolved);
        }

        if (obligation.Kind == BindingObligationKind.OriginOutlives && obligation.Longer is { } longer && obligation.Shorter is { } shorter)
        {
            return this.ProvesOriginOutlives(longer, shorter, obligation.Use, obligation.Condition) &&
                (!obligation.Equality || this.ProvesOriginOutlives(shorter, longer, obligation.Use, obligation.Condition));
        }

        return false;
    }

    // SPEC 15.6.1, 15.6.5: the judgment of an Origin obligation's relation; `==` holds when both directions do, and otherwise takes
    // the worse direction, Refuted before Unknown before Unrepresentable. `reversed` tells that the reverse direction decides it, so
    // a record names that direction's longer end first.
    internal OriginJudgment JudgeOriginObligation(in BindingObligation obligation, out bool reversed)
    {
        reversed = false;
        var forward = this.JudgeOriginRelation(obligation.Longer!, obligation.Shorter!, obligation.Use, obligation.Body, obligation.Condition);
        if (!obligation.Equality || forward == OriginJudgment.Refuted)
        {
            return forward;
        }

        var backward = this.JudgeOriginRelation(obligation.Shorter!, obligation.Longer!, obligation.Use, obligation.Body, obligation.Condition);
        reversed = Severity(backward) > Severity(forward);
        return reversed ? backward : forward;
    }

    // SPEC 15.6.5: an unproven relation fails only when it is Refuted, a finite Origin outliving a fixed one, or Unknown between two
    // fixed Origins, both in `body`. `condition` is the Semantics case the relation is required in (BindingObligation.Condition).
    internal bool OriginRelationFails(BoundOrigin longer, BoundOrigin shorter, Koto use, ExecutableBody body, ulong condition = 0)
        => this.JudgeOriginRelation(longer, shorter, use, body, condition) is OriginJudgment.Refuted or OriginJudgment.Unknown;

    // SPEC 15.6.5: local storage regions carry their source bounds to ownership; a finite-to-local-to-fixed chain is refuted
    // even in checking code. Fixed contracts still need established premises; finite relations carry their actual Loans
    // through the same reaching-value graph as inferred local regions. `body` formed the relation; an enclosing body's atom, which only
    // its Closure's environment brings in, is fixed there and never judged through its sources (SPEC 15.6.5).
    internal OriginJudgment JudgeOriginRelation(BoundOrigin longer, BoundOrigin shorter, Koto use, ExecutableBody body, ulong condition = 0)
    {
        longer = this.OriginAtUse(longer, use);
        shorter = this.OriginAtUse(shorter, use);
        if (this.SourcesIn(longer, body))
        {
            return this.JudgeLocalRegion(longer, shorter, use, body, condition);
        }

        if (longer.Kind != OriginKind.Intersection || this.ProvesOriginOutlives(longer, shorter, use, condition))
        {
            return this.JudgeOriginAtoms(longer, shorter, use, body, condition);
        }

        // SPEC 15.6.5: a meet outlives an Origin when each of its operands does, each the end of its own chain (as OriginOutlives
        // and VerifiedRegionOutlives read it): a raw Place borrow's anchor is an inferred region (SPEC 5.2.2), a local region is judged
        // through its sources; the worst operand decides.
        var worst = OriginJudgment.Proven;
        for (var i = 0; i < longer.Operands.Count; i++)
        {
            var judgment = this.JudgeOriginRelation(longer.Operands[i], shorter, use, body, condition);
            worst = Severity(judgment) > Severity(worst) ? judgment : worst;
        }

        return worst;
    }

    // The atom that supplies an Origin at its use (DECISIONS R4-M133): the Origin declarations and local initializers that enclose the use
    // applied, innermost first, and no receiver resolution: a Borrow's own atom, which a classified Closure's receiver resolution alone
    // turns into ρ. A record's Supplier is this, at its use.
    internal BoundOrigin SuppliedAtUse(BoundOrigin origin, Koto use)
    {
        var initializers = this.initializerOrigins.Count != 0;
        for (var node = use; node is not null; node = node.Parent)
        {
            if (this.originDeclarations.TryGetValue(node, out var declaration) && declaration.State >= 2)
            {
                origin = this.ResolveOrigin(origin, declaration);
            }

            if (initializers && this.initializerOrigins.TryGetValue(node, out var inferred) && inferred.State >= 2)
            {
                origin = this.ResolveOrigin(origin, inferred);
            }
        }

        return origin;
    }

    // The order in which a judgment fails a relation: Refuted before Unknown before Unrepresentable.
    private static int Severity(OriginJudgment judgment) => judgment switch
    {
        OriginJudgment.Refuted => 3,
        OriginJudgment.Unknown => 2,
        OriginJudgment.Unrepresentable => 1,
        _ => 0,
    };

    private static bool IsWithin(Koto use, Koto declaration)
    {
        for (var current = use; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, declaration))
            {
                return true;
            }
        }

        return false;
    }

    private OriginJudgment JudgeOriginAtoms(BoundOrigin longer, BoundOrigin shorter, Koto use, ExecutableBody body, ulong condition = 0)
    {
        if (UnstatedRelation(longer, shorter) is { } holds)
        {
            return holds ? OriginJudgment.Proven : OriginJudgment.Refuted; // Rulings S4.
        }

        if (RegionIn(shorter, body))
        {
            return OriginJudgment.Proven;
        }

        if (this.ProvesOriginOutlives(longer, shorter, use, condition))
        {
            return OriginJudgment.Proven;
        }

        longer = this.OriginAtUse(longer, use);
        shorter = this.OriginAtUse(shorter, use);
        if (RefutesOriginRelation(longer, shorter, body))
        {
            return OriginJudgment.Refuted;
        }

        return longer.Kind == OriginKind.Unbound || shorter.Kind == OriginKind.Unbound ? OriginJudgment.Unrepresentable
            : FixedIn(longer, body) && FixedIn(shorter, body) ? OriginJudgment.Unknown
            : OriginJudgment.Proven;
    }

    // A local's omitted Origins remain open while its initializer acquires values, whether written in its annotation or
    // in a construction qualifier. Published contracts and subsequent uses of the local never open inference again.
    private OriginDeclaration? OpenInitializerInference(BoundOrigin atom, Koto use)
        => this.OpenInitializer(atom, use) is { } local ? this.InitializerDeclaration(local) : null;

    // The declaration of the Origins a local's initializer infers, created with the first Origin its initializer omits or bounds.
    private OriginDeclaration InitializerDeclaration(VariableKoto local)
    {
        if (!this.initializerOrigins.TryGetValue(local, out var declaration))
        {
            this.initializerOrigins.Add(local, declaration = new(local));
            this.originStateVersion++;
        }

        return declaration;
    }

    // The local whose initializer still infers `atom` at `use` (OpenInitializerInference), by lookup alone: a local whose
    // declaration does not exist yet is open, since its declaration would start unresolved.
    private VariableKoto? OpenInitializer(BoundOrigin atom, Koto use)
    {
        // A body's stable storage slot is already owned by its declaration. In particular, an enclosing closure initializer
        // must not solve that slot as one of its own omitted Origins.
        if (IsLocalRegion(atom))
        {
            return null;
        }

        for (var node = atom.Binder; node is not null; node = node.Parent)
        {
            if (node is VariableKoto { InitializerKoto: { } initializer } variable && IsWithin(use, initializer) &&
                (IsWithin(atom.Binder!, initializer) || (variable.TypeKoto is { } annotation && IsWithin(atom.Binder!, annotation))))
            {
                return this.initializerOrigins.TryGetValue(variable, out var declaration) && declaration.State >= 2 ? null : variable;
            }
        }

        return null;
    }

    // The one resolution of an Origin at its use (2a0-D D4), which every judgment, record, the old reporter and the C1-F4 view read: the
    // atom that supplies it there (SuppliedAtUse), then the receiver of each classified Closure that encloses the use (ResolveReceiver),
    // innermost first.
    private BoundOrigin OriginAtUse(BoundOrigin origin, Koto use)
    {
        origin = this.SuppliedAtUse(origin, use);
        for (var node = use; node is not null; node = node.Parent)
        {
            if (origin.Kind is OriginKind.Projection or OriginKind.Intersection &&
                node is FunctionKoto { IsAnonymous: true, ClosureStorage: { Classified: true } plan } closure)
            {
                origin = this.ResolveReceiver(origin, closure, plan.Receiver);
            }
        }

        return origin;
    }

    // SPEC 15.8.2, 15.6.5 (DECISIONS R4-M126 P5): in a classified Closure's body an environment binding is a Field of the call receiver's
    // referent, so its own atom resolves to the receiver Origin ρ under a borrowed receiver, and ρ, no borrow under `owner/Self`, to
    // static (the binding is then the call's own storage); a meet resolves operand by operand. An Origin without the Closure's atoms is
    // returned as it is.
    private BoundOrigin ResolveReceiver(BoundOrigin origin, FunctionKoto closure, SemanticsKind receiver)
    {
        if (origin.Kind == OriginKind.Intersection)
        {
            var resolved = origin;
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (!ReferenceEquals(this.ResolveReceiver(origin.Operands[i], closure, receiver), origin.Operands[i]))
                {
                    resolved = BoundOrigin.Static;
                    for (var j = 0; j < origin.Operands.Count; j++)
                    {
                        resolved = this.Meet(resolved, this.ResolveReceiver(origin.Operands[j], closure, receiver));
                    }

                    break;
                }
            }

            return resolved;
        }

        if (origin.Kind != OriginKind.Projection || !ReferenceEquals(origin.Binder, closure))
        {
            return origin;
        }

        return (RoleOf(origin), receiver == SemanticsKind.Owner) switch
        {
            (ProjectionRole.ClosureValue, true) => BoundOrigin.Static,
            (ProjectionRole.Environment, false) => this.OriginAtom(closure, OriginKind.Projection, ClosureValueSlot),
            _ => origin,
        };
    }

    // DECISIONS R4-M126 §2.5 (b): an Origin relation formed naming a pending atom (PendingAtom), new or one obligationSet already holds,
    // joins the list of the outermost unclassified Closure whose atoms it names: one append per formation (PLAN V16), so a relation
    // formed twice is judged twice, the second finding its use failed or proving it again, with no second record.
    private void AddPendingRelation(in BindingObligation obligation)
    {
        if (obligation is not { Kind: BindingObligationKind.OriginOutlives, Longer: { } longer, Shorter: { } shorter } ||
            PendingOwner(shorter, PendingOwner(longer, null)) is not { } owner)
        {
            return;
        }

        this.pendingRelations.Add((owner, obligation, obligation.Type));

        // The outermost of `owner` and the Closures whose pending atoms `origin` names; the unclassified Closures are being bound, nested.
        static FunctionKoto? PendingOwner(BoundOrigin origin, FunctionKoto? owner)
        {
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                owner = PendingOwner(origin.Operands[i], owner);
            }

            return PendingAtom(origin) && origin.Binder is FunctionKoto closure && (owner is null || IsWithin(owner, closure)) ? closure : owner;
        }
    }

    // DECISIONS R4-M126 §2.5 (d): a binding of `closure` starts, so the entries formed in its body or a body nested in it leave the lists
    // (a consultation's binding or any rebinding leaves with them); the binding appends its own.
    private void LeavePendingRelations(FunctionKoto closure)
    {
        var kept = 0;
        for (var i = 0; i < this.pendingRelations.Count; i++)
        {
            var entry = this.pendingRelations[i];
            if (!FormedWithin(entry.Obligation.Body, closure))
            {
                this.pendingRelations[kept++] = entry;
            }
        }

        this.pendingRelations.RemoveRange(kept, this.pendingRelations.Count - kept);

        static bool FormedWithin(ExecutableBody body, FunctionKoto closure)
        {
            for (; body.IsBody; body = EvaluatingBody(body))
            {
                if (ReferenceEquals(body.Definition?.Declaration, closure))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // DECISIONS R4-M126 §2.5 (b), F2: `closure` is classified, so its entries are judged with the receiver resolution active, each once;
    // an entry whose obligation was removed in place since (no longer in obligationSet) is skipped. Other Closures' entries stay.
    private void JudgePendingRelations(FunctionKoto closure)
    {
        var kept = 0;
        for (var i = 0; i < this.pendingRelations.Count; i++)
        {
            var entry = this.pendingRelations[i];
            if (!ReferenceEquals(entry.Owner, closure))
            {
                this.pendingRelations[kept++] = entry;
            }
            else if (this.obligationSet.Contains(entry.Obligation))
            {
                this.RoutePendingRelation(entry.Obligation, entry.Destination);
            }
        }

        this.pendingRelations.RemoveRange(kept, this.pendingRelations.Count - kept);
    }

    // DECISIONS R4-M126 F3 (R4-M133): a pending relation, its ends resolved, routed as a binding-time fit is: fixed-fixed or Refuted, its
    // chains are found by the fit's position search in the body that formed it, passed explicitly, and recorded as a fit's are
    // (RecordOriginChains: at RelationAt, a failed Origin environment's dependent, a body chain a twin); any other body chain stays a row
    // for A5 and the old reporter, unjudged here. A relation Binding judges is no row (2a0-D D1): its obligation leaves, as one judged at
    // binding time is never formed (AddObligation's callers route by BodyChain), unless a recorded chain is a body chain, whose obligation
    // stays the twin's row (R4-M108, R4-M111). Each chain records as its Supplier the part of the obligation's longer end, as it supplies
    // the end at the use (SuppliedAtUse), that holds the chain's end, and the entry's destination (DECISIONS R4-M133).
    private void RoutePendingRelation(in BindingObligation obligation, BoundType? destination)
    {
        var (use, body) = (obligation.Use, obligation.Body);
        var longer = this.OriginAtUse(obligation.Longer!, use);
        var shorter = this.OriginAtUse(obligation.Shorter!, use);
        var refuted = RefutesOriginRelation(longer, shorter, body) || (obligation.Equality && RefutesOriginRelation(shorter, longer, body));
        if ((BodyChain(longer, shorter, body) && !refuted) || use.BindingFailure != BindingFailure.None)
        {
            return;
        }

        var all = this.relationScratch;
        all.Clear();
        var variance = obligation.Equality ? OriginVariance.Invariant : OriginVariance.Covariant;
        if (new OriginFitSearch(this, use, body, OriginFitMode.Judged, all).FailedOriginPart(longer, shorter, variance, obligation.Condition) is { } first)
        {
            for (var i = 0; i < all.Count; i++)
            {
                var supplier = this.SuppliedBy(this.SuppliedAtUse(obligation.Longer!, use), all[i].Longer, use);
                all[i] = all[i] with { Supplier = supplier, Clause = obligation.Clause, WellFormed = obligation.WellFormed };
            }

            var firstSupplier = this.SuppliedBy(this.SuppliedAtUse(obligation.Longer!, use), first.Longer, use);
            var recorded = first with { Supplier = firstSupplier, Clause = obligation.Clause, WellFormed = obligation.WellFormed };
            if (this.RecordOriginChains(use, RelationAt(obligation), body, destination, recorded, this.UnrefutedCause(use, recorded)))
            {
                return;
            }
        }

        // Formed late in the list (the Closure is classified once its body is bound), so it is found from the end.
        this.obligationSet.Remove(obligation);
        this.obligations.RemoveAt(this.obligations.LastIndexOf(obligation));
        this.originStateVersion++;
    }

    // The part of `supplied`, a relation's end as it supplies it at `use` (SuppliedAtUse), that supplied `resolved`, a record's end there
    // (DECISIONS R4-M133): the first operand of a meet whose resolution holds it, otherwise the end itself.
    private BoundOrigin SuppliedBy(BoundOrigin supplied, BoundOrigin resolved, Koto use)
    {
        for (var i = 0; i < supplied.Operands.Count; i++)
        {
            if (OriginIncludes(this.OriginAtUse(supplied.Operands[i], use), resolved))
            {
                return this.SuppliedBy(supplied.Operands[i], resolved, use);
            }
        }

        return supplied;
    }

    // SPEC 15.3.6 (PLAN G74): a pure question: whether the premises visible at `use` entail `longer outlives shorter`
    // (EntailsOrigin). A request leaves inference, obligation and declaration state unchanged, which every request checks
    // (originStateVersion). `condition` is the Semantics case the relation is required in (SPEC 15.6.5).
    private bool ProvesOriginOutlives(BoundOrigin longer, BoundOrigin shorter, Koto use, ulong condition = 0)
    {
        var version = this.originStateVersion;
        longer = this.OriginAtUse(longer, use);
        shorter = this.OriginAtUse(shorter, use);
        this.OriginProofMetrics?.Enter(longer, shorter, use, this.originPremiseDepth);
        var proven = this.EntailsOrigin(longer, shorter, use, condition);
        if (version != this.originStateVersion)
        {
            throw new InvalidOperationException($"The Origin proof of `{longer} outlives {shorter}` changed inference or obligation state (PLAN G74).");
        }

        return proven;
    }

    // SPEC 15.4.4 (PLAN G74 U1): the constraint a fit collects. A fit that requires a value to outlive an Origin omitted in a local's
    // annotation or initializer, while that initializer is still bound, bounds the Origin with the value, and the relation is proven
    // again once the Origin is resolved; any other requirement is the pure proof. An open region holds no Loans, so a value fitted
    // into it would lose its Loans: such a fit is never collected here.
    private bool FitOriginOutlives(BoundOrigin longer, BoundOrigin shorter, Koto use, ulong condition = 0)
    {
        longer = this.OriginAtUse(longer, use);
        shorter = this.OriginAtUse(shorter, use);
        if (!OriginOutlives(longer, shorter) && shorter is { Kind: OriginKind.Inference, Open: false } && this.OpenInitializerInference(shorter, use) is { } pending)
        {
            this.BoundInitializerOrigin(pending, shorter, longer, use, condition);
            return true;
        }

        return this.ProvesOriginOutlives(longer, shorter, use, condition);
    }

    // SPEC 15.4.4, 10.1: the answer of FitOriginOutlives without its side effects, for applicability. The requirement that a value
    // outlive an Origin omitted in an open initializer is satisfiable (the asymmetry of plan T4 is kept), and only while a selected
    // call's fits are collected (collectingFits) is the value recorded as the Origin's bound; any other requirement is the pure proof.
    private bool FitsOriginOutlives(BoundOrigin longer, BoundOrigin shorter, Koto use, ulong condition = 0)
    {
        longer = this.OriginAtUse(longer, use);
        shorter = this.OriginAtUse(shorter, use);
        if (!OriginOutlives(longer, shorter) && shorter is { Kind: OriginKind.Inference, Open: false } && this.OpenInitializer(shorter, use) is { } local)
        {
            if (this.collectingFits)
            {
                this.CollectInitializerBound(local, shorter, longer, use, condition);
            }

            return true;
        }

        return this.ProvesOriginOutlives(longer, shorter, use, condition);
    }

    // SPEC 10.1: the Origin part of a fit (FitsTypeCore). During candidate evaluation and while a selected call's fits are collected
    // it is the pure answer; any other fit records its bounds at once (FitOriginOutlives).
    private bool FitOrigin(BoundOrigin longer, BoundOrigin shorter, Koto use, ulong condition = 0)
        => this.collectingFits || this.candidateTrials != 0 ? this.FitsOriginOutlives(longer, shorter, use, condition) : this.FitOriginOutlives(longer, shorter, use, condition);

    // The collector's record (CollectFitBounds): `bound` bounds the Origin `variable` that `local` omits.
    private void CollectInitializerBound(VariableKoto local, BoundOrigin variable, BoundOrigin bound, Koto use, ulong condition)
        => this.BoundInitializerOrigin(this.InitializerDeclaration(local), variable, bound, use, condition);

    // Records `bound` for a local's omitted Origin. A bound required only in some Semantics cases still bounds the Origin in every case.
    private void BoundInitializerOrigin(OriginDeclaration pending, BoundOrigin variable, BoundOrigin bound, Koto use, ulong condition)
    {
        pending.Replacements[variable] = pending.Replacements.TryGetValue(variable, out var previous) ? this.Meet(previous, bound) : bound;
        this.originStateVersion++;
        this.AddObligation(new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, null, bound, variable, Condition: condition));
    }

    private ConstraintProof CheckTypeOriginRelations(BoundType type, BindingScope scope)
    {
        if (type.Symbol is not { } symbol || !this.originDeclarations.TryGetValue(symbol.Declaration, out var declaration) || declaration.State != 3)
        {
            return ConstraintProof.Proven;
        }

        if (declaration.Failure is not null)
        {
            return ConstraintProof.Error;
        }

        foreach (var relation in declaration.Relations)
        {
            var a = this.SubstituteStoredOrigin(relation.Longer, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
            var b = this.SubstituteStoredOrigin(relation.Shorter, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
            if (!this.ProvesOriginOutlives(a, b, scope.Owner) || (relation.Equality && !this.ProvesOriginOutlives(b, a, scope.Owner)))
            {
                return ConstraintProof.Unknown;
            }
        }

        return ConstraintProof.Proven;
    }

    // SPEC 15.3.3, 15.6.1: a Type's own clauses are premises of its definition and obligations of each use, so a local annotation
    // proves them substituted with the Origins its Type binds, also for a Type nested in it. Each is an obligation at the Type
    // occurrence that ownership judges and reports as a declared relation with the Type's clause related; it is never a premise of
    // the body. A Function Type's inputs are its callee's premises, judged at each call's input (RequireInputPremises), so they are not entered.
    private void AddTypeClauseObligations(BoundType type, Koto occurrence, int depth = 0)
    {
        if (depth > 64)
        {
            return;
        }

        if (type.OriginArguments.Count != 0 && type.Symbol is { } symbol &&
            this.originDeclarations.TryGetValue(symbol.Declaration, out var declaration) && declaration.State == 3)
        {
            foreach (var relation in declaration.Relations)
            {
                var a = this.SubstituteStoredOrigin(relation.Longer, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
                var b = this.SubstituteStoredOrigin(relation.Shorter, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
                if (!ReferenceEquals(a, b))
                {
                    this.AddObligation(new(BindingObligationKind.OriginOutlives, occurrence, BindingDeadline.BodyOrigins, null, a, b, Equality: relation.Equality, Clause: relation.Syntax));
                }
            }
        }

        if (type.Kind == BoundTypeKind.Function)
        {
            return;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            this.AddTypeClauseObligations(type.Components[i], occurrence, depth + 1);
        }
    }

    private bool CheckCallOriginRelations(Koto function, BoundOrigin[] origins, BoundOrigin[] inputs, Koto use, BoundType? declaringType)
    {
        if (!this.originDeclarations.TryGetValue(function, out var declaration))
        {
            return true;
        }

        if (declaration.Failure is not null)
        {
            return false;
        }

        foreach (var relation in declaration.Relations)
        {
            var a = Substitute(relation.Longer);
            var b = Substitute(relation.Shorter);
            if (!this.ProvesOriginOutlives(a, b, use) || (relation.Equality && !this.ProvesOriginOutlives(b, a, use)))
            {
                return false;
            }
        }

        return true;

        BoundOrigin Substitute(BoundOrigin origin)
        {
            if (declaringType?.Symbol is { } owner)
            {
                origin = this.SubstituteStoredOrigin(origin, owner.Declaration, (BoundOrigin[])declaringType.OriginArguments);
            }

            return this.SubstituteStoredOrigin(origin, function, origins.AsSpan(0, function.BoundSymbol?.Schema?.Origins.Count ?? 0), inputs.AsSpan(0, InputOriginCount(function)));
        }
    }

    private void BindTypeOriginContracts()
    {
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is DeclarationContainerKoto container && this.scopes.TryGetValue(container, out var scope) &&
                this.BeginOriginDeclaration(container, scope) is { } declaration)
            {
                for (var b = 0; b < container.Bases.Count; b++)
                {
                    this.BindType(container.Bases[b], scope);
                }

                this.CompleteOriginDeclaration(declaration);
            }
            else if (AssociatedHead(this.nodes[i]) is OriginApplicationKoto)
            {
                var node = this.nodes[i];
                var associatedScope = this.scopes[node];
                var associatedOrigins = this.BeginOriginDeclaration(node, associatedScope);
                this.BindAssociatedFormation(node, associatedScope);
                this.CompleteOriginDeclaration(associatedOrigins);
            }
        }
    }
}
