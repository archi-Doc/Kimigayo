// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // An independent array literal defaults differently from a contextual call argument.
    private static bool IsCallArgument(Koto node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is InvocationKoto)
            {
                return true;
            }

            if (parent is not (LabeledKoto or ParenthesizedKoto))
            {
                return false;
            }
        }

        return false;
    }

    // SPEC 13.5.4.2: a direct literal converted at compile time. The literal, its sign and its parentheses take the target Type,
    // and the conversion carries the folded constant that is produced instead of evaluating the literal.
    private static BoundType? CompleteFolded(ConversionKoto conversion, NumberLiteralKoto number, BoundType target, Int128 folded, ConversionBinding binding)
    {
        conversion.FoldedConstant = folded;
        for (var node = conversion.Left; ; node = ((UnaryKoto)node).Operand)
        {
            Complete(node, target);
            if (ReferenceEquals(node, number))
            {
                break;
            }
        }

        conversion.ConversionBinding = binding;
        return Complete(conversion, target);
    }

    // SPEC 13.5.4.1: the explanation of a numeric conversion rejected for a different integer argument.
    internal const string WrappingConversionNote = "A numeric conversion enters or leaves Wrapping<U> only from U itself, so the range check stays visible (SPEC 13.5.4.1)";

    // SPEC 13.5.4.3: E@wrap<U> wraps an integer or wrapping integer value to the integer or wrapping integer Type U, concrete
    // or generic, without a check. A direct literal is wrapped at compile time; for a generic U only the generic literals fit.
    private BoundType? BindWrapConversion(ConversionKoto conversion, BindingScope scope, Koto argument, string operation)
    {
        var target = this.BindType(argument, scope);
        var operand = KotoHelper.UnwrapParentheses(conversion.Left);
        var number = KotoHelper.SignedNumber(operand, out var negative);
        if (operation != Constants.WrapOperation)
        {
            return this.BindBitConversion(conversion, scope, target, number, negative);
        }

        if (target is not null && number is { IsInteger: true } && this.IsArithmeticInteger(target, scope))
        {
            var width = ScalarTypes.Width(target, this.compilation.PointerWidth);
            if (width == 0)
            {
                // A generic U: the literal must fit every instance, so it is an ordinary generic literal and no wrapping happens.
                conversion.ConversionBinding = ConversionBinding.Literal;
                Complete(conversion.Right, target);
                return Complete(conversion, this.BindNode(conversion.Left, scope, target) is null ? null : target);
            }

            if (!number.TryGetIntegerMagnitude(out var magnitude))
            {
                return this.Fail(conversion, BindingFailure.InvalidLiteral);
            }

            // The exact value modulo 2^N, stored as the sign-extended N-bit payload like every other constant.
            var payload = negative ? (UInt128)0 - magnitude : magnitude;
            Complete(conversion.Right, target);
            return CompleteFolded(conversion, number, target, ScalarTypes.Normalize(unchecked((Int128)payload), width), ConversionBinding.Wrap);
        }

        var source = this.BindNode(conversion.Left, scope);
        if (source is null || target is null)
        {
            return Complete(conversion, null);
        }

        if (ReferenceEquals(source, BoundType.Never))
        {
            conversion.ConversionBinding = ConversionBinding.Abrupt;
            return Complete(conversion, BoundType.Never);
        }

        source = this.ReadReferent(conversion.Left, source)!; // SPEC 13.5.2: a read position.
        if (!this.IsArithmeticInteger(source, scope) || !this.IsArithmeticInteger(target, scope))
        {
            return this.Fail(conversion, BindingFailure.InvalidWrapConversion);
        }

        conversion.ConversionBinding = ConversionBinding.Wrap;
        Complete(conversion.Right, target);
        return Complete(conversion, target);
    }

    // SPEC 13.5.4.4: E@bits<U> reinterprets the bits between a floating-point Type and an integer or wrapping integer Type of
    // the same fixed width, in either direction, without a check; a direct literal is converted at compile time.
    private BoundType? BindBitConversion(ConversionKoto conversion, BindingScope scope, BoundType? target, NumberLiteralKoto? number, bool negative)
    {
        if (target is not null && number is not null)
        {
            Int128 folded;
            if (number.IsInteger && FloatingTypes.Supports(target))
            {
                // The integer literal is wrapped to the unsigned Type of the target's width, and those bits are read as the float.
                if (!number.TryGetIntegerMagnitude(out var magnitude))
                {
                    return this.Fail(conversion, BindingFailure.InvalidLiteral);
                }

                var width = ReferenceEquals(target, BoundType.F32) ? 32 : 64;
                var bits = ScalarTypes.Normalize(unchecked((Int128)(negative ? (UInt128)0 - magnitude : magnitude)), width);
                folded = width == 32 ? (Int128)unchecked((uint)(long)bits) : (Int128)unchecked((long)bits);
            }
            else if (!number.IsInteger && BitWidth(target, this.compilation.PointerWidth) is var width && width != 0)
            {
                // The floating literal is rounded once to the floating-point Type of the target's width, and its bits are read as U.
                if (!FloatingTypes.TryLiteral(number.SourceSpelling, width == 32 ? BoundType.F32 : BoundType.F64, negative, out var bits))
                {
                    return this.Fail(conversion, BindingFailure.InvalidLiteral);
                }

                folded = ScalarTypes.Normalize(bits, width);
            }
            else
            {
                this.BindNode(conversion.Left, scope);
                return this.Fail(conversion, target.ContainsParameter ? BindingFailure.GenericBitConversion : BindingFailure.InvalidBitConversion);
            }

            Complete(conversion.Right, target);
            return CompleteFolded(conversion, number, target, folded, ConversionBinding.Bits);
        }

        var source = this.BindNode(conversion.Left, scope);
        if (source is null || target is null)
        {
            return Complete(conversion, null);
        }

        if (ReferenceEquals(source, BoundType.Never))
        {
            conversion.ConversionBinding = ConversionBinding.Abrupt;
            return Complete(conversion, BoundType.Never);
        }

        source = this.ReadReferent(conversion.Left, source)!; // SPEC 13.5.2: a read position.
        if (source.ContainsParameter || target.ContainsParameter)
        {
            return this.Fail(conversion, BindingFailure.GenericBitConversion);
        }

        var sourceFloating = FloatingTypes.Supports(source);
        var targetFloating = FloatingTypes.Supports(target);
        if (sourceFloating == targetFloating || BitWidth(sourceFloating ? target : source, this.compilation.PointerWidth) != (ReferenceEquals(sourceFloating ? source : target, BoundType.F32) ? 32 : 64))
        {
            return this.Fail(conversion, BindingFailure.InvalidBitConversion);
        }

        conversion.ConversionBinding = ConversionBinding.Bits;
        Complete(conversion.Right, target);
        return Complete(conversion, target);
    }

    // SPEC 13.5.4.2: a direct floating-point literal converted to an integer Type is truncated toward zero from its exact decimal
    // value and must fit the target's range. The literal never takes an intermediate floating-point Type, so 9007199254740993.0@i64
    // is exact, and a value outside the range is a compile-time error at the literal rather than a runtime Abort.
    private BoundType? BindTruncatedLiteral(ConversionKoto conversion, BoundType target, Koto operand)
    {
        var number = KotoHelper.SignedNumber(operand, out var negative)!;
        if (!number.TryGetTruncatedMagnitude(out var magnitude) ||
            !ScalarTypes.TryLiteral(target, magnitude, negative, this.compilation.PointerWidth, out var bits))
        {
            this.Fail(operand, BindingFailure.InvalidLiteral);
            for (var node = conversion.Left; !ReferenceEquals(node, operand); node = ((UnaryKoto)node).Operand)
            {
                Complete(node, null);
            }

            return Complete(conversion, null);
        }

        return CompleteFolded(conversion, number, target, bits, ConversionBinding.Literal);
    }

    private BindingScope? conversionEvidenceScope;
    private NumberLiteralKoto? floatingIntegerLiteral;

    internal static bool SupportsIdentityAcquisition(BoundType type)
        => ObjectTypes.HandleMode(type) is not null ||
            type.Semantics is SemanticsKind.Owner or SemanticsKind.Raw;

    // SPEC 13.5.3: E@copy is bound as the Identity acquisition of a proven-Copy value.
    internal static bool IsCopyOperation(ConversionKoto conversion)
        => ConversionTargetSyntax(conversion) is TypeSemanticsKoto { Type: null, Identifier: Constants.CopyOperation };

    // The fixed width of an integer or wrapping integer Type that @bits accepts: 32 or 64, never a platform-dependent or
    // 128-bit one (SPEC 13.5.4.4).
    private static int BitWidth(BoundType type, int pointerWidth)
    {
        if (!type.HasIntegerArithmetic || type.Underlying.Name is "isize" or "usize")
        {
            return 0;
        }

        var width = ScalarTypes.Width(type, pointerWidth);
        return width is 32 or 64 ? width : 0;
    }

    // A Kimi position or range Type other than an integer (SPEC 4.6.2, 4.6.3).
    private static bool IsPositionOrRangeType(BoundType type)
        => type.Symbol?.LibraryDeclaration is KimiDeclarationId.FromEnd or KimiDeclarationId.Start or KimiDeclarationId.End or KimiDeclarationId.Range or
            KimiDeclarationId.ClosedRange or KimiDeclarationId.ResolvedRange;

    // SPEC 13.5: a bare Semantics name or operation directly after @ completes the operation. A grouped target is a Type,
    // so a grouped bare name such as (ref) is bound, and rejected, as a Type; grouping stays transparent for complete targets.
    // Bare @owner remains invalid; object shorthands select their payload target from the input (SPEC 13.5.8).
    private static Koto ConversionTargetSyntax(ConversionKoto conversion)
    {
        var syntax = conversion.Right;
        var grouped = false;
        while (true)
        {
            if (syntax is ParenthesizedTypeKoto parentheses)
            {
                syntax = parentheses.Type;
                grouped = true;
            }
            else if (syntax is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner })
            {
                syntax = inner;
            }
            else
            {
                return grouped && syntax is TypeSemanticsKoto { Type: null } bare &&
                    !(CompilerHelper.TryParse(bare.Identifier, out var semantics) && semantics is SemanticsKind.Owner or SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc)
                    ? conversion.Right : syntax;
            }
        }
    }

    // SPEC 13.5.5.2: @ref, @uniq and the borrow of @raw (SPEC 5.4) borrow the immediately written slot whatever it stores.
    private static bool BorrowsWrittenSlot(BoundType type)
        => AdtDef.IsStruct(type) || AdtDef.IsEnum(type) || type.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Tuple or BoundTypeKind.Closure or BoundTypeKind.Function or BoundTypeKind.FunctionItem or BoundTypeKind.Array or BoundTypeKind.Dictionary or BoundTypeKind.Slice or BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.SemanticsAdaptation ||
            ReferenceTypes.IsStorage(type) || ReferenceTypes.IsPointer(type) || ScalarTypes.Supports(type) || ReferenceEquals(type, BoundType.Unit) || ReferenceEquals(type, BoundType.String) || IsBorrow(type.Semantics) || IsObjectSemantics(type.Semantics);

    // SPEC 3.5: a same-Type acquisition Copies a proven-Copy value and transfers a temporary; a Non-Copy Place needs @move.
    private BoundType? CompleteIdentity(ConversionKoto conversion, BoundType type)
    {
        var scope = this.ConstraintScope(conversion);
        // Copy alone admits borrow Types, whose normalized targets select Borrow rather than identity.
        if (!this.CanSelectIdentity(type, scope))
        {
            return this.Fail(conversion, BindingFailure.UnprovenConstraint);
        }

        if (IsBarePlace(conversion.Left) && this.ProveCopy(type, conversion) != ConstraintProof.Proven)
        {
            return this.FailAcquisition(conversion, BindingFailure.TransferRequired, conversion.Left);
        }

        conversion.ConversionBinding = ReferenceEquals(type, BoundType.Never) ? ConversionBinding.Abrupt : ConversionBinding.Identity;
        return Complete(conversion, type);
    }

    private BoundType? CompleteTransfer(ConversionKoto conversion, BoundType type, BindingScope scope)
    {
        // SPEC 6.2.3.4: construction also allows no Non-Copy Move out of its receiver.
        if (!OffersTake(conversion.Left) ||
            (IsSpecialField(KotoHelper.UnwrapParentheses(conversion.Left), out var special) && special.IsConstructor && this.ProveCopy(type, conversion) != ConstraintProof.Proven))
        {
            var failure = AccessFailure(conversion.Left, take: true);
            return this.Fail(conversion, failure == BindingFailure.InvalidAssignment && KotoHelper.UnwrapParentheses(conversion.Left) is ConversionKoto { ConversionBinding: ConversionBinding.PairFollow } ? BindingFailure.ExclusivePathTake : failure);
        }

        // SPEC 11.1: consuming var storage requires its accessible standard setter,
        // including enclosing owned projections. Move does not require a writable
        // root; a custom getter instead starts a separate result value.
        var source = KotoHelper.UnwrapParentheses(conversion.Left);
        while (!IsGetterResult(source))
        {
            if (source.BoundSymbol?.Property is { Declaration.DeclarationKind: PropertyDeclarationKind.Var } property)
            {
                if (!property.Setter.IsStandard)
                {
                    return this.Fail(conversion, BindingFailure.InvalidAssignment);
                }

                if (!this.Accessible(property.Symbol, scope, property.Setter.Access, (source as MemberAccessKoto)?.Left.BoundType))
                {
                    return this.Fail(conversion, BindingFailure.Access);
                }
            }

            if (source is not BinaryKoto projection || !(projection is MemberAccessKoto || ElementAccess.IsSyntax(projection)) ||
                projection.Left.BoundType?.Semantics != SemanticsKind.Owner)
            {
                break;
            }

            source = KotoHelper.UnwrapParentheses(projection.Left);
        }

        conversion.ConversionBinding = ReferenceEquals(type, BoundType.Never) ? ConversionBinding.Abrupt : ConversionBinding.Transfer;
        return Complete(conversion, type);
    }

    private bool ConversionCanComplete(Koto source, BindingScope scope)
    {
        var structural = this.ResultStructure();
        this.conversionEvidenceScope = scope;
        try
        {
            return structural.CanComplete(source);
        }
        finally
        {
            this.conversionEvidenceScope = null;
        }
    }

    private bool ResultNeverEvidence(Koto source)
    {
        if (source.BoundType is { } known)
        {
            return ReferenceEquals(known, BoundType.Never);
        }

        // Probe only ordinary name/signature evidence, without binding operands
        // or reentering conversion inference during the structural traversal.
        if (this.conversionEvidenceScope is not { } scope || source is not (InvocationKoto or IdentifierNameKoto))
        {
            return false;
        }

        for (var parent = source.Parent; parent is not null; parent = parent.Parent)
        {
            if (this.scopes.TryGetValue(parent, out var enclosing) ||
                (parent.Parent is CodeBlockKoto { Parent: FunctionKoto { IsGenerated: true } } &&
                parent.CodeContext.SourceDocument is { } document && this.scopes.TryGetValue(document, out enclosing)))
            {
                scope = enclosing;
                break;
            }
        }

        return ReferenceEquals(this.ResultEvidence(source, scope), BoundType.Never);
    }

    // SPEC 5.2.2: a borrow of a raw Place recovers no owner or Loan. Its referent is a fresh anchor, so the result Origin has no
    // upper bound and is fitted to the expected Type, result or storage destination: a returned borrow takes the declared result's
    // Origin, and any other one is the anchor, which fits every destination while its Loan is checked under the anchor.
    private BoundType? BorrowRawPlace(ConversionKoto conversion, SemanticsKind semantics, BoundType referent)
    {
        var origin = ResultFunction(conversion)?.BoundSymbol?.Type is { Kind: BoundTypeKind.Semantics, Components: [var declared], Origin: { } result } declaredResult &&
            declaredResult.Semantics == semantics && ReferenceEquals(declared, referent) ? result : this.OriginAtom(conversion, OriginKind.Anchor, 0, "anchor");
        var borrowed = this.InternType(BoundTypeKind.Semantics, null, semantics, [referent], origin: origin);
        Complete(conversion.Right, borrowed);
        conversion.ConversionBinding = ConversionBinding.Borrow;
        return Complete(conversion, borrowed);
    }

    private BoundType? BindConversion(ConversionKoto conversion, BindingScope scope, BoundType? expected = null)
    {
        var syntax = ConversionTargetSyntax(conversion);
        if (syntax is TypeSemanticsKoto { Type: null, Identifier: Constants.FollowOperation, HasOrigin: false })
        {
            // SPEC 13.5.5.1: E@follow selects the referent Place of a ref/uniq value, or the complete payload of a
            // proven Sealed object handle. It reads only the reference and acquires nothing.
            var reference = this.BindNode(conversion.Left, scope);
            if (reference is null)
            {
                return Complete(conversion, null);
            }

            if (ReferenceEquals(reference, BoundType.Never))
            {
                conversion.ConversionBinding = ConversionBinding.Abrupt;
                return Complete(conversion, reference);
            }

            if (reference is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
            {
                Complete(conversion.Right, reference.Components[0]);
                conversion.ConversionBinding = ConversionBinding.Follow;
                return Complete(conversion, reference.Components[0]);
            }

            if (IsObjectSemantics(reference.Semantics) && reference.Components.Count == 1 &&
                this.RequestCapability(reference.Components[0], this.Library.Sealed, scope) == ConstraintProof.Proven)
            {
                Complete(conversion.Right, reference.Components[0]);
                conversion.ConversionBinding = ConversionBinding.PayloadFollow;
                return Complete(conversion, reference.Components[0]);
            }

            if (TryPairLayer(reference, out _, out _))
            {
                // SPEC 13.5.5.1: a pair layer is followed only when its admitted set lies in value or valueborrow.
                if (this.FollowablePair(reference, scope, out var pairTarget) is var admitted && admitted == SemanticsMask.None)
                {
                    return this.Fail(conversion, BindingFailure.UnprovenConstraint);
                }

                this.pairFollows[conversion] = admitted;
                Complete(conversion.Right, pairTarget);
                conversion.ConversionBinding = ConversionBinding.PairFollow;
                return Complete(conversion, pairTarget);
            }

            return this.Fail(conversion, BindingFailure.TypeMismatch);
        }

        if (syntax is TypeSemanticsKoto { Type: null, Identifier: Constants.RawKeyword, HasOrigin: false })
        {
            // SPEC 5.4: P@raw is the address of the written slot. It performs the checks of an immediately ending @ref and
            // converts the borrowed address; the pointer carries no Origin, so it keeps no Loan. The operation's borrow is
            // recorded on the operator.
            var stored = this.BindNode(conversion.Left, scope);
            if (stored is null)
            {
                return Complete(conversion, null);
            }

            if (ReferenceEquals(stored, BoundType.Never))
            {
                conversion.ConversionBinding = ConversionBinding.Abrupt;
                return Complete(conversion, stored);
            }

            BoundType borrowed;
            if (ElementAccess.IsRawPlace(conversion.Left))
            {
                // SPEC 5.4, 5.2.2: the address of a raw Place or a part of one borrows that Place as a fresh anchor.
                borrowed = this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [stored], origin: BoundOrigin.Static);
            }
            else if (!BorrowsWrittenSlot(stored) ||
                !this.AdaptInput(conversion.Left, this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [stored]), stored, scope, null, null, out borrowed, out _, out _, explicitBorrow: true))
            {
                return this.Fail(conversion, BindingFailure.InvalidAssignment);
            }

            Complete(conversion.Right, borrowed);
            conversion.ConversionBinding = ConversionBinding.Address;
            return Complete(conversion, this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Raw, [stored]));
        }

        if (syntax is TypeSemanticsKoto { Type: not null, SemanticsParameter: null, SemanticsKind: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq })
        {
            var pattern = this.BindType(conversion.Right, scope, this.TypeContext(conversion.Right, scope) with { SuppressOuter = true });
            var actual = this.BindNode(conversion.Left, scope);
            if (pattern is null || actual is null)
            {
                return Complete(conversion, null);
            }

            if (ReferenceEquals(actual, BoundType.Never))
            {
                conversion.ConversionBinding = ConversionBinding.Abrupt;
                return Complete(conversion, actual);
            }

            if (TryAdaptationSelector(actual, out _) && IsBorrow(pattern.Semantics))
            {
                var origin = pattern.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq
                    ? this.SlotOrigin(conversion.Left) : this.PlaceOrigin(conversion.Left);
                var borrowed = this.Reference(pattern.Semantics, pattern.Components[0], pattern.Origin ?? origin);
                return this.BindCaseAdaptation(conversion, scope, actual, borrowed);
            }

            if (pattern.Semantics == SemanticsKind.ObjUniq && ObjectTypes.HandleMode(actual) is { PayloadAuthority: LoanRequirement.Ref })
            {
                return this.FailObjectAuthority(conversion, conversion.Left);
            }

            if (ObjectTypes.IsBorrow(pattern) && (ObjectTypes.HandleMode(actual) is not null || ObjectTypes.IsBorrow(actual)) &&
                !ReferenceEquals(actual.Components[0], pattern.Components[0]))
            {
                return this.BindObjectUpcast(conversion, scope, actual, pattern);
            }

            // SPEC 13.5.5.2: a typed borrow names exactly the stored Type of the written slot; it selects no
            // referent, copies no same-Type reference and never Reborrows. Payloads are selected with @follow.
            if (pattern.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq && !ReferenceEquals(actual, pattern.Components[0]))
            {
                return this.Fail(conversion, BindingFailure.TypeMismatch);
            }

            if (pattern.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq && pattern.Origin is null && ElementAccess.IsRawPlace(conversion.Left))
            {
                return this.BorrowRawPlace(conversion, pattern.Semantics, actual);
            }

            if (pattern.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq && pattern.Origin is null && ReferenceEquals(actual, pattern.Components[0]) &&
                this.BorrowablePlace(conversion.Left, scope, pattern.Semantics == SemanticsKind.Uniq))
            {
                var storage = this.InternType(BoundTypeKind.Semantics, null, pattern.Semantics, [actual], origin: this.SlotOrigin(conversion.Left));
                Complete(conversion.Right, storage);
                conversion.ConversionBinding = ConversionBinding.Borrow;
                return Complete(conversion, storage);
            }

            BoundType adapted;
            var fits = ObjectTypes.IsBorrow(pattern)
                ? this.AdaptObjectBorrow(conversion.Left, pattern, actual, scope, true, out adapted, out _, out _)
                : this.AdaptInput(conversion.Left, pattern, actual, scope, null, null, out adapted, out _, out _, explicitBorrow: true);
            if (!fits ||
                !FitsType(adapted.Components[0], pattern.Components[0]) ||
                (pattern.Origin is not null && !this.CheckTypeUse(adapted, pattern, conversion)))
            {
                return this.Fail(conversion, BindingFailure.InvalidAssignment);
            }

            var result = pattern.Origin is null ? adapted : pattern;
            Complete(conversion.Right, result);
            conversion.ConversionBinding = ConversionBinding.Borrow;
            return Complete(conversion, result);
        }

        if (syntax is TypeSemanticsKoto { Type: null, Identifier: Constants.MoveOperation, OriginName: null, OriginExpression: null, OriginArguments: null })
        {
            // SPEC 13.5.3: @move transfers a Movable Place, even a Copy one; a Temporary Value passes its ownership.
            var transferred = this.BindNode(conversion.Left, scope);
            if (transferred is null)
            {
                return Complete(conversion, null);
            }

            Complete(conversion.Right, transferred);
            return this.CompleteTransfer(conversion, transferred, scope);
        }

        if (syntax is TypeSemanticsKoto { Type: null, Identifier: Constants.CopyOperation, OriginName: null, OriginExpression: null, OriginArguments: null })
        {
            // SPEC 13.5.3: @copy Copies a proven-Copy value and never transfers or borrows; a Temporary Value is used as is.
            var copied = this.BindNode(conversion.Left, scope);
            if (copied is null)
            {
                return Complete(conversion, null);
            }

            Complete(conversion.Right, copied);
            if (ReferenceEquals(copied, BoundType.Never))
            {
                conversion.ConversionBinding = ConversionBinding.Abrupt;
                return Complete(conversion, copied);
            }

            if (this.ProveCopy(copied, conversion) != ConstraintProof.Proven)
            {
                return this.Fail(conversion, BindingFailure.NonCopyOperand);
            }

            conversion.ConversionBinding = ConversionBinding.Identity;
            return Complete(conversion, copied);
        }

        if (syntax is TypeSemanticsKoto { Type: null, HasOrigin: false } shorthand && CompilerHelper.TryParse(shorthand.Identifier, out var semantics))
        {
            // SPEC 10.8: an expected borrow of the same Semantics fits an untyped literal operand to its referent
            // Type. A typed operand keeps its own Type: the borrow or reborrow forms from its Place, never from a read.
            var operandExpectation = semantics is not (SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc) && IsUnfittedLiteral(conversion.Left) && expected is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } && expected.Semantics == semantics ? expected.Components[0] : null;
            var operandType = this.BindNode(conversion.Left, scope, operandExpectation);
            if (operandType is null)
            {
                return Complete(conversion, null);
            }

            if (semantics is SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc)
            {
                if (ReferenceEquals(operandType, BoundType.Never))
                {
                    Complete(conversion.Right, operandType);
                    conversion.ConversionBinding = ConversionBinding.Abrupt;
                    return Complete(conversion, operandType);
                }

                var payload = IsObjectSemantics(operandType.Semantics) ? operandType.Components[0] : operandType;
                var objectTarget = this.InternType(BoundTypeKind.Semantics, null, semantics, [payload]);
                return this.BindObjectAdaptation(conversion, scope, operandType, objectTarget);
            }

            if (semantics is SemanticsKind.ObjRef or SemanticsKind.ObjUniq && IsObjectSemantics(operandType.Semantics))
            {
                if (semantics == SemanticsKind.ObjUniq && ObjectTypes.HandleMode(operandType) is { PayloadAuthority: LoanRequirement.Ref })
                {
                    return this.FailObjectAuthority(conversion, conversion.Left);
                }

                var pattern = this.InternType(BoundTypeKind.Semantics, null, semantics, [operandType.Components[0]]);
                if (!this.AdaptObjectBorrow(conversion.Left, pattern, operandType, scope, true, out var adapted, out _, out _))
                {
                    return this.Fail(conversion, semantics == SemanticsKind.ObjUniq ? AccessFailure(conversion.Left) : BindingFailure.InvalidAssignment);
                }

                Complete(conversion.Right, adapted);
                conversion.ConversionBinding = ConversionBinding.Borrow;
                return Complete(conversion, adapted);
            }

            if (semantics is SemanticsKind.Ref or SemanticsKind.Uniq && ElementAccess.IsRawPlace(conversion.Left))
            {
                return this.BorrowRawPlace(conversion, semantics, operandType);
            }

            if (semantics is SemanticsKind.Ref or SemanticsKind.Uniq && BorrowsWrittenSlot(operandType))
            {
                // SPEC 13.5.5.2: @ref/@uniq borrow the immediately written slot whatever it stores; a stored
                // reference is Reborrowed only through @follow or at a fixed expected Type (SPEC 10.2).
                var pattern = this.InternType(BoundTypeKind.Semantics, null, semantics, [operandType]);
                if (!this.AdaptInput(conversion.Left, pattern, operandType, scope, null, null, out var adapted, out _, out _, explicitBorrow: true))
                {
                    return this.Fail(conversion, semantics == SemanticsKind.Uniq ? AccessFailure(conversion.Left) : BindingFailure.InvalidAssignment);
                }

                Complete(conversion.Right, adapted);
                conversion.ConversionBinding = ConversionBinding.Borrow;
                return Complete(conversion, adapted);
            }

            // SPEC 13.5.3: bare @owner names no Core and is not an operation; @copy, @move or a complete
            // target such as @owner/T states the acquisition.
            if (semantics == SemanticsKind.Owner)
            {
                Complete(conversion.Right, operandType);
                return this.Fail(conversion, BindingFailure.BareOwningShorthand);
            }

            // SPEC 13.5.5.2: @objref and @objuniq borrow an object payload; another operand has none, as for @objref/T.
            if (semantics is SemanticsKind.ObjRef or SemanticsKind.ObjUniq && !ReferenceEquals(operandType, BoundType.Never))
            {
                Complete(conversion.Right, operandType);
                return this.Fail(conversion, BindingFailure.InvalidAssignment);
            }

            this.Fail(conversion.Right, BindingFailure.Unsupported, true);
            return this.Fail(conversion, BindingFailure.Unsupported, true);
        }

        if (syntax is TypeSemanticsKoto { ConversionOperation: { } operation, Type: { } argument })
        {
            return this.BindWrapConversion(conversion, scope, argument, operation);
        }

        if (syntax is TypeSemanticsKoto { Type: null } or IdentifierNameKoto &&
            this.LookupAdaptationTarget(syntax, scope, out var genericSemantics) is { } designation && genericSemantics)
        {
            return this.BindSemanticsAdaptation(conversion, scope, designation);
        }

        var target = this.BindType(conversion.Right, scope);
        if (target is not null)
        {
            target = this.SubstituteIdentityPremises(target, scope);
            Complete(conversion.Right, target);
        }

        // Explicit owner targets use the same normalized numeric/identity operation.
        // Borrow and other ownership adaptations retain their separate rules.
        var plain = syntax is not TypeSemanticsKoto { Type: not null, IsTransparentWrapper: false } explicitSemantics ||
            (explicitSemantics.SemanticsKind == SemanticsKind.Owner && explicitSemantics.SemanticsParameter is null);
        var operand = KotoHelper.UnwrapParentheses(conversion.Left);
        var signed = KotoHelper.SignedNumber(operand, out _);
        var literal = signed is { IsInteger: true };
        var floatingLiteral = signed is { IsInteger: false };
        if (plain && floatingLiteral && target is { IsInteger: true } && ScalarTypes.Width(target, this.compilation.PointerWidth) != 0)
        {
            return this.BindTruncatedLiteral(conversion, target, operand);
        }

        var fit = plain && ((literal && (target is { HasIntegerArithmetic: true } || this.TakesGenericLiterals(target, scope))) || (target is { IsFloatingPoint: true } && (literal || floatingLiteral)));
        var previousLiteral = this.floatingIntegerLiteral;
        BoundType? source;
        try
        {
            this.floatingIntegerLiteral = fit && literal && target is { IsFloatingPoint: true }
                ? operand as NumberLiteralKoto ?? ((UnaryKoto)operand).Operand as NumberLiteralKoto : null;
            // SPEC 5.4: an integer literal pointer-cast input is first fitted to usize; null@raw/U is Typed Null Formation, a null
            // of the target Type (SPEC 5.1).
            var pointerInput = !ReferenceTypes.IsPointer(target) ? null : literal ? BoundType.USize : operand is NullLiteralKoto ? target : null;
            source = this.BindNode(conversion.Left, scope, fit ? target : pointerInput);
        }
        finally
        {
            this.floatingIntegerLiteral = previousLiteral;
        }

        if (source is null || target is null)
        {
            return Complete(conversion, null);
        }

        if (ReferenceEquals(source, BoundType.Never))
        {
            conversion.ConversionBinding = ConversionBinding.Abrupt;
            return Complete(conversion, BoundType.Never);
        }

        if (TryAdaptationSelector(target, out _) || TryAdaptationSelector(source, out _))
        {
            return this.BindCaseAdaptation(conversion, scope, source, target);
        }

        if (ObjectTypes.HandleMode(target) is not null)
        {
            return this.BindObjectAdaptation(conversion, scope, source, target);
        }

        // SPEC 13.5.2: the operand of a numeric conversion, or of an Identity Acquisition to a read Type, is a read position, so
        // safe reference layers ending in a read Type supply its value; borrow, follow and object targets took the reference above.
        if (plain)
        {
            source = this.ReadReferent(conversion.Left, source)!;
        }

        if (ReferenceTypes.IsPointer(source) || ReferenceTypes.IsPointer(target))
        {
            // SPEC 5.4-5.5: a pointer converts to another pointer Type or usize, and usize to a pointer.
            if ((ReferenceTypes.IsPointer(source) && (ReferenceTypes.IsPointer(target) || ReferenceEquals(target, BoundType.USize))) ||
                (ReferenceEquals(source, BoundType.USize) && ReferenceTypes.IsPointer(target)))
            {
                conversion.ConversionBinding = ConversionBinding.Pointer;
                return Complete(conversion, target);
            }

            return this.Fail(conversion, BindingFailure.TypeMismatch);
        }

        if (!plain)
        {
            return this.Fail(conversion, BindingFailure.Unsupported, true);
        }

        // SPEC 13.5.3: an explicitly written owning Semantics on an unchanged Type is the same-Type
        // acquisition, including for numeric values; it is neither a transfer nor a numeric conversion.
        if (ReferenceEquals(source, target) && source.Semantics == SemanticsKind.Owner &&
            syntax is TypeSemanticsKoto { Type: not null, IsTransparentWrapper: false, SemanticsKind: SemanticsKind.Owner, SemanticsParameter: null })
        {
            return this.CompleteIdentity(conversion, target);
        }

        // SPEC 8.4.7.3: a checked conversion between Types that satisfy PrimitiveInteger, including unbound ones, and the
        // conversions between a proven T and Wrapping<T>, including a generic literal fitted to Wrapping<T>.
        if ((this.IsGenericInteger(source, scope) && this.IsIntegerOperand(target, scope)) || (source.IsInteger && this.IsGenericInteger(target, scope)) ||
            (this.IsGenericWrapping(source, scope) && (ReferenceEquals(target, source) || ReferenceEquals(target, source.Components[0]))) ||
            (this.IsGenericWrapping(target, scope) && ReferenceEquals(source, target.Components[0])))
        {
            conversion.ConversionBinding = fit ? ConversionBinding.Literal : ConversionBinding.Integer;
            return Complete(conversion, target);
        }

        // SPEC 13.5.4.1: the only numeric conversion that involves a wrapping integer Type is between U and Wrapping<U>, which
        // keeps the value and the bits; a different integer argument or a floating-point Type is a static error, so that a
        // conversion whose name suggests wrapping never Aborts at runtime.
        if (source.IsWrappingInteger || target.IsWrappingInteger)
        {
            if (!ReferenceEquals(source.Underlying, target.Underlying))
            {
                return this.FailMismatch(conversion, conversion, source, target);
            }

            conversion.ConversionBinding = fit ? ConversionBinding.Literal : ConversionBinding.Integer;
            return Complete(conversion, target);
        }

        if (source.IsNumeric && target.IsNumeric)
        {
            if (source.IsFloatingPoint && target.IsFloatingPoint)
            {
                conversion.ConversionBinding = fit ? ConversionBinding.Literal : ConversionBinding.Floating;
                return Complete(conversion, target);
            }

            // SPEC 13.5.4: an integer conversion is checked against the target's range, whatever its width; only lowering
            // needs the width of isize and usize.
            if (!(source.IsInteger && target.IsInteger) &&
                (ScalarTypes.Width(source, this.compilation.PointerWidth) == 0 || ScalarTypes.Width(target, this.compilation.PointerWidth) == 0))
            {
                var integer = source.IsInteger ? source : target;
                if (ScalarTypes.Width(integer, this.compilation.PointerWidth) is > 0 and <= 64)
                {
                    conversion.ConversionBinding = ConversionBinding.Numeric;
                    return Complete(conversion, target);
                }

                return this.Fail(conversion, BindingFailure.Unsupported, true);
            }

            conversion.ConversionBinding = fit ? ConversionBinding.Literal : ConversionBinding.Integer;
            return Complete(conversion, target);
        }

        if (ReferenceEquals(source, target))
        {
            // A Type-only target Copies and never transfers a Non-Copy Place.
            if (SupportsIdentityAcquisition(source))
            {
                return this.CompleteIdentity(conversion, target);
            }
        }

        // SPEC 13.5.3: only a numeric conversion changes an owned Core, so an owned Core that is primitive on one side only, or a
        // different position or range Type, has no defined operation: a Type error, not an unimplemented form.
        if (!ReferenceEquals(source, target) && source.Semantics == SemanticsKind.Owner && target.Semantics == SemanticsKind.Owner &&
            ((source.Kind == BoundTypeKind.Primitive) != (target.Kind == BoundTypeKind.Primitive) || (IsPositionOrRangeType(source) && IsPositionOrRangeType(target))))
        {
            return this.FailMismatch(conversion, conversion, source, target);
        }

        // SPEC 13.5.3: an owned target never extracts its Core through a safe reference (only the value read of a read Type does,
        // above). Other ownership/borrow adaptations require their own verified paths.
        var unsupported = !SupportsIdentityAcquisition(target) || (!SupportsIdentityAcquisition(source) && source.Semantics is not (SemanticsKind.Ref or SemanticsKind.Uniq));
        return this.Fail(conversion, unsupported ? BindingFailure.Unsupported : BindingFailure.TypeMismatch, unsupported);
    }
}
