// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns drop elaboration (A2; SPEC 16.2, 16.3.2, 15.1.3; DECISIONS R4-M6, R4-M6a) of one body per Semantics case view.
// Input: the state MirInit's checks replay holds before each Drop (the lanes MaybeUninit, MaybeInit and MaybePartial
// over MirMoveData's paths), each state event there and the paths A1's entry initializes (the arguments'; a constructing _0's own bit has
// no subtree event, so no point); the selected view (MirCases). Output: MirProgram.Drops, per Drop its verdict groups (a group equal but
// for its views to the Drop's previous group is merged into it; lookups are by view), the body's flags and their set and clear points,
// the views judged (A1 and A2 completed them), the body's first A1 or A2 problem, an inline
// copy's included (its code, its subject in its location's scope and MirReport.Add's index, -1 for a copy's, which the report drops; both
// set it after Add; the table is exact only without one, R4-M6a, R4-M102, R4-M132), and MovedPlace_Kd through MirReport where a layer
// with its own drop may be incomplete at a runtime Drop, inside an inline default copy with the copy's call as its Inlined fact (R4-M69).
// Elide where the view's Type needs no drop (MirTypes.MayNeedDrop) or nothing is
// initialized on any path; Keep where the whole is initialized on every path or the Place has no Move Path, and in a check-only group
// also where Flag or an incomplete layer with its own drop would be; Flag where it is complete or wholly absent on every path, the flag
// mirroring the path's own bit; Open where a part may be missing: the components without a child path follow the own bit, each child
// path its own verdict. The states are per solve, recorded as Type-free trees; the Type rule is per view. Flags are numbered in path
// order per body, so a subtree's flags are one range.
// Invariant: a body the table marks has an Error problem in MirReport (R6-check IC, MirAnalysisTest): the one its index names, of its code
// at its subject, or for -1 its template's of that code and subject (an inline copy's, R4-M69) or MirReport's InternalInvariant_Kd (a merge
// naming another requirement); generation's gate reads the mark.
// A Drop only check edges reach (SPEC 14.10.3) with a Move Path gets the group of what it would destroy from its assumed paths, by the
// same rules but flagless (check-only code has no flags, R4-M6a), so the instance collector reads it; one without a Move Path has none.
// Must not rewrite MIR, judge a check-only Drop (no problem, no mark), read syntax, or apply a derived-problem rule (MirReport's).

#pragma warning disable SA1402 // The verdict, its rows, the flag points and the table belong to drop elaboration.

using System.Runtime.InteropServices;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

/// <summary>What destroying a Drop's Place, or a part of it, runs under a view class (DECISIONS R4-M6). A layer whose Type declares a drop
/// and that may be incomplete is never Open in an exact body: at a runtime Drop it is MovedPlace_Kd (the row stays Open, the body
/// inexact), in a check-only group Keep. A check-only group has no Flag row: Flag's state reads as Keep there.</summary>
internal enum MirDropVerdict : byte
{
    Keep, // Its drop glue: it is initialized on every path or has no Move Path (in a check-only group, see the summary).
    Elide, // Nothing: its Type needs no drop in the view, or no path initializes it.
    Flag, // Its drop glue where its flag is set: it is complete or wholly absent on every path; runtime groups only.
    Open, // No own drop: a part may be missing; the child rows follow, a component without one destroyed where the own bit is set.
}

/// <summary>A row of a Drop's verdict group under a view class (DECISIONS R4-M6a): the group's first row is the dropped Place (Depth 0),
/// followed under Open by its child paths' rows in pre-order.</summary>
/// <param name="Block">The block whose terminator is the Drop.</param>
/// <param name="Views">The views of the group, bit i for view i.</param>
/// <param name="Verdict">The verdict.</param>
/// <param name="Flag">The flag Flag tests, or under Open the own bit's flag; -1 for none (under Open, the own bit initialized on every
/// path, or a check-only group, which has no flags).</param>
/// <param name="Depth">The depth below the dropped Place.</param>
/// <param name="Step">The step from the parent row's Place: Field, Base (one depth), ConstIndex or Downcast; unused at depth 0.</param>
/// <param name="Arg">The step's argument.</param>
internal readonly record struct MirDropRow(int Block, ulong Views, MirDropVerdict Verdict, int Flag, int Depth, MirProjectionKind Step, int Arg);

