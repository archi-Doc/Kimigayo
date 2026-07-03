// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Arc.Collections;

#pragma warning disable SA1401 // Fields should be private

namespace Kimigayo.Language;

/// <summary>
/// Builds a <see cref="ReadOnlySequence{T}"/> backed by pooled arrays.
/// </summary>
/// <remarks>
/// The returned sequence is valid only until this builder is disposed.
/// Do not store or use the returned sequence after calling <see cref="Dispose"/>.
/// </remarks>
/// <typeparam name="T">The element type stored in the sequence.</typeparam>
public ref struct SequenceBuilder<T>
{
    public const int DefaultInitialCapacity = 256;
    public const int MaxChunkCapacity = 32 * 1024;
    public const int SegmentPoolCapacity = 2;

    private static readonly ObjectPool<PooledSequenceSegment> SegmentPool = new(() => new(), SegmentPoolCapacity);

    private readonly bool clearArrayOnReturn;
    private T[]? currentArray;
    private int currentIndex;

    private PooledSequenceSegment? firstSegment;
    private PooledSequenceSegment? lastSegment;

    private long length;
    private int nextChunkCapacity;
    private bool isFinalized;
    private ReadOnlySequence<T> sequence;

    public SequenceBuilder(int initialCapacity = DefaultInitialCapacity, bool? clearArrayOnReturn = null)
    {
        if (initialCapacity <= 0 || initialCapacity > MaxChunkCapacity)
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

        this.clearArrayOnReturn = clearArrayOnReturn switch
        {
            true => true,
            false => false,
            _ => RuntimeHelpers.IsReferenceOrContainsReferences<T>(),
        };
    }

    public long Length => this.length;

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

    public void AddRange(ReadOnlySpan<T> values)
    {
        if (this.isFinalized)
        {
            ThrowAlreadyFinalized();
        }

        while (!values.IsEmpty)
        {
            var array = this.currentArray;
            if (array is null)
            {
                array = this.RentChunk();
                this.currentArray = array;
            }

            if ((uint)this.currentIndex >= (uint)array.Length)
            {
                this.CommitCurrentChunk();
                continue;
            }

            var destination = array.AsSpan(this.currentIndex);
            var copyLength = Math.Min(destination.Length, values.Length);

            values.Slice(0, copyLength).CopyTo(destination);

            this.currentIndex += copyLength;
            this.length += copyLength;
            values = values.Slice(copyLength);
        }
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
            this.sequence = new ReadOnlySequence<T>(array.AsMemory(0, this.currentIndex));
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
        this.sequence = new ReadOnlySequence<T>(first, 0, last, last.Memory.Length);
        return this.sequence;
    }

    public void Dispose()
    {
        var array = this.currentArray;
        if (array is not null)
        {
            ArrayPool<T>.Shared.Return(array, this.clearArrayOnReturn);
            this.currentArray = null;
        }

        var segment = this.firstSegment;
        while (segment is not null)
        {
            var next = segment.GetNextSegment();
            var segmentArray = segment.Array;
            if (segmentArray is not null)
            {
                ArrayPool<T>.Shared.Return(segmentArray, this.clearArrayOnReturn);
            }

            segment.ResetForPool();
            SegmentPool.Return(segment);
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

    [DoesNotReturn]
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
            // Keep the current rented array for subsequent writes.
            return;
        }

        var runningIndex = this.length - written;

        var segment = SegmentPool.Rent();
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

    private sealed class PooledSequenceSegment : ReadOnlySequenceSegment<T>
    {
        internal T[]? Array;

        public void Initialize(T[] array, int length, long runningIndex)
        {
            this.Array = array;
            this.Memory = array.AsMemory(0, length);
            this.RunningIndex = runningIndex;
            this.Next = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetNext(PooledSequenceSegment next)
        {
            this.Next = next;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PooledSequenceSegment? GetNextSegment()
        {
            return (PooledSequenceSegment?)this.Next;
        }

        public void ResetForPool()
        {
            this.Array = null;
            this.Memory = default;
            this.RunningIndex = 0;
            this.Next = null;
        }
    }
}
