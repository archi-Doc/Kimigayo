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
public sealed class IndentWriter
{
    public const int MaxIndents = 256;

    private static readonly char[] SpaceBuffer;

    static IndentWriter()
    {
        SpaceBuffer = new char[Constants.IndentationSpaces * MaxIndents];
        Array.Fill(SpaceBuffer, ' ');
    }

    #region FieldAndProperty

    private readonly StringBuilder builder;
    private int indentLevel;
    private bool isLineStart = true;

    #endregion

    public IndentWriter()
    {
        this.builder = new StringBuilder();
    }

    public IndentWriter(int capacity)
    {
        this.builder = new StringBuilder(capacity);
    }

    /// <summary>
    /// Gets or sets the number of characters in the builder.
    /// </summary>
    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.builder.Length;

        set
        {
            this.builder.Length = value;
            this.isLineStart =
                value == 0 ||
                this.builder[value - 1] == BaseHelper.LfChar;
        }
    }

    /// <summary>
    /// Gets or sets the capacity of the internal buffer.
    /// </summary>
    public int Capacity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.builder.Capacity;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => this.builder.Capacity = value;
    }

    /// <summary>
    /// Gets the current indentation level.
    /// </summary>
    public int IndentLevel => this.indentLevel;

    public char this[int index] => this.builder[index];

    /// <summary>
    /// Increases the indentation level by one.
    /// </summary>
    /// <returns> This <see cref="IndentWriter"/> instance.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter IncrementIndent()
    {
        if (this.indentLevel == MaxIndents)
        {
            throw new InvalidOperationException("The indentation level is too large.");
        }

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter ResetIndent()
    {
        this.indentLevel = 0;
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
    public int EnsureCapacity(int capacity)
        => this.builder.EnsureCapacity(capacity);

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
            if (newline == BaseHelper.CrChar &&
                position < value.Length &&
                value[position] == BaseHelper.LfChar)
            {
                position++;
            }
        }

        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Write(bool value)
    {
        this.WriteIndentIfRequired();
        this.builder.Append(value);
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Write(int value)
    {
        this.WriteIndentIfRequired();
        this.builder.Append(value);
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Write(uint value)
    {
        this.WriteIndentIfRequired();
        this.builder.Append(value);
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Write(long value)
    {
        this.WriteIndentIfRequired();
        this.builder.Append(value);
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Write(ulong value)
    {
        this.WriteIndentIfRequired();
        this.builder.Append(value);
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Write(float value)
    {
        this.WriteIndentIfRequired();
        this.builder.Append(value);
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Write(double value)
    {
        this.WriteIndentIfRequired();
        this.builder.Append(value);
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Write(decimal value)
    {
        this.WriteIndentIfRequired();
        this.builder.Append(value);
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter WriteLine(char value)
    {
        this.Write(value);
        return this.WriteLine();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter WriteLine(int value)
    {
        this.Write(value);
        return this.WriteLine();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter WriteLine(long value)
    {
        this.Write(value);
        return this.WriteLine();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter WriteLine(double value)
    {
        this.Write(value);
        return this.WriteLine();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter WriteLine(object? value)
    {
        this.Write(value);
        return this.WriteLine();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Append(char value)
        => this.Write(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Append(string? value)
        => this.Write(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Append(ReadOnlySpan<char> value)
        => this.Write(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Append(bool value)
        => this.Write(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Append(int value)
        => this.Write(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Append(uint value)
        => this.Write(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Append(long value)
        => this.Write(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Append(ulong value)
        => this.Write(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Append(float value)
        => this.Write(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Append(double value)
        => this.Write(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Append(decimal value)
        => this.Write(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter Append(object? value)
        => this.Write(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter AppendLine()
        => this.WriteLine();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter AppendLine(string? value)
        => this.WriteLine(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndentWriter AppendLine(ReadOnlySpan<char> value)
        => this.WriteLine(value);

    public void CopyTo(int sourceIndex, Span<char> destination, int count)
    {
        this.builder.CopyTo(sourceIndex, destination, count);
    }

    public StringBuilder.ChunkEnumerator GetChunks()
        => this.builder.GetChunks();

    public override string ToString()
        => this.builder.ToString();

    public string ToString(int startIndex, int length)
        => this.builder.ToString(startIndex, length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteIndentIfRequired()
    {
        if (!this.isLineStart || this.indentLevel == 0)
        {
            return;
        }

        this.isLineStart = false;
        this.builder.Append(SpaceBuffer.AsSpan(0, Constants.IndentationSpaces * this.indentLevel));
    }
}