/// <summary>A point where drop flags change (DECISIONS R4-M6a): when the location completes (a terminator on its runtime successor edge;
/// Index -1 is the body's entry), the flags [First, First + Count) are set or cleared, in the views given. Flags start clear.</summary>
/// <param name="At">The location.</param>
/// <param name="First">The first flag.</param>
/// <param name="Count">The number of flags.</param>
/// <param name="Set">Whether they are set.</param>
/// <param name="Views">The views, bit i for view i.</param>
internal readonly record struct MirFlagPoint(MirLocation At, int First, int Count, bool Set, ulong Views);

/// <summary>Drop elaboration's output for a program version (DECISIONS R4-M6a), by body ordinal (its index in
/// <see cref="MirProgram.Bodies"/>): the body's verdict rows, ordered by block, so a Drop's groups are contiguous, in view order; its flag
/// points, ordered by block, each view's in location order; its number of flags; the views it judged, those A1 and A2 completed (all
/// of the body's, none past A1's arena limit, R4-M15a); and its first A1 or A2 problem (R4-M102). A body not analyzed has none. A group
/// says what its Drop destroys, the statement every walk of it cites: a Keep or Flag row its Type whole (its drop, then every part); an
/// Open row its child rows, its parts without a row and a base without one, never a drop of its own; an Elide row nothing. A Drop only the
/// checks reach has the group of what it would destroy (SPEC 14.10.3, 8.15.3), flagless, where a layer with its own drop that may be
/// incomplete is a Keep row with no child rows; one without a Move Path has none; A4 runs no destructor for it (R4-M6a). R5 code
/// generation, A4, A5, the instance collector and the dump read it.</summary>
internal sealed class MirDropTable
{
    private readonly List<End> ends = []; // Each closed body's ends, judged views and first problem.

    /// <summary>Gets the rows of the bodies closed, which <see cref="MirDrops"/> writes.</summary>
    internal List<MirDropRow> Rows { get; } = [];

    /// <summary>Gets the flag points of the bodies closed, which <see cref="MirDrops"/> writes.</summary>
    internal List<MirFlagPoint> Points { get; } = [];

    /// <summary>Gets the verdict rows of a body.</summary>
    /// <param name="body">The body's ordinal.</param>
    /// <returns>Its rows, empty for a body not analyzed.</returns>
    internal ReadOnlySpan<MirDropRow> RowsOf(int body)
        => (uint)body < (uint)this.ends.Count ? CollectionsMarshal.AsSpan(this.Rows)[this.Start(body).Rows..this.ends[body].Rows] : default;

    /// <summary>Gets the flag points of a body.</summary>
    /// <param name="body">The body's ordinal.</param>
    /// <returns>Its points, empty for a body not analyzed.</returns>
    internal ReadOnlySpan<MirFlagPoint> PointsOf(int body)
        => (uint)body < (uint)this.ends.Count ? CollectionsMarshal.AsSpan(this.Points)[this.Start(body).Points..this.ends[body].Points] : default;

    /// <summary>Gets the number of drop flags of a body.</summary>
    /// <param name="body">The body's ordinal.</param>
    /// <returns>Its flags, 0 for a body not analyzed.</returns>
    internal int FlagsOf(int body) => (uint)body < (uint)this.ends.Count ? this.ends[body].Flags : 0;

    /// <summary>Gets the views A1 and A2 completed for a body, which A5 checks (R4-M15a): views 0 to the count less one.</summary>
    /// <param name="body">The body's ordinal.</param>
    /// <returns>Its completed views: every view of the body, 0 past A1's arena limit or for a body not analyzed.</returns>
    internal long ViewsOf(int body) => (uint)body < (uint)this.ends.Count ? this.ends[body].Views : 0;

