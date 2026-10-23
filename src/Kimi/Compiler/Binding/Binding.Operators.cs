// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns unary and binary operator binding: syntax, scope and expected Type enter; bound Types, adaptations and failures leave.
// Operand binding, destination authority, fitting and operator completion retain their order. Arithmetic selection and
// comparison composition keep their existing owners. This owner neither emits code nor formats diagnostics.
public sealed partial class Binding
{
    // SPEC 13.3.1: a primitive without arithmetic (bool, Unit, char, string) or safe reference layers ending in string. Never fits
    // every operator, and a pointer operand is displaced by SPEC 5.3 before this test.
    private static bool NonNumericOperand(BoundType type)
        => (type.Kind == BoundTypeKind.Primitive && !type.IsNumeric && !ReferenceEquals(type, BoundType.Never)) || ReferenceTypes.EndsInString(type);

    // SPEC 13.2, 13.3.1: a numeric Type without the integer operators (%, bitwise, shift, increment, decrement), a floating-point Type.
    private static bool NonIntegerOperand(BoundType type) => type.IsNumeric && !type.HasIntegerArithmetic;

    private bool IsGenericInteger(BoundType? type, BindingScope scope)
        => type is { Kind: BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection } &&
            this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: this.Library.PrimitiveInteger)), scope) == ConstraintProof.Proven;

    // SPEC 8.7.4, 3.1.1: Wrapping<T> over a Type proven PrimitiveInteger has the wrapping integer operators, unary minus for
    // every instantiation, the generic literals 0 through 127 and the conversions to and from T; each instance uses its Scalar.
    private bool IsGenericWrapping(BoundType? type, BindingScope scope)
        => type is { Kind: BoundTypeKind.Constructed, Components.Count: 1 } && type.Symbol?.LibraryDeclaration == KimiDeclarationId.Wrapping &&
            this.IsGenericInteger(type.Components[0], scope);

    // An integer Type, concrete or generic: the Types that positions, range boundaries, lengths and shift counts accept.
    private bool IsIntegerOperand(BoundType? type, BindingScope scope) => type is { IsInteger: true } || this.IsGenericInteger(type, scope);

    // A Type with the integer operators (SPEC 13.3.1): an integer or wrapping integer Type, concrete or generic.
    private bool IsArithmeticInteger(BoundType? type, BindingScope scope)
        => type is { HasIntegerArithmetic: true } || this.IsGenericInteger(type, scope) || this.IsGenericWrapping(type, scope);

    private BoundType? BindUnary(UnaryKoto unary, BindingScope scope, BoundType? expected)
    {
        if (unary is FromEndIndexKoto fromEnd)
        {
            return this.BindFromEnd(fromEnd, scope, expected);
        }

        if (unary is MacroKoto)
        {
            return unary.Operand is InvocationKoto { Method: IdentifierNameKoto { IdentifierName: "tryWrite" } }
                ? this.BindTryWrite(unary, scope) : this.BindAbort(unary, scope);
        }

        if (this.TryArithmetic(unary, scope, out var arithmeticResult))
        {
            return arithmeticResult;
        }

        if (unary.Akind is KotoKind.PrefixMinus or KotoKind.PrefixPlus && unary.Operand is NumberLiteralKoto number)
        {
            if (this.TakesGenericLiterals(expected, scope))
            {
                if (!number.IsInteger)
                {
                    return this.Fail(unary, BindingFailure.TypeMismatch);
                }

                if (!FitsGenericInteger(number, unary.Akind == KotoKind.PrefixMinus))
                {
                    return this.Fail(unary, BindingFailure.InvalidLiteral);
                }

                Complete(number, expected);
                return Complete(unary, expected);
            }

            // A directly signed literal is fitted as a signed value (SPEC 12.3).
            var type = DefaultLiteralType(number, expected);
            if (!LiteralCategoryMatches(number, type) && !(ReferenceEquals(number, this.floatingIntegerLiteral) && type.IsFloatingPoint))
            {
                return this.Fail(unary, BindingFailure.TypeMismatch);
            }

            if (!FitsLiteral(number, type, unary.Akind == KotoKind.PrefixMinus, this.compilation.PointerWidth))
            {
                return this.Fail(unary, BindingFailure.InvalidLiteral);
            }

            Complete(number, type);
            return Complete(unary, type);
        }

        var operand = this.BindNode(unary.Operand, scope, unary.Akind == KotoKind.Not ? BoundType.Boolean : unary.Akind == KotoKind.Dereference ? null : expected);
        if (unary.Akind is KotoKind.Not or KotoKind.PrefixPlus or KotoKind.PrefixMinus)
        {
            operand = this.ReadReferent(unary.Operand, operand); // SPEC 3.3: a value operand denotes its Copy referent.
        }

        if (operand is null)
        {
            return Complete(unary, null);
        }

        switch (unary.Akind)
        {
            case KotoKind.Not:
                return Compatible(operand, BoundType.Boolean) ? Complete(unary, BoundType.Boolean) : this.Fail(unary, BindingFailure.TypeMismatch);
            case KotoKind.PrefixPlus:
                return operand.IsNumeric || this.IsGenericInteger(operand, scope) || this.IsGenericWrapping(operand, scope) ? Complete(unary, operand)
                    : NonNumericOperand(operand) ? this.FailOperand(unary, operand, BindingFailure.NonNumericOperand)
                    : this.Fail(unary, BindingFailure.TypeMismatch);
            case KotoKind.PrefixMinus:
                // Negation is defined for signed integers, floating-point values and every wrapping integer Type (SPEC 13.2, 13.3.1).
                return (operand.IsNumeric && !operand.IsUnsignedInteger) || this.IsGenericWrapping(operand, scope) ? Complete(unary, operand)
                    : NonNumericOperand(operand) ? this.FailOperand(unary, operand, BindingFailure.NonNumericOperand)
                    : this.Fail(unary, BindingFailure.TypeMismatch);
            case KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement:
                if (!this.ValidPropertyWritePath(unary.Operand, scope))
                {
                    return this.FailWrite(unary, unary.Operand);
                }

                if (ElementAccess.DestinationType(unary.Operand, operand) is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq } &&
                    ReferenceBindingAssignment(unary.Operand) is { } referenceBinding)
                {
                    return this.Fail(unary, referenceBinding);
                }

                if (!Writable(unary.Operand) && ElementAccess.WritableRoot(unary.Operand) is null)
                {
                    return this.FailWrite(unary, unary.Operand);
                }

                if (this.HolderPlaceFailure(unary.Operand, HolderAccess.Update, null, unary))
                {
                    return null;
                }

                // Increment and decrement apply to integer and wrapping integer Types, never to floats (SPEC 13.2).
                var destination = ElementAccess.DestinationType(unary.Operand, operand);
                return this.IsArithmeticInteger(destination, scope) ? Complete(unary, destination)
                    : destination is not null && NonNumericOperand(destination) ? this.FailOperand(unary, destination, BindingFailure.NonNumericOperand)
                    : destination is not null && NonIntegerOperand(destination) ? this.FailOperand(unary, destination, BindingFailure.NonIntegerOperand)
                    : this.Fail(unary, BindingFailure.TypeMismatch);
            case KotoKind.Dereference:
                // SPEC 5.2: *p denotes a Place of the pointee Type; the unsafe context is checked by control flow.
                return ReferenceTypes.IsPointer(operand) ? Complete(unary, operand.Components[0]) : this.Fail(unary, BindingFailure.TypeMismatch);
            default:
                return this.Fail(unary, BindingFailure.Internal);
        }
    }

    private BoundType? BindBinary(BinaryKoto binary, BindingScope scope, BoundType? expected)
    {
        binary.ComparisonActive = false;
        if (this.TryArithmetic(binary, scope, out var arithmeticResult))
        {
            return arithmeticResult;
        }

        var kind = binary.Akind;
        if (kind is KotoKind.Conversion or KotoKind.As or KotoKind.Is)
        {
            this.BindNode(binary.Left, scope);
            this.BindType(binary.Right, scope);
            return this.Fail(binary, BindingFailure.Unsupported, true);
        }

        var logical = kind is KotoKind.And or KotoKind.Or;
        var assignment = kind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals;
        var operation = assignment && kind != KotoKind.Equals ? KotoHelper.CompoundOperation(kind) : kind;
        var comparison = operation is KotoKind.LessThan or KotoKind.LessThanEquals or KotoKind.GreaterThan or KotoKind.GreaterThanEquals or KotoKind.EqualsEquals or KotoKind.ExclamationEquals;
        var shift = operation is KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan;
        var arithmetic = operation is KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash or KotoKind.Percent or KotoKind.Ampersand or KotoKind.Bar or KotoKind.Caret;
        var integerOnly = operation is KotoKind.Percent or KotoKind.Ampersand or KotoKind.Bar or KotoKind.Caret;
        BoundType? left;
        BoundType? right;
        // SPEC 3.3 and 13.4: an operand of Type ref/T or uniq/T denotes its Copy referent; an assignment
        // target keeps its Place, and an assigned value is read only where the target's Type expects it.
        if (shift)
        {
            // The count may have any integer Type and is typed independently of the shifted operand (SPEC 12.3, 13.3.1).
            left = this.BindNode(binary.Left, scope, assignment ? null : expected);
            left = assignment ? ElementAccess.DestinationType(binary.Left, left) : this.ReadReferent(binary.Left, left);
            right = this.BindNode(binary.Right, scope);

            // SPEC 3.5.3: the count of a compound shift is Scalar-read like that of a plain shift (below).
            right = assignment ? this.ReadReferent(binary.Right, right) : right;
        }
        else if (IsUnfittedLiteral(binary.Left) && !IsUnfittedLiteral(binary.Right) && !assignment)
        {
            right = this.ReadReferent(binary.Right, this.BindNode(binary.Right, scope, logical ? BoundType.Boolean : null));
            left = this.BindNode(binary.Left, scope, right ?? (comparison ? null : expected));
        }
        else
        {
            left = this.BindNode(binary.Left, scope, logical ? BoundType.Boolean : comparison || assignment ? null : expected);
            left = assignment ? ElementAccess.DestinationType(binary.Left, left) : this.ReadReferent(binary.Left, left);

            // SPEC 15.1.6: a compound update of a reference binding, or a literal assigned to it, is fitted to the referent
            // so that the one diagnostic names the binding's mode instead of a literal mismatch.
            if (assignment && left is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } &&
                (kind != KotoKind.Equals || IsUnfittedLiteral(binary.Right)) && ReferenceBindingAssignment(binary.Left) is { } referenceBinding)
            {
                this.BindNode(binary.Right, scope, left.Components[0]);
                return this.Fail(binary, referenceBinding);
            }

            // SPEC 5.3: a pointer is displaced by an isize count, including in p += n and p -= n.
            // SPEC 13.4: a comparison reads through every reference layer, so the other operand is fitted to the referent.
            var comparand = comparison && left is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq } && !ReferenceTypes.IsString(left) ? ComparisonReferent(left) : left;
            // SPEC 13.3.1: a left operand without the operator is the problem, so the right operand is bound without its Type.
            // SPEC 3.1.3, 13.4: a non-completing left operand fits the right operand's Type, so it expects nothing of it.
            var expectedRight = logical ? BoundType.Boolean
                : !assignment && ReferenceEquals(left, BoundType.Never) ? null
                : ReferenceTypes.IsPointer(left) && operation is KotoKind.Plus or KotoKind.Minus ? BoundType.ISize
                : arithmetic && left is not null && (NonNumericOperand(left) || (integerOnly && NonIntegerOperand(left))) ? null
                : comparison && comparand?.CarriesAnyOrigin == true && !IsUnfittedLiteral(binary.Right) ? null
                : comparand;
            right = this.BindExpected(binary.Right, scope, expectedRight, assignment ? binary.Left : null);
            if (assignment && kind == KotoKind.Equals && left is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } &&
                right is not null && !Compatible(right, left) && Compatible(right, left.Components[0]) && ReferenceBindingAssignment(binary.Left) is { } valueBinding)
            {
                return this.Fail(binary, valueBinding);
            }
        }

        if (!assignment)
        {
            right = this.ReadReferent(binary.Right, right);
        }

        if (left is null || right is null)
        {
            return Complete(binary, null);
        }

        if (arithmetic && !assignment && ReferenceEquals(left, BoundType.Never))
        {
            left = right; // The operation is checked at the Type the non-completing operand fits (SPEC 3.1.3, 13.3.1).
        }

        if (assignment && !this.ValidPropertyWritePath(binary.Left, scope))
        {
            return this.FailWrite(binary, binary.Left);
        }

        if (assignment && !Writable(binary.Left) && ElementAccess.WritableRoot(binary.Left) is null &&
            !(kind == KotoKind.Equals && (CanInitializeLocal(binary.Left, scope) || (IsSpecialField(binary.Left, out var constructor) && constructor.IsConstructor))))
        {
            return this.FailWrite(binary, binary.Left);
        }

        if (assignment && this.HolderPlaceFailure(binary.Left, kind == KotoKind.Equals ? HolderAccess.Write : HolderAccess.Update, binary.Right, binary))
        {
            return null;
        }

        var result = assignment ? BoundType.Unit : left;
        if (assignment && kind != KotoKind.Equals && this.PropertySetterInput(KotoHelper.UnwrapParentheses(binary.Left)) is { } setterInput &&
            !this.FitsTypeAt(left, setterInput, binary))
        {
            return this.FailMismatch(binary, binary.Left, left, setterInput);
        }

        if (shift)
        {
            // SPEC 13.3.1: the shifted operand may be a wrapping integer Type; the count is an integer Type, never a wrapping one.
            return NonNumericOperand(left) ? this.FailOperand(binary, left, BindingFailure.NonNumericOperand)
                : NonIntegerOperand(left) ? this.FailOperand(binary, left, BindingFailure.NonIntegerOperand)
                : !this.IsArithmeticInteger(left, scope) ? this.Fail(binary, BindingFailure.TypeMismatch)
                : this.IsIntegerOperand(right, scope) || ReferenceEquals(right, BoundType.Never) ? Complete(binary, result)
                : this.FailOperand(binary, right, BindingFailure.InvalidShiftCount);
        }

        // Shared references compare their immediate referents, independently of the two input Origins.
        if (comparison && (ReferenceTypes.EndsInString(left) || ReferenceEquals(left, BoundType.Never)) &&
            (ReferenceTypes.EndsInString(right) || ReferenceEquals(right, BoundType.Never)))
        {
            // SPEC 13.4: every operand is inspected in place through at most one reference.
            this.CompareInPlace(binary.Left);
            this.CompareInPlace(binary.Right);
            this.StringThroughLayers(binary.Left, left);
            this.StringThroughLayers(binary.Right, right);
            return Complete(binary, BoundType.Boolean);
        }

        if (logical)
        {
            // Both operands have a bool context, even when the left transfers.
            // Never fitting does not waive checking of an unreachable right side.
            return !Compatible(left, BoundType.Boolean) ? this.FailMismatch(binary, binary.Left, left, BoundType.Boolean)
                : !Compatible(right, BoundType.Boolean) ? this.FailMismatch(binary, binary.Right, right, BoundType.Boolean)
                : Complete(binary, BoundType.Boolean);
        }

        if (ReferenceTypes.IsPointer(left) && operation is KotoKind.Plus or KotoKind.Minus)
        {
            // SPEC 5.3: an isize count; a zero element stride makes every displacement zero.
            return ReferenceEquals(right, BoundType.ISize) || ReferenceEquals(right, BoundType.Never)
                ? Complete(binary, result)
                : this.FailMismatch(binary, binary.Right, right, BoundType.ISize);
        }

        // SPEC 13.3.1: arithmetic and bitwise operators take numeric operands, so bool, Unit, char and string have none. A string
        // operand is judged through its reference layers and before the operands are compared, so that two string references
        // with distinct Origins are not reported as a mismatch of one Type with itself; an interpolated literal is the one way
        // to join strings (SPEC 12.4.2). % and the bitwise operators take integers only, so a floating-point left operand is
        // the problem whatever the other operand is.
        if (arithmetic && NonNumericOperand(left))
        {
            return this.FailOperand(binary, left, BindingFailure.NonNumericOperand);
        }

        if (integerOnly && NonIntegerOperand(left))
        {
            return this.FailOperand(binary, left, BindingFailure.NonIntegerOperand);
        }

        if (comparison)
        {
            // SPEC 13.4, 3.4.3: qualifying pair layers are among the followed layers.
            left = this.ComparisonThroughPairs(binary.Left, left);
            right = this.ComparisonThroughPairs(binary.Right, right);
        }

        // SPEC 13.4: a comparison reads through every safe reference layer of either operand; a Unit referent is read like a Scalar.
        if (comparison && (ComparisonReferent(left).Kind != BoundTypeKind.Primitive || ComparisonReferent(right).Kind != BoundTypeKind.Primitive ||
            !ReferenceEquals(ComparisonReferent(left), left) || !ReferenceEquals(ComparisonReferent(right), right)))
        {
            if (!ReferenceEquals(left, BoundType.Unit) && ReferenceEquals(ComparisonReferent(left), BoundType.Unit))
            {
                this.adaptations[binary.Left] = new(ExpectedAdaptationKind.ReferentRead, BoundType.Unit);
            }

            if (!ReferenceEquals(right, BoundType.Unit) && ReferenceEquals(ComparisonReferent(right), BoundType.Unit))
            {
                this.adaptations[binary.Right] = new(ExpectedAdaptationKind.ReferentRead, BoundType.Unit);
            }

            left = ComparisonReferent(left);
            right = ComparisonReferent(right);
        }

        if (comparison && ReferenceEquals(left, BoundType.Never))
        {
            // The other operand still supplies and must prove the user comparison capability.
            left = right;
        }

        // SPEC 15.6.1: an assignment's Origin part is judged under the premises in scope, such as `origin other outlives anchor`.
        if (comparison ? !Compatible(right, left) : !this.FitsTypeAt(right, left, binary) && !this.CheckLocalTypeUse(right, left, binary.Right))
        {
            if (comparison && this.CommonOriginType(left, right) is { } common)
            {
                left = common;
            }
            else
            {
                return this.FailMismatch(binary, binary.Right, right, left);
            }
        }

        if (kind == KotoKind.Equals)
        {
            return Complete(binary, BoundType.Unit);
        }

        // Built-in comparisons retain priority over the user Contract mapping (SPEC 13.4.1); a generic integer uses them too.
        var primitive = left.Kind == BoundTypeKind.Primitive && !ReferenceEquals(left, BoundType.Never);
        var genericInteger = !primitive && (this.IsGenericInteger(left, scope) || this.IsGenericWrapping(left, scope));
        if (comparison && ReferenceTypes.IsPointer(left))
        {
            // SPEC 5.1: same-Type pointers, or a pointer and null, compare addresses; ordering is not defined.
            return operation is KotoKind.EqualsEquals or KotoKind.ExclamationEquals ? Complete(binary, BoundType.Boolean) : this.Fail(binary, BindingFailure.TypeMismatch);
        }

        if (comparison)
        {
            // bool and Unit support equality only; numbers, char, and string are also ordered (SPEC 13.4).
            var ordered = left.IsNumeric || ReferenceEquals(left, BoundType.Char) || ReferenceEquals(left, BoundType.String);
            if ((primitive && (ordered || operation is KotoKind.EqualsEquals or KotoKind.ExclamationEquals)) || genericInteger)
            {
                return Complete(binary, BoundType.Boolean);
            }

            return primitive ? this.Fail(binary, BindingFailure.TypeMismatch) : this.BindContractComparison(binary, left, scope);
        }

        if (genericInteger)
        {
            return Complete(binary, result); // SPEC 8.7.4: every arithmetic, bitwise and remainder operator is defined for integers.
        }

        if (left.IsNumeric)
        {
            // The initial native profile excludes wide division, including unreachable bodies.
            if (ScalarTypes.Width(left, this.compilation.PointerWidth) == 128 && operation is KotoKind.Slash or KotoKind.Percent)
            {
                return this.Fail(binary, BindingFailure.Unsupported, true);
            }

            return Complete(binary, result);
        }

        // SPEC 13.3.1: the bitwise operators take integers only and have no user extension. SPEC 13.3.4, 13.3: an
        // operand Type that is neither numeric nor a provider of the operator, such as an unconstrained Type parameter, a Tuple
        // or a raw pointer product, selects no conformance.
        if (operation is KotoKind.Ampersand or KotoKind.Bar or KotoKind.Caret)
        {
            return this.FailOperand(binary, left, BindingFailure.NonIntegerOperand);
        }

        var rejected = binary.ArithmeticStorage ??= new();
        rejected.Active = false;
        rejected.Candidates.Clear();
        rejected.Sources.Clear();
        return this.FailExplained(ref this.arithmeticSelectionFailures, binary, BindingFailure.ArithmeticSelection, new(rejected, left, right, ArithmeticContract(operation), "No proven conformance fits the counterpart structure"));
    }

    // SPEC 13.4, 10.2: a string operand behind several reference layers is read as one shared reference to the string;
    // a single ref or uniq layer is inspected as it is.
    private void StringThroughLayers(Koto operand, BoundType type)
    {
        if (type.Components.Count == 1 && ReferenceTypes.EndsInString(type) && !ReferenceTypes.IsStringReference(type) &&
            this.SharedReferenceThroughLayers(type, BoundType.String, out _) is { } shared)
        {
            this.adaptations[operand] = new(ExpectedAdaptationKind.ReferenceRead, shared);
        }
    }
}
