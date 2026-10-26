// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The rows of TyCtxt.Retains (Binding.Retention.cs computes the atoms and premises they are read from).
public sealed partial class Binding
{
    // The rows of the contract being computed, the inputs whose writable storage may receive a pointer tied to no input, and the positions
    // the hold walk of an output has met (Held: a Type with the Type its declaration writes there, both interned, so compared by
    // reference, and whether it is lent), reused.
    private readonly List<Retention> retentionRows = new();
    private readonly List<int> retentionUntiedStores = new();
    private readonly HashSet<(BoundType Type, BoundType? Written, bool Lent)> retentionMet = new();

    // What an output's hold walk met (SPEC 15.6.4, 5.7 Calls; DECISIONS BU-40): a raw layer without an Origin the call may write (rule
    // A), a `static` one, and one without an Origin behind a borrow or a stated raw layer in the result (R′: lent, not written). The
    // output is untied when it met Unstated or Static.
    [Flags]
    private enum Holding : byte
    {
        None = 0,
        Unstated = 1,
        Static = 2,
        Lent = 4,
    }

    // What an output keeps of an input beside the kinds its premises give (RetentionRows): its own value (a Value row) and what any chain
    // of reads through it carries (a Chain row, which subsumes the premises' Content).
    [Flags]
    private enum Kept : byte
    {
        None = 0,
        Value = 1,
        Chain = 2,
    }

    // Whether a reader may read a Type whose storage has this verdict (SPEC 23.1.2; DECISIONS BU-36, R4-M139): Prepared, and Rejected, whose
    // parts that do not substitute hold, place and destroy nothing, so what holds whatever they are stays; not a Limit (retention's
    // CallRetention.Limit, the MIR's own limit). Rejected storage rests on a Binding failure (DIAGNOSTICS rule 8: a part Binding did not
    // bind makes it incomplete), so in a complete compilation it is a broken contract. TyCtxt.Readable publishes it to the MIR.
    internal bool Readable(BoundType type, StorageOutcome storage)
    {
        if (storage == StorageOutcome.Rejected && this.Result.IsComplete)
        {
            throw new InvalidOperationException($"The storage of {type.Name} is Rejected in a complete compilation.");
        }

        return storage != StorageOutcome.Limit;
    }

    // An input retained through one of its occurrences: Value over Content; an atom of no kind gives none (a raw input's own layer).
    private static void Mark(RetentionKind?[] kinds, in RetentionAtom atom)
    {
        if (atom.Kind is { } kind)
        {
            kinds[atom.Input] = kind == RetentionKind.Value ? RetentionKind.Value : kinds[atom.Input] ?? RetentionKind.Content;
        }
    }

