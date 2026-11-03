// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

// MirReport's merged view, which publication reads (R4-M11's merge across bodies; R6-check H3-2, design §2).
// - Responsibility: one merged problem per code, subject, Reason and class (Merged), in the order of its first member; the class is a
//   record's MirRecordClass, or none for the analyses' problems, which merge across body ordinals and NoBody; each merged problem's
//   members (Members), its facts (MergedFacts) and its Ordinal, its Reason's position among the merged problems of its code, subject and
//   class (Q-R4-4); members naming two requirements add the InternalInvariant_Kd merged problem at the subject (a read cannot call Add).
// - Inputs: All and its facts, as Add and Clear left them.
// - Outputs: Merged, Members and MergedFacts.
// - Invariants: every problem of All is a member of exactly one merged problem, which takes its first member's code, requirement, subject
//   and Reason (a disagreement's InternalInvariant_Kd may have none); members are in All's order; a member whose requirement disagrees
//   stays in its key's merged problem, its body among the members and its causes counted, where Add drops such a problem for the defect
//   (R4's U1b note n1); a merged problem's facts are its first member's but their Cause tail, then, when every member is derived, their
//   causes once each, a Value >= 0 the index in Merged of the problem's merged problem (the remap is total) but never its own (members
//   causing one another, across NoBody and a body, name no other problem; R4's U1b note n2), -1 (2g-1's Binding cause) kept; the view is
//   computed once, on its first read after an Add or a Clear, in pooled storage, so a warm version allocates nothing.
// - Prohibitions: no write to All or its facts; no publication.
internal sealed partial class MirReport
{
    private readonly Dictionary<(DiagnosticCode Code, SourceRef Subject, MirReason Reason, MirRecordClass? Class), int> merges = new(); // Each key's.
    private readonly Dictionary<(DiagnosticCode Code, SourceRef Subject, MirRecordClass? Class), int> ordinals = new(); // Each next Ordinal.
    private readonly List<MergedProblem> merged = [];
    private readonly List<(int Start, int Count)> memberSpans = []; // Each merged problem's members.
    private readonly List<int> members = []; // The merged problems' members, by index in All.
    private readonly List<int> owners = []; // Each problem's merged problem.
    private readonly List<MirFact> mergedFacts = [];
    private readonly List<(int Start, int Count)> factSpans = []; // Each merged problem's facts.
    private bool isMerged; // Whether the view reflects All; an Add or a Clear resets it.

    /// <summary>Gets the merged problems, one per code, subject, Reason and class, in the order of their first members.</summary>
    internal ReadOnlySpan<MergedProblem> Merged
    {
        get
        {
            this.MergeAll();
            return CollectionsMarshal.AsSpan(this.merged);
        }
    }

    /// <summary>Gets the members of a merged problem, which name their bodies and views.</summary>
    /// <param name="merged">The merged problem's index in <see cref="Merged"/>.</param>
    /// <returns>Its members' indices in <see cref="All"/>, in that order.</returns>
    internal ReadOnlySpan<int> Members(int merged)
    {
        this.MergeAll();
        return CollectionsMarshal.AsSpan(this.members).Slice(this.memberSpans[merged].Start, this.memberSpans[merged].Count);
    }

    /// <summary>Gets the facts of a merged problem: its first member's but their Cause tail, then, when every member is derived, the
    /// members' causes once each, each Value an index in <see cref="Merged"/> (-1 kept).</summary>
    /// <param name="merged">The merged problem's index in <see cref="Merged"/>.</param>
    /// <returns>Its facts.</returns>
    internal ReadOnlySpan<MirFact> MergedFacts(int merged)
    {
        this.MergeAll();
        return CollectionsMarshal.AsSpan(this.mergedFacts).Slice(this.factSpans[merged].Start, this.factSpans[merged].Count);
    }

