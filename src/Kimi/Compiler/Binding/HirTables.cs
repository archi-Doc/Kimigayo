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
    internal List<Koto>? Broken; // The nodes the latest publish found breaking a HIR contract: failure facts without their causes (Binding.FailureFact, RestingCauses), or a Type holding a split's mark.
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
        BindingSymbolKind.Parameter => Binding.InLaterDefault(node, symbol) ? HirRes.PreparedParam : HirRes.Param,
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

    /// <summary>Gets the decider of a node whose acquisition is by case, through which consumers read either stored decider (the HIR
    /// publisher, which writes the index's row and walks an invocation's calls, and HasFacts read the stored members); null for any other
    /// node.
    /// <list type="bullet">
    /// <item>A published user index whose selection is by case (SPEC 4.7.1 step 3, 8.14, 13.5.5; R7 plan Z23): its plan row's Mode 2,
    /// which the HIR publisher writes where the element's use needs exclusive capability only in the admitted cases where a standing take
    /// Reborrows and <c>indexUniq</c> is bound, with the Type of the Place whose take is by case (the element's, or a Field's or Tuple
    /// element's below it) in the row's Plan. In each admitted case the selection is <c>indexUniq</c> where that Type's case Type is an
    /// exclusive reference or object view (the case in which the take Reborrows) and <c>index</c> otherwise; the index's ExpansionPlan
    /// names both calls, and both calls' inputs are published, a receiver's two takes in its two rows (DECISIONS R4-M145). A selection that
    /// is one call in every case (Mode 0 <c>index</c>, Mode 1 <c>indexUniq</c>) has none; no reader reads Mode 2 otherwise.</item>
    /// <item>A value call whose Closure receiver is by case (SPEC 7.7.3, 8.15.2; PLAN G13): its CallPlan's
    /// <see cref="CallPlan.CaseDecider"/>, the callee's Closure Type. In each admitted case the call acquires the callee exclusively
    /// (<see cref="CallPlan.ReceiverOperation"/>, the column's take) where that Type's case receiver is Exclusive and shared
    /// (<see cref="CallPlan.SharedReceiver"/>, the plan row's take) otherwise. No invocation Mode is written.</item>
    /// </list></summary>
    /// <param name="binding">The reader's Binding, whose current pass's calls are read (<see cref="CallPlan.Of"/>).</param>
    /// <param name="node">An index expression or an invocation.</param>
    /// <returns>The decider, or null.</returns>
    internal static BoundType? CaseDecider(Binding binding, Koto node) => node switch
    {
        IndexKoto => PlanOf(node) is { Mode: 2, Plan: BoundType decider } ? decider : null,
        InvocationKoto => CallPlan.Of(binding, node)?.CaseDecider,
        _ => null,
    };

    /// <summary>Gets whether an explicit discard destroys its operand's Place in place, transferring nothing (SPEC 14.2.4, 5.2.3): its
    /// operand is a Transfer whose Take OffersTake allowed through its raw branch (Binding.IsRawPlace; Binding.DestroysInPlace),
    /// the discard's plan row Flag.</summary>
    /// <param name="discard">The explicit discard.</param>
    /// <returns>Whether it destroys in place, transferring nothing.</returns>
    internal static bool DestroysInPlace(DiscardKoto discard) => PlanOf(discard).Flag;

    /// <summary>Gets the take by which the form of the Error a kept operand belongs to reads the operand's whole Place, default where it
    /// does not (DECISIONS D-986; <see cref="PlanRow.KeptAdjust"/>): its <see cref="HirAdjust.Reads"/> is whether the form reads it. Read
    /// only at a kept operand, whose Adjust column is <c>(None, None)</c> under an Error.</summary>
    /// <param name="kept">A kept operand of an Error.</param>
    /// <returns>The form's take of its whole Place, or default.</returns>
    internal static HirAdjust KeptAdjust(Koto kept) => PlanOf(kept).KeptAdjust;

    /// <summary>Gets whether the form of the Error a kept operand belongs to takes the operand as an invocation's receiver or argument
    /// (SPEC 15.6.7; <see cref="PlanRow.KeptInput"/>), whether or not it reads it.</summary>
    /// <param name="kept">A kept operand of an Error.</param>
    /// <returns>Whether the Error's form takes it as an invocation's input.</returns>
    internal static bool KeptInput(Koto kept) => PlanOf(kept).KeptInput;

    /// <summary>Publishes the Op, Completion, Adjust and PlaceShape columns of the source modules from the latest completed Binding pass,
    /// once per pass; the library module's are published by the library pass (<see cref="LibraryVersion"/>).</summary>
    /// <param name="compilation">The bound compilation.</param>
    /// <returns>The pass the columns describe, 0 before the first.</returns>
    internal static ulong Publish(Compilation compilation) => compilation.Binding.PublishHir();

    /// <summary>Gets the library module's publication version (C-379 F1; PLAN G949 S1-r1b's RB1, S1-1 and Stage 2 U3): Binding.Frozen
    /// once the compilation's library is frozen, the same in every compilation of the process; 0 for a library not frozen.</summary>
    /// <param name="compilation">The compilation.</param>
    /// <returns>The version.</returns>
    internal static ulong LibraryVersion(Compilation compilation) => compilation.Binding.LibraryVersion;

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
            table.EnsureWritable(nameof(PlanFor));
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
        var table = node.SyntaxOwner!;
        table.EnsureWritable(nameof(Of));
        return ref table.Hir;
    }

    /// <summary>Clears a node's base row, as Binding's first visit of a pass does; its plan row stays for reuse.</summary>
    /// <param name="node">The node.</param>
    internal static void Reset(Koto node)
    {
        if (node.SyntaxOwner is { } table)
        {
            table.EnsureWritable(nameof(Reset));
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

/// <summary>A pass family's column in a <see cref="NodeColumns"/>: a fact family's accessor names its column so the recorder can choose
/// the columns that hold the fact (Binding.RecordedFact, Binding.SetRecordedFact; DECISIONS G949 S1 as R7-N5 B4 amends it).</summary>
/// <typeparam name="T">The family's row.</typeparam>
/// <param name="columns">The columns.</param>
/// <returns>The family's column.</returns>
internal delegate ref StampedColumn<T, Binding.PassCounter> FactFamily<T>(ref NodeColumns columns);

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
    internal StampedColumn<OmittedBaseSelection?, Binding.PassCounter> ImplicitConstructorDecisions; // Pass: a derived structure's omitted-base decision (Binding.ImplicitConstructors.cs).
    internal PagedColumn<(SourceDocument Document, BindingScope? Scope)[]?> Documents; // Kept: Scopes' composite column, a module root's document scopes.
    internal PagedColumn<Binding.AssociatedInference?> AssociatedInference; // Kept: a conforming Type's inference batch (Binding.AssociatedInference.cs).
    internal PagedColumn<BoundOrigin?[]?> OriginAtoms; // Kept: a binder's Origin atoms, a KeyedRow by kind and slot (Binding.Origins.cs).
    internal PagedColumn<BoundOrigin?[]?> OpenOrigins; // Kept: a source's open regions, a KeyedRow by key (Binding.OpenOrigins.cs).
    internal StampedColumn<BoundOrigin[]?, Binding.EditCounter> AssociatedOrigins; // Edit: an associated Type declaration's Origin parameters (Binding.AssociatedOrigins.cs).
    internal StampedColumn<Binding.ObjectErasureEvidence, Binding.PassCounter> ObjectErasures; // Pass: an operation's or immutable alias's erased-view evidence (Binding.ObjectViews.cs).
    internal StampedColumn<Binding.DeferredDefault?, Binding.PassCounter> DeferredDefaults; // Pass: a result slot's deferred default, its atom and inputs (Binding.Origins.Annotations.cs).
    internal StampedColumn<BoundType?, Binding.PassCounter> InheritedOriginTypes; // Pass: the inherited contract's Type an omitted Origin takes (Binding.OriginInheritance.cs).
    internal StampedColumn<bool?, Binding.PassCounter> DeferredConstraints; // Pass: whether a clause waits for the conformances (Binding.Constraints.cs).
    internal StampedColumn<BoundMatch?, Binding.PassCounter> Matches; // Pass: the plan of a match the pass indexed (Binding.Patterns.cs).
    internal StampedColumn<BoundAdaptation, Binding.PassCounter> Adaptations; // Pass: the implicit adaptation selected for an expression (Binding.ArgumentOperations.cs).
    internal StampedColumn<int, Binding.PassCounter> ShapeContractHeads; // Pass: a function's first shape contract as the later of an overload pair, its index + 1 (Binding.ShapeContracts.cs).
    internal PagedColumn<Binding.OriginDeclaration?> OriginDeclarations; // Kept: an owner's declared Origins (Binding.OriginDeclarations.cs).
    internal PagedColumn<List<string>?> DiscoveredOrigins; // Kept: a binder's discovered Origin names (Binding.OriginDeclarations.cs).
    internal PagedColumn<Binding.OriginRequirementWork?> OriginRequirementNodes; // Kept: a declaration's Origin requirement work (Binding.OriginRequirements.cs).
    internal PagedColumn<Binding.OriginDeclaration?> InitializerOrigins; // Kept: the Origins a local's initializer omits (Binding.LocalOrigins.cs).
    internal PagedColumn<InvocationKoto?[]?> PropertyCalls; // Kept: a property access's synthesized accessor calls (Binding.PropertyCalls.cs).
    internal PagedColumn<MemberAccessKoto?> StorageProjections; // Kept: an accessor body's synthesized `self` storage projection (Binding.PropertyCalls.cs).
    internal PagedColumn<MemberAccessKoto?> PropertyUpdateStorage; // Kept: a property update's synthesized storage access (Binding.PropertyCalls.cs).
    internal PagedColumn<EvaluatedKoto?> PropertyUpdateInputs; // Kept: a property update's synthesized input (Binding.PropertyCalls.cs).
    internal PagedColumn<InvocationKoto?[]?> IndexerCalls; // Kept: an index expression's synthesized `index` and `indexUniq` calls (Binding.Indexers.cs).
    internal PagedColumn<InvocationKoto?> ViewRangeCalls; // Kept: a view's range selection's synthesized `slice` call (Binding.Views.cs).
    internal PagedColumn<InvocationKoto?> RangeCalls; // Kept: a range or position key's synthesized PositionSyntax call (Binding.Ranges.cs).
    internal PagedColumn<InvocationKoto?> ResolvedKeys; // Kept: a selection key's synthesized `resolved` call (Binding.Ranges.cs).
    internal PagedColumn<BoundMatch?> MatchPlans; // Kept: the plan object a match keeps across rebinds (Binding.Patterns.cs).
    internal PagedColumn<Binding.Refusal?> Refusals; // Kept: the refusal a refused analysis's site holds (Binding.ConstraintValidation.Proofs.cs).
    internal PagedColumn<Binding.TypeRoleCause> FormationCauses; // Kept: a pair occurrence's Language formation causes, emptied at each Final check (Binding.ConstraintValidation.cs).
    internal StampedColumn<(bool Started, BindingScope? Scope), Binding.PassCounter> AliasTargets; // Pass: an alias's container scope (Binding.Aliases.cs).
    internal StampedColumn<Binding.AliasImport, Binding.PassCounter> ImportedContainers; // Pass: the alias import a qualifier's head selected (Binding.Aliases.cs).
    internal StampedColumn<Koto?, Binding.PassCounter> FailingSteps; // Pass: the step a use's published Unresolved lookup failed at, where it is not the use (Binding.ResolvePath.cs).
    internal StampedColumn<Binding.QualificationFact, Binding.PassCounter> QualificationFailures; // Pass: the qualification a failed Name or Type required (Binding.Qualification.cs).
    internal StampedColumn<(Binding.QualificationFact Value, Binding.QualificationFact Type), Binding.PassCounter> QualificationStops; // Work: a use's provisional qualification stop in each namespace, read by the resolver's miss, the running pass's working state, released at the freeze (Binding.Qualification.cs).
    internal StampedColumn<SemanticsMask, Binding.PassCounter> PairFollows; // Pass: the admitted set of the pair layer a @follow node follows (Binding.PairLayers.cs).
    internal StampedColumn<SemanticsMask, Binding.PassCounter> ImplicitPairFollows; // Pass: the admitted set of a pair layer selection follows implicitly (Binding.PairLayers.cs).
    internal StampedColumn<(bool Rejected, Binding.EffectBoundRejection Rejection), Binding.PassCounter> EffectBoundRejections; // Pass: an invalid effect item's rejection (Binding.EffectDeclarations.cs).
    internal StampedColumn<BoundArgumentOperation, Binding.PassCounter> ReceiverOperations; // Work: the receiver or storage operation selected for a call or member access, released at the freeze (Binding.ArgumentOperations.cs).
    internal StampedColumn<BoundLength?, Binding.PassCounter> InferredArrayLengths; // Pass: the length an initializer supplied for a written length hole (Binding.ArrayInference.cs).
    internal StampedColumn<Koto?, Binding.PassCounter> ArrayInferenceShapes; // Pass: the written fixed-array shape an empty literal or a result target takes its evidence from (Binding.ArrayInference.cs).
    internal StampedColumn<Binding.ResultContext?, Binding.PassCounter> ResultContexts; // Work: a result target's context, the running pass's working state, released at the freeze (Binding.ControlFlow.cs).
    internal StampedColumn<Binding.UniqIndexing, Binding.PassCounter> UniqIndexing; // Pass: a user or view index expression's UniqIndexable fact (Binding.Indexers.cs).
    internal StampedColumn<Binding.RequirementGroup?, Binding.PassCounter> RequirementGroups; // Work: a member access's lookup through requirements, the running pass's working state, released at the freeze (Binding.ContractCalls.cs).
    internal StampedColumn<int, Binding.PassCounter> MemberSelections; // Work: a member access's published side and lookup selection, as its row's index in the pass's rows plus 1, the running pass's working state, released at the freeze (Binding.MemberLookup.cs).
    internal StampedColumn<bool, Binding.PassCounter> InferredConstructionTargets; // Work: whether a construction qualifier leaves its own slots open for its construction to complete, the running pass's working state, released at the freeze (Binding.ContainerReferences.cs).
    internal StampedColumn<(uint Query, StructuralCompletion.Completion Completion), Binding.PassCounter> ResultCompletions; // Work: Binding's result, conversion and fallthrough checks' (Binding.ResultStructure) structural completion of a node in its query, a row of another query absent, released at the freeze (StructuralCompletion.cs).
    internal StampedColumn<(uint Query, StructuralCompletion.Completion Completion), Binding.PassCounter> GuessedCompletions; // Work: the guess rule's (Binding.GuessedCompletionNodes) structural completion of a node in its query, a row of another query absent, released at the freeze (StructuralCompletion.cs).
    internal StampedColumn<(uint Query, StructuralCompletion.Completion Completion), Binding.PassCounter> PublishedCompletions; // Work: the HIR publisher's (Binding.Hir.cs) structural completion of a node in its query, a row of another query absent, released at the freeze (StructuralCompletion.cs).
    internal StampedColumn<(uint Query, StructuralCompletion.Completion Completion), Binding.PassCounter> LintCompletions; // Work: the Final pass's lint walk's (Binding.Lints.cs) structural completion of a node in its query, a row of another query absent, released at the freeze (StructuralCompletion.cs).
    internal StampedColumn<bool?, Binding.PassCounter> ReachedThroughBorrow; // Work: whether a selection reaches its Place through a Slice or a borrow, a memo of a final fact of its syntax valid while its pass's stamp stands (the pass, then the queries after it, its reports included, until the next ResetPass), released at the freeze (Binding.ElementAccess.cs).
    internal StampedColumn<(uint Publication, HirAdjust Take, bool Input), Binding.PassCounter> FormReads; // Work: the HIR publisher's (Binding.Hir.cs) publication in which its Error's form took a node, the take that read its whole Place (else default) and whether it was an invocation's input, a row of another publication absent, released at the freeze (Binding.Hir.Adjust.cs; DECISIONS D-986).
    internal StampedColumn<bool, Binding.PassCounter> JudgedOccurrences; // Work: whether JudgeOccurrence judged a written Type occurrence in the running pass, its per-pass query's memo, released at the freeze (Binding.ConstraintValidation.cs).
    internal PagedColumn<Binding.Specialization?> SpecializationStorage; // Kept: a specialization's entry, its slot arrays reused by every pass (Binding.Specializations.cs).
    internal StampedColumn<Binding.EarlyRead, Binding.PassCounter> EarlyReads; // Pass: a use's lookup that saw a base clause not ready, read again by VerifyEarlyReads (Binding.BaseLayers.cs).
    internal PagedColumn<BoundType?> KnownBases; // Kept: a struct's base clause's final Type from a first run, emptied by a Bind not started from it, before each fill and by an edit (Binding.BaseLayers.cs).
    internal StampedColumn<bool, Binding.PassCounter> ResolvedSlices; // Work: whether a selection applies one ResolvedRange value, the running pass's working state, released at the freeze (Binding.Ranges.cs).
    internal StampedColumn<(int Start, int Count), Binding.PassCounter> Prerequisites; // Work: a node's consultation range in the pass's prerequisite store, the running pass's dependence store, released at the freeze (Binding.Diagnostics.Prerequisites.cs).
    internal StampedColumn<Koto?, Binding.PassCounter> PartPrerequisites; // Work: the part a declaration-level check read outside a frame, the running pass's dependence store, released at the freeze (Binding.Diagnostics.Prerequisites.cs).
    internal StampedColumn<uint, Binding.PassCounter> PrerequisiteVisits; // Work: the publication walks' visited set (RestsOnRecoveryOrLimit, AddPrerequisiteKeys), a row carrying the walk's number, a row of another walk absent, released at the freeze (Binding.Diagnostics.Prerequisites.cs).
    internal StampedColumn<(Koto? Parent, Koto? Next), Binding.PassCounter> DuplicateDeclarations; // Pass: a declaration's duplicate group row, its parent toward the group's first and the first's earliest later declaration, written by the pairwise checks and read by RecordDuplicateGroups (Binding.DuplicateDiagnostics.cs).
    internal StampedColumn<bool, Binding.PassCounter> ResolvingTypes; // Work: BindType's cycle guard, whether a Type syntax's BindType is on the stack, the running pass's working state, released at the freeze (Binding.Types.cs).
    internal StampedColumn<Koto?, Binding.PassCounter> SynthesizedCalls; // Work: a call Binding synthesized in the pass, its consumer, the running pass's working state, released at the freeze (Binding.Check.cs).
    internal PagedColumn<List<BindingSymbol>?> GatheredGroups; // Kept: a use's function group gathered at the alias stage, emptied in place at each publication of its head (Binding.Imports.cs).
    internal PagedColumn<(bool Pattern, bool Guard)> IndexedPatterns; // Kept: whether the latest run of a node's pass indexed it as a pattern node or a guard scope, copied to the pass's previous-run lists and emptied at its reset (Binding.Patterns.cs).
    internal PagedColumn<bool> FormedTypes; // Kept: whether a node forms its Type, written by BindType (a written occurrence, a rooted name's operand), EnumQualifierType, ConstructionTargetType, BindContractReference and the Dictionary literal's binder, emptied at its pass's reset (Binding.Types.cs, FormedTypeRows).

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
        this.ImplicitConstructorDecisions.Grow(length);
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
        this.DeferredDefaults.Grow(length);
        this.OriginDeclarations.Grow(length);
        this.DiscoveredOrigins.Grow(length);
        this.OriginRequirementNodes.Grow(length);
        this.InitializerOrigins.Grow(length);
        this.PropertyCalls.Grow(length);
        this.StorageProjections.Grow(length);
        this.PropertyUpdateStorage.Grow(length);
        this.PropertyUpdateInputs.Grow(length);
        this.IndexerCalls.Grow(length);
        this.ViewRangeCalls.Grow(length);
        this.RangeCalls.Grow(length);
        this.ResolvedKeys.Grow(length);
        this.MatchPlans.Grow(length);
        this.Refusals.Grow(length);
        this.FormationCauses.Grow(length);
        this.AliasTargets.Grow(length);
        this.ImportedContainers.Grow(length);
        this.FailingSteps.Grow(length);
        this.QualificationFailures.Grow(length);
        this.QualificationStops.Grow(length);
        this.PairFollows.Grow(length);
        this.ImplicitPairFollows.Grow(length);
        this.EffectBoundRejections.Grow(length);
        this.ReceiverOperations.Grow(length);
        this.InferredArrayLengths.Grow(length);
        this.ArrayInferenceShapes.Grow(length);
        this.ResultContexts.Grow(length);
        this.UniqIndexing.Grow(length);
        this.RequirementGroups.Grow(length);
        this.MemberSelections.Grow(length);
        this.InferredConstructionTargets.Grow(length);
        this.ResultCompletions.Grow(length);
        this.GuessedCompletions.Grow(length);
        this.PublishedCompletions.Grow(length);
        this.LintCompletions.Grow(length);
        this.ReachedThroughBorrow.Grow(length);
        this.FormReads.Grow(length);
        this.JudgedOccurrences.Grow(length);
        this.SpecializationStorage.Grow(length);
        this.KnownBases.Grow(length);
        this.EarlyReads.Grow(length);
        this.ResolvedSlices.Grow(length);
        this.Prerequisites.Grow(length);
        this.PartPrerequisites.Grow(length);
        this.PrerequisiteVisits.Grow(length);
        this.DuplicateDeclarations.Grow(length);
        this.ResolvingTypes.Grow(length);
        this.SynthesizedCalls.Grow(length);
        this.GatheredGroups.Grow(length);
        this.IndexedPatterns.Grow(length);
        this.FormedTypes.Grow(length);
    }

    // PLAN G949 S1-1's freeze of the library's families: every stamped column, both counters' (F2: an edit advances the edit counter,
    // which would leave the library's edit family stale with no later library pass to rewrite it), one row each: Freeze for a fact
    // family, whose library rows a later pass reads; Release for a working-state family ("Work:"), whose rows no reader reads after the
    // pass that wrote them, so the library keeps none (DECISIONS BU-220 as R7-N5 B2a-2 amends it).
    internal void Freeze(Binding binding)
    {
        this.VirtualOverrides.Freeze(binding);
        this.VirtualEffectBounds.Freeze(binding);
        this.Specializations.Freeze(binding);
        this.ImplicitConstructorDecisions.Freeze(binding);
        this.AssociatedOrigins.Freeze(binding);
        this.ObjectErasures.Freeze(binding);
        this.DeferredDefaults.Freeze(binding);
        this.InheritedOriginTypes.Freeze(binding);
        this.DeferredConstraints.Freeze(binding);
        this.Matches.Freeze(binding);
        this.Adaptations.Freeze(binding);
        this.ShapeContractHeads.Freeze(binding);
        this.AliasTargets.Freeze(binding);
        this.ImportedContainers.Freeze(binding);
        this.FailingSteps.Freeze(binding);
        this.QualificationFailures.Freeze(binding);
        this.QualificationStops.Release();
        this.PairFollows.Freeze(binding);
        this.ImplicitPairFollows.Freeze(binding);
        this.EffectBoundRejections.Freeze(binding);
        this.ReceiverOperations.Release();
        this.InferredArrayLengths.Freeze(binding);
        this.ArrayInferenceShapes.Freeze(binding);
        this.ResultContexts.Release();
        this.UniqIndexing.Freeze(binding);
        this.RequirementGroups.Release();
        this.MemberSelections.Release();
        this.InferredConstructionTargets.Release();
        this.ResultCompletions.Release();
        this.GuessedCompletions.Release();
        this.PublishedCompletions.Release();
        this.LintCompletions.Release();
        this.ReachedThroughBorrow.Release();
        this.FormReads.Release();
        this.JudgedOccurrences.Release();
        this.EarlyReads.Freeze(binding);
        this.ResolvedSlices.Release();
        this.Prerequisites.Release();
        this.PartPrerequisites.Release();
        this.PrerequisiteVisits.Release();
        this.DuplicateDeclarations.Freeze(binding);
        this.ResolvingTypes.Release();
        this.SynthesizedCalls.Release();
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
            ImplicitConstructorDecisions = this.ImplicitConstructorDecisions.Renumbered(length),
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
            DeferredDefaults = this.DeferredDefaults.Renumbered(length),
            OriginDeclarations = this.OriginDeclarations.Renumbered(length),
            DiscoveredOrigins = this.DiscoveredOrigins.Renumbered(length),
            OriginRequirementNodes = this.OriginRequirementNodes.Renumbered(length),
            InitializerOrigins = this.InitializerOrigins.Renumbered(length),
            PropertyCalls = this.PropertyCalls.Renumbered(length),
            StorageProjections = this.StorageProjections.Renumbered(length),
            PropertyUpdateStorage = this.PropertyUpdateStorage.Renumbered(length),
            PropertyUpdateInputs = this.PropertyUpdateInputs.Renumbered(length),
            IndexerCalls = this.IndexerCalls.Renumbered(length),
            ViewRangeCalls = this.ViewRangeCalls.Renumbered(length),
            RangeCalls = this.RangeCalls.Renumbered(length),
            ResolvedKeys = this.ResolvedKeys.Renumbered(length),
            MatchPlans = this.MatchPlans.Renumbered(length),
            Refusals = this.Refusals.Renumbered(length),
            FormationCauses = this.FormationCauses.Renumbered(length),
            AliasTargets = this.AliasTargets.Renumbered(length),
            ImportedContainers = this.ImportedContainers.Renumbered(length),
            FailingSteps = this.FailingSteps.Renumbered(length),
            QualificationFailures = this.QualificationFailures.Renumbered(length),
            QualificationStops = this.QualificationStops.Renumbered(length),
            PairFollows = this.PairFollows.Renumbered(length),
            ImplicitPairFollows = this.ImplicitPairFollows.Renumbered(length),
            EffectBoundRejections = this.EffectBoundRejections.Renumbered(length),
            ReceiverOperations = this.ReceiverOperations.Renumbered(length),
            InferredArrayLengths = this.InferredArrayLengths.Renumbered(length),
            ArrayInferenceShapes = this.ArrayInferenceShapes.Renumbered(length),
            ResultContexts = this.ResultContexts.Renumbered(length),
            UniqIndexing = this.UniqIndexing.Renumbered(length),
            RequirementGroups = this.RequirementGroups.Renumbered(length),
            MemberSelections = this.MemberSelections.Renumbered(length),
            InferredConstructionTargets = this.InferredConstructionTargets.Renumbered(length),
            ResultCompletions = this.ResultCompletions.Renumbered(length),
            GuessedCompletions = this.GuessedCompletions.Renumbered(length),
            PublishedCompletions = this.PublishedCompletions.Renumbered(length),
            LintCompletions = this.LintCompletions.Renumbered(length),
            ReachedThroughBorrow = this.ReachedThroughBorrow.Renumbered(length),
            FormReads = this.FormReads.Renumbered(length),
            JudgedOccurrences = this.JudgedOccurrences.Renumbered(length),
            SpecializationStorage = this.SpecializationStorage.Renumbered(length),
            KnownBases = this.KnownBases.Renumbered(length),
            EarlyReads = this.EarlyReads.Renumbered(length),
            ResolvedSlices = this.ResolvedSlices.Renumbered(length),
            Prerequisites = this.Prerequisites.Renumbered(length),
            PartPrerequisites = this.PartPrerequisites.Renumbered(length),
            PrerequisiteVisits = this.PrerequisiteVisits.Renumbered(length),
            DuplicateDeclarations = this.DuplicateDeclarations.Renumbered(length),
            ResolvingTypes = this.ResolvingTypes.Renumbered(length),
            SynthesizedCalls = this.SynthesizedCalls.Renumbered(length),
            GatheredGroups = this.GatheredGroups.Renumbered(length),
            IndexedPatterns = this.IndexedPatterns.Renumbered(length),
            FormedTypes = this.FormedTypes.Renumbered(length),
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
        this.OriginDeclarations.Retire();
        this.DiscoveredOrigins.Retire();
        this.OriginRequirementNodes.Retire();
        this.InitializerOrigins.Retire();
        this.PropertyCalls.Retire();
        this.StorageProjections.Retire();
        this.PropertyUpdateStorage.Retire();
        this.PropertyUpdateInputs.Retire();
        this.IndexerCalls.Retire();
        this.ViewRangeCalls.Retire();
        this.RangeCalls.Retire();
        this.ResolvedKeys.Retire();
        this.MatchPlans.Retire();
        this.Refusals.Retire();
        this.FormationCauses.Retire();
        this.SpecializationStorage.Retire();
        this.KnownBases.Retire();
        this.GatheredGroups.Retire();
        this.IndexedPatterns.Retire();
        this.FormedTypes.Retire();
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
        this.OriginDeclarations.MoveTo(ref target.OriginDeclarations, from, to);
        this.DiscoveredOrigins.MoveTo(ref target.DiscoveredOrigins, from, to);
        this.OriginRequirementNodes.MoveTo(ref target.OriginRequirementNodes, from, to);
        this.InitializerOrigins.MoveTo(ref target.InitializerOrigins, from, to);
        this.PropertyCalls.MoveTo(ref target.PropertyCalls, from, to);
        this.StorageProjections.MoveTo(ref target.StorageProjections, from, to);
        this.PropertyUpdateStorage.MoveTo(ref target.PropertyUpdateStorage, from, to);
        this.PropertyUpdateInputs.MoveTo(ref target.PropertyUpdateInputs, from, to);
        this.IndexerCalls.MoveTo(ref target.IndexerCalls, from, to);
        this.ViewRangeCalls.MoveTo(ref target.ViewRangeCalls, from, to);
        this.RangeCalls.MoveTo(ref target.RangeCalls, from, to);
        this.ResolvedKeys.MoveTo(ref target.ResolvedKeys, from, to);
        this.MatchPlans.MoveTo(ref target.MatchPlans, from, to);
        this.Refusals.MoveTo(ref target.Refusals, from, to);
        this.FormationCauses.MoveTo(ref target.FormationCauses, from, to);
        this.SpecializationStorage.MoveTo(ref target.SpecializationStorage, from, to);
        this.KnownBases.MoveTo(ref target.KnownBases, from, to);
        this.GatheredGroups.MoveTo(ref target.GatheredGroups, from, to);
        this.IndexedPatterns.MoveTo(ref target.IndexedPatterns, from, to);
        this.FormedTypes.MoveTo(ref target.FormedTypes, from, to);
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
    /// R4-M109), written at each call input of each pass; a by-case value call's Shared acquisition of its callee beside the Exclusive one
    /// the column keeps (<see cref="CallPlan.SharedReceiver"/>; SPEC 7.7.3, PLAN G13), the same call's two takes of one input; or a Place in
    /// Discard Context's bare acquisition (SPEC 14.2.2), written at each such
    /// Place of each pass, where the column's unset value is the container discarding its item. At a call input it is never an operand
    /// take before the node's first call: each record of the uses resets its preceding input at a call (<c>Binding.EarlierAdjust</c>). The
    /// reset is unobservable today (the implementation review's mutant M5): each operand that a node's first call also takes as its first
    /// input (an index's key, an object creation's operand, a <c>for</c>'s Subject) is taken by the same Adjust both times. It keeps the row
    /// a call's take, as <c>Binding.Claimed</c>, <c>UseOf</c> and the MIR builder's <c>Taken</c> read it, without resting on that.
    /// Default otherwise: a publish clears every row's first, so it is always that publish's, a read of a take's claim (Binding.Claimed)
    /// included. A node three or more calls take (a <c>$tryWrite</c>'s storage) has no defined value here (PLAN G103).</summary>
    internal HirAdjust Adjust;

    /// <summary>The Type of the value a container or an explicit discard discards (SPEC 14.2.2, 14.2.4; DECISIONS BU-98), at the Place
    /// it takes (the outermost group): the Type the Place supplies (<c>Binding.SuppliedType</c>), a stored exclusive reference's Reborrow
    /// by Binding's name (DECISIONS BU-27), which no consumer states for a discard; an explicit discard's operand included, whose Adjust
    /// is the column's. Every publish writes it for every node it reaches: null for any other node (a value statement holds its Meaning)
    /// and for a discarded Place Binding did not resolve, so a row never keeps an earlier pass's Type, also after an in-place edit.</summary>
    internal BoundType? DiscardedType;

    /// <summary>The take by which the form of the Error a kept operand belongs to reads the operand's whole Place in a use not in place
    /// (DECISIONS D-986; SPEC 13.4, 15.1.5, 15.1.6), default where it does not, so its <see cref="HirAdjust.Reads"/> is whether the form
    /// reads it: the Adjust rule's (<c>Binding.AdjustChildren</c>) take for the Error's form (its <c>KindOp</c>), an acquisition or a
    /// shared borrow, as it takes a valid node's child of the same form, an argument of an application Binding did not resolve by a
    /// stand-in, its evaluation as a value, which every candidate's operation reads as (DECISIONS G1178; SPEC 7.4, 10.1, 10.2); of two
    /// takes of one operand (a user index's calls by case), the later that reads, unlike the column, which keeps the later take (R4-M145).
    /// Default for a kept operand the form uses in place (a projection's receiver or base, a follow's, borrow's, runtime test's or case
    /// adaptation's operand, a group's operand), borrows exclusively, reads through a reference or whose acquisition failed, and for
    /// every other node. Written with <see cref="KeptInput"/> at each kept operand of each publish, whose column stays the kept mark
    /// <c>(None, None)</c> (DECISIONS R5-M63): a kept operand is evaluated as its Place and never acquired, so no claim reads this
    /// (R7-M8). A publish clears every row's first.</summary>
    internal HirAdjust KeptAdjust;

    /// <summary>Whether the form of the Error a kept operand belongs to takes the operand as an invocation's receiver or argument (SPEC
    /// 15.6.7), read or not: the Adjust rule gives it as an input of one of the form's calls (<c>Binding.IHirUses.Call</c>), an argument of
    /// an application Binding did not resolve under the form Call (its one call, which has no plan) included. False for a Case payload,
    /// an aggregate's element, an operator's operand, a conversion's or a Subject, and for every other node; written and cleared with
    /// <see cref="KeptAdjust"/>.</summary>
    internal bool KeptInput;

    // An Int128 would align the row to 16 bytes; two halves keep it at 64 (56 before the kept operand's take: Unsafe.SizeOf, D-986).
    private bool folded;
    private ulong foldedHigh;
    private ulong foldedLow;

    /// <summary>Gets or sets the folded constant of a conversion, a number literal's value, a fixed-array element's constant index (SPEC
    /// 15.1.3, on a ConstIndex or an Index Place, however the array is reached, in range or not: Binding.ConstantIndex) or a Tuple
    /// element's position.</summary>
    internal Int128? Folded
    {
        readonly get => this.folded ? new Int128(this.foldedHigh, this.foldedLow) : null;
        set => (this.folded, this.foldedHigh, this.foldedLow) = value is { } constant ? (true, (ulong)(constant >> 64), (ulong)constant) : (false, 0UL, 0UL);
    }

    /// <summary>Gets a conversion's kind, which its row's Mode holds.</summary>
    internal readonly ConversionBinding Conversion => (ConversionBinding)this.Mode;
}