    // One closure from each output atom (OriginPremiseClosure, every Semantics case's premises followed) marks the inputs whose atoms it
    // reaches, Value over Content, a carrier's through its node. The result is untied when one of its non-static atoms marks no input. A
    // result the call forms within a raw parameter's storage (`within`) retains that argument's own value. Each output's hold walk (Held,
    // each position once per lent bit, BU-35, BU-40) keeps a raw input's own value where a layer with an Origin or an abstract part can
    // hold it (R4-M116 R-B4), and reports the raw layers it met: one the call writes without an Origin keeps every input by rule A
    // (WrittenUnstated), one the result lends keeps the raw inputs (R′), and one stepping Unstated or Static, not lent, unties the
    // output (Untied, UntiedStores). The work is what the closures reach, each node with its incoming premises once per closure, and the
    // hold walks; the transfer key is read once, at the first output that meets a written unstated layer, under `premises`, the scope
    // TyCtxt read an instance with an abstract part under (null: none, or an erased callable's, which has no callee). CallRetention.Limit
    // where the hold walk meets storage past its limits (Held).
    private CallRetention RetentionRows(
        OriginPremiseEnvironment environment,
        int[] nodes,
        ReadOnlySpan<BoundType> inputs,
        FunctionKoto? callee,
        BindingScope? premises)
    {
        // The sources at a node, linked atom indexes.
        var atoms = this.retentionAtoms;
        var first = new int[environment.Closure.NodeCount];
        var next = new int[atoms.Count];
        Array.Fill(first, -1);
        for (var i = 0; i < atoms.Count; i++)
        {
            if (atoms[i].Input >= 0)
            {
                (next[i], first[nodes[i]]) = (first[nodes[i]], i);
            }
        }

        var rows = this.retentionRows;
        rows.Clear();
        this.retentionUntiedStores.Clear();
        var untied = false;
        var within = KimiLibraryCatalog.Within(callee?.BoundSymbol);
        Transfer? transfer = null;
        var kinds = new RetentionKind?[inputs.Length];
        var kept = new Kept[inputs.Length];
        for (var output = Retention.Result; output < inputs.Length; output++)
        {
            Array.Clear(kinds);
            Array.Clear(kept);
            if (output == Retention.Result && within >= 0)
            {
                kept[within] = Kept.Value;
            }

            for (var i = 0; i < atoms.Count; i++)
            {
                if (atoms[i].Input == output && (output == Retention.Result || atoms[i].Writable))
                {
                    var tied = this.MarkClosure(environment.Closure, nodes[i], first, next, kinds);
                    untied |= output == Retention.Result && !tied && atoms[i].Origin?.Kind != OriginKind.Static;
                }
            }

            // The hold walk from each of the output's holds, each position once per lent bit.
            this.retentionMet.Clear();
            var met = Holding.None;
            foreach (var (at, type) in this.retentionHolds)
            {
                met |= at == output ? this.Held(type, null, output, kept, lent: false) : Holding.None;
            }

            if ((met & Holding.Unstated) != 0)
            {
                transfer ??= this.TransferOf(callee, inputs, premises);
                this.WrittenUnstated(kept, output, inputs, transfer.Value, callee?.BoundSymbol);
            }
            else if ((met & Holding.Lent) != 0)
            {
                foreach (var (input, _) in this.retentionRawInputs)
                {
                    kept[input] |= Kept.Value | Kept.Chain;
                }
            }

            var held = (met & (Holding.Unstated | Holding.Static)) != 0;
            untied |= held && output == Retention.Result;
            if (held && output >= 0)
            {
                this.retentionUntiedStores.Add(output);
            }

            for (var i = 0; i < inputs.Length; i++)
            {
                if ((kept[i] & Kept.Value) != 0 || kinds[i] == RetentionKind.Value)
                {
                    rows.Add(new(i, RetentionKind.Value, output));
                }

                if ((kept[i] & Kept.Chain) != 0 || kinds[i] == RetentionKind.Content)
                {
                    rows.Add(new(i, (kept[i] & Kept.Chain) != 0 ? RetentionKind.Chain : RetentionKind.Content, output));
                }
            }
        }

        return this.retentionLimit ? CallRetention.Limit : new(rows.ToArray(), untied, [.. this.retentionUntiedStores]);
    }

    // SPEC 15.6.4's rule A (DECISIONS R4-M131, BU-40): at a raw layer without an Origin that the call may write in `output`, every input
    // keeps what any chain of reads through it carries, and every input but the one written through keeps its own value; narrower for a
    // published transfer operation, which writes only its own outputs (TransferWrites, TransferKept).
    private void WrittenUnstated(Kept[] kept, int output, ReadOnlySpan<BoundType> inputs, Transfer transfer, BindingSymbol? callee)
    {
        for (var i = 0; TransferWrites(transfer, callee, output) && i < inputs.Length; i++)
        {
            kept[i] |= i == output ? Kept.Chain : this.TransferKept(transfer, callee, inputs[i], i, output);
        }
    }

    // The closure from one target node, marking the inputs whose atoms it reaches; whether it reached one.
    private bool MarkClosure(OriginPremiseClosure closure, int target, int[] first, int[] next, RetentionKind?[] kinds)
    {
        var start = this.OriginProofMetrics is null ? 0 : Stopwatch.GetTimestamp();
        closure.Close(target, -1, ulong.MaxValue);
        this.OriginProofMetrics?.Closure(closure, this.originPremiseDepth > 1, Stopwatch.GetTimestamp() - start);
        var tied = false;
        for (var k = 0; k < closure.LastWork.Insertions; k++)
        {
            for (var source = first[closure.ReachedNode(k)]; source >= 0; source = next[source])
            {
                Mark(kinds, this.retentionAtoms[source]);
                tied = true;
            }
        }

        return tied;
    }

