// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
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

    // SPEC 7: an unsafe function supports direct calls only, so every appearance of its name
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
                default:
                    root = target;
                    return target.Parent is not MemberAccessKoto;
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

    // SPEC 12.3: an unfitted literal is an untyped literal or literal-only expression still waiting for its Type.
    private static bool IsUnfittedLiteral(Koto node)
    {
        node = KotoHelper.UnwrapParentheses(node);
        return node.BoundType is null &&
            (node is NullLiteralKoto || KotoHelper.SignedNumber(node, out _) is not null ||
            IsLiteralOnlyOperation(node) || IsLiteralOnlyFromEnd(node) || IsLiteralOnlyRange(node));
    }

    // SPEC 4.6.2, 12.3: a range whose written boundaries, at least one, are all literal-only is itself literal-only.
    private static bool IsLiteralOnlyRange(Koto node)
        => node is RangeKoto range && (range.Start ?? range.End) is not null &&
            (range.Start is null || IsLiteralOnlyPosition(range.Start)) && (range.End is null || IsLiteralOnlyPosition(range.End));

    // SPEC 12.3: one classification serves waiting arguments, defaults and integer-only positions. Numeric classes
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

        if (node is UnaryKoto { Akind: KotoKind.PrefixMinus or KotoKind.PrefixPlus } sign)
        {
            return NumericLiteralDefault(sign.Operand);
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

    // SPEC 8.7.4: an untyped literal fits an unbound integer Type only when it fits all twelve integer Types, 0 through 127.
    private static bool FitsGenericInteger(NumberLiteralKoto literal, bool negative)
        => literal.IsInteger && literal.TryGetIntegerMagnitude(out var magnitude) && (negative ? magnitude == 0 : magnitude <= 127);

    private static bool LiteralCategoryMatches(NumberLiteralKoto literal, BoundType type)
        => literal.IsInteger ? type.HasIntegerArithmetic : type.IsFloatingPoint;

    private static bool FitsLiteral(NumberLiteralKoto literal, BoundType type, bool negative, int pointerWidth)
    {
        if (type.IsFloatingPoint)
        {
            return ScalarTypes.TryFloatLiteral(literal, type, negative, out _);
        }

        return literal.TryGetIntegerMagnitude(out var magnitude) && FitsIntegerMagnitude(magnitude, type, negative, pointerWidth);
    }

    // SPEC 3.1.1: a wrapping integer Type fits literals by the range of its integer argument.
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

    // SPEC 8.7.4: a symbolic Type proven PrimitiveInteger has the built-in integer operators; each instance uses the
    // operations of its concrete Type.
    // A shared literal argument constructs an owner temporary before acquiring its borrow.
    private static BoundType LiteralInputType(BoundType parameter)
        => parameter is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components: [var referent] } ? referent : parameter;

    // A symbolic Type whose literals are the generic 0 through 127 (SPEC 8.7.4).
    private bool TakesGenericLiterals(BoundType? type, BindingScope scope) => this.IsGenericInteger(type, scope) || this.IsGenericWrapping(type, scope);

    // SPEC 12.3: the default Type of an unfitted literal or literal-only expression, or null for null and other syntax.
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
            // SPEC 4.6.2: each boundary fits its S or E, and a `^a` operand the T of FromEnd<T>. A comparison operand is
            // already fitted.
            return this.FitsLiteralPosition(node, type, scope);
        }

        var number = KotoHelper.SignedNumber(node, out var negative);
        if (number is not null && this.TakesGenericLiterals(type, scope))
        {
            return FitsGenericInteger(number, negative);
        }

        if (number is null && IsLiteralOnlyOperation(node))
        {
            // SPEC 12.3: every literal fits the Type its operator propagates and every operator is defined for that Type;
            // a shift count is typed independently of the candidate. A wrapping integer Type has unary - for every argument.
            var integer = this.IsArithmeticInteger(type, scope);
            var numeric = integer || type.IsFloatingPoint;
            return node switch
            {
                UnaryKoto { Akind: KotoKind.PrefixMinus } negated => numeric && !type.IsUnsignedInteger && !this.IsGenericInteger(type, scope) && this.FitsInputLiteral(negated.Operand, type, scope),
                UnaryKoto { Akind: KotoKind.PrefixPlus } plus => numeric && this.FitsInputLiteral(plus.Operand, type, scope),
                BinaryKoto { Akind: KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan } shifted => integer && this.FitsInputLiteral(shifted.Left, type, scope),
                BinaryKoto binary => numeric && this.FitsInputLiteral(binary.Left, type, scope) && this.FitsInputLiteral(binary.Right, type, scope),
                _ => false,
            };
        }

        if (number is not null)
        {
            return LiteralCategoryMatches(number, type) && FitsLiteral(number, type, negative, this.compilation.PointerWidth);
        }

        return node is not NullLiteralKoto || type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw };
    }

    private BoundType? BindNode(Koto node, BindingScope scope, BoundType? expected = null)
    {
        if (this.WaitsForPosition(node, expected))
        {
            return null; // SPEC 10.5.1, 14.9.2, 4.3: a literal waits for the Type its position decides later.
        }

        // The caller consulted this node: its failure becomes the caller's prerequisite (SPEC 23.1.2).
        var parent = this.BeginConsultation(node);
        var type = this.BindAndAdaptNode(node, scope, expected);
        this.EndConsultation(node, parent);
        this.Consulted(node);
        return type;
    }

    private BoundType? BindAndAdaptNode(Koto node, BindingScope scope, BoundType? expected)
    {
        if (expected is { ContainsParameter: true })
        {
            expected = this.SubstituteIdentityPremises(expected, scope);
        }

        // SPEC 14.9.2: a result target that waited for its position takes the Type that position supplies, or the outcome its root gave it.
        var actual = this.WaitedResult(node) is { } waited ? this.DecideWaiting(node, waited, expected) : this.BindNodeCore(node, scope, expected);
        if (expected is not null && actual is not null && HasArrayLengthHole(expected))
        {
            expected = this.CompleteArrayExpectation(expected, actual);
        }

        if (actual is { ContainsParameter: true } && this.SubstituteIdentityPremises(actual, scope) is var substituted && !ReferenceEquals(substituted, actual))
        {
            // SPEC 8.2: the expression has the one Type that the identity premises of its scope make of its Types.
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
            (expected is not null || !this.AtArgumentPosition(node)))
        {
            actual = this.BindFunctionItem(node, item, scope);
        }

        if (expected is not null && actual is not null && this.ErasesToFunction(node, actual, expected))
        {
            node.ErasedFunctionType = expected;
            return expected;
        }

        if (expected is not null && actual is not null && this.FailsFunctionConversion(node, actual, expected))
        {
            return null;
        }

        // SPEC 10.2: one implicit operation of the common adaptation, recorded once for every later stage; a fitting stored exclusive
        // reference is Reborrowed (SPEC 3.5.1, 10.2.2), as at a bare acquisition position.
        if (expected is not null && actual is not null && node.ErasedFunctionType is null &&
            (!Compatible(actual, expected) || StoresExclusiveReference(node, actual)) &&
            this.ExpectedAdaptation(node, actual, expected) is { } adaptation)
        {
            this.adaptations[node] = adaptation;
            return adaptation.Type;
        }

        // SPEC 3.5: without a fixed expected Type, the bare acquisition of a Place storing an exclusive reference Reborrows it.
        if (expected is null && this.StoredReborrow(node, actual) is { } reborrowed)
        {
            return reborrowed;
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
                this.CheckLiteralArguments(invocation); // SPEC 17.5.4
                return called;
            case SyntaxFormKoto { Akind: KotoKind.InferredCase } inferred:
                var inferredSymbol = this.InferredCase(inferred, scope, expected);
                return inferredSymbol is null ? null : this.BindEnumConstruction(inferred, inferred, inferredSymbol, null, scope, expected);
            case IndexKoto index:
                return this.BindElement(index, scope);
            case RangeKoto range:
                return this.BindRangeValue(range, scope, expected); // SPEC 4.6.2: range syntax outside an index position.
            case MemberAccessKoto { Right: NumberLiteralKoto } tupleElement:
                return this.BindElement(tupleElement, scope);
            case MemberAccessKoto { Right: ErrorKoto missing } incomplete:
                return this.CompleteDependent(incomplete, missing); // SPEC 23.1.2: a member name that failed to parse explains the access.
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
                var target = BindTransferTarget(jump);
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
                    resultType = this.SubstituteIdentityPremises(resultType, scope); // SPEC 8.2
                }

                var actual = jump.Expression is { } operand ? this.BindExpected(operand, scope, resultType, (target as FunctionKoto)?.ReturnType) : BoundType.Unit;
                if (jump.Parent is TryKoto propagation && actual?.Symbol != propagation.Expression.BoundType?.Symbol)
                {
                    this.Fail(jump, BindingFailure.TypeMismatch);
                }

                if (jump is not ContinueKoto)
                {
                    if (jump.Parent is not TryKoto)
                    {
                        targetResult?.Add(jump.Expression, actual);
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
            // SPEC 7.7, 10.5: a value has no Type parameters, so explicit Type arguments select nothing, as at a value call.
            this.BindUnknownChildren(reference, scope);
            var resolved = name.BindingState == BindingState.Resolved;
            return this.Fail(reference, resolved ? BindingFailure.NoApplicableCandidate : BindingFailure.Unsupported, !resolved);
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
        if (Receiver(node) is { } receiver && ((symbol.Declaration is FunctionKoto { BaseInitializer: { } initializer } && IsWithin(node, initializer)) ||
            (!receiver.Whole && (node.Parent is not MemberAccessKoto access || !ReferenceEquals(access.Left, node)))))
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

        // SPEC 7.7.2: a static Property's storage there is a static access, never a capture (StaticStorageAccess).
        if (symbol.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage or BindingSymbolKind.PatternCandidate or BindingSymbolKind.Capture && scope.Function != symbol.Scope.Function &&
            !StaticStorageAccess(symbol, scope))
        {
            if (this.CaptureOuterUse(node, symbol, scope) is not { } capture)
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
                // SPEC 7: an unsafe function supports direct calls only. An overload group is checked after selection.
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
            var target = Grouped(node);
            var assignment = target.Parent is BinaryKoto binary && ReferenceEquals(binary.Left, target) && binary.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals;
            var write = assignment && target.Parent!.Akind == KotoKind.Equals;
            var update = (assignment && !write) || target.Parent?.Akind is KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement;
            if (IsSpecialField(node, out var specialFunction) && specialFunction.IsConstructor)
            {
                if (!ReferenceEquals(symbol.Scope.Owner, specialFunction.BoundSymbol!.Scope.Owner) || !property.IsStored ||
                    (!write && (!property.Getter.IsStandard || (update && !property.Setter.IsStandard))))
                {
                    // SPEC 6.3.4, 11.7: construction forbids inherited Field and computed access and custom accessor calls on self.
                    return this.Fail(node, BindingFailure.InvalidAssignment);
                }

                return Complete(node, this.MemberRead(node, symbol, scope));
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
                        // SPEC 11.3: a receiver that only the accessor's wrongly shaped written receiver rejects rests on that declaration.
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
                    (update && !property.Setter.IsStandard && (input is null || !this.BindPropertyCall(node, property.Setter, scope, input))))
                {
                    return null;
                }

                return update ? this.BindPropertyUpdate(node, scope) : Complete(node, this.AccessorUseType(node, operation));
            }

            if (node is MemberAccessKoto stored && sourceReceiver?.BoundType is { } storedSource && this.memberSelections.TryGetValue(stored, out var storageSelection) && storageSelection.Path is not null)
            {
                this.receiverOperations[node] = new(sourceReceiver, storedSource, storageSelection.DeclaringType, ArgumentOperationKind.StorageProjection, ArgumentAdaptation.Exact, storageSelection.Path);
            }
        }

        if (symbol.Type is null)
        {
            this.ConsultTypeSource(symbol);
        }

        return Complete(node, node is MemberAccessKoto ? this.MemberRead(node, symbol, scope) : symbol.Type);
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
}
