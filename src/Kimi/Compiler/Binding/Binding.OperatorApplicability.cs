// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns operator legality and the directional expectation it licenses. The original operator, ordered successful operand Types
// and scope enter; a short-lived decision leaves. Only actual queries carry a requirement and proof.
// A failed decision supplies no expectation and keeps its original Operand and judged Terminal; actual proof subjects stay in Requirement.
// User arithmetic selection and destination authority stay separate.
public sealed partial class Binding
{
    private readonly record struct OperatorApplicability(
        Koto? ExpectedOperand, BoundType? Counterpart, BindingFailure Failure, BoundConstraint? Requirement,
        ConstraintProof? Proof, BoundType? Operand, BoundType? Terminal);

    private static KotoKind OperatorKind(Koto operation)
        => operation.Akind is >= KotoKind.PlusEquals and <= KotoKind.GreaterThanGreaterThanEquals
            ? KotoHelper.CompoundOperation(operation.Akind) : operation.Akind;

    private static bool ComparisonOperator(KotoKind kind)
        => kind is >= KotoKind.LessThan and <= KotoKind.GreaterThanEquals or KotoKind.EqualsEquals or KotoKind.ExclamationEquals;

    private static BindingFailure OperatorProofFailure(ConstraintProof proof, BindingFailure refuted) => proof switch
    {
        ConstraintProof.Proven => BindingFailure.None,
        ConstraintProof.Refuted => refuted,
        ConstraintProof.Unknown => BindingFailure.UnprovenConstraint,
        ConstraintProof.Error => BindingFailure.InvalidConstraint,
        _ => BindingFailure.Internal,
    };

    // The string restriction applies equally to built-in and selected user arithmetic; it needs no conformance query.
    private static OperatorApplicability ArithmeticPairApplicability(
        BoundType? left, BoundType? leftCore, BoundType? right, BoundType? rightCore)
    {
        if (ReferenceTypes.EndsInString(leftCore))
        {
            return new(null, null, BindingFailure.NonNumericOperand, null, null, left, leftCore);
        }

        return ReferenceTypes.EndsInString(rightCore)
            ? new(null, null, BindingFailure.NonNumericOperand, null, null, right, rightCore) : default;
    }

    // Only successfully bound operands enter. A failed fit is not a Type fact, even if syntax retains an old Type slot.
    private OperatorApplicability OperatorApplicabilityOf(Koto operation, BoundType? left, BoundType? right, BindingScope scope)
    {
        var kind = OperatorKind(operation);
        var operand = left;
        var source = operation is BinaryKoto binarySource ? binarySource.Left : operation;
        Koto? fittedOperand = null;
        if (ReferenceEquals(left, BoundType.Never) && operation is BinaryKoto fitting &&
            (ComparisonOperator(fitting.Akind) || fitting.Akind is KotoKind.Asterisk or >= KotoKind.Slash and <= KotoKind.Minus or
                >= KotoKind.Ampersand and <= KotoKind.Bar))
        {
            if (right is null)
            {
                return default;
            }

            if (!ReferenceEquals(right, BoundType.Never))
            {
                left = operand = right;
                source = fitting.Right;
                fittedOperand = fitting.Left;
            }
        }

        if (operation is BinaryKoto arithmetic && kind is KotoKind.Asterisk or >= KotoKind.Slash and <= KotoKind.Minus)
        {
            var leftCore = left is null ? null : this.ArithmeticTerminal(source, left);
            var rightCore = right is null ? null : this.ArithmeticTerminal(arithmetic.Right, right);
            var restriction = ArithmeticPairApplicability(operand, leftCore, right, rightCore);
            if (restriction.Failure != BindingFailure.None)
            {
                return restriction;
            }

            left = leftCore;
        }

        if (left is null)
        {
            return operation is BinaryKoto binary && right is not null
                ? this.RightOperatorApplicability(binary, right, scope) : default;
        }

        if (ComparisonOperator(kind) && operation is BinaryKoto)
        {
            left = this.ArithmeticTerminal(source, left);
        }

        var decision = this.LeftOperatorApplicability(left, kind, scope);
        if (decision.Failure != BindingFailure.None)
        {
            return decision with { Operand = operand };
        }

        if (operation is not BinaryKoto pair)
        {
            return decision;
        }

        if (left.IsNumeric && right is not null && kind is KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash)
        {
            var counterpart = this.NumericApplicability(right, scope);
            if (counterpart.Failure != BindingFailure.None)
            {
                return counterpart;
            }
        }

        if (fittedOperand is not null)
        {
            return decision with { ExpectedOperand = fittedOperand, Counterpart = left };
        }

        var expected = left;
        if (ComparisonOperator(kind))
        {
            if (expected.CarriesAnyOrigin && !IsUnfittedLiteral(pair.Right))
            {
                expected = null;
            }
        }
        else if (ReferenceTypes.IsPointer(left) && kind is KotoKind.Plus or KotoKind.Minus)
        {
            expected = BoundType.ISize;
        }
        else if (kind is KotoKind.And or KotoKind.Or)
        {
            expected = BoundType.Boolean;
        }

        if ((ReferenceEquals(left, BoundType.Never) && kind is not (KotoKind.And or KotoKind.Or)) ||
            kind is KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan)
        {
            expected = null;
        }

        return decision with { ExpectedOperand = expected is null ? null : pair.Right, Counterpart = expected };
    }

