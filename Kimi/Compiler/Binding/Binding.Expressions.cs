// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static bool Compatible(BoundType actual, BoundType expected) => FitsType(actual, expected);

    private static bool Writable(Koto node)
    {
        node = KotoHelper.UnwrapParentheses(node);
        if (node.BoundSymbol is { Kind: BindingSymbolKind.Storage, Scope.Owner: PropertyAccessorKoto syntax })
        {
            var accessor = Accessor(syntax);
            if (accessor.Receiver is { Semantics: not SemanticsKind.Uniq })
            {
                return false;
            }
        }

        return node.BoundSymbol?.Declaration is VariableKoto { VariableKind: VariableKind.Var };
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

    private static bool IsUnfittedLiteral(Koto node)
    {
        node = KotoHelper.UnwrapParentheses(node);
        return node.BoundType is null && (node is NumberLiteralKoto or NullLiteralKoto || node is PrefixMinusKoto { Operand: NumberLiteralKoto } or PrefixPlusKoto { Operand: NumberLiteralKoto });
    }

    private static BoundType DefaultLiteralType(NumberLiteralKoto literal, BoundType? expected)
        => expected ?? (literal.IsInteger ? BoundType.I32 : BoundType.F64);

    private static bool LiteralCategoryMatches(NumberLiteralKoto literal, BoundType type)
        => literal.IsInteger ? type.IsInteger : type.IsFloatingPoint;

    private static KotoKind CompoundOperation(KotoKind assignment) => assignment switch
    {
        KotoKind.PlusEquals => KotoKind.Plus,
        KotoKind.MinusEquals => KotoKind.Minus,
        KotoKind.AsteriskEquals => KotoKind.Asterisk,
        KotoKind.SlashEquals => KotoKind.Slash,
        KotoKind.PercentEquals => KotoKind.Percent,
        KotoKind.AmpersandEquals => KotoKind.Ampersand,
        KotoKind.CaretEquals => KotoKind.Caret,
        KotoKind.BarEquals => KotoKind.Bar,
        KotoKind.LessThanLessThanEquals => KotoKind.LessThanLessThan,
        KotoKind.GreaterThanGreaterThanEquals => KotoKind.GreaterThanGreaterThan,
        _ => assignment,
    };

    private static bool FitsLiteral(NumberLiteralKoto literal, BoundType type, bool negative, int pointerWidth)
    {
        if (!literal.IsInteger)
        {
            return FitsFloat(literal.SourceSpelling, type);
        }

        if (!type.IsInteger || !literal.TryGetIntegerMagnitude(out var magnitude))
        {
            return false;
        }

        var signed = type.Name[0] == 'i';
        var bits = type.Name is "isize" or "usize" ? pointerWidth : int.Parse(type.Name.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture);
        if (bits == 0)
        {
            return false;
        }

        if (negative && !signed)
        {
            return magnitude == 0;
        }

        var max = signed ? ((UInt128)1 << (bits - 1)) - (negative ? (UInt128)0 : 1) : bits == 128 ? UInt128.MaxValue : ((UInt128)1 << bits) - 1;
        return magnitude <= max;
    }

    private static bool FitsFloat(ReadOnlySpan<char> source, BoundType type)
    {
        char[]? rented = null;
        try
        {
            if (source.Contains('_'))
            {
                rented = System.Buffers.ArrayPool<char>.Shared.Rent(source.Length);
                var length = 0;
                for (var i = 0; i < source.Length; i++)
                {
                    if (source[i] != '_')
                    {
                        rented[length++] = source[i];
                    }
                }

                source = rented.AsSpan(0, length);
            }

            const System.Globalization.NumberStyles style = System.Globalization.NumberStyles.Float;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            return type.Name switch
            {
                "f32" => float.TryParse(source, style, culture, out var value) && float.IsFinite(value),
                "f64" => double.TryParse(source, style, culture, out var value) && double.IsFinite(value),
                _ => false,
            };
        }
        finally
        {
            if (rented is not null)
            {
                System.Buffers.ArrayPool<char>.Shared.Return(rented);
            }
        }
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

        return Fail(node, BindingFailure.TypeMismatch);
    }

    private BoundType? BindNode(Koto node, BindingScope scope, BoundType? expected = null)
    {
        scope = this.NodeScope(node, scope);
        if (node is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance, Parent: DeclarationContainerKoto } conditionalDeclaration && TryConditionalBlock(conditionalDeclaration, out var implementationBlock))
        {
            this.BindNode(implementationBlock, scope);
            return Complete(node, BoundType.Unit);
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

        if (node.AttributeChain is { } attribute)
        {
            this.BindNode(attribute, scope);
        }

        switch (node)
        {
            case SyntaxFormKoto { Akind: KotoKind.EnumCase } enumeration when TryEnumPayload(enumeration, out var payload):
                Complete(enumeration.Operands[0], BoundType.Unit);
                Complete(payload, BoundType.Unit);
                return Complete(enumeration, BoundType.Unit);
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

                if (container is not (ContractKoto or StructKoto) && container.Bases.Count != 0)
                {
                    return Fail(node, BindingFailure.Unsupported, true);
                }

                return Complete(node, container.BoundSymbol?.Type ?? BoundType.Unit);
            case FunctionKoto function:
                return this.BindFunction(function, scope);
            case VariableKoto variable:
                return this.BindVariable(variable, scope);
            case AliasKoto alias:
                this.AliasTarget(alias, scope);
                return null;
            case CodeBlockKoto block:
                BoundType? blockType = BoundType.Unit;
                for (var i = 0; i < block.Items.Count; i++)
                {
                    var type = this.BindNode(block.Items[i], scope, block.HasTrailingExpression ? expected : null);
                    if (block.HasTrailingExpression)
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
            case NumberLiteralKoto number:
                var numberType = DefaultLiteralType(number, expected);
                if (!LiteralCategoryMatches(number, numberType))
                {
                    return Fail(node, BindingFailure.TypeMismatch);
                }

                return FitsLiteral(number, numberType, false, this.compilation.PointerWidth) ? Complete(node, numberType) : Fail(node, BindingFailure.InvalidLiteral);
            case NullLiteralKoto:
                return expected is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Unsafe } ? Complete(node, expected) : Fail(node, BindingFailure.MissingType, true);
            case IdentifierNameKoto identifier:
                return this.BindName(identifier, scope);
            case InvocationKoto invocation:
                return this.BindCall(invocation, scope, expected);
            case MemberAccessKoto member:
                var memberSymbol = this.Member(member, scope);
                if (memberSymbol is null)
                {
                    return Fail(member, BindingFailure.MissingName, true);
                }

                return this.BindReference(member, memberSymbol, scope);
            case ParenthesizedKoto parent:
                return Complete(node, this.BindNode(parent.Operand, scope, expected));
            case UnaryKoto unary:
                return this.BindUnary(unary, scope, expected);
            case BinaryKoto binary:
                return this.BindBinary(binary, scope, expected);
            case IfKoto conditional:
                BoundType? common = null;
                var pending = false;
                for (var i = 0; i < conditional.Branches.Count; i++)
                {
                    var branch = conditional.Branches[i];
                    this.RequireType(branch.Condition, scope, BoundType.Boolean);
                    var branchType = this.BindNode(branch.Body, scope, expected);
                    pending |= branchType is null;
                    common = this.Join(node, common, branchType);
                }

                var elseType = conditional.ElseBody is { } elseBody ? this.BindNode(elseBody, scope, expected) : BoundType.Unit;
                pending |= elseType is null;
                common = this.Join(node, common, elseType);
                return Complete(node, pending ? null : common);
            case WhileKoto loop:
                this.RequireType(loop.Condition, scope, BoundType.Boolean);
                this.BindNode(loop.Body, scope);
                return Complete(node, BoundType.Unit);
            case LoopKoto loop:
                this.BindNode(loop.Body, scope);
                return Fail(node, BindingFailure.Unsupported, true);
            case JumpKoto jump:
                BoundType? resultType = expected;
                if (jump is ReturnKoto)
                {
                    for (var boundary = scope; boundary is not null; boundary = boundary.Parent)
                    {
                        if (boundary.Owner is PropertyAccessorKoto a)
                        {
                            resultType = Accessor(a).Result;
                            break;
                        }

                        if (boundary.Owner is FunctionKoto f)
                        {
                            resultType = this.symbols.GetValueOrDefault(f)?.Type;
                            break;
                        }
                    }
                }

                if (jump.Expression is { } expression)
                {
                    var actual = this.BindNode(expression, scope, resultType);
                    if (actual is not null && resultType is not null && !Compatible(actual, resultType))
                    {
                        Fail(expression, BindingFailure.TypeMismatch);
                    }
                }

                return Complete(node, BoundType.Never);
            case BlockStatementKoto statement:
                this.BindNode(statement.Body, scope);
                return Complete(node, BoundType.Unit);
            case RequireKoto require:
                this.RequireType(require.Condition, scope, BoundType.Boolean);
                this.BindNode(require.ElseBody, scope);
                return Complete(node, BoundType.Unit);
            case LabeledKoto labeled:
                return Complete(node, this.BindNode(labeled.Target, scope, expected));
            case TupleLiteralKoto tuple:
                return this.BindTuple(tuple, scope, expected);
            case ArrayLiteralKoto array when expected is { Kind: BoundTypeKind.FixedArray }:
                for (var i = 0; i < array.Elements.Count; i++)
                {
                    this.RequireType(array.Elements[i], scope, expected.Components[0]);
                }

                return array.Elements.Count == expected.Length ? Complete(node, expected) : Fail(node, BindingFailure.TypeMismatch);
            case PropertyAccessorKoto accessor:
                return this.BindAccessorBody(accessor, scope);
        }

        // Unsupported semantics stay explicit and cannot pass final Bound checking.
        this.BindUnknownChildren(node, scope);
        return Fail(node, BindingFailure.Unsupported, true);
    }

    private BoundType? BindFunction(FunctionKoto function, BindingScope scope)
    {
        var symbol = this.symbols.GetValueOrDefault(function);
        if (symbol is not null)
        {
            this.BindHeader(symbol);
        }

        for (var i = 0; i < function.GenericArguments.Count; i++)
        {
            this.BindType(function.GenericArguments[i], scope);
        }

        for (var i = 0; i < function.TypeConstraints.Count; i++)
        {
            this.BindNode(function.TypeConstraints[i], scope);
        }

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var parameter = function.Parameters[i];
            this.BindType(parameter.Type, scope);
            if (parameter.DefaultValue is { } value)
            {
                this.RequireType(value, scope.Parent ?? scope, parameter.Type.BoundType);
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
            var result = this.BindNode(expression, scope, symbol?.Type);
            if (symbol is not null)
            {
                if (symbol.Type is { } expected && result is not null && !Compatible(result, expected))
                {
                    Fail(expression, BindingFailure.TypeMismatch);
                }
            }
        }

        if (function.IsAnonymous || function.IsConstructor || function.IsDestructor || function.IsSpecialization)
        {
            return Fail(function, BindingFailure.Unsupported, true);
        }

        return Complete(function, function.IsGenerated ? BoundType.Unit : symbol?.Type);
    }

    private BoundType? BindVariable(VariableKoto variable, BindingScope scope)
    {
        var symbol = this.symbols[variable];
        if (symbol.Resolving)
        {
            return Fail(variable, BindingFailure.Cycle, true);
        }

        if (symbol.Property is not null)
        {
            this.BindHeader(symbol);
        }

        symbol.Resolving = true;
        var declared = symbol.Property is not null ? symbol.Type : variable.TypeKoto is { } type ? this.BindType(type, scope) : null;
        var inferred = variable.InitializerKoto is { } initializer ? this.BindNode(initializer, scope, declared) : null;
        symbol.Resolving = false;
        if (declared is not null && inferred is not null && !this.CheckTypeUse(inferred, declared, variable))
        {
            Fail(variable, BindingFailure.TypeMismatch);
        }

        symbol.Type = declared ?? inferred;
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
            Fail(variable, BindingFailure.MissingType, true);
        }

        return Complete(variable, symbol.Type);
    }

    private BoundType? BindName(IdentifierNameKoto node, BindingScope scope)
    {
        var symbol = this.Lookup(node.IdentifierName, scope, node, false);
        if (symbol is null)
        {
            return Fail(node, BindingFailure.MissingName, true);
        }

        return this.BindReference(node, symbol, scope);
    }

    private BoundType? BindReference(Koto node, BindingSymbol symbol, BindingScope scope)
    {
        node.BoundSymbol = symbol;
        if (symbol.ConditionalDeclaration is not null)
        {
            var conditionalType = node is MemberAccessKoto conditionalMember && this.memberSelections.TryGetValue(conditionalMember, out var memberSelection) ? memberSelection.DeclaringType : null;
            var proof = this.ProveMemberConditions(symbol, conditionalType, scope);
            if (proof != ConstraintProof.Proven)
            {
                this.RequireConstraint(node, proof, this.capabilityMode);
                return null;
            }

            if (symbol.Kind == BindingSymbolKind.Function)
            {
                return Fail(node, BindingFailure.Unsupported, true);
            }
        }

        if (symbol.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage && scope.Function != symbol.Scope.Function)
        {
            return Fail(node, BindingFailure.Capture);
        }

        if (symbol.Kind == BindingSymbolKind.Function)
        {
            this.BindHeader(symbol);
            // A function group is resolved for call selection, but not an inferred first-class value.
            node.BindingState = BindingState.Resolved;
            return null;
        }

        if (symbol.Type is null && symbol.Declaration is VariableKoto variable)
        {
            this.BindVariable(variable, symbol.Scope);
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
            var operation = write ? property.Setter : property.Getter;
            var sourceReceiver = (node as MemberAccessKoto)?.Left;
            if ((write || update) && sourceReceiver?.BoundType is { Semantics: SemanticsKind.Ref or SemanticsKind.ObjRef or SemanticsKind.Rc or SemanticsKind.Arc })
            {
                return Fail(node, BindingFailure.InvalidAssignment);
            }

            if (!operation.IsPresent || !this.Accessible(symbol, scope, operation.Access, sourceReceiver?.BoundType))
            {
                return Fail(node, BindingFailure.Access);
            }

            if (update && (!property.Setter.IsPresent || !this.Accessible(symbol, scope, property.Setter.Access, sourceReceiver?.BoundType)))
            {
                return Fail(node, BindingFailure.Access);
            }

            if (!operation.IsStandard || (update && !property.Setter.IsStandard))
            {
                if (node is MemberAccessKoto projected && sourceReceiver?.BoundType is { } sourceType && this.memberSelections.TryGetValue(projected, out var pathSelection) && pathSelection.Path is not null && operation.Receiver is { } declaredReceiver && this.MemberType(declaredReceiver, pathSelection.DeclaringType) is { } required)
                {
                    if (!this.AdaptInput(sourceReceiver, required, sourceType, scope, pathSelection.Path, pathSelection.DeclaringType, out var projectedReceiver, out var quality, out var kind))
                    {
                        return Fail(node, BindingFailure.TypeMismatch);
                    }

                    this.receiverOperations[node] = new(sourceReceiver, sourceType, projectedReceiver, kind, quality, pathSelection.Path, 0, ProjectedReceiverProof(symbol));
                }

                // Callable Property uses need the operation/Origin plans of expression
                // checking. A declaration-side witness alone must not bypass that boundary.
                return Fail(node, BindingFailure.Unsupported, true);
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

        return Complete(node, type);
    }

    private BoundType? RequireType(Koto node, BindingScope scope, BoundType? expected)
    {
        var actual = this.BindNode(node, scope, expected);
        if (expected is not null && actual is not null && !Compatible(actual, expected))
        {
            Fail(node, BindingFailure.TypeMismatch);
        }

        return actual;
    }

    private BoundType? BindUnary(UnaryKoto unary, BindingScope scope, BoundType? expected)
    {
        if (unary.Akind is KotoKind.PrefixMinus or KotoKind.PrefixPlus && unary.Operand is NumberLiteralKoto number)
        {
            // A directly signed literal is fitted as a signed value (SPEC 12.3.1).
            var type = DefaultLiteralType(number, expected);
            if (!LiteralCategoryMatches(number, type))
            {
                return Fail(unary, BindingFailure.TypeMismatch);
            }

            if (!FitsLiteral(number, type, unary.Akind == KotoKind.PrefixMinus, this.compilation.PointerWidth))
            {
                return Fail(unary, BindingFailure.InvalidLiteral);
            }

            Complete(number, type);
            return Complete(unary, type);
        }

        var operand = this.BindNode(unary.Operand, scope, unary.Akind == KotoKind.Not ? BoundType.Boolean : expected);
        if (operand is null)
        {
            return Complete(unary, null);
        }

        switch (unary.Akind)
        {
            case KotoKind.Not:
                return ReferenceEquals(operand, BoundType.Boolean) ? Complete(unary, operand) : Fail(unary, BindingFailure.TypeMismatch);
            case KotoKind.PrefixPlus:
                return operand.IsNumeric ? Complete(unary, operand) : Fail(unary, BindingFailure.TypeMismatch);
            case KotoKind.PrefixMinus:
                // Negation is defined for signed integers and floating-point values only (SPEC 13.2).
                return operand.IsNumeric && !operand.IsUnsignedInteger ? Complete(unary, operand) : Fail(unary, BindingFailure.TypeMismatch);
            case KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement:
                if (!Writable(unary.Operand))
                {
                    return Fail(unary, BindingFailure.InvalidAssignment);
                }

                // Increment and decrement do not apply to floats (SPEC 13.2).
                return operand.IsInteger ? Complete(unary, operand) : Fail(unary, BindingFailure.TypeMismatch);
            default:
                return Fail(unary, BindingFailure.Unsupported, true);
        }
    }

    private BoundType? BindBinary(BinaryKoto binary, BindingScope scope, BoundType? expected)
    {
        var kind = binary.Akind;
        if (kind is KotoKind.Conversion or KotoKind.As or KotoKind.Is)
        {
            this.BindNode(binary.Left, scope);
            this.BindType(binary.Right, scope);
            return Fail(binary, BindingFailure.Unsupported, true);
        }

        var logical = kind is KotoKind.And or KotoKind.Or;
        var assignment = kind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals;
        var operation = assignment ? CompoundOperation(kind) : kind;
        var comparison = operation is KotoKind.LessThan or KotoKind.LessThanEquals or KotoKind.GreaterThan or KotoKind.GreaterThanEquals or KotoKind.EqualsEquals or KotoKind.ExclamationEquals;
        var shift = operation is KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan;
        BoundType? left;
        BoundType? right;
        if (shift)
        {
            // The count may have any integer Type; only an untyped count adopts the shifted Type (SPEC 13.3).
            left = this.BindNode(binary.Left, scope, assignment ? null : expected);
            right = this.BindNode(binary.Right, scope, left is { IsInteger: true } ? left : null);
        }
        else if (IsUnfittedLiteral(binary.Left) && !IsUnfittedLiteral(binary.Right) && !assignment)
        {
            right = this.BindNode(binary.Right, scope, logical ? BoundType.Boolean : null);
            left = this.BindNode(binary.Left, scope, right ?? (comparison ? null : expected));
        }
        else
        {
            left = this.BindNode(binary.Left, scope, logical ? BoundType.Boolean : comparison || assignment ? null : expected);
            right = this.BindNode(binary.Right, scope, left);
        }

        if (left is null || right is null)
        {
            return Complete(binary, null);
        }

        if (assignment && !Writable(binary.Left) && !(kind == KotoKind.Equals && CanInitializeLocal(binary.Left, scope)))
        {
            return Fail(binary, BindingFailure.InvalidAssignment);
        }

        var result = assignment ? BoundType.Unit : left;
        if (shift)
        {
            return left.IsInteger && right.IsInteger ? Complete(binary, result) : Fail(binary, BindingFailure.TypeMismatch);
        }

        if (!Compatible(right, left))
        {
            return Fail(binary, BindingFailure.TypeMismatch);
        }

        if (kind == KotoKind.Equals)
        {
            return Complete(binary, BoundType.Unit);
        }

        if (logical)
        {
            return ReferenceEquals(left, BoundType.Boolean) ? Complete(binary, BoundType.Boolean) : Fail(binary, BindingFailure.TypeMismatch);
        }

        // Only built-in primitive operations are decided here. User Types need Contract mappings or
        // pointer rules (SPEC 13.4.1, 5.3), which are not bound yet.
        var primitive = left.Kind == BoundTypeKind.Primitive && !ReferenceEquals(left, BoundType.Never);
        if (comparison)
        {
            // bool and Unit support equality only; numbers, char, and string are also ordered (SPEC 13.4).
            var ordered = left.IsNumeric || ReferenceEquals(left, BoundType.Char) || ReferenceEquals(left, BoundType.String);
            if (primitive && (ordered || operation is KotoKind.EqualsEquals or KotoKind.ExclamationEquals))
            {
                return Complete(binary, BoundType.Boolean);
            }

            return primitive ? Fail(binary, BindingFailure.TypeMismatch) : Fail(binary, BindingFailure.Unsupported, true);
        }

        if (left.IsNumeric)
        {
            // % and bitwise operators accept integers only, including their compound forms (SPEC 13.3).
            return operation is KotoKind.Percent or KotoKind.Ampersand or KotoKind.Caret or KotoKind.Bar && !left.IsInteger
                ? Fail(binary, BindingFailure.TypeMismatch)
                : Complete(binary, result);
        }

        // string + string concatenates; there is no other built-in string arithmetic (SPEC 13.3).
        if (operation == KotoKind.Plus && ReferenceEquals(left, BoundType.String))
        {
            return Complete(binary, result);
        }

        return primitive ? Fail(binary, BindingFailure.TypeMismatch) : Fail(binary, BindingFailure.Unsupported, true);
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
}
