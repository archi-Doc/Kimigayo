// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the Binding facts and published HIR columns indexed by a module's SyntaxId, and Binding's private node families (NodeColumns).
// Input: Binding's decisions and syntax identity. Output: reusable columns and PlanRows for readers.
// Column rows move with renumbered nodes and are cleared for the current pass; a node family's kept rows move with their nodes too,
// and a pass or edit family's rows read absent once the counter its column follows advances, until its first write (StampedColumn).
// Must not let consumers reconstruct semantic facts from syntax; no file outside Compiler/Binding names NodeColumns (DECISIONS BU-220).

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
/// The published columns, <see cref="Ops"/>, <see cref="Completions"/>, <see cref="Adjusts"/> and <see cref="Places"/>, are
/// empty until <see cref="Publish"/> fills them for a pass.
/// </summary>
internal struct HirTables
{
    internal BindingState[] States;
    internal BindingFailure[] Failures;
    internal object?[] Meanings;
    internal BindingSymbol?[] Symbols;
    internal PathEnd[] Lookups; // How a Type-name use's published lookup ended (Binding.NamePath, R7-N1e2-1R); None before it.
    internal BoundType?[] ErasedTypes;
    internal int[] PlanIndexes;
    internal HirOp[]? Ops;
    internal HirCompletion[]? Completions;
    internal HirAdjust[]? Adjusts;
    internal HirPlace[]? Places;
    internal List<DefaultDecl>? Defaults; // The parameter defaults the latest publish evaluated, in walk order: the MIR's default templates.
    internal List<Koto>? Broken; // The nodes Binding resolved that the latest publish found without the facts their Op guarantees, published Error.
    internal NodeColumns Families; // Binding's private node families, read and written only by their accessors (DECISIONS BU-220).

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
        this.Lookups = new PathEnd[length];
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

    // The node's resolved Type: its Meaning when that is a Type, else null (an Origin, an unresolved node).
    internal static BoundType? TypeOf(Koto node) => Meaning(node) as BoundType;

    internal static BindingSymbol? Symbol(Koto node) => node.SyntaxOwner is { } table ? table.Hir.Symbols[node.SyntaxId] : null;

    internal static PathEnd LookupOf(Koto node) => node.SyntaxOwner is { } table ? table.Hir.Lookups[node.SyntaxId] : default;

    // The Symbols column's one writer and its contract (R7-N1e2-1R): a use whose published lookup selected keeps its selection
    // (Binding.NamePath), so a later write must be equal; and no node's entry is a bound Contract reference, which is a use's Type
    // (Binding.CompleteQualifier, SPEC 8.6.1, 9.7.1). Another meaning is written where it belongs, never over a selection.
    internal static void WriteSymbol(Koto node, BindingSymbol? symbol)
    {
        ref var hir = ref Of(node);
        if ((hir.Lookups[node.SyntaxId] == PathEnd.Selected && !ReferenceEquals(hir.Symbols[node.SyntaxId], symbol)) ||
            (symbol is { Declaration: ContractKoto, Type: { } bound } && !ReferenceEquals(bound.Symbol, symbol)))
        {
            throw new InvalidOperationException($"`{node}` held {hir.Symbols[node.SyntaxId]}, then was written {symbol} (InternalInvariant; R7-N1e2-1R).");
        }

        hir.Symbols[node.SyntaxId] = symbol;
    }

    internal static BoundType? ErasedType(Koto node) => node.SyntaxOwner is { } table ? table.Hir.ErasedTypes[node.SyntaxId] : null;

    // A node the latest publish did not reach, such as one synthesized after it, reads as unpublished.
    internal static HirOp Op(Koto node) => node.SyntaxOwner?.Hir.Ops is { } ops && node.SyntaxId < ops.Length ? ops[node.SyntaxId] : default;

