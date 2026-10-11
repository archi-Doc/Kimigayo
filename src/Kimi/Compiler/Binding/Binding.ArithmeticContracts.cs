// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private Dictionary<Koto, ArithmeticConformanceFailure>? arithmeticConformanceFailures;

    private readonly record struct ArithmeticConformanceFailure(BoundType Type, BindingSymbol Contract, string Condition);

    // Outer owner is independent of Owned: an ordinary user struct may retain external borrows.
    private static bool UserArithmeticProvider(BoundType type)
        => type.Semantics == SemanticsKind.Owner && type.Kind is BoundTypeKind.Nominal or BoundTypeKind.Constructed &&
            type.Symbol is { Declaration: StructKoto or EnumKoto, LibraryDeclaration: not (KimiDeclarationId.Wrapping or KimiDeclarationId.Loan) };

    private bool BuiltinNumeric(BoundType type, BindingScope scope)
        => type.IsNumeric || this.IsGenericInteger(type, scope) || this.IsGenericWrapping(type, scope);

    private bool ValidArithmeticConformance(BoundConformancePath path, BoundType self)
    {
        if (ArithmeticContracts.Identity(path.Contract) is not { } id)
        {
            return true;
        }

        if (!UserArithmeticProvider(self))
        {
            return Fail(self, "Self must be an ordinary outer-owner struct or enum, not a built-in or compiler-closed Type");
        }

        if (id == KimiDeclarationId.Negatable)
        {
            return true;
        }

        var counterpart = this.ContractType(path.Contract.Type!.Components[0], path.Scope);
        if (ArithmeticContracts.IsLeft(id))
        {
            return this.BuiltinNumeric(counterpart, path.Scope) || Fail(counterpart, "Every binding must give Lhs a built-in numeric Type");
        }

        return (this.ResultSemantics(counterpart, path.Scope) == SemanticsMask.Owner && this.NotString(counterpart, path.Scope)) ||
            Fail(counterpart, "Every binding must give Rhs outer owner Semantics, excluding string");

        bool Fail(BoundType type, string condition)
        {
            this.FailExplained(ref this.arithmeticConformanceFailures, path.Use, BindingFailure.ArithmeticConformance, new(type, path.Contract, condition));
            return false;
        }
    }

    private bool NotString(BoundType type, BindingScope scope)
        => !type.IsAbstract ? !ReferenceEquals(type, BoundType.String)
            : this.BuiltinNumeric(type, scope) || this.ProveConstraint(this.NotStringConstraint(type), scope) == ConstraintProof.Proven;

    private BoundConstraint NotStringConstraint(BoundType type)
        => this.NegateConstraint(this.InternConstraint(new(ConstraintKind.TypeIdentity, type, BoundType.String)));

    // Publish eligibility with the same provenance as other Contract-derived facts. Self conformance declarations
    // are obligations, never environment assumptions, so they cannot establish their own counterpart eligibility.
    private void AddArithmeticPremises(BoundContract declaration, BoundType self, ConstraintEnvironment environment, BindingSymbol source)
    {
        if (ArithmeticContracts.Identity(declaration.Symbol) is not { } id)
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
