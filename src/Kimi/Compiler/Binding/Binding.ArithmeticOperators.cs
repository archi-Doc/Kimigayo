// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static KimiDeclarationId ArithmeticContract(KotoKind operation, bool left = false)
    {
        var id = operation switch
        {
            KotoKind.Plus => KimiDeclarationId.Addable,
            KotoKind.Minus => KimiDeclarationId.Subtractable,
            KotoKind.Asterisk => KimiDeclarationId.Multipliable,
            KotoKind.Slash => KimiDeclarationId.Dividable,
            KotoKind.Percent => KimiDeclarationId.RemainderProvider,
            _ => KimiDeclarationId.Negatable,
        };
        return left ? id + (KimiDeclarationId.LeftAddable - KimiDeclarationId.Addable) : id;
    }

    private BoundType ArithmeticTerminal(Koto operand, BoundType type)
        => ComparisonReferent(this.ComparisonThroughPairs(operand, type));

    private bool HasArithmeticPremise(BoundType type, KimiDeclarationId id, BindingScope scope)
        => this.Library.GetSymbol(id) is { } contract && this.HasContractFact(type, contract, scope);

    private bool ArithmeticNumericDomain(BoundType type, BindingScope scope)
    {
        if (this.BuiltinNumeric(type, scope))
        {
            return true;
        }

        // A Left premise publishes numeric eligibility without enumerating concrete numeric Types.
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { Invalid: false } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (fact.Kind == ConstraintKind.Contract && fact.Contract is { Type.Components: [var counterpart] } contract &&
                    ArithmeticContracts.Identity(contract) is { } id && ArithmeticContracts.IsLeft(id) && AssociatedIdentityMatches(type, counterpart) && this.AvailableConstraintFact(environment, fact))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Type independent expressions once. Candidate trials inspect only their structure or literal syntax.
    private bool TryArithmetic(BinaryKoto binary, BindingScope scope, out BoundType? result)
    {
        result = null;
        var operation = binary.Akind;
        if (operation is not (KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash or KotoKind.Percent) || IsLiteralOnlyOperation(binary))
        {
            return false;
        }

        var leftLiteral = IsUnfittedLiteral(binary.Left);
        var rightLiteral = IsUnfittedLiteral(binary.Right);
        var left = leftLiteral ? null : this.BindIndependentArgument(binary.Left, scope);
        var leftCore = left is null ? null : this.ArithmeticTerminal(binary.Left, left);
        var ordinary = leftCore is not null && (UserArithmeticProvider(leftCore) || this.HasArithmeticPremise(leftCore, ArithmeticContract(operation), scope));
        // A fixed left provider can give the other operand a bounded expected Type after selection.
        var right = rightLiteral ? null : this.BindIndependentArgument(binary.Right, scope);
        var rightCore = right is null ? null : this.ArithmeticTerminal(binary.Right, right);
        var reverse = ((leftLiteral && NumericLiteralDefault(binary.Left) is not null) || (leftCore is not null && this.ArithmeticNumericDomain(leftCore, scope))) &&
            rightCore is not null && (UserArithmeticProvider(rightCore) || this.HasArithmeticPremise(rightCore, ArithmeticContract(operation, true), scope));
        if (!ordinary && !reverse)
        {
            if (leftCore is not null && rightCore is not null && UserArithmeticProvider(rightCore) && !ReferenceTypes.EndsInString(leftCore) && !ReferenceTypes.IsPointer(leftCore))
            {
                var rejected = binary.ArithmeticStorage ??= new();
                rejected.Candidates.Clear();
                rejected.Sources.Clear();
                this.FailExplained(ref this.arithmeticSelectionFailures, binary, BindingFailure.ArithmeticSelection, new(rejected, rightCore, leftCore, ArithmeticContract(operation, true), "A right provider requires a built-in numeric left operand"));
                return true;
            }

            return false;
        }

        if (ReferenceTypes.EndsInString(leftCore) || ReferenceTypes.EndsInString(rightCore))
        {
            this.FailOperand(binary, ReferenceTypes.EndsInString(leftCore) ? leftCore! : rightCore!, BindingFailure.NonNumericOperand);
            return true;
        }

        // The whole built-in operation must be proven, rather than just one numeric operand.
        if (leftCore is not null && ReferenceEquals(leftCore, rightCore) && this.BuiltinNumeric(leftCore, scope))
        {
            return false;
        }

        var plan = binary.ArithmeticStorage ??= new();
        plan.Active = false;
        var self = ordinary ? leftCore! : rightCore!;
        var other = ordinary ? binary.Right : binary.Left;
        var counterpart = ordinary ? rightCore : leftCore;
        var id = ArithmeticContract(operation, !ordinary);
        var selected = this.SelectArithmetic(binary, plan, self, other, counterpart, id, scope);
        if (selected is null)
        {
            return true;
        }

        var requirement = selected.Contract!.MembersByName[ArithmeticContracts.Method(id)][0];
        plan.Call = this.BindSelectedRequirement(binary, plan.Call, [binary.Left, binary.Right], self, selected, requirement, scope);
        plan.Active = plan.Call.BoundCall is not null;
        result = Complete(binary, plan.Active ? plan.Call.BoundType : null);
        return true;
    }

    private bool TryArithmetic(UnaryKoto unary, BindingScope scope, out BoundType? result)
    {
        result = null;
        if (unary.Akind != KotoKind.PrefixMinus || NumericLiteralDefault(unary) is not null)
        {
            return false;
        }

        var operand = this.BindIndependentArgument(unary.Operand, scope);
        if (operand is null)
        {
            return false;
        }

        var self = this.ArithmeticTerminal(unary.Operand, operand);
        if ((self.IsNumeric || this.IsGenericWrapping(self, scope)) || !(UserArithmeticProvider(self) || this.HasArithmeticPremise(self, KimiDeclarationId.Negatable, scope)))
        {
            return false;
        }

        var plan = unary.ArithmeticStorage ??= new();
        plan.Active = false;
        var selected = this.SelectArithmetic(unary, plan, self, null, null, KimiDeclarationId.Negatable, scope);
        if (selected is null)
        {
            return true;
        }

        var requirement = selected.Contract!.MembersByName["negated"][0];
        plan.Call = this.BindSelectedRequirement(unary, plan.Call, [unary.Operand], self, selected, requirement, scope);
        plan.Active = plan.Call.BoundCall is not null;
        result = Complete(unary, plan.Active ? plan.Call.BoundType : null);
        return true;
    }

    private BindingSymbol? SelectArithmetic(Koto use, BoundArithmetic plan, BoundType self, Koto? other, BoundType? counterpart, KimiDeclarationId id, BindingScope scope)
    {
        plan.Candidates.Clear();
        plan.Sources.Clear();
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { Invalid: false } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (fact.Kind == ConstraintKind.Contract && AssociatedIdentityMatches(fact.Subject, self) && fact.Contract is { } contract && this.AvailableConstraintFact(environment, fact))
                {
                    Add(this.AppliedAssociatedContract(fact, self), ArithmeticFactUse(fact, current));
                }
            }
        }

        if (self.Symbol is { } owner && this.conformancesByType.TryGetValue(owner, out var identities))
        {
            for (var i = 0; i < identities.Count; i++)
            {
                if (identities[i].Contract.Type is { } reference && this.StoredType(reference, self) is { } bound)
                {
                    Add(this.BoundContractReference(bound), identities[i].DirectClause ?? (Koto)identities[i].Paths[0].Declaration);
                }
            }
        }

        BindingSymbol? selected = null;
        var count = 0;
        var pending = false;
        for (var i = 0; i < plan.Candidates.Count; i++)
        {
            var candidate = plan.Candidates[i];
            if (other is not null)
            {
                var expected = candidate.Type!.Components[0];
                if (IsUnfittedLiteral(other) ? !this.FitsInputLiteral(other, expected, scope) :
                    counterpart is not null ? !FitsStructuralPart(counterpart, expected) : !IsWaitingNestedCall(other) && !IsWaitingCallable(other))
                {
                    continue;
                }
            }

            var proof = this.ProveConformance(self, candidate, scope);
            if (proof == ConstraintProof.Error)
            {
                this.Fail(use, BindingFailure.InvalidConstraint);
                return null;
            }

            pending |= proof == ConstraintProof.Unknown;
            if (proof == ConstraintProof.Proven)
            {
                selected = candidate;
                count++;
            }
        }

        if (pending || count != 1)
        {
            this.FailExplained(ref this.arithmeticSelectionFailures, use, BindingFailure.ArithmeticSelection, new(plan, self, counterpart, id, pending ? "A structurally fitting conditional conformance remains unproven" : count == 0 ? "No proven conformance fits the counterpart structure" : "Multiple proven conformances fit the counterpart structure"));
            return null;
        }

        return selected;

        void Add(BindingSymbol reference, Koto source)
        {
            if (ArithmeticContracts.Identity(reference) == id && !plan.Candidates.Contains(reference))
            {
                plan.Candidates.Add(reference);
                plan.Sources.Add(source);
            }

            if (reference.Contract is { } shape)
            {
                for (var i = 0; i < shape.Ancestors.Count; i++)
                {
                    if (ArithmeticContracts.Identity(shape.Ancestors[i]) == id && !plan.Candidates.Contains(shape.Ancestors[i]))
                    {
                        plan.Candidates.Add(shape.Ancestors[i]);
                        plan.Sources.Add(source);
                    }
                }
            }
        }
    }
}
