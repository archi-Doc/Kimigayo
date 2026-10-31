// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the Binding facts and published HIR columns indexed by a module's SyntaxId.
// Input: Binding's decisions and syntax identity. Output: reusable columns and PlanRows for readers.
// Column rows move with renumbered nodes and are cleared for the current pass.
// Must not let consumers reconstruct semantic facts from syntax.

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
/// The published columns, <see cref="Ops"/>, <see cref="Res"/>, <see cref="Completions"/>, <see cref="Adjusts"/> and
/// <see cref="Places"/>, are empty until <see cref="Publish"/> fills them for a pass.
/// </summary>
internal struct HirTables
{
    internal BindingState[] States;
    internal BindingFailure[] Failures;
    internal object?[] Meanings;
    internal BindingSymbol?[] Symbols;
    internal BoundType?[] ErasedTypes;
    internal int[] PlanIndexes;
    internal HirOp[]? Ops;
    internal HirRes[]? Res;
    internal HirCompletion[]? Completions;
    internal HirAdjust[]? Adjusts;
    internal HirPlace[]? Places;
    internal List<DefaultDecl>? Defaults; // The parameter defaults the latest publish evaluated, in walk order: the MIR's default templates.
    internal List<Koto>? Broken; // The nodes Binding resolved that the latest publish found without the facts their Op guarantees, published Error.

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

    // Resolve the state first: Compilation.Binding is lazy and must remain uncreated for unresolved nodes.
    internal static bool HasCurrentBinding(Koto node) => State(node) == BindingState.Resolved &&
        (node.CodeContext.Compilation.Binding.IsRunning || node.CodeContext.Compilation.Binding.Result != default);

    internal static BindingFailure Failure(Koto node) => node.SyntaxOwner is { } table ? table.Hir.Failures[node.SyntaxId] : default;

    internal static object? Meaning(Koto node) => node.SyntaxOwner is { } table ? table.Hir.Meanings[node.SyntaxId] : null;

    internal static BindingSymbol? Symbol(Koto node) => node.SyntaxOwner is { } table ? table.Hir.Symbols[node.SyntaxId] : null;

    internal static BoundType? ErasedType(Koto node) => node.SyntaxOwner is { } table ? table.Hir.ErasedTypes[node.SyntaxId] : null;

    // A node the latest publish did not reach, such as one synthesized after it, reads as unpublished.
    internal static HirOp Op(Koto node) => node.SyntaxOwner?.Hir.Ops is { } ops && node.SyntaxId < ops.Length ? ops[node.SyntaxId] : default;

    internal static HirRes ResOf(Koto node) => node.SyntaxOwner?.Hir.Res is { } res && node.SyntaxId < res.Length ? res[node.SyntaxId] : default;

    internal static HirCompletion CompletionOf(Koto node) => node.SyntaxOwner?.Hir.Completions is { } completions && node.SyntaxId < completions.Length ? completions[node.SyntaxId] : default;

    internal static HirAdjust AdjustOf(Koto node) => node.SyntaxOwner?.Hir.Adjusts is { } adjusts && node.SyntaxId < adjusts.Length ? adjusts[node.SyntaxId] : default;

    internal static HirPlace PlaceOf(Koto node) => node.SyntaxOwner?.Hir.Places is { } places && node.SyntaxId < places.Length ? places[node.SyntaxId] : default;

    /// <summary>Gets the Type identity a member access or an element access was resolved through (SPEC 8.2.3; DECISIONS BU-72, temporary
    /// until G170's identity query): <c>P is U</c>, whose Subject P is the Type parameter its receiver's Type has through its Semantics
    /// layers and whose RequiredType U is the identical complete Type Binding resolved the access on; null for any other node.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The interned identity, or null.</returns>
    internal static BoundConstraint? IdentityPremise(Koto node) => PlanOf(node).Second as BoundConstraint;

    /// <summary>Publishes the Op, Res, Completion, Adjust and PlaceShape columns of every module from the latest completed Binding pass, once per pass.</summary>
    /// <param name="compilation">The bound compilation.</param>
    /// <returns>The pass the columns describe, 0 before the first.</returns>
    internal static ulong Publish(Compilation compilation) => compilation.Binding.PublishHir();

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
/// constant (a conversion's, or a number literal's value, a ConstIndex position and a Tuple element's position, which
/// <see cref="HirTables.Publish"/> writes), a mode and a flag. Each Koto class fixes what its slots hold; a class reads them by type, so a slot it does not use reads as absent.
/// </summary>
internal struct PlanRow
{
    internal object? Plan;
    internal object? Second;
    internal BoundFormatting? Formatting;
    internal byte Mode;
    internal bool Flag;

    /// <summary>The node's Adjust that the Adjust column cannot hold (DECISIONS R4-M145): the earlier of two calls of one consumer that take
    /// it by different Adjusts, whose later one the column keeps, a compound update's getter beside its setter (SPEC 13.7.2, 11.3; DECISIONS
    /// R4-M109), written at each call input of each pass; or a Place in Discard Context's bare acquisition (SPEC 14.2.2), written at each such
    /// Place of each pass, where the column's unset value is the container discarding its item. Default otherwise. A node three or more
    /// calls take (a <c>$tryWrite</c>'s storage) has no defined value here (PLAN G103).</summary>
    internal HirAdjust Adjust;

    // An Int128 would align the row to 16 bytes; two halves keep it at 48.
    private bool folded;
    private ulong foldedHigh;
    private ulong foldedLow;

    /// <summary>Gets or sets the folded constant of a conversion, a number literal's value, a ConstIndex position or a Tuple element's
    /// position.</summary>
    internal Int128? Folded
    {
        readonly get => this.folded ? new Int128(this.foldedHigh, this.foldedLow) : null;
        set => (this.folded, this.foldedHigh, this.foldedLow) = value is { } constant ? (true, (ulong)(constant >> 64), (ulong)constant) : (false, 0UL, 0UL);
    }
}
