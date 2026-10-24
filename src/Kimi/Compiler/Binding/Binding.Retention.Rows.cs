// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The rows of TyCtxt.Retains (Binding.Retention.cs computes the atoms and premises they are read from).
public sealed partial class Binding
{
    // The rows of the contract being computed, the inputs whose writable storage may receive a pointer tied to no input, and the positions
    // the hold walk of an output has met (Held: a Type with the Type its declaration writes there, both interned, so compared by
    // reference), reused.
    private readonly List<Retention> retentionRows = new();
    private readonly List<int> retentionUntiedStores = new();
    private readonly HashSet<(BoundType Type, BoundType? Written)> retentionMet = new();

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
    // result with a raw layer without an Origin retains every input as Value, a raw one also as Content (SPEC 5.7 Calls, D6); one the
    // call forms within a raw parameter's storage (`within`) retains that argument's own value. A raw input's own value is a Value row of
    // the outputs that can hold it, and an output whose hold walk meets a raw layer stepping Unstated or Static is untied (Held: Untied,
    // UntiedStores; DECISIONS R4-M116). The work is what the closures reach, each node with its incoming premises once per closure, and
    // each output's hold walk, each position once (BU-35). CallRetention.Limit where a stored Type is not instantiated.
    private CallRetention RetentionRows(OriginPremiseEnvironment environment, int[] nodes, int inputCount, int within)
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
        var kinds = new RetentionKind?[inputCount];
        var owned = new bool[inputCount];
        for (var output = Retention.Result; output < inputCount; output++)
        {
            Array.Clear(kinds);
            Array.Clear(owned);
            if (output == Retention.Result)
            {
                this.ResultRetains(kinds, owned, within);
            }

            for (var i = 0; i < atoms.Count; i++)
            {
                if (atoms[i].Input == output && (output == Retention.Result || atoms[i].Writable))
                {
                    var tied = this.MarkClosure(environment.Closure, nodes[i], first, next, kinds);
                    untied |= output == Retention.Result && !tied && atoms[i].Origin?.Kind != OriginKind.Static;
                }
            }

            // The hold walk from each of the output's holds, each position once.
            this.retentionMet.Clear();
            var held = false;
            foreach (var (at, type) in this.retentionHolds)
            {
                held |= at == output && this.Held(type, null, output, owned);
            }

            untied |= held && output == Retention.Result;
            if (held && output >= 0)
            {
                this.retentionUntiedStores.Add(output);
            }

            for (var i = 0; i < inputCount; i++)
            {
                if (owned[i])
                {
                    rows.Add(new(i, RetentionKind.Value, output));
                }

                if (kinds[i] is { } kind)
                {
                    rows.Add(new(i, kind, output));
                }
            }
        }

        return this.retentionLimit ? CallRetention.Limit : new(rows.ToArray(), untied, [.. this.retentionUntiedStores]);
    }

    // SPEC 5.7 Calls, 15.6.4 (D6; DECISIONS R4-M116 items 3 and 4): a result with a raw layer without an Origin, outside a nominal Type's
    // Fields, retains every input as Value, a raw input also with a read through it (the layer, one of its holds, unties it: Held); a
    // result the call forms within a raw parameter's storage (KimiLibraryCatalog.Within: Raw.slice's `storage`) retains that argument's
    // own value.
    private void ResultRetains(RetentionKind?[] kinds, bool[] owned, int within)
    {
        if (this.retentionUnstatedResult)
        {
            Array.Fill(kinds, RetentionKind.Value);
            foreach (var (input, _) in this.retentionRawInputs)
            {
                (owned[input], kinds[input]) = (true, RetentionKind.Content);
            }
        }

        if (within >= 0)
        {
            owned[within] = true;
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

    // SPEC 15.6.4, 5.7 Calls (DECISIONS R4-M116 items 2 and 5, R-B2, BU-30, BU-35), the hold walk: a position of `output` the call may
    // write, a hold (retentionHolds: a position of the result, or of input j's writable storage) or a position below one, with `written`,
    // the Type a struct's Field or an enum's Case payload declares there while it is a Tuple, a fixed array or a raw layer (null elsewhere:
    // off a declaration, or below a Type parameter or another part it does not write inline). Each raw input other than `output` whose
    // Type the position can hold retains its own value there (R-B4: a raw layer agreeing with it, Origins fitted: their erasures,
    // TyCtxt.Erase, are one Type, SPEC 3.8; or an abstract part, which an instance may make one); a pointer is never part of its own
    // pointee. A raw layer stepping Unstated or Static (DeclaredStep: its declaration's cap where `written` states the layer, its Origin's
    // kind elsewhere, DECISIONS BU-24) unties `output`, the result returned: a pointer the call forms there is tied to no argument. A value
    // stored at a writable position brings its whole Type (BU-1), so the walk goes below every layer that stores (IsCallable), as
    // RetentionParts' writable positions do, and into a struct's or enum's stored content. What a position gives depends on its Type and
    // `written` alone and only accumulates, so each position is walked once per output (retentionMet): the work is linear in the distinct
    // positions, not in the paths to them through the interned Types (V16). A Type whose storage Binding does not complete, a growing one
    // among them (PrepareTypeStorage's limits, DECISIONS BU-12), makes the contract CallRetention.Limit.
    private bool Held(BoundType? type, BoundType? written, int output, bool[] owned)
    {
        if (type is null)
        {
            this.retentionLimit = true;
            return false;
        }

        written = ReferenceTypes.IsPointer(written) || written?.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray ? written : null;
        if (!this.retentionMet.Add((type, written)))
        {
            return false;
        }

        var nominal = AdtDef.IsStruct(type) || AdtDef.IsEnum(type);
        if (nominal && !this.PrepareTypeStorage(type))
        {
            this.retentionLimit = true;
            return false;
        }

        var raw = type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw };
        foreach (var (input, pointer) in this.retentionRawInputs)
        {
            owned[input] |= input != output && (type.IsAbstract || (raw && ReferenceEquals(this.types.Erase(type), this.types.Erase(pointer))));
        }

        var untied = raw && DeclaredStep(type, written) is RawStep.Unstated or RawStep.Static;
        untied |= nominal && this.Stores(type, output, owned);
        for (var i = 0; !IsCallable(type) && i < type.Components.Count; i++)
        {
            untied |= this.Held(type.Components[i], InlinePart(written, i), output, owned);
        }

        return untied;
    }

    // The hold walk (Held) of a struct's or enum's stored content (SPEC 5.7 Slots and ownership): its base, and its Fields and Case
    // payloads with the Types their declarations write; whether one unties `output`.
    private bool Stores(BoundType instance, int output, bool[] owned)
    {
        if (instance.Symbol?.Adt is not { } adt)
        {
            return false;
        }

        var untied = AdtDef.Base(instance) is { } parent && this.Held(parent, null, output, owned);
        for (var i = 0; AdtDef.IsStruct(instance) && i < adt.Fields.Length; i++)
        {
            untied |= this.Held(this.StoredType(adt.Fields[i], instance), WrittenType(adt.Fields[i]), output, owned);
        }

        for (var i = 0; AdtDef.IsEnum(instance) && i < adt.Cases.Length; i++)
        {
            for (var j = 0; adt.Cases[i] is { } selected && j < selected.Payload.Length; j++)
            {
                untied |= this.Held(this.StoredType(selected.Payload[j], instance), WrittenType(selected.Payload[j]), output, owned);
            }
        }

        return untied;
    }
}
