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
        ConstraintProof? Proof, BoundType? Operand, BoundType? Terminal)
    {
        // The cause an Error proof left (TakeFailedCause), taken raw at the decision (OperatorRequirement); read only by FailOperator, whose
        // RestOnCause rests the operation's record on it when it is a failed node other than the operation. None for a composite
        // comparison's proof (ComparisonProof).
        internal Koto? Cause { get; init; }
    }

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
        if (ReferenceEquals(ComparisonReferent(leftCore), BoundType.String))
        {
            return new(null, null, BindingFailure.NonNumericOperand, null, null, left, leftCore);
        }

        return ReferenceEquals(ComparisonReferent(rightCore), BoundType.String)
            ? new(null, null, BindingFailure.NonNumericOperand, null, null, right, rightCore) : default;
    }

    // Only successfully bound operands enter. A failed fit is not a Type fact, even if syntax retains an old Type slot.
    private OperatorApplicability OperatorApplicabilityOf(Koto operation, BoundType? left, BoundType? right, BindingScope scope)
    {
        var kind = OperatorKind(operation);
        var operand = left;
        BoundType? rightCore = null;
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
            var leftCore = left is null ? null : this.OperandTerminal(source, left);
            rightCore = right is null ? null : this.OperandTerminal(arithmetic.Right, right);
            var restriction = ArithmeticPairApplicability(operand, leftCore, right, rightCore);
            if (restriction.Failure != BindingFailure.None)
            {
                return restriction;
            }

            // SPEC 13.3.4, 5.3: the raw-pointer row takes the operand's own Type; a raw pointer reached through a layer has no provider.
            if (kind is KotoKind.Plus or KotoKind.Minus && leftCore is { IsPointer: true } && !ReferenceEquals(leftCore, operand))
            {
                return new(null, null, BindingFailure.OperatorNotDefined, null, null, operand, leftCore);
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
            left = this.OperandTerminal(source, left);
        }

        var decision = this.LeftOperatorApplicability(left, kind, scope, operation);
        if (decision.Failure != BindingFailure.None)
        {
            return decision with { Operand = operand };
        }

        if (operation is not BinaryKoto pair)
        {
            return decision;
        }

        // SPEC 13.3.4: the counterpart of a left the table admitted (numeric or a proven integer parameter) is judged at its terminal
        // Type, as the left operand is, and keeps its own Type; a Never counterpart fits the admitted left (3.1.3), and a raw pointer's
        // counterpart is its own row's (isize).
        if (!left.IsPointer && rightCore is not null && !ReferenceEquals(rightCore, BoundType.Never))
        {
            var counterpart = this.NumericApplicability(rightCore, scope, operation);
            if (counterpart.Failure != BindingFailure.None)
            {
                return counterpart with { Operand = right };
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
        else if (left.IsPointer && kind is KotoKind.Plus or KotoKind.Minus)
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
            right = this.OperandTerminal(binary.Right, right);
        }

        var decision = this.LeftOperatorApplicability(right, kind, scope, binary);
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

    private OperatorApplicability LeftOperatorApplicability(BoundType type, KotoKind kind, BindingScope scope, QuerySite site)
    {
        var terminal = type;
        OperatorApplicability decision;
        if (ComparisonOperator(kind))
        {
            terminal = ComparisonReferent(type);
            decision = this.ComparisonApplicability(terminal, kind, scope, site);
        }
        else
        {
            decision = kind switch
            {
                KotoKind.Percent or KotoKind.Ampersand or KotoKind.Bar or KotoKind.Caret
                    => this.IntegerApplicability(type, scope, site, BindingFailure.NonIntegerOperand),
                KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan
                    => this.IntegerApplicability(type, scope, site, BindingFailure.NonIntegerOperand),
                KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement
                    => this.IntegerApplicability(type, scope, site, BindingFailure.NonIntegerOperand),
                KotoKind.PrefixMinus => this.NegationApplicability(type, scope, site),
                KotoKind.Plus or KotoKind.Minus when type.IsPointer => default,
                KotoKind.PrefixPlus or KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash
                    => this.NumericApplicability(type, scope, site),
                _ => default,
            };
        }

        return decision.Failure == BindingFailure.None ? decision : decision with { Operand = type, Terminal = terminal };
    }

    private OperatorApplicability IntegerApplicability(BoundType type, BindingScope scope, QuerySite site, BindingFailure failure)
    {
        if (type.HasIntegerArithmetic)
        {
            return default;
        }

        var subject = type is { Kind: BoundTypeKind.Constructed, Components.Count: 1 } &&
            type.Symbol?.LibraryDeclaration == KimiDeclarationId.Wrapping ? type.Components[0] : type;
        if (subject.IsProofDecided)
        {
            return this.OperatorRequirement(subject, this.Library.PrimitiveInteger, scope, site, failure);
        }

        return new(null, null, NonNumericOperand(type) ? BindingFailure.NonNumericOperand : failure, null, null, type, type);
    }

    private OperatorApplicability ShiftCountApplicability(BoundType type, BindingScope scope, QuerySite site)
    {
        if (type.IsInteger || ReferenceEquals(type, BoundType.Never))
        {
            return default;
        }

        return type.IsProofDecided
            ? this.OperatorRequirement(type, this.Library.PrimitiveInteger, scope, site, BindingFailure.InvalidShiftCount)
            : new(null, null, BindingFailure.InvalidShiftCount, null, null, type, type);
    }

    // SPEC 13.3.3, 13.3.4: any other Type is built-in numeric only as a parameter with proven PrimitiveInteger; else no row applies (a
    // non-numeric primitive or a string NonNumericOperand, an unproven query UnprovenConstraint, a raw pointer, Tuple, fixed array or
    // Never no provider: a Never operand proves no direction, 3.1.3). A user provider's routes are TryArithmetic's, first.
    private OperatorApplicability NumericApplicability(BoundType type, BindingScope scope, QuerySite site)
        => type.IsNumeric ? default : this.IntegerApplicability(type, scope, site, BindingFailure.OperatorNotDefined);

    private OperatorApplicability NegationApplicability(BoundType type, BindingScope scope, QuerySite site)
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
            return this.IntegerApplicability(type, scope, site, BindingFailure.NonIntegerOperand);
        }

        if (type.IsProofDecided)
        {
            return this.OperatorRequirement(type, this.Library.GetSymbol(KimiDeclarationId.Negatable)!, scope, site, BindingFailure.UnsatisfiedConstraint);
        }

        return new(null, null, BindingFailure.OperatorNotDefined, null, null, type, type);
    }

    private OperatorApplicability ComparisonApplicability(BoundType type, KotoKind kind, BindingScope scope, QuerySite site)
    {
        var equality = kind is KotoKind.EqualsEquals or KotoKind.ExclamationEquals;
        var ordered = type.IsNumeric || ReferenceEquals(type, BoundType.Char) || ReferenceEquals(ComparisonReferent(type), BoundType.String);
        if (ReferenceEquals(type, BoundType.Never) || ordered)
        {
            return default;
        }

        if (type.Kind == BoundTypeKind.Primitive || type.IsPointer)
        {
            return equality ? default : new(null, null, BindingFailure.OperatorNotDefined, null, null, type, type);
        }

        var integer = this.IntegerApplicability(type, scope, site, BindingFailure.NonIntegerOperand);
        if (integer.Proof == ConstraintProof.Proven)
        {
            return integer;
        }

        var contract = this.Library.GetSymbol(equality ? KimiDeclarationId.Equatable : KimiDeclarationId.Comparable)!;
        if (!TyCtxt.IsComposite(type))
        {
            return this.OperatorRequirement(type, contract, scope, site, BindingFailure.UnsatisfiedConstraint);
        }

        var proof = this.ComparisonProof(type, contract, scope, site, operators: true, out var witness);
        return new(
            null,
            null,
            OperatorProofFailure(proof, BindingFailure.UnsatisfiedConstraint),
            witness,
            proof,
            proof == ConstraintProof.Proven ? null : type,
            proof == ConstraintProof.Proven ? null : type);
    }

    // The one decision of every single-requirement operator (arithmetic, integer, shift count, negation, a non-composite comparison).
    // SPEC 8.2.3, 23.1.2 (rule W; PLAN G1356, B's ruling 7): an Error proof read a failed premise or Type context whose record is
    // reported, so the decision takes the cause the proof left (TakeFailedCause) at once and carries it (Cause): FailOperator rests the
    // record on it (RestOnCause, RequireClauses' rule, R7-C3 B1) and IsDerived's InvalidConstraint row derives it. A decision its caller
    // discards (ComparisonApplicability's PrimitiveInteger probe) drops the cause it took, so none stays in the channel.
    private OperatorApplicability OperatorRequirement(BoundType subject, BindingSymbol contract, BindingScope scope, QuerySite site, BindingFailure refuted)
    {
        var requirement = this.InternConstraint(new(ConstraintKind.Contract, subject, contract: contract));
        var proof = this.ProveConstraint(requirement, scope, site);
        return new(
            null,
            null,
            OperatorProofFailure(proof, refuted),
            requirement,
            proof,
            proof == ConstraintProof.Proven ? null : subject,
            proof == ConstraintProof.Proven ? null : subject)
        {
            Cause = proof == ConstraintProof.Error ? this.TakeFailedCause().Use : null,
        };
    }
}
