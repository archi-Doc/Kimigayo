// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Kimi.Compiler;

// Dense Boolean and three-state Loan tables retain constant-time indexed access without a byte per cell.
internal struct PackedAnalysisTable(int bits)
{
    private readonly int indexShift = bits == 1 ? 3 : 2;
    private readonly int valueMask = (1 << bits) - 1;
    private byte[] storage = [];

    internal int ByteCapacity => this.storage.Length;

    internal int Capacity => this.storage.Length << this.indexShift;

    internal LoanRequirement this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (LoanRequirement)((this.storage[index >> this.indexShift] >> ((index & ((1 << this.indexShift) - 1)) * bits)) & this.valueMask);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            var shift = (index & ((1 << this.indexShift) - 1)) * bits;
            ref var cell = ref this.storage[index >> this.indexShift];
            cell = (byte)((cell & ~(this.valueMask << shift)) | ((int)value << shift));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsSet(int index) => this[index] != LoanRequirement.None;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Set(int index, bool value) => this[index] = value ? LoanRequirement.Ref : LoanRequirement.None;

    internal void Reset(int cells)
    {
        var bytes = this.Ensure(cells);
        this.storage.AsSpan(0, bytes).Clear();
    }

    internal void CopyFrom(PackedAnalysisTable source, int cells)
    {
        var bytes = this.Ensure(cells);
        source.storage.AsSpan(0, bytes).CopyTo(this.storage);
    }

    private int Ensure(int cells)
    {
        var bytes = (int)(((long)cells + (1 << this.indexShift) - 1) >> this.indexShift);
        if (this.storage.Length < bytes)
        {
            Array.Resize(ref this.storage, bytes);
        }

        return bytes;
    }
}
