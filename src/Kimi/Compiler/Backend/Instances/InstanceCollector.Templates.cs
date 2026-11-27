// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the site templates of the checked set (R5 package K 4.5, 4.7 and 4.8): one per body, the body's sites in location order, each with
// whether a runtime path from the body's entry reaches it, which replaying an item's template resolves; the library store's kept while
// its program's library segment holds, the program store's built per program version (PLAN G950) and kept across its runs (PLAN G951).
// Input: a built, validated and stepped MirProgram, its build-order list (Bodies), its library segment (LibrarySegment) and A2's fact
// (MirDropTable.LibraryHolds); the Target
// and Need columns of the intrinsic rows; LayoutNeeds and Layout.Indirect (DECISIONS R5-M149).
// Output: each body's sites (none for a body with a finding, MirBody.HasFinding), which Replay reads, and its layout needs past its
// Places' roots' storage (InstanceCollector.Layouts.cs' ListNeeds), which LayOutNeeds reads, in its TemplateStore: the library store
// (keyed on the program and the library segment it was built for) for a body A2's library store holds (MirDropTable.LibraryHolds, the one
// per-ordinal fact, read through TemplatesOf; PLAN G951 U2-K), the program store for another, each by the body's ordinal itself (the
// Back's C-378). The library store holds the templates of the bodies A2's library store holds in the key's segment, the library
// list's, and grows within the segment as the list does: a body first demanded in a version is templated into the program store
// there and into the library store at the next version, whose library step holds it, once per segment (PLAN G951 U2-K-b).
// Sites: a call whose callee may carry a target (a Fn, Requirement or Value callee, an intrinsic whose row names one; a Virtual call none,
// X5) or whose intrinsic lays out its Type argument; a Drop; a case adaptation (an instance's creation, DECISIONS R5-M111); an aggregate;
// an erasure to a Function Type; a function item constant; a cast or binary operation whose Types depend on slots (K 4.7, checked per
// instance); a binary operation whose operator row has a layout need (a raw pointer's displacement, K 4.8).
// Invariants: built at each run's start, before it replays an item, in one walk in build order over the bodies built since the version's
// last walk (PLAN G951, G337 U2): a run replays only bodies A2's table holds, which were built before it began (InstanceCollector.cs,
// Round), so every body it replays is templated; a body its store holds is not templated again, so the
// library store's templates are built once per library segment of a program (Run compares the program and its LibrarySegment, so
// another program's or a rebuilt segment's templates are never kept) and the program store's once per program version. MirDropTable.LibraryHolds is stable within a
// version from the analyses' library step, which Run's input (a stepped program) has passed, so a body's store is the same in every
// run of the version; a template is a function of its
// body alone: the runtime bit is one MirReach walk without CheckTarget edges, and a template holds locations and the body's Places only,
// no substitution or target.
// Must not: resolve a target, read TyCtxt, an item or the root range, or record anything: the definition-stage capability check (A3b) is
// judged work, run in every version's root loop (InstanceCollector.Capabilities.cs).

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

internal sealed partial class InstanceCollector
{
    private readonly MirReach reach = new();
    private readonly TemplateStore libraryTemplates = new(); // The library store: kept while its key holds.
    private readonly TemplateStore programTemplates = new(); // The program store: built per program version, kept across its runs.
    private (MirProgram? Program, int Segment) libraryKey; // The program and library segment the library store's templates are of.
    private int extent; // The build position up to which the version's walks have visited the bodies (PLAN G951).

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

    // The templates of the bodies without one, the one walk: the bodies built since the version's last walk, in build order (Run starts
    // a version at position 0; each run walks at its start, PLAN G951, G337 U2), each appended to its store
    // (TemplatesOf) unless the store holds it: the library store keeps its templates while its key is the program and its library
    // segment (MirProgram.LibrarySegment). A body with a finding, which no consumer reads (DECISIONS R5-M183), has an empty range and is
    // not read.
    private void Templates(MirProgram program)
    {
        if (this.libraryKey != (program, program.LibrarySegment))
        {
            this.libraryTemplates.Clear();
            this.libraryKey = (program, program.LibrarySegment);
        }

        for (; this.extent < program.Bodies.Length; this.extent++)
        {
            var body = program.Bodies[this.extent];
            var store = this.TemplatesOf(body.Ordinal);
            if (store.Has(body.Ordinal))
            {
                continue;
            }

            store.Begin(body.Ordinal);
            this.Templated++;
            if (!body.HasFinding)
            {
                this.Template(store, body);
            }

            store.End(body.Ordinal);
        }
    }

    // The store of a body's template: the library's where A2's library store holds the body (MirDropTable.LibraryHolds, the one fact,
    // stable within a version), else the program's.
    private TemplateStore TemplatesOf(int body) => this.program!.Drops.LibraryHolds(body) ? this.libraryTemplates : this.programTemplates;

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

    // A store's site templates and layout needs (PLAN G950; the Back's C-378), data only, so the library store's are kept across its
    // compilation's versions while its key holds: its bodies' sites and needs in the order they were templated, each body's own sites
    // and own needs by the body's ordinal itself (no offset; the ordinals may have holes, PLAN G951 U2-K), and whether it holds a
    // body's template (its mark is the store's generation, which Clear advances).
    private sealed class TemplateStore
    {
        private (MirLocation At, bool Runtime)[] sites = [];
        private (MirLocation At, MirPlace Step, int Pair)[] needs = [];
        private (int Start, int End)[] siteRanges = []; // By ordinal, a body's own sites.
        private (int Start, int End)[] needRanges = []; // By ordinal, a body's own needs.
        private int[] marks = []; // By ordinal, the generation the body's template was ended in.
        private int generation = 1;
        private int siteCount;
        private int needCount;

        // A body's sites.
        internal ReadOnlySpan<(MirLocation At, bool Runtime)> Sites(int body) => this.sites.AsSpan(this.siteRanges[body].Start..this.siteRanges[body].End);

        // A body's layout needs: the location, the step's Place and the PairLayer step whose case decides it (0: none).
        internal ReadOnlySpan<(MirLocation At, MirPlace Step, int Pair)> Needs(int body) => this.needs.AsSpan(this.needRanges[body].Start..this.needRanges[body].End);

        // Whether the store holds a body's template.
        internal bool Has(int body) => (uint)body < (uint)this.marks.Length && this.marks[body] == this.generation;

        // Begins a body: its sites and needs are the ones added until its End.
        internal void Begin(int body)
        {
            MirDataflow.Grow(ref this.siteRanges, body + 1);
            MirDataflow.Grow(ref this.needRanges, body + 1);
            MirDataflow.Grow(ref this.marks, body + 1);
            (this.siteRanges[body], this.needRanges[body]) = ((this.siteCount, this.siteCount), (this.needCount, this.needCount));
        }

        // Ends the body begun: the store holds its template.
        internal void End(int body) => (this.siteRanges[body].End, this.needRanges[body].End, this.marks[body]) = (this.siteCount, this.needCount, this.generation);

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
            if (this.needCount > this.needRanges[body].Start && this.needs[this.needCount - 1] is var last && last.At == at && last.Step == step)
            {
                return;
            }

            if (this.needCount == this.needs.Length)
            {
                Array.Resize(ref this.needs, Math.Max(256, 2 * this.needCount));
            }

            this.needs[this.needCount++] = (at, step, pair);
        }

        // Removes every body's template, keeping the storage.
        internal void Clear() => (this.siteCount, this.needCount, this.generation) = (0, 0, this.generation + 1);
    }
}
