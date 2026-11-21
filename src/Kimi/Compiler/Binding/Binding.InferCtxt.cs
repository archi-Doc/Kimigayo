// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// InferCtxt (SPEC 10.8; DECISIONS R7-I2): the owner of every solve's slot frames, with an undo log and snapshots.
// - Responsibility: the slot frames (a solve's Type slots, length slots, slot bindings and environment judgments: a call
//   candidate's, one per candidate, a Case construction's, a fixed construction's re-check, an implicit constructor's, a function
//   reference's), held privately, with each pair slot's selector evidence (SPEC 8.1.2, 10.8: the first source's Type, whose outer
//   Semantics every other source of the slot's binder must have; none for a slot fixed before any evidence) and the solve's node; their
//   writes, each logged while a snapshot is open; Snapshot and Rollback. A frame lives as long as its solve, so its bindings, judgments
//   and selectors are that solve's alone.
// - Inputs: Rent, with the solve's node and the scope of the use the solve judges (no default: each Rent site gives its own), and
//   Return from the solve that owns a frame; writes through a SlotFrame (SetType, SetLength, AddBinding, SetEnvironmentJudgment,
//   SetSelector, ClearTypes, ClearLengths, CopyTypes) from the unifier (Infer, InferLength, InferArrayLength), a solve's seeding and
//   clearing, a trial's write-back and an open Origin's instantiation.
// - Outputs: read-only views (SlotFrame.Types, SlotFrame.Lengths, SlotFrame.Bindings, SlotFrame.EnvironmentJudgment,
//   SlotFrame.Selectors) and SlotFrame.Node, which SlotSolution and the other solves read; SlotFrame.View, the frame's scope as given,
//   which only the unifier reads (R7-C2c2 c2-4b, design R4.12c: J4 forms a slot's bindings in that view).
// - Invariants: no array or mutable span of a frame leaves this file, so the compiler admits no unlogged write but the environment
//   judgments', which are written only with no snapshot open (asserted); no snapshot is open and the log is empty between two solves;
//   a frame is returned only with no snapshot open (asserted); Rollback undoes in reverse order; the frames and the log are pooled and
//   reused (warm 0 B).
// - Prohibitions: no rule is decided here (fit, meet, applicability, formation, an Origin relation); no syntax is read. The Origin and
//   input-Origin slots, the All* saves of a comparison and the slot arrays of solves that infer nothing (explicit arguments of a Function
//   Item, a specialization, a contract match) are not frames.
public sealed partial class Binding
{
    private readonly InferCtxt infer = new();

    private readonly record struct InferSnapshot(int Log, int Depth, int Pool);

    // G1128: one binding of a slot, the Type its evidence gave and the composed polarity of its position (15.3.5: 1, -1,
    // 0); pooled nodes reused across solves (warm 0 B), newest first, the head in the marks column (logged like the other columns).
    private sealed class SlotBinding
    {
        internal BoundType Type { get; set; } = null!;

        internal int Polarity { get; set; }

        internal int Frame { get; set; }

        internal SlotBinding? Next { get; set; }
    }