    /// <summary>Gets a body's first A1 or A2 problem, an inline default copy's included (R4-M102): its verdicts are exact only without one
    /// (R4-M6a). A1's arena limit is <c>OwnershipStorageLimit_Kd</c> at the body (the default source), its views 0 (R4-M15a), whose facts
    /// carry its arguments (G213); A3, A4 and A5 set none.</summary>
    /// <param name="body">The body's ordinal.</param>
    /// <returns>The problem's code, its subject (the template's for a copy's, R4-M69) in its location's scope, which locates it out of an
    /// inline default (K 4.9), and its index in the report, -1 for a copy's, which the report drops (R4-M132); null for an exact body or a
    /// body not analyzed.</returns>
    internal (DiagnosticCode Code, SourceInfo Source, int Problem)? Incomplete(int body)
        => (uint)body < (uint)this.ends.Count ? this.ends[body].Incomplete : null;

    /// <summary>Gets a Drop's verdict group under a view, the one lookup of its readers.</summary>
    /// <param name="body">The body's ordinal.</param>
    /// <param name="block">The block whose terminator is the Drop.</param>
    /// <param name="view">The view.</param>
    /// <returns>The index of the group's first row in <see cref="RowsOf"/>, or -1 when the Drop has no row: only the checks reach it and
    /// its Place has no Move Path, or the body was not analyzed.</returns>
    internal int Group(int body, int block, long view)
    {
        var rows = this.RowsOf(body);
        var (low, high) = (0, rows.Length);
        while (low < high)
        {
            var middle = (low + high) >> 1;
            (low, high) = rows[middle].Block < block ? (middle + 1, high) : (low, middle);
        }

        for (var row = low; row < rows.Length && rows[row].Block == block; row++)
        {
            if (rows[row].Depth == 0 && (rows[row].Views & (1UL << (int)view)) != 0)
            {
                return row;
            }
        }

        return -1;
    }

    /// <summary>Gets whether a Drop destroys nothing under a view: its group is Elide (SPEC 16.2.1, 14.10.3), the one statement A5 and the
    /// instance collector read.</summary>
    /// <param name="body">The body's ordinal.</param>
    /// <param name="block">The block whose terminator is the Drop.</param>
    /// <param name="view">The view.</param>
    /// <returns>Whether it destroys nothing; false for a Drop without a row.</returns>
    internal bool Elides(int body, int block, long view)
        => this.Group(body, block, view) is >= 0 and var group && this.RowsOf(body)[group].Verdict == MirDropVerdict.Elide;

    /// <summary>Closes the next body: its rows and points are the ones added since the last body.</summary>
    /// <param name="flags">Its number of flags.</param>
    /// <param name="views">The views judged, from view 0.</param>
    /// <param name="incomplete">Its first A1 or A2 problem, or null.</param>
    internal void Close(int flags, long views, (DiagnosticCode Code, SourceInfo Source, int Problem)? incomplete)
        => this.ends.Add(new(this.Rows.Count, this.Points.Count, flags, views, incomplete));

    /// <summary>Removes every body, keeping the storage.</summary>
    internal void Clear()
    {
        this.Rows.Clear();
        this.Points.Clear();
        this.ends.Clear();
    }

    // Where a closed body's rows and points begin: the previous body's ends.
    private End Start(int body) => body > 0 ? this.ends[body - 1] : default;

    // A closed body's ends in the rows and points, its flags, its judged views and its first A1 or A2 problem.
    private readonly record struct End(int Rows, int Points, int Flags, long Views, (DiagnosticCode Code, SourceInfo Source, int Problem)? Incomplete);
}

/// <summary>Drop elaboration (A2, SPEC 16.2, 16.3.2; DECISIONS R4-M6a): <see cref="Begin"/> starts a body, MirInit's checks replay records
/// each Drop's state tree and reach (<see cref="Record"/>) and each runtime state event
/// (<see cref="Event"/>), <see cref="Judge"/> applies a view's
/// Types to the trees, and <see cref="End"/> numbers the flags and writes the points.</summary>
/// <param name="moves">The move paths, built for the body.</param>
/// <param name="cases">The Semantics case views, the judged view selected.</param>
internal sealed class MirDrops(MirMoveData moves, MirCases cases)
{
    private readonly List<Node> trees = []; // The state trees of the Drops recorded in the current solve, each in pre-order.
    private readonly List<StateEvent> events = [];
    private readonly HashSet<BoundType> walked = new(ReferenceEqualityComparer.Instance); // MayNeedDrop's scratch.
    private int[] last = []; // Each block's last group in the table's rows, -1 without one.
    private bool[] reached = []; // Each recorded Drop's block: whether the runtime edges reach it.
    private bool[] flagged = []; // Each path a judged row tests.
    private int[] before = []; // Each path's flag: the flagged paths before it.
    private BoundType?[] types = []; // The Type of each depth of the tree judged.
    private int[] counts = []; // The block cursors of a counting sort.
    private MirDropRow[] rowOrder = [];
    private MirFlagPoint[] pointOrder = [];
    private MirBody body = null!;
    private MirDropTable table = null!;
    private MirReport report = null!;
    private int firstRow; // The body's first row and point in the table.
    private int firstPoint;
    private long judged; // The body's views judged, from view 0.
    private (DiagnosticCode Code, SourceInfo Source, int Problem)? incomplete; // The body's first A1 or A2 problem.

