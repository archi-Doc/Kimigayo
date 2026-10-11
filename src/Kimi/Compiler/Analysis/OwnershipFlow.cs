// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// A compact transfer range describes operand correspondence, without prescribing a dependency or authority join.
internal readonly record struct OwnershipTransfers(int Destination, int Source, int Count, int DestinationStep = 0, int SourceStep = 0)
{
    internal (int Destination, int Source) this[int index] => (uint)index < (uint)this.Count
        ? (this.Destination + (index * this.DestinationStep), this.Source + (index * this.SourceStep))
        : throw new ArgumentOutOfRangeException(nameof(index));
}

internal static class OwnershipFlow
{
    // SPEC 15.6.3: the operand shape shared by independent transfer policies. Every
    // OwnershipOperationKind has a row (FlowOf); a kind without one is an internal invariant failure, never a silent omission.
    internal enum OperationFlow : byte
    {
        Unclassified,

        // No value reaches a Place at this operation; its row states why.
        None,

        // The Input takes the Place's authority: an acquisition.
        InputFromPlace,

        // The Place takes the Input's authority: a store.
        PlaceFromInput,

        // The Dictionary takes the key's and the value's authority.
        DictionaryEntry,

        // The constructed Place takes every payload's authority.
        Construction,

        // Every payload takes the decomposed Place's authority.
        Decomposition,

        // The result takes every entry's authority, and a writable referent retains the entries its storage Type names.
        Call,

        // The referent, or else the holder, of an exclusive reference takes the stored value's authority through the storage Type.
        Store,

        // Old contents reach the result/other target before new contents are installed; slot authority is unchanged.
        Update,
    }

    // SPEC 15.6.3: the operand shape of a value reaching the Place its operation defines.
    internal enum ValueFlow : byte
    {
        Unclassified,

        // The value transfers no authority; its row states why.
        None,

        // The defined Place takes the authority of its one operand's Place.
        Operand,

        // The defined Place takes the sequence receiver's authority.
        Receiver,

        // The closure temporary takes every entry's authority.
        Entries,
    }

    // SPEC 15.6.3: the flow row of each operation kind (SharedEngineTotalityTest checks that none is Unclassified).
    internal static OperationFlow FlowOf(OwnershipOperationKind kind) => kind switch
    {
        // Acquisitions: the Input holds what the acquired Place holds.
        OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow or OwnershipOperationKind.AcquirePattern => OperationFlow.InputFromPlace,

        // Stores: the Place holds what the stored Input holds.
        OwnershipOperationKind.Write or OwnershipOperationKind.InitializeSubject or OwnershipOperationKind.PayloadPlacement => OperationFlow.PlaceFromInput,
        OwnershipOperationKind.StoreDictionaryEntry => OperationFlow.DictionaryEntry,
        OwnershipOperationKind.CompleteConstruction => OperationFlow.Construction,
        OwnershipOperationKind.DecomposeCase => OperationFlow.Decomposition,
        OwnershipOperationKind.Call => OperationFlow.Call,
        OwnershipOperationKind.StorePointer or OwnershipOperationKind.WriteElement => OperationFlow.Store,
        OwnershipOperationKind.ReplaceBorrowed or OwnershipOperationKind.ExchangeBorrowed or OwnershipOperationKind.SwapBorrowed => OperationFlow.Update,

        // Control flow and scope: no value reaches a Place. Declare brings an uninitialized Place into scope and Cleanup
        // destroys one, which holds nothing after.
        OwnershipOperationKind.Entry or OwnershipOperationKind.Exit or OwnershipOperationKind.Branch or OwnershipOperationKind.Declare or
            OwnershipOperationKind.Cleanup => OperationFlow.None,

        // Tests, checks and observations read values defined elsewhere and define no Place.
        OwnershipOperationKind.MatchDispatch or OwnershipOperationKind.PatternTest or OwnershipOperationKind.CheckDictionaryKey or
            OwnershipOperationKind.CheckReceiverField or OwnershipOperationKind.TestObserve or OwnershipOperationKind.TestMessage or
            OwnershipOperationKind.TestAbort => OperationFlow.None,

        // Loans of Places defined elsewhere end (SPEC 15.6.7) or activate (SPEC 15.6.7, call reservations).
        OwnershipOperationKind.EndComparisonLoans or OwnershipOperationKind.ActivateCallBorrows => OperationFlow.None,

        // A receiver or element path is selected; the Produce that reads it carries its value (FlowOf(OwnershipValueKind)).
        OwnershipOperationKind.LocateReceiver or OwnershipOperationKind.ProjectElement => OperationFlow.None,

        // SPEC 13.7: an intrinsic update borrows its target in place under a reservation; the target keeps its own authority.
        OwnershipOperationKind.UpdateTarget => OperationFlow.None,

        // A Place defined or read with a value: the value's row carries the flow.
        OwnershipOperationKind.Produce or OwnershipOperationKind.Read => OperationFlow.None,

        // The Consume or Borrow into the entry carried the authority, which the Call or the Closure value takes from the entry.
        OwnershipOperationKind.CallEntry => OperationFlow.None,

        // The Write securing the result carried the delivered value (OwnershipAnalysis.Results.cs).
        OwnershipOperationKind.Deliver => OperationFlow.None,

        // A method's receiver field is a Place without an Input; its Loans come from its Type (AddType).
        OwnershipOperationKind.InitializeReceiverField => OperationFlow.None,

        // SPEC 13.7: the Input of a borrowed field write is a ComputeUpdate scalar; no reference is stored.
        OwnershipOperationKind.WriteBorrowedField => OperationFlow.None,

        _ => OperationFlow.Unclassified,
    };