    // One solve's slot frame: its Type slots, length slots, slot bindings, environment judgments and pair slots' selector evidence, and
    // the solve's node, read through read-only views and written only through its operations (logged but the judgments', which are
    // written with no snapshot open).
    private readonly struct SlotFrame
    {
        private readonly InferCtxt store;
        private readonly int id;
        private readonly BindingScope view;

        internal SlotFrame(InferCtxt store, int id, BindingScope view)
        {
            this.store = store;
            this.id = id;
            this.view = view;
        }

        // The scope of the use whose solve owns the frame (a call's, a construction's, a reference's), carried for J4 (design R4.12c).
        internal BindingScope View => this.view;

        internal ReadOnlySpan<BoundType?> Types => this.store.Types(this.id);

        internal ReadOnlySpan<BoundLength?> Lengths => this.store.Lengths(this.id);

        // SPEC 8.1.2, 10.8: per pair slot, the first source of its binder (an occurrence of the original or an application), the
        // evidence every other source is compared with (Binding.IsSource); none for a slot fixed before any evidence.
        internal ReadOnlySpan<BoundType?> Selectors => this.store.Selectors(this.id);

        // The node of the solve the frame belongs to (a call, a reference's use, a construction), whose open regions it keys.
        internal Koto Node => this.store.Node(this.id);

        internal int Id => this.id;

        // SPEC 10.8, 15.3.6 (J2, G1128 R1.3 (f)): whether every binding of the slot in this solve is at a covariant position.
        internal bool BindsCovariantly(int slot) => this.store.BindsCovariantly(this.id, slot);

        // G1128 R1.3 (e), F's ruling 38: whether the slot holds two distinct bindings, so that slot variables would add something. The
        // bindings are formed in the solve's view (Infer's head, R7-C3), where identical Types are one object (ClassForm's invariant
        // (2)), so reference identity is the identity judge's verdict.
        internal bool BindsDistinctTypes(int slot)
        {
            var head = this.Bindings(slot);
            for (var node = head?.Next; node is not null; node = node.Next)
            {
                if (!ReferenceEquals(node.Type, head!.Type))
                {
                    return true;
                }
            }

            return false;
        }

        // DECISIONS BU-86: the slot's environment judgment in this solve: a known call signature's result over its closure's hidden
        // environment receiver gave it no evidence (Infer); read by the candidate's trial (EnvironmentJudgment).
        internal PendingJudgment? EnvironmentJudgment(int slot) => this.store.EnvironmentJudgment(this.id, slot);

        internal void SetType(int slot, BoundType? value) => this.store.SetType(this.id, slot, value);

        internal SlotBinding? Bindings(int slot) => this.store.Bindings(this.id, slot);

        internal void AddBinding(int slot, BoundType type, int polarity) => this.store.AddBinding(this.id, slot, type, polarity);

        internal void SetEnvironmentJudgment(int slot, PendingJudgment value) => this.store.SetEnvironmentJudgment(this.id, slot, value);

        internal void SetLength(int slot, BoundLength? value) => this.store.SetLength(this.id, slot, value);

        internal void SetSelector(int slot, BoundType value) => this.store.SetSelector(this.id, slot, value);

        internal void ClearTypes(int count) => this.store.ClearTypes(this.id, count);

        internal void ClearLengths(int count) => this.store.ClearLengths(this.id, count);

        internal void CopyTypes(ReadOnlySpan<BoundType?> source) => this.store.CopyTypes(this.id, source);
    }

    private sealed class InferCtxt
    {
        private SlotBinding?[] pool = new SlotBinding?[16];
        private int poolCount;
        private int rented;
        private BoundType?[][] types = new BoundType?[8][];
        private BoundLength?[][] lengths = new BoundLength?[8][];
        private BoundType?[][] selectors = new BoundType?[8][];
        private Koto?[] nodes = new Koto?[8];
        private object?[][] marks = new object?[8][];
        // Typed, so outside the object?[] log: written only with no snapshot open (asserted).
        private PendingJudgment?[][] environments = new PendingJudgment?[8][];
        private int[] free = new int[8];
        private int frameCount;
        private int freeCount;
        private Undo[] log = new Undo[32];
        private int logCount;
        private int depth;

        // A frame of at least `capacity` Type and length slots, all unbound, for the solve at `node` judged in `view`.
        internal SlotFrame Rent(int capacity, Koto node, BindingScope view)
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
                    Array.Resize(ref this.marks, this.frameCount * 2);
                    Array.Resize(ref this.environments, this.frameCount * 2);
                    Array.Resize(ref this.free, this.frameCount * 2);
                }

