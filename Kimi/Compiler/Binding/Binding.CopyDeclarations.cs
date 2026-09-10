// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<Koto, CopyDeclaration> copyDeclarations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DeclarationContainerKoto, List<CopyDeclaration>> copyByType = new(ReferenceEqualityComparer.Instance);

    private static bool PositiveRequirement(BoundConstraint constraint)
        => constraint.Kind == ConstraintKind.And ? PositiveRequirement(constraint.Left!) && PositiveRequirement(constraint.Right!) : constraint.Kind is ConstraintKind.TypeIdentity or ConstraintKind.Semantics or ConstraintKind.Contract;

    private bool DeclaresCopy(BoundConstraint constraint)
        => constraint.Kind == ConstraintKind.And ? this.DeclaresCopy(constraint.Left!) || this.DeclaresCopy(constraint.Right!) : constraint.Kind == ConstraintKind.Contract && IsRefinement(constraint.Contract!, this.Core.Copy);

    private void BindCopyDeclarations()
    {
        foreach (var declaration in this.copyDeclarations.Values)
        {
            declaration.Active = false;
        }

        foreach (var declarations in this.copyByType.Values)
        {
            declarations.Clear();
        }

        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is not DeclarationContainerKoto container || container is not (StructKoto or EnumKoto))
            {
                continue;
            }

            for (var i = 0; i < container.ConstraintNodes.Count; i++)
            {
                var clause = container.ConstraintNodes[i];
                if (clause.Left is IdentifierNameKoto { IdentifierName: "Self" } && clause.BoundConstraint is { } bound && this.DeclaresCopy(bound))
                {
                    this.RegisterCopy(clause, container, this.scopes[container], null);
                }
            }

            for (var i = 0; i < container.Members.Count; i++)
            {
                if (container.Members[i] is not SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } conditional || conditional.Operands.Length != 2 || conditional.Operands[0] is not IsKoto target || conditional.Operands[1] is not SyntaxFormKoto premises)
                {
                    continue;
                }

                var scope = this.GetScope(conditional, this.scopes[container]);
                this.BindConstraint(target, this.scopes[container]);
                var valid = target.Left is IdentifierNameKoto { IdentifierName: "Self" } && target.BoundConstraint is { Kind: ConstraintKind.Contract } proposition && ReferenceEquals(proposition.Contract, this.Core.Copy) && premises.Operands.Length != 0;
                for (var j = 0; j < premises.Operands.Length; j++)
                {
                    var condition = (IsKoto)premises.Operands[j];
                    this.BindConstraint(condition, scope);
                    valid &= condition.BoundConstraint is { } requirement && PositiveRequirement(requirement) && ReferenceEquals(condition.Left.BoundSymbol?.Scope, this.scopes[container]);
                }

                Complete(premises, BoundType.Boolean);
                if (valid)
                {
                    this.ExpandScopeContractPremises(scope);
                    Complete(conditional, BoundType.Unit);
                    this.RegisterCopy(target, container, scope, premises);
                }
                else
                {
                    Fail(conditional, BindingFailure.InvalidConstraint);
                    (scope.Constraints ??= new()).Invalid = true;
                }
            }
        }
    }

    private void RegisterCopy(IsKoto clause, DeclarationContainerKoto container, BindingScope scope, SyntaxFormKoto? premises)
    {
        if (!this.copyDeclarations.TryGetValue(clause, out var declaration))
        {
            declaration = new(clause, container, scope, premises);
            this.copyDeclarations.Add(clause, declaration);
        }

        if (!this.copyByType.TryGetValue(container, out var list))
        {
            this.copyByType.Add(container, list = new());
        }

        declaration.Active = true;
        list.Add(declaration);
    }

    private void ValidateCopyDeclarations(BindingMode mode)
    {
        foreach (var declarations in this.copyByType.Values)
        {
            for (var i = 0; i < declarations.Count; i++)
            {
                var declaration = declarations[i];
                var proof = this.RequestCapability(this.SelfType(declaration.Container.BoundSymbol!), this.Core.Copy, declaration.Scope, derivation: true);
                this.RequireConstraint(declaration.Clause, proof, mode);
            }
        }
    }

    private sealed class CopyDeclaration(IsKoto clause, DeclarationContainerKoto container, BindingScope scope, SyntaxFormKoto? premises)
    {
        internal IsKoto Clause { get; } = clause;

        internal DeclarationContainerKoto Container { get; } = container;

        internal BindingScope Scope { get; } = scope;

        internal SyntaxFormKoto? Premises { get; } = premises;

        internal bool Active { get; set; }
    }
}