    // A right operand cannot rule out an unknown left provider; the pair restriction has already been judged.
    private OperatorApplicability RightOperatorApplicability(BinaryKoto binary, BoundType right, BindingScope scope)
    {
        var kind = OperatorKind(binary);
        var arithmetic = kind is KotoKind.Asterisk or >= KotoKind.Slash and <= KotoKind.Minus;
        if (ReferenceEquals(right, BoundType.Never) || !IsUnfittedLiteral(binary.Left) ||
            kind is KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan)
        {
            return default;
        }

        var operand = right;
        if (ComparisonOperator(kind))
        {
            right = this.ArithmeticTerminal(binary.Right, right);
        }

        var decision = this.LeftOperatorApplicability(right, kind, scope);
        if (arithmetic && !right.IsNumeric && !right.HasIntegerArithmetic && decision.Proof != ConstraintProof.Proven)
        {
            return default;
        }

        if (decision.Failure != BindingFailure.None)
        {
            return decision with { Operand = operand };
        }

        return decision with { ExpectedOperand = binary.Left, Counterpart = right };
    }

    private OperatorApplicability LeftOperatorApplicability(BoundType type, KotoKind kind, BindingScope scope)
    {
        var terminal = type;
        OperatorApplicability decision;
        if (ComparisonOperator(kind))
        {
            terminal = ComparisonReferent(type);
            decision = this.ComparisonApplicability(terminal, kind, scope);
        }
        else
        {
            decision = kind switch
            {
                KotoKind.Percent or KotoKind.Ampersand or KotoKind.Bar or KotoKind.Caret
                    => this.IntegerApplicability(type, scope, BindingFailure.NonIntegerOperand),
                KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan
                    => this.IntegerApplicability(type, scope, BindingFailure.NonIntegerOperand),
                KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement
                    => this.IntegerApplicability(type, scope, BindingFailure.NonIntegerOperand),
                KotoKind.PrefixMinus => this.NegationApplicability(type, scope),
                KotoKind.PrefixPlus => type.IsNumeric ? default : this.IntegerApplicability(type, scope, BindingFailure.OperatorNotDefined),
                KotoKind.Plus or KotoKind.Minus when ReferenceTypes.IsPointer(type) => default,
                KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash => this.NumericApplicability(type, scope),
                _ => default,
            };
        }

        return decision.Failure == BindingFailure.None ? decision : decision with { Operand = type, Terminal = terminal };
    }

    private OperatorApplicability IntegerApplicability(BoundType type, BindingScope scope, BindingFailure failure)
    {
        if (type.HasIntegerArithmetic)
        {
            return default;
        }

        var subject = type is { Kind: BoundTypeKind.Constructed, Components.Count: 1 } &&
            type.Symbol?.LibraryDeclaration == KimiDeclarationId.Wrapping ? type.Components[0] : type;
        if (subject.Kind is BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection)
        {
            return this.OperatorRequirement(subject, this.Library.PrimitiveInteger, scope, failure);
        }

        return new(null, null, NonNumericOperand(type) ? BindingFailure.NonNumericOperand : failure, null, null, type, type);
    }

