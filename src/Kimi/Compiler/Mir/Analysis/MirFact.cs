// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the vocabulary of a MIR problem's related sites (A6; DECISIONS R4-M3a, R4-M57, R4-M69, R4-M92, R4-M97): what a related site is and
// the record of one.
// Input: the kind and site a recording rule gives. Output: MirFact values, which MirReport keeps per problem and the difference ledger reads.
// Invariants: a fact is a site the problem relates, never a message: an Inlined fact names the inline default copy whose template an A1 or
// A2 Language problem belongs to, which a limit record never carries (R4-M69, until 2g-6), an Evaluated fact the default's operation of a
// Loan problem a body owns inside a copy, recorded at the omitting call (R4-M97), and an effect fact the static Field an effect met, its
// kind what decided it (R4-M92); a problem has at most one fact of each kind but its witness's.
// Must not carry message text, or decide which problem a fact belongs to or whether it is kept (MirReport's).

#pragma warning disable SA1402 // The fact vocabulary and its record belong together.

namespace Kimi.Compiler.Mir;

/// <summary>What a related site of a problem is (R4-M3a): a Loan's anchor, its witness holder's next use, the store whose value
/// carries it, the anchor of the reservation the problem's activation or destruction is of (design A3 6.4), the call of the inline
/// default copy an A1 or A2 Language problem was found in (R4-M69), the default's operation of a Loan problem a body owns inside a copy
/// (R4-M97), or the declaration of the static Field a Call's or Drop's effects met, by what decided it (R4-M92).</summary>
internal enum MirFactKind : byte
{
    Anchor,
    Use,
    Store,
    Reservation,
    Inlined, // The call of the Inlined scope an A1 or A2 problem lies in: its template's check (SPEC 7.2.3), which MirReport drops.
    EffectRead, // A read key of the Field (SPEC 15.6.4): it meets a unique Loan.
    EffectWrite, // A write key of the Field: it meets a Loan in any mode.
    EffectUnknown, // An unknown effect or an erased destruction (SPEC 15.6.4, 16.3.3): it meets every mutable-static Loan.
    Evaluated, // The default's operation of a Loan problem a body owns inside a copy, whose subject is the omitting call (SPEC 15.6).
}

/// <summary>A related site of a problem (R4-M3a).</summary>
/// <param name="Kind">What the site is.</param>
/// <param name="Site">The syntax.</param>
internal readonly record struct MirFact(MirFactKind Kind, SourceRef Site);
