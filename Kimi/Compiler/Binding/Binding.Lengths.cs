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

    private BoundLength? BindLength(Koto syntax, BindingScope scope, bool final = true)
    {
        var bits = this.compilation.PointerWidth;
        var maximum = bits == 16 ? short.MaxValue : bits == 32 ? int.MaxValue : long.MaxValue;
        var minimum = bits == 16 ? short.MinValue : bits == 32 ? int.MinValue : long.MinValue;
        BoundLength? result = null;
        if (syntax is ParenthesizedKoto parent)
        {
            result = this.BindLength(parent.Operand, scope, false);
        }
        else if (syntax is NumberLiteralKoto number && number.TryGetIntegerMagnitude(out var magnitude) && magnitude <= long.MaxValue)
        {
            result = this.InternLength(KotoKind.NumberLiteral, (long)magnitude);
        }
        else if (syntax is IdentifierNameKoto name)
        {
            var symbol = this.Lookup(name.IdentifierName, scope, syntax, false);
            if (symbol?.Kind == BindingSymbolKind.LengthParameter)
            {
                syntax.BoundSymbol = symbol;
                result = this.InternLength(KotoKind.IdentifierName, parameter: symbol);
            }
        }
        else if (syntax is UnaryKoto unary && unary.Akind is KotoKind.PrefixPlus or KotoKind.PrefixMinus)
        {
            var operand = this.BindLength(unary.Operand, scope, false);
            if (operand is not null)
            {
                if (unary.Akind == KotoKind.PrefixPlus)
                {
                    result = operand;
                }
                else if (operand.IsConstant && operand.Value != long.MinValue)
                {
                    result = this.InternLength(KotoKind.NumberLiteral, -operand.Value);
                }
                else if (!operand.IsConstant)
                {
                    result = this.InternLength(unary.Akind, left: operand);
                }
            }
        }
        else if (syntax is BinaryKoto binary && binary.Akind is KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash or KotoKind.Percent)
        {
            var left = this.BindLength(binary.Left, scope, false);
            var right = this.BindLength(binary.Right, scope, false);
            if (left is not null && right is not null)
            {
                if (left.IsConstant && right.IsConstant)
                {
                    // Required constant evaluation rejects the same exceptional inputs for
                    // both division and remainder, before executing host arithmetic.
                    if (binary.Akind is KotoKind.Slash or KotoKind.Percent &&
                        (right.Value == 0 || (left.Value == minimum && right.Value == -1)))
                    {
                        Fail(syntax, BindingFailure.InvalidTypeFormation);
                        return null;
                    }

                    try
                    {
                        var value = binary.Akind switch
                        {
                            KotoKind.Plus => checked(left.Value + right.Value),
                            KotoKind.Minus => checked(left.Value - right.Value),
                            KotoKind.Asterisk => checked(left.Value * right.Value),
                            KotoKind.Slash => left.Value / right.Value,
                            _ => left.Value % right.Value,
                        };
                        result = this.InternLength(KotoKind.NumberLiteral, value);
                    }
                    catch (OverflowException)
                    {
                    }
                }
                else
                {
                    result = this.InternLength(binary.Akind, left: left, right: right);
                }
            }
        }

        if (result is null || (result.IsConstant && (result.Value < (final ? 0 : minimum) || result.Value > maximum)))
        {
            Fail(syntax, BindingFailure.InvalidTypeFormation);
            return null;
        }

        Complete(syntax, BoundType.ISize);
        if (final && !result.IsConstant)
        {
            this.AddObligation(new(BindingObligationKind.TypeFormation, syntax, BindingDeadline.Instantiation));
        }

        return result;
    }
}
