// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly CopyDeclarationRows copyDeclarations;
    private readonly CopyByTypeRows copyByType;

    // A pending atom (Provisional: its name is missing) names a Contract or a complete Type once found, both positive (SPEC 8.8.1).
    private static bool PositiveRequirement(BoundConstraint constraint)
        => constraint.Kind == ConstraintKind.And ? PositiveRequirement(constraint.Left!) && PositiveRequirement(constraint.Right!) : constraint.Kind is ConstraintKind.TypeIdentity or ConstraintKind.Semantics or ConstraintKind.Contract or ConstraintKind.Callable or ConstraintKind.Unresolved;

    // SPEC 3.5.2, 8.7.1: only the Type's own `Self is Copy` opts in; a Contract that requires Copy, as a parent or a clause, never does.
    private bool DeclaresCopy(BoundConstraint constraint)
        => constraint.Kind == ConstraintKind.And ? this.DeclaresCopy(constraint.Left!) || this.DeclaresCopy(constraint.Right!) : constraint.Kind == ConstraintKind.Contract && ReferenceEquals(constraint.Contract, this.Library.Copy);

    private void BindCopyDeclarations()
    {
        foreach (var declaration in this.copyDeclarations.Values.In(this.pass))
        {
            declaration!.Active = false;
        }

        foreach (var declarations in this.copyByType.Values.In(this.pass))
        {
            declarations!.Clear();
        }

        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is not DeclarationContainerKoto container)
            {
                continue;
            }

            for (var i = 0; i < container.ConstraintNodes.Count; i++)
            {
                var clause = container.ConstraintNodes[i];
                if (DeclaresConformance(clause) && clause.BoundConstraint is { } bound && this.DeclaresCopy(bound))
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
            CopyDeclarationRows.Slot(clause) = declaration = new(clause, container, scope, premises);
        }

        if (!this.copyByType.TryGetValue(container, out var list))
        {
            CopyByTypeRows.Slot(container) = list = new();
        }

        declaration.Active = true;
        declaration.Container = container;
        declaration.Scope = scope;
        declaration.Premises = premises;
        list.Add(declaration);
    }

    private void ValidateCopyDeclarations(BindingMode mode)
    {
        // D2b-d's d8 order: the source modules by ordinal, then the library, each by ascending id (the walk's order on a cold compile).
        foreach (var declarations in this.copyByType.Values.In(this.pass))
        {
            for (var i = 0; i < declarations!.Count; i++)
            {
                var declaration = declarations[i];
                var proof = this.RequestCapability(this.SelfType(declaration.Container.BoundSymbol!), this.Library.Copy, declaration.Scope, declaration.Clause, derivation: true);
                this.RequireConstraint(declaration.Clause, proof, mode, proof == ConstraintProof.Error ? this.ConformanceDiagnosticCause(declaration.Container) : null);
            }
        }
    }

    internal sealed class CopyDeclaration(IsKoto clause, DeclarationContainerKoto container, BindingScope scope, SyntaxFormKoto? premises)
    {
        private PremiseFailure? failed;

        internal IsKoto Clause { get; } = clause;

        // What a proof reading the opt-in once its clause failed rests on: the clause's own record (NominalCopy; SPEC 23.1.2; R7-C6 a,
        // F's C8 rulings 25 and 29: a limit's record makes the reader its dependent, a Language one leaves the reader's own), made once,
        // as an environment's PremiseFailure is.
        internal PremiseFailure Failed => this.failed ??= new(this.Clause, null);

        internal DeclarationContainerKoto Container { get; set; } = container;

        internal BindingScope Scope { get; set; } = scope;

        internal SyntaxFormKoto? Premises { get; set; } = premises;

        internal bool Active { get; set; }
    }

    /// <summary>The `copyDeclarations` family's one accessor (DECISIONS BU-220): a kept family, a `Self is Copy` clause's declaration kept in its
    /// clause's module's NodeColumns at its SyntaxId, made once and reset in place by each pass (BindCopyDeclarations' Active). Its members
    /// keep the names its readers use.</summary>
    private readonly struct CopyDeclarationRows(Binding binding)
    {
        internal FamilyRows<CopyDeclaration?> Values => new(binding, static (table, _) => table.Hir.Families.CopyDeclarations.Enumerate());

        internal static ref CopyDeclaration? Slot(Koto clause) => ref HirTables.Of(clause).Families.CopyDeclarations.Ref(clause.SyntaxId);

        internal bool TryGetValue(Koto clause, [NotNullWhen(true)] out CopyDeclaration? declaration)
            => (declaration = clause.SyntaxOwner is { } table ? table.Hir.Families.CopyDeclarations.Get(clause.SyntaxId) : null) is not null;
    }

    /// <summary>The `copyByType` family's one accessor (DECISIONS BU-220): a kept family, a container's Copy declarations kept in its module's
    /// NodeColumns at its SyntaxId, the list made once and refilled by each pass (BindCopyDeclarations). Its members keep the names its
    /// readers use.</summary>
    private readonly struct CopyByTypeRows(Binding binding)
    {
        internal FamilyRows<List<CopyDeclaration>?> Values => new(binding, static (table, _) => table.Hir.Families.CopyByType.Enumerate());

        internal static ref List<CopyDeclaration>? Slot(DeclarationContainerKoto container) => ref HirTables.Of(container).Families.CopyByType.Ref(container.SyntaxId);

        internal bool TryGetValue(DeclarationContainerKoto container, [NotNullWhen(true)] out List<CopyDeclaration>? declarations)
            => (declarations = container.SyntaxOwner is { } table ? table.Hir.Families.CopyByType.Get(container.SyntaxId) : null) is not null;
    }
}
