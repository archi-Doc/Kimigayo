# 2c-D design review (full process): C2 and A5-F7 under R4-M99 / R4-M123

Reviewer slot, 2026-10-25. I wrote neither design. Review only: no repository edit, commit or push. Base `origin/dev` `038f12ed4`
(R4-M123's SPEC on dev); throwaway worktree `bin/worktrees/2c-d-r` (removed at the end, junction unlinked first). Read: both designs
and their probes, `brief-design.md`, `review-brief.md`, `s2-common.md`, AGENTS, SPEC §3.5.1, §7.7.1–§7.7.4, §15.3.4, §15.4.2–§15.4.3,
§15.6.3–§15.6.5, §15.8.2, DECISIONS R4-M99, R4-M123, BU-28 (with its F3 note), R4-M118 (on `claude/a5-2a-1c`), R4-M119, R5-M102, OQ4,
the A5 step 2 plan's rows 2c-D … 2c-7, PD6, PD13, PD29, AR-2, AB-2, PLAN G65/G147/G216/G229, 2x-P7's record, spec-closure's design and
review (n1, n2b), and the code each design names. My probes are in `probes-r\` (§12). "(H)" marks a claim no run checked.

## 0. Verdict

**Neither design is accepted as written; recommend a merge with design B as the base.** Each has blocking findings (§9): A judges the
receiver Origin before the receiver is known (false rejections of SPEC-valid Consuming bodies, three of them accepted on dev today) and
keeps two classes for an environment binding's atom; B misreads G216 against SPEC 15.3.4 and narrows the OQ4 gate below R1, which my
probe shows lets the old Analysis accept the SPEC-invalid t5b and n1. Both miss R4-M123's nested-entry meet (n1/n2b), which becomes a
live unsound acceptance the moment 2c-5 lifts R1.

The merge (§10): B's receiver resolution (containment meets ρ, one `OriginAtUse` row, pending atoms judged at classification, no
builder change, receiver at input 0, `HoldsLoans` by captures, the old Emission follow-up, the reserved-slot role test); from A the
G216 ruling (2c-9), the gate's predicate (R1's exact condition, `ResultFixedForCall` kept to R6-check) in B's program-level shape,
PD13's per-case representation and view hold, the result record at the returned value with `CallLocalOrigin` deleted (owner option),
the file split of 2c-0 and the `x3` test; and new: the nested-entry binding Type (n1/n2b) before 2c-5.

## 1. The problem and the general rule

R4-M99 / R4-M123: a Closure body is checked against its call receiver as a method body is against `self`; its environment bindings are
Fields of the receiver's referent. So every Origin, containment and retention decision is a receiver rule whose owner already exists
(PlaceOrigins' walk, OriginClasses / `OriginAtUse`, `TyCtxt.Retains`, MirBorrows' W). The closure-specific remainder is three stated
rules: receiver selection per case (7.7.3), permissions (7.7.2, D1) and result elision without the receiver (15.8.2 ¶3). One timing
fact is closure-specific and both designs meet it: **the receiver is classified from the bound body** (`Binding.Closures.cs`: body
bound at `:368-391`, `ClosureEffects.Classify` at `:430`), so any judgment whose answer depends on the receiver (ρ fixed or `static`,
an environment atom contained in ρ or body-local) must wait for classification. That is the general rule a correct design implements.

I tried a shorter form than B's two names (env_i and ρ): name every Borrow in the body by the binding's atom only and resolve it per
receiver. It fails: under `owner/Self` a Borrow of a binding is body-local while a Reborrow through a captured `uniq` is contained in `o`
only, so one atom cannot carry both; two names with one resolution is the minimum. Re-binding the body once the receiver is known (B's
option E) doubles closure binding. So B's pending-and-judge-at-classification is the shortest correct mechanism I found.

## 2. Comparison on the design chain

| Step | A | B | Assessment |
| --- | --- | --- | --- |
| Required meaning | §1 table: per receiver, D1, D2, F3 (b) | §1 table, same; t4g stays an error | Equal and correct, except both omit R4-M123's nested-entry meet (B1) and B misreads G216 (B4) |
| What the representation lacks | L1–L12; misses the reserved-slot tests and the old Emission check | L1–L12 incl. reserved-slot tests (L12) and the Emission check (round 7) | B's list is more complete (N2, N3) |
| Owner of the receiver-dependent class | Binding's walk meets ρ; premise ρ == static after classification; view row 4 per receiver; MIR builder types the receiver local; judge keeps env_i body-local | Walk meets ρ; one `OriginAtUse` row resolves env_i → ρ (borrowed) / ρ → static (Consuming); pending until classified; judged at classification; judge and view share it; no builder change | A has two classes and judges too early (B2, B3); B has one owner and the right timing |
| Retention (C2) | `TyCtxt.Retains` + receiver at input n, D1 one walk row | same, receiver at input 0 | Same mechanism; position 0 matches methods and R5-M102's ABI (N7) |
| Stores (A5-F7) | W, rule G, P13 hold, temporary rule deleted, `HoldsLoans` by captures | same | Equal; both prototypes give SPEC on G1's forms |
| Result contract | formed after classification; finite sources → `ResultFit` at the returned value judged by Binding (both pipelines); `CallLocalOrigin` deleted; no source → `static` | formed after classification; finite sources: default keeps `CallLocalOrigin` (header), MIR at the returned value; no admissible source → CallResult, Unbound at the call | A's location is SPEC's on both pipelines (owner option OQ-F7-2); the no-source corner needs both halves (N5) |
| Value call | substitute ρ by the receiver Origin; R1 goes in Binding | same | Equal |
| Gate (OQ4 (a)) | per call in `CallValue`, R1's predicate as a Transitional fact, `ResultFixedForCall` kept | program-level, predicate "names ρ", `ResultFixedForCall`'s Closure branch deleted | A's predicate, B's shape (B5, N1) |
| PD13 | capture bit + `TyCtxt.ClosureReceiverIn(case Type)`; view hold | case word over pair binders, renumbered by the view; witness test at the conflict (PD13's letter) | A's representation is local (no renumbering); hold vs witness is an owner item, census 0 for both (H) |
| G216 | not formed in the evaluating body; body assumes, each call proves (valid) | §15.4.2 error, `MissingOriginBinding_Kd` | A is right: SPEC 15.3.4 (B4) |
| Deletions | temporary rule, P4's clause, R1, `CallLocalOrigin` (+ `LocalBorrow*`, see N8), row 4 interim, G216 limit, `HoldsLoans` opacity, S3 formals | temporary rule, R1, `HasEnvironmentOrigin`, `ResultFixedForCall`'s Closure branch (wrong, B5), row 4 interim, G216 limit, view/builder plans | Merge: §10 |
| Size (H) | +250/−170, t+600 | +295/−190, t+530 | Merge about +300/−150, t+600 (H) |

## 3. Soundness

- **G6 / g6a** (live on dev's MIR: a use-after-free through `[p@move, q@move]`): both meet ρ when the walk reaches an environment
  binding through a dereferenced layer, so `p[0]@uniq` is `meet(o_p, ρ)` and the store into `*q` is Unknown. B's round 10 shows g6a,
  g6b, g6d, g6e `UnprovenOriginRelation_Kd` on both pipelines and g6c (Consuming, valid) unchanged. A's walk gives the same name but
  judges it before classification, so g6c becomes a false error (B2). Since g6a is live, the unit that adds ρ (2c-3B) goes first.
- **Stores** (f7a–f7d, c-f7c2, c-f7d2, f7o4 errors; f7a2, f7o, x3, G229's class S valid): both prototypes reach SPEC's verdicts with
  the receiver input, D1's `let` row, W and `HoldsLoans` by captures; f7n1 is held by P13 (until G148 (1)). Rule G's self-store row
  rejects c-f7c-ok / v4 and f7h2's uniq view by SPEC 15.6.4's per-argument letter (OQ-F7-3).
- **Containment and results**: borrowed receiver: a Borrow of a binding contained in ρ, a Reborrow through a captured `uniq` in `o` and
  ρ, through a captured `ref` in `o`; Consuming: body-local, `o` only. B's B3+B4 give exactly this (round 10: r1e, r1f, t4g, g65a, r1g
  Unpr; t4a, t4e, t4f valid). A gives the same names but the Consuming half only after classification, which Binding's fit judgment
  (`BindNodeCore → FitsTypeAt → RecordOriginFitFailure`) does not wait for (B2).
- **Nested closures**: f7j1, rc4 stay valid in B's round 10. **n1/n2b are not covered by either design (B1)**: a bare entry's binding
  Type is its source's (`Binding.Captures.cs`, `environment.Type = type`), so an inner `[u]` of an outer binding names only `o_u`, not
  `meet(o_u, ρ_outer)` (SPEC 7.7.2 as R4-M123 words it). Once 2c-5 removes R1, `outer()`'s result does not depend on its receiver, a
  second `outer()` succeeds and `pa`, `pb` are two live `uniq` paths to `x` (spec-closure's review §"The nested case matters"; my
  `gate-narrow.txt`: n1 accepted on both pipelines with R1 narrowed).
- **Pass-through Dependent (R4-M118)**: B's "CallResult becomes Unbound at the call" is a new source of `Unbound`, acceptable as an
  R4-M118 row ("a later site adds a row under it") only on a failure path: a sourceless position in a valid program would be a Dependent
  atom in a complete compilation, which R4-M118 (F) publishes as `InternalInvariant_Kd`. A's "no source → `static`" is right for a
  sourceless position and wrong on the failure path (an invented `static` resting on a failure, SPEC 15.4.4). Merge both halves (N5).
  B's pending atoms of a Closure Binding never classified must be Dependent in the view (N4 (c)).
- **Default pipeline**: B's narrower gate makes the old Analysis accept t5b and n1 (B5). A's gate keeps R1's predicate and is sound.

## 4. SPEC conformance

- **D1** (`let` bindings not writable): both, one walk row (f7o valid, f7o4 error). **D2**: both; A puts Binding's record at the
  returned value on both pipelines (SPEC's location), B keeps the default's header record until H11 (OQ-F7-2).
- **F3 (b)**: t4a, t4e valid (B round 10; A regresses them, B2); **t4g stays an error** in both, by 15.6.3's receiver containment, as
  R4-M123 states. **The nested-entry meet** R4-M123 assigns to 2c-D ("the entry's meet (n2b …)") is in neither (B1).
- **G216 ruling: h1 and h2 are valid.** SPEC 15.3.4 "Nested signatures": "in a local's initialized Type and an anonymous function's
  parameters an omitted one, or an omitted Origin of a nested borrow layer, is a body inference variable, fixed once". 15.4.2's "an
  omitted Origin of a borrow layer nested in an input is an error" is the general rule for signatures, which 15.3.4 excepts for anonymous
  functions; B's named-function probe (`func g(p: ref/(ref/i32))` → `MissingOriginBinding_Kd`) is consistent with that. 2x-P7's reading
  and A's 2c-9 stand: the header's well-formedness is a premise of the Closure body and an obligation of each call, never a row of the
  evaluating body. B's 2c-H would reject every such header (B4).

## 5. Symptom-fix check

- A: view row 4 classed per receiver beside the judge's body-local class, with a builder change to feed the view: a second copy of a
  class rule, against `Binding.OriginClasses.cs`' contract ("each class is decided here once, by one predicate that every reader
  shares") and BU-28's one rule (B3). A's per-call gate lets the old Analysis judge the rest of a body it never judged (N1).
- B: the old Emission helper strips ρ only from a two-operand meet (a shape match; a meet of three operands still fails) (N2). The
  narrower gate is a wrong result, not a symptom fix (B5). B's pending class is a timing rule with one owner and a removal condition
  (classification), not a fallback.
- Both: no `Supports*` gate, side table or try-then-fallback found; `ResultFixedForCall` kept to R6-check is a Transitional input of
  the D5 gate with its stage, not a second rule.

## 6. Default-pipeline changes and D5

OQ7 (a) census rows of the merge (per unit, H until each unit's census): 2c-1 (G147 (3)) per its row; 2c-3B: r1e, r1f, t4g, n2b
rejected (SPEC 15.6.3, 7.7.2), g6a, g6b, g6d, g6e Loan (and g6e's CallActivation) → `UnprovenOriginRelation_Kd` (the valid g6c keeps
the old path's conservative Loan record), g65a, r1g,
§15.8.2's `invalid` `Unsatisfied…` → `Unproven…` (the spec-cases (a) difference closes); 2c-9: none (h1, h2 already accepted on the
default; the MIR's located U goes); 2c-5: under OQ-F7-2 (a) the Refuted inferred-result records move to the returned value and F2's
derived `NotCallable_Kd`/`UnresolvedBinding_Kd` go; Binding records downstream of calls R1 failed newly appear on both pipelines
(SPEC 23.1.2; N11); the gate keeps every R1 record's code and span. No library or milestone program is expected to change (H; both
prototypes ran `MirCensusTest`). D5: the gate (OQ4 (a)); the old Emission's element-borrow Origin check (`BodyLowering.Sequences.cs:137`;
`:164` compares two such Origins and is unaffected) as a mechanical follow-up the package names, until R6-gen (N2). The program-level
gate analyzes nothing else in a gated program, so the old Analysis sees no new form.

## 7. Size, speed, V16, warm

Merge: production about +300/−150 (Binding about +210/−130, MIR +65/−10, old Analysis +15, Emission +5), tests about +600 (H). No file
crosses a limit (`Binding.Closures.cs` 4,420 tokens shrinks after 2c-0; `Binding.FunctionTypes.cs` does not grow, the plan's 2c-5
condition). Speed: one meet per Borrow through a captured reference; one row in `OriginAtUse` (hot: B's pinned warm Bind A/B over
Milestones 1, 20, 33 in 2c-3B); the classification judgment over the closure's obligation range (linear; B's prototype scanned all
obligations); one more Value-call input, memoized. Warm Bind and warm MIR 0 B (A's 288 B was its prototype's environment read). V16
"F7 captures" (N captures × M calls, 2× and 4×) in 2c-3.

## 8. Reviewers

BU for every Binding part (2c-0, 2c-1, 2c-2, 2c-3B (BU implements under PD6: containment and classes are acceptance rules; R4
reviews the view part), 2c-9, 2c-3's Binding commit, 2c-5) in parallel with R4's A5 reviewer (R4-M119). R5 (AR-2) for 2c-2 and 2c-3:
the receiver position and the contract shape C8 and Value-call code generation share. With no builder change, the merge needs no W1
slot. 2c-4 is BU's with R4's review (AB-2).

## 9. Findings

### Blocking

- **B1 (both) — the nested-entry meet is missing; n1 becomes unsound when R1 goes.** R4-M123 assigns 2c-D "the entry's meet (n2b: both
  pipelines accept a nested entry's `during u` that SPEC rejects)"; BU-28's note says `EnclosingAtom` does not give it. Neither design
  changes the bare entry's binding Type (`Binding.Captures.cs` `Capture`: `environment.Type = type`; `CaptureEntry` keeps it for a bare
  Reborrow). n2b stays accepted on both pipelines (`gate-base.txt`); n1 is accepted on both once R1 is narrowed (`gate-narrow.txt`) and,
  by reading, on either design's MIR after 2c-5 (the result names `o_u`, a Content row, no receiver Loan). Fix (2c-3B, BU): a bare entry
  whose source is an enclosing Closure's environment binding takes the Reborrow child's Type, its Origin the walk's `meet(o, ρ_outer)`
  (`o` under a Consuming outer by B4's resolution), as R4-M123's 7.7.2 states; done before 2c-5. Tests: n1 a Loan conflict at the second
  `outer()` on the MIR after 2c-5 (the default's gate U before), n2b `UnprovenOriginRelation_Kd` in the inner body on both pipelines,
  f7j1, rc4, s773 unchanged.
- **B2 (A) — ρ is judged before the receiver is classified.** A's 2c-8 makes ρ fixed in the Closure's own body from the start and supplies
  `ρ == static` for `owner/Self` as a premise read "after classification"; Binding judges a fixed-fixed fit at binding time, before
  `Classify`. So `return u` (a bare Reborrow, `meet(o_u, ρ)`) in a Consuming body is Unknown: t4a and t4e (SPEC-valid by F3 (b)), t4f
  and g6c (valid, accepted on dev: a regression). Evidence: B's round 3 with the same names and no deferral
  (`probes-b\results-proto3.txt`: t4e, t4f `UnprovenOriginRelation_Kd@L8 return u`, g6c `@L6`, t4a `@L10`); A §15 leaves it (H). Fix:
  B4's pending atoms judged at classification.
- **B3 (A) — two classes of an environment binding's atom.** A keeps `Binding.OriginClasses` reading env_i as body-local (the class the
  judge and the old Analysis's reporter read: g65a, r1g and §15.8.2's `invalid` stay `Unsatisfied…` on the default, dev's old-reporter
  record) while view row 4 makes the same atom Inferred under a borrowed receiver, bounded through a new MIR receiver Origin (2c-6b, a W1
  file), so the mir pipeline says `Unproven…`. That is a second copy of the class rule, against `Binding.OriginClasses.cs`' contract
  ("each class is decided here once, by one predicate that every reader shares"; BU-28, R4-M101 E5), and it keeps G65's pipeline code
  difference to R6-check. B's single resolution gives one `Unproven…`, Binding's record, on both pipelines (round 10) with no builder
  change.
- **B4 (B) — G216 ruled against SPEC 15.3.4.** 2c-H would report `MissingOriginBinding_Kd` for SPEC-valid h1, h2 and every Closure
  header with an omitted nested borrow Origin (§4). Take A's 2c-9.
- **B5 (B) — the OQ4 gate is narrower than R1.** B's predicate ("a value call's declared Closure signature names its ρ") misses R1's
  `dependent` half (`ResultFixedForCall`'s capture scan: a result carried by a Non-Copy capture), which B also deletes. Those calls then
  bind and the old Analysis runs: with R1 narrowed to environment-naming results (`probe-narrow-r1.diff`), the default **accepts t5b**
  (SPEC: error at `y = 5`, R-C) **and n1**, and newly accepts t4a, t4b and t5 (D5: no new accepted form; OQ4 (a): keep the verdict).
  Fix: A's predicate (R1's exact condition, `HasEnvironmentOrigin` reading ρ or neither per-call nor `ResultFixedForCall`, published by
  Binding as a `[Transitional("R6-check")]` fact on the value call's plan; `ResultFixedForCall`'s Closure branch kept as its only input)
  in B's program-level shape (§10).

### Non-blocking (each becomes a condition of its unit or an open issue)

- **N1 (A)** The per-call gate in `CallValue` lets the old Analysis run on programs it never analyzed (Binding was incomplete), so
  records change: with R1 narrowed, t3b (SPEC-valid) gets `ComparisonLoanConflict_Kd` at `a` (`gate-narrow.txt`), so A's "snap
  (default unchanged)" would not hold (H for A's exact gate). B's shape (report at each gated call, analyze nothing else) keeps R1's
  records exactly; only the partition changes.
- **N2 (both)** The old Emission's element-borrow check (`BodyLowering.Sequences.cs:137`, `sameOrigin`) fails generation once Binding meets
  ρ (B round 7, `ClosureEnvironmentOriginTest`'s ElementBorrow fixture); A's 2c-8 hits it too, unlisted. Name it in the package as a D5
  follow-up until R6-gen, compare modulo every receiver operand of the meet (not B's two-operand shape), and run the closure native
  families on both generators in 2c-3B.
- **N3 (A)** `Slot <= EnvironmentSlot(0)` reads ρ as an environment binding once ρ is produced: `Binding.CallableProofs.cs:22`,
  `Binding.Diagnostics.cs:243`, `Binding.Closures.cs:87` (under Consuming ρ would be call-local) and `Binding.ValueCalls.cs:243`. One role
  test (`RoleOf`), B's L12.
- **N4 (B)** Pending atoms need four stated rules and tests: (a) `Classify` runs on every path that bound the body — `Binding.Closures.cs`
  returns at `:393-396` and `:408-410` before `:430`, so a failed result or parameter Type would leave pending relations unjudged and lose
  an independent record (SPEC 23.1.2; classify before those returns); (b) a relation naming several Closures' pending atoms is judged at
  the outermost one's classification (n1, n2b, f7j1, rc4); (c) an unclassified Closure's pending atoms are Dependent in the view (an
  R4-M118 row), tested beside an unrelated failure; (d) a consultation's (`BeginConsultation`) obligations leave the range with it.
- **N5 (both)** Result formation: a position with no source is `static` (A); a position whose every source was left out (Refuted) is the
  CallResult atom, Unbound and Dependent at the call (B, an R4-M118 row); never `static` on a failure path (A) nor Unbound in a complete
  compilation (B). Today `ResolveLocalResultOrigins` leaves a sourceless region unresolved (`Binding.LocalRegions.Types.cs:172`).
- **N6 (B; A to state)** The Value-callee memo key `(signature, owner)` must include the callee's instance Closure Type: the receiver
  input's referent can differ between generic instances whose signatures are equal. A test: a generic closure whose capture's Type is T.
- **N7** Receiver at input 0 (B): `MirCallee.Position` already maps a method receiver to 0 and R5-M102's ABI passes the environment
  first; A's input n is a second convention. AR-2 confirms.
- **N8 (A)** `LocalBorrow` / `LocalBorrowFinder` are read by `Binding.Diagnostics.BorrowSource` (`:375`); they cannot go with
  `CallLocalOrigin`.
- **N9 (B)** R4-M119 allows one shared-representation change at a time: 2c-3B (Binding's published Origins) and 2c-3 (`TyCtxt.Retains`'
  inputs) are not parallel; 2c-3B first (it closes the live g6a).
- **N10 (B)** 2c-0's one `Binding.ClosureContracts.cs`, to which 2c-1, 2c-2, 2c-3B and 2c-5 add, would hold receiver classification,
  result formation and C2's facts; A's split by responsibility (`Binding.ClosureReceivers.cs`, `Binding.ClosureResults.cs`) fits the
  Structure rule.
- **N11 (both)** After 2c-5 Binding completes calls R1 failed, so Binding records downstream of them appear on both pipelines; 2c-5's
  snapshot classifies them as SPEC 23.1.2 records, not gate leaks.
- **N12** PD13: A's capture bit and `TyCtxt.ClosureReceiverIn(case Type)` need no binder renumbering (B's case word does); use it with
  either located U. B's claim that the strongest receiver can err "in either direction" is unshown; by reading it is conservative (H).
- **N13** Tests to merge: A's `x3` (dev's MIR false conflict from P4's clause), B's v1–v4 and g6a–g6e, 2x-P7's forms, n1/n2b.
- **N14** The MIR halves (2c-6, `ResultFit`) wait for G:A5-F4 (2a-1a–2a-2 are not on dev; `TyCtxt.Origins.cs:19`).

## 10. Recommended package (merge)

| Unit | Lane | Change | Depends | Review | Hold |
| --- | --- | --- | --- | --- | --- |
| 2c-0 | B | bp: `Binding.ClosureReceivers.cs` (7.7.3) and `Binding.ClosureResults.cs` (15.8.2) with contracts (A) | — | R (BU) | BU |
| 2c-1 | B | G147 (3), the plan's row | 2c-0 | R (BU) | BU |
| 2c-2 | B | C2's facts, publication only: the per-case receiver (capture bit + `TyCtxt.ClosureReceiverIn`, A), `Classified` (B); warm 0 B | 2c-1 | D+R (BU, R5) | BU |
| 2c-3B | B | B's B3+B4 (walk meets ρ; `OriginAtUse` row; pending atoms judged at classification with N4), the nested-entry binding Type (B1), the role test (N3), the old Emission follow-up (N2); closes G6 (g6a), G5's codes, n2b; OQ7 (a) census; pinned warm A/B; closure native families both generators | 2c-2 | D+R (BU implements, R4 reviews) | BU, D5 |
| 2c-9 | B | G216 by A's §3.9 | — | R (BU) | BU |
| 2c-3 | C | `TyCtxt.Retains` with the receiver at input 0 and D1's row (memo key N6); MirBorrows input 0, W, arity, temporary rule and P4's clause deleted; `HoldsLoans` by captures; PD13 per OQ-F7-1 (iii); ER-closure-stores; V16; +led | 2c-2, G:A5-F4, after 2c-3B (N9) | G:A5-F7 (Binding part BU in parallel; R5 AR-2) | adv, BU, sh |
| 2c-4 | C | BU's 0c from 2c-3's branch (AB-2) | 2c-3 | BU's R + G:A5-F7 | BU |
| 2c-5 | B | Result formation after classification (N5), `ResultFit` rows at the returned value judged in the classification pass, `CallLocalOrigin` per OQ-F7-2; ρ substituted at value calls; R1 deleted in Binding; the program-level gate with R1's predicate (Transitional; `ResultFixedForCall` to R6-check) | 2c-3B, 2c-3, B1 landed | G:A5-F7 (BU in parallel) | BU, D5 |
| 2c-6 | C | MirOrigins judges the result rows; view rows 4/5 by the resolution | G:A5-F4, 2c-5 | G:A5-F7 | — |
| 2c-7 | — | Records (§11 of each design, merged) | all | G:A5-F7 | — |

Deleted by the merge: the temporary rule and A3 P4's Closure clause; R1 in Binding; `HasEnvironmentOrigin`'s failure use (it stays as
the gate predicate's reader until R6-check); row 4's interim; G216's pinned limit; `HoldsLoans`' Closure opacity; the plan's
`TyCtxt.Closure` view, `TyCtxt.Closures.cs` and MIR-builder receiver Origin; the S3 plan's result formals; under OQ-F7-2 (a)
`CallLocalOrigin`. Kept to R6-check: the gate and `ResultFixedForCall`'s Closure branch. Records: DECISIONS (the approved package
superseding plan 2c-2's row, R4-M99 §6's D14 sentence, 2a0-D row 4's interim and D19's "Finite until 2c-6", the S3 formals; per-unit
entries; the G216 ruling by SPEC 15.3.4; the nested-entry exception to BU-28's "source's Type"; the D5 items), PLAN (G65's G6/n2b
rows, G147 (3), G216, G229, F1/F2), STATUS, CODEMAP.

## 11. Owner questions (final form)

| ID | Question | Options | Recommendation |
| --- | --- | --- | --- |
| OQ-F7-1 | Approve the merged package (OQ2) with its departures from owner-approved texts: (i) no `ClosureContract` / `TyCtxt.Closure` view and no MIR-builder receiver Origin: C2 is `BoundClosure`'s facts, `TyCtxt.Retains` with the receiver as input 0 and Binding's receiver resolution with judgment at classification; (ii) A3 P4's Closure clause retired (ER-closure-stores); (iii) PD13's located `Unsupported_Kd` as R4-M73's view hold whenever a call's acquired receiver is stronger than its view's case receiver; (iv) S3's result formals not built (S3-0's gate R moot); (v) the old Emission's element-borrow Origin check compares modulo the receiver Origin until R6-gen (D5 follow-up); (vi) the OQ4 gate is program-level with R1's predicate, `ResultFixedForCall` kept as its input until R6-check | (a) approve all; (b) approve with (iii) as PD13's letter: a per-Loan "stronger than its case" fact and a witness test at the conflict (+40 lines, a new fact); (c) approve with other per-item changes | (a): (iii)'s hold is 3 lines over an existing mechanism, keeps every such program rejected and no library, milestone or 2x-P7 program reaches it (H) |
| OQ-F7-2 | Where an inferred Closure result's Refuted source is reported on the default pipeline | (a) `CallLocalOrigin` deleted in 2c-5: Binding's record at the returned value on both pipelines (SPEC 15.8.2, D2; G7 closes on both; default locations move, classified under OQ7 (a)); (b) PD13's letter: `CallLocalOrigin` stays until H11 at the Closure header on the default (the Closure keeps its Type, F2 closes), the MIR at the returned value, a (b) ledger row to R6-check | (a) |
| OQ-F7-3 | Rule G's per-argument self-store row reaches Closures: `[holder@uniq]` storing into the referent and then read (c-f7c-ok, v4) and f7h2's uniq view are rejected on the MIR, f7c and f7d get a second record, as dev's MIR already does for the method analog | (a) one rule with methods (SPEC 15.6.4's letter); GXS (G148 (2)) stays unscheduled; 2c-3 reports the ledger count and the planning session returns if library, milestone or corpus forms appear; (b) schedule GXS before R4's exit (full process, a SPEC 15.6.4 sentence); (c) a located `Unsupported_Kd` for these forms until GXS | (a) |

Decided elsewhere, not asked: D1, D2, F3 and t4g's error (R4-M123); the nested-entry meet (R4-M123's 7.7.2, an implementation item:
B1); G216 (SPEC 15.3.4: valid, answering B's Q-B); B's Q-A (t4g, already R4-M123's); OQ4 (a); B's OQ-B1 is OQ7 (a) with the per-unit
census (O3 only if a milestone or library program changes); B's OQ-B2 is OQ-F7-1 (i); A's OQ-F7-1 items (iv) and (v) are OQ-F7-2 and
(vi) here.

## 12. Probes and what remains unverified

- `probes-r\` (README there): `gate\` (t1a, t3a, t3b, t4a–t4c, t5, t5b, f7e1 from probes-b; n1, n2b from spec-closure), `gate-base.txt`
  (dev as is) and `gate-narrow.txt` (`probe-narrow-r1.diff`, `KIMI_REVIEW_GATE=1`: R1 kept only for results naming the callee's
  environment, B's predicate for every form whose design-B result names no ρ), run with 2x-P7's `ScratchProbeP7Test` (Release build of
  `tests/xUnitTest`, two builds, no class run).
- Used as evidence without re-running: B's rounds 3 and 10 (`probes-b\results-proto3.txt`, `results-proto10.txt`) for B2 and §3.
- Unverified: the merge as a whole (no prototype); n1 on either design's MIR after 2c-5 (by reading, with spec-closure's review); A's
  per-call gate's exact extra records (N1, by the narrowed probe); B's pending rules for nested and failed
  Closures (N4); the PD13 hold's reachability; censuses, warm A/B and V16 of every unit.

## Re-check of the merged package

Re-check of `package.md` (2026-10-25) against this review, the owner's answers (OQ-F7-1 「(a) すべて承認 (推奨)」, OQ-F7-2 「(a) 削除する
(推奨)」, OQ-F7-3 「(a) メソッドと同じ規則 (推奨)」) and dev `937fd8148`, by reading (the lines cited were checked on `937fd8148`).

**Verdict: ACCEPT WITH FIXES.** B1–B5 are resolved, the owner's answers are applied, nothing departs from the approved direction; F1–F6
below are text changes to `package.md` before the planning session commits R4-M126 (none changes a unit's direction; F1 adds a
requirement and a test to 2c-5's D+R and is the only substantive one).

### Blocking findings resolved

| Finding | Where the package resolves it | Check |
| --- | --- | --- |
| B1 nested-entry meet (n1/n2b) | P4, §2.3 (`CaptureEntry`: a bare Reborrow of an enclosing Closure's binding takes `meet(o, ρ_outer)`; `o` under a Consuming outer by P5); 2c-3B before 2c-5; tests n1, n2b, f7j1, rc4, s773 | Resolved; deeper nesting composes (the inner binding's Type already holds `meet(o, ρ_outer)`, the next level meets `ρ_inner`). See F1 for the call side |
| B2 ρ judged before classification | P5, §2.5 (pending atoms fitted as local regions, judged at classification); D-9 moves `Classify` before the early returns | Resolved. Pending is a class in `OriginClasses` (neither fixed nor finite), so every judge, not only the fits, routes such a relation to an obligation through `BodyChain`. See F2, F3 |
| B3 two classes of env_i | P5's one `OriginAtUse` row; no row-4-per-receiver, no builder change, no W1 slot | Resolved |
| B4 G216 | P10, §2.15 (15.3.4); B's 2c-H listed as rejected | Resolved |
| B5 gate narrower than R1 | §2.13: R1's exact condition (`dependent` or `HasEnvironmentOrigin` reading ρ), `ResultFixedForCall` kept whole, program-level after the completeness check | Resolved. Over 2c-5's published result the condition selects the same calls: `FixedInBodyOrigin` is false for ρ and for the CallResult atom, so `dependent` covers both (`Binding.ValueCalls.cs:248-250`), and a capture-carried Origin fails `ResultFixedForCall`. See F4 |

The owner's answers: (i) P1, §2.1, §3; (ii) §2.10 ER-closure-stores; (iii) §2.10 PD13's view hold with D-1's facts; (iv) §3; (v) P11,
§2.14; (vi) §2.13; OQ-F7-2 (a) P8, §2.6, §3; OQ-F7-3 (a) §2.10 with 2c-3's count. N1–N14 are each taken up (N1 §2.13, N2 §2.14 and
D-15, N3 §2.12, N4 §2.5, N5 §2.6, N6 D-7, N7 §2.8, N8 D-8, N9 §4, N10 2c-0, N11 §5, N12 D-1, N13 the test lists, N14 §2.11).

### Fixes (text in package.md before R4-M126 is committed)

- **F1 (2c-5, P9): a Closure Type substituted inside a result needs a consistent call signature.** §2.7 substitutes ρ "at any position
  (a Closure Type's environment components included: n1)". `SubstituteStoredOrigins` rebuilds a Closure Type's components but keeps its
  `ClosureContext` (`Binding.Storage.cs`, the `Intern` at the end of the walk), and `ClosureSignature` (`Binding.ClosureInstances.cs:13`)
  returns the definition's `Signature` when there is no context. After P4 the inner Closure's result names `ρ_outer`, so `a@move()`'s
  result still names `ρ_outer` in `main`, where it is the outer literal's temporary (row 5) or another body's atom. n1 stays rejected
  (`a` carries `outer`'s receiver Loan), but a valid variant that fits the inner call's result to a fixed Origin can get Binding's false
  Refuted (its judge reads another body's Projection as body-local) or a row-9 located U on the MIR. The same applies to an inner
  `[x@ref]`/`[x@uniq]` entry of an outer binding once 2c-5 resolves `env_x(outer)` to `ρ_outer`. Required: 2c-5's design substitutes
  the returned Closure value's signature with the same map (for example through the closure context `ClosureSignature` already applies,
  as generic contexts do); tests: an n1 variant whose inner call's result is fitted to a fixed Origin, and no outer-closure atom in the
  caller's MIR.
- **F2 (2c-3B, §2.5 (b), (d), D-2): pending entries and in-place removals.** D-2 cites the in-place removals as its reason for lists,
  but an entry whose obligation is later removed (`Binding.LocalOrigins.cs:51-52, 98-99`, `ImplicitConstructors`, `OriginClauses`,
  `ConstraintValidation`, `OriginInheritance`) would still be judged at classification. Required: judge only entries still in
  `obligationSet` (or remove them with the obligation), and append an obligation once per binding of its Closure.
- **F3 (2c-3B, §2.5 (b)): the classification pass routes as binding time does.** After resolution Binding judges what is fixed-fixed or
  Refuted; what stays a body chain stays a row for A5 and the old reporter, so g65a gets one record on mir, not Binding's and
  MirOrigins'. Add the sentence and a mir one-record test (H until MirOrigins is on dev).
- **F4 (2c-5, §2.13, D-5): the gate reads the final binding.** Value calls inside a closure argument are bound under consultations and
  candidate trials; a list entry from a binding that is not kept could gate a call the final binding does not (a false U on the
  default). Required: keep the fact on the value call's plan (overwritten per binding) and have the gate re-read each listed call's plan,
  or rebuild the list from final plans.
- **F5 (§1 table): split "Result naming ρ, or an Origin inside `Env`".** Naming ρ carries the receiver Loan and what a read through it
  carries (Value); naming only an Origin inside `Env` carries the read-through, not the receiver Loan (Content; t2a valid). The
  implementation (`Retains`' Value/Content rows) is right; the text reads as if t2a were an error.
- **F6 (§8, R4-M126's text):** 2c-5's DECISIONS entry records "CallResult becomes `Unbound` at a call" as an R4-M118 row ("a later site
  adds a row under it"); 2c-5 already waits for G:A5-F4, which brings R4-M118 to dev.

### The writer's decisions D-1 … D-16

| ID | Ruling |
| --- | --- |
| D-1 | Accept: a capture bit alone cannot rebuild the receiver of a case, so the case-free receiver is needed; `ClosureReceiverIn` with its reader (Short). 2c-2's done condition ("derivable from the published facts") is then tested through the facts, without the function |
| D-2 | Accept with F2. Lists keyed by the outermost unclassified Closure give N4 (b) and (d) by construction; the scan at each binding start is linear in the open entries per Closure binding (H for many inner Closures in one large outer body; V16 in 2c-3B if it shows) |
| D-3 | Accept: Unclassed (row 9) gives a located U, safe as a guard; with (a) no bound body stays unclassified |
| D-4 | Accept: rows from every returned value to the published position Origin are Proven by the meet law or Refuted, and the same row reaches MirOrigins (one judgment, not a pre-decided subset) |
| D-5 | Accept with F4 |
| D-6 | Accept: otherwise MirBorrows decides by the callee Type what TyCtxt's contract decided |
| D-7 | Accept: the key shapes cannot collide (Closure Type, Function signature, BindingSymbol); BU-34's residual R1 (closure contexts keep a raw W) can only duplicate an entry with equal rows; `Env` is the MIR operand's case Type, read through `Apply` as BU-34's rule asks |
| D-8 | Accept: `Binding.OriginDiagnostics.cs` is the Origin-diagnostics concern and `Binding.Diagnostics.cs` may not grow |
| D-9 | Accept, with a condition: `Classify` before the early returns now classifies failed Closures (`Receiver`/`ExclusiveUse` set where they stayed defaults); the MIR builder types a failed Closure's receiver local by `Receiver` (`MirBuilder.cs:220-223`), so a failed program's MIR dump may change. If the bp triple is not byte-equal, the move is a behavior change and belongs to 2c-3B with its snapshot classified |
| D-10 | **Recommend the package's option (own D+R for 2c-3 and 2c-5, each merging alone; G:A5-F7 = 2c-6, 2c-7).** In the plan 2c-3 held no Binding file (its contract was the 2c-2 view); now it holds adv and BU files (`TyCtxt.Retention.Calls.cs`, `Binding.Retention.Atoms.cs`), so PD29's letter puts it outside the group, and the plan's 2c-5 row already held BU and D5 inside the group against PD29. R4-M119's serial order (2c-2 → 2c-3B → 2c-3 → 2c-5) separates 2c-3 from the group's end by 2c-4 and 2c-5; holding BU's retention files that long blocks G160-2c, G240, G242 and G243. Conditions: BU confirms AB-2's adaptation, and 0c lands only after 2c-3, never before (plan risk row 5: A3-closure would become unsound); 2c-7's closing review covers 2c-3–2c-6 end to end. The alternative (in the group, holds in PLAN §3) is sound but holds BU's files longest |
| D-11 | Accept: lane B is serial, so 2c-9 never overlaps 2c-2's or 2c-3B's `Binding.Closures.cs` edits; "beside one of them in the other lane" is beside lane C's 2c-3 (disjoint files) |
| D-12 | Accept: the role sets are right; `HasEnvironmentOrigin` must read ρ between 2c-3B and 2c-5 (R1 still decides), and `CallLocalOrigin` must never read ρ as call-local (t4b's Consuming result names `meet(o_u, ρ)` before 2c-5's resolution) |
| D-13 | **Accept the open premise**, with a probe list. A missing receiver well-formedness premise (`OwnedOrigins(Env)` outlive ρ) can only give a false Unknown, never an acceptance; a premise row, if needed, is a catalog row under the method receiver's rule (R2), the planning session's before 2c-3B merges, not an owner question. Probe forms: (i) `[var a, var b]` holding `ref/i32 during o`, `swap(a@uniq, b@uniq)` with a generic `swap<T>(x: uniq/T, y: uniq/T)` (the instantiated input `uniq/(ref/i32 during o) during ρ`); (ii) `let p = a@uniq; p@follow = r` with a captured `r`; (iii) the same through `Option<ref/i32>`; each under Shared and Exclusive receivers |
| D-14 | Accept: stop and report is the brief's rule for a representation it does not give |
| D-15 | Accept (meets of any arity, nested meets flattened, no interning; a mechanical old-path check) |
| D-16 | Confirmed on `937fd8148`: `sameOrigin` at `BodyLowering.Sequences.cs:131` (used at `:140`), the two-element-Borrow compare at `:159`; `Classify` at `Binding.Closures.cs:430`, the early returns at `:393-396` and `:410` |

### Units, order, reviewers, census, R4-M119

- Units and order: lane B 2c-0 → 2c-1 → 2c-2 → 2c-3B → (2c-5 after 2c-3), 2c-9 any time after 2c-0; lane C after G:A5-F4 2c-3 → 2c-4 →
  2c-6 → 2c-7. Dependencies are right (2c-5 needs 2c-3B for P4, so n1 is safe, 2c-3 for retention reading ρ, and 2a-2's `ResultFit`;
  2c-4 needs 2c-3). R4-M119 holds: the four representation units are strictly serial, and the parallel pairs (2c-9 beside 2c-3; 2c-4
  beside 2c-5) touch disjoint files.
- Reviewers: BU for every Binding part, R5 (AR-2) for 2c-2 and 2c-3, AB-5's BU reviewer for 2c-1, 2c-2 and 2c-5 covered, no W1 slot;
  2c-3B "BU implements, R4 reviews; or the reverse" follows PD6.
- OQ7 (a) census (§5): right per unit (2c-1 Exclusive reclassification and `let` calls; 2c-3B r1e, r1f, t4g, n2b, G65's codes, g6a/b/d/e
  codes, the erasure role; 2c-5 locations, F2, downstream records, the gate; 2c-9 none). The census should also cover nested forms P4
  newly rejects beyond n2b (an inner annotated `during u` over a nested entry), under the same 2c-3B row; O3 is stated.
- BU-34 (`937fd8148`) changes nothing in the package: its files (`TyCtxt.cs`, `TyCtxt.Raw.cs`, `Binding.PairCases.cs`,
  `Binding.RawOrigins.Steps.cs`, `Binding.TypeRelations.cs`, `Binding.FunctionItems.cs`) meet none of the package's; a raw pair argument
  is a Copy acquisition, so P4 and D-14 never see it; its reading rule ("a slot's binding only through `Apply`") is what 2c-3's
  `Retaining` already does for Value callees (the operand's case Type, not `Subst.Types`). The other dev changes since `038f12ed4` are
  R5's codegen and object files.
- R4-M126 is free on dev and every branch (R4-M125 only on `claude/a5-g241`). The entry text is right; with F6 it names the R4-M118 row,
  and it may name this re-check as the review that cleared the package.

### Not verified

No prototype of the merged package and no run in this re-check: F1's false-Refuted variant is by reading of `SubstituteStoredOrigins`
and `ClosureSignature`; F3's double record waits for MirOrigins on dev; D-9's bp claim and D-13's premise are their units'.
