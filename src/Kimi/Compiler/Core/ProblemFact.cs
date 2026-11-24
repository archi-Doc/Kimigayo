// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the fact vocabulary of a problem (A6; DECISIONS R4-M3a, R4-M57, R4-M69, R4-M92, R4-M97, R4-M132; R7-E1): what a fact is and the
// record of one. It lives in Core, over no phase's structure, so that a phase which may not reference Mir can use it.
// Input: the kind, site and value a recording rule gives. The MIR analyses' recording rules write Anchor through Cause; Binding's
// recorder writes Cause with Value -1 (a never-shown cause a check names, R7-E2 E2-3) and the kinds after Cause, appended, and a MIR
// record carrying one of those is a broken contract.
// Output: ProblemFact values, which MirReport keeps per problem and Core's publisher (`Compilation.Publication`) reads, beside the test MIR
// side (`MirTargetSide.MirSide`), which reads the problems they belong to, and which BindingReport keeps for Binding's adapter.
// Invariants: a fact is a site the problem relates and, for a kind that names one, its typed value, never a message (R4-M132): an
// Evaluated fact the default's operation of a Loan problem a body owns inside a copy, recorded at the omitting call (R4-M97), and an
// effect fact the static Field an effect met, its kind what decided it (R4-M92), and a Relation fact (an Origin problem's
// last edge, D18) the edge's site and as its Value the row's index in the body's C1-F4 view, or -1 for a containment edge at its Ref, and
// the storage limit's Required, Limit and Table facts (G213, R4-M132) the body and as their Values the bytes its arena needs, the
// analyses' byte limit and the arena (a MirStorageTable), the catalog's requiredBytes, limitBytes and table of OwnershipStorageLimit_Kd,
// a collection record's Limit fact (R7-T1) its subject and IMPL A.4's Type height limit, TypeHeightLimit_Kd's limit,
// and a Cause fact (SPEC 15.6.5, 23.1.2; rule W, R4-M132) the subject of a problem this one is caused only by and as its Value that
// problem's index in MirReport.All, in MirReport.Merged's facts its merged problem's index in Merged (R6-check H3-2), -1 a Binding cause at
// the site (A3's guessed sites, R5-M186), and an Actual or Expected fact the syntax that shows a mismatch and as its Value an index
// in BindingReport's kept sides, a Type, or a length where its requirement compares lengths (SPEC 4.3.1), an ActualForm or ExpectedForm fact the same syntax and as its Value a Binding MismatchForm row and, above its
// byte, an anonymous function's parameter count plus one, 0 for none, a Span fact its subject and as its Value the located part,
// start << 32 | length, an Operand or Terminal fact the syntax an operator failed at and as its Value an index in BindingReport's kept sides,
// a Constraint fact the same syntax and as its Value an index in the kept propositions, one not proven or refuted, and an OriginRecord fact
// the subject and as its Value an index in the kept Origin records, a Related fact a location the record shows (its own or a related one) and as its Value the
// role's ordinal in its code's adapter row, a Count fact the subject and as its Value a count the requirement states, in the row's order,
// a Reason fact the subject and as its Value a row of the closed Binding enumeration its requirement names, or its code's packed rows
// (ParameterShapeMismatch_Kd's packs its mode, the other mode and the matching key, a byte each; a selection's candidate its CandidateFlags
// and two receivers), and above them an ordinal into its party's own parameters or Type parameters (a candidate's receiver parameter, a
// correlation's slot, E3c-2's Case index, a Callable call's position in its candidates' common signature), and a Key fact the conflicting
// parameter and as its Value an index in BindingReport's kept sides, the Type at which two parameter shapes' matching keys overlap (SPEC
// 7.4), neither side of a mismatch (R7-E2), and an Instance fact (R7-E2 E2-5b) as its Value an index in BindingReport's kept sides, a
// Type its location does not determine, read by its site: at the preceding Related fact's own site, the instance that party is (a related
// Contract's or associated declaration's declaring instance, or a construction's constructed Type), and at a member declaration of that
// party, the Type that instance binds the member to, and a Checked fact (AF-3b) a candidate party's argument part checked against its
// expected Type whose own resolution there failed (SPEC 10.5.1) and as its Value that Type's index in BindingReport's kept sides times
// two, plus one where the resolution is ambiguous (else none applies), packed and unpacked by BindingReport only (KeepChecked, Checked),
// at most one per party, after its Reason; a kind that names no value has -1; a problem has at most one fact of each kind but
// its witness's, its causes, which come last, its Related facts, each read by its role (its Value) and shown in the order its site
// records them, its Count and Instance facts, in their row's order, and an acquisition record's second Reason, a transfer's moving
// cases after its packed Reason (R7-E4 E4a-2, read by position); a selection's record is the subject's facts, then per party its
// Related (role 3 or 5) and that party's entries up to the next party Related, in the order its adapter renders them, read by position,
// never by a kind's first fact.
// Must not carry message text or a phase's structure, or decide which problem a fact belongs to or whether it is kept (MirReport's).

