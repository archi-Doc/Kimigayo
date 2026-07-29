// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc.Collections;
using Kimi.Compiler.Parsing;

namespace Kimi;

/// <summary>
/// Provides a high-performance string builder with automatic indentation.
/// </summary>
/// <remarks>
/// This type is not thread-safe.<br/>
/// CR, LF, and CRLF sequences within each appended character span are normalized to LF.<br/>
/// Call <see cref="Dispose"/> to return pooled resources.
/// </remarks>
public ref struct IndentedStringBuilder
{
    /// <summary>
    /// The default number of spaces per indentation level.
    /// </summary>
    public const int DefaultSpacesPerIndent = 4;

    private const int SpaceBufferLength = 512;

    private static readonly char[] SpaceBuffer;

    private readonly int spacesPerIndent;
    private PooledStringBuilder builder;
    private int indentLevel;
    private bool isLineStart = true;

    static IndentedStringBuilder()
    {
        SpaceBuffer = new char[SpaceBufferLength];
        Array.Fill(SpaceBuffer, BaseHelper.SpaceChar);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IndentedStringBuilder"/> struct.
    /// </summary>
    /// <param name="spacesPerIndent">
    /// The number of spaces inserted for each indentation level.
    /// </param>
    public IndentedStringBuilder(int spacesPerIndent = DefaultSpacesPerIndent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(spacesPerIndent);

        this.spacesPerIndent = spacesPerIndent;
    }

    /// <summary>
    /// Gets the number of characters in the builder.
    /// </summary>
    public readonly int Length => this.builder.Length;

    /// <summary>
    /// Gets the current indentation level.
    /// </summary>
    public readonly int IndentLevel => this.indentLevel;

    /// <summary>
    /// Gets the number of spaces per indentation level.
    /// </summary>
    public readonly int SpacesPerIndent => this.spacesPerIndent;

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
    /// Sets the current indentation level.
    /// </summary>
    /// <param name="indentLevel">The new indentation level.</param>
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

    /// <summary>
    /// Appends a Boolean value with indentation when required.
    /// </summary>
    /// <param name="value">The Boolean value to append.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(bool value)
    {
        this.AppendIndentIfRequired();
        this.builder.Append(value);
    }

    /// <summary>
    /// Appends a Boolean value without inserting indentation.
    /// </summary>
    /// <param name="value">The Boolean value to append.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendWithoutIndent(bool value)
    {
        this.builder.Append(value);
        this.isLineStart = false;
    }

    /// <summary>
    /// Appends a character with indentation when required.
    /// </summary>
    /// <param name="value">The character to append.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(char value)
    {
        if (value == BaseHelper.LfChar || value == BaseHelper.CrChar)
        {
            this.builder.Append(BaseHelper.LfChar);
            this.isLineStart = true;
            return;
        }

        this.AppendIndentIfRequired();
        this.builder.Append(value);
    }

    /// <summary>
    /// Appends a character without inserting indentation.
    /// </summary>
    /// <param name="value">The character to append.</param>
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

    /// <summary>
    /// Appends the formatted representation of a value without inserting indentation.
    /// </summary>
    /// <typeparam name="T">The type of value to append.</typeparam>
    /// <param name="value">The value to format and append.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendWithoutIndent<T>(T value)
        where T : ISpanFormattable
    {
        this.builder.Append(value);
        this.isLineStart = false;
    }

    /// <summary>
    /// Appends character memory with indentation and newline normalization.
    /// </summary>
    /// <param name="value">The character memory to append.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(ReadOnlyMemory<char> value)
        => this.Append(value.Span);

    /// <summary>
    /// Appends a character span with indentation and newline normalization.
    /// </summary>
    /// <param name="value">The character span to append.</param>
    public void Append(ReadOnlySpan<char> value)
    {
        var position = 0;
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
                    if (position < value.Length &&
                        value[position] == BaseHelper.LfChar)
                    {
                        position++;
                    }

                    continue;
                }

                this.AppendIndentIfRequired();
            }

            ReadOnlySpan<char> remaining = value[position..];
            int newlineIndex =
                remaining.IndexOfAny(BaseHelper.CrChar, BaseHelper.LfChar);

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
            if (newline == BaseHelper.CrChar &&
                position < value.Length &&
                value[position] == BaseHelper.LfChar)
            {
                position++;
            }
        }
    }

    /// <summary>
    /// Ensures that the builder ends with a blank line represented by two consecutive line feed characters.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnsureTrailingBlankLine()
    {
        this.builder.EnsureTrailingBlankLine();
        this.isLineStart = true;
    }

    /// <summary>
    /// Ensures that the builder ends with a space unless it already ends with a space or line feed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnsureTrailingSpace()
    {
        this.builder.EnsureTrailingSpace();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendTrailingSpaceOrLineFeed(KotoWriteOptions options)
    {
        if (options.HasFlag(KotoWriteOptions.AppendLineFeed))
        {
            this.AppendLine();
        }
        else if (options.HasFlag(KotoWriteOptions.AppendSpace))
        {
            this.EnsureTrailingSpace();
        }
    }

    /// <summary>
    /// Appends a line feed character.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendLine()
    {
        this.builder.Append(BaseHelper.LfChar);
        this.isLineStart = true;
    }

    /// <summary>
    /// Appends the formatted representation of a value followed by a line feed.
    /// </summary>
    /// <typeparam name="T">The type of value to append.</typeparam>
    /// <param name="value">The value to format and append.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendLine<T>(T value)
        where T : ISpanFormattable
    {
        this.Append(value);
        this.AppendLine();
    }

    /// <summary>
    /// Appends character memory followed by a line feed.
    /// </summary>
    /// <param name="value">The character memory to append.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendLine(ReadOnlyMemory<char> value)
        => this.AppendLine(value.Span);

    /// <summary>
    /// Appends a character span followed by a line feed.
    /// </summary>
    /// <param name="value">The character span to append.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendLine(ReadOnlySpan<char> value)
    {
        this.Append(value);
        this.AppendLine();
    }

    /// <summary>
    /// Returns the accumulated characters as a string.
    /// </summary>
    /// <returns>A string containing all appended characters.</returns>
    public override string ToString()
        => this.builder.ToString();

    /// <summary>
    /// Returns pooled resources used by the builder.
    /// </summary>
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

        if (this.indentLevel == 0 || this.spacesPerIndent == 0)
        {
            return;
        }

        int remaining = this.spacesPerIndent * this.indentLevel;
        while (remaining > 0)
        {
            int length = Math.Min(SpaceBufferLength, remaining);
            this.builder.Append(SpaceBuffer.AsSpan(0, length));
            remaining -= length;
        }
    }
}
