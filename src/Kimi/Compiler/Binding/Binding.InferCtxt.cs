// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// InferCtxt (SPEC 10.8; DECISIONS R7-I2): the owner of every solve's slot frames, with an undo log and snapshots.
// - Responsibility: the slot frames (a solve's Type slots and length slots: a call candidate's, a Case construction's, a fixed
//   construction's re-check, an implicit constructor's, a function reference's), held privately, with each pair slot's selector
//   evidence (SPEC 8.1.2, 10.8: the first source's Type whose outer Semantics fixes s while W is still unbound) and the solve's node;
//   their writes, each logged while a snapshot is open; Snapshot and Rollback.
// - Inputs: Rent and Return from the solve that owns a frame; writes through a SlotFrame (SetType, SetLength, ClearTypes, ClearLengths,
//   CopyTypes, CopyLengths) from the unifier (Infer, InferLength, InferArrayLength), a candidate's seeding and clearing, a trial's
//   write-back, the selected candidate's restore and an open Origin's instantiation.
// - Outputs: read-only views (SlotFrame.Types, SlotFrame.Lengths, SlotFrame.Selectors) and SlotFrame.Node, which SlotSolution and the
//   other solves read.
// - Invariants: no array or mutable span of a frame leaves this file, so the compiler admits no unlogged write; no snapshot is open and
//   the log is empty between two solves; a frame is returned only with no snapshot open (asserted); Rollback undoes in reverse order;
//   the frames and the log are pooled and reused (warm 0 B).
// - Prohibitions: no rule is decided here (fit, meet, applicability, formation, an Origin relation); no syntax is read. The Origin and
//   input-Origin slots, the All* saves of a comparison and the slot arrays of solves that infer nothing (explicit arguments of a Function
//   Item, a specialization, a contract match) are not frames.
public sealed partial class Binding
{
    private readonly InferCtxt infer = new();

    private readonly record struct InferSnapshot(int Log, int Depth);

    // One solve's slot frame: its Type and length slots, read through read-only views and written only through its logged operations.
    private readonly struct SlotFrame
    {
        private readonly InferCtxt store;
        private readonly int id;

        internal SlotFrame(InferCtxt store, int id)
        {
            this.store = store;
            this.id = id;
        }

        internal ReadOnlySpan<BoundType?> Types => this.store.Types(this.id);

        internal ReadOnlySpan<BoundLength?> Lengths => this.store.Lengths(this.id);

        // SPEC 8.1.2, 10.8: per pair slot, the first source whose outer Semantics fixes its binder while the slot itself is unbound.
        internal ReadOnlySpan<BoundType?> Selectors => this.store.Selectors(this.id);

        // The node of the solve the frame belongs to (a call, a reference's use, a construction), whose open regions it keys.
        internal Koto Node => this.store.Node(this.id);

        internal int Id => this.id;

        internal void SetType(int slot, BoundType? value) => this.store.SetType(this.id, slot, value);

        internal void SetLength(int slot, BoundLength? value) => this.store.SetLength(this.id, slot, value);

        internal void SetSelector(int slot, BoundType value) => this.store.SetSelector(this.id, slot, value);

        internal void ClearTypes(int count) => this.store.ClearTypes(this.id, count);

        internal void ClearLengths(int count) => this.store.ClearLengths(this.id, count);

        internal void CopyTypes(ReadOnlySpan<BoundType?> source) => this.store.CopyTypes(this.id, source);

        internal void CopyLengths(ReadOnlySpan<BoundLength?> source) => this.store.CopyLengths(this.id, source);
    }

    private sealed class InferCtxt
    {
        private BoundType?[][] types = new BoundType?[8][];
        private BoundLength?[][] lengths = new BoundLength?[8][];
        private BoundType?[][] selectors = new BoundType?[8][];
        private Koto?[] nodes = new Koto?[8];
        private int[] free = new int[8];
        private int frameCount;
        private int freeCount;
        private Undo[] log = new Undo[32];
        private int logCount;
        private int depth;

