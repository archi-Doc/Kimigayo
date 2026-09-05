// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading;
using Kimi.Diagnostics;

/// <summary>
/// Owns immutable Kimi source text and maps UTF-16 offsets to lines and characters.
/// </summary>
/// <remarks>
/// Offsets and character positions use .NET UTF-16 code units. Line terminators may be
/// <c>\n</c>, <c>\r</c>, or <c>\r\n</c>; returned line spans exclude those terminators.
/// </remarks>
[TinyhandObject]
public sealed partial class SourceDocument
{
    [IgnoreMember]
    private int[]? lineStarts;

    /// <summary>
    /// Gets the source path.
    /// </summary>
    [Key(0)]
    public string Path { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the complete source text.
    /// </summary>
    [Key(1)]
    public string SourceText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the number of physical lines in the source text.
    /// </summary>
    [IgnoreMember]
    public int LineCount => this.GetLineStarts().Length;

    /// <summary>
    /// Gets the absolute start offset of each physical line.
    /// </summary>
    [IgnoreMember]
    public ReadOnlySpan<int> LineStarts => this.GetLineStarts();

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceDocument"/> class.
    /// </summary>
    /// <param name="path">The source URL or path.</param>
    /// <param name="sourceText">The complete source text.</param>
    public SourceDocument(string path, string sourceText)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(sourceText);

        this.Path = path;
        this.SourceText = sourceText;
    }

    /// <summary>
    /// Gets a span over the complete source text.
    /// </summary>
    /// <returns>A span over the complete source text.</returns>
    public ReadOnlySpan<char> AsSpan()
        => this.SourceText.AsSpan();

    /// <summary>
    /// Gets a line without its terminating line break.
    /// </summary>
    /// <param name="line">The zero-based line number.</param>
    /// <returns>The requested source line.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="line"/> is outside the document.</exception>
    public ReadOnlySpan<char> GetLineSpan(int line)
    {
        var starts = this.GetLineStarts();
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(line, starts.Length);

        var text = this.SourceText.AsSpan();
        return text[starts[line]..GetLineEnd(text, starts, line)];
    }

    /// <summary>
    /// Converts an absolute offset to a line and character position.
    /// </summary>
    /// <param name="offset">The zero-based absolute offset.</param>
    /// <returns>The corresponding source position.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is outside the document.</exception>
    public SourcePosition GetPosition(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, this.SourceText.Length);

