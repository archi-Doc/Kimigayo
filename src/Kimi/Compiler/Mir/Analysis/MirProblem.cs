// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the record of a problem a MIR analysis finds (A6; DECISIONS R4-M3a, R4-M57, R5-M23, R4-M71) and its Reason, the relation an Origin
// problem names (2a0-D D18, DECISIONS R4-M113); its related sites are MirFact values (MirFact.cs).
// Input: the values a recording rule gives: its code, the requirement it judged, its subject, the body it found it in, the Semantics case
// views that have it and, for an Origin problem, its relation. Output: MirProblem values, which MirReport keeps (the Reason in its key) and
// the difference ledger reads.
// Invariants: a problem's requirement is the one its recording rule names (the Ownership partition's, or ControlFlow for A3, DIAGNOSTICS
// rule 1); its views are bits of its body's MirCases views, and a problem no body describes (A4's obligation rows, at Binding's rows) has
// NoBody, whose views name no body's cases (A4's codes take no case fact); a Reason's ends are the C1-F4 view's canonical atoms (its table's
// entries), so one relation has one Reason, and every other problem has the default Reason.
// Must not derive a requirement from a code, carry message text, or merge or drop problems (MirReport's).

#pragma warning disable SA1402 // The problem and its Reason belong together.

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
/// <param name="Reason">The relation an Origin problem names (D18); the default for any other problem.</param>
internal readonly record struct MirProblem(
    DiagnosticCode Code,
    DiagnosticRequirement Requirement,
    SourceRef Subject,
    int Body,
    ulong Views,
    MirReason Reason = default)
{
    /// <summary>The body of a problem no body describes: A4's obligation rows, which lie at Binding's rows (R4-M71); its views name no
    /// body's cases.</summary>
    internal const int NoBody = -1;
}

/// <summary>The relation an Origin problem names (2a0-D D18): its longer and shorter ends, the C1-F4 view's canonical atoms of the
/// body, and whether it is an equality; two problems at one subject are two relations only when their Reasons differ.</summary>
/// <param name="Longer">The end required to outlive the other.</param>
/// <param name="Shorter">The other end.</param>
/// <param name="Equality">Whether the relation is an equality, failed in both directions.</param>
internal readonly record struct MirReason(BoundOrigin? Longer, BoundOrigin? Shorter, bool Equality);
