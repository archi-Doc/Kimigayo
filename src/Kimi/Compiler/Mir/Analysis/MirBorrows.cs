// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns A5's Loan stratum (SPEC 15.6–15.8, 7.2.3; design A3 3, 5 and 6 with its errata; DECISIONS R4-M57, R4-M66) per body and Semantics
// case view.
// Input: a validated body, its dataflow (MirDataflow, begun) and move paths (MirMoveData, built), the selected view (MirCases), A2's
// verdicts (MirProgram.Drops), Binding's call retention (TyCtxt.Retains) and published rows (TyCtxt.Published), and A4's key rows of the
// program version (MirEffects.Keys).
// Output: each Loan conflict at its access, activation or Call's or Drop's effects, coded by A3 5's A6 table (CallReservationConflict_Kd
// for a Loan in its reservation holder, CallActivationConflict_Kd or PlacementActivationConflict_Kd for an activation at a Call or at the
// writeback write, else ComparisonLoanConflict_Kd, also for a value that keeps a guard candidate's guard-local Origin past its EndBorrow),
// with the Loan's anchor, its witness's next use or the store that carries it, an activation's reservation and, inside an inline default
// copy, the default's operation (Evaluated, the problem at the outermost omitting call; R4-M97) as facts, a failure whose events all lie in
// one evaluation of a copy left to its template; Unsupported_Kd at a destruction or a call it does not classify (a destruction walk past
// its bound, and the cases of the rules below), a limit record at its operation also inside a copy, and in a view a Call holds (its W
// stored a value past a holder that passes the guard only by argument, P13's interim, R4-M73) every other Loan problem as Unsupported_Kd at
// its subject, through MirReport; and Work, the steps it took (A3 6.2).
// Rules, each with its inputs, records and Unsupported_Kd cases in its owner's contract:
// - a Call's contract rows, P9's among them: MirBorrows.CallRows; their transfer, with r3: MirBorrows.Calls;
// - the effect rule and its initialization proof: MirBorrows.Effects;
// - a default template's slot-rooted test, R26-borrow and R26-access, and the copy disposition: MirBorrows.Defaults;
// - a Place's steps and their classes, E17's Inline row included: MirSteps (R4-M100);
// - the problems' codes and facts and the held view: MirBorrows.Reports.
// Invariants: a holder is a root local whose case Type may hold a Loan (MirTypes.HoldsLoans), or E, the storage the body does not own,
// static storage included (a direct Loan on a static names E); its state is a MirLoanSets node of Loans and markers, which never conflict
// and no ending removes (Φ_E, and the per-local markers of the role table, Marker). An Opaque site's value is {Φ_E}: it rests on a recorded
// error, so it carries none of its roots' Loans (R4-M70). A Loan exists per non-raw Ref site, and per PairAcquire the view makes a
// Reborrow, in its Use's marked mode (E13 (4)), whose Place passes no shared borrow Deref (E3), named by its whole Place; access is
// syntactic, by Place prefix. A check reads holders, never Loan ids: the holders live after the access but those it overwrites, E, and the
// values the access takes (E13 (1): an operand's, its operation's operands' values up to it and roots after it in evaluation order; a
// borrow's R2 parents and E5's seed; an observing destruction's value; another access's, the values it stores into a live holder, transit,
// and E5's seed), closed through the holders their direct Loans name; an activation the operation's operands' values and its reservation
// holder's seed, that holder left out; all from one gather per location. A two-phase Loan is shared only in its reservation holder, where a
// write or destruction of a prefix of its Place reaches it (E1). Destroying a Move Path in place ends every Loan whose path it exactly
// prefixes (E8), a whole static's included, after the checks and the transfer: the Loans are ordered by the Move Path of their Places'
// longest inline prefixes (MirMoveData's pre-order), so those of a path's subtree are one run of ids. A Call that moves a pending slot
// ends the Loans of its Move Path's subtree, a default's inspection Loans, before its plan and checks (R-a, R4-M96). A holder a direct
// Loan names keeps its state at every head.
// Must not read syntax or HIR, judge Origins, re-derive retention or Move Paths, intern projections, or copy the body per view.

namespace Kimi.Compiler.Mir;

/// <summary>A5's Loan stratum (design A3, R4 shadow: the old path still decides): <see cref="Run"/> checks a body under the selected
/// view.</summary>
/// <param name="types">The Type context, whose retention and published rows calls read.</param>
/// <param name="flow">The dataflow, begun with the body.</param>
/// <param name="moves">The move paths, built for the body.</param>
/// <param name="cases">The Semantics case views, a view of the body selected.</param>
/// <param name="effects">A4, whose last run's key rows the effect rule reads.</param>
internal sealed partial class MirBorrows(TyCtxt types, MirDataflow flow, MirMoveData moves, MirCases cases, MirEffects effects)
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
