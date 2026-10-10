// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

#pragma warning disable SA1401 // Fields should be private: the columns are indexed in place.

namespace Kimi.Compiler;

/// <summary>
/// The base HIR columns of one module, indexed by <see cref="Koto.SyntaxId"/>: each node's Binding state, failure, meaning (a
/// Type or an Origin, which occupy different namespaces), selected symbol and erased Function Type. The module's
/// <see cref="SyntaxTable"/> sizes them with its ids, keeps them across warm rebinds and moves each row with its node when it
/// renumbers. A node without an id has the default row; its first write gives it one.
/// </summary>
internal struct HirTables
{
    internal BindingState[] States;
    internal BindingFailure[] Failures;
    internal object?[] Meanings;
    internal BindingSymbol?[] Symbols;
    internal BoundType?[] ErasedTypes;

    internal HirTables(int length)
    {
        this.States = new BindingState[length];
        this.Failures = new BindingFailure[length];
        this.Meanings = new object?[length];
        this.Symbols = new BindingSymbol?[length];
        this.ErasedTypes = new BoundType?[length];
    }

    internal static BindingState State(Koto node) => node.SyntaxOwner is { } table ? table.Hir.States[node.SyntaxId] : default;

    internal static BindingFailure Failure(Koto node) => node.SyntaxOwner is { } table ? table.Hir.Failures[node.SyntaxId] : default;

    internal static object? Meaning(Koto node) => node.SyntaxOwner is { } table ? table.Hir.Meanings[node.SyntaxId] : null;

    internal static BindingSymbol? Symbol(Koto node) => node.SyntaxOwner is { } table ? table.Hir.Symbols[node.SyntaxId] : null;

    internal static BoundType? ErasedType(Koto node) => node.SyntaxOwner is { } table ? table.Hir.ErasedTypes[node.SyntaxId] : null;

    /// <summary>Gets the columns that hold a node's row for a write, giving the node an id first when it has none.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The columns of the node's table.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ref HirTables Of(Koto node)
    {
        SyntaxTable.EnsureId(node);
        return ref node.SyntaxOwner!.Hir;
    }

    /// <summary>Clears a node's row, as Binding's first visit of a pass does.</summary>
    /// <param name="node">The node.</param>
    internal static void Reset(Koto node)
    {
        if (node.SyntaxOwner is { } table)
        {
            table.Hir.Copy(0, table.Hir, node.SyntaxId);
        }
    }

    /// <summary>Resizes every column.</summary>
    /// <param name="length">The new length.</param>
    internal void Resize(int length)
    {
        Array.Resize(ref this.States, length);
        Array.Resize(ref this.Failures, length);
        Array.Resize(ref this.Meanings, length);
        Array.Resize(ref this.Symbols, length);
        Array.Resize(ref this.ErasedTypes, length);
    }

    /// <summary>Copies one row; row 0, which no node has, is the default row.</summary>
    /// <param name="from">The source row.</param>
    /// <param name="target">The destination columns, these included.</param>
    /// <param name="to">The destination row.</param>
    internal readonly void Copy(int from, HirTables target, int to)
    {
        target.States[to] = this.States[from];
        target.Failures[to] = this.Failures[from];
        target.Meanings[to] = this.Meanings[from];
        target.Symbols[to] = this.Symbols[from];
        target.ErasedTypes[to] = this.ErasedTypes[from];
    }
}
