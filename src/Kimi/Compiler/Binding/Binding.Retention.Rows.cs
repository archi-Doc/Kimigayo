// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The rows of TyCtxt.Retains (Binding.Retention.cs computes the atoms and premises they are read from).
public sealed partial class Binding
{
    // The rows of the contract being computed, the inputs whose writable storage may receive a pointer tied to no input, the structs and
    // enums a stored-content walk has met (StoresUntied) and the inline path it is at (StoredStep's positions), reused.
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
    // result with a raw layer without an Origin retains every input as Value, a raw one also as Content, and is untied (SPEC 5.7 Calls,
    // D6); one the call forms within a raw parameter's storage (`within`) retains that argument's own value. A raw input's own value is a
    // Value row of the outputs that can hold it (Holds), and an output whose struct or enum stores an untied raw layer is untied (Untied,
    // UntiedStores; DECISIONS R4-M116). The work is what the closures reach, each node with its incoming premises once per closure, and
    // the stored-content walks of the outputs' structs and enums. CallRetention.Limit where a stored Type is not instantiated.
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
                untied = this.ResultRetains(kinds, owned, within);
            }

            for (var i = 0; i < atoms.Count; i++)
            {
                if (atoms[i].Input == output && (output == Retention.Result || atoms[i].Writable))
                {
                    var tied = this.MarkClosure(environment.Closure, nodes[i], first, next, kinds);
                    untied |= output == Retention.Result && !tied && atoms[i].Origin?.Kind != OriginKind.Static;
                }
            }

            var stores = this.HeldBy(output, owned);
            untied |= stores && output == Retention.Result;
            if (stores && output >= 0)
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
    // Fields, retains every input as Value, a raw input also with a read through it, and is untied; a result the call forms within a raw
    // parameter's storage (KimiLibraryCatalog.Within: Raw.slice's `storage`) retains that argument's own value. Whether it is untied.
    private bool ResultRetains(RetentionKind?[] kinds, bool[] owned, int within)
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

        return this.retentionUnstatedResult;
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

    // SPEC 15.6.4, 5.7 Calls (DECISIONS R4-M116 items 2 and 5, R-B2): what `output`'s holds (retentionHolds: positions of the result, or
    // of input j's writable storage) may hold: each raw input other than j that a hold can hold (Holds) retains its own value there, never
    // input j's own, since a pointer is never part of its own pointee; and whether a struct or enum there stores a raw layer stepping
    // Unstated or Static (StoresUntied), a pointer the Type's Fields alone state, which a call that forms it there ties to no argument.
    private bool HeldBy(int output, bool[] owned)
    {
        var untied = false;
        foreach (var (at, type) in this.retentionHolds)
        {
            if (at != output)
            {
                continue;
            }

            this.Holds(type, output, owned);
            this.retentionStored.Clear();
            untied |= (AdtDef.IsStruct(type) || AdtDef.IsEnum(type)) && this.StoresUntied(type, output, owned);
        }

        return untied;
    }

    // SPEC 15.6.4 (DECISIONS R4-M116, R-B4): whether a raw input other than `output` can leave its own value at a position of `type`: a raw
    // layer agreeing with the input's Type under normalized identity with Origins fitted (SPEC 3.8: their erasures, TyCtxt.Erase, are one
    // Type), or an abstract part, which an instance may make one.
    private void Holds(BoundType type, int output, bool[] owned)
    {
        var raw = type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw };
        foreach (var (input, pointer) in this.retentionRawInputs)
        {
            owned[input] |= input != output && (type.IsAbstract || (raw && ReferenceEquals(this.types.Erase(type), this.types.Erase(pointer))));
        }
    }

    // A struct's or enum's stored content (SPEC 5.7 Slots and ownership: its inline storage, its base's, its Fields and Case payloads, their
    // Tuple and fixed-array elements, nested structs' and enums', and a `during self` pointee), each struct or enum once per walk: the
    // raw layers and abstract parts there may hold a raw input's own value (Holds); whether a raw layer there steps Unstated or Static.
    private bool StoresUntied(BoundType instance, int output, bool[] owned)
    {
        if (!this.retentionStored.Add(instance) || instance.Symbol?.Adt is not { } adt)
        {
            return false;
        }

        var start = this.retentionPath.Count;
        var untied = AdtDef.Base(instance) is { } parent && this.StoresUntied(parent, output, owned);
        for (var i = 0; AdtDef.IsStruct(instance) && i < adt.Fields.Length; i++)
        {
            untied |= this.StoredPart(instance, i, start, this.StoredType(adt.Fields[i], instance), output, owned);
        }

        for (var i = 0; AdtDef.IsEnum(instance) && i < adt.Cases.Length; i++)
        {
            for (var j = 0; adt.Cases[i] is { } selected && j < selected.Payload.Length; j++)
            {
                this.retentionPath.Add(j);
                untied |= this.StoredPart(instance, i, start, this.StoredType(selected.Payload[j], instance), output, owned);
                this.retentionPath.RemoveAt(this.retentionPath.Count - 1);
            }
        }

        return untied;
    }

    // A part of `instance`'s stored content: a raw layer on Field or Case `part`'s inline path from `start` steps as the stored reader says
    // (StoredStep), one past a `during self` pointee (`part` -1) by its Origin's kind, as any layer no declaration states there (DECISIONS
    // BU-24); a `during self` layer's pointee is stored content too. A part Binding's storage instantiation does not complete makes the
    // contract CallRetention.Limit.
    private bool StoredPart(BoundType instance, int part, int start, BoundType? type, int output, bool[] owned)
    {
        if (type is null)
        {
            this.retentionLimit = true;
            return false;
        }

        this.Holds(type, output, owned);
        if (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw })
        {
            var step = part >= 0 ? this.StoredStep(instance, part, CollectionsMarshal.AsSpan(this.retentionPath)[start..]) : StepOf(type.RawOrigin);
            return step is RawStep.Unstated or RawStep.Static ||
                (step == RawStep.Own && this.StoredPart(instance, -1, start, type.Components[0], output, owned));
        }

        var untied = (AdtDef.IsStruct(type) || AdtDef.IsEnum(type)) && this.StoresUntied(type, output, owned);
        for (var i = 0; type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray && i < type.Components.Count; i++)
        {
            this.retentionPath.Add(i);
            untied |= this.StoredPart(instance, part, start, type.Components[i], output, owned);
            this.retentionPath.RemoveAt(this.retentionPath.Count - 1);
        }

        return untied;
    }
}
