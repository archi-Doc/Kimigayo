// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 13.3.3's eligibility conditions of an explicitly declared conformance, in check order; the adapter explains each (DECISIONS
    // BU-111).
    internal enum ArithmeticConformanceCause : byte
    {
        NotUserProvider,
        LhsNotNumeric,
        RhsNotOwner,
    }

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
            return Fail(null, ArithmeticConformanceCause.NotUserProvider);
        }

        if (id == KimiDeclarationId.Negatable)
        {
            return true;
        }

        var counterpart = this.ContractType(path.Contract.Type!.Components[0], path.Scope);
        if (KimiLibraryCatalog.IsLeftContract(id))
        {
            return this.BuiltinNumeric(counterpart, path.Scope, path.Use) || Fail(counterpart, ArithmeticConformanceCause.LhsNotNumeric);
        }

        return (this.ResultSemantics(counterpart, path.Scope, path.Use) == SemanticsMask.Owner && this.NotString(counterpart, path.Scope, path.Use)) ||
            Fail(counterpart, ArithmeticConformanceCause.RhsNotOwner);

        // The cause (Reason), the arithmetic Contract's declaration (Related, role 0) and, for a counterpart, the Contract instance the
        // conformance states (Instance at that declaration, the Contract applied to the counterpart; ReportArithmeticConformance).
        bool Fail(BoundType? type, ArithmeticConformanceCause cause)
        {
            var at = SourceRef.Of(path.Use);
            var related = this.FactSite(path.Contract.Declaration);
            ReadOnlySpan<ProblemFact> facts =
            [
                new(ProblemFactKind.Reason, at, (long)cause),
                new(ProblemFactKind.Related, related, 0),
                type is null ? default : new(ProblemFactKind.Instance, related, this.report.Keep(this.ArithmeticReference(id, type).Type!)),
            ];
            this.Record(path.Use, BindingFailure.ArithmeticConformance, type is null ? facts[..2] : facts);
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
}
