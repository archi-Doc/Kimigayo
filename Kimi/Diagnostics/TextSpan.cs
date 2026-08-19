// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Diagnostics;

/// <summary>
/// Represents an absolute, half-open span in a source document.
/// </summary>
/// <param name="Start">The zero-based absolute start offset.</param>
/// <param name="Length">The span length.</param>
public readonly record struct TextSpan(int Start, int Length) : IComparable<TextSpan>
{
    /// <summary>
    /// Gets the exclusive absolute end offset.
    /// </summary>
    public int End => checked(this.Start + this.Length);

    /// <summary>
    /// Creates a span from absolute start and exclusive end offsets.
    /// </summary>
    /// <param name="start">The absolute start offset.</param>
    /// <param name="end">The exclusive absolute end offset.</param>
    /// <returns>The resulting text span.</returns>
    public static TextSpan FromBounds(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
        return new(start, checked(end - start));
    }

    public int CompareTo(TextSpan other)
    {
        var comparison = this.Start.CompareTo(other.Start);
        return comparison != 0 ? comparison : this.Length.CompareTo(other.Length);
    }

    public override string ToString()
        => $"[{this.Start}..{this.End})";
}
