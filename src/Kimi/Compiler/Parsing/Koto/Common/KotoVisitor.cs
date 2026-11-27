// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Parsing;

/// <summary>Walks owned syntax directly without allocating child iterators or temporary arrays.</summary>
public class KotoVisitor
{
    /// <summary>Visits a node and its attributes and children.</summary>
    /// <param name="node">The node to visit.</param>
    public virtual void Visit(Koto node) => node.VisitChildren(this);

    /// <summary>Visits a source slot (<see cref="ChildSlots.Source"/>): a compile-time directive's marker, or its Conditions and
    /// excluded parts. Excluded syntax receives no semantic check (SPEC 19.4), so the default walks nothing; only a walk of the
    /// source itself overrides it.</summary>
    /// <param name="source">The source slot's nodes.</param>
    public virtual void VisitSource(IReadOnlyList<Koto> source)
    {
    }

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
}