    // SPEC 15.6.3: the flow row of each value kind (SharedEngineTotalityTest checks that none is Unclassified).
    internal static ValueFlow FlowOf(OwnershipValueKind kind) => kind switch
    {
        // A copied value, a reference loaded through another reference (SPEC 3.4.1, 10.2) or a field read through a reference.
        OwnershipValueKind.Alias or OwnershipValueKind.PointerLoad or OwnershipValueKind.BorrowedField => ValueFlow.Operand,

        // SPEC 14.6.2, 4.6: an element borrowed or read through a sequence descends from its receiver.
        OwnershipValueKind.Sequence => ValueFlow.Receiver,

        // SPEC 7.6.2, 15.8.2: the closure environment holds the entries' Loans.
        OwnershipValueKind.Closure => ValueFlow.Entries,

        // No source Place: an exclusive input is its own root (VerifyBorrows) and a Capture's binding is a carrier
        // (RecordCarriers) or its own root (IsExclusiveBorrowInput); nothing is transferred.
        OwnershipValueKind.None or OwnershipValueKind.StaticRead or OwnershipValueKind.Constant or OwnershipValueKind.Parameter or
            OwnershipValueKind.Capture => ValueFlow.None,

        // Carried by the operation's row: a call result (Call), an acquired address (Borrow), a store (StorePointer), the
        // entries of a Dictionary literal (StoreDictionaryEntry) and a pattern binding or Subject (AcquirePattern, InitializeSubject).
        OwnershipValueKind.Call or OwnershipValueKind.DefaultCall or OwnershipValueKind.DefaultRead or OwnershipValueKind.Borrow or OwnershipValueKind.Address or OwnershipValueKind.PointerStore or
            OwnershipValueKind.DictionaryLiteral or OwnershipValueKind.PatternProjection => ValueFlow.None,

        // Computed owned scalars and tests hold no reference.
        OwnershipValueKind.Unary or OwnershipValueKind.Binary or OwnershipValueKind.StringComparison or OwnershipValueKind.ContractComparison or
            OwnershipValueKind.RuntimeTypeTest or OwnershipValueKind.Formatting => ValueFlow.None,

        // A join of one Place's scalar values keeps that Place's dependencies, which are per Place.
        OwnershipValueKind.Phi => ValueFlow.None,

        // An element read has no operand: a stored reference is loaded through the slot borrow (PointerLoad), and an element
        // temporary's Loans come from its Type (AddType) and its ancestry from the projected root (ProjectedBorrowValue).
        OwnershipValueKind.Element => ValueFlow.None,

        // The operation carries the update; a borrowed field write has a scalar input.
        OwnershipValueKind.BorrowedFieldWrite or OwnershipValueKind.BorrowedUpdate => ValueFlow.None,

        // SPEC 7.6.4: an erased Function value holds no Loan.
        OwnershipValueKind.ClosureErasure => ValueFlow.None,

        // SPEC 5.2, 5.2.2, 5.4, 13.5.4: a conversion yields an owned scalar, a raw pointer without an Origin or a raw Place borrow
        // rooted by its anchor (AddOrigin); a raw projection accesses nothing.
        OwnershipValueKind.Convert or OwnershipValueKind.PointerProject => ValueFlow.None,

        _ => ValueFlow.Unclassified,
    };

    internal static OwnershipTransfers Transfers(OwnershipOperation operation)
        => operation.Place < 0 || operation.Input < 0 ? default : FlowOf(operation.Kind) switch
        {
            OperationFlow.InputFromPlace => new(operation.Input, operation.Place, 1),
            OperationFlow.PlaceFromInput => new(operation.Place, operation.Input, 1),
            _ => default,
        };

    internal static OwnershipTransfers Transfers(OwnershipConstructionPlan plan, bool decompose = false)
        => decompose ? new(plan.PayloadStart, plan.Place, plan.PayloadCount, DestinationStep: 1)
        : new(plan.Place, plan.PayloadStart, plan.PayloadCount, SourceStep: 1);
}
