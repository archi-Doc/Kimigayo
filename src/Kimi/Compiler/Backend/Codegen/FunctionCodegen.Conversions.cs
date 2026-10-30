// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Backend;

// The scalar conversion row of FunctionCodegen (R5 plan C1b, package C 3.2 I5, I7 and I9; DECISIONS R5-M80): an integer, wrapping,
// floating-point, numeric, bit or raw pointer Cast (FunctionCodegen.Casts) into its Place through LlvmScalarText's conversions (P4b), by
// one table keyed by the cast kind, its source's and target's Type classes and their widths. A Proven Cast (MirRvalue.Proven, the
// builder's IMPL 21.3.3 fact) writes no check; any other check Aborts at the statement's location text (SPEC 22.5.1-22.5.2).
internal sealed partial class FunctionCodegen
{
    // SPEC 13.5.4: halfway between the largest f32 and 2^128, from which a double rounds to infinity.
    private const double FloatOverflow = 3.40282356779733661637539395458142568448e38;

    // A conversion operand's class (SPEC 13.5.4, 5.4): an integer or wrapping integer Type by its signedness, a floating-point Type or a raw
    // pointer.
    private enum Scalar : byte
    {
        None,
        Signed,
        Unsigned,
        Floating,
        Pointer,
    }

    // A conversion's check (SPEC 13.5.4, 22.5.2), each one LlvmScalarText writer.
    private enum Bound : byte
    {
        None,
        Range, // An integer outside the target's range: KIMI_E_INT_CONVERSION.
        Truncation, // A floating-point value whose truncation the target cannot hold, NaN or an infinity: KIMI_E_INT_CONVERSION.
        Narrowing, // A finite value that rounds to infinity: KIMI_E_FLOAT_CONVERSION.
    }

    // SPEC 13.5.4, 5.4-5.5: the row of a cast kind from a source class to a target class, by the sign of the target's width less the
    // source's: the LLVM cast (null: the source representation is kept) and its check; null for a pair Binding never admits.
    private static Conversion? ConversionOf(MirCastKind kind, Scalar from, Scalar to, int widening) => (kind, from, to, widening) switch
    {
        (MirCastKind.Integer, _, _, < 0) => new("trunc", Bound.Range), // @Type never truncates or wraps: it checks the range.
        (MirCastKind.Integer, _, _, 0) => new(null, Bound.Range),
        (MirCastKind.Integer, Scalar.Signed, _, > 0) => new("sext", Bound.Range),
        (MirCastKind.Integer, Scalar.Unsigned, _, > 0) => new("zext", Bound.Range),
        (MirCastKind.Wrap, _, _, < 0) => new("trunc"), // The low bits, read as the target.
        (MirCastKind.Wrap, _, _, 0) => new(null),
        (MirCastKind.Wrap, Scalar.Signed, _, > 0) => new("sext"), // Extended by the operand's signedness.
        (MirCastKind.Wrap, Scalar.Unsigned, _, > 0) => new("zext"),
        (MirCastKind.Floating, _, _, < 0) => new("fptrunc", Bound.Narrowing), // Ties to even; NaN, infinities and the sign of zero kept.
        (MirCastKind.Floating, _, _, 0) => new(null),
        (MirCastKind.Floating, _, _, > 0) => new("fpext"),
        (MirCastKind.Numeric, Scalar.Signed, Scalar.Floating, _) => new("sitofp"), // Ties to even; an integer zero is positive zero.
        (MirCastKind.Numeric, Scalar.Unsigned, Scalar.Floating, _) => new("uitofp"),
        (MirCastKind.Numeric, Scalar.Floating, Scalar.Signed, _) => new("fptosi", Bound.Truncation), // Toward zero.
        (MirCastKind.Numeric, Scalar.Floating, Scalar.Unsigned, _) => new("fptoui", Bound.Truncation),
        (MirCastKind.Bits, _, _, 0) => new("bitcast"), // The same bits, NaN payloads and the sign of zero kept.
        (MirCastKind.Pointer, Scalar.Pointer, Scalar.Pointer, _) => new(null), // Address and provenance kept.
        (MirCastKind.Pointer, Scalar.Pointer, Scalar.Unsigned, _) => new("ptrtoint"), // The numeric address.
        (MirCastKind.IntToPointer, _, Scalar.Pointer, _) => new("inttoptr"), // An equal address without provenance.
        _ => null, // A pair of Type classes Binding does not admit.
    };

    // A closed Type's class; None for a Type no conversion row takes (an object handle, a Closure).
    private static Scalar ClassOf(BoundType type)
    {
        if (type.HasIntegerArithmetic)
        {
            return type.Underlying.IsUnsignedInteger ? Scalar.Unsigned : Scalar.Signed;
        }

        if (type.IsFloatingPoint)
        {
            return Scalar.Floating;
        }

        return type.Semantics == SemanticsKind.Raw ? Scalar.Pointer : Scalar.None;
    }

    // A Range's tests in the source Type (SPEC 13.5.4), spelled into text (2 * SpellingLength characters): each side where the source holds
    // a value the target cannot, against the target's extreme at the source's width; none in an instance whose Types the target holds.
    private static ScalarBounds Range(Scalar from, ValueLowering source, Scalar to, ValueLowering target, Span<char> text)
    {
        var width = 8 * source.Layout.Size;
        var signed = from == Scalar.Signed;
        var minimum = Minimum(8 * target.Layout.Size, to == Scalar.Signed);
        var maximum = Maximum(8 * target.Layout.Size, to == Scalar.Signed);
        string? upper = null;
        if (Maximum(width, signed) > maximum)
        {
            upper = signed ? "sgt" : "ugt";
        }

        return new(
            Minimum(width, signed) < minimum ? "slt" : null,
            Spell(new('\0', Signed(minimum, width)), text[..SpellingLength]),
            upper,
            Spell(new('\0', Signed(unchecked((Int128)maximum), width)), text[SpellingLength..]));
    }