    private OperatorApplicability ShiftCountApplicability(BoundType type, BindingScope scope)
    {
        if (type.IsInteger || ReferenceEquals(type, BoundType.Never))
        {
            return default;
        }

        return type.Kind is BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection
            ? this.OperatorRequirement(type, this.Library.PrimitiveInteger, scope, BindingFailure.InvalidShiftCount)
            : new(null, null, BindingFailure.InvalidShiftCount, null, null, type, type);
    }

    private OperatorApplicability NumericApplicability(BoundType type, BindingScope scope)
    {
        if (type.IsNumeric || ReferenceEquals(type, BoundType.Never))
        {
            return default;
        }

        if (NonNumericOperand(type))
        {
            return new(null, null, BindingFailure.NonNumericOperand, null, null, type, type);
        }

        if (ReferenceTypes.IsPointer(type))
        {
            return new(null, null, BindingFailure.OperatorNotDefined, null, null, type, type);
        }

        // A successful PrimitiveInteger query selects the built-in path; a non-integer arithmetic provider retains selection.
        var integer = this.IntegerApplicability(type, scope, BindingFailure.NonIntegerOperand);
        return integer.Proof == ConstraintProof.Proven ? integer : default;
    }

    private OperatorApplicability NegationApplicability(BoundType type, BindingScope scope)
    {
        if (type.IsNumeric)
        {
            return type.IsUnsignedInteger ? new(null, null, BindingFailure.OperatorNotDefined, null, null, type, type) : default;
        }

        if (NonNumericOperand(type))
        {
            return new(null, null, BindingFailure.NonNumericOperand, null, null, type, type);
        }

        if (type.Symbol?.LibraryDeclaration == KimiDeclarationId.Wrapping)
        {
            return this.IntegerApplicability(type, scope, BindingFailure.NonIntegerOperand);
        }

        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection)
        {
            return this.OperatorRequirement(type, this.Library.GetSymbol(KimiDeclarationId.Negatable)!, scope, BindingFailure.UnsatisfiedConstraint);
        }

        return new(null, null, BindingFailure.OperatorNotDefined, null, null, type, type);
    }

    private OperatorApplicability ComparisonApplicability(BoundType type, KotoKind kind, BindingScope scope)
    {
        var equality = kind is KotoKind.EqualsEquals or KotoKind.ExclamationEquals;
        var ordered = type.IsNumeric || ReferenceEquals(type, BoundType.Char) || ReferenceTypes.EndsInString(type);
        if (ReferenceEquals(type, BoundType.Never) || ordered)
        {
            return default;
        }

        if (type.Kind == BoundTypeKind.Primitive || ReferenceTypes.IsPointer(type))
        {
            return equality ? default : new(null, null, BindingFailure.OperatorNotDefined, null, null, type, type);
        }

        var integer = this.IntegerApplicability(type, scope, BindingFailure.NonIntegerOperand);
        if (integer.Proof == ConstraintProof.Proven)
        {
            return integer;
        }

        var contract = this.Library.GetSymbol(equality ? KimiDeclarationId.Equatable : KimiDeclarationId.Comparable)!;
        if (!ComparisonTypes.IsComposite(type))
        {
            return this.OperatorRequirement(type, contract, scope, BindingFailure.UnsatisfiedConstraint);
        }

        var proof = this.ComparisonProof(type, contract, scope, operators: true, out var witness);
        return new(
            null,
            null,
            OperatorProofFailure(proof, BindingFailure.UnsatisfiedConstraint),
            witness,
            proof,
            proof == ConstraintProof.Proven ? null : type,
            proof == ConstraintProof.Proven ? null : type);
    }

    private OperatorApplicability OperatorRequirement(BoundType subject, BindingSymbol contract, BindingScope scope, BindingFailure refuted)
    {
        var requirement = this.InternConstraint(new(ConstraintKind.Contract, subject, contract: contract));
        var proof = this.ProveConstraint(requirement, scope);
        return new(
            null,
            null,
            OperatorProofFailure(proof, refuted),
            requirement,
            proof,
            proof == ConstraintProof.Proven ? null : subject,
            proof == ConstraintProof.Proven ? null : subject);
    }
}
