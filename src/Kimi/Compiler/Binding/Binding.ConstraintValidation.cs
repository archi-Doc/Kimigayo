// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The causes of each pair occurrence that a final check failed with a Language cause, for its evidence and for the separate record
    // of an unproven target role (Check).
    private Dictionary<Koto, TypeRoleCause>? formationCauses;

    // Conformance verifications and agreements in progress; inside them a conformance limited by OCC-X stays Unknown.
    private int conformanceChecks;

    // The conformance uses whose completeness rests on a specification, settled at publication (SettleSpecificationLinks).
    private List<Koto>? specificationLinks;

    // The independent causes of an unproven Type role or pair formation (TypeRoleCauses).
    [Flags]
    private enum TypeRoleCause : byte
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
    /// separate clauses intersect, and no premise admits all nine Semantics.
    /// </summary>
    /// <param name="whole">The pair's whole Type.</param>
    /// <param name="scope">The scope whose premises apply.</param>
    /// <returns>The admitted Semantics.</returns>
    internal SemanticsMask AdmittedSemantics(BoundType whole, BindingScope scope)
    {
        var admitted = SemanticsMask.All;
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { Invalid: false } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (SemanticsPremise(fact, whole) && this.AvailableConstraintFact(environment, fact))
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

    // The argument a selected call binds a Callable clause's slot with (CallableOriginFailure's ArgumentBinding).
    private static Koto? CallableArgument(Koto use, IsKoto clause)
    {
        if (use is not InvocationKoto { BoundCall: { Target.Declaration: FunctionKoto function } plan } ||
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

    private ConstraintProof CheckCallTypeConstraints(CallPlan call, BindingScope scope)
    {
        // A linked constructor is judged as the public declaration it selected (SPEC 22.1).
        var target = this.Library.PresentedTarget(call.Target);
        if (this.InvalidDeclarationContext(target.Declaration))
        {
            return ConstraintProof.Error;
        }

        // SPEC 10.1: the selected candidate's obligation is its trial's conjunction (StepFiveConjunction), as one proof that keeps its
        // facts for the record, then the selected call's own Types.
        this.BeginProof();
        ConstraintProof proof;
        if (target.Declaration is FunctionKoto function)
        {
            // The declared signature stays the trial's part until R7-C2 (b) (PLAN G375): read here it would fault on library declarations.
            var bindings = new CandidateBindings(
                function, target, call.TypeArguments, call.LengthArguments, call.ConformingType, call.DeclaringType, call.DeclaringType, default, false);
            proof = this.StepFiveConjunction(bindings, scope, false, null);
        }
        else
        {
            proof = call.DeclaringType is { } declaringType ? this.CheckTypeConstraints(declaringType, scope) : ConstraintProof.Proven;
            proof = CombineProof(proof, this.ProveMemberConditions(target, call.DeclaringType, scope), true);
        }

        // The slots past the callee's own (a constructor's owner slots) keep the Final check's formation read.
        var first = target.Declaration is FunctionKoto callee ? System.Math.Min(call.TypeArguments.Length, CallOwnSlots(callee).Count) : 0;
        for (var i = first; i < call.TypeArguments.Length; i++)
        {
            if (call.TypeArguments[i] is { } argument)
            {
                proof = CombineProof(proof, this.CheckTypeConstraints(argument, scope), true);
            }
        }

        if (UnresolvedTypeDeclarationContext(target.Declaration))
        {
            proof = CombineProof(proof, ConstraintProof.Unknown, true);
        }

        proof = CombineProof(proof, this.CheckTypeConstraints(call.ReturnType, scope), true);
        if (call.ConformingType is { } conformingType)
        {
            proof = CombineProof(proof, this.CheckTypeConstraints(conformingType, scope), true);
        }

        proof = CombineProof(proof, this.CheckOperationTypeConstraints(call.ReceiverOperation, scope), true);
        foreach (var operation in call.ArgumentOperations)
        {
            proof = CombineProof(proof, this.CheckOperationTypeConstraints(operation, scope), true);
        }

        var fact = this.ProofCause(proof) is null ? this.ProofFact(proof) : null;
        var clause = this.ProofClause(proof);
        proof = this.EndProof(proof);
        this.LeaveUnknown(fact, proof);
        this.LeaveClause(clause, proof);
        return proof;
    }

    // SPEC 10.1 step 5, 8.1.3 (DECISIONS: G572 U2a): a candidate's Constraints and premises over its bindings, one conjunction in
    // one order: the callee's Constraints, its owner Type's formation, each own binding's formation (a length slot bound, a Type slot
    // formed, an unsolved slot only where 10.8 leaves it unbound), the member's conditions. The candidate trial (SlotSolution.Prove) and
    // the selected call's obligation (CheckCallTypeConstraints) judge it over the same value; the declared signature stays the trial's
    // (C2 (b), PLAN G375). `incomplete` and `slotOwner` are the trial's: a waiting argument's open slots (10.5.1, 10.8).
    private ConstraintProof StepFiveConjunction(in CandidateBindings bindings, BindingScope scope, bool incomplete, Koto? slotOwner)
    {
        var function = bindings.Function;
        var open = !bindings.Unbound.IsEmpty;
        var clauseOwner = bindings.InfersOwner ? null : bindings.DeclaringType; // A constructor inferring its owner's slots (10.8.1) has none yet.
        var proof = this.CheckConstraints(
            function.TypeConstraints,
            function,
            bindings.Slots,
            scope,
            bindings.Self,
            clauseOwner,
            bindings.Lengths,
            incomplete: incomplete || open,
            skipUnresolved: open,
            slotOwner: slotOwner);
        if (bindings.Owner is { } owner)
        {
            proof = CombineProof(proof, this.CheckTypeConstraints(owner, scope), true);
        }
        else if (bindings.InfersOwner && bindings.DeclaringType is not null && slotOwner is StructKoto openOwner)
        {
            // A constructor whose owner Type is not closed yet judges its owner's clauses over the slots inferred so far (10.8.1).
            proof = CombineProof(
                proof,
                this.CheckConstraints(openOwner.ConstraintNodes, openOwner, bindings.Slots, scope, incomplete: true, skipUnresolved: !bindings.Unbound.IsEmpty),
                true);
        }

        var own = CallOwnSlots(function);
        var lengths = bindings.Lengths;
        for (var i = 0; i < own.Count && i < bindings.Slots.Length; i++)
        {
            var slot = own[i] is LengthParameterKoto ? (i < lengths.Length && lengths[i] is not null ? ConstraintProof.Proven : ConstraintProof.Unknown)
                : bindings.Slots[i] is { } argument ? this.CheckTypeConstraints(argument, scope)
                : bindings.Unbound.Contains(i) ? ConstraintProof.Proven : ConstraintProof.Unknown;
            proof = CombineProof(proof, slot, true);
        }

        return CombineProof(proof, this.ProveMemberConditions(bindings.Target, bindings.DeclaringType, scope), true);
    }

    private ConstraintProof CheckOperationTypeConstraints(in BoundArgumentOperation operation, BindingScope scope)
    {
        var proof = operation.SourceType is { } source ? this.CheckTypeConstraints(source, scope) : ConstraintProof.Proven;
        if (operation.ParameterType is { } parameter && !ReferenceEquals(parameter, operation.SourceType))
        {
            proof = CombineProof(proof, this.CheckTypeConstraints(parameter, scope), true);
        }

        return proof;
    }

    // Late witness/Effect failures invalidate declarations even when normalization already erased
    // their projections. Reuse the ordinary validation until declaration states stop changing.
    private void InvalidateProjectionCertificates(BindingMode mode)
    {
        while (this.ValidateClosedTypeConstraints(mode) | this.ValidateDeclarationProjectionInputs(mode) | this.ValidateConstraintEnvironments(sourcesOnly: true))
        {
            this.ClearCapabilityResults();
            this.ValidateBaseDeclarations(mode);
            this.ValidateProperties(mode);
            this.ValidateConformances(mode, true);
        }

        this.ClearCapabilityResults();
    }

    private bool ValidateDeclarationProjectionInputs(BindingMode mode)
    {
        var changed = false;
        foreach (var use in this.projectionUses)
        {
            var declaration = ProjectionDeclarationOwner(use.Use);
            if (declaration is null)
            {
                continue;
            }

            var state = declaration.BindingState;
            var proof = this.CheckProjectionInputs(use.Type, use.Contract, this.ConstraintScope(use.Use));
            this.RequireConstraint(declaration, proof, mode);
            if (declaration.Parent is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } conditional)
            {
                // The target owns conformance/Copy evidence; the block owns member applicability.
                this.RequireConstraint(conditional, proof, mode);
            }

            changed |= state != declaration.BindingState;
        }

        return changed;
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
                    proof ??= this.CheckProjectionInputs(use.Type, use.Contract, this.ConstraintScope(use.Use));
                    this.RequireConstraint(call, proof.Value, mode);
                }
                else if (parent is IsKoto { IsRuntimeTest: true } test && ReferenceEquals(node, test.Right))
                {
                    proof ??= this.CheckProjectionInputs(use.Type, use.Contract, this.ConstraintScope(use.Use));
                    this.RequireConstraint(test, proof.Value, mode);
                    if (proof != ConstraintProof.Proven)
                    {
                        test.BoundRuntimeTest = null;
                    }
                }
            }
        }
    }

    private ConstraintProof CheckProjectionInputs(BoundType type, BindingSymbol contract, BindingScope scope)
        => CombineProof(this.CheckTypeConstraints(type, scope), this.ProveConformance(type, contract, scope), true);

    private bool ValidateClosedTypeConstraints(BindingMode mode)
    {
        var changed = false;
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is IsKoto { BoundConstraint.HasUnresolved: true, Parent: DeclarationKoto owner } pending)
            {
                var previous = owner.BindingState;
                var pendingProof = this.ProveConstraint(pending.BoundConstraint!, this.ConstraintScope(pending));
                if (pendingProof == ConstraintProof.Proven)
                {
                    pendingProof = ConstraintProof.Unknown;
                }

                this.RequireConstraint(pending, pendingProof, mode);
                this.RequireConstraint(owner, pendingProof, mode);
                changed |= previous != owner.BindingState;
            }

            if (this.nodes[i] is IsKoto { BindingState: BindingState.Invalid, BoundConstraint: { } failedConstraint, Parent: DeclarationContainerKoto implementation } failed && implementation is StructKoto or EnumKoto && IsSelfConstraint(failed) && this.InvalidConstraintRequirement(failedConstraint))
            {
                // An invalid required declaration invalidates the implementing Type.
                // A path-local witness failure must preserve independent conformances.
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
                        this.Fail(contract, BindingFailure.UnsatisfiedConstraint);
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
            var proof = CombineProof(this.CheckTypeConstraints(subject, scope), this.ProveConstraint(constraint, scope), true);
            var state = container.BindingState;
            this.RequireConstraint(clause, proof, mode);
            this.RequireConstraint(container, proof, mode);
            changed |= state != container.BindingState;
        }

        return changed;
    }

    private void ValidateConstraintUses(BindingMode mode)
    {
        for (var i = 0; i < this.nodes.Count; i++)
        {
            var node = this.nodes[i];
            if (node is IsKoto { BoundConstraint: { } constraint } clause && clause.Parent is not ContractKoto && clause.Left.BoundType?.Kind != BoundTypeKind.AssociatedProjection && !(this.copyDeclarations.TryGetValue(clause, out var copy) && copy.Active) && clause.Left.BoundSymbol?.Kind is not (BindingSymbolKind.TypeParameter or BindingSymbolKind.SemanticsTarget or BindingSymbolKind.SemanticsParameter))
            {
                this.RequireConstraint(clause, this.ProveConstraint(constraint, this.ConstraintScope(clause)), mode);
            }

            if (node.BoundType is { Kind: BoundTypeKind.Constructed or BoundTypeKind.Dictionary, Symbol.Declaration: DeclarationContainerKoto container } type && container.ConstraintNodes.Count != 0 && !this.inferredConstructorTargets.Contains(node) &&
                (type.Kind != BoundTypeKind.Dictionary || type.Components.Count != 0))
            {
                var scope = this.ConstraintScope(node);
                var result = this.CheckConstraints(container.ConstraintNodes, container, (BoundType[])type.Components, scope, origins: (BoundOrigin[])type.OriginArguments);
                this.RequireConstraint(node, result, mode);
                if (node.BindingState == BindingState.Invalid)
                {
                    // Invalid type formation in a clause must not remain available as evidence.
                    for (var parent = node.Parent; parent is not null; parent = parent.Parent)
                    {
                        if (parent is IsKoto { BoundConstraint: not null })
                        {
                            (scope.Constraints ??= new()).Invalid = true;
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

    // SPEC 23.1.2: an unproven Definition obligation publishes its Language cause, a missing slot before a static exclusive one, and
    // keeps an unproven target role of the same occurrence as a separate record; every final check recomputes the causes.
    private void FailTypeRole(BindingObligation obligation)
    {
        var causes = this.TypeRoleCauses(obligation);
        var failure = (causes & TypeRoleCause.MissingSlot) != 0 ? BindingFailure.MissingOrigin
            : (causes & TypeRoleCause.StaticExclusive) != 0 ? BindingFailure.InvalidOrigin
            : BindingFailure.UnprovenConstraint;
        this.Fail(obligation.Use, failure);
        if (failure != BindingFailure.UnprovenConstraint)
        {
            (this.formationCauses ??= new(ReferenceEqualityComparer.Instance))[obligation.Use] = causes;
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
        if (this.formationCauses is not { Count: > 0 } causes)
        {
            return null;
        }

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
        static Koto? FieldOccurrence(Koto read, Dictionary<Koto, TypeRoleCause> causes)
        {
            if (KotoHelper.UnwrapParentheses(read) is not MemberAccessKoto { BoundSymbol.Declaration: { } member })
            {
                return null;
            }

            foreach (var cause in causes.Keys)
            {
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
    private bool ReportFormationCause(Koto node, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        if (this.formationCauses?.TryGetValue(node, out var causes) != true || node is not TypeSemanticsKoto { BoundSymbol.Pair: { } pair } application ||
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
            node.Report(requirement, code, evidence: [$"{name} admits {text}, and a borrow needs an outer-Origin slot"], note: "A pair layer stores an outer Origin in its binder's borrow cases, which this occurrence does not name (SPEC 8.1.2)");
        }
        else
        {
            node.Report(requirement, code, evidence: [$"{name} admits {text}, which a static Origin never admits"], note: "A static outer-Origin slot admits no exclusive borrow (SPEC 8.1.2)");
        }

        return true;
    }

    private void RequireConstraint(Koto use, ConstraintProof proof, BindingMode mode, Koto? diagnosticCause = null)
    {
        if (proof == ConstraintProof.Error)
        {
            this.FailConstraint(use, diagnosticCause ?? this.TakeFailedCause().Use);
        }
        else if (proof == ConstraintProof.Refuted)
        {
            this.Fail(use, BindingFailure.UnsatisfiedConstraint);
        }
        else if (proof == ConstraintProof.Unknown && mode == BindingMode.Final)
        {
            this.FailUnprovenConstraint(use, this.TakeUnknownFact(), unresolved: false);
        }
        else if (proof == ConstraintProof.Unknown)
        {
            this.RequireUnproven(use, mode); // The provisional pass leaves the U fact to the next judgment.
        }
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
    // comparison's failing condition (design-r2 Amendments 4 and 5); without one, the Constraint alone; a foreign fact is a broken
    // contract. RequireConstraint, RequireUnproven and the direct sites that settle a clause list themselves (Enums, Specializations)
    // record through it.
    private BoundType? FailUnprovenConstraint(Koto use, CallableUnknown? fact, bool unresolved)
    {
        if (!this.OwnFrame(use, fact?.Epoch))
        {
            return null;
        }

        if (fact is not { Clause: { } clause } || !fact.Failure.IsCondition)
        {
            return this.Fail(use, BindingFailure.UnprovenConstraint, unresolved);
        }

        var at = CallableArgument(use, clause) ?? use; // Located as the trial locates it (CallableOriginFailure).
        var record = new CallableConstraintFact(at, clause, fact.Subject, this.ContractFact(at, fact.Failure, use));
        return this.FailExplained(ref this.callableConstraints, use, BindingFailure.UnprovenConstraint, record, unresolved);
    }

    // A proof of a proposition, settled where it is no longer combined (EndProof).
    private ConstraintProof ProveConstraint(BoundConstraint proposition, BindingScope scope)
    {
        this.BeginProof();
        return this.EndProof(this.ProveProposition(proposition, scope));
    }

    // SPEC 23.1.1: each conformance limit a failing check rested on, reported once at its location: an inherited conformance limited by
    // OCC-X at the derived Type's base clause (an explicit one was reported at its declaration, ValidateConformances). Runs in
    // Binding.Check's Final prologue, before the summaries' sites (FailNeededSites) and the derived uses resting on either (BU-76).
    private void PublishNeededLimits()
    {
        for (var i = 0; i < this.impls.Paths.Count; i++)
        {
            if (this.impls.Paths[i] is { Needed: true } path)
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
            return this.HasValueRole(type, scope, objectTarget) ? TypeRoleCause.None : TypeRoleCause.Role;
        }

        if (obligation.Kind != BindingObligationKind.TypeFormation ||
            obligation.Use is not TypeSemanticsKoto { SemanticsParameter: not null, BoundType: { } applied } application || application.BoundSymbol?.Pair?.WholeType is not { } whole)
        {
            return TypeRoleCause.Role;
        }

        // SPEC 8.1.1, 8.1.2: every admitted Semantics of `s` must form a valid Type with `U`; object-family bindings additionally need `U`
        // to be an Object Target.
        var admitted = this.AdmittedSemantics(whole, scope);
        const SemanticsMask objectFamily = SemanticsMask.Object | SemanticsMask.ObjectBorrow;
        if (admitted == SemanticsMask.None)
        {
            return TypeRoleCause.Role;
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

            if (!(applied.Kind == BoundTypeKind.Parameter || (admitted & ~objectFamily) == 0 || this.HasValueRole(type, scope, false)) ||
                ((admitted & objectFamily) != 0 && !this.HasValueRole(type, scope, true)))
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

        if (((admitted & SemanticsMask.Object) != 0 && !this.HasValueRole(type, scope, true)) ||
            ((admitted & (SemanticsMask.Owner | SemanticsMask.Raw)) != 0 && !this.HasValueRole(type, scope, false)))
        {
            causes |= TypeRoleCause.Role;
        }

        return causes;
    }

    /// <summary>Decides the value-Type role of a generic target, or its Object Target role (SPEC 8.7.3).</summary>
    private bool HasValueRole(BoundType type, BindingScope scope, bool objectTarget)
    {
        if (objectTarget)
        {
            // A determined Core is judged directly and shallowly, even with unbound Type arguments.
            if (type.Kind is BoundTypeKind.Nominal or BoundTypeKind.Constructed && type.Symbol?.Declaration is StructKoto or EnumKoto)
            {
                return type.Symbol.ObjectPayloadOptOut is null;
            }

            if (this.RequestCapability(type, this.Library.ObjectPayload, scope) == ConstraintProof.Proven)
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
        else if (this.RequestCapability(type, this.Library.ObjectPayload, scope) == ConstraintProof.Proven)
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
            if (current.Constraints is not { Invalid: false } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (fact.Kind == ConstraintKind.TypeIdentity && this.FactStates(fact, type, out var stated) && stated is { } required && required.Kind is not (BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.AssociatedProjection or BoundTypeKind.SemanticsApplication) && (!objectTarget || (required.Kind is BoundTypeKind.Nominal or BoundTypeKind.Constructed && required.Symbol?.Declaration is StructKoto or EnumKoto && required.Symbol.ObjectPayloadOptOut is null)) && this.ProveConstraint(fact, scope) == ConstraintProof.Proven)
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

    // SPEC 10.1 steps 3 to 5 (DECISIONS: G572 U2a): a candidate's bindings as inference leaves them and the selected call publishes
    // them; the one input of step 5's conjunction (StepFiveConjunction).
    // - Inputs: the trial's solution (SlotSolution.Prove) or the selected call's plan (CheckCallTypeConstraints, CallPlan's columns).
    // - Invariants: Slots holds the call's slots, the callee's own first (CallOwnSlots), a constructor's owner slots after them; a null Type
    //   slot is unsolved, and Unbound lists those 10.8 leaves unbound (none in a plan); Lengths is per slot; DeclaringType is the Type the
    //   member is found in, Owner its closed formation (a constructor's ConstructionType), null while its slots are open; InfersOwner holds
    //   while a constructor's trial infers its owner's slots (10.8.1), never in a plan.
    // - Prohibitions: no judgment but step 5's conjunction reads it; it holds no verdict.
    private readonly ref struct CandidateBindings
    {
        internal CandidateBindings(
            FunctionKoto function,
            BindingSymbol target,
            ReadOnlySpan<BoundType?> slots,
            ReadOnlySpan<BoundLength?> lengths,
            BoundType? self,
            BoundType? declaringType,
            BoundType? owner,
            SlotSet unbound,
            bool infersOwner)
        {
            this.Function = function;
            this.Target = target;
            this.Slots = slots;
            this.Lengths = lengths;
            this.Self = self;
            this.DeclaringType = declaringType;
            this.Owner = owner;
            this.Unbound = unbound;
            this.InfersOwner = infersOwner;
        }

        internal FunctionKoto Function { get; }

        internal BindingSymbol Target { get; }

        internal ReadOnlySpan<BoundType?> Slots { get; }

        internal ReadOnlySpan<BoundLength?> Lengths { get; }

        internal BoundType? Self { get; }

        internal BoundType? DeclaringType { get; }

        internal BoundType? Owner { get; }

        internal SlotSet Unbound { get; }

        internal bool InfersOwner { get; }
    }
}
