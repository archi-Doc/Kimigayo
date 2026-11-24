// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The causes of each pair occurrence that a final check failed with a Language cause, for its evidence and for the separate record
    // of an unproven target role (Check).
    private readonly FormationCauseRows formationCauses;

    // Conformance verifications and agreements in progress: a limit context, not an obligation (the obligation table holds the paths
    // being verified), read by ResolveConformance alone: inside them a conformance limited by OCC-X stays Unknown.
    private int conformanceChecks;

    // The conformance uses whose completeness rests on a specification, settled at publication (SettleSpecificationLinks).
    private List<Koto>? specificationLinks;

    // SPEC 23.1.2: the declaration whose own validation step runs (ValidateConstraintEnvironments), when it entered the step without a
    // failure. A failure the step records against it (a direction contradiction, a premise error, a proof's) is no failed context for the
    // step's own other checks, a self-read being no failed input; the context fails for every later check (DeclarationFailed).
    private Koto? validationStep;

    // The change signal (invalidations) at the start of the latest walk of the pass's environments, -1 at the pass's start (ResetPass):
    // a walk that changed the binding moved the signal past it (ValidateConstraintEnvironments).
    private int environmentsJudged = -1;

    // The independent causes of an unproven Type role or pair formation (TypeRoleCauses).
    [Flags]
    internal enum TypeRoleCause : byte
    {
        None = 0,

        // SPEC 8.1.2: no outer-Origin slot, although a borrow binding is admitted (MissingOriginBinding_Kd).
        MissingSlot = 1,

        // SPEC 8.1.2: a static outer-Origin slot, although an exclusive borrow binding is admitted (InvalidOriginBinding_Kd).
        StaticExclusive = 2,

        // SPEC 8.7.3: the target is not proven a value Type or an Object Target (UnprovenConstraint_Kd).
        Role = 4,
    }

    /// <summary>
    /// Computes the admitted set of a Semantics binding from every available premise on it (SPEC 8.3):
    /// names and categories contribute their members, and/or/not intersect, unite and complement,
    /// separate clauses intersect, and no premise admits all nine Semantics. An Invalid environment's Semantics premises stay premises
    /// (a failed clause's fact is an Error, never a Semantics premise), so None is the 8.3.4 Error: the binder admits no case.
    /// </summary>
    /// <param name="whole">The pair's whole Type.</param>
    /// <param name="scope">The scope whose premises apply.</param>
    /// <returns>The admitted Semantics.</returns>
    internal SemanticsMask AdmittedSemantics(BoundType whole, BindingScope scope)
    {
        var admitted = SemanticsMask.All;
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (SemanticsPremise(fact, whole) && (this.AvailableConstraintFact(environment, fact, out _) || environment.IsFailed(fact)))
                {
                    admitted &= SemanticsSet(fact);
                }
            }
        }

        return admitted;
    }

    /// <summary>Tests whether a constraint speaks only about the Semantics of <paramref name="whole"/>; other premises never restrict its admitted set (SPEC 8.3).</summary>
    private static bool SemanticsPremise(BoundConstraint constraint, BoundType whole)
        => constraint.Kind switch
        {
            ConstraintKind.Semantics => ReferenceEquals(constraint.Subject, whole),
            ConstraintKind.And or ConstraintKind.Or => SemanticsPremise(constraint.Left!, whole) && SemanticsPremise(constraint.Right!, whole),
            ConstraintKind.Not => SemanticsPremise(constraint.Left!, whole),
            _ => false,
        };

    /// <summary>Computes the Semantics set of a premise accepted by <see cref="SemanticsPremise"/>: and/or/not intersect, unite and complement.</summary>
    private static SemanticsMask SemanticsSet(BoundConstraint constraint)
        => constraint.Kind switch
        {
            ConstraintKind.Semantics => constraint.Mask,
            ConstraintKind.And => SemanticsSet(constraint.Left!) & SemanticsSet(constraint.Right!),
            ConstraintKind.Or => SemanticsSet(constraint.Left!) | SemanticsSet(constraint.Right!),
            _ => SemanticsMask.All & ~SemanticsSet(constraint.Left!),
        };

    /// <summary>Gets the subject of one clause's proposition (SPEC 8.3): every atom of `T is A or B`, `T is A and B` or `T is not A`
    /// carries the clause's subject and the composite only its operands; an Error or Unresolved atom has none.</summary>
    private static BoundType? ClauseSubject(BoundConstraint constraint)
        => constraint.Kind switch
        {
            ConstraintKind.And or ConstraintKind.Or => ClauseSubject(constraint.Left!) ?? ClauseSubject(constraint.Right!),
            ConstraintKind.Not => ClauseSubject(constraint.Left!),
            _ => constraint.Subject,
        };

    // The declaration a clause pending in Provisional mode leaves pending (BU-151; SPEC 8.6.2: a conformance is Refuted only when every
    // path fails): its own declaration, or for a conditional conformance's target the Type, whose conformances wait on the Contract
    // (SPEC 8.8.1, 8.8.3). A condition is a premise: a proof applying the conformance reads its pending atom as Unknown
    // (CheckConstraints), so it leaves no declaration pending.
    private static DeclarationKoto? PendingDeclaration(IsKoto clause) => clause.Parent switch
    {
        DeclarationKoto declaration => declaration,
        SyntaxFormKoto { Akind: KotoKind.ConditionalConformance, Parent: DeclarationKoto type, Operands: [var target, ..] } when ReferenceEquals(target, clause) => type,
        _ => null,
    };

    private static Koto? ProjectionDeclarationOwner(Koto use)
    {
        if (((Koto?)FunctionSignatureOwner(use) ?? PropertySignatureOwner(use)) is { } signature)
        {
            return signature;
        }

        var node = use;
        while (node.Parent is { } parent && node is not (FunctionKoto or PropertyKoto))
        {
            if (node is IsKoto clause)
            {
                if (parent is SyntaxFormKoto premises && premises.Parent is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } conditional && ReferenceEquals(conditional.Operands[1], premises))
                {
                    return conditional.Operands[0];
                }

                if (clause.IsAssociatedConstraint)
                {
                    return parent is ContractKoto ? parent : clause;
                }

                IReadOnlyList<Koto>? constraints = parent switch
                {
                    FunctionKoto function => function.TypeConstraints,
                    DeclarationContainerKoto container when container is ContractKoto || !IsSelfConstraint(clause) => container.ConstraintNodes,
                    _ => null,
                };
                if (constraints is not null)
                {
                    for (var i = 0; i < constraints.Count; i++)
                    {
                        if (ReferenceEquals(clause, constraints[i]))
                        {
                            return parent;
                        }
                    }
                }
            }

            if (parent is DeclarationContainerKoto)
            {
                break;
            }

            node = parent;
        }

        if (node.Parent is StructKoto structure && ReferenceEquals(node, structure.BaseClause))
        {
            return node;
        }

        return node is SyntaxFormKoto { Akind: KotoKind.EnumCase, Parent: EnumKoto enumeration } ? enumeration : null;
    }

    // SPEC 8.3, 8.3.1: the one table of a proof's outcome to its failure. A Proven outcome is no failure: its record is a broken contract.
    private static BindingFailure ConstraintFailureOf(ConstraintProof proof) => proof switch
    {
        ConstraintProof.Refuted => BindingFailure.UnsatisfiedConstraint,
        ConstraintProof.Unknown => BindingFailure.UnprovenConstraint,
        ConstraintProof.Error => BindingFailure.InvalidConstraint,
        _ => BindingFailure.Internal,
    };

    // The argument a selected call binds a Callable clause's slot with (CallableOriginFailure's ArgumentBinding).
    private Koto? CallableArgument(Koto use, IsKoto clause)
    {
        if (use is not InvocationKoto invocation || this.CallOf(invocation) is not { Target.Declaration: FunctionKoto function } plan ||
            clause.BoundConstraint?.Subject is not { Kind: BoundTypeKind.Parameter, Symbol: { } slot })
        {
            return null;
        }

        foreach (var operation in plan.ArgumentOperations)
        {
            if (operation is { Source: { } source, ParameterIndex: >= 0 and var index } && index < function.Parameters.Count &&
                function.Parameters[index].Type.BoundType is { } pattern && ReferenceEquals(CallableSlotType(pattern, CallSlotOwner(function))?.Symbol, slot))
            {
                return source;
            }
        }

        return null;
    }

    // R7-C3 (B1'; SPEC 8.2.3, 23.1.2; F's C2 rulings 250, 252, 255): the declarations' premise failures reach their fixpoint before the
    // bodies are bound, so no reader records against a premise whose verdict can still change. A premise fails only on a failed fact (its
    // proof, or a projection input of its clause, answers Error); the failed set only grows (FailPremise), at most a round per premise.
    // An unproven input fails no premise: it is its declaration's own requirement, recorded after the bodies with its uses' records
    // (ValidateDeclarationProjectionInputs, which names it).
    private void SettlePremiseFailures(BindingMode mode)
    {
        var failed = this.ValidateConstraintEnvironments(mode);
        while (mode == BindingMode.Final && (this.FailPremiseInputs() | failed))
        {
            this.ClearCapabilityResults();
            failed = this.ValidateConstraintEnvironments(mode);
        }
    }

    // Late witness/Effect failures invalidate declarations even when normalization already erased
    // their projections. Reuse the ordinary validation until declaration states stop changing. The closed clauses are judged before
    // the environments in each round (the condition's order): a source state published since (PublishSourceStates) reaches a walk's
    // Error only through the failure ValidateClosedTypeConstraints records first (invalidations).
    private void InvalidateProjectionCertificates(BindingMode mode)
    {
        while (this.ValidateClosedTypeConstraints(mode) | this.ValidateDeclarationProjectionInputs(mode) | this.ValidateConstraintEnvironments(mode))
        {
            this.ClearCapabilityResults();
            this.ValidateBaseDeclarations(mode);
            this.ValidateProperties(mode);
            this.ValidateConformances(mode, true);
        }

        this.ClearCapabilityResults();
    }

    // The running pass's environments, its own modules' scopes (In, PLAN G949 S1-r2), judged at the validation step and again in a
    // certificate round only when a judgment's inputs may have changed since the last walk: a judgment acts on an Error, which rests on a
    // failed input or on contradictory evidence that only a failure changes (the rounds' contract), and every write of either advances
    // the change signal (invalidations), the walk's own included, so the walk runs again when the signal moved since the last one began.
    private bool ValidateConstraintEnvironments(BindingMode mode)
    {
        if (this.environmentsJudged == this.invalidations)
        {
            return false;
        }

        this.environmentsJudged = this.invalidations;
        var changed = false;
        foreach (var scope in this.scopes.Values.In(this.pass))
        {
            if (scope.Constraints is not { } environment)
            {
                continue;
            }

            // SPEC 23.1.2 (F's C2 rulings 320, 324): the direction contradiction, the identity premise error (once per pass, so the
            // certificate rounds terminate; SPEC 8.2.3) and the facts' proofs are independent: all are judged on the premises as the step
            // found them, none reading a failure this step records against the owner as its failed context (validationStep). A
            // contradiction then fails the pair's own premises alone (FailPairPremise); its report site keeps a member from reporting it
            // again (MetInParent), so every check reading no premise of the pair is judged.
            this.validationStep = scope.Owner.BindingFailure == BindingFailure.None ? scope.Owner : null;
            var state = scope.Owner.BindingState;
            var contradiction = this.ValidateArithmeticDirections(scope);
            changed |= state != scope.Owner.BindingState;
            changed |= this.RecordIdentityPremiseError(scope, mode);
            var judged = this.JudgeFacts(scope, environment, environment, mode, ref changed);

            // SPEC 8.2.3 (F's C2 ruling 282, the owner's O-296 (a)): a premise error is reported where the premises causing it first
            // meet. A scope's own premises meet its enclosing scopes' there, so their premises are judged in this view too: a
            // conditional conformance's conditions against its Type's premises (l2). The enclosing premise stays valid in its own scope.
            for (var enclosing = scope.Parent; !judged.Failed && enclosing is not null; enclosing = enclosing.Parent)
            {
                if (enclosing.Constraints is { } inherited)
                {
                    judged = this.JudgeFacts(scope, environment, inherited, mode, ref changed);
                }
            }

            if (contradiction is { } pair)
            {
                if (judged is { Failed: true, Rests: false })
                {
                    // The contradiction holds the owner's slot: the proof's own failure is a problem of its own beside it, as the premise
                    // error's (C-363's C1; DIAGNOSTICS 4.3).
                    this.Record(scope.Owner, BindingFailure.InvalidConstraint, []);
                }

                changed |= this.FailPairPremise(pair.Row, scope) | this.FailPairPremise(pair.Other, scope);
            }

            this.validationStep = null;
        }

        return changed;
    }

    // The judgment of `facts`' premises in `scope`'s view, in its owner's frame; the first Error is the owner's record, and a premise of
    // the scope's own environment fails with it (FailPremise). Whether one failed, and whether its proof read a failed input (SPEC
    // 23.1.2: its record then rests on that input's, which RequireConstraint takes).
    private (bool Failed, bool Rests) JudgeFacts(BindingScope scope, ConstraintEnvironment environment, ConstraintEnvironment facts, BindingMode mode, ref bool changed)
    {
        foreach (var fact in facts.Facts)
        {
            if (facts.IsFailed(fact) || this.DerivedFailure(facts, fact) is not null)
            {
                continue; // The premise's own judgment ran once; its state is Failed (here, or at its origin: ruling 282).
            }

            // The environment's judgment is its owner's, the node its record names: the proof runs in the owner's frame.
            this.failedCause = null;
            var frame = this.BeginConsultation(scope.Owner);
            var proof = this.ProveConstraint(fact, scope, scope.Owner);
            if (proof != ConstraintProof.Error)
            {
                this.DropConsultation(frame);
                continue;
            }

            var previous = scope.Owner.BindingState;
            var rests = this.failedCause is not null;
            changed |= this.FailPremise(environment, fact, scope.Owner); // R7-C3: the premise's own judgment writes its state Failed
            // Other input facts can have independent contradictions. Record a
            // sole missing-name cause only when this is the entire environment.
            this.RequireConstraintInFrame(scope.Owner, proof, mode, frame, facts.Facts.Count == 1 ? this.FindConformanceDiagnosticCause(scope.Owner, fact) : null);
            changed |= previous != scope.Owner.BindingState;
            return (true, rests);
        }

        return (false, false);
    }

    private bool ValidateDeclarationProjectionInputs(BindingMode mode)
    {
        var changed = false;
        foreach (var use in this.projectionUses)
        {
            if (ProjectionDeclarationOwner(use.Use) is { } declaration)
            {
                var frame = this.BeginConsultation(declaration); // The proof's reads of other nodes (RequireConstraintInFrame).
                changed |= this.RecordDeclarationInput(use, declaration, this.CheckProjectionInputs(use.Type, use.Contract, this.ConstraintScope(use.Use), declaration), mode, frame);
            }
        }

        return changed;
    }

    // The fixpoint's input writer (SettlePremiseFailures, Final): only a premise clause's projection input that answers Error (a failed
    // fact, which fails the premise) is recorded before the bodies; every other input waits for ValidateDeclarationProjectionInputs,
    // after them.
    private bool FailPremiseInputs()
    {
        var changed = false;
        foreach (var use in this.projectionUses)
        {
            if (ProjectionDeclarationOwner(use.Use) is not { } declaration || this.PremiseClause(use.Use, declaration) is null)
            {
                continue;
            }

            var frame = this.BeginConsultation(declaration);
            var proof = this.CheckProjectionInputs(use.Type, use.Contract, this.ConstraintScope(use.Use), declaration);
            if (proof != ConstraintProof.Error)
            {
                this.DropConsultation(frame);
                continue;
            }

            changed |= this.RecordDeclarationInput(use, declaration, proof, BindingMode.Final, frame);
        }

        return changed;
    }

    // A declaration's projection input `proof` in its frame: the declaration's requirement, what it gives the declaration's environment at
    // Final (FailInput) and, for a conditional conformance, the block's member applicability. True when a state or a premise changed.
    private bool RecordDeclarationInput((Koto Use, BoundType Type, BindingSymbol Contract) use, Koto declaration, ConstraintProof proof, BindingMode mode, (int Start, Koto? Node) frame)
    {
        var state = declaration.BindingState;
        this.RequireConstraintInFrame(declaration, proof, mode, frame);
        var changed = mode == BindingMode.Final && this.FailInput(use, declaration, proof);
        if (declaration.Parent is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } conditional)
        {
            // The target owns conformance/Copy evidence; the block owns member applicability.
            this.RequireConstraint(conditional, proof, mode);
        }

        return changed | (state != declaration.BindingState);
    }

    // R7-C3 (F's C2 rulings 252, 255; SPEC 8.2.3, 23.1.2): what a declaration's projection input that is not proven gives its environment
    // at Final. An Error (a failed fact) fails the premise clause it lies in. An Unknown is the declaration's own requirement: it fails no
    // premise and is named (NameUnprovenInput), so a judgment whose proof reads that input rests on the declaration's record and every
    // other judgment is in the remainder. A Refuted input gives nothing beyond its record. True when a premise newly failed.
    private bool FailInput((Koto Use, BoundType Type, BindingSymbol Contract) use, Koto declaration, ConstraintProof proof)
    {
        if (proof is ConstraintProof.Proven or ConstraintProof.Refuted || !this.scopes.TryGetValue(declaration, out var premises) ||
            premises.Constraints is not { } environment)
        {
            return false;
        }

        if (proof == ConstraintProof.Unknown)
        {
            environment.NameUnprovenInput(use.Type, use.Contract, declaration);
            this.premiseFailures++; // The Failed state's census (ConstraintEnvironment's contract).
            return false;
        }

        return this.PremiseClause(use.Use, declaration) is { BoundConstraint: { } premise } && this.FailPremise(environment, premise, declaration);
    }

    // The premise clause of `declaration` a node lies in, if any (a projection use in a premise's side).
    private IsKoto? PremiseClause(Koto node, Koto declaration)
    {
        for (var current = node; current.Parent is { } parent; current = parent)
        {
            if (current is IsKoto clause && ReferenceEquals(parent, declaration))
            {
                return clause;
            }
        }

        return null;
    }

    private void ValidateExpressionProjectionInputs(BindingMode mode)
    {
        foreach (var use in this.projectionUses)
        {
            ConstraintProof? proof = null;
            for (var node = use.Use; node.Parent is { } parent && parent is not DeclarationKoto; node = parent)
            {
                // Explicit Type syntax can normalize away the constrained qualifier.
                if (parent is InvocationKoto call && ReferenceEquals(node, call.Method))
                {
                    proof ??= this.CheckProjectionInputs(use.Type, use.Contract, this.ConstraintScope(use.Use), use.Use);
                    this.RequireConstraint(call, proof.Value, mode);
                }
                else if (parent is IsKoto { IsRuntimeTest: true } test && ReferenceEquals(node, test.Right))
                {
                    proof ??= this.CheckProjectionInputs(use.Type, use.Contract, this.ConstraintScope(use.Use), use.Use);
                    this.RequireConstraint(test, proof.Value, mode);
                    if (proof != ConstraintProof.Proven)
                    {
                        test.BoundRuntimeTest = null;
                    }
                }
            }
        }
    }

    private ConstraintProof CheckProjectionInputs(BoundType type, BindingSymbol contract, BindingScope scope, QuerySite site)
        => CombineProof(this.CheckTypeConstraints(type, scope, site), this.ProveConformance(type, contract, scope, site), true);

    private bool ValidateClosedTypeConstraints(BindingMode mode)
    {
        var changed = false;
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is IsKoto { BoundConstraint.HasUnresolved: true } pending && PendingDeclaration(pending) is { } owner)
            {
                // In the declaration's scope: a conditional target is bound in its Type's, never under the conformance's premises.
                var previous = owner.BindingState;
                var pendingFrame = this.BeginConsultation(pending);
                var pendingProof = this.ProveConstraint(pending.BoundConstraint!, this.ConstraintScope(owner), pending);
                if (pendingProof == ConstraintProof.Proven)
                {
                    pendingProof = ConstraintProof.Unknown;
                }

                // The clause, then a conditional conformance between it and its Type, and the declaration: their outcomes are the clause's.
                this.RequireConstraintInFrame(pending, pendingProof, mode, pendingFrame);
                for (Koto? node = pending.Parent; node is not null; node = ReferenceEquals(node, owner) ? null : node.Parent)
                {
                    var nodeFrame = this.BeginConsultation(node);
                    this.RequireConstraintInFrame(node, pendingProof, mode, nodeFrame, pending);
                }

                changed |= previous != owner.BindingState;
            }

            if (this.nodes[i] is IsKoto { BindingState: BindingState.Invalid, BoundConstraint: { } failedConstraint, Parent: DeclarationContainerKoto implementation } failed && DeclaresConformance(failed) && this.InvalidConstraintRequirement(failedConstraint))
            {
                // An invalid required declaration invalidates the implementing Type.
                // A path-local witness failure must preserve independent conformances.
                // Transitional, R7-E5 (D-924): an owner over its failed part spelled as a failure (ruling 16 B1 (a)): the Type rests on
                // its invalid required declaration `failed` and takes that declaration's code until R7-E5's per-problem inputs.
                var previous = implementation.BindingState;
                this.Fail(implementation, failed.BindingFailure);
                changed |= previous != implementation.BindingState;
            }

            if (this.nodes[i] is ContractKoto { BoundSymbol.Contract: { } shape } contract)
            {
                if (shape.HasUnresolvedParents)
                {
                    var previous = contract.BindingState;
                    this.RequireUnproven(contract, mode);
                    changed |= previous != contract.BindingState;
                }

                for (var a = 0; a < shape.Ancestors.Count; a++)
                {
                    if (this.InvalidDeclarationContext(shape.Ancestors[a].Declaration))
                    {
                        var previous = contract.BindingState;
                        this.Fail(contract, BindingFailure.UnsatisfiedConstraint); // Over an ancestor's invalid context, which it reads: the slot's drop until R7-E5 (D-924).
                        changed |= previous != contract.BindingState;
                        break;
                    }

                    if (UnresolvedTypeDeclarationContext(shape.Ancestors[a].Declaration))
                    {
                        var previous = contract.BindingState;
                        this.RequireUnproven(contract, mode);
                        changed |= previous != contract.BindingState;
                    }
                }
            }

            if (this.nodes[i] is not IsKoto { Parent: DeclarationContainerKoto container, IsAssociatedConstraint: false, BoundConstraint: { } constraint, Left.BoundType: { } subject } clause ||
                container is not (StructKoto or EnumKoto or ContractKoto) || IsSelfConstraint(clause) ||
                this.DependentConstraint(constraint, contractSelf: container is ContractKoto))
            {
                continue;
            }

            // Closed clauses are declaration obligations, never assumptions or new conformances.
            var scope = this.scopes[container];
            var frame = this.BeginConsultation(clause);
            var proof = CombineProof(this.CheckTypeConstraints(subject, scope, clause), this.ProveConstraint(constraint, scope, clause), true);
            var state = container.BindingState;
            this.RequireConstraintInFrame(clause, proof, mode, frame);
            var containerFrame = this.BeginConsultation(container); // The owner's outcome is its clause's.
            this.RequireConstraintInFrame(container, proof, mode, containerFrame, clause);
            changed |= state != container.BindingState;
        }

        return changed;
    }

    private void ValidateConstraintUses(BindingMode mode)
    {
        for (var i = 0; i < this.nodes.Count; i++)
        {
            var node = this.nodes[i];
            if (node is IsKoto { BoundConstraint: { } constraint } clause && clause.Parent is not ContractKoto && clause.Left.BoundType?.Kind != BoundTypeKind.AssociatedProjection && !(this.copyDeclarations.TryGetValue(clause, out var copy) && copy.Active) && clause.Left.BoundSymbol is not { IsGenericInput: true })
            {
                var frame = this.BeginConsultation(clause);
                this.RequireConstraintInFrame(clause, this.ProveConstraint(constraint, this.ConstraintScope(clause), clause), mode, frame);
            }

            // R7-V1c: one judgment per written Type occurrence (JudgedOccurrence), recorded at the Type node it names. A check that
            // recorded at a node first keeps its record.
            if (this.JudgedOccurrence(node) is { Symbol.Declaration: DeclarationContainerKoto container } type)
            {
                var written = this.OccurrenceTypeNode(node);
                var scope = this.ConstraintScope(node);
                var use = this.VerdictUse(written); // A synthesized call's Type is its consumer's verdict (PLAN G214).
                var frame = this.BeginConsultation(use);
                var result = this.CheckConstraints(container.ConstraintNodes, new CallSubstitution(container, (BoundType[])type.Components, origins: (BoundOrigin[])type.OriginArguments), scope, use);
                this.RequireConstraintInFrame(use, result, mode, frame);
                if (result != ConstraintProof.Proven)
                {
                    this.RestLayersOnType(node, written);
                }

                if (node.BindingState == BindingState.Invalid)
                {
                    // Invalid type formation in a clause must not remain available as evidence.
                    for (var parent = node.Parent; parent is not null; parent = parent.Parent)
                    {
                        if (parent is IsKoto { BoundConstraint: { } fact } && scope.Constraints is { } environment)
                        {
                            // R7-C3: the clause's own judgment fails its premise; a conformance binding's clause is no premise (not in Facts:
                            // FailPremise does nothing), its failure being its path's state (design-c31b §3 (c)).
                            this.FailPremise(environment, fact, scope.Owner);
                            break;
                        }

                        if (parent is DeclarationKoto)
                        {
                            break;
                        }
                    }
                }
            }
        }

        // Compact in place: discharging N obligations never performs N list searches/removals.
        var remaining = 0;
        for (var i = 0; i < this.obligations.Count; i++)
        {
            var obligation = this.obligations[i];
            if (obligation.Deadline == BindingDeadline.Definition && this.TypeRoleCauses(obligation) == TypeRoleCause.None)
            {
                this.obligationSet.Remove(obligation);
                continue;
            }

            this.obligations[remaining++] = obligation;
        }

        this.obligations.RemoveRange(remaining, this.obligations.Count - remaining);
    }

    // R7-V1c (SPEC 8.3.1, 8.6.3, 23.1.2; F's V1c rulings 3-4): the Type ValidateConstraintUses judges at `node`, or null. A layer that
    // forms its operand's Type (SameTypeOperand) is that operand's occurrence, so the walk judges a written occurrence once: at its
    // outermost such layer with a constructed Type that has clauses. A layer forming another Type, or none (a wrapper whose
    // annotation failed), is not the occurrence below it, which is judged at its own node.
    private BoundType? JudgedOccurrence(Koto node)
        => this.ConstrainedType(node) is { } type && !(node.Parent is { } layer && ReferenceEquals(this.SameTypeOperand(layer), node) && this.ConstrainedType(layer) is not null) ? type : null;

    // R7-V1c (F's V1c ruling 4): the operand of a layer of Type syntax whose formed Type is its one Type component's, by the identity
    // judge (SameCompleteType: a transparent wrapper, parentheses, an explicit `owner/`, any such layer alike), else null; a layer
    // forming another Type (`ref/`, `uniq/`, a pair layer, `T?`) has none.
    private Koto? SameTypeOperand(Koto node)
        => node is TypeKoto { TypeComponentCount: 1, BoundType: { } formed } layer && layer.TypeComponent(0) is { BoundType: { } type } operand && this.SameCompleteType(formed, type) ? operand : null;

    // The Type node of the occurrence judged at `node`: the operand below every layer that forms its operand's Type.
    private Koto OccurrenceTypeNode(Koto node)
    {
        while (this.SameTypeOperand(node) is { } operand)
        {
            node = operand;
        }

        return node;
    }

    private BoundType? ConstrainedType(Koto node)
        => node.BoundType is { Kind: BoundTypeKind.Constructed or BoundTypeKind.Dictionary, Symbol.Declaration: DeclarationContainerKoto { ConstraintNodes.Count: not 0 } } type &&
            !this.inferredConstructionTargets.Contains(node) && (type.Kind != BoundTypeKind.Dictionary || type.Components.Count != 0) ? type : null;

    // SPEC 23.1.2 (rule W): the layers of a written Type occurrence from its judged node down to its Type node `written` are that Type;
    // when the Type's judgment leaves it failed or unproven, a layer with no failure of its own rests on it (RestOnPart) or takes its
    // unproven state, so a reader of the declaration's written Type reads a failed Type.
    private void RestLayersOnType(Koto judged, Koto written)
    {
        var state = this.RecordedState(written);
        for (var layer = written; state != BindingState.Resolved && !ReferenceEquals(layer, judged);)
        {
            layer = layer.Parent!;
            if (this.RecordedState(layer) == BindingState.Invalid)
            {
                continue;
            }

            if (state == BindingState.Invalid)
            {
                this.RestOnPart(layer, written);
            }
            else
            {
                this.SetRecordedState(layer, state);
            }
        }
    }

    // SPEC 23.1.2: an unproven Definition obligation publishes its Language cause, a missing slot before a static exclusive one, and
    // keeps an unproven target role of the same occurrence as a separate record; every final check recomputes the causes. The role is
    // a proof in the use's declaration context, so in a failed one (ReadFailedDeclaration) it rests on that declaration's record and
    // keeps none of its own; a Language cause stays the occurrence's own.
    private void FailTypeRole(BindingObligation obligation)
    {
        var causes = this.TypeRoleCauses(obligation);
        if ((causes & TypeRoleCause.Role) != 0 && this.InvalidDeclarationContextCause(obligation.Use) is { } failed)
        {
            causes &= ~TypeRoleCause.Role;
            if (causes == TypeRoleCause.None)
            {
                this.CompleteDependent(obligation.Use, failed);
                return;
            }
        }

        var failure = (causes & TypeRoleCause.MissingSlot) != 0 ? BindingFailure.MissingOrigin
            : (causes & TypeRoleCause.StaticExclusive) != 0 ? BindingFailure.InvalidOrigin
            : BindingFailure.UnprovenConstraint;
        this.Fail(obligation.Use, failure);
        if (failure != BindingFailure.UnprovenConstraint)
        {
            this.formationCauses[obligation.Use] = causes;
            if ((causes & TypeRoleCause.Role) != 0)
            {
                this.Record(obligation.Use, BindingFailure.UnprovenConstraint, []);
            }
        }
    }

    // SPEC 23.1.2: a call or fit that reads a Field whose declared pair occurrence failed with a Language cause rests on that failure:
    // the Field's Type, completed only at the Definition deadline, is what the read could not use. Returns that occurrence.
    private Koto? UnformedFieldRead(Koto node)
    {
        var causes = this.formationCauses.Values.In(this.pass);
        if (node is InvocationKoto call)
        {
            for (var i = 0; i < call.ArgumentNodes.Count; i++)
            {
                if (FieldOccurrence(call.ArgumentNodes[i], causes) is { } argument)
                {
                    return argument;
                }
            }

            return null;
        }

        return FieldOccurrence(node, causes);

        // The failed occurrence whose nearest enclosing declaration is the Field or property that the member read selects.
        static Koto? FieldOccurrence(Koto read, FamilyRows<TypeRoleCause> causes)
        {
            if (KotoHelper.UnwrapParentheses(read) is not MemberAccessKoto { BoundSymbol.Declaration: { } member })
            {
                return null;
            }

            while (causes.MoveNext())
            {
                var cause = causes.Node;
                for (var at = cause.Parent; at is not null; at = at.Parent)
                {
                    if (ReferenceEquals(at, member))
                    {
                        return cause;
                    }

                    if (at is DeclarationKoto)
                    {
                        break;
                    }
                }
            }

            return null;
        }
    }

    // SPEC 8.1.2: the evidence of a pair occurrence failed with a Language cause.
    private bool ReportFormationCause(Koto node, DiagnosticCollection? store, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        if (!this.formationCauses.TryGetValue(node, out var causes) || node is not TypeSemanticsKoto { BoundSymbol.Pair: { } pair } application ||
            pair.WholeType is not { } whole)
        {
            return false;
        }

        var admitted = this.AdmittedSemantics(whole, this.ConstraintScope(node));
        var text = new StringBuilder();
        AppendSemantics(text, admitted & ((causes & TypeRoleCause.MissingSlot) != 0 ? SemanticsMask.Borrow : SemanticsMask.Uniq | SemanticsMask.ObjUniq), " or ");
        var name = application.SemanticsParameter!;
        if ((causes & TypeRoleCause.MissingSlot) != 0)
        {
            node.ReportIn(store, requirement, code, evidence: [$"{name} admits {text}, and a borrow needs an outer-Origin slot"], note: "A pair layer stores an outer Origin in its binder's borrow cases, which this occurrence does not name (SPEC 8.1.2)");
        }
        else
        {
            node.ReportIn(store, requirement, code, evidence: [$"{name} admits {text}, which a static Origin never admits"], note: "A static outer-Origin slot admits no exclusive borrow (SPEC 8.1.2)");
        }

        return true;
    }

    // The constraint family's one recorder (SPEC 8.3, 23.1.2; R7-E3 E3b delta 2.3): a proof's outcome at a use, through the one mapping
    // (ConstraintFailureOf), with the facts its caller built, recorded at a use without a failure; a use that already failed keeps its
    // first failure and drops them (the slot's drop). Its callers: RequireConstraint's Refuted arm, FailUnprovenConstraint's fact-less
    // branch, FailConstraint's record and a requirement's named cause (Constraints.Requirements), Enums', Specializations' and
    // FailIterationSubject's outcomes, FunctionReferences' fact-less pending proof and its Error without a held cause, a Pattern Type's
    // check and recheck (Patterns) and a construction's late Type formation (Enums; R7-E3 E3c-2). FailClauseRecord records always (E2-5b);
    // FailUnprovenConstraint's Callable branch and FailCallableSelection keep `callableConstraints` until R7-E4.
    [Transitional("R7", Reason = "Records only at a use without a failure, so the use keeps its first failure (E2-5a's drop: a Refuted constraint may rest on the use's own rejected registration, probe c2; FailIterationSubject's iterable that already failed, RangeDiagnosticTest's `missing..3`), until R7-E5's per-problem inputs (D-924) decide a dependent record.")]
    private BoundType? RecordConstraint(Koto use, ConstraintProof proof, ReadOnlySpan<ProblemFact> facts, bool unresolved = false)
        => this.RecordedFailure(use) == BindingFailure.None
            ? this.Record(use, ConstraintFailureOf(proof), facts, unresolved: unresolved)
            : this.Fail(use, ConstraintFailureOf(proof), unresolved);

    // The one table of a proof at a use: Error is FailConstraint's record (its guard and cause, R7-E5), unless it rests on a limit's
    // record (ProofRestsOnLimit: the use is that record's dependent, SPEC 23.1.2; R7-C6 a, F's C8 ruling 9 C1), Refuted and the final
    // Unknown the constraint family's (RecordConstraint), the provisional Unknown RequireUnproven's. `proposition`: the caller's
    // proposition, kept as the record's Constraint fact (the erasure's `S is Owned`, SPEC 15.2.3); none for a use whose proposition is
    // its own clauses.
    private void RequireConstraint(Koto use, ConstraintProof proof, BindingMode mode, Koto? diagnosticCause = null, BoundConstraint? proposition = null)
    {
        if (proof == ConstraintProof.Error && diagnosticCause is null && this.ProofRestsOnLimit(use, proof))
        {
            return;
        }

        if (proof == ConstraintProof.Error)
        {
            var cause = diagnosticCause ?? this.TakeFailedCause().Use;
            if (cause is not null && this.RecordedFailure(use) == BindingFailure.None)
            {
                this.Consulted(cause); // SPEC 23.1.2: the proof's evidence explains its outcome, never a record the use's own check owns.
            }

            this.FailConstraint(use, cause);
        }
        else if (proof == ConstraintProof.Refuted)
        {
            this.RecordConstraint(use, proof, proposition is null ? [] : [this.PropositionFact(use, proposition)]);
        }
        else if (proof == ConstraintProof.Unknown && mode == BindingMode.Final)
        {
            this.FailUnprovenConstraint(use, this.TakeUnknownFact(), unresolved: false, proposition);
        }
        else if (proof == ConstraintProof.Unknown)
        {
            this.RequireUnproven(use, mode); // The provisional pass leaves the U fact to the next judgment.
        }
    }

    // A caller's proposition as its record's Constraint fact, sited at the use.
    private ProblemFact PropositionFact(Koto use, BoundConstraint proposition)
        => new(ProblemFactKind.Constraint, this.FactSite(use), this.report.Keep(proposition));

    // SPEC 23.1.2: the outcome of a validation pass's proof at a use, inside the use's frame (opened before the proof). What the proof
    // read of other nodes becomes the prerequisite of its outcome when the outcome is the use's record; a record the use's own check
    // owns (the conformance's verdict, or a failure it held before the outcome) never derives from the frame, which is dropped.
    private void RequireConstraintInFrame(Koto use, ConstraintProof proof, BindingMode mode, (int Start, Koto? Node) frame, Koto? diagnosticCause = null)
    {
        var owned = this.RecordedFailure(use) != BindingFailure.None;
        this.RequireConstraint(use, proof, mode, diagnosticCause);
        if (owned)
        {
            this.DropConsultation(frame);
            return;
        }

        this.EndConsultation(use, frame);
    }

    // An Unknown Constraint that no judgment of its own proved (an unresolved parent; r2-6, BU's P1) takes no proof's fact: the final
    // pass records the Constraint alone; the provisional pass, which can gain bindings/conformance evidence across the Mod boundary,
    // only marks the use unresolved.
    private void RequireUnproven(Koto use, BindingMode mode)
    {
        if (mode == BindingMode.Final)
        {
            this.FailUnprovenConstraint(use, null, unresolved: false);
        }
        else if (use.BindingState != BindingState.Invalid)
        {
            use.BindingState = BindingState.Unresolved;
        }
    }

    // The one record of a Constraint proven Unknown (census #25): with the U fact its judgment left (TakeUnknownFact), the clause and its
    // comparison's failing condition (design-r2 Amendments 4 and 5); without one, the constraint family's record (RecordConstraint) with
    // the caller's proposition if any; a foreign fact is a broken contract. RequireConstraint, RequireUnproven and the direct sites that
    // settle a clause list themselves (Enums, Specializations) record through it.
    private BoundType? FailUnprovenConstraint(Koto use, CallableUnknown? fact, bool unresolved, BoundConstraint? proposition = null)
    {
        if (!this.OwnFrame(use, fact?.Epoch))
        {
            return null;
        }

        if (fact is not { Clause: { } clause } || !fact.Failure.IsCondition)
        {
            return this.RecordConstraint(use, ConstraintProof.Unknown, proposition is null ? [] : [this.PropositionFact(use, proposition)], unresolved);
        }

        var at = this.CallableArgument(use, clause) ?? use; // Located as the trial locates it (CallableOriginFailure).
        var record = new CallableConstraintFact(at, clause, fact.Subject, this.ContractFact(at, fact.Failure, use));
        return this.FailExplained(CallableConstraintRows.Family, use, BindingFailure.UnprovenConstraint, record, unresolved);
    }

    // A proof of a proposition from `site` (QuerySite; PLAN G651 a1), settled where it is no longer combined (EndProof).
    private ConstraintProof ProveConstraint(BoundConstraint proposition, BindingScope scope, QuerySite site)
    {
        this.BeginProof(site);
        return this.EndProof(this.ProveProposition(proposition, scope));
    }

    // SPEC 23.1.1: each conformance limit a failing check rested on, reported once at its location: an inherited conformance limited by
    // OCC-X at the derived Type's base clause (an explicit one was reported at its declaration, ValidateConformances). Runs in
    // Binding.Check's Final prologue, before the summaries' sites (FailNeededSites) and the derived uses resting on either (BU-76).
    private void PublishNeededLimits()
    {
        for (var i = 0; i < this.impls.InPass.Paths.Count; i++)
        {
            if (this.impls.InPass.Paths[i] is { Needed: true } path)
            {
                this.Fail(path.Use, BindingFailure.Unsupported);
            }
        }
    }

    // SPEC 8.1.1, 8.1.2, 8.7.3: the independent causes for which a Type occurrence's role or pair formation is not proven, None when
    // it is. A missing outer-Origin slot under an admitted borrow and a static slot under an admitted exclusive borrow are Language
    // causes; a target that is not proven a value Type or an Object Target is a Proof cause, judged separately.
    private TypeRoleCause TypeRoleCauses(BindingObligation obligation)
    {
        if (obligation.Type is not { } type)
        {
            return TypeRoleCause.Role;
        }

        var scope = this.ConstraintScope(obligation.Use);
        var objectTarget = obligation.Use is TypeSemanticsKoto { Type: not null, SemanticsKind: SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc or SemanticsKind.ObjRef or SemanticsKind.ObjUniq };
        if (obligation.Kind == BindingObligationKind.TypeRole)
        {
            return this.HasValueRole(type, scope, obligation.Use, objectTarget) ? TypeRoleCause.None : TypeRoleCause.Role;
        }

        if (obligation.Kind != BindingObligationKind.TypeFormation ||
            obligation.Use is not TypeSemanticsKoto { SemanticsParameter: not null, BoundType: { } applied } application || application.BoundSymbol?.Pair?.WholeType is not { } whole)
        {
            return TypeRoleCause.Role;
        }

        // SPEC 8.1.1, 8.1.2: every admitted Semantics of `s` must form a valid Type with `U`; object-family bindings additionally need `U`
        // to be an Object Target.
        // An empty set is the 8.3.4 Error of the environment that emptied it, its one record (ValidateConstraintEnvironments): no case
        // forms here, so the occurrence has no cause of its own.
        var admitted = this.AdmittedSemantics(whole, scope);
        const SemanticsMask objectFamily = SemanticsMask.Object | SemanticsMask.ObjectBorrow;
        if (admitted == SemanticsMask.None)
        {
            return TypeRoleCause.None;
        }

        var causes = TypeRoleCause.None;
        if (application.OriginExpression is not null || application.OriginName is not null || (applied.Kind == BoundTypeKind.SemanticsApplication && applied.Origin is not null))
        {
            // SPEC 8.1.2: an outer-Origin slot, written or recorded for a direct input, is active only for the admitted borrow
            // bindings and contributes nothing for the others. A static Origin admits no exclusive borrow.
            if (applied.Origin?.Kind == OriginKind.Static && (admitted & (SemanticsMask.Uniq | SemanticsMask.ObjUniq)) != 0)
            {
                causes |= TypeRoleCause.StaticExclusive;
            }

            if (!(applied.Kind == BoundTypeKind.Parameter || (admitted & ~objectFamily) == 0 || this.HasValueRole(type, scope, obligation.Use, false)) ||
                ((admitted & objectFamily) != 0 && !this.HasValueRole(type, scope, obligation.Use, true)))
            {
                causes |= TypeRoleCause.Role;
            }

            return causes;
        }

        // Without an Origin slot, no borrow binding can be admitted; owning objects need no Origin.
        if ((admitted & SemanticsMask.Borrow) != 0)
        {
            causes |= TypeRoleCause.MissingSlot;
        }

        if (((admitted & SemanticsMask.Object) != 0 && !this.HasValueRole(type, scope, obligation.Use, true)) ||
            ((admitted & (SemanticsMask.Owner | SemanticsMask.Raw)) != 0 && !this.HasValueRole(type, scope, obligation.Use, false)))
        {
            causes |= TypeRoleCause.Role;
        }

        return causes;
    }

    /// <summary>Decides the value-Type role of a generic target, or its Object Target role (SPEC 8.7.3).</summary>
    private bool HasValueRole(BoundType type, BindingScope scope, QuerySite site, bool objectTarget)
    {
        if (objectTarget)
        {
            // A determined Core is judged directly and shallowly, even with unbound Type arguments.
            if (type.Kind is BoundTypeKind.Nominal or BoundTypeKind.Constructed && type.Symbol?.Declaration is StructKoto or EnumKoto)
            {
                return this.ObjectPayloadOptOut(type.Symbol) is null;
            }

            if (this.RequestCapability(type, this.Library.ObjectPayload, scope, site) == ConstraintProof.Proven)
            {
                return true;
            }

            // Pair evidence: the caller's valid `s/T` already establishes the target when `s` is object-family.
            if (type.Kind == BoundTypeKind.TargetProjection && type.Symbol?.WholeType is { } pair && this.HasSemanticsRole(pair, SemanticsMask.Object | SemanticsMask.ObjectBorrow, scope))
            {
                return true;
            }
        }
        else if (type.Kind is not (BoundTypeKind.TargetProjection or BoundTypeKind.AssociatedProjection))
        {
            return true;
        }
        else if (this.RequestCapability(type, this.Library.ObjectPayload, scope, site) == ConstraintProof.Proven)
        {
            // SPEC 8.7.3: ObjectPayload proves a complete owner value Type, including a pair's target.
            return true;
        }
        else if (type.Kind == BoundTypeKind.TargetProjection && type.Symbol?.WholeType is { } whole && this.HasSemanticsRole(whole, SemanticsMask.Owner | SemanticsMask.ValueBorrow | SemanticsMask.Raw, scope))
        {
            return true;
        }

        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (fact.Kind == ConstraintKind.TypeIdentity && this.FactStates(fact, type, out var stated) && stated is { } required && required.Kind is not (BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.AssociatedProjection or BoundTypeKind.SemanticsApplication) && (!objectTarget || (required.Kind is BoundTypeKind.Nominal or BoundTypeKind.Constructed && required.Symbol?.Declaration is StructKoto or EnumKoto && this.ObjectPayloadOptOut(required.Symbol) is null)) && this.ProveConstraint(fact, scope, site) == ConstraintProof.Proven)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Tests whether the admitted set of a Semantics binding is contained in <paramref name="allowed"/> (SPEC 8.3).</summary>
    private bool HasSemanticsRole(BoundType whole, SemanticsMask allowed, BindingScope scope)
    {
        var admitted = this.AdmittedSemantics(whole, scope);
        return admitted != SemanticsMask.None && (admitted & ~allowed) == 0;
    }

    /// <summary>The `formationCauses` family's one accessor (DECISIONS BU-220): a kept family, the Language causes a pair occurrence's
    /// Definition obligation failed with (FailTypeRole), kept in the occurrence's module's NodeColumns at its SyntaxId (None for none)
    /// and emptied at each Final check's start over the running pass's modules (ProduceFailures; a re-check, CheckBound, recomputes the
    /// causes in the same pass, so a pass stamp cannot stand for the step). Its members keep the names its readers use; Values is d8's
    /// order (ascending id), which UnformedFieldRead's walk reads.</summary>
    private readonly struct FormationCauseRows(Binding binding)
    {
        internal FamilyRows<TypeRoleCause> Values => new(binding, static (table, _) => table.Hir.Families.FormationCauses.Enumerate());

        internal TypeRoleCause this[Koto use]
        {
            get => this.TryGetValue(use, out var causes) ? causes : throw new KeyNotFoundException($"`{use}` has no formation cause in this pass.");
            set => HirTables.Of(use).Families.FormationCauses.Ref(use.SyntaxId) = value;
        }

        internal bool ContainsKey(Koto use) => this.TryGetValue(use, out _);

        internal bool TryGetValue(Koto use, out TypeRoleCause causes)
            => (causes = use.SyntaxOwner is { } table ? table.Hir.Families.FormationCauses.Get(use.SyntaxId) : TypeRoleCause.None) != TypeRoleCause.None;

        // The Final check's step: the running pass's modules lose every row (a pass judges its own obligations only).
        internal void Empty()
        {
            for (var i = 0; i < binding.pass.Count; i++)
            {
                binding.pass[i].Syntax.Hir.Families.FormationCauses.Empty();
            }
        }
    }
}
