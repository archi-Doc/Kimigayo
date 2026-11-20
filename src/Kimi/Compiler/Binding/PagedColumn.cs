// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kimi.Compiler;

// PagedColumn<T> (DECISIONS BU-220): the one store form of Binding's per-module node families (NodeColumns, HirTables.cs); a pass or
// edit family's column is its stamped form, StampedColumn<T, TCounter> below.
// - Responsibility: a column indexed by a dense id (a node's SyntaxId in its module) in pages of PageSize slots, carved from one arena
//   at a page's first write; each operation once: Get, Ref, Empty, Grow, Enumerate, MoveTo, and Renumbered and Retire for a
//   renumbering.
// - Inputs: ids below the count Grow last gave (the module's id count, SyntaxTable.Allocate). Outputs: the rows, by value or by reference.
// - Invariants: default(T) is an absent row, so the absent page reads default; the directory is allocated at the column's first write
//   (a column without rows has none); a directory entry is 0 for an absent page, else the page's offset in the arena plus 1, so the first
//   page (offset 0) is distinct from absent; a carved page never moves within the arena, which starts at one page, grows by doubling,
//   never past the module's dense size (every page carved), and keeps its capacity through Empty; a renumbering moves each present row
//   into the spare arena, so a kept column's two arenas alternate and allocate again only when the module grew, and clears the retired
//   one once its moves end (Retire), so no removed row stays reachable.
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
                Array.Resize(ref this.arena, Math.Min(Math.Max(PageSize, this.used * 2), this.pages * PageSize));
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

    /// <summary>Gives the empty column a renumbering moves this column's rows into (MoveTo): its arena the spare one, which Retire left
    /// cleared, and this column's arena its spare.</summary>
    /// <param name="count">The renumbered module's id count.</param>
    /// <returns>The column.</returns>
    internal readonly PagedColumn<T> Renumbered(int count)
    {
        var column = new PagedColumn<T>
        {
            arena = this.spare ?? (this.arena is { } current ? new T[current.Length] : []),
            spare = this.arena,
        };
        column.Grow(count);
        return column;
    }

    /// <summary>Clears the spare arena once a renumbering's moves have ended: the retired arena's rows, those of removed nodes among
    /// them, stay reachable no longer.</summary>
    internal readonly void Retire()
    {
        if (this.spare is { } spare)
        {
            Array.Clear(spare);
        }
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

// KeyedRow (DECISIONS BU-220): the form of a composite column's row for an ordered key (originAtoms', openOrigins'), beside the column.
// - Responsibility: a row's entries in ascending key, then room (null) that doubles when filled; each operation once: Find and Put, each
//   by binary search, so a row of N entries takes about log N probes and about 2N slots in all.
// - Inputs: the row (null when it has none), a key and the entries' key (keyOf: one order for every Find and Put of the row), and the
//   caller's probe counter. Outputs: the entry at a key, or the row with an entry put at its key in place of the one there.
// - Prohibitions: an identity-keyed row (a key with no order, as Documents' SourceDocument) stays scanned by its accessor; no entry is
//   removed; no key rule (the family's accessor decides it).
internal static class KeyedRow
{
    /// <summary>Gets the entry at a key; none, null.</summary>
    /// <typeparam name="T">The row's entry type, a class (null is room).</typeparam>
    /// <typeparam name="TKey">The key type, ordered.</typeparam>
    /// <param name="row">The row.</param>
    /// <param name="key">The key sought.</param>
    /// <param name="keyOf">An entry's key.</param>
    /// <param name="probes">The probe counter.</param>
    /// <returns>The entry at the key.</returns>
    internal static T? Find<T, TKey>(T?[]? row, TKey key, Func<T, TKey> keyOf, ref long probes)
        where T : class
        where TKey : IComparable<TKey>
        => Index(row, Count(row, ref probes), key, keyOf, ref probes) is >= 0 and var i ? row![i] : null;

    /// <summary>Puts an entry at its key, in place of the one there, if any; a full row's room doubles.</summary>
    /// <typeparam name="T">The row's entry type, a class (null is room).</typeparam>
    /// <typeparam name="TKey">The key type, ordered.</typeparam>
    /// <param name="row">The row.</param>
    /// <param name="entry">The entry put.</param>
    /// <param name="keyOf">An entry's key.</param>
    /// <param name="probes">The probe counter.</param>
    internal static void Put<T, TKey>(ref T?[]? row, T entry, Func<T, TKey> keyOf, ref long probes)
        where T : class
        where TKey : IComparable<TKey>
    {
        var count = Count(row, ref probes);
        var i = Index(row, count, keyOf(entry), keyOf, ref probes);
        if (i < 0)
        {
            if (count == (row?.Length ?? 0))
            {
                Array.Resize(ref row, Math.Max(1, count * 2));
            }

            i = ~i;
            Array.Copy(row!, i, row!, i + 1, count - i);
        }

        row![i] = entry;
    }

    // The index of the entry at a key among a row's first count entries, else the bitwise complement of the index it goes at.
    private static int Index<T, TKey>(T?[]? row, int count, TKey key, Func<T, TKey> keyOf, ref long probes)
        where T : class
        where TKey : IComparable<TKey>
    {
        var (low, high) = (0, count - 1);
        while (low <= high)
        {
            probes++;
            var middle = (low + high) >>> 1;
            var order = keyOf(row![middle]!).CompareTo(key);
            if (order == 0)
            {
                return middle;
            }

            (low, high) = order < 0 ? (middle + 1, high) : (low, middle - 1);
        }

        return ~low;
    }

    // A row's entries: its room after them is null.
    private static int Count<T>(T?[]? row, ref long probes)
        where T : class
    {
        var (low, high) = (0, row?.Length ?? 0);
        while (low < high)
        {
            probes++;
            var middle = (low + high) >>> 1;
            (low, high) = row![middle] is null ? (low, middle) : (middle + 1, high);
        }

        return low;
    }
}

// StampedColumn<T, TCounter> (DECISIONS BU-220): a pass or edit family's column, the stamped form of PagedColumn<T>.
// - Responsibility: a column whose rows hold only while its stamp is the current value of the counter its declaration names (TCounter:
//   Binding.PassCounter, which ResetPass advances, or Binding.EditCounter, which only Invalidate advances); each operation once: Get,
//   Ref, Enumerate, Grow and Renumbered, Freeze or Release at the library's freeze (its family's one NodeColumns.Freeze row), and
//   Holds, the one comparison of the stamp with its counter, which Get, Ref and Enumerate each ask.
// - Inputs: ids below the count Grow last gave, and the Binding whose counter the column follows. Outputs: the current rows.
// - Invariants: a stale column reads default at every id and enumerates nothing; its first write after its counter advanced empties it,
//   keeping its capacity, and stamps it; a renumbering hands it over unmoved and emptied, capacity and stamp kept, so no removed node's
//   row stays reachable: SyntaxTable.Number runs at a Bind's start, after the edit's Invalidate and before the pass's ResetPass, so no
//   handed-over row was still to be read.
// - Prohibitions: no other stamp check and no lifetime flag (a column's lifetime is the counter it follows); no row read past Get and
//   Enumerate.
internal struct StampedColumn<T, TCounter>
    where TCounter : IStampCounter
{
    private PagedColumn<T> rows;
    private ulong stamp;

    /// <summary>Gets the slots the column's pages hold.</summary>
    internal readonly int Used => this.rows.Used;

    /// <summary>Gets the arena's slots no page holds yet.</summary>
    internal readonly int Free => this.rows.Free;

    /// <summary>Gets a row; every row of a stale column reads default.</summary>
    /// <param name="id">The id.</param>
    /// <param name="binding">The Binding whose counter the column follows.</param>
    /// <returns>The row.</returns>
    internal readonly T Get(int id, Binding binding) => this.Holds(binding, out _) ? this.rows.Get(id) : default!;

    /// <summary>Gets a row for a write; the column's first write since its counter advanced empties it and stamps it.</summary>
    /// <param name="id">The id.</param>
    /// <param name="binding">The Binding whose counter the column follows.</param>
    /// <returns>The row.</returns>
    internal ref T Ref(int id, Binding binding)
    {
        if (!this.Holds(binding, out var counter))
        {
            this.rows.Empty();
            this.stamp = counter;
        }

        return ref this.rows.Ref(id);
    }

    /// <summary>Gets the current rows in ascending id order; a stale column has none.</summary>
    /// <param name="binding">The Binding whose counter the column follows.</param>
    /// <returns>The rows.</returns>
    internal readonly PagedColumn<T>.Enumerator Enumerate(Binding binding) => (this.Holds(binding, out _) ? this.rows : default).Enumerate();

    /// <summary>Gives the column room for the ids below a count.</summary>
    /// <param name="count">The module's id count.</param>
    internal void Grow(int count) => this.rows.Grow(count);

    /// <summary>Gives the column a renumbering hands over: this one, unmoved and emptied (its capacity and stamp kept), grown to the
    /// renumbered module's ids.</summary>
    /// <param name="count">The renumbered module's id count.</param>
    /// <returns>The column.</returns>
    internal readonly StampedColumn<T, TCounter> Renumbered(int count)
    {
        var column = this;
        column.rows.Empty();
        column.rows.Grow(count);
        return column;
    }

    /// <summary>Freezes the column (PLAN G949 S1-1's freeze of the library's columns): a current column's rows hold from here in every
    /// later pass and edit; a stale one stays stale.</summary>
    /// <param name="binding">The Binding whose counter the column follows.</param>
    internal void Freeze(Binding binding)
    {
        if (this.Holds(binding, out _))
        {
            this.stamp = Binding.Frozen;
        }
    }

    /// <summary>Releases the column at the library's freeze in place of <see cref="Freeze"/>, for a family whose rows are its pass's
    /// working state, read only inside the pass that wrote them (DECISIONS BU-220 as R7-N5 B2a-2 amends it): its rows, arena and
    /// stamp go, so every row reads default at every counter (stamp 0 is current only at a Binding's counter 0, before its first run,
    /// where no row is left either), as a column the library pass never wrote.</summary>
    internal void Release() => this = default;

    // The one comparison of the column's stamp with the current value of its counter (ruling 118), through the one currency rule.
    private readonly bool Holds(Binding binding, out ulong counter) => Binding.Current(this.stamp, counter = TCounter.Of(binding));
}

/// <summary>A Binding counter a stamped column follows (StampedColumn): Binding.PassCounter or Binding.EditCounter.</summary>
internal interface IStampCounter
{
    /// <summary>Gets the counter's current value.</summary>
    /// <param name="binding">The Binding.</param>
    /// <returns>The value.</returns>
    static abstract ulong Of(Binding binding);
}
