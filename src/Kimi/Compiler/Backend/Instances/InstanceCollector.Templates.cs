// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the site templates of the checked set (R5 package K 4.5, 4.7 and 4.8): one per body, the body's sites in location order, each with
// whether a runtime path from the body's entry reaches it, which replaying an item's template resolves; the library segment's kept while
// its library version holds, the program segment's built per program version (PLAN G950) and kept across its runs (PLAN G951).
// Input: a built, validated MirProgram, its segments (LibraryBodies, ProgramBodies) and its library version (LibraryVersion); the Target
// and Need columns of the intrinsic rows; LayoutNeeds and Layout.Indirect (DECISIONS R5-M149).
// Output: each body's sites (none for a body with a finding, MirBody.HasFinding), which Replay reads, and its layout needs past its
// Places' roots' storage (InstanceCollector.Layouts.cs' ListNeeds), which LayOutNeeds reads, in its segment's TemplateStore: the library
// segment's store (stamped with the library version it was built under) below MirProgram.LibraryCount, the program's from it, each by the
// body's ordinal itself (R8-L1 M-B1's split rule; the Back's C-378).
// Sites: a call whose callee may carry a target (a Fn, Requirement or Value callee, an intrinsic whose row names one; a Virtual call none,
// X5) or whose intrinsic lays out its Type argument; a Drop; a case adaptation (an instance's creation, DECISIONS R5-M111); an aggregate;
// an erasure to a Function Type; a function item constant; a cast or binary operation whose Types depend on slots (K 4.7, checked per
// instance); a binary operation whose operator row has a layout need (a raw pointer's displacement, K 4.8).
// Invariants: built before any item is replayed, the library segment's once per library version (Run compares its stamp; the stamp is
// valid for the one program its compilation's builder publishes), the program segment's in build order over the bodies built since the
// version's last walk, which a replay extends to the bodies a lookup built during the run (PLAN G951); a template is a function of its
// body alone: the runtime bit is one MirReach walk without CheckTarget edges, and a template holds locations and the body's Places only,
// no substitution or target.
// Must not: resolve a target, read TyCtxt, an item or the root range, or record anything: the definition-stage capability check (A3b) is
// judged work, run in every run's root loop (InstanceCollector.Capabilities.cs).

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

internal sealed partial class InstanceCollector
{
    private readonly MirReach reach = new();
    private readonly TemplateStore libraryTemplates = new(); // The library segment's templates, kept while its library version holds.
    private readonly TemplateStore programTemplates = new(); // The program segment's, built per program version, kept across its runs.
    private int extent; // The build position up to which the program segment's bodies have templates in the version (PLAN G951).

    // K 4.5 rows 1-9: a call carries a target by its callee: a Fn, Requirement or Value callee, or an intrinsic whose row names one; a
    // Virtual call none, its receiver's VTable holding the entries (X5).
    private static bool Carries(in MirCallee callee) => callee.Kind switch
    {
        MirCalleeKind.Fn or MirCalleeKind.Requirement or MirCalleeKind.Value => true,
        MirCalleeKind.Intrinsic => IntrinsicRows.Of((IntrinsicId)callee.Arg).Target != IntrinsicTarget.None,
        MirCalleeKind.Virtual => false,
        _ => throw new InvalidOperationException($"A call has the callee kind {callee.Kind}."),
    };

    // K 4.5 rows 8 and 11-13, K 4.7 and K 4.8: a statement is a site by its rvalue: a case adaptation, which carries its payload's
    // descriptor where its instance's case creates an object (DECISIONS R5-M111); an aggregate, a by-value use whose Closure environment
    // also carries a target; an erasure to a Function Type; a function item constant; an operation whose Types depend on slots, which the
    // capability table checks per instance; an operator with a layout need (LayoutNeeds; DECISIONS R5-M149); a keyed Storage, whose
    // need its key's Type in each instance decides (LayoutNeeds; PLAN G597), the unkeyed one none.
    private static bool IsSite(MirBody body, in MirStatement statement)
        => statement is { Kind: MirStatementKind.Assign, Rvalue: var rvalue } && rvalue.Kind switch
    {
        MirRvalueKind.Adapt or MirRvalueKind.Aggregate => true,
        MirRvalueKind.Cast when (MirCastKind)rvalue.Op == MirCastKind.Erasure => true,
        MirRvalueKind.Cast => IsOpen(body, rvalue),
        MirRvalueKind.BinaryOp => IsOpen(body, rvalue) ||
            LayoutNeeds.Of((MirBinaryOp)rvalue.Op, OperandType(body, body.Operands[rvalue.Start])) != LayoutNeed.None,
        MirRvalueKind.Storage => rvalue.Count != 0,
        MirRvalueKind.Use => body.Operands[rvalue.Start] is { Kind: MirOperandKind.Const } operand && body.Consts[operand.Arg].Kind == MirConstKind.FnItem,
        _ => false,
    };

    // Whether a block's terminator is a site: a Drop (row 10), a call that may carry a target, or an intrinsic call whose row reads its Type
    // argument (a by-value use; IntrinsicRow.ReadsTypeArgument, the one rule an Item of its function follows too, Bodyless).
    private static bool IsSite(in MirTerminator terminator) => terminator.Kind == MirTerminatorKind.Drop ||
        (terminator is { Kind: MirTerminatorKind.Call, Callee: var callee } &&
            (Carries(callee) || (callee.Kind == MirCalleeKind.Intrinsic && IntrinsicRows.Of((IntrinsicId)callee.Arg).ReadsTypeArgument)));

