// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// A2's statement of what destroying a value destroys, in destruction order (PLAN G702; SPEC 16.3.2, 15.1.3, 8.15.3; DECISIONS R4-M6a,
// R8-M23): a level, the direct parts of a Type as move paths name them in the order MirTypes.LevelPart states (the one statement of
// SPEC 16.3.2's order of a level, a Type fact), composed for both of its uses: a drop group's walk (Destroyed), the level with the
// group's rows, and a level no row governs (Level), a glue function's. A4 reads each destroyed part's Type; the Backend maps each
// step to drop glue (DropGlue.Destroyed, DropGlue.Level) and states no order of its own.
// Input: a row of a body's drop table and the row's Type in the reader's view of it (the view's case Type for A4, an instance's closed
// Type for the Backend), or a Type alone. Output: the steps in destruction order, each part named by its move-path step from its
// level's Type with the Type MirBody.PartType gives it, under its governing row or, without one, its parent's own bit.
// Invariants: a level with rows and the level without rows are one enumeration (Walker.Level over MirTypes.LevelPart), so a group that
// moved nothing out of a level and a glue function's level give the same parts in the same order; no order is stated here: the index
// of a walk is keyed by the part's step and argument, not by its rank, and a part's child row is found by its key (Child); a walk
// reads the rows below its row in two passes, their count and their index (Index: each row under its parent row and its part's key,
// sorted once), then searches the index once per part of an Open level and once per fixed-array level (Children), and reads each
// child row's verdict once: no row is read again per level, at any depth; a part Binding rejected (no Type) has no step; a child row
// of an Open row names a direct part of that row's Type (MirDropTable's contract, which A2's writer meets by construction: the walk
// trusts it and checks nothing).
// Temporary, a need of the Backend's stated in Mir's level: a fixed array without an element is one empty Run in a level no row
// governs. The instance collector requests the element's glue from that Run and code generation reads that target for `[0 of T]`
// (it writes no loop: the Run has no element), as before PLAN G702, so the checked set and generated code are unchanged. It goes
// with the unit that stops requesting and reading that target (a checked-set and generated-code change with its own tests).
// Must not: state the order of a level's parts (MirTypes.LevelPart); decide a verdict (A2's rows state it; this walk and the readers
// of a group's first row, FunctionCodegen.Drops and A5's Elides, read it), whether a part needs a drop (MirTypes.MayNeedDrop, the
// reader's), or a part's glue, position or layout, a declared destructor, a collection's elements or a handle's payload (the Backend's).

#pragma warning disable SA1402 // The step and its kind belong to the walk.

using System.Buffers;

namespace Kimi.Compiler.Mir;

/// <summary>What a step of a destruction walk is (<see cref="MirDropTable.Destroyed"/>, <see cref="MirDropTable.Level"/>).</summary>
internal enum MirDropStepKind : byte
{
    Whole, // A part destroyed whole (its drop, then every part), by its Keep or Flag row, or by its parent's own bit without a row.
    Run, // A fixed array's elements without a row at the positions Arg to Last, each whole under the parent's own bit, in the level's
    // order (MirTypes.LevelPart: a fixed array's elements from the last, so from Last down to Arg).
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

    /// <summary>Gets the rows the walks of this table have read in their passes over the rows below a walked row, the count and the
    /// index, each row once per pass: what the walk's scaling rows bound (no pass per part, per level or per depth). Not counted: the
    /// walker's searches of the sorted index (one per part of an Open level, one per fixed-array level) and its read of each child
    /// row's verdict. Not synchronized: exact only where no walks of the table run concurrently; only those rows
    /// (MirDropTest.AnOpenLevelsChildRowsAreReadInOnePass, NestedOpenLevelsReadOnlyTheirDirectChildRows) read it, no compiler
    /// path.</summary>
    internal long WalkReads { get; private set; }

    /// <summary>Appends the level of a Type no row governs, a glue function's (SPEC 16.3.2): an Open step, each direct part Whole in
    /// destruction order (<see cref="MirTypes.LevelPart"/>), a fixed array's elements one Run of every element, then the End step:
    /// the level of <see cref="Destroyed"/> with no row.</summary>
    /// <param name="type">The Type, an aggregate move paths name the parts of.</param>
    /// <param name="into">The list the steps are appended to.</param>
    internal static void Level(BoundType type, List<MirDropStep> into) => new Walker(default, default, into).Level(type, -1, -1, default, -1);

