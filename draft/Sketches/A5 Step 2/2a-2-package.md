# 2a-2 package: rule W on the MIR, with MirProblem's facts as one representation (design C, approved)

G:A5-F4's merge unit (A5 step 2 plan; 2a0-D-package.md §10's 2a-2 row, superseded by this package). Full process: two independent
designs (`2a-2-design-A.md`, `2a-2-design-B.md`), a design review that chose their combination C (`2a-2-review.md` §5–§9), R5's
confirmation and the owner's approval. The implementer follows `2a-2-review.md` §5 (each part of C and its source) and §6 (units,
sizes, tests), with the conditions below.

## Owner (2026-10-25, the R4 planning session's dialog, verbatim)

- The departures (`2a-2-review.md` §7, one list: Judge records before the Loan stratum; the builder's `Result` role and MirOrigins'
  `Fixed` replace R4-M104 (H)'s row link, 2a0-D D2's `ResultFit` and D18's `Result` fact; 2a0-D §10's 2a-2 row replaced by §6's units
  and `MirBorrowsTest.Causes.cs`; R4-M71's merge: derived only when every grouped report is, causes unioned, the key unchanged; a
  `MirFact` is a site and its kind's typed value): 「(a) 一括で承認 (推奨)」.
- Publication (OQ-2): 「(a) PrerequisiteUnavailable_Kd (推奨)」 — a derived MIR Loan problem is published as
  `PrerequisiteUnavailable_Kd` with `DerivedFrom` = its causes' check keys; DIAGNOSTICS §4.3's invariant is unchanged.
- Vocabulary (COMPILER_ARCHITECTURE's closed vocabularies): `MirFactKind` Cause, Required, Limit, Table and `MirLocalRole.Result`:
  「(a) すべて承認 (推奨)」.
- OQ-3 (a call storing a borrow into a container): decided by adopting C, A's receiving check over every Origin of the receiving Type
  (no owner question); 2c-6 re-expects the closure forms.

## R5's confirmation (2026-10-25; `2a-2-review.md` §9, all eight) and its conditions

1. `MirFact(Kind, Site, long Value)` is H3's one input (Required/Limit → Arguments, Table → the `table` evidence, Relation → H2's row,
   Cause → DerivedFrom, the rest → related locations and evidence). H3 maps a Kind through one table, one row per Kind; a Kind without a
   row is `InternalInvariant_Kd`, not a switch arm.
2. OQ-2 (a) is H3's own fallback row (PLAN10 P10).
3. One resolver, in H3's publisher, maps a `Cause` index to the check key published after R4-M71's cross-body merge; derived-ness merges
   the same way, causes unioned. R5 accepts this extension of AR-7 to MIR-to-MIR causes, beside 2g-1's Binding causes (−1). No consumer
   resolves an index itself; an index naming a problem the merge or a later rule removed is a broken contract, not a silent drop.
4. `tests/xUnitTest/MirDifferenceLedger.ComparedSet.cs`'s line is R4's to edit in U3 (R4's difference ledger).
5. U2 holds `Mir/Build/MirBuilder.Transfers.cs`, `Mir/MirVocabulary.cs`, `Mir/MirValidator*.cs`, the MIR goldens and the semantic
   snapshot dumps (W1 files; PLAN §3 hold line); R5 reviews the role and its validator rule. `MirLocalRole.Result` changes nothing code
   generation reads (frame homes, ResultSlot): U2's evidence is a sel Unit run of `FunctionCodegenTest` with the result-heavy families'
   fixtures, `.ll` byte-identical to U2's parent, and the sel gate at 0; if the role changes a home, stop and tell R5 before landing.
6. U4 changes A2's `Incomplete` (= Code, SourceInfo, report index) and R5's gate (`FunctionCodegen.Write`) together, with no temporary
   second form: the gate records `OwnershipStorageLimit_Kd` with the Required/Limit/Table facts, applies KD12 through
   `Subject(item, body, source)`, deletes R5-M96's interim and the `Subject(item, SourceRef)` overload, one located record per refused
   body. R5 reviews that Backend hunk and its codegen-pending effect; R4 tells R5 before U4 starts.
7. No further H3 need for A5's codes; a later need is a new Kind row in the same table.
8. 2g-5's PreparedInput rule never drops a problem another names as a cause, failing closed: every Cause index resolves after
   suppression, else `InternalInvariant_Kd`.

## Review conditions carried into the units

- A-B1 (V16): MirLoanSets builds a marker set in one pass (a new operation) with a V16 row for "read through a parameter after each
  refuted store"; A-B2: a merge unions causes (§4.3); B-B1: the role only on the temp whose Destination is local 0, the validator rule
  "every Result local is a Temp of local 0's Type, read only by whole Moves into local 0".
- Cost: measure the library's whole MIR analysis with the refutes gate (the review measured A's prototype +3.1–3.3%, +2.2–2.5% with an
  early exit); C's 16 derived problems on 65be867e9's corpus are inferred, not run.
- DECISIONS: one entry in U1's commit (C, the owner's answers, R5's conditions and its AR-7 acceptance, the vocabulary approval).
