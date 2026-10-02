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
    private Dictionary<(int Place, int Root), int>? storedBorrowKeys;
    private List<int>? storedBorrowHeads;
    private List<(int Operation, int Next)>? storedBorrowLinks;
    private List<(int Source, int Operation, int Destination, bool Applied)>? storedBorrowTransfers;
    private List<int>? storedBorrowPlaces;
    private ulong[] storedBorrowActive = [];
    private int[] storedBorrowQueue = [];
    private int storedBorrowWords;

    private void ClearStoredBorrows()
    {
        this.storedBorrowKeys?.Clear();
        this.storedBorrowHeads?.Clear();
        this.storedBorrowLinks?.Clear();
        this.storedBorrowTransfers?.Clear();
        this.storedBorrowPlaces?.Clear();
    }

    private bool HasStoredBorrowRecord(int place, int root)
        => this.storedBorrowKeys is { } keys && keys.ContainsKey((place, root));

    // Whether the holder depends on root only after some store, not everywhere.
    private bool IsStoredBorrow(int place, int root)
        => this.storedBorrowKeys is { } keys && keys.TryGetValue((place, root), out var key) && this.storedBorrowHeads![key] != StoredEverywhere;

    // Records a store after which the holder depends on root; returns whether the record changed.
    private bool AddStoredBorrowStart(int place, int root, int operation)
    {
        var key = this.StoredBorrowKey(place, root);
        var head = this.storedBorrowHeads![key];
        if (head == StoredEverywhere)
        {
            return false;
        }

        for (var link = head; link >= 0; link = this.storedBorrowLinks![link].Next)
        {
            if (this.storedBorrowLinks![link].Operation == operation)
            {
                return false;
            }
        }

        (this.storedBorrowLinks ??= new()).Add((operation, head));
        this.storedBorrowHeads[key] = this.storedBorrowLinks.Count - 1;
        return true;
    }

    // Records that the destination takes the source's stored dependency on root at the operation; returns whether it is new.
    private bool AddStoredBorrowTransfer(int source, int destination, int root, int operation)
    {
        var from = this.StoredBorrowKey(source, root);
        var to = this.StoredBorrowKey(destination, root);
        if (this.storedBorrowHeads![to] == StoredEverywhere)
        {
            return false;
        }

        var transfers = this.storedBorrowTransfers ??= new();
        for (var i = 0; i < transfers.Count; i++)
        {
            if (transfers[i] is var transfer && transfer.Source == from && transfer.Operation == operation && transfer.Destination == to)
            {
                return false;
            }
        }

        transfers.Add((from, operation, to, false));
        return true;
    }

    // A recorded dependency that also arrives from a holder depending on root everywhere exists everywhere.
    private bool SetStoredBorrowEverywhere(int place, int root)
    {
        var key = this.StoredBorrowKey(place, root);
        if (this.storedBorrowHeads![key] == StoredEverywhere)
        {
            return false;
        }

        this.storedBorrowHeads[key] = StoredEverywhere;
        return true;
    }

    private int StoredBorrowKey(int place, int root)
    {
        var keys = this.storedBorrowKeys ??= new();
        var heads = this.storedBorrowHeads ??= new();
        if (!keys.TryGetValue((place, root), out var key))
        {
            key = heads.Count;
            keys.Add((place, root), key);
            heads.Add(-1);
            (this.storedBorrowPlaces ??= new()).Add(place);
        }

        return key;
    }

    // Whether a recorded dependency exists before the operation; true when the dependency has no record.
    private bool StoredBorrowActive(int place, int root, int operation)
        => this.storedBorrowKeys is not { } keys || !keys.TryGetValue((place, root), out var key) || this.StoredBorrowActive(key, operation);

    private bool StoredBorrowActive(int key, int operation)
        => this.storedBorrowHeads![key] == StoredEverywhere ||
            (this.storedBorrowActive[(key * this.storedBorrowWords) + (operation >> 6)] & (1UL << operation)) != 0;

    // Spreads each record from its stores up to a redefinition of the holder, then, until nothing changes, from each transfer
    // whose source is active there. A store or transfer is itself active only when a later execution reaches it again.
    private void PrepareStoredBorrowActivity()
    {
        var keys = this.storedBorrowHeads?.Count ?? 0;
        if (keys == 0)
        {
            return;
        }

        var operations = this.Operations.Count;
        var words = this.storedBorrowWords = (operations + 63) >> 6;
        Grow(ref this.storedBorrowActive, OwnershipStorage.Cells(keys, words, 64, "stored borrow activity"));
        this.storedBorrowActive.AsSpan(0, keys * words).Clear();
        Grow(ref this.storedBorrowQueue, operations);
        for (var key = 0; key < keys; key++)
        {
            for (var link = this.storedBorrowHeads![key]; link >= 0; link = this.storedBorrowLinks![link].Next)
            {
                this.SpreadStoredBorrow(key, this.storedBorrowLinks![link].Operation, this.storedBorrowPlaces![key]);
            }
        }

        var transfers = this.storedBorrowTransfers;
        bool changed;
        do
        {
            changed = false;
            for (var i = 0; transfers is not null && i < transfers.Count; i++)
            {
                var (source, operation, destination, applied) = transfers[i];
                if (!applied && (this.StoredBorrowActive(source, operation) || this.StoredBorrowEstablishedAt(source, operation)))
                {
                    transfers[i] = (source, operation, destination, true);
                    this.SpreadStoredBorrow(destination, operation, -1);
                    changed = true;
                }
            }
        }
        while (changed);
    }

    // Whether a store or an applied transfer at the operation itself establishes the record, as when one call retains a referent
    // in an argument and returns a value depending on it.
    private bool StoredBorrowEstablishedAt(int key, int operation)
    {
        for (var link = this.storedBorrowHeads![key]; link >= 0; link = this.storedBorrowLinks![link].Next)
        {
            if (this.storedBorrowLinks![link].Operation == operation)
            {
                return true;
            }
        }

        for (var i = 0; this.storedBorrowTransfers is { } transfers && i < transfers.Count; i++)
        {
            if (transfers[i] is { Applied: true } transfer && transfer.Destination == key && transfer.Operation == operation)
            {
                return true;
            }
        }

        return false;
    }

    // Marks the operations after start in the row, stopping at a redefinition of the holder, if any.
    private void SpreadStoredBorrow(int row, int start, int holder)
    {
        var active = this.storedBorrowActive.AsSpan(row * this.storedBorrowWords, this.storedBorrowWords);
        var queued = this.QueueStoredBorrowSuccessors(start, active, 0);
        while (queued > 0)
        {
            var operation = this.storedBorrowQueue[--queued];
            if (holder < 0 || !DefinesBorrowHolder(this.Operations[operation], holder))
            {
                queued = this.QueueStoredBorrowSuccessors(operation, active, queued);
            }
        }
    }

    private int QueueStoredBorrowSuccessors(int operation, Span<ulong> active, int queued)
    {
        for (var e = this.EdgeHeads[operation]; e >= 0; e = this.Edges[e].Next)
        {
            if (this.Edges[e].Kind != OwnershipEdgeKind.Abort)
            {
                queued = this.QueueStoredBorrow(this.Edges[e].To, active, queued);
            }
        }

        for (var e = this.checkingBorrowHeads[operation]; e >= 0; e = this.checkingBorrowEdges[e].Next)
        {
            queued = this.QueueStoredBorrow(this.checkingBorrowEdges[e].To, active, queued);
        }

        return queued;
    }

    private int QueueStoredBorrow(int target, Span<ulong> active, int queued)
    {
        ref var word = ref active[target >> 6];
        if ((word & (1UL << target)) == 0)
        {
            word |= 1UL << target;
            this.storedBorrowQueue[queued++] = target;
        }

        return queued;
    }
}
