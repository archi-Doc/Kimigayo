// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591

/// <summary>Interned checked length expression. Symbolic arithmetic is validated at instantiation.</summary>
public sealed class BoundLength(KotoKind operation, long value, BindingSymbol? parameter, BoundLength? left, BoundLength? right)
{
    public KotoKind Operation { get; } = operation;

    public long Value { get; } = value;

    public BindingSymbol? Parameter { get; } = parameter;

    public BoundLength? Left { get; } = left;

    public BoundLength? Right { get; } = right;

    public bool IsConstant => this.Parameter is null && this.Left is null && this.Right is null;
}

public sealed partial class Binding
{
    private readonly Dictionary<(KotoKind Operation, long Value, BindingSymbol? Parameter, BoundLength? Left, BoundLength? Right), BoundLength> lengths = new();
    private readonly List<BindingSymbol> activeLengthConstants = new();
    private readonly ScratchBuffers<BoundLength?> lengthScratch = new();

    internal bool IsVerifiedLengthObligation(BindingObligation obligation)
    {
        if (obligation is not { Kind: BindingObligationKind.TypeFormation, Deadline: BindingDeadline.Instantiation, Length: { } length } ||
            obligation.Use.BindingState != BindingState.Resolved || obligation.Use.BoundType is not { IsInteger: true })
        {
            return false;
        }

        var function = this.ConstraintScope(obligation.Use).Function;
        return (function is not null && IsSignatureLength(obligation.Use, function)) || this.ProveLength(length, function);
    }

