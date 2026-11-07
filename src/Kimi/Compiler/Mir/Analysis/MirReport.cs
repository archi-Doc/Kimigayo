// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the problems the MIR analyses find in one program version and its construction and collection records (A6; DECISIONS R4-M3a, R5-M23,
// R4-M71; R6-check H3-1); their records are MirProblem and ProblemFact (MirProblem.cs, Core/ProblemFact.cs).
// Input: Add calls, each a problem with its requirement, which its recording rule names (R5-M23), its body (a record's MirRecordClass,
// through MirProgram.Report) and its facts (related sites, added by their first producer).
// Output: one problem per code, subject, body and Reason (R4-M71: views number one body's cases; 2a0-D
// D18: an Origin problem's relation, the default for other problems) with the views that have it merged, in insertion order (bodies in
// program order), and each problem's facts by its index, which Add returns (R4-M132). A merge keeps the first problem, its requirement and
// facts included, and ORs the views; merged problems name one requirement, and a disagreement is a recording rule's defect, reported as
// one InternalInvariant_Kd problem at the subject, as is a Cause fact whose Value is neither -1 nor an index in All (H3-2 N2), which no
// producer writes (rule W's causes are indices Add returned). Every analysis InternalInvariant_Kd names Ownership.Internal (these defects
// and A5's broken contracts). Add is the one home of the derived-problem rules: a problem with an Inlined fact, a Language problem found
// inside an inline default copy, is its template's check and dropped (R4-M10, SPEC 7.2.3, R4-M69); a problem with
// Cause facts (its tail) is derived (rule W, SPEC 15.6.5, R4-M132), and a merged problem is derived exactly when every report it groups
// is, its causes their union (a direct report drops the first's Cause tail; a derived one adds its new causes). Merged, the view
// publication reads (MirReport.Merged.cs; R4-M11, R6-check H3-2), has one merged problem per code, subject, Reason and class, in the order
// of its first member, with its members (indices in All), its facts (its first member's but their Cause tail, then by the rule above
// over its members, each Cause's Value an index in Merged, once each, never its own) and its Ordinal, its Reason's position among the merged problems of
// its code, subject and class; the class keeps each record class's rows apart from the other's and from the analyses' problems, which
// merge across bodies and NoBody: A4 (NoBody) and A5 (a body) both record Unsupported_Kd and OwnershipDepthLimit_Kd under one requirement
// each (Ownership.Unsupported, Ownership.DepthLimit), so at one subject and Reason they merge and never disagree, and A4's obligation
// codes are never A5's. Members naming two requirements add the InternalInvariant_Kd merged problem at the subject, which, naming
// Ownership.Internal, never disagrees again. Merged is computed on its first read after an Add or a Clear. Storage is pooled, so a warm
// version allocates nothing.
// Must not publish, decide acceptance, derive a requirement from a code, or keep a problem a rule of Add drops.

using System.Runtime.InteropServices;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

/// <summary>The problems of one program version, one per code, subject, body and Reason: its construction and collection records and the
/// analyses' problems, which Compilation publishes from <see cref="Merged"/> (PublishMirRecords) and whose Errors decide acceptance
/// (DECISIONS R5-M185): a problem added again ORs its views into the first, whose requirement it must name; <see cref="Merged"/> merges
/// them across bodies for publication.</summary>
internal sealed partial class MirReport
{
    private readonly Dictionary<(DiagnosticCode Code, SourceRef Subject, int Body, MirReason Reason), int> index = new(); // The problem of each key.
    private readonly List<MirProblem> problems = [];
    private readonly List<ProblemFact> facts = [];
    private readonly List<(int Start, int Count)> spans = []; // Each problem's facts.

    /// <summary>Gets the problems in the order they were first added.</summary>
    internal ReadOnlySpan<MirProblem> All => CollectionsMarshal.AsSpan(this.problems);

    /// <summary>Gets the facts of a problem.</summary>
    /// <param name="problem">The problem's index in <see cref="All"/>.</param>
    /// <returns>Its facts.</returns>
    internal ReadOnlySpan<ProblemFact> Facts(int problem) => CollectionsMarshal.AsSpan(this.facts).Slice(this.spans[problem].Start, this.spans[problem].Count);

