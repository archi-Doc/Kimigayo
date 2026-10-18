// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The explicit numeric conversions of SPEC 13.5.4 that BindConversion selects by their syntax: E@wrap<U> and E@bits<U>, and a direct
// floating-point literal converted to an integer Type; a direct literal operand is folded to the target's constant at compile time.
public sealed partial class Binding
{
    // SPEC 13.5.4: the explanation of a numeric conversion rejected for a different integer argument.
    internal const string WrappingConversionNote = "A numeric conversion enters or leaves Wrapping<U> only from U itself, so the range check stays visible (SPEC 13.5.4)";

    // SPEC 13.5.4: a direct literal converted at compile time. The literal, its sign and its parentheses take the target Type,
    // and the conversion carries the folded constant that is produced instead of evaluating the literal.
    private static BoundType? CompleteFolded(ConversionKoto conversion, NumberLiteralKoto number, BoundType target, Int128 folded, ConversionBinding binding)
    {
        conversion.FoldedConstant = folded;
        for (var node = conversion.Left; ; node = ((UnaryKoto)node).Operand)
        {
            Complete(node, target);
            if (ReferenceEquals(node, number))
            {
                break;
            }
        }

        conversion.ConversionBinding = binding;
        return Complete(conversion, target);
    }

    // The fixed width of an integer or wrapping integer Type that @bits accepts: 32 or 64, never a platform-dependent or
    // 128-bit one (SPEC 13.5.4).
    private static int BitWidth(BoundType type, int pointerWidth)
    {
        if (!type.HasIntegerArithmetic || type.Underlying.Name is "isize" or "usize")
        {
            return 0;
        }

        var width = ScalarTypes.Width(type, pointerWidth);
        return width is 32 or 64 ? width : 0;
    }

    // SPEC 13.5.4: E@wrap<U> wraps an integer or wrapping integer value to the integer or wrapping integer Type U, concrete
    // or generic, without a check. A direct literal is wrapped at compile time; for a generic U only the generic literals fit.
    private BoundType? BindWrapConversion(ConversionKoto conversion, BindingScope scope, Koto argument, string operation)
    {
        var target = this.BindType(argument, scope);
        var operand = KotoHelper.UnwrapParentheses(conversion.Left);
        var number = KotoHelper.SignedNumber(operand, out var negative);
        if (operation != Constants.WrapOperation)
        {
            return this.BindBitConversion(conversion, scope, target, number, negative);
        }

        if (target is not null && number is { IsInteger: true } && this.IsArithmeticInteger(target, scope))
        {
            var width = ScalarTypes.Width(target, this.compilation.PointerWidth);
            if (width == 0)
            {
                // A generic U: the literal must fit every instance, so it is an ordinary generic literal and no wrapping happens.
                conversion.ConversionBinding = ConversionBinding.Literal;
                Complete(conversion.Right, target);
                return Complete(conversion, this.BindNode(conversion.Left, scope, target) is null ? null : target);
            }

            if (!number.TryGetIntegerMagnitude(out var magnitude))
            {
                return this.Fail(conversion, BindingFailure.InvalidLiteral);
            }

            // The exact value modulo 2^N, stored as the sign-extended N-bit payload like every other constant.
            var payload = negative ? (UInt128)0 - magnitude : magnitude;
            Complete(conversion.Right, target);
            return CompleteFolded(conversion, number, target, ScalarTypes.Normalize(unchecked((Int128)payload), width), ConversionBinding.Wrap);
        }

        var source = this.BindNode(conversion.Left, scope);
        if (source is null || target is null)
        {
            return Complete(conversion, null);
        }

        if (ReferenceEquals(source, BoundType.Never))
        {
            conversion.ConversionBinding = ConversionBinding.Abrupt;
            return Complete(conversion, BoundType.Never);
        }

        source = this.ReadReferent(conversion.Left, source)!; // SPEC 13.5.2: a read position.
        if (!this.IsArithmeticInteger(source, scope) || !this.IsArithmeticInteger(target, scope))
        {
            return this.Fail(conversion, BindingFailure.InvalidWrapConversion);
        }

        conversion.ConversionBinding = ConversionBinding.Wrap;
        Complete(conversion.Right, target);
        return Complete(conversion, target);
    }

