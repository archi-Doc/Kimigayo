// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the problems the MIR analyses find in one program version and its construction and collection records (A6; DECISIONS R4-M3a, R5-M23,
// R4-M71; R6-check H3-1), and the least use of each use over a limit per declaration, the collection records of R5-M171's limit table;
// their records are MirProblem and ProblemFact (MirProblem.cs, Core/ProblemFact.cs).
// Input: Add calls, each a problem with its requirement, which its recording rule names (R5-M23), its body (a record's MirRecordClass,
// through MirProgram.Report) and its facts (related sites, added by their first producer); a round's problems, held until it commits
// (Rounds below); and a library segment's rows, which their owner keeps as data (Keep, Kept) and replays at the step where it first added
// them (Replay; PLAN G950), Cause values moved by the offset; the limit table's uses (MirReport.Limits.cs; DECISIONS R5-M171, PLAN G651
// U2b R6), each a key (its record's code, an erased Type or none, a forming site, a MirDefKey declaration) with its subject and fallback
// bit and its producer's round: the instance collector's held with its run's round and dropped with a missed run; a MIR body site's (Uses)
// added at once in every round.
// Output: the limit table's records, one Collection-class ControlFlow record per key at its least use (SourceRef order), flushed at
// each clean collector run's end (the round's least merged into the committed least, the flush, then Commit; a repeated clean run of a
// version writes each key's row again) and, with no collector run, on the first read of Merged after a committed least changed; one
// problem per code, subject, body and Reason (R4-M71: views number one body's cases; 2a0-D
// D18: an Origin problem's relation, the default for other problems) with the views that have it merged, in insertion order (bodies in
// program order), and each problem's facts by its index, which Add returns (R4-M132). A merge keeps the first problem, its requirement and
// facts included, and ORs the views; merged problems name one requirement, and a disagreement is a recording rule's defect, reported as
// one InternalInvariant_Kd problem at the subject, as is a Cause fact whose Value is neither -1 nor an index in All (H3-2 N2), which no
// producer writes (rule W's causes are indices Add returned). Every analysis InternalInvariant_Kd names Ownership.Internal (these defects
// and A5's broken contracts). Add is the one home of the derived-problem rules: a problem with Cause facts (its tail) is derived (rule
// W, SPEC 15.6.5, R4-M132), and a merged problem is derived exactly when every report it groups is, its causes their union (a direct report drops the first's Cause tail; a derived one adds its new causes). Merged, the view
// publication reads (MirReport.Merged.cs; R4-M11, R6-check H3-2), has one merged problem per code, subject, Reason and class, in the order
// of its first member, with its members (indices in All), its facts (its first member's but their Cause tail, then by the rule above
// over its members, each Cause's Value an index in Merged, once each, never its own) and its Ordinal, its Reason's position among the merged problems of
// its code, subject and class; the class keeps each record class's rows apart from the other's and from the analyses' problems, which
// merge across bodies and NoBody: A4 (NoBody) and A5 (a body) both record Unsupported_Kd and OwnershipDepthLimit_Kd under one requirement
// each (Ownership.Unsupported, Ownership.DepthLimit), so at one subject and Reason they merge and never disagree, and A4's obligation
// codes are never A5's. Members naming two requirements add the InternalInvariant_Kd merged problem at the subject, which, naming
// Ownership.Internal, never disagrees again. Merged is computed on its first read after an Add or a Clear. Storage is pooled, so a warm
// version allocates nothing.
// Rounds (PLAN G951's round rule; the one commit of A4's rounds and the instance collector's runs): one round at a time, which Begin opens
// for a body value (A4's obligations' NoBody, MirRecordClass.Collection) and Commit or Drop ends; any other order is a broken contract and
// throws. While it is open, Add holds each problem of that body value with its facts (a row kept as data, Kept; R7-T1: the collector's
// height record carries its limit), which no producer outside the round adds meanwhile; every other problem, such as a construction
// record of a body a lookup builds during the round, is added at once. Commit adds the held problems in order as Add adds them; Drop
// discards them, so a round that missed commits nothing; Clear closes a round. A kept row (Keep, Replay) was committed when it was kept
// and is replayed outside any round, so a round holds only its own version's problems.
// Must not publish, decide acceptance, derive a requirement from a code, or keep a problem a rule of Add drops; flush the limit table
// on a read of All or Records (MirAnalysis and A5's rule W read them while producers still run), take the collector's limit use outside
// its round, or take a body site's use after the table flushed outside a Collection round or from a library body.

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
    private readonly Kept held = new(); // The open round's problems with their facts (Begin).
    private int? round; // The body value of the open round's problems (Begin), or none.

    /// <summary>Gets the problems in the order they were first added.</summary>
    internal ReadOnlySpan<MirProblem> All => CollectionsMarshal.AsSpan(this.problems);

    /// <summary>Gets the facts of a problem.</summary>
    /// <param name="problem">The problem's index in <see cref="All"/>.</param>
    /// <returns>Its facts.</returns>
    internal ReadOnlySpan<ProblemFact> Facts(int problem) => CollectionsMarshal.AsSpan(this.facts).Slice(this.spans[problem].Start, this.spans[problem].Count);

    /// <summary>Adds a problem with its facts, or merges its views into the first problem of the same code, subject, body and Reason, which must
    /// name the same requirement: a merge that names another is the recording rules' defect, an InternalInvariant_Kd problem at the
    /// subject, as is a Cause fact naming no problem (neither -1 nor an index in <see cref="All"/>). A merged problem is derived exactly
    /// when every report of it is, its Cause facts their union (R4-M132).</summary>
    /// <param name="problem">The problem.</param>
    /// <param name="facts">Its facts, kept for a new problem, of which only the Cause facts merge.</param>
    /// <returns>The problem's index in <see cref="All"/>, the first problem's on a merge; -1 for a defect and a problem an
    /// open round holds (<see cref="Begin"/>).</returns>
    internal int Add(in MirProblem problem, ReadOnlySpan<ProblemFact> facts = default)
    {
        if (problem.Body == this.round)
        {
            this.held.Add(problem, facts);
            return -1;
        }

        var namesNoProblem = false;
        foreach (var fact in facts)
        {
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

    /// <summary>Removes every problem and least use, keeping the storage.</summary>
    internal void Clear()
    {
        (this.isMerged, this.round, this.unflushed, this.flushed) = (false, null, false, false);
        this.held.Clear(0);
        this.limits.Clear();
        this.roundLimits.Clear();
        this.index.Clear();
        this.problems.Clear();
        this.facts.Clear();
        this.spans.Clear();
    }

    /// <summary>Opens a round of a consumer of step-2 facts (Rounds in the contract): until <see cref="Commit"/> or <see cref="Drop"/>, Add
    /// holds each problem of the round's body value; any other problem is added at once. A round opened while one is open is a broken
    /// contract.</summary>
    /// <param name="body">The body value of the round's problems: A4's obligations' NoBody, the collector's MirRecordClass.Collection.</param>
    internal void Begin(int body)
        => this.round = this.round is { } open ? throw new InvalidOperationException($"A round of {body} began in the open round of {open} (InternalInvariant).") : body;

    /// <summary>Ends a clean round: adds the problems it held, in order, each with its facts, as Add adds them.</summary>
    internal void Commit()
    {
        this.End();
        for (var row = 0; row < this.held.Count; row++)
        {
            this.Add(this.held[row], this.held.Facts(row));
        }

        this.held.Clear(0);
    }

    /// <summary>Ends a round that missed: drops the problems it held and its least uses.</summary>
    internal void Drop()
    {
        this.End();
        this.held.Clear(0);
        this.roundLimits.Clear();
    }

    /// <summary>Keeps the problems added from an index on as data, each with its facts as Add left them: a library segment's rows
    /// (PLAN G950), which <see cref="Replay"/> adds to a later version's report at the same step.</summary>
    /// <param name="from">The index in <see cref="All"/> of the first problem kept.</param>
    /// <param name="into">The kept rows, which an earlier keeping held.</param>
    internal void Keep(int from, Kept into)
    {
        into.Clear(from);
        for (var problem = from; problem < this.problems.Count; problem++)
        {
            into.Add(this.problems[problem], this.Facts(problem));
        }
    }

    /// <summary>Adds kept rows after the problems added so far, each as its keeping found it, so the report equals the one their first adding
    /// made at this step; a Cause fact naming a kept row moves by the rows' offset (a kept row's causes name kept rows only; -1 stays).</summary>
    /// <param name="rows">The kept rows, none of whose keys the report holds.</param>
    internal void Replay(Kept rows)
    {
        this.isMerged = false;
        var offset = this.problems.Count - rows.From;
        for (var row = 0; row < rows.Count; row++)
        {
            var problem = rows[row];
            this.index.Add((problem.Code, problem.Subject, problem.Body, problem.Reason), this.problems.Count);
            this.problems.Add(problem);
            this.spans.Add((this.facts.Count, rows.Facts(row).Length));
            foreach (var fact in rows.Facts(row))
            {
                this.facts.Add(fact.Kind == ProblemFactKind.Cause && fact.Value >= rows.From ? fact with { Value = fact.Value + offset } : fact);
            }
        }
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

    // Closes the open round: a Commit or Drop without one is a broken contract (Rounds).
    private void End()
    {
        if (this.round is null)
        {
            throw new InvalidOperationException("A round ended with none open (InternalInvariant).");
        }

        this.round = null;
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

    /// <summary>Rows of a report kept as data, read in two ways: a library segment's rows (<see cref="Keep"/>), the problems with their facts
    /// and the index the first had in its report, from which <see cref="Replay"/> moves their Cause facts; and the open round's held problems
    /// (<see cref="Begin"/>; R7-T1), kept from 0, which Commit adds through Add and which are never replayed. It references no analysis, so
    /// its owner can hold it across versions.</summary>
    internal sealed class Kept
    {
        private readonly List<MirProblem> problems = [];
        private readonly List<ProblemFact> facts = [];
        private readonly List<(int Start, int Count)> spans = []; // Each row's facts.

        /// <summary>Gets the index in its report of the first row kept.</summary>
        internal int From { get; private set; }

        /// <summary>Gets the number of rows.</summary>
        internal int Count => this.problems.Count;

        /// <summary>Gets a row's problem.</summary>
        /// <param name="row">The row.</param>
        internal MirProblem this[int row] => this.problems[row];

        /// <summary>Gets a row's facts.</summary>
        /// <param name="row">The row.</param>
        /// <returns>Its facts.</returns>
        internal ReadOnlySpan<ProblemFact> Facts(int row) => CollectionsMarshal.AsSpan(this.facts).Slice(this.spans[row].Start, this.spans[row].Count);

        /// <summary>Removes every row, keeping the storage, for rows kept from an index.</summary>
        /// <param name="from">The index in its report of the first row to be kept.</param>
        internal void Clear(int from)
        {
            this.From = from;
            this.problems.Clear();
            this.facts.Clear();
            this.spans.Clear();
        }

        /// <summary>Adds a row.</summary>
        /// <param name="problem">The problem.</param>
        /// <param name="facts">Its facts.</param>
        internal void Add(in MirProblem problem, ReadOnlySpan<ProblemFact> facts)
        {
            this.problems.Add(problem);
            this.spans.Add((this.facts.Count, facts.Length));
            foreach (var fact in facts)
            {
                this.facts.Add(fact);
            }
        }
    }
}
