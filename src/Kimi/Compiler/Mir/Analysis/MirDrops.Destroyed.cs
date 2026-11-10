// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// A2's statement of what destroying a value destroys, in destruction order (PLAN G702; SPEC 16.3.2, 15.1.3, 8.15.3; DECISIONS R4-M6a,
// R8-M23): the one statement of a level's order, the direct parts of a Type as move paths name them (SPEC 16.3.2: a struct's Fields in
// reverse declaration order, then its base; a Tuple's elements and a Closure's captures from the last; an enum's Case payloads by
// ordinal; a fixed array's elements from the last), read for both of its uses: a drop group's walk (Destroyed), the level composed with
// the group's rows, and a level no row governs (Level), a glue function's. A4 reads each destroyed part's Type; the Backend maps each
// step to drop glue (DropGlue.Destroyed, DropGlue.Level) and states no order of its own.
// Input: a row of a body's drop table and the row's Type in the reader's view of it (the view's case Type for A4, an instance's closed
// Type for the Backend), or a Type alone. Output: the steps in destruction order, each part named by its move-path step from its
// level's Type with the Type MirBody.PartType gives it, under its governing row or, without one, its parent's own bit.
// Invariants: a level with rows and the level without rows are one enumeration (Walker.Level), so a group that moved nothing out of a
// level and a glue function's level give the same parts in the same order; a walk reads the rows below its row in two passes, their
// count and their index (Index: each row under its parent row and its part's place, sorted once), so a level takes its direct child
// rows as a range of the index (Children) and reads no row: nothing is searched per part or read again per level, at any depth; a part
// Binding rejected (no Type) has no step; a child row of an Open row names a direct part of that row's Type (MirDropTable's contract,
// which A2's writer meets by construction: the walk trusts it and checks nothing).
// Temporary: a fixed array without an element is one empty Run in a level no row governs, a need of the Backend's, not of the order
// (its glue function targets the element's glue and writes a loop that never runs, as before PLAN G702, so generated code is
// unchanged); it goes with the unit that stops generating that target and loop (a generated-code change with its own fixtures).
// Must not: decide a verdict (A2's rows state it; this walk and the readers of a group's first row, FunctionCodegen.Drops and A5's
// Elides, read it), whether a part needs a drop (MirTypes.MayNeedDrop, the reader's), or a part's glue, position or layout, a declared
// destructor, a collection's elements or a handle's payload (the Backend's).

#pragma warning disable SA1402 // The step and its kind belong to the walk.

using System.Buffers;

namespace Kimi.Compiler.Mir;

/// <summary>What a step of a destruction walk is (<see cref="MirDropTable.Destroyed"/>, <see cref="MirDropTable.Level"/>).</summary>
internal enum MirDropStepKind : byte
{
    Whole, // A part destroyed whole (its drop, then every part), by its Keep or Flag row, or by its parent's own bit without a row.
    Run, // A fixed array's elements without a row, from Last down to Arg, each whole under the parent's own bit.
    Open, // A level destroyed part by part, never by a drop of its own: its steps follow, up to its End.
    End, // The end of a level, with its Open step's columns.
}

/// <summary>A step of a destruction walk (<see cref="MirDropTable.Destroyed"/>, <see cref="MirDropTable.Level"/>), built by its kind's
/// factory (<see cref="Whole"/>, <see cref="Run"/>, <see cref="Open"/>, <see cref="End"/>), which takes the columns the kind states.</summary>
/// <param name="Kind">What the step is.</param>
/// <param name="Type">The part's Type (the destroyed value's own at the top), a Run's element Type.</param>
/// <param name="Parent">The row whose own bit a part without a row, or a Run, follows; -1 at the top and in a level no row governs.</param>
/// <param name="Row">The part's governing row, or -1.</param>
/// <param name="Step">The part's move-path step from its level's Type: Field, Base, ConstIndex or Downcast; unused at the top.</param>
/// <param name="Arg">The step's argument; a Run's first position; -1 at the top.</param>
/// <param name="Last">A Run's last position; -1 for any other step.</param>
internal readonly record struct MirDropStep(MirDropStepKind Kind, BoundType Type, int Parent, int Row, MirProjectionKind Step, long Arg, long Last)
{
    /// <summary>A part destroyed whole.</summary>
    /// <param name="type">The part's Type.</param>
    /// <param name="parent">The row whose own bit the part follows without a row of its own; -1 for none.</param>
    /// <param name="row">The part's governing row, or -1.</param>
    /// <param name="step">The part's move-path step from its level's Type.</param>
    /// <param name="arg">The step's argument; -1 at the top.</param>
    /// <returns>The step.</returns>
    internal static MirDropStep Whole(BoundType type, int parent, int row, MirProjectionKind step, long arg)
        => new(MirDropStepKind.Whole, type, parent, row, step, arg, -1);

    /// <summary>A fixed array's elements without a row, from the last position down to the first; a first above the last is no
    /// element.</summary>
    /// <param name="element">The element Type.</param>
    /// <param name="parent">The row whose own bit the elements follow; -1 for none.</param>
    /// <param name="first">The first position.</param>
    /// <param name="last">The last position.</param>
    /// <returns>The step.</returns>
    internal static MirDropStep Run(BoundType element, int parent, long first, long last)
        => new(MirDropStepKind.Run, element, parent, -1, MirProjectionKind.ConstIndex, first, last);

    /// <summary>The start of a level destroyed part by part.</summary>
    /// <param name="type">The level's Type.</param>
    /// <param name="parent">The row of the level it is a part of; -1 for none.</param>
    /// <param name="row">The level's row, an Open row; -1 for a level no row governs.</param>
    /// <param name="step">The level's move-path step from the Type it is a part of.</param>
    /// <param name="arg">The step's argument; -1 at the top.</param>
    /// <returns>The step.</returns>
    internal static MirDropStep Open(BoundType type, int parent, int row, MirProjectionKind step, long arg)
        => new(MirDropStepKind.Open, type, parent, row, step, arg, -1);

    /// <summary>The end of a level.</summary>
    /// <param name="open">The level's Open step.</param>
    /// <returns>The step, with the Open step's columns.</returns>
    internal static MirDropStep End(in MirDropStep open) => open with { Kind = MirDropStepKind.End };
}

