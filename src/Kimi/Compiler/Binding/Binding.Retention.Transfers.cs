// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Call retention, the transfer rule (SPEC 15.6.4 as DECISIONS R4-M131 states it; BU-40; contract in TyCtxt.Retention.cs).
// Responsibility: which published operation transfers values, and how it narrows rule A's rows at a raw layer without an Origin.
// Inputs: the callee of the contract being computed, its instantiated inputs, and the premise scope TyCtxt read an instance with an
// abstract part under, RetentionRows' parameter, which it hands to TransferOf (the library root's for any other).
// Outputs: the contract's Transfer (TransferOf), read by RetentionRows once, at its first output whose hold walk meets a written
// unstated layer, so an ordinary call pays nothing; the outputs it writes (TransferWrites) and what an input keeps there (TransferKept).
// Keys, each a published declaring identity, never a name or a file (SPEC 22.1): an Iterator's `next`, a callee for which `for`'s
// witness test selects the Iterator conformance of its receiver's referent (SelectsWitness), that conformance proven under the premises
// (a conditional one at the instance: OwningIterator<I> when I is Iterator); `exchange` and `swap` by their catalog IDs; an operation of
// SPEC 4.6 to 4.9: a member of Array or Dictionary (4.9, whose receiver's element storage keeps only its added inputs,
// KimiLibraryCatalog.Added), of a Type of TransferTypes, or of the fixed array (KimiLibrary.FixedArrayMembers).
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
    // writes its result and its receiver's element storage, never through another argument (`append`'s added `raw/(raw/u8)`), and
    // `exchange` writes its result and its target, never through `with` (15.7); rule A forms no row at another output.
    private static bool TransferWrites(Transfer transfer, BindingSymbol? callee, int output) => output == Retention.Result || transfer switch
    {
        Transfer.Elements => output == callee!.ReceiverIndex,
        Transfer.Exchange => output == ExchangeTarget,
        _ => true,
    };

    // The transfer key of a callee (the file's contract), its Iterator conformance proven under `premises` (null: the library root's).
    private Transfer TransferOf(FunctionKoto? callee, ReadOnlySpan<BoundType> inputs, BindingScope? premises)
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

        if (symbol.LibraryDeclaration == KimiDeclarationId.Swap || ReferenceEquals(callee.Parent, this.Library.FixedArrayMembers) ||
            (owner is { } type && Array.IndexOf(TransferTypes, type) >= 0))
        {
            return Transfer.Values;
        }

        if (symbol.ReceiverIndex < 0 || this.BorrowReferent(inputs[symbol.ReceiverIndex], out _, out _) is not { } self)
        {
            return Transfer.None;
        }

        return this.SelectsWitness(symbol, self, this.Library.Iterator) &&
            this.ResolveConformance(self, this.Library.Iterator, premises ?? this.EffectPremises(null), out _) == ConstraintProof.Proven
            ? Transfer.Values : Transfer.None;
    }

    // What input i, other than `output`, keeps at `output`'s written unstated layer: its own value and its chain; under a transfer
    // operation a borrowed argument (BorrowReferent: not a raw pointer, a Slice or UniqSlice included) its chain only, `exchange`'s result
    // nothing but its target's chain, and a 4.9 operation's receiver storage nothing of an input that is not added.
    private Kept TransferKept(Transfer transfer, BindingSymbol? callee, BoundType input, int i, int output)
    {
        if ((transfer == Transfer.Exchange && output == Retention.Result && i != ExchangeTarget) ||
            (transfer == Transfer.Elements && output == callee!.ReceiverIndex && !KimiLibraryCatalog.Added(callee, i)))
        {
            return Kept.None;
        }

        return transfer == Transfer.None || this.BorrowReferent(input, out _, out _) is null ? Kept.Value | Kept.Chain : Kept.Chain;
    }
}
