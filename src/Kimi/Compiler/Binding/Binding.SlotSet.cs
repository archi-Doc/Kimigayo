// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly ScratchBuffers<ulong> slotWordScratch = new();

    // Most calls fit in the inline word. Larger declarations borrow only the additional words;
    // scratch lifetime follows candidate nesting, not a language limit or a global pool.
    private SlotSet RentSlotSet(int count)
    {
        var words = count > 64 ? ((count - 1) >> 6) : 0;
        var tail = this.slotWordScratch.Rent(words);
        tail.AsSpan(0, words).Clear();
        return new(tail, words);
    }

    private void ReturnSlotSet(SlotSet set) => this.slotWordScratch.Return(set.Tail);

    private struct SlotSet(ulong[] tail, int words)
    {
        private ulong head;
        private int count;

        internal readonly ulong[] Tail => tail;

        internal readonly bool IsEmpty => this.count == 0;

        internal readonly bool Contains(int slot)
            => slot >= 0 && (slot < 64 ? (this.head & (1UL << slot)) != 0 :
                ((slot >> 6) - 1) < words && (tail[(slot >> 6) - 1] & (1UL << (slot & 63))) != 0);

        internal void Add(int slot)
        {
            if (this.Contains(slot))
            {
                return;
            }

            if (slot < 64)
            {
                this.head |= 1UL << slot;
            }
            else
            {
                tail[(slot >> 6) - 1] |= 1UL << (slot & 63);
            }

            this.count++;
        }

        internal void Clear()
        {
            this.head = 0;
            this.count = 0;
            tail.AsSpan(0, words).Clear();
        }

        internal readonly int First()
        {
            if (this.head != 0)
            {
                return BitOperations.TrailingZeroCount(this.head);
            }

            for (var i = 0; i < words; i++)
            {
                if (tail[i] != 0)
                {
                    return ((i + 1) << 6) + BitOperations.TrailingZeroCount(tail[i]);
                }
            }

            return -1;
        }
    }
}