                id = this.frameCount++;
                this.types[id] = [];
                this.lengths[id] = [];
                this.selectors[id] = [];
                this.marks[id] = [];
                this.environments[id] = [];
            }

            if (this.types[id].Length < capacity)
            {
                this.types[id] = new BoundType?[Math.Max(capacity, 4)];
                this.lengths[id] = new BoundLength?[Math.Max(capacity, 4)];
                this.selectors[id] = new BoundType?[Math.Max(capacity, 4)];
                this.marks[id] = new object?[Math.Max(capacity, 4)];
                this.environments[id] = new PendingJudgment?[Math.Max(capacity, 4)];
            }

            this.nodes[id] = node;
            this.rented++;
            return new(this, id, view);
        }

        // Returns a frame, cleared; never under an open snapshot, whose Rollback would write it. The binding pool is reset when the
        // outermost frame returns (candidate frames interleave their appends).
        internal void Return(SlotFrame frame)
        {
            Debug.Assert(this.depth == 0, "A frame is returned only with no snapshot open.");
            Array.Clear(this.types[frame.Id]);
            Array.Clear(this.lengths[frame.Id]);
            Array.Clear(this.selectors[frame.Id]);
            this.nodes[frame.Id] = null;
            Array.Clear(this.marks[frame.Id]);
            Array.Clear(this.environments[frame.Id]);
            this.free[this.freeCount++] = frame.Id;
            if (--this.rented == 0)
            {
                for (var i = 0; i < this.poolCount; i++)
                {
                    this.pool[i]!.Type = null!;
                    this.pool[i]!.Next = null;
                }

                this.poolCount = 0;
            }
        }

        internal SlotBinding? Bindings(int id, int slot)
        {
            var head = this.marks[id][slot] as SlotBinding;
            for (var node = head; node is not null; node = node.Next)
            {
                if (node.Frame != id)
                {
                    throw new InvalidOperationException("A frame's binding chain reaches only its own frame's nodes (InternalInvariant).");
                }
            }

            return head;
        }

        internal void AddBinding(int id, int slot, BoundType type, int polarity)
        {
            if (this.poolCount == this.pool.Length)
            {
                Array.Resize(ref this.pool, this.poolCount * 2);
            }

            var node = this.pool[this.poolCount++] ??= new();
            var frame = this.marks[id];
            (node.Type, node.Polarity, node.Frame, node.Next) = (type, polarity, id, frame[slot] as SlotBinding);
            this.Push(frame, slot);
            frame[slot] = node;
        }

        internal ReadOnlySpan<BoundType?> Types(int id) => this.types[id];

        internal ReadOnlySpan<BoundLength?> Lengths(int id) => this.lengths[id];

        internal ReadOnlySpan<BoundType?> Selectors(int id) => this.selectors[id];

        internal Koto Node(int id) => this.nodes[id]!;

        internal bool BindsCovariantly(int id, int slot)
        {
            for (var node = this.marks[id][slot] as SlotBinding; node is not null; node = node.Next)
            {
                if (node.Polarity != 1)
                {
                    return false;
                }
            }

            return true;
        }

        internal PendingJudgment? EnvironmentJudgment(int id, int slot) => this.environments[id][slot];

        internal void SetEnvironmentJudgment(int id, int slot, PendingJudgment value)
        {
            Debug.Assert(this.depth == 0, "An environment judgment is written only with no snapshot open.");
            this.environments[id][slot] = value;
        }

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

        // A solve's Type slots are cleared with their bindings (the marks column's heads) and selector evidence: each describes the slot
        // it clears.
        internal void ClearTypes(int id, int count)
        {
            this.Clear(this.types[id], count);
            this.Clear(this.marks[id], count);
            this.Clear(this.selectors[id], count);
            this.environments[id].AsSpan(0, count).Clear(); // A judgment describes the solve the slots begin.
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

        internal InferSnapshot Snapshot() => new(this.logCount, ++this.depth, this.poolCount);

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

            this.poolCount = snapshot.Pool; // The nodes appended since the snapshot are unreachable once the heads are restored.
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
