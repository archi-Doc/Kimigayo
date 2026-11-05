# 2a-2 design B: rule W on the MIR, with MirProblem's facts as one representation

Designer slot B of 2a-2's merge part (full process; R4-M104, R4-M122). Written independently of design A (no `design-A*` file read).
Base: `origin/claude/a5-2a-1c` `c546f636a` merged locally with `origin/dev` `13da54130`/`47ece7a1f` (throwaway worktree
`bin\worktrees\2a-2-b`, removed at the end). No repository commit or push. A throwaway prototype of this design ran on that base
(§12); "(H)" marks a claim no run checked. `M/` = `src/Kimi/Compiler/Mir/`, `MA/` = `M/Analysis/`, `T/` = `tests/xUnitTest/Tests/`.

## 0. Decisions in brief

| # | Decision | Owner component |
| --- | --- | --- |
| W1 | A Loan problem is **derived** when every Loan of the access's check set it conflicts with was found only in the result or in E, each there **as received** (as the borrow, a Copy or a Reborrow of it, into a part with a fixed Origin), and its Borrow's own Origin has Refuted problems in every view of the problem. It keeps its code and gets one `Cause` fact per such Refuted problem. Nothing is dropped; no verdict changes | `MirBorrows` decides (it owns the carriers); `MirOrigins` publishes the causes |
| W2 | **Received halves.** Each value and holder state is a pair of interned `MirLoanSets` sets in one word: its Loans (low half, what every existing reader reads) and the subset that did **not** arrive as received (high half, "indirect"). Joins union both halves, so "received on every path" is the AND the proposal names. No new element class, no extra arena word | `MirBorrows` (new concern file `MirBorrows.Causes.cs`) |
| W3 | **The result in transit** is the builder's fact: the return target's result temp (R5-M50's delivery temp) gets `MirLocalRole.Result`; A5's result holders are local 0 and that temp | builder (`MirBuilder.Transfers`), checked by `MirValidator` |
| W4 | **A receiving part's class** (fixed or not) and **a Borrow's causes** are published by `MirOrigins`, which owns every Origin classification on the MIR: `Causes(statement)` (the report indices and views of the Refuted problems whose finite source is the own Origin of the Ref at a statement), `FixedPart(type, site)` and `Refutes(body)` | `MirOrigins` (new concern file `MirOrigins.Causes.cs`) |
| W5 | **Step 4's order** becomes Judge, Record, then the Loan stratum per view: Origin problems are in the report before the Loan problems that name them (the old order existed only for 2a0-D's rule 2) | driver (`MirAnalysis`) |
| W6 | **One fact row.** `MirFact(Kind, Site, long Value)` is a problem's typed fact: a related site, a cause or a catalog argument. `Value` is the Relation's row, a Cause's problem index (−1: a Binding failure at `Site`, 2g-1's), and G213's `Required`/`Limit` bytes and `Table`. `MirReport.Add` returns the problem's index | `MirFact`, `MirReport` |
| W7 | `MirReport`'s merge: a problem is derived only when every report of it is, naming the same causes; otherwise its `Cause` facts (always last) are dropped | `MirReport` |
| W8 | **Publication (H3, R5's):** a problem with `Cause` facts is published as `PrerequisiteUnavailable_Kd` with the causes' check keys (DIAGNOSTICS §4.3 unchanged; suppressed whenever its causes are published). The ledger matches a derived MIR problem only where the old side has the record, and drops it otherwise | R5 (H3); the ledger (shared file) |
| W9 | **G213:** A1's and A5's arena problems carry `Required`, `Limit` and `Table` facts through one recorder; `MirDropTable.Incomplete` carries the problem's report index and the operation's `SourceInfo` (A-2c), so R5's gate records `OwnershipStorageLimit_Kd` with its arguments and applies KD12 | `MirDataflow` (the one limit decision), A1, A2 |

## 1. Required meaning

- **SPEC §15.6.5, W (R4-M122's text).** A Loan conflict on the Loan of a Borrow whose finite Origin Refuted chains carry to fixed Origins
  is caused only by the Refuted relations of those chains (§23.1.2) when every value that carries the Loan and may be used after the access
  is the result or storage the body does not own and received the Loan only as the borrow or a Copy or Reborrow of it (§15.6), stored into
  a part with a fixed Origin; a call's result, or storage the call writes, that may retain such a value as an argument counts as a Copy of
  it (§15.6.4), and a read through a reference to a whole value counts as a Copy of that value.
- **The forms it governs:** every Loan conflict code (`ComparisonLoanConflict_Kd`, `CallReservationConflict_Kd`, `CallActivationConflict_Kd`,
  `PlacementActivationConflict_Kd`; err(destroy), err(effect), err(ended) included). Not: R26 (`DefaultArgumentBorrow_Kd`,
  `DefaultArgumentAccess_Kd`: §7.2.3's requirements, not Loan conflicts; 2g-5's PreparedInput grouping stays), R3's destruction hold (no
  Borrow), Origin problems (relations are never derived), Unproven relations (derive nothing).
- **§23.1.2.** Independent failures are reported; a derived one may be. So a conservative reading (an independent report of a conflict W
  makes derived) is SPEC-legal; a derived label on an independent conflict is not.
- **§15.6 (carriers).** "Values used after the access": the holders live after it that it does not replace, E, the values it stores into
  a live holder or E (transit), and the values it takes. The holder rule: a value read from a part carries every Loan of the holder, as no
  Copy of the borrow; an aggregate or Closure holding the borrow is not the borrow.

## 2. What the representation lacks, and who owns each fact

| Fact W needs | Today | Owner that publishes it | Consumer |
| --- | --- | --- | --- |
| (a) Per (Loan, holder): arrived as received | absent | `MirBorrows`' transfer (it forms every value) | `MirBorrows`' derived test |
| (b) The result in transit (return temps before `_0`) | absent: the result temp is an ordinary `Temp` (the round's §5 item 1) | the builder (R5-M50 creates the temp) | `MirBorrows` |
| (c) A store's receiving part class | only through `TyCtxt.OriginAtom`, which MirBorrows must not call | `MirOrigins` (all Origin classes on the MIR come through it) | `MirBorrows`' arrival rule |
| (d) A Loan's Borrow atom and its Refuted chains | `MirOrigins.refs` and `pending`, unpublished | `MirOrigins` | `MirBorrows` |
| (e) Per-part Loan facts | absent | — (not added: part reads and aggregates make Loans indirect, conservative, §3's allowance) | — |
| (f) A problem's causes | `MirProblem` has no cause slot (G213) | the deciding stratum records them as facts | `MirReport` (merge), H3, the ledger |
| (g) A problem's catalog arguments (`requiredBytes`, `limitBytes`, `table`) | no argument slot (G213) | the recorder of the limit (`MirDataflow`) | R5's gate, H3 |
| (h) The scope of `Incomplete`'s problem (A-2c) | `SourceRef` only | A1, A2 (they record it) | R5's gate (KD12) |

The twins' Loan flow (round §5 item 2) stays absent until H11: W derives nothing in the literal twin forms, and the independent conflicts
the twins lose are the recorded Opaque limit (R4-M122's open item), not this unit's.

## 3. Design

### 3.1 MirFact, MirProblem, MirReport (W6, W7)

- `MirFact(MirFactKind Kind, SourceRef Site, long Value = -1)`; `Row` becomes `Value` (a long, so G213's byte counts fit). Contract: "a
  typed fact of a problem: a related site, a cause, or a catalog argument; never a message; a problem has at most one fact of each kind
  but its witness's and its causes; its `Cause` facts come last". New kinds: `Cause` (2a-2c), `Required`, `Limit`, `Table` (2a-2d).
  `MirFactKind` is not a closed vocabulary (ARCH lists none for Mir facts).
  - `Cause`: `Site` the cause's subject, `Value` the cause's index in the same report; `Value` −1 names a Binding failure at `Site` that
    publication resolves through `Binding.FailureCauses` (2g-1's Opaque-site cause, PD17; 2g-D adopts or rejects that half).
- `MirProblem` is unchanged (no argument or cause slot: facts carry both).
- `MirReport.Add` returns the index of the problem it added or merged into, −1 for one it dropped (an Inlined fact); its contract gains the
  merge of derived-ness (it still drops only R4-M10's and R4-M69's problems). A merge keeps the first problem's `Cause` facts only when the
  arriving report names the same causes; otherwise the merged problem is independent and its `Cause` tail is dropped (the span shortens).
  Two recorders read the index: `MirOrigins.Record` (W4) and A1/A2 for `Incomplete` (W9).

### 3.2 The Origin stratum publishes (W4, W5)

- Driver step 4: `origins.Judge(body, views)`, `origins.Record(report)`, then `borrows.Run` per view. `MirOrigins`' "must not add a
  problem in Judge (Record adds them after MirBorrows')" becomes "Record adds them before the Loan stratum, which names them as causes";
  ARCH's Origin-stratum row and `MirAnalysis`' step-4 sentence change with it.
- `MirOrigins.Causes.cs` (contract first: "the Origin stratum's facts for the Loan stratum, of the body last judged"):
  - `Record` keeps, per Unsatisfied problem it adds, (its report index, its class's views, its finite source atom); the source is read
    from `Reason.Longer` against the view's canonical atoms, so `MirOrigins.Chains.cs` (near the 5,000-token limit) is not edited.
  - `Causes(int statement, List<...> into)`: the problems whose source is the own atom of the Ref at that statement (a per-statement
    `refAt` table filled by `CollectRefs`; none for a Ref inside an Inlined scope, a raw Ref or a PairAcquire Reborrow, which form no atom:
    such a Loan derives nothing).
  - `FixedPart(BoundType? part, SourceRef site)`: whether the part's outer Origin is Fixed in the judged body at the site
    (`TyCtxt.OriginAtom`, the view's own append; MirBorrows never calls the view).
  - `Refutes(body)`: whether the judged body has a Refuted problem. Where it has none W can derive nothing, so MirBorrows keeps every
    value's indirect half empty (no arrival queries, no kept bits, `Conflict` stops at the first conflict as today): W's logic costs
    nothing in a body without a Refuted relation, which is every accepted program.
- `StorageLimit` (D20) clears the stratum's problems, so a body over the Origin arena's limit has no causes (its conflicts are independent).

### 3.3 The Loan stratum (W1, W2)

**Representation.** A value and a holder state are one `ulong`: low 32 bits the Loan set (as today: every `(int)state[x]` reader is
unchanged), high 32 bits the indirect subset I ⊆ L. Joins union both halves (Loan indirect on some path ⇒ indirect); endings remove from
both; an empty or all-indirect half needs no second set operation (fast paths). The arena keeps one word per (head, flow holder).

**Transfer rules for I** (each a row of one table in `MirBorrows.Causes.cs`'s contract; the plumbing lives in the existing owners):

| Form (SPEC) | I of the value |
| --- | --- |
| A Ref's own Loan; a guard candidate's γ_H (E7) | empty (born in its destination), then the arrival rule |
| A whole-root Copy or Move; a value-keeping Cast (Erasure, Upcast, Pointer) | the root's I |
| A Deref through a reference (`Content`): per direct Loan naming a holder's whole root (its Place has no step) | that holder's I (the read-through clause) |
| — through a direct Loan naming a part of a holder, through Φ_E or a reference with no direct Loan (E's content), a static's E | all indirect |
| A part step (Inline, Index, Own) in a read or a Reborrow's layer | all indirect (holder rule) |
| A Reborrow's parents (R2) | per layer as above: the root's whole value through whole-root references keeps I |
| An aggregate; a Value callee's environment | all indirect |
| An Opaque site | {Φ_E} (no Loan) |
| A call's result retaining an argument (Value row; a borrow argument's Content too) | the argument's I (the call clause) |
| A Content retention | the read-through rule above |
| **Arrival** into the result (local 0, the `Result` temp) or E, whole or part | kept only when `FixedPart` of the receiving Place's case Type holds, else all indirect |
| Arrival into a local: whole replacement / part store | kept / all indirect |
| A write of a whole referent through a reference into a local holder (a store whose last step is that Deref; a call's write through an argument, W's call clause) | kept when every direct Loan of the reference naming that holder borrows its whole root (the written value is a Copy into the holder's whole value, the write-side twin of the read-through clause), else all indirect |
| A call's write through argument a into E (W's call clause) | kept only when E is a first-level target of a's value (`refs` reaches it) and a's referent's outer Origin is fixed, else all indirect |

**The derived test.** The gather tags each candidate with one more bit, `Kept`: found in some pass in a carrier other than the result or E,
or there not as received (a transit value's carrier is the holder it is stored into; a taken operand value is always such a carrier). This is
conservative against the exact check set (a carrier in a pass the access does not take also counts) and is what the round's 113 programs
read (§12). A marker's bit is a stamp beside `markSeen` (Based and Stored passes, err(ended)'s own set).
`Conflict` (`MirBorrows.Checks`) scans its root's candidates as now and returns the access's conflict: its first conflicting candidate,
and, in a body that `Refutes` (where it scans on past the first), whether every conflicting candidate is derived (¬`Kept`, and `Causes`
of its Loan's location covers the problem's views), with the causes in a scratch list; `Reports` appends them as `Cause` facts after the
witness facts (deduplicated by index). err(ended) does the same
for the marker, whose Borrow is the first Ref issued into the candidate (recorded by `Loans()`). A held view (R4-M73) records its
`Unsupported_Kd` with no cause. An access that meets several Loans stays one problem (R4-M96): derived only when all its Loans are.
Member limits hold: no member gains a seventh parameter (the conflict is one returned value, not a bag).

### 3.4 The builder (W3)

`MirLocalRole.Result`: the result temp a valued transfer to the **return target** (targets[0]) creates when it leaves a scope with a
cleanup (R5-M50). `MirDump` prints roles already (30 goldens gain ` (Result)`); `MirValidator` checks the role's contract: at most one
such local, a `Temp` of local 0's Type. Without cleanup the transfer writes `_0` directly, which is a result holder already.

### 3.5 Publication and the ledger (W8)

- **H3 (R5).** Facts map to the record: `Anchor`/`Use`/`Store`/`Reservation`/effect/`Evaluated` → related locations with roles;
  `Relation`'s row → the view's Source, Clause, Destination (H2), `Reason` → relation, longer, shorter; views → `case`; `Required`/`Limit`
  → Arguments, `Table` → `table` evidence; `Cause` → `DerivedFrom`, the published check key of the cause problem (an index maps to the record
  H3 makes of that problem after R4-M71's merge across bodies), or `Binding.FailureCauses(Site)` for −1. Recommended (OQ-W1 (a)): a problem
  with causes is `PrerequisiteUnavailable_Kd`, so §4.3's invariant and suppression apply unchanged (its causes are Origin problems, never
  derived by W; one 2g-1 derives from a Binding failure suppresses it through that failure's record); a merged record across bodies is
  derived only when every merged problem is.
- **Ledger** (`tests/xUnitTest/MirDifferenceLedger.ComparedSet.cs`, shared-file protocol): `Sides` marks a MIR problem with a `Cause`
  fact derived; `Match` pairs it with an old record of its code and node where one exists (side both) and drops it otherwise (never side
  new). One rule
  for W's and 2g-1's causes; it replaces the step-2 plan's 2g-1 wording "excludes MIR problems carrying a cause fact", which would turn a
  record both paths report into an old-only row.

### 3.6 G213 and A-2c (W9)

- `MirDataflow.StorageLimit(MirReport report, MirBody body, ulong views, MirStorageTable table, long words) → int`: the one record of
  R4-M15a's limit, `OwnershipStorageLimit_Kd` (`Ownership.StorageLimit`) at the body with `Required` (words × 8), `Limit` (the byte limit)
  and `Table` facts; `MirStorageTable` = {Initialization (A1), Loans (A5's Loan arena), Origins (A5's Origin arena)}, rendered as text only
  by the adapter. `Prepare` reports the words it refused (`out long`). Its three callers (`MirInit.Run`, `MirBorrows.Run`,
  `MirOrigins.StorageLimit`) call it; their three inline constructions go.
- `MirDropTable.Incomplete(body) → (DiagnosticCode Code, SourceInfo Source, int Problem)?`: A1's and A2's recorders pass the operation's
  `SourceInfo` and the index `MirReport.Add` returned (−1 for an inline copy's problem, which the report drops). R5's gate then records
  `OwnershipStorageLimit_Kd` with the facts of `Problem` and locates through `Subject(item, body, source)` (KD12), and deletes R5-M96's
  arena interim and the `Subject(item, SourceRef)` overload (R5's unit).

## 4. Options considered

| Question | Options | Chosen, and why |
| --- | --- | --- |
| The bit (round §6) | **B1** shadow elements in the Loan sets (the round's prototype); **B2** a dense per-holder bitset (holders × ⌈L/64⌉ words per head); **B3** a second interned set per holder in the state word's high half | **B3**: no new element class (B1 needs a `< shadowBase` filter at every element consumer: `Content`, `Refs`, `Write`, `Seed`, `Consider`, `Reserve`, `RawBesideLoan`, the endings' ranges; a second copy of the element classification), Bytes and Work equal to today on the round's shapes (B1: Bytes ×2–5, Work up to +52%), no arena growth (B2 adds A3's dense term to every head, the largest memory term) |
| The bit's precision | exact (a second `Tags` per candidate, `Kept.Takes(taking)`); one bit | **one bit**: the same readings on all 113 programs (§12); R4-M96 deleted a second `Tags` as unneeded; conservative is SPEC-legal |
| The result in transit | a structural pre-pass over temps (the round's emulation); a builder change writing `_0` before the Scope Exit; a builder role | **role**: the builder decides the temp, so it publishes it; a pre-pass is a consumer re-deciding a builder fact (a symptom, and it misread user locals in the round); writing `_0` early departs from R5-M50's one delivery rule |
| A store's part class | `FixedPart` of the receiving Place's case Type (**F-a**); the transfer's relation row (proposal §4's link, **F-b**); the callee retention row's receiving Origin (**F-c**, a Binding view change) | **F-a**: one query, no syntax link (F-b would match MIR sites to row Uses). Its known imprecision: a call writing a container's element (`target@follow.append(local@ref)`) reads the container's outer Origin, so the conflict stays independent (SPEC-legal). F-c is OQ-W2 |
| The cause link | the cause's report index; its key (code, subject, Reason) in the fact; the relation row | **index**: one int in the existing row; a Reason does not fit a fact; H3 maps an index to the key it publishes |
| Where W is decided | MirReport at Add (2a0-D §7's merge context); MirBorrows | **MirBorrows**: it owns the carriers; MirReport keeps only the merge of derived-ness (W7) |
| Publication | (a) `PrerequisiteUnavailable_Kd` + causes; (b) the Loan code + `DerivedFrom` (§4.3's invariant changes) | (a), OQ-W1 (R5's contract) |

Rejected outright: dropping derived conflicts (R4-M104: nothing is dropped); deriving by Origins or by Loan identity (R4-M104's rejected
arrival tests); per-part Loan facts now (a new representation for a SPEC-allowed precision gain; m-twice, m-pair and B1 (a) stay
conservative as §3 allows); a mode flag steering the transfer (`Refutes` is a published fact of the body that only skips work that cannot
change a reading).

## 5. What becomes unnecessary (deletions)

- Planned, never implemented (records and packages lose them): 2a0-D §7's rules 1 and 2 and its merge context (`RefutedAtResult`,
  `Ended` in `MirReport`); `MirFactKind.Result` and 2a0-D §8's R5 ask for it (the ask keeps Reason, Relation and `contained`, and gains
  `Cause`); D2's `ResultFit` row source (R4-M101 E6 deferred it to 2a-2: W reads carriers, not row sources; the comment in
  `Binding/TyCtxt.Origins.cs` loses "until 2a-2", a BU file, one line); 2a0-D §9's planned N-6 and DOUBLE records; the step-2 plan's 2g-5
  A5-F4 and A5-F7 escape parts (W derives them where the facts exist; 2g-5 keeps only the PreparedInput rule, which must not drop a
  problem another problem names as a cause: unreachable today, pinned by 2g-5); the round's structural result-temp pre-pass.
- Code: `MirOrigins`' order constraint and its "for 2a-2's rules" clauses; the three inline `OwnershipStorageLimit_Kd` constructions (2a-2d);
  R5-M96's arena interim and `Subject(item, SourceRef)` (R5, after 2a-2d).

## 6. Sites sharing the cause

- Every Loan-conflict recorder: `MirBorrows.Locations` (accesses), `.Activations` (activation, err(destroy)), `.Effects` (`Met`'s
  err(effect); R3's `Destroying` has no Borrow and records no cause), `.Checks.Ended` (err(ended)). All pass through `Conflict` or `Ended`
  and `Reports`, so W is one rule there.
- A5-F7's escape (2c-6 adds closure result rows): W applies unchanged once those relations exist; 2c-6 re-expects its tests.
- 2g-1's Opaque-site cause (R4-M10's): the same `Cause` fact with `Value` −1 (2g-D decides).
- G213's other half: every catalog argument a MIR code needs; the catalog names arguments only for `OwnershipStorageLimit_Kd` among MIR
  codes today (the others carry evidence only: `case`, the Origin relation's), so 2a-2d covers all.

## 7. Units

Order: 2a-2a → 2a-2b → 2a-2c → 2a-2d → records (2a-2e). 2a-2a–2a-2c form G:A5-F4's 2a-2 under Q6 form 2 (one group review with 2a-4 and
2a-5), except 2a-2b, which holds W1 files and takes its own R5-reviewed review (PD29). 2a-2d is G213's (R4, A1) with R5's review of the
fact shape.

| ID | Change | Tests (fail before unless "pin") | Done when | Size (H) | Hold |
| --- | --- | --- | --- | --- | --- |
| 2a-2a (bp) | `MirFact.Value` (long); `MirReport.Add` returns the index; step 4's order (Record before the Loan stratum); MirBorrows' values and states as pairs with I always empty (`MirBorrows.Causes.cs` holds the pair operations, contract first) | bp: diagnostic snapshot byte-equal; mir ledger raw rows equal as sorted multisets (only the order of a body's Origin and Loan problems changes); MIR classes unchanged; warm 0 B | the above, and the pinned A/B of §8 recorded | MA +70/−45 | — |
| 2a-2b | `MirLocalRole.Result` on the return target's result temp; `MirValidator`'s role rule; dump goldens and the MIR semantic snapshot re-expected (role text only) | `MirBuildTest`: a return leaving a cleanup scope has one Result temp, one without cleanup none, a block value's delivery temp none; `MirValidatorTest`: two Result locals, a Result of another Type → `InternalInvariant_Kd` | every changed golden differs only by ` (Result)` | M +10, goldens ±30 | W1 (`MirBuilder.Transfers.cs`, `MirVocabulary.cs`, `MirValidator*.cs`, `tests/mir`) |
| 2a-2c | §3.2–§3.5: I's rules, arrival, `Kept`, `Conflict`'s derived result, `Cause` facts, `MirOrigins.Causes.cs`, W7, the ledger rule; DIAGNOSTICS, DEC, ARCH | `T/MirBorrowsTest.Causes.cs` (new partial, the MIR-problem layer through `MirAnalysis`): **derived** (each asserts the `Cause` facts by subject): j-bad-v, row2-v, j-region, m-write4-v (SPEC's `store`), m-store-v, jregion2 and two-flows-v (two causes), loop-store-v (both conflicts), id-return-v (call clause into the result), rv-g-param (err(ended), call clause into E; S and P stay), rv-g-param-i (read-through clause), keepA (storage end derived, write independent), rf-block/if/match-v2, and the ledger shapes e3c4d30c20af, 0433a531b1ba, 38df74b325ce, 27e4888a2ae9 and d4a09871a00b (a call writes a local through `x@uniq`, which is then returned: the write-through row) (§12); **independent, pins**: m-write (SPEC's `keep`), its `var` forms and m-write3, m-scope, m-scope-use, m-twice-v, same-root-v, m-pair-v, fK, r3-b1c, r3-b1d-v, rc-fT-v, rc-flow, r2-b1a-v, r2-b1b-v, g-two, g-return, cf-g-reassign, rv-g-annot-esc, no-twin, the container call (`target@follow.append(local@ref)`, OQ-W2), a local slot written by a call (6227f1ba89a2), a part read of a call result (6d75d7b343f0); **repair**: j-bad-v.r1 and m-store-v.r1 lose the conflict; **views**: a pair-binder body whose Refuted chain exists in one class only keeps the conflict independent (if constructible; else recorded unverified); `MirReportTest`-level: a derived and an independent report of one key merge independent, two with different causes merge independent; `MirDifferenceTest`: a derived MIR problem pairs with an old record, and is dropped without one; V16 and warm rows of §8 | every §3 row (var forms) reads as §12; MIR problem multisets equal to 2a-2b's by (code, subject, body, Reason) (labels only); default pipeline untouched (snapshot byte-equal); mir ledger: the derived a5-new Loan rows leave the MIR side (11 of the 23 Loan problems of the Binding-complete programs with a MIR Refuted relation on `65be867e9`'s ledger, re-measured at the unit's base), none goes from both to old; the 12 that stay are classified: 7 SPEC-independent, 5 container calls (OQ-W2; 4 of them in closure bodies, which also wait for 2c-6's environment classes) | MA +200/−15, ledger +12, T +380 | ledger (shared file, R5) |
| 2a-2d | §3.6: `MirDataflow.StorageLimit`, `MirStorageTable`, the three callers; `Incomplete` with `SourceInfo` and the index; DIAGNOSTICS' storage-limit sentence for the MIR | `MirInitTest`, `MirBorrowsTest`, `MirOriginsTest` arena tests assert the three facts (fail before: no facts); `MirDropTest`: `Incomplete` names the report index of A1's arena problem and −1 for an inline copy's | every arena problem carries its arguments; R5's gate unit can drop its interim | MA +30/−12, T +60 | `MirInit.cs`, `MirDrops.cs` (R4's) |
| 2a-2e (doc) | PLAN (G213 closes on R4's side, R5's gate row), STATUS (A5's derived conflicts; the conservative readings), CODEMAP | — | — | docs | — |

Verification per unit (AGENTS): Unit runs of the MIR classes (`MirOriginsTest`, `MirBorrowsTest`, `MirAnalysisTest`, `MirBuildTest`,
`MirDifferenceTest`, `MirDumpTest`, `MirInitTest`, `MirDropTest`, `MirEffectsTest`, `OriginRowsTest`), `-Pipeline mir` ledger for 2a-2a
and 2a-2c (registered heavy runs, bases from `shared-base`), the diagnostic snapshot for 2a-2a, the session at the end.

## 8. Cost and V16

Measured on the prototype (§12), A5's Loan stratum (`MirBorrows.Run` over every view of the shape body), base `c546f636a`+dev vs B:

| Shape (N = 32 / 64 / 128) | Work base → B | Bytes base → B |
| --- | --- | --- |
| copies (N Loans, 8 whole copies each) | 71,639 / 255,815 / 961,511 → equal | 132 / 260 / 516 → equal |
| joins (two Loans per holder in a branch) | 35,672 / 118,473 / 425,195 → equal | equal |
| aggregates (nested Tuples read by parts) | 29,078 / 103,512 / 388,315 → equal | 2,376 / 8,840 / 34,056 → equal |
| calls (retaining call and copy) | 61,319 / 223,719 / 851,879 → equal | equal |
| ended (borrow/write pairs of one root) | 9,135 / 22,354 / 61,205 → equal | equal |
| escapes (N stores through a parameter: N S, N derived C) | 24,038 / 83,046 / 306,086 → +0.27 / +0.15 / +0.08 % | equal |

(The round's B1 on the first five: Bytes ×4, ×2.0, ×4.9, ×2.5, ×2.5; Work up to +52%.) Wall time, N = 256, interleaved A/B processes:
tiered JIT, min of 40 repeats × 6 processes, after the last two micro-changes (one content memo array, an inlined union fast path): B is
+5–11% (min), −1…+8% (median) on the six stress shapes; pinned with `DOTNET_TieredCompilation=0`, before them: +20–31%, of which W's logic
is ≈ 0 (a build with the logic disabled cost the same: the pair plumbing is the cost). `MirAnalysis.Run` over the library's 217 bodies
(all strata, tiered): +0.4% (min), −2.5% (median), within noise. An A/A run of two copies of the base build differed by ≤ 3%.

V16 rows (2a-2c; Work at 2× and 4×): "W halves": the aggregates shape with one Refuted relation in the body (W tracking on): Bytes within
c of the same body without it, Work linear; "W causes": the escapes shape: N derived conflicts, Work ≤ c·(base + N·causes). Warm rows:
`MirBorrowsTest`'s and `MirOriginsTest`'s allocation tests plus one with derived conflicts (the cause lists are pooled): 0 B. Speed bound
for 2a-2a's commit (its own measurement): Loan stratum ≤ +10% (tiered) on the V16 shapes and the corpus's MIR analysis within noise; else
the implementer profiles (the SuspendThread+ClrMD sampler) before landing.

## 9. Records

- **DECISIONS** (2a-2c's entry, R4-M1xx; the next free at writing is R4-M127): rule W on the MIR as §0's W1–W8, the transfer table, the
  conservative readings (one bit; part reads; F-a's container call; twins until H11), the deletions of §5, the departures of §11 OQ-W3, the
  rejected options of §4; superseding R4-M94's merge rules, D13's order, D18's `Result`, D2's `ResultFit` and 2a0-D §10's 2a-2 row (with
  R4-M104/R4-M122 already superseding the expectations). 2a-2b's entry: the `Result` role. 2a-2d's: G213's facts and `Incomplete`.
- **DIAGNOSTICS:** §4.3 "Later phases": "Other ownership problems of the function stay direct" gains: "except a MIR Loan conflict SPEC
  §15.6.5 makes caused only by Refuted relations, derived from those Origin relation problems (its `Cause` facts)"; the MIR Loan problems
  paragraph names the `Cause` fact; the storage-limit paragraph names the MIR's three facts (2a-2d). The invariant stays (OQ-W1 (a)).
- **ARCH:** the Loan stratum row (received halves, W), the Origin stratum row ("`Judge` and `Record` before the Loan stratum", the
  published causes and part classes), the MIR analysis problems row (`MirFact`'s Value and kinds, the merge of derived-ness), the driver's
  step 4; the `MirBorrows.Causes.cs` and `MirOrigins.Causes.cs` files.
- **CODEMAP:** the two new concern files. **PLAN:** G213's R4 side closes with 2a-2d; R5's gate row; G:A5-F4's position. **STATUS:** A5
  labels derived conflicts in shadow (no verdict); the conservative readings.

## 10. What R5 must confirm (H3, the gate, the shared files)

1. **The fact row** `MirFact(Kind, Site, long Value)` as H3's input, and the kind → record mapping of §3.5 (Arguments from `Required` and
   `Limit`, `table` from `Table`, related locations, evidence, `DerivedFrom` from `Cause`).
2. **OQ-W1:** publication of a derived problem as `PrerequisiteUnavailable_Kd` with its causes' check keys (no change to §4.3's invariant),
   or the Loan code with `DerivedFrom` (an invariant change, R5's contract).
3. **Causes by index:** H3 resolves a `Cause`'s index to the check key it publishes for that problem after R4-M71's cross-body merge, and
   applies W7's rule across bodies (a merged record is derived only when all its problems are). This extends AR-7 (MIR-to-MIR causes)
   beside 2g-1's Binding causes (`Value` −1).
4. **The ledger rule** (derived problems matched only where the old side has them) in `MirDifferenceLedger.ComparedSet.cs`, under the
   shared-file protocol, and whether R4 edits it in 2a-2c.
5. **W1:** a hold for 2a-2b (`MirBuilder.Transfers.cs`, `MirVocabulary.cs`, `MirValidator*.cs`, `tests/mir` goldens) and R5's review of
   the role.
6. **The gate (A-2c, G213):** `Incomplete` = (Code, SourceInfo, report index); R5's unit records `OwnershipStorageLimit_Kd` with the
   problem's facts, applies KD12 through `Subject(item, body, source)`, and deletes R5-M96's interim and the `SourceRef` overload.
7. That no H3 need beyond these exists for A5's codes (PD31's rows: Reason, Relation, Anchor, `contained`, Cause; Q18's Loan facts as
   today's Anchor/Use/Store/Reservation).

## 11. Owner questions

| ID | Question | Options | Recommendation |
| --- | --- | --- | --- |
| OQ-W1 | How is a Loan conflict W makes derived published once H3 publishes MIR problems? (R5's contract; DIAGNOSTICS §4.3) | (a) `PrerequisiteUnavailable_Kd` with the Refuted problems as prerequisites: §4.3 unchanged; it is suppressed whenever its causes are published, which is always while they are direct (W derives no relation; a relation 2g-1 derives from a Binding failure carries the suppression through that failure's record); (b) its own Loan code with `DerivedFrom`: the invariant "`PrerequisiteUnavailable_Kd` exactly when `DerivedFrom` is not empty" gains a MIR exception, and the record shows its Loan code where a cause is unresolved; (c) not recorded (rejected by R4-M104: nothing is dropped) | **(a)**: no contract change; both readings give the same published result while the causes are published |
| OQ-W2 | A call that stores a borrow into a container through a parameter or capture (`target@follow.append(local@ref)`; the closure forms `arr.append(local@ref)`) is derived by SPEC (the element part's Origin is fixed), but the MIR sees only the argument's referent Origin, so it stays independent (SPEC-legal) | (a) keep F-a's conservative reading, record it in STATUS, classify the ledger rows (5 on the measured base: 1 outside closures, 4 in closure bodies, which also wait for 2c-6's environment classes); (b) BU extends the retention view so each store row names its receiving Origin (`TyCtxt.Retention.cs`, after G160-2c), and A5 reads it (F-c); (c) A5 reads the relation row the call realizes (F-b: matches MIR sites to row Uses) | **(a)** now, with a PLAN row for (b) if the owner wants those rows closed before R6-check |
| OQ-W3 | Confirm the departures from owner-approved texts as one list (OQ6's pattern) | (a) confirm all: (1) 2a0-D D13's order → Judge, Record, Loan stratum; (2) D18's `Result` fact and D2's `ResultFit` are not added; (3) §7's merge context and rules → MirBorrows' `Cause` facts and MirReport's merge of derived-ness; (4) §10's 2a-2 row (tests, done condition, size) → §7 here; (5) the step-2 plan's 2g-1 ledger wording → the matched-only-where-old rule; (6) `MirFact`'s int → a long `Value` carrying causes and arguments (G213's "one shared representation"); (b) per item | **(a)** |

## 12. Probes (what ran)

All in `probes-b\`. The worktree's prototype is `w-b.patch` (+454/−85 in `src`, throwaway, with probe comments) and
`ScratchW2BProbeTest.cs.txt`.
- **Readings:** the round's 113 programs (`all\`, from `proto\cases\`) on the B prototype: every Loan problem's derived/independent reading
  equals the round's (`compare.py`, `compare-whole.py`: same 113, diff 0), with the builder role in place of the round's structural pre-pass, the paired halves
  in place of shadow elements, report-index causes, and both the exact (second `Tags`) and the one-bit `Kept` (identical readings).
  Causes name the expected Refuted problems (jregion2 and two-flows-v: two; rv-g-param-i: the candidate's, not `x`'s).
- **Tests:** ten MIR classes, 716 tests: only the 30 `MirDumpTest` goldens fail, each by the ` (Result)` role text; the warm allocation
  and scaling tests pass; the step-4 reorder changes no test (`classes-final*.log`, `classes-whole2.log` with the write-through row).
- **Ledger:** the 23 programs of `65be867e9`'s mir ledger (`shared-base`) with a MIR Refuted relation and an a5-new Loan row (24 rows, all
  side new, Binding complete; one Loan problem per program on B's base) re-run on B (`ledger\`, `ledger-whole\`): 11 derived (returns
  through locals, Reborrows and Slice handles, a generic call's result, stores through a parameter, and two locals a call writes through
  `x@uniq` that are then returned: derived only with the write-through row, independent under the round's "a local the call writes takes
  the Loan indirectly"); 12 independent: 7 SPEC-independent (a local slot a call writes that is read later ×2, a part read of a call result,
  three local arrays and one Option aggregate in closure bodies), 5 container calls (one outside closures, four `append`s into captured
  arrays; OQ-W2). The write-through row changes none of the 113 round readings (`compare-whole.py`).
- **Cost:** `Cost` (six shapes) and `Library` (the library's MIR analysis), A/B and A/A, pinned and tiered (`t*.txt`, `summarize.py`).
- **MIR shapes:** dumps of the return lowering (`cases\`, `small\`): `_8 = copy r; _9 = move _8; StorageDead…; _0 = move _9`, `_9` the
  return target's result temp at scope 0; guard candidates' Refs past a shared Deref form no Loan, only the marker.

## 13. Unverified

- The pair-binder per-view coverage (no probe body with several classes reached a conflict).
- The literal twin forms' readings after H11 (as their `var` forms, H).
- H3's publication (not prototyped; R5's).
- 2a-2d's facts (not prototyped); the gate's KD12 move for an inlined default outside the root modules stays unreachable (R5-M96).
- The speed bound on the full corpus (only the library and synthetic shapes were timed; machine load from other sessions widened the
  pinned medians).
- That no existing test asserts the absence of facts that `Cause` adds in other classes than the ten run (a Functional run settles it).
