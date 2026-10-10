// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

#pragma warning disable SA1401 // Fields should be private: the columns are indexed in place.
#pragma warning disable SA1402 // The plan row belongs to its columns.

namespace Kimi.Compiler;

/// <summary>
/// The base HIR columns of one module, indexed by <see cref="Koto.SyntaxId"/>: each node's Binding state, failure, meaning (a
/// Type or an Origin, which occupy different namespaces), selected symbol, erased Function Type and the index of its plan row.
/// The module's <see cref="SyntaxTable"/> sizes them with its ids, keeps them across warm rebinds and moves each row, plan row
/// included, with its node when it renumbers. A node without an id has the default row; its first write gives it one.
/// </summary>
internal struct HirTables
{
    internal BindingState[] States;
    internal BindingFailure[] Failures;
    internal object?[] Meanings;
    internal BindingSymbol?[] Symbols;
    internal BoundType?[] ErasedTypes;
    internal int[] PlanIndexes;

    /// <summary>The plan rows from 1 to <see cref="PlanCount"/>; a node gets one at its first plan write and keeps it across passes.</summary>
    internal PlanRow[] Plans;
    internal int PlanCount;

    private static readonly PlanRow NoPlan;

    // A default plan value written to a node without a plan row lands here; no reader sees this row.
    private static PlanRow discarded;

    internal HirTables(int length, int plans)
    {
        this.States = new BindingState[length];
        this.Failures = new BindingFailure[length];
        this.Meanings = new object?[length];
        this.Symbols = new BindingSymbol?[length];
        this.ErasedTypes = new BoundType?[length];
        this.PlanIndexes = new int[length];
        this.Plans = new PlanRow[plans + 1];
    }

    internal static BindingState State(Koto node) => node.SyntaxOwner is { } table ? table.Hir.States[node.SyntaxId] : default;

    internal static BindingFailure Failure(Koto node) => node.SyntaxOwner is { } table ? table.Hir.Failures[node.SyntaxId] : default;

    internal static object? Meaning(Koto node) => node.SyntaxOwner is { } table ? table.Hir.Meanings[node.SyntaxId] : null;

    internal static BindingSymbol? Symbol(Koto node) => node.SyntaxOwner is { } table ? table.Hir.Symbols[node.SyntaxId] : null;

    internal static BoundType? ErasedType(Koto node) => node.SyntaxOwner is { } table ? table.Hir.ErasedTypes[node.SyntaxId] : null;

    /// <summary>Gets a node's plan row, or the default row when it has none.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The plan row.</returns>
    internal static ref readonly PlanRow PlanOf(Koto node)
    {
        if (node.SyntaxOwner is { } table && table.Hir.PlanIndexes[node.SyntaxId] is var index and not 0)
        {
            return ref table.Hir.Plans[index];
        }

        return ref NoPlan;
    }

    /// <summary>Gets a node's plan row for a write, adding one when a value other than the default is written to a node without one.</summary>
    /// <param name="node">The node.</param>
    /// <param name="written">Whether the written value is not the default.</param>
    /// <returns>The plan row to write.</returns>
    internal static ref PlanRow PlanFor(Koto node, bool written)
    {
        if (node.SyntaxOwner is { } table && table.Hir.PlanIndexes[node.SyntaxId] is var index and not 0)
        {
            return ref table.Hir.Plans[index];
        }

        if (!written)
        {
            return ref discarded;
        }

        ref var hir = ref Of(node);
        var added = hir.AddPlan(default);
        hir.PlanIndexes[node.SyntaxId] = added;
        return ref hir.Plans[added];
    }

    /// <summary>Gets the columns that hold a node's row for a write, giving the node an id first when it has none.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The columns of the node's table.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ref HirTables Of(Koto node)
    {
        SyntaxTable.EnsureId(node);
        return ref node.SyntaxOwner!.Hir;
    }

    /// <summary>Clears a node's base row, as Binding's first visit of a pass does; its plan row stays for reuse.</summary>
    /// <param name="node">The node.</param>
    internal static void Reset(Koto node)
    {
        if (node.SyntaxOwner is { } table)
        {
            table.Hir.CopyBase(0, table.Hir, node.SyntaxId);
        }
    }

    /// <summary>Resizes every column indexed by id.</summary>
    /// <param name="length">The new length.</param>
    internal void Resize(int length)
    {
        Array.Resize(ref this.States, length);
        Array.Resize(ref this.Failures, length);
        Array.Resize(ref this.Meanings, length);
        Array.Resize(ref this.Symbols, length);
        Array.Resize(ref this.ErasedTypes, length);
        Array.Resize(ref this.PlanIndexes, length);
        this.Plans ??= new PlanRow[16];
    }

    /// <summary>Moves one node's row to another table or to the node's new id; its plan row is added to the target's plan rows.</summary>
    /// <param name="from">The source row.</param>
    /// <param name="target">The destination columns.</param>
    /// <param name="to">The destination row.</param>
    internal readonly void Move(int from, ref HirTables target, int to)
    {
        this.CopyBase(from, target, to);
        target.PlanIndexes[to] = this.PlanIndexes[from] is var plan and not 0 ? target.AddPlan(this.Plans[plan]) : 0;
    }

    // Row 0, which no node has, is the default row.
    private readonly void CopyBase(int from, HirTables target, int to)
    {
        target.States[to] = this.States[from];
        target.Failures[to] = this.Failures[from];
        target.Meanings[to] = this.Meanings[from];
        target.Symbols[to] = this.Symbols[from];
        target.ErasedTypes[to] = this.ErasedTypes[from];
    }

    private int AddPlan(PlanRow row)
    {
        if (++this.PlanCount == this.Plans.Length)
        {
            Array.Resize(ref this.Plans, this.PlanCount * 2);
        }

        this.Plans[this.PlanCount] = row;
        return this.PlanCount;
    }
}

/// <summary>
/// The plan slots of one node (the planIndex column): its kind's retained plan, a second plan, its formatting plan, a folded
/// constant, a mode and a flag. Each Koto class fixes what its slots hold; a class reads them by type, so a slot it does not use
/// reads as absent.
/// </summary>
internal struct PlanRow
{
    internal object? Plan;
    internal object? Second;
    internal BoundFormatting? Formatting;
    internal byte Mode;
    internal bool Flag;

    // An Int128 would align the row to 16 bytes; two halves keep it at 48.
    private bool folded;
    private ulong foldedHigh;
    private ulong foldedLow;

    /// <summary>Gets or sets the folded constant of a conversion.</summary>
    internal Int128? Folded
    {
        readonly get => this.folded ? new Int128(this.foldedHigh, this.foldedLow) : null;
        set => (this.folded, this.foldedHigh, this.foldedLow) = value is { } constant ? (true, (ulong)(constant >> 64), (ulong)constant) : (false, 0UL, 0UL);
    }
}
