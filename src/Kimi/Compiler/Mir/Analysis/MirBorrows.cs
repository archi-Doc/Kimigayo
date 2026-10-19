// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns A5's Loan stratum (SPEC 15.6–15.8; design A3 3, 5 and 6 with its errata; DECISIONS R4-M57, R4-M63, R4-M66) per body and Semantics
// case view.
// Input: a validated body, its dataflow (MirDataflow, begun) and move paths (MirMoveData, built), the selected view (MirCases), A2's
// verdicts (MirProgram.Drops), Binding's call retention (TyCtxt.Retains, an intrinsic's declaration included, with the intrinsic table's
// retained operand, MirDescriptors) and published rows (TyCtxt.Published). Output: each Loan
// conflict at its access or activation, coded by A3 5's A6 table (CallReservationConflict_Kd for a Loan in its reservation holder,
// CallActivationConflict_Kd or PlacementActivationConflict_Kd for an activation at a Call or at the writeback write, else
// ComparisonLoanConflict_Kd, also for a value that keeps a guard candidate's guard-local Origin past its EndBorrow), with the Loan's
// anchor, its witness's next use or the store that carries it, an activation's reservation and an inline default copy's call (R4-M69) as
// facts, and Unsupported_Kd at a destruction or a call it does not classify (a destruction walk past its bound, CallRetention.Limit), a
// limit record without that fact, or at a default's omitting call for a conflict whose Loan only sets with its default taint hold (R4-M70),
// through MirReport; and Work, the steps it took (A3 6.2).
// Invariants: a holder is a root local whose case Type may hold a Loan (MirTypes.HoldsLoans), or E, the storage the body does not own; its
// state is a MirLoanSets node of Loans and markers, which never conflict and no ending removes (Φ_E, and the per-local markers of the role
// table, Marker). An Opaque site's value is {Φ_E}: it rests on a recorded error, so it carries none of its roots' Loans (R4-M70). A
// Loan exists per non-raw Ref site, and per PairAcquire the view makes a Reborrow, in its Use's marked mode (E13 (4)), whose Place passes no
// shared borrow Deref (E3), named by its whole Place; access is syntactic, by Place prefix. A check reads holders, never Loan ids: the
// holders live after the access but those it overwrites, E, and the values the access takes (E13 (1): an operand's, its operation's
// operands' values up to it and roots after it in evaluation order; a borrow's R2 parents and E5's seed; an observing destruction's
// value; another access's, the values it stores into a live holder, transit, and E5's seed), closed through the holders their direct
// Loans name; an activation the operation's operands' values and its reservation holder's seed, that holder left out, all from one gather
// per location. A two-phase Loan is shared only in its reservation
// holder, where a write or destruction of a prefix of its Place reaches it (E1). Destroying a Move Path in place ends every Loan whose path
// it exactly prefixes (E8), after the checks and the transfer: the Loans are ordered by the Move Path of their Places' longest inline
// prefixes (MirMoveData's pre-order), so those of a path's subtree are one run of ids. A holder a direct Loan names keeps its state at
// every head.
// Must not read syntax or HIR, judge Origins, re-derive retention or Move Paths, intern projections, or copy the body per view.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

/// <summary>A5's Loan stratum (design A3, R4 shadow: the old path still decides): <see cref="Run"/> checks a body under the selected
/// view.</summary>
/// <param name="types">The Type context, whose retention and published rows calls read.</param>
/// <param name="flow">The dataflow, begun with the body.</param>
/// <param name="moves">The move paths, built for the body.</param>
/// <param name="cases">The Semantics case views, a view of the body selected.</param>
internal sealed partial class MirBorrows(TyCtxt types, MirDataflow flow, MirMoveData moves, MirCases cases)
{
    private const int Escaped = 0; // E's slot, and the element of the marker Φ_E.
    private const int Markers = 1; // The element of local 0's marker, after Φ_E: local L's is Markers + L, which its role decides (Marker).

    private readonly MirLoanSets sets = new();
    private readonly HashSet<BoundType> seen = new(ReferenceEqualityComparer.Instance);
    private int[] slots = []; // Each local's holder slot, -1 for none; E is slot 0.
    private bool[] reserves = []; // By slot: whether a two-phase Loan is issued into it, its reservation holder.
    private MirBody body = null!;
    private MirProgram program = null!;
    private long view;
    private ulong views;
    private int holderCount; // The slots, E's included.
    private int flowCount; // The slots a head stores: E and the flow holders.
    private int loanBase; // The element of Loan 0: after Φ_E and the per-local markers.
    private int escapedSet; // The set {Φ_E}.

