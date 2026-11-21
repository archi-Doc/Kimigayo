// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Globalization;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The explicit numeric conversions of SPEC 13.5.4 that BindConversion selects by their syntax: E@wrap<U> and E@bits<U>, and a direct
// floating-point literal converted to an integer Type; a direct literal operand is folded to the target's constant at compile time.
// The literal fit every number literal reads is here too: an integer literal's range and payload (SPEC 3.1; TryLiteral) and a float
// literal's selected-precision bits (TryFloatLiteral).
public sealed partial class Binding
{
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
    private static int BitWidth(BoundType type)
        => type.Width is 32 or 64 && !ReferenceEquals(type.Underlying, BoundType.ISize) && !ReferenceEquals(type.Underlying, BoundType.USize) ? type.Width : 0;

    // SPEC 3.1's ranges: whether an integer literal of a magnitude and sign fits a Type with a width, and its value as that Type's payload
    // (TyCtxt.Normalize, DECISIONS BU-133); the one literal-fit predicate of lengths, expression literals, a truncated conversion
    // (DECISIONS BU-130) and a literal-only position (TryLiteralBits). A Type whose instance decides its width (Width 0) fits none here.
    private static bool TryLiteral(BoundType? type, UInt128 magnitude, bool negative, out Int128 bits)
    {
        bits = 0;
        if (type is not { Width: > 0 and var width })
        {
            return false;
        }

        var signed = type.IsSigned;
        var limit = signed ? ((UInt128)1 << (width - 1)) - (negative ? 0U : 1U) : width == 128 ? UInt128.MaxValue : ((UInt128)1 << width) - 1;
        if (magnitude > limit || (negative && !signed && magnitude != 0))
        {
            return false;
        }

        bits = TyCtxt.Normalize(unchecked((Int128)(negative ? (UInt128)0 - magnitude : magnitude)), width);
        return true;
    }

    // Selected-precision literal bits, independent of integer fitting and arithmetic.
    private static bool TryFloatLiteral(NumberLiteralKoto number, BoundType? type, bool negative, out long bits)
    {
        if (!number.IsInteger)
        {
            return TryFloatLiteral(number.SourceSpelling, type, negative, out bits);
        }

        bits = 0;
        if (type is not { IsFloatingPoint: true } || !number.TryGetIntegerMagnitude(out var magnitude))
        {
            return false;
        }

        // Format the exact magnitude into stack storage and parse at the destination
        // precision. UInt128 -> double -> float would round twice for some integers.
        Span<char> digits = stackalloc char[39];
        return magnitude.TryFormat(digits, out var written, provider: CultureInfo.InvariantCulture) &&
            TryFloatLiteral(digits[..written], type, negative && magnitude != 0, out bits);
    }

    private static bool TryFloatLiteral(ReadOnlySpan<char> source, BoundType? type, bool negative, out long bits)
    {
        bits = 0;
        if (type is not { IsFloatingPoint: true })
        {
            return false;
        }

        char[]? rented = null;
        try
        {
            if (source.Contains('_'))
            {
                rented = ArrayPool<char>.Shared.Rent(source.Length);
                var length = 0;
                foreach (var c in source)
                {
                    if (c != '_')
                    {
                        rented[length++] = c;
                    }
                }

                source = rented.AsSpan(0, length);
            }

            // Parse directly into the selected format: f64 then f32 could double-round.
            if (ReferenceEquals(type, BoundType.F32))
            {
                if (!float.TryParse(source, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
                {
                    return false;
                }

                bits = BitConverter.SingleToUInt32Bits(value) ^ (negative ? 0x80000000U : 0U);
            }
            else
            {
                if (!double.TryParse(source, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                {
                    return false;
                }

                bits = BitConverter.DoubleToInt64Bits(value) ^ (negative ? long.MinValue : 0);
            }

            return true;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    // SPEC 13.5.4: E@wrap<U> wraps an integer or wrapping integer value to the integer or wrapping integer Type U, concrete
    // or generic, without a check. A direct integer literal is wrapped at compile time from its exact value; it never fails.
    private BoundType? BindWrapConversion(ConversionKoto conversion, BindingScope scope, Koto argument, string operation)
    {
        var target = this.BindType(argument, scope) is { } bound ? this.ClassForm(bound, scope, out _) : null; // SPEC 8.2.3, as @Type's.
        var operand = KotoHelper.UnwrapParentheses(conversion.Left);
        var number = KotoHelper.SignedNumber(operand, out var negative);
        if (operation != Constants.WrapOperation)
        {
            return this.BindBitConversion(conversion, scope, target, number, negative);
        }

        if (target is not null && number is { IsInteger: true } && this.IsArithmeticInteger(target, scope))
        {
            if (!number.TryGetIntegerMagnitude(out var magnitude))
            {
                return this.Fail(conversion, BindingFailure.InvalidLiteral);
            }

            // The exact value modulo 2^N, stored as the sign-extended N-bit payload like every other constant; a U whose instance decides
            // N (SPEC 8.7.4) keeps it modulo 2^128, whose low N bits are each instance's (TyCtxt.Normalize).
            var payload = negative ? (UInt128)0 - magnitude : magnitude;
            Complete(conversion.Right, target);
            return CompleteFolded(conversion, number, target, TyCtxt.Normalize(unchecked((Int128)payload), target.Width), ConversionBinding.Wrap);
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
                var bits = TyCtxt.Normalize(unchecked((Int128)(negative ? (UInt128)0 - magnitude : magnitude)), width);
                folded = width == 32 ? (Int128)unchecked((uint)(long)bits) : (Int128)unchecked((long)bits);
            }
            else if (!number.IsInteger && BitWidth(target) is var width && width != 0)
            {
                // The floating literal is rounded once to the floating-point Type of the target's width, and its bits are read as U.
                if (!TryFloatLiteral(number.SourceSpelling, width == 32 ? BoundType.F32 : BoundType.F64, negative, out var bits))
                {
                    return this.Fail(conversion, BindingFailure.InvalidLiteral);
                }

                folded = TyCtxt.Normalize(bits, width);
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
        if (sourceFloating == targetFloating || BitWidth(sourceFloating ? target : source) != (ReferenceEquals(sourceFloating ? source : target, BoundType.F32) ? 32 : 64))
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
            !TryLiteral(target, magnitude, negative, out var bits))
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
