# 2a-2 design A: rule W on the MIR, with MirProblem's facts as one representation

Status: design A (R4 designer slot), 2026-10-25. Design only: no repository edit, commit or push. Base: `origin/claude/a5-2a-1c`
`c546f636a` merged locally with `origin/dev` `07482cba6` (DECISIONS and CODEMAP conflicts resolved by union; throwaway worktree
`bin\worktrees\2a-2-a`). Every claim marked **(P)** was run on a throwaway prototype of this design (`probes-a\proto-a.patch`); "(H)" marks a
reading no run checked. Sources read: the brief, `s2-common.md`, AGENTS, R4-HANDOFF §4, SPEC 15.6/15.6.4/15.6.5/23.1.2, DIAGNOSTICS §4.1–4.3
and the MIR Loan table, DECISIONS R4-M71, R4-M72, R4-M94, R4-M96, R4-M104, R4-M118, R4-M122, R5-M96, the proposal
(`draft/Changes/2026-10-21 Failures Caused Only by Other Failures.md`), the prototype round (`proto-round.md`, `w-bit.patch`), 2a0-D §0–§12,
the A5 step 2 plan's PD17/PD31/AR-7/2g rows, the R5/R6 plan's H0/H3 rows, PLAN G213, and the code of `Mir/Analysis`, the builder's
transfers, `MirDump`, `FunctionCodegen`'s gate and the ledger's `Sides`.

## 0. Recommendation in brief

- **One fact representation.** `MirFact(Kind, Site, Value)`: `Row` (int) becomes `Value` (long), the kind's typed value. Rule W's causes are
  facts of a new kind `Cause` whose Value is the cause problem's index in `MirReport.All`; G213's arguments are facts too (`Required`, the
  bytes an arena needed, and `Table`, which arena). `MirReport.Add` returns the problem's index; a problem with a `Cause` fact is
  *derived*, and derivedness is part of `MirReport`'s key, so a derived and a direct problem never merge.
