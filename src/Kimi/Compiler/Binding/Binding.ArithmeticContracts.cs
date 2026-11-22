// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly ArithmeticConformanceFailureRows arithmeticConformanceFailures;

    // SPEC 13.3.3's eligibility conditions of an explicitly declared conformance, in check order; the adapter explains each (DECISIONS
    // BU-111).
    internal enum ArithmeticConformanceCause : byte
    {
        NotUserProvider,
        LhsNotNumeric,
        RhsNotOwner,
    }

    internal readonly record struct ArithmeticConformanceFailure(BoundType Type, BindingSymbol Contract, ArithmeticConformanceCause Cause);

    // Outer owner is independent of Owned: an ordinary user struct may retain external borrows.
    private static bool UserArithmeticProvider(BoundType type)
        => type.Semantics == SemanticsKind.Owner && type.Kind is BoundTypeKind.Nominal or BoundTypeKind.Constructed &&
            type.Symbol is { Declaration: StructKoto or EnumKoto, LibraryDeclaration: not (KimiDeclarationId.Wrapping or KimiDeclarationId.Loan) };

    private bool BuiltinNumeric(BoundType type, BindingScope scope, QuerySite site)
        => type.IsNumeric || this.IsGenericInteger(type, scope, site) || this.IsGenericWrapping(type, scope, site);

    private bool ValidArithmeticConformance(BoundConformancePath path, BoundType self)
    {
        if (ArithmeticContractSyntax.Identity(path.Contract) is not { } id)
        {
            return true;
        }

        if (!UserArithmeticProvider(self))
        {
            return Fail(self, ArithmeticConformanceCause.NotUserProvider);
        }

        if (id == KimiDeclarationId.Negatable)
        {
            return true;
        }

        var counterpart = this.ContractType(path.Contract.Type!.Components[0], path.Scope);
        if (ArithmeticContracts.IsLeft(id))
        {
            return this.BuiltinNumeric(counterpart, path.Scope, path.Use) || Fail(counterpart, ArithmeticConformanceCause.LhsNotNumeric);
        }

        return (this.ResultSemantics(counterpart, path.Scope, path.Use) == SemanticsMask.Owner && this.NotString(counterpart, path.Scope, path.Use)) ||
            Fail(counterpart, ArithmeticConformanceCause.RhsNotOwner);

        bool Fail(BoundType type, ArithmeticConformanceCause cause)
        {
            this.FailExplained(ArithmeticConformanceFailureRows.Family, path.Use, BindingFailure.ArithmeticConformance, new(type, path.Contract, cause));
            return false;
        }
    }

    private bool NotString(BoundType type, BindingScope scope, QuerySite site)
        => !type.IsAbstract ? !ReferenceEquals(type, BoundType.String)
            : this.BuiltinNumeric(type, scope, site) || this.ProveConstraint(this.NotStringConstraint(type), scope, site) == ConstraintProof.Proven;

    private BoundConstraint NotStringConstraint(BoundType type)
        => this.NegateConstraint(this.InternConstraint(new(ConstraintKind.TypeIdentity, type, BoundType.String)));

    // Publish eligibility with the same provenance as other Contract-derived facts. Self conformance declarations
    // are obligations, never environment assumptions, so they cannot establish their own counterpart eligibility.
    private void AddArithmeticPremises(BoundContract declaration, BoundType self, ConstraintEnvironment environment, BindingSymbol source)
    {
        if (ArithmeticContractSyntax.Identity(declaration.Symbol) is not { } id)
        {
            return;
        }

        this.AddConstraintFact(environment, this.InternConstraint(new(ConstraintKind.Semantics, self, mask: SemanticsMask.Owner)), source);
        this.AddConstraintFact(environment, this.NotStringConstraint(self), source);
        if (id != KimiDeclarationId.Negatable && declaration.Symbol.Type is { Components: [var counterpart] })
        {
            this.AddConstraintFact(environment, this.InternConstraint(new(ConstraintKind.Semantics, counterpart, mask: SemanticsMask.Owner)), source);
            this.AddConstraintFact(environment, this.NotStringConstraint(counterpart), source);
        }
    }

    /// <summary>The `arithmeticConformanceFailures` family's one accessor (DECISIONS BU-220): a pass family, the eligibility condition an explicitly
    /// declared arithmetic conformance fails (SPEC 13.3.3), stored with its failure by the recorder (RecordedFact) and written by FailExplained
    /// (a present row always has its Type). Its members keep the names its readers use.</summary>
    private readonly struct ArithmeticConformanceFailureRows(Binding binding)
    {
        // The family's column in a NodeColumns, the store the recorder decides (RecordedFact, SetRecordedFact).
        internal static ref StampedColumn<ArithmeticConformanceFailure, PassCounter> Family(ref NodeColumns columns) => ref columns.ArithmeticConformanceFailures;

        internal bool TryGetValue(Koto use, out ArithmeticConformanceFailure failure) => (failure = binding.RecordedFact(use, Family)).Type is not null;
    }
}