#pragma warning disable SA1402 // The fact vocabulary and its record belong together.

namespace Kimi.Compiler;

/// <summary>What a related site of a problem is (R4-M3a): a Loan's anchor, its witness holder's next use, the store whose value
/// carries it, the anchor of the reservation the problem's activation or destruction is of (design A3 6.4), the default's operation of a Loan problem a body owns inside a copy
/// (R4-M97), the declaration of the static Field a Call's or Drop's effects met, by what decided it (R4-M92), an Origin problem's last
/// edge (D18), the body of a storage limit with one of its arguments (G213, R4-M132), a problem this one is caused only by (rule W,
/// R4-M132), or, for Binding, the syntax that shows a mismatch with one of its two sides, the subject with its located part, a failed operator's
/// operand, judged Type or proposition, a further Origin record (R7-E1), a related location, a stated count, a closed reason, the Type two
/// parameter shapes overlap at, the Type a related party stands for (R7-E2) or a candidate's checked part with its expected Type (AF-3b).</summary>
internal enum ProblemFactKind : byte
{
    Anchor,
    Use,
    Store,
    Reservation,
    EffectRead, // A read key of the Field (SPEC 15.6.4): it meets a unique Loan.
    EffectWrite, // A write key of the Field: it meets a Loan in any mode.
    EffectUnknown, // An unknown effect or an erased destruction (SPEC 15.6.4, 16.3.3): it meets every mutable-static Loan.
    Evaluated, // The default's operation of a Loan problem a body owns inside a copy, whose subject is the omitting call (SPEC 15.6).
    Relation, // An Origin problem's last edge (2a0-D D18): a row of the body's C1-F4 view (Value), or a containment edge at its Ref (Value -1).
    Required, // A storage limit's body: the bytes its arena needs (Value), OwnershipStorageLimit_Kd's requiredBytes (MirDataflow.StorageLimit).
    Limit, // A storage limit's body: the analyses' byte limit (Value), its limitBytes; a height record's subject: IMPL A.4's height (R7-T1).
    Table, // A storage limit's body: the arena (Value, a MirStorageTable), its table.
    Cause, // A problem this one is caused only by (SPEC 15.6.5, 23.1.2): its subject, its index in MirReport.All (Value, -1 Binding's); facts' tail.
    Actual, // Binding: the syntax that shows a mismatch, the side found (Value, an index in BindingReport's kept sides, a Type or length; SPEC 23.3).
    Expected, // Binding: the syntax that shows a mismatch, the side required (Value, the same).
    Span, // Binding: the part of its subject the record is located at (Value, start << 32 | length; SPEC 23.1.1).
    ActualForm, // Binding: as Actual, for a side with no kept value, a form (Value, a MismatchForm row and its count).
    ExpectedForm, // Binding: as Expected, the same.
    Operand, // Binding: the syntax an operator failed at, its operand's Type (Value, an index in BindingReport's kept sides; SPEC 13).
    Terminal, // Binding: the same syntax, the Type the operator judged (Value, the same).
    Constraint, // Binding: the proposition its check needs and could not prove: an operator's requirement, a clause's proposition at a selected call or a range iteration's entry (Value, an index in the kept propositions).
    OriginRecord, // Binding: the subject, one of its Origin records beyond the first (Value, an index in the kept Origin records; SPEC 15.6.1).
    Related, // Binding: a location the record shows, its own or a related one, by role (Value, the role's ordinal in its code's row; SPEC 23.3).
    Count, // Binding: the subject, a count its requirement states, such as an arity (Value, the count; in its code's row order).
    Reason, // Binding: the subject, a row of the closed enumeration its requirement names, or its code's packed rows (Value; text the adapter's).
    Key, // Binding: the conflicting parameter, the Type at which its matching key overlaps an earlier one's (Value, a kept side's index; SPEC 7.4).
    Instance, // Binding: at the preceding Related fact's site, the instance that party is; at a member declaration of that party, the Type that instance binds the member to (Value, a kept side's index; SPEC 8.4.3, 8.9.2).
    Checked, // Binding: at a candidate party, the argument part checked against its expected Type whose own resolution there failed (Value, that Type's kept side index times two, plus one where the resolution is ambiguous, else none applies: BindingReport.KeepChecked, Checked; SPEC 10.5.1).
}

/// <summary>A fact of a problem (R4-M3a, R4-M132): a site it relates and, for a kind that names one, its typed value.</summary>
/// <param name="Kind">What the site is.</param>
/// <param name="Site">The syntax.</param>
/// <param name="Value">A Relation fact's row index in the body's C1-F4 view, -1 for a containment edge; a storage limit's bytes, byte limit or
/// arena; a Cause's problem index (in Merged's facts, its merged problem's); a kept value's index; a form's row and count; a located part; a
/// related location's role ordinal; a stated count; a reason's row; -1 for a kind that names no value.</param>
internal readonly record struct ProblemFact(ProblemFactKind Kind, SourceRef Site, long Value = -1);