- **The Origin stratum records first.** `MirOrigins.Judge` adds its problems itself (the separate `Record` after MirBorrows goes: its
  only reason was 2a0-D's rule 2, which W supersedes) and then publishes, for the Loan stratum, per Ref statement the recorded Refuted
  problems whose finite source is that Ref's Origin (`Causes`) and the class of a receiving Type (`Fixed`: every Origin it holds is Fixed).
- **The builder publishes the result in transit.** The temp a `return` delivers into `_0` after the Scope Exit (SPEC 16.2.2; the
  builder's existing "result temp", R5-M50) gets the role `MirLocalRole.Result`.
- **MirBorrows applies W with indirect markers.** The run's *W elements* are its Loans (and guard markers) whose Borrow has Refuted causes
  covering the run's views. Each has one *indirect marker*, an element in the marker range below `loanBase`, which a value carries where
  the element may have arrived other than as the borrow, a Copy or a Reborrow of it, stored into a part with a fixed Origin. The node-union
  join makes the marker a may-fact. Every existing Loan reader already skips markers, so no reader changes; a body with no Refuted relation
  has no W element and pays nothing **(P: identical Work and Bytes)**. A conflict is derived when every candidate of its root that the
  check set takes is a W element that only escape holders (E, `_0`, a Result temp) keep, none indirectly; it records the union of their
  causes as `Cause` facts. Nothing is dropped.
- **Publication (H3, R5 to confirm).** A MIR problem with `Cause` facts is published as `PrerequisiteUnavailable_Kd` with `DerivedFrom` =
  its causes' check keys, so DIAGNOSTICS §4.3's invariant is unchanged; only its sentence "Other ownership problems of the function stay
  direct" gains W's derived kind. The ledger leaves a problem with a `Cause` fact out of the compared MIR side (2g-1's planned rule, first
  instance).
- **Evidence (P).** On the prototype round's 113 programs the problem sets and every derived/independent label equal the round's. On
  the 1,240 sources of the 65be867e9 ledger with a Loan or Origin row, 14 problems become derived, matching 15 base new-side
  `ComparisonLoanConflict_Kd` rows (2a0-D's "≈ 13"); the 9 other new-side Loan rows beside an Unsatisfied row stay independent, each for
  W's own reason. Valid shapes: Work and Bytes identical; W shapes: Work +0.4–2.6 %, Bytes +3–10 % (the round's shadows: Bytes ×2–5);
  warm 0 B; MIR classes 1,236 run, 1 failure explained by the test helper (§9).
- **Units:** U1 (bp: the fact value, `Add`'s index, the recording order), U2 (the merge part: the role, W, the key, the ledger, records),
  U3 (G213's arguments and `Incomplete`'s source, with R5's gate change). Size ≈ +345/−55 in `src` (U2 ≈ +280/−15), tests ≈ +330.

## 1. Required meaning

SPEC 15.6.5 (R4-M104, R4-M122): a Loan conflict on the Loan of a Borrow whose finite Origin Refuted chains carry to fixed Origins is caused
only by the Refuted relations of those chains (23.1.2) when every value that carries the Loan and may be used after the access

1. is **the result or storage the body does not own**,
2. **received the Loan only as the borrow or a Copy or Reborrow of it** (15.6: not as an aggregate holding it, not as a value read from a
   holder, which carries every Loan of the holder; a whole-value Move of the borrow is the borrow), and
3. was **stored into a part with a fixed Origin**;

a call's result, or storage the call writes, that may retain such a value as an argument counts as a Copy of it (15.6.4), and a read
through a reference to a whole value counts as a Copy of that value. It changes which records are derived, never a verdict. Relations
are never derived; Unproven relations derive nothing. R4-M96: an access that meets several Loans is one MIR problem with one witness, direct
when any Loan's conflict is independent. DIAGNOSTICS §4.3: `Code == PrerequisiteUnavailable_Kd` exactly when `DerivedFrom` is not empty;
a derived fact is suppressed only when every cause reaches a published direct Error.

## 2. What the representation lacks or wrongly allows

| # | Fact W needs | Today | Owner that decides it |
| --- | --- | --- | --- |
| F1 | The result in transit: at the body's storage ends the returned value is in a builder temp (`_7 = move _6; StorageDead(local); _0 = move _7`), not in `_0` | Not published; the round emulated it by a def-use pre-pass (≈ 90 lines), which admitting user locals made wrong (m-write, m-scope) | The builder, which creates that temp (`MirBuilder.Transfers`, R5-M50's result temp) |
| F2 | A Borrow's Refuted causes: the Unsatisfied problems whose finite source is the Borrow's Origin, per view | MirOrigins keeps its problems in `pending` and adds them **after** MirBorrows (`MirAnalysis.Borrows`; MirOrigins' "Must not … add a problem in Judge (Record adds them after MirBorrows', for 2a-2's rules)") | MirOrigins (Refuted is its judgment) |
| F3 | A receiving part's class: whether the Origins of the part a store or a call writes are Fixed | Only `TyCtxt.OriginAtom`, which only MirOrigins reads (2a-1c: "only MirOrigins reads class") | MirOrigins, through the view |
| F4 | A Loan's (or guard marker's) Borrow atom | Inside MirOrigins (`refs`) | MirOrigins; folded into F2 (`Causes` is per Ref statement) |
| F5 | Per-part Loan facts | Absent: a part read reads the whole holder (`Read`) | Not designed: without them a part read is an indirect arrival, which is conservative (SPEC-legal, §3 m-twice) |
| F6 | The twins' Loan flow | `HirOp.Error` is an Opaque `{Φ_E}` (R4-M70) | H11 (not 2a-2) |
| F7 | A derived problem's causes | `MirProblem` has no cause or argument slot; a `MirFact` is a site (R4-M71); `MirReport.Add` returns nothing | `MirFact`/`MirReport` (shared with R5's H3) |
| F8 | G213: an arena problem's `requiredBytes`, `limitBytes`, `table`; `Incomplete`'s scope | None of the three `OwnershipStorageLimit_Kd` recorders (A1 `MirInit.cs:65`, A5 `MirBorrows.Solve`, A5 `MirOrigins.StorageLimit`) carries them; `MirDropTable.Incomplete` is `(Code, SourceRef)` | `MirDataflow` (the one limit decision, D20), the three recorders, `MirDrops` |

What the representation **wrongly allows** today: an Origin problem and a Loan problem of one escape are two unrelated direct problems,
so H3 could publish only direct records (the proposal §1's double record), and a merge rule in `MirReport` keyed by atom or candidate
(2a0-D §7) is the only place the relation could live, which is the symptom R4-M104 replaced.

## 3. Owners and the canonical path

| Fact | Owner | Published as | Consumer |
| --- | --- | --- | --- |
| Result in transit | MIR builder | `MirLocalRole.Result` on the temp a return delivers whole into `_0` | MirBorrows (escape holders) |
| A Borrow's causes | MirOrigins | `MirOrigins.Causes(statement)`: indices in `MirReport.All` of the Unsatisfied problems whose source is the Ref's own atom | MirBorrows (W elements) |
| A receiving Type's class | MirOrigins (view) | `MirOrigins.Fixed(type, site)` | MirBorrows (arrival) |
| Directness of an arrival | MirBorrows | indirect markers in the Loan sets | MirBorrows' check |
| A derived problem | MirBorrows records, MirReport keeps | `Cause` facts; derived in the key | ledger, H3 |
| Arena arguments | MirDataflow (bytes), recorders (arena) | `Required`, `Table` facts | H3, R5's gate |
| A body's first A1/A2 problem | A1/A2 recorders, `MirDrops` | `Incomplete = (Code, Source: SourceInfo, Problem: index or −1)` | R5's gate |

Mir consumes without re-deriving: MirBorrows never reads an atom or a row; MirOrigins never reads a Loan set.

## 4. The design

### 4.1 `MirFact` and `MirReport` (shared with R5)

- `MirFact(MirFactKind Kind, SourceRef Site, long Value = -1)`. Contract: "a fact is a site the problem relates and, for a kind that names
  one, its typed value": `Relation` (row index, −1 containment; the old `Row`), `Cause` (§4.4), and U3's `Required` (bytes) and `Table`
  (a `MirArena` value). A problem has at most one fact of each kind but its witness's and its causes (one `Cause` per distinct cause, in
  first-found order). 16 → 24 bytes per fact (a few per problem).
- `MirFactKind.Cause`: "a problem this one is caused only by (SPEC 15.6.5, 23.1.2): Value its index in `MirReport.All`, Site its subject".
  2g-1's planned cause (an Opaque site's Binding failure, PD17) can be this kind's site form (Value −1, H3 reading `Binding.FailureCauses`
  at Site); 2g-D decides that form, 2a-2 adds only the problem form.
