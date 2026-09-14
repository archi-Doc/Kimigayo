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
        declaration.Container = container;
        declaration.Scope = scope;
        declaration.Premises = premises;
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

        internal DeclarationContainerKoto Container { get; set; } = container;

        internal BindingScope Scope { get; set; } = scope;

        internal SyntaxFormKoto? Premises { get; set; } = premises;

        internal bool Active { get; set; }
    }
}