    /// <summary>Begins a body whose move paths are built.</summary>
    /// <param name="body">The body.</param>
    /// <param name="program">The program, whose drop table and problems the body's verdicts go to.</param>
    internal void Begin(MirBody body, MirProgram program)
    {
        (this.body, this.table, this.report) = (body, program.Drops, program.Problems);
        (this.firstRow, this.firstPoint, this.judged, this.incomplete) = (this.table.Rows.Count, this.table.Points.Count, 0, null);
        this.trees.Clear();
        this.events.Clear();
        MirDataflow.Grow(ref this.last, body.Blocks.Length);
        Array.Fill(this.last, -1, 0, body.Blocks.Length);
        MirDataflow.Grow(ref this.reached, body.Blocks.Length);
        MirDataflow.Grow(ref this.flagged, moves.Paths.Length);
        Array.Clear(this.flagged, 0, moves.Paths.Length);
    }

    /// <summary>Records the state tree of a Drop from the state before it, on the assumed paths for a Drop only the checks reach (SPEC
    /// 14.10.3), and its reach, which <see cref="Judge"/> reads; a Drop without a Move Path has one Keep node where the runtime edges reach
    /// it, else none.</summary>
    /// <param name="state">The state.</param>
    /// <param name="block">The block whose terminator is the Drop.</param>
    /// <param name="path">The dropped Place's path, or -1 when it has none: a static, or a Place through a reference or an index.</param>
    /// <param name="runtime">Whether the runtime edges reach the Drop.</param>
    internal void Record(ulong[] state, int block, int path, bool runtime)
    {
        this.reached[block] = runtime;
        if (path >= 0)
        {
            this.Subtree(state, block, path, 0);
        }
        else if (runtime)
        {
            this.trees.Add(new(block, -1, MirDropVerdict.Keep, -1, 0));
        }
    }

    /// <summary>Records a state event the runtime edges reach: a definition sets, a Move, a Drop, a StorageLive or a StorageDead clears;
    /// the entry (Index -1) sets the paths it initializes.</summary>
    /// <param name="at">The location.</param>
    /// <param name="path">The path, whose subtree the event changes.</param>
    /// <param name="set">Whether it initializes the subtree.</param>
    /// <param name="views">The views of the solve.</param>
    internal void Event(MirLocation at, int path, bool set, ulong views) => this.events.Add(new(at, path, set, views));

    /// <summary>Records an A1 or A2 problem of the body where its rule forms it, after MirReport.Add, which drops an inline default copy's
    /// (R4-M69): the first is the body's <see cref="MirDropTable.Incomplete"/> (R4-M102, R4-M132).</summary>
    /// <param name="code">The problem's code.</param>
    /// <param name="source">Its subject in its location's scope (the default for the body).</param>
    /// <param name="problem">Its index in the report, -1 for a problem the report dropped.</param>
    internal void Incomplete(DiagnosticCode code, in SourceInfo source, int problem) => this.incomplete ??= (code, source, problem);

    /// <summary>Judges the recorded trees under the selected view, the views in order from 0; a body a view decides records its trees again
    /// for the next view.</summary>
    /// <param name="view">The view.</param>
    internal void Judge(long view)
    {
        var next = 0;
        while (next < this.trees.Count)
        {
            next = this.Tree(next, 1UL << (int)view);
        }

        if (moves.ViewDependent)
        {
            this.trees.Clear();
        }

        this.judged++;
    }

