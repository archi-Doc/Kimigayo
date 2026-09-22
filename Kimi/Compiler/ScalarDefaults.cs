// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The executable default-expression subset with no ownership or escaping-Loan effects.</summary>
internal static class ScalarDefaults
{
    internal static bool Supports(FunctionKoto function, int parameterIndex)
    {
        var parameter = function.Parameters[parameterIndex];
        return parameter.DefaultValue is { } expression && SupportsValue(parameter.Type.BoundType) &&
            SupportsExpression(expression, function, parameterIndex);
    }

    internal static bool SupportsValue(BoundType? type) => ScalarTypes.Supports(type) || ReferenceEquals(type, BoundType.Unit);

    internal static bool SupportsPatternValue(BoundType? type)
    {
        if (SupportsValue(type))
        {
            return true;
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

    private static bool SupportsExpression(Koto expression, FunctionKoto function, int parameterIndex)
    {
        if (expression.AttributeChain is not null || expression.BindingState != BindingState.Resolved ||
            (!SupportsValue(expression.BoundType) && !ReferenceEquals(expression.BoundType, BoundType.Never)))
        {
            return false;
        }

        return expression switch
        {
            NumberLiteralKoto or BoolLiteralKoto or CharLiteralKoto => true,
            UnitLiteralKoto or TupleLiteralKoto { Elements.Count: 0 } or TupleTypeKoto { ElementNodes.Count: 0 } => true,
            IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Parameter } symbol } =>
                ReferenceEquals(symbol.Scope.Owner, function) && symbol.Slot < parameterIndex,
            IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local, Declaration: FieldKoto local } } =>
                IsInsideDefault(local, function, parameterIndex),
            IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local or BindingSymbolKind.PatternCandidate, Declaration: SyntaxFormKoto { Akind: KotoKind.BindingPattern } pattern } } =>
                IsInsideDefault(pattern, function, parameterIndex),
            MemberAccessKoto field when ElementAccess.BorrowedPathRoot(field) is not null => SupportsPreparedStorage(field, function, parameterIndex),
            BinaryKoto element when ElementAccess.IsSyntax(element) => SupportsPreparedStorage(element, function, parameterIndex),
            ParenthesizedKoto parentheses => SupportsExpression(parentheses.Operand, function, parameterIndex),
            InvocationKoto { BoundCall: { } call } invocation => SupportsCall(invocation, call, function, parameterIndex),
            IfKoto conditional => SupportsConditional(conditional, function, parameterIndex),
            MatchKoto match => SupportsMatch(match, function, parameterIndex),
            RequireKoto require => SupportsExpression(require.Condition, function, parameterIndex) &&
                (require.ElseBody is CodeBlockKoto failure ? SupportsBody(failure, function, parameterIndex) : SupportsExpression(require.ElseBody, function, parameterIndex)),
            DoKoto scoped => SupportsBody(scoped.Body, function, parameterIndex),
            LoopKoto loop => SupportsBody(loop.Body, function, parameterIndex),
            WhileKoto loop => SupportsExpression(loop.Condition, function, parameterIndex) && SupportsBody(loop.Body, function, parameterIndex),
            LabeledKoto labeled => SupportsExpression(labeled.Target, function, parameterIndex),
            ExitKoto or YieldKoto or ContinueKoto => SupportsTransfer((JumpKoto)expression, function, parameterIndex),
            ConversionKoto conversion when conversion.ConversionBinding is ConversionBinding.Identity or ConversionBinding.Literal or
                ConversionBinding.Integer or ConversionBinding.Floating or ConversionBinding.Numeric =>
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

    // SPEC 7.2.3: a default may call an ordinary function (effects included) whose value arguments are
    // themselves supported default expressions; the scalar result is independent of the prepared slots.
    // Receivers, borrowed arguments, callable values and nested omitted defaults keep their guards, since
    // one call prepares one frame of pending slots.
    private static bool SupportsCall(InvocationKoto invocation, BoundCall call, FunctionKoto function, int parameterIndex)
    {
        if (invocation.IsValueCall || call.Receiver is not null || !call.DefaultArguments.IsEmpty ||
            call.Target.CompilerFunction != CompilerFunctionKind.None || call.Target.Declaration is not FunctionKoto { IsAnonymous: false } ||
            call.ArgumentOperations.Length != invocation.ArgumentNodes.Count)
        {
            return false;
        }

        for (var i = 0; i < invocation.ArgumentNodes.Count; i++)
        {
            if (call.ArgumentOperations[i].Kind != ArgumentOperationKind.Value || !SupportsExpression(invocation.ArgumentNodes[i], function, parameterIndex))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SupportsConditional(IfKoto conditional, FunctionKoto function, int parameterIndex)
    {
        if (conditional.ElseBody is { } otherwise ? !SupportsBody(otherwise, function, parameterIndex) : !ReferenceEquals(conditional.BoundType, BoundType.Unit))
        {
            return false;
        }

        for (var i = 0; i < conditional.Branches.Count; i++)
        {
            var branch = conditional.Branches[i];
            if (!SupportsExpression(branch.Condition, function, parameterIndex) || !SupportsBody(branch.Body, function, parameterIndex))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SupportsMatch(MatchKoto match, FunctionKoto function, int parameterIndex)
    {
        if (!SupportsMatchSubject(match.Expression, function, parameterIndex))
        {
            return false;
        }

        for (var i = 0; i < match.Arms.Count; i++)
        {
            var arm = match.Arms[i];
            if ((arm.Guard is { } guard && !SupportsExpression(guard, function, parameterIndex)) || !(arm.Body is CodeBlockKoto block
                ? SupportsBody(block, function, parameterIndex)
                : SupportsExpression(arm.Body, function, parameterIndex)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SupportsMatchSubject(Koto subject, FunctionKoto function, int parameterIndex)
    {
        if (SupportsValue(subject.BoundType) || ReferenceEquals(subject.BoundType, BoundType.Never))
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

    private static bool SupportsBody(CodeBlockKoto body, FunctionKoto function, int parameterIndex)
    {
        if (body.AttributeChain is not null)
        {
            return false;
        }

        for (var i = 0; i < body.Items.Count; i++)
        {
            var item = body.Items[i];
            if (item is FieldKoto local)
            {
                if (local.AttributeChain is not null ||
                    !SupportsPatternValue(local.BoundType) ||
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
        return IsInsideDefault(KotoHelper.ResolveTransferTarget(jump), function, parameterIndex) &&
            (jump.Expression is null || SupportsExpression(jump.Expression, function, parameterIndex));
    }

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
                SupportsPatternValue(local.BoundType) && IsInsideDefault(local, function, parameterIndex),
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
