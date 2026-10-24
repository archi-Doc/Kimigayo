# 2c-D package: C2 and A5-F7, a Closure call as a receiver call (merged; for the re-check and the owner's recorded approval)

R4 designer slot, 2026-10-25. Design only: no repository edit, commit or push; no probe was needed beyond reading (§11). Base
`origin/dev` `3f24d8d4b` (its `src/` equals the review's base `038f12ed4`; R4-M123's SPEC on dev). Inputs: `design-A.md`,
`design-B.md` and their probes, `review.md` (its §0 merge, B1–B5, N1–N14, §10, §11), `brief-design.md`, `brief-package.md`,
`..\s2-common.md`, AGENTS, SPEC §3.5.1, §7.7.2–§7.7.4, §15.3.4, §15.4.2, §15.6.3–§15.6.5, §15.8.2, DECISIONS R4-M99, R4-M119,
R4-M122, R4-M123, R4-M94, BU-27, BU-28, R4-M118 (on `claude/a5-2a-1c`), R5-M102, the A5 step 2 plan (rows 2c-0 … 2c-7, PD1, PD5,
PD6, PD13, PD19, PD29, OQ4, OQ7, AR-2, AB-2, AB-5), the 2a0-D package (§3 row 4, D19, its 2c-D row), the S3 plan, `p7-round.md`,
PLAN G65/G75/G147/G148/G216/G229/G240–G243, and the code each item names. `B/` = `src/Kimi/Compiler/Binding/`, `M/` =
`src/Kimi/Compiler/Mir/`. "(H)" marks a claim no run checked. §10 lists what this package decided beyond the review, with reasons.

The owner's answers (2026-10-25, verbatim): OQ-F7-1 「(a) すべて承認 (推奨)」, OQ-F7-2 「(a) 削除する (推奨)」, OQ-F7-3
「(a) メソッドと同じ規則 (推奨)」 (§9 records them). Pending, written in as recommended: D-10 with AB-2's adaptation (BU has
confirmed the adaptation) and the implementer split 「0c だけ BU (推奨)」 (R4 implements 2c-0, 2c-1, 2c-2, 2c-3B, 2c-5 and 2c-9;
BU only 0c).

Revision 2 (2026-10-25): the design reviewer's re-check (`review.md`, "Re-check of the merged package", on dev `937fd8148`: ACCEPT
WITH FIXES; BU-34 there changes nothing here) is applied: F1–F6, D-9's condition, D-13's probe forms, the census addition and D-10's
conditions (§12 lists the changes).

## 0. The package in brief

A Closure body is checked against its call receiver as a method body is against `self` (R4-M99, R4-M123). Every decision is a
receiver rule whose owner already exists; the merge adds no closure contract record, no closure view and no MIR-builder change.

| # | Decision | Owner (file) | Unit |
| --- | --- | --- | --- |
| P1 | **C2 is `BoundClosure`'s published facts**: captures, `EnvironmentType`, `Signature`, `Receiver` (the strongest, PD13), plus the per-case receiver facts (a capture bit and the case-free receiver, A's representation) and `Classified` | `B/Binding.ClosureReceivers.cs` (2c-0's split) | 2c-2 |
| P2 | **ρ = `OriginAtom(k, Projection, ClosureValueSlot)`**, the reserved atom of k's call receiver; no storage | Binding | 2c-3B |
| P3 | **Containment through the receiver layer**: a walk that reaches an environment binding's Place after dereferencing a layer stored in it meets ρ there (BU-27's open C3, G6); a Borrow of the binding itself keeps `env_i` | `B/Binding.PlaceOrigins.cs` | 2c-3B |
| P4 | **Nested-entry binding Type (review B1)**: a bare entry whose source is an enclosing Closure's environment binding takes the Reborrow child's Type, outer Origin `meet(o, ρ_outer)` (R4-M123's 7.7.2), by P3's rule | `B/Binding.Captures.cs` | 2c-3B |
| P5 | **One receiver resolution** at use (`OriginAtUse`, which the judge, the records, the old reporter and the C1-F4 view read): once k is classified, `env_i -> ρ` under a borrowed receiver and `ρ -> static` under `owner/Self`; before that both are **pending**, judged at classification by four rules (§2.5) | `B/Binding.OriginProof.cs`, `B/Binding.OriginClasses.cs` | 2c-3B |
| P6 | **Retention is `TyCtxt.Retains` with the receiver at input 0** (`R/Env during ρ`, `Env` by value under `owner/Self`); D1 is one walk row; the memo key holds the instance Closure Type (N6) | `B/TyCtxt.Retention.Calls.cs`, `B/Binding.Retention.Atoms.cs` | 2c-3 |
| P7 | **A5-F7's rule**: position 0 of a Closure callee's contract is the callee operand; stores into the receiver go through W (rule G, P13's hold); the temporary rule and A3 P4's Closure clause go; `HoldsLoans` walks a concrete Closure's components; PD13's hold is R4-M73's view hold | `M/Analysis/MirBorrows.CallRows.cs`, `.Calls.cs`, `M/MirTypes.Holding.cs` | 2c-3 |
| P8 | **Results after classification** (N5): each position the meet of its admissible sources; `static` with no source; the CallResult atom when every source is left out or rests on a failure; one `ResultFit` row per returned value and position at the returned value; `CallLocalOrigin` deleted (OQ-F7-2 (a)); the Closure keeps its Type (F2) | `B/Binding.ClosureResults.cs` | 2c-5 |
| P9 | **Value calls**: ρ substituted by the receiver acquisition's Origin, a returned Closure value's call signature through its closure context with the same map (F1); R1's `Unsupported_Kd` deleted in Binding; the default keeps its verdict through a program-level gate with R1's exact condition (OQ4 (a), OQ-F7-1 (vi)) | `B/Binding.ValueCalls.cs`, old Analysis (D5) | 2c-5 |
| P10 | **G216: h1, h2 valid** (SPEC 15.3.4): a Closure header's well-formedness forms no row of the evaluating body; the body assumes it, each call proves it (A's 2c-9) | Binding (header binding) | 2c-9 |
| P11 | **The old Emission's element-borrow check compares modulo every ρ operand** until R6-gen (D5 follow-up, OQ-F7-1 (v)) | `src/Kimi/Compiler/Emission/BodyLowering.Sequences.cs` | 2c-3B |
| P12 | **One role test** (`RoleOf`) replaces `Slot <= EnvironmentSlot(0)` at four sites (N3) | Binding | 2c-3B |

## 1. The rule (R4-M99 + R4-M123, as implemented)

SPEC 15.8.2 ¶2: a Closure body is checked against its call receiver (7.7.3) as an instance function's body is against its
receiver; its environment bindings are Fields of the receiver's referent, so result Origins, containment (15.6.3) and retention
(15.6.4) follow the receiver rules. Closure-specific, each stated once: receiver selection (7.7.3, per admitted case), permissions
(7.7.2: D1, retention into the environment follows them) and elision without the receiver (15.8.2 ¶4). F3 (b): a bare entry naming an
enclosing local or parameter has that local's Origin; a nested bare entry's child is the meet of the Origins that contain it.

| | Shared `ref/Self` | Exclusive `uniq/Self` | Consuming `owner/Self` |
| --- | --- | --- | --- |
| Borrow of a binding `b` (`b@ref`, `b.f@ref`) | contained in ρ: `env_b` resolves to ρ | same | body-local: `env_b`, Finite |
| Borrow through a captured `uniq/T during o` | `meet(o, ρ)` (ρ the first shared layer) | `meet(o, ρ)` | `o` (ρ resolves to `static`); the binding's storage end ends the child, its parent Loans stay |
| Borrow through a captured `ref/T during o` | `o` | `o` | `o` |
| Nested bare entry `[u]` of an outer binding `u: uniq/T during o` | inner binding `uniq/T during meet(o, ρ_outer)` under a borrowed outer receiver; `o` under a Consuming outer | | |
| Writable environment storage (15.6.4, D1) | none | `var` bindings, captured exclusive referents | captured exclusive referents |
| Result naming ρ (a Value row of the receiver) | carries the receiver Loan and what a read through the receiver carries (15.6.4 step 4) | same | — (ρ resolves to `static`) |
| Result naming only an Origin inside `Env`, a capture's or a fixed signature Origin (a Content row) | carries what a read through the receiver carries, not the receiver Loan (t2a valid) | same | a result naming any Origin of the owned receiver carries all its Loans (t5b) |
| Inferred result | each position outlived by every source; a body-local source Refuted at the returned value, a `static` source valid (D2); the Closure keeps its Type | | |
| Annotated result | ordinary elision without the receiver (t6b); no annotation name resolves to an environment binding | | |
| Header (15.3.4) | an omitted nested Origin is an evaluating-body variable fixed once; the header's well-formedness is a premise of the body and an obligation of each call (h1, h2 valid) | | |

Acquisition stays §7.7.3's with the strongest receiver (PD13); a call substitutes ρ by its receiver borrow. Forms (2x-P7's names;
SPEC verdict; the unit that gives it on the MIR, the default when different):