    /// <summary>Ends the body: numbers its flags in path order, writes its flag points, orders its rows and points by block and closes its
    /// part of the table with the views judged.</summary>
    internal void End()
    {
        var paths = moves.Paths;
        MirDataflow.Grow(ref this.before, paths.Length + 1);
        this.before[0] = 0;
        for (var path = 0; path < paths.Length; path++)
        {
            this.before[path + 1] = this.before[path] + (this.flagged[path] ? 1 : 0);
        }

        var rows = CollectionsMarshal.AsSpan(this.table.Rows)[this.firstRow..];
        for (var i = 0; i < rows.Length; i++)
        {
            rows[i] = rows[i].Flag < 0 ? rows[i] : rows[i] with { Flag = this.before[rows[i].Flag] };
        }

        foreach (var change in this.events)
        {
            var first = this.before[change.Path];
            var end = this.before[paths[change.Path].End];
            if (end > first)
            {
                this.table.Points.Add(new(change.At, first, end - first, change.Set, change.Views));
            }
        }

        this.ByBlock(rows, ref this.rowOrder, static x => x.Block);
        this.ByBlock(CollectionsMarshal.AsSpan(this.table.Points)[this.firstPoint..], ref this.pointOrder, static x => x.At.Block);
        this.table.Close(this.before[paths.Length], this.judged, this.incomplete);
    }

    /// <summary>Closes the next body's entry for a body with a validator finding (MirBody.HasFinding), which no consumer reads (DECISIONS
    /// R5-M183): an empty entry (no rows or points, no flags, <see cref="MirDropTable.ViewsOf"/> 0), so every later body keeps its own.</summary>
    /// <param name="program">The program, whose drop table the entry goes to.</param>
    internal void Exclude(MirProgram program) => program.Drops.Close(0, 0, null);

    private static bool Any(ulong[] state, int lane, int first, int end) => MirDataflow.Any(state, MirInit.Lanes, lane, first, end);

    // The state verdict of the subtree [path, end).
    private static MirDropVerdict Verdict(ulong[] state, int path, int end)
    {
        if (!Any(state, MirInit.Uninit, path, end))
        {
            return MirDropVerdict.Keep;
        }

        if (!Any(state, MirInit.Init, path, end))
        {
            return MirDropVerdict.Elide;
        }

        return Any(state, MirInit.Partial, path + 1, end) ? MirDropVerdict.Open : MirDropVerdict.Flag;
    }

    // A path's state verdict, and under Open each direct child's: Keep where the subtree is initialized on every path, Elide where nothing of
    // it is on any path, Flag where no part was taken out of it, else Open, with the own bit's flag where that may be uninitialized.
    private void Subtree(ulong[] state, int block, int path, int depth)
    {
        var end = moves.Paths[path].End;
        var verdict = Verdict(state, path, end);
        var own = verdict == MirDropVerdict.Flag || (verdict == MirDropVerdict.Open && Any(state, MirInit.Uninit, path, path + 1));
        this.trees.Add(new(block, path, verdict, own ? path : -1, depth));
        for (var child = path + 1; verdict == MirDropVerdict.Open && child < end; child = moves.Paths[child].End)
        {
            this.Subtree(state, block, child, depth + 1);
        }
    }