    // Computes the view unless it reflects All: each problem's merged problem and the counts of their members first, then the members of
    // each, in All's order, from the counts' running sum, then the facts.
    private void MergeAll()
    {
        if (this.isMerged)
        {
            return;
        }

        this.isMerged = true;
        var all = this.All;
        this.merges.Clear();
        this.ordinals.Clear();
        this.merged.Clear();
        this.memberSpans.Clear();
        this.owners.Clear();
        var defect = DiagnosticRequirement.Ownership(OwnershipRequirement.Internal);
        foreach (ref readonly var problem in all)
        {
            var slot = this.Open(problem);
            this.owners.Add(slot);
            CollectionsMarshal.AsSpan(this.memberSpans)[slot].Count++;
            if (CollectionsMarshal.AsSpan(this.merged)[slot].Requirement != problem.Requirement)
            {
                this.Open(new(DiagnosticCode.InternalInvariant_Kd, defect, problem.Subject, MirProblem.NoBody, 0));
            }
        }

        var spans = CollectionsMarshal.AsSpan(this.memberSpans);
        var start = 0;
        foreach (ref var span in spans)
        {
            (span, start) = ((start, 0), start + span.Count);
        }

        CollectionsMarshal.SetCount(this.members, all.Length);
        var order = CollectionsMarshal.AsSpan(this.members);
        for (var i = 0; i < all.Length; i++)
        {
            ref var span = ref spans[this.owners[i]];
            order[span.Start + span.Count++] = i;
        }

        this.MergeFacts();
    }

    // The merged problem of a problem's key, opened with the next Ordinal of its code, subject and class if the key has none.
    private int Open(in MirProblem problem)
    {
        var recordClass = problem.RecordClass;
        ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(
            this.merges, (problem.Code, problem.Subject, problem.Reason, recordClass), out var exists);
        if (!exists)
        {
            ref var ordinal = ref CollectionsMarshal.GetValueRefOrAddDefault(this.ordinals, (problem.Code, problem.Subject, recordClass), out _);
            slot = this.merged.Count;
            this.merged.Add(new(problem.Code, problem.Requirement, problem.Subject, problem.Reason, recordClass, ordinal++));
            this.memberSpans.Add((0, 0));
        }

        return slot;
    }

    // Each merged problem's facts: its first member's but their Cause tail, then, when every member is derived (R4-M132's rule over the
    // members: a direct one wins), the members' causes.
    private void MergeFacts()
    {
        this.mergedFacts.Clear();
        this.factSpans.Clear();
        for (var m = 0; m < this.merged.Count; m++)
        {
            var start = this.mergedFacts.Count;
            var group = this.Members(m);
            var derived = group.Length > 0;
            foreach (var member in group)
            {
                derived &= Causes(this.Facts(member)) > 0;
            }

            if (group.Length > 0)
            {
                var first = this.Facts(group[0]);
                this.mergedFacts.AddRange(first[..^Causes(first)]);
            }

            var tail = this.mergedFacts.Count;
            for (var i = 0; derived && i < group.Length; i++)
            {
                this.AddCauses(group[i], tail);
            }

            this.factSpans.Add((start, this.mergedFacts.Count - start));
        }
    }

    // Adds a member's causes after the merged facts' tail start, each Value >= 0 remapped to the index in Merged of its problem's merged
    // problem and -1 kept, once each; a cause in the member's own merged problem names no other problem and is skipped.
    private void AddCauses(int member, int tail)
    {
        var facts = this.Facts(member);
        foreach (var cause in facts[^Causes(facts)..])
        {
            var remapped = cause.Value < 0 ? cause : cause with { Value = this.owners[(int)cause.Value] };
            if (remapped.Value != this.owners[member] && CollectionsMarshal.AsSpan(this.mergedFacts)[tail..].IndexOf(remapped) < 0)
            {
                this.mergedFacts.Add(remapped);
            }
        }
    }

    /// <summary>A merged problem of <see cref="Merged"/>: its first member's code, requirement, subject and Reason, its class and its
    /// Ordinal.</summary>
    /// <param name="Code">The code.</param>
    /// <param name="Requirement">The requirement its first member names.</param>
    /// <param name="Subject">The syntax the problem is about.</param>
    /// <param name="Reason">The relation an Origin problem names; the default for any other problem.</param>
    /// <param name="Class">The record class of a construction or collection record; null for the analyses' problems, merged across bodies
    /// and <see cref="MirProblem.NoBody"/>.</param>
    /// <param name="Ordinal">Its Reason's position among the merged problems of its code, subject and class, in the order of their first
    /// members.</param>
    internal readonly record struct MergedProblem(
        DiagnosticCode Code,
        DiagnosticRequirement Requirement,
        SourceRef Subject,
        MirReason Reason,
        MirRecordClass? Class,
        int Ordinal);
}