        // A frame of at least `capacity` Type and length slots, all unbound, for the solve at `node`.
        internal SlotFrame Rent(int capacity, Koto node)
        {
            int id;
            if (this.freeCount != 0)
            {
                id = this.free[--this.freeCount];
            }
            else
            {
                if (this.frameCount == this.types.Length)
                {
                    Array.Resize(ref this.types, this.frameCount * 2);
                    Array.Resize(ref this.lengths, this.frameCount * 2);
                    Array.Resize(ref this.selectors, this.frameCount * 2);
                    Array.Resize(ref this.nodes, this.frameCount * 2);
                    Array.Resize(ref this.free, this.frameCount * 2);
                }

                id = this.frameCount++;
                this.types[id] = [];
                this.lengths[id] = [];
                this.selectors[id] = [];
            }

            if (this.types[id].Length < capacity)
            {
                this.types[id] = new BoundType?[Math.Max(capacity, 4)];
                this.lengths[id] = new BoundLength?[Math.Max(capacity, 4)];
                this.selectors[id] = new BoundType?[Math.Max(capacity, 4)];
            }

            this.nodes[id] = node;
            return new(this, id);
        }

        // Returns a frame, cleared; never under an open snapshot, whose Rollback would write it.
        internal void Return(SlotFrame frame)
        {
            Debug.Assert(this.depth == 0, "A frame is returned only with no snapshot open.");
            Array.Clear(this.types[frame.Id]);
            Array.Clear(this.lengths[frame.Id]);
            Array.Clear(this.selectors[frame.Id]);
            this.nodes[frame.Id] = null;
            this.free[this.freeCount++] = frame.Id;
        }

        internal ReadOnlySpan<BoundType?> Types(int id) => this.types[id];

        internal ReadOnlySpan<BoundLength?> Lengths(int id) => this.lengths[id];

        internal ReadOnlySpan<BoundType?> Selectors(int id) => this.selectors[id];

        internal Koto Node(int id) => this.nodes[id]!;

        internal void SetType(int id, int slot, BoundType? value)
        {
            var frame = this.types[id];
            this.Push(frame, slot);
            frame[slot] = value;
        }

        internal void SetLength(int id, int slot, BoundLength? value)
        {
            var frame = this.lengths[id];
            this.Push(frame, slot);
            frame[slot] = value;
        }

        internal void SetSelector(int id, int slot, BoundType value)
        {
            var frame = this.selectors[id];
            this.Push(frame, slot);
            frame[slot] = value;
        }

        // A solve's Type slots are cleared with their selector evidence: one solution's state.
        internal void ClearTypes(int id, int count)
        {
            this.Clear(this.types[id], count);
            this.Clear(this.selectors[id], count);
        }

        internal void ClearLengths(int id, int count) => this.Clear(this.lengths[id], count);

        internal void CopyTypes(int id, ReadOnlySpan<BoundType?> source)
        {
            var frame = this.types[id];
            for (var i = 0; i < source.Length; i++)
            {
                this.Push(frame, i);
            }

            source.CopyTo(frame);
        }

        internal void CopyLengths(int id, ReadOnlySpan<BoundLength?> source)
        {
            var frame = this.lengths[id];
            for (var i = 0; i < source.Length; i++)
            {
                this.Push(frame, i);
            }

            source.CopyTo(frame);
        }

        internal InferSnapshot Snapshot() => new(this.logCount, ++this.depth);

        // Restores every write since `snapshot` in reverse order.
        internal void Rollback(InferSnapshot snapshot)
        {
            Debug.Assert(snapshot.Depth == this.depth, "Snapshots close in reverse order.");
            while (this.logCount > snapshot.Log)
            {
                ref var undo = ref this.log[--this.logCount];
                undo.Frame[undo.Index] = undo.Old;
                undo = default;
            }

            if (--this.depth == 0)
            {
                this.logCount = 0;
            }
        }

        private void Clear<T>(T?[] frame, int count)
            where T : class
        {
            for (var i = 0; i < count; i++)
            {
                this.Push(frame, i);
            }

            frame.AsSpan(0, count).Clear();
        }

        private void Push(object?[] frame, int index)
        {
            if (this.depth == 0)
            {
                return;
            }

            if (this.logCount == this.log.Length)
            {
                Array.Resize(ref this.log, this.logCount * 2);
            }

            this.log[this.logCount++] = new(frame, index, frame[index]);
        }

        // One logged frame write: the frame's array, the element and its earlier value.
        private record struct Undo(object?[] Frame, int Index, object? Old);
    }
}
