// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private Dictionary<Koto, ArithmeticSelectionFailure>? arithmeticSelectionFailures;
    private Dictionary<Koto, ArithmeticDirectionFailure>? arithmeticDirectionFailures;

    // SPEC 13.3.4: why the fixed provider route selects no unique requirement, in check order; the adapter explains each (DECISIONS
    // BU-111).
    private enum ArithmeticSelectionCause : byte
    {
        NonNumericLeft,
        Unproven,
        NoFit,
        Ambiguous,
    }

    private readonly record struct ArithmeticSelectionFailure(BoundArithmetic Plan, BoundType Self, BoundType? Counterpart, KimiDeclarationId Id, ArithmeticSelectionCause Cause);

    private readonly record struct ArithmeticDirectionFailure(BoundType Left, BoundType Right, KimiDeclarationId Id, BindingSymbol Ordinary, BindingSymbol Reverse, Koto OrdinaryUse, Koto ReverseUse);

    private static string ArithmeticOperatorText(KotoKind operation) => operation switch
    {
        KotoKind.Plus => "+",
        KotoKind.Minus or KotoKind.PrefixMinus => "-",
        KotoKind.Asterisk => "*",
        KotoKind.Slash => "/",
        KotoKind.Percent => "%",
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    // SPEC 13.3.3: an explicitly declared arithmetic conformance whose Self or counterpart is not eligible, at the conformance; the
    // Contract declaration is related.
    private static void ReportArithmeticConformance(Koto use, DiagnosticRequirement requirement, DiagnosticCode code, ArithmeticConformanceFailure failure)
    {
        var note = failure.Cause switch
        {
            ArithmeticConformanceCause.NotUserProvider => "Self must be an ordinary outer-owner struct or enum, not a built-in or compiler-closed Type",
            ArithmeticConformanceCause.LhsNotNumeric => "Every binding must give Lhs a built-in numeric Type",
            ArithmeticConformanceCause.RhsNotOwner => "Every binding must give Rhs outer owner Semantics, excluding string",
            _ => throw new UnreachableException(),
        };
        use.Report(requirement, code, DiagnosticTypeName(failure.Type), failure.Contract.Name, note: note, evidence: [failure.Cause], related: [("contract", failure.Contract.Declaration, "arithmetic Contract declaration")]);
    }

    private Koto ArithmeticFactUse(BoundConstraint fact, BindingScope scope)
    {
        var clauses = scope.Owner is FunctionKoto function ? function.TypeConstraints : scope.Owner is DeclarationContainerKoto container ? container.ConstraintNodes : [];
        for (var i = 0; i < clauses.Count; i++)
        {
            if (clauses[i] is IsKoto clause && Contains(clause.BoundConstraint, fact))
            {
                return ContractUse(clause.Right, fact.Contract) ?? clause;
            }
        }

        return scope.Owner;

        static bool Contains(BoundConstraint? root, BoundConstraint fact)
            => ReferenceEquals(root, fact) || (root?.Kind == ConstraintKind.And && (Contains(root.Left, fact) || Contains(root.Right, fact)));

        // The requirement naming the premise's bound Contract reference (ContractReferenceOf: its Type, else its selection).
        Koto? ContractUse(Koto node, BindingSymbol? contract)
            => ReferenceEquals(this.ContractReferenceOf(node), contract) ? node
            : node is BinaryKoto { Akind: KotoKind.And } conjunction ? ContractUse(conjunction.Left, contract) ?? ContractUse(conjunction.Right, contract)
            : node is ParenthesizedKoto parentheses ? ContractUse(parentheses.Operand, contract) : null;
    }

    private bool ValidateArithmeticDirections(BindingScope scope, ConstraintEnvironment environment)
    {
        foreach (var fact in environment.Facts)
        {
            if (fact.Kind != ConstraintKind.Contract || fact.Subject is not { } subject || fact.Contract is not { Type.Components: [var counterpart] } contract ||
                ArithmeticContractSyntax.Identity(contract) is not { } id || id == KimiDeclarationId.Negatable ||
                !this.AvailableConstraintFact(environment, fact, out _))
            {
                continue;
            }

            var reversed = ArithmeticContracts.IsLeft(id);
            var oppositeId = reversed ? id - (KimiDeclarationId.LeftAddable - KimiDeclarationId.Addable) : id + (KimiDeclarationId.LeftAddable - KimiDeclarationId.Addable);
            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Constraints is not { Invalid: false } other)
                {
                    continue;
                }

                foreach (var oppositeFact in other.Facts)
                {
                    if (oppositeFact.Kind == ConstraintKind.Contract && oppositeFact.Contract is { Type.Components: [var oppositeCounterpart] } opposite &&
                        ArithmeticContractSyntax.Identity(opposite) == oppositeId && AssociatedIdentityMatches(oppositeFact.Subject, counterpart) &&
                        AssociatedIdentityMatches(oppositeCounterpart, subject) && this.AvailableConstraintFact(other, oppositeFact, out _))
                    {
                        var here = this.ArithmeticFactUse(fact, scope);
                        var there = this.ArithmeticFactUse(oppositeFact, current);
                        this.FailExplained(ref this.arithmeticDirectionFailures, scope.Owner, BindingFailure.ArithmeticDirection, reversed ? new(counterpart, subject, oppositeId, opposite, contract, there, here) : new(subject, counterpart, id, contract, opposite, here, there));
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private void ReportArithmeticSelection(Koto use, DiagnosticRequirement requirement, ArithmeticSelectionFailure failure)
    {
        var related = new (string Role, Koto Node, string? Message)[failure.Plan.Candidates.Count];
        for (var i = 0; i < related.Length; i++)
        {
            related[i] = ("conformance", failure.Plan.Sources[i], DiagnosticTypeName(failure.Plan.Candidates[i].Type!));
        }

        var counterpartText = failure.Counterpart is { } counterpart ? DiagnosticTypeName(counterpart)
            : use is UnaryKoto ? "none (unary operation)" : "not independently typed";
        var note = failure.Cause switch
        {
            ArithmeticSelectionCause.NonNumericLeft => "A right provider requires a built-in numeric left operand",
            ArithmeticSelectionCause.Unproven => "A structurally fitting conditional conformance remains unproven",
            ArithmeticSelectionCause.NoFit => "No proven conformance fits the counterpart structure",
            ArithmeticSelectionCause.Ambiguous => "Multiple proven conformances fit the counterpart structure",
            _ => throw new UnreachableException(),
        };
        use.Report(
            requirement,
            DiagnosticCode.ArithmeticSelection_Kd,
            ArithmeticOperatorText(ArithmeticContractSyntax.Operator(failure.Id)),
            this.Library.GetSymbol(failure.Id)!.Name,
            note: note,
            evidence: [DiagnosticTypeName(failure.Self), counterpartText, failure.Cause],
            related: related);
    }
}
