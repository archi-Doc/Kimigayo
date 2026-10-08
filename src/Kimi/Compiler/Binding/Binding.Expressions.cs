// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 6.4, 14.6.1, 14.8.2: var locals, var Pattern bindings and var iteration slots are reassignable.
    internal static bool IsMutableDeclaration(Koto? declaration)
        => declaration is VariableKoto { VariableKind: VariableKind.Var } or SyntaxFormKoto { IsMutablePattern: true } ||
            (declaration is IdentifierNameKoto { Parent: ForKoto loop } slot && loop.IsMutableSlot(slot));

    private int defaultBindingDepth;

    // SPEC 15.1.6: a pattern or for binding on a shared or exclusive path is a reference; a value of its referent Type
    // assigned to it names that mode and the spellings that bind or update a value instead. A binding whose stored Type
    // is itself a reference, on an owned path, gets the ordinary mismatch. A guard candidate is a shared layer that
    // grants Read only (SPEC 14.8.3).
    private static BindingFailure? ReferenceBindingAssignment(Koto target)
        => KotoHelper.UnwrapParentheses(target) is IdentifierNameKoto { BoundSymbol.Kind: BindingSymbolKind.PatternCandidate } ? BindingFailure.SharedPathAccess
            : KotoHelper.UnwrapParentheses(target) is IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local, Type: { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } type } symbol } &&
                (symbol.BindsReference || symbol.Declaration is IdentifierNameKoto { Parent: ForKoto { Mode: not SubjectMode.ByValue } })
            ? type.Semantics == SemanticsKind.Uniq ? BindingFailure.ExclusiveBindingAssignment : BindingFailure.SharedBindingAssignment : null;

    private static bool Compatible(BoundType actual, BoundType expected) => FitsType(actual, expected);

    private static bool Writable(Koto node)
    {
        node = KotoHelper.UnwrapParentheses(node);
        if (node.BoundSymbol?.Property is { IsStored: false, Setter.IsPresent: true })
        {
            return true; // The accessor's Receiver Expression checks its own capability.
        }

        // SPEC 5.2: binding mutability does not decide pointee write permission.
        if (node is DereferenceKoto dereference)
        {
            return ReferenceTypes.IsPointer(dereference.Operand.BoundType);
        }

        if ((node is IndexKoto index && ReferenceTypes.IsPointer(index.Left.BoundType)) || ElementAccess.IsPointerPath(node))
        {
            return true;
        }

        if (node.BoundSymbol?.Kind == BindingSymbolKind.PatternCandidate || ReachedThroughShared(node))
        {
            return false; // SPEC 3.4, 15.6.2: a shared layer anywhere on the path grants Read only.
        }

        if (node is ConversionKoto { ConversionBinding: ConversionBinding.Follow } selected)
        {
            // SPEC 13.5.5.1: the referent of uniq/T offers Write; a shared layer on the path bounds it.
            return selected.Left.BoundType?.Semantics == SemanticsKind.Uniq && !ReachedThroughShared(selected.Left);
        }

        if (node is ConversionKoto { ConversionBinding: ConversionBinding.PairFollow } pair)
        {
            return pair.CodeContext.Compilation.Binding.PairCapability(pair, null, true); // SPEC 13.5.5.1 pair layers
        }

        if (node is ConversionKoto { ConversionBinding: ConversionBinding.PayloadFollow } payload)
        {
            return payload.Left.BoundType?.Semantics switch
            {
                SemanticsKind.ObjUniq => !ReachedThroughShared(payload.Left),
                SemanticsKind.Obj => Writable(payload.Left),
                _ => false,
            };
        }

        if (node is InvocationKoto placeCall && ElementAccess.PlaceCallReference(placeCall) is { } published)
        {
            return published.Semantics == SemanticsKind.Uniq; // SPEC 7.1.1: place uniq/T offers Write; place ref/T does not.
        }

        if (node is IndexKoto userIndex && ElementAccess.IsUserIndex(userIndex))
        {
            return ElementAccess.IndexerCall(userIndex, true) is not null; // SPEC 4.6.9: an update selects indexUniq.
        }

        if (ElementAccess.IsExclusiveArrayElement(node))
        {
            return true;
        }

        if (node is MemberAccessKoto { Right: NumberLiteralKoto } nested && !ReferenceTypes.IsTuple(ElementAccess.AccessType(nested.Left)) &&
            ElementAccess.BorrowedPathRoot(nested) is { } root)
        {
            return ElementAccess.AccessType(root, true)!.Semantics == SemanticsKind.Uniq; // An inline Tuple level below a borrowed base.
        }

        if (node is MemberAccessKoto tupleElement && ReferenceTypes.IsTuple(ElementAccess.AccessType(tupleElement.Left)))
        {
            return ElementAccess.AccessType(tupleElement.Left, true)!.Semantics == SemanticsKind.Uniq && ElementAccess.TryBorrowedTupleElement(tupleElement, out _, out _);
        }

        if (node is MemberAccessKoto { BoundSymbol.Property.IsStored: true } throughLayers &&
            node.CodeContext.Compilation.Binding.TryGetAdaptation(throughLayers.Left, out var receiver) && receiver.Kind == ExpectedAdaptationKind.ReferenceRead)
        {
            return receiver.Type.Semantics == SemanticsKind.Uniq; // SPEC 3.4.1: a shared layer bounds the path to shared access.
        }

        if (node is MemberAccessKoto { BoundSymbol.Property.IsStored: true } field &&
            (StructStorage.IsStruct(field.Left.BoundType) || ObjectTypes.HandleMode(field.Left.BoundType) is not null) &&
            !IsSpecialField(field, out _) && !Writable(field.Left) && ElementAccess.WritableRoot(field.Left) is null)
        {
            return false; // A mutable field still requires a mutable owning root.
        }

        if (node.BoundSymbol is { Kind: BindingSymbolKind.Storage, Scope.Owner: PropertyAccessorKoto syntax })
        {
            var accessor = Accessor(syntax);
            if (accessor.Receiver is { Semantics: not SemanticsKind.Uniq })
            {
                return false;
            }
        }

        return node.BoundSymbol?.MutableCapture == true || IsMutableDeclaration(node.BoundSymbol?.Declaration);
    }

    // SPEC 3.5: the positions without a fixed expected Type that acquire any Place operand by bare acquisition.
    // Arguments, assignment sources and results take their expected Type from the
    // declaration and adapt there instead; an anonymous function's expression body without a written or expected result
    // Type is a result source (SPEC 14.9.1) whose Type the bare acquisition infers.
    private static bool IsBareAcquisitionPosition(Koto node)
    {
        var target = node;
        while (target.Parent is ParenthesizedKoto parenthesized)
        {
            target = parenthesized;
        }

        return target.Parent switch
        {
            VariableKoto variable => ReferenceEquals(variable.InitializerKoto, target),
            DiscardKoto discard => ReferenceEquals(discard.Operand, target),
            TupleLiteralKoto or ArrayLiteralKoto => true,
            FunctionKoto { IsAnonymous: true } function => ReferenceEquals(function.ExpressionBody, target),
            _ => false,
        };
    }

    // SPEC 7.7: an unsafe function supports direct calls only, so every appearance of its name
    // that is not the callee of a direct call acquires it as a value. This is the complement of
    // the callee position, not a list of acquisition positions, so no position stays unchecked.
    private static bool IsValueUse(Koto node)
        => !TryNameRoot(node, out var target) || target.Parent is not InvocationKoto invocation || !ReferenceEquals(invocation.Method, target);

    // Parentheses, the member-access right side and an explicit type-argument list stay part of the
    // referenced name. Any other enclosing node consumes the name itself. A name used as a
    // member-access receiver is already a value use, so it has no enclosing name root.
    private static bool TryNameRoot(Koto node, out Koto root)
    {
        var target = node;
        while (true)
        {
            switch (target.Parent)
            {
                case ParenthesizedKoto:
                case MemberAccessKoto member when ReferenceEquals(member.Right, target):
                case GenericsKoto generics when ReferenceEquals(generics.Identifier, target):
                    target = target.Parent!;
                    continue;
                case MemberAccessKoto:
                    root = target;
                    return false;
                default:
                    root = target;
                    return true;
            }
        }
    }

    private static bool CanInitializeLocal(Koto node, BindingScope scope)
    {
        node = KotoHelper.UnwrapParentheses(node);
        if (node.BoundSymbol is not { Kind: BindingSymbolKind.Local, Declaration: FieldKoto, Scope: var declarationScope })
        {
            return false;
        }

        // Source scopes deliberately have no function owner. Nested functions cannot assign
        // these locals; CFG alone decides first placement for a local in its own body.
        return ReferenceEquals(declarationScope.Function, scope.Function);
    }

    // SPEC 12.3.1: an unfitted literal is an untyped literal or literal-only expression still waiting for its Type.
    private static bool IsUnfittedLiteral(Koto node)
    {
        node = KotoHelper.UnwrapParentheses(node);
        return node.BoundType is null &&
            (node is NumberLiteralKoto or NullLiteralKoto || node is PrefixMinusKoto { Operand: NumberLiteralKoto } or PrefixPlusKoto { Operand: NumberLiteralKoto } ||
            IsLiteralOnlyOperation(node) || IsLiteralOnlyFromEnd(node) || IsLiteralOnlyRange(node));
    }

    // SPEC 13.3: a primitive without arithmetic (bool, Unit, char, string) or safe reference layers ending in string. Never fits
    // every operator, and a pointer operand is displaced by SPEC 5.3 before this test.
    private static bool NonNumericOperand(BoundType type)
        => (type.Kind == BoundTypeKind.Primitive && !type.IsNumeric && !ReferenceEquals(type, BoundType.Never)) || ReferenceTypes.EndsInString(type);

    // SPEC 13.2, 13.3: a numeric Type without the integer operators (%, bitwise, shift, increment, decrement), a floating-point Type.
    private static bool NonIntegerOperand(BoundType type) => type.IsNumeric && !type.HasIntegerArithmetic;

    // SPEC 4.6.3.1, 12.3.1: a range whose written boundaries, at least one, are all literal-only is itself literal-only.
    private static bool IsLiteralOnlyRange(Koto node)
        => node is RangeKoto range && (range.Start ?? range.End) is not null &&
            (range.Start is null || IsLiteralOnlyPosition(range.Start)) && (range.End is null || IsLiteralOnlyPosition(range.End));

    // SPEC 12.3.1: one classification serves waiting arguments, defaults and integer-only positions. Numeric classes
    // never mix; position/range syntax is classified separately. No values are evaluated or Types committed here.
    private static bool IsLiteralOnlyOperation(Koto node)
        => node is UnaryKoto or BinaryKoto && NumericLiteralDefault(node) is not null;

    private static bool IsIntegerLiteralOnly(Koto node)
        => ReferenceEquals(NumericLiteralDefault(node), BoundType.I32);

    private static BoundType? NumericLiteralDefault(Koto node)
    {
        node = KotoHelper.UnwrapParentheses(node);
        if (node is NumberLiteralKoto literal)
        {
            return literal.IsInteger ? BoundType.I32 : BoundType.F64;
        }

        if (node is PrefixMinusKoto or PrefixPlusKoto)
        {
            return NumericLiteralDefault(((UnaryKoto)node).Operand);
        }

        if (node is BinaryKoto { Akind: KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash or KotoKind.Percent or KotoKind.Ampersand or KotoKind.Bar or KotoKind.Caret or KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan } binary &&
            NumericLiteralDefault(binary.Left) is { } kind &&
            (ReferenceEquals(kind, BoundType.I32) || binary.Akind is KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash) &&
            ReferenceEquals(kind, NumericLiteralDefault(binary.Right)))
        {
            return kind;
        }

        return null;
    }

    private static BoundType DefaultLiteralType(NumberLiteralKoto literal, BoundType? expected)
        => expected ?? (literal.IsInteger ? BoundType.I32 : BoundType.F64);

    // SPEC 8.4.7.3: an untyped literal fits an unbound integer Type only when it fits all twelve integer Types, 0 through 127.
    private static bool FitsGenericInteger(NumberLiteralKoto literal, bool negative)
        => literal.IsInteger && literal.TryGetIntegerMagnitude(out var magnitude) && (negative ? magnitude == 0 : magnitude <= 127);

    private static bool LiteralCategoryMatches(NumberLiteralKoto literal, BoundType type)
        => literal.IsInteger ? type.HasIntegerArithmetic : type.IsFloatingPoint;

    private static bool FitsLiteral(NumberLiteralKoto literal, BoundType type, bool negative, int pointerWidth)
    {
        if (type.IsFloatingPoint)
        {
            return FloatingTypes.TryLiteral(literal, type, negative, out _);
        }

        return literal.TryGetIntegerMagnitude(out var magnitude) && FitsIntegerMagnitude(magnitude, type, negative, pointerWidth);
    }

    // SPEC 3.1.1.1: a wrapping integer Type fits literals by the range of its integer argument.
    private static bool FitsIntegerMagnitude(UInt128 magnitude, BoundType type, bool negative, int pointerWidth)
    {
        if (!type.HasIntegerArithmetic)
        {
            return false;
        }

        var name = type.Underlying.Name;
        var signed = name[0] == 'i';
        var bits = name is "isize" or "usize" ? pointerWidth : int.Parse(name.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture);
        if (bits == 0)
        {
            // A target-independent literal must fit even the smallest integer
            // storage width. Larger native-sized literals still require a target.
            bits = 8;
        }

        if (negative && !signed)
        {
            return magnitude == 0;
        }

        var max = signed ? ((UInt128)1 << (bits - 1)) - (negative ? (UInt128)0 : 1) : bits == 128 ? UInt128.MaxValue : ((UInt128)1 << bits) - 1;
        return magnitude <= max;
    }

    // SPEC 8.4.7.3: a symbolic Type proven PrimitiveInteger has the built-in integer operators; each instance uses the
    // operations of its concrete Type.
    // A shared literal argument constructs an owner temporary before acquiring its borrow.
    private static BoundType LiteralInputType(BoundType parameter)
        => parameter is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components.Count: 1 } ? parameter.Components[0] : parameter;

    private bool IsGenericInteger(BoundType? type, BindingScope scope)
        => type is { Kind: BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection } &&
            this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: this.Library.PrimitiveInteger)), scope) == ConstraintProof.Proven;

    // SPEC 8.4.7.3, 3.1.1.1: Wrapping<T> over a Type proven PrimitiveInteger has the wrapping integer operators, unary minus for
    // every instantiation, the generic literals 0 through 127 and the conversions to and from T; each instance uses its Scalar.
    private bool IsGenericWrapping(BoundType? type, BindingScope scope)
        => type is { Kind: BoundTypeKind.Constructed, Components.Count: 1 } && type.Symbol?.LibraryDeclaration == KimiDeclarationId.Wrapping &&
            this.IsGenericInteger(type.Components[0], scope);

    // An integer Type, concrete or generic: the Types that positions, range boundaries, lengths and shift counts accept.
    private bool IsIntegerOperand(BoundType? type, BindingScope scope) => type is { IsInteger: true } || this.IsGenericInteger(type, scope);

    // A Type with the integer operators (SPEC 13.3): an integer or wrapping integer Type, concrete or generic.
    private bool IsArithmeticInteger(BoundType? type, BindingScope scope)
        => type is { HasIntegerArithmetic: true } || this.IsGenericInteger(type, scope) || this.IsGenericWrapping(type, scope);

    // A symbolic Type whose literals are the generic 0 through 127 (SPEC 8.4.7.3).
    private bool TakesGenericLiterals(BoundType? type, BindingScope scope) => this.IsGenericInteger(type, scope) || this.IsGenericWrapping(type, scope);

    // SPEC 12.3.1: the default Type of an unfitted literal or literal-only expression, or null for null and other syntax.
    private BoundType? LiteralDefault(Koto node)
    {
        node = KotoHelper.UnwrapParentheses(node);
        return NumericLiteralDefault(node) ?? (IsLiteralOnlyFromEnd(node) || IsLiteralOnlyRange(node) ? this.LiteralPositionDefault(node) : null);
    }

    private bool FitsInputLiteral(Koto node, BoundType type, BindingScope scope)
    {
        node = KotoHelper.UnwrapParentheses(node);
        if ((IsLiteralOnlyFromEnd(node) || IsLiteralOnlyRange(node)) && node.BoundType is null)
        {
            // SPEC 4.6.3.1: each boundary fits its S or E, and a `^a` operand the T of FromEnd<T>. A comparison operand is
            // already fitted.
            return this.FitsLiteralPosition(node, type, scope);
        }

        if (node is NumberLiteralKoto or PrefixMinusKoto { Operand: NumberLiteralKoto } or PrefixPlusKoto { Operand: NumberLiteralKoto } && this.TakesGenericLiterals(type, scope))
        {
            return FitsGenericInteger(node as NumberLiteralKoto ?? (NumberLiteralKoto)((UnaryKoto)node).Operand, node is PrefixMinusKoto);
        }

        if (node is not (PrefixMinusKoto { Operand: NumberLiteralKoto } or PrefixPlusKoto { Operand: NumberLiteralKoto }) && IsLiteralOnlyOperation(node))
        {
            // SPEC 12.3.1: every literal fits the Type its operator propagates and every operator is defined for that Type;
            // a shift count is typed independently of the candidate. A wrapping integer Type has unary - for every argument.
            var integer = this.IsArithmeticInteger(type, scope);
            var numeric = integer || type.IsFloatingPoint;
            return node switch
            {
                PrefixMinusKoto negated => numeric && !type.IsUnsignedInteger && !this.IsGenericInteger(type, scope) && this.FitsInputLiteral(negated.Operand, type, scope),
                PrefixPlusKoto plus => numeric && this.FitsInputLiteral(plus.Operand, type, scope),
                BinaryKoto { Akind: KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan } shifted => integer && this.FitsInputLiteral(shifted.Left, type, scope),
                BinaryKoto binary => numeric && this.FitsInputLiteral(binary.Left, type, scope) && this.FitsInputLiteral(binary.Right, type, scope),
                _ => false,
            };
        }

        return node switch
        {
            NumberLiteralKoto number => LiteralCategoryMatches(number, type) && FitsLiteral(number, type, false, this.compilation.PointerWidth),
            PrefixMinusKoto { Operand: NumberLiteralKoto number } => LiteralCategoryMatches(number, type) && FitsLiteral(number, type, true, this.compilation.PointerWidth),
            PrefixPlusKoto { Operand: NumberLiteralKoto number } => LiteralCategoryMatches(number, type) && FitsLiteral(number, type, false, this.compilation.PointerWidth),
            NullLiteralKoto => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw },
            _ => true,
        };
    }

    private BoundType? Join(Koto node, BoundType? a, BoundType? b)
    {
        if (a is null || ReferenceEquals(a, BoundType.Never))
        {
            return b;
        }

        if (b is null || ReferenceEquals(b, BoundType.Never) || ReferenceEquals(a, b))
        {
            return a;
        }

        return this.FailMismatch(node, node, b, a);
    }

    private BoundType? BindNode(Koto node, BindingScope scope, BoundType? expected = null)
    {
        // The caller consulted this node: its failure becomes the caller's prerequisite (SPEC 23.3.6.4).
        var parent = this.BeginConsultation(node);
        var type = this.BindAndAdaptNode(node, scope, expected);
        this.EndConsultation(node, parent);
        this.Consulted(node);
        return type;
    }

    // SPEC 7.6.4: a concrete Closure converts to an expected common Function Type when its signature fits, its minimum
    // receiver is Shared and its complete environment is Owned. Initializations, returns and call arguments share this judgment.
    private bool ErasesToFunction(Koto node, BoundType actual, BoundType expected)
        => this.ErasureSignatureFits(actual, expected, node) &&
            (actual.Kind != BoundTypeKind.Closure || actual.Symbol?.Declaration is FunctionKoto { BoundClosure.Receiver: SemanticsKind.Ref }) &&
            this.ProveOwned(actual, node) == ConstraintProof.Proven;

    // SPEC 7.6.4, 10.5, 10.7: the signature part of an erasure, which a call's applicability uses; the Shared receiver and the Owned
    // environment are judged after selection, at the argument, and a failure there never selects another candidate. A Function Item's
    // Origin conditions belong to its signature, proven from the required contract at `use` (SPEC 15.3.7).
    private bool ErasureSignatureFits(BoundType actual, BoundType expected, Koto use)
        => expected.Kind == BoundTypeKind.Function &&
            ((actual.Kind == BoundTypeKind.FunctionItem && this.FunctionItemSignature(actual) is { } signature && this.ItemContractFits(actual, signature, expected, use)) ||
            (actual.Kind == BoundTypeKind.Closure && actual.Symbol?.Declaration is FunctionKoto { BoundClosure: not null } &&
            this.ClosureSignature(actual) is { } closureSignature && CallableSignatureFits(closureSignature, expected, SignatureOwner(actual))));

    private BoundType? BindAndAdaptNode(Koto node, BindingScope scope, BoundType? expected)
    {
        if (expected is { ContainsParameter: true })
        {
            expected = this.SubstituteIdentityPremises(expected, scope);
        }

        var actual = this.BindNodeCore(node, scope, expected);
        if (expected is not null && actual is not null && HasArrayLengthHole(expected))
        {
            expected = this.CompleteArrayExpectation(expected, actual);
        }

        if (actual is { ContainsParameter: true } && this.SubstituteIdentityPremises(actual, scope) is var substituted && !ReferenceEquals(substituted, actual))
        {
            // SPEC 8.3: the expression has the one Type that the identity premises of its scope make of its Types.
            if (ReferenceEquals(node.BoundType, actual))
            {
                node.BoundType = substituted;
            }

            actual = substituted;
        }

        if (actual is null && expected?.Kind == BoundTypeKind.Function && node.BindingState == BindingState.Resolved &&
            node.BoundSymbol is { Kind: BindingSymbolKind.Function } symbol && IsValueUse(node))
        {
            return this.BindFunctionReference(node, symbol, expected, scope);
        }

        if (actual is null && expected?.Kind != BoundTypeKind.Function && node.BindingState == BindingState.Resolved &&
            node.BoundSymbol is { Kind: BindingSymbolKind.Function } item && IsValueUse(node) &&
            (expected is not null || !TryNameRoot(node, out var root) || root.Parent is not InvocationKoto))
        {
            actual = this.BindFunctionItem(node, item, scope);
        }

        if (expected is not null && actual is not null && this.ErasesToFunction(node, actual, expected))
        {
            node.ErasedFunctionType = expected;
            return expected;
        }

        // SPEC 10.2: one implicit operation of the common adaptation, recorded once for every later stage.
        if (expected is not null && actual is not null && node.ErasedFunctionType is null &&
            (!Compatible(actual, expected) || (actual.Semantics == SemanticsKind.Uniq && expected.Semantics == SemanticsKind.Uniq && IsBarePlace(node))) &&
            this.ExpectedAdaptation(node, actual, expected) is { } adaptation)
        {
            this.adaptations[node] = adaptation;
            return adaptation.Type;
        }

        // SPEC 3.5: without a fixed expected Type, the bare acquisition of a Place storing an exclusive reference Reborrows
        // it in its own Semantics, exactly as an annotation of the same complete Type would.
        if (expected is null && actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Components.Count: 1 } &&
            node.ErasedFunctionType is null && IsBareAcquisitionPosition(node) && IsBarePlace(node) &&
            this.ExpectedAdaptation(node, actual, actual) is { } reborrow)
        {
            this.adaptations[node] = reborrow;
            return reborrow.Type;
        }

        return node.ErasedFunctionType ?? actual;
    }

    private BoundType? BindNodeCore(Koto node, BindingScope scope, BoundType? expected)
    {
        if (this.patternNodes.Contains(node))
        {
            throw new InvalidOperationException("Pattern syntax must be handled by the positional Pattern binder.");
        }

        scope = this.NodeScope(node, scope);
        if (node is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance, Parent: DeclarationContainerKoto } conditionalDeclaration && TryConditionalBlock(conditionalDeclaration, out var implementationBlock))
        {
            this.BindNode(implementationBlock, scope);
            return Complete(node, BoundType.Unit);
        }

        // Attribute-chain entries are independent selected markers. An indexed
        // error or completed built-in marker must not suppress an earlier entry.
        if (node is AttributeKoto { AttributeChain: { } precedingAttribute })
        {
            this.BindNode(precedingAttribute, scope);
        }

        if (node.BindingState == BindingState.Resolved)
        {
            return node.BoundType;
        }

        // Declaration errors must not prevent independent bodies and siblings from being visited.
        if (node.BindingState == BindingState.Invalid && node is not DeclarationKoto)
        {
            return node.BoundType;
        }

        if (node is FunctionKoto testDefinition && TestDefinition.Marker(testDefinition) is not null && !TestDefinition.IsIncluded(testDefinition))
        {
            return testDefinition.BoundType;
        }

        if (node is not AttributeKoto && node.AttributeChain is { } attribute)
        {
            this.BindNode(attribute, scope);
        }

        switch (node)
        {
            case ExpressionKoto recovered when recovered is not ErrorKoto && recovered.CodeContext.RecoveryCause(recovered) is not null:
                // The parser's guess of a rejected form, or a form it reported as misplaced: its operands are checked on their
                // own, its combination is not, and every check of it rests on the syntax Error (DIAGNOSTICS.md §4.3). Arm
                // Patterns have no meaning apart from their match, so a recovered match with arms is checked by its own binder.
                if (recovered is MatchKoto { Arms.Count: > 0 } recoveredMatch)
                {
                    this.BindMatch(recoveredMatch, scope, null);
                }
                else if (recovered is IsKoto clause)
                {
                    // A recovery Constraint keeps an Error constraint and binds none of its parts, as a function's does.
                    clause.BoundConstraint = this.InternConstraint(new(ConstraintKind.Error));
                }
                else
                {
                    foreach (var part in recovered.ChildNodes)
                    {
                        this.BindNode(part, scope);
                    }
                }

                return Complete(node, null);
            case EvaluatedKoto evaluated:
                // A desugaring's evaluated operand has its source's Type; binding it evaluates nothing.
                return Complete(evaluated, evaluated.Source.BoundType);
            case EffectBoundKoto effect:
                return this.BindEffectBound(effect);
            case SyntaxFormKoto { Akind: KotoKind.EnumCase } enumeration when TryEnumPayload(enumeration, out var payload):
                enumeration.Operands[0].BoundSymbol = enumeration.BoundSymbol;
                Complete(enumeration.Operands[0], BoundType.Unit);
                Complete(payload, BoundType.Unit);
                return Complete(enumeration, BoundType.Unit);
            case ParenthesizedTypeKoto:
                // Grouped Types are bound by Type/qualifier entry points, never as runtime values.
                return this.Fail(node, BindingFailure.InvalidTypeFormation);
            case SyntaxFormKoto { Akind: KotoKind.ConstructorReference } constructorReference:
                return this.BindBaseConstructor(constructorReference, scope);
            case TypeKoto:
                return this.BindType(node, scope);
            case DeclarationContainerKoto container:
                for (var i = 0; i < container.GenericParameterNodes.Count; i++)
                {
                    this.BindType(container.GenericParameterNodes[i], scope);
                }

                for (var i = 0; i < container.Bases.Count; i++)
                {
                    this.BindType(container.Bases[i], scope);
                }

                for (var i = 0; i < container.ConstraintNodes.Count; i++)
                {
                    this.BindNode(container.ConstraintNodes[i], scope);
                }

                for (var i = 0; i < container.Members.Count; i++)
                {
                    this.BindNode(container.Members[i], scope);
                }

                for (var i = 0; i < container.NestedContainers.Count; i++)
                {
                    this.BindNode(container.NestedContainers[i], scope);
                }

                if (container.IsRoot && container.Kotonoha.GeneratedFunction is { } generated)
                {
                    this.BindNode(generated, scope);
                }

                if (container is StructKoto { ImplicitConstructor: { } constructor })
                {
                    this.BindNode(constructor, scope);
                }

                if (container is not (ContractKoto or StructKoto) && container.Bases.Count != 0)
                {
                    return this.Fail(node, BindingFailure.Unsupported, true);
                }

                if (LengthSlot(container) is { } slot)
                {
                    this.AddPrerequisite(node, slot); // A slot the parser reported as misplaced explains the failure.
                    return this.Fail(node, BindingFailure.InvalidTypeFormation);
                }

                return Complete(node, container.BoundSymbol?.Type ?? BoundType.Unit);
            case FunctionKoto function:
                return function.IsAnonymous ? this.BindClosure(function, scope, expected) : this.BindFunction(function, scope);
            case VariableKoto variable:
                return this.BindVariable(variable, scope);
            case NoInitKoto directive:
                return this.BindNoInit(directive, scope, expected);
            case AliasKoto alias:
                this.AliasTarget(alias);
                return null;
            case DiscardKoto discard:
                this.BindNode(discard.Operand, scope);
                return Complete(node, BoundType.Unit);
            case CodeBlockKoto block:
                BoundType? blockType = BoundType.Unit;
                for (var i = 0; i < block.Items.Count; i++)
                {
                    var type = this.BindNode(block.Items[i], scope, block.HasTrailingExpression && KotoHelper.IsValueContext(block) ? expected : null);
                    if (block.HasTrailingExpression && KotoHelper.IsValueContext(block))
                    {
                        blockType = type;
                    }
                    else if (ReferenceEquals(type, BoundType.Never))
                    {
                        blockType = BoundType.Never;
                    }
                }

                return Complete(node, blockType);
            case BoolLiteralKoto:
                return Complete(node, BoundType.Boolean);
            case UnitLiteralKoto:
                return Complete(node, BoundType.Unit);
            case CharLiteralKoto:
                return Complete(node, BoundType.Char);
            case StringLiteralKoto:
                return Complete(node, BoundType.String);
            case InterpolatedStringKoto interpolation:
                return this.BindInterpolation(interpolation, scope);
            case NumberLiteralKoto number:
                if (this.TakesGenericLiterals(expected, scope))
                {
                    return !number.IsInteger ? this.Fail(node, BindingFailure.TypeMismatch) : FitsGenericInteger(number, false) ? Complete(node, expected) : this.Fail(node, BindingFailure.InvalidLiteral);
                }

                var numberType = DefaultLiteralType(number, expected);
                if (!LiteralCategoryMatches(number, numberType) && !(ReferenceEquals(number, this.floatingIntegerLiteral) && numberType.IsFloatingPoint))
                {
                    return this.RecordMismatch(node, node, number.IsInteger ? "integer literal" : "floating-point literal", numberType); // The Type keeps a generic default's parameter (SPEC 7.2.3).
                }

                return FitsLiteral(number, numberType, false, this.compilation.PointerWidth) ? Complete(node, numberType) : this.Fail(node, BindingFailure.InvalidLiteral);
            case NullLiteralKoto:
                return expected is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw } ? Complete(node, expected) : this.Fail(node, BindingFailure.MissingType, true);
            case BaseReferenceKoto baseReference:
                return this.BindBaseReference(baseReference, scope);
            case IdentifierNameKoto identifier:
                return this.BindName(identifier, scope);
            case InvocationKoto invocation:
                var called = this.BindCall(invocation, scope, expected);
                this.CheckLiteralArguments(invocation); // SPEC 17.4.4
                return called;
            case SyntaxFormKoto { Akind: KotoKind.InferredCase } inferred:
                var inferredSymbol = this.InferredCase(inferred, scope, expected);
                return inferredSymbol is null ? null : this.BindEnumConstruction(inferred, inferred, inferredSymbol, null, scope, expected);
            case IndexKoto index:
                return this.BindElement(index, scope);
            case RangeKoto range:
                return this.BindRangeValue(range, scope, expected); // SPEC 4.6.3: range syntax outside an index position.
            case MemberAccessKoto { Right: NumberLiteralKoto } tupleElement:
                return this.BindElement(tupleElement, scope);
            case MemberAccessKoto { Right: ErrorKoto missing } incomplete:
                return this.CompleteDependent(incomplete, missing); // SPEC 23.3.6.4: a member name that failed to parse explains the access.
            case MemberAccessKoto member:
                if (this.BindSequenceMember(member, scope, out var sequenceType))
                {
                    return sequenceType;
                }

                var memberSymbol = this.Member(member, scope, expected);
                if (memberSymbol is null)
                {
                    return this.Fail(member, this.MissingFailure(member, scope, BindingFailure.MissingName), true);
                }

                return memberSymbol.EnumCase is not null ? this.BindEnumConstruction(member, member, memberSymbol, null, scope, expected) : this.BindReference(member, memberSymbol, scope);
            case ParenthesizedKoto parent:
                return Complete(node, this.BindNode(parent.Operand, scope, expected));
            case UnaryKoto unary:
                return this.BindUnary(unary, scope, expected);
            case ConversionKoto conversion:
                return this.BindConversion(conversion, scope, expected);
            case IsKoto { IsRuntimeTest: true } test:
                return this.BindRuntimeTypeTest(test, scope);
            case BinaryKoto binary:
                return this.BindBinary(binary, scope, expected);
            case MatchKoto match:
                return this.BindMatch(match, scope, expected);
            case IfKoto conditional:
                var conditionalResult = this.BeginResult(conditional, scope, expected);
                for (var i = 0; i < conditional.Branches.Count; i++)
                {
                    var branch = conditional.Branches[i];
                    this.RequireType(branch.Condition, scope, BoundType.Boolean);
                    this.BindNode(branch.Body, scope, conditionalResult.Expected);
                }

                if (conditional.ElseBody is { } elseBody)
                {
                    this.BindNode(elseBody, scope, conditionalResult.Expected);
                }

                return this.FinishResult(conditional, conditionalResult);
            case DoKoto scoped:
                var scopedResult = this.BeginResult(scoped, scope, expected);
                this.BindNode(scoped.Body, scope, scopedResult.Expected);
                return this.FinishResult(scoped, scopedResult);
            case ForKoto iteration:
                return this.BindIteration(iteration, scope);
            case WhileKoto loop:
                var whileResult = this.BeginResult(loop, scope, BoundType.Unit);
                this.RequireType(loop.Condition, scope, BoundType.Boolean);
                this.BindNode(loop.Body, scope);
                return this.FinishResult(loop, whileResult);
            case LoopKoto loop:
                var loopResult = this.BeginResult(loop, scope, expected);
                this.BindNode(loop.Body, scope);
                return this.FinishResult(loop, loopResult);
            case JumpKoto jump:
                var target = KotoHelper.ResolveTransferTarget(jump);
                this.resultContexts.TryGetValue(target ?? jump, out var targetResult);
                var resultType = targetResult?.Expected;
                if (target is FunctionKoto f)
                {
                    resultType = this.symbols.GetValueOrDefault(f)?.Type ?? resultType;
                }
                else if (target is PropertyAccessorKoto a)
                {
                    resultType = Accessor(a).Result;
                }
                else if (target is ForKoto or WhileKoto or DeferredBlockKoto)
                {
                    resultType = BoundType.Unit;
                }

                if (resultType is { ContainsParameter: true })
                {
                    resultType = this.SubstituteIdentityPremises(resultType, scope); // SPEC 8.3
                }

                var actual = jump.Expression is { } operand ? this.BindExpected(operand, scope, resultType, (target as FunctionKoto)?.ReturnType) : BoundType.Unit;
                if (jump.Parent is TryKoto propagation && actual?.Symbol != propagation.Expression.BoundType?.Symbol)
                {
                    this.Fail(jump, BindingFailure.TypeMismatch);
                }

                if (jump is ReturnKoto && target is FunctionKoto { ReturnType: PlaceResultKoto place } && jump.Expression is { } placeOperand &&
                    (actual is null || !ReferenceEquals(actual, BoundType.Never)))
                {
                    this.CheckPlaceResultOperand(placeOperand, place, jump);
                }

                if (jump is not ContinueKoto)
                {
                    if (jump.Parent is not TryKoto)
                    {
                        targetResult?.Sources.Add(actual);
                    }

                    if (actual is not null && resultType is not null && !this.FitsTypeAt(actual, resultType, node) && !this.CheckLocalTypeUse(actual, resultType, jump.Expression ?? jump))
                    {
                        this.FailMismatch(jump, jump.Expression ?? jump, actual, resultType);
                    }
                }

                return Complete(jump, BoundType.Never);
            case BlockStatementKoto statement:
                this.BindNode(statement.Body, scope);
                return Complete(node, BoundType.Unit);
            case RequireKoto require:
                this.RequireType(require.Condition, scope, BoundType.Boolean);
                this.BindNode(require.ElseBody, scope);
                return Complete(node, BoundType.Unit);
            case TestVerificationKoto verification:
                return this.BindVerification(verification, scope);
            case LabeledKoto labeled:
                return Complete(node, this.BindNode(labeled.Target, scope, expected));
            case TupleLiteralKoto tuple:
                return this.BindTuple(tuple, scope, expected);
            case DictionaryLiteralKoto dictionary:
                return this.BindDictionaryLiteral(dictionary, scope, expected);
            case ArrayLiteralKoto { FillLength: not null } fill:
                return this.BindArrayFill(fill, scope, expected);
            case ArrayLiteralKoto array when expected is { Kind: BoundTypeKind.FixedArray or BoundTypeKind.Array }:
                return this.BindContextualArrayLiteral(array, scope, expected);
            case ArrayLiteralKoto { Elements.Count: 0 } array when expected is null && this.MissingArrayElementCause(array) is { } holeCause:
                return this.CompleteDependent(array, holeCause);
            case ArrayLiteralKoto { Elements.Count: 0 } array when expected is null && this.MissingExpectationCause(array) is { } arrayCause:
                return this.CompleteDependent(array, arrayCause);
            case ArrayLiteralKoto array when expected is null && array.Elements.Count != 0 && !IsCallArgument(array):
                return this.BindIndependentArrayLiteral(array, scope);
            case PropertyAccessorKoto accessor:
                return this.BindAccessorBody(accessor, scope);
            case GenericsKoto { Identifier: IdentifierNameKoto or MemberAccessKoto } reference when IsValueUse(reference):
                return this.BindExplicitReferenceName(reference, scope);
        }

        // Unsupported semantics stay explicit and cannot pass final Bound checking.
        this.BindUnknownChildren(node, scope);
        return this.Fail(node, BindingFailure.Unsupported, true);
    }

    // SPEC 10.5: a function Name with explicit Type arguments in a value position is a reference whose candidates take
    // those arguments. It is resolved here and selected as a value, or against the fixed expected call signature.
    private BoundType? BindExplicitReferenceName(GenericsKoto reference, BindingScope scope)
    {
        var name = reference.Identifier!;
        this.BindNodeCore(name, scope, null);
        if (name.BindingState != BindingState.Resolved || name.BoundSymbol is not { Kind: BindingSymbolKind.Function } group)
        {
            this.BindUnknownChildren(reference, scope);
            return this.Fail(reference, BindingFailure.Unsupported, true);
        }

        for (var i = 0; i < reference.TypeArguments.Count; i++)
        {
            var syntax = reference.TypeArguments[i];
            if (this.IsLengthArgument(syntax, scope))
            {
                if (this.BindLength(syntax, scope) is null)
                {
                    return this.CompleteDependent(reference, syntax);
                }

                continue;
            }

            if (this.BindType(syntax, scope) is null)
            {
                return this.CompleteDependent(reference, syntax);
            }
        }

        reference.BoundSymbol = group;
        reference.BindingState = BindingState.Resolved;
        return null;
    }

    private BoundType? BindFunction(FunctionKoto function, BindingScope scope)
    {
        var symbol = this.symbols.GetValueOrDefault(function);
        if (symbol is not null)
        {
            this.BindHeader(symbol);
        }

        // The indexing pass only had the nominal shell. A constructor/destructor body sees the complete Self,
        // including its Type parameters and Origins, just as an explicit receiver does.
        if (this.SpecialReceiver(function) is { } receiver && symbol?.Scope.Owner.BoundSymbol is { Schema: not null } owner)
        {
            receiver.Type = this.DeclarationSelf(owner);
        }

        for (var i = 0; i < function.GenericArguments.Count; i++)
        {
            this.BindType(function.GenericArguments[i], scope);
        }

        for (var i = 0; i < function.TypeConstraints.Count; i++)
        {
            this.BindNode(function.TypeConstraints[i], scope);
        }

        for (var i = 0; i < function.EffectBounds.Count; i++)
        {
            this.BindNode(function.EffectBounds[i], scope);
        }

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var parameter = function.Parameters[i];
            this.symbols[parameter].Type = this.BindType(parameter.Type, scope);
            if (parameter.DefaultValue is { } value)
            {
                this.defaultBindingDepth++;
                try
                {
                    this.RequireType(value, scope, parameter.Type.BoundType);
                }
                finally
                {
                    this.defaultBindingDepth--;
                }
            }
        }

        if (function.BaseInitializer is { } initializer)
        {
            this.BindNode(initializer, scope);
        }

        if (function.ReturnType is { } resultSyntax)
        {
            this.BindType(resultSyntax, scope);
        }

        if (function.Body is { } body)
        {
            this.BindNode(body, scope);
        }

        if (function.ExpressionBody is { } expression)
        {
            var discards = KotoHelper.DiscardsFunctionBody(function);
            var result = discards ? this.BindNode(expression, scope) : this.BindExpected(expression, scope, symbol?.Type, function.ReturnType);
            var placeItem = expression is CodeBlockKoto { IsExpressionBody: true, Items.Count: 1 } single ? single.Items[0] : expression;
            if (function.ReturnType is PlaceResultKoto place && !discards && (result is null || !ReferenceEquals(result, BoundType.Never)))
            {
                this.CheckPlaceResultOperand(placeItem, place, ReferenceEquals(placeItem, expression) ? function : expression);
            }

            if (symbol is not null)
            {
                var structural = this.ResultStructure();
                if (!discards && (KotoHelper.IsBodyExpression(expression) || structural.CanComplete(expression)) &&
                    symbol.Type is { } expected && result is not null && !this.FitsTypeAt(result, expected, function))
                {
                    this.FailMismatch(expression, expression, result, expected);
                }
            }
        }

        if (function.IsAnonymous || (function.IsSpecialization && !this.specializations.ContainsKey(function)))
        {
            return this.Fail(function, BindingFailure.Unsupported, true);
        }

        return Complete(function, function.IsGenerated ? BoundType.Unit : symbol?.Type);
    }

    private BoundType? BindVariable(VariableKoto variable, BindingScope scope)
    {
        // A variable is checked in its own frame, also when a use binds it first (BindReference).
        var frame = this.BeginConsultation(variable);
        var type = this.BindVariableCore(variable, scope);
        this.EndConsultation(variable, frame);
        this.Consulted(variable);
        return type;
    }

    private BoundType? BindVariableCore(VariableKoto variable, BindingScope scope)
    {
        var symbol = this.symbols[variable];
        if (variable.InitializerKoto is NoInitKoto directive && !this.CheckNoInitDeclaration(directive, variable))
        {
            return this.CompleteDependent(variable, directive);
        }

        if (symbol.Resolving)
        {
            return this.Fail(variable, BindingFailure.Cycle, true);
        }

        if (symbol.Property is not null)
        {
            this.BindHeader(symbol);
        }

        symbol.Resolving = true;
        if (symbol.Kind == BindingSymbolKind.Local && variable.TypeKoto is { } annotation && variable.InitializerKoto is { } arrayInitializer and not NoInitKoto)
        {
            this.InferArrayAnnotation(annotation, arrayInitializer, scope);
        }

        var originDeclaration = this.BeginOriginDeclaration(variable, scope);
        var declared = symbol.Property is not null ? symbol.Type : variable.TypeKoto is { } type ? this.BindType(type, scope) : null;
        if (symbol.Kind == BindingSymbolKind.Local && declared is { ContainsParameter: true })
        {
            declared = this.SubstituteIdentityPremises(declared, scope); // SPEC 8.3: one Type in the premise's scope.
        }

        if (originDeclaration is not null)
        {
            foreach (var set in originDeclaration.Sets.Values)
            {
                this.BindOriginSetType(set, scope);
            }

            this.CompleteOriginDeclaration(originDeclaration);
            declared = variable.TypeKoto?.BoundType ?? declared;
        }

        var inferred = variable.InitializerKoto is { } initializer ? this.BindExpected(initializer, scope, declared, variable.TypeKoto) : null;
        symbol.Resolving = false;
        if (inferred is not null && this.initializerOrigins.TryGetValue(variable, out var initializerOrigins) &&
            initializerOrigins.State < 2 && initializerOrigins.Replacements.Count != 0)
        {
            // SPEC 15.4.4: finish all initializer acquisition bounds before checking the local's fixed Type.
            inferred = this.ResolveInitializerOrigins(inferred, initializerOrigins, scope);
        }

        if (symbol.Kind == BindingSymbolKind.Local && declared is not null && inferred is not null)
        {
            // Select the Copy-read shape before inferring omitted Origins. Comparing the
            // unresolved Origin with the stored reference's Origin would reject ref/ref/T
            // initializers before the ordinary lifetime constraints can be inferred.
            if (variable.InitializerKoto is { } value && !ReferenceTypes.StorageMatches(inferred, declared) &&
                this.ReadTypeReferent(inferred, scope) is { } referent && ReferenceTypes.StorageMatches(referent, declared))
            {
                this.adaptations[value] = new(ExpectedAdaptationKind.ReferentRead, referent);
                inferred = referent;
            }

            declared = this.InferLocalOrigins(declared, inferred, variable, scope);
        }

        if (declared is not null && inferred is not null && !this.CheckTypeUse(inferred, declared, variable))
        {
            this.FailMismatch(variable, variable.InitializerKoto ?? variable, inferred, declared);
        }

        if (symbol.Kind == BindingSymbolKind.Local && declared is not null && variable.TypeKoto is { } occurrence)
        {
            this.AddTypeClauseObligations(declared, occurrence); // SPEC 15.3.3: the Type's clauses at this use.
        }

        if (symbol.Kind == BindingSymbolKind.Local && variable.TypeKoto is null && declared is null && inferred is not null)
        {
            declared = this.LocalBorrowType(inferred, variable, variable.InitializerKoto!);
        }

        // An unavailable written Type cannot be replaced by the initializer's Type.
        symbol.Type = variable.TypeKoto is null ? declared ?? inferred : declared;
        variable.NameKoto.BoundSymbol = symbol;
        Complete(variable.NameKoto, symbol.Type);
        if (variable is PropertyKoto property)
        {
            for (var i = 0; i < property.Accessors.Count; i++)
            {
                this.BindNode(property.Accessors[i], scope);
            }
        }

        if (symbol.Type is null && variable.BindingFailure == BindingFailure.None)
        {
            if (variable.TypeKoto is { } written)
            {
                // A property's written Type is bound with its header; the variable consulted it all the same.
                this.Consulted(written);
            }

            this.Fail(variable, BindingFailure.MissingType, true);
        }

        return Complete(variable, symbol.Type);
    }

    // SPEC 7.1.1: the operand of a Place result designates a Place, never a value, temporary, if, match or do, and an
    // exclusive Place result is never reached through a shared layer.
    private void CheckPlaceResultOperand(Koto operand, PlaceResultKoto place, Koto report)
    {
        if (!IsBarePlace(operand))
        {
            this.Fail(operand.BindingState == BindingState.Invalid ? report : operand, BindingFailure.PlaceRequired);
        }
        else if (place.IsExclusive && PathAuthority(operand) == SemanticsKind.Ref)
        {
            this.Fail(operand, BindingFailure.SharedPathAccess);
        }
    }

    private BoundType? BindName(IdentifierNameKoto node, BindingScope scope)
    {
        var symbol = this.Lookup(node.IdentifierName, scope, node, false);
        if (symbol is null)
        {
            return this.Fail(node, BindingFailure.MissingName, true);
        }

        return this.BindReference(node, symbol, scope);
    }

    private BoundType? BindReference(Koto node, BindingSymbol symbol, BindingScope scope)
    {
        node.BoundSymbol = symbol;
        if (symbol.Name == "self" && symbol.Declaration is FunctionKoto special && (special.IsConstructor || special.IsDestructor) &&
            ((special.BaseInitializer is { } initializer && IsWithin(node, initializer)) || node.Parent is not MemberAccessKoto access || !ReferenceEquals(access.Left, node)))
        {
            return this.Fail(node, BindingFailure.InvalidAssignment);
        }

        if (symbol.ConditionalDeclaration is not null && symbol.Kind != BindingSymbolKind.Function)
        {
            var conditionalType = node is MemberAccessKoto conditionalMember && this.memberSelections.TryGetValue(conditionalMember, out var memberSelection) ? memberSelection.DeclaringType : null;
            var proof = this.ProveMemberConditions(symbol, conditionalType, scope);
            if (proof != ConstraintProof.Proven)
            {
                this.RequireConstraint(node, proof, this.capabilityMode);
                return null;
            }
        }

        if (symbol.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage or BindingSymbolKind.PatternCandidate or BindingSymbolKind.Capture && scope.Function != symbol.Scope.Function)
        {
            if (scope.Function is not { IsAnonymous: true, Captures: null } closure ||
                this.Capture(closure, symbol, this.scopes[closure]) is not { } capture)
            {
                return this.Fail(node, BindingFailure.Capture);
            }

            symbol = capture;
            node.BoundSymbol = symbol;
        }

        if (symbol.Kind == BindingSymbolKind.Function)
        {
            this.BindHeader(symbol);
            if (symbol.Next is null && symbol.Declaration is FunctionKoto { IsAnonymous: false } named && (named.Modifier & ModifierKind.Unsafe) != 0 && IsValueUse(node))
            {
                // SPEC 7.7: an unsafe function supports direct calls only. An overload group is checked after selection.
                return this.Fail(node, BindingFailure.UnsafeFunctionValue);
            }

            // A function group is resolved for call selection, but not an inferred first-class value.
            node.BindingState = BindingState.Resolved;
            return null;
        }

        if (symbol.Type is null && symbol.Declaration is VariableKoto variable)
        {
            if (variable is PropertyKoto)
            {
                // Property and storage Types belong to the header. A use never re-enters accessor bodies.
                var declaration = variable.BoundSymbol!;
                if (!declaration.HeaderBound && declaration.Resolving)
                {
                    this.Fail(variable, BindingFailure.Cycle, true);
                }
                else
                {
                    this.BindHeader(declaration);
                }

                if (symbol.Type is null)
                {
                    this.Consulted(variable.TypeKoto ?? variable.InitializerKoto ?? variable);
                }
            }
            else
            {
                this.BindVariable(variable, symbol.Scope);
            }
        }

        if (symbol.Kind == BindingSymbolKind.Storage && symbol.Scope.Owner is PropertyAccessorKoto storageAccessor)
        {
            this.BindStorageProjection(node, Accessor(storageAccessor));
        }

        if (symbol.Property is { } property)
        {
            var target = node;
            while (target.Parent is ParenthesizedKoto parentheses)
            {
                target = parentheses;
            }

            var assignment = target.Parent is BinaryKoto binary && ReferenceEquals(binary.Left, target) && binary.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals;
            var write = assignment && target.Parent!.Akind == KotoKind.Equals;
            var update = (assignment && !write) || target.Parent?.Akind is KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement;
            if (IsSpecialField(node, out var specialFunction) && specialFunction.IsConstructor)
            {
                if (!ReferenceEquals(symbol.Scope.Owner, specialFunction.BoundSymbol!.Scope.Owner) || !property.IsStored ||
                    (!write && (!property.Getter.IsStandard || (update && !property.Setter.IsStandard))))
                {
                    return this.Fail(node, BindingFailure.Unsupported);
                }

                return Complete(node, symbol.Type);
            }

            var operation = write ? property.Setter : property.Getter;
            var sourceReceiver = (node as MemberAccessKoto)?.Left;
            if ((write || update) && sourceReceiver?.BoundType is { Semantics: SemanticsKind.Ref or SemanticsKind.ObjRef or SemanticsKind.Rc or SemanticsKind.Arc })
            {
                return this.FailWrite(node, node);
            }

            if (!operation.IsPresent || !this.Accessible(symbol, scope, operation.Access, sourceReceiver?.BoundType))
            {
                return this.Fail(node, BindingFailure.Access);
            }

            if (update && (!property.Setter.IsPresent || !this.Accessible(symbol, scope, property.Setter.Access, sourceReceiver?.BoundType)))
            {
                return this.Fail(node, BindingFailure.Access);
            }

            if (!operation.IsStandard || (update && !property.Setter.IsStandard))
            {
                if (node is MemberAccessKoto projected && sourceReceiver?.BoundType is { } sourceType && this.memberSelections.TryGetValue(projected, out var pathSelection) &&
                    (pathSelection.Path is not null || IsObjectSemantics(sourceType.Semantics)) && operation.Receiver is { } declaredReceiver && this.MemberType(declaredReceiver, pathSelection.DeclaringType) is { } required)
                {
                    // SPEC 7.3, 13.7: an accessor receiver is a Receiver Expression and is acquired implicitly.
                    if (!this.AdaptInput(sourceReceiver, required, sourceType, scope, pathSelection.Path, pathSelection.DeclaringType, out var projectedReceiver, out var quality, out var kind, explicitBorrow: update, receiver: true))
                    {
                        // SPEC 11.2: a receiver that only the accessor's wrongly shaped written receiver rejects rests on that declaration.
                        if (this.ReceiverRestsOnAccessorShape(node, operation, sourceReceiver, sourceType, pathSelection.DeclaringType, pathSelection.Path, scope, update))
                        {
                            return null;
                        }

                        var objectProjection = IsObjectSemantics(sourceType.Semantics);
                        return this.Fail(node, objectProjection ? BindingFailure.Unsupported : BindingFailure.TypeMismatch, objectProjection);
                    }

                    var compatibility = kind == ArgumentOperationKind.PayloadProjection ? ConstraintProof.Proven : ProjectedReceiverProof(symbol, operation);
                    this.receiverOperations[node] = new(sourceReceiver, sourceType, projectedReceiver, kind, quality, pathSelection.Path, 0, compatibility);
                }

                var input = target.Parent is BinaryKoto parentBinary ? parentBinary.Right : target.Parent;
                if (update && target.Parent is { } updateExpression)
                {
                    input = this.PropertyUpdateInput(node, updateExpression);
                }

                if ((!operation.IsStandard && !this.BindPropertyCall(node, operation, scope, write ? input : null)) ||
                    (update && !property.Setter.IsStandard && (input is null || !this.BindPropertyCall(node, property.Setter, scope, input))) ||
                    (update && (node is not MemberAccessKoto updateMember || !this.BindPropertyUpdate(updateMember, scope))))
                {
                    return null;
                }

                return Complete(node, write ? this.PropertySetterInput(node) : this.propertyCalls.GetValueOrDefault((node, PropertyAccessorKind.Get))?.BoundType ?? property.Getter.Result);
            }

            if (node is MemberAccessKoto stored && sourceReceiver?.BoundType is { } storedSource && this.memberSelections.TryGetValue(stored, out var storageSelection) && storageSelection.Path is not null)
            {
                this.receiverOperations[node] = new(sourceReceiver, storedSource, storageSelection.DeclaringType, ArgumentOperationKind.StorageProjection, ArgumentAdaptation.Exact, storageSelection.Path);
            }
        }

        var type = symbol.Type;
        if (node is MemberAccessKoto member && type is not null && this.memberSelections.TryGetValue(member, out var selection) && selection.DeclaringType is { } declaringType)
        {
            type = this.StoredType(type, declaringType);
        }
        else if (type is null && symbol is { Kind: BindingSymbolKind.Parameter, Declaration: FunctionKoto function, Slot: var slot } &&
            (uint)slot < (uint)function.Parameters.Count)
        {
            // A parameter's Type is bound with its function's header; a use without a Type consulted it.
            this.Consulted(function.Parameters[slot].Type);
        }
        else if (type is null && symbol is { Kind: BindingSymbolKind.Parameter, Declaration: PropertyAccessorKoto accessorSyntax })
        {
            var accessor = Accessor(accessorSyntax);
            var annotation = ReferenceEquals(symbol, accessor.SelfSymbol) ? accessorSyntax.ReceiverType : accessorSyntax.ValueType;
            this.Consulted(annotation ?? accessor.Property.Declaration.TypeKoto ?? accessor.Property.Declaration);
        }

        return Complete(node, type);
    }

    private BoundType? RequireType(Koto node, BindingScope scope, BoundType? expected)
    {
        var actual = this.BindNode(node, scope, expected);
        if (expected is not null && actual is not null && !this.FitsTypeAt(actual, expected, node) && !this.CheckLocalTypeUse(actual, expected, node))
        {
            this.FailMismatch(node, node, actual, expected);
        }

        return actual;
    }

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

            // A directly signed literal is fitted as a signed value (SPEC 12.3.1).
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
                // Negation is defined for signed integers, floating-point values and every wrapping integer Type (SPEC 13.2, 13.3).
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
                return this.Fail(unary, BindingFailure.Unsupported, true);
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
            // The count may have any integer Type and is typed independently of the shifted operand (SPEC 12.3.1, 13.3).
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
            // SPEC 13.3: a left operand without the operator is the problem, so the right operand is bound without its Type.
            // SPEC 3.1.5, 13.4: a non-completing left operand fits the right operand's Type, so it expects nothing of it.
            var expectedRight = logical ? BoundType.Boolean
                : !assignment && ReferenceEquals(left, BoundType.Never) ? null
                : ReferenceTypes.IsPointer(left) && operation is KotoKind.Plus or KotoKind.Minus ? BoundType.ISize
                : arithmetic && left is not null && (NonNumericOperand(left) || (integerOnly && NonIntegerOperand(left))) ? null
                : comparison && comparand?.CarriesOrigin == true && !IsUnfittedLiteral(binary.Right) ? null
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
            left = right; // The operation is checked at the Type the non-completing operand fits (SPEC 3.1.5, 13.3).
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

        var result = assignment ? BoundType.Unit : left;
        if (assignment && kind != KotoKind.Equals && this.PropertySetterInput(KotoHelper.UnwrapParentheses(binary.Left)) is { } setterInput &&
            !this.FitsTypeAt(left, setterInput, binary))
        {
            return this.FailMismatch(binary, binary.Left, left, setterInput);
        }

        if (shift)
        {
            // SPEC 13.3: the shifted operand may be a wrapping integer Type; the count is an integer Type, never a wrapping one.
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

        // SPEC 13.3: arithmetic and bitwise operators take numeric operands, so bool, Unit, char and string have none. A string
        // operand is judged through its reference layers and before the operands are compared, so that two string references
        // with distinct Origins are not reported as a mismatch of one Type with itself; an interpolated literal is the one way
        // to join strings (SPEC 12.3.3). % and the bitwise operators take integers only, so a floating-point left operand is
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
            // SPEC 13.4, 3.4.1: qualifying pair layers are among the followed layers.
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
            return Complete(binary, result); // SPEC 8.4.7.3: every arithmetic, bitwise and remainder operator is defined for integers.
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

        // Every primitive with an arithmetic operator is numeric and handled above; arithmetic on other Types remains deferred.
        return this.Fail(binary, BindingFailure.Unsupported, true);
    }

    private BoundType? BindTuple(TupleLiteralKoto tuple, BindingScope scope, BoundType? expected)
    {
        var buffer = this.RentTypes(tuple.Elements.Count);
        try
        {
            var complete = true;
            for (var i = 0; i < tuple.Elements.Count; i++)
            {
                var type = this.BindNode(tuple.Elements[i], scope, expected is { Kind: BoundTypeKind.Tuple } && expected.Components.Count == tuple.Elements.Count ? expected.Components[i] : null);
                buffer[i] = type!;
                complete &= type is not null;
            }

            return Complete(tuple, complete ? this.InternType(BoundTypeKind.Tuple, null, SemanticsKind.Owner, buffer.AsSpan(0, tuple.Elements.Count)) : null);
        }
        finally
        {
            this.typeScratch.Return(buffer, clearArray: true);
        }
    }

    private ChildBinder? childBinder;

    private void BindUnknownChildren(Koto node, BindingScope scope)
    {
        var visitor = this.childBinder ??= new(this);
        var previous = visitor.Scope;
        visitor.Scope = scope;
        node.VisitChildren(visitor);
        visitor.Scope = previous;
    }

    private sealed class ChildBinder(Binding binding) : KotoVisitor
    {
        internal BindingScope Scope { get; set; } = null!;

        public override void Visit(Koto node) => binding.BindNode(node, this.Scope);
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
