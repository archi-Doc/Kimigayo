// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

// MirReport's limit table (IMPL 21.3.2, DIAGNOSTICS rule 6; DECISIONS R5-M171, R7-M54; PLAN G651 U2b R6): the least use of each use over
// a limit per declaration, flushed as one Collection-class record per key.
// - Responsibility: each key's least use, a round's least held with its round, and the flush of every key's record at its least use.
// - Inputs: the instance collector's uses (Limit), each a key (its record's code; an erased Type, or none where a forming site's
//   substitution passes IMPL A.4's height; the forming site; the declaration) and its subject, made in its run's round (Begin with
//   MirRecordClass.Collection); the run's end (FlushLimits, before Commit) and the first Merged read after a committed least changed.
// - Outputs: per key one ControlFlow record of MirRecordClass.Collection at its least subject (SourceRef order: module, then the syntax
//   table's pre-order), TypeHeightLimit_Kd with its limit fact (IMPL A.4), added through Add in subject order, then code.
// - Invariants: a round's uses are its own (Drop discards them, FlushLimits merges them into the committed least); a flush writes each
//   key's one row again through Add's key, so a repeated clean run of a version, its committed least unchanged, adds no problem (ruling 16).
// - Prohibitions: no flush on a read of All or Records (A5's rule W and MirAnalysis read them while producers still run); no use outside
//   the collector's round.
internal sealed partial class MirReport
{
    // The committed least use of each key and the open round's (Limit), each subject the least so far.
    private readonly Dictionary<(DiagnosticCode Code, BoundType? Type, SourceRef Site, MirDefKey Declaration), (bool Fallback, SourceRef Subject)> limits = new();
    private readonly Dictionary<(DiagnosticCode Code, BoundType? Type, SourceRef Site, MirDefKey Declaration), (bool Fallback, SourceRef Subject)> roundLimits = new();
    private readonly List<(SourceRef Subject, DiagnosticCode Code)> flushing = []; // FlushLimits' scratch.
    private bool unflushed; // Whether a committed least changed since the last flush (Merged flushes it).
    private bool flushed; // Whether the table flushed in this version: a body site's use after it is a broken contract.

    /// <summary>Adds the instance collector's use over a limit in a declaration to its run's round (IMPL 21.3.2; DECISIONS R5-M171): keyed
    /// by the limit's record and an erased Type (a Layout limit, its code the collector's; the type-driven height cap) or, where no Type
    /// is formed, a forming site whose substitution passes IMPL A.4's height (TypeHeightLimit_Kd, erased null; R7-T1), kept as the least
    /// subject of the declaration's uses in SourceRef order, whatever the collection's order. A use outside the collector's round is a
    /// broken contract.</summary>
    /// <param name="code">The record's code.</param>
    /// <param name="erased">The erased Type over the limit, or null for a forming site's.</param>
    /// <param name="forming">The forming site, for a null Type; default otherwise.</param>
    /// <param name="declaration">The declaration holding the use.</param>
    /// <param name="subject">The use's subject.</param>
    internal void Limit(DiagnosticCode code, BoundType? erased, SourceRef forming, MirDefKey declaration, SourceRef subject)
    {
        if (this.round != (int)MirRecordClass.Collection)
        {
            throw new InvalidOperationException("A limit use outside the collector's round (InternalInvariant).");
        }

        _ = Least(this.roundLimits, (code, erased, forming, declaration), (false, subject));
    }

    /// <summary>Adds the uses a MIR body site's marks make (PLAN G651 U2b R2, R6; Binding.CloseBodySite) at once, in every round, a lazily
    /// built body's included: each a TypeHeightLimit_Kd use in the body's declaration, a refused instance's keyed by its erased Type (as
    /// Layout's limit), a refused substitution's by its subject as its forming site (as the collector's Formed), a body site's own subject a
    /// fallback after every use subject. A use after the table flushed outside a Collection round, or from a library body, is a broken
    /// contract.</summary>
    /// <param name="types">The compilation's TyCtxt, which erases a refused instance.</param>
    /// <param name="marks">The body site's marks.</param>
    /// <param name="declaration">The body's declaration.</param>
    internal void Uses(TyCtxt types, ReadOnlySpan<SiteMark> marks, MirDefKey declaration)
    {
        foreach (var mark in marks)
        {
            if (mark.Subject.IsLibrary || (this.flushed && this.round != (int)MirRecordClass.Collection))
            {
                throw new InvalidOperationException($"A height use at {mark.Subject} from a library body or after the limit table flushed (InternalInvariant).");
            }

            var key = mark.Refused is { } refused ? (DiagnosticCode.TypeHeightLimit_Kd, types.Erase(refused), default(SourceRef), declaration)
                : (DiagnosticCode.TypeHeightLimit_Kd, (BoundType?)null, mark.Subject, declaration);
            this.unflushed |= Least(this.limits, key, (mark.Fallback, mark.Subject));
        }
    }

    /// <summary>Flushes the table at the clean collector run's end, before its round's <see cref="Commit"/>: merges the round's least uses
    /// into the committed ones, then adds every key's record at its least use in subject order (DIAGNOSTICS rule 6) to the round.</summary>
    internal void FlushLimits()
    {
        foreach (var (key, subject) in this.roundLimits)
        {
            this.unflushed |= Least(this.limits, key, subject);
        }

        this.roundLimits.Clear();
        this.Flush();
    }

    // The order of two uses (PLAN G651 U2b R6): a body site's fallback subject after every use subject, then SourceRef.Order.
    private static int Order((bool Fallback, SourceRef Subject) x, (bool Fallback, SourceRef Subject) y)
        => x.Fallback != y.Fallback ? (x.Fallback ? 1 : -1) : SourceRef.Order(x.Subject, y.Subject);

    // Keeps a use as its key's least; whether it changed the table.
    private static bool Least(
        Dictionary<(DiagnosticCode Code, BoundType? Type, SourceRef Site, MirDefKey Declaration), (bool Fallback, SourceRef Subject)> table,
        (DiagnosticCode Code, BoundType? Type, SourceRef Site, MirDefKey Declaration) key,
        (bool Fallback, SourceRef Subject) use)
    {
        ref var least = ref CollectionsMarshal.GetValueRefOrAddDefault(table, key, out var found);
        if (found && Order(use, least) >= 0)
        {
            return false;
        }

        least = use;
        return true;
    }

    // IMPL 21.3.2 (DECISIONS R5-M171): the record of each key at its least use, in subject order, then code: TypeHeightLimit_Kd with its
    // limit (IMPL A.4; R7-T1), each through Add, so a key's row written again merges into itself.
    private void Flush()
    {
        (this.unflushed, this.flushed) = (false, true);
        if (this.limits.Count == 0)
        {
            return;
        }

        this.flushing.Clear();
        foreach (var (key, least) in this.limits)
        {
            this.flushing.Add((least.Subject, key.Code));
        }

        this.flushing.Sort(static (x, y) => SourceRef.Order(x.Subject, y.Subject) is var order && order != 0 ? order : x.Code.CompareTo(y.Code));
        foreach (var (subject, code) in this.flushing)
        {
            var record = new MirProblem(code, DiagnosticRequirement.ControlFlow, subject, (int)MirRecordClass.Collection, MirProblem.NoBodyViews);
            _ = code == DiagnosticCode.TypeHeightLimit_Kd ? this.Add(record, [new(ProblemFactKind.Limit, subject, Binding.TypeHeightLimit)]) : this.Add(record);
        }
    }
}
