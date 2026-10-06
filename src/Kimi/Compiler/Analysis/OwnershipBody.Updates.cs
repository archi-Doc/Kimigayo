// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    // SPEC 15.7.3: slot authority stays in the pointer; this record transfers only its complete stored contents.
    private readonly List<ContentUpdate> contentUpdates = new();
    private readonly HashSet<(int Place, int Root)> contentSlots = new();
    private readonly List<(int From, int Next)> contentPredecessors = new();
    private int[] contentHeads = [];
    private int[] contentWork = [];
    private bool[] contentVisited = [];

    private readonly record struct ContentUpdate(int Operation, int Target, int Incoming, int Result, BoundType Type, bool Swap);

    private void PrepareContentUpdates(int count)
    {
        for (var id = 0; id < this.Operations.Count; id++)
        {
            var operation = this.Operations[id];
            var value = this.Values[id];
            if (operation.Kind is not (OwnershipOperationKind.UpdateBorrowed or OwnershipOperationKind.StorePointer) || value.Count == 0)
            {
                continue;
            }

            var pointer = this.ValueOperands[value.Start];
            var holder = ValuePlaceForBorrow(this.Operations[pointer]);
            if (holder < 0 || this.Places[holder].Type is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Components: [var storage] })
            {
                continue; // Raw stores carry no safe Loan authority.
            }

            var target = this.StoredReferent(pointer, holder);
            var incoming = operation.Kind == OwnershipOperationKind.StorePointer ? operation.Place : operation.Input;
            var kind = (operation.Source as InvocationKoto)?.BoundCall?.Target.CompilerFunction;
            var swap = operation.Kind == OwnershipOperationKind.UpdateBorrowed && kind == CompilerFunctionKind.Swap;
            if (swap)
            {
                var otherPointer = this.ValueOperands[value.Start + 1];
                incoming = this.StoredReferent(otherPointer, incoming);
            }

            var result = operation.Kind == OwnershipOperationKind.UpdateBorrowed && kind == CompilerFunctionKind.Exchange ? operation.Place : -1;
            this.contentUpdates.Add(new(id, target, incoming, result, storage, swap));
            this.RegisterContentSlot(target, storage, count);
            if (swap)
            {
                this.RegisterContentSlot(incoming, storage, count);
            }

            if (result >= 0)
            {
                this.RegisterContentSlot(result, storage, count);
            }
        }
    }

    // The complete stored Type bounds which roots can arrive, but each transfer establishes its actual inputs.
    // No root is acquired just because its Origin has the same name as another root's.
    private void RegisterContentSlot(int place, BoundType storage, int count)
    {
        for (var i = 0; i < this.borrowRoots!.Count; i++)
        {
            var root = this.borrowRoots[i];
            // An opaque referent is represented by its reference holder. Its outer storage Loan survives writes through it;
            // only the stored Type's internal dependencies participate in replacing contents (SPEC 15.7.3).
            var holder = this.Places[place].Type;
            if (root == place || (!ReferenceEquals(holder, storage) && holder.Origin is { } origin && this.OriginNamesRoot(origin, root)) || (this.IsExclusiveBorrowInput(root)
                ? this.NamedOriginRequirement(storage, this.Places[root].Type.Origin!) == LoanRequirement.None
                : this.borrowDependencies[(place * count) + root] == LoanRequirement.None))
            {
                continue;
            }

            if (this.contentSlots.Add((place, root)))
            {
                (this.storedBorrows ??= new()).Key(place, root);
            }
        }
    }

    private bool ReplacesContents(int operation, int place)
    {
        for (var i = 0; i < this.contentUpdates.Count; i++)
        {
            var update = this.contentUpdates[i];
            if (update.Operation == operation && (update.Target == place || (update.Swap && update.Incoming == place)))
            {
                return true;
            }
        }

        return false;
    }

    private bool InvalidatesContentChild(int operation, int holder)
    {
        foreach (var update in this.contentUpdates)
        {
            if (update.Operation != operation || update.Type.Semantics != SemanticsKind.Uniq)
            {
                continue;
            }

            var value = this.Values[operation];
            if (this.IsBorrowAncestor(this.ValueOperands[value.Start], holder) ||
                (update.Swap && this.IsBorrowAncestor(this.ValueOperands[value.Start + 1], holder)))
            {
                continue; // The operation's own storage Loan and its parents survive the content change.
            }

            var definition = this.borrowDefinitions[holder] >= 0 ? this.borrowDefinitions[holder] : this.ProducingValue(holder, operation);
            if ((holder != update.Target && this.IsBorrowAncestor(definition, update.Target)) ||
                (update.Swap && holder != update.Incoming && this.IsBorrowAncestor(definition, update.Incoming)))
            {
                return true;
            }
        }

        return false;
    }

    private void PrepareContentPredecessors()
    {
        Grow(ref this.contentHeads, this.Operations.Count);
        Grow(ref this.contentWork, this.Operations.Count);
        Grow(ref this.contentVisited, this.Operations.Count);
        this.contentHeads.AsSpan(0, this.Operations.Count).Fill(-1);
        this.contentPredecessors.Clear();
        for (var i = 0; i < this.Edges.Count; i++)
        {
            var edge = this.Edges[i];
            if (edge.Kind != OwnershipEdgeKind.Abort)
            {
                Add(edge.From, edge.To);
            }
        }

        for (var from = 0; from < this.Operations.Count; from++)
        {
            for (var e = this.checkingBorrowHeads[from]; e >= 0; e = this.checkingBorrowEdges[e].Next)
            {
                Add(from, this.checkingBorrowEdges[e].To);
            }
        }

        void Add(int from, int to)
        {
            this.contentPredecessors.Add((from, this.contentHeads[to]));
            this.contentHeads[to] = this.contentPredecessors.Count - 1;
        }
    }

    // Returned complete contents whose contract does not name the writable slot's Loan have independent exclusive capabilities
    // from that slot's surviving contents. Universal checking forbids duplicating them or returning a child tied to the slot. Their possible
    // lifetime roots may overlap; that union is not evidence that the two capabilities alias (SPEC 15.6.3, 15.7.3).
    private bool IndependentUpdatedCallResult(int value, int holder, int at)
    {
        var holderValue = this.borrowDefinitions[holder] >= 0 ? this.borrowDefinitions[holder] : this.ProducingValue(holder, at);
        holder = this.ContentOwner(holderValue, holder);
        for (var remaining = this.Operations.Count; remaining > 0 && value >= 0; remaining--)
        {
            var operation = this.Operations[value];
            if (operation.Kind == OwnershipOperationKind.Call && operation.Place >= 0)
            {
                var result = this.Places[operation.Place].Type;
                if (!ReferenceEquals(result, this.Places[holder].Type))
                {
                    return false;
                }

                for (var entry = value - 1; entry >= 0 && this.Operations[entry] is { Kind: OwnershipOperationKind.CallEntry } input && ReferenceEquals(input.Source, operation.Source); entry--)
                {
                    var reference = this.Places[input.Place].Type;
                    if (reference is { Semantics: SemanticsKind.Uniq, Components: [var storage], Origin: { } origin } &&
                        ReferenceEquals(storage, result) && !Binding.ContainsOrigin(result, origin) &&
                        this.StoredReferent(entry, input.Place) == holder && this.ContentsUnchangedSince(holder, value, at))
                    {
                        return true;
                    }
                }

                return false;
            }

            var node = this.Values[value];
            if (node.Kind is OwnershipValueKind.Alias or OwnershipValueKind.Address or OwnershipValueKind.PointerLoad or OwnershipValueKind.BorrowedField && node.Count == 1)
            {
                value = this.ValueOperands[node.Start];
            }
            else if (operation.Kind is OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow && operation.Place >= 0 && this.borrowDefinitions[operation.Place] is >= 0 and var definition && definition < value)
            {
                value = definition;
            }
            else if (operation.Kind == OwnershipOperationKind.Produce && value > 0 && this.Operations[value - 1] is { Kind: OwnershipOperationKind.Call } call &&
                call.Place == operation.Place && ReferenceEquals(call.Source, operation.Source))
            {
                value--;
            }
            else
            {
                return false;
            }
        }

        return false;
    }

    // A stored reference's access keeps the complete owner slot whose contents supplied it. Stop at a call or an input;
    // only actual address/load edges establish this connection, never a matching Origin.
    private int ContentOwner(int value, int fallback, bool throughLoads = true)
    {
        for (var remaining = this.Operations.Count; remaining > 0 && value >= 0; remaining--)
        {
            var operation = this.Operations[value];
            var node = this.Values[value];
            if (operation.Kind == OwnershipOperationKind.Borrow && node.Kind == OwnershipValueKind.Address && node.Count == 0 && operation.Place >= 0)
            {
                return operation.Place;
            }

            if ((node.Kind is OwnershipValueKind.Alias or OwnershipValueKind.Address || (throughLoads && node.Kind is OwnershipValueKind.PointerLoad or OwnershipValueKind.BorrowedField)) && node.Count == 1)
            {
                value = this.ValueOperands[node.Start];
            }
            else
            {
                return fallback;
            }
        }

        return fallback;
    }

    // All paths to this use must pass the recorded call without a later write. Back edges and checking continuations count.
    private bool ContentsUnchangedSince(int holder, int definition, int before)
    {
        this.contentVisited.AsSpan(0, this.Operations.Count).Clear();
        this.contentWork[0] = before;
        this.contentVisited[before] = true;
        var count = 1;
        var found = false;
        while (count > 0)
        {
            var at = this.contentWork[--count];
            if (this.contentHeads[at] < 0)
            {
                return false;
            }

            for (var e = this.contentHeads[at]; e >= 0; e = this.contentPredecessors[e].Next)
            {
                var predecessor = this.contentPredecessors[e].From;
                if (predecessor == definition)
                {
                    found = true;
                    continue;
                }

                if (DefinesBorrowHolder(this.Operations[predecessor], holder) || this.ReplacesContents(predecessor, holder))
                {
                    return false;
                }

                if (this.Operations[predecessor].Kind is OwnershipOperationKind.StorePointer or OwnershipOperationKind.UpdateBorrowed &&
                    this.Values[predecessor] is { Count: > 0 } update && this.ContentOwner(this.ValueOperands[update.Start], -1, throughLoads: false) == holder)
                {
                    return false; // An in-place write of a stored part can also replace its exclusive capability.
                }

                foreach (var retention in this.retentions)
                {
                    if (retention.Operation == predecessor && retention.Referent == holder)
                    {
                        return false;
                    }
                }

                if (!this.contentVisited[predecessor])
                {
                    this.contentVisited[predecessor] = true;
                    this.contentWork[count++] = predecessor;
                }
            }
        }

        return found;
    }
}
