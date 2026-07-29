// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc.Collections;

namespace Kimi;

/// <summary>
/// Provides a high-performance text builder with automatic indentation.<br/>
/// Append(): Appends a value to the character buffer with indentation.<br/>
/// If the value starts on a new line, the current indentation is inserted first.
/// </summary>
/// <remarks>
/// This type is not thread-safe.<br/>
/// CR, LF, and CRLF sequences within each appended character span are normalized to LF.
/// </remarks>
public ref struct IndentedStringBuilder
{
    public const int DefaultSpacesPerIndent = 4;
    private const int SpaceBufferLength = 512;

    private static readonly char[] SpaceBuffer;

    static IndentedStringBuilder()
    {
        SpaceBuffer = new char[SpaceBufferLength];
        Array.Fill(SpaceBuffer, ' ');
    }

    #region FieldAndProperty

    private readonly int spacesPerIndent;
    private PooledStringBuilder builder;
    private int indentLevel;
    private bool isLineStart = true;

    #endregion

    public IndentedStringBuilder(int spacesPerIndent = DefaultSpacesPerIndent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(spacesPerIndent);

        this.spacesPerIndent = spacesPerIndent;
    }

    /// <summary>
    /// Gets the number of characters in the builder.
    /// </summary>
    public int Length => this.builder.Length;

    /// <summary>
    /// Gets the current indentation level.
    /// </summary>
    public int IndentLevel => this.indentLevel;

    /// <summary>
    /// Increases the indentation level by one.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncrementIndent()
    {
        this.indentLevel++;
    }

    /// <summary>
    /// Decreases the indentation level by one.
    /// </summary>
    /// <remarks>
    /// Nothing happens when the indentation level is already zero.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DecrementIndent()
    {
        if (this.indentLevel > 0)
        {
            this.indentLevel--;
        }
    }

    /// <summary>
    /// Resets the indentation level to zero.
    /// </summary>
    /// <param name="indentLevel">The indentation level.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetIndent(int indentLevel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(indentLevel);
        this.indentLevel = indentLevel;
    }

    /// <summary>
    /// Removes all characters while preserving the current indentation level.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        this.builder.Clear();
        this.isLineStart = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(bool value)
    {
        this.AppendIndentIfRequired();
        this.builder.Append(value);
    }

    public void AppendWithoutIndent(bool value)
    {
        this.builder.Append(value);
        this.isLineStart = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(char value)
    {
        if (value == BaseHelper.LfChar || value == BaseHelper.CrChar)
        {
            this.builder.Append(BaseHelper.LfChar);
            this.isLineStart = true;
        }

        this.AppendIndentIfRequired();
        this.builder.Append(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendWithoutIndent(char value)
    {
        if (value == BaseHelper.LfChar || value == BaseHelper.CrChar)
        {
            this.builder.Append(BaseHelper.LfChar);
            this.isLineStart = true;
            return;
        }

        this.builder.Append(value);
        this.isLineStart = false;
    }

    /// <summary>
    /// Appends the formatted representation of a value.
    /// </summary>
    /// <typeparam name="T">The type of value to append.</typeparam>
    /// <param name="value">The value to format and append.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append<T>(T value)
        where T : ISpanFormattable
    {
        this.AppendIndentIfRequired();
        this.builder.Append(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendWithoutIndent<T>(T value)
        where T : ISpanFormattable
    {
        this.builder.Append(value);
        this.isLineStart = false;
    }

    public void Append(ReadOnlyMemory<char> value)
        => this.Append(value.Span);

    public void Append(ReadOnlySpan<char> value)
    {
        int position = 0;

        while (position < value.Length)
        {
            if (this.isLineStart)
            {
                char first = value[position];

                // Do not add indentation to an empty line.
                if (first == BaseHelper.LfChar)
                {
                    this.builder.Append(BaseHelper.LfChar);
                    position++;
                    continue;
                }

                if (first == BaseHelper.CrChar)
                {
                    this.builder.Append(BaseHelper.LfChar);
                    position++;

                    // Normalize CRLF to a single LF.
                    if (position < value.Length && value[position] == BaseHelper.LfChar)
                    {
                        position++;
                    }

                    continue;
                }

                this.AppendIndentIfRequired();
            }

            ReadOnlySpan<char> remaining = value[position..];
            int newlineIndex = remaining.IndexOfAny(BaseHelper.CrChar, BaseHelper.LfChar);

            if (newlineIndex < 0)
            {
                this.builder.Append(remaining);
                return;
            }

            if (newlineIndex > 0)
            {
                this.builder.Append(remaining[..newlineIndex]);
            }

            char newline = remaining[newlineIndex];

            this.builder.Append(BaseHelper.LfChar);
            this.isLineStart = true;

            position += newlineIndex + 1;

            // Normalize CRLF to a single LF.
            if (newline == BaseHelper.CrChar && position < value.Length && value[position] == BaseHelper.LfChar)
            {
                position++;
            }
        }
    }

    /// <summary>
    /// Ensures that the builder ends with a blank line represented by two
    /// consecutive line feed characters.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnsureTrailingBlankLine()
    {
        this.builder.GetLastTwoChars(out var previous, out var last);

        if (last != BaseHelper.LfChar)
        {
            this.AppendWithoutIndent(BaseHelper.LfChar);
            this.AppendWithoutIndent(BaseHelper.LfChar);
        }
        else if (previous != BaseHelper.LfChar)
        {
            this.AppendWithoutIndent(BaseHelper.LfChar);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendLine()
    {
        this.builder.AppendLine();
        this.isLineStart = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendLine<T>(T value)
        where T : ISpanFormattable
    {
        this.Append(value);
        this.AppendLine();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendLine(ReadOnlyMemory<char> value)
        => this.AppendLine(value.Span);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendLine(ReadOnlySpan<char> value)
    {
        this.Append(value);
        this.AppendLine();
    }

    public readonly override string ToString()
        => this.builder.ToString();

    public void Dispose()
        => this.builder.Dispose();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendIndentIfRequired()
    {
        if (!this.isLineStart)
        {
            return;
        }

        this.isLineStart = false;

        if (this.indentLevel == 0)
        {
            return;
        }

        var remaining = this.spacesPerIndent * this.indentLevel;
        while (remaining > 0)
        {
            var length = Math.Min(SpaceBufferLength, remaining);
            this.builder.Append(SpaceBuffer.AsSpan(0, length));
            remaining -= length;
        }
    }
}
