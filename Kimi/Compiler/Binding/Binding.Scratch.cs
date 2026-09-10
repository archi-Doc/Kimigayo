// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly ScratchBuffers<BoundType?> typeScratch = new();
    private readonly ScratchBuffers<BoundOrigin> originScratch = new();
    private readonly ScratchBuffers<int> indexScratch = new();
    private readonly ScratchBuffers<bool> flagScratch = new();

    // Nullability describes the caller's view: argument inference leaves holes, while Type
    // construction fills every slot before publishing a span. Both share the same stack.
    private BoundType[] RentTypes(int count) => (BoundType[])(object)this.typeScratch.Rent(count);

    /// <summary>Binding-owned nested scratch, independent of other compilations' pool traffic.</summary>
    private sealed class ScratchBuffers<T>
    {
        private readonly List<Buffer> buffers = new();
        private int depth;

        internal T[] Rent(int count)
        {
            if (count == 0)
            {
                return [];
            }

            if (this.depth == this.buffers.Count)
            {
                this.buffers.Add(new(new T[Math.Max(16, count)], count));
            }

            var buffer = this.buffers[this.depth];
            if (buffer.Values.Length < count)
            {
                buffer = new(new T[Math.Max(count, buffer.Values.Length * 2)], count);
            }

            buffer.Used = count;
            this.buffers[this.depth++] = buffer;
            return buffer.Values;
        }

        internal void Return(T[] values, bool clearArray = false)
        {
            if (values.Length == 0)
            {
                return;
            }

            var buffer = this.buffers[--this.depth];
            System.Diagnostics.Debug.Assert(ReferenceEquals(buffer.Values, values), "Scratch must be returned in nested order.");
            if (clearArray)
            {
                Array.Clear(values, 0, buffer.Used);
            }
        }

        private record struct Buffer(T[] Values, int Used);
    }
}
