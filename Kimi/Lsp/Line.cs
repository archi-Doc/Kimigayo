// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;

namespace Kimigayo.Lsp;

internal sealed class Line : IDisposable
{
    private char[] buffer;
    private int length;

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

    public void Set(ReadOnlySpan<char> value)
    {
        this.EnsureCapacityWithoutCopy(value.Length);

        value.CopyTo(this.buffer);

        this.length = value.Length;
    }

    public void Set(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
    {
        var newLength = first.Length + second.Length;
        this.EnsureCapacityWithoutCopy(newLength);

        var span = this.buffer.AsSpan(0, newLength);

        first.CopyTo(span);
        second.CopyTo(span[first.Length..]);

        this.length = newLength;
    }

    public void Set(ReadOnlySpan<char> first, ReadOnlySpan<char> second, ReadOnlySpan<char> third)
    {
        var newLength = first.Length + second.Length + third.Length;
        this.EnsureCapacityWithoutCopy(newLength);

        var span = this.buffer.AsSpan(0, newLength);

        first.CopyTo(span);
        second.CopyTo(span[first.Length..]);
        third.CopyTo(span[(first.Length + second.Length)..]);

        this.length = newLength;
    }

    public void Dispose()
    {
        var array = this.buffer;

        this.buffer = [];
        this.length = 0;

        if (array.Length != 0)
        {
            ArrayPool<char>.Shared.Return(array);
        }
    }

    private void EnsureCapacityWithoutCopy(int requiredLength)
    {
        if (requiredLength <= this.buffer.Length)
        {
            return;
        }

        if (this.buffer.Length != 0)
        {
            ArrayPool<char>.Shared.Return(this.buffer);
        }

        this.buffer = ArrayPool<char>.Shared.Rent(requiredLength);
    }

    /*private void EnsureCapacity(int requiredLength)
    {
        if (requiredLength <= this.buffer.Length)
        {
            return;
        }

        var newBuffer = ArrayPool<char>.Shared.Rent(requiredLength);

        if (this.length > 0)
        {
            this.buffer.AsSpan(0, this.length).CopyTo(newBuffer);
        }

        var oldBuffer = this.buffer;
        this.buffer = newBuffer;

        if (oldBuffer.Length != 0)
        {
            ArrayPool<char>.Shared.Return(oldBuffer);
        }
    }*/
}
