// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

// MirReport's merged view, which publication reads (R4-M11's merge across bodies; R6-check H3-2, design §2).
// - Responsibility: one merged problem per code, subject, Reason and class (Merged), in the order of its first member; the class is a
//   record's MirRecordClass, or none for the analyses' problems, which merge across body ordinals and NoBody; each merged problem's
//   members (Members) and its Ordinal, its Reason's position among the merged problems of its code, subject and class (Q-R4-4).
// - Inputs: All, as Add and Clear left it.
// - Outputs: Merged and Members.
// - Invariants: every problem of All is a member of exactly one merged problem, which takes its first member's code, requirement, subject
//   and Reason; members are in All's order; the view is computed once, on its first read after an Add or a Clear, in pooled storage, so a
//   warm version allocates nothing.
// - Prohibitions: no write to All or its facts; no publication.
internal sealed partial class MirReport
{
    private readonly Dictionary<(DiagnosticCode Code, SourceRef Subject, MirReason Reason, MirRecordClass? Class), int> merges = new(); // Each key's.
    private readonly Dictionary<(DiagnosticCode Code, SourceRef Subject, MirRecordClass? Class), int> ordinals = new(); // Each next Ordinal.
    private readonly List<MergedProblem> merged = [];
    private readonly List<(int Start, int Count)> memberSpans = []; // Each merged problem's members.
    private readonly List<int> members = []; // The merged problems' members, by index in All.
    private readonly List<int> owners = []; // Each problem's merged problem, while the view is computed.
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

    // Computes the view unless it reflects All: each problem's merged problem and the counts of their members first, then the members of
    // each, in All's order, from the counts' running sum.
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
        foreach (ref readonly var problem in all)
        {
            MirRecordClass? recordClass = problem.IsRecord ? (MirRecordClass)problem.Body : null;
            ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(
                this.merges, (problem.Code, problem.Subject, problem.Reason, recordClass), out var exists);
            if (!exists)
            {
                ref var ordinal = ref CollectionsMarshal.GetValueRefOrAddDefault(this.ordinals, (problem.Code, problem.Subject, recordClass), out _);
                slot = this.merged.Count;
                this.merged.Add(new(problem.Code, problem.Requirement, problem.Subject, problem.Reason, recordClass, ordinal++));
                this.memberSpans.Add((0, 0));
            }

            this.owners.Add(slot);
            CollectionsMarshal.AsSpan(this.memberSpans)[slot].Count++;
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
