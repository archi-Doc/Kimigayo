// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns what a failing check rests on (SPEC 23.1.1, 23.1.2): a cause a proof met, whose Use the check's record is derived from, and,
// when the cause is a location-only limit, the mark by which Binding.Check publishes that limit's one Unsupported_Kd.
// Input: a producer met while a check proved its requirement: a conformance path resting on its specification or limited by OCC-X; a
// component summary's site (a limit, L, which a proof carries until its answer is no longer combined, DECISIONS BU-22); the node a
// component summary's Error rests on (a stored Type that failed, or its failed operand context).
// Output: the Use a failing check's record rests on; Rest, which records that a failing check needed it.
// Invariants: a check clears Binding's one channel before its proof and takes the cause after it (Binding.TakeFailedCause); only that
// keeps a cause a reader did not fail on from being taken by a later check; each pass starts with the channel clear; a limit is
// published once per pass, only when a failing check rested on it; a Use is failed by its owner by the end of Binding.Check's Final
// prologue, where the derived uses resting on it are settled (DECISIONS BU-76): a Type node by the Definition deadline, a limit when the
// prologue publishes it (OCC-X limits, then the summaries' sites), after the erasure that holds a cause ran in body binding
// (FailOwnedConversion's only caller is RecordMismatch).
// Must not publish a record itself or decide a proof.
internal interface ICheckCause
{
    /// <summary>Gets the syntax the failing check's record rests on.</summary>
    Koto Use { get; }

    /// <summary>Records that a failing check rested on this cause.</summary>
    void Rest();
}