- `MirReport.Add` returns the problem's index (the existing one on a merge; −1 for a problem a rule drops, and for the disagreement
  record's arrival). The key gains `Derived` (the problem has a `Cause` fact): `(Code, Subject, Body, Reason, Derived)`. A derived problem
  merges only with derived ones (views OR-ed, the first producer's facts, so its causes, kept); a direct one never absorbs a derived one.
  This mirrors DIAGNOSTICS' problem key, where the published derived record has another code. Contract line: "`Add` is the one home of the
  derived-problem rules: a problem with an Inlined fact is dropped (R4-M69); a problem with Cause facts is kept as derived (SPEC 15.6.5)".
- `MirProblem` itself does not change (no field): derivedness is a fact, as the brief's "one representation" asks.

### 4.2 MirOrigins: records first, publishes causes and classes

- `Judge(body, views, report)` adds its problems at its end (the `pending` list stays: `StorageLimit` still clears the body's earlier
  classes), keeps each one's index, and builds, by a counting sort over the recorded Unsatisfied problems' finite source nodes, a
  per-Ref list (refs are in statement order). Driver step 4: "the Origin stratum judges and records, then the Loan stratum per view"; the
  "Must not … Record adds them after MirBorrows'" line and the `Record` member go. **(P)** with the reorder (and the rest of the prototype)
  the MIR classes pass (595/595); the before/after ledger multiset is U1's own evidence.
- `Causes(int statement) → ReadOnlySpan<int>`: the recorded Unsatisfied problems whose source is the own atom of the Ref at that statement
  (a body Judge left with no view or over the arena limit has none). Two Refs of one root share an atom and so share causes (m-twice).
- `Fixed(BoundType? type, SourceRef site) → bool`: the type holds at least one Origin, every Origin it holds (its own, its Origin
  arguments', its components', each distinct part once per walk) is Fixed at the site, and it holds no raw layer and no part the body does
  not know (a Type parameter, a projection, a Callable). One rule for every receiving part: a store's destination Place's case Type and a
  call's written referent's case Type (the call may write any of its parts that can hold the argument, so all of them must be fixed).
  **(P)** Index loops, not `foreach` over `IReadOnlyList` (its enumerator allocated 1,536 B warm until changed).
- Contract changes: Outputs gain the two publications ("for the Loan stratum's rule W, read after Judge and before the next body's");
  Invariants unchanged (no Loan set read). `TyCtxt.OriginAtom`'s append of an unseen atom is the view's documented memo, now also reached
  from the query (H: no Judge output changes, since Judge's own walk precedes it).

### 4.3 The builder: `MirLocalRole.Result`

`MirBuilder.Transfers`, where a transfer departs scopes with cleanups and the target's Destination becomes "a result temp in the target's
scope": when the Destination is `_0` (the body's return target), the temp gets `MirLocalRole.Result` ("the body's result in transit: the
temp a return delivers whole into `_0` after the Scope Exit, SPEC 16.2.2"). One line plus the vocabulary row. A tail value already lives in
`_0` during cleanup. An inner construct's delivered temp (a block exited from an inner scope) gets no role: such a carrier reads
independent (conservative). `MirDump` shows the role: **(P)** 30 of 32 goldens change, 88 lines, each only the role on one temp.

### 4.4 MirBorrows: rule W (`Mir/Analysis/MirBorrows.Causes.cs`, contract first)

**Input.** MirBorrows' constructor gains `MirOrigins origins` (A4's keys are the precedent, R4-M92). After `Loans()`, before `Holders()`:

- *W elements.* A Loan is one when `origins.Causes(loan.Location)` is non-empty and the OR of those problems' views covers the run's
  views (`this.views`; a run for every view of a view-independent body needs causes in all of them). A guard candidate H's marker γ_H is
  one when every Ref statement that binds H is (a Ref past a shared Deref binds H but forms no Loan, so the statements, not the Loans, are
  its Borrows: **(P)** `rv-g-param` and `rv-g-param-i` needed exactly this). Each W element keeps its causes (those problems with a view
  of the run).
- *Element layout.* `[Φ_E][per-local markers][one indirect marker per W element][Loans]`: `loanBase = Markers + locals + k`. With
  `k = 0` nothing below changes.
- *Escape holders.* E, `_0`'s slot and the slots of Result temps.

**Transfer rules** (only where `k > 0`; `Indirect(S)` = S with the marker of each W element of S, memoized per set id in a pooled array):

| Transfer | Directness |
| --- | --- |
| Ref: the issued Loan into its destination (`own`), γ_H with it | as the arrival rule below |
| Read of a Place (`Read`) | kept for a whole root, or a chain of Derefs each through references to whole values (every direct Loan of the layer names a whole root, no Φ_E: the read-through clause); any other step makes it `Indirect` (a part read, an own step) |
| Reborrow parents (`Issued`) | each layer's parents kept while the layer is a whole value read so far, else `Indirect` |
| Use, Adapt, value-keeping Cast | the operand's value as it is (the round's rule) |
| Aggregate | `Indirect` of the union |
| Opaque | `{Φ_E}` (no Loan) |
| Call result | a Value row keeps the argument's value (the call clause); the read through a borrow argument and a Content row are `Indirect`; a Value callee's environment is `Indirect`; untied adds Φ_E |
| Arrival (`Store`'s entries and a Call's store rows) | into an escape holder: kept iff `origins.Fixed(receiving type, site)` (the destination Place's case Type, or the written argument's referent's), else `Indirect`; into another holder: kept only for a whole-root replacement, else `Indirect` |
| Join | node union (the marker is a may-fact) |
| Ending (E8, R-a) | an ended Loan's marker ends with it (`End` removes `Indirect(ended)`) |

**Check.** `Scan(set, via)` passes the set to `Consider`; for a W element found in a holder, the candidate's `Tags.Kept` (or, for γ_H in
the Based/Stored passes, a stamped `markKept`) is set when `via` is a transit value (−1), not an escape holder, or holds the element's
marker. All passes count (conservative, as the round).