    // A Truncation's tests (SPEC 13.5.4): the minimum's predecessor where the source's significand holds it, else the minimum, and
    // 2^(N - sign), unordered so NaN and the infinities fail.
    private static ScalarBounds Truncation(ValueLowering source, Scalar to, ValueLowering target, Span<char> text)
    {
        var signed = to == Scalar.Signed;
        var exponent = (8 * target.Layout.Size) - (signed ? 1 : 0);
        var exact = !signed || exponent < (source.Layout.Size == 4 ? 24 : 53);
        var limit = Math.ScaleB(1.0, exponent);
        return Floating(exact ? "ule" : "ult", (signed ? -limit : 0.0) - (exact ? 1.0 : 0.0), "uge", limit, text);
    }

    // A conversion with its tests.
    private static ScalarConversion Tested(in ScalarConversion conversion, in ScalarBounds bounds)
        => new(conversion.Cast, conversion.Source, conversion.Target, conversion.Input, conversion.Result, bounds);

    // Floating-point bounds in their hexadecimal double spelling.
    private static ScalarBounds Floating(string lowerPredicate, double lower, string upperPredicate, double upper, Span<char> text) => new(
        lowerPredicate,
        Spell(new('x', BitConverter.DoubleToUInt64Bits(lower)), text[..SpellingLength]),
        upperPredicate,
        Spell(new('x', BitConverter.DoubleToUInt64Bits(upper)), text[SpellingLength..]));

    // An integer Type's extremes at a width: 0 or -2^(N - 1), and 2^(N - sign) - 1.
    private static Int128 Minimum(int width, bool signed) => signed ? Int128.MinValue >> (128 - width) : 0;

    private static UInt128 Maximum(int width, bool signed) => UInt128.MaxValue >> (128 - width + (signed ? 1 : 0));

    // A scalar Cast into its Place: its operand in its computation representation, its row's cast and, unless Proven, its check, which
    // Aborts at the statement's location and names its temporaries by the cast's number; the result stored into the Place's storage. Each
    // check is one rule here, its tests and its LlvmScalarText writer: a Range without a side writes the cast alone, and a Narrowing's
    // ordered tests let NaN and the infinities convert.
    private void ScalarCast(in MirStatement statement)
    {
        var rvalue = statement.Rvalue;
        if (!this.Compute(this.body.Operands[rvalue.Start], out var input) || input.Lowering is not { } source ||
            this.LoweringOf(statement.Place) is not { } target)
        {
            return; // A broken contract, recorded.
        }

        var from = ClassOf(input.Type);
        var to = ClassOf(this.InstanceType(statement.Place)!);
        if (ConversionOf((MirCastKind)rvalue.Op, from, to, target.Layout.Size.CompareTo(source.Layout.Size)) is not { } row)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, statement.Source); // Binding admits no other pair (SPEC 13.5.4, 5.4-5.5).
            return;
        }

        var id = this.next++;
        var result = row.Cast is null ? input.Value : new Value('t', id);
        Span<char> text = stackalloc char[7 * SpellingLength]; // The operand, the result, two bounds and a failure's three operands.
        var conversion = new ScalarConversion(
            row.Cast,
            source.ComputationType,
            target.ComputationType,
            Spell(input.Value, text[..SpellingLength]),
            Spell(result, text.Slice(SpellingLength, SpellingLength)),
            default);
        var bounds = text.Slice(2 * SpellingLength, 2 * SpellingLength);
        var failures = text[(4 * SpellingLength)..];
        switch (rvalue.Proven ? Bound.None : row.Bound)
        {
            case Bound.None:
                LlvmScalarText.WriteConversion(this.output, conversion);
                break;
            case Bound.Range:
                var range = Range(from, source, to, target, bounds);
                if (range.LowerPredicate is null && range.UpperPredicate is null)
                {
                    LlvmScalarText.WriteConversion(this.output, conversion);
                    break;
                }

                if (!this.FailureAt(id, statement.Source, failures, out var outside))
                {
                    return; // Recorded.
                }

                LlvmScalarText.WriteCheckedConversion(this.output, Tested(conversion, range), outside);
                break;
            case Bound.Truncation:
                if (!this.FailureAt(id, statement.Source, failures, out var truncated))
                {
                    return; // Recorded.
                }

                LlvmScalarText.WriteCheckedConversion(this.output, Tested(conversion, Truncation(source, to, target, bounds)), truncated);
                break;
            case Bound.Narrowing:
                if (!this.FailureAt(id, statement.Source, failures, out var overflow))
                {
                    return; // Recorded.
                }

                var threshold = Floating("ole", -FloatOverflow, "oge", FloatOverflow, bounds);
                LlvmScalarText.WriteFloatingNarrowing(this.output, Tested(conversion, threshold), overflow);
                break;
            default:
                throw new InvalidOperationException($"The check {row.Bound} has no writer.");
        }

        this.Store(statement, result);
    }

    // The failure of the check named by id, Aborting at a site's location (Location), spelled into text; false after a record.
    private bool FailureAt(int id, in SourceInfo source, Span<char> text, out ScalarCheck failure)
    {
        var located = this.Location(source, out var site);
        failure = located ? this.Failure(id, site, text) : default;
        return located;
    }

    // A row of the conversion table: the LLVM cast (null: the representation is kept) and the check.
    private readonly record struct Conversion(string? Cast, Bound Bound = Bound.None);
}
