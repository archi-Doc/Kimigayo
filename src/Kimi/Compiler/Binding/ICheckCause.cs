// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns what a failing check rests on (SPEC 23.1.1, 23.1.2): a cause a proof met, whose Use the check's record is derived from, and,
// when the cause is a location-only limit, the mark by which Binding.Check publishes that limit's one Unsupported_Kd.
// Input: a producer met while a check proved its requirement (a conformance path resting on its specification or limited by OCC-X).
// Output: the Use a failing check's record rests on; Rest, which records that a failing check needed the cause.
// Invariants: a check clears Binding's one channel before its proof and takes the cause after it (Binding.TakeFailedCause); a limit is
// published once per pass, only when a failing check rested on it.
// Must not publish a record itself or decide a proof.
internal interface ICheckCause
{
    /// <summary>Gets the syntax the failing check's record rests on.</summary>
    Koto Use { get; }

    /// <summary>Records that a failing check rested on this cause.</summary>
    void Rest();
}
