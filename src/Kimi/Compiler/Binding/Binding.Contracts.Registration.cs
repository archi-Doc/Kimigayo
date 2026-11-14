// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Conformance registration (SPEC 8.6.1, 8.6.2, 8.8): the Self-clause and conditional-block registrar of ImplTable. Self clauses register
// their direct and refinement paths here; conditional blocks (BindConditionalDeclarations) add theirs through
// RegisterConformanceDeclaration, inherited paths (RegisterInheritedConformances) through ImplTable.Register.
// Input: bound Self clauses, conditional blocks. Output: their paths in ImplTable; each Type's ObjectPayload opt-out, its own or inherited
// (SPEC 8.7.3), and the object forms formed before it (VerifyEarlyObjectForms); the Self-clause failures: a closed or intrinsic
// Contract's grant, an invalid negated clause and a colliding direct conformance (SPEC 8.7, 8.7.1, 8.9.1).
// Invariants: a clause registers its direct path before its ancestors' paths, in the Contract's ancestor order.
// Must not: verify a path (ContractMatching, InheritedConformances) or decide availability at a use (ResolveConformance).
public sealed partial class Binding
{
    private readonly ImplTable impls = new();

    // Early reads of the conformances (VerifyEarlyProjections), each with the path end its producer formed (End): a Projection over a
    // declared struct or enum that found no associated Type before this pass's conformance declarations registered (NoteEarlyProjection),
    // and a Selected nested Type whose 9.7.3 judgment found no evidence before the proofs answer (AlsoAssociated). Temporary (G621, G728):
    // removed when R7-D2 d3 reads conformance declarations by demand.
    private readonly List<(MemberAccessKoto Use, PathEnd End, BoundType Type, BindingScope Scope, string Name, BindingSymbol? Qualifier)> earlyProjections = new();

    // Object forms over a concrete Type formed before the opt-outs are published (VerifyEarlyObjectForms).
    private List<(Koto Use, BoundType Target)>? earlyObjectForms;

    // Whether this pass's conformance declarations have registered (RegisterConformances, after BindConstraints' pass 0).
    private bool conformancesRegistered;

    // SPEC 8.6.1: `Self is C` in a Type declaration declares conformance; RegisterConformances registers exactly these clauses.
    private static bool DeclaresConformance(IsKoto clause)
        => clause.Parent is StructKoto or EnumKoto && IsSelfConstraint(clause);

    private void RegisterConformances()
    {
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is not DeclarationContainerKoto container)
            {
                continue;
            }

            for (var i = 0; i < container.ConstraintNodes.Count; i++)
            {
                var clause = container.ConstraintNodes[i];
                if (DeclaresConformance(clause) && clause.BoundConstraint is { } constraint)
                {
                    Register(constraint, container.BoundSymbol!, clause, true);
                }
            }
        }

        this.conformancesRegistered = true;
        this.VerifyEarlyProjections();
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

    // SPEC 8.6.1, 8.5.2 (G621's limit): an unqualified projection over a declared Type that found no associated Type before the
    // conformance declarations registered (its candidates come from the conformance table) and finds one now names an existing member;
    // its conformance declaration bound before the table filled, which is an analysis limit: a located Unsupported_Kd that stays
    // unresolved, so the clause and the Type rest on it, never a missing member. One that still finds none keeps its missing-Type
    // record, which VerifyEarlyReads may read again. A qualified projection searches its Contract, so it finds an existing member before
    // registration. A nested Type whose 9.7.3 judgment (AlsoAssociated) found no evidence is judged again by AlsoAssociated, here and
    // once the proofs answer (BindDeclarations, after ValidateConformances): one that resolves both ways is ambiguous, the record its
    // timely judgment gives (BindContainerReference), though its users were bound on the nested Type; one still without evidence before
    // the proofs answer notes itself again. The note says which judgment it is (End), never where the path's lookup was published.
    private void VerifyEarlyProjections()
    {
        var noted = this.earlyProjections.Count; // A judgment still early notes itself again, behind these.
        for (var i = 0; i < noted; i++)
        {
            var (use, end, type, scope, name, qualifier) = this.earlyProjections[i];
            if (end == PathEnd.Selected)
            {
                if (this.AlsoAssociated(type, name, use, scope))
                {
                    this.Fail(use, BindingFailure.Ambiguous, unresolved: true); // SPEC 9.7.3: it resolves both ways.
                }
            }
            else if (use.BindingFailure is BindingFailure.MissingName or BindingFailure.MissingType && this.FindAssociated(type, scope, name, qualifier, use) is not null)
            {
                use.BindingFailure = BindingFailure.None;
                this.prerequisites.Remove(use);
                this.Fail(use, BindingFailure.Unsupported, true);
            }
        }

        this.earlyProjections.RemoveRange(0, noted);
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

    // SPEC 8.7.3: an object form over a concrete Type formed while the Constraints bind, before the opt-outs are published, is judged
    // again by the same rule once they are (an early read of the opt-out, as BaseLayers' early reads; removed with them at R7-D2 d3, when
    // a declaration's opt-out is read by demand).
    private void VerifyEarlyObjectForms()
    {
        if (this.earlyObjectForms is not { Count: > 0 } forms)
        {
            return;
        }

        foreach (var (use, target) in forms)
        {
            if (use.BindingFailure == BindingFailure.None)
            {
                this.RenouncedObjectTarget(use, target);
            }
        }

        forms.Clear();
    }

    // SPEC 8.7.3: the one read of an object form's concrete target against the opt-out, its own or inherited: an opted-out target fails
    // the form with NotObjectPayload_Kd naming the declaring Type. BindType reads it at formation and VerifyEarlyObjectForms once more.
    private bool RenouncedObjectTarget(Koto form, BoundType target)
    {
        if (target.Symbol?.ObjectPayloadOptOut is not { } renounced)
        {
            return false;
        }

        this.FailObjectPayload(form, renounced);
        return true;
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
        if (this.ConformancesOf(type) is { } previousBindings)
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
