// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the problems of one Binding pass (DIAGNOSTICS 4.1; R7-E1): Binding's record beside MirReport, over the same fact vocabulary
// (ProblemFact), one problem per check key (Subject, Requirement, Condition).
// Input: Add calls of Binding's recorder (Binding.Problems.cs), each a problem and its facts; Keep calls, each a Type-valued fact's value;
// Unlink and Restore calls of a retried or abandoned attempt.
// Output: a subject's problems in recording order, a problem by its key, its facts, a fact's kept value.
// Invariants: a linked subject's rows and the key index agree, Unlink and Restore keeping them in step; a report of a linked key equals the
// first, or it is a broken contract (DIAGNOSTICS 4.3): an InternalInvariant_Kd problem at its subject, as MirReport's; an unlinked problem
// stays in its rows, unreachable, so that a saved chain can come back, and Count still counts it; a fact's Value is the kind's
// (ProblemFact.cs), an index in the kept values for a value-holding kind; the storage stays between passes, so a warm pass allocates nothing
// once its sizes are reached.
// Must not decide a failure, derive a requirement from a code, or keep or render text.

#pragma warning disable SA1402 // The problem and its report belong together.

using System.Runtime.InteropServices;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

/// <summary>A side of a mismatch that is no Type (SPEC 23.3): a closed row, whose text Binding's adapter owns, so no check writes one.</summary>
internal enum MismatchForm : byte
{
    AnonymousFunction, // With its parameter count, or none where no Function Type is expected.
    IntegerLiteral,
    FloatingPointLiteral,
    PositionType, // A Type proven Position or PositionRange.
}

/// <summary>A problem Binding found: its check key (DIAGNOSTICS 4.1) and code; its facts are in the report.</summary>
/// <param name="Code">The code.</param>
/// <param name="Requirement">The requirement its check judged.</param>
/// <param name="Subject">The node the check is about.</param>
/// <param name="Condition">The part of the requirement, 0 for the only one.</param>
internal readonly record struct BindingProblem(DiagnosticCode Code, DiagnosticRequirement Requirement, SourceRef Subject, ushort Condition);

