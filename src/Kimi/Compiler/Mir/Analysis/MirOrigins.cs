// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns A5's Origin stratum (SPEC 15.6.1, 15.6.3, 15.6.5; 2a0-D §6, D13-D20; DECISIONS R4-M94, R4-M113, R4-M114, R4-M118), per body and per
// class of its views: the graph of the body's rows (the C1-F4 view) with its Refs' containment edges, the Refuted and Unknown chains
// through it and the relations between two fixed Origins (MirOrigins.Chains.cs), and the located limit at an end the view leaves
// unclassed; and, for the Loan stratum's rule W, each Ref's causes and a receiving Type's class (MirOrigins.Causes.cs, R4-M132).
// - Inputs: a validated body and the views A1 and A2 completed (MirDropTable.ViewsOf); MirCases (a view's borrow binders and case Types);
//   MirSteps (a Ref's classed steps); the C1-F4 view (TyCtxt.OriginRows, OriginAtom, an atom's part), keyed by MirDefKey.Body, its
//   fixed-fixed oracles (TyCtxt.UniversalRelations: the body's, and its layer's for the layer's part) and its findings
//   (TyCtxt.OriginFindings).
// - Outputs, which Judge adds at its end with each class's views, before the Loan stratum (R4-M132): UnsatisfiedOriginRelation_Kd
//   (Ownership.UnprovenOrigin) at a chain's last edge's site (a row's Site, or the Ref's statement for a containment edge), with Reason
//   (finite source, fixed end, false) of the view's
//   canonical atoms and the facts Relation (the edge's row, -1 for a containment edge) and Anchor (the first Ref whose Origin is the
//   source); UnprovenOriginRelation_Kd (Ownership.UnprovenOrigin) where the oracle does not derive a chain or edge between fixed Origins,
//   at its last edge's site with Reason (fixed source, fixed target, false), or (Longer, Shorter, true) for an Equality row failing both
//   ways, and the fact Relation; Unsupported_Kd (Ownership.Unsupported) at a row or Ref with an Unclassed end (2a0-D §3 row 9, D19),
//   whatever Binding's completeness (DECISIONS R4-M113), while a Dependent node, an end a Binding failure left Unbound or an unclassified
//   Closure's pending atom (R4-M126 §2.5 (c)), is never a source or a judged target, so only a chain through it is judged, between its
//   ends (SPEC 23.1.2, R4-M118); OwnershipStorageLimit_Kd once at a body whose arena MirDataflow.Within refuses, through
//   MirDataflow.StorageLimit with its arguments (G213), which the stratum then skips (D20); and, which Findings adds once per program
//   after step 4, InternalInvariant_Kd (Ownership.Internal, NoBody, every view) at each of the view's findings, a broken view contract
//   in a complete compilation (R4-M101 E2, E4).
// - Invariants: no liveness, CFG order, Loan set or syntax is read; every Origin comes through the view, a row's ends by index and a Ref's
//   or a dereferenced layer's through OriginAtom at the Ref's site; a chain's verdict does not depend on reachability; views whose active
//   rows and containment edges are equal form one class, judged once, under the borrow binders all its views share (SPEC 15.6.5); an edge
//   of the layer's part is judged with the layer's oracle (R4-M101 E4); a Ref inside an Inlined scope adds no containment (a default's rows
//   are its template's, R4-M101 (b)); storage is pooled, so a warm run allocates nothing.
// - Must not: judge a relation Binding judged directly (it forms no row, D1); decode a slot or read a binder or forming body (only the view
//   does); port the preserves-results skip or the top-like Anchor (PD9); or drop a problem (MirReport's).

using System.Runtime.InteropServices;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

/// <summary>A5's Origin stratum (2a0-D §6): <see cref="Judge"/> judges a body's classes of views and adds their problems;
/// <see cref="Findings"/> adds the view's broken contracts once per program.</summary>
/// <param name="types">The Type context, whose C1-F4 view gives the rows and the atoms.</param>
/// <param name="cases">The Semantics case views.</param>
internal sealed partial class MirOrigins(TyCtxt types, MirCases cases)
{
    private readonly MirSteps steps = new(cases);
    private readonly List<Pending> pending = []; // The problems Judge found, in order, with their class's views.
    private readonly List<MirFact> pendingFacts = [];
    private MirStep[] walk = [];
    private Ref[] refs = []; // The body's Refs with an Origin outside every Inlined scope, in body order.
    private int[] anchors = []; // Per table index below anchorCount: the first of those Refs whose Origin it is, or -1.
    private ulong[] rowBits = []; // Per class: its active rows, rowWords words each.
    private Edge[] contained = []; // Per class: its containment edges, from the class's start.
    private Class[] classes = [];
    private int refCount;
    private int anchorCount; // The table's size when the Refs were collected; a later index (a layer's or meet operand's Origin) has none.
    private int classCount;
    private int containedCount;
    private int rowWords;
    private ulong allViews;
    private Binding.UniversalRelations? ownOracle; // The body's oracle and its layer's, opened on first use and disposed after Judge.
    private Binding.UniversalRelations? layerOracle;
    private bool layerFirst; // Whether the layer's oracle was opened first; premise environments are returned in reverse order.
    private MirBody body = null!;
    private ExecutableBody key;
    private Binding.OriginRowSet slice = null!;

