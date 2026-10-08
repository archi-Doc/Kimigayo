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
/// so an unmeasured compilation pays one null check per proof entry and allocates nothing.
/// </summary>
internal sealed class OriginProofMetrics
{
    private readonly HashSet<(BoundOrigin Longer, BoundOrigin Shorter, Koto Use)> queries = new();

    /// <summary>Gets the top-level proof requests: entries made while no proof was active.</summary>
    internal long Requests { get; private set; }

    /// <summary>Gets every proof entry, recursive subgoals included.</summary>
    internal long Calls { get; private set; }

    /// <summary>Gets the entries refused because the same relation was already on the active search path.</summary>
    internal long PathRejections { get; private set; }

    /// <summary>Gets the deepest active search path observed.</summary>
    internal int MaxDepth { get; private set; }

    /// <summary>Gets the distinct normalized relations proven at a use.</summary>
    internal int DistinctQueries => this.queries.Count;

    internal void Reset()
    {
        this.Requests = 0;
        this.Calls = 0;
        this.PathRejections = 0;
        this.MaxDepth = 0;
        this.queries.Clear();
    }

    internal void Enter(BoundOrigin longer, BoundOrigin shorter, Koto use, int depth)
    {
        this.Calls++;
        if (depth == 0)
        {
            this.Requests++;
        }

        if (depth + 1 > this.MaxDepth)
        {
            this.MaxDepth = depth + 1;
        }

        this.queries.Add((longer, shorter, use));
    }

    internal void Reject() => this.PathRejections++;
}
