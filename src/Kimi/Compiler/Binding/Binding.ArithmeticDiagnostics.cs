// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private Dictionary<Koto, ArithmeticSelectionFailure>? arithmeticSelectionFailures;
    private Dictionary<Koto, ArithmeticDirectionFailure>? arithmeticDirectionFailures;

    private readonly record struct ArithmeticSelectionFailure(BoundArithmetic Plan, BoundType Self, BoundType? Counterpart, KimiDeclarationId Id, string Condition);

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

    private static Koto ArithmeticFactUse(BoundConstraint fact, BindingScope scope)
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

        static Koto? ContractUse(Koto node, BindingSymbol? contract)
            => ReferenceEquals(node.BoundSymbol, contract) ? node
            : node is AndKoto conjunction ? ContractUse(conjunction.Left, contract) ?? ContractUse(conjunction.Right, contract)
            : node is ParenthesizedKoto parentheses ? ContractUse(parentheses.Operand, contract) : null;
    }

    private bool ValidateArithmeticDirections(BindingScope scope, ConstraintEnvironment environment)
    {
        foreach (var fact in environment.Facts)
        {
            if (fact.Kind != ConstraintKind.Contract || fact.Subject is not { } subject || fact.Contract is not { Type.Components: [var counterpart] } contract ||
                ArithmeticContracts.Identity(contract) is not { } id || id == KimiDeclarationId.Negatable || !this.AvailableConstraintFact(environment, fact))
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
                        ArithmeticContracts.Identity(opposite) == oppositeId && AssociatedIdentityMatches(oppositeFact.Subject, counterpart) && AssociatedIdentityMatches(oppositeCounterpart, subject) && this.AvailableConstraintFact(other, oppositeFact))
                    {
                        var here = ArithmeticFactUse(fact, scope);
                        var there = ArithmeticFactUse(oppositeFact, current);
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

        use.Report(requirement, DiagnosticCode.ArithmeticSelection_Kd, ArithmeticOperatorText(ArithmeticContracts.Operator(failure.Id)), this.Library.GetSymbol(failure.Id)!.Name, evidence: [DiagnosticTypeName(failure.Self), failure.Counterpart is { } counterpart ? DiagnosticTypeName(counterpart) : "not independently typed", failure.Condition], related: related);
    }
}
