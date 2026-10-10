// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Owns the live-holder result and its reusable storage. The solver reads operation facts and explicit graph edges;
// it never changes dependency/authority tables or publishes diagnostics.
internal struct BorrowLiveness()
{
    private PackedAnalysisTable live = new(1);
    private List<int>? places;

    internal readonly int Count => this.places?.Count ?? 0;

    internal readonly int ByteCapacity => this.live.ByteCapacity;

    internal readonly int PlaceAt(int slot) => this.places![slot];

    internal readonly bool IsLive(int operation, int slot) => this.live.IsSet((operation * this.Count) + slot);

    internal readonly void Clear() => this.places?.Clear();

    internal void Solve(OwnershipBody body, PackedAnalysisTable dependencies, IReadOnlyList<int> roots)
    {
        (this.places ??= new()).Clear();
        var count = body.Places.Count;
        for (var p = 0; p < count; p++)
        {
            for (var r = 0; r < roots.Count; r++)
            {
                if (dependencies[(p * count) + roots[r]] != LoanRequirement.None)
                {
                    this.places.Add(p);
                    break;
                }
            }
        }

        var liveWidth = this.places.Count;
        this.live.Reset(OwnershipStorage.Cells(liveWidth, body.Operations.Count, 1, "borrow liveness"));
        bool changed;
        do
        {
            changed = false;
            for (var op = body.Operations.Count - 1; op >= 0; op--)
            {
                for (var slot = 0; slot < liveWidth; slot++)
                {
                    var p = this.places![slot];
                    var live = Uses(body, op, p);
                    if (!OwnershipBody.DefinesBorrowHolder(body.Operations[op], p))
                    {
                        for (var e = body.EdgeHeads[op]; e >= 0 && !live; e = body.Edges[e].Next)
                        {
                            var edge = body.Edges[e];
                            live |= edge.Kind != OwnershipEdgeKind.Abort && this.live.IsSet((edge.To * liveWidth) + slot);
                        }
                    }

                    var at = (op * liveWidth) + slot;
                    changed |= live != this.live.IsSet(at);
                    this.live.Set(at, live);
                }
            }
        }
        while (changed);
    }

    private static bool Uses(OwnershipBody body, int id, int place)
    {
        var operation = body.Operations[id];
        if (operation.Kind is OwnershipOperationKind.CheckDictionaryKey or OwnershipOperationKind.StoreDictionaryEntry)
        {
            return operation.Place == place || operation.Input == place ||
                (operation.Kind == OwnershipOperationKind.StoreDictionaryEntry && body.OperationSteps[id] == place);
        }

        if (body.Values[id] is { Kind: OwnershipValueKind.Formatting, Count: 1 } formatting &&
            OwnershipBody.ValuePlaceForBorrow(body.Operations[body.ValueOperands[formatting.Start]]) == place)
        {
            return true;
        }

        if (operation.Kind == OwnershipOperationKind.ActivateCallBorrows)
        {
            for (var r = operation.Reservation; r >= 0; r = body.CallReservations[r].Next)
            {
                if (body.Operations[body.CallReservations[r].Borrow].Place == place)
                {
                    return true; // Keep existing parent authority active until the child is activated.
                }
            }
        }

        if (operation.IsWholeUpdate)
        {
            return body.Values[id].Constant == place || operation.Input == place;
        }

        if (operation.Kind == OwnershipOperationKind.CompleteConstruction)
        {
            // SPEC 15.6.3, 15.6.7 (PLAN G53): a literal takes its payloads when it completes, so a payload placed
            // earlier holds its Loan while the later elements are evaluated, and a parent acquired exclusively by a
            // later placement meets its Reborrow child placed in the same literal.
            var construction = body.Constructions[body.OperationSteps[id]];
            return place >= construction.PayloadStart && place < construction.PayloadStart + construction.PayloadCount;
        }

        if (operation.Kind == OwnershipOperationKind.Call && operation.Input == place)
        {
            return true; // SPEC 15.6.4: a value call's receiver and its environment's Loans stay protected through the call.
        }

        if (operation.Kind == OwnershipOperationKind.EndComparisonLoans)
        {
            for (var loan = body.LoanInputs[id]; loan >= 0; loan = body.ComparisonLoans[loan].Parent)
            {
                if (body.ComparisonLoans[loan] is { Guard: >= 0 } guard && guard.Place == place)
                {
                    return true; // Subject and referents remain protected through guard cleanup.
                }
            }
        }

        if (body.Values[id] is { Kind: OwnershipValueKind.Sequence, Constant: var sequence } && body.Sequences[(int)sequence].Receiver == place)
        {
            return true;
        }

        if (operation.Input == place && operation.Kind is OwnershipOperationKind.Write or OwnershipOperationKind.WriteElement or OwnershipOperationKind.WriteBorrowedField or OwnershipOperationKind.PayloadPlacement or OwnershipOperationKind.InitializeSubject)
        {
            return true;
        }

        return operation.Place == place && operation.Kind switch
        {
            OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow or OwnershipOperationKind.CallEntry or OwnershipOperationKind.Deliver or OwnershipOperationKind.LocateReceiver or OwnershipOperationKind.WriteBorrowedField or OwnershipOperationKind.StorePointer or OwnershipOperationKind.PatternTest or OwnershipOperationKind.AcquirePattern => true,
            OwnershipOperationKind.Cleanup => body.Places[place].CleanupObservesBorrows,
            _ => false,
        };
    }
}