    /// <summary>Adds a problem with its facts, or merges its views into the first problem of the same code, subject, body and Reason, which must
    /// name the same requirement: a merge that names another is the recording rules' defect, an InternalInvariant_Kd problem at the
    /// subject, as is a Cause fact naming no problem (neither -1 nor an index in <see cref="All"/>). A problem with an Inlined fact is
    /// dropped: found inside an inline default copy, it is its template's check (SPEC 7.2.3, R4-M69). A merged problem is derived exactly
    /// when every report of it is, its Cause facts their union (R4-M132).</summary>
    /// <param name="problem">The problem.</param>
    /// <param name="facts">Its facts, kept for a new problem, of which only the Cause facts merge.</param>
    /// <returns>The problem's index in <see cref="All"/>, the first problem's on a merge; -1 for a dropped problem and for a defect.</returns>
    internal int Add(in MirProblem problem, ReadOnlySpan<ProblemFact> facts = default)
    {
        var namesNoProblem = false;
        foreach (var fact in facts)
        {
            if (fact.Kind == ProblemFactKind.Inlined)
            {
                return -1;
            }

            namesNoProblem |= fact.Kind == ProblemFactKind.Cause && fact.Value != -1 && (ulong)fact.Value >= (ulong)this.problems.Count;
        }

        if (namesNoProblem)
        {
            return this.Defect(problem);
        }

        this.isMerged = false;
        ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(
            this.index, (problem.Code, problem.Subject, problem.Body, problem.Reason), out var exists);
        if (exists)
        {
            ref var first = ref CollectionsMarshal.AsSpan(this.problems)[slot];
            if (first.Requirement != problem.Requirement)
            {
                return this.Defect(problem);
            }

            first = first with { Views = first.Views | problem.Views };
            this.Merge(slot, facts);
            return slot;
        }

        slot = this.problems.Count;
        this.problems.Add(problem);
        this.spans.Add((this.facts.Count, facts.Length));
        foreach (var fact in facts)
        {
            this.facts.Add(fact);
        }

        return slot;
    }

    /// <summary>Removes every problem, keeping the storage.</summary>
    internal void Clear()
    {
        this.isMerged = false;
        this.index.Clear();
        this.problems.Clear();
        this.facts.Clear();
        this.spans.Clear();
    }

    // The number of a fact span's trailing Cause facts.
    private static int Causes(ReadOnlySpan<ProblemFact> facts)
    {
        var count = 0;
        while (count < facts.Length && facts[facts.Length - 1 - count].Kind == ProblemFactKind.Cause)
        {
            count++;
        }

        return count;
    }

    // A recording rule's defect in place of a problem (R5-M23, H3-2 N2): one InternalInvariant_Kd problem at its subject in its body and
    // views, under Ownership.Internal; -1, Add's value for it.
    private int Defect(in MirProblem problem)
    {
        var defect = DiagnosticRequirement.Ownership(OwnershipRequirement.Internal);
        this.Add(new(DiagnosticCode.InternalInvariant_Kd, defect, problem.Subject, problem.Body, problem.Views));
        return -1;
    }

    // Merges a report's derivedness into a problem (R4-M132, R4-M96's rule per report): a direct problem stays direct; a derived one meeting
    // a direct report drops its Cause tail; meeting a derived report it adds the causes it lacks, moving its span to the end first.
    private void Merge(int problem, ReadOnlySpan<ProblemFact> facts)
    {
        var (start, count) = this.spans[problem];
        var held = Causes(this.Facts(problem));
        if (held == 0)
        {
            return;
        }

        var arriving = Causes(facts);
        if (arriving == 0)
        {
            this.spans[problem] = (start, count - held);
            return;
        }

        foreach (var cause in facts[^arriving..])
        {
            if (this.Facts(problem)[^held..].IndexOf(cause) >= 0)
            {
                continue;
            }

            if (start + count != this.facts.Count)
            {
                var moved = this.facts.Count;
                for (var i = 0; i < count; i++)
                {
                    this.facts.Add(this.facts[start + i]);
                }

                start = moved;
            }

            this.facts.Add(cause);
            (count, held) = (count + 1, held + 1);
            this.spans[problem] = (start, count);
        }
    }
}
