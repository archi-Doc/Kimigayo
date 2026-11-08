// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Conformance registration (SPEC 8.6.1, 8.6.2, 8.8): the Self-clause and conditional-block registrar of ImplTable. Self clauses register
// their direct and refinement paths here; conditional blocks (BindConditionalDeclarations) add theirs through
// RegisterConformanceDeclaration, inherited paths (RegisterInheritedConformances) through ImplTable.Register.
// Input: bound Self clauses, conditional blocks. Output: their paths in ImplTable; each Type's ObjectPayload opt-out, its own or inherited
// (SPEC 8.7.3); the Self-clause failures: a closed or intrinsic Contract's grant, an invalid negated clause and a colliding direct
// conformance (SPEC 8.7, 8.7.1, 8.9.1).
// Invariants: a clause registers its direct path before its ancestors' paths, in the Contract's ancestor order.
// Must not: verify a path (ContractMatching, InheritedConformances) or decide availability at a use (ResolveConformance).
public sealed partial class Binding
{
    private readonly ImplTable impls = new();

    private void RegisterConformances()
    {
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is not DeclarationContainerKoto container || container is not (StructKoto or EnumKoto))
            {
                continue;
            }

            for (var i = 0; i < container.ConstraintNodes.Count; i++)
            {
                var clause = container.ConstraintNodes[i];
                if (IsSelfConstraint(clause) && clause.BoundConstraint is { } constraint)
                {
                    Register(constraint, container.BoundSymbol!, clause, true);
                }
            }
        }

        this.PublishInheritedOptOuts();

        void Register(BoundConstraint constraint, BindingSymbol type, IsKoto use, bool standalone)
        {
            if (constraint.Kind == ConstraintKind.And)
            {
                Register(constraint.Left!, type, use, false);
                Register(constraint.Right!, type, use, false);
            }
            else if (constraint.Contract is { } closed && this.IsClosedContractGrant(closed, type))
            {
                this.Fail(use, BindingFailure.ClosedContractConformance); // SPEC 8.7: the conforming Types of a closed Contract are fixed.
            }
            else if (constraint.Contract is { } intrinsic && IntrinsicContract.Of(intrinsic.Intrinsic).Declaration is not BindingFailure.None and var failure)
            {
                this.Fail(use, failure); // SPEC 8.7.1: decided by the Type's structure alone.
            }
            else if (constraint is { Kind: ConstraintKind.Contract, Contract: { Contract: not null } contract })
            {
                this.RegisterConformanceDeclaration(type, contract, use, this.scopes[type.Declaration], null);
            }
            else if (constraint.Kind == ConstraintKind.Not)
            {
                // SPEC 8.2, 8.7.3: the only negated Self clause is the standalone, unrepeated ObjectPayload opt-out.
                if (standalone && constraint.Left is { Kind: ConstraintKind.Contract, Contract.Intrinsic: IntrinsicKind.ObjectPayload } && !ReferenceEquals(type.ObjectPayloadOptOut, type))
                {
                    type.ObjectPayloadOptOut = type;
                }
                else
                {
                    this.Fail(use, BindingFailure.InvalidSelfClause);
                }
            }
        }
    }

    // SPEC 8.7.3: derived structs inherit the opt-out. Nominal bases are read here, before headers bind, so that every later object
    // formation sees the effective state; a clause not ready ends the chain, and this runs again once clauses are bindable.
    private void PublishInheritedOptOuts()
    {
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is StructKoto { BaseClause: not null } structure && structure.BoundSymbol is { ObjectPayloadOptOut: null } symbol)
            {
                symbol.ObjectPayloadOptOut = this.InheritedObjectPayloadOptOut(structure);
            }
        }
    }

    private BindingSymbol? InheritedObjectPayloadOptOut(StructKoto structure)
    {
        foreach (var layer in this.BaseLayers(structure))
        {
            if (layer.ObjectPayloadOptOut is { } optOut)
            {
                return optOut;
            }
        }

        return null;
    }

    private void RegisterConformanceDeclaration(BindingSymbol type, BindingSymbol contract, IsKoto use, BindingScope scope, SyntaxFormKoto? premises)
    {
        if (this.impls.OfType(type) is { } previousBindings)
        {
            foreach (var identity in previousBindings)
            {
                if (!ReferenceEquals(identity.Contract, contract) && identity.DirectClause is { } clause &&
                    this.ContractBindingsMayCollide(identity.Contract, contract, scope))
                {
                    identity.Invalid = true;
                    this.FailDuplicate(clause, use);
                }
            }
        }

        var direct = this.impls.Register(type, contract, contract, use, scope, premises);
        if (direct.Identity.DirectClause is { } previous)
        {
            direct.Identity.Invalid = true;
            this.FailDuplicate(use, previous);
        }

        direct.Identity.DirectClause = use;
        var shape = contract.Contract!;
        for (var i = 0; i < shape.Ancestors.Count; i++)
        {
            this.impls.Register(type, shape.Ancestors[i], contract, use, scope, premises);
        }
    }
}
