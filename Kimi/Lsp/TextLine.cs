// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Lsp;

using System.Buffers;
using System.Runtime.CompilerServices;

/// <summary>
/// Represents one text line backed by a pooled char buffer.
/// This type is not thread-safe.
/// </summary>
internal sealed class TextLine : IDisposable
{
    private char[] buffer = [];
    private int length;
    private bool disposed;

    public int Length => this.length;

    public ReadOnlySpan<char> AsSpan()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        return this.buffer.AsSpan(0, this.length);
    }

    public override string ToString()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        return this.length == 0 ? string.Empty : new string(this.buffer, 0, this.length);
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.ReturnAndClear();
    }

    public void Set(ReadOnlySpan<char> value)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        if (value.Length == 0)
        {
            this.ReturnAndClear();
            return;
        }

        this.EnsureCapacity(value.Length);
        value.CopyTo(this.buffer);
        this.length = value.Length;
    }

    public void Set(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        var newLength = first.Length + second.Length;
        if (newLength == 0)
        {
            this.ReturnAndClear();
            return;
        }

        // Always build into a fresh buffer.
        // This keeps the method safe even if the source spans refer to an existing Line buffer.
        var oldBuffer = this.buffer;
        var newBuffer = ArrayPool<char>.Shared.Rent(newLength);
        var destination = newBuffer.AsSpan(0, newLength);

        first.CopyTo(destination);
        second.CopyTo(destination[first.Length..]);

        this.buffer = newBuffer;
        this.length = newLength;

        Return(oldBuffer);
    }

    public void Set(ReadOnlySpan<char> first, ReadOnlySpan<char> second, ReadOnlySpan<char> third)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        var newLength = first.Length + second.Length + third.Length;
        if (newLength == 0)
        {
            this.ReturnAndClear();
            return;
        }

        // Always build into a fresh buffer.
        // This avoids self-buffer overwrite bugs with prefix/suffix spans.
        var oldBuffer = this.buffer;
        var newBuffer = ArrayPool<char>.Shared.Rent(newLength);
        var destination = newBuffer.AsSpan(0, newLength);

        first.CopyTo(destination);
        second.CopyTo(destination[first.Length..]);
        third.CopyTo(destination[(first.Length + second.Length)..]);

        this.buffer = newBuffer;
        this.length = newLength;

        Return(oldBuffer);
    }

    internal void Replace(int start, int end, ReadOnlySpan<char> replacement)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        start = Math.Clamp(start, 0, this.length);
        end = Math.Clamp(end, start, this.length);

        var prefixLength = start;
        var suffixStart = end;
        var suffixLength = this.length - end;
        var newLength = prefixLength + replacement.Length + suffixLength;
        if (newLength == 0)
        {
            this.ReturnAndClear();
            return;
        }

        var oldBuffer = this.buffer;

        if (newLength > oldBuffer.Length)
        {
            var newBuffer = ArrayPool<char>.Shared.Rent(newLength);
            var destination = newBuffer.AsSpan(0, newLength);

            oldBuffer.AsSpan(0, prefixLength).CopyTo(destination);
            replacement.CopyTo(destination[prefixLength..]);
            oldBuffer.AsSpan(suffixStart, suffixLength).CopyTo(destination[(prefixLength + replacement.Length)..]);

            this.buffer = newBuffer;
            this.length = newLength;

            Return(oldBuffer);
            return;
        }

        // Move suffix first. CopyTo handles overlapping ranges.
        var span = oldBuffer.AsSpan();
        span.Slice(suffixStart, suffixLength).CopyTo(span.Slice(prefixLength + replacement.Length));
        replacement.CopyTo(span[prefixLength..]);

        this.length = newLength;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Return(char[] buffer)
    {
        if (buffer.Length != 0)
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCapacity(int requiredLength)
    {
        if (requiredLength <= this.buffer.Length)
        {
            return;
        }

        Return(this.buffer);
        this.buffer = ArrayPool<char>.Shared.Rent(requiredLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReturnAndClear()
    {
        if (this.buffer.Length != 0)
        {
            ArrayPool<char>.Shared.Return(this.buffer);
        }

        this.buffer = [];
        this.length = 0;
    }
}