    /// <summary>Gets the work of the last <see cref="Judge"/> (2a0-D §6.4): node visits, edge steps, bitset and mask words, and the
    /// oracle's Close and Outlives calls, summed over its classes.</summary>
    internal long Work { get; private set; }

    /// <summary>Gets the classes of views the last <see cref="Judge"/> formed (V16's 64-view body).</summary>
    internal int Classes => this.classCount;

    /// <summary>Gets the containment edges the last <see cref="Judge"/> formed, over its classes (V16's "F4 containment").</summary>
    internal int ContainmentEdges => this.containedCount;

    /// <summary>Judges a body over the views A1 and A2 completed (step 4, before MirBorrows): forms its classes of views, finds each
    /// class's problems and adds them (R4-M132).</summary>
    /// <param name="body">The validated body.</param>
    /// <param name="views">The number of views judged, from view 0 (MirDropTable.ViewsOf).</param>
    /// <param name="report">The program's problems.</param>
    internal void Judge(MirBody body, long views, MirReport report)
    {
        (this.body, this.classCount, this.containedCount, this.Work, this.refCount, this.causeCount) = (body, 0, 0, 0, 0, 0);
        this.edgesClass = -1;
        this.pending.Clear();
        this.pendingFacts.Clear();
        if (views == 0)
        {
            return;
        }

        (this.key, this.allViews) = (body.Key.Body, ulong.MaxValue >> (64 - (int)views));
        this.slice = types.OriginRows(this.key);
        this.CollectRefs();
        this.rowWords = (this.slice.Rows.Count + 63) >> 6;
        for (var view = 0L; view < views; view++)
        {
            cases.Select(body, view);
            this.Classify(view, cases.Borrows());
        }

        try
        {
            for (var c = 0; c < this.classCount; c++)
            {
                if (!this.JudgeClass(c, report))
                {
                    break; // D20: the stratum skips a body over the limit.
                }
            }
        }
        finally
        {
            var (first, second) = this.layerFirst ? (this.layerOracle, this.ownOracle) : (this.ownOracle, this.layerOracle);
            second?.Dispose();
            first?.Dispose();
            (this.ownOracle, this.layerOracle, this.layerFirst) = (null, null, false);
        }

        var facts = CollectionsMarshal.AsSpan(this.pendingFacts);
        this.recorded.Clear();
        foreach (var found in this.pending)
        {
            var problem = new MirProblem(found.Code, found.Requirement, found.Subject, this.body.Ordinal, found.Views, found.Reason);
            this.recorded.Add(report.Add(problem, facts.Slice(found.FactStart, found.FactCount)));
        }

        this.IndexCauses();
    }

