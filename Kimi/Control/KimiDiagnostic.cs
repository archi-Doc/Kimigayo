// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

[ValueLinkObject(Isolation = IsolationLevel.Serializable)]
public sealed partial record class Diagnostic
{
    public Range Range { get; init; }

    public Kimigayo.Lsp.DiagnosticSeverity Severity { get; init; }

    public string? Code { get; init; }

    // public string? CodeDescription { get; init; }

    public string? Source { get; init; }

    public string Message { get; init; } = string.Empty;

    [Link(Primary = true, Unique = true, Type = ChainType.Ordered)]
    public Position StartPosition => this.Range.Start;
}

public readonly record struct Range : IComparable<Range>
{
    public Position Start { get; }

    public Position End { get; }

    public Range(Position start, Position end)
    {
        this.Start = start;
        this.End = end;
    }

    public int CompareTo(Range other)
    {
        var cmp = this.Start.CompareTo(other.Start);
        if (cmp != 0)
        {
            return cmp;
        }

        cmp = this.End.CompareTo(other.End);
        return cmp;
    }
}

public readonly record struct Position : IComparable<Position>
{
    public int Line { get; }

    public int Character { get; }

    public Position(int line, int character)
    {
        this.Line = line;
        this.Character = character;
    }

    public int CompareTo(Position other)
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
}
