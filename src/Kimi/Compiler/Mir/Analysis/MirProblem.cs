// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the record of a problem a MIR analysis finds and of its related sites (A6; DECISIONS R4-M3a, R4-M57, R5-M23, R4-M71).
// Input: the values a recording rule gives: its code, the requirement it judged, its subject, the body it found it in, the Semantics case
// views that have it, and each related site with what it is. Output: MirProblem and MirFact values, which MirReport keeps and the
// difference ledger reads.
// Invariants: a problem's requirement is the one its recording rule names (the Ownership partition's, or ControlFlow for A3, DIAGNOSTICS
// rule 1); its views are bits of its body's MirCases views, and a problem no body describes (A4's obligation rows, at Binding's rows) has
// NoBody, whose views name no body's cases (A4's codes take no case fact); a fact is a site the problem relates, never a message: an
// Inlined fact names the inline default copy whose template a Language problem belongs to, which a limit record never carries (R4-M69).
// Must not derive a requirement from a code, carry message text, or merge or drop problems (MirReport's).

#pragma warning disable SA1402 // The problem and fact records belong together.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

/// <summary>A problem a MIR analysis finds (R4-M3a, R5-M23, R4-M71): its code, the requirement the recording rule judged (the Ownership
/// partition's, or ControlFlow for A3, DIAGNOSTICS rule 1; never looked up from the code), its subject, the body whose Semantics case views
/// (<see cref="MirCases"/>) number its views, and those views, one bit per view. Related sites are facts, which their first producer
/// adds.</summary>
/// <param name="Code">The code.</param>
/// <param name="Requirement">The requirement its recording rule names.</param>
/// <param name="Subject">The syntax the problem is about.</param>
/// <param name="Body">The ordinal of the body whose views <paramref name="Views"/> numbers, or <see cref="NoBody"/>.</param>
/// <param name="Views">The views that have the problem, bit i for view i (every bit: a problem no view decides).</param>
internal readonly record struct MirProblem(DiagnosticCode Code, DiagnosticRequirement Requirement, SourceRef Subject, int Body, ulong Views)
{
    /// <summary>The body of a problem no body describes: A4's obligation rows, which lie at Binding's rows (R4-M71); its views name no
    /// body's cases.</summary>
    internal const int NoBody = -1;
}

/// <summary>What a related site of a problem is (R4-M3a): a Loan's anchor, its witness holder's next use, the store whose value
/// carries it, the anchor of the reservation the problem's activation or destruction is of (design A3 6.4), or the call of the inline
/// default copy a Language problem was found in (R4-M69).</summary>
internal enum MirFactKind : byte
{
    Anchor,
    Use,
    Store,
    Reservation,
    Inlined, // The call of the Inlined scope the problem lies in: its template's check (SPEC 7.2.3), which MirReport drops.
}

/// <summary>A related site of a problem (R4-M3a).</summary>
/// <param name="Kind">What the site is.</param>
/// <param name="Site">The syntax.</param>
internal readonly record struct MirFact(MirFactKind Kind, SourceRef Site);