/// <summary>A side of a mismatch (SPEC 23.3): a Type, or a closed form that is no Type.</summary>
/// <param name="Type">The Type, or null for a form.</param>
/// <param name="Form">The form's row.</param>
/// <param name="Count">An anonymous function's parameter count, or -1.</param>
internal readonly record struct MismatchSide(BoundType? Type, MismatchForm Form = default, int Count = -1)
{
    public static implicit operator MismatchSide(BoundType type) => new(type);
}

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
    private readonly List<BoundType> types = [];
    private readonly List<BoundConstraint> constraints = [];
    private readonly List<OriginRecord> origins = [];

    /// <summary>Gets the number of problems this pass added, unlinked ones included.</summary>
    internal int Count => this.problems.Count;

    /// <summary>Gets or sets the count of this pass's fact sites that no semantic write had numbered.</summary>
    [Transitional("R7", Reason = "The row of the fact-site hypothesis, until R7-N5 owns SourceRef.Of numbering an id-less node.")]
    internal int IdlessSites { get; set; }

    /// <summary>Gets a problem.</summary>
    /// <param name="problem">The problem's index.</param>
    /// <returns>The problem.</returns>
    internal BindingProblem Problem(int problem) => this.problems[problem];

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

    /// <summary>Keeps a Type-valued fact's Type: a reference only, so a warm pass boxes nothing.</summary>
    /// <param name="type">The Type.</param>
    /// <returns>Its index, the fact's Value.</returns>
    internal long Keep(BoundType type)
    {
        this.types.Add(type);
        return this.types.Count - 1;
    }

    /// <summary>Keeps a Constraint fact's proposition: an interned reference.</summary>
    /// <param name="constraint">The proposition.</param>
    /// <returns>Its index, the fact's Value.</returns>
    internal long Keep(BoundConstraint constraint)
    {
        this.constraints.Add(constraint);
        return this.constraints.Count - 1;
    }

    /// <summary>Keeps an Origin record, whose fields are no facts of their own until E4's relation fact.</summary>
    /// <param name="record">The record.</param>
    /// <returns>Its index, the fact's Value.</returns>
    [Transitional("R7", Reason = "An Origin record's row, until E4's relation fact takes both phases' records.")]
    internal long Keep(in OriginRecord record)
    {
        this.origins.Add(record);
        return this.origins.Count - 1;
    }

    /// <summary>Gets a Type-valued fact's Type.</summary>
    /// <param name="fact">The fact.</param>
    /// <returns>The Type.</returns>
    internal BoundType Value(in ProblemFact fact) => this.types[(int)fact.Value];

    /// <summary>Gets a Constraint fact's proposition.</summary>
    /// <param name="fact">The fact.</param>
    /// <returns>The proposition.</returns>
    internal BoundConstraint Constraint(in ProblemFact fact) => this.constraints[(int)fact.Value];

    /// <summary>Gets an OriginRecord fact's record.</summary>
    /// <param name="fact">The fact.</param>
    /// <returns>The record.</returns>
    internal OriginRecord Origin(in ProblemFact fact) => this.origins[(int)fact.Value];

    /// <summary>Numbers an Origin record of a subject beyond its first: the condition after the subject's, in recording order.</summary>
    /// <param name="subject">The subject.</param>
    /// <param name="record">The record.</param>
    /// <returns>Its condition, or 0 when the subject has the record.</returns>
    internal ushort OriginCondition(SourceRef subject, in OriginRecord record)
    {
        ushort last = 0;
        for (var i = this.First(subject); i >= 0; i = this.Next(i))
        {
            foreach (ref readonly var fact in this.Facts(i))
            {
                if (fact.Kind == ProblemFactKind.OriginRecord && this.Origin(fact) == record)
                {
                    return 0;
                }
            }

            last = Math.Max(last, this.problems[i].Condition);
        }

        return (ushort)(last + 1);
    }

    /// <summary>Adds a problem with its facts. A second report of a linked key must equal the first (DIAGNOSTICS 4.3).</summary>
    /// <param name="problem">The problem.</param>
    /// <param name="facts">Its facts.</param>
    /// <returns>The problem's index, the first one's when the key is linked; -1 for a defect.</returns>
    internal int Add(in BindingProblem problem, ReadOnlySpan<ProblemFact> facts = default)
    {
        ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(this.index, (problem.Subject, problem.Requirement, problem.Condition), out var exists);
        if (exists)
        {
            return this.Equal(slot, problem.Code, facts) ? slot : this.Defect(problem);
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
        this.types.Clear();
        this.constraints.Clear();
        this.origins.Clear();
        this.IdlessSites = 0;
    }

    private bool Equal(int first, DiagnosticCode code, ReadOnlySpan<ProblemFact> reported)
    {
        var kept = this.Facts(first);
        if (this.problems[first].Code != code || kept.Length != reported.Length)
        {
            return false;
        }

        for (var i = 0; i < kept.Length; i++)
        {
            if (kept[i].Kind != reported[i].Kind || kept[i].Site != reported[i].Site || !this.EqualValue(kept[i], reported[i]))
            {
                return false;
            }
        }

        return true;
    }

    private bool EqualValue(in ProblemFact kept, in ProblemFact reported) => kept.Kind switch
    {
        ProblemFactKind.Actual or ProblemFactKind.Expected or ProblemFactKind.Operand or ProblemFactKind.Terminal
            => Equals(this.Value(kept), this.Value(reported)),
        ProblemFactKind.Constraint => ReferenceEquals(this.Constraint(kept), this.Constraint(reported)),
        _ => kept.Value == reported.Value,
    };

    // A recording rule's defect in place of a report (DIAGNOSTICS 4.3, as MirReport.Add's): one InternalInvariant_Kd problem at its subject;
    // -1, Add's value for it.
    private int Defect(in BindingProblem problem)
    {
        this.Add(new(DiagnosticCode.InternalInvariant_Kd, DiagnosticRequirement.Binding(BindingFailure.Internal), problem.Subject, 0));
        return -1;
    }
}
