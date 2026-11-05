// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The conversions of LlvmScalarText (SPEC 4.7.1, 13.5.4, 22.5.2; DECISIONS R5-M17): a conversion's cast, its range checks over its bounds,
// and a floating narrowing's finite test.

#pragma warning disable SA1402 // The conversion and bounds records are this writer's inputs.

namespace Kimi.Compiler.Backend;

/// <summary>
/// One numeric conversion: its LLVM cast (such as trunc, sext, fptosi or bitcast; null when the source representation is kept),
/// its source and target Types, its input's spelling, its result's name and the bounds whose tests find an input the target
/// cannot hold.
/// </summary>
internal readonly ref struct ScalarConversion(
    string? cast, string source, string target, ReadOnlySpan<char> input, ReadOnlySpan<char> result, ScalarBounds bounds)
{
    internal string? Cast { get; } = cast;

    internal string Source { get; } = source;

    internal string Target { get; } = target;

    internal ReadOnlySpan<char> Input { get; } = input;

    internal ReadOnlySpan<char> Result { get; } = result;

    internal ScalarBounds Bounds { get; } = bounds;
}

/// <summary>
/// A conversion's range tests: per side, the comparison predicate (icmp, or fcmp on a floating source) that holds when the input
/// lies beyond the target on that side, and the bound's spelling in the source Type; a null predicate when the target holds
/// every source value on that side.
/// </summary>
internal readonly ref struct ScalarBounds(string? lowerPredicate, ReadOnlySpan<char> lower, string? upperPredicate, ReadOnlySpan<char> upper)
{
    internal string? LowerPredicate { get; } = lowerPredicate;

    internal ReadOnlySpan<char> Lower { get; } = lower;

    internal string? UpperPredicate { get; } = upperPredicate;

    internal ReadOnlySpan<char> Upper { get; } = upper;
}

internal static partial class LlvmScalarText
{
    // The double infinities, which a floating narrowing converts unchanged.
    private const string PositiveInfinity = "0x7FF0000000000000";
    private const string NegativeInfinity = "0xFFF0000000000000";

    // A conversion without a check or bound (SPEC 13.5.4: widening, integer to floating, wrapping, bit and pointer conversions):
    // its cast, or nothing when the source representation is kept.
    internal static void WriteConversion(TextWriter output, in ScalarConversion conversion)
        => WriteCast(output, conversion, conversion.Result);

    // A checked integer or floating-to-integer conversion (SPEC 13.5.4, 22.5.2): an input beyond a bound Aborts with
    // KIMI_E_INT_CONVERSION; the unordered predicates of a floating source also reject NaN.
    internal static void WriteCheckedConversion(TextWriter output, in ScalarConversion conversion, in ScalarCheck check)
    {
        WriteBounds(output, conversion, check.Id);
        WriteFailure(output, WriteOutside(output, conversion.Bounds, check.Id), AbortReasons.IntegerConversion, check);
        WriteCast(output, conversion, conversion.Result);
    }

    // A narrowing floating conversion (SPEC 13.5.4, 22.5.2): a finite input that rounds beyond the target's finite range Aborts
    // with KIMI_E_FLOAT_CONVERSION, while infinities and NaN convert.
    internal static void WriteFloatingNarrowing(TextWriter output, in ScalarConversion conversion, in ScalarCheck check)
    {
        var id = check.Id;
        WriteBounds(output, conversion, id);
        var outside = WriteOutside(output, conversion.Bounds, id);
        WriteBound(output, "%notPositiveInfinity", id, "one", conversion, PositiveInfinity);
        WriteBound(output, "%notNegativeInfinity", id, "one", conversion, NegativeInfinity);
        WriteName(output, "  %finite", id);
        WriteName(output, " = and i1 %notPositiveInfinity", id);
        WriteName(output, ", %notNegativeInfinity", id);
        WriteName(output, "\n  %overflow", id);
        WriteName(output, " = and i1 %finite", id);
        output.Write(", ");
        WriteName(output, outside, id);
        output.Write('\n');
        WriteFailure(output, "%overflow", AbortReasons.FloatingConversion, check);
        WriteCast(output, conversion, conversion.Result);
    }

    // `result = cast source input to target`, or nothing when the source representation is kept.
    private static void WriteCast(TextWriter output, in ScalarConversion conversion, ReadOnlySpan<char> result)
    {
        if (conversion.Cast is not { } cast)
        {
            return;
        }

        output.Write("  ");
        output.Write(result);
        output.Write(" = ");
        output.Write(cast);
        output.Write(' ');
        output.Write(conversion.Source);
        output.Write(' ');
        output.Write(conversion.Input);
        output.Write(" to ");
        output.Write(conversion.Target);
        output.Write('\n');
    }

    // The tests of the present bounds, lower + Id and upper + Id.
    private static void WriteBounds(TextWriter output, in ScalarConversion conversion, int id)
    {
        var bounds = conversion.Bounds;
        if (bounds.LowerPredicate is { } lower)
        {
            WriteBound(output, "%lower", id, lower, conversion, bounds.Lower);
        }

        if (bounds.UpperPredicate is { } upper)
        {
            WriteBound(output, "%upper", id, upper, conversion, bounds.Upper);
        }
    }

    // `role + id = icmp or fcmp predicate source input, bound`: the comparison of the input in its source Type.
    private static void WriteBound(TextWriter output, string role, int id, string predicate, in ScalarConversion conversion, ReadOnlySpan<char> bound)
    {
        Span<char> name = stackalloc char[NameLength];
        WriteOperation(output, new ScalarOperation(predicate, conversion.Source, conversion.Input, bound, Format(role, id, name), comparison: true));
    }

    // The test that the input lies beyond the target: one bound's, or both joined as invalid + Id. Returns its role.
    private static string WriteOutside(TextWriter output, in ScalarBounds bounds, int id)
    {
        if (bounds.LowerPredicate is null || bounds.UpperPredicate is null)
        {
            return bounds.LowerPredicate is null ? "%upper" : "%lower";
        }

        WriteName(output, "  %invalid", id);
        WriteName(output, " = or i1 %lower", id);
        WriteName(output, ", %upper", id);
        output.Write('\n');
        return "%invalid";
    }
}
