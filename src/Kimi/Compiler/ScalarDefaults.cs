// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The executable default-expression subset: scalar computations, independent owned values and common Functions whose
/// environments retain no Loan of a prepared argument.</summary>
internal static class ScalarDefaults
{
    internal static bool Supports(FunctionKoto function, int parameterIndex)
    {
        var parameter = function.Parameters[parameterIndex];
        if (parameter.DefaultValue is not { } expression)
        {
            return false;
        }

        var type = parameter.Type.BoundType;
        return SupportsResult(type) ? SupportsExpression(expression, function, parameterIndex) :
            IsErasedResult(type) && SupportsErased(expression, function, parameterIndex);
    }

    /// <summary>Gets whether a node lies in the default of a later parameter of the function that declares a parameter, so that
    /// the parameter names the slot its call prepared, which the default can neither move nor keep a borrow of (SPEC 7.2.3).</summary>
    /// <param name="node">The node, such as a closure.</param>
    /// <param name="symbol">The parameter it reads.</param>
    /// <returns>Whether the node is in a later default of the parameter's function.</returns>
    internal static bool InLaterDefault(Koto node, BindingSymbol symbol)
    {
        if (symbol.Kind != BindingSymbolKind.Parameter || symbol.Scope.Owner is not FunctionKoto declaration)
        {
            return false;
        }

        Koto child = node;
        for (var parent = node.Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            if (ReferenceEquals(parent, declaration))
            {
                for (var i = symbol.Slot + 1; i < declaration.Parameters.Count; i++)
                {
                    if (ReferenceEquals(declaration.Parameters[i].DefaultValue, child))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        return false;
    }

    /// <summary>Gets whether a call can deliver this instance of a universally checked default, including a generic Copy
    /// parameter instantiated with a borrowed value (SPEC 7.2.3, 7.6.4).</summary>
    /// <param name="type">The parameter Type.</param>
    /// <returns>Whether lowering delivers the default.</returns>
    internal static bool SupportsDelivered(BoundType? type) => SupportsResult(type) || IsErasedResult(type) || ReferenceTypes.IsBorrow(type) ||
        (type is not null && (type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray || StructStorage.IsStruct(type) || EnumStorage.IsEnum(type)));

    /// <summary>Gets whether a default supplies a supported value; universal Type/Origin fitting and ownership check its dependencies.</summary>
    /// <param name="type">The parameter Type.</param>
    /// <returns>Whether the Type is a supported default result.</returns>
    internal static bool SupportsResult(BoundType? type) => ScalarTypes.Supports(type) || ReferenceEquals(type, BoundType.Unit) || ReferenceEquals(type, BoundType.String) || ReferenceTypes.IsBorrow(type) ||
        SupportsAggregate(type) || (type is not null && AbstractTypes.IsAbstract(type));

    /// <summary>Gets whether a default expression may compute or read a value of this Type: a supported result, or a safe
    /// reference to one, such as a binding of a shared Subject (SPEC 15.1.6).</summary>
    /// <param name="type">The expression Type.</param>
    /// <returns>Whether the Type is readable inside a default.</returns>
    internal static bool SupportsValue(BoundType? type) => ScalarTypes.Supports(type) || ReferenceEquals(type, BoundType.Unit) ||
        (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } && SupportsValue(type.Components[0]));

    internal static bool SupportsPatternValue(BoundType? type)
    {
        if (SupportsValue(type))
        {
            return true;
        }

        if (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            return SupportsPatternValue(type.Components[0]);
        }

        if (type is not { Kind: BoundTypeKind.Tuple, Semantics: SemanticsKind.Owner, Origin: null, OriginArguments.Count: 0 })
        {
            return false;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (!SupportsPatternValue(type.Components[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SupportsAggregate(BoundType? type) => type is not null &&
        (type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray || StructStorage.IsStruct(type) || EnumStorage.IsEnum(type));

    private static bool SupportsExpressionType(BoundType? type) => SupportsValue(type) || SupportsResult(type) || ReferenceTypes.IsString(type) ||
        (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } && SupportsExpressionType(type.Components[0]));

    private static bool IsErasedResult(BoundType? type) => type is { Kind: BoundTypeKind.Function };

    // SPEC 7.2.3, 7.6.4, 14.9.1: a default of a common Function Type whose every result source erases a Function
    // Item, or an anonymous function whose call is Shared and whose entries Copy preceding parameters. Its environment then holds
    // no Borrow of a prepared slot, so the erased value is independent of the pending call. Selections, `do` bodies and their
    // transfers deliver such sources. Generic declarations are checked universally before each call instantiates their storage.
    private static bool SupportsErased(Koto expression, FunctionKoto function, int parameterIndex)
    {
        if (expression.AttributeChain is not null || expression.BindingState != BindingState.Resolved)
        {
            return false;
        }

        if (expression.ErasedFunctionType is not null)
        {
            return SupportsErasedSource(KotoHelper.UnwrapParentheses(expression), function, parameterIndex);
        }

        if (ReferenceEquals(expression.BoundType, BoundType.Never))
        {
            return SupportsExpression(expression, function, parameterIndex);
        }

        return IsErasedResult(expression.BoundType) && expression switch
        {
            IdentifierNameKoto => SupportsPreparedStorage(expression, function, parameterIndex),
            InvocationKoto { BoundValueCall: { } call } invocation => SupportsValueCall(invocation, call, function, parameterIndex),
            InvocationKoto { BoundCall: { } call } invocation => SupportsCall(invocation, call, function, parameterIndex),
            ParenthesizedKoto parentheses => SupportsErased(parentheses.Operand, function, parameterIndex),
            IfKoto conditional => SupportsConditional(conditional, function, parameterIndex, true),
            MatchKoto match => SupportsMatch(match, function, parameterIndex, true),
            DoKoto scoped => SupportsBody(scoped.Body, function, parameterIndex, true),
            LoopKoto loop => SupportsBody(loop.Body, function, parameterIndex, false),
            LabeledKoto labeled => SupportsErased(labeled.Target, function, parameterIndex),
            _ => false,
        };
    }

    private static bool SupportsErasedSource(Koto source, FunctionKoto function, int parameterIndex)
    {
        if (source.AttributeChain is not null || source.BindingState != BindingState.Resolved)
        {
            return false;
        }

        if (source is not FunctionKoto { IsAnonymous: true } literal)
        {
            // The erasure adapter calls the Item's own generated entry, so a compiler-implemented or bodyless declaration, or one
            // that takes its caller's location, has none to erase.
            return source.BoundSymbol is { Kind: BindingSymbolKind.Function, Declaration: FunctionKoto { IsRequirement: false } item } &&
                (item.Body ?? item.ExpressionBody) is not null && !KimiLibraryCatalog.RequiresCallerLocation(source.BoundSymbol) &&
                source.BoundType is { Kind: BoundTypeKind.FunctionItem };
        }

        if (literal.BoundClosure is not { Receiver: SemanticsKind.Ref } closure)
        {
            return false;
        }

        // An entry Copies a prepared value from its pending slot. A local created by the default is acquired by ordinary
        // capture rules and may be moved into its environment. An entry that cannot Copy is the declaration check's own
        // TransferRequired_Kd (SPEC 7.6.2), so its default never executes.
        for (var i = 0; i < closure.Captures.Count; i++)
        {
            var capture = closure.Captures[i];
            if (capture.Source is { Kind: BindingSymbolKind.Local } local && IsInsideDefault(local.Declaration, function, parameterIndex) &&
                capture.Environment.CaptureAcquisition is CaptureAcquisition.Copy or CaptureAcquisition.Move)
            {
                continue;
            }

            if (capture.Environment.CaptureAcquisition != CaptureAcquisition.Copy || capture.Source is not { Kind: BindingSymbolKind.Parameter } parameter ||
                !ReferenceEquals(parameter.Scope.Owner, function) || parameter.Slot >= parameterIndex ||
                (!SupportsPatternValue(capture.Environment.Type) && !SupportsResult(capture.Environment.Type) &&
                    (capture.Environment.Type is not { } type || literal.CodeContext.Compilation.Binding.ProveCopy(type, literal) != ConstraintProof.Refuted)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SupportsExpression(Koto expression, FunctionKoto function, int parameterIndex)
    {
        if (expression.ErasedFunctionType is not null || IsErasedResult(expression.BoundType))
        {
            return SupportsErased(expression, function, parameterIndex);
        }

        if (expression.AttributeChain is not null || expression.BindingState != BindingState.Resolved ||
            (!SupportsExpressionType(expression.BoundType) && !ReferenceEquals(expression.BoundType, BoundType.Never)))
        {
            return false;
        }

        if (expression.CodeContext.Compilation.Binding.TryGetEnumConstruction(expression, out var construction))
        {
            foreach (var operation in construction!.PayloadOperations)
            {
                if (operation.Source is not { } payload || !SupportsExpression(payload, function, parameterIndex))
                {
                    return false;
                }
            }

            return true;
        }

        if (expression.CodeContext.Compilation.Binding.PropertyCall(expression, PropertyAccessorKind.Get) is { BoundCall: { } getter } access)
        {
            return SupportsCall(access, getter, function, parameterIndex);
        }

        return expression switch
        {
            NumberLiteralKoto or BoolLiteralKoto or CharLiteralKoto or StringLiteralKoto => true,
            UnitLiteralKoto or TupleLiteralKoto { Elements.Count: 0 } or TupleTypeKoto { ElementNodes.Count: 0 } => true,
            TupleLiteralKoto tuple => SupportsElements(tuple.Elements, function, parameterIndex),
            ArrayLiteralKoto array => SupportsElements(array.Elements, function, parameterIndex),
            IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Parameter } symbol } =>
                ReferenceEquals(symbol.Scope.Owner, function) && symbol.Slot < parameterIndex,
            IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local, Declaration: FieldKoto local } } =>
                IsInsideDefault(local, function, parameterIndex),
            IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local or BindingSymbolKind.PatternCandidate, Declaration: SyntaxFormKoto { Akind: KotoKind.BindingPattern } pattern } } =>
                IsInsideDefault(pattern, function, parameterIndex),
            MemberAccessKoto field when ElementAccess.BorrowedPathRoot(field) is not null => SupportsPreparedStorage(field, function, parameterIndex),
            BinaryKoto element when ElementAccess.IsSyntax(element) => SupportsPreparedStorage(element, function, parameterIndex),
            ParenthesizedKoto parentheses => SupportsExpression(parentheses.Operand, function, parameterIndex),
            InvocationKoto { BoundValueCall: { } call } invocation => SupportsValueCall(invocation, call, function, parameterIndex),
            InvocationKoto { BoundCall: { } call } invocation => SupportsCall(invocation, call, function, parameterIndex),
            IfKoto conditional => SupportsConditional(conditional, function, parameterIndex, false),
            MatchKoto match => SupportsMatch(match, function, parameterIndex, false),
            RequireKoto require => SupportsExpression(require.Condition, function, parameterIndex) &&
                (require.ElseBody is CodeBlockKoto failure ? SupportsBody(failure, function, parameterIndex, false) : SupportsExpression(require.ElseBody, function, parameterIndex)),
            DoKoto scoped => SupportsBody(scoped.Body, function, parameterIndex, false),
            LoopKoto loop => SupportsBody(loop.Body, function, parameterIndex, false),
            WhileKoto loop => SupportsExpression(loop.Condition, function, parameterIndex) && SupportsBody(loop.Body, function, parameterIndex, false),
            LabeledKoto labeled => SupportsExpression(labeled.Target, function, parameterIndex),
            ExitKoto or YieldKoto or ContinueKoto => SupportsTransfer((JumpKoto)expression, function, parameterIndex),
            ConversionKoto conversion when conversion.ConversionBinding is ConversionBinding.Identity or ConversionBinding.Literal or
                ConversionBinding.Integer or ConversionBinding.Floating or ConversionBinding.Numeric or ConversionBinding.Transfer =>
                SupportsExpression(conversion.Left, function, parameterIndex),
            ConversionKoto { ConversionBinding: ConversionBinding.Borrow, BoundType.Semantics: SemanticsKind.Ref } conversion =>
                SupportsExpression(conversion.Left, function, parameterIndex),
            UnaryKoto unary when unary.Akind is KotoKind.PrefixPlus or KotoKind.PrefixMinus or KotoKind.Not =>
                SupportsExpression(unary.Operand, function, parameterIndex),
            UnaryKoto unary when unary.Akind is KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement =>
                SupportsWritableLocal(unary.Operand, function, parameterIndex),
            BinaryKoto binary when binary.Akind == KotoKind.Equals || ElementAccess.UpdateOperator(binary.Akind) != KotoKind.Invalid =>
                SupportsWritableLocal(binary.Left, function, parameterIndex) && SupportsExpression(binary.Right, function, parameterIndex),
            BinaryKoto binary when binary.Akind is KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash or KotoKind.Percent or
                KotoKind.Ampersand or KotoKind.Bar or KotoKind.Caret or KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan or
                KotoKind.EqualsEquals or KotoKind.ExclamationEquals or KotoKind.LessThan or KotoKind.LessThanEquals or
                KotoKind.GreaterThan or KotoKind.GreaterThanEquals or KotoKind.And or KotoKind.Or =>
                SupportsExpression(binary.Left, function, parameterIndex) && SupportsExpression(binary.Right, function, parameterIndex),
            _ => false,
        };
    }

    private static bool SupportsElements(IReadOnlyList<Koto> elements, FunctionKoto function, int parameterIndex)
    {
        for (var i = 0; i < elements.Count; i++)
        {
            if (!SupportsExpression(elements[i], function, parameterIndex))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 7.2.3: a default may call an ordinary function (effects included), or format text, with value arguments and temporary
    // shared inspections of supported expressions; its scalar or string result is independent of the prepared slots.
    // Each call prepares its own frame of pending slots, so nested omitted defaults may temporarily replace the outer context.
    // Shared receivers (including callable values) use the same temporary inspections. Exclusive arguments retain their guard.
    private static bool SupportsCall(InvocationKoto invocation, BoundCall call, FunctionKoto function, int parameterIndex)
    {
        if (invocation.IsValueCall ||
            call.Target.CompilerFunction is not (CompilerFunctionKind.None or CompilerFunctionKind.TextToString) || call.Target.Declaration is not FunctionKoto { IsAnonymous: false } ||
            call.ArgumentOperations.Length != invocation.ArgumentNodes.Count ||
            (call.Receiver is { } receiver && (call.ReceiverOperation.ParameterType?.Semantics != SemanticsKind.Ref || !SupportsExpression(receiver, function, parameterIndex))))
        {
            return false;
        }

        for (var i = 0; i < invocation.ArgumentNodes.Count; i++)
        {
            if ((call.ArgumentOperations[i].Kind != ArgumentOperationKind.Value && call.ArgumentOperations[i].ParameterType?.Semantics != SemanticsKind.Ref) ||
                !SupportsExpression(invocation.ArgumentNodes[i], function, parameterIndex))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SupportsValueCall(InvocationKoto invocation, BoundValueCall call, FunctionKoto function, int parameterIndex)
    {
        if (call.ReceiverKind != SemanticsKind.Ref || !SupportsExpression(call.Receiver, function, parameterIndex) || call.Arguments.Length != invocation.ArgumentNodes.Count)
        {
            return false;
        }

        for (var i = 0; i < invocation.ArgumentNodes.Count; i++)
        {
            if ((call.Arguments[i].Kind != ArgumentOperationKind.Value && call.Arguments[i].ParameterType?.Semantics != SemanticsKind.Ref) ||
                !SupportsExpression(invocation.ArgumentNodes[i], function, parameterIndex))
            {
                return false;
            }
        }

        return true;
    }

    // `erased` selects the result kind of the arms: an erased common Function (SupportsErased) or a scalar.
    private static bool SupportsConditional(IfKoto conditional, FunctionKoto function, int parameterIndex, bool erased)
    {
        if (conditional.ElseBody is { } otherwise ? !SupportsBody(otherwise, function, parameterIndex, erased) : !ReferenceEquals(conditional.BoundType, BoundType.Unit))
        {
            return false;
        }

        for (var i = 0; i < conditional.Branches.Count; i++)
        {
            var branch = conditional.Branches[i];
            if (!SupportsExpression(branch.Condition, function, parameterIndex) || !SupportsBody(branch.Body, function, parameterIndex, erased))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SupportsMatch(MatchKoto match, FunctionKoto function, int parameterIndex, bool erased)
    {
        if (!SupportsMatchSubject(match.Expression, function, parameterIndex))
        {
            return false;
        }

        for (var i = 0; i < match.Arms.Count; i++)
        {
            var arm = match.Arms[i];
            if ((arm.Guard is { } guard && !SupportsExpression(guard, function, parameterIndex)) || !(arm.Body is CodeBlockKoto block
                ? SupportsBody(block, function, parameterIndex, erased)
                : SupportsResultSource(arm.Body, function, parameterIndex, erased)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SupportsMatchSubject(Koto subject, FunctionKoto function, int parameterIndex)
    {
        if (subject.ErasedFunctionType is not null || SupportsExpressionType(subject.BoundType) || IsErasedResult(subject.BoundType) || ReferenceEquals(subject.BoundType, BoundType.Never))
        {
            return SupportsExpression(subject, function, parameterIndex);
        }

        if (subject.AttributeChain is not null || subject.BindingState != BindingState.Resolved || !SupportsPatternValue(subject.BoundType))
        {
            return false;
        }

        if (subject is ParenthesizedKoto parentheses)
        {
            return SupportsMatchSubject(parentheses.Operand, function, parameterIndex);
        }

        if (subject is ConversionKoto { ConversionBinding: ConversionBinding.Transfer } transfer)
        {
            // SPEC 15.1.6: a ByValue Subject of a prepared scalar-only tuple is a Copy.
            return SupportsMatchSubject(transfer.Left, function, parameterIndex);
        }

        if (subject is TupleLiteralKoto tuple)
        {
            for (var i = 0; i < tuple.Elements.Count; i++)
            {
                if (!SupportsMatchSubject(tuple.Elements[i], function, parameterIndex))
                {
                    return false;
                }
            }

            return true;
        }

        // Prepared scalar-only tuples are Copy. Acquiring the private Subject
        // cannot consume an earlier argument or introduce owned cleanup.
        return SupportsPreparedStorage(subject, function, parameterIndex);
    }

    // An erased body delivers its single item (SPEC 14.2); the items of a longer body are discarded, and its transfers
    // deliver to their own targets (SupportsTransfer).
    private static bool SupportsBody(CodeBlockKoto body, FunctionKoto function, int parameterIndex, bool erased)
    {
        if (body.AttributeChain is not null)
        {
            return false;
        }

        if (erased && body.Items.Count == 1)
        {
            return body.Items[0] is not FieldKoto && SupportsErased(body.Items[0], function, parameterIndex);
        }

        for (var i = 0; i < body.Items.Count; i++)
        {
            var item = body.Items[i];
            if (item is FieldKoto local)
            {
                if (local.AttributeChain is not null ||
                    !(SupportsPatternValue(local.BoundType) || SupportsExpressionType(local.BoundType) || IsErasedResult(local.BoundType)) ||
                    (local.InitializerKoto is { } initializer && !SupportsMatchSubject(initializer, function, parameterIndex)))
                {
                    return false;
                }
            }
            else if (!SupportsExpression(item, function, parameterIndex))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SupportsTransfer(JumpKoto jump, FunctionKoto function, int parameterIndex)
    {
        var target = KotoHelper.ResolveTransferTarget(jump);
        return IsInsideDefault(target, function, parameterIndex) &&
            (jump.Expression is null || SupportsResultSource(jump.Expression, function, parameterIndex, IsErasedResult(target!.BoundType)));
    }

    private static bool SupportsResultSource(Koto source, FunctionKoto function, int parameterIndex, bool erased)
        => erased ? SupportsErased(source, function, parameterIndex) : SupportsExpression(source, function, parameterIndex);

    private static bool SupportsWritableLocal(Koto target, FunctionKoto function, int parameterIndex)
    {
        var root = KotoHelper.UnwrapParentheses(target);
        while (root is BinaryKoto element && ElementAccess.IsSyntax(element) && ElementAccess.TryType(element, out _, out _))
        {
            if (element.Left.BoundType?.Kind != BoundTypeKind.Tuple || !SupportsPatternValue(element.Left.BoundType))
            {
                return false;
            }

            root = KotoHelper.UnwrapParentheses(element.Left);
        }

        if (root is not IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local } symbol } ||
            !IsInsideDefault(symbol.Declaration, function, parameterIndex) || !SupportsExpression(target, function, parameterIndex))
        {
            return false;
        }

        // Guard candidates are a distinct immutable identity. Only the selected
        // body's var binding owns a writable local, just like a var declaration.
        return symbol.Declaration is FieldKoto { VariableKind: VariableKind.Var } or
            SyntaxFormKoto { Akind: KotoKind.BindingPattern, IsMutablePattern: true };
    }

    private static bool SupportsPreparedStorage(Koto source, FunctionKoto function, int parameterIndex)
    {
        if (source.AttributeChain is not null || source.BindingState != BindingState.Resolved)
        {
            return false;
        }

        return source switch
        {
            ParenthesizedKoto parentheses => SupportsPreparedStorage(parentheses.Operand, function, parameterIndex),
            IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Parameter } symbol } =>
                ReferenceEquals(symbol.Scope.Owner, function) && symbol.Slot < parameterIndex,
            IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local, Declaration: FieldKoto local } } =>
                (SupportsPatternValue(local.BoundType) || SupportsExpressionType(local.BoundType) || IsErasedResult(local.BoundType)) && IsInsideDefault(local, function, parameterIndex),
            IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local, Declaration: SyntaxFormKoto { Akind: KotoKind.BindingPattern } pattern } } local =>
                SupportsPatternValue(local.BoundType) && IsInsideDefault(pattern, function, parameterIndex),
            MemberAccessKoto field when ElementAccess.BorrowedPathRoot(field) is not null =>
                SupportsPreparedStorage(field.Left, function, parameterIndex),
            BinaryKoto element when ElementAccess.IsSyntax(element) && ElementAccess.TryType(element, out _, out _) =>
                SupportsPreparedStorage(element.Left, function, parameterIndex) &&
                (element is not IndexKoto || SupportsExpression(element.Right, function, parameterIndex)),
            _ => false,
        };
    }

    private static bool IsInsideDefault(Koto? node, FunctionKoto function, int parameterIndex)
    {
        var expression = function.Parameters[parameterIndex].DefaultValue;
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, expression))
            {
                return true;
            }
        }

        return false;
    }
}
