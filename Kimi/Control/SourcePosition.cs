// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Diagnostics;

public readonly record struct SourcePosition : IComparable<SourcePosition>
{
    public int Line { get; }

    public int Character { get; }

    public SourcePosition(int line, int character)
    {
        this.Line = line;
        this.Character = character;
    }

    public int CompareTo(SourcePosition other)
    {
        if (this.Line < other.Line)
        {
            return -1;
        }
        else if (this.Line > other.Line)
        {
            return 1;
        }
        else if (this.Character < other.Character)
        {
            return -1;
        }
        else if (this.Character > other.Character)
        {
            return 1;
        }

        return 0;
    }

    public override string ToString()
    {
        return $"({this.Line},{this.Character})";
    }
}
