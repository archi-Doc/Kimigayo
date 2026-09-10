// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Parsing;

/// <summary>Walks owned syntax directly without allocating child iterators or temporary arrays.</summary>
public class KotoVisitor
{
    /// <summary>Visits a node and its attributes and children.</summary>
    /// <param name="node">The node to visit.</param>
    public virtual void Visit(Koto node) => node.VisitChildren(this);

    /// <summary>Visits a compact array or list by index.</summary>
    /// <typeparam name="T">The child type.</typeparam>
    /// <param name="nodes">The children, if present.</param>
    public void VisitMany<T>(IReadOnlyList<T>? nodes)
        where T : Koto
    {
        if (nodes is not null)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                this.Visit(nodes[i]);
            }
        }
    }

    internal static int Count<T>(IReadOnlyList<T> nodes) => nodes.Count;
}
