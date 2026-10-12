// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the move paths of one MIR body (SPEC 15.1.3; DECISIONS R4-M21).
// Input: a validated body, its dataflow order (MirDataflow.Begin) and whether the body constructs its object (AdtDef.IsConstructor). Output:
// the paths numbered in pre-order, so a subtree is the range [path, End), each with its step from its parent, and the path of each Place.
// A path is a local or a step from a path: Field, Base (one depth at a time), ConstIndex or Downcast, keyed by its parent, kind and argument;
// a constructor's _0 has a child per Field and its base. A PairLayer step is transparent: it is the Place itself in the owner case, and the
// accesses of a reference case end before it (MirInit). Deref and Index end a path: an access through them is an access of the prefix's
// path, and a Move through them has no Move Path, except through a raw pointer (an untracked Take). Roots whose events all lie in one chain
// from their StorageLive (a StorageDead elsewhere kills whatever state arrives) are chain-local and numbered after the flow roots, so a head
// stores only the flow paths. A path admits one placement when its root is a let binding or it lies in a constructor's Field placed once
// (SPEC 15.1.1, 11.3.1).
// Must not decide initialization, copy a body per view, intern projections or change the body.

#pragma warning disable SA1402 // The path record belongs to the paths.

using System.Runtime.InteropServices;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

/// <summary>A move path by its pre-order number: its root local, its parent (-1 for a root), the end of its subtree [path, End), whether
/// it admits one placement per binding lifetime (a let binding's, or a constructor's Field placed once, <see cref="AdtDef.PlacedOnce"/>)
/// and its step from its parent, whose Type <see cref="MirBody.ProjectedType"/> gives (a root's is unused).</summary>
/// <param name="Local">The root local.</param>
/// <param name="Parent">The parent path, or -1.</param>
/// <param name="End">The path after its subtree.</param>
/// <param name="Once">Whether it admits one placement.</param>
/// <param name="Kind">The step: Field, Base (one depth), ConstIndex or Downcast.</param>
/// <param name="Arg">The step's argument.</param>
internal readonly record struct MirMovePath(int Local, int Parent, int End, bool Once, MirProjectionKind Kind, int Arg);

/// <summary>The move paths of one body (SPEC 15.1.3, DECISIONS R4-M21), view-independent: <see cref="Build"/> collects them in one walk of
/// the body's uses, and <see cref="Find"/> gives a Place's path and whether the Place is the path itself, a raw Place below it or a Place
/// without a Move Path below it.</summary>
internal sealed class MirMoveData
{
    /// <summary>The Place is its path.</summary>
    internal const int Direct = 0;

    /// <summary>The Place is through a raw pointer below its path: an untracked Take (SPEC 5.2.3).</summary>
    internal const int Raw = 1;

    /// <summary>The Place is through a reference, an object handle or an index below its path: it has no Move Path.</summary>
    internal const int NoPath = 2;

    private readonly Dictionary<(int Parent, MirProjectionKind Kind, int Arg), int> children = new(); // Each child by creation index.
    private readonly Dictionary<MirPlace, int> places = new(); // Each projected Place's path by creation index << 2 | access.
    private int[] parents = []; // By creation index; locals first.
    private (MirProjectionKind Kind, int Arg)[] steps = []; // By creation index: the step from the parent.
    private int[] sizes = [];
    private int[] numbers = []; // Each creation index's pre-order number.
    private bool[] once = []; // By creation index: a let root or a constructor's Field placed once.
    private int[] chains = []; // Each local's chain head, -1 without an event, -2 in more than one.
    private bool[] live = []; // Whether a local's first event, in reverse postorder, is its StorageLive.
    private MirMovePath[] paths = [];
    private MirBody body = null!;
    private int created;

    /// <summary>Gets the paths by pre-order number.</summary>
    internal ReadOnlySpan<MirMovePath> Paths => this.paths.AsSpan(0, this.created);

