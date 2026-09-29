// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 17.4.4: a position or range key built only from literal-only expressions, `^` applied to one and omitted boundaries
// is judged exactly. Each position is `c * L + k` in the target length L, and resolution is a set of linear inequalities
// in L; the key is warned about when no length resolves it or, for a fixed array, when its length N does not. The warning
// changes neither Type fitting, execution nor overload choice.
public sealed partial class Binding
{
    private readonly List<(Koto Node, string Message)> positionWarnings = new();
    private int reportedPositionWarnings;

    // Whether a literal-only integer expression has a known value in the i32 arithmetic it is evaluated in; an operation
    // that would Abort at run time (overflow, division by zero, an invalid shift count) has none.
    private static bool TryLiteralValue(Koto node, out long value)
    {
        value = 0;
        node = KotoHelper.UnwrapParentheses(node);
        switch (node)
        {
            case NumberLiteralKoto { IsInteger: true } number:
                if (!number.TryGetIntegerMagnitude(out var magnitude) || magnitude > (UInt128)int.MaxValue + 1)
                {
                    return false;
                }

                value = (long)magnitude;
                return true;
            case PrefixMinusKoto negated:
                if (!TryLiteralValue(negated.Operand, out var operand))
                {
                    return false;
                }

                value = -operand;
                return value is >= int.MinValue and <= int.MaxValue;
            case PrefixPlusKoto plus:
                return TryLiteralValue(plus.Operand, out value) && value <= int.MaxValue;
            case BinaryKoto binary when TryLiteralValue(binary.Left, out var left) && TryLiteralValue(binary.Right, out var right) &&
                left <= int.MaxValue && right <= int.MaxValue:
                value = binary.Akind switch
                {
                    KotoKind.Plus => left + right,
                    KotoKind.Minus => left - right,
                    KotoKind.Asterisk => left * right,
                    KotoKind.Slash when right != 0 && !(left == int.MinValue && right == -1) => left / right,
                    KotoKind.Percent when right != 0 && !(left == int.MinValue && right == -1) => left % right,
                    KotoKind.Ampersand => left & right,
                    KotoKind.Bar => left | right,
                    KotoKind.Caret => left ^ right,
                    KotoKind.LessThanLessThan when right is >= 0 and < 32 => (int)left << (int)right,
                    KotoKind.GreaterThanGreaterThan when right is >= 0 and < 32 => (int)left >> (int)right,
                    _ => long.MinValue,
                };
                return value is >= int.MinValue and <= int.MaxValue;
            default:
                return false;
        }
    }

    // A literal position `c * L + k`: an integer literal (c = 0), `^a` (c = 1, k = -a), or an omitted start or end.
    private static bool TryLiteralPosition(Koto? boundary, bool end, out bool fromLength, out long offset)
    {
        fromLength = end;
        offset = 0;
        if (boundary is null)
        {
            return true;
        }

        var node = KotoHelper.UnwrapParentheses(boundary);
        fromLength = node is FromEndIndexKoto;
        if (!TryLiteralValue(node is FromEndIndexKoto fromEnd ? fromEnd.Operand : node, out var value))
        {
            return false;
        }

        offset = fromLength ? -value : value;
        return true;
    }

    // Judges the key of a built-in selection whose receiver sequence is `core`.
    private void CheckLiteralKey(IndexKoto source, BoundType core)
    {
        var fixedLength = core.Kind == BoundTypeKind.FixedArray && core.Length >= 0 ? core.Length : -1;
        var key = KotoHelper.UnwrapParentheses(source.Right);
        bool everyLength;
        bool atLength;
        if (key is RangeKoto range)
        {
            if (!TryLiteralPosition(range.Start, false, out var startFromLength, out var start) ||
                !TryLiteralPosition(range.End, true, out var endFromLength, out var end))
            {
                return;
            }

            everyLength = !RangeResolves(range.IsInclusive, startFromLength, start, endFromLength, end, -1);
            atLength = fixedLength >= 0 && !RangeResolves(range.IsInclusive, startFromLength, start, endFromLength, end, fixedLength);
        }
        else
        {
            if (!TryLiteralPosition(key, false, out var fromLength, out var offset))
            {
                return;
            }

            everyLength = !ElementResolves(fromLength, offset, -1);
            atLength = fixedLength >= 0 && !ElementResolves(fromLength, offset, fixedLength);
        }

        if (!everyLength && !atLength)
        {
            return;
        }

        foreach (var warning in this.positionWarnings)
        {
            if (ReferenceEquals(warning.Node, source.Right))
            {
                return;
            }
        }

        var kind = key is RangeKoto ? "range" : "element position";
        this.positionWarnings.Add((source.Right, everyLength
            ? string.Create(CultureInfo.InvariantCulture, $"The {kind} {source.Right} fails to resolve for every length")
            : string.Create(CultureInfo.InvariantCulture, $"The {kind} {source.Right} fails to resolve for the fixed array's length {fixedLength}")));

        static bool ElementResolves(bool fromLength, long offset, long length)
        {
            var interval = new LengthInterval(length);
            interval.RequireElement(fromLength, offset);
            return !interval.IsEmpty;
        }

        static bool RangeResolves(bool closed, bool startFromLength, long start, bool endFromLength, long end, long length)
        {
            var interval = new LengthInterval(length);
            interval.RequireBoundary(startFromLength, start);
            if (closed)
            {
                interval.RequireElement(endFromLength, end);
            }
            else
            {
                interval.RequireBoundary(endFromLength, end);
            }

            interval.Require((endFromLength ? 1 : 0) - (startFromLength ? 1 : 0), end - start); // start <= end (or last).
            return !interval.IsEmpty;
        }
    }

    // The lengths L >= 0 (or the one fixed length) that satisfy every added inequality `a * L + b >= 0`, a in {-1, 0, 1}.
    private struct LengthInterval
    {
        private long low;
        private long high;

        internal LengthInterval(long length)
        {
            (this.low, this.high) = length >= 0 ? (length, length) : (0, long.MaxValue);
        }

        internal readonly bool IsEmpty => this.low > this.high;

        // A boundary `c * L + k` in [0, L].
        internal void RequireBoundary(bool fromLength, long offset)
        {
            this.Require(fromLength ? 1 : 0, offset);
            this.Require(fromLength ? 0 : 1, -offset);
        }

        // An element position `c * L + k` in [0, L - 1].
        internal void RequireElement(bool fromLength, long offset)
        {
            this.Require(fromLength ? 1 : 0, offset);
            this.Require(fromLength ? 0 : 1, -offset - 1);
        }

        internal void Require(long a, long b)
        {
            if (a == 0)
            {
                if (b < 0)
                {
                    (this.low, this.high) = (1, 0);
                }
            }
            else if (a > 0)
            {
                this.low = Math.Max(this.low, -b);
            }
            else
            {
                this.high = Math.Min(this.high, b);
            }
        }
    }
}
