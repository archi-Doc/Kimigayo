# Post-Reduction Work Items

Date: 2026-10-12. Status: adopted by the owner on 2026-10-12 (C4h decided; the rest as recommended). Work list; every item can wait until the [compiler reduction plan](<2026-10-10 Compiler Size Reduction and Single MIR Plan.md>) completes (R9), and none is assigned to a stage. No SPEC change: each item applies an existing rule. Items needed before R5 are in [Pre-R5 Work Items](<2026-10-12 Pre-R5 Work Items.md>).

Base: `fcb190f4` (`dev`). Observations come from the R1 inventory (`fb7a7ba5`) and R1g-C (`61482071`, 2026-10-10); rows marked "checked" were re-read at the base.

## 1. Scope and rules

- **Content:** diagnostic category defects, two lexical defects and two library gaps found in R1 and left by its early close. Defects: a valid form reported with a Language code, an invalid form reported as `Unsupported_Kd`, or a derived failure reported again.
- **Why they can wait:** none produces wrong code. The only milestone programs affected (P33 and P38 for C4a, P36 for C4f) are rejected either way until their milestones resume after R6, so only the code differs. Most items sit in Binding, which R7 rebuilds (Resolver, InferCtxt, ContractSolver, CallPlan pipeline, Problem and Error propagation), or in the library, which R8 reorganizes. Fixed now, they would be redone there; fixed afterwards, once.
- **Independent items:** C4g and C4h (lexer and parser) touch nothing the plan rebuilds and may be done at any stage boundary. C3g and C4b (library only) may be done in parallel after R6 (plan §12.2, R8 row); before R6 they would add accepted forms to the frozen old paths (D5).
- **Before starting an item:** reproduce it at the current HEAD. If R7 or R8 already fixed it, delete the row and cite the commit. Follow AGENTS (Diagnostics) and SPEC §23: a valid but unimplemented form is one location-only `Unsupported_Kd`; a SPEC violation reports the code of its rule, reusing the code its neighbours use (as R1g-C did).
- **Out of scope:** work assigned to R5–R9; PLAN §7 issues and the milestone order, which resumes after R6; the pre-R5 items.

## 2. Valid forms rejected with a Language code

Target: accept the form under its general rule, or report one location-only `Unsupported_Kd`.

| ID | Form | Observed | Owner (at R1) | SPEC |
| --- | --- | --- | --- | --- |
| C4a | Refinement after `is`: Program 33 `require view is Leaf and view.extra == 20`; Program 38 `view@follow@ref` after `if view is Lamp` | `UnresolvedBinding_Kd` / `TypeMismatch_Kd` | member lookup in `Binding.Expressions`; Sealed view follow in `Binding.Conversions` | §14.10.1–§14.10.2 |
| C4b | `S is Indexable<isize>` with Array or Slice (checked: `Array.kimi` and `Slice.kimi` declare no Indexable conformance; `UniqSlice.kimi:3` does) | proof failure | library declarations (plan §5.8, library first) | §4.6.9 |
| C3g | Array `first` and `last` (checked: absent from `Array.kimi`) | `UnresolvedBinding_Kd` | library: declare both | §4.7.2 |
| C4d | `wrap<u/U>(x: u/U, b: u/Box<U>)` with borrowed arguments | `NoApplicableOverload_Kd` | call applicability (`Binding.Calls`) | — |
| C4e | Borrow of a temporary's part: `show(make().n)`, `Console.writeLine(make()[0].name)` | `NoApplicableOverload_Kd` | `BorrowableTemporary` (`Binding.ArgumentOperations`); related: P34_REMAINING R3 | — |
| C4f | Borrow of a mutable static storage key (P36) | `InvalidAssignment_Kd` | `AccessFailure` (`Binding.ArgumentOperations`) | §15.6.2, §22 |
| C4g | `open` as an ordinary Name (`let open = 2`, `file.open(...)`); checked: `TokenKind.Open` is in the reserved range (`TokenKind.cs:49`, below `Alias = 96`) | syntax error | lexer token class and the modifier position | §2.5.1 (contextual keywords) |
| C4h | `task;` as the task argument or task slot (owner decision 2026-10-12: a location-only `Unsupported_Kd` at the task slot; Appendix D.5.2 keeps tasks specified and defers only their implementation) | `SemicolonNotAllowed_Kd` | tokenizer `;` rule and task-slot recognition | §2.4, §24.2 |
| C1m | `@raw` of a Range-kind, Never or other nominal Place; `@ref`/`@uniq` of the same fall to `Unsupported_Kd` (checked: `BorrowsWrittenSlot`, `Binding.Conversions.cs:253`) | `InvalidAssignment_Kd` | delete the Type allowlist (widens acceptance) | §13.5.5.2 |