    private static bool IsSignatureLength(Koto node, FunctionKoto function)
    {
        for (Koto? current = node; current is not null && !ReferenceEquals(current, function); current = current.Parent)
        {
            if (ReferenceEquals(current, function.ReturnType))
            {
                return true;
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (ReferenceEquals(current, function.Parameters[i].Type))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SameLengthSignature(BoundLength? a, BoundLength? b, Koto aBinder, Koto bBinder)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null || a.Operation != b.Operation || a.Value != b.Value || !SignatureSlotEquals(a.Parameter, b.Parameter, aBinder, bBinder))
        {
            return false;
        }

        return SameLengthSignature(a.Left, b.Left, aBinder, bBinder) && SameLengthSignature(a.Right, b.Right, aBinder, bBinder);
    }

    private static bool TryLengthArithmetic(KotoKind operation, BoundType type, int width, UInt128 left, UInt128 right, out UInt128 value)
    {
        value = 0;
        var signed = ScalarTypes.Signed(type);
        var minimum = unchecked(-(Int128)((UInt128)1 << (width - 1)));
        if (operation is KotoKind.Slash or KotoKind.Percent && (right == 0 || (signed && (Int128)left == minimum && (Int128)right == -1)))
        {
            return false;
        }

        try
        {
            if (signed)
            {
                var a = (Int128)left;
                var b = (Int128)right;
                var result = operation switch
                {
                    KotoKind.Plus => checked(a + b),
                    KotoKind.Minus => checked(a - b),
                    KotoKind.Asterisk => checked(a * b),
                    KotoKind.Slash => a / b,
                    _ => a % b,
                };
                if (result < minimum || result > ~minimum)
                {
                    return false;
                }

                value = unchecked((UInt128)result);
            }
            else
            {
                value = operation switch
                {
                    KotoKind.Plus => checked(left + right),
                    KotoKind.Minus => checked(left - right),
                    KotoKind.Asterisk => checked(left * right),
                    KotoKind.Slash => left / right,
                    _ => left % right,
                };
                if (width < 128 && value >= ((UInt128)1 << width))
                {
                    return false;
                }
            }

            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static int CompareLength(BoundLength? left, BoundLength? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null || right is null)
        {
            return left is null ? -1 : 1;
        }

        var result = left.Operation.CompareTo(right.Operation);
        if (result == 0)
        {
            result = left.Value.CompareTo(right.Value);
        }

        if (result == 0)
        {
            result = (left.Parameter?.Slot ?? -1).CompareTo(right.Parameter?.Slot ?? -1);
        }

        if (result == 0)
        {
            result = (left.Parameter?.Declaration.Span.Start ?? -1).CompareTo(right.Parameter?.Declaration.Span.Start ?? -1);
        }

        if (result == 0)
        {
            result = CompareLength(left.Left, right.Left);
        }

        return result == 0 ? CompareLength(left.Right, right.Right) : result;
    }

    private BoundLength InternLength(KotoKind operation, long value = 0, BindingSymbol? parameter = null, BoundLength? left = null, BoundLength? right = null)
    {
        if (operation is KotoKind.Plus or KotoKind.Asterisk && CompareLength(left, right) > 0)
        {
            (left, right) = (right, left);
        }

        var key = (operation, value, parameter, left, right);
        if (!this.lengths.TryGetValue(key, out var result))
        {
            this.lengths.Add(key, result = new(operation, value, parameter, left, right));
        }

        return result;
    }

    private bool ValidLength(long value) => value >= 0 && (this.compilation.PointerWidth == 64 || value <= int.MaxValue);

    private bool IsLengthArgument(Koto syntax, BindingScope scope)
    {
        syntax = UnwrapTypeSyntax(syntax);
        if (syntax is ParenthesizedTypeKoto grouped)
        {
            return this.IsLengthArgument(grouped.Type, scope);
        }

        if (syntax is NumberLiteralKoto or ParenthesizedKoto ||
            syntax is UnaryKoto { Akind: KotoKind.PrefixMinus or KotoKind.PrefixPlus } ||
            syntax is BinaryKoto { Akind: KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash or KotoKind.Percent })
        {
            return true;
        }

        var name = TypeSpelling(syntax);
        if (syntax is MemberAccessKoto member)
        {
            return this.LengthArgumentSymbol(member, scope)?.Kind == BindingSymbolKind.Property;
        }

        return name is not null && this.Lookup(name, scope, syntax, false) is { Kind: BindingSymbolKind.LengthParameter or BindingSymbolKind.Local or BindingSymbolKind.Property or BindingSymbolKind.Parameter };
    }

    private BoundLength? SubstituteLength(BoundLength expression, Koto binder, ReadOnlySpan<BoundLength?> arguments)
    {
        if (expression.Parameter is { } parameter)
        {
            return ReferenceEquals(parameter.Scope.Owner, binder) ? arguments[parameter.Slot] : expression;
        }

        if (expression.IsConstant)
        {
            return expression;
        }

        var left = this.SubstituteLength(expression.Left!, binder, arguments);
        var right = expression.Right is { } operand ? this.SubstituteLength(operand, binder, arguments) : null;
        if (left is null || (expression.Right is not null && right is null))
        {
            return null;
        }

        if (!left.IsConstant || right is { IsConstant: false })
        {
            return this.InternLength(expression.Operation, left: left, right: right);
        }

        if (expression.Operation == KotoKind.PrefixMinus)
        {
            var minimum = this.compilation.PointerWidth == 64 ? long.MinValue : int.MinValue;
            return left.Value == minimum ? null : this.InternLength(KotoKind.NumberLiteral, -left.Value);
        }

        return TryLengthArithmetic(expression.Operation, BoundType.ISize, this.compilation.PointerWidth, unchecked((UInt128)(Int128)left.Value), unchecked((UInt128)(Int128)right!.Value), out var value)
            ? this.InternLength(KotoKind.NumberLiteral, unchecked((long)value)) : null;
    }

    private bool InferLength(BoundType pattern, BoundType actual, Koto binder, BoundLength?[] arguments)
    {
        var supplied = actual.LengthExpression ?? this.InternLength(KotoKind.NumberLiteral, actual.Length);
        if (pattern.LengthExpression is { Parameter: { } parameter } && ReferenceEquals(parameter.Scope.Owner, binder))
        {
            var previous = arguments[parameter.Slot];
            if (previous is null)
            {
                arguments[parameter.Slot] = supplied;
                return true;
            }

            return ReferenceEquals(previous, supplied);
        }

        var required = pattern.LengthExpression is { } expression ? this.SubstituteLength(expression, binder, arguments) : this.InternLength(KotoKind.NumberLiteral, pattern.Length);
        // A later input may establish slots used by a compound expression. The
        // completed signature is checked after all direct-slot evidence is known.
        return required is null || ReferenceEquals(required, supplied);
    }

    private bool ProveLength(BoundLength expression, FunctionKoto? function)
    {
        if (expression.IsConstant)
        {
            return this.ValidLength(expression.Value);
        }

        if (expression.Parameter is not null)
        {
            return true; // Every length slot is inherently nonnegative isize.
        }

        if (function is not null)
        {
            if (Contains(function.BoundSymbol?.Type))
            {
                return true;
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (Contains(function.Parameters[i].Type.BoundType))
                {
                    return true;
                }
            }
        }

        return false;

        bool Contains(BoundType? type)
        {
            if (type is null)
            {
                return false;
            }

            if (ReferenceEquals(type.LengthExpression, expression))
            {
                return true;
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (Contains(type.Components[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private bool ProveTypeLengths(BoundType type, FunctionKoto? function)
    {
        if (type.LengthExpression is { } expression && !this.ProveLength(expression, function))
        {
            return false;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (!this.ProveTypeLengths(type.Components[i], function))
            {
                return false;
            }
        }

        return true;
    }

    private BoundLength? CorrespondingLength(BoundLength? length, Koto from, Koto to)
    {
        if (length is null)
        {
            return null;
        }

        var parameter = length.Parameter;
        if (parameter is not null && ReferenceEquals(parameter.Scope.Owner, from))
        {
            parameter = to.BoundSymbol!.Schema!.GenericSlots[parameter.Slot].Symbol;
        }

        return this.InternLength(length.Operation, length.Value, parameter, this.CorrespondingLength(length.Left, from, to), this.CorrespondingLength(length.Right, from, to));
    }

    private BoundLength? BindLength(Koto syntax, BindingScope scope)
    {
        BoundType? type = null;
        if (!this.LengthTypeEvidence(syntax, scope, ref type) ||
            !this.EvaluateLength(syntax, scope, type ?? BoundType.ISize, out var value, out var symbolic))
        {
            Fail(syntax, BindingFailure.InvalidTypeFormation);
            return null;
        }

        if (symbolic is not null)
        {
            if (scope.Function is { } function && !IsSignatureLength(syntax, function) && !this.ProveLength(symbolic, function))
            {
                Fail(syntax, BindingFailure.InvalidTypeFormation);
                return null;
            }

            this.AddObligation(new(BindingObligationKind.TypeFormation, syntax, BindingDeadline.Instantiation, Length: symbolic));
            return symbolic;
        }

        var maximum = ((UInt128)1 << (this.compilation.PointerWidth - 1)) - 1;
        if (value > maximum)
        {
            Fail(syntax, BindingFailure.InvalidTypeFormation);
            return null;
        }

        return this.InternLength(KotoKind.NumberLiteral, (long)value);
    }

    // Probe established integer Types before fitting any literal-only subtree.
    // Normal name/member binding supplies lookup, accessibility and capture checks.
    private bool LengthTypeEvidence(Koto syntax, BindingScope scope, ref BoundType? type)
    {
        if (syntax is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner })
        {
            return this.LengthTypeEvidence(inner, scope, ref type);
        }

        if (syntax is ParenthesizedTypeKoto grouped)
        {
            return this.LengthTypeEvidence(grouped.Type, scope, ref type);
        }

        if (syntax is ParenthesizedKoto parent)
        {
            return this.LengthTypeEvidence(parent.Operand, scope, ref type);
        }

        if (syntax is NumberLiteralKoto { IsInteger: true })
        {
            return true;
        }

        if (syntax is UnaryKoto unary && unary.Akind is KotoKind.PrefixPlus or KotoKind.PrefixMinus)
        {
            return this.LengthTypeEvidence(unary.Operand, scope, ref type);
        }

        if (syntax is BinaryKoto binary && binary.Akind is KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash or KotoKind.Percent)
        {
            return this.LengthTypeEvidence(binary.Left, scope, ref type) && this.LengthTypeEvidence(binary.Right, scope, ref type);
        }

        if (syntax is not (IdentifierNameKoto or MemberAccessKoto or TypeSemanticsKoto { Type: null }))
        {
            return false;
        }

        var established = this.BindLengthName(syntax, scope);
        if (established is not { IsInteger: true, Semantics: SemanticsKind.Owner } || syntax.BindingFailure != BindingFailure.None ||
            (type is not null && !ReferenceEquals(type, established)))
        {
            return false;
        }

        type = established;
        return true;
    }

    private bool EvaluateLength(Koto syntax, BindingScope scope, BoundType type, out UInt128 value, out BoundLength? symbolic)
    {
        value = 0;
        symbolic = null;
        var width = ScalarTypes.Width(type, this.compilation.PointerWidth);
        var signed = ScalarTypes.Signed(type);
        if (syntax is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner })
        {
            if (!this.EvaluateLength(inner, scope, type, out value, out symbolic))
            {
                return false;
            }
        }
        else if (syntax is ParenthesizedTypeKoto grouped)
        {
            if (!this.EvaluateLength(grouped.Type, scope, type, out value, out symbolic))
            {
                return false;
            }
        }
        else if (syntax is ParenthesizedKoto parent)
        {
            if (!this.EvaluateLength(parent.Operand, scope, type, out value, out symbolic))
            {
                return false;
            }
        }
        else if (syntax is NumberLiteralKoto or PrefixMinusKoto { Operand: NumberLiteralKoto } or PrefixPlusKoto { Operand: NumberLiteralKoto })
        {
            var literal = syntax as NumberLiteralKoto ?? (NumberLiteralKoto)((UnaryKoto)syntax).Operand;
            var negative = syntax is PrefixMinusKoto;
            if (!literal.IsInteger || !literal.TryGetIntegerMagnitude(out var magnitude) ||
                !ScalarTypes.TryLiteral(type, magnitude, negative, this.compilation.PointerWidth, out _))
            {
                return false;
            }

            value = negative ? unchecked((UInt128)0 - magnitude) : magnitude;
            Complete(literal, type);
        }
        else if (syntax is IdentifierNameKoto or MemberAccessKoto or TypeSemanticsKoto { Type: null })
        {
            if (!ReferenceEquals(this.BindLengthName(syntax, scope), type) || syntax.BoundSymbol is not { } symbol || syntax.BindingFailure != BindingFailure.None)
            {
                return false;
            }

            if (symbol.Kind == BindingSymbolKind.LengthParameter)
            {
                symbolic = this.InternLength(KotoKind.IdentifierName, parameter: symbol);
            }
            else
            {
                if (symbol.Declaration is not VariableKoto { VariableKind: VariableKind.Let, InitializerKoto: { } initializer } variable ||
                    !(symbol.Kind == BindingSymbolKind.Local ||
                    (symbol.Property is { IsStored: true, Getter.IsStandard: true } && symbol.Scope.Owner is GroupKoto)) ||
                    this.activeLengthConstants.Contains(symbol))
                {
                    return false;
                }

                this.activeLengthConstants.Add(symbol);
                try
                {
                    // Bind a forward static initializer with its declared Type first.
                    // Its ordinary arithmetic Types must remain fixed during evaluation.
                    if (variable.BindingState != BindingState.Resolved)
                    {
                        this.BindNode(variable, symbol.Scope);
                    }

                    if (variable.BindingFailure != BindingFailure.None ||
                        !this.EvaluateLength(initializer, symbol.Scope, type, out value, out symbolic))
                    {
                        return false;
                    }
                }
                finally
                {
                    this.activeLengthConstants.RemoveAt(this.activeLengthConstants.Count - 1);
                }
            }
        }
        else if (syntax is UnaryKoto unary && unary.Akind is KotoKind.PrefixPlus or KotoKind.PrefixMinus)
        {
            if (!this.EvaluateLength(unary.Operand, scope, type, out value, out symbolic))
            {
                return false;
            }

            if (unary.Akind == KotoKind.PrefixMinus)
            {
                if (!signed)
                {
                    return false;
                }

                if (symbolic is not null)
                {
                    symbolic = this.InternLength(unary.Akind, left: symbolic);
                }
                else if ((Int128)value == -(Int128)((UInt128)1 << (width - 1)))
                {
                    return false;
                }
                else
                {
                    value = unchecked((UInt128)0 - value);
                }
            }
        }
        else if (syntax is BinaryKoto binary && binary.Akind is KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash or KotoKind.Percent)
        {
            if (!this.EvaluateLength(binary.Left, scope, type, out var left, out var leftSymbolic) ||
                !this.EvaluateLength(binary.Right, scope, type, out var right, out var rightSymbolic))
            {
                return false;
            }

            if (leftSymbolic is not null || rightSymbolic is not null)
            {
                symbolic = this.InternLength(
                    binary.Akind,
                    left: leftSymbolic ?? this.InternLength(KotoKind.NumberLiteral, unchecked((long)left)),
                    right: rightSymbolic ?? this.InternLength(KotoKind.NumberLiteral, unchecked((long)right)));
            }
            else if (!TryLengthArithmetic(binary.Akind, type, width, left, right, out value))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        Complete(syntax, type);
        return true;
    }

    private BoundType? BindLengthName(Koto syntax, BindingScope scope)
    {
        if (syntax is TypeSemanticsKoto { Type: null } name)
        {
            return this.Lookup(name.Identifier, scope, syntax, false) is { } symbol ? this.BindReference(syntax, symbol, scope) : null;
        }

        if (syntax is MemberAccessKoto member && member.Right is not IdentifierNameKoto)
        {
            var symbol = this.LengthArgumentSymbol(member, scope);
            if (symbol is null)
            {
                return null;
            }

            var result = this.BindReference(member, symbol, scope);
            member.Right.BoundSymbol = symbol;
            Complete(member.Right, result);
            return result;
        }

        return this.BindNode(syntax, scope);
    }

    private BindingSymbol? LengthArgumentSymbol(MemberAccessKoto member, BindingScope scope)
    {
        if (this.TypeName(member.Left, scope, false) is not { Declaration: GroupKoto } qualifier ||
            !this.scopes.TryGetValue(qualifier.Declaration, out var members) || TypeSpelling(member.Right) is not { } name)
        {
            return null;
        }

        member.Left.BoundSymbol = qualifier;
        member.Left.BindingState = BindingState.Resolved;
        return members.Values.GetValueOrDefault(name);
    }
}
