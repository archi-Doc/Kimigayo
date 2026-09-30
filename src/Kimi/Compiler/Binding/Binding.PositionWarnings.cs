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
    private readonly List<(Koto Node, string Kind, long FixedLength)> positionWarnings = new();

    private static long FixedLength(BoundType? core) => core is { Kind: BoundTypeKind.FixedArray, Length: >= 0 } ? core.Length : -1;

    private static Int128 SignedLiteral(UInt128 bits, int width) => unchecked((Int128)(bits << (128 - width))) >> (128 - width);

    // Whether a literal-only integer expression has a known value in its bound integer Type; an operation
    // that would Abort at run time (overflow, division by zero, an invalid shift count) has none.
    private bool TryLiteralValue(Koto node, out Int128 value)
    {
        value = 0;
        node = KotoHelper.UnwrapParentheses(node);
        if (!this.TryLiteralBits(node, out var bits))
        {
            return false;
        }

        var width = ScalarTypes.Width(node.BoundType, this.compilation.PointerWidth);
        var signed = ScalarTypes.Signed(node.BoundType!);
        // Resolution only admits [0, isize.MaxValue]. Saturate outside that interval before forming inequalities,
        // so even a u128 maximum or a negated i128 minimum cannot overflow their arithmetic.
        var limit = (Int128)1 << (this.compilation.PointerWidth - 1);
        value = signed ? Int128.Clamp(SignedLiteral(bits, width), -limit, limit) : bits >= (UInt128)limit ? limit : (Int128)bits;
        return true;
    }

    // Fixed-width bit patterns keep the common literal path allocation-free, including 128-bit inputs.
    private bool TryLiteralBits(Koto node, out UInt128 bits)
    {
        bits = 0;
        node = KotoHelper.UnwrapParentheses(node);
        if (node.BoundType is not { IsInteger: true } type || ScalarTypes.Width(type, this.compilation.PointerWidth) is not (> 0 and var width))
        {
            return false;
        }

        var signed = ScalarTypes.Signed(type);
        var mask = UInt128.MaxValue >> (128 - width);
        var sign = (UInt128)1 << (width - 1);
        switch (node)
        {
            case NumberLiteralKoto { IsInteger: true } number:
                return number.TryGetIntegerMagnitude(out bits) && bits <= (signed ? sign - 1 : mask);
            case PrefixMinusKoto { Operand: NumberLiteralKoto number }:
                if (!number.TryGetIntegerMagnitude(out var magnitude) || magnitude > (signed ? sign : 0))
                {
                    return false;
                }

                bits = unchecked((UInt128)0 - magnitude) & mask;
                return true;
            case PrefixMinusKoto negated:
                if (!this.TryLiteralBits(negated.Operand, out var operand) || (signed ? operand == sign : operand != 0))
                {
                    return false;
                }

                bits = unchecked((UInt128)0 - operand) & mask;
                return true;
            case PrefixPlusKoto plus:
                return this.TryLiteralBits(plus.Operand, out bits);
            case BinaryKoto binary when this.TryLiteralBits(binary.Left, out var left) && this.TryLiteralBits(binary.Right, out var right):
                switch (binary.Akind)
                {
                    case KotoKind.Plus:
                        bits = unchecked(left + right) & mask;
                        return signed ? ((left ^ bits) & (right ^ bits) & sign) == 0 : left <= mask - right;
                    case KotoKind.Minus:
                        bits = unchecked(left - right) & mask;
                        return signed ? ((left ^ right) & (left ^ bits) & sign) == 0 : left >= right;
                    case KotoKind.Asterisk:
                        var negativeLeft = signed && (left & sign) != 0;
                        var negativeRight = signed && (right & sign) != 0;
                        var a = negativeLeft ? unchecked((UInt128)0 - left) & mask : left;
                        var b = negativeRight ? unchecked((UInt128)0 - right) & mask : right;
                        var negative = negativeLeft != negativeRight;
                        var limit = signed ? sign - (negative ? 0U : 1U) : mask;
                        if (b != 0 && a > limit / b)
                        {
                            return false;
                        }

                        bits = negative ? unchecked((UInt128)0 - (a * b)) & mask : a * b;
                        return true;
                    case KotoKind.Slash or KotoKind.Percent:
                        if (right == 0 || (signed && left == sign && right == mask))
                        {
                            return false;
                        }

                        bits = signed ? unchecked((UInt128)(binary.Akind == KotoKind.Slash
                            ? SignedLiteral(left, width) / SignedLiteral(right, width)
                            : SignedLiteral(left, width) % SignedLiteral(right, width))) & mask
                            : binary.Akind == KotoKind.Slash ? left / right : left % right;
                        return true;
                    case KotoKind.Ampersand:
                        bits = left & right;
                        return true;
                    case KotoKind.Bar:
                        bits = left | right;
                        return true;
                    case KotoKind.Caret:
                        bits = left ^ right;
                        return true;
                    case KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan when right < (uint)width:
                        bits = binary.Akind == KotoKind.LessThanLessThan ? (left << (int)right) & mask
                            : signed ? unchecked((UInt128)(SignedLiteral(left, width) >> (int)right)) & mask : left >> (int)right;
                        return true;
                    default:
                        return false;
                }

            default:
                return false;
        }
    }

    // A literal position `c * L + k`: an integer literal (c = 0), `^a` (c = 1, k = -a), or an omitted start or end.
    private bool TryLiteralPosition(Koto? boundary, bool end, out bool fromLength, out Int128 offset)
    {
        fromLength = end;
        offset = 0;
        if (boundary is null)
        {
            return true;
        }

        var node = KotoHelper.UnwrapParentheses(boundary);
        fromLength = node is FromEndIndexKoto;
        if (!this.TryLiteralValue(node is FromEndIndexKoto fromEnd ? fromEnd.Operand : node, out var value))
        {
            return false;
        }

        offset = fromLength ? -value : value;
        return true;
    }

    // Judges the key of a built-in selection whose receiver sequence is `core`.
    private void CheckLiteralKey(IndexKoto source, BoundType core)
        => this.CheckLiteralPosition(source.Right, KotoHelper.UnwrapParentheses(source.Right) is RangeKoto ? LiteralRole.Range : LiteralRole.Element, FixedLength(core));

    // SPEC 17.4.4: judges a literal-only position or range passed to a Kimi operation that resolves it: a sequence
    // operation's position or range parameter, or the receiver of `resolve`/`tryResolve` on a position or range Type.
    private void CheckLiteralArguments(InvocationKoto call)
    {
        if (call.BoundCall is not { Target.Declaration: FunctionKoto function } bound || !ReferenceEquals(function.CodeContext.Kotonoha, this.Library.Kotonoha))
        {
            return;
        }

        var owner = function.Parent;
        if (ReferenceEquals(owner, this.Library.Slice.Declaration) || ReferenceEquals(owner, this.Library.DynamicArray.Declaration) || ReferenceEquals(owner, this.Library.FixedArrayMembers))
        {
            var receiver = bound.Receiver?.BoundType;
            var core = receiver is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } ? receiver.Components[0] : receiver;
            var fixedLength = FixedLength(core);
            var mapping = bound.ArgumentToParameter;
            for (var i = 0; i < mapping.Length && i < call.ArgumentNodes.Count; i++)
            {
                if ((uint)mapping[i] < (uint)function.Parameters.Count && OperationRole(function.Name, function.Parameters[mapping[i]].InternalName) is { } role)
                {
                    this.CheckLiteralPosition(call.ArgumentNodes[i], role, fixedLength);
                }
            }
        }
        else if (function.Name is "resolve" or "tryResolve" && bound.Receiver is { } positioned && owner is DeclarationContainerKoto { BoundSymbol: { } type })
        {
            if (ReferenceEquals(type, this.Library.FromEnd) || ReferenceEquals(type, this.Library.Start) || ReferenceEquals(type, this.Library.End))
            {
                this.CheckLiteralPosition(positioned, LiteralRole.Boundary, -1);
            }
            else if (ReferenceEquals(type, this.Library.Range) || ReferenceEquals(type, this.Library.ClosedRange))
            {
                this.CheckLiteralPosition(positioned, LiteralRole.Range, -1);
            }
        }

        // The resolution a public position entry performs (SPEC 4.6.6, 4.7.2).
        static LiteralRole? OperationRole(string? operation, string? parameter) => (operation, parameter) switch
        {
            ("tryGet" or "tryGetUniq" or "remove" or "swapRemove", "index") or ("swap", "first" or "second") => LiteralRole.Element,
            ("splitAt" or "trySplitAt" or "insert", "index") => LiteralRole.Boundary,
            ("trySlice", "range") => LiteralRole.Range,
            _ => null,
        };
    }

    private void CheckLiteralPosition(Koto node, LiteralRole role, long fixedLength)
    {
        var syntax = KotoHelper.UnwrapParentheses(node);
        var maximumLength = this.compilation.PointerWidth == 32 ? int.MaxValue : long.MaxValue;
        bool everyLength;
        bool atLength;
        if (role == LiteralRole.Range)
        {
            if (syntax is not RangeKoto range || !this.TryLiteralPosition(range.Start, false, out var startFromLength, out var start) ||
                !this.TryLiteralPosition(range.End, true, out var endFromLength, out var end))
            {
                return;
            }

            everyLength = !RangeResolves(range.IsInclusive, startFromLength, start, endFromLength, end, -1, maximumLength);
            atLength = fixedLength >= 0 && !RangeResolves(range.IsInclusive, startFromLength, start, endFromLength, end, fixedLength, maximumLength);
        }
        else
        {
            if (syntax is RangeKoto || !this.TryLiteralPosition(syntax, false, out var fromLength, out var offset))
            {
                return;
            }

            everyLength = !PositionResolves(role, fromLength, offset, -1, maximumLength);
            atLength = fixedLength >= 0 && !PositionResolves(role, fromLength, offset, fixedLength, maximumLength);
        }

        if (!everyLength && !atLength)
        {
            return;
        }

        foreach (var warning in this.positionWarnings)
        {
            if (ReferenceEquals(warning.Node, node))
            {
                return;
            }
        }

        var kind = role switch { LiteralRole.Range => "range", LiteralRole.Element => "element position", _ => "position" };
        // A position that fails at every length has no length to name; one that fails at a fixed array's only length names it.
        this.positionWarnings.Add((node, kind, everyLength ? -1 : fixedLength));

        static bool PositionResolves(LiteralRole role, bool fromLength, Int128 offset, long length, long maximumLength)
        {
            var interval = new LengthInterval(length, maximumLength);
            if (role == LiteralRole.Element)
            {
                interval.RequireElement(fromLength, offset);
            }
            else
            {
                interval.RequireBoundary(fromLength, offset);
            }

            return !interval.IsEmpty;
        }

        static bool RangeResolves(bool closed, bool startFromLength, Int128 start, bool endFromLength, Int128 end, long length, long maximumLength)
        {
            var interval = new LengthInterval(length, maximumLength);
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

    // How a use resolves a literal-only argument: as an element position, a boundary, or a range.
    private enum LiteralRole : byte
    {
        Element,
        Boundary,
        Range,
    }

    // The lengths L >= 0 (or the one fixed length) that satisfy every added inequality `a * L + b >= 0`, a in {-1, 0, 1}.
    private struct LengthInterval
    {
        private Int128 low;
        private Int128 high;

        internal LengthInterval(long length, long maximumLength)
        {
            (this.low, this.high) = length >= 0 ? (length, length) : (0, maximumLength);
        }

        internal readonly bool IsEmpty => this.low > this.high;

        // A boundary `c * L + k` in [0, L].
        internal void RequireBoundary(bool fromLength, Int128 offset)
        {
            this.Require(fromLength ? 1 : 0, offset);
            this.Require(fromLength ? 0 : 1, -offset);
        }

        // An element position `c * L + k` in [0, L - 1].
        internal void RequireElement(bool fromLength, Int128 offset)
        {
            this.Require(fromLength ? 1 : 0, offset);
            this.Require(fromLength ? 0 : 1, -offset - 1);
        }

        internal void Require(long a, Int128 b)
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
                this.low = Int128.Max(this.low, -b);
            }
            else
            {
                this.high = Int128.Min(this.high, b);
            }
        }
    }
}
