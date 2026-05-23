// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;

namespace Kimigayo.Lsp;

internal sealed class Line : IDisposable
{
    #region FieldAndProperty

    private char[] buffer;
    private int length;
    private bool disposed;

    #endregion

    public Line()
    {
        this.buffer = [];
    }

    public int Length => this.length;

    public ReadOnlySpan<char> AsSpan()
    {
        return this.buffer.AsSpan(0, this.length);
    }

    public override string ToString()
    {
        return this.length == 0 ? string.Empty : new string(this.buffer, 0, this.length);
    }

    public void Set(ReadOnlySpan<char> first)
    {
        var newLength = first.Length;

        char[]? oldBuffer = default;
        if (newLength > this.buffer.Length)
        {
            oldBuffer = this.buffer;
            this.buffer = ArrayPool<char>.Shared.Rent(newLength);
        }

        first.CopyTo(this.buffer);
        this.length = first.Length;

        if (oldBuffer?.Length > 0)
        {
            ArrayPool<char>.Shared.Return(oldBuffer);
        }
    }

    public void Set(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
    {
        var newLength = first.Length + second.Length;
        char[]? oldBuffer = default;
        if (newLength > this.buffer.Length)
        {
            oldBuffer = this.buffer;
            this.buffer = ArrayPool<char>.Shared.Rent(newLength);
        }

        var span = this.buffer.AsSpan(0, newLength);

        first.CopyTo(span);
        second.CopyTo(span[first.Length..]);

        this.length = newLength;

        if (oldBuffer?.Length > 0)
        {
            ArrayPool<char>.Shared.Return(oldBuffer);
        }
    }

    public void Replace(int start, int end, ReadOnlySpan<char> replacement)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        start = Math.Clamp(start, 0, this.length);
        end = Math.Clamp(end, start, this.length);

        var prefixLength = start;
        var suffixStart = end;
        var suffixLength = this.length - end;
        var newLength = prefixLength + replacement.Length + suffixLength;

        var oldBuffer = this.buffer;

        if (newLength > oldBuffer.Length)
        {
            var newBuffer = ArrayPool<char>.Shared.Rent(newLength);
            var destination = newBuffer.AsSpan(0, newLength);

            oldBuffer.AsSpan(0, prefixLength).CopyTo(destination);
            replacement.CopyTo(destination[prefixLength..]);
            oldBuffer.AsSpan(suffixStart, suffixLength)
                .CopyTo(destination[(prefixLength + replacement.Length)..]);

            this.buffer = newBuffer;
            this.length = newLength;

            if (oldBuffer.Length != 0)
            {
                ArrayPool<char>.Shared.Return(oldBuffer);
            }

            return;
        }

        var bufferSpan = oldBuffer.AsSpan();

        // Move suffix first. Span.CopyTo handles overlapping ranges.
        bufferSpan.Slice(suffixStart, suffixLength)
            .CopyTo(bufferSpan.Slice(prefixLength + replacement.Length));

        replacement.CopyTo(bufferSpan.Slice(prefixLength));

        this.length = newLength;
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        if (this.buffer.Length != 0)
        {
            ArrayPool<char>.Shared.Return(this.buffer);
            this.buffer = [];
        }

        this.length = 0;
    }
}
