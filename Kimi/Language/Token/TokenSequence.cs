// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;

namespace Kimigayo.Language;

#pragma warning disable SA1124

public ref struct ByteSequence : IBufferWriter<Token>, IDisposable
{
    public const int DefaultVaultSize = 1024; // 1024 x 40 = 40kb
    private static ArrayPool<Token> arrayPool = ArrayPool<Token>.Shared; // ArrayPool<Token>.Create(2 * 1024, 100);

    #region FieldAndProperty

    private Vault? firstVault;
    private Vault? lastVault;

    #endregion

    public ReadOnlySequence<Token> ToReadOnlySequence()
    {
        return this.firstVault == null ?
            ReadOnlySequence<Token>.Empty :
            new ReadOnlySequence<Token>(this.firstVault, 0, this.lastVault!, this.lastVault!.Size);
    }

    public ReadOnlyMemory<Token> ToReadOnlyMemory()
    {
        if (this.firstVault == null)
        {
            return default;
        }
        else if (this.firstVault == this.lastVault)
        {// Single vault
            return new ReadOnlyMemory<Token>(this.firstVault.Array, 0, this.firstVault.Size);
        }
        else
        {// Multiple vaults
            return new ReadOnlySequence<Token>(this.firstVault, 0, this.lastVault!, this.lastVault!.Size).ToArray();
        }
    }

    public ReadOnlySpan<Token> ToReadOnlySpan()
    {
        if (this.firstVault == null)
        {
            return default;
        }
        else if (this.firstVault == this.lastVault)
        {// Single vault
            return new ReadOnlySpan<Token>(this.firstVault.Array, 0, this.firstVault.Size);
        }
        else
        {// Multiple vaults
            return new ReadOnlySequence<Token>(this.firstVault, 0, this.lastVault!, this.lastVault!.Size).ToArray();
        }
    }

    public void Advance(int count)
    {
        if (this.lastVault == null)
        {
            throw new InvalidOperationException("Cannot advance before acquiring memory.");
        }

        this.lastVault.Advance(count);
    }

    public void Dispose()
    {
        var current = this.firstVault;
        while (current != null)
        {
            var next = (Vault?)current.Next;

            arrayPool.Return(current.Array);
            current.Clear();

            current = next;
        }

        this.firstVault = this.lastVault = null;
    }

    public Memory<Token> GetMemory(int sizeHint = 0) => this.GetVault(sizeHint).RemainingMemory;

    public Span<Token> GetSpan(int sizeHint = 0) => this.GetVault(sizeHint).RemainingSpan;

    private Vault GetVault(int sizeHint)
    {
        int bufferSizeToAllocate = 0;

        if (sizeHint == 0)
        {
            if (this.lastVault == null || this.lastVault.Remaining == 0)
            {
                bufferSizeToAllocate = DefaultVaultSize;
            }
        }
        else
        {
            if (this.lastVault == null || this.lastVault.Remaining < sizeHint)
            {
                bufferSizeToAllocate = Math.Max(sizeHint, DefaultVaultSize);
            }
        }

        if (bufferSizeToAllocate > 0)
        {
            var vault = new Vault(arrayPool.Rent(bufferSizeToAllocate));
            this.AddVault(vault);
        }

        return this.lastVault!;
    }

    private void AddVault(Vault vault)
    {
        if (this.lastVault == null)
        {
            this.firstVault = this.lastVault = vault;
        }
        else
        {
            if (this.lastVault.Size > 0)
            {// Add a new block.
                this.lastVault.SetNext(vault);
            }
            else
            {// The last block is completely unused. Replace it instead of appending to it.
                var current = this.firstVault!;
                if (this.firstVault == this.lastVault)
                { // Only one vault.
                    this.firstVault = vault;
                }
                else
                {
                    while (current.Next != this.lastVault)
                    {
                        current = (Vault)current.Next!;
                    }
                }

                arrayPool.Return(this.lastVault.Array);
                this.lastVault.Clear();

                current.SetNext(vault);
            }

            this.lastVault = vault;
        }
    }

    private class Vault : ReadOnlySequenceSegment<Token>
    {
        public Vault(Token[] array)
        {
            this.Array = array;
            this.Memory = array;
        }

        internal Token[] Array { get; set; }

        internal int Size { get; set; }

        internal int Remaining => this.Array.Length - this.Size;

        internal Memory<Token> RemainingMemory => this.Array.AsMemory().Slice(this.Size);

        internal Span<Token> RemainingSpan => this.Array.AsSpan().Slice(this.Size);

        internal void Advance(int count)
        {
            if ((uint)count > (uint)this.Remaining)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            this.Size += count;
        }

        internal void SetNext(Vault next)
        {
            this.Next = next;
            next.RunningIndex = this.RunningIndex + this.Size;
            this.Memory = this.Memory.Slice(0, this.Size);
        }

        internal void Clear()
        {
            this.Memory = default;
            this.Next = null;
            this.RunningIndex = 0;
            this.Size = 0;
            this.Array = null!;
        }
    }
}