    // SPEC 15.6.4, 5.7 Calls (DECISIONS R4-M116 items 2 and 5, R-B2, BU-30, BU-35, BU-40), the hold walk: a position of `output` the call
    // may write or lend, a hold (retentionHolds: the result's whole Type, or a position of input j's writable storage) or a position below
    // one, with `written`, the Type a struct's Field or an enum's Case payload declares there while it is a Tuple, a fixed array or a raw
    // layer (null elsewhere: off a declaration, or below a Type parameter or another part it does not write inline), and whether it is
    // `lent`: below a borrow in the result (BorrowReferent: a borrow layer, a pair layer admitting only borrows, a Slice's or UniqSlice's
    // elements) or below a result raw layer stating an input's or slot's Origin (stepping Exclusive or Shared: it retains as that borrow
    // would, SPEC 5.7 Calls, so what it addresses is that borrow's storage, 5.2.1; PLAN G255's C1). Each raw input other than `output`
    // whose Type the position can hold retains its own value there (R-B4: a raw layer stating an Origin or `static` that agrees with it,
    // Origins fitted: their erasures, TyCtxt.Erase, are one Type, SPEC 3.8; or an abstract part, which an instance may make one); at a raw
    // layer without an Origin rule A or R′ decides it (RetentionRows); a pointer is never part of its own pointee. A raw layer steps by
    // DeclaredStep (its declaration's cap where `written` states the layer, its Origin's kind elsewhere, DECISIONS BU-24): Unstated is
    // what the call writes there (Holding.Unstated) or lends (Holding.Lent), and Static is storage the body does not own (Holding.Static,
    // lent or not). A value stored at a writable position brings its whole Type (BU-1), so the walk goes below every layer that stores
    // (IsCallable), as RetentionParts' writable positions do, and into a struct's or enum's stored content. What a position gives depends
    // on its Type, `written` and `lent` alone and only accumulates, so each position is walked at most once per lent bit and output
    // (retentionMet): the work is linear in the distinct positions, not in the paths to them through the interned Types (V16). A struct or
    // enum whose storage is a Limit, a growing one among them (PrepareTypeStorage's limits, DECISIONS BU-12), makes the contract
    // CallRetention.Limit; Rejected storage is walked as far as it substitutes, a part that does not (null) holding nothing (Readable,
    // BU-36).
    private Holding Held(BoundType? type, BoundType? written, int output, Kept[] kept, bool lent)
    {
        if (type is null)
        {
            return Holding.None;
        }

        written = ReferenceTypes.IsPointer(written) || written?.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray ? written : null;
        if (!this.retentionMet.Add((type, written, lent)))
        {
            return Holding.None;
        }

        var nominal = AdtDef.IsStruct(type) || AdtDef.IsEnum(type);
        if (nominal && !this.Readable(type, this.PrepareTypeStorage(type)))
        {
            this.retentionLimit = true;
            return Holding.None;
        }

        RawStep? step = type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw } ? DeclaredStep(type, written) : null;
        foreach (var (input, pointer) in this.retentionRawInputs)
        {
            var holds = type.IsAbstract || (step is not (null or RawStep.Unstated) && ReferenceEquals(this.types.Erase(type), this.types.Erase(pointer)));
            kept[input] |= input != output && holds ? Kept.Value : Kept.None;
        }

        var met = step switch
        {
            RawStep.Static => Holding.Static,
            RawStep.Unstated when lent => Holding.Lent,
            RawStep.Unstated => Holding.Unstated,
            _ => Holding.None,
        };
        var below = lent ||
            (output == Retention.Result && (this.BorrowReferent(type, out _, out _) is not null || step is RawStep.Exclusive or RawStep.Shared));
        met |= nominal ? this.Stores(type, output, kept, below) : Holding.None;
        for (var i = 0; !IsCallable(type) && i < type.Components.Count; i++)
        {
            met |= this.Held(type.Components[i], InlinePart(written, i), output, kept, below);
        }

        return met;
    }

    // The hold walk (Held) of a struct's or enum's stored content (SPEC 5.7 Slots and ownership): its base, and its Fields and Case
    // payloads with the Types their declarations write; what they met. A Case's payloads are walked only where its instance
    // substituted that Case (set in CaseTypes), the part the storage verdict walked (DECISIONS BU-37): the per-part rule at the Case's
    // grain, which bounds the walk, so a failed Case's other payloads hold nothing (walked by payload, a failed Case's growing siblings
    // would not end).
    private Holding Stores(BoundType instance, int output, Kept[] kept, bool lent)
    {
        if (instance.Symbol?.Adt is not { } adt)
        {
            return Holding.None;
        }

        var met = AdtDef.Base(instance) is { } parent ? this.Held(parent, null, output, kept, lent) : Holding.None;
        for (var i = 0; AdtDef.IsStruct(instance) && i < adt.Fields.Length; i++)
        {
            met |= this.Held(this.StoredType(adt.Fields[i], instance), WrittenType(adt.Fields[i]), output, kept, lent);
        }

        var cases = AdtDef.CaseTypes(instance) ?? [];
        for (var i = 0; i < cases.Length; i++)
        {
            for (var j = 0; cases[i] is not null && adt.Cases[i] is { } selected && j < selected.Payload.Length; j++)
            {
                met |= this.Held(this.StoredType(selected.Payload[j], instance), WrittenType(selected.Payload[j]), output, kept, lent);
            }
        }

        return met;
    }
}
