// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Backend;

// The conversion rows (R5 plan C1b, package C 3.2 I5, I7 and I9; DECISIONS R5-M80): a Cast into an unprojected local through
// LlvmScalarText's conversions (P4b), by one table keyed by the cast kind, its source's and target's Type classes and their widths. A
// Proven Cast (MirRvalue.Proven, the builder's IMPL 21.3.3 fact) writes no check; any other check Aborts at the statement's location text
// (SPEC 22.5.1-22.5.2). Upcasts (C6a) and erasures (C5b) have no row yet.
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
    // source's: the LLVM cast (null: the source representation is kept) and its check; null for a kind without a row yet.
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
        _ => null, // Upcast (C6a), Erasure (C5b).
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

    // A check's tests in the source Type, spelled into text (2 * SpellingLength characters), SPEC 13.5.4: a Range tests each side where the
    // source holds a value the target cannot, against the target's extreme at the source's width (none in an instance whose Types the target
    // holds); a Truncation the minimum's predecessor where the source's significand holds it, else the minimum, and 2^(N - sign), unordered
    // so NaN fails; a Narrowing the f32 overflow threshold, ordered so NaN converts.
    private static ScalarBounds Bounds(Bound bound, Scalar from, ValueLowering source, Scalar to, ValueLowering target, Span<char> text)
    {
        var (width, signed, targetWidth, targetSigned) = (8 * source.Layout.Size, from == Scalar.Signed, 8 * target.Layout.Size, to == Scalar.Signed);
        if (bound == Bound.Range)
        {
            var (minimum, maximum) = (Minimum(targetWidth, targetSigned), Maximum(targetWidth, targetSigned));
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

        if (bound == Bound.Truncation)
        {
            var exponent = targetWidth - (targetSigned ? 1 : 0);
            var exact = !targetSigned || exponent < (width == 32 ? 24 : 53);
            var limit = Math.ScaleB(1.0, exponent);
            return Floating(exact ? "ule" : "ult", (targetSigned ? -limit : 0.0) - (exact ? 1.0 : 0.0), "uge", limit, text);
        }

        return bound == Bound.Narrowing ? Floating("ole", -FloatOverflow, "oge", FloatOverflow, text) : default;
    }

    // Floating-point bounds in their hexadecimal double spelling.
    private static ScalarBounds Floating(string lowerPredicate, double lower, string upperPredicate, double upper, Span<char> text) => new(
        lowerPredicate,
        Spell(new('x', BitConverter.DoubleToUInt64Bits(lower)), text[..SpellingLength]),
        upperPredicate,
        Spell(new('x', BitConverter.DoubleToUInt64Bits(upper)), text[SpellingLength..]));

    // An integer Type's extremes at a width: 0 or -2^(N - 1), and 2^(N - sign) - 1.
    private static Int128 Minimum(int width, bool signed) => signed ? Int128.MinValue >> (128 - width) : 0;

    private static UInt128 Maximum(int width, bool signed) => UInt128.MaxValue >> (128 - width + (signed ? 1 : 0));

    // LLVM's hexadecimal spelling of a floating constant of size bytes (LlvmScalarText's precondition): a double's bits, or a float's
    // widened exactly, a NaN with its sign, payload and signaling bit, which a hardware widening would set.
    private static Int128 Hexadecimal(Int128 bits, int size)
    {
        if (size == 8)
        {
            return unchecked((ulong)bits);
        }

        var single = unchecked((uint)bits);
        if ((single & 0x7F800000) != 0x7F800000)
        {
            return BitConverter.DoubleToUInt64Bits(BitConverter.UInt32BitsToSingle(single)); // Exact for a finite value.
        }

        return ((ulong)(single >> 31) << 63) | (0x7FFUL << 52) | ((ulong)(single & 0x7FFFFF) << 29);
    }

    // A Cast into an unprojected local: its operand in its computation representation, its row's cast and, unless Proven, its check, which
    // Aborts at the statement's location and names its temporaries by the cast's number; the result stored into the local's storage.
    private void Cast(in MirStatement statement, FunctionAbi abi)
    {
        var (rvalue, local) = (statement.Rvalue, statement.Place.Root);
        if (!this.Compute(this.body.Operands[rvalue.Start], out var input))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, statement.Source); // A projected Place (C2a), a static (C7b) or a raw pointer's null (C8a).
            return;
        }

        if (input.Lowering is not { } source)
        {
            return; // A broken contract, recorded.
        }

        var (from, to, target) = (ClassOf(input.Type), ClassOf(this.types[local]), this.slots[local].Value);
        if (ConversionOf((MirCastKind)rvalue.Op, from, to, target.Layout.Size.CompareTo(source.Layout.Size)) is not { } row)
        {
            this.Report(DiagnosticCode.Unsupported_Kd, statement.Source);
            return;
        }

        Span<char> text = stackalloc char[4 * SpellingLength];
        var bounds = Bounds(rvalue.Proven ? Bound.None : row.Bound, from, source, to, target, text[..(2 * SpellingLength)]);
        var bound = bounds.LowerPredicate is null && bounds.UpperPredicate is null ? Bound.None : row.Bound;
        var location = bound == Bound.None ? -1 : this.Location(statement.Source, abi);
        if (bound != Bound.None && location < 0)
        {
            return; // Recorded.
        }

        var id = this.next++;
        var result = row.Cast is null ? input.Value : new Value('t', id);
        var conversion = new ScalarConversion(
            row.Cast,
            source.ComputationType,
            target.ComputationType,
            Spell(input.Value, text.Slice(2 * SpellingLength, SpellingLength)),
            Spell(result, text[(3 * SpellingLength)..]),
            bounds);
        switch (bound)
        {
            case Bound.None:
                LlvmScalarText.WriteConversion(this.output, conversion);
                break;
            case Bound.Range or Bound.Truncation:
                LlvmScalarText.WriteCheckedConversion(this.output, conversion, this.Failure(id, location, stackalloc char[3 * SpellingLength]));
                break;
            case Bound.Narrowing:
                LlvmScalarText.WriteFloatingNarrowing(this.output, conversion, this.Failure(id, location, stackalloc char[3 * SpellingLength]));
                break;
            default:
                throw new InvalidOperationException($"The check {bound} has no writer.");
        }

        this.Store(local, result);
    }

    // A row of the conversion table: the LLVM cast (null: the representation is kept) and the check.
    private readonly record struct Conversion(string? Cast, Bound Bound = Bound.None);
}