    // Whether a step of a body's Places may leave its root's storage: a Deref, a PairLayer or an Index (of a sequence handle), the steps whose
    // prefix may be laid out without its component.
    private static bool MayLeave(MirBody body)
    {
        var projections = body.Projections;
        for (var id = 1; id < projections.Length; id++)
        {
            if (projections[id].Kind is MirProjectionKind.Deref or MirProjectionKind.PairLayer or MirProjectionKind.Index)
            {
                return true;
            }
        }

        return false;
    }

    // The templates of the bodies without one: the library segment's once per library version (its store's stamp,
    // MirProgram.LibraryVersion), then the program segment's bodies built since the version's last walk, in build order (Run starts a
    // version at LibraryCount; Replay calls it when a lookup built bodies during the run, PLAN G951).
    private void Templates(MirProgram program)
    {
        if (this.libraryTemplates.Version != program.LibraryVersion)
        {
            this.libraryTemplates.Clear();
            Build(this.libraryTemplates, program.LibraryBodies);
            this.libraryTemplates.Version = program.LibraryVersion;
        }

        Build(this.programTemplates, program.Bodies[this.extent..]);
        this.extent = program.Bodies.Length;

        // Bodies' templates appended to their segment's store, in build order; a body with a finding, which no consumer reads (DECISIONS
        // R5-M183), has an empty range and is not read.
        void Build(TemplateStore store, ReadOnlySpan<MirBody> bodies)
        {
            foreach (var body in bodies)
            {
                store.Begin(body.Ordinal);
                this.Templated++;
                if (!body.HasFinding)
                {
                    this.Template(store, body);
                }

                store.End(body.Ordinal);
            }
        }
    }

    // The store of a body's template: the library segment's below MirProgram.LibraryCount (R8-L1 M-B1's split rule).
    private TemplateStore TemplatesOf(int body) => body < this.program!.LibraryCount ? this.libraryTemplates : this.programTemplates;

    // A body's sites, appended to its store in location order with their runtime bits, and the layout needs of its Places past their
    // roots' storage (K 4.8, ListNeeds) where a step may leave it.
    private void Template(TemplateStore store, MirBody body)
    {
        var blocks = body.Blocks;
        var leaves = MayLeave(body);
        this.reach.Clear(blocks.Length);
        this.reach.Walk(body, 0, checks: false);
        for (var block = 0; block < blocks.Length; block++)
        {
            var start = blocks[block].Start;
            var end = blocks[block].Count;
            for (var index = 0; index <= end; index++)
            {
                if (index < end ? IsSite(body, body.Statements[start + index]) : IsSite(blocks[block].Terminator))
                {
                    store.AddSite(new(block, index), this.reach[block]);
                }

                if (leaves)
                {
                    this.ListNeeds(store, body, new(block, index));
                }
            }
        }
    }

    // A segment's site templates and layout needs (PLAN G950; the Back's C-378), data only, so the library segment's are kept across its
    // compilation's versions while its library version holds: its bodies' sites and needs in body order, each body's first
    // site and first need by the body's ordinal itself (the program's from the library's count, no offset), the next ordinal's entry
    // ending them, and the library version it was built under (0: none; Binding's library publication is never 0).
    private sealed class TemplateStore
    {
        private (MirLocation At, bool Runtime)[] sites = [];
        private (MirLocation At, MirPlace Step, int Pair)[] needs = [];
        private int[] starts = []; // By ordinal, a body's first site.
        private int[] needStarts = []; // By ordinal, a body's first need.
        private int siteCount;
        private int needCount;

        internal ulong Version { get; set; }

        // A body's sites.
        internal ReadOnlySpan<(MirLocation At, bool Runtime)> Sites(int body) => this.sites.AsSpan(this.starts[body]..this.starts[body + 1]);

        // A body's layout needs: the location, the step's Place and the PairLayer step whose case decides it (0: none).
        internal ReadOnlySpan<(MirLocation At, MirPlace Step, int Pair)> Needs(int body) => this.needs.AsSpan(this.needStarts[body]..this.needStarts[body + 1]);

        // Begins a body: its sites and needs are the ones added until its End.
        internal void Begin(int body)
        {
            MirDataflow.Grow(ref this.starts, body + 2);
            MirDataflow.Grow(ref this.needStarts, body + 2);
            (this.starts[body], this.needStarts[body]) = (this.siteCount, this.needCount);
        }

        internal void End(int body) => (this.starts[body + 1], this.needStarts[body + 1]) = (this.siteCount, this.needCount);

        internal void AddSite(MirLocation at, bool runtime)
        {
            if (this.siteCount == this.sites.Length)
            {
                Array.Resize(ref this.sites, Math.Max(256, this.siteCount * 2));
            }

            this.sites[this.siteCount++] = (at, runtime);
        }

        // A need of the body begun, once per location and step.
        internal void AddNeed(int body, MirLocation at, MirPlace step, int pair)
        {
            if (this.needCount > this.needStarts[body] && this.needs[this.needCount - 1] is var last && last.At == at && last.Step == step)
            {
                return;
            }

            if (this.needCount == this.needs.Length)
            {
                Array.Resize(ref this.needs, Math.Max(256, 2 * this.needCount));
            }

            this.needs[this.needCount++] = (at, step, pair);
        }

        // Removes every body's template, keeping the storage and the stamp.
        internal void Clear() => (this.siteCount, this.needCount) = (0, 0);
    }
}
