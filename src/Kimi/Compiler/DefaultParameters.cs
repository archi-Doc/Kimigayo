// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Lexical identity of a prepared parameter read from a later default (SPEC 7.2.3).</summary>
internal static class DefaultParameters
{
    /// <summary>Gets whether a node lies in the default of a later parameter of the function that declares a parameter, so that
    /// the parameter names the slot its call prepared, which the default can neither move nor keep a borrow of (SPEC 7.2.3).</summary>
    /// <param name="node">The node, such as a closure.</param>
    /// <param name="symbol">The parameter it reads.</param>
    /// <returns>Whether the node is in a later default of the parameter's function.</returns>
    internal static bool InLaterDefault(Koto node, BindingSymbol symbol)
    {
        if (symbol.Kind != BindingSymbolKind.Parameter || symbol.Scope.Owner is not FunctionKoto declaration)
        {
            return false;
        }

        Koto child = node;
        for (var parent = node.Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            if (ReferenceEquals(parent, declaration))
            {
                for (var i = symbol.Slot + 1; i < declaration.Parameters.Count; i++)
                {
                    if (ReferenceEquals(declaration.Parameters[i].DefaultValue, child))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        return false;
    }
}
