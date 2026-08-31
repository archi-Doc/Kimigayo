// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

using System.Buffers;
using Kimi.Diagnostics;

/// <summary>
/// Owns immutable Kimi source text and maps UTF-16 offsets to lines and characters.
/// </summary>
/// <remarks>
/// Offsets and character positions use .NET UTF-16 code units. Line terminators may be
/// <c>\n</c>, <c>\r</c>, or <c>\r\n</c>; returned line spans exclude those terminators.
/// </remarks>
public sealed class SourceDocument
{
    private static readonly SearchValues<char> LineBreakChars = SearchValues.Create("\r\n");

    private readonly int[] lineStarts;

    /// <summary>
    /// Gets the source path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the complete source text.
    /// </summary>
    public string SourceText { get; }

    /// <summary>
    /// Gets the number of physical lines in the source text.
    /// </summary>
    public int LineCount => this.lineStarts.Length;

    /// <summary>
    /// Gets the absolute start offset of each physical line.
    /// </summary>
    public ReadOnlySpan<int> LineStarts => this.lineStarts;

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
        this.lineStarts = CreateLineStarts(sourceText.AsSpan());
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
    public ReadOnlySpan<char> GetLineSpan(int line)
    {
        var starts = this.lineStarts;
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(line, starts.Length);

        var text = this.SourceText.AsSpan();
        var start = starts[line];
        var end = line + 1 < starts.Length ? starts[line + 1] : text.Length;

        // A line ends with exactly one terminator: "\n", "\r", or "\r\n".
        if (end > start && text[end - 1] == '\n')
        {
            end--;
        }

        if (end > start && text[end - 1] == '\r')
        {
            end--;
        }

        return text[start..end];
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

        var line = Array.BinarySearch(this.lineStarts, offset);
        if (line < 0)
        {
            line = ~line - 1;
        }

        return new(line, offset - this.lineStarts[line]);
    }

    /// <summary>
    /// Converts a text span to line and character positions.
    /// </summary>
    /// <param name="span">The absolute source span.</param>
    /// <returns>The corresponding source range.</returns>
    public SourceRange GetSourceRange(SourceSpan span)
    {
        this.ValidateSpan(span);
        return new(this.GetPosition(span.Start), this.GetPosition(span.End));
    }

    /// <summary>
    /// Converts a line and character position to an absolute offset.
    /// </summary>
    /// <param name="position">The source position.</param>
    /// <returns>The corresponding absolute offset.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is outside the document text.</exception>
    public int GetOffset(SourcePosition position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position.Line);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position.Line, this.lineStarts.Length);

        var lineStart = this.lineStarts[position.Line];
        var lineLength = this.GetLineSpan(position.Line).Length;
        ArgumentOutOfRangeException.ThrowIfNegative(position.Character);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position.Character, lineLength);
        return lineStart + position.Character;
    }

    /// <summary>
    /// Converts line and character positions to an absolute text span.
    /// </summary>
    /// <param name="range">The source range.</param>
    /// <returns>The corresponding absolute text span.</returns>
    public SourceSpan GetTextSpan(SourceRange range)
        => SourceSpan.FromBounds(this.GetOffset(range.Start), this.GetOffset(range.End));

    private static int[] CreateLineStarts(ReadOnlySpan<char> sourceText)
    {
        // Start small to avoid renting a buffer proportional to the total character count
        // for minified/generated documents that contain very few line breaks.
        var pool = ArrayPool<int>.Shared;
        var initialCapacity = Math.Clamp((sourceText.Length / 64) + 1, 4, 256);
        var buffer = pool.Rent(initialCapacity);
        try
        {
            var count = 0;
            buffer[count++] = 0;

            var i = 0;
            while (true)
            {
                var next = sourceText[i..].IndexOfAny(LineBreakChars);
                if (next < 0)
                {
                    break;
                }

                i += next;
                if (sourceText[i] == '\r' && i + 1 < sourceText.Length && sourceText[i + 1] == '\n')
                {
                    i++;
                }

                i++;
                if (count == buffer.Length)
                {
                    var larger = pool.Rent(buffer.Length * 2);
                    buffer.AsSpan(0, count).CopyTo(larger);
                    pool.Return(buffer);
                    buffer = larger;
                }

                buffer[count++] = i;
            }

            return buffer.AsSpan(0, count).ToArray();
        }
        finally
        {
            pool.Return(buffer);
        }
    }

    private void ValidateSpan(SourceSpan span)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(span.Start);
        ArgumentOutOfRangeException.ThrowIfNegative(span.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(span.End, this.SourceText.Length);
    }
}
