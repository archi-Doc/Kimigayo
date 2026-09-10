// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private void ValidateConstraintUses(BindingMode mode)
    {
        for (var i = 0; i < this.nodes.Count; i++)
        {
            var node = this.nodes[i];
            if (node is IsKoto { BoundConstraint: { } constraint } clause && clause.Left.BoundSymbol?.Kind is not (BindingSymbolKind.TypeParameter or BindingSymbolKind.SemanticsTarget or BindingSymbolKind.SemanticsParameter))
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

    private void RequireConstraint(Koto use, ConstraintProof proof, BindingMode mode)
    {
        if (proof == ConstraintProof.Error)
        {
            Fail(use, BindingFailure.InvalidConstraint);
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

        if (obligation.Kind == BindingObligationKind.TypeFormation && obligation.Use is TypeSemanticsKoto { SemanticsParameter: not null } application && application.BoundSymbol?.Pair?.WholeType is { } whole)
        {
            // Borrow application still needs its Origin/Loan contract. This step discharges only
            // applications proved to need no outer Origin, without choosing a concrete instance.
            return this.HasSemanticsRole(whole, SemanticsMask.Owner | SemanticsMask.Unsafe, scope) && this.HasValueRole(type, scope, false);
        }

        return false;
    }

    private bool HasValueRole(BoundType type, BindingScope scope, bool objectTarget)
    {
        if (!objectTarget && type.Kind is not (BoundTypeKind.TargetProjection or BoundTypeKind.AssociatedProjection))
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