    /// <summary>Appends the walk of what destroying a value by a row of a body's drop table destroys, in destruction order: a Keep or a
    /// Flag row the value Whole; an Elide row no step; an Open row an Open step, each direct part of its Type by the part's child row
    /// (that row's walk), a part without one Whole under the Open row's own bit, a fixed array's elements without a row as Run steps
    /// between its element rows, then the End step.</summary>
    /// <param name="body">The ordinal of the body whose rows the walk reads.</param>
    /// <param name="row">The row, a group's first or a row below it.</param>
    /// <param name="type">The row's Type in the reader's view.</param>
    /// <param name="into">The list the steps are appended to.</param>
    internal void Destroyed(int body, int row, BoundType type, List<MirDropStep> into)
    {
        var rows = this.RowsOf(body);
        var (count, indexed) = (0, 0);
        while (row + 1 + count < rows.Length && rows[row + 1 + count].Depth > rows[row].Depth)
        {
            count++;
        }

        if (count == 0)
        {
            new Walker(rows, default, into).Step(type, -1, row, default, -1); // No row below: no index (the common Drop).
            return;
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

    // An entry of a walk's index: a child row under its parent row and the key of the part it names (the step's argument; a base,
    // whose argument is a depth, below every other), so a level's entries are one range of the sorted index and a part's is found by
    // its key. The key is no rank: the order of a level is MirTypes.LevelPart's.
    private static Int128 Entry(int parent, MirProjectionKind step, long arg, int child)
        => ((Int128)parent << 64) | ((Int128)(step == MirProjectionKind.Base ? 0 : arg + 1) << 32) | child;

    // Writes the entries of the rows below a row (its child rows and, each after its own, theirs: the group's pre-order), reading each
    // row once; the row after them.
    private static int Index(ReadOnlySpan<MirDropRow> rows, int row, Span<Int128> index, ref int indexed)
    {
        var child = row + 1;
        while (child < rows.Length && rows[child].Depth > rows[row].Depth)
        {
            index[indexed++] = Entry(row, rows[child].Step, rows[child].Arg, child);
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

        // A level, from its Open step to its End: the direct parts of a Type in the order MirTypes.LevelPart states, each by its child
        // row under the level's row (none: a level no row governs, every part Whole); a part its level's Type does not have (a Field
        // or Case Binding rejected) has no step.
        internal void Level(BoundType type, int parent, int row, MirProjectionKind step, long arg)
        {
            var open = MirDropStep.Open(type, parent, row, step, arg);
            into.Add(open);
            var count = MirTypes.LevelCount(type);
            if (type is { Kind: BoundTypeKind.FixedArray, LengthExpression: null })
            {
                this.Elements(type, row, count);
                count = 0;
            }

            for (var rank = 0L; rank < count; rank++)
            {
                var (kind, at) = MirTypes.LevelPart(type, rank);
                if (MirBody.PartType(type, kind, (int)at) is { } part)
                {
                    this.Step(part, row, this.Child(row, kind, at), kind, at);
                }
            }

            into.Add(MirDropStep.End(open));
        }

        // The entries of a level's row in the index, by key: its child rows. A level no row governs has none.
        private Span<Int128> Children(int row)
        {
            var first = ~this.index.BinarySearch<Int128, Int128>(Entry(row, MirProjectionKind.Base, 0, 0)); // Below them: no child row is 0.
            var end = ~this.index.BinarySearch<Int128, Int128>(Entry(row + 1, MirProjectionKind.Base, 0, 0));
            return this.index[first..end];
        }

        // The child row of the part a step names under a level's row, its entry of the index; -1 for a part without a row.
        private int Child(int row, MirProjectionKind step, long arg)
        {
            var key = Entry(row, step, arg, 0);
            var at = ~this.index.BinarySearch<Int128, Int128>(key);
            return at < this.index.Length && this.index[at] >> 32 == key >> 32 ? (int)(this.index[at] & uint.MaxValue) : -1;
        }

        // A fixed array's elements in the level's order, which descends from its first rank's position (MirTypes.LevelPart): each
        // element with a row by its walk, each run of elements without one between them one Run step. The rowed elements are the
        // level's entries by position, taken from the highest. An array without an element is one empty Run (the header's Temporary).
        private void Elements(BoundType type, int row, long count)
        {
            var element = type.Components[0];
            if (count == 0)
            {
                into.Add(MirDropStep.Run(element, row, 0, -1));
                return;
            }

            var children = this.Children(row);
            var end = MirTypes.LevelPart(type, count - 1).Arg - 1; // Below the last position destroyed.
            for (var at = MirTypes.LevelPart(type, 0).Arg; ; at--)
            {
                var stop = children.Length == 0 ? end : (long)((children[^1] >> 32) & uint.MaxValue) - 1; // The run ends above it.
                if (stop != at)
                {
                    into.Add(MirDropStep.Run(element, row, stop + 1, at));
                }

                if (children.Length == 0)
                {
                    return;
                }

                var child = (int)(children[^1] & uint.MaxValue);
                children = children[..^1];
                at = stop;
                this.Step(element, row, child, MirProjectionKind.ConstIndex, at);
            }
        }
    }
}
