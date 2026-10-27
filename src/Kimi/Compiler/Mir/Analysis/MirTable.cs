// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the pooled analysis tables: tables of int values by an unmanaged key that a Mir/Analysis owner empties per view or per body and
// fills again, read and filled only by lookup and insertion (V16; DECISIONS R4-M146), for example the Loan sets' nodes and memos (A5), the
// Move paths' children and Places (A1, A2, A5) and A5's addresses (MirBorrows.Addresses). A pooled table of another contract is PLAN G136's.
// Input: the keys and the values its owner adds. Output: a key's value, an entry by reference, and Clear, which empties the table in
// proportion to its entries; Cleared, the slots the last Clear wrote.
// Invariants: Clear removes the entries one by one while they fill less than a 64th of the storage, else clears the storage whole (its
// buckets, and its entries up to their high-water count, at most as many): a removal costs about 100 bucket clears (R4-M146), so a 64th
// keeps either path within 64 slots per entry. The keys are listed while the table is sparse, the only state whose Clear reads them:
// between two Clears the entries only grow, and a resize leaves the table at least about 2/5 full, more than a 64th, so a table found
// sparse at Clear had no resize since the last one and was sparse at every insertion. That rests on Dictionary's growth, the assumption
// this table makes: the storage changes only by a resize, when every entry slot is used, to about twice as many. The storage only grows,
// so a warm run of a sized workload allocates nothing.
// Must not: remove a single entry.

using System.Runtime.InteropServices;

namespace Kimi.Compiler.Mir;

/// <summary>A pooled analysis table of int values whose <see cref="Clear"/> costs what its entries cost (V16, DECISIONS R4-M146).</summary>
/// <typeparam name="TKey">The key, a plain value without references.</typeparam>
/// <param name="comparer">The key comparer, or null for the default one.</param>
internal sealed class MirTable<TKey>(IEqualityComparer<TKey>? comparer = null)
    where TKey : unmanaged
{
    private readonly Dictionary<TKey, int> entries = new(comparer);
    private TKey[] keys = []; // The entries' keys in the order they were added, while the table is sparse.
    private int listed; // The entries a sparse table may hold since the last Clear: a 64th of its storage then, less one.

    /// <summary>Gets the slots the last <see cref="Clear"/> wrote: the entries it removed, or the storage's buckets when it cleared it
    /// whole (its entries up to their high-water count, at most as many, besides).</summary>
    internal long Cleared { get; private set; }

    /// <summary>Gets the value of a key the table holds.</summary>
    /// <param name="key">The key.</param>
    /// <returns>Its value.</returns>
    internal int this[TKey key] => this.entries[key];

    /// <summary>Gets the value of a key, if the table holds it.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">Its value, or 0.</param>
    /// <returns>Whether the table holds the key.</returns>
    internal bool TryGetValue(TKey key, out int value) => this.entries.TryGetValue(key, out value);

    /// <summary>Adds a key the table does not hold.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">Its value.</param>
    internal void Add(TKey key, int value)
    {
        this.entries.Add(key, value);
        this.List(key);
    }

    /// <summary>Gets the entry of a key by reference, added with the value 0 when the table does not hold it.</summary>
    /// <param name="key">The key.</param>
    /// <param name="exists">Whether the table held it.</param>
    /// <returns>The entry's value.</returns>
    internal ref int Entry(TKey key, out bool exists)
    {
        ref var value = ref CollectionsMarshal.GetValueRefOrAddDefault(this.entries, key, out exists);
        if (!exists)
        {
            this.List(key);
        }

        return ref value;
    }

    /// <summary>Removes every entry, keeping the storage: one by one while they fill less than a 64th of it, else the storage whole.</summary>
    internal void Clear()
    {
        var (count, capacity) = (this.entries.Count, this.entries.EnsureCapacity(0));
        if (count * 64L >= capacity)
        {
            this.entries.Clear();
            this.Cleared = capacity;
        }
        else
        {
            foreach (var key in this.keys.AsSpan(0, count))
            {
                this.entries.Remove(key);
            }

            this.Cleared = count;
        }

        this.listed = (capacity - 1) / 64; // The storage changes only by a resize, after which the table is dense.
    }

    private void List(TKey key)
    {
        var count = this.entries.Count;
        if (count <= this.listed)
        {
            MirDataflow.Grow(ref this.keys, count);
            this.keys[count - 1] = key;
        }
    }
}
