// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using System.Text;

namespace Kimi;

/// <summary>
/// Provides a high-performance text builder with automatic indentation.
/// </summary>
/// <remarks>
/// This class is not thread-safe.
/// All newline characters written through this class are normalized to '\n'.
/// </remarks>
public class IndentWriter
{
    public const int DefaultSpacesPerIndent = 4;
    private const int SpaceBufferLength = 512;

    private static readonly char[] SpaceBuffer;

    static IndentWriter()
    {
        SpaceBuffer = new char[SpaceBufferLength];
        Array.Fill(SpaceBuffer, ' ');
    }

    #region FieldAndProperty

    private readonly int spacesPerIndent;
    private readonly StringBuilder builder;
    private int indentLevel;
    private bool isLineStart = true;

    #endregion

    public IndentWriter(int spacesPerIndent = DefaultSpacesPerIndent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(spacesPerIndent);

        this.spacesPerIndent = spacesPerIndent;
        this.builder = new StringBuilder();
    }

    public IndentWriter(int capacity, int spacesPerIndent = DefaultSpacesPerIndent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        ArgumentOutOfRangeException.ThrowIfNegative(spacesPerIndent);

        this.spacesPerIndent = spacesPerIndent;
        this.builder = new StringBuilder(capacity);
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
    /// <returns> This <see cref="IndentWriter"/> instance.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter IncrementIndent()
    {
        this.indentLevel++;
        return this;
    }

    /// <summary>
    /// Decreases the indentation level by one.
    /// </summary>
    /// <returns> This <see cref="IndentWriter"/> instance.</returns>
    /// <remarks>
    /// Nothing happens when the indentation level is already zero.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter DecrementIndent()
    {
        if (this.indentLevel > 0)
        {
            this.indentLevel--;
        }

        return this;
    }

    /// <summary>
    /// Resets the indentation level to zero.
    /// </summary>
    /// <returns> This <see cref="IndentWriter"/> instance.</returns>
    /// <param name="indentLevel">The indentation level.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter SetIndent(int indentLevel)
    {
        this.indentLevel = indentLevel;
        return this;
    }

    /// <summary>
    /// Removes all characters while preserving the current indentation level.
    /// </summary>
    /// <returns> This <see cref="IndentWriter"/> instance.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Clear()
    {
        this.builder.Clear();
        this.isLineStart = true;
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Write(char value)
    {
        if (value == BaseHelper.LfChar || value == BaseHelper.CrChar)
        {
            this.builder.Append(BaseHelper.LfChar);
            this.isLineStart = true;
            return this;
        }

        this.WriteIndentIfRequired();
        this.builder.Append(value);
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Write(string? value)
    {
        if (value is not null)
        {
            this.Write(value.AsSpan());
        }

        return this;
    }

    public IndentWriter Write(ReadOnlySpan<char> value)
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

                this.WriteIndentIfRequired();
            }

            ReadOnlySpan<char> remaining = value[position..];
            int newlineIndex = remaining.IndexOfAny(BaseHelper.CrChar, BaseHelper.LfChar);

            if (newlineIndex < 0)
            {
                this.builder.Append(remaining);
                return this;
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

        return this;
    }

    public IndentWriter Write(bool value)
    {
        this.WriteIndentIfRequired();
        this.builder.Append(value);
        return this;
    }

    public IndentWriter Write(int value)
    {
        this.WriteIndentIfRequired();
        this.builder.Append(value);
        return this;
    }

    public IndentWriter Write(uint value)
    {
        this.WriteIndentIfRequired();
        this.builder.Append(value);
        return this;
    }

    public IndentWriter Write(long value)
    {
        this.WriteIndentIfRequired();
        this.builder.Append(value);
        return this;
    }

    public IndentWriter Write(ulong value)
    {
        this.WriteIndentIfRequired();
        this.builder.Append(value);
        return this;
    }

    public IndentWriter Write(float value)
    {
        this.WriteIndentIfRequired();
        this.builder.Append(value);
        return this;
    }

    public IndentWriter Write(double value)
    {
        this.WriteIndentIfRequired();
        this.builder.Append(value);
        return this;
    }

    public IndentWriter Write(decimal value)
    {
        this.WriteIndentIfRequired();
        this.builder.Append(value);
        return this;
    }

    public IndentWriter Write(object? value)
    {
        if (value is not null)
        {
            this.WriteIndentIfRequired();
            this.builder.Append(value);
        }

        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter WriteLine()
    {
        this.builder.Append(BaseHelper.LfChar);
        this.isLineStart = true;
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter AppendEmptyLine()
    {
        this.isLineStart = true;

        if (this.builder.Length == 0)
        {
            this.builder.Append(BaseHelper.LfChar);
            return this;
        }

        if (this.builder.Length >= 2 && this.builder[this.builder.Length - 1] == BaseHelper.LfChar && this.builder[this.builder.Length - 2] == BaseHelper.LfChar)
        {
            return this;
        }

        if (this.builder[this.builder.Length - 1] != BaseHelper.LfChar)
        {
            this.builder.Append(BaseHelper.LfChar);
        }

        this.builder.Append(BaseHelper.LfChar);

        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter WriteLine(string? value)
    {
        this.Write(value);
        return this.WriteLine();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter WriteLine(ReadOnlySpan<char> value)
    {
        this.Write(value);
        return this.WriteLine();
    }

    public IndentWriter WriteLine(char value)
    {
        this.Write(value);
        return this.WriteLine();
    }

    public IndentWriter Append(char value)
        => this.Write(value);

    public IndentWriter Append(ReadOnlySpan<char> value)
    {
        this.WriteIndentIfRequired();
        this.builder.Append(value);
        return this;
    }

    public IndentWriter AppendLine()
        => this.WriteLine();

    public IndentWriter AppendLine(string? value)
        => this.WriteLine(value);

    public IndentWriter AppendLine(ReadOnlySpan<char> value)
        => this.WriteLine(value);

    public override string ToString()
        => this.builder.ToString();

    public string ToString(int startIndex, int length)
        => this.builder.ToString(startIndex, length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteIndentIfRequired()
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
