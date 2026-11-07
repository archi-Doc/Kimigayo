// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the problems of one Binding pass (DIAGNOSTICS 4.1; R7-E1): Binding's record beside MirReport, over the same fact vocabulary
// (ProblemFact), one problem per check key (Subject, Requirement, Condition).
// Input: Add calls of Binding's recorder (Binding.Problems.cs), each a problem and its facts; Keep calls, each a Type-valued fact's value;
// Unlink and Restore calls of a retried or abandoned attempt.
// Output: a subject's problems in recording order, a problem by its key, its facts, a fact's kept value.
// Invariants: a linked subject's rows and the key index agree, Unlink and Restore keeping them in step; a report of a linked key keeps the
// first; an unlinked problem stays in its rows, unreachable, so that a saved chain can come back, and Count still counts it; a fact's Value
// is the kind's (ProblemFact.cs), an index in the kept values for a Type-valued kind; the storage stays between passes, so a warm pass
// allocates nothing once its sizes are reached.
// Must not decide a failure, derive a requirement from a code, or render text.

#pragma warning disable SA1402 // The problem and its report belong together.

using System.Runtime.InteropServices;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

/// <summary>A problem Binding found: its check key (DIAGNOSTICS 4.1) and code; its facts are in the report.</summary>
/// <param name="Code">The code.</param>
/// <param name="Requirement">The requirement its check judged.</param>
/// <param name="Subject">The node the check is about.</param>
/// <param name="Condition">The part of the requirement, 0 for the only one.</param>
internal readonly record struct BindingProblem(DiagnosticCode Code, DiagnosticRequirement Requirement, SourceRef Subject, ushort Condition);

/// <summary>The problems of one Binding pass, per check key and, in recording order, per subject.</summary>
internal sealed class BindingReport
{
    /// <summary>The chain of a subject without problems.</summary>
    internal static readonly (int First, int Last) NoChain = (-1, -1);

    private readonly Dictionary<(SourceRef Subject, DiagnosticRequirement Requirement, ushort Condition), int> index = new(); // The linked problem of each key.
    private readonly Dictionary<SourceRef, (int First, int Last)> chains = new(); // Each subject's linked problems.
    private readonly List<BindingProblem> problems = [];
    private readonly List<(int Start, int Count, int Next)> rows = []; // Each problem's facts and the next problem of its subject.
    private readonly List<ProblemFact> facts = [];
    private readonly List<object> values = [];

    /// <summary>Gets the number of problems this pass added, unlinked ones included.</summary>
    internal int Count => this.problems.Count;

    /// <summary>Gets or sets the count of this pass's fact sites that no semantic write had numbered.</summary>
    internal int IdlessSites { get; set; }

    /// <summary>Gets a problem.</summary>
    /// <param name="problem">The problem's index.</param>
    internal ref readonly BindingProblem this[int problem] => ref CollectionsMarshal.AsSpan(this.problems)[problem];

    /// <summary>Gets the facts of a problem.</summary>
    /// <param name="problem">The problem's index.</param>
    /// <returns>Its facts.</returns>
    internal ReadOnlySpan<ProblemFact> Facts(int problem) => CollectionsMarshal.AsSpan(this.facts).Slice(this.rows[problem].Start, this.rows[problem].Count);

    /// <summary>Gets the first linked problem of a subject.</summary>
    /// <param name="subject">The subject.</param>
    /// <returns>Its index, or -1.</returns>
    internal int First(SourceRef subject) => this.chains.TryGetValue(subject, out var chain) ? chain.First : -1;

    /// <summary>Gets the next problem of the same subject.</summary>
    /// <param name="problem">The problem's index.</param>
    /// <returns>The next index, or -1.</returns>
    internal int Next(int problem) => this.rows[problem].Next;

    /// <summary>Finds a linked problem by its check key.</summary>
    /// <param name="subject">The subject.</param>
    /// <param name="requirement">The requirement.</param>
    /// <param name="condition">The condition.</param>
    /// <returns>Its index, or -1.</returns>
    internal int Find(SourceRef subject, DiagnosticRequirement requirement, ushort condition)
        => this.index.TryGetValue((subject, requirement, condition), out var found) ? found : -1;

    /// <summary>Keeps a Type-valued fact's value: a reference only, so a warm pass boxes nothing.</summary>
    /// <param name="value">A BoundType, a BoundLength or the text of what a mismatch found.</param>
    /// <returns>Its index, the fact's Value.</returns>
    internal long Keep(object value)
    {
        this.values.Add(value);
        return this.values.Count - 1;
    }

    /// <summary>Gets a Type-valued fact's value.</summary>
    /// <param name="fact">The fact.</param>
    /// <returns>The value.</returns>
    internal object Value(in ProblemFact fact) => this.values[(int)fact.Value];

    /// <summary>Adds a problem with its facts, or finds the linked problem of its key, which keeps its own facts.</summary>
    /// <param name="problem">The problem.</param>
    /// <param name="facts">Its facts.</param>
    /// <returns>The problem's index, the first one's when the key is linked.</returns>
    internal int Add(in BindingProblem problem, ReadOnlySpan<ProblemFact> facts = default)
    {
        ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(this.index, (problem.Subject, problem.Requirement, problem.Condition), out var exists);
        if (exists)
        {
            return slot;
        }

        var added = slot = this.problems.Count;
        this.problems.Add(problem);
        this.rows.Add((this.facts.Count, facts.Length, -1));
        foreach (var fact in facts)
        {
            this.facts.Add(fact);
        }

        ref var chain = ref CollectionsMarshal.GetValueRefOrAddDefault(this.chains, problem.Subject, out var linked);
        if (linked)
        {
            this.rows[chain.Last] = this.rows[chain.Last] with { Next = added };
            chain.Last = added;
        }
        else
        {
            chain = (added, added);
        }

        return added;
    }

    /// <summary>Unlinks a subject's problems: the attempt that found them is retried.</summary>
    /// <param name="subject">The subject.</param>
    /// <returns>Its chain, which <see cref="Restore"/> links back, or <see cref="NoChain"/>.</returns>
    internal (int First, int Last) Unlink(SourceRef subject)
    {
        if (!this.chains.Remove(subject, out var chain))
        {
            return NoChain;
        }

        for (var i = chain.First; i >= 0; i = this.rows[i].Next)
        {
            this.index.Remove((subject, this.problems[i].Requirement, this.problems[i].Condition));
        }

        return chain;
    }

    /// <summary>Gives a subject the chain it had: the retried attempt is abandoned, with what it recorded.</summary>
    /// <param name="subject">The subject.</param>
    /// <param name="chain">The chain <see cref="Unlink"/> returned.</param>
    internal void Restore(SourceRef subject, (int First, int Last) chain)
    {
        this.Unlink(subject);
        if (chain.First < 0)
        {
            return;
        }

        this.chains[subject] = chain;
        for (var i = chain.First; i >= 0; i = this.rows[i].Next)
        {
            this.index[(subject, this.problems[i].Requirement, this.problems[i].Condition)] = i;
        }
    }

    /// <summary>Removes every problem, keeping the storage.</summary>
    internal void Clear()
    {
        this.index.Clear();
        this.chains.Clear();
        this.problems.Clear();
        this.rows.Clear();
        this.facts.Clear();
        this.values.Clear();
        this.IdlessSites = 0;
    }
}
