// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The rows of TyCtxt.Retains (Binding.Retention.cs computes the atoms and premises they are read from).
public sealed partial class Binding
{
    // The rows of the contract being computed, the inputs whose writable storage may receive a pointer tied to no input, the structs and
    // enums the hold walk of an output has met (Held) and the inline path it is at (StoredStep's positions), reused.
    private readonly List<Retention> retentionRows = new();
    private readonly List<int> retentionUntiedStores = new();
    private readonly HashSet<BoundType> retentionStored = new(ReferenceEqualityComparer.Instance);
    private readonly List<int> retentionPath = new();

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
    // each output's hold walk. CallRetention.Limit where a stored Type is not instantiated.
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

            // The hold walk from each of the output's holds, each struct's or enum's stored content once.
            this.retentionStored.Clear();
            var held = false;
            foreach (var (at, type) in this.retentionHolds)
            {
                held |= at == output && this.Held(type, null, -1, 0, output, owned);
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

    // SPEC 15.6.4, 5.7 Calls (DECISIONS R4-M116 items 2 and 5, R-B2, BU-30), the hold walk: a position of `output` the call may write,
    // a hold (retentionHolds: a position of the result, or of input j's writable storage) or a part of a held struct's or enum's stored
    // content (Field or Case `part` of `instance` on its inline path from `start`; -1 past it). Each raw input other than `output` whose
    // Type the position can hold retains its own value there (R-B4: a raw layer agreeing with it, Origins fitted: their erasures,
    // TyCtxt.Erase, are one Type, SPEC 3.8; or an abstract part, which an instance may make one); a pointer is never part of its own
    // pointee. A raw layer stepping Unstated or Static (StoredStep on an instance's inline path, its Origin's kind elsewhere, DECISIONS
    // BU-24) unties `output`, the result returned: a pointer the call forms there is tied to no argument. The walk goes below a position
    // as RetentionParts' does (Opens: a raw layer's pointee by its step, an exclusive borrow's referent, never past a shared layer) and
    // into a struct's or enum's stored content (SPEC 5.7 Slots and ownership: its base, Fields and Case payloads, their Tuple and
    // fixed-array elements inline). A part Binding's storage instantiation does not complete makes the contract CallRetention.Limit.
    private bool Held(BoundType? type, BoundType? instance, int part, int start, int output, bool[] owned)
    {
        if (type is null)
        {
            this.retentionLimit = true;
            return false;
        }

        var raw = type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw };
        foreach (var (input, pointer) in this.retentionRawInputs)
        {
            owned[input] |= input != output && (type.IsAbstract || (raw && ReferenceEquals(this.types.Erase(type), this.types.Erase(pointer))));
        }

        if (AdtDef.IsStruct(type) || AdtDef.IsEnum(type))
        {
            if (!this.retentionStored.Add(type) || type.Symbol?.Adt is not { } adt)
            {
                return false;
            }

            var first = this.retentionPath.Count;
            var stores = AdtDef.Base(type) is { } parent && this.Held(parent, null, -1, first, output, owned);
            for (var i = 0; AdtDef.IsStruct(type) && i < adt.Fields.Length; i++)
            {
                stores |= this.Held(this.StoredType(adt.Fields[i], type), type, i, first, output, owned);
            }

            for (var i = 0; AdtDef.IsEnum(type) && i < adt.Cases.Length; i++)
            {
                for (var j = 0; adt.Cases[i] is { } selected && j < selected.Payload.Length; j++)
                {
                    this.retentionPath.Add(j);
                    stores |= this.Held(this.StoredType(selected.Payload[j], type), type, i, first, output, owned);
                    this.retentionPath.RemoveAt(first);
                }
            }

            return stores;
        }

        var step = !raw ? RawStep.Unstated
            : part >= 0 ? this.StoredStep(instance!, part, CollectionsMarshal.AsSpan(this.retentionPath)[start..]) : StepOf(type.RawOrigin);
        var untied = raw && step is RawStep.Unstated or RawStep.Static;
        var inline = type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray;
        for (var i = 0; Opens(type, step) && i < type.Components.Count; i++)
        {
            this.retentionPath.Add(i);
            untied |= this.Held(type.Components[i], instance, inline ? part : -1, start, output, owned);
            this.retentionPath.RemoveAt(this.retentionPath.Count - 1);
        }

        return untied;
    }
}
