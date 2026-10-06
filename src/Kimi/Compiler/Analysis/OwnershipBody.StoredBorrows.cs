// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// SPEC 15.6.4: a dependency that a store into a holder creates, such as a Dictionary entry or a referent a call retains through
// its public contract, exists only after that store. A recorded (holder, root) dependency is active at each operation that one
// of its stores reaches without passing a redefinition of the holder, which drops the stored value, and at each operation that a
// transfer reaches once the transfer's source can hold the dependency there. A transferred dependency arrives with the
// definition that carries it, such as a call's result or a borrow, so a transfer is ordered after its source but no definition
// ends it. A loop's back edge reaches earlier operations, so source order never decides. A dependency without a record exists
// everywhere.
public sealed partial class OwnershipBody
{
    private const int StoredEverywhere = -2;

    // Created by the first body that stores a dependency and reused by the next analyses of this body.
    private StoredBorrowTable? storedBorrows;

    private void ClearStoredBorrows() => this.storedBorrows?.Clear();

    private bool HasStoredBorrowRecord(int place, int root)
        => this.storedBorrows is { } table && table.Keys.ContainsKey((place, root));

    // Whether the holder depends on root only after some store, not everywhere.
    private bool IsStoredBorrow(int place, int root)
        => this.storedBorrows is { } table && table.Keys.TryGetValue((place, root), out var key) && table.Heads[key] != StoredEverywhere;

    // Records a store after which the holder depends on root; returns whether the record changed.
    private bool AddStoredBorrowStart(int place, int root, int operation)
    {
        var table = this.storedBorrows ??= new();
        var key = table.Key(place, root);
        var head = table.Heads[key];
        if (head == StoredEverywhere)
        {
            return false;
        }

        for (var link = head; link >= 0; link = table.Links[link].Next)
        {
            if (table.Links[link].Operation == operation)
            {
                return false;
            }
        }

        table.Links.Add((operation, head));
        table.Heads[key] = table.Links.Count - 1;
        return true;
    }

    // Records that the destination takes the source's stored dependency on root at the operation; returns whether it is new.
    private bool AddStoredBorrowTransfer(int source, int destination, int root, int operation)
    {
        var table = this.storedBorrows ??= new();
        var from = table.Key(source, root);
        var to = table.Key(destination, root);
        if (table.Heads[to] == StoredEverywhere)
        {
            return false;
        }

        for (var i = 0; i < table.Transfers.Count; i++)
        {
            if (table.Transfers[i] is var transfer && transfer.Source == from && transfer.Operation == operation && transfer.Destination == to)
            {
                return false;
            }
        }

        table.Transfers.Add((from, operation, to, false));
        return true;
    }

    // A recorded dependency that also arrives from a holder depending on root everywhere exists everywhere.
    private bool SetStoredBorrowEverywhere(int place, int root)
    {
        var table = this.storedBorrows ??= new();
        var key = table.Key(place, root);
        if (table.Heads[key] == StoredEverywhere)
        {
            return false;
        }

        table.Heads[key] = StoredEverywhere;
        return true;
    }

    // Whether a recorded dependency exists before the operation; true when the dependency has no record.
    private bool StoredBorrowActive(int place, int root, int operation)
        => this.storedBorrows is not { } table || !table.Keys.TryGetValue((place, root), out var key) || table.Active(key, operation);

    // Spreads each record from its stores up to a redefinition of the holder, then, until nothing changes, from each transfer
    // whose source is active there. A store or transfer is itself active only when a later execution reaches it again.
    private void PrepareStoredBorrowActivity()
    {
        if (this.storedBorrows is not { Heads.Count: > 0 } table)
        {
            return;
        }

        var keys = table.Heads.Count;
        var operations = this.Operations.Count;
        var words = table.Words = (operations + 63) >> 6;
        Grow(ref table.Activity, OwnershipStorage.Cells(keys, words, 64, "stored borrow activity"));
        table.Activity.AsSpan(0, keys * words).Clear();
        Grow(ref table.Queue, operations);
        for (var key = 0; key < keys; key++)
        {
            for (var link = table.Heads[key]; link >= 0; link = table.Links[link].Next)
            {
                this.SpreadStoredBorrow(table, key, table.Links[link].Operation, table.Places[key]);
            }
        }

        bool changed;
        do
        {
            changed = false;
            for (var i = 0; i < table.Transfers.Count; i++)
            {
                var (source, operation, destination, applied) = table.Transfers[i];
                if (!applied && (table.Active(source, operation) || table.EstablishedAt(source, operation)))
                {
                    table.Transfers[i] = (source, operation, destination, true);
                    this.SpreadStoredBorrow(table, destination, operation, -1);
                    changed = true;
                }
            }
        }
        while (changed);
    }