    /// <summary>What a local's marker, element <see cref="Markers"/> plus the local, is: the role table of the per-local markers (R4-M70),
    /// by the local's role (<see cref="MarkerOf"/>).</summary>
    private enum Marker : byte
    {
        None,
        Guard, // A guard candidate H's γ_H (E7): on the Loans issued into H; at H's EndBorrow a value that keeps it escapes (err(ended)).
        Taint, // A pending slot D's default taint (until A5-F9, unit 2e): on the value a default's evaluation places into D (Placed).
    }

    /// <summary>Gets the work of the runs (A3 6.2): the pre-pass's walks, transfers, both problems' slot joins, the holders of each point
    /// that ends Loans, each check's bit scan, the entries and taken values it lists and walks, stored values and seeds, the set elements its
    /// loops and the set operations scanned (MirLoanSets.Scanned), the checks' stamps it clears, candidates compared and set bytes
    /// made.</summary>
    internal long Work { get; private set; }

    /// <summary>Gets the element bytes of the Loan sets the runs made (MirLoanSets.Bytes, A3 6.2's ν), which Work also counts.</summary>
    internal long Bytes { get; private set; }

    /// <summary>Checks a body under the selected view; view 0 of a body no view decides checks it for every view.</summary>
    /// <param name="program">The program, whose drop table it reads and whose problems it adds to.</param>
    /// <param name="body">The body.</param>
    /// <param name="view">The selected view.</param>
    internal void Run(MirProgram program, MirBody body, long view)
    {
        if (view > 0 && !moves.ViewDependent)
        {
            return;
        }

        var all = ulong.MaxValue >> (64 - (int)MirCases.Count(body));
        (this.program, this.body, this.view, this.views) = (program, body, view, moves.ViewDependent ? 1UL << (int)view : all);
        this.sets.Clear();
        (this.loanBase, this.escapedSet) = (Markers + body.Locals.Length, this.sets.Add(0, Escaped));
        this.Loans();
        if (this.loanCount == 0 && !this.guarded)
        {
            return; // A view without a Loan or a guard candidate has no problem (A3 6.1; E7's γ needs no Loan).
        }

        this.Holders();
        this.Endings();
        this.Touches();
        var words = (this.holderCount + 63) >> 6;
        var state = flow.Prepare((this.flowCount + 63) >> 6, this.flowCount, Math.Max(words, this.holderCount));
        if (state is null)
        {
            var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.StorageLimit);
            program.Problems.Add(new(DiagnosticCode.OwnershipStorageLimit_Kd, requirement, body.Source, body.Ordinal, all)); // R4-M15a.
            return;
        }

        var liveness = new Liveness(this, state);
        flow.Backward(ref liveness);
        liveness.Recording = true;
        flow.ReplayBackward(ref liveness);
        var carrying = new Carrying(this, state);
        flow.Forward(ref carrying);
        (carrying.Checks, this.chainHead) = (true, -1);
        this.Restamp();
        flow.Replay(ref carrying);
        this.Bytes += this.sets.Bytes;
        this.Work += this.sets.Bytes + this.sets.Scanned;
    }

    // The role table of the per-local markers (R4-M70): local L's marker by L's role; every rule reads a marker's kind here, so a later
    // kind adds a row.
    private Marker MarkerOf(int local) => this.body.Locals[local].Role switch
    {
        MirLocalRole.GuardCandidate => Marker.Guard,
        MirLocalRole.PendingSlot => Marker.Taint,
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

    // The holders of the view (A3 3): E in slot 0, then the flow holders, whose states cross heads (MirMoveData's flow roots and the roots a
    // direct Loan names, whose states the closure and the Loans' content read anywhere), then the chain-local ones; then each Loan's
    // reservation holder and named holder as slots.
    private void Holders()
    {
        var locals = this.body.Locals;
        MirDataflow.Grow(ref this.slots, locals.Length);
        this.holderCount = 1;
        for (var i = 0; i < locals.Length; i++)
        {
            this.slots[i] = MirTypes.HoldsLoans(cases.Type(locals[i].Type), this.seen) ? -2 : -1;
        }

        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < locals.Length; i++)
            {
                if (this.slots[i] == -2 && (this.named[i] || moves.Find(new(i), out _) < moves.FlowCount) == (pass == 0))
                {
                    this.slots[i] = this.holderCount++;
                }
            }

            this.flowCount = pass == 0 ? this.holderCount : this.flowCount;
        }

        MirDataflow.Grow(ref this.reserves, this.holderCount);
        Array.Clear(this.reserves, 0, this.holderCount);
        for (var l = 0; l < this.loanCount; l++)
        {
            ref var loan = ref this.loans[l];
            (loan.Holder, loan.Named) = (loan.Holder >= 0 ? this.slots[loan.Holder] : -1, loan.Named >= 0 ? this.slots[loan.Named] : -1);
            if (loan is { Mode: MirRefKind.TwoPhaseUniq, Holder: >= 0 })
            {
                this.reserves[loan.Holder] = true;
            }
        }

        this.Work += (2 * locals.Length) + this.loanCount + this.holderCount;
    }
}
