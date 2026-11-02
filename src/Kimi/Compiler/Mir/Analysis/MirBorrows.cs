// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns A5's Loan stratum (SPEC 15.6–15.8, 7.2.3; design A3 3, 5 and 6 with its errata; DECISIONS R4-M57, R4-M66) per body and Semantics
// case view.
// Input: a validated body, its dataflow (MirDataflow, begun) and move paths (MirMoveData, built), the selected view (MirCases), A2's
// verdicts (MirProgram.Drops), Binding's call retention (TyCtxt.Retains), published rows (TyCtxt.Published), stored parts and their raw
// layers' steps, members and statement (TyCtxt.StoredParts, RawStep, RawMembers, RawStated), A4's key rows of the program version
// (MirEffects.Keys), and the Origin stratum's causes and classes of the body it judged before (MirOrigins.Causes, Refutes, Fixed; rule W,
// R4-M132).
// Output: each Loan conflict at its access, activation or Call's or Drop's effects, coded by A3 5's A6 table (CallReservationConflict_Kd
// for a Loan in its reservation holder, CallActivationConflict_Kd or PlacementActivationConflict_Kd for an activation at a Call or at the
// writeback write, else ComparisonLoanConflict_Kd, also for a value that keeps a guard candidate's guard-local Origin past its EndBorrow),
// with the Loan's anchor, its witness's next use or the store that carries it, an activation's reservation and, inside an inline default
// copy, the default's operation (Evaluated, the problem at the outermost omitting call; R4-M97) as facts, a failure whose events all lie in
// one evaluation of a copy left to its template; Unsupported_Kd at a call or a Borrow or read it does not classify (a call whose W
// reaches a holder the guard leaves undecided, MirWrites.Limit, R4-M147, and the cases of the rules below); OwnershipDepthLimit_Kd where a
// check's verdict rests on a part past a walk's bound or on a call's retention limit (R4-M153: a borrowed root's Drop, a destroying call,
// a store W or its referent leaves undecided there, A4's Deep at a call or Drop, and a conflict only drop-liveness gives, at the drop-live
// holder's declaration); each a limit record at its operation also inside a copy, and in a view PD13 holds (a Closure call acquiring more than the
// view's case admits, R4-M148) every rejection at its subject, through MirReport; InternalInvariant_Kd at a Value call whose declared
// signature does not number its operands (G136, R4-M148); the own-step Warning OwnStepDeparture_Kd at an
// operation that may end an owning value's stay in its holder while a pointer formed through its own step may still be used, with its
// anchor, witness and Evaluated facts (SPEC 5.2.1 condition 3); a Loan conflict caused only by Refuted Origin relations (SPEC 15.6.5) with
// their problems as Cause facts; and Work, the steps it took (A3 6.2).
// Rules, each with its inputs, records and Unsupported_Kd cases in its owner's contract:
// - a raw pointer's addresses and own-step entries, the untracked rule and PQ-10's Borrows and reads: MirBorrows.Addresses (R4-M116);
// - the own-step entries' departures: MirBorrows.OwnSteps (R4-M116);
// - a Call's contract rows, P9's and untied stores among them: MirBorrows.CallRows; their transfer: MirBorrows.Calls;
// - the effect rule and its initialization proof: MirBorrows.Effects;
// - a default template's slot-rooted test, R26-borrow and R26-access, and the copy disposition: MirBorrows.Defaults;
// - a Place's steps and their classes, E17's Inline row included: MirSteps (R4-M100);
// - the problems' codes and facts, and PD13's held view: MirBorrows.Reports, its fact MirBorrows.CallRows' (R4-M148);
// - a Call's rows, a Closure callee's call receiver at the callee operand among them: MirBorrows.CallRows (R4-M148);
// - rule W, its elements, indirect markers, escape holders and causes: MirBorrows.Causes (R4-M132).
// Invariants: a holder is a root local whose case Type may hold a Loan (MirTypes.HoldsLoans, a raw layer included), or E, the storage the
// body does not own, static storage included (a direct Loan on a static names E), or a holder's drop slot, whose state is always empty
// (drop-liveness, MirBorrows.Holders; R4-M153); its state is two MirLoanSets nodes, Placed and Shared, whose union σ(h) every reader
// reads (MirBorrows.States, R4-M160), of Loans, addresses and markers, of which only Loans conflict and only Loans, with their
// indirect markers, an ending removes (Φ_E, the per-local markers of the role table, Marker, and rule W's indirect markers,
// MirBorrows.Causes). An
// Opaque site's value is {Φ_E}: it rests on a recorded error, so it carries none of its roots' Loans (R4-M70). A Loan exists per non-raw
// Ref site, and per PairAcquire the view makes a Reborrow, in its Use's marked mode (E13 (4)), whose Place passes no shared borrow Deref
// (E3), named by its whole Place, a raw Place borrow's, past a raw Deref, its fresh anchor's, which no access reaches (SPEC 5.2.2,
// R4-M156); an address, after the Loans, names the holder a raw pointer addresses (SPEC 15.6 Addresses, R4-M116), as a direct Loan does,
// and a value that may refer to storage the body does not own carries Φ_E where it is formed, E's own state from the entry; access is
// syntactic, by Place prefix. A check reads holders, never Loan ids: the holders live after the access but those it
// overwrites, E, and the values the access takes (E13 (1): an operand's, its operation's operands' values up to it and roots after it in
// evaluation order; a borrow's R2 parents and E5's seed; an observing destruction's value; another access's, the values it stores into a
// live holder, transit, and E5's seed), closed through the holders their direct Loans name; an activation the operation's operands' values
// and its reservation holder's seed, that holder left out; all from one gather per location. A two-phase Loan is shared only in its
// reservation holder, where a write or destruction of a prefix of its Place reaches it (E1). Destroying a Move Path in place ends every
// Loan whose path it exactly prefixes (E8), a whole static's included, after the checks and the transfer: the Loans are ordered by the Move
// Path of their Places' longest inline prefixes (MirMoveData's pre-order), so those of a path's subtree are one run of ids. A Call that
// moves a pending slot ends the Loans of its Move Path's subtree, a default's inspection Loans, before its plan and checks (R-a, R4-M96). A
// holder a direct Loan names keeps its state at every head.
// Must not read syntax or HIR, judge Origins (it reads MirOrigins' published causes and classes), re-derive retention or Move Paths, intern
// projections, or copy the body per view.