    /// <summary>Gets the number of flow paths, numbered first: the paths whose state crosses a head.</summary>
    internal int FlowCount { get; private set; }

    /// <summary>Gets a value indicating whether a view decides an access of the body: a case-dependent access (a PairLayer step, a
    /// PairAcquire, an Adapt; <see cref="MirAccess.CaseDependent"/>).</summary>
    internal bool ViewDependent { get; private set; }

    /// <summary>Gets a value indicating whether the body constructs its object in _0 from fresh storage (SPEC 6.2.3).</summary>
    internal bool Constructing { get; private set; }

    /// <summary>Collects the move paths of a body and numbers them.</summary>
    /// <param name="body">The validated body.</param>
    /// <param name="flow">The dataflow, begun with the body.</param>
    internal void Build(MirBody body, MirDataflow flow)
    {
        this.body = body;
        var locals = body.Locals.Length;
        (this.created, this.ViewDependent) = (0, false);
        this.children.Clear();
        this.places.Clear();
        MirDataflow.Grow(ref this.chains, locals);
        MirDataflow.Grow(ref this.live, locals);
        Array.Fill(this.chains, -1, 0, locals);
        for (var i = 0; i < locals; i++)
        {
            var root = this.Add(-1); // Adding grows the arrays first.
            this.once[root] = body.Locals[i] is { Kind: MirLocalKind.Var, Mutable: false };
        }

        this.Constructing = body.Key.Kind == MirBodyKind.Fn && AdtDef.IsConstructor(body.Key.Decl);
        if (this.Constructing)
        {
            var self = body.Locals[0].Type;
            for (var i = 0; i < AdtDef.Count(self); i++)
            {
                var field = this.Child(0, MirProjectionKind.Field, i);
                this.once[field] = AdtDef.PlacedOnce(self, i);
            }

            if (AdtDef.Base(self) is not null)
            {
                this.Child(0, MirProjectionKind.Base, 1);
            }
        }

        var events = new Events(this, flow);
        foreach (var block in flow.Order)
        {
            for (var i = 0; i <= body.Blocks[block].Count; i++)
            {
                events.At(block, i < body.Blocks[block].Count ? body.Statements[body.Blocks[block].Start + i].Kind : MirStatementKind.Assign);
                MirUses.ForEach(body, new(block, i), ref events);
            }
        }

        this.Number(locals);
    }

    /// <summary>Gets the path of a Place of the built body and how the Place is below it.</summary>
    /// <param name="place">A local's Place.</param>
    /// <param name="access"><see cref="Direct"/>, <see cref="Raw"/> or <see cref="NoPath"/>.</param>
    /// <returns>The path's number.</returns>
    internal int Find(MirPlace place, out int access)
    {
        var found = place.Projection == 0 ? place.Root << 2 : this.places[place];
        access = found & 3;
        return this.numbers[found >> 2];
    }

    private int Add(int parent)
    {
        var index = this.created++;
        MirDataflow.Grow(ref this.parents, this.created);
        MirDataflow.Grow(ref this.once, this.created);
        MirDataflow.Grow(ref this.steps, this.created);
        (this.parents[index], this.once[index], this.steps[index]) = (parent, false, default);
        return index;
    }

    // The child of a path by a step, added at its first Place.
    private int Child(int parent, MirProjectionKind kind, int arg)
    {
        ref var child = ref CollectionsMarshal.GetValueRefOrAddDefault(this.children, (parent, kind, arg), out var exists);
        if (!exists)
        {
            child = this.Add(parent);
            this.steps[child] = (kind, arg);
        }

        return child;
    }

