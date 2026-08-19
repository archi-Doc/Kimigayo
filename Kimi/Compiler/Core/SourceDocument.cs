// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>
/// Owns an immutable source text and provides line-based access to it.
/// </summary>
public sealed class SourceDocument
{
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
    /// Initializes a new instance of the <see cref="SourceDocument"/> class.
    /// </summary>
    /// <param name="url">The source URL or path.</param>
    /// <param name="sourceText">The complete source text.</param>
    public SourceDocument(string url, string sourceText)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(sourceText);

        this.Path = url;
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
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(line, this.lineStarts.Length);

        var start = this.lineStarts[line];
        var end = line + 1 < this.lineStarts.Length ? this.lineStarts[line + 1] : this.SourceText.Length;

        while (end > start && this.SourceText[end - 1] is '\r' or '\n')
        {
            end--;
        }

        return this.SourceText.AsSpan(start, end - start);
    }

    private static int[] CreateLineStarts(ReadOnlySpan<char> sourceText)
    {
        var starts = new List<int> { 0 };

        for (var i = 0; i < sourceText.Length; i++)
        {
            if (sourceText[i] == '\r')
            {
                if (i + 1 < sourceText.Length && sourceText[i + 1] == '\n')
                {
                    i++;
                }

                starts.Add(i + 1);
            }
            else if (sourceText[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return starts.ToArray();
    }
}