        var starts = this.GetLineStarts();
        var line = FindLine(starts, offset);
        return new(line, offset - starts[line]);
    }

    /// <summary>
    /// Converts a text span to line and character positions.
    /// </summary>
    /// <param name="span">The absolute source span.</param>
    /// <returns>The corresponding source range.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="span"/> is outside the document.</exception>
    public SourceRange GetSourceRange(SourceSpan span)
    {
        var start = span.Start;
        ArgumentOutOfRangeException.ThrowIfNegative(start, nameof(span));
        ArgumentOutOfRangeException.ThrowIfNegative(span.Length, nameof(span));
        var end = span.End; // Throws when the end offset overflows.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(end, this.SourceText.Length, nameof(span));

        // The end never precedes the start, so the second search only scans the remaining lines.
        var starts = this.GetLineStarts();
        var startLine = FindLine(starts, start);
        var endLine = startLine + FindLine(starts.AsSpan(startLine), end);
        return new(
            new(startLine, start - starts[startLine]),
            new(endLine, end - starts[endLine]));
    }

    /// <summary>
    /// Converts a line and character position to an absolute offset.
    /// </summary>
    /// <param name="position">The source position.</param>
    /// <returns>The corresponding absolute offset.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is outside the document text.</exception>
    public int GetOffset(SourcePosition position)
    {
        var starts = this.GetLineStarts();
        var line = position.Line;
        var character = position.Character;
        ArgumentOutOfRangeException.ThrowIfNegative(line, nameof(position));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(line, starts.Length, nameof(position));
        ArgumentOutOfRangeException.ThrowIfNegative(character, nameof(position));

        var text = this.SourceText.AsSpan();
        var lineStart = starts[line];
        ArgumentOutOfRangeException.ThrowIfGreaterThan(character, GetLineEnd(text, starts, line) - lineStart, nameof(position));
        return lineStart + character;
    }

    /// <summary>
    /// Converts line and character positions to an absolute text span.
    /// </summary>
    /// <param name="range">The source range.</param>
    /// <returns>The corresponding absolute text span.</returns>
    public SourceSpan GetTextSpan(SourceRange range)
        => SourceSpan.FromBounds(this.GetOffset(range.Start), this.GetOffset(range.End));

    /// <summary>
    /// Gets the exclusive end offset of a line, excluding its terminator.
    /// </summary>
    /// <param name="text">The complete source text.</param>
    /// <param name="lineStarts">The line start table.</param>
    /// <param name="line">The zero-based line number.</param>
    /// <returns>The exclusive end offset.</returns>
    private static int GetLineEnd(ReadOnlySpan<char> text, ReadOnlySpan<int> lineStarts, int line)
    {
        var start = lineStarts[line];
        var next = line + 1;
        var end = next < lineStarts.Length ? lineStarts[next] : text.Length;

        // A line ends with exactly one terminator: "\n", "\r", or "\r\n".
        if (end > start && text[end - 1] == Constants.LfChar)
        {
            end--;
        }

        if (end > start && text[end - 1] == Constants.CrChar)
        {
            end--;
        }

        return end;
    }

    /// <summary>
    /// Finds the line that contains an offset.
    /// </summary>
    /// <param name="lineStarts">The line start table; never empty and always starting at zero.</param>
    /// <param name="offset">The non-negative absolute offset.</param>
    /// <returns>The highest index whose line start does not exceed <paramref name="offset"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindLine(ReadOnlySpan<int> lineStarts, int offset)
    {
        var low = 0;
        var high = lineStarts.Length - 1;
        while (low < high)
        {// Bias the midpoint upward so that the range always narrows.
            var middle = (int)(((uint)low + (uint)high + 1) >> 1);
            if (lineStarts[middle] <= offset)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    private static int[] CreateLineStarts(ReadOnlySpan<char> sourceText)
    {
        // Start small to avoid renting a buffer proportional to the total character count
        // for minified/generated documents that contain very few line breaks.
        var pool = ArrayPool<int>.Shared;
        var buffer = pool.Rent(Math.Clamp((sourceText.Length >> 6) + 1, 4, 256));
        try
        {
            buffer[0] = 0;
            var count = 1;
            var index = 0;
            while (true)
            {
                // The two-value overload is vectorized and measures the same as a cached
                // SearchValues here, so the scan runs at memory bandwidth without one.
                var next = sourceText[index..].IndexOfAny(Constants.CrChar, Constants.LfChar);
                if (next < 0)
                {
                    break;
                }

                index += next;
                if (sourceText[index] == Constants.CrChar &&
                    (uint)(index + 1) < (uint)sourceText.Length &&
                    sourceText[index + 1] == Constants.LfChar)
                {
                    index++;
                }

                index++;
                if (count == buffer.Length)
                {
                    var larger = pool.Rent(count * 2);
                    buffer.AsSpan(0, count).CopyTo(larger);
                    pool.Return(buffer);
                    buffer = larger;
                }

                buffer[count++] = index;
            }

            return buffer.AsSpan(0, count).ToArray();
        }
        finally
        {
            pool.Return(buffer);
        }
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
        => this.lineStarts = null;

    /// <summary>
    /// Gets the line start table, building it on first use.
    /// </summary>
    /// <returns>The absolute start offset of each physical line.</returns>
    /// <remarks>
    /// Only diagnostics and editor services map offsets to positions, so a clean compilation
    /// never pays for the table. Racing callers compute identical tables, so the loser of the
    /// race simply discards its copy.
    /// </remarks>
    private int[] GetLineStarts()
    {
        var starts = Volatile.Read(ref this.lineStarts);
        if (starts is null)
        {
            starts = CreateLineStarts(this.SourceText.AsSpan());
            Volatile.Write(ref this.lineStarts, starts);
        }

        return starts;
    }
}
