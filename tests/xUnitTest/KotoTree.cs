// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Generic;
using Kimi.Compiler.Parsing;

namespace XunitTest;

/// <summary>Syntax tree traversal shared by tests.</summary>
internal static class KotoTree
{
    /// <summary>Gets the direct syntax-tree children of a node, in the order <see cref="Koto.VisitChildren"/> visits them.</summary>
    /// <param name="node">The node.</param>
    /// <returns>A new list of the children.</returns>
    internal static List<Koto> Children(this Koto node)
    {
        var collector = new Collector();
        node.VisitChildren(collector);
        return collector.Nodes;
    }

    /// <summary>Enumerates a node and its written descendants in depth-first source order. A struct's synthesized constructor is a
    /// child the compiler derives, not one the program writes, so it is left out.</summary>
    /// <param name="node">The root node.</param>
    /// <returns>The node and every written descendant.</returns>
    internal static IEnumerable<Koto> Walk(Koto node)
    {
        yield return node;
        foreach (var child in node.Children())
        {
            if (child is FunctionKoto { IsImplicitConstructor: true })
            {
                continue;
            }

            foreach (var nested in Walk(child))
            {
                yield return nested;
            }
        }
    }

    private sealed class Collector : KotoVisitor
    {
        internal List<Koto> Nodes { get; } = [];

        public override void Visit(Koto node) => this.Nodes.Add(node);
    }
}
