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

    private BindingScope? conversionEvidenceScope;
    private NumberLiteralKoto? floatingIntegerLiteral;

    internal static bool SupportsIdentityAcquisition(BoundType type)
        => type.HandleMode is not null ||
            type.Semantics is SemanticsKind.Owner or SemanticsKind.Raw;

    // SPEC 13.5.3: E@copy is bound as the Identity acquisition of a proven-Copy value.
    internal static bool IsCopyOperation(ConversionKoto conversion)
        => ConversionTargetSyntax(conversion) is TypeSemanticsKoto { Type: null, Identifier: Constants.CopyOperation };

    // A Kimi position or range Type other than an integer (SPEC 4.6.1, 4.6.2).
    private static bool IsPositionOrRangeType(BoundType type)
        => type.Symbol?.LibraryDeclaration is KimiDeclarationId.FromEnd or KimiDeclarationId.Start or KimiDeclarationId.End or KimiDeclarationId.Range or
            KimiDeclarationId.ClosedRange or KimiDeclarationId.ResolvedRange;

    // SPEC 13.5: a bare Semantics name or operation directly after @ completes the operation. A grouped target is a Type,
    // so a grouped bare name such as (ref) is bound, and rejected, as a Type; grouping stays transparent for complete targets.
    // Bare @owner remains invalid; object shorthands select their payload target from the input (SPEC 13.6.2).
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

    // SPEC 13.5.6: @ref, @uniq and the borrow of @raw (SPEC 5.4) borrow the immediately written slot whatever it stores, Never too.
    private static bool BorrowsWrittenSlot(BoundType type)
        => AdtDef.IsStruct(type) || AdtDef.IsEnum(type) || type.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Tuple or BoundTypeKind.Closure or BoundTypeKind.Function or BoundTypeKind.FunctionItem or BoundTypeKind.Array or BoundTypeKind.Dictionary or BoundTypeKind.Slice or BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.SemanticsAdaptation ||
            ReferenceTypes.IsStorage(type) || ReferenceTypes.IsPointer(type) || ScalarTypes.Supports(type) || IsRuntimeRecordType(type) ||
            IsBorrow(type.Semantics) || IsObjectSemantics(type.Semantics);

    // Unit, string and Never: the intrinsic Types the runtime's records lay out (IMPL 21.1.3), which ScalarTypes does not cover.
    private static bool IsRuntimeRecordType(BoundType type)
        => ReferenceEquals(type, BoundType.Unit) || ReferenceEquals(type, BoundType.String) || ReferenceEquals(type, BoundType.Never);

    // SPEC 14.9.3, 14.10.3 (DECISIONS BU-6): an operand of Type Never that is no Place is a Never value, which a conversion only passes
    // on (Abrupt); an explicit acquisition of a Place of Type Never (a move, a copy, a borrow) keeps its operation and its effect.
    private static bool IsNeverValue(Koto operand, BoundType type)
        => ReferenceEquals(type, BoundType.Never) && !IsBarePlace(operand) && !ElementAccess.IsRawPlace(operand);

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

        conversion.ConversionBinding = ConversionBinding.Identity;
        return Complete(conversion, type);
    }

    // SPEC 13.5.3: @move of a Place of Type Never keeps its Move (IsNeverValue; SPEC 14.10.3: x@move Moves x on an assumed path),
    // whose Type alone makes it supply no normal completion (SPEC 14.9.3).
    private BoundType? CompleteTransfer(ConversionKoto conversion, BoundType type, BindingScope scope)
    {
        // SPEC 6.3.4: a special receiver's part may give up no Non-Copy value (Receiver).
        if (!OffersTake(conversion.Left) ||
            (ReceiverOfPart(conversion.Left) is not null && this.ProveCopy(type, conversion) != ConstraintProof.Proven))
        {
            return this.Fail(conversion, AccessFailure(conversion.Left, take: true));
        }

        if (this.HolderPlaceFailure(conversion.Left, HolderAccess.Move, null, conversion))
        {
            return null;
        }

        // SPEC 11.2: consuming var storage requires its accessible standard setter,
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

        conversion.ConversionBinding = IsNeverValue(conversion.Left, type) ? ConversionBinding.Abrupt : ConversionBinding.Transfer;
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

    private BoundType? BindConversion(ConversionKoto conversion, BindingScope scope, BoundType? expected = null)
    {
        var syntax = ConversionTargetSyntax(conversion);
        if (syntax is TypeSemanticsKoto follow && follow.Names(Constants.FollowOperation))
        {
            // SPEC 13.5.5: E@follow selects the referent Place of a ref/uniq value, or the complete payload of a
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

            if (reference is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components: [var referent] })
            {
                Complete(conversion.Right, referent);
                conversion.ConversionBinding = ConversionBinding.Follow;
                return Complete(conversion, referent);
            }

            if (IsObjectSemantics(reference.Semantics) && reference.Components.Count == 1 &&
                this.ProveSealed(reference.Components[0], scope) == ConstraintProof.Proven)
            {
                Complete(conversion.Right, reference.Components[0]);
                conversion.ConversionBinding = ConversionBinding.PayloadFollow;
                return Complete(conversion, reference.Components[0]);
            }

            if (TryPairLayer(reference, out _, out _))
            {
                // SPEC 13.5.5: a pair layer is followed only when its admitted set lies in value or valueborrow.
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

        if (syntax is TypeSemanticsKoto raw && raw.Names(Constants.RawKeyword))
        {
            return this.BindAddress(conversion, scope);
        }

        if (syntax is TypeSemanticsKoto { Type: not null, SemanticsParameter: null, SemanticsKind: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq })
        {
            var pattern = this.BindType(conversion.Right, scope, this.TypeContext(conversion.Right, scope) with { SuppressOuter = true });
            var actual = this.BindNode(conversion.Left, scope);
            if (pattern is null || actual is null ||
                (IsExclusive(pattern.Semantics) && this.HolderPlaceFailure(conversion.Left, HolderAccess.ExclusiveBorrow, null, conversion)))
            {
                return Complete(conversion, null);
            }

            if (IsNeverValue(conversion.Left, actual))
            {
                conversion.ConversionBinding = ConversionBinding.Abrupt;
                return Complete(conversion, actual);
            }

            if (TryAdaptationSelector(actual, out _) && IsBorrow(pattern.Semantics))
            {
                var origin = pattern.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq
                    ? this.SlotOrigin(conversion.Left) : this.PlaceOrigin(conversion.Left);
                var borrowed = this.types.Reference(pattern.Semantics, pattern.Components[0], pattern.Origin ?? origin);
                return this.BindCaseAdaptation(conversion, scope, actual, borrowed);
            }

            if (pattern.Semantics == SemanticsKind.ObjUniq && actual.HandleMode is { PayloadAuthority: LoanRequirement.Ref })
            {
                return this.FailObjectAuthority(conversion, conversion.Left);
            }

            if (pattern.IsObjectBorrow && (actual.HandleMode is not null || actual.IsObjectBorrow) &&
                !ReferenceEquals(actual.Components[0], pattern.Components[0]))
            {
                return this.BindObjectUpcast(conversion, scope, actual, pattern);
            }

            // SPEC 13.5.6: a typed borrow names exactly the stored Type of the written slot; it selects no
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
                var storage = this.types.Intern(BoundTypeKind.Semantics, null, pattern.Semantics, [actual], origin: this.SlotOrigin(conversion.Left));
                Complete(conversion.Right, storage);
                conversion.ConversionBinding = ConversionBinding.Borrow;
                return Complete(conversion, storage);
            }

            BoundType adapted;
            var fits = pattern.IsObjectBorrow
                ? this.AdaptObjectBorrow(conversion.Left, pattern, actual, scope, true, out adapted, out _, out _)
                : this.AdaptInput(conversion.Left, pattern, actual, scope, null, null, out adapted, out _, out _, explicitBorrow: true);
            if (!fits ||
                !IsSubtype(adapted.Components[0], pattern.Components[0]) ||
                (pattern.Origin is not null && !this.CheckTypeUse(adapted, pattern, conversion)))
            {
                return this.Fail(conversion, this.BorrowFailure(conversion.Left, scope, pattern.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq));
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
            if (this.ProveCopy(copied, conversion) != ConstraintProof.Proven)
            {
                return this.Fail(conversion, BindingFailure.NonCopyOperand);
            }

            conversion.ConversionBinding = IsNeverValue(conversion.Left, copied) ? ConversionBinding.Abrupt : ConversionBinding.Identity;
            return Complete(conversion, copied);
        }

        if (syntax is TypeSemanticsKoto { Type: null, HasOrigin: false } shorthand && CompilerHelper.TryParse(shorthand.Identifier, out var semantics))
        {
            // SPEC 10.8: an expected borrow of the same Semantics fits an untyped literal operand to its referent
            // Type. A typed operand keeps its own Type: the borrow or reborrow forms from its Place, never from a read.
            var operandExpectation = semantics is not (SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc) &&
                this.KindOf(conversion.Left) == PartKind.Literal &&
                expected is { Kind: BoundTypeKind.Semantics, Components: [var referent] } && expected.Semantics == semantics ? referent : null;
            var operandType = this.BindNode(conversion.Left, scope, operandExpectation);
            if (operandType is null || (IsExclusive(semantics) && this.HolderPlaceFailure(conversion.Left, HolderAccess.ExclusiveBorrow, null, conversion)))
            {
                return Complete(conversion, null);
            }

            // SPEC 14.9.3: an object creation of a Never operand and a borrow of a Never value only pass it on (IsNeverValue).
            if ((semantics is SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc && ReferenceEquals(operandType, BoundType.Never)) ||
                (semantics is SemanticsKind.Ref or SemanticsKind.Uniq && IsNeverValue(conversion.Left, operandType)))
            {
                Complete(conversion.Right, operandType);
                conversion.ConversionBinding = ConversionBinding.Abrupt;
                return Complete(conversion, operandType);
            }

            if (semantics is SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc)
            {
                var payload = IsObjectSemantics(operandType.Semantics) ? operandType.Components[0] : operandType;
                var objectTarget = this.types.Intern(BoundTypeKind.Semantics, null, semantics, [payload]);
                return this.BindObjectAdaptation(conversion, scope, operandType, objectTarget);
            }

            if (semantics is SemanticsKind.ObjRef or SemanticsKind.ObjUniq && IsObjectSemantics(operandType.Semantics))
            {
                if (semantics == SemanticsKind.ObjUniq && operandType.HandleMode is { PayloadAuthority: LoanRequirement.Ref })
                {
                    return this.FailObjectAuthority(conversion, conversion.Left);
                }

                var pattern = this.types.Intern(BoundTypeKind.Semantics, null, semantics, [operandType.Components[0]]);
                if (!this.AdaptObjectBorrow(conversion.Left, pattern, operandType, scope, true, out var adapted, out _, out _))
                {
                    return this.Fail(conversion, this.BorrowFailure(conversion.Left, scope, semantics == SemanticsKind.ObjUniq));
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
                // SPEC 13.5.6: @ref/@uniq borrow the immediately written slot whatever it stores; a stored
                // reference is Reborrowed only through @follow or at a fixed expected Type (SPEC 10.2).
                var pattern = this.types.Intern(BoundTypeKind.Semantics, null, semantics, [operandType]);
                if (!this.AdaptInput(conversion.Left, pattern, operandType, scope, null, null, out var adapted, out _, out _, explicitBorrow: true))
                {
                    return this.Fail(conversion, this.BorrowFailure(conversion.Left, scope, semantics == SemanticsKind.Uniq));
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

            // SPEC 13.5.6: @objref and @objuniq borrow an object payload; another operand has none, as for @objref/T.
            if (semantics is SemanticsKind.ObjRef or SemanticsKind.ObjUniq && !IsNeverValue(conversion.Left, operandType))
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
            source = this.BindNode(conversion.Left, scope, fit ? target : PointerInput(target, literal, operand));
        }
        finally
        {
            this.floatingIntegerLiteral = previousLiteral;
        }

        if (source is null || target is null)
        {
            return Complete(conversion, null);
        }

        // A conversion of a Never operand passes it on (Abrupt); its same-Type acquisition of a Place is an Identity (IsNeverValue).
        if (ReferenceEquals(source, BoundType.Never) && (!ReferenceEquals(target, source) || IsNeverValue(conversion.Left, source)))
        {
            conversion.ConversionBinding = ConversionBinding.Abrupt;
            return Complete(conversion, BoundType.Never);
        }

        if (TryAdaptationSelector(target, out _) || TryAdaptationSelector(source, out _))
        {
            return this.BindCaseAdaptation(conversion, scope, source, target);
        }

        if (target.HandleMode is not null)
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
            return this.CompletePointerConversion(conversion, source, target);
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

        // SPEC 8.7.4: a checked conversion between Types that satisfy PrimitiveInteger, including unbound ones, and the
        // conversions between a proven T and Wrapping<T>, including a generic literal fitted to Wrapping<T>.
        if ((this.IsGenericInteger(source, scope) && this.IsIntegerOperand(target, scope)) || (source.IsInteger && this.IsGenericInteger(target, scope)) ||
            (this.IsGenericWrapping(source, scope) && (ReferenceEquals(target, source) || ReferenceEquals(target, source.Components[0]))) ||
            (this.IsGenericWrapping(target, scope) && ReferenceEquals(source, target.Components[0])))
        {
            conversion.ConversionBinding = fit ? ConversionBinding.Literal : ConversionBinding.Integer;
            return Complete(conversion, target);
        }

        // SPEC 13.5.4: the only numeric conversion that involves a wrapping integer Type is between U and Wrapping<U>, which
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
