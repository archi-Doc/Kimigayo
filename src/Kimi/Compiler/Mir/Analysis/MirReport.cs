// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the problems the MIR analyses find in one program version (A6; DECISIONS R4-M3a, R5-M23, R4-M71); their records are MirProblem
// and MirFact (MirProblem.cs, MirFact.cs).
// Input: Add calls, each a problem with its requirement, which its recording rule names (R5-M23), its body and its facts (related sites,
// added by their first producer). Output: one problem per code, subject, body and Reason (R4-M71: views number one body's cases; 2a0-D
// D18: an Origin problem's relation, the default for other problems) with the views that have it merged, in insertion order (bodies in program order), and each problem's facts by its
// index. A merge keeps the first problem, its requirement and facts included, and ORs the views; merged problems name one requirement,
// and a disagreement is a recording rule's defect, reported as one InternalInvariant_Kd problem at the subject. Add is the one home of the
// rules that drop derived problems (R4-M10): a problem with an Inlined fact, a Language problem found inside an inline default copy, is its
// template's check (SPEC 7.2.3, R4-M69). Storage is pooled, so a warm version allocates nothing.
// Must not publish (R4-M11's merge across bodies by code, subject and Reason is publication's), decide acceptance, derive a requirement
// from a code, or keep a problem a rule of Add drops.

using System.Runtime.InteropServices;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

/// <summary>The problems of one program version (R4 shadow: unpublished until R6, compared by the difference ledger), one per code,
/// subject, body and Reason: a problem added again ORs its views into the first, whose requirement it must name.</summary>
internal sealed class MirReport
{
    private readonly Dictionary<(DiagnosticCode Code, SourceRef Subject, int Body, MirReason Reason), int> index = new(); // The problem of each key.
    private readonly List<MirProblem> problems = [];
    private readonly List<MirFact> facts = [];
    private readonly List<(int Start, int Count)> spans = []; // Each problem's facts.

    /// <summary>Gets the problems in the order they were first added.</summary>
    internal ReadOnlySpan<MirProblem> All => CollectionsMarshal.AsSpan(this.problems);

    /// <summary>Gets the facts of a problem.</summary>
    /// <param name="problem">The problem's index in <see cref="All"/>.</param>
    /// <returns>Its facts.</returns>
    internal ReadOnlySpan<MirFact> Facts(int problem) => CollectionsMarshal.AsSpan(this.facts).Slice(this.spans[problem].Start, this.spans[problem].Count);

    /// <summary>Adds a problem with its facts, or merges its views into the first problem of the same code, subject, body and Reason, which must
    /// name the same requirement: a merge that names another is the recording rules' defect, an InternalInvariant_Kd problem at the
    /// subject. A problem with an Inlined fact is dropped: found inside an inline default copy, it is its template's check (SPEC 7.2.3,
    /// R4-M69).</summary>
    /// <param name="problem">The problem.</param>
    /// <param name="facts">Its facts, kept only for a new problem.</param>
    internal void Add(in MirProblem problem, ReadOnlySpan<MirFact> facts = default)
    {
        foreach (var fact in facts)
        {
            if (fact.Kind == MirFactKind.Inlined)
            {
                return;
            }
        }

        ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(this.index, (problem.Code, problem.Subject, problem.Body, problem.Reason), out var exists);
        if (exists)
        {
            ref var first = ref CollectionsMarshal.AsSpan(this.problems)[slot];
            if (first.Requirement != problem.Requirement)
            {
                var defect = DiagnosticRequirement.Ownership(OwnershipRequirement.Internal);
                this.Add(new(DiagnosticCode.InternalInvariant_Kd, defect, problem.Subject, problem.Body, problem.Views));
                return;
            }

            first = first with { Views = first.Views | problem.Views };
            return;
        }

        slot = this.problems.Count;
        this.problems.Add(problem);
        this.spans.Add((this.facts.Count, facts.Length));
        foreach (var fact in facts)
        {
            this.facts.Add(fact);
        }
    }

    /// <summary>Removes every problem, keeping the storage.</summary>
    internal void Clear()
    {
        this.index.Clear();
        this.problems.Clear();
        this.facts.Clear();
        this.spans.Clear();
    }
}
