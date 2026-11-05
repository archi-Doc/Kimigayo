// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Conformance registration (SPEC 8.6.1, 8.6.2, 8.8): the owner of the conformance tables. Self clauses register their direct and
// refinement paths here; conditional blocks (BindConditionalDeclarations) and inherited paths (RegisterInheritedConformances) add
// theirs through RegisterConformanceDeclaration and RegisterConformance.
// Input: bound Self clauses, conditional blocks and a base's paths. Output: `conformances` (one identity per Type and Contract),
// `conformancePaths` (one path per Type, Contract, declaration and root Contract), `conformancesByType` and `activeConformancePaths`;
// each Type's ObjectPayload opt-out, its own or inherited (SPEC 8.7.3); the Self-clause failures: a closed or intrinsic Contract's
// grant, an invalid negated clause and a colliding direct conformance (SPEC 8.7, 8.7.1, 8.9.1).
// Invariants: a path joins the lists only at its own registration, so they follow its caller's order and a derived Type's equal its
// base's; the caller that registers a path registers its root path; each key keeps one object across binds.
// Must not: verify a path (ContractMatching, InheritedConformances) or decide availability at a use (ResolveConformance).
public sealed partial class Binding
{
    private readonly Dictionary<(BindingSymbol Type, BindingSymbol Contract), BoundConformance> conformances = new();
    private readonly Dictionary<(BindingSymbol Type, BindingSymbol Contract, IsKoto Declaration, BindingSymbol Root), BoundConformancePath> conformancePaths = new();
    private readonly List<BoundConformancePath> activeConformancePaths = new();
    private readonly Dictionary<BindingSymbol, List<BoundConformance>> conformancesByType = new(ReferenceEqualityComparer.Instance);

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

        // SPEC 8.7.3: derived structs inherit the opt-out. Bases are resolved by name here,
        // before headers bind, so that every later object formation sees the effective state.
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is StructKoto { Bases.Count: > 0 } structure && structure.BoundSymbol is { ObjectPayloadOptOut: null } symbol)
            {
                symbol.ObjectPayloadOptOut = this.InheritedObjectPayloadOptOut(structure);
            }
        }

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

    private BindingSymbol? InheritedObjectPayloadOptOut(StructKoto structure)
    {
        // The current language has one inline base. The bound limits invalid cycles too.
        for (var depth = 0; structure.Bases.Count > 0 && depth <= this.nodes.Count; depth++)
        {
            var syntax = structure.Bases[0];
            var type = syntax.BoundType?.Symbol ?? this.TypeName(syntax, this.scopes[structure], false);
            if (type is null)
            {
                return null;
            }

            if (type.ObjectPayloadOptOut is not null)
            {
                return type.ObjectPayloadOptOut;
            }

            if (type.Declaration is not StructKoto next)
            {
                return null;
            }

            structure = next;
        }

        return null;
    }

    private void RegisterConformanceDeclaration(BindingSymbol type, BindingSymbol contract, IsKoto use, BindingScope scope, SyntaxFormKoto? premises)
    {
        if (this.conformancesByType.TryGetValue(type, out var previousBindings))
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

        var direct = this.RegisterConformance(type, contract, contract, use, scope, premises);
        if (direct.Identity.DirectClause is { } previous)
        {
            direct.Identity.Invalid = true;
            this.FailDuplicate(use, previous);
        }

        direct.Identity.DirectClause = use;
        var shape = contract.Contract!;
        for (var i = 0; i < shape.Ancestors.Count; i++)
        {
            this.RegisterConformance(type, shape.Ancestors[i], contract, use, scope, premises);
        }
    }

    private BoundConformancePath RegisterConformance(BindingSymbol type, BindingSymbol contract, BindingSymbol root, IsKoto use, BindingScope scope, SyntaxFormKoto? premises)
    {
        var result = this.ConformancePath(type, contract, use, root);
        if (!result.Active)
        {
            var identity = result.Identity;
            result.Active = true;
            result.InheritedFrom = null;
            result.AdditionalInheritedSources?.Clear();
            result.InheritedBase = null;
            result.Use = use;
            result.Declaration = use;
            result.RootContract = root;
            // The root path is named here and registered by its own call, so the lists below follow the caller's order whichever
            // path comes first: an inherited base's order, where a refinement may follow its parent Contract's own conformance.
            result.RootPath = ReferenceEquals(contract, root) ? result : this.ConformancePath(type, root, use, root);
            result.Premises = premises;
            result.Scope.Parent = scope;
            this.activeConformancePaths.Add(result);
            if (identity.PathStorage.Count == 0)
            {
                if (!this.conformancesByType.TryGetValue(type, out var list))
                {
                    this.conformancesByType.Add(type, list = new());
                }

                list.Add(identity);
            }

            identity.PathStorage.Add(result);
        }

        return result;
    }

    // One path per Type, Contract, declaration and root Contract, created when first named; only its registration activates it.
    private BoundConformancePath ConformancePath(BindingSymbol type, BindingSymbol contract, IsKoto use, BindingSymbol root)
    {
        if (!this.conformances.TryGetValue((type, contract), out var identity))
        {
            this.conformances.Add((type, contract), identity = new(type, contract));
        }

        if (!this.conformancePaths.TryGetValue((type, contract, use, root), out var result))
        {
            this.conformancePaths.Add((type, contract, use, root), result = new(type, contract) { Identity = identity });
        }

        return result;
    }
}