/// <content>The destruction walk.</content>
internal sealed partial class MirDropTable
{
    private const int Inline = 32; // The rows below a walked row indexed on the stack; a larger group rents.

    /// <summary>Gets the rows the walks of this table have read, each once per pass over the rows below a walked row (their count,
    /// then their index): the measure the walk's scaling rows bound (no row is read per part or again per level). Not synchronized:
    /// exact only where no walks of the table run concurrently; only those rows (MirDropTest.AnOpenLevelsChildRowsAreReadInOnePass,
    /// NestedOpenLevelsReadOnlyTheirDirectChildRows) read it, no compiler path.</summary>
    internal long WalkReads { get; private set; }

    /// <summary>Appends the level of a Type no row governs, a glue function's (SPEC 16.3.2): an Open step, each direct part Whole in
    /// destruction order, a fixed array's elements one Run of every element, then the End step: the level of
    /// <see cref="Destroyed"/> with no row.</summary>
    /// <param name="type">The Type, an aggregate move paths name the parts of.</param>
    /// <param name="into">The list the steps are appended to.</param>
    internal static void Level(BoundType type, List<MirDropStep> into) => new Walker(default, default, into).Level(type, -1, -1, default, -1);

    /// <summary>Appends the walk of what destroying a value by a row of a body's drop table destroys, in destruction order: a Keep or a
    /// Flag row the value Whole; an Elide row no step; an Open row an Open step, each direct part of its Type by the part's child row
    /// (that row's walk), a part without one Whole under the Open row's own bit, a fixed array's elements without a row as Run steps
    /// between its element rows, then the End step.</summary>
    /// <param name="body">The body whose rows the walk reads.</param>
    /// <param name="row">The row, a group's first or a row below it.</param>
    /// <param name="type">The row's Type in the reader's view.</param>
    /// <param name="into">The list the steps are appended to.</param>
    internal void Destroyed(MirBody body, int row, BoundType type, List<MirDropStep> into)
    {
        var rows = this.RowsOf(body.Ordinal);
        var (count, indexed) = (0, 0);
        while (row + 1 + count < rows.Length && rows[row + 1 + count].Depth > rows[row].Depth)
        {
            count++;
        }

        var rented = count > Inline ? ArrayPool<Int128>.Shared.Rent(count) : null;
        Span<Int128> index = rented is null ? stackalloc Int128[Inline] : rented;
        index = index[..count];
        _ = Index(rows, row, index, ref indexed);
        index.Sort();
        this.WalkReads += count + indexed;
        new Walker(rows, index, into).Step(type, -1, row, default, -1);
        if (rented is not null)
        {
            ArrayPool<Int128>.Shared.Return(rented);
        }
    }

    // An entry of a walk's index: a child row under its parent row and its part's place in the parent's level, so the entries sort by
    // level, then by place.
    private static Int128 Entry(int parent, long place, int child) => ((Int128)parent << 64) | ((Int128)(place + 1) << 32) | child;

    // The place and the child row of an entry.
    private static (long Place, int Row) Read(Int128 entry) => ((long)((entry >> 32) & uint.MaxValue) - 1, (int)(entry & uint.MaxValue));

    // A part's place in its level, higher the earlier it is destroyed: a struct's base below its Fields, a Field, an element or a
    // capture at its index, a Case's payload from the highest by its ordinal.
    private static long Place(MirProjectionKind step, long arg)
        => step == MirProjectionKind.Base ? -1 : step == MirProjectionKind.Downcast ? int.MaxValue - arg : arg;

