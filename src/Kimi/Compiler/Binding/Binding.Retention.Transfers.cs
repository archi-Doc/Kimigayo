// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Call retention, the transfer rule (SPEC 15.6.4 as DECISIONS R4-M131 states it; BU-40; contract in TyCtxt.Retention.cs).
// Responsibility: which published operation transfers values, how it narrows rule A's rows at a raw layer without an Origin, and which
// stores it writes only where its receiver refers (SPEC 4.9.6; DECISIONS BU-42).
// Inputs: the callee of the contract being computed, its instantiated inputs, and the premise scope TyCtxt read an instance with an
// abstract part under, RetentionRows' parameter, which it hands to IteratorTransfer (the library root's for any other).
// Outputs: the contract's Transfer: its declaring-identity key (TransferKey), read by RetentionRows once per contract, and for a callee
// that key does not name its Iterator key (IteratorTransfer), read once, at the first output whose hold walk meets a written unstated
// layer, so an ordinary call pays two symbol reads; the outputs it writes (TransferWrites) and what an input keeps there (TransferKept);
// from the identity key alone, the contract's CallRetention.ReferentStores (ReferentStores).
// Keys, each a published declaring identity, never a name or a file (SPEC 22.1): `exchange` and `swap` by their catalog IDs; an operation
// of SPEC 4.6 to 4.9: a member of Array or Dictionary (4.9, whose receiver's element storage keeps only its added inputs,
// KimiLibraryCatalog.Added), of a Type of TransferTypes, or of the fixed array (KimiLibrary.FixedArrayMembers); an Iterator's `next`, a
// callee for which `for`'s witness test selects the Iterator conformance of its receiver's referent (SelectsWitness), that conformance
// proven under the premises (a conditional one at the instance: OwningIterator<I> when I is Iterator).
// Invariant: TyCtxt memoizes a contract per callee, Subst and premise scope, so each key is read once per instance, never per call.
// Must not: reach a library Type outside these sections (DictionaryStorage, HeapBuffer), or narrow a row rule A does not form.
public sealed partial class Binding
{
    // `exchange`'s target, the input whose chain its result keeps (KimiLibraryShapeTest pins the declaration's shape).
    private const int ExchangeTarget = 0;

    // SPEC 4.6 to 4.8's Types, whose operations transfer values (4.9's Array and Dictionary are Transfer.Elements).
    private static readonly KimiDeclarationId[] TransferTypes =
    [
        KimiDeclarationId.Slice, KimiDeclarationId.UniqSlice, KimiDeclarationId.FromEnd, KimiDeclarationId.Start, KimiDeclarationId.End,
        KimiDeclarationId.Range, KimiDeclarationId.ClosedRange, KimiDeclarationId.ResolvedRange,
    ];

    // The published operations that transfer values (SPEC 15.6.4): none; one whose borrowed inputs keep only their chains; `exchange`, whose
    // result also keeps only its target's chain; a 4.9 operation, whose receiver's element storage also keeps only its added inputs.
    private enum Transfer : byte
    {
        None,
        Values,
        Exchange,
        Elements,
    }

    // Whether a transfer operation writes `output` there: a 4.9 operation's row is its complete published effect (SPEC 4.9.6), which
    // writes its result and its receiver's element storage (ReceiverStorage), never through another argument (`append`'s added
    // `raw/(raw/u8)`), and `exchange` writes its result and its target, never through `with` (15.7); rule A forms no row at another output.
    private static bool TransferWrites(Transfer transfer, BindingSymbol? callee, int output) => output == CallRetention.ResultOutput || transfer switch
    {
        Transfer.Elements => ReceiverStorage(callee!, output),
        Transfer.Exchange => output == ExchangeTarget,
        _ => true,
    };