    // What a node's selected symbol names, derived from the symbol, so no column repeats it; a parameter read in the default of a later
    // parameter of its function names the prepared slot (SPEC 7.2.3).
    internal static HirRes ResOf(Koto node) => Symbol(node) is not { } symbol ? HirRes.None : symbol.Kind switch
    {
        BindingSymbolKind.Local or BindingSymbolKind.PatternCandidate => HirRes.Local,
        BindingSymbolKind.Parameter => DefaultParameters.InLaterDefault(node, symbol) ? HirRes.PreparedParam : HirRes.Param,
        BindingSymbolKind.Capture => HirRes.Capture,
        BindingSymbolKind.Storage => HirRes.Field,
        BindingSymbolKind.Property => Binding.IsStatic(symbol) ? HirRes.Static : HirRes.Field,
        BindingSymbolKind.EnumCase => HirRes.Variant,
        _ => HirRes.Decl,
    };

    internal static HirCompletion CompletionOf(Koto node) => node.SyntaxOwner?.Hir.Completions is { } completions && node.SyntaxId < completions.Length ? completions[node.SyntaxId] : default;

    internal static HirAdjust AdjustOf(Koto node) => node.SyntaxOwner?.Hir.Adjusts is { } adjusts && node.SyntaxId < adjusts.Length ? adjusts[node.SyntaxId] : default;

    internal static HirPlace PlaceOf(Koto node) => node.SyntaxOwner?.Hir.Places is { } places && node.SyntaxId < places.Length ? places[node.SyntaxId] : default;

    /// <summary>Gets the Type identity a member access or an element access was resolved through (SPEC 8.2.3; DECISIONS BU-72):
    /// <c>P is U</c>, whose Subject P is the Type parameter its receiver's Type has through its Semantics layers and whose RequiredType U
    /// is the identical complete Type Binding resolved the access on; null for any other node.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The interned identity, or null.</returns>
    [Transitional("R7", Reason = "DECISIONS BU-72: removed when G170's identity query lands (C3), when TyCtxt answers the identical Type under premises.")]
    internal static BoundConstraint? IdentityPremise(Koto node) => PlanOf(node).Second as BoundConstraint;

    /// <summary>Gets whether an explicit discard destroys its operand's Place in place, transferring nothing (SPEC 14.2.4, 5.2.3): its
    /// operand is a Transfer whose Take OffersTake allowed through its raw branch (ElementAccess.IsRawPlace; Binding.DestroysInPlace),
    /// the discard's plan row Flag.</summary>
    /// <param name="discard">The explicit discard.</param>
    /// <returns>Whether it destroys in place, transferring nothing.</returns>
    internal static bool DestroysInPlace(DiscardKoto discard) => PlanOf(discard).Flag;

    /// <summary>Publishes the Op, Completion, Adjust and PlaceShape columns of every module from the latest completed Binding pass, once per pass.</summary>
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

    /// <summary>Gives the columns a renumbering of this table's module moves its rows into (<see cref="Move"/>): the base columns new, each
    /// node family's column empty over the spare arena its kept rows move into (PagedColumn.Renumbered).</summary>
    /// <param name="length">The renumbered module's id count.</param>
    /// <param name="plans">The plan rows to start with.</param>
    /// <returns>The columns.</returns>
    internal readonly HirTables Renumbered(int length, int plans) => new(length, plans) { Families = this.Families.Renumbered(length) };

    /// <summary>Ends a renumbering's moves (<see cref="Renumbered"/>, <see cref="Move"/>): the node families clear their retired arenas.</summary>
    internal readonly void Retire() => this.Families.Retire();

    /// <summary>Resizes every column indexed by id.</summary>
    /// <param name="length">The new length.</param>
    internal void Resize(int length)
    {
        this.Families.Resize(length);
        Array.Resize(ref this.States, length);
        Array.Resize(ref this.Failures, length);
        Array.Resize(ref this.Meanings, length);
        Array.Resize(ref this.Symbols, length);
        Array.Resize(ref this.Lookups, length);
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
        this.Families.Move(from, ref target.Families, to);
    }

