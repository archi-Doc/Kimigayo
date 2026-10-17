// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the problems the MIR analyses find in one program version (A6; DECISIONS R4-M3a).
// Input: Add calls, each a problem with its facts (related sites, added by their first producer). Output: one problem per code and subject
// (the canonical Problem key; Reason joins it with U11's facts, R4-M3a) with the views that have it merged, in insertion order (bodies in
// program order), and each problem's facts by its index. A merge keeps the first problem, its facts included, and ORs the views. Add is the
// one home of the rules that drop derived problems (R4-M10); storage is pooled, so a warm version allocates nothing.
// Must not publish, decide acceptance, or keep a problem a rule of Add drops.

#pragma warning disable SA1402 // The problem and fact records belong to the report.

using System.Runtime.InteropServices;

namespace Kimi.Compiler.Mir;

/// <summary>A problem a MIR analysis finds (R4-M3a): its code, its subject and the Semantics case views (<see cref="MirCases"/>) that
/// have it, one bit per view. Related sites are facts, which their first producer adds; R6 derives the published requirement from the
/// code and the facts.</summary>
/// <param name="Code">The code.</param>
/// <param name="Subject">The syntax the problem is about.</param>
/// <param name="Views">The views that have the problem, bit i for view i (every bit: a problem no view decides).</param>
internal readonly record struct MirProblem(DiagnosticCode Code, SourceRef Subject, ulong Views);

/// <summary>What a related site of a problem is (R4-M3a): a Loan's anchor, its witness holder's next use, or the store whose value
/// carries it (design A3 6.4).</summary>
internal enum MirFactKind : byte
{
    Anchor,
    Use,
    Store,
}

/// <summary>A related site of a problem (R4-M3a).</summary>
/// <param name="Kind">What the site is.</param>
/// <param name="Site">The syntax.</param>
internal readonly record struct MirFact(MirFactKind Kind, SourceRef Site);

/// <summary>The problems of one program version (R4 shadow: unpublished until R6, compared by the difference ledger), one per code and
/// subject: a problem added again ORs its views into the first.</summary>
internal sealed class MirReport
{
    private readonly Dictionary<Problem, int> index = new(); // The problem of each code and subject.
    private readonly List<MirProblem> problems = [];
    private readonly List<MirFact> facts = [];
    private readonly List<(int Start, int Count)> spans = []; // Each problem's facts.

    /// <summary>Gets the problems in the order they were first added.</summary>
    internal ReadOnlySpan<MirProblem> All => CollectionsMarshal.AsSpan(this.problems);

    /// <summary>Gets the facts of a problem.</summary>
    /// <param name="problem">The problem's index in <see cref="All"/>.</param>
    /// <returns>Its facts.</returns>
    internal ReadOnlySpan<MirFact> Facts(int problem) => CollectionsMarshal.AsSpan(this.facts).Slice(this.spans[problem].Start, this.spans[problem].Count);

    /// <summary>Adds a problem with its facts, or merges its views into the first problem of the same code and subject.</summary>
    /// <param name="problem">The problem.</param>
    /// <param name="facts">Its facts, kept only for a new problem.</param>
    internal void Add(in MirProblem problem, ReadOnlySpan<MirFact> facts = default)
    {
        ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(this.index, new(problem.Code, problem.Subject), out var exists);
        if (exists)
        {
            ref var first = ref CollectionsMarshal.AsSpan(this.problems)[slot];
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