    // SPEC 13.5.4: E@bits<U> reinterprets the bits between a floating-point Type and an integer or wrapping integer Type of
    // the same fixed width, in either direction, without a check; a direct literal is converted at compile time.
    private BoundType? BindBitConversion(ConversionKoto conversion, BindingScope scope, BoundType? target, NumberLiteralKoto? number, bool negative)
    {
        if (target is not null && number is not null)
        {
            Int128 folded;
            if (number.IsInteger && target.IsFloatingPoint)
            {
                // The integer literal is wrapped to the unsigned Type of the target's width, and those bits are read as the float.
                if (!number.TryGetIntegerMagnitude(out var magnitude))
                {
                    return this.Fail(conversion, BindingFailure.InvalidLiteral);
                }

                var width = ReferenceEquals(target, BoundType.F32) ? 32 : 64;
                var bits = ScalarTypes.Normalize(unchecked((Int128)(negative ? (UInt128)0 - magnitude : magnitude)), width);
                folded = width == 32 ? (Int128)unchecked((uint)(long)bits) : (Int128)unchecked((long)bits);
            }
            else if (!number.IsInteger && BitWidth(target, this.compilation.PointerWidth) is var width && width != 0)
            {
                // The floating literal is rounded once to the floating-point Type of the target's width, and its bits are read as U.
                if (!ScalarTypes.TryFloatLiteral(number.SourceSpelling, width == 32 ? BoundType.F32 : BoundType.F64, negative, out var bits))
                {
                    return this.Fail(conversion, BindingFailure.InvalidLiteral);
                }

                folded = ScalarTypes.Normalize(bits, width);
            }
            else
            {
                this.BindNode(conversion.Left, scope);
                return this.Fail(conversion, target.ContainsParameter ? BindingFailure.GenericBitConversion : BindingFailure.InvalidBitConversion);
            }

            Complete(conversion.Right, target);
            return CompleteFolded(conversion, number, target, folded, ConversionBinding.Bits);
        }

        var source = this.BindNode(conversion.Left, scope);
        if (source is null || target is null)
        {
            return Complete(conversion, null);
        }

        if (ReferenceEquals(source, BoundType.Never))
        {
            conversion.ConversionBinding = ConversionBinding.Abrupt;
            return Complete(conversion, BoundType.Never);
        }

        source = this.ReadReferent(conversion.Left, source)!; // SPEC 13.5.2: a read position.
        if (source.ContainsParameter || target.ContainsParameter)
        {
            return this.Fail(conversion, BindingFailure.GenericBitConversion);
        }

        var sourceFloating = source.IsFloatingPoint;
        var targetFloating = target.IsFloatingPoint;
        if (sourceFloating == targetFloating || BitWidth(sourceFloating ? target : source, this.compilation.PointerWidth) != (ReferenceEquals(sourceFloating ? source : target, BoundType.F32) ? 32 : 64))
        {
            return this.Fail(conversion, BindingFailure.InvalidBitConversion);
        }

        conversion.ConversionBinding = ConversionBinding.Bits;
        Complete(conversion.Right, target);
        return Complete(conversion, target);
    }

    // SPEC 13.5.4: a direct floating-point literal converted to an integer Type is truncated toward zero from its exact decimal
    // value and must fit the target's range. The literal never takes an intermediate floating-point Type, so 9007199254740993.0@i64
    // is exact, and a value outside the range is a compile-time error at the literal rather than a runtime Abort.
    private BoundType? BindTruncatedLiteral(ConversionKoto conversion, BoundType target, Koto operand)
    {
        var number = KotoHelper.SignedNumber(operand, out var negative)!;
        if (!number.TryGetTruncatedMagnitude(out var magnitude) ||
            !ScalarTypes.TryLiteral(target, magnitude, negative, this.compilation.PointerWidth, out var bits))
        {
            this.Fail(operand, BindingFailure.InvalidLiteral);
            for (var node = conversion.Left; !ReferenceEquals(node, operand); node = ((UnaryKoto)node).Operand)
            {
                Complete(node, null);
            }

            return Complete(conversion, null);
        }

        return CompleteFolded(conversion, number, target, bits, ConversionBinding.Literal);
    }
}
