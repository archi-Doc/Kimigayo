// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static bool InvalidConstraintRequirement(BoundConstraint constraint)
        => (constraint.Contract is { } contract && InvalidDeclarationContext(contract.Declaration)) ||
        (constraint.RequiredType is { } required && InvalidConstraintType(required)) ||
        (constraint.Left is { } left && InvalidConstraintRequirement(left)) ||
        (constraint.Right is { } right && InvalidConstraintRequirement(right));

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

        if (node.Parent is StructKoto structure)
        {
            for (var i = 0; i < structure.Bases.Count; i++)
            {
                if (ReferenceEquals(node, structure.Bases[i]))
                {
                    return node;
                }
            }
        }

        return node is SyntaxFormKoto { Akind: KotoKind.EnumCase, Parent: EnumKoto enumeration } ? enumeration : null;
    }

    private ConstraintProof CheckTypeConstraints(BoundType type, BindingScope scope)
    {
        if (type.Symbol is { } symbol && InvalidDeclarationContext(symbol.Declaration))
        {
            return ConstraintProof.Error;
        }

        var proof = type.Symbol is { } declaration && UnresolvedTypeDeclarationContext(declaration.Declaration) ? ConstraintProof.Unknown : ConstraintProof.Proven;
        for (var i = 0; i < type.Components.Count; i++)
        {
            proof = CombineProof(proof, this.CheckTypeConstraints(type.Components[i], scope), true);
        }

        if (type is { Kind: BoundTypeKind.Constructed, Symbol.Declaration: DeclarationContainerKoto container })
        {
            for (var parent = container; parent is not null && !parent.IsRoot; parent = parent.Parent as DeclarationContainerKoto)
            {
                proof = CombineProof(proof, this.CheckConstraints(parent.ConstraintNodes, container, (BoundType[])type.Components, scope), true);
            }
        }

        return proof;
    }

    private ConstraintProof CheckSignatureTypeConstraints(FunctionKoto function)
    {
        var scope = this.scopes[function];
        var proof = function.BoundSymbol?.Type is { } result ? this.CheckTypeConstraints(result, scope) : ConstraintProof.Unknown;
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            proof = CombineProof(proof, function.Parameters[i].Type.BoundType is { } parameter ? this.CheckTypeConstraints(parameter, scope) : ConstraintProof.Unknown, true);
        }

        return proof;
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

    private ConstraintProof CheckClosedDeclarationConstraints(DeclarationContainerKoto container)
    {
        var proof = ConstraintProof.Proven;
        for (var i = 0; i < container.ConstraintNodes.Count; i++)
        {
            var clause = container.ConstraintNodes[i];
            if (clause.IsAssociatedConstraint || IsSelfConstraint(clause))
            {
                continue;
            }

            if (clause.BoundConstraint is not { } constraint)
            {
                proof = CombineProof(proof, ConstraintProof.Unknown, true);
                continue;
            }

            if (DependentConstraint(constraint, contractSelf: container is ContractKoto))
            {
                continue;
            }

            var scope = this.scopes[container];
            var subject = clause.Left.BoundType;
            var formation = subject is null ? ConstraintProof.Unknown : this.CheckTypeConstraints(subject, scope);
            proof = CombineProof(proof, CombineProof(formation, this.ProveConstraint(constraint, scope), true), true);
        }

        return proof;
    }

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

            if (this.nodes[i] is IsKoto { BindingState: BindingState.Invalid, BoundConstraint: { } failedConstraint, Parent: DeclarationContainerKoto implementation } failed && implementation is StructKoto or EnumKoto && IsSelfConstraint(failed) && InvalidConstraintRequirement(failedConstraint))
            {
                // An invalid required declaration invalidates the implementing Type.
                // A path-local witness failure must preserve independent conformances.
                var previous = implementation.BindingState;
                Fail(implementation, failed.BindingFailure);
                changed |= previous != implementation.BindingState;
            }

            if (this.nodes[i] is ContractKoto { BoundSymbol.Contract: { } shape } contract)
            {
                if (shape.HasUnresolvedParents)
                {
                    var previous = contract.BindingState;
                    this.RequireConstraint(contract, ConstraintProof.Unknown, mode);
                    changed |= previous != contract.BindingState;
                }

                for (var a = 0; a < shape.Ancestors.Count; a++)
                {
                    if (InvalidDeclarationContext(shape.Ancestors[a].Declaration))
                    {
                        var previous = contract.BindingState;
                        Fail(contract, BindingFailure.UnsatisfiedConstraint);
                        changed |= previous != contract.BindingState;
                        break;
                    }

                    if (UnresolvedTypeDeclarationContext(shape.Ancestors[a].Declaration))
                    {
                        var previous = contract.BindingState;
                        this.RequireConstraint(contract, ConstraintProof.Unknown, mode);
                        changed |= previous != contract.BindingState;
                    }
                }
            }

            if (this.nodes[i] is not IsKoto { Parent: DeclarationContainerKoto container, IsAssociatedConstraint: false, BoundConstraint: { } constraint, Left.BoundType: { } subject } clause || container is not (StructKoto or EnumKoto or ContractKoto) || IsSelfConstraint(clause) || DependentConstraint(constraint, contractSelf: container is ContractKoto))
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

            if (node.BoundType is { Kind: BoundTypeKind.Constructed, Symbol.Declaration: DeclarationContainerKoto container } type && container.ConstraintNodes.Count != 0)
            {
                var scope = this.ConstraintScope(node);
                var result = this.CheckConstraints(container.ConstraintNodes, container, (BoundType[])type.Components, scope);
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
            if (obligation.Deadline == BindingDeadline.Definition && this.ProveTypeRole(obligation))
            {
                this.obligationSet.Remove(obligation);
                continue;
            }

            this.obligations[remaining++] = obligation;
        }

        this.obligations.RemoveRange(remaining, this.obligations.Count - remaining);
    }

    private void RequireConstraint(Koto use, ConstraintProof proof, BindingMode mode, Koto? diagnosticCause = null)
    {
        if (proof == ConstraintProof.Error)
        {
            this.FailConstraint(use, diagnosticCause);
        }
        else if (proof == ConstraintProof.Refuted)
        {
            Fail(use, BindingFailure.UnsatisfiedConstraint);
        }
        else if (proof == ConstraintProof.Unknown)
        {
            // The provisional pass can gain bindings/conformance evidence across the Mod boundary.
            if (mode == BindingMode.Final)
            {
                Fail(use, BindingFailure.UnprovenConstraint);
            }
            else if (use.BindingState != BindingState.Invalid)
            {
                use.BindingState = BindingState.Unresolved;
            }
        }
    }

    private bool ProveTypeRole(BindingObligation obligation)
    {
        if (obligation.Type is not { } type)
        {
            return false;
        }

        var scope = this.ConstraintScope(obligation.Use);
        var objectTarget = obligation.Use is TypeSemanticsKoto { Type: not null, SemanticsKind: SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc or SemanticsKind.ObjRef or SemanticsKind.ObjUniq };
        if (obligation.Kind == BindingObligationKind.TypeRole)
        {
            return this.HasValueRole(type, scope, objectTarget);
        }

        if (obligation.Kind == BindingObligationKind.TypeFormation &&
            obligation.Use is TypeSemanticsKoto { SemanticsParameter: not null, BoundType: { } applied } application && application.BoundSymbol?.Pair?.WholeType is { } whole)
        {
            if (application.OriginExpression is not null || application.OriginName is not null)
            {
                var allowed = applied.Origin?.Kind == OriginKind.Static ? SemanticsMask.Ref | SemanticsMask.ObjRef : SemanticsMask.ValueBorrow | SemanticsMask.ObjRef | SemanticsMask.ObjUniq;
                return this.HasSemanticsRole(whole, allowed, scope) && (applied.Kind == BoundTypeKind.Parameter || this.HasValueRole(type, scope, false));
            }

            if (applied.Kind == BoundTypeKind.SemanticsApplication && applied.Origin is not null)
            {
                // The outer slot activates only for safe-borrow bindings. Object
                // bindings additionally require the ordinary payload formation proof.
                return this.HasValueRole(type, scope, false) &&
                    (this.HasValueRole(type, scope, true) || this.HasSemanticsRole(whole, SemanticsMask.Owner | SemanticsMask.ValueBorrow | SemanticsMask.Unsafe, scope));
            }

            return this.HasSemanticsRole(whole, SemanticsMask.Owner | SemanticsMask.Unsafe, scope) && this.HasValueRole(type, scope, false);
        }

        return false;
    }

    private bool HasValueRole(BoundType type, BindingScope scope, bool objectTarget)
    {
        if (objectTarget && type.Kind == BoundTypeKind.Parameter && type.Symbol is { Slot: 0 } parameter &&
            ReferenceEquals(parameter.Scope.Owner, this.Library.MakeObj.Declaration))
        {
            // The intrinsic's declaration carries payload eligibility; each call
            // must prove this rule for its inferred or explicit actual T.
            return true;
        }

        if (!objectTarget && type.Kind is not (BoundTypeKind.TargetProjection or BoundTypeKind.AssociatedProjection))
        {
            return true;
        }

        if (objectTarget && this.RequestCapability(type, this.Library.Sealed, scope) == ConstraintProof.Proven)
        {
            return true;
        }

        if (objectTarget && type.Kind is BoundTypeKind.Nominal or BoundTypeKind.Constructed && type.Symbol?.Declaration is StructKoto)
        {
            return true;
        }

        if (!objectTarget && type.Kind == BoundTypeKind.TargetProjection && type.Symbol?.WholeType is { } whole && this.HasSemanticsRole(whole, SemanticsMask.Owner | SemanticsMask.ValueBorrow | SemanticsMask.Unsafe, scope))
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
                if (fact.Kind == ConstraintKind.TypeIdentity && ReferenceEquals(fact.Subject, type) && fact.RequiredType is { } required && required.Kind is not (BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.AssociatedProjection or BoundTypeKind.SemanticsApplication) && (!objectTarget || (required.Kind is BoundTypeKind.Nominal or BoundTypeKind.Constructed && required.Symbol?.Declaration is StructKoto)) && this.ProveConstraint(fact, scope) == ConstraintProof.Proven)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool HasSemanticsRole(BoundType whole, SemanticsMask allowed, BindingScope scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { Invalid: false } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (fact.Kind == ConstraintKind.Semantics && ReferenceEquals(fact.Subject, whole) && (fact.Mask & ~allowed) == 0 && this.ProveConstraint(fact, scope) == ConstraintProof.Proven)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
