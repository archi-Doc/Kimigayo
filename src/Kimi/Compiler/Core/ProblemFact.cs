// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the fact vocabulary of a problem (A6; DECISIONS R4-M3a, R4-M57, R4-M69, R4-M92, R4-M97, R4-M132; R7-E1): what a fact is and the
// record of one. It lives in Core, over no phase's structure, so that a phase which may not reference Mir can use it.
// Input: the kind, site and value a recording rule gives. The MIR analyses' recording rules write Anchor through Cause, and Cause stays
// theirs; Binding's recorder writes the kinds after Cause, appended, and a MIR record carrying one is a broken contract.
// Output: ProblemFact values, which MirReport keeps per problem and Core's publisher (`Compilation.Publication`) reads, beside the test MIR
// side (`MirTargetSide.MirSide`), which reads the problems they belong to, and which BindingReport keeps for Binding's adapter.
// Invariants: a fact is a site the problem relates and, for a kind that names one, its typed value, never a message (R4-M132): an Inlined
// fact names the inline default copy whose template an A1 or A2 Language problem belongs to, which a limit record never carries (R4-M69,
// until 2g-6), an Evaluated fact the default's operation of a Loan problem a body owns inside a copy, recorded at the omitting call
// (R4-M97), and an effect fact the static Field an effect met, its kind what decided it (R4-M92), and a Relation fact (an Origin problem's
// last edge, D18) the edge's site and as its Value the row's index in the body's C1-F4 view, or -1 for a containment edge at its Ref, and
// the storage limit's Required, Limit and Table facts (G213, R4-M132) the body and as their Values the bytes its arena needs, the
// analyses' byte limit and the arena (a MirStorageTable), the catalog's requiredBytes, limitBytes and table of OwnershipStorageLimit_Kd,
// and a Cause fact (SPEC 15.6.5, 23.1.2; rule W, R4-M132) the subject of a problem this one is caused only by and as its Value that
// problem's index in MirReport.All, in MirReport.Merged's facts its merged problem's index in Merged (R6-check H3-2), -1 a Binding cause at
// the site (A3's guessed sites, R5-M186; 2g-1's), and an Actual or Expected fact the syntax that shows a mismatch and as its Value an index
// in BindingReport's kept Types, an ActualForm or ExpectedForm fact the same syntax and as its Value a MismatchForm row and, above its byte,
// an anonymous function's parameter count plus one, 0 for none, and a Span fact its subject and as its Value the located part,
// start << 32 | length; a kind that names no value has -1; a problem has at most one fact of each kind but its witness's and its causes,
// which come last.
// Must not carry message text or a phase's structure, or decide which problem a fact belongs to or whether it is kept (MirReport's).

#pragma warning disable SA1402 // The fact vocabulary and its record belong together.

namespace Kimi.Compiler;

/// <summary>What a related site of a problem is (R4-M3a): a Loan's anchor, its witness holder's next use, the store whose value
/// carries it, the anchor of the reservation the problem's activation or destruction is of (design A3 6.4), the call of the inline
/// default copy an A1 or A2 Language problem was found in (R4-M69), the default's operation of a Loan problem a body owns inside a copy
/// (R4-M97), the declaration of the static Field a Call's or Drop's effects met, by what decided it (R4-M92), an Origin problem's last
/// edge (D18), the body of a storage limit with one of its arguments (G213, R4-M132), a problem this one is caused only by (rule W,
/// R4-M132), or, for Binding, the syntax that shows a mismatch with one of its two sides or the subject with its located part (R7-E1).</summary>
internal enum ProblemFactKind : byte
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
    Relation, // An Origin problem's last edge (2a0-D D18): a row of the body's C1-F4 view (Value), or a containment edge at its Ref (Value -1).
    Required, // A storage limit's body: the bytes its arena needs (Value), OwnershipStorageLimit_Kd's requiredBytes (MirDataflow.StorageLimit).
    Limit, // A storage limit's body: the analyses' byte limit (Value), its limitBytes.
    Table, // A storage limit's body: the arena (Value, a MirStorageTable), its table.
    Cause, // A problem this one is caused only by (SPEC 15.6.5, 23.1.2): its subject, its index in MirReport.All (Value); facts' tail.
    Actual, // Binding: the syntax that shows a mismatch, the Type found (Value, an index in BindingReport's kept Types; SPEC 23.3).
    Expected, // Binding: the syntax that shows a mismatch, the Type required (Value, the same).
    Span, // Binding: the subject of a located record, its part (Value, start << 32 | length; SPEC 23.1.1).
    ActualForm, // Binding: as Actual, for a side that is no Type (Value, a MismatchForm row and its count).
    ExpectedForm, // Binding: as Expected, the same.
}

/// <summary>A side of a mismatch that is no Type (SPEC 23.3): a closed row, whose text Binding's adapter owns, so no check writes one.</summary>
internal enum MismatchForm : byte
{
    AnonymousFunction, // With its parameter count, or none where no Function Type is expected.
    IntegerLiteral,
    FloatingPointLiteral,
    PositionType, // A Type proven Position or PositionRange.
}

/// <summary>A fact of a problem (R4-M3a, R4-M132): a site it relates and, for a kind that names one, its typed value.</summary>
/// <param name="Kind">What the site is.</param>
/// <param name="Site">The syntax.</param>
/// <param name="Value">A Relation fact's row index in the body's C1-F4 view, -1 for a containment edge; a storage limit's bytes, byte limit or
/// arena; a Cause's problem index (in Merged's facts, its merged problem's); a kept Type's index; a form's row and count; a located part; -1
/// for a kind that names no value.</param>
internal readonly record struct ProblemFact(ProblemFactKind Kind, SourceRef Site, long Value = -1);