| Forms | SPEC | Unit |
| --- | --- | --- |
| f7a/622, f7b/1379, f7c/575, f7d/576, f7o4 | E (Loan) | 2c-3 (default already E) |
| c-f7c2, c-f7d2 | E | 2c-3 (default A: G75 E7, R6-check) |
| f7a2, f7o, x3, v1–v3, f7f, f7g (E), f7k1 (E)/f7k2, f7m, f7j1, cc1, rc1–rc4, s773, 130a–130g | as SPEC (unchanged, x3's false MIR conflict goes) | 2c-3 |
| v4 / c-f7c-ok, f7h2's uniq view | E on the MIR by rule G's self-store row (OQ-F7-3 (a)); the default accepts | 2c-3 |
| f7h2, f7i1, f7n2 | A (MIR false conflicts, G147 (8i)) | 2c-4 |
| f7n1 | uniq E: held by P13 after 2c-3 | 2c-3 |
| f7k3 (13972.1438) | E Loan@5 (MIR A) | 2c-1 |
| g6a (a live MIR use-after-free), g6b, g6d, g6e | E `UnprovenOriginRelation_Kd` | 2c-3B, both pipelines |
| g6c, t4e, t4f | A (keep; g6c keeps the default's conservative record) | 2c-3B |
| r1e, r1f, t4g, n2b | E `UnprovenOriginRelation_Kd` (both pipelines accept today) | 2c-3B |
| g65a, r1g, §15.8.2's `invalid` | E Unpr (Unsat today) | 2c-3B, both |
| h1, h2 | A (2a-1b's MIR U goes) | 2c-9 |
| t1a–t1d, t3a–t3d, t4a–t4c, t5, t5b, f7e1, f7e2, r1a–r1c, n1 | SPEC (U on both today) | 2c-5 on the MIR; the default's gate keeps R1's U |
| t7a, t7b, e2 (F2), t7c, e1 | E Unsat at the returned value, the Closure typed | 2c-5, both |
| t7d, e3 | one record at the returned value (W derives the storage end) | 2c-6 (MIR) |
| s-p3, s-p3d, s-p3e, s-k5, s-r4, s-r6 (G229) | A (MIR accepts; keep) | 2c-3; default G65 to R6-check |

## 2. The representation per component

### 2.1 C2: `BoundClosure`'s facts (2c-2)

`BoundClosure` already publishes captures (source, environment binding, acquisition, `let`/`var`, Type), `EnvironmentType`,
`Signature` and `EvaluatingBody`. 2c-2 adds, all written by `ClosureEffects` (moved to `B/Binding.ClosureReceivers.cs` by 2c-0):

- **The per-case receiver (PD13, A's representation).** `Receiver` keeps its meaning: the strongest requirement over every case,
  which acquisition (`AcquireValueCallee`), the body's receiver local, the ABI and R5 read. New: each capture's bit "a bare acquisition
  of this binding (in the body, or by a nested Closure's bare entry) makes the call Exclusive only in its exclusive cases"
  (`BareReborrows` by case), and the closure's case-free receiver (the requirement from every other use). In a view whose case Type of
  the callee is known, the receiver is the case-free one raised to Exclusive where a marked capture's case component is an exclusive
  layer: `TyCtxt.ClosureReceiverIn(case callee Type)`, a pure function that lands with its consumer in 2c-3 (§10 D-1).
- **`Classified`**, reset when a binding of k starts and set when `ClosureEffects` has run: the moment ρ and `env_i` stop being
  pending. `Classify` moves from after the Signature (`Binding.Closures.cs:430`) to right after the body is bound (after `:391`),
  before the early returns at `:393-396` and `:410` (N4 (a)); it reads neither the Signature nor `EnvironmentType`. It now also
  classifies a failed Closure (`Receiver`, `ExclusiveUse` set where they stayed defaults), and the MIR builder types a failed Closure's
  receiver local by `Receiver` (`M/Build/MirBuilder.cs:220-223`), so a failed program's MIR dump may change: the move is 2c-2's only
  if its bp triple is byte-equal; otherwise it is a behavior change and moves to 2c-3B with its snapshot classified (§10 D-9).

No `ClosureContract` record and no `TyCtxt.Closure` view: retention is `TyCtxt.Retains`' (§2.8), containment `PlaceOrigins`'
(§2.3), classes `OriginClasses`' (§2.4), the result the published signature's (§2.6). A separate view would restate retention and
result Origins for one callee kind, which R4-M99 §1 removed (OQ-F7-1 (i)).

### 2.2 ρ

`OriginAtom(k, OriginKind.Projection, ClosureValueSlot)` (`B/Binding.PlaceOrigins.cs:60`, documented as "the receiver of a call on
that literal"; nothing produces it today). 2a0-D row 5 already classes it Fixed in k's body and Finite (the literal's temporary) in
the evaluating body. P5 makes the first true only once k is classified.

### 2.3 Containment (P3) and the nested-entry Type (P4) (2c-3B)

One function gives an environment binding's own containment: for a root that is an environment binding of Closure k, ρ_k; for any
other root, none. A walk that reaches such a root after dereferencing an exclusive layer stored in it, with no shared layer ending it
first (C-ref: a captured `ref/T during o` stays `o`), meets that containment: BU-27's walk (`ContainedOrigin` over `NextPathStep`,
`MeetStep`'s Root step) and every naming site that names a Place reached through a layer stored in a binding, such as `PlaceOrigin`'s
direct-layer return, which names `p[0]@uniq` through a captured `uniq/Array` (g6a). The unit's commit lists the sites its search
found. A Borrow of the binding's own Place keeps the binding's atom `env_i` (unchanged). The walk names, P5 resolves: both names are
independent of the receiver mode, which the body's binding cannot know yet.

P4 reads the same function: in `CaptureEntry`, a bare entry that Reborrows (`CaptureAcquisition.Reborrow`, and `Bare` in its
Reborrow cases) a source that is an enclosing Closure's environment binding gets the source Type with its outer Origin met with that
binding's containment, `meet(o, ρ_outer)`; a source that is an enclosing local or parameter keeps its Type (BU-28, F3 (b)). Under a
Consuming outer P5 resolves `ρ_outer` to `static`, so the binding names `o`, as BU-28's SPEC reading note says. This is the exception
to BU-28's "the binding's Type is its source's" that R4-M123's 7.7.2 states. For a by-case (`Bare`) entry the meet applies to the
pair layer's outer-Origin slot in its borrow cases; if the Type cannot carry it, 2c-3B stops and reports (§10 D-14).

### 2.4 The receiver resolution and the classes (P5) (2c-3B)

- **Resolution.** `OriginAtUse` (`B/Binding.OriginProof.cs:189`), the one resolution every judge, record, the old reporter and the
  C1-F4 view read (2a0-D D4), gains one row beside Origin declarations and local initializers: at each Closure literal k on the use's
  ancestor chain whose `Classified` is set, `env_i(k) -> ρ_k` when k's receiver is Shared or Exclusive and `ρ_k -> static` when it
  is Consuming, through meets (`Meet` drops `static`). A use inside a nested Closure j passes j and then k, so each resolves its own.
- **Classes** (`B/Binding.OriginClasses.cs`, one predicate every reader shares): in k's body and the bodies nested in it, ρ_k is fixed
  once k is classified (a receiver Origin, 15.6.5) and never body-local; before that ρ_k and `env_i(k)` are **pending**, neither fixed
  nor finite; `EnclosingAtom`'s ClosureValue row (k's literal value, a root of k's evaluating body) applies only outside k. Under
  Consuming the resolution removes ρ_k, and `env_i(k)` stays body-local (Finite).
- **Consequences.** The view's row 4 (`env_i`) is met only in a Consuming body, where its present Finite class is the rule; row 5
  classes ρ Fixed; so the view needs no new class and the MIR receiver needs no Origin (no builder change, no W1 slot). The judge and
  the view read one rule, so G65's environment-borrow code family closes on both pipelines (g65a, r1g, §15.8.2's `invalid`:
  `UnprovenOriginRelation_Kd`).

### 2.5 Pending atoms: four rules (review N4) (2c-3B)

While k is bound its receiver is unknown (it is classified from the bound body), so a judgment that depends on it waits:

- **Fit.** A fit or relation whose end names a pending atom is fitted as one naming a local region is (`HasLocalRegion` reads it): its
  structure is checked now, its Origin positions become obligations (deadline BodyOrigins), and no verdict is recorded yet. Without
  this a Consuming body's valid `return u` fails at binding time (t4a, t4e, t4f, g6c: design B's rounds 3–6).
- **(a) Every path that bound the body classifies it.** 2c-2's move puts `Classify` before every early return after the body binds,
  so a failed result or parameter Type still judges the pending relations (an independent record, SPEC 23.1.2). A capture failure
  returns before the body binds, so no pending relation exists there.
- **(b) The outermost Closure judges, routing as binding time does.** An obligation that names pending atoms is appended, when
  formed (`AddObligation`, also when `obligationSet` already holds it), once per binding of its Closure, to the pending list of the
  outermost unclassified Closure among those whose atoms it names. `Classify(k)` takes k's list with the resolution active:
  - an entry whose obligation is no longer in `obligationSet` (removed in place: `B/Binding.LocalOrigins.cs:51-52, 98-99`,
    `ImplicitConstructors`, `OriginClauses`, `ConstraintValidation`, `OriginInheritance`) is skipped, or removed with its obligation
    (F2);
  - each other entry is routed as a binding-time judgment routes it (F3): a relation now fixed-fixed or Refuted is judged and its
    failure recorded as binding time records it (`FailExplained`, the same fact); a relation that stays a body chain (`BodyChain`)
    stays a row for A5 and the old reporter, unjudged here, so each failure has one record per pipeline (g65a: Binding's one record
    on mir, not Binding's and MirOrigins');
  - then the list is cleared. A relation in an inner body naming ρ_outer (n2b, f7j1, rc4) is routed once, at the outer's
    classification. The obligations stay for A5 and the old reporter; a judged failure leaves Binding incomplete, so the reporter never
    judges it twice.
- **(c) An unclassified Closure's pending atoms are Dependent in the view** (an R4-M118 row: never a source or a judged target; (F)
  publishes `InternalInvariant_Kd` in a complete compilation). With (a) no bound body stays unclassified, so the row is a guard. Until
  R4-M118 is on dev (it is on `claude/a5-2a-1c`, G:A5-F4) the view classes them Unclassed (row 9); whichever of 2c-3B and G:A5-F4's
  merge lands second makes them Dependent (§10 D-3).
- **(d) A binding's pending entries leave with it.** Each binding of k starts by removing from the open pending lists the entries
  formed in k's body or a body nested in k (their `Body` stamp), so a consultation's binding (`BindClosureArgument`'s
  `BeginConsultation`) or any rebinding leaves the lists with it, and the new binding appends its own, once each (an obligation
  `obligationSet` deduplicates is still appended). In a candidate trial fits are pure and form no entry. A list, not an index range:
  obligations are deduplicated and removed in place (`ResolveInitializerOrigins`, `InferLocalOrigins`), so indices are unstable, and
  (b) skips an entry whose obligation was removed (§10 D-2).

Cost: the lists are linear in the pending obligations; warm Bind 0 B (reused lists).

### 2.6 Result formation (P8) (2c-5)

In `B/Binding.ClosureResults.cs`, after classification (S3 gate R's timing item): `ResolveLocalResultOrigins` as today, then the
receiver resolution over the result. Each Origin position of an inferred result:

- is the meet of its **admissible** sources (fixed in k after resolution: an Input, a capture's or enclosing body's atom, ρ under a
  borrowed receiver, `static`); a body-local source (a local, a parameter's own storage, a temporary, `env_i` under `owner/Self`, a
  mutable static Borrow) is left out;
- is the Closure's **CallResult** atom (row 5, Fixed in k) when every source is left out or a source rests on a Binding failure; at
  a call that atom becomes `Unbound`, Dependent: an R4-M118 row ("a later site adds a row under it"; the call records nothing on it;
  R4-M118 reaches dev with G:A5-F4, which 2c-5 waits for) (F6); the Closure has its own record;
- is **`static`** (the meet of none) when the position has no source and none failed: no value flows there, so the contract is
  vacuous; never on a failure path (15.4.4) and never Unbound in a complete compilation (review N5).

Binding forms one `ResultFit` row per returned value and Origin position, from that value's Origin at the position to the published
position Origin, located at the returned value (its source nodes, captured before the result context's reset), and judges it in the
classification pass: an admissible source is Proven by the meet law, a body-local one Refuted at the returned value (G7, D2). The same
rows reach the C1-F4 view and MirOrigins (2c-6), where rule W derives the storage-end conflict of the same Loan (G8). `CallLocalOrigin`
and its failure of the Closure are deleted (OQ-F7-2 (a)): the record is Binding's at the returned value on both pipelines, the
Closure keeps its Type (F2: no `Never` result, no derived `NotCallable_Kd`/`UnresolvedBinding_Kd`, no validator finding).
`CallResultOrigin` stays (its display "call result" when the shorter end is the CallResult atom). An annotated result keeps its
ordinary fit (f7e1's `invalid`).

### 2.7 Value calls (P9) (2c-5)

`BindValueCall`: a callee result naming ρ, at any position (a Closure Type's environment components included: n1), is substituted
by the receiver acquisition's Origin: the callee Borrow's `PlaceOrigin` for a Shared or Exclusive call on an owned Closure, the
Reborrow's `AdaptedType` Origin for `uniq/F` (`AcquireValueCallee` publishes it, BU-27), the reference's own Origin for `ref/F`; a
Consuming callee's result names no ρ (P5). CallResult becomes `Unbound`. Deleted: `dependent` (`B/Binding.ValueCalls.cs:464-472`) and
R1's two failures (`:563-574`); `HasEnvironmentOrigin` loses its failure use and stays as the gate predicate's reader;
`ResultFixedForCall` (`B/Binding.FunctionTypes.cs:276`) is kept whole as the gate's input until R6-check (a predicate read from C2's
rows would also gate Copy-carried results the old path accepts, `ConcreteResultsKeepFixedExternalOrigins`). Retention at the call
follows P6: the result names the receiver input's outer Origin, so it carries the receiver Loan (f7e2's `get@move`, t1b–t1d).

**A Closure value in a call's result (F1).** `SubstituteStoredOrigins` rebuilds a Closure Type's components but keeps its
`ClosureContext` (the `Intern` at the end of its walk, `B/Binding.Storage.cs`), and `ClosureSignature`
(`B/Binding.ClosureInstances.cs:13`) applies only that context to the definition's `Signature` (the definition's own when there is
none). After P4 an inner Closure's signature names ρ_outer, and so does an inner `[x@ref]`/`[x@uniq]` entry of an outer binding once
`env_x(outer)` resolves to ρ_outer. 2c-5 therefore substitutes the returned Closure value's call signature with the same map, through
the closure context `ClosureSignature` already applies (a context `Subst` carries Origins, as generic contexts do). Without it a caller
of the returned Closure (`a@move()` in n1) sees ρ_outer, the outer literal's temporary (row 5) or another body's atom: n1 stays
rejected, but a valid variant fitting the inner call's result to a fixed Origin gets Binding's false Refuted or a row-9 U on the MIR.

### 2.8 Retention: `TyCtxt.Retains` with the receiver at input 0 (P6) (2c-3)

- For a Value callee whose `SignatureOwner` is a Closure, `Retains(BoundType callee, BoundType signature, definition)`
  (`B/TyCtxt.Retention.Calls.cs:45`) computes over `[receiver, parameters…]`: the receiver `R/Env during ρ` (R = `Receiver`, the
  strongest; `Env` the callee operand's instance Closure Type), `Env` by value under `owner/Self`. Position 0 matches a method
  receiver (`MirCallee.Position`) and R5-M102's ABI (environment first) (N7). A Function-typed or Callable-bound value keeps C8's
  contract (no receiver input; SPEC 8.10). One predicate, owned by TyCtxt, says whether a Value callee's contract has the receiver
  input; `Retains` and MirBorrows' position mapping read it (§10 D-6).
- The walk is the method walk: a borrowed input's referent is Content, writable at invariant positions when exclusive; a by-value
  input's parts are writable past exclusive layers. **D1's row** (in `RetentionParts`, `B/Binding.Retention.Atoms.cs:139`): a
  Closure-kind component whose environment binding is `let` (`BoundClosure` capture's mutability) is walked covariant from its own
  position, so the binding is not written and what an exclusive layer inside it refers to is. The referent's atoms include the
  Closure Type's fixed signature Origins (SPEC 15.2.3, O-own), through one OwnedOrigins function BU's F1 unit reads too (whichever
  lands first adds it).
- **Memo key (N6):** `(instance Closure Type, null, signature)` for a Closure callee (the owner is the Type's symbol), so generic
  instances with equal signatures and different environments get their own rows; an erased callable keeps `(signature, null,
  ConstraintScope)`. No allocation on lookup (§10 D-7).

### 2.9 What a Closure value holds (2c-3)

`MirTypes.Holds` (`M/MirTypes.Holding.cs:159`) answers `Loan | Write` for every Function or Closure value. With stores into the
receiver, W would then step past every Closure holder into all it holds, `Φ_E` included (f7a2, v1–v3 false conflicts in both
prototypes). A concrete Closure is walked by its components (its environment Types, IMPL 21.1.2: a Tuple of them); a Function value
stays opaque. `HoldsLoans` has one reader (`MirBorrows.Reach`).

### 2.10 A5-F7's rule in MirBorrows (P7) (2c-3)

- `CallRows.Rows`: for a callee whose contract has the receiver input, row input or output 0 is the callee operand and input i ≥ 1
  is argument i-1; other callees unchanged. G136's Value-callee arity: the token's inputs equal the operands plus the receiver.
- `Calls.Call`: the callee operand's value is input 0 (read before the Moves); a store row into it writes W of its value: for a
  reference receiver layer 1 is the Closure holder and deeper layers the captured referents; for a moved receiver what its value
  names. Rule G (R4-M73, no exclusion), P13's hold and the empty-value rule apply unchanged: this is **ER-closure-stores** (A3's
  erratum, numbered at adoption, PD4: A5-F7's capture stores are W's stores into a Closure call's receiver input, whose rows are
  `TyCtxt.Retains`'; A3 §5's F7 line and P4's Closure clause are replaced). The temporary rule (`M/Analysis/MirBorrows.Calls.cs:40-41`)
  goes.
- **Rule G's self-store row reaches Closures (OQ-F7-3 (a))**: the receiver's Content-to-receiver row (a method's `self` has it)
  rejects `[holder@uniq]` forms that store into `holder`'s referent and read `holder` after the last use (v4, c-f7c-ok, f7h2's uniq
  view), and adds a second record to f7c, f7d, as dev's MIR does for the method analog. GXS (G148 (2)) stays unscheduled; 2c-3 reports
  the ledger count, and the planning session returns to the owner if library, milestone or corpus forms appear.
- **PD13 (OQ-F7-1 (iii))**: at a Value call whose acquired receiver (`Receiver`) is stronger than `TyCtxt.ClosureReceiverIn` of its
  view's case callee Type, the view is held (R4-M73's existing hold: every Loan problem of the view a located `Unsupported_Kd` at its
  access). Retention uses the strongest receiver too; the hold covers a view whose case admits less. Body side: k is checked with the
  strongest receiver in every view, conservative by reading (H). Removal: per-case callee acquisition, PLAN G13.

### 2.11 MirOrigins (2c-6)

MirOrigins judges 2c-5's `ResultFit` rows (Refuted at the returned value; rule W derives the storage end, one record for t7d, e3) and
reads rows 4 and 5 through the resolution, with no new class. It waits for G:A5-F4 (2a-1a–2a-2 are not on dev, N14).

### 2.12 One role test (P12, N3) (2c-3B)

`Slot <= EnvironmentSlot(0)` also matches ρ (and `HelperSlot`/`ReceiverSlot`, whose binders are never Closures). Each site reads
`RoleOf` (`B/Binding.OriginRows.cs:125`, the view's and `EnclosingAtom`'s) for the roles it means: the "closure: call receiver"
displays (`B/Binding.CallableProofs.cs:22`, `B/Binding.Diagnostics.cs:243`) Environment or ClosureValue; `HasEnvironmentOrigin`
(`B/Binding.ValueCalls.cs:243`, read by R1, by erasure and Callable evidence at `B/Binding.Calls.cs:913`) Environment or ClosureValue,
so a result naming ρ is receiver-dependent (7.7.4); `CallLocalOrigin` (`Binding.Closures.cs:87`, in `Binding.ClosureResults.cs` after
2c-0, deleted by 2c-5) Environment only, so ρ is never "call-local".

### 2.13 The gate (OQ4 (a), OQ-F7-1 (vi); D5) (2c-5)

R1 made Binding incomplete, so the old Analysis never ran on such a program, nor did the old reporter. The gate keeps exactly that:

- **Predicate (A's): R1's exact condition**, evaluated by `BindValueCall` where R1 failed: `dependent` (`!PerCallSignature &&
  !ResultFixedForCall`) or a per-call result for which `HasEnvironmentOrigin` (reading ρ, §2.12) holds. Over the published result
  of 2c-5 it selects the same calls (a result naming ρ, or an Origin a Non-Copy capture carries, is not fixed for the call); B's
  narrower "names ρ" misses the Consuming forms (t4a, t4b, t5, t5b, n1; review B5).
- **The final binding decides (F4).** Value calls inside a closure argument are bound under consultations and candidate trials, so a
  binding that is not kept must not gate. `BindValueCall` keeps the condition as a `[Transitional("R6-check")]` fact on the value
  call's plan, overwritten by each binding of the call, and lists the calls it set it on (reset per bind); the gate re-reads each
  listed call's plan and gates only where the final binding's fact holds (§10 D-5).
- **Shape (B's): program-level.** In `OwnershipAnalysis.Analyze`, right after the completeness check (`OwnershipAnalysis.cs:86`) and
  before `ReportUnprovenOriginObligations`, if any listed call's final plan holds the fact, the gate reports `Ownership.Unsupported`
  (code `Unsupported_Kd`, R1's span) at each such call and returns: nothing else of the program is analyzed, as with an incomplete
  Binding. R1's records keep code and span; the partition becomes Ownership. A per-call gate in `CallValue` is rejected (N1: the old
  Analysis would judge bodies it never judged; t3b gets a new `ComparisonLoanConflict_Kd`).
- Removal: R6-check with the old Analysis (H11 gains the fact, its list and `ResultFixedForCall`). The mir pipeline ignores it.

### 2.14 The old Emission follow-up (P11; OQ-F7-1 (v)) (2c-3B)

`BodyLowering.Sequences.cs:131-134` (`sameOrigin`) requires an element Borrow's Origin to be the receiver's (`ReferenceEquals`); once
P3 meets ρ, `view[1]@ref` through a captured `uniq/Array` is `meet(o, ρ)` and generation fails (`ClosureEnvironmentOriginTest`'s
`ElementBorrow` fixture). The check compares the two Origins' operand sets after dropping every operand whose role is ClosureValue
(meets of any arity, nested meets flattened; no interning) (N2, §10 D-15). `:159` compares two element Borrows' Origins, both with ρ,
and is unchanged. The unit's commit states its search of the old Emission and the old Analysis for Origin identity comparisons; the old
Analysis's ClosureValue reader (`OwnershipBody.Borrows.cs:561`) matches only an evaluating body's literal temporary, so ρ inside a
Closure body records nothing there (H; 2c-3B's snapshot). Removal: R6-gen.

### 2.15 G216 (P10) (2c-9)

SPEC 15.3.4 "Nested signatures": in an anonymous function's parameters an omitted Origin of a nested borrow layer is a body
inference variable, fixed once; 15.4.2's "an omitted Origin of a borrow layer nested in an input is an error" is the rule for named
signatures, which 15.3.4 excepts. So h1 and h2 are valid (review B4; B's 2c-H is rejected). A Closure header's well-formedness row
(`ι outlives π`) is not formed in the evaluating body: the body assumes it (R2 supplies its inputs' well-formedness) and each call
proves it (`RequireInputPremises` forms the call's rows; h2's "the call's rows do"). `AClosureHeadersOmittedInnerOriginIsAPinnedLimit`
(`OriginRowsTest.Classes.cs:147`) flips; the located U on 2a-1b's MIR goes when G:A5-F4 meets dev.

## 3. Deletions

| Deleted | Unit | Size (H) |
| --- | --- | --- |
| The MIR's temporary environment-to-result rule (`MirBorrows.Calls.cs:40-41`) and with it A3 P4's Closure clause (ER-closure-stores) | 2c-3 | −4, comments −6 |
| `HoldsLoans`' opacity of a concrete Closure (`MirTypes.Holding.cs:159`) | 2c-3 | ±0 |
| R1 in Binding: `dependent` and its two `Unsupported_Kd` (`Binding.ValueCalls.cs:464-472`, `:563-574`); `HasEnvironmentOrigin`'s failure use | 2c-5 | −25 |
| `CallLocalOrigin` and the Closure's failure (`Binding.Closures.cs:38-89`, `:431-436`) (OQ-F7-2 (a)) | 2c-5 | −55 |
| View row 4's interim (2a0-D §3 row 4 and D19's "Environment atoms are Finite until 2c-6") and its pin as an interim | 2c-3B | test rows |
| G216's pinned limit and the evaluating-body header row | 2c-9 | −8 |
| `Slot <= EnvironmentSlot(0)` at four sites | 2c-3B | ±0 |
| Planned and not written: `ClosureContract`, `TyCtxt.Closure(k)`, `TyCtxt.Closures.cs` (plan 2c-2, +170), the MIR-builder receiver Origin and its W1 slot (R4-M99 §6), S3's result formals and S3-0's gate R, 2x-R11 (`Binding.FunctionTypes.cs` does not grow), B's case word over pair binders, B's 2c-H | — | −170 planned |
| Kept to R6-check (not deleted): the gate, `ResultFixedForCall`'s Closure branch, `HasEnvironmentOrigin` as the predicate's reader. Kept: `LocalBorrow`/`LocalBorrowFinder` (read by `Binding.Diagnostics.BorrowSource`, N8), `CallResultOrigin` (display) | — | — |

## 4. Units, in order

Lane B: Binding (one implementer). Lane C: the A5 chain after G:A5-F4's merge (PD1). R4-M119: at most one unit changing a shared
representation at a time; 2c-2, 2c-3B, 2c-3 and 2c-5 each do (BoundClosure's facts; Binding's Origins of Borrows and the classes;
`TyCtxt.Retains`' inputs and the guard; the published result and value calls), so they run strictly one after another, 2c-3B before
2c-3 (it closes the live g6a; N9). 2c-0, 2c-1 and 2c-9 change no representation. Every unit follows VERIFICATION (Unit checks, a
Session at the end of a working session, heavy runs registered); letters as the plan's (bp, snap, +cen, +led, +V16, +warm, +cold,
sel, both). Tests are written failing first where a defect exists (named "fails before").

| Unit | Lane | Content | Files | Depends | Tests (fails before) and checks | Done when | Size (H) | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2c-0 | B | bp split by responsibility (N10): `ClosureEffects` (SPEC 7.7.3) to `Binding.ClosureReceivers.cs`; `CallLocalOrigin`, `CallResultOrigin` (SPEC 15.8.2) to `Binding.ClosureResults.cs`; `LocalBorrow`, `LocalBorrowFinder` to `Binding.OriginDiagnostics.cs` beside their reader's concern (N8, §10 D-8); each new file opens with its contract | `B/Binding.Closures.cs`, two new files, `B/Binding.OriginDiagnostics.cs` | — | ClosureReceiverClassificationTest, CaptureDiagnosticTest, ClosureResultOriginTest, ClosureEnvironmentOriginTest, BlockClosureInferenceTest, ArchitectureRulesTest; bp | snapshots byte-equal; no file over 5,000 tokens (counts in the commit); CODEMAP, ARCH name the files | move ±270 | R (BU) | BU |
| 2c-1 | B | G147 (3) as the plan's row: one exclusive-use predicate over the Place reached from a capture replaces the outer arms and `UsesReferentExclusively` | `B/Binding.ClosureReceivers.cs` | 2c-0 | reproducer 13972.1438 (`24028725b625`) errs at `let q = n` on the MIR (fails before); f7k3, f7k4; classification rows; snap (a) SPEC 7.7.3; +cen | 13972.1438 pairs | +10/−15, t+40 | R (BU) | BU |
| 2c-9 | B | G216 (§2.15) | the header binding (`B/Binding.Closures.cs` or its well-formedness site) | 2c-0 (file) | `AClosureHeadersOmittedInnerOriginIsAPinnedLimit` flipped: no header row in g's or k's slice, k's call rows prove it (fails before); a body needing the premise valid (`func [] (p: ref/(ref/i32)) -> ref/i32 => p@follow`); the named twin `func g(p: ref/(ref/i32))` stays `MissingOriginBinding_Kd`; h1, h2 both; snap; +cen | no Closure header row lies in an evaluating body | +5/−8, t±20 | R (BU) | BU |
| 2c-2 | B | C2's facts, publication only (§2.1): the capture bit and the case-free receiver, `Classified`, `Classify` moved before the early returns | `B/Binding.ClosureReceivers.cs`, `BoundClosure`, `B/Binding.Closures.cs` | 2c-1 | classification rows per case (f7l1, f7l2: owner case Shared, uniq case Exclusive; a nested bare entry of a pair capture), `Classified` across rebinds, a failed Closure classified (each early-return path); warm Bind 0 B; bp both | every admitted case's receiver is derivable from the published facts (tested through the facts; `ClosureReceiverIn` lands in 2c-3); snapshots byte-equal, else the `Classify` move leaves 2c-2 for 2c-3B with its snapshot classified (a failed Closure's receiver local, `MirBuilder.cs:220-223`; §10 D-9) | +45, t+60 | D+R: R4 implements; BU reviews, R5 for AR-2 | BU |
| 2c-3B | B | P2–P5, the four pending rules with F2's and F3's routing (§2.5), P12, P11 (§2.3, §2.4, §2.12, §2.14); the `Classify` move if 2c-2's bp triple was not byte-equal (D-9); closes G6 (g6a) and n2b, G5's codes; probes the receiver well-formedness premise with D-13's three forms | `B/Binding.PlaceOrigins.cs`, `B/Binding.Captures.cs`, `B/Binding.OriginProof.cs`, `B/Binding.OriginClasses.cs`, `B/Binding.LocalRegions.Types.cs`, `B/Binding.ClosureReceivers.cs`, `B/Binding.Closures.cs` (the reset at a binding's start), `B/Binding.Origins.cs` (AddObligation), the four role sites, `src/Kimi/Compiler/Emission/BodyLowering.Sequences.cs` | 2c-2 | g6a (MIR accepts a use-after-free: fails before), g6b, g6d, g6e Unpr both; g6c, t4a body, t4e, t4f valid; r1a–r1f, t4g, n2b Unpr (fail before: accepted); g65a, r1g, f7e1's `invalid` Unpr (Unsat before); f7j1, rc4, s773 valid; each error form also beside an unrelated Binding error (the record stays, 23.1.2); N4: (a) a failed parameter or result Type beside a pending failure, (b) n2b judged once at the outer, (c) a view row for an unclassified Closure's atom, (d) a closure argument bound under a consultation and again records each pending failure once; F2: an entry whose obligation is removed in place before classification is not judged; F3: g65a has one record on mir, Binding's (H until MirOrigins is on dev); D-13's probe forms: (i) `[var a, var b]` holding `ref/i32 during o` with `swap(a@uniq, b@uniq)` for a generic `swap<T>(x: uniq/T, y: uniq/T)` (instantiated input `uniq/(ref/i32 during o) during ρ`), (ii) `let p = a@uniq; p@follow = r` with a captured `r`, (iii) the same through `Option<ref/i32>`, each under Shared and Exclusive receivers (a false Unknown sends the premise to the planning session before merge); `OriginRowsTest` row 4: borrowed receiver Fixed/ClosureValue, Consuming Finite/Environment; `VerifiedSpecExampleTest`'s two §15.8.2 variants' old-path code Unsat → Unpr (SPEC's code, the (a) difference closes); `ClosureEnvironmentOriginTest`'s `ElementBorrow` (Emission); snap classified (OQ7 (a): 15.6.3, 15.6.5, 7.7.2, 7.7.4); +cen; sel; closure native families (TupleCapture, StructCapture, FixedArrayCapture, FunctionCapture, ContextualClosure, ClosureEnvironment) on both generators; warm Bind 0 B; a pinned warm Bind A/B over Milestones 1, 20, 33 (`OriginAtUse` is hot) | both pipelines give §1's verdicts and codes on G5's and G6's forms and n2b; no pending atom survives Bind | +120/−15, t+160 | D+R: R4 implements (the implementer split, §0); BU reviews the Binding part beside R4's reviewer for the view part | BU, D5 |
| 2c-3 | C | P6, §2.9, P7 (§2.8–§2.10): `Retains` with the receiver input, D1's row, the memo key, the receiver-input predicate and `ClosureReceiverIn` (TyCtxt); MirBorrows' mapping, W stores, arity, PD13's hold, the temporary rule deleted; `HoldsLoans` by components; ER-closure-stores | `B/TyCtxt.Retention.Calls.cs`, `B/Binding.Retention.Atoms.cs`, `M/Analysis/MirBorrows.CallRows.cs`, `M/Analysis/MirBorrows.Calls.cs`, `M/MirTypes.Holding.cs`, new `tests/.../MirBorrowsTest.Closures.cs` (PD19) | 2c-2, 2c-3B merged (N9), G:A5-F4 merged (PD1); after BU's G160-2c and ordered against BU's G240 unit (retention files) | `15.8.2-environment-retention` on the MIR (copyOut valid, `z@follow = 43` E); `MirBorrowsTest.Closures`: f7a/622, f7b/1379, f7c/575, f7d/576, c-f7c2, c-f7d2, f7o4 E (fail before: accepted); x3 valid (fails before: P4's false conflict); f7a2, f7o, f7f, f7g, f7k1/f7k2, f7m, f7j1, cc1, rc1–rc4, 130a–130g, G229's s-p3, s-p3d, s-p3e, s-k5, s-r4, s-r6 as SPEC; v1–v3 valid (the guard's regression); v4, c-f7c-ok E (rule G); f7n1 held; an arity row; a PD13 hold row (a view whose acquired receiver exceeds `ClosureReceiverIn`; a constructed body if no program reaches it, as R4-M73's held rows are tested); `RetentionTest`: the two closure rows renumbered (receiver at 0, the approved design's reason in the commit), Shared/Exclusive/Consuming rows, D1's `let` row, a fixed-signature-Origin row, a generic closure whose capture Type is T (N6); +V16 "F7 captures" (N captures × M calls, 2× and 4×; G243 not triggered); +warm with a Closure call; +cold; +cen (Milestone34); +led (the 4 ledger rows pair; rule G's self-store forms counted and reported, OQ-F7-3) | the 4 rows 2x-P7 names pair, no temporary rule remains, the count is reported | +80/−12, t+220 | D+R (PD29, §10 D-10): R4's A5 reviewer, BU for the Binding part in parallel (R4-M119), R5 (AR-2: input 0, the contract shape C8 and Value-call code generation share) | adv, BU, sh |
| 2c-4 | C | BU's 0c, the plan's row (owner-or-uniq pair carriers' self-store rows) | `B/Binding.Retention.cs` (BU) | 2c-3 merged (AB-2 adapted, BU confirmed); never before 2c-3: A3-closure would become unsound (plan risk row 5) | the plan's: RetentionTest; pair-cases on mir accept the eight (8i) programs in both views; A3-closure stays rejected; f7h2, f7i1, f7n2 valid on the MIR; +cen; +led | the 16 (8i) rows are gone | +10/−20, t±20 | BU's R + R4 | BU |
| 2c-5 | B | P8, P9, the gate (§2.6, §2.7, §2.13) | `B/Binding.ClosureResults.cs`, `B/Binding.Closures.cs`, `B/Binding.ValueCalls.cs`, `B/Binding.ControlFlow.cs` only if the result context's source nodes must outlive `FinishResult`, `src/Kimi/Compiler/Analysis/OwnershipAnalysis.cs` (D5) | 2c-3B, 2c-3 (the call's retention must read ρ), G:A5-F4 (`ResultFit`, 2a-2) | default: `ClosureResultOriginTest.AResultOverTheEnvironmentIsUnsupported`, `ClosureEnvironmentOriginTest`, `BlockClosureInferenceTest` keep R1's code and span (gate); mir: t1a–t1d, t3a–t3d, t4a–t4c, t5, t5b, f7e1, f7e2, r1a–r1c by SPEC (fail before: U); n1 a Loan conflict at the second `outer()` (fails before: U); F1: an n1 variant whose inner call's result is fitted to a fixed Origin is valid on both pipelines, and no outer-Closure atom appears in the caller's MIR; F4: a value call inside a closure argument bound under a consultation or candidate trial is gated by its final binding only; §15.8.2's `get@move` `ComparisonLoanConflict_Kd`; t7a, t7b, e2 typed, no `NotCallable_Kd`/`UnresolvedBinding_Kd`, no validator finding (fail before); the Refuted record at the returned value on both pipelines (t7a–t7c, e1, e2); a sourceless position `static` and an all-Refuted position CallResult → Unbound at the call; erasure: a result naming ρ is not erasable; spec-cases `15.8.2-bare-entry-origin` and `15.8.2-closure-dependencies` (MIR side to SPEC; the default's gate U stays a (b) row); snap classified (location moves 15.8.2, downstream records 23.1.2, N11); +cen; +led | no Binding `Unsupported_Kd` at a Closure call; `Binding.FunctionTypes.cs` does not grow (counts in the commit) | +115/−85, t+140 | D+R (PD29, §10 D-10): R4 implements; R4's reviewer, BU in parallel; R5 informed (the published result names ρ; code generation reads no Origins, R5-M102) | BU, D5 |
| 2c-6 | C | §2.11 | `M/Analysis/MirOrigins*.cs` | G:A5-F4, 2c-5 | `ClosureResultOriginTest`'s refutation theory on mir; f7e1, t7a–t7d, e1–e3 at the returned value; one problem per failure with W (t7d, e3: fail before, double); g65a, r1g Unpr on the MIR; S3's `f` and `g` (2a0-D's 2c-D row); +cen; +led | no Origin row on a Closure is unclassified | +20, t+60 | G:A5-F7 | — |
| 2c-7 | — | Records (§8): PLAN, STATUS, CODEMAP, ARCH, DIAGNOSTICS, `classification.tsv`, pair-case target sides (A3-closure, A8-capture, A9-*, B1-*), X-2 registration of PD13's hold and the closure `Unsupported` families outside A5-F7 (`MirBuilder.Values.cs:287`, `Binding.Captures.cs:105-121`), `Landed += F7`, ARCH's R4 row and PLAN §3; the family's closing review (S3-6's role), covering 2c-3–2c-6 end to end | docs | 2c-3–2c-6 | Doc + Evid: +led | the 5 blocking rows 2x-T2 assigned are gone; no closure signature is unclassified | doc | G:A5-F7 | — |

Order: lane B 2c-0 → 2c-1 → 2c-2 → 2c-3B → (2c-5 after 2c-3); 2c-9 any time in lane B after 2c-0 (first, if G:A5-F4's group needs
G216 before 2a-1a's exit, PLAN G216). Lane C, after G:A5-F4's merge: 2c-3 (after 2c-3B) → 2c-4 → [2c-5] → 2c-6 → 2c-7. The
shared-representation sequence is 2c-2 → 2c-3B → 2c-3 → 2c-5; 2c-9 and 2c-4 may run beside one of them in the other lane.

Implementers (the owner, 「0c だけ BU (推奨)」, confirmed): R4 implements 2c-0, 2c-1, 2c-2, 2c-3B, 2c-5 and
2c-9 in lane B and 2c-3, 2c-6 and 2c-7 in lane C; BU implements only 0c (2c-4). Reviewers: BU for every Binding part (2c-0, 2c-1,
2c-2, 2c-3B, 2c-9, 2c-3's Binding part, 2c-5) in parallel with R4's A5 reviewer (R4-M119); R5 (AR-2) for 2c-2 and 2c-3; no W1 slot
(no `MirBuilder` or validator change). Review forms (DEC Q6, PD29; D-10, the owner: 「(a) 単独で入れる (推奨)」): 2c-0, 2c-1, 2c-9
take one review (R); 2c-2, 2c-3B, 2c-3, 2c-5 one design and one review (D+R) each, and merge alone; 2c-4 is BU's, after 2c-3's merge
and never before it; G:A5-F7 (one group review) is 2c-6 and 2c-7, whose closing review covers 2c-3–2c-6 end to end (§10 D-10).

## 5. Default-pipeline acceptance changes (OQ7 (a); census per unit)

| Unit | Change on the default pipeline | SPEC | Census |
| --- | --- | --- | --- |
| 2c-0, 2c-2 | none | — | bp |
| 2c-1 | Closures that use a capture exclusively through a Place reached from it become Exclusive: their body conflicts (f7k3 already E) and calls on a `let` Closure (`InvalidAssignment_Kd`) | 7.7.3 | +cen, snap (a) |
| 2c-9 | none (h1, h2 already accepted; only the MIR's U goes) | 15.3.4 | snap |
| 2c-3B | rejections: r1e, r1f, t4g, n2b (`UnprovenOriginRelation_Kd` in the body); code: g65a, r1g, §15.8.2's `invalid` Unsat → Unpr; g6a, g6b, g6d, g6e: the old Analysis's conservative Loan (and g6e's CallActivation) → Binding's Unpr, same verdict; g6c unchanged; nested forms P4 newly rejects beyond n2b (an inner annotated `during u` over a nested bare entry under a borrowed outer receiver); erasure and Callable evidence: a result naming ρ is receiver-dependent | 15.6.3, 15.6.5, 7.7.2, 7.7.4 | +cen, snap (a) |
| 2c-3, 2c-4, 2c-6 | none (MIR only) | — | +led |
| 2c-5 | gated programs keep R1's code and span, partition Binding → Ownership; Binding records downstream of calls R1 failed appear (N11); the inferred-result Refuted record moves from the Closure to the returned value; F2's derived `NotCallable_Kd`/`UnresolvedBinding_Kd` go | 15.8.2, 23.1.2 | +cen, snap classified |

No library or milestone program is expected to change (H; both prototypes passed `MirCensusTest`); if one does, O3 applies.

## 6. Size, speed and allocation

- **Size (H).** Production about +415/−155 besides 2c-0's ±270 move (Binding about +325/−145 with BU's 0c, Mir +65/−12, old
  Analysis +15, Emission +10); tests about +700. The review's merge estimate was +300/−150; the difference is P4, the pending lists,
  P8's rows and keeping `ResultFixedForCall` (−70 that design B had counted). No file crosses 5,000 tokens: `Binding.Closures.cs` (4,420) shrinks in 2c-0;
  `Binding.FunctionTypes.cs` does not change; `Binding.Diagnostics.cs` (over the limit) changes ±0 (one role test).
- **Speed (H, to measure).** Bind: one meet per Borrow through a captured reference (interned); one `Classified` test per Closure
  ancestor in `OriginAtUse` (pinned warm A/B in 2c-3B); pending lists linear; one more Value-call input, memoized per (instance Closure
  Type, signature). A5: one operand per Closure call; Closures that hold no Loan stop being W holders. V16 "F7 captures" in 2c-3.
- **Allocation.** Warm Bind and warm MIR 0 B (reused lists, interning; A's 288 B was its prototype's environment read); cold Bind
  measured in 2c-3B and 2c-5.

## 7. Placement of the related items

| Item | Placement |
| --- | --- |
| G6 / BU-27's C3 / G226 C3, n2b | 2c-3B (both pipelines); PLAN G65's 2c-D sentence closes |
| G65 | environment-borrow codes close in 2c-3B on both; the rest (non-Place callable receivers, R4-M112's v3/w3/w4) old path, R6-check |
| G229 (class S) | MIR keeps accepting (2c-3's rows); the default's rejection stays G65, R6-check |
| G216 | 2c-9 |
| G147 (3) / (8i) | 2c-1 / 2c-4 |
| G75 E7 | MIR closes with 2c-3 (c-f7c2, c-f7d2); default R6-check; "maximal rather than per-case Closure receivers" is PD13's hold (G13) |
| G148 (2) GXS | unscheduled (OQ-F7-3 (a)); 2c-3 reports the count |
| G240, G242, G243 | a Closure contract the hold walk cannot complete is `CallRetention.Limit`, as for any callee (BU's G240 unit); 2c-3 is ordered against it and checks G243's growth in V16 |
| F1 (Owned proof lacks fixed signature Origins) | BU's PLAN row; 2c-3 and F1 share one OwnedOrigins function |
| F2 | 2c-5 |
| F3 | decided (b) (R4-M123): no change for enclosing locals; the nested entry is P4 (2c-3B) |
| S1015 | §7.7.4's erasure condition reads "names ρ" (2c-3B's role test, 2c-5); §15.6.4 item 1's capture store (a mutable-static Borrow stored through a capture) is Refuted once P42 lifts 2b-6 (b)'s `Unsupported_Kd` (X-2 list); `ValueCalls.cs:541`'s owner is Binding's value-call binding (today `:563-574`), replaced in 2c-5 by R4 with BU's review (PD6) |
| D2's `t8a` | P42's `Unsupported_Kd` until P42; then valid by P8's `static` source |

## 8. Records

- DECISIONS: R4-M126 (§9) with the approval; per unit, at implementation: 2c-2 (C2's facts and the per-case representation), 2c-3B (ρ,
  the root containment and the nested-entry Type as R4-M123's exception to BU-28's "source's Type", the resolution, pending atoms and
  their four rules, the role test, the Emission D5 follow-up with its removal at R6-gen; with BU), 2c-9 (G216's ruling by 15.3.4),
  2c-3 (C2's retention, A5-F7's rule, ER-closure-stores numbered as an A3 erratum, the PD13 hold), 2c-5 (result formation with F1's
  closure-context substitution, the deletions, the D5 gate on the final binding's fact and its removal at R6-check, and "CallResult
  becomes `Unbound` at a call" recorded as an R4-M118 row, "a later site adds a row under it"; F6), 2c-6 if it adds a decision.
- PLAN: G65's 2c-D sentence and G6/n2b at 2c-3B; G147 (3) at 2c-1, (8i) at 2c-4; G216 at 2c-9; G229, G75 E7, F1, F2, G13 at 2c-7;
  §3's holds per unit (adv, BU, sh, D5) and BU's G240/G160-2c ordering.
- STATUS: closure results and calls (R1's row) at 2c-5; f7o's default rejection stays spec-closure's row; G229's default limit
  unchanged.
- CODEMAP and ARCH (2c-0's files; the TyCtxt row: a Closure callee's contract has its receiver at input 0; the MirTypes row:
  `HoldsLoans` by components; the transitional gate fact and its list); DIAGNOSTICS (an inferred Closure result's Refuted source at the returned
  value; ρ's display "closure: call receiver").
- INTEGRATED (the planning session): the S3 plan's items mapped to these units (formals not built, S3-0's gate R moot, S3-6 as 2c-7's
  closing review); R4-M99's proposal §6 sentences superseded (frozen text, corrections recorded there).

## 9. DECISIONS entry (R4-M126; free on dev and every remote and local branch, checked 2026-10-25)

The owner's answers are in place (2026-10-25; committed as DECISIONS R4-M126); the planning session
replaces each with the verbatim answer, or revises the sentence if the owner differs, before committing.

```
- **R4-M126 A5-F7's design package (2c-D) approved: Closure calls as receiver calls in the existing owners** (owner, 2026-10-25,
  relayed by the R4 planning session, verbatim answers: OQ-F7-1 「(a) すべて承認 (推奨)」, OQ-F7-2 「(a) 削除する (推奨)」, OQ-F7-3
  「(a) メソッドと同じ規則 (推奨)」, on the review groups 「(a) 単独で入れる (推奨)」 (D-10, with AB-2's adaptation), on the implementers 「0c だけ BU (推奨)」
  (relayed by BU, confirmed in R4's dialog: 「はい、そのとおり」); R4-M74 OQ2, plan PD5) The package is `draft/Sketches/A5 Step 2/2c-D-package.md`
  (design B as the base with parts of design A, merged by the full-process design review, whose blocking B1–B5 it resolves, and
  cleared by the review's re-check, ACCEPT WITH FIXES, its F1–F6 applied; the designs and the review are beside it as `2c-D-design-A.md`, `2c-D-design-B.md` and `2c-D-review.md`).
  Implementers: R4 for every unit but 0c (2c-4), which is BU's. Units and review forms (DEC Q6, PD29): 2c-0, 2c-1 and 2c-9 one
  review each; 2c-2, 2c-3B, 2c-3 and 2c-5 one design and one review each, BU reviewing every Binding part beside R4's reviewer
  (R4-M119) and R5 2c-2 and 2c-3 (AR-2), each merging alone; 2c-4 is BU's 0c (BU's review and R4's), after 2c-3's merge and never
  before it; G:A5-F7, one group review, is 2c-6 and 2c-7, whose closing review covers 2c-3–2c-6 end to end. Order: 2c-0, 2c-1,
  2c-2, 2c-3B in lane B; 2c-3 after 2c-3B and G:A5-F4's merge, then 2c-4; 2c-5 after 2c-3; 2c-6 and 2c-7 last; 2c-9 any time in
  lane B. In short:
  - Rule (R4-M99, R4-M123): a Closure body is checked against its call receiver ρ (`ClosureValueSlot`'s atom) as a method body is
    against `self`. A walk that reaches an environment binding through a layer stored in it meets ρ (BU-27's C3, G6); a nested bare
    entry of an outer binding takes `meet(o, ρ_outer)` (R4-M123's 7.7.2; an exception to BU-28's "source's Type"). Once a Closure is
    classified, `OriginAtUse` resolves its environment atoms to ρ under a borrowed receiver and ρ to `static` under `owner/Self`;
    before that both are pending, fitted as local regions and routed at the outermost pending Closure's classification as binding
    time routes them (every path classifies; a binding's pending entries leave with it, entries whose obligation was removed are
    skipped; unclassified atoms Dependent in the view).
  - C2 is `BoundClosure`'s facts (the strongest receiver for acquisition, a per-capture bit and the case-free receiver for each
    case, `Classified`); retention is `TyCtxt.Retains` with the receiver at input 0 (D1: a `let` binding is not written; the memo
    key holds the instance Closure Type); MirBorrows maps input 0 to the callee operand and stores through W (rule G, P13);
    `HoldsLoans` walks a concrete Closure's components.
  - Results are formed after classification: each position the meet of its admissible sources, `static` with none, the CallResult
    atom when every source is Refuted or failed; CallResult becomes `Unbound` at a call, an R4-M118 row ("a later site adds a row
    under it"); one `ResultFit` row per returned value at the returned value. Value calls substitute ρ by the receiver acquisition's
    Origin, a returned Closure value's call signature through its closure context with the same map. G216: h1 and h2 are valid
    (SPEC 15.3.4).
  - Departures approved (OQ-F7-1 (a)): (i) no `ClosureContract`, no `TyCtxt.Closure` view and no MIR-builder receiver Origin;
    (ii) A3 P4's Closure clause retired (ER-closure-stores, an A3 erratum numbered by 2c-3); (iii) PD13's located `Unsupported_Kd` is
    R4-M73's view hold whenever a call's acquired receiver is stronger than its view's case receiver; (iv) S3's result formals are not
    built (S3-0's gate R moot); (v) the old Emission's element-borrow Origin check compares modulo ρ until R6-gen (D5); (vi) OQ4 (a)'s
    gate is program-level in the old Analysis, reporting `Unsupported_Kd` at each call meeting R1's exact condition (a Transitional
    fact on the value call's plan, read from the final binding) and analyzing nothing else, `ResultFixedForCall` kept as its input
    until R6-check.
  - OQ-F7-2 (a): `CallLocalOrigin` is deleted in 2c-5; Binding reports an inferred result's Refuted source at the returned value on
    both pipelines (G7 closes on both), the Closure keeping its Type (F2). OQ-F7-3 (a): rule G's self-store row applies to Closures as
    to methods (v4, c-f7c-ok, f7h2's uniq view rejected on the MIR; f7c, f7d get a second record); GXS stays unscheduled; 2c-3
    reports the ledger count.
  - Supersedes: the plan's 2c-2 row (`TyCtxt.Closure(k)`, `TyCtxt.Closures.cs`) and 2c-0's token reason (Q9); 2x-R11 (not needed);
    R4-M99's proposal §6 on D14's walk and the MIR builder; 2a0-D §3 row 4's interim and D19's "Environment atoms are Finite until
    2c-6"; PD13's "`CallLocalOrigin` stays until R6-check's H11"; the S3 plan's C2 formals; AB-2's "lands in G:A5-F7's dev merge"
    (2c-4 follows 2c-3's merge, BU confirming); PD6's "BU implements Binding acceptance rules" for 2c-3B (R4 implements it).
  - Rejected: design A's view row 4 per receiver with a builder receiver Origin and ρ judged before classification (two classes of
    one atom; false errors on valid Consuming bodies); design B's §15.4.2 error for h1/h2 and its narrower gate (the default would
    accept t5b and n1); rebinding a Closure body once its receiver is known; per-case callee acquisition (PLAN G13); the receiver at
    input n; a per-call gate in `CallValue`; PD13's per-Loan witness fact (OQ-F7-1 (b)); `CallLocalOrigin` until H11 (OQ-F7-2 (b));
    GXS before R4's exit or a hold for rule G's forms (OQ-F7-3 (b), (c)).
  - Numbering: R4-M126 is free on dev and every remote and local branch (R4-M118 on 2a-1c; R4-M120 and R4-M124 on 4b; R4-M125 on
    G241).
```

## 10. Decisions this package made beyond the review (each with its reason)

| ID | Decision | Reason |
| --- | --- | --- |
| D-1 | PD13's facts: a capture bit plus the closure's case-free receiver; `TyCtxt.ClosureReceiverIn` lands in 2c-3, not 2c-2 | A's "one bit" alone cannot tell whether Exclusive came only from marked captures; a TyCtxt function with no reader until 2c-3 is speculative (AGENTS: Short) |
| D-2 | Pending obligations kept in per-Closure lists, appended once per binding to the outermost unclassified Closure they name, reset per binding; entries whose obligation was removed in place are skipped (F2) | The review's "index range" is unstable: `obligationSet` deduplicates and `ResolveInitializerOrigins`/`InferLocalOrigins` remove in place; the list gives N4 (b) and (d) by construction and stays linear in the open entries (V16 in 2c-3B if many inner Closures in one large body show it) |
| D-3 | Unclassed (row 9) until R4-M118 is on dev, then Dependent | R4-M118's class is on `claude/a5-2a-1c`, not dev; 2c-3B may land first |
| D-4 | One `ResultFit` row per returned value and position, to the published position Origin (B), not only for left-out sources (A); "failure path" = a source resting on a Binding failure | Rows to the published meet are Proven by the meet law with no premise, and MirOrigins judges the same input Binding judged; forming rows only for left-out sources would decide the verdict before forming the row (a second copy of the judgment); rows to the CallResult atom would need a premise binding it to the meet |
| D-5 | The gate sits after the completeness check and before the old reporter; the condition is a Transitional fact on the value call's plan, overwritten per binding, with a list of the calls that set it; the gate re-reads each listed plan (F4) | With R1 the reporter never ran on these programs, so R1's records stay exact; the list costs nothing when empty, where B's pre-visit walks every body; re-reading the final plan keeps a discarded consultation or trial binding from gating a call (a false U on the default) |
| D-6 | One TyCtxt predicate says whether a Value callee's contract has the receiver input; `Retains` and MirBorrows' mapping read it | Otherwise MirBorrows decides by the callee Type what TyCtxt's contract already decided (a consumer deciding by syntax) |
| D-7 | Memo key `(instance Closure Type, null, signature)` | N6 asks for the instance Type; the owner is the Type's symbol, and a key without a boxed tuple keeps warm 0 B |
| D-8 | `LocalBorrow`/`LocalBorrowFinder` move to `Binding.OriginDiagnostics.cs` in 2c-0 | Their only reader is `BorrowSource` (N8); `Binding.Diagnostics.cs` is over the token limit and may not grow |
| D-9 | The `Classify` move is 2c-2's (bp), not 2c-3B's, on one condition: if 2c-2's bp triple is not byte-equal, the move is a behavior change and belongs to 2c-3B with its snapshot classified | Both `Classified` and N4 (a) need it before 2c-3B's pending atoms exist (AGENTS: Separate commits); but it now classifies failed Closures, whose receiver local the MIR builder types by `Receiver` (`MirBuilder.cs:220-223`), so a failed program's MIR dump may change |
| D-10 | The package's option (the reviewer recommends it; the owner chose it, 「(a) 単独で入れる (推奨)」): 2c-3 and 2c-5 take their own D+R reviews and merge alone; G:A5-F7 is 2c-6 and 2c-7. Conditions: BU confirms AB-2's adaptation (BU has); 0c lands only after 2c-3, never before (plan risk row 5: A3-closure would become unsound); 2c-7's closing review covers 2c-3–2c-6 end to end | PD29: a unit holding a W1, adv or BU file is outside its family's group; in the plan 2c-3 held no Binding file, now it holds adv and BU files, and the plan's 2c-5 row already held BU and D5 inside the group against PD29; R4-M119's serial order puts 2c-4 and 2c-5 between 2c-3 and the group's end, so a group hold would block BU's G160-2c, G240, G242 and G243 longest. Alternative (the review's first table, sound): keep 2c-3 and 2c-5 in G:A5-F7 with their holds in PLAN §3; the entry then lists them in the group and drops the AB-2 sentence |
| D-11 | 2c-9 after 2c-0 in lane B, first if G:A5-F4's exit needs it | It shares `Binding.Closures.cs` with 2c-0's move; PLAN G216 asks for it before 2a-1a's exit |
| D-12 | Roles per site (§2.12) | `HasEnvironmentOrigin` must read ρ while R1 stands (2c-3B before 2c-5), else ρ would reach a caller unsubstituted; `CallLocalOrigin` must not read ρ as call-local |
| D-13 | A's receiver well-formedness premise (2c-8: `OwnedOrigins(Env)` outlive ρ) is not built; 2c-3B probes three forms: (i) `[var a, var b]` holding `ref/i32 during o`, `swap(a@uniq, b@uniq)` for a generic `swap<T>(x: uniq/T, y: uniq/T)` (instantiated input `uniq/(ref/i32 during o) during ρ`); (ii) `let p = a@uniq; p@follow = r` with a captured `r`; (iii) the same through `Option<ref/i32>`; each under Shared and Exclusive receivers | Binding forms no well-formedness row for a Borrow's own Type ("the argument's own Type is well formed", `RequireInputPremises`), and no known form relates a capture Origin to ρ as the shorter end; a missing premise can only give a false Unknown, never an acceptance; if a probe shows one, the premise is a catalog row under the method receiver's rule (`Binding.OriginPremises`, a Closure body's R2), the planning session's before 2c-3B merges, not an owner question |
| D-14 | A by-case (`Bare`) nested entry meets ρ_outer on the pair layer's outer slot; if the Type cannot carry it, 2c-3B stops and reports | Same rule as P4; s2-common: a representation the brief does not give is a stop |
| D-15 | The Emission compare drops ClosureValue-role operands from operand sets, without interning | Covers meets of any arity (N2) and keeps old-path code mechanical |
| D-16 | Corrected sites: `BodyLowering.Sequences.cs:131-134` and `:159` (the review cited `:137`, `:164`) | The lines on dev |

## 11. Unverified and open

- No merged prototype: B's rounds 3 and 10 and A's prototype cover parts (P3–P5 without P4, the pending fit, judgment at
  classification without lists; P6–P7 with input n or 0); the review's gate probes show B5 and N1. Unrun: P4 and N4's rules (n1, n2b,
  nested and failed Closures, consultations), D-2's lists with F2's skip, F3's routing, P8's positions and rows, F1's closure-context
  substitution, the gate with F4's re-read, 2c-9, PD13's hold (no program known), D-13's premise question, MirOrigins with the
  resolution (2a-1a–2a-2 not on dev).
- By reading only (the re-check's): F1's false-Refuted variant (`SubstituteStoredOrigins`, `ClosureSignature`); F3's double record
  waits for MirOrigins on dev; D-9's bp claim and D-13's premise are their units'.
- Censuses, the full ledger, snapshots, warm and cold Bind, the pinned A/B, V16 and native families of every unit.
- Whether rule G's second record in f7c, f7d is classed as derived (W) or independent by the ledger (2c-3's count).
- C8's erased `uniq/F` receivers: an erased signature has no receiver input, and a Callable signature names no written Origin, so no
  argument can be retained into its hidden environment (H), noted for C8's owner (R5).
- Owner answers (2026-10-25): D-10 with AB-2's adaptation 「(a) 単独で入れる (推奨)」; the implementer split 「0c だけ BU (推奨)」 (confirmed). Recorded in DECISIONS R4-M126.

## 12. Revision 2: what changed after the re-check

| Item | Change | Where |
| --- | --- | --- |
| F1 | 2c-5 substitutes a returned Closure value's call signature through its closure context with the same map (`ClosureSignature`, `Binding.ClosureInstances.cs:13`); tests: an n1 variant fitting the inner call's result to a fixed Origin, no outer-Closure atom in the caller's MIR | §2.7, 2c-5's row, §8, §9 |
| F2 | Pending entries appended once per binding; an entry whose obligation was removed in place is skipped or removed with it; a test | §2.5 (b), (d), 2c-3B's row, D-2, §9 |
| F3 | The classification pass routes as binding time does (fixed-fixed or Refuted judged; a body chain stays a row); g65a one record on mir (H until MirOrigins) | §2.5 (b), 2c-3B's row, §9 |
| F4 | The gate reads the final binding: a Transitional fact on the value call's plan, overwritten per binding, re-read by the gate; a test | §2.13, 2c-5's row, D-5, §9 |
| F5 | §1's row split: naming ρ carries the receiver Loan and the read-through (Value); naming only an Origin inside `Env` carries the read-through (Content; t2a valid) | §1 |
| F6 | "CallResult becomes `Unbound` at a call" recorded as an R4-M118 row | §2.6, §8, §9 |
| D-9 | The `Classify` move stays 2c-2's only if its bp triple is byte-equal, else it moves to 2c-3B | §2.1, 2c-2's and 2c-3B's rows, D-9 |
| D-13 | Three probe forms under 2c-3B, each under Shared and Exclusive | 2c-3B's row, D-13 |
| Census | Nested forms P4 newly rejects beyond n2b, under 2c-3B | §5 |
| D-10 | The package's option with its conditions (BU confirmed AB-2's adaptation; 0c only after 2c-3; 2c-7's closing review covers 2c-3–2c-6) | §4, 2c-4's and 2c-7's rows, D-10, §9 |
| Implementers | R4 implements every unit but 0c (BU), as the owner answered (「0c だけ BU (推奨)」, confirmed) | §0, §4, 2c-2's, 2c-3B's and 2c-5's rows, §9 |
