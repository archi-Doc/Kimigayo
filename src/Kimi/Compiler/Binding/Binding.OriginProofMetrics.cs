// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    /// <summary>Gets or sets the opt-in Origin proof counters; null, the default, collects nothing.</summary>
    internal OriginProofMetrics? OriginProofMetrics { get; set; }
}

/// <summary>
/// Opt-in work counters of Origin proof (PLAN G74). They are collected only while <see cref="Binding.OriginProofMetrics"/> is set,
/// so an unmeasured compilation pays one null check per proof request and allocates nothing.
/// </summary>
internal sealed class OriginProofMetrics
{
    private readonly HashSet<(BoundOrigin Longer, BoundOrigin Shorter, Koto Use)> queries = new();
    private readonly long[] edges = new long[(int)OriginPremiseRule.LocalPlace + 1];

    /// <summary>Gets the top-level proof requests: requests made while no premise environment was active.</summary>
    internal long Requests { get; private set; }

    /// <summary>Gets every proof request, nested ones included.</summary>
    internal long Entries { get; private set; }

    /// <summary>Gets the premise environments built: requests the structural instances did not answer, one closure each.</summary>
    internal long Closures { get; private set; }

    /// <summary>Gets the environments built while another one was being built.</summary>
    internal long NestedClosures { get; private set; }

    /// <summary>Gets the nodes of all environments: the Origin expressions of the queries, premises and their operands.</summary>
    internal long Nodes { get; private set; }

    /// <summary>Gets the premise edges of all environments.</summary>
    internal long Edges { get; private set; }

    /// <summary>Gets the meet operand incidences of all environments.</summary>
    internal long MeetIncidences { get; private set; }

    /// <summary>Gets the most nodes of one environment.</summary>
    internal int MaxNodes { get; private set; }

    /// <summary>Gets the most edges of one environment.</summary>
    internal int MaxEdges { get; private set; }

    /// <summary>Gets the worklist insertions of all closures.</summary>
    internal long Insertions { get; private set; }

    /// <summary>Gets the edge activations of all closures.</summary>
    internal long Activations { get; private set; }

    /// <summary>Gets the operand decrements of all closures.</summary>
    internal long Decrements { get; private set; }

    /// <summary>Gets the time spent building environments and closing them, in Stopwatch ticks.</summary>
    internal long ClosureTicks { get; private set; }

    /// <summary>Gets the closures whose work exceeded their graph: insertions over nodes, activations over edges or decrements over
    /// meet incidences (proposal §3.7); always zero.</summary>
    internal long BoundViolations { get; private set; }

    /// <summary>Gets the obligation verdicts computed: one per obligation per analysis of a Binding (PLAN G74 U4).</summary>
    internal long Verdicts { get; private set; }

    /// <summary>Gets the distinct normalized relations proven at a use.</summary>
    internal int DistinctQueries => this.queries.Count;

    /// <summary>Gets the premise edges a catalog rule introduced in all environments.</summary>
    /// <param name="rule">The rule.</param>
    /// <returns>The edge count.</returns>
    internal long EdgesOf(OriginPremiseRule rule) => this.edges[(int)rule];

    internal void Reset()
    {
        this.Requests = 0;
        this.Entries = 0;
        this.Closures = 0;
        this.NestedClosures = 0;
        this.Nodes = 0;
        this.Edges = 0;
        this.MeetIncidences = 0;
        this.MaxNodes = 0;
        this.MaxEdges = 0;
        this.Insertions = 0;
        this.Activations = 0;
        this.Decrements = 0;
        this.BoundViolations = 0;
        this.Verdicts = 0;
        this.ClosureTicks = 0;
        Array.Clear(this.edges);
        this.queries.Clear();
    }

    internal void Enter(BoundOrigin longer, BoundOrigin shorter, Koto use, int depth)
    {
        this.Entries++;
        if (depth == 0)
        {
            this.Requests++;
        }

        this.queries.Add((longer, shorter, use));
    }

    internal void Verdict() => this.Verdicts++;

    internal void Closure(OriginPremiseClosure closure, bool nested, long ticks)
    {
        this.Closures++;
        this.ClosureTicks += ticks;
        if (nested)
        {
            this.NestedClosures++;
        }

        this.Nodes += closure.NodeCount;
        this.Edges += closure.EdgeCount;
        this.MeetIncidences += closure.MeetIncidences;
        this.MaxNodes = Math.Max(this.MaxNodes, closure.NodeCount);
        this.MaxEdges = Math.Max(this.MaxEdges, closure.EdgeCount);
        var (insertions, activations, decrements) = closure.LastWork;
        this.Insertions += insertions;
        this.Activations += activations;
        this.Decrements += decrements;
        if (insertions > closure.NodeCount || activations > closure.EdgeCount || decrements > closure.MeetIncidences)
        {
            this.BoundViolations++;
        }

        for (var i = 0; i < closure.EdgeCount; i++)
        {
            this.edges[(int)closure.EdgeRule(i)]++;
        }
    }
}
