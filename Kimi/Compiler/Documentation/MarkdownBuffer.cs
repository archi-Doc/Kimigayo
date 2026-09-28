// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Documentation;

#pragma warning disable SA1202 // Keep growth next to its scratch-buffer implementation.

// Parser-owned scratch only. Starts in caller-provided (typically stack) storage
// and rents from the pool only when that overflows. Never expose the storage
// through the immutable tree.
internal ref struct MarkdownBuffer<T>
{
    private Span<T> items;

    private T[]? rented;

    internal int Count;

    internal MarkdownBuffer(Span<T> initial)
    {
        this.items = initial;
    }

    internal ref T this[int index] => ref this.items[index];

    internal ReadOnlySpan<T> Span => this.items[..this.Count];

    internal int Add(T item)
    {
        if (this.Count == this.items.Length)
        {
            this.Grow(this.Count + 1);
        }

        this.items[this.Count] = item;
        return this.Count++;
    }

    // Rent the expected final size once instead of doubling through the pool.
    internal void EnsureCapacity(int capacity)
    {
        if (this.items.Length < capacity)
        {
            this.Grow(capacity);
        }
    }

    internal void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            this.items[..this.Count].Clear();
        }

        this.Count = 0;
    }

    public void Dispose()
    {
        this.Count = 0;
        this.items = default;
        if (this.rented is { } rented)
        {
            this.rented = null;
            ArrayPool<T>.Shared.Return(rented, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int minimum)
    {
        var next = ArrayPool<T>.Shared.Rent(Math.Max(minimum, Math.Max(16, checked(this.items.Length * 2))));
        this.items[..this.Count].CopyTo(next);
        if (this.rented is { } previous)
        {
            ArrayPool<T>.Shared.Return(previous, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }

        this.rented = next;
        this.items = next;
    }
}
