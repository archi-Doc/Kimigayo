// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The Origin proof and the relation judge (SPEC 15.3.3, 15.3.6, 15.4.4, 15.6.1, 15.6.5; PLAN G74; DECISIONS R7-M66, R7-M120).
// - Responsibility: the pure proof (ProvesOriginOutlives; Final's IsVerifiedOriginObligation) and an Origin's one resolution at its
//   use (OriginAtUse); a fit's Origin part (FitOrigin, FitConjunct) and the bounds it gives an open initializer's omitted Origins
//   (FitOriginOutlives, CollectInitializerBound); a Type's clauses, proven (CheckTypeOriginRelations) or obligations at an occurrence
//   (AddTypeClauseObligations), a callee's in a comparison (CheckCallOriginRelations) and Type declarations' Origin contracts
//   (BindTypeOriginContracts); a comparison's region rows (LeavesBodyRows, ComparedRegionLimit); and the relation judge
//   (JudgeOriginRelation, JudgeOriginObligation) for OJ1, OJ3 and OJ4 alone: the declaration frame (JudgeDeclarationPosition,
//   FailedDeclaredRelation), Owned's static check (RefutedStatic) and a Callable comparison (OriginFailure, ComparedRegionLimit,
//   LeavesBodyRows).
// - Inputs: the premises at a use (EntailsOrigin, Binding.OriginPremises.cs), the Origin declarations and local initializers, the
//   classes (Binding.OriginClasses.cs) and the region graph.
// - Outputs: a proof's answer, a judgment (OriginJudgment), the obligations a fit or a Type's clauses form (AddObligation) and a
//   comparison's rows in its query's row sink (comparisonRows).
// - Invariants: a proof request leaves originStateVersion unchanged (PLAN G74's throw); between fixed Origins the judge reads premises
//   alone, so a relation they do not prove is Unknown.
// - Prohibitions: no body relation is judged here (FormBodyRelation forms its row and A5 judges it; a Type's clause over an Origin a body
//   formed is the row of the call that instantiated the Type, BodyRelation); no record (its callers record);
//   no message text.
public sealed partial class Binding
{
    // The row sink of every reader of a comparison's region conjuncts (CallableInstance.Rows; LeavesBodyRows): one reused list (warm
    // 0 B), each reader's segment from the end it found when it built its query, formed or dropped and truncated when it returns.
    private readonly List<BindingObligation> comparisonRows = new();

    // The one version of the Origin state that premise environments are built from: Origin declarations' formation and replacements,
    // local initializer inference, inherited declarations and the obligations whose region bounds R5 and J1 read. A reused environment (a
    // premise session's, a Callable comparison's, a body's oracle) is valid only at the version it was built at, and a proof request
    // leaves it unchanged (PLAN G74). It stays while a session's judge changes that state between its requests (DECISIONS R7-M120).
    private int originStateVersion;

    // The candidate evaluations in progress (EvaluateCallCandidates): the Origin part of a fit there is the pure answer, since only the
    // selected candidate's bounds are recorded, after selection (FitsOriginOutlives); a refusal read there is held for the selection
    // in the same way (Refusal.Settle, SettleSelected).
    private int candidateTrials;

    // Set while a selected call's fits are collected (CollectFitBounds): the Origin part of a fit is then the pure answer, and the
    // bounds of Origins omitted in open initializers are recorded for that call (FitsOriginOutlives).
    private bool collectingFits;

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
        // reads it): a raw Place borrow's anchor is an inferred region (SPEC 5.2.2), a local region is judged through its sources; the
        // worst operand decides.
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
    // turns into ρ. A record's Supplier is this, at its use. A `let` region, written or a declaration's replacement, reads as its value in
    // its own body (RegionValueAtUse; PLAN G492-a (ii)).
    internal BoundOrigin SuppliedAtUse(BoundOrigin origin, Koto use) => this.CompletedOrigin(this.RegionValueAtUse(this.DeclaredAtUse(origin, use), use));

