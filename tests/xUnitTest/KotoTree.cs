// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Generic;
using Kimi.Compiler.Parsing;

namespace XunitTest;

/// <summary>Syntax tree traversal shared by tests.</summary>
internal static class KotoTree
{
    /// <summary>Enumerates a node and its written descendants in depth-first source order. A struct's synthesized constructor is a
    /// child the compiler derives, not one the program writes, so it is left out.</summary>
    /// <param name="node">The root node.</param>
    /// <returns>The node and every written descendant.</returns>
    internal static IEnumerable<Koto> Walk(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
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
}
