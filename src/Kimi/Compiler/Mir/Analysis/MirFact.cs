// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the vocabulary of a MIR problem's facts (A6; DECISIONS R4-M3a, R4-M57, R4-M69, R4-M92, R4-M97, R4-M132): what a fact is and the
// record of one.
// Input: the kind, site and value a recording rule gives. Output: MirFact values, which MirReport keeps per problem and the difference
// ledger reads.
// Invariants: a fact is a site the problem relates and, for a kind that names one, its typed value, never a message (R4-M132): an Inlined
// fact names the inline default copy whose template an A1 or A2 Language problem belongs to, which a limit record never carries (R4-M69,
// until 2g-6), an Evaluated fact the default's operation of a Loan problem a body owns inside a copy, recorded at the omitting call
// (R4-M97), and an effect fact the static Field an effect met, its kind what decided it (R4-M92), and a Relation fact (an Origin problem's
// last edge, D18) the edge's site and as its Value the row's index in the body's C1-F4 view, or -1 for a containment edge at its Ref, and
// the storage limit's Required, Limit and Table facts (G213, R4-M132) the body and as their Values the bytes its arena needs, the
// analyses' byte limit and the arena (a MirStorageTable), the catalog's requiredBytes, limitBytes and table of OwnershipStorageLimit_Kd,
// and a Cause fact (SPEC 15.6.5, 23.1.2; rule W, R4-M132) the subject of a problem this one is caused only by and as its Value that
// problem's index in MirReport.All (-1 reserved for 2g-1's Binding cause at the site); a kind that names no value has -1; a problem has
// at most one fact of each kind but its witness's and its causes, which come last.
// Must not carry message text, or decide which problem a fact belongs to or whether it is kept (MirReport's).

#pragma warning disable SA1402 // The fact vocabulary and its record belong together.

namespace Kimi.Compiler.Mir;

/// <summary>What a related site of a problem is (R4-M3a): a Loan's anchor, its witness holder's next use, the store whose value
/// carries it, the anchor of the reservation the problem's activation or destruction is of (design A3 6.4), the call of the inline
/// default copy an A1 or A2 Language problem was found in (R4-M69), the default's operation of a Loan problem a body owns inside a copy
/// (R4-M97), the declaration of the static Field a Call's or Drop's effects met, by what decided it (R4-M92), an Origin problem's last
/// edge (D18), the body of a storage limit with one of its arguments (G213, R4-M132), or a problem this one is caused only by (rule W,
/// R4-M132).</summary>
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
    Relation, // An Origin problem's last edge (2a0-D D18): a row of the body's C1-F4 view (Value), or a containment edge at its Ref (Value -1).
    Required, // A storage limit's body: the bytes its arena needs (Value), OwnershipStorageLimit_Kd's requiredBytes (MirDataflow.StorageLimit).
    Limit, // A storage limit's body: the analyses' byte limit (Value), its limitBytes.
    Table, // A storage limit's body: the arena (Value, a MirStorageTable), its table.
    Cause, // A problem this one is caused only by (SPEC 15.6.5, 23.1.2): its subject, its index in MirReport.All (Value); facts' tail.
}

/// <summary>A fact of a problem (R4-M3a, R4-M132): a site it relates and, for a kind that names one, its typed value.</summary>
/// <param name="Kind">What the site is.</param>
/// <param name="Site">The syntax.</param>
/// <param name="Value">A Relation fact's row index in the body's C1-F4 view, -1 for a containment edge; a storage limit's bytes, byte limit or
/// arena; a Cause's problem index; -1 for a kind that names no value.</param>
internal readonly record struct MirFact(MirFactKind Kind, SourceRef Site, long Value = -1);