    // O-400 (b) (PLAN G492-a (ii) Revision 5): a failed condition of a Callable comparison is the located limit where a direction its
    // ends' values do not prove has, at its shorter end, a region of the use's body published as an inclusion (ReadsLetValue: a `let`
    // region, or a slot an equality fixes to one, FixedRegion): the comparison reads that end as the region (O-363 (a)), which may lie
    // inside its value, so only the region reading decides the condition, and the uses of the compared Type read values (LeavesBodyRows
    // leaves no row their values do not prove), until (a) or (a') designs the region reading at uses. A direction with a fixed shorter end
    // keeps its record: a `let` region outlives nothing its value does not, so the readings agree; so does a region equal to its value (a
    // slot fixed by equality to an Origin through which no inclusion is read, FixedRegion; SPEC 15.3.6) at either end.
    // Valid and invalid programs alike: no fact at the comparison separates them, since each verdict is decided at the uses of the
    // compared Type (us2 and us2n are identical through the comparison). So the invalid us family is rejected at the comparison by
    // this limit at dev's UnprovenOriginContract_Kd site, a record that differs from the one O-400 (b) states (Q13; no acceptance move).
    internal bool ComparedRegionLimit(BoundOrigin longer, BoundOrigin shorter, bool equality, Koto use)
    {
        return Shrinks(longer, shorter) && (!equality || Shrinks(shorter, longer)) &&
            (this.ReadsLetValue(shorter, use) || (equality && this.ReadsLetValue(longer, use)));

        bool Shrinks(BoundOrigin a, BoundOrigin b)
            => this.ReadsLetValue(b, use) || this.JudgeOriginRelation(a, b, use, this.formingBody) == OriginJudgment.Proven;
    }

    // Whether `end` holds a region of the use's body that may lie inside the value a use reads for it (RegionValueAtUse): a value published
    // as an inclusion (LocalRegion.Equality false): a `let` region's, or that of a `let` or `var` slot an equality fixes to such an end
    // (the name, C-548's, says its `let` case). A region whose value is an equality equals it, so it is none and the value reading
    // decides (SPEC 15.3.6 "Unless fixed by equality"; DECISIONS R8-M56 as amended, C-548, C-554). Read by O-400 (b)'s limit
    // (ComparedRegionLimit) and by FixedRegion, whose region equal to such an end lies inside its value too.
    internal bool ReadsLetValue(BoundOrigin end, Koto use)
    {
        for (var i = 0; this.regionValues != 0 && i < (end.Kind == OriginKind.Intersection ? end.Operands.Count : 1); i++)
        {
            if (this.ValueRow(end.Kind == OriginKind.Intersection ? end.Operands[i] : end, BodyOwner(use)) is { Equality: false })
            {
                return true;
            }
        }

        return false;
    }

    // Whether `end` holds a region of the use's body with a published value, an inclusion or an equality, which a use reads as that value
    // (RegionValueAtUse changes it): the comparison leaves a row over it only where the values prove it (LeavesBodyRows, O-400 (b)).
    internal bool ReadsAsValue(BoundOrigin end, Koto use) => !ReferenceEquals(this.RegionValueAtUse(end, use), end);

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

