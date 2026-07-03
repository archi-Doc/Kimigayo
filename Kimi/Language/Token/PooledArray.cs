// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Kimi.Language.Token;

public ref struct PooledArray<T>
{
    public const int DefaultInitialCapacity = 256;

    private static readonly ArrayPool<T> Pool = ArrayPool<T>.Shared;

    private int initialCapacity;
    private bool clearArrayOnReturn;
    private T[]? rentArray;
    private int count;

    public readonly int Count => this.count;

    public readonly int Capacity => this.rentArray?.Length ?? 0;

    public PooledArray(int initialCapacity = DefaultInitialCapacity, bool? clearArrayOnReturn = null)
    {
        if (initialCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        }

        this.initialCapacity = initialCapacity;
        this.clearArrayOnReturn = GetClearArrayOnReturn(clearArrayOnReturn);
        this.rentArray = null;
        this.count = 0;
    }

    public void Add(T item)
    {
        var array = this.rentArray;

        if ((uint)this.count >= (uint)(array?.Length ?? 0))
        {
            this.EnsureCapacity(this.count + 1);
            array = this.rentArray;
        }

        array![this.count++] = item;
    }

    public void AddRange(ReadOnlySpan<T> items)
    {
        if (items.IsEmpty)
        {
            return;
        }

        this.EnsureCapacity(this.count + items.Length);
        items.CopyTo(this.rentArray.AsSpan(this.count));
        this.count += items.Length;
    }

    public Span<T> GetWritableSpan(int sizeHint)
    {
        if (sizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHint));
        }

        if (sizeHint == 0 && this.rentArray is null)
        {
            return [];
        }

        this.EnsureCapacity(this.count + sizeHint);
        return this.rentArray.AsSpan(this.count);
    }

    public void Advance(int count)
    {
        var array = this.rentArray;
        if ((uint)count > (uint)((array?.Length ?? 0) - this.count))
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        this.count += count;
    }

    public readonly Span<T> AsSpan()
        => this.rentArray is null ? [] : this.rentArray.AsSpan(0, this.count);

    public readonly ReadOnlySpan<T> AsReadOnlySpan()
        => this.AsSpan();

    public void Clear()
    {
        var array = this.rentArray;

        if (array is not null)
        {
            Pool.Return(array, this.clearArrayOnReturn);
            this.rentArray = null;
        }

        this.count = 0;
    }

    public void Dispose()
    {
        this.Clear();
    }

    private static bool GetClearArrayOnReturn(bool? clearArray) => clearArray switch
    {
        true => true,
        false => false,
        _ => RuntimeHelpers.IsReferenceOrContainsReferences<T>(),
    };

    [MemberNotNull(nameof(rentArray))]
    private void EnsureCapacity(int minimumCapacity)
    {
        Debug.Assert(minimumCapacity > 0);

        if (this.rentArray is null)
        {
            var capacity = this.initialCapacity;
            if (capacity <= 0)
            {
                capacity = DefaultInitialCapacity;
                this.initialCapacity = capacity;
                this.clearArrayOnReturn = GetClearArrayOnReturn(null);
            }

            if (capacity < minimumCapacity)
            {
                capacity = minimumCapacity;
            }

            this.rentArray = Pool.Rent(capacity);
            return;
        }

        if (this.rentArray.Length >= minimumCapacity)
        {
            return;
        }

        var newCapacity = this.rentArray.Length << 1;
        if (newCapacity < minimumCapacity)
        {
            newCapacity = minimumCapacity;
        }

        var newArray = Pool.Rent(newCapacity);
        this.rentArray.AsSpan(0, this.count).CopyTo(newArray);

        Pool.Return(this.rentArray, this.clearArrayOnReturn);

        this.rentArray = newArray;
    }
}
