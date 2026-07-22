// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Kimi.Language;

#pragma warning disable SA1401 // Fields should be private

public ref struct TokenSequenceBuilderRef
{
    private PooledSequenceBuilderRef<Token> builder;

    public TokenSequenceBuilderRef(int initialCapacity = 256, bool? clearArrayOnReturn = null)
    {
        this.builder = new(initialCapacity, clearArrayOnReturn);
    }

    public long Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.builder.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(Token token)
        => this.builder.Add(token);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySequence<Token> ToReadOnlySequence()
        => this.builder.ToReadOnlySequence();

    public void Dispose()
        => this.builder.Dispose();
}

public ref struct PooledSequenceBuilderRef<T>
{
    public const int DefaultInitialCapacity = 256;
    public const int MaxChunkCapacity = 32 * 1024;

    private const sbyte ClearModeDefault = 0;
    private const sbyte ClearModeFalse = 1;
    private const sbyte ClearModeTrue = 2;

    private T[]? currentArray;
    private int currentIndex;

    private PooledSequenceSegmentRef<T>? firstSegment;
    private PooledSequenceSegmentRef<T>? lastSegment;

    private long length;
    private int nextChunkCapacity;
    private bool isFinalized;
    private ReadOnlySequence<T> sequence;

    private sbyte clearArrayOnReturnMode;

    public PooledSequenceBuilderRef(
        int initialCapacity = DefaultInitialCapacity,
        bool? clearArrayOnReturn = null)
    {
        if (initialCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        }

        this.currentArray = null;
        this.currentIndex = 0;

        this.firstSegment = null;
        this.lastSegment = null;

        this.length = 0;
        this.nextChunkCapacity = initialCapacity;
        this.isFinalized = false;
        this.sequence = ReadOnlySequence<T>.Empty;

        this.clearArrayOnReturnMode = clearArrayOnReturn switch
        {
            true => ClearModeTrue,
            false => ClearModeFalse,
            null => ClearModeDefault,
        };
    }

    public long Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.length;
    }

    private bool ClearArrayOnReturn
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return this.clearArrayOnReturnMode switch
            {
                ClearModeTrue => true,
                ClearModeFalse => false,
                _ => RuntimeHelpers.IsReferenceOrContainsReferences<T>(),
            };
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T value)
    {
        if (this.isFinalized)
        {
            ThrowAlreadyFinalized();
        }

        var array = this.currentArray;
        if (array is null)
        {
            array = this.RentChunk();
            this.currentArray = array;
        }

        if ((uint)this.currentIndex >= (uint)array.Length)
        {
            this.CommitCurrentChunk();

            array = this.RentChunk();
            this.currentArray = array;
        }

        array[this.currentIndex++] = value;
        this.length++;
    }

    public ReadOnlySequence<T> ToReadOnlySequence()
    {
        if (this.isFinalized)
        {
            return this.sequence;
        }

        this.isFinalized = true;

        if (this.length == 0)
        {
            this.sequence = ReadOnlySequence<T>.Empty;
            return this.sequence;
        }

        // Fast path: only one array was used.
        // No ReadOnlySequenceSegment allocation is needed.
        if (this.firstSegment is null)
        {
            var array = this.currentArray!;
            this.sequence = new ReadOnlySequence<T>(
                array.AsMemory(0, this.currentIndex));

            return this.sequence;
        }

        // Multi-chunk path.
        // Commit the current partially-filled chunk as the final segment.
        if (this.currentIndex > 0)
        {
            this.CommitCurrentChunk();
        }

        var first = this.firstSegment!;
        var last = this.lastSegment!;

        this.sequence = new ReadOnlySequence<T>(
            first,
            0,
            last,
            last.Memory.Length);

        return this.sequence;
    }

    public void Dispose()
    {
        var clearArray = this.ClearArrayOnReturn;

        var array = this.currentArray;
        if (array is not null)
        {
            ArrayPool<T>.Shared.Return(array, clearArray: clearArray);
            this.currentArray = null;
        }

        var segment = this.firstSegment;
        while (segment is not null)
        {
            var next = segment.GetNextSegment();
            var segmentArray = segment.Array;

            if (segmentArray is not null)
            {
                ArrayPool<T>.Shared.Return(segmentArray, clearArray: clearArray);
            }

            PooledSequenceSegmentPoolRef<T>.Return(segment);
            segment = next;
        }

        this.currentIndex = 0;
        this.firstSegment = null;
        this.lastSegment = null;
        this.length = 0;
        this.sequence = ReadOnlySequence<T>.Empty;
        this.isFinalized = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetNextChunkCapacity(int currentCapacity)
    {
        if (currentCapacity >= MaxChunkCapacity)
        {
            return currentCapacity;
        }

        if (currentCapacity > MaxChunkCapacity / 2)
        {
            return MaxChunkCapacity;
        }

        return currentCapacity * 2;
    }

    private static void ThrowAlreadyFinalized()
        => throw new InvalidOperationException("The sequence has already been finalized.");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private T[] RentChunk()
    {
        var capacity = this.nextChunkCapacity;
        if (capacity <= 0)
        {
            capacity = DefaultInitialCapacity;
        }

        var array = ArrayPool<T>.Shared.Rent(capacity);

        this.nextChunkCapacity = GetNextChunkCapacity(array.Length);

        return array;
    }

    private void CommitCurrentChunk()
    {
        var array = this.currentArray;
        if (array is null)
        {
            return;
        }

        var written = this.currentIndex;
        if (written == 0)
        {
            return;
        }

        var runningIndex = this.length - written;

        var segment = PooledSequenceSegmentPoolRef<T>.Rent();
        segment.Initialize(array, written, runningIndex);

        if (this.firstSegment is null)
        {
            this.firstSegment = segment;
        }
        else
        {
            this.lastSegment!.SetNext(segment);
        }

        this.lastSegment = segment;

        this.currentArray = null;
        this.currentIndex = 0;
    }
}

internal static class PooledSequenceSegmentPoolRef<T>
{
    private static PooledSequenceSegmentRef<T>? head;

    public static PooledSequenceSegmentRef<T> Rent()
    {
        while (true)
        {
            var current = Volatile.Read(ref head);
            if (current is null)
            {
                return new PooledSequenceSegmentRef<T>();
            }

            var next = current.PoolNext;

            if (Interlocked.CompareExchange(ref head, next, current) == current)
            {
                current.PoolNext = null;
                return current;
            }
        }
    }

    public static void Return(PooledSequenceSegmentRef<T> segment)
    {
        segment.ResetForPool();

        while (true)
        {
            var current = Volatile.Read(ref head);
            segment.PoolNext = current;

            if (Interlocked.CompareExchange(ref head, segment, current) == current)
            {
                return;
            }
        }
    }
}

internal sealed class PooledSequenceSegmentRef<T> : ReadOnlySequenceSegment<T>
{
    internal T[]? Array;
    internal PooledSequenceSegmentRef<T>? PoolNext;

    public void Initialize(T[] array, int length, long runningIndex)
    {
        this.Array = array;
        this.Memory = array.AsMemory(0, length);
        this.RunningIndex = runningIndex;
        this.Next = null;
        this.PoolNext = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetNext(PooledSequenceSegmentRef<T> next)
    {
        this.Next = next;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PooledSequenceSegmentRef<T>? GetNextSegment()
    {
        return (PooledSequenceSegmentRef<T>?)this.Next;
    }

    public void ResetForPool()
    {
        this.Array = null;
        this.Memory = default;
        this.RunningIndex = 0;
        this.Next = null;
        this.PoolNext = null;
    }
}
