// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Owns the Origin summary of an interned Type (DECISIONS BU-63): the Origin expression nodes its parts' Origins and Origin arguments
// state (Nodes) and those its raw layers state (RawNodes), each distinct, a meet node and its operands included, in the order a walk of
// the parts meets them first (its own Origin, its Origin arguments, then each component's), and whether it holds a `during self` layer in
// its inline storage (HoldsHolder: the layer itself, or a Tuple or fixed array with a component holding one). Binding's walks that fold a
// test over the Origins a Type names read the summary instead of walking the parts, which an interned DAG reaches along many paths.
// Input: an interned Type. Output: its summary, formed on its first read from its components' summaries, which are read first.
// Invariants: a summary is a function of the Type alone (an interned Type and its Origins never change; a test that reads a binding state
// runs on the nodes when a walk reads them), so an entry never goes stale and the table is never cleared, as interning is not; a Type
// carrying no Origin has the empty summary and no entry; an array equal element by element to a component's is that component's; a
// warm pass reads its Types' entries and allocates nothing.
// Must not: hold a fact that depends on a query, a pass or a binding state, or be read outside Binding: it is no shared representation.
public sealed partial class Binding
{
    private readonly Dictionary<BoundType, OriginSummary> originSummaries = new(ReferenceEqualityComparer.Instance);
    private readonly List<BoundOrigin> summaryNodes = new(); // A summary's Nodes while it is formed.
    private readonly List<BoundOrigin> summaryRawNodes = new(); // A summary's RawNodes while it is formed.

    // A Type's Origin summary: the table's entry, formed on the first read.
    private OriginSummary SummaryOf(BoundType type)
    {
        if (!type.CarriesAnyOrigin)
        {
            return OriginSummary.Empty;
        }

        if (this.originSummaries.TryGetValue(type, out var known))
        {
            return known;
        }

        // The components first: every one is then read from the table, and the scratch lists below are this Type's alone.
        var holder = type.RawOrigin?.Kind == OriginKind.Holder;
        for (var i = 0; i < type.Components.Count; i++)
        {
            holder |= this.SummaryOf(type.Components[i]).HoldsHolder && type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray;
        }

        var nodes = this.summaryNodes;
        var raw = this.summaryRawNodes;
        nodes.Clear();
        raw.Clear();
        AddNodes(type.Origin, nodes);
        AddNodes(type.RawOrigin, raw);
        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            AddNodes(type.OriginArguments[i], nodes);
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            var part = this.SummaryOf(type.Components[i]);
            AddAll(part.Nodes, nodes);
            AddAll(part.RawNodes, raw);
        }

        BoundOrigin[]? nodeArray = nodes.Count == 0 ? [] : null;
        BoundOrigin[]? rawArray = raw.Count == 0 ? [] : null;
        for (var i = 0; i < type.Components.Count && (nodeArray is null || rawArray is null); i++)
        {
            var part = this.SummaryOf(type.Components[i]);
            nodeArray ??= part.Nodes.AsSpan().SequenceEqual(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(nodes)) ? part.Nodes : null;
            rawArray ??= part.RawNodes.AsSpan().SequenceEqual(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(raw)) ? part.RawNodes : null;
        }

        var summary = new OriginSummary(nodeArray ?? nodes.ToArray(), rawArray ?? raw.ToArray(), holder);
        this.originSummaries.Add(type, summary);
        return summary;

        static void AddNodes(BoundOrigin? origin, List<BoundOrigin> into)
        {
            if (origin is null)
            {
                return;
            }

            if (!into.Contains(origin))
            {
                into.Add(origin);
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                AddNodes(origin.Operands[i], into);
            }
        }

        static void AddAll(BoundOrigin[] part, List<BoundOrigin> into)
        {
            for (var i = 0; i < part.Length; i++)
            {
                if (!into.Contains(part[i]))
                {
                    into.Add(part[i]);
                }
            }
        }
    }

    // A Type's Origin summary (Binding.OriginSummaries).
    private readonly record struct OriginSummary(BoundOrigin[] Nodes, BoundOrigin[] RawNodes, bool HoldsHolder)
    {
        internal static readonly OriginSummary Empty = new([], [], false);
    }
}