namespace Kimi.Compiler.Mir;

/// <summary>A5's Loan stratum (design A3, R4 shadow: the old path still decides): <see cref="Run"/> checks a body under the selected
/// view.</summary>
/// <param name="types">The Type context, whose retention and published rows calls read.</param>
/// <param name="flow">The dataflow, begun with the body.</param>
/// <param name="moves">The move paths, built for the body.</param>
/// <param name="cases">The Semantics case views, a view of the body selected.</param>
/// <param name="effects">A4, whose last run's key rows the effect rule reads.</param>
/// <param name="origins">The Origin stratum, whose causes and classes of the body it judged rule W reads.</param>
internal sealed partial class MirBorrows(TyCtxt types, MirDataflow flow, MirMoveData moves, MirCases cases, MirEffects effects, MirOrigins origins)
{
    private const int Escaped = 0; // E's slot, and the element of the marker Φ_E.
    private const int Markers = 1; // The element of local 0's marker, after Φ_E: local L's is Markers + L, which its role decides (Marker).

    private readonly MirLoanSets sets = new();
    private readonly HashSet<BoundType> seen = new(ReferenceEqualityComparer.Instance);
    private MirBody body = null!;
    private MirProgram program = null!;
    private long view;
    private ulong views;
    private int loanBase; // The element of Loan 0: after Φ_E and the per-local markers.
    private int escapedSet; // The set {Φ_E}.

    /// <summary>What a local's marker, element <see cref="Markers"/> plus the local, is: the role table of the per-local markers (R4-M70),
    /// by the local's role (<see cref="MarkerOf"/>).</summary>
    private enum Marker : byte
    {
        None,
        Guard, // A guard candidate H's γ_H (E7): on the Loans issued into H; at H's EndBorrow a value that keeps it escapes (err(ended)).
    }

    /// <summary>Gets the work of the runs (A3 6.2): the pre-pass's walks, transfers, both problems' slot joins, the holders of each point
    /// that ends Loans, each check's bit scan, the entries and taken values it lists and walks, stored values and seeds, the set elements its
    /// loops and the set operations scanned (MirLoanSets.Scanned), the checks' stamps it clears, candidates compared and set bytes
    /// made.</summary>
    internal long Work { get; private set; }

    /// <summary>Gets the element bytes of the Loan sets the runs made (MirLoanSets.Bytes, A3 6.2's ν), which Work also counts.</summary>
    internal long Bytes { get; private set; }

    // The role table of the per-local markers (R4-M70): local L's marker by L's role; every rule reads a marker's kind here, so a later
    // kind adds a row.
    private Marker MarkerOf(int local) => this.body.Locals[local].Role switch
    {
        MirLocalRole.GuardCandidate => Marker.Guard,
        _ => Marker.None,
    };

    // A location's index: a statement's in the body, a terminator's after every statement.
    private int Location(int block, int index)
    {
        ref readonly var data = ref this.body.Blocks[block];
        return index < data.Count ? data.Start + index : this.body.Statements.Length + block;
    }

    // A root's key: a local, or a static after the locals.
    private int Key(int root) => root >= 0 ? root : this.body.Locals.Length + ~root;
}
