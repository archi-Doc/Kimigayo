// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns Core's one publisher of MIR records (R6-check H3-2; DECISIONS R5-M184): it decides how a MIR record is published, never whether it
// exists, whether it is dependent or its requirement (the recording rule's, R5-M23); and the one location form, Locate, which code
// generation's records take too (DECISIONS R5-M65).
// Input: the program of the latest Analyze, its construction records (MirProgram.Records with MirRecordClass.Construction, R6-check H3-1)
// and validator findings, and the program the latest collection recorded into, its collection records (MirRecordClass.Collection), the
// library's as well as the source modules'; for Publish, a program's MirReport.Merged problem, its members' bodies, binders and views,
// its merged facts, the C1-F4 rows a Relation fact names (TyCtxt.OriginRows) and, for a Binding cause, Binding.FailureCauses at its site.
// Output: adapter calls only (DiagnosticCollection.ReportLocation, Koto.Report and Koto.ReportDerived, in the subject's collection); no table
// of its own. MirRecords is the one selection of the records, which Analysis, Problems, the generation gate and PublishMirRecords read.
// Invariants: until H5, which publishes every merged problem and removes the selection, the publisher publishes today's rows only, on every
// pipeline (DECISIONS R5-M156): the construction records, the findings as InternalInvariant_Kd, then the collection records, each located
// in the ControlFlow requirement. A record is a code without facts, so its location is its subject (DIAGNOSTICS 4.1, DECISIONS R4-M71): the
// records of two nodes at one location, such as the siblings a for expansion synthesizes with its span, are one problem. The MIR analyses'
// problems stay unpublished until H5 (R4-M1). Publish maps one merged problem (design 3.1-3.4): Located (a record class, or a code of
// category Unsupported, Resource or Internal without Arguments) by its location, which carries no fact, any other at its subject with its
// Ordinal as the condition; KeyOf is the key either form records, so a Cause names its problem's record; a problem with Cause facts is
// PrerequisiteUnavailable_Kd naming its causes' keys, a MIR cause's by KeyOf, a Binding cause's (-1) Binding's; a located problem with a
// fact, a Binding cause without Binding's causes, an Inlined fact and a kind without a row are broken contracts, InternalInvariant_Kd at
// the subject under Ownership.Internal, as MirReport's defects. The fact table has one arm per MirFactKind: a site fact is a related
// location with DIAGNOSTICS' A6 role and wording, a Relation fact Binding.OriginRelationFacts' evidence and locations, Required, Limit
// and Table OwnershipStorageLimit_Kd's arguments and table; the case text is Binding.CaseFact's over the members' masked views, only when
// every member gives the same (Binding renders it). Publish has no production caller until H5.
// Must not: be steered by a pipeline mode, flag or optional parameter, or publish a MIR record any other way.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public partial class Compilation
{
    // The requirement of the publisher's broken-contract records, as of MirReport's (Ownership.Internal).
    private static readonly DiagnosticRequirement Defect = DiagnosticRequirement.Ownership(OwnershipRequirement.Internal);

    private readonly List<PairBinder> caseBinders = []; // The binders the first member's case text names.
    private readonly List<PairBinder> memberBinders = []; // Another member's.

    /// <summary>Publishes one merged problem of a program's report (R6-check H3-2, design 3.1-3.4): a located problem by its location, any
    /// other at its subject with its Ordinal as the condition, a derived one as <c>PrerequisiteUnavailable_Kd</c> naming its causes' keys;
    /// a broken contract as <c>InternalInvariant_Kd</c> at the subject. No production caller until H5.</summary>
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
        List<DiagnosticKey>? causes = null;
        foreach (var fact in facts)
        {
            // The fact table (design 3.3), one arm per MirFactKind; the resolver (3.4): a MIR cause by its merged index, a Binding cause (-1)
            // by Binding's causes at its site.
            switch (fact.Kind)
            {
                case Mir.MirFactKind.Anchor:
                case Mir.MirFactKind.Use:
                case Mir.MirFactKind.Store:
                case Mir.MirFactKind.Reservation:
                case Mir.MirFactKind.Evaluated:
                case Mir.MirFactKind.EffectRead:
                case Mir.MirFactKind.EffectWrite:
                case Mir.MirFactKind.EffectUnknown:
                    var (role, label) = RelatedRole(fact.Kind);
                    (related ??= []).Add((role, this.SyntaxOf(fact.Site), label));
                    break;
                case Mir.MirFactKind.Relation when this.Relation(program, problem, body, fact) is { } relation:
                    (evidence, var lines) = relation;
                    (related ??= []).AddRange(lines ?? []);
                    break;
                case Mir.MirFactKind.Required:
                    required = fact.Value;
                    break;
                case Mir.MirFactKind.Limit:
                    limit = fact.Value;
                    break;
                case Mir.MirFactKind.Table:
                    evidence = [((Mir.MirStorageTable)fact.Value).ToString()];
                    break;
                case Mir.MirFactKind.Cause when fact.Value >= 0:
                    (causes ??= []).Add(this.KeyOf(program, (int)fact.Value));
                    break;
                case Mir.MirFactKind.Cause when this.Binding.FailureCauses(this.SyntaxOf(fact.Site)) is { } failures:
                    (causes ??= []).AddRange(failures);
                    break;
                case Mir.MirFactKind.Relation: // A relation without a body, Reason ends or row source.
                case Mir.MirFactKind.Cause: // A Binding cause at a node Binding resolved.
                case Mir.MirFactKind.Inlined: // MirReport.Add drops its problem.
                default: // A kind without a row.
                    this.Locate(Defect, DiagnosticCode.InternalInvariant_Kd, problem.Subject);
                    return;
            }
        }

        var node = this.SyntaxOf(problem.Subject);
        if (causes is not null)
        {
            node.ReportDerived(problem.Requirement, [.. causes], (ushort)problem.Ordinal);
            return;
        }

        var found = this.CaseFact(program, merged, out var single);
        node.Report(
            problem.Requirement,
            problem.Code,
            required,
            limit,
            Binding.CaseNote(null, found, single),
            evidence: evidence ?? Binding.CaseEvidence(problem.Code, null, found),
            related: Binding.WithCaseDeclarations(related?.ToArray(), this.caseBinders, found),
            condition: (ushort)problem.Ordinal);
    }

    // The related location of a fact naming a site (design 3.3; DIAGNOSTICS' A6 fact tables): its role and wording.
    private static (string Role, string Label) RelatedRole(Mir.MirFactKind kind) => kind switch
    {
        Mir.MirFactKind.Anchor => ("borrow", "borrow that created the loan"),
        Mir.MirFactKind.Use => ("loan", "later use of the value retaining the conflicting loan"),
        Mir.MirFactKind.Store => ("loan", "value retaining the conflicting loan"),
        Mir.MirFactKind.Reservation => ("reservation", "conflicting exclusive call reservation"),
        Mir.MirFactKind.Evaluated => ("default", "the default this call omits, evaluated here, does this"),
        Mir.MirFactKind.EffectRead => ("declaration", "the call or destruction may read this static"),
        Mir.MirFactKind.EffectWrite => ("declaration", "the call or destruction may write this static"),
        Mir.MirFactKind.EffectUnknown => ("declaration", "the call's or destruction's effects are unknown, so they may access this static"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    // Whether a problem's location is its subject (design 3.2): a construction or collection record, or a code of category Unsupported,
    // Resource or Internal without message arguments.
    private static bool Located(in Mir.MirReport.MergedProblem problem)
        => problem.Class is not null || (DiagnosticEntries.TryGet(problem.Code, out var entry) && entry.Arguments is null &&
            entry.Category is DiagnosticCategory.Unsupported or DiagnosticCategory.Resource or DiagnosticCategory.Internal);

    // The MIR's records of the latest Analyze, each located in the ControlFlow requirement, in MirRecords' order (today's rows, until H5).
    private void PublishMirRecords()
    {
        this.problems.Clear();
        this.MirRecords(this.problems);
        foreach (var problem in this.problems)
        {
            this.Locate(DiagnosticRequirement.ControlFlow, problem.Code, problem.Subject);
        }
    }

    // The case text of a merged problem (design 3.3; DECISIONS BU-82): Binding.CaseFact over each member's body's binders and its views
    // masked by MirCases.Count first, since some producers write every view as ulong.MaxValue (Q-BU-2); given only when every member gives
    // the same text, and a NoBody member gives none. The first member's named binders are left in caseBinders.
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

            var body = program.Bodies[member.Body];
            var count = Mir.MirCases.Count(body);
            var mask = count >= 64 ? ulong.MaxValue : (1UL << (int)count) - 1;
            var text = Binding.CaseFact(body.Binders, member.Views & mask, i == 0 ? this.caseBinders : this.memberBinders, out var one);
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
    // the Reason's, the longer one also the supplier (Q-BU-1). Null for a broken contract: no body, no Reason ends, or a row MirOrigins forms.
    private (object[] Evidence, (string Role, Koto At, string? Label)[]? Related)? Relation(
        Mir.MirProgram program, in Mir.MirReport.MergedProblem problem, int body, Mir.MirFact fact)
    {
        if (body == Mir.MirProblem.NoBody || problem.Reason is not { Longer: { } longer, Shorter: { } shorter, Equality: var equality })
        {
            return null;
        }

        var refuted = problem.Code == DiagnosticCode.UnsatisfiedOriginRelation_Kd;
        if (fact.Value < 0)
        {
            return Binding.OriginRelationFacts(new(this.SyntaxOf(fact.Site), longer, longer, shorter, equality, null, refuted, WellFormed: true), "wellFormed");
        }

        var row = this.Types.OriginRows(program.Bodies[body].Key.Body).Rows[(int)fact.Value];
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
            : Binding.OriginRelationFacts(new(at, longer, longer, shorter, equality, row.Destination, refuted, row.Clause, WellFormed: wellFormed), source);
    }

    // A code without facts keyed by its subject's location, in the subject's collection (DIAGNOSTICS 4.1; DECISIONS R5-M65, R4-M71).
    private void Locate(DiagnosticRequirement requirement, DiagnosticCode code, SourceRef subject)
    {
        var node = this.SyntaxOf(subject);
        node.DiagnosticCollection?.ReportLocation(requirement, node.Span, code, node.CodeContext.SourceDocument);
    }

    // The key a merged problem is published under, by Publish's form, so a derived problem names it as its cause (design 3.2).
    private DiagnosticKey KeyOf(Mir.MirProgram program, int merged)
    {
        var problem = program.Problems.Merged[merged];
        var node = this.SyntaxOf(problem.Subject);
        return !Located(problem) ? node.KeyOf(problem.Requirement, (ushort)problem.Ordinal)
            : node.DiagnosticCollection?.LocationKey(problem.Requirement, node.Span, node.CodeContext.SourceDocument) ?? DiagnosticKey.Unresolved;
    }

    // The MIR records of the latest Analyze, added to the list when one is given: the construction records and validator findings, the
    // library's as well as the source modules', and every record of the checked set (SPEC 23.2.2: check reports every error of the checked
    // set, its layout obligations among them; DECISIONS R5-M156), on every pipeline.
    private int MirRecords(List<Problem>? into)
    {
        var count = 0;
        if (this.mir is { } program)
        {
            count = Add(program.Records(Mir.MirRecordClass.Construction), into);
            count += program.Findings.Count;
            foreach (var finding in program.Findings)
            {
                into?.Add(new(DiagnosticCode.InternalInvariant_Kd, finding.Subject));
            }
        }

        return this.collected is { } collected ? count + Add(collected.Records(Mir.MirRecordClass.Collection), into) : count;

        static int Add(Mir.MirProgram.RecordRows rows, List<Problem>? into)
        {
            var count = 0;
            foreach (var row in rows)
            {
                count++;
                into?.Add(row);
            }

            return count;
        }
    }
}
