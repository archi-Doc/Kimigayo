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

    private BoundLength InternLength(KotoKind operation, long value = 0, BindingSymbol? parameter = null, BoundLength? left = null, BoundLength? right = null)
    {
        var key = (operation, value, parameter, left, right);
        if (!this.lengths.TryGetValue(key, out var result))
        {
            this.lengths.Add(key, result = new(operation, value, parameter, left, right));
        }

        return result;
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
            this.AddObligation(new(BindingObligationKind.TypeFormation, syntax, BindingDeadline.Instantiation));
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

        if (syntax is not (IdentifierNameKoto or MemberAccessKoto))
        {
            return false;
        }

        var established = this.BindNode(syntax, scope);
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
        if (syntax is ParenthesizedKoto parent)
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
        else if (syntax is IdentifierNameKoto or MemberAccessKoto)
        {
            if (!ReferenceEquals(this.BindNode(syntax, scope), type) || syntax.BoundSymbol is not { } symbol || syntax.BindingFailure != BindingFailure.None)
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
}