    /// <summary>Adds the C1-F4 view's findings, each a broken view contract in a complete compilation, as one InternalInvariant_Kd at its
    /// site in every view (R4-M101 E2, E4): once per program, after step 4's reads of the view.</summary>
    /// <param name="report">The program's problems.</param>
    internal void Findings(MirReport report)
    {
        var findings = types.OriginFindings;
        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.Internal);
        for (var i = 0; i < findings.Count; i++)
        {
            report.Add(new(DiagnosticCode.InternalInvariant_Kd, requirement, findings[i], MirProblem.NoBody, ulong.MaxValue));
        }
    }

    // A part's fixed-fixed oracle (2a0-D §4, D8): the body's, or the layer's for the layer's part (the Type's public premises, SPEC 6.3.3;
    // R4-M101 E4, R4-M110 Q1 (a)), opened on first use.
    private Binding.UniversalRelations Oracle(bool layer)
    {
        if (!layer)
        {
            return this.ownOracle ??= types.UniversalRelations(this.key);
        }

        if (this.layerOracle is null)
        {
            (this.layerOracle, this.layerFirst) = (types.UniversalRelations(this.slice.Layer), this.ownOracle is null);
        }

        return this.layerOracle;
    }

    // The body's Refs that issue a borrow with an Origin (SPEC 15.6.3): a non-raw Ref outside every Inlined scope, its own Origin its
    // Type's outer Origin classed at its statement; and per table index the first of them in body order whose Origin it is (the Anchor).
    private void CollectRefs()
    {
        this.refCount = 0;
        var statements = this.body.Statements;
        for (var s = 0; s < statements.Length; s++)
        {
            ref readonly var statement = ref statements[s];
            if (statement is not { Kind: MirStatementKind.Assign, Rvalue: { Kind: MirRvalueKind.Ref, Type.Origin: { } origin } rvalue } ||
                (MirRefKind)rvalue.Op == MirRefKind.Raw || this.body.Inlining(statement.Source.Scope) >= 0)
            {
                continue;
            }

            var site = this.body.Subject(statement.Source);
            MirDataflow.Grow(ref this.refs, this.refCount + 1);
            this.refs[this.refCount++] = new(s, types.OriginAtom(this.key, origin, site), site);
        }

        var count = this.anchorCount = this.slice.Atoms.Count;
        MirDataflow.Grow(ref this.anchors, count);
        Array.Fill(this.anchors, -1, 0, count);
        for (var r = this.refCount - 1; r >= 0; r--)
        {
            this.anchors[this.refs[r].Own] = r;
        }
    }

    // A view's class (2a0-D §6.2): its active rows (a row's case mask within the view's borrow binders) and its Refs' containment edges; a
    // view equal to an earlier one joins that class, whose condition keeps the borrow binders every view of it has (SPEC 15.6.5).
    private void Classify(long view, ulong borrows)
    {
        var words = this.rowWords;
        MirDataflow.Grow(ref this.rowBits, (this.classCount + 1) * words);
        MirDataflow.Grow(ref this.classes, this.classCount + 1);
        var bits = this.rowBits.AsSpan(this.classCount * words, words);
        bits.Clear();
        var rows = this.slice.Rows;
        for (var r = 0; r < rows.Count; r++)
        {
            if ((rows[r].CaseMask & ~borrows) == 0)
            {
                bits[r >> 6] |= 1UL << r;
            }
        }

        var start = this.containedCount;
        for (var r = 0; r < this.refCount; r++)
        {
            this.Contain(r);
        }

        var edges = this.contained.AsSpan(start, this.containedCount - start);
        for (var c = 0; c < this.classCount; c++)
        {
            ref var known = ref this.classes[c];
            if (this.rowBits.AsSpan(c * words, words).SequenceEqual(bits) && this.contained.AsSpan(known.Start, known.Count).SequenceEqual(edges))
            {
                (known.Views, known.Borrows) = (known.Views | (1UL << (int)view), known.Borrows & borrows);
                this.containedCount = start;
                return;
            }
        }

        this.classes[this.classCount++] = new(1UL << (int)view, borrows, start, edges.Length);
    }

    // A Ref's containment edges under the view (SPEC 15.6.3; 2a0-D D14): from its last step outward, a uniq layer's Origin outlives the
    // Ref's and the walk continues, a shared one's too and the walk stops (a Slice's Index is a shared one, E6), an own or inline step adds
    // no edge, and a raw Deref stops. MirSteps gives a pair layer its case's step (none in the owner case) and a PreparedInput Deref Inline.
    private void Contain(int r)
    {
        ref readonly var found = ref this.refs[r];
        var place = this.body.Statements[found.Statement].Rvalue.Place;
        var count = this.steps.Walk(this.body, place, reborrow: false, ref this.walk);
        for (var i = count - 1; i >= 0; i--)
        {
            var step = this.walk[i];
            if (step.Class == MirStepClass.Raw)
            {
                return;
            }

            if (step.Class is not (MirStepClass.Uniq or MirStepClass.Shared))
            {
                continue;
            }

            if (cases.Type(this.body.TypeOf(place with { Projection = step.Prefix })!).Origin is { } layer)
            {
                MirDataflow.Grow(ref this.contained, this.containedCount + 1);
                this.contained[this.containedCount++] = new(types.OriginAtom(this.key, layer, found.Site), found.Own, r);
            }

            if (step.Class == MirStepClass.Shared)
            {
                return;
            }
        }
    }

    // A problem Judge found: its code, requirement, subject, Reason, its class's views and its facts.
    private readonly record struct Pending(
        DiagnosticCode Code,
        DiagnosticRequirement Requirement,
        SourceRef Subject,
        MirReason Reason,
        ulong Views,
        int FactStart,
        int FactCount);

    // A Ref that issues a borrow: its statement, its own Origin's table index and its site.
    private readonly record struct Ref(int Statement, int Own, SourceRef Site);

    // A containment edge: the layer's Origin outlives the Ref's (both table indices), at Ref.
    private readonly record struct Edge(int Tail, int Head, int Ref);

    // An edge of a class's graph (ClassEdges): its ends (-1: a row or Ref with an Unclassed end, which forms none), site and row (-1: a
    // containment edge).
    private readonly record struct ClassEdge(int Tail, int Head, SourceRef Site, int Row);

    // A class of views: their bits, the borrow binders they all share and its containment edges in `contained`.
    private record struct Class(ulong Views, ulong Borrows, int Start, int Count);
}
