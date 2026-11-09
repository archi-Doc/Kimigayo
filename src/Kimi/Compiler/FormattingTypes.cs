// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 22.10.2's built-in formattable Types, SPEC 22.10.5's bounded ones and rule L's stack limit (STATUS "Text and formatting"; the old
// utf8-formatting §6.1): the closed tables every phase reads by Type. A bound is the implementation's widest encoding of a bounded Type,
// which an interpolation's instance sums; a user or library formatter's Type, a string and a view are unbounded (SPEC 22.10.5's list is
// closed: no declaration adds a row).
internal static class FormattingTypes
{
    /// <summary>Rule L's windows-x64 limit: a direct bounded interpolation whose literal bytes and value bounds sum to at most this many
    /// bytes is formatted on a stack region of exactly that sum, after substitution as before; any other uses the heap buffer.</summary>
    internal const int StackLimit = 1024;

    internal static bool IsBuiltin(BoundType type) => type.Semantics == SemanticsKind.Owner &&
        (ScalarTypes.Supports(type) || ReferenceEquals(type, BoundType.Unit) || ReferenceEquals(type, BoundType.String) || IsUtf8Slice(type));

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
        if (ScalarTypes.Width(type) is var width and not 0)
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
    /// 0. A value Type with an abstract part is decided where its instance can only be what it is: a construction other than
    /// <c>Wrapping</c> is unbounded; a Type parameter, a projection, a Semantics term or <c>Wrapping</c> over one is undecided.</summary>
    /// <param name="values">The Tuple of the interpolation's value Types.</param>
    /// <param name="literal">Its literal bytes.</param>
    /// <returns>The region's bytes, 0 for none, or -1 while an undecided value leaves it open: the closed values and the literal bytes
    /// within the limit and no value unbounded.</returns>
    internal static long StackBound(BoundType values, long literal)
    {
        var sum = literal;
        var open = false;
        for (var i = 0; i < values.Components.Count; i++)
        {
            var value = values.Components[i];
            if (!value.HasAbstractPart)
            {
                var bound = Bound(value);
                if (bound < 0)
                {
                    return 0;
                }

                sum += bound;
            }
            else if (value.IsAbstract || value.Symbol?.LibraryDeclaration == KimiDeclarationId.Wrapping)
            {
                open = true;
            }
            else
            {
                return 0;
            }
        }

        return sum > StackLimit ? 0 : open ? -1 : sum;
    }
}