    // Marks the operations after start in the record's row, stopping at a redefinition of the holder, if any.
    private void SpreadStoredBorrow(StoredBorrowTable table, int key, int start, int holder)
    {
        if (table.Heads[key] == StoredEverywhere)
        {
            return;
        }

        var active = table.Activity.AsSpan(key * table.Words, table.Words);
        var queued = this.QueueStoredBorrowSuccessors(table, start, active, 0);
        while (queued > 0)
        {
            var operation = table.Queue[--queued];
            if (holder < 0 || !DefinesBorrowHolder(this.Operations[operation], holder))
            {
                queued = this.QueueStoredBorrowSuccessors(table, operation, active, queued);
            }
        }
    }

    private int QueueStoredBorrowSuccessors(StoredBorrowTable table, int operation, Span<ulong> active, int queued)
    {
        for (var e = this.EdgeHeads[operation]; e >= 0; e = this.Edges[e].Next)
        {
            if (this.Edges[e].Kind != OwnershipEdgeKind.Abort)
            {
                queued = table.Enqueue(this.Edges[e].To, active, queued);
            }
        }

        for (var e = this.checkingBorrowHeads[operation]; e >= 0; e = this.checkingBorrowEdges[e].Next)
        {
            queued = table.Enqueue(this.checkingBorrowEdges[e].To, active, queued);
        }

        return queued;
    }

    private sealed class StoredBorrowTable
    {
#pragma warning disable SA1401 // The owning body fills the storage directly.
        internal readonly Dictionary<(int Place, int Root), int> Keys = new();

        // Per record: its holder, and the first of its stores (or StoredEverywhere) linked through Links.
        internal readonly List<int> Places = new();
        internal readonly List<int> Heads = new();
        internal readonly List<(int Operation, int Next)> Links = new();
        internal readonly List<(int Source, int Operation, int Destination, bool Applied)> Transfers = new();

        // Per record, one bit per operation where it is active.
        internal ulong[] Activity = [];
        internal int[] Queue = [];
        internal int Words;
#pragma warning restore SA1401

        internal void Clear()
        {
            this.Keys.Clear();
            this.Places.Clear();
            this.Heads.Clear();
            this.Links.Clear();
            this.Transfers.Clear();
        }

        internal int Key(int place, int root)
        {
            if (!this.Keys.TryGetValue((place, root), out var key))
            {
                key = this.Heads.Count;
                this.Keys.Add((place, root), key);
                this.Places.Add(place);
                this.Heads.Add(-1);
            }

            return key;
        }

        internal bool Active(int key, int operation)
            => this.Heads[key] == StoredEverywhere || (this.Activity[(key * this.Words) + (operation >> 6)] & (1UL << operation)) != 0;

        // Whether a store or an applied transfer at the operation itself establishes the record, as when one call retains a
        // referent in an argument and returns a value depending on it.
        internal bool EstablishedAt(int key, int operation)
        {
            for (var link = this.Heads[key]; link >= 0; link = this.Links[link].Next)
            {
                if (this.Links[link].Operation == operation)
                {
                    return true;
                }
            }

            for (var i = 0; i < this.Transfers.Count; i++)
            {
                if (this.Transfers[i] is { Applied: true } transfer && transfer.Destination == key && transfer.Operation == operation)
                {
                    return true;
                }
            }

            return false;
        }

        internal int Enqueue(int target, Span<ulong> active, int queued)
        {
            ref var word = ref active[target >> 6];
            if ((word & (1UL << target)) == 0)
            {
                word |= 1UL << target;
                this.Queue[queued++] = target;
            }

            return queued;
        }
    }
}