    // SPEC 15.6.1: the judge reads premises alone, so a relation between two fixed Origins it does not prove is Unknown, a region at a fixed
    // end included, whose value is no premise: no reader takes it as holding. A caller that leaves a row asks ConstrainsRegions first.
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
                return this.initializerOrigins.TryGetValue(variable, out var declaration) && declaration.Resolves ? null : variable;
            }
        }

        return null;
    }

    // The one resolution of an Origin at its use (2a0-D D4), which every judgment, record, the old reporter and the C1-F4 view read: the
    // atom that supplies it there (SuppliedAtUse; a deferred result slot's atom Final proved first read as its completion, R7-O2 O2-F),
    // then the receiver of each classified Closure that encloses the use (ResolveReceiver), innermost first.
    private BoundOrigin OriginAtUse(BoundOrigin origin, Koto use) => this.ReceiverAtUse(this.SuppliedAtUse(origin, use), use);

    // SPEC 15.3.7, 15.4.4 under the owner's O-363 (a): an end of a relation of the Callable comparison `comparison` (InComparison) at its
    // use, in its walk and in its conditions alike. A comparison is a later use that constrains a `let` local's slot, so a `let` region of
    // the use's own body stays the region, an inference variable, never its value: RegionValueAtUse's one exception. Outside a comparison
    // it is OriginAtUse.
    private BoundOrigin OriginAtUse(BoundOrigin origin, Koto use, in CallableInstance comparison)
        => InComparison(comparison) ? this.SlotAtUse(origin, use) : this.OriginAtUse(origin, use);

    // An end of a body row at its use, as the row's producer read it (the owner's O-385 (a): one reading per stored Origin): a Callable
    // comparison's row (Compared) as the comparison does, the slot (DeclaredAtUse), every other row as its value (SuppliedAtUse). The one
    // reading of a row's ends and the one reader of Compared: every reader of a row (the C1-F4 view, the local-region graph, the pending
    // list) reads its ends so, and what it stores from them it keeps as read.
    private BoundOrigin SuppliedAtUse(BoundOrigin origin, in BindingObligation obligation)
        => obligation.Compared ? this.DeclaredAtUse(origin, obligation.Use) : this.SuppliedAtUse(origin, obligation.Use);

    // A row's end as its producer read it (SuppliedAtUse with the row), its receivers resolved: OriginAtUse's composition for a row.
    private BoundOrigin OriginAtUse(BoundOrigin origin, in BindingObligation obligation)
        => this.ReceiverAtUse(this.SuppliedAtUse(origin, obligation), obligation.Use);

    // An Origin at its use as written: OriginAtUse with a `let` region kept as the slot, not its value (RegionValueAtUse). A written
    // Origin binds so (BindOrigin), and a comparison's relation reads so (above); Binding.LocalRegions.cs's contract.
    private BoundOrigin SlotAtUse(BoundOrigin origin, Koto use) => this.ReceiverAtUse(this.DeclaredAtUse(origin, use), use);

    // The Origin declarations and local initializers that enclose the use applied, innermost first.
    private BoundOrigin DeclaredAtUse(BoundOrigin origin, Koto use)
    {
        origin = this.CompletedOrigin(origin);
        for (var node = use; node is not null; node = node.Parent)
        {
            if (this.originDeclarations.TryGetValue(node, out var declaration) && declaration.Resolves)
            {
                origin = this.ResolveOrigin(origin, declaration);
            }

            if (this.initializerOrigins.TryGetValue(node, out var inferred) && inferred.Resolves)
            {
                origin = this.ResolveOrigin(origin, inferred);
            }
        }

        return origin;
    }

    // The receiver of each classified Closure that encloses the use, innermost first (ResolveReceiver).
    private BoundOrigin ReceiverAtUse(BoundOrigin origin, Koto use)
    {
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

    // SPEC 15.3.6 (PLAN G74): a pure question: whether the premises visible at `use` entail `longer outlives shorter`
    // (EntailsOrigin). A request leaves inference, obligation and declaration state unchanged, which every request checks
    // (originStateVersion). `condition` is the Semantics case the relation is required in (SPEC 15.6.5).
    private bool ProvesOriginOutlives(BoundOrigin longer, BoundOrigin shorter, Koto use, ulong condition = 0)
        => this.ProvesOriginOutlives(longer, shorter, use, default(CallableInstance), condition);

    // The request made in the Callable comparison `comparison`, which assumes its required signatures' own well-formedness too (the
    // owner's (a)); outside a comparison it is the default instance. `excluded` is the premise rule the relation may not rest on
    // (EntailsOrigin; a HeldResult row's, PublishedPremise).
    private bool ProvesOriginOutlives(
        BoundOrigin longer, BoundOrigin shorter, Koto use, in CallableInstance comparison, ulong condition = 0, OriginPremiseRule excluded = OriginPremiseRule.Target)
    {
        var version = this.originStateVersion;
        longer = this.OriginAtUse(longer, use, comparison);
        shorter = this.OriginAtUse(shorter, use, comparison);
        this.OriginProofMetrics?.Enter(this.originPremiseDepth);
        var proven = this.EntailsOrigin(longer, shorter, use, comparison, condition, excluded);
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
    private bool FitOriginOutlives(BoundOrigin longer, BoundOrigin shorter, Koto use, in CallableInstance comparison, ulong condition = 0)
    {
        if (!OriginOutlives(longer, shorter) && shorter is { Kind: OriginKind.Inference, Open: false } && this.OpenInitializerInference(shorter, use) is { } pending)
        {
            this.BoundInitializerOrigin(pending, shorter, longer, use, condition);
            return true;
        }

        return this.ProvesOriginOutlives(longer, shorter, use, comparison, condition);
    }

    // SPEC 15.4.4, 10.1: the answer of FitOriginOutlives without its side effects, for applicability. The requirement that a value
    // outlive an Origin omitted in an open initializer is satisfiable (the asymmetry of plan T4 is kept), and only while a selected
    // call's fits are collected (collectingFits) is the value recorded as the Origin's bound; any other requirement is the pure proof.
    private bool FitsOriginOutlives(BoundOrigin longer, BoundOrigin shorter, Koto use, in CallableInstance comparison, ulong condition = 0)
    {
        if (!OriginOutlives(longer, shorter) && shorter is { Kind: OriginKind.Inference, Open: false } && this.OpenInitializer(shorter, use) is { } local)
        {
            if (this.collectingFits)
            {
                this.CollectInitializerBound(local, shorter, longer, use, condition);
            }

            return true;
        }

        return this.ProvesOriginOutlives(longer, shorter, use, comparison, condition);
    }

    // SPEC 10.1: the Origin part of a fit (RelationWalk). During candidate evaluation and while a selected call's fits are collected
    // it is the pure answer; any other fit records its bounds at once (FitOriginOutlives). `comparison` is the Callable comparison the
    // fit is part of, whose premises its proof assumes. Its ends arrive as written: they are instantiated (CallableInstance.Instantiate)
    // and resolved here, once (OriginAtUse, as the comparison reads them), and the comparison's one decision for a region conjunct reads
    // them as written (LeavesBodyRows).
    // SPEC 23.1.2 (DECISIONS Q13 rule W; PLAN G739): a conjunct one of whose ends rests on a failed part holds and gives no evidence
    // (HoldsResting, after the ordinary answer). A recording fit that held so publishes it (fitRested): its entry leaves the fit's
    // positions as rows of its body (CheckTypeUse), which A5 judges with the atom Dependent (a chain through it between its ends;
    // DECISIONS R4-M118), or the declaration judge rests (JudgeDeclarationRows). The walks entered through IsSubtypeAt (a callable
    // contract) and CallableSignatureFits set it too and form no row: no entry reads it there, and the next fit's entry clears it.
    private bool FitOrigin(BoundOrigin writtenLonger, BoundOrigin writtenShorter, Koto use, in CallableInstance comparison, ulong condition = 0)
    {
        var longer = this.OriginAtUse(comparison.Instantiate(writtenLonger), use, comparison);
        var shorter = this.OriginAtUse(comparison.Instantiate(writtenShorter), use, comparison);
        if (this.FitConjunct(longer, shorter, use, comparison, condition))
        {
            return true;
        }

        if (!this.HoldsResting(longer, shorter, condition, new FitLeaf(this, use, comparison)))
        {
            return this.LeavesBodyRows(writtenLonger, writtenShorter, use, comparison, condition, null);
        }

        this.fitRested |= !this.collectingFits && this.candidateTrials == 0;
        return true;
    }

    // SPEC 15.6.1 under the owner's O-261 Q2 (a) (DECISIONS <G492-a (iii)>): the one decision for a condition of a comparison that its
    // premises did not prove, called by the condition's owning check (the walk's leaf, the clause check, the well-formedness visit; never
    // the solve). With a local region of the forming body at its longer end it is that body's relation, a row A5 judges by SPEC 15.6.5.
    // This member owns which ends leave a row (DECISIONS R4-M222) and keeps the one-way form (a region at the longer end only, not
    // RegionChain's either end): the rows it leaves are the longer end's conjuncts, and a region at a condition's shorter end is judged by
    // the comparison's premises alone, as before R7-O2 A1 widened RegionChain for the body's own relations.
    // A meet at the longer end is its operands' conjuncts (SPEC 15.3.6): a conjunct with a fixed longer end keeps the premises alone (J1
    // included) and the condition fails with it; a region operand's conjunct the premises do not prove is a row. The comparison forms
    // no row: it publishes each to its query's row sink (CallableInstance.Rows), whose reader forms or drops them; a query without one
    // (a Constraint's proof, conformance, specialization, a contract match) reads the conjunct as not proven: premises alone.
    // A row's every end is one A5 judges in the body (the Middle's option (b), board C-400), so a shorter end that is another
    // declaration's atom (the view's row 9, ForeignDeclarationAtom) keeps the comparison's own record (SPEC 15.6.1, premises alone) where
    // the comparison decides it by SPEC: a Function Item's universal or an anonymous function's per-call Origin, both of which it solves
    // (SPEC 15.3.7, DefinitionContractFits; board D-822), or an enclosing comparison's required per-call Origin, which is rigid (a30:
    // g's result compared under the outer required j0). A required per-call Origin of this comparison is the L1 lift's. A condition
    // naming a deferred result slot's atom not yet completed, at either end (DeferredIn, R7-O2 O2-F), is one row before that guard: its
    // premises cannot decide it before Final (SPEC 15.4.4), so A5 judges it with the atom completed or reads a refuted atom's Dependent
    // end, and where A5 cannot class the other end its located limit stands, never a record judged without the fact. The ends arrive
    // as written; the comparison instantiates them here.
    private bool LeavesBodyRows(BoundOrigin longer, BoundOrigin shorter, Koto use, in CallableInstance comparison, ulong condition, Koto? clause)
    {
        if (comparison.Rows is not { } rows)
        {
            return false;
        }

        longer = this.OriginAtUse(comparison.Instantiate(longer), use, comparison);
        shorter = this.OriginAtUse(comparison.Instantiate(shorter), use, comparison);
        if (this.formingBody.IsBody && (this.DeferredIn(longer) is not null || this.DeferredIn(shorter) is not null))
        {
            rows.Add(new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, null, longer, shorter, Clause: clause, Condition: condition) { Compared = true });
            return true;
        }

        if (!(this.formingBody.IsBody && HasLocalRegion(longer)) ||
            (this.ForeignDeclarationAtom(shorter, this.formingBody) && !comparison.IsRequiredSlot(shorter)))
        {
            return false;
        }

        var conjuncts = longer.Kind == OriginKind.Intersection ? longer.Operands.Count : 1;
        for (var i = 0; i < conjuncts; i++)
        {
            var operand = conjuncts == 1 ? longer : longer.Operands[i];
            if (!HasLocalRegion(operand) && !this.ProvesOriginOutlives(operand, shorter, use, comparison, condition))
            {
                return false;
            }
        }

        // The L1 lift: a required per-call Origin j at the shorter end is rigid: the comparison holds for every j its premises allow (SPEC
        // 15.3.7), so `R outlives j` holds exactly when R outlives the meet of the body's Origins those premises put above j, `static`
        // when they put none (j may be any Origin then). The row names that bound, an Origin of the body.
        var bound = comparison.IsRequiredSlot(shorter) ? this.RequiredSlotBound(shorter, use, comparison, condition) : shorter;
        for (var i = 0; i < conjuncts; i++)
        {
            var operand = conjuncts == 1 ? longer : longer.Operands[i];
            if (HasLocalRegion(operand) && (conjuncts == 1 || !this.ProvesOriginOutlives(operand, shorter, use, comparison, condition)))
            {
                // O-400 (b): a row over a region of the use's body with a value only where its ends' values prove it too, as every use of
                // the compared Type reads them; otherwise the condition fails (ComparedRegionLimit classes it). A row over `var` regions
                // alone is as before.
                if ((this.ReadsAsValue(operand, use) || this.ReadsAsValue(bound, use)) && this.JudgeOriginRelation(this.OriginAtUse(operand, use), this.OriginAtUse(bound, use), use, this.formingBody, condition) != OriginJudgment.Proven)
                {
                    return false;
                }

                rows.Add(new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, null, operand, bound, Clause: clause, Condition: condition) { Compared = true });
            }
        }

        return true;
    }

    // The L1 lift's bound: the meet of the Origins of the body that the comparison's premises entail to outlive the required per-call
    // Origin `slot` (the closure from it; a per-call Origin of a Function Type, a top-like node or an unnamed node is no bound), `static`
    // when none; `slot` itself where the premises cannot be read (MaxOriginPremiseDepth), so A5 keeps its located limit there.
    private BoundOrigin RequiredSlotBound(BoundOrigin slot, Koto use, in CallableInstance comparison, ulong condition)
    {
        if (this.BeginRequest(use, comparison, slot, null) is not { } environment)
        {
            return slot;
        }

        BoundOrigin bound = BoundOrigin.Static;
        try
        {
            var closure = environment.Closure;
            var target = closure.Node(slot, out _);
            _ = closure.Close(target, -1, condition);
            var first = true;
            for (var node = 0; node < closure.NodeCount; node++)
            {
                if (node != target && closure.Reached(node) && closure.Origin(node) is { Kind: not (OriginKind.Static or OriginKind.Anchor or OriginKind.Intersection) } origin &&
                    origin.Binder is not FunctionTypeKoto)
                {
                    bound = first ? origin : this.Meet(bound, origin);
                    first = false;
                }
            }
        }
        finally
        {
            this.EndRequest(environment);
        }

        return bound;
    }

    // A fit at a value that read its comparisons with the row sink from `start` (comparisonRows): outside a trial or a collected fit, the
    // region conjuncts of its comparisons that held are the body's rows (AddObligation, the one writer), also when another part of the
    // fit failed (SPEC 23.1.2: each an independent requirement; a failed comparison kept none, RelationWalk); the segment is then dropped.
    private void FormComparisonRows(int start)
    {
        for (var i = start; this.candidateTrials == 0 && !this.collectingFits && i < this.comparisonRows.Count; i++)
        {
            this.AddObligation(this.comparisonRows[i]);
        }

        this.DropComparisonRows(start);
    }

    // SPEC 15.6.1: a selection, or a re-reading of comparisons that a fit at the value judged and formed, takes the region conjuncts from
    // `start` and forms none.
    private void DropComparisonRows(int start) => this.comparisonRows.RemoveRange(start, this.comparisonRows.Count - start);

    // The ordinary answer of one conjunct, its ends resolved at the use: pure in a candidate trial or a collected fit, recording otherwise.
    private bool FitConjunct(BoundOrigin longer, BoundOrigin shorter, Koto use, in CallableInstance comparison, ulong condition)
        => this.collectingFits || this.candidateTrials != 0 ? this.FitsOriginOutlives(longer, shorter, use, comparison, condition) :
            this.FitOriginOutlives(longer, shorter, use, comparison, condition);

    // The collector's record (CollectFitBounds): `bound` bounds the Origin `variable` that `local` omits.
    private void CollectInitializerBound(VariableKoto local, BoundOrigin variable, BoundOrigin bound, Koto use, ulong condition)
        => this.BoundInitializerOrigin(this.InitializerDeclaration(local), variable, bound, use, condition);

    // Records `bound` for a local's omitted Origin. A bound required only in some Semantics cases still bounds the Origin in every case.
    private void BoundInitializerOrigin(OriginDeclaration pending, BoundOrigin variable, BoundOrigin bound, Koto use, ulong condition)
    {
        this.MeetInitializerBound(pending, variable, bound);
        this.AddObligation(new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, null, bound, variable, Condition: condition));
    }

    // SPEC 15.3.3, 15.6.1: the declared Origin relations of an instantiated Type, proven from the premises at `at` (a scope's owner for
    // a Type formed in a scope, the occurrence for an associated formation, whose walk reaches a header's getter, HeaderGetter); Unknown
    // when one is not. A relation over an Origin a body formed is not this proof's: it is a row of the call that instantiated the Type
    // (BodyRelation; DECISIONS R7-M66, PLAN G990), which A5 judges and which never decides applicability.
    private ConstraintProof CheckTypeOriginRelations(BoundType type, Koto at)
    {
        if (type.Symbol is not { } symbol || !this.originDeclarations.TryGetValue(symbol.Declaration, out var declaration) || !declaration.Completed)
        {
            return ConstraintProof.Proven;
        }

        foreach (var relation in declaration.Relations)
        {
            var a = this.SubstituteStoredOrigin(relation.Longer, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
            var b = this.SubstituteStoredOrigin(relation.Shorter, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
            if (!BodyRelation(a, b) &&
                (!this.ProvesOriginOutlives(a, b, at) || (relation.Equality && !this.ProvesOriginOutlives(b, a, at))))
            {
                return ConstraintProof.Unknown;
            }
        }

        return ConstraintProof.Proven;
    }

    // SPEC 15.3.3, 15.6.1: a Type's own clauses are premises of its definition and obligations of each use, so a local annotation
    // proves them substituted with the Origins its Type binds, also for a Type nested in it. A fixed-fixed one (no body chain) that the
    // premises at the occurrence entail (PublishedPremise: the enclosing declarations' and, inside a nested signature, its own inputs',
    // its per-call Origins fixed, so for every choice of them) is Binding's direct proof and forms no row (2a0-D D1; as
    // CollectDeclarationRows); each other, a body chain included (a local's inferred Origin, which region inference reads), is an
    // obligation at the Type occurrence that ownership judges and reports as a declared relation with the Type's clause related; it is
    // never a premise of the body. A Function Type's inputs are its callee's premises, judged at each call's input (RequireInputPremises), so they are not entered;
    // a nested signature's written result, a Function Type's or an anonymous function's, is formed at its own occurrence (FormNestedResult;
    // DECISIONS R4-M162).
    private void AddTypeClauseObligations(BoundType type, Koto occurrence)
    {
        this.termWalks++;
        if (type.OriginArguments.Count != 0 && type.Symbol is { } symbol &&
            this.originDeclarations.TryGetValue(symbol.Declaration, out var declaration) && declaration.Completed)
        {
            foreach (var relation in declaration.Relations)
            {
                var a = this.SubstituteStoredOrigin(relation.Longer, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
                var b = this.SubstituteStoredOrigin(relation.Shorter, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
                var obligation = new BindingObligation(BindingObligationKind.OriginOutlives, occurrence, BindingDeadline.BodyOrigins, null, a, b, Equality: relation.Equality, Clause: relation.Syntax);
                if (!ReferenceEquals(a, b) &&
                    (ConstrainsRegions(this.OriginAtUse(a, occurrence), this.OriginAtUse(b, occurrence), this.formingBody) || !this.PublishedPremise(obligation, a, b)))
                {
                    this.AddObligation(obligation);
                }
            }
        }

        if (type.Kind == BoundTypeKind.Function)
        {
            return;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            this.AddTypeClauseObligations(type.Components[i], occurrence);
        }
    }

    private bool CheckCallOriginRelations(
        Koto function, BoundOrigin[] origins, BoundOrigin[] inputs, Koto use, BoundType? declaringType, in CallableInstance comparison)
    {
        if (!this.originDeclarations.TryGetValue(function, out var declaration))
        {
            return true;
        }

        foreach (var relation in declaration.Relations)
        {
            var a = Substitute(relation.Longer);
            var b = Substitute(relation.Shorter);
            if ((!this.ProvesOriginOutlives(a, b, use, comparison) && !this.LeavesBodyRows(a, b, use, comparison, 0, relation.Syntax)) ||
                (relation.Equality && !this.ProvesOriginOutlives(b, a, use, comparison) && !this.LeavesBodyRows(b, a, use, comparison, 0, relation.Syntax)))
            {
                this.failedCallClause = (a, b, relation.Equality, relation.Syntax);
                return false;
            }
        }

        return true;

        BoundOrigin Substitute(BoundOrigin origin)
            => this.Substitute(origin, SubstitutionOf(function, declaringType, default, default, origins, inputs));
    }

    private void BindTypeOriginContracts()
    {
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is DeclarationContainerKoto container && this.scopes.TryGetValue(container, out var scope) &&
                this.BeginOriginDeclaration(container, scope) is { } declaration)
            {
                for (var b = 0; container is not ContractKoto && b < container.Bases.Count; b++)
                {
                    this.BindType(container.Bases[b], scope); // A Contract's parents are its registration's (BindNode).
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

    // The fit's leaf of HoldsResting: one conjunct's ordinary answer at the fit's use (FitConjunct).
    private readonly struct FitLeaf(Binding binding, Koto use, CallableInstance comparison) : IOriginConjunct
    {
        public bool Holds(BoundOrigin longer, BoundOrigin shorter, ulong condition)
            => binding.FitConjunct(longer, shorter, use, comparison, condition);
    }

    // The proof's leaf of HoldsResting: one conjunct's proof at `use` in `comparison` (ProvesOriginOutlives), for the Origin inference's
    // bounds (SolveOriginInference) and an implementation's claims (ClaimHolds).
    private readonly struct ProofLeaf(Binding binding, Koto use, CallableInstance comparison) : IOriginConjunct
    {
        public bool Holds(BoundOrigin longer, BoundOrigin shorter, ulong condition)
            => binding.ProvesOriginOutlives(longer, shorter, use, comparison, condition);
    }
}
