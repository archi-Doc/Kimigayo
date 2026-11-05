// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns what a failing check rests on (SPEC 23.1.1, 23.1.2): a cause a proof met, whose Use the check's record is derived from, and,
// when the cause is a location-only limit, the mark by which Binding.Check publishes that limit's one Unsupported_Kd.
// Input: a producer met while a check proved its requirement: a conformance path resting on its specification or limited by OCC-X; a
// component summary's site (a limit, L, which a proof carries until its answer is no longer combined, DECISIONS BU-22); the node a
// component summary's Error rests on (a stored Type that failed, its failed operand context, or the hops of a conformance resolution that
// re-entered itself; an identity step is never a re-entry, DECISIONS BU-22).
// Output: the Use a failing check's record rests on, the first of the cause's candidates (Next: a resolution's inner hops, none for a
// single node); a re-entry's site, which a use takes when none of the candidates failed (ReentrySite); Rest, which records that a
// failing check needed it.
// Invariants: a check clears Binding's one channel before its proof and takes the cause after it (Binding.TakeFailedCause); only that
// keeps a cause a reader did not fail on from being taken by a later check; each pass starts with the channel clear; a limit is
// published once per pass, only when a failing check rested on it; a Use is failed by its owner by the end of Binding.Check's Final
// prologue, where the derived uses resting on it are settled (DECISIONS BU-76): a Type node by the Definition deadline, a limit when the
// prologue fails it (OCC-X limits, then the summaries' sites; the loop publishes each once), after the erasure that holds a cause ran
// in body binding (FailOwnedConversion's only caller is RecordMismatch).
// Must not publish a record itself or decide a proof.
internal interface ICheckCause
{
    /// <summary>Gets the syntax the failing check's record rests on: the cause's first candidate.</summary>
    Koto Use { get; }

    /// <summary>Gets the next candidate (DECISIONS BU-76): a resolution's inner hop; null for a cause of one node.</summary>
    ICheckCause? Next { get; }

    /// <summary>Gets the site of the stored Type a re-entry was read at (DECISIONS BU-22): the located Unsupported_Kd a use resting on
    /// the re-entry takes when none of its candidates failed; null for any other cause.</summary>
    ICheckCause? ReentrySite { get; }

    /// <summary>Records that a failing check rested on this cause.</summary>
    void Rest();
}