**Derived test** (`Report(code, candidate, …)`, the one Loan-conflict path, and `Ended`): the problem is derived when every candidate on
the conflicting candidate's root that the check set takes (`Takes(taking)`, not `Covered`) is a W element and not `Kept`; for err(ended),
when γ_H is a W element and not kept. Its causes are the union of those elements' causes, appended as `Cause` facts after the witness
facts (`facts` becomes a pooled growable array). Taking the root's taken candidates instead of only the conflicting ones is a superset
(more problems direct): sound, no access re-scan, no parameter added to the 6-parameter `Report`/`Conflict`. A held view (P13) still turns
the problem into `Unsupported_Kd` (`Record`). Every other rule (R26, effects' Unsupported, limits) records no cause.

**Contract lines** (`MirBorrows.cs`): Input gains MirOrigins' causes and classes; the state invariant names the indirect markers ("markers
… which never conflict; an indirect marker ends with its Loan"); Output gains "a conflict caused only by Refuted relations (SPEC 15.6.5)
with their `Cause` facts"; "Must not … judge Origins" stays (MirBorrows reads published causes and classes only).

### 4.5 The ledger

`MirDifferenceLedger.Sides` (tests, R5's file after R5-Q17, hold adv): a MIR problem with a `Cause` fact is not on the compared MIR side.
**(P)** `VerifiedSpecExampleTest` (with `15.6.5-derived-conflict`'s `Dependent` row) and `MirDifferenceTest` pass.

### 4.6 Publication: what H3 needs (R5 to confirm)

1. **Derived records.** H3 publishes a MIR problem with `Cause` facts as `PrerequisiteUnavailable_Kd` at its subject, `DerivedFrom` = the
   check keys H3 gives its causes (by index in `MirReport.All`, through H3's own merge across bodies). The causes are direct Errors that
   are never held or dropped (Origin problems: no Inlined fact, no hold), so §4.3's finalization suppresses the derived record whenever its
   causes are published; it is published unsuppressed only if a cause is not. The MIR keeps the Loan code (ledger, spec cases' `Dependent`
   rows, tests). DIAGNOSTICS §4.3's invariant is unchanged.
2. **Merge.** R4-M71's publication merge `(code, subject, Reason)` gains Derived, as `MirReport`'s key does.
3. **Arguments (U3).** `OwnershipStorageLimit_Kd`: `requiredBytes` = the `Required` fact, `limitBytes` = `MirDataflow`'s limit (its one
   owner, D20), `table` = the `Table` fact's arena, rendered by the adapter.
4. **Facts read as `Value`.** H2's Relation row is `Value`.
5. **The gate (R5-M96, U3).** `Incomplete(body)` returns `(Code, Source: SourceInfo, Problem)`: the gate locates by
   `collector.Subject(item, body, source)` (KD12's move; the `Subject(item, SourceRef)` overload goes) and takes the arena problem's
   arguments from `Problems.Facts(Problem)`; the arena interim (a located `Unsupported_Kd`) goes.

Rejected for H3: publishing the derived record with its own code and `DerivedFrom` (changes §4.3's invariant and `DiagnosticCollection`'s
validation for no observable gain while the causes are published); not publishing it (DIAGNOSTICS' derived machinery exists for this).

### 4.7 G213 (U3)

- `MirDataflow` publishes the words it refused (`Prepare` returning null; `Within` for MirOrigins' arena); the three
  `OwnershipStorageLimit_Kd` recorders add `Required` (bytes) and `Table` (`MirArena`: `Initialization` for A1, `Loans` and `Origins` for
  A5's two arenas) facts. One cause, three sites, one rule (AGENTS' "search finds no other site").
- `MirDrops.Incomplete(code, source, problem)`: A1's and A2's recorders pass the problem's `SourceInfo` (its scope) and `Add`'s index (−1
  for an inline copy's, which `Add` drops; those are arity-0 codes, so the gate needs no argument for them).

## 5. Options

| Option | What | Verdict |
| --- | --- | --- |
| **I. Inside the strata** (no shared structure) | MirBorrows derives the result by a def-use pre-pass, calls `TyCtxt.OriginAtom` itself, receives causes from MirOrigins through a side channel by atom, and keeps derived problems in its own side table or a merge rule in `MirReport` | Rejected: a consumer deciding what the builder should publish; a second class reader; a side table for one feature; G213 and H3 still lack a slot (AGENTS' symptom list) |
| **II. Shared structure** (recommended) | §4: fact value and `Cause`, `Add`'s index and Derived key, MirOrigins first, the role | Adopted |
| III. A cause field on `MirProblem` | `MirProblem.Causes` (a span) beside facts | Rejected: a second representation of what facts already are; grows every problem |
| IV. Drop the derived conflict | R4-M94's style | Rejected by R4-M104 ("nothing is dropped") |

Bit representations (the round's cost question):

| Representation | Cost | Verdict |
| --- | --- | --- |
| B1. A shadow element per Loan for every Loan, above the Loans (the round) | Bytes ×2–5 in every body; every Loan reader must skip shadows (≈ 10 sites; a forgotten one indexes `loans` past its count) | Rejected |
| **B2. One indirect marker per W element, in the marker range** | 0 where no Refuted relation exists; W shapes Bytes +3–10 %; no reader change **(P)** | Adopted |
| B3. A dense per-holder bitset beside the state (A3's term) | Every value function returns a pair; the transfer code doubles | Rejected (size) |
| B4. One "dirty" marker per set | Smallest, but one indirect W Loan taints every W Loan of its carrier (E) | Rejected (precision for no saving over B2) |

Other choices: the result fact by role (adopted) vs a `MirBody` field (a second per-local fact form) vs the round's pre-pass (Option I) vs
linking MIR transfers to `ResultFit` rows (the proposal §4's "(H)" direction: a Binding Source value and a statement-to-row link for a fact
the builder already knows); the receiving check by every Origin of the receiving Type (adopted: **(P)** the outer Origin alone missed 5
container and closure-environment stores, e.g. `target@follow.append(local@ref)`) vs the outer Origin only; the derived test over the root's
taken candidates (adopted) vs only the conflicting ones (needs the access again, a seventh parameter); the merge with Derived in the key
(adopted) vs turning a derived problem direct on a direct arrival (needs fact removal).

## 6. Deletions

- Planned, never implemented (records only): 2a0-D §7's rules 1 and 2 with `RefutedAtResult` and `Ended`; `MirFactKind.Result` and 2a0-D
  §8's R5 ask for it; D2's `ResultFit` Source (2a0-2's E6 left it to 2a-2: W needs no row link; `TyCtxt.Origins.cs:23`'s comment "without
  `ResultFit` until 2a-2" becomes "D2 without `ResultFit` (R4-M…)", a Binding file, comment only); 2g-5's A5-F4 and A5-F7 escape parts (they
  are W's causes); DIAGNOSTICS' planned A6 merge rows, N-6's residual and the DOUBLE (R4-M122 already lists them).
- Superseded texts that take effect with U2 (R4-M122's "with 2a-2" list): R4-M94's merge rules 1 and 2, OQ-4 (b), OQ-3 (2) and (9),
  PD11's two halves and A3 E7's merge sentence, 2a0-D §10's 2a-2 done-condition exception and its listed expectations (m-store, jregion2,
  m-twice/same-root, g-return, `rv-g-param`, `cf-g-reassign`), DIAGNOSTICS §4.3's "stay direct" sentence (§10).
- Code: `MirOrigins.Record` and its ordering line (§4.2); MirOrigins' header sentence "The merge's facts are 2a-2's."; the round's def-use
  pre-pass is never written (≈ 90 lines); in U3, R5's gate interim and `InstanceCollector.Subject(item, SourceRef)`.

## 7. Units

**U1 (bp) — the fact value, `Add`'s index, the recording order.** `MirFact.Row` → `Value` (long) with its contract; `MirReport.Add` returns
the index; `MirOrigins.Judge` records (Record and the line go), the driver's step 4 sentence; tests read `Value` and pass a report to
`Judge`; the DECISIONS entry (§10). Verification: VERIFICATION's behavior-preserving procedure (the diagnostic snapshot and the semantic
snapshot with `KIMI_SEMANTIC_MIR=1`, which dumps the MIR but no problem, byte-equal; MIR classes; the mir ledger equal as a multiset).
≈ +25/−25. R4-only files but the driver (`MirAnalysis.cs`, shared: a hold line).

**U2 (behavior) — the merge part (W).** Commit 1: the builder's `MirLocalRole.Result`, the 30 goldens and the semantic snapshot's MIR dumps (Mir/Build,
MirVocabulary: hold lines with R5/BU). Commit 2: `MirFactKind.Cause`; the Derived key; `MirOrigins.Causes.cs` (Causes, Fixed); `MirBorrows.Causes.cs` (W) and
the transfer and check edits (`Values`, `Flow`, `Calls`, `Candidates`, `CheckSets`, `Solve`, `Reports`, `Checks`, `Witnesses`); the ledger
line; `MirBorrowsTest`'s `Stratum` helper builds MirOrigins and judges each body first (its re-run then merges into the driver's derived
problems; **(P)** without it `AStaticsLoanNamesEAndAWholeWriteEndsIt` sees one derived and one direct `y = 2`); records (§10).
Size ≈ src +280/−15 (MirBorrows.Causes ≈ 170 with contract, MirOrigins.Causes ≈ 70, edits ≈ 55), goldens ±88, tests ≈ +260.

Tests (`tests/xUnitTest/Tests/MirBorrowsTest.Causes.cs`, following its file; 2a0-D named `MirOriginsTest.Merge.cs`), one theory at the MIR
problem layer, each row "code subject [<- cause subjects]"; derived rows fail before (no `Cause` fact exists), independent rows pass before:

- Derived: j-region and `j-bad`'s `var` form; §3 row 2's `var` form; `m-write4-v` (SPEC's `store` shape); `m-store-v`; two flows and
  `cf/p/jregion2` (both causes); a loop store (next iteration's Borrow and the end); `return id(r)` (call clause); `rv-g-param` (err(ended)
  through `keep`'s write) and its `v@ref` form (read-through clause); round 3's keepA end (its write independent); the `if`/`match`/block
  result `-v2` forms; a container append through a parameter (`target@follow.append(local@ref)`); a closure storing into its `var`
  environment binding (`arr.append(local@ref)`).
- Independent (pins): m-write and its `var` forms, m-write3, m-scope, m-scope-use, g-two, g-return, `cf-g-reassign` (its S at the return
  present: the R4-M94 pin flips), `rv-g-annot-esc`, fK, B1 (a), (b), (c), (d) `var` forms, fT, the flow-insensitive row, m-pair and m-twice
  and same-root `var` forms, a conflict without a Refuted twin, `o = .Some(local@ref)` (aggregate), `put(slot@uniq, local@ref)` into a
  local; `rv/guard-annot` (its S kept, no conflict); PreparedInput (`DefaultArgumentBorrow_Kd` carries no `Cause`: grouping, 2g-5).
- The twins' limit (pins): j-bad and m-write4 literal forms report the relation only (R4-M122: conforming). m-36f is vacuous on this base
  (R4-M122) and gets no row.
- `MirAnalysisTest`: `Add`'s index; a derived and a direct problem of one key stay two; two derived ones merge keeping the first's causes.
- A cause contract test over the theory's programs: each `Cause` names an Unsatisfied problem of the same body whose Reason's source is the
  Borrow's atom (PD31).
- V16 (`MirBorrowsTest`): "W stores" (N locals copied 8 times, stored through a parameter) and "W aggregates" (nested Tuples, a part
  stored): Work at 2× and 4× within c of the W-free run's (**(P)** +0.7 %/+0.6 %/+0.4 % and +1.5 %/+2.3 %/+2.6 %); a valid shape's Work and
  Bytes equal with and without a Refuted relation elsewhere.
- Warm 0 B: the driver's step 4 over the theory's programs (**(P)** 0 B, 9 derived problems).
- `VerifiedSpecExampleTest`, `MirDumpTest`, `MirDifferenceTest`, `OriginRowsTest`, `MirOriginsTest` (Judge's report argument).

Done when: every derived row passes and every pin holds; Unit classes and a Session (default; a mir Session, since the builder changes:
R4-HANDOFF's G226 lesson) pass; the diagnostic snapshot is byte-equal (default pipeline untouched); the mir ledger loses exactly the
derived problems' new-side rows (**(P)** ≈ 15 on 65be867e9's corpus) and gains none; warm 0 B; V16 rows pass.

**H3's needs** add no R4 unit: U2's `Cause` facts and U3's arguments are what §4.6 reads; R5's H3 implements §4.6 and reviews it first.

**U3 — G213.** §4.7 plus R5's gate change in the same merge (R5 holds `Backend/Codegen/**`: R5 makes the gate's edit, or grants R4 a
one-line hold). Tests: `MirInitTest` (the arena problem's `Required` and `Table`), `MirOriginsTest`/`MirBorrowsTest` limit tests (their
facts), `MirDropsTest` (`Incomplete`'s scope inside an inline copy: KD12's move), R5's `FunctionCodegenTest` arena row (the code becomes
`OwnershipStorageLimit_Kd` with arguments). ≈ +40/−15, tests ≈ +60.

Order: U1 → U2 → U3 on one R4 branch stacked on `claude/a5-2a-1c`; U2 and U3 are independent of each other after U1. 2a-4 and 2a-5 follow
(2a0-D §10), then the group review and the merge of G:A5-F4.

## 8. V16 and cost

- Work terms added per view, only where k > 0 (k = W elements): `Indirect` scans each distinct set once (memoized), so ≤ the elements of
  the sets the transfers make, i.e. within a constant of the existing set work; `Kept` adds one binary search per W element scanned;
  `Fixed` walks each receiving Type once per W-carrying store (distinct parts once); the derived test scans the root's candidates once per
  conflict. Memory: k marker elements per set that holds a W element (Bytes ≤ 2× those sets), pooled arrays by set count, Loan count and
  holder count; under `MirDataflow`'s limit no new term (the state words do not change).
- **(P)** (`ScratchWaProbeTest.Cost`, the driver, all bodies): valid copies and valid nested Tuples identical in Work and Bytes with W on and
  off; W stores Work +0.7/+0.6/+0.4 % and Bytes +6/+5/+3 % at N = 32/64/128; W aggregates +1.5/+2.3/+2.6 % and +10/+7/+4 %; W joins
  +0.6/+0.7/+0.5 % and +6/+5/+3 %. The round's shadows cost Bytes ×4–5 on the nested Tuples.

## 9. What was probed (all in `probes-a\`; the throwaway worktree `bin\worktrees\2a-2-a` was removed at the end, junction unlinked first)

- `proto-a.patch`: the prototype (`src` +453/−32 in 19 files: `MirBorrows.Causes.cs` 285 lines, `MirOrigins.Causes.cs` 96 lines; plus
  the scratch probe test). Not the implementation: it keeps `Record` separate, an O(refs × problems) causes index, env toggles.
- `compare.py`: the round's 113 programs (`cases\*.kimi`, outputs `*.a.out`) against the round's `.on.out`: problem sets equal and every
  W label equal (113/113), after the guard-marker fix (Borrows are the Ref statements binding H, not Loans).
- `corpus\` (1,240 sources of `shared-base\65be867e9\mir-diff` with a Loan or Origin row), `ledgermatch.py`, `corpus-derived.txt`: 14
  derived problems, each matching base new-side rows (15); `new-loan-near-s.txt`: the 24 new-side Loan rows in comparisons with an
  Unsatisfied row; the 9 that stay are aggregates (`.Some(inner@move)` ×3, `.Some(local@ref)`), local carriers (`put(slot@uniq, …)` ×2,
  `assign(x@uniq, y@uniq)` ×2) and a Tuple result read by part. With the outer-Origin receiving check only: 9 derived.
- `led-off\`, `led-on\`: tier 1 (`MirDifferenceTest.RequestedCorpus`, mir): 76 raw rows, equal with W off and on (no derived problem there).
- `base-classes.*`, `a-classes*.*`: MIR classes 595/0 before and after; with Validator/Dataflow/Effects/Drops/FunctionCodegen 1,236 run, 1
  failure (the `Stratum` helper, §7); `MirDumpTest`: 30 goldens differ by the role only.
- `cost-on.log`, `cost-off.log`; the warm probe (1,536 B → 0 B after index loops).
- No heavy run: no Session, no full ledger. The full mir ledger and the snapshot are U2's evidence.

## 10. Records

- **DECISIONS** (one entry, the planning session's number, written in U1's commit, which first implements the representation; U2 and U3
  implement the rest and cite it): "R4-M… Rule W on the MIR;
  MirProblem's facts as one representation (2a-2, G:A5-F4, full process)": the rule's implementation (§4.4) and its conservative readings
  (part reads, the root's taken candidates, transit values, Content retention, an inner delivered temp); the representation (§4.1); the
  recording order (§4.2) superseding 2a0-D §6.1's "Record after MirBorrows"; the role (§4.3); the ledger line; the H3 contract as
  confirmed by R5 (§4.6); departures (§11); rejected options (§5); G213's arguments with U3.
- **DIAGNOSTICS**: §4.3's "Other ownership problems of the function stay direct" gains "except a Loan conflict caused only by Refuted
  relations (SPEC 15.6.5), which A5 records with `Cause` facts and publication derives from them"; the MIR Loan table gains that row; the
  A6 fact rows gain `Cause` (and U3's `Required`, `Table`).
- **ARCH** canonical rows: Loan stratum (rule W, the escape holders, the indirect markers), Origin stratum (records in Judge; Causes, Fixed),
  MIR analysis problems (the fact value, Derived in the key, `Add`'s index), MIR construction (the Result role), Drop elaboration (U3's
  `Incomplete`).
- **CODEMAP**: the two new files. **PLAN**: G213's row (R4's part with U3), R4's line, the hold lines. **STATUS**: the Origin boundary row
  gains the twins' wider loss (the round's §4 item 1: a value the failed node reads also loses its later conflicts until H11), on the
  planning session's call.
- **R5's review** (R4-M104, AR-7's extension) of §4.6 before U2's merge.

## 11. Departures from approved texts (for confirmation as one list)

1. 2a0-D's test file `MirOriginsTest.Merge.cs` → `MirBorrowsTest.Causes.cs` (the rule's owner).
2. The proposal §4's "(H)" direction "the link from a MIR transfer to its relation row" → the Result role and `Fixed` (no row link; no
   `ResultFit`, 2a0-D D2's sixth Source value is not added).
3. 2a0-D §6.1's order "Judge, MirBorrows per view, then Record" → Judge records before MirBorrows (rule 2, its reason, is superseded).
4. R4-M96's "direct when any Loan's conflict is independent" applied over the root's candidates the check set takes (a superset).
5. R4-M71's `MirReport` key gains Derived; "a `MirFact` is a site" becomes "a site and its kind's typed value".

## 12. Questions

**For R5 (confirm):** §4.6 items 1–5; the hold on `tests/xUnitTest/MirDifferenceLedger.ComparedSet.cs` (adv) for U2's one line and on
`Mir/Build/MirBuilder.Transfers.cs`/`MirVocabulary.cs` for the role; U3's gate edit (who writes it).

**For the owner** (recommended answers first):

- OQ-A1. Confirm §11's departures: (a) all, as listed (recommended: each is a consequence of R4-M104 and the facts the round found);
  (b) per item.
- OQ-A2. The H3 contract for derived MIR problems, should R5 prefer to keep the Loan code: (a) `PrerequisiteUnavailable_Kd` with
  `DerivedFrom`, DIAGNOSTICS §4.3's invariant unchanged (recommended: no contract change, suppressed whenever its causes are published,
  2g-1's pattern); (b) its own code with `DerivedFrom`, the invariant and `DiagnosticCollection`'s validation changed.

No closed vocabulary grows (`MirLocalRole`, `MirFactKind` and the new `MirArena` are not in ARCH's list).

## 13. Unverified

- Pair-layer bodies with several views (per-view causes, the Derived key's two-problem case): no program here has several views; U2's
  `MirAnalysisTest` row tests the key directly (H).
- Inline default copies and held views with W elements; a cause in a body with an inline copy (H: a copy's Refs are not MirOrigins' refs,
  so its Loans derive nothing).
- `Fixed`'s abstract-part and raw-layer exclusions (the prototype excluded raw layers only) and its per-walk memo (the prototype walked
  without one; G140's DAG cost).
- The full mir ledger (tier 2) and the default-pipeline snapshot: estimated from the corpus probe, not run.
- The literal twin forms' post-H11 readings (as their `var` forms, H).
