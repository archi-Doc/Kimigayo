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
        => type.Semantics == SemanticsKind.Owner &&
            (type.Kind is BoundTypeKind.Primitive or BoundTypeKind.Tuple or BoundTypeKind.FixedArray || Kimi.Compiler.EnumStorage.IsEnum(type));

    private static Koto ConversionTargetSyntax(ConversionKoto conversion)
    {
        var syntax = conversion.Right;
        while (true)
        {
            if (syntax is ParenthesizedTypeKoto parentheses)
            {
                syntax = parentheses.Type;
            }
            else if (syntax is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner })
            {
                syntax = inner;
            }
            else
            {
                return syntax;
            }
        }
    }

    // SPEC 3.5: a same-Type acquisition Copies a proven-Copy value and transfers a temporary; a Non-Copy Place needs @move.
    private BoundType? CompleteIdentity(ConversionKoto conversion, BoundType type)
    {
        if (IsBarePlace(conversion.Left) && this.ProveCopy(type, conversion) != ConstraintProof.Proven)
        {
            return Fail(conversion, BindingFailure.TransferRequired);
        }

        conversion.ConversionBinding = ReferenceEquals(type, BoundType.Never) ? ConversionBinding.Abrupt : ConversionBinding.Identity;
        return Complete(conversion, type);
    }

    private BoundType? CompleteTransfer(ConversionKoto conversion, BoundType type, BindingScope scope)
    {
        if (KotoHelper.UnwrapParentheses(conversion.Left).BoundSymbol?.Kind == BindingSymbolKind.PatternCandidate ||
            KotoHelper.UnwrapParentheses(conversion.Left) is ConversionKoto { ConversionBinding: ConversionBinding.Deref or ConversionBinding.PayloadDeref })
        {
            return Fail(conversion, BindingFailure.InvalidAssignment); // SPEC 15.1.5: a referent offers no Take.
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
                    return Fail(conversion, BindingFailure.InvalidAssignment);
                }

                if (!this.Accessible(property.Symbol, scope, property.Setter.Access, (source as MemberAccessKoto)?.Left.BoundType))
                {
                    return Fail(conversion, BindingFailure.Access);
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
        var structural = this.resultStructure ??= new(this.ResultNeverEvidence);
        structural.Clear();
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
        if (syntax is TypeSemanticsKoto { Type: null, Identifier: Constants.DerefOperation, HasOrigin: false })
        {
            // SPEC 13.5.5.1: E@deref selects the referent Place of a ref/uniq value, or the complete payload of a
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
                conversion.ConversionBinding = ConversionBinding.Deref;
                return Complete(conversion, reference.Components[0]);
            }

            if (IsObjectSemantics(reference.Semantics) && reference.Components.Count == 1 &&
                this.RequestCapability(reference.Components[0], this.Library.Sealed, scope) == ConstraintProof.Proven)
            {
                Complete(conversion.Right, reference.Components[0]);
                conversion.ConversionBinding = ConversionBinding.PayloadDeref;
                return Complete(conversion, reference.Components[0]);
            }

            return Fail(conversion, BindingFailure.TypeMismatch);
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

            if (ObjectTypes.IsBorrow(pattern) && (ObjectTypes.IsOwner(actual) || ObjectTypes.IsBorrow(actual)) &&
                !ReferenceEquals(actual.Components[0], pattern.Components[0]))
            {
                return this.BindObjectUpcast(conversion, scope, actual, pattern);
            }

            // SPEC 13.5.5.2: a typed borrow names exactly the stored Type of the written slot; it selects no
            // referent, copies no same-Type reference and never Reborrows. Payloads are selected with @deref.
            if (pattern.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq && !ReferenceEquals(actual, pattern.Components[0]))
            {
                return Fail(conversion, BindingFailure.TypeMismatch);
            }

            if (pattern.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq && pattern.Origin is null && ReferenceEquals(actual, pattern.Components[0]) &&
                this.BorrowablePlace(conversion.Left, scope, pattern.Semantics == SemanticsKind.Uniq))
            {
                var storage = this.InternType(BoundTypeKind.Semantics, null, pattern.Semantics, [actual], origin: this.PlaceOrigin(conversion.Left));
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
                return Fail(conversion, BindingFailure.InvalidAssignment);
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

        if (syntax is TypeSemanticsKoto { Type: null, HasOrigin: false } shorthand && CompilerHelper.TryParse(shorthand.Identifier, out var semantics))
        {
            // SPEC 10.8: an expected borrow of the same Semantics fits an untyped literal operand to its referent
            // Type. A typed operand keeps its own Type: the borrow or reborrow forms from its Place, never from a read.
            var operandExpectation = IsUnfittedLiteral(conversion.Left) && expected is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } && expected.Semantics == semantics ? expected.Components[0] : null;
            var operandType = this.BindNode(conversion.Left, scope, operandExpectation);
            if (operandType is null)
            {
                return Complete(conversion, null);
            }

            if (semantics is SemanticsKind.ObjRef or SemanticsKind.ObjUniq && IsObjectSemantics(operandType.Semantics))
            {
                var pattern = this.InternType(BoundTypeKind.Semantics, null, semantics, [operandType.Components[0]]);
                if (!this.AdaptObjectBorrow(conversion.Left, pattern, operandType, scope, true, out var adapted, out _, out _))
                {
                    return Fail(conversion, BindingFailure.InvalidAssignment);
                }

                Complete(conversion.Right, adapted);
                conversion.ConversionBinding = ConversionBinding.Borrow;
                return Complete(conversion, adapted);
            }

            if (semantics is SemanticsKind.Ref or SemanticsKind.Uniq &&
                (StructStorage.IsStruct(operandType) || Compiler.EnumStorage.IsEnum(operandType) || operandType.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Tuple or BoundTypeKind.Closure or BoundTypeKind.Array or BoundTypeKind.Dictionary or BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection || ReferenceTypes.IsStorage(operandType) ||
                    ScalarTypes.Supports(operandType) || ReferenceEquals(operandType, BoundType.Unit) || ReferenceEquals(operandType, BoundType.String) || IsBorrow(operandType.Semantics) || IsObjectSemantics(operandType.Semantics)))
            {
                // SPEC 13.5.5.2: @ref/@uniq borrow the immediately written slot whatever it stores; a stored
                // reference is Reborrowed only through @deref or at a fixed expected Type (SPEC 10.2).
                var pattern = this.InternType(BoundTypeKind.Semantics, null, semantics, [operandType]);
                if (!this.AdaptInput(conversion.Left, pattern, operandType, scope, null, null, out var adapted, out _, out _, explicitBorrow: true))
                {
                    return Fail(conversion, BindingFailure.InvalidAssignment);
                }

                Complete(conversion.Right, adapted);
                conversion.ConversionBinding = ConversionBinding.Borrow;
                return Complete(conversion, adapted);
            }

            // SPEC 13.5.3: an owning-Semantics spelling that matches the operand's outer Semantics is the ordinary
            // same-Type acquisition: a Copy of a Copy value, the transfer of a temporary, never a transfer from a Place.
            if (semantics is SemanticsKind.Owner or SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc && operandType.Semantics == semantics)
            {
                for (var targetNode = conversion.Right; ;)
                {
                    Complete(targetNode, operandType);
                    if (ReferenceEquals(targetNode, syntax))
                    {
                        break;
                    }

                    targetNode = targetNode is ParenthesizedTypeKoto parentheses ? parentheses.Type : ((TypeSemanticsKoto)targetNode).Type!;
                }

                return this.CompleteIdentity(conversion, operandType);
            }

            Fail(conversion.Right, BindingFailure.Unsupported, true);
            return Fail(conversion, BindingFailure.Unsupported, true);
        }

        var target = this.BindType(conversion.Right, scope);

        // Explicit owner targets use the same normalized numeric/identity operation.
        // Borrow and other ownership adaptations retain their separate rules.
        var plain = syntax is not TypeSemanticsKoto { Type: not null, IsTransparentWrapper: false } explicitSemantics ||
            (explicitSemantics.SemanticsKind == SemanticsKind.Owner && explicitSemantics.SemanticsParameter is null);
        var operand = KotoHelper.UnwrapParentheses(conversion.Left);
        var literal = operand is NumberLiteralKoto { IsInteger: true } or
            PrefixMinusKoto { Operand: NumberLiteralKoto { IsInteger: true } } or
            PrefixPlusKoto { Operand: NumberLiteralKoto { IsInteger: true } };
        var floatingLiteral = operand is NumberLiteralKoto { IsInteger: false } or
            PrefixMinusKoto { Operand: NumberLiteralKoto { IsInteger: false } } or
            PrefixPlusKoto { Operand: NumberLiteralKoto { IsInteger: false } };
        var fit = plain && ((target is { IsInteger: true } && literal) || (target is { IsFloatingPoint: true } && (literal || floatingLiteral)));
        var previousLiteral = this.floatingIntegerLiteral;
        BoundType? source;
        try
        {
            this.floatingIntegerLiteral = fit && literal && target is { IsFloatingPoint: true }
                ? operand as NumberLiteralKoto ?? ((UnaryKoto)operand).Operand as NumberLiteralKoto : null;
            // SPEC 5.4: an integer literal pointer-cast input is first fitted to usize.
            source = this.BindNode(conversion.Left, scope, fit ? target : literal && ReferenceTypes.IsPointer(target) ? BoundType.USize : null);
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

        if (ReferenceTypes.IsPointer(source) || ReferenceTypes.IsPointer(target))
        {
            // SPEC 5.4-5.5: a pointer converts to another pointer Type or usize, and usize to a pointer.
            if ((ReferenceTypes.IsPointer(source) && (ReferenceTypes.IsPointer(target) || ReferenceEquals(target, BoundType.USize))) ||
                (ReferenceEquals(source, BoundType.USize) && ReferenceTypes.IsPointer(target)))
            {
                conversion.ConversionBinding = ConversionBinding.Pointer;
                return Complete(conversion, target);
            }

            return Fail(conversion, BindingFailure.TypeMismatch);
        }

        if (ObjectTypes.IsOwner(target) && (ObjectTypes.IsOwner(source) || ObjectTypes.IsBorrow(source)))
        {
            return this.BindObjectUpcast(conversion, scope, source, target);
        }

        if (!plain)
        {
            return Fail(conversion, BindingFailure.Unsupported, true);
        }

        // SPEC 13.5.3: an explicitly written owning Semantics on an unchanged Type is the same-Type
        // acquisition, including for numeric values; it is neither a transfer nor a numeric conversion.
        if (ReferenceEquals(source, target) && source.Semantics == SemanticsKind.Owner &&
            syntax is TypeSemanticsKoto { Type: not null, IsTransparentWrapper: false, SemanticsKind: SemanticsKind.Owner, SemanticsParameter: null })
        {
            return this.CompleteIdentity(conversion, target);
        }

        if (source.IsNumeric && target.IsNumeric)
        {
            if (source.IsFloatingPoint && target.IsFloatingPoint)
            {
                conversion.ConversionBinding = fit ? ConversionBinding.Literal : ConversionBinding.Floating;
                return Complete(conversion, target);
            }

            if (ScalarTypes.Width(source, this.compilation.PointerWidth) == 0 || ScalarTypes.Width(target, this.compilation.PointerWidth) == 0)
            {
                var integer = source.IsInteger ? source : target;
                if (ScalarTypes.Width(integer, this.compilation.PointerWidth) is > 0 and <= 64)
                {
                    conversion.ConversionBinding = ConversionBinding.Numeric;
                    return Complete(conversion, target);
                }

                return Fail(conversion, BindingFailure.Unsupported, true);
            }

            conversion.ConversionBinding = fit ? ConversionBinding.Literal : ConversionBinding.Integer;
            return Complete(conversion, target);
        }

        if (ReferenceEquals(source, target))
        {
            // A Type-only target Copies and never transfers a Non-Copy Place.
            if (SupportsIdentityAcquisition(source))
            {
                if (IsBarePlace(conversion.Left) && this.ProveCopy(source, conversion) != ConstraintProof.Proven)
                {
                    return Fail(conversion, BindingFailure.TransferRequired);
                }

                conversion.ConversionBinding = ConversionBinding.Identity;
                return Complete(conversion, target);
            }
        }

        // Other ownership/borrow adaptations require their own verified paths.
        var unsupported = !SupportsIdentityAcquisition(source) || !SupportsIdentityAcquisition(target);
        return Fail(conversion, unsupported ? BindingFailure.Unsupported : BindingFailure.TypeMismatch, unsupported);
    }
}