    // A Drop's tree under the view: the Types top-down from the Drop's case Type; a node whose Type needs no drop is Elide with its subtree,
    // and an Open layer whose Type declares a drop may be incomplete where that drop would run (SPEC 15.1.3, 16.3.2): MovedPlace_Kd at a
    // runtime Drop. A check-only Drop is read here alone: not judged and flagless (R4-M6a), so Flag's state reads as Keep, no row has a
    // flag, and such a layer is destroyed whole (Keep, its drop included). Only an Open row has child rows. Returns the next tree.
    private int Tree(int first, ulong views)
    {
        var trees = CollectionsMarshal.AsSpan(this.trees);
        var block = trees[first].Block;
        var runtime = this.reached[block];
        var start = this.table.Rows.Count;
        var skipped = int.MaxValue; // The depth of the node whose subtree has no rows.
        ref readonly var drop = ref this.body.Blocks[block];
        var next = first;
        for (; next < trees.Length && (next == first || trees[next].Depth > 0); next++)
        {
            var node = trees[next];
            if (node.Depth > skipped)
            {
                continue;
            }

            var step = node.Path < 0 ? default : moves.Paths[node.Path];
            MirDataflow.Grow(ref this.types, node.Depth + 1);
            var type = this.types[node.Depth] = node.Depth == 0 ? cases.Type(this.body.TypeOf(drop.Terminator.Place)!)
                : this.body.ProjectedType(this.types[node.Depth - 1], step.Kind, step.Arg);
            var state = node.State == MirDropVerdict.Flag && !runtime ? MirDropVerdict.Keep : node.State;
            var verdict = MirTypes.MayNeedDrop(type, this.walked) ? state : MirDropVerdict.Elide;
            if (verdict == MirDropVerdict.Open && AdtDef.Destructor(type!) is not null)
            {
                if (runtime)
                {
                    var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.PossiblyMovedUse);
                    var subject = this.body.Subject(drop.Terminator.Source);
                    var inlined = this.body.Inlining(drop.Terminator.Source.Scope);
                    var problem = this.report.Add(
                        new(DiagnosticCode.MovedPlace_Kd, requirement, subject, this.body.Ordinal, views),
                        inlined < 0 ? default : [new(ProblemFactKind.Inlined, this.body.Scopes[inlined].Source)]);
                    this.Incomplete(DiagnosticCode.MovedPlace_Kd, drop.Terminator.Source, problem);
                }
                else
                {
                    verdict = MirDropVerdict.Keep;
                }
            }

            skipped = verdict == MirDropVerdict.Open ? int.MaxValue : node.Depth;
            var flag = verdict == MirDropVerdict.Elide || !runtime ? -1 : node.Flag;
            if (flag >= 0)
            {
                this.flagged[flag] = true;
            }

            this.table.Rows.Add(new(block, views, verdict, flag, node.Depth, step.Kind, step.Arg));
        }

        this.Merge(block, start);
        return next;
    }

    // A group equal but for its views to the Drop's previous group is merged into it (R4-M6a); lookups are by view (MirDropTable.Group).
    private void Merge(int block, int start)
    {
        var rows = CollectionsMarshal.AsSpan(this.table.Rows);
        var last = this.last[block];
        var count = rows.Length - start;
        var same = last >= 0 && (last + count == start || rows[last + count].Depth == 0);
        for (var i = 0; same && i < count; i++)
        {
            same = rows[last + i] with { Views = 0 } == rows[start + i] with { Views = 0 };
        }

        if (!same)
        {
            this.last[block] = start;
            return;
        }

        for (var i = 0; i < count; i++)
        {
            rows[last + i] = rows[last + i] with { Views = rows[last + i].Views | rows[start + i].Views };
        }

        this.table.Rows.RemoveRange(start, count);
    }

    // Orders the body's rows or points by block, stably (a counting sort), so a Drop's groups and a block's points are contiguous.
    private void ByBlock<T>(Span<T> items, ref T[] order, Func<T, int> block)
    {
        if (items.Length < 2)
        {
            return;
        }

        var blocks = this.body.Blocks.Length;
        MirDataflow.Grow(ref this.counts, blocks + 1);
        Array.Clear(this.counts, 0, blocks + 1);
        foreach (var item in items)
        {
            this.counts[block(item) + 1]++;
        }

        for (var b = 0; b < blocks; b++)
        {
            this.counts[b + 1] += this.counts[b];
        }

        MirDataflow.Grow(ref order, items.Length);
        foreach (var item in items)
        {
            order[this.counts[block(item)]++] = item;
        }

        order.AsSpan(0, items.Length).CopyTo(items);
    }

    // A node of a Drop's state tree: its path (-1: no Move Path), its state verdict, its flag's path and its depth.
    private readonly record struct Node(int Block, int Path, MirDropVerdict State, int Flag, int Depth);

    // A state event: the subtree of the path set or cleared at a location, in the views of the solve.
    private readonly record struct StateEvent(MirLocation At, int Path, bool Set, ulong Views);
}
