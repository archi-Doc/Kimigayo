// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the value identity of a syntax location across compiler phases.
// Input: a module's numbered node. Output: its module ordinal and SyntaxId.
// The default names no node; later phases use this pair when referring to syntax.
// Must not carry syntax structure or semantic state.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>A syntax node as a value: the ordinal of the module whose <see cref="SyntaxTable"/> numbers it and its
/// <see cref="Koto.SyntaxId"/> there. MIR and the records made from it name syntax only through it; the default names no node.</summary>
/// <param name="Module">The module ordinal (<see cref="SyntaxTable.Module"/>).</param>
/// <param name="Id">The node's id in that module; 0 is no node.</param>
internal readonly record struct SourceRef(ushort Module, int Id)
{
    /// <summary>Gets the reference of a node, giving a node without an id the next id of its module.</summary>
    /// <param name="node">The node.</param>
    /// <returns>Its module ordinal and id.</returns>
    internal static SourceRef Of(Koto node)
    {
        var id = SyntaxTable.EnsureId(node);
        return new(node.SyntaxOwner!.Module, id);
    }
}

/// <summary>Where a MIR local, statement or terminator comes from (plan 5.4): its syntax, its scope in the body and whether it
/// starts a source statement, where debug information steps.</summary>
/// <param name="Syntax">The syntax.</param>
/// <param name="Scope">The index of its scope in the body's scope table.</param>
/// <param name="StatementStart">Whether it starts a source statement.</param>
internal readonly record struct SourceInfo(SourceRef Syntax, ushort Scope, bool StatementStart);