    // Row 0, which no node has, is the default row.
    private readonly void CopyBase(int from, HirTables target, int to)
    {
        target.States[to] = this.States[from];
        target.Failures[to] = this.Failures[from];
        target.Meanings[to] = this.Meanings[from];
        target.Symbols[to] = this.Symbols[from];
        target.Lookups[to] = this.Lookups[from];
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
/// Binding's private node families of one module (DECISIONS BU-220): one column per family, indexed by the SyntaxId of the node that keys
/// it. A kept family's <see cref="PagedColumn{T}"/> keeps its rows across passes, and a renumbering moves each row with its node
/// (<see cref="HirTables.Renumbered"/>, <see cref="HirTables.Move"/>). A pass or edit family's <see cref="StampedColumn{T, TCounter}"/>
/// holds its rows while its stamp is the value of the counter its declaration names (Binding.PassCounter, Binding.EditCounter), empties
/// itself at its first write after that counter advanced, and is handed over unmoved by a renumbering. Its family's accessor, in the
/// family's component, reads and writes a column; only Compiler/Binding names this type.
/// </summary>
internal struct NodeColumns
{
    internal PagedColumn<BindingSymbol?> Symbols; // Kept: the symbol declared at a node (Binding.Scopes.cs).
    internal PagedColumn<BindingSymbol?[]?> Parameters; // Kept: Symbols' composite column, a function's parameters' symbols by index.
    internal PagedColumn<BindingSymbol?> Receivers; // Kept: a constructor's or destructor's receiver `self` (Binding.Structs.cs).
    internal PagedColumn<Binding.CopyDeclaration?> CopyDeclarations; // Kept: a `Self is Copy` clause's declaration (Binding.CopyDeclarations.cs).
    internal PagedColumn<List<Binding.CopyDeclaration>?> CopyByType; // Kept: a container's Copy declarations (Binding.CopyDeclarations.cs).
    internal PagedColumn<BoundEnumConstruction?> EnumConstructions; // Kept: a construction expression's enum construction plan (Binding.Enums.cs).
    internal PagedColumn<BindingScope?> Scopes; // Kept: a node's scope (Binding.Scopes.cs).
    internal StampedColumn<Binding.VirtualOverride, Binding.PassCounter> VirtualOverrides; // Pass: an override's verified slot (Binding.VirtualOverrides.cs).
    internal PagedColumn<List<(FunctionKoto Original, FunctionKoto Implementation)>?> OverrideEntries; // Kept: a Type's implementations by original, emptied each pass (Binding.VirtualOverrides.cs).
    internal StampedColumn<EffectBoundKoto?, Binding.PassCounter> VirtualEffectBounds; // Pass: a virtual function's confining effect bound (Binding.EffectDeclarations.cs).
    internal StampedColumn<Binding.Specialization?, Binding.PassCounter> Specializations; // Pass: a verified explicit specialization's entry (Binding.Specializations.cs).
    internal PagedColumn<List<FunctionKoto>?> SpecializationsByOriginal; // Kept: an original's verified specializations, emptied each pass (Binding.Specializations.cs).
    internal PagedColumn<(SourceDocument Document, BindingScope? Scope)[]?> Documents; // Kept: Scopes' composite column, a module root's document scopes.
    internal PagedColumn<Binding.AssociatedInference?> AssociatedInference; // Kept: a conforming Type's inference batch (Binding.AssociatedInference.cs).
    internal PagedColumn<BoundOrigin?[]?> OriginAtoms; // Kept: a binder's Origin atoms, a KeyedRow by kind and slot (Binding.Origins.cs).
    internal PagedColumn<BoundOrigin?[]?> OpenOrigins; // Kept: a source's open regions, a KeyedRow by key (Binding.OpenOrigins.cs).
    internal StampedColumn<BoundOrigin[]?, Binding.EditCounter> AssociatedOrigins; // Edit: an associated Type declaration's Origin parameters (Binding.AssociatedOrigins.cs).
    internal StampedColumn<Binding.ObjectErasureEvidence, Binding.PassCounter> ObjectErasures; // Pass: an operation's or immutable alias's erased-view evidence (Binding.ObjectViews.cs).
    internal StampedColumn<BoundType?, Binding.PassCounter> InheritedOriginTypes; // Pass: the inherited contract's Type an omitted Origin takes (Binding.OriginInheritance.cs).
    internal StampedColumn<bool?, Binding.PassCounter> DeferredConstraints; // Pass: whether a clause waits for the conformances (Binding.Constraints.cs).
    internal StampedColumn<BoundMatch?, Binding.PassCounter> Matches; // Pass: the plan of a match the pass indexed (Binding.Patterns.cs).
    internal StampedColumn<BoundAdaptation, Binding.PassCounter> Adaptations; // Pass: the implicit adaptation selected for an expression (Binding.ArgumentOperations.cs).
    internal StampedColumn<int, Binding.PassCounter> ShapeContractHeads; // Pass: a function's first shape contract as the later of an overload pair, its index + 1 (Binding.ShapeContracts.cs).

    internal void Resize(int length)
    {
        this.Symbols.Grow(length);
        this.Parameters.Grow(length);
        this.Receivers.Grow(length);
        this.EnumConstructions.Grow(length);
        this.CopyDeclarations.Grow(length);
        this.CopyByType.Grow(length);
        this.Scopes.Grow(length);
        this.Documents.Grow(length);
        this.SpecializationsByOriginal.Grow(length);
        this.Specializations.Grow(length);
        this.VirtualEffectBounds.Grow(length);
        this.OverrideEntries.Grow(length);
        this.VirtualOverrides.Grow(length);
        this.AssociatedInference.Grow(length);
        this.OriginAtoms.Grow(length);
        this.OpenOrigins.Grow(length);
        this.AssociatedOrigins.Grow(length);
        this.ShapeContractHeads.Grow(length);
        this.InheritedOriginTypes.Grow(length);
        this.DeferredConstraints.Grow(length);
        this.Matches.Grow(length);
        this.Adaptations.Grow(length);
        this.ObjectErasures.Grow(length);
    }

    // The library's columns start at their stated slots (KimiLibrarySize: no program row lands in them).
    internal void Reserve(KimiLibrarySize.Tables stated)
    {
        this.Symbols.Reserve(stated.Declarations);
        this.Parameters.Reserve(stated.Parameters);
        this.Receivers.Reserve(stated.Receivers);
        this.EnumConstructions.Reserve(stated.EnumConstructions);
        this.CopyDeclarations.Reserve(stated.CopyDeclarations);
        this.CopyByType.Reserve(stated.CopyByType);
        this.InheritedOriginTypes.Reserve(stated.InheritedOriginTypes);
        this.DeferredConstraints.Reserve(stated.DeferredConstraints);
        this.Matches.Reserve(stated.Matches);
        this.Adaptations.Reserve(stated.Adaptations);
        this.Specializations.Reserve(stated.Specializations);
        this.SpecializationsByOriginal.Reserve(stated.SpecializationsByOriginal);
        this.Scopes.Reserve(stated.Scopes);
        this.OriginAtoms.Reserve(stated.OriginAtoms);
        this.OpenOrigins.Reserve(stated.OpenOrigins);
        this.AssociatedOrigins.Reserve(stated.AssociatedOrigins);
    }

    // A kept family's column gives a renumbering an empty column over its spare arena; a stamped family's is handed over unmoved and
    // emptied, its stamp kept (StampedColumn.Renumbered).
    internal readonly NodeColumns Renumbered(int length)
    {
        var columns = new NodeColumns
        {
            Symbols = this.Symbols.Renumbered(length),
            Parameters = this.Parameters.Renumbered(length),
            Receivers = this.Receivers.Renumbered(length),
            EnumConstructions = this.EnumConstructions.Renumbered(length),
            CopyDeclarations = this.CopyDeclarations.Renumbered(length),
            CopyByType = this.CopyByType.Renumbered(length),
            Scopes = this.Scopes.Renumbered(length),
            Documents = this.Documents.Renumbered(length),
            SpecializationsByOriginal = this.SpecializationsByOriginal.Renumbered(length),
            Specializations = this.Specializations.Renumbered(length),
            VirtualEffectBounds = this.VirtualEffectBounds.Renumbered(length),
            OverrideEntries = this.OverrideEntries.Renumbered(length),
            VirtualOverrides = this.VirtualOverrides.Renumbered(length),
            AssociatedInference = this.AssociatedInference.Renumbered(length),
            OriginAtoms = this.OriginAtoms.Renumbered(length),
            OpenOrigins = this.OpenOrigins.Renumbered(length),
            AssociatedOrigins = this.AssociatedOrigins.Renumbered(length),
            ShapeContractHeads = this.ShapeContractHeads.Renumbered(length),
            InheritedOriginTypes = this.InheritedOriginTypes.Renumbered(length),
            DeferredConstraints = this.DeferredConstraints.Renumbered(length),
            Matches = this.Matches.Renumbered(length),
            Adaptations = this.Adaptations.Renumbered(length),
            ObjectErasures = this.ObjectErasures.Renumbered(length),
        };
        return columns;
    }

    // A renumbering's moves have ended: the kept families clear their retired arenas; a stamped family's column was handed over unmoved
    // (Renumbered) and retires nothing.
    internal readonly void Retire()
    {
        this.Symbols.Retire();
        this.Parameters.Retire();
        this.Receivers.Retire();
        this.EnumConstructions.Retire();
        this.CopyDeclarations.Retire();
        this.CopyByType.Retire();
        this.Scopes.Retire();
        this.Documents.Retire();
        this.SpecializationsByOriginal.Retire();
        this.OverrideEntries.Retire();
        this.AssociatedInference.Retire();
        this.OriginAtoms.Retire();
        this.OpenOrigins.Retire();
    }

    // A kept family's row moves with its node; a stamped family's rows were handed over (Renumbered).
    internal readonly void Move(int from, ref NodeColumns target, int to)
    {
        this.Symbols.MoveTo(ref target.Symbols, from, to);
        this.Parameters.MoveTo(ref target.Parameters, from, to);
        this.Receivers.MoveTo(ref target.Receivers, from, to);
        this.EnumConstructions.MoveTo(ref target.EnumConstructions, from, to);
        this.CopyDeclarations.MoveTo(ref target.CopyDeclarations, from, to);
        this.CopyByType.MoveTo(ref target.CopyByType, from, to);
        this.Scopes.MoveTo(ref target.Scopes, from, to);
        this.Documents.MoveTo(ref target.Documents, from, to);
        this.SpecializationsByOriginal.MoveTo(ref target.SpecializationsByOriginal, from, to);
        this.OverrideEntries.MoveTo(ref target.OverrideEntries, from, to);
        this.AssociatedInference.MoveTo(ref target.AssociatedInference, from, to);
        this.OriginAtoms.MoveTo(ref target.OriginAtoms, from, to);
        this.OpenOrigins.MoveTo(ref target.OpenOrigins, from, to);
    }
}

/// <summary>
/// The plan slots of one node (the planIndex column): its kind's retained plan, a second plan, its formatting plan, a folded
/// constant (a conversion's, or a number literal's value, a ConstIndex position and a Tuple element's position, which
/// <see cref="HirTables.Publish"/> writes), a mode and a flag (an explicit discard's: <see cref="HirTables.DestroysInPlace"/>). Each Koto
/// class fixes what its slots hold; a class reads them by type, so a slot it does not use reads as absent.
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
    /// Place of each pass, where the column's unset value is the container discarding its item. Default otherwise: a publish clears every
    /// row's first, so it is always that publish's, a read of a take's claim (Binding.Claimed) included. A node three or more
    /// calls take (a <c>$tryWrite</c>'s storage) has no defined value here (PLAN G103).</summary>
    internal HirAdjust Adjust;

    /// <summary>The Type of the value a container or an explicit discard discards (SPEC 14.2.2, 14.2.4; DECISIONS BU-98), at the Place
    /// it takes (the outermost group): the Type the Place supplies (<c>Binding.SuppliedType</c>), a stored exclusive reference's Reborrow
    /// by Binding's name (DECISIONS BU-27), which no consumer states for a discard; an explicit discard's operand included, whose Adjust
    /// is the column's. Every publish writes it for every node it reaches: null for any other node (a value statement holds its Meaning)
    /// and for a discarded Place Binding did not resolve, so a row never keeps an earlier pass's Type, also after an in-place edit.</summary>
    internal BoundType? DiscardedType;

    // An Int128 would align the row to 16 bytes; two halves keep it at 56.
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

    /// <summary>Gets a conversion's kind, which its row's Mode holds.</summary>
    internal readonly ConversionBinding Conversion => (ConversionBinding)this.Mode;
}