C4a is also P33's flow refinement (PLAN §4). If P33 implements it first, the row closes; the stage-baseline row for Program 33 changes with it. C4c (omitted Origins in constructions, F22) is tracked as G10 in PLAN §7.

## 3. Invalid or unsettled forms reported as `Unsupported_Kd`

Target: the Language code of the violated rule. Confirm the clause first.

| ID | Form | Rule to apply |
| --- | --- | --- |
| R1g-1 | `self.helper()` on a construction receiver | the construction-receiver rule R1g-C applied to Field and Property access (C1a, SPEC §6.2.3.4) |
| R1g-2 | `S.v` for an instance Property | instance-only Property access, as R1g-C applied to a Contract Property requirement (C1l, SPEC §11.4, `UnresolvedBinding_Kd`) |
| C3f | Unknown attribute on a test function: `Unsupported_Kd`, while unknown attributes elsewhere get `InvalidTypeFormation_Kd` | SPEC §6.5: one code for an unknown attribute |
| C1p | `let v = make().0` with a Non-Copy part is `Unsupported_Kd`, while `make().0@move` is `StaticMovePathRequired_Kd` | decide from SPEC §3.4 and §15.1.5 whether a temporary's part is a movable Place, then make both forms agree |

## 4. Failures reported again or in the wrong partition

Target: rest on the cause (derived), or Internal where unreachable. R7's Problem and Error propagation is the natural owner: check each row in R7's design, and if R7 covers it, the row leaves this document.

| ID | Site (at R1) | Change |
| --- | --- | --- |
| C3a | Base clause whose Type is unbound or whose stored Type failed: `selection.Pending` → `Unsupported_Kd` (checked: `Binding.BaseCalls.cs:89`) | depend on the base clause's failure |
| C3b | `FailConstructorAbsence` (`Binding.Structs`) joins three cases: `T.init()` under an identity premise (keep `Unsupported_Kd`), `type is null` (depends on the qualifier) and Explicit/Eligible without a decision (Internal) | split the condition |
| C3c | Constraint proof Error or Refuted reported as `UnprovenConstraint` (Proof) in `Binding.Calls` and `Binding.PropertyCalls.cs:211` | derived; Unknown → `Unsupported_Kd` stays (OCC-X) |
| C3d | A capture rejected by the closure capture allowlist may publish `Unsupported_Kd` and `InvalidCaptureBinding_Kd` together | reproduce; derive the Language record |
| C3e | `FailedOriginRelation` between finite body Origins → `Unsupported_Kd` | Internal only if shown unreachable; otherwise keep (low confidence) |

## Appendix A. Disposition of the R1 inventory

The R1 working inventory (written at `fb7a7ba5` for R1g; N and NEW ids from `artifacts/verify/reduction-R0/narrow-*.md`) existed only in a temporary folder. Dispositions at `fcb190f4`:

| Items | Disposition |
| --- | --- |
| N1, N2, N4, N5 form 4, N7, the N8 table, N12, NEW-C2–C5, NEW-F4–F5, NEW-B2-1, L4, OCC-X waiting machinery, function-reference gates, renames, control-flow types, Advice and Notes | done (R1a–R1f) |
| A1–A11 (A5 and A7: sites that test input reaches stay `Unsupported_Kd`) | done (R1g-A `3f67d2ed`) |
| A12 measurement hooks | R6/R7 (R1d `3d0a770b`) |
| A13 one case limit | done (R4 U0a `7f1e6317`, DECISIONS R4-M18) |
| B1 (N16) | done (`fef83a63`); a D2 regression recorded in STATUS and PLAN |
| B2 (N5 form 3) | kept as a general rule (`2a6655ee`); reopen only on owner instruction |
| C1a–b, C1c (deleted), C1d, C1f–g, C1i–j, C1l, C1n–o; C1q–s (`Unsupported_Kd`); C2a, C2d–f; C5a | done (R1g-C `61482071`) |
| C1e, C1h, C1k | no change: not invalid, or not reached (R1g-C) |
| C2b | not reproducible at `fef83a63`; its DIAGNOSTICS finding was removed |
| C2c | old ownership analysis: R4 ledger, deleted with the old path in R6 |
| C3h, D4, C5b (= D5) | R8, R7, R6 |
| D1–D3 (N8 constant fold, N9 Sealed direct release, N13 allowlist) | old paths, frozen (D5) and deleted in R6. The object-finalization timing (pre-R5 B1) shows whether the new generator needs an equivalent of N9 under R5's P1 gate. |
| D6 (L15–L21), D8 | MIR analyses and R7 generalization; L21 needs a SPEC decision (R4 ledger category (c) if it surfaces) |
| D7 stale documentation | pre-R5 D2 |
| C1m, C1p, C3a–g, C4a–b, C4d–h, `self.helper()`, `S.v` | this document |
| C4c | PLAN §7 G10 |
| G87, R0 timing baselines | pre-R5 §4, §2 |
