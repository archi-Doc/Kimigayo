// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns Core's one publisher of MIR records (R6-check H3-2; DECISIONS R5-M184): it decides how a MIR record is published, never whether it
// exists, whether it is dependent or its requirement (the recording rule's, R5-M23); and the one location form, Locate, which code
// generation's records take too (DECISIONS R5-M65).
// Input: the program of the latest Analyze: its report's MirReport.Merged problems (its construction and collection records among them,
// MirRecordClass, R6-check H3-1; the analyses' problems), their members' bodies, binders and views, their merged facts, the C1-F4 rows a
// Relation fact names (TyCtxt.OriginRows) and, for a Binding cause, Binding.RestingCauses at its site (DECISIONS R5-M186); and its
// validator findings.
// Output: adapter calls only (DiagnosticCollection.ReportLocation, Koto.ReportIn and Koto.ReportDerivedIn, in the store Core's store
// decision gives the subject, StoreOf); no table of its own.
// Invariants: PublishMirRecords publishes every merged problem through Publish, then each finding as InternalInvariant_Kd located in the
// ControlFlow requirement (DECISIONS R5-M156, R5-M185): the rows Compilation.Problems gives, in its order. A record is a
// code without facts, so its location is its subject (DIAGNOSTICS 4.1, DECISIONS R4-M71): the records of two nodes at one location, such as
// the siblings a for expansion synthesizes with its span, are one problem. Publish maps one merged problem (design 3.1-3.4): Located (a
// record class, or a code of category Unsupported, Resource or Internal without Arguments) by its location, which carries no fact, any
// other at its subject with its Ordinal as the condition; KeyOf is the key either form records, so a Cause names its problem's record; a
// problem with Cause facts is PrerequisiteUnavailable_Kd naming its causes' keys, a MIR cause's by KeyOf, a Binding cause's (-1) Binding's;
// a located problem with a fact, a Binding cause without Binding's causes, an Inlined fact and a kind without a row are broken contracts,
// InternalInvariant_Kd at the subject under Ownership.Internal, as MirReport's defects. The fact table has one arm per ProblemFactKind: a site
// fact is a related location with DIAGNOSTICS' A6 role and wording, a Relation fact Binding.OriginRelationFacts' evidence and locations,
// Required, Limit and Table OwnershipStorageLimit_Kd's arguments and table; the case text is Binding.CaseFact's over the members' masked
// views, only when every member gives the same (Binding renders it).
// Must not: be steered by a mode, flag or optional parameter, or publish a MIR record any other way.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public partial class Compilation
{
    // The requirement of the publisher's broken-contract records, as of MirReport's (Ownership.Internal).
    private static readonly DiagnosticRequirement Defect = DiagnosticRequirement.Ownership(OwnershipRequirement.Internal);

    // The requirement of a refused library's own record (B's ruling 40 (i)): a broken build, Binding's internal failure.
    private static readonly DiagnosticRequirement RefusedLibrary = DiagnosticRequirement.Binding(BindingFailure.Internal);

    private readonly List<PairBinder> caseBinders = []; // The binders the first member's case text names.
    private readonly List<PairBinder> memberBinders = []; // Another member's.

    // The store of a library subject's record (Core's store decision, StoreOf; PLAN G949 Stage 2 U2a): this compilation's root collection.
    private DiagnosticCollection LibraryStore => this.Kotonoha.DiagnosticCollection;

    /// <summary>Publishes one merged problem of a program's report (R6-check H3-2, design 3.1-3.4): a located problem by its location, any
    /// other at its subject with its Ordinal as the condition, a derived one as <c>PrerequisiteUnavailable_Kd</c> naming its causes' keys;
    /// a broken contract as <c>InternalInvariant_Kd</c> at the subject.</summary>
    /// <param name="program">The program whose report holds the problem.</param>
    /// <param name="merged">The problem's index in the report's <c>Merged</c>.</param>
    internal void Publish(Mir.MirProgram program, int merged)
    {
        var problem = program.Problems.Merged[merged];
        var facts = program.Problems.MergedFacts(merged);
        if (Located(problem))
        {
            // ReportLocation takes no fact, so a located problem has none (design 3.2, N3).
            this.Locate(facts.IsEmpty ? problem.Requirement : Defect, facts.IsEmpty ? problem.Code : DiagnosticCode.InternalInvariant_Kd, problem.Subject);
            return;
        }

        var body = program.Problems.All[program.Problems.Members(merged)[0]].Body; // The body whose views and rows the facts name.
        (object? required, object? limit, object?[]? evidence) = (null, null, null);
        List<(string Role, Koto At, string? Label)>? related = null;
        (string Role, Koto In, SourceSpan Span, string? Label)[]? spans = null;
        List<DiagnosticKey>? causes = null;
        foreach (var fact in facts)
        {
            // The fact table (design 3.3), one arm per ProblemFactKind; the resolver (3.4): a MIR cause by its merged index, a Binding cause (-1)
            // by Binding's causes at its site.
            switch (fact.Kind)
            {
                case ProblemFactKind.Anchor:
                case ProblemFactKind.Use:
                case ProblemFactKind.Store:
                case ProblemFactKind.Reservation:
                case ProblemFactKind.Evaluated:
                case ProblemFactKind.EffectRead:
                case ProblemFactKind.EffectWrite:
                case ProblemFactKind.EffectUnknown:
                    var (role, label) = RelatedRole(fact.Kind);
                    (related ??= []).Add((role, this.SyntaxOf(fact.Site), label));
                    break;
                case ProblemFactKind.Relation when this.Relation(program, problem, body, fact) is { } relation:
                    (evidence, var lines, spans) = relation;
                    (related ??= []).AddRange(lines ?? []);
                    break;
                case ProblemFactKind.Required:
                    required = fact.Value;
                    break;
                case ProblemFactKind.Limit:
                    limit = fact.Value;
                    break;
                case ProblemFactKind.Table:
                    evidence = [((Mir.MirStorageTable)fact.Value).ToString()];
                    break;
                case ProblemFactKind.Cause when fact.Value >= 0:
                    (causes ??= []).Add(this.KeyOf(program, (int)fact.Value));
                    break;
                case ProblemFactKind.Cause when this.Binding.RestingCauses(this.SyntaxOf(fact.Site)) is { } resting:
                    (causes ??= []).AddRange(resting);
                    break;
                case ProblemFactKind.Relation: // A relation without a body, Reason ends or row source.
                case ProblemFactKind.Cause: // A Binding cause at a node with no resting causes, such as one Binding resolved.
                case ProblemFactKind.Inlined: // MirReport.Add drops its problem.
                default: // A kind without a row, as Binding's kinds are here (C-103).
                    this.Locate(Defect, DiagnosticCode.InternalInvariant_Kd, problem.Subject);
                    return;
            }
        }

        // Reported into the store Core's store decision gives the subject (StoreOf; PLAN G949 Stage 2 U3-3, the owner's O-425 (a)), whole:
        // a library subject's record keeps its case evidence and related declarations.
        var node = this.SyntaxOf(problem.Subject);
        var store = this.StoreOf(node);
        if (causes is not null)
        {
            node.ReportDerivedIn(store, problem.Requirement, [.. causes], (ushort)problem.Ordinal);
            return;
        }

        var found = this.CaseFact(program, merged, out var single);
        node.ReportIn(
            store,
            problem.Requirement,
            problem.Code,
            required,
            limit,
            Binding.CaseNote(null, found, single),
            evidence: evidence ?? Binding.CaseEvidence(problem.Code, null, found),
            related: Binding.WithCaseDeclarations(related?.ToArray(), this.caseBinders, found),
            condition: (ushort)problem.Ordinal,
            relatedSpans: spans);
    }

    /// <summary>Publishes a refused library (PLAN G949 Stage 2 U2a; design 5.3, R3; C2 (4); B's ruling 40 (i)): each Binding-side record as
    /// InternalInvariant_Kd at its library location in this compilation's store for a library subject (StoreOf), under its own
    /// requirement, and the refusal's own InternalInvariant_Kd at the first counted record's location, so a refused library always
    /// publishes an Error, a syntax Warning alone included. A record without a document is at the project file, as Binding reports it. A
    /// syntax record is never here: it stays the library module's syntax list's, which that list's owner publishes (form (R)).</summary>
    /// <param name="refusal">The refusal (LibraryLayer.Refusal).</param>
    internal void PublishRefusal(LibraryLayer.Refused refusal)
    {
        foreach (var record in refusal.Records)
        {
            this.PublishLocated(record.Requirement, record);
        }

        this.PublishLocated(RefusedLibrary, refusal.First);
    }

    /// <summary>Gets Core's one store decision for a record of a node (PLAN G949 Stage 2 U2a; design 5.3, M-R2-B1; U3-3): a library node's
    /// record goes to this compilation's root collection, at the node's own span and document (the library's collections are the freeze's,
    /// detached at U2b); any other node's to its own collection. Publish, Locate, KeyOf and Binding's per-record steps (ReportIssue, the
    /// derived records and their prerequisite keys) go through it; PublishRefusal publishes into the root collection directly. The other
    /// direct Report and ReportDerived callers (duplicate declarations, lints, startup, non-failing records, callable proofs, ...) report on
    /// program subjects only because a library with any record is refused (FreezeLibrary); a later caller whose subject may be a library
    /// node reports through this decision.</summary>
    /// <param name="node">The record's subject.</param>
    /// <returns>The collection, or <see langword="null"/> when the node records nowhere.</returns>
    internal DiagnosticCollection? StoreOf(Koto node)
        => node.SyntaxOwner is { IsLibrary: true } ? this.LibraryStore : node.DiagnosticCollection;

    // The related location of a fact naming a site (design 3.3; DIAGNOSTICS' A6 fact tables): its role and wording.
    private static (string Role, string Label) RelatedRole(ProblemFactKind kind) => kind switch
    {
        ProblemFactKind.Anchor => ("borrow", "borrow that created the loan"),
        ProblemFactKind.Use => ("loan", "later use of the value retaining the conflicting loan"),
        ProblemFactKind.Store => ("loan", "value retaining the conflicting loan"),
        ProblemFactKind.Reservation => ("reservation", "conflicting exclusive call reservation"),
        ProblemFactKind.Evaluated => ("default", "the default this call omits, evaluated here, does this"),
        ProblemFactKind.EffectRead => ("declaration", "the call or destruction may read this static"),
        ProblemFactKind.EffectWrite => ("declaration", "the call or destruction may write this static"),
        ProblemFactKind.EffectUnknown => ("declaration", "the call's or destruction's effects are unknown, so they may access this static"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    // Whether a problem's location is its subject (design 3.2): a construction or collection record, or a code of category Unsupported,
    // Resource or Internal without message arguments.
    private static bool Located(in Mir.MirReport.MergedProblem problem)
        => problem.Class is not null || (DiagnosticEntries.TryGet(problem.Code, out var entry) && entry.Arguments is null &&
            entry.Category is DiagnosticCategory.Unsupported or DiagnosticCategory.Resource or DiagnosticCategory.Internal);

    // The rows of the latest Analyze (Compilation.Problems' order): every merged problem through Publish, then each validator finding as
    // InternalInvariant_Kd located in the ControlFlow requirement; none before an analysis and after invalidation.
    private void PublishMirRecords()
    {
        if (this.mir is not { } program)
        {
            return;
        }

        for (var i = 0; i < program.Problems.Merged.Length; i++)
        {
            this.Publish(program, i);
        }

        foreach (var finding in program.Findings)
        {
            this.Locate(DiagnosticRequirement.ControlFlow, DiagnosticCode.InternalInvariant_Kd, finding.Subject);
        }
    }

    // The case text of a merged problem (design 3.3; DECISIONS BU-82): Binding.CaseFact over each member's body's binders and its views,
    // which are bits of that body's views (MirProblem); given only when every member gives the same text, and a NoBody member gives none.
    // The first member's named binders are left in caseBinders.
    private string? CaseFact(Mir.MirProgram program, int merged, out bool single)
    {
        (single, string? found) = (false, null);
        var members = program.Problems.Members(merged);
        for (var i = 0; i < members.Length; i++)
        {
            var member = program.Problems.All[members[i]];
            if (member.Body == Mir.MirProblem.NoBody)
            {
                return null;
            }

            var text = Binding.CaseFact(program.Body(member.Body).Binders, member.Views, i == 0 ? this.caseBinders : this.memberBinders, out var one);
            if (i == 0)
            {
                (found, single) = (text, one);
            }
            else if (text != found)
            {
                return null;
            }
        }

        return found;
    }

    // The fact table's Relation row (design 3.3): an Origin problem's last edge through Binding.OriginRelationFacts, its site, clause,
    // destination and source from the body's C1-F4 row, a containment edge (-1) without a row as wellFormed; the ends are
    // the Reason's, the longer one also the supplier (Q-BU-1) unless the row's own longer end as formed supplied it (Binding.RowSupplier,
    // DECISIONS R4-M133). Null for a broken contract: no body, no Reason ends, or a row MirOrigins forms.
    private (object[] Evidence, (string Role, Koto At, string? Label)[]? Related, (string Role, Koto In, SourceSpan Span, string? Label)[]? Spans)? Relation(
        Mir.MirProgram program, in Mir.MirReport.MergedProblem problem, int body, ProblemFact fact)
    {
        if (body == Mir.MirProblem.NoBody || problem.Reason is not { Longer: { } longer, Shorter: { } shorter, Equality: var equality })
        {
            return null;
        }

        var refuted = problem.Code == DiagnosticCode.UnsatisfiedOriginRelation_Kd;
        if (fact.Value < 0)
        {
            return this.Binding.OriginRelationFacts(new(this.SyntaxOf(fact.Site), longer, longer, shorter, equality, null, refuted, WellFormed: true), "wellFormed");
        }

        var row = this.Types.OriginRows(program.Body(body).Key.Body).Rows[(int)fact.Value];
        var source = row.Source switch
        {
            OriginRowSource.Fit => "fit",
            OriginRowSource.Declared => "declared",
            OriginRowSource.WellFormed => "wellFormed",
            _ => null, // Contained: MirOrigins' own edge, never a row of the view.
        };

        var at = this.SyntaxOf(row.Site);
        var wellFormed = row.Source == OriginRowSource.WellFormed;
        return source is null ? null
            : this.Binding.OriginRelationFacts(new(at, longer, this.Binding.RowSupplier(row.Supplier, longer, at), shorter, equality, row.Destination, refuted, row.Clause, FixedBy: row.FixedBy, WellFormed: wellFormed), source);
    }

    // A code without facts keyed by its subject's location, in the subject's collection (DIAGNOSTICS 4.1; DECISIONS R5-M65, R4-M71).
    private void Locate(DiagnosticRequirement requirement, DiagnosticCode code, SourceRef subject)
    {
        var node = this.SyntaxOf(subject);
        this.StoreOf(node)?.ReportLocation(requirement, node.Span, code, node.CodeContext.SourceDocument);
    }

    // An InternalInvariant_Kd at a located record's place: its library location in the library store, or the project file without one.
    private void PublishLocated(DiagnosticRequirement requirement, LocatedRecord at)
    {
        if (at is { Document: { } document, Span: { } span })
        {
            this.LibraryStore.ReportLocation(requirement, span, DiagnosticCode.InternalInvariant_Kd, document);
        }
        else
        {
            this.Diagnostics.Report(DiagnosticPartition.Binding, DiagnosticCode.InternalInvariant_Kd, this.Project.FilePath);
        }
    }

    // The key a merged problem is published under, by Publish's form, so a derived problem names it as its cause (design 3.2).
    private DiagnosticKey KeyOf(Mir.MirProgram program, int merged)
    {
        var problem = program.Problems.Merged[merged];
        var node = this.SyntaxOf(problem.Subject);
        var store = this.StoreOf(node);
        return !Located(problem) ? node.KeyIn(store, problem.Requirement, (ushort)problem.Ordinal)
            : store?.LocationKey(problem.Requirement, node.Span, node.CodeContext.SourceDocument) ?? DiagnosticKey.Unresolved;
    }
}
