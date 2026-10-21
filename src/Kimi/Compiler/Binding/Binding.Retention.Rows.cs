// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;

namespace Kimi.Compiler;

// The rows of TyCtxt.Retains (Binding.Retention.cs computes the atoms and premises they are read from).
public sealed partial class Binding
{
    // The rows of the contract being computed, reused.
    private readonly List<Retention> retentionRows = new();

    // An input retained through one of its occurrences: Value over Content.
    private static void Mark(RetentionKind?[] kinds, in RetentionAtom atom)
        => kinds[atom.Input] = atom.Kind == RetentionKind.Value ? RetentionKind.Value : kinds[atom.Input] ?? RetentionKind.Content;

    // One closure from each output atom (OriginPremiseClosure, every Semantics case's premises followed) marks the inputs whose atoms it
    // reaches, Value over Content, a carrier's through its node. The result is untied when one of its non-static atoms marks no input. The
    // work is what the closures reach, each node with its incoming premises once per closure.
    private CallRetention RetentionRows(OriginPremiseEnvironment environment, int[] nodes, int inputCount)
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
        var untied = false;
        var kinds = new RetentionKind?[inputCount];
        for (var output = Retention.Result; output < inputCount; output++)
        {
            Array.Clear(kinds);
            for (var i = 0; i < atoms.Count; i++)
            {
                if (atoms[i].Input == output && (output == Retention.Result || atoms[i].Writable))
                {
                    var tied = this.MarkClosure(environment.Closure, nodes[i], first, next, kinds);
                    untied |= output == Retention.Result && !tied && atoms[i].Origin?.Kind != OriginKind.Static;
                }
            }

            for (var i = 0; i < inputCount; i++)
            {
                if (kinds[i] is { } kind)
                {
                    rows.Add(new(i, kind, output));
                }
            }
        }

        return new(rows.ToArray(), untied);
    }

    // The closure from one target node, marking the inputs whose atoms it reaches; whether it marked one.
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
}
