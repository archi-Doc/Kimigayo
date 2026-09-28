// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Generic;
using Kimi.Compiler.Parsing;

namespace XunitTest;

/// <summary>Syntax tree traversal shared by tests.</summary>
internal static class KotoTree
{
    /// <summary>Enumerates a node and its descendants in depth-first source order.</summary>
    /// <param name="node">The root node.</param>
    /// <returns>The node and every descendant.</returns>
    internal static IEnumerable<Koto> Walk(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var nested in Walk(child))
            {
                yield return nested;
            }
        }
    }
}
