// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Crypto;

namespace Kimi.Diagnostics;

public readonly record struct SourceRange : IComparable<SourceRange>
{// 8 + 8 = 16
    public SourcePosition Start { get; }

    public SourcePosition End { get; }

    public static SourceRange FromString(string str)
    {
        var hash = (int)FarmHash.Hash64(str);
        var position = new SourcePosition(hash, 0);
        return new(position, position);
    }

    public SourceRange(SourcePosition start, SourcePosition end)
    {
        this.Start = start;
        this.End = end;
    }

    public int CompareTo(SourceRange other)
    {
        var cmp = this.Start.CompareTo(other.Start);
        if (cmp != 0)
        {
            return cmp;
        }

        cmp = this.End.CompareTo(other.End);
        return cmp;
    }

    public override string ToString()
    {
        return $"({this.Start.Line + 1},{this.Start.Character + 1},{this.End.Line + 1},{this.End.Character + 1})";
    }
}
