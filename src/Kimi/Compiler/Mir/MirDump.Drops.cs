// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;
using System.Text;

namespace Kimi.Compiler.Mir;

// Drop elaboration's output on an analyzed body: each Drop's verdicts and the flag points (DECISIONS R4-M6a).
internal static partial class MirDump
{
    // A Drop's verdict groups, contiguous from view 0's, one per view class, each with its views where they are not all: [keep],
    // [elide in v0; open (.0 elide) in v1].
    private static void Verdicts(StringBuilder text, MirBody body, MirDropTable drops, int ordinal, int block)
    {
        var rows = drops.RowsOf(ordinal);
        var group = drops.Group(ordinal, block, 0);
        var separator = " [";
        while (group >= 0 && group < rows.Length && rows[group].Block == block)
        {
            var next = Verdict(text.Append(separator), rows, group);
            Views(text, body, rows[group].Views);
            (group, separator) = (next, "; ");
        }

        text.Append(separator == "; " ? "]" : string.Empty);
    }

    // A verdict row and, under Open, its children in pre-order, each after its step: keep, flag f0, open f1 (.0 elide, .base1 open (.2 keep)).
    private static int Verdict(StringBuilder text, ReadOnlySpan<MirDropRow> rows, int row)
    {
        var top = rows[row];
        text.Append(top.Verdict.ToString().ToLowerInvariant()).Append(top.Flag >= 0 ? $" f{top.Flag}" : string.Empty);
        var next = row + 1;
        while (next < rows.Length && rows[next].Depth > top.Depth)
        {
            text.Append(next == row + 1 ? " (" : ", ").Append(rows[next].Step == MirProjectionKind.Downcast ? "(_" : string.Empty);
            next = Verdict(Step(text, rows[next].Step, rows[next].Arg).Append(' '), rows, next);
        }

        text.Append(next > row + 1 ? ")" : string.Empty);
        return next;
    }

    // The flag points at a location: [set f0], [clear f1-f3 in v1].
    private static StringBuilder Points(StringBuilder text, MirBody body, ReadOnlySpan<MirFlagPoint> points, MirLocation at)
    {
        foreach (var point in points)
        {
            if (point.At == at)
            {
                Views(Flags(text.Append(point.Set ? " [set " : " [clear "), point.First, point.Count), body, point.Views).Append(']');
            }
        }

        return text;
    }

    private static StringBuilder Flags(StringBuilder text, int first, int count)
        => text.Append('f').Append(first).Append(count > 1 ? $"-f{first + count - 1}" : string.Empty);

    // The views of a group or point where they are not all the body's: " in v0, v2".
    private static StringBuilder Views(StringBuilder text, MirBody body, ulong views)
    {
        var bits = views == ulong.MaxValue >> (64 - (int)MirCases.Count(body)) ? 0UL : views;
        for (var separator = " in "; bits != 0; bits &= bits - 1, separator = ", ")
        {
            text.Append(separator).Append('v').Append(BitOperations.TrailingZeroCount(bits));
        }

        return text;
    }
}
