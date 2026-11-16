// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kimi.Compiler;

// PagedColumn<T> (DECISIONS BU-220): the one store form of Binding's per-module node families (NodeColumns, HirTables.cs).
// - Responsibility: a column indexed by a dense id (a node's SyntaxId in its module) in pages of PageSize slots, carved from one arena
//   at a page's first write; each operation once: Get, Ref, Empty, Grow, Reserve, Enumerate, MoveTo, and Renumbered for a renumbering.
// - Inputs: ids below the count Grow last gave (the module's id count, SyntaxTable.Allocate). Outputs: the rows, by value or by reference.
// - Invariants: default(T) is an absent row, so the absent page reads default; the directory is allocated at the column's first write
//   (a column without rows has none); a directory entry is 0 for an absent page, else the page's offset in the arena plus 1, so the first
//   page (offset 0) is distinct from absent; a carved page never moves within the arena, which starts at its reserved size (Reserve: the
//   library's stated size, KimiLibrarySize), grows by doubling, never past the module's dense size (every page carved), and keeps its
//   capacity through Empty; a renumbering moves each present row into the spare arena, so a kept column's two arenas alternate and
//   allocate again only when the module grew.
// - Prohibitions: no hashing; no store decision or key rule (a family's one accessor decides both); no read or write at or past Grow's
//   count; a reference Ref returns is used before the column's next Ref, since a carve can replace the arena.
internal struct PagedColumn<T>
{
    /// <summary>The slots of a page: the least bytes of the measured page sizes for the node families with library rows (G949 D2b-d §3,
    /// re-measured with N5-1's first families: 8 at their spread, about one row in 15 ids).</summary>
    internal const int PageSize = 8;

    private const int PageShift = 3;

    private int[]? directory;
    private T[]? arena;
    private int used;
    private T[]? spare;
    private int pages;
    private int reserve;

    /// <summary>Gets the slots the column's pages hold.</summary>
    internal readonly int Used => this.used;

    /// <summary>Gets the arena's slots no page holds yet.</summary>
    internal readonly int Free => (this.arena?.Length ?? 0) - this.used;

    /// <summary>Gets a row; an absent row, its page carved or not, reads default.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The row.</returns>
    internal readonly T Get(int id)
        => this.directory is { } directory && (uint)(id >> PageShift) < (uint)directory.Length && directory[id >> PageShift] is var page and not 0
            ? this.arena![page - 1 + (id & (PageSize - 1))] : default!;

    /// <summary>Gets a row for a write, carving its page, cleared, at the page's first write.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The row.</returns>
    internal ref T Ref(int id)
    {
        this.directory ??= new int[this.pages];
        ref var page = ref this.directory[id >> PageShift];
        if (page == 0)
        {
            if (this.arena is null || this.used == this.arena.Length)
            {
                Array.Resize(ref this.arena, Math.Min(Math.Max(Math.Max(PageSize, this.reserve), this.used * 2), this.pages * PageSize));
            }

            this.arena.AsSpan(this.used, PageSize).Clear();
            page = this.used + 1;
            this.used += PageSize;
        }

        return ref this.arena![page - 1 + (id & (PageSize - 1))];
    }

    /// <summary>Makes every row absent, keeping the capacity; a row's references are cleared, so no earlier row's object stays reachable.</summary>
    internal void Empty()
    {
        if (this.directory is { } directory)
        {
            Array.Clear(directory);
        }

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>() && this.arena is { } arena)
        {
            arena.AsSpan(0, this.used).Clear();
        }

        this.used = 0;
    }

    /// <summary>Gives the column room for the ids below a count; its pages stay, and a column without rows allocates nothing.</summary>
    /// <param name="count">The module's id count.</param>
    internal void Grow(int count)
    {
        this.pages = (count + PageSize - 1) >> PageShift;
        if (this.directory is not null)
        {
            Array.Resize(ref this.directory, this.pages);
        }
    }

    /// <summary>Sets the arena's first allocation: a column whose size is stated (the library's, KimiLibrarySize) starts at it.</summary>
    /// <param name="slots">The slots.</param>
    internal void Reserve(int slots) => this.reserve = slots;

    /// <summary>Gets the present rows in ascending id order.</summary>
    /// <returns>The rows.</returns>
    internal readonly Enumerator Enumerate() => new(this.directory, this.arena);

    /// <summary>Moves a present row to another column's id, as a renumbering moves its node.</summary>
    /// <param name="target">The column of the renumbered module.</param>
    /// <param name="from">The row's id here.</param>
    /// <param name="to">Its id in the target.</param>
    internal readonly void MoveTo(ref PagedColumn<T> target, int from, int to)
    {
        var row = this.Get(from);
        if (!IsAbsent(ref row))
        {
            target.Ref(to) = row;
        }
    }

    /// <summary>Gives the empty column a renumbering moves this column's rows into (MoveTo): its arena the spare one, cleared, so no row of
    /// the renumbering before last stays reachable, and this column's arena its spare.</summary>
    /// <param name="count">The renumbered module's id count.</param>
    /// <returns>The column.</returns>
    internal readonly PagedColumn<T> Renumbered(int count)
    {
        var arena = this.spare ?? (this.arena is { } current ? new T[current.Length] : []);
        Array.Clear(arena);
        var column = new PagedColumn<T> { arena = arena, spare = this.arena, reserve = this.reserve };
        column.Grow(count);
        return column;
    }

    // default(T) is all zero bytes, so a row is absent when its bytes are; a comparer would box a struct row.
    private static bool IsAbsent(ref T row) => MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref row), Unsafe.SizeOf<T>()).IndexOfAnyExcept((byte)0) < 0;

    /// <summary>The present rows of a column in ascending id order: its carved pages by page, each page's present rows by slot.</summary>
    internal struct Enumerator(int[]? directory, T[]? arena)
    {
        private int page = -1;
        private int slot = PageSize;
        private int offset;

        /// <summary>Gets the current row.</summary>
        public readonly T Current => arena![this.offset + this.slot];

        /// <summary>Gets the current row's id.</summary>
        internal readonly int Id => (this.page << PageShift) + this.slot;

        /// <summary>Gets this enumerator, so a foreach can take the column's rows.</summary>
        /// <returns>This enumerator.</returns>
        public readonly Enumerator GetEnumerator() => this;

        /// <summary>Moves to the next present row.</summary>
        /// <returns>Whether there is one.</returns>
        public bool MoveNext()
        {
            while (this.NextSlot())
            {
                if (!IsAbsent(ref arena![this.offset + this.slot]))
                {
                    return true;
                }
            }

            return false;
        }

        private bool NextSlot()
        {
            if (++this.slot < PageSize)
            {
                return true;
            }

            while (directory is not null && ++this.page < directory.Length)
            {
                if (directory[this.page] is var start and not 0)
                {
                    (this.offset, this.slot) = (start - 1, 0);
                    return true;
                }
            }

            return false;
        }
    }
}
