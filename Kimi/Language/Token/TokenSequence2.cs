// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Runtime.CompilerServices;

#pragma warning disable SA1401 // Fields should be private

namespace Kimigayo.Language;

public ref struct TokenSequenceBuilder2
{
    private const int DefaultInitialCapacity = 256;

    private Token[]? _currentArray;   // 現在書き込み中の配列
    private int _currentCount;        // _currentArray 内の書き込み済み数
    private Segment? _firstSegment;   // 満杯になり確定済みのセグメント(先頭)
    private Segment? _lastSegment;    // 同(末尾)
    private long _sealedLength;       // 確定済みセグメントの合計長

    public TokenSequenceBuilder2(int initialCapacity)
    {
        this._currentArray = ArrayPool<Token>.Shared.Rent(initialCapacity);
        this._currentCount = 0;
        this._firstSegment = null;
        this._lastSegment = null;
        this._sealedLength = 0;
    }

    public readonly long Length => this._sealedLength + this._currentCount;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddToken(Token token)
    {
        Token[]? array = this._currentArray;
        int count = this._currentCount;

        if (array is not null && (uint)count < (uint)array.Length)
        {
            array[count] = token;
            this._currentCount = count + 1;
            return;
        }

        this.AddTokenSlow(token);
    }

    public ReadOnlySequence<Token> Build()
    {
        if (this._firstSegment is null)
        {
            return this._currentArray is null
                ? ReadOnlySequence<Token>.Empty
                : new ReadOnlySequence<Token>(this._currentArray, 0, this._currentCount);
        }

        var tail = new Segment(this._currentArray!, this._currentCount, this._sealedLength);
        this._lastSegment!.SetNext(tail);

        var sequence = new ReadOnlySequence<Token>(this._firstSegment, 0, tail, this._currentCount);

        return sequence;
    }

    public void Dispose()
    {
        bool clear = RuntimeHelpers.IsReferenceOrContainsReferences<Token>();

        Segment? segment = this._firstSegment;
        while (segment is not null)
        {
            ArrayPool<Token>.Shared.Return(segment.Array, clear);
            segment = (Segment?)segment.Next;
        }

        if (this._currentArray is not null)
        {
            ArrayPool<Token>.Shared.Return(this._currentArray, clear);
        }

        this._currentArray = null;
        this._firstSegment = null;
        this._lastSegment = null;
        this._currentCount = 0;
        this._sealedLength = 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AddTokenSlow(Token token)
    {
        if (this._currentArray is null)
        {
            this._currentArray = ArrayPool<Token>.Shared.Rent(DefaultInitialCapacity);
        }
        else
        {
            this.SealCurrentArray();
            int newSize = Math.Min(this._currentArray!.Length * 2, 0x40000000); // 上限 2^30
            this._currentArray = ArrayPool<Token>.Shared.Rent(newSize);
            this._currentCount = 0;
        }

        this._currentArray[this._currentCount++] = token;
    }

    private void SealCurrentArray()
    {
        var segment = new Segment(this._currentArray!, this._currentCount, this._sealedLength);
        this._sealedLength += this._currentCount;

        if (this._firstSegment is null)
        {
            this._firstSegment = segment;
        }
        else
        {
            this._lastSegment!.SetNext(segment);
        }

        this._lastSegment = segment;
    }

    private sealed class Segment : ReadOnlySequenceSegment<Token>
    {
        public readonly Token[] Array;

        public Segment(Token[] array, int count, long runningIndex)
        {
            this.Array = array;
            this.Memory = new ReadOnlyMemory<Token>(array, 0, count);
            this.RunningIndex = runningIndex;
        }

        public void SetNext(Segment next) => this.Next = next;
    }
}
