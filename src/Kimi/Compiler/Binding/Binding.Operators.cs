// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns unary and binary operator binding: syntax, scope and expected Type enter; bound Types, adaptations and failures leave.
// Operand binding, destination authority, fitting and operator completion retain their order. Arithmetic selection and
// comparison composition keep their existing owners. This owner neither emits code nor formats diagnostics.
public sealed partial class Binding
{
    // SPEC 13.3.1: a primitive without arithmetic (bool, Unit, char, string) or safe reference layers ending in string.
    // Never fitting needs a valid counterpart Type; it is not numeric capability. Pointer displacement follows SPEC 5.3.
    private static bool NonNumericOperand(BoundType type)
        => (type.Kind == BoundTypeKind.Primitive && !type.IsNumeric && !ReferenceEquals(type, BoundType.Never)) || ReferenceTypes.EndsInString(type);

    private bool IsGenericInteger(BoundType? type, BindingScope scope)
        => type is { IsProofDecided: true } &&
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

            if (!FitsLiteral(number, type, unary.Akind == KotoKind.PrefixMinus))
            {
                return this.Fail(unary, BindingFailure.InvalidLiteral);
            }

            Complete(number, type);
            return Complete(unary, type);
        }

        // SPEC 12.3, 13.3.4: only a literal-only operand takes the expected Type; any other operand is typed on its own, once.
        var operand = this.BindNode(unary.Operand, scope, unary.Akind == KotoKind.Not ? BoundType.Boolean : unary.Akind != KotoKind.Dereference && IsUnfittedLiteral(unary.Operand) ? expected : null);
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
            case KotoKind.PrefixPlus or KotoKind.PrefixMinus:
                var decision = this.OperatorApplicabilityOf(unary, operand, null, scope);
                return decision.Failure == BindingFailure.None ? Complete(unary, operand) : this.FailOperator(unary, unary, decision);
            case KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement:
                if (!this.CheckDestination(unary, unary.Operand, operand, scope) || !this.FitsUpdateResult(unary, unary.Operand, operand))
                {
                    return this.FailRoute(unary, unary.Operand, operand, null, scope);
                }

                // Increment and decrement apply to integer and wrapping integer Types, never to floats (SPEC 13.2).
                var update = this.OperatorApplicabilityOf(unary, operand, null, scope);
                return update.Failure == BindingFailure.None ? Complete(unary, operand) : this.FailOperator(unary, unary, update);
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
        var kind = binary.Akind;
        var assignment = kind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals;
        if (this.TryArithmetic(binary, scope, out var arithmeticResult))
        {
            return assignment ? this.CompleteRoutedUpdate(binary, arithmeticResult, scope) : arithmeticResult;
        }

        if (kind is KotoKind.Conversion or KotoKind.As or KotoKind.Is)
        {
            this.BindNode(binary.Left, scope);
            this.BindType(binary.Right, scope);
            return this.Fail(binary, BindingFailure.Unsupported, true);
        }

        var operation = OperatorKind(binary);
        var shift = operation is KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan;
        if (!assignment && !shift && IsUnfittedLiteral(binary.Left) && !IsUnfittedLiteral(binary.Right))
        {
            return this.BindBinaryFromRight(binary, scope, expected);
        }

        var context = ComparisonOperator(kind) || assignment ? null : expected;
        if (kind is KotoKind.And or KotoKind.Or)
        {
            context = BoundType.Boolean;
        }

        var bound = this.BindNode(binary.Left, scope, context);
        var left = assignment ? bound : this.ReadReferent(binary.Left, bound);
        // Keep a reference binding's authority failure ahead of literal fitting and compound applicability.
        if (assignment && left is { Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } &&
            (kind != KotoKind.Equals || IsUnfittedLiteral(binary.Right)) && ReferenceBindingAssignment(binary.Left) is { } referenceBinding)
        {
            var value = this.BindNode(binary.Right, scope, left.Components[0]);
            this.Fail(binary, referenceBinding);
            return kind == KotoKind.Equals ? null : this.FailRoute(binary, binary.Left, left, value, scope);
        }

        var decision = this.OperatorApplicabilityOf(binary, left, null, scope);
        var counterpart = ReferenceEquals(decision.ExpectedOperand, binary.Right) ? decision.Counterpart : null;
        var right = this.BindExpected(binary.Right, scope, counterpart, assignment ? binary.Left : null);
        if (assignment && kind == KotoKind.Equals && left is { Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } &&
            right is not null && !Compatible(right, left) && Compatible(right, left.Components[0]) &&
            ReferenceBindingAssignment(binary.Left) is { } valueBinding)
        {
            return this.Fail(binary, valueBinding);
        }

        right = !assignment || shift ? this.ReadReferent(binary.Right, right) : right;
        // SPEC 23.1.2: whether the destination admits the write does not rest on the value, so a failed value does not hide it. SPEC 5.7: a
        // write admits a `during self` layer's Place only with null at every layer, which the value decides.
        if (assignment && left is not null && (!this.CheckDestination(binary, binary.Left, left, scope) || (kind == KotoKind.Equals
            ? this.HolderPlaceFailure(binary.Left, HolderAccess.Write, binary.Right, binary) : !this.FitsUpdateResult(binary, binary.Left, left))))
        {
            return kind == KotoKind.Equals ? null : this.FailRoute(binary, binary.Left, left, right, scope);
        }

        if (decision.Failure == BindingFailure.None && right is not null && (left is null || ReferenceEquals(left, BoundType.Never)))
        {
            decision = this.OperatorApplicabilityOf(binary, left, right, scope);
        }

        if (decision.Failure != BindingFailure.None)
        {
            return this.FailOperator(binary, binary, decision);
        }

        if (left is null || right is null)
        {
            return Complete(binary, null);
        }

        return this.CompleteBinary(binary, left, right, scope, decision);
    }

    // A literal may receive a Type from the known right operand only after that ordered operation is legal.
    private BoundType? BindBinaryFromRight(BinaryKoto binary, BindingScope scope, BoundType? expected)
    {
        var logical = binary.Akind is KotoKind.And or KotoKind.Or;
        var right = this.ReadReferent(binary.Right, this.BindNode(binary.Right, scope, logical ? BoundType.Boolean : null));
        var decision = right is null ? default : this.OperatorApplicabilityOf(binary, null, right, scope);
        var context = ReferenceEquals(decision.ExpectedOperand, binary.Left) ? decision.Counterpart : null;
        if (right is null && !ComparisonOperator(binary.Akind))
        {
            context = expected;
        }

        var left = this.BindNode(binary.Left, scope, context);
        if (decision.Failure != BindingFailure.None)
        {
            return this.FailOperator(binary, binary, decision);
        }

        if (left is null || right is null)
        {
            return Complete(binary, null);
        }

        if (decision.ExpectedOperand is null)
        {
            decision = this.OperatorApplicabilityOf(binary, left, right, scope);
        }

        return decision.Failure == BindingFailure.None ? this.CompleteBinary(binary, left, right, scope, decision)
            : this.FailOperator(binary, binary, decision);
    }

    // SPEC 23.1.2: the route of a compound assignment or update whose destination failed reads neither the destination's authority nor its
    // Holder, so it stays a verdict, on the read Type: its failure is the node's other problem. A route a fit alone refuses stays with the
    // mismatch family (E3).
    private BoundType? FailRoute(Koto operation, Koto destination, BoundType operand, BoundType? right, BindingScope scope)
    {
        var route = this.OperatorApplicabilityOf(operation, this.ReadReferent(destination, operand), right, scope);
        return route.Failure == BindingFailure.None ? null : this.FailOperator(operation, operation, route);
    }

    // SPEC 13.7.1, 3.4, 15.1.5: the one destination check of an assignment, compound assignment, increment or decrement, run once the
    // operands and route have bound, whatever they gave: the target is located with write permission under the path, `let`/`var` and
    // Property permissions. It reads the target, never the value's Type. An update of a reference binding names the binding's mode
    // (SPEC 15.1.6); a plain write's mode record rests on its value (BindBinary). An update reads a `during self` layer's Place (SPEC 5.7).
    private bool CheckDestination(Koto operation, Koto target, BoundType type, BindingScope scope)
    {
        if (!this.ValidPropertyWritePath(target, scope))
        {
            this.FailWrite(operation, target);
            return false;
        }

        var write = operation is BinaryKoto { Akind: KotoKind.Equals };
        if (!write && type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq } &&
            ReferenceBindingAssignment(target) is { } referenceBinding)
        {
            this.Fail(operation, referenceBinding);
            return false;
        }

        if (!Writable(target) && ElementAccess.WritableRoot(target) is null &&
            !(write && (CanInitializeLocal(target, scope) || (IsSpecialField(target, out var constructor) && constructor.IsConstructor))))
        {
            this.FailWrite(operation, target);
            return false;
        }

        return write || !this.HolderPlaceFailure(target, HolderAccess.Update, null, operation);
    }

    // SPEC 13.7.1, 13.7.2: a compound assignment through an arithmetic route (TryArithmetic) has its destination checked whatever the route
    // gave, then fits the route's result to the destination Type: a Property's setter input, else the Place's.
    private BoundType? CompleteRoutedUpdate(BinaryKoto binary, BoundType? computed, BindingScope scope)
    {
        if (binary.Left.BoundType is not { } target || !this.CheckDestination(binary, binary.Left, target, scope) || computed is null)
        {
            return Complete(binary, null);
        }

        var destination = this.PropertyUpdateDestination(binary.Left) ?? target;
        return this.FitsValueAt(computed, destination, binary, binary, out var failure)
            ? Complete(binary, BoundType.Unit) : this.FailMismatch(binary, binary, computed, destination, failure);
    }

    // SPEC 13.7.1: compound assignment, increment and decrement compute in the read Type; the result is fitted to a Property's destination
    // Type. An arithmetic route fits its own result (CompleteRoutedUpdate).
    private bool FitsUpdateResult(Koto operation, Koto target, BoundType read)
    {
        if (this.PropertyUpdateDestination(target) is not { } destination || this.FitsValueAt(read, destination, operation, target, out var failure))
        {
            return true;
        }

        this.FailMismatch(operation, target, read, destination, failure);
        return false;
    }

    // Completes the already judged operation: fit its other operand, enforce implementation boundaries, or select its existing plan.
    private BoundType? CompleteBinary(BinaryKoto binary, BoundType left, BoundType right, BindingScope scope, OperatorApplicability decision)
    {
        var operation = OperatorKind(binary);
        if (ReferenceEquals(decision.ExpectedOperand, binary.Left) && decision.Counterpart is { } fitted)
        {
            left = fitted;
        }

        if (ComparisonOperator(operation))
        {
            return this.CompleteComparison(binary, left, right, scope, decision);
        }

        var assignment = binary.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals;
        var result = assignment ? BoundType.Unit : left;
        if (operation is KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan)
        {
            var count = this.ShiftCountApplicability(right, scope);
            return count.Failure == BindingFailure.None ? Complete(binary, result) : this.FailOperator(binary, binary.Right, count);
        }

        if (operation is KotoKind.And or KotoKind.Or)
        {
            return !Compatible(left, BoundType.Boolean) ? this.FailMismatch(binary, binary.Left, left, BoundType.Boolean, ComparisonFailure.Shape)
                : !Compatible(right, BoundType.Boolean) ? this.FailMismatch(binary, binary.Right, right, BoundType.Boolean, ComparisonFailure.Shape)
                : Complete(binary, BoundType.Boolean);
        }

        if (ReferenceTypes.IsPointer(left) && operation is KotoKind.Plus or KotoKind.Minus)
        {
            return ReferenceEquals(right, BoundType.ISize) || ReferenceEquals(right, BoundType.Never)
                ? Complete(binary, result) : this.FailMismatch(binary, binary.Right, right, BoundType.ISize, ComparisonFailure.Shape);
        }

        if (!this.FitsValueAt(right, left, binary, binary.Right, out var fit))
        {
            return this.FailMismatch(binary, binary.Right, right, left, fit);
        }

        if (binary.Akind == KotoKind.Equals)
        {
            return Complete(binary, BoundType.Unit);
        }

        if (decision.Requirement?.Contract == this.Library.PrimitiveInteger && decision.Proof == ConstraintProof.Proven)
        {
            return Complete(binary, result);
        }

        if (left.IsNumeric)
        {
            // A valid wide division remains an implementation boundary, independently of operator applicability.
            return left.Width == 128 && operation is KotoKind.Slash or KotoKind.Percent
                ? this.Fail(binary, BindingFailure.Unsupported, true) : Complete(binary, result);
        }

        var rejected = binary.ArithmeticStorage ??= new();
        rejected.Active = false;
        rejected.Candidates.Clear();
        rejected.Sources.Clear();
        return this.FailExplained(
            ref this.arithmeticSelectionFailures,
            binary,
            BindingFailure.ArithmeticSelection,
            new(rejected, left, right, ArithmeticContract(operation), ArithmeticSelectionCause.NoFit));
    }

    // SPEC 13.4, 10.2: a string operand behind several reference layers is read as one shared reference to the string;
    // a single ref or uniq layer is inspected as it is.
    private void StringThroughLayers(Koto operand, BoundType type)
    {
        if (type.Components.Count == 1 && ReferenceTypes.EndsInString(type) && !ReferenceTypes.IsStringReference(type) &&
            this.SharedReferenceThroughLayers(type, BoundType.String, this.ConstraintScope(operand), out _) is { } shared)
        {
            this.adaptations[operand] = new(ExpectedAdaptationKind.ReferenceRead, shared);
        }
    }
}