    // Writes the entries of the rows below a row (its child rows and, each after its own, theirs: the group's pre-order), reading each
    // row once; the row after them.
    private static int Index(ReadOnlySpan<MirDropRow> rows, int row, Span<Int128> index, ref int indexed)
    {
        var child = row + 1;
        while (child < rows.Length && rows[child].Depth > rows[row].Depth)
        {
            index[indexed++] = Entry(row, Place(rows[child].Step, rows[child].Arg), child);
            child = Index(rows, child, index, ref indexed);
        }

        return child;
    }

    // A walk: the rows of the body and the index of those below the walked row (none for a level no row governs) and the steps it
    // appends, which every step shares.
    private readonly ref struct Walker(ReadOnlySpan<MirDropRow> rows, Span<Int128> index, List<MirDropStep> into)
    {
        private readonly ReadOnlySpan<MirDropRow> rows = rows;
        private readonly Span<Int128> index = index;

        // A part's walk by its governing row under its parent: an Open row's level, nothing for an Elide row, else the part whole.
        internal void Step(BoundType type, int parent, int row, MirProjectionKind step, long arg)
        {
            var verdict = row < 0 ? MirDropVerdict.Keep : this.rows[row].Verdict;
            if (verdict == MirDropVerdict.Open)
            {
                this.Level(type, parent, row, step, arg);
            }
            else if (verdict != MirDropVerdict.Elide)
            {
                into.Add(MirDropStep.Whole(type, parent, row, step, arg));
            }
        }

        // SPEC 16.3.2, the one statement of a level's order: from its Open step to its End, the direct parts of a Type as move paths
        // name them, in destruction order, each by its child row under the level's row (none: a level no row governs, every part Whole).
        // The child rows are the level's range of the index (Children), taken in the order the parts are named (Child).
        internal void Level(BoundType type, int parent, int row, MirProjectionKind step, long arg)
        {
            var open = MirDropStep.Open(type, parent, row, step, arg);
            into.Add(open);
            var children = this.Children(row);
            if (type.Kind is BoundTypeKind.Tuple or BoundTypeKind.Closure)
            {
                this.Fields(type, row, type.Components.Count, ref children);
            }
            else if (type is { Kind: BoundTypeKind.FixedArray, LengthExpression: null })
            {
                this.Elements(type, row, ref children);
            }
            else if (AdtDef.CaseTypes(type) is { } payloads)
            {
                for (var ordinal = 0; ordinal < payloads.Length; ordinal++)
                {
                    this.Part(type, row, MirProjectionKind.Downcast, ordinal, ref children);
                }
            }
            else
            {
                this.Fields(type, row, AdtDef.IsStruct(type) ? AdtDef.Count(type) : 0, ref children);
                this.Part(type, row, MirProjectionKind.Base, 1, ref children);
            }

            into.Add(MirDropStep.End(open));
        }

        // The child row of the part at a place, the highest left of its level's (the parts are named from the highest), then taken; -1
        // for a part without a row.
        private static int Child(ref Span<Int128> children, long place)
        {
            if (children.Length == 0 || Read(children[^1]).Place != place)
            {
                return -1;
            }

            var row = Read(children[^1]).Row;
            children = children[..^1];
            return row;
        }

        // The child rows of a level's row, by place: the entries of the index under that parent row, found by its order, no row read.
        // A level no row governs has none.
        private Span<Int128> Children(int row)
        {
            var first = ~this.index.BinarySearch<Int128, Int128>(Entry(row, -1, 0)); // Below every entry of the row: a child row is not 0.
            var end = ~this.index.BinarySearch<Int128, Int128>(Entry(row + 1, -1, 0));
            return this.index[first..end];
        }

        // A struct's Fields, a Tuple's elements or a Closure's captures, the last first.
        private void Fields(BoundType type, int row, int count, ref Span<Int128> children)
        {
            for (var i = count - 1; i >= 0; i--)
            {
                this.Part(type, row, MirProjectionKind.Field, i, ref children);
            }
        }

        // A direct part by its child row; a part its level's Type does not have (no base, a Field or Case Binding rejected) has no step.
        private void Part(BoundType type, int row, MirProjectionKind step, int arg, ref Span<Int128> children)
        {
            var child = Child(ref children, Place(step, arg));
            if (MirBody.PartType(type, step, arg) is { } part)
            {
                this.Step(part, row, child, step, arg);
            }
        }

        // A fixed array's elements from the last: each run of elements without a row one Run step, each element with a row by its walk
        // (an element's place is its position). An array without an element is one empty Run (the header's Temporary).
        private void Elements(BoundType type, int row, ref Span<Int128> children)
        {
            var element = type.Components[0];
            for (long last = type.Length - 1, first; ; last = first - 1)
            {
                var child = children.Length == 0 ? -1 : Read(children[^1]).Row;
                first = child < 0 ? 0 : Read(children[^1]).Place + 1;
                if (first <= last || type.Length == 0)
                {
                    into.Add(MirDropStep.Run(element, row, first, last));
                }

                if (child < 0)
                {
                    return;
                }

                children = children[..^1];
                this.Step(element, row, child, MirProjectionKind.ConstIndex, --first);
            }
        }
    }
}
