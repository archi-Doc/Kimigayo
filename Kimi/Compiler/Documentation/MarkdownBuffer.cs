// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Documentation;

#pragma warning disable SA1202 // Keep growth next to its scratch-buffer implementation.

// Parser-owned scratch only. Never expose a rented array through the immutable tree.
internal struct MarkdownBuffer<T> : IDisposable
{
    private T[]? items;

    internal int Count;

    internal ref T this[int index] => ref this.items![index];

    internal ReadOnlySpan<T> Span => this.items.AsSpan(0, this.Count);

    internal int Add(T item)
    {
        if (this.items is null || this.Count == this.items.Length)
        {
            this.Grow();
        }

        this.items![this.Count] = item;
        return this.Count++;
    }

    internal void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            this.items.AsSpan(0, this.Count).Clear();
        }

        this.Count = 0;
    }

    public void Dispose()
    {
        if (this.items is { } items)
        {
            this.items = null;
            this.Count = 0;
            ArrayPool<T>.Shared.Return(items, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow()
    {
        var next = ArrayPool<T>.Shared.Rent(this.items is null ? 16 : checked(this.items.Length * 2));
        if (this.items is { } previous)
        {
            previous.AsSpan(0, this.Count).CopyTo(next);
            ArrayPool<T>.Shared.Return(previous, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }

        this.items = next;
    }
}