    // The path of a Place by creation index << 2 | access, its prefix's first: a Base of depth d is d steps of depth 1, a PairLayer is its
    // prefix's path, and Deref and Index end the path.
    private int Resolve(MirPlace place)
    {
        if (place.Projection == 0)
        {
            return place.Root << 2;
        }

        if (this.places.TryGetValue(place, out var found))
        {
            return found;
        }

        var row = this.body.Projections[place.Projection];
        var prefix = place with { Projection = row.Prefix };
        found = this.Resolve(prefix);
        if ((found & 3) == Direct && row.Kind != MirProjectionKind.PairLayer)
        {
            var path = found >> 2;
            found = row.Kind switch
            {
                MirProjectionKind.Deref => (path << 2) | (this.body.TypeOf(prefix)?.Semantics == SemanticsKind.Raw ? Raw : NoPath),
                MirProjectionKind.Index => (path << 2) | NoPath,
                MirProjectionKind.Base => this.Bases(path, row.Arg) << 2,
                _ => this.Child(path, row.Kind, row.Arg) << 2,
            };
        }

        this.places.Add(place, found);
        return found;
    }

    // The base subobject at a depth, one Base step per depth.
    private int Bases(int path, int depth)
    {
        for (var i = 0; i < depth; i++)
        {
            path = this.Child(path, MirProjectionKind.Base, 1);
        }

        return path;
    }

    // Numbers the paths in pre-order without a stack: subtree sizes in reverse creation order (a child is created after its parent), flow
    // roots then chain-local roots by their sizes, then each child at its parent's cursor.
    private void Number(int locals)
    {
        MirDataflow.Grow(ref this.sizes, this.created);
        MirDataflow.Grow(ref this.numbers, this.created);
        MirDataflow.Grow(ref this.paths, this.created);
        Array.Fill(this.sizes, 1, 0, this.created);
        for (var c = this.created - 1; c >= locals; c--)
        {
            this.sizes[this.parents[c]] += this.sizes[c];
        }

        var next = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            for (var local = 0; local < locals; local++)
            {
                if ((pass == 1) == (this.chains[local] >= 0 && this.live[local]))
                {
                    (this.numbers[local], next) = (next, next + this.sizes[local]);
                }
            }

            this.FlowCount = pass == 0 ? next : this.FlowCount;
        }

        var cursors = this.sizes; // Reused: a node's size is read before it becomes its cursor.
        for (var c = 0; c < this.created; c++)
        {
            var parent = this.parents[c];
            var number = parent < 0 ? this.numbers[c] : cursors[parent];
            var end = number + this.sizes[c];
            if (parent >= 0)
            {
                (this.numbers[c], cursors[parent]) = (number, cursors[parent] + this.sizes[c]);
            }

            this.once[c] |= parent >= 0 && this.once[parent];
            var local = parent < 0 ? c : this.paths[this.numbers[parent]].Local;
            this.paths[number] = new(local, parent < 0 ? -1 : this.numbers[parent], end, this.once[c], this.steps[c].Kind, this.steps[c].Arg);
            cursors[c] = number + 1;
        }
    }

    // The events of a body's locals in reverse postorder (their chain and whether the first is their StorageLive; a StorageDead is none),
    // and its Places' paths.
    private struct Events(MirMoveData data, MirDataflow flow) : IMirUses
    {
        private int chain;
        private MirStatementKind kind;

        public readonly void Operand(in MirOperand operand)
        {
        }

        public readonly void Effect(MirEffect effects, int site)
        {
        }

        public readonly void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            if (place.IsStatic)
            {
                return;
            }

            _ = data.Resolve(place);
            data.ViewDependent |= (access & MirAccess.CaseDependent) != 0;
            ref var chain = ref data.chains[place.Root];
            if (this.kind == MirStatementKind.StorageDead)
            {
                return;
            }

            if (chain == -1)
            {
                data.live[place.Root] = this.kind == MirStatementKind.StorageLive;
            }

            chain = chain == -1 || chain == this.chain ? this.chain : -2;
        }

        // A location of a block; a terminator's kind is Assign.
        internal void At(int block, MirStatementKind kind) => (this.chain, this.kind) = (flow.Head(block), kind);
    }
}