    // Whether `output` is the store at the callee's receiver (an input's writable storage, never the result), the one receiver predicate
    // TransferWrites, TransferKept and ReferentStores read: a 4.9 operation's receiver's element storage, and for any operation of SPEC 4.6
    // to 4.9 with a borrowed receiver the storage that receiver refers to (4.9.6). A member without a receiver (construction: its
    // ReceiverIndex is -1, which is CallRetention.ResultOutput) has none, so the receiver narrowing never reaches its result (SPEC 4.9.6, 15.6.4).
    private static bool ReceiverStorage(BindingSymbol callee, int output) => output >= 0 && output == callee.ReceiverIndex;

    // The declaring-identity key of a callee (the file's contract): `exchange`, a 4.9 operation, `swap` or a 4.6 to 4.8 operation; None
    // for any other, whose Iterator key IteratorTransfer reads.
    private Transfer TransferKey(FunctionKoto? callee)
    {
        if (callee?.BoundSymbol is not { } symbol)
        {
            return Transfer.None;
        }

        if (symbol.LibraryDeclaration == KimiDeclarationId.Exchange)
        {
            return Transfer.Exchange;
        }

        var owner = callee.Parent?.BoundSymbol?.LibraryDeclaration;
        if (owner is KimiDeclarationId.Array or KimiDeclarationId.Dictionary)
        {
            return Transfer.Elements;
        }

        return symbol.LibraryDeclaration == KimiDeclarationId.Swap || ReferenceEquals(callee.Parent, this.Library.FixedArrayMembers) ||
            (owner is { } type && Array.IndexOf(TransferTypes, type) >= 0) ? Transfer.Values : Transfer.None;
    }

    // The Iterator key of a callee TransferKey does not name (the file's contract), its Iterator conformance proven under `premises`
    // (null: the library root's).
    private Transfer IteratorTransfer(BindingSymbol? callee, ReadOnlySpan<BoundType> inputs, BindingScope? premises)
    {
        if (callee is not { ReceiverIndex: >= 0 } symbol || this.BorrowReferent(inputs[symbol.ReceiverIndex], out _, out _) is not { } self)
        {
            return Transfer.None;
        }

        return this.SelectsWitness(symbol, self, this.Library.Iterator) &&
            this.ResolveConformance(self, this.Library.Iterator, premises ?? this.EffectPremises(null), out _) == ConstraintProof.Proven
            ? Transfer.Values : Transfer.None;
    }

    // What input i, other than `output`, keeps at `output`'s written unstated layer: its own value and its chain; under a transfer
    // operation a borrowed argument (BorrowReferent: not a raw pointer, a Slice or UniqSlice included) its chain only, `exchange`'s result
    // nothing but its target's chain, and a 4.9 operation's receiver storage (ReceiverStorage) nothing of an input that is not added.
    private RetentionKind TransferKept(Transfer transfer, BindingSymbol? callee, BoundType input, int i, int output)
    {
        if ((transfer == Transfer.Exchange && output == CallRetention.ResultOutput && i != ExchangeTarget) ||
            (transfer == Transfer.Elements && ReceiverStorage(callee!, output) && !KimiLibraryCatalog.Added(callee, i)))
        {
            return RetentionKind.None;
        }

        return transfer == Transfer.None || this.BorrowReferent(input, out _, out _) is null ? RetentionKind.Value | RetentionKind.Chain : RetentionKind.Chain;
    }

    // CallRetention.ReferentStores (SPEC 4.9.6; DECISIONS BU-42): under the identity key of an operation of SPEC 4.6 to 4.9 (`exchange` and
    // `swap` have no receiver), the store at its receiver (ReceiverStorage) when the receiver is an exclusive borrow (BorrowReferent), whose
    // referent is that storage; a shared borrow's storage is no store, and a receiver taken by value is the callee's own. The shared empty
    // array for none.
    private int[] ReferentStores(Transfer key, BindingSymbol? callee, ReadOnlySpan<BoundType> inputs)
    {
        var receiver = callee?.ReceiverIndex ?? CallRetention.ResultOutput;
        return key is Transfer.Elements or Transfer.Values && ReceiverStorage(callee!, receiver) &&
            this.BorrowReferent(inputs[receiver], out _, out var exclusive) is not null && exclusive ? [receiver] : [];
    }
}
