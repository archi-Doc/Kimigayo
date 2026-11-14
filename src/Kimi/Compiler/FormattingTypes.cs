// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// SPEC 22.10.5's bounded Types and rule L's stack limit (STATUS "Text and formatting"; the old utf8-formatting §6.1): the closed tables
// read by Type. A bound is the implementation's widest encoding of a bounded Type, which an interpolation's instance sums; a user or
// library formatter's Type, a string and a view are unbounded (SPEC 22.10.5's list is closed: no declaration adds a row). The two
// interpolation terms' values read one walk of the value Types (Measure): a direct line's stack region (StackBound) and a write
// position's hint (HintBound) differ only in their rules over it.
internal static class FormattingTypes
{
    /// <summary>Rule L's windows-x64 limit: a direct bounded interpolation whose literal bytes and value bounds sum to at most this many
    /// bytes is formatted on a stack region of exactly that sum, after substitution as before; any other uses the heap buffer.</summary>
    internal const int StackLimit = 1024;

    internal static bool IsUtf8Slice(BoundType? type) => type?.Symbol?.LibraryDeclaration == KimiDeclarationId.Utf8Slice;

    /// <summary>Gets the widest encoding of a value of a Type SPEC 22.10.5 bounds: a primitive or wrapping integer's by its width and
    /// signedness (an isize's as its width's), f32 17, f64 24, char 4, bool 5 and Unit 2.</summary>
    /// <param name="type">A closed Type, or null.</param>
    /// <returns>Its bound in bytes, or -1 for an unbounded Type.</returns>
    internal static int Bound(BoundType? type)
    {
        if (type is null)
        {
            return -1;
        }

        // An integer's widest decimal by its width: the unsigned maximum's digits, or the signed minimum's with its '-'.
        if (type.Width is var width and not 0)
        {
            var signed = type.IsSigned;
            return width switch
            {
                8 => signed ? 4 : 3,
                16 => signed ? 6 : 5,
                32 => signed ? 11 : 10,
                64 => 20,
                128 => signed ? 40 : 39,
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            };
        }

        return type switch
        {
            _ when ReferenceEquals(type, BoundType.F32) => 17,
            _ when ReferenceEquals(type, BoundType.F64) => 24,
            _ when ReferenceEquals(type, BoundType.Char) => 4,
            _ when ReferenceEquals(type, BoundType.Boolean) => 5,
            _ when ReferenceEquals(type, BoundType.Unit) => 2,
            _ => -1,
        };
    }

    /// <summary>Gets rule L's stack region of a direct interpolation (the interpolation-bound term's value, TyCtxt.InterpolationBound): its
    /// literal bytes plus each value's <see cref="Bound"/> when every value is bounded and the sum is at most <see cref="StackLimit"/>, else
    /// 0 (the values as <see cref="Measure"/> decides them).</summary>
    /// <param name="values">The Tuple of the interpolation's value Types.</param>
    /// <param name="literal">Its literal bytes.</param>
    /// <returns>The region's bytes, 0 for none, or -1 while an undecided value leaves it open: the closed values and the literal bytes
    /// within the limit and no value unbounded.</returns>
    internal static long StackBound(BoundType values, long literal) => Measure(values, literal) switch
    {
        { Unbounded: true } or { Sum: < 0 or > StackLimit } => 0,
        { Open: true } => -1,
        var (sum, _, _) => sum,
    };

    /// <summary>Gets the hint of an owning interpolation's write position (the interpolation-hint term's value, TyCtxt.InterpolationHint;
    /// DECISIONS R3-10): the bytes its adapter's later writes may still reserve, the later literal bytes plus each later bounded value's
    /// <see cref="Bound"/>, an unbounded value counted 0 (its write reserves its own exact length), with no limit; a sum past the maximum
    /// object size (2^63 - 1) gives 0. The values as <see cref="Measure"/> decides them.</summary>
    /// <param name="values">The Tuple of the later value Types.</param>
    /// <param name="literal">The later literal bytes.</param>
    /// <returns>The hint's bytes, or -1 while an undecided value leaves it open within the maximum object size.</returns>
    internal static long HintBound(BoundType values, long literal) => Measure(values, literal) switch
    {
        { Sum: < 0 } => 0,
        { Open: true } => -1,
        var (sum, _, _) => sum,
    };

    // SPEC 22.10.5: an interpolation's literal bytes and value Types as an instance can decide them, the one walk both terms read: Sum is
    // the literal plus each bounded value's Bound (-1 once past the maximum object size), Open whether a value is undecided, Unbounded
    // whether one is unbounded. A value Type with an abstract part is decided where its instance can only be what it is: a construction
    // other than Wrapping is unbounded; a Type parameter, a projection, a Semantics term or Wrapping over one is undecided.
    private static (long Sum, bool Open, bool Unbounded) Measure(BoundType values, long literal)
    {
        var sum = literal;
        var open = false;
        var unbounded = false;
        for (var i = 0; i < values.Components.Count; i++)
        {
            var value = values.Components[i];
            if (!value.HasAbstractPart && Bound(value) is var bound and >= 0)
            {
                sum = sum < 0 || sum > long.MaxValue - bound ? -1 : sum + bound;
            }
            else if (value.HasAbstractPart && (value.IsAbstract || value.Symbol?.LibraryDeclaration == KimiDeclarationId.Wrapping))
            {
                open = true;
            }
            else
            {
                unbounded = true;
            }
        }

        return (sum, open, unbounded);
    }
}
