# 2c-D design B: C2 and A5-F7, a Closure call as a receiver call

Designer slot B, 2026-10-25. Design only: no repository commit, push or record. Base `origin/dev` `a30f83887` (rule W in SPEC, R4-M122;
G160-2c, BU-30). Checked at the end against spec-closure's commit `a0db1ec96` (R4-M123, not merged): its §15.8.2, §7.7.2 and §3.5.1
text is what §1 assumes, and its F3 example is t4a (a Consuming call), not t4g. Throwaway worktree `bin/worktrees/2c-d-b` (removed at the end); probes, the prototype patch and every run in
`probes-b\` beside this file. Inputs: brief-design.md, s2-common.md, `p7-round.md` (read whole), R4-M99 and its proposal, the A5
step 2 plan (2c rows, PD13, PD29, OQ4, AR-2, AB-2), 2a0-D (§3), the S3 plan, DECISIONS BU-27, BU-28, BU-30, R4-M118, R5-M102, PLAN
G65/G75/G147/G148/G216/G229/G240, SPEC §3.5.1, §7.7, §15.2.3, §15.4.2, §15.6.3-§15.6.5, §15.8. The owner's F3 answer (b) applies.
"(H)" marks a claim no run checked. `B/` = `src/Kimi/Compiler/Binding/`, `M/` = `src/Kimi/Compiler/Mir/`.

## 0. The package in brief

R4-M99 says a Closure body is checked against its call receiver as a method body is against `self`, its environment bindings being
Fields of the receiver's referent. Design B implements exactly that sentence with the components that already own the method rules,
and adds no closure contract object and no closure view:

| # | Decision | Owner (file) | Unit |
| --- | --- | --- | --- |
| B1 | **C2 is the published closure definition (`BoundClosure`), extended by three facts:** the receiver per admitted case (the strongest kept, PD13), the receiver Origin ρ, and the call contract's result formed by the receiver rules after the receiver is classified. No `ClosureContract` record and no `TyCtxt.Closure` view | Binding, C2's producer file `B/Binding.ClosureContracts.cs` | 2c-0, 2c-2, 2c-5 |
| B2 | **ρ = `Projection[k#ClosureValueSlot]`**, the reserved atom PlaceOrigins already documents as "the receiver of a call on that literal" and the C1-F4 view's row 5 already classes Fixed in k's body. Nothing produces it today | Binding (PlaceOrigins) | 2c-2 |
| B3 | **Containment (G6, BU-27's open C3):** a Borrow reached through a reference held in an environment binding meets ρ (the receiver layer); a Borrow of the binding itself keeps the binding's atom `env_i` | `B/Binding.PlaceOrigins.cs` | 2c-3B |
| B4 | **The receiver resolution:** once k's receiver is classified, Binding's resolution at a use (OriginAtUse, D4, which the judge and the view both read) resolves in k's body `env_i -> ρ` when the receiver is borrowed (Shared, Exclusive) and `ρ -> static` when it is Consuming. Until then `env_i` and ρ are **pending** (neither fixed nor finite): a binding-time fit naming one is fitted as one naming a local region is (its Origin positions become rows), and when k is classified Binding judges the relations its body formed over them and records their failures as binding-time judgments do | `B/Binding.OriginProof.cs` (OriginAtUse), `B/Binding.OriginClasses.cs`, `B/Binding.LocalRegions.Types.cs`, C2's producer file | 2c-3B |
| B5 | **C2's call retention is `TyCtxt.Retains(callee, signature)` with the receiver as input 0** (`R/Env during ρ`, `Env` by value under `owner/Self`); D1 is one walk row: a `let` environment binding is no storage the call writes, its captured exclusive referents are | `B/TyCtxt.Retention.Calls.cs`, `B/Binding.Retention.Atoms.cs` | 2c-3 |
| B6 | **A5's write guard reads a concrete Closure's environment** (its components, as a Tuple's); a Function value stays opaque | `M/MirTypes.Holding.cs` | 2c-3 |
| B7 | **A5-F7's rule:** a Closure-typed Value callee's operand is input/output 0; stores into the receiver go through W; the temporary rule is deleted; PD13's caller-side located `Unsupported_Kd` | `M/Analysis/MirBorrows.CallRows.cs`, `.Calls.cs` | 2c-3 |
| B8 | **Value calls (2c-5):** ρ in a callee's result is substituted by the receiver acquisition's Origin; R1's `Unsupported_Kd`, `ResultFixedForCall`'s capture scan and `HasEnvironmentOrigin` go; OQ4's gate in the old Analysis | `B/Binding.ValueCalls.cs`, `B/Binding.FunctionTypes.cs`, old Analysis (D5) | 2c-5 |
| B9 | **Closure result rows (2c-6):** each returned source of an inferred result gets a row to the published result Origin at that source; a position with no admissible source is the closure's CallResult atom; the Closure keeps its Type (F2) | Binding (rows), MirOrigins judges them | 2c-5, 2c-6 |
| B10 | **G216:** a Closure header's omitted Origin of a nested borrow layer is SPEC 15.4.2's error (`MissingOriginBinding_Kd`), as in a named function's signature; today Binding binds it as an evaluating-body region | Binding (BU) | 2c-H |

What this removes: R1's receiver-dependent `Unsupported_Kd`, `ResultFixedForCall`'s capture scan, `HasEnvironmentOrigin`, the MIR's
temporary environment-to-result rule, the C1-F4 view's row 4 interim (it becomes the Consuming rule), G216's pinned located
`Unsupported_Kd`, the plan's `TyCtxt.Closure` view and `TyCtxt.Closures.cs`, its 2x-R4 split (already gone by Q9 and BU-30), the
plan's MIR-builder receiver Origin (no `MirBuilder` change, so no W1 slot). `CallLocalOrigin` stays the default pipeline's record
until H11 (PD13), but no longer fails the Closure.

Prototype: B2–B7 and the old-Emission follow-up of B3 ran on dev (`prototype-b.patch`, about +165/−20 without its two trace
hooks). On the 109 closure programs (2x-P7's 98 and 11 new) the MIR gives SPEC's verdict on every store case 2x-P7 lists (G1: f7a-f7d,
c-f7c2, c-f7d2, f7o, f7o4; the pair-capture forms f7h*/f7n* show P13's hold until 0c and the per-part guard; rule G adds a second
record to f7c/f7d and rejects v4, §3.6), and
Binding gives SPEC's verdict and code on both pipelines for the receiver-containment forms (G6, among them a use-after-free the MIR
accepts today) and the environment-borrow forms (G5: `UnprovenOriginRelation_Kd`, today `Unsatisfied…`); `MirCensusTest` (library,
44 milestones, goldens, SPEC variants) stays at zero; of 1,419 tests in 44 closure, capture, Origin and retention classes only the
five rows the design changes fail (§10).

## 1. Required meaning

R4-M99 with the owner's D1 and D2 (2026-10-25), F3 (b) (the binding of a bare-entry `[u]` has its source's Origin) and SPEC as
being integrated by spec-closure:

- **The body.** A Closure body k is checked against `self: R/Env` where R is k's receiver (§7.7.3: `ref`, `uniq`, `owner`) and Env's
  Fields are the environment bindings with their capture Types (BU-28: the source's Type; F3 (b): a bare entry's too). §7.7.2's
  permissions decide what the body may do (assign a `var` binding, use captured `uniq` referents, move out only when Consuming).
- **Containment (§15.6.3).** A Borrow through `ref/Self` or `uniq/Self` is contained in the receiver Origin ρ and in each borrow
  between, up to and including the first shared layer: a Borrow of a binding `b` is contained in ρ (so it is no body-local Borrow,
  §15.6.5); a Reborrow through a captured `uniq/T during o` in `o` and ρ; a shared Borrow through a captured `ref/T during o` in `o`
  only. Under `owner/Self` the bindings are locals of the call: a Borrow of one is body-local (Finite), a Reborrow through a captured
  reference is contained in `o` only, the binding's storage end ending the child while its parent Loans stay.
- **Results.** Annotated: ordinary elision, the receiver never named (§15.8.2 ¶3, OQ-C2 (a)). Inferred: each Origin is a per-call
  Origin outlived by every source of the returned values by the rules above (§15.8.2 ¶2); a source whose Origin is body-local is
  Refuted at the returned value (D2), a `static` source is valid (D2), the Closure keeps its Type.
- **Retention (§15.6.4).** Per argument, the receiver being an argument: a result naming ρ carries the receiver Loan and every Loan a
  read through the receiver carries (step 4); a result or writable storage that may retain an Origin inside the receiver's referent
  Type carries what a read through it carries. The writable storage of an Exclusive call is its `var` bindings and captured exclusive
  referents (D1), of a Consuming call its captured exclusive referents, of a Shared call none; a result naming a capture Origin of an
  owned receiver carries all of that argument's Loans.
- **Calls.** Acquisition stays §7.7.3's (PD13: the strongest receiver for acquisition); the call substitutes ρ by its receiver borrow.

| Case | Reads | Writes (retention outputs) | Result may name |
| --- | --- | --- | --- |
| Shared `ref/Self` | the environment | nothing | ρ (env-owned data), `o` of a captured `ref`, `o` and ρ through a captured `uniq`, inputs, `static` |
| Exclusive `uniq/Self` | the environment | `var` bindings, captured exclusive referents | as Shared |
| Consuming `owner/Self` | the environment | captured exclusive referents | `o` (Reborrow or moved-out reference), inputs, `static`; never ρ, never `env_i` (Refuted) |

The verdict table this gives is 2x-P7's SPEC column with two corrections (§9): F3 (b) makes t4a and t4e valid, and t4g stays an
error (`UnprovenOriginRelation_Kd`) because its shared Borrow through the captured `uniq` is contained in ρ (§15.6.3), not because of
§3.5.1; and h1/h2 (G216) are errors by §15.4.2.

## 2. What the representation lacks today (sites)

| # | Lacks or wrongly allows | Sites | Gap (p7 §5) |
| --- | --- | --- | --- |
| L1 | One maximal receiver; two copies of the exclusive-use rule | `BoundClosure.Receiver`; `B/Binding.Closures.cs` ClosureEffects `Visit` arms (:493-553) and `UsesReferentExclusively` (:563-602; `n@uniq` of a capture falls through) | G3, G13 |
| L2 | No receiver Origin anywhere: `ClosureValueSlot` has no producer; MIR local 1 is `R/Env` with no Origin (`M/Build/MirBuilder.cs:219-225`) | `B/Binding.PlaceOrigins.cs:59` | G5, G6 |
| L3 | A Borrow through a captured reference is named by the reference's layers only, the walk ending at the binding as at a root | `PlaceOrigins.MeetStep`'s `PathStep.Root` (:160), `ReferentOrigin` (:95), `PathReborrowOrigin` (:89) | G6 (BU-27's open C3) |
| L4 | `env_i` is body-local in every receiver mode (judge `BodyLocalIn`, view row 4 interim) | `B/Binding.OriginClasses.cs:114` (`BodyLocalIn`); view `ClassifyAtom`/`RootKind` on 2a-1c | G5, G65 |
| L5 | The published result names `env_i`, formed before the receiver is classified | `BindConcreteClosure` (:398-436: `ResolveLocalResultOrigins` at :400, Signature :417, Classify :430, `CallLocalOrigin` :431) | G4, G7 |
| L6 | Value calls cannot bind a receiver-dependent result: R1 | `B/Binding.ValueCalls.cs:464-472` (`dependent`), `:563-574`; `HasEnvironmentOrigin` (:203-245); `B/Binding.FunctionTypes.cs:276-355 ResultFixedForCall` | G4 |
| L7 | A Value callee's retention has no receiver | `B/TyCtxt.Retention.Calls.cs` `Retains(BoundType, BoundType, …)` (inputs = the signature's) | G1 |
| L8 | The MIR approximates by carrying the closure value into the result and stores nothing into the receiver | `M/Analysis/MirBorrows.Calls.cs` `Call` (:40-41, the temporary rule) | G1 |
| L9 | A5's write guard treats every Closure value as opaque ("may hold an exclusive reference"), so W from a closure holder reaches E through any captured caller borrow | `M/MirTypes.Holding.cs:159-162` | found by the prototype (§10) |
| L10 | `CallLocalOrigin` fails the Closure: `Never` result, derived `NotCallable_Kd`, MIR validator findings | `Binding.Closures.cs:431-436` | F2, G7 |
| L11 | A Closure header's nested omitted borrow Origin is bound as an evaluating-body `Inference` atom (named functions report `MissingOriginBinding_Kd`) | header binding in `BindConcreteClosure` under the local initializer's inference | G216 (G10) |
| L12 | Reserved-slot tests read `Slot <= EnvironmentSlot(0)`, which also matches ρ and `HelperSlot` | `Binding.CallableProofs.cs:22`, `Binding.Diagnostics.cs:243`, `Binding.Closures.cs:87`, `Binding.ValueCalls.cs:243` | — |

## 3. The design

### 3.1 C2: the published closure definition (B1)

`BoundClosure` already publishes captures (source, environment binding with its acquisition, `let`/`var` and Type), `EnvironmentType`
(a Closure-kind Type whose components are the binding Types), `Signature` and `EvaluatingBody`. C2 adds:

1. **The receiver per admitted case.** ClosureEffects (moved to `B/Binding.ClosureContracts.cs` by 2c-0) records, besides the strongest
   `Receiver`, the case conditions under which the call is Exclusive and Consuming (a case word over k's pair binders, Binding's
   obligation `Condition` encoding, which the view renumbers to a body's binders). Only a bare acquisition of a pair-layer capture
   makes them differ (§7.7.3); every other closure has one receiver. `Receiver` keeps its meaning: acquisition (PD13), the body's
   checks, the resolution (B4).
2. **`Classified`**, set after ClosureEffects runs in the current binding of k and reset at its start: the moment ρ stops being pending.
3. **The call contract's result** (2c-5): formed after classification (S3 gate R's timing item: today the Signature precedes it).

ρ needs no storage: it is the interned atom `OriginAtom(k, Projection, ClosureValueSlot)`.

Why no `ClosureContract` record or `TyCtxt.Closure` view (the plan's 2c-2): under R4-M99 every part of such a contract is a receiver
rule that a component already owns. Retention is `TyCtxt.Retains`' (B5), containment PlaceOrigins' (B3), classes OriginClasses' and
the view's (B4), the result the published signature's (B8). A separate view would restate retention and result Origins for one
callee kind, which is R4-M99 §1's list of closure-specific constructions. One fact moves: the receiver per case, which only C2 owns.

### 3.2 Containment through the receiver layer (B3)

BU-27's one walk (`ContainedOrigin` over `NextPathStep`) meets the Origins of the safe borrow layers from the last dereference
outward up to and including the first shared one, and names a Borrow reached through no layer by its root's atom. Under R4-M99 an
environment binding's Place is `(*self).b`: the receiver layer is one more layer above every binding. The rule:

- a Borrow whose walk reaches an environment binding after dereferencing a layer below it (a Reborrow or shared Borrow through a
  captured `uniq`, a view or a pair layer's exclusive case; an element of a captured `uniq/Array`) meets ρ there and ends;
- a Borrow of the binding itself (`n@ref`, `n.f@ref`, an implicit borrow of `n`) keeps the binding's atom `env_i`, as today.

Both names are independent of the receiver mode, which the body's binding cannot know yet (the receiver is classified from the bound
body). B4 gives them their meaning per receiver: `env_i -> ρ` under a borrowed receiver (the literal BU-27 name, "contained in ρ"),
`ρ -> static` under an owned one (no receiver layer: `meet(o, ρ) = o`). The implementation passes the met Origin into the walk at
the binding (ReferentOrigin and PathReborrowOrigin call `ContainedOrigin` with it) instead of a mode flag.

### 3.3 The receiver resolution (B4)

- **Resolution.** `OriginAtUse` (the one resolution every judge, record and the C1-F4 view read, 2a0-D D4) gains one row beside
  Origin declarations and local initializers: at a Closure literal on the use's ancestor chain whose receiver is classified,
  `env_i -> ρ` when the receiver is Shared or Exclusive, `ρ -> static` when Consuming, through meets.
- **Classes** (`Binding.OriginClasses`, shared by the judge and the view): ρ of k is fixed in k once classified (a receiver Origin,
  §15.6.5; it is no enclosing body's atom in k, unlike k's literal value seen from the evaluating body); it is never body-local; while
  k is being bound it is pending, neither fixed nor finite.
- **Pending atoms.** While k is being bound its receiver is unknown, so `env_i` (body-local only under `owner/Self`) and ρ (fixed
  only under a borrowed receiver) are pending: neither fixed nor finite. A fit whose ends name one is fitted as a fit naming a local
  region is (`HasLocalRegion` reads it): its structure is checked now and its Origin positions become rows (deadline BodyOrigins).
  Without this a Consuming body's valid `return u` fails at binding time (t4a, t4e, t4f, g6c: prototype rounds 3-6).
- **Judged at classification.** When ClosureEffects has classified k, Binding judges the relations k's binding formed whose ends name
  k's pending atoms (an index range of the obligations, so linear), with the resolution now active, and records each failure as a
  binding-time judgment records it (`FailExplained`, the same fact). The rows stay for A5. Deferring them to the old reporter instead
  would lose an independent record whenever Binding is incomplete (SPEC 23.1.2): with R1 still in place, §15.8.2's `invalid` lost its
  record (round 9); judged at classification it is `UnprovenOriginRelation_Kd` on both pipelines (round 10).
- **Consequences.** The view's row 4 (`env_i`) is met only in a Consuming body, where its present Finite class is the rule (R4-M99:
  "anchored in a local of the call"); row 5 classes ρ Fixed; R4-M99 §6's "contained in the receiver by D14's walk" becomes "resolved to
  the receiver Origin", so **the MIR receiver needs no Origin** and the builder does not change (no W1 slot). `g65a`'s `n@ref` returned
  `during p` becomes `ρ outlives p`, Unknown: `UnprovenOriginRelation_Kd` on both pipelines (today `Unsatisfied…`), the code SPEC
  gives (round 10: g65a, r1g, §15.8.2's `invalid`), so G65's environment-borrow code family closes and spec-cases' (a) difference on
  §15.8.2 goes. The judge and the view read one rule, so no pipeline keeps a second class for these atoms.

### 3.4 Retention: `TyCtxt.Retains` with the receiver (B5)

`Retains(BoundType callee, BoundType signature, definition)` already reads a Closure's or Item's signature under its declaration's
premises. For a Closure owner its inputs become `[receiver, parameters…]`, the receiver being `R/Env during ρ` (`Env` by value
under `owner/Self`; `Env` is the callee operand's Closure Type, an instance's for a generic closure, R its strongest receiver), and the walk is the method walk: a borrowed input's referent is Content and, when exclusive, writable at
invariant positions; a by-value input's parts are writable past exclusive layers. One row makes it D1's: in `RetentionParts`, a
Closure-kind component whose environment binding is `let` is walked covariant from its own position (not composed with the enclosing
invariance), so the binding is not written but what an exclusive layer inside it refers to is. Rows are per argument and memoized
per (signature, owner) as today; positions shift by one for Closure callees (the receiver is the callee operand, §3.6).

Verified rows (prototype): f7a's closure gives `1C->result 0V->result 1C->1 0V->1` (receiver at MIR position 1), the same shape as
its method analog `Env.call`. The self-store Content row on the receiver is the method's too (rule G).

### 3.5 The write guard reads a Closure's environment (B6)

`MirTypes.Holds` gives a Closure value `Loan | Write` without looking inside (written when no Binding fact said what a Closure holds).
With B5, W from a closure holder then reaches E through any captured reference to caller storage, and a Loan stored into the closure
is joined into E, live to the end: f7a2 (SPEC valid) and three smaller forms (v1–v3) are rejected. A concrete Closure's components are
its environment Types (C2's `EnvironmentType`), so the walk reads them as a Tuple's; a Function value keeps the opaque answer (its
hidden environment is unknown). `HoldsLoans` has one reader (`MirBorrows.Reach`).

### 3.6 A5-F7's rule (B7)

- `CallRows.Rows`: for a Value callee whose Type below its reference layer is a Closure, row input or output 0 is the callee operand,
  input i ≥ 1 is argument i-1; other callees unchanged (Function-typed and Callable-bound values keep C8's erased retention).
- `Calls.Call`: the callee operand's value is the receiver input (read before the Moves); `ret(receiver, κ)` as for any argument
  (Value: the value and a read through it; Content: a read through it); a store row into the receiver writes W of its value: for a
  reference receiver layer 1 is the closure holder and deeper layers the captured referents; for a moved receiver the holders its
  value names. G148's rule G, P13's hold (a store past a holder whose Type passes the guard only through a Type argument or pair layer
  holds the view: f7n1/f7n2 and the A3-closure forms show it) and the empty-value rule apply unchanged; this is ER-closure-stores
  (W with G148's adopted rule, which has no exclusion). The temporary rule goes.
- **Rule G's self-store row reaches closures.** The receiver's Content-to-receiver row (a method's `self` has it too) stores what a
  read through the receiver carries, the capture's own borrow Loan included, into a captured exclusive referent. So `[holder@uniq]`
  whose call stores into `holder`'s referent, followed by a read of `holder` after the closure's last use (v4), is rejected on the
  MIR: SPEC 15.6.4's per-argument letter, the class `MirBorrowsTest.ACallStoreFollowsSpecsPerArgumentRule` pins for functions
  (twoLayersRead: "rustc accepts … SPEC's letter rejects them (no loss)"), which G148 (2)'s GXS would recover. The default and dev's
  MIR accept v4; f7c and f7d get the same second record. 2c-3's ledger run counts these forms (§9 OQ-B3).
- **PD13, caller side.** A Loan the call's receiver acquisition issues is stronger than the receiver of the view's case when C2's
  case conditions say so; a Loan conflict whose only witness is such a Loan is the located `Unsupported_Kd` at the conflict (the
  checks' existing witness walk; no program in the library, milestones or 2x-P7 reaches it, H).
- **PD13, body side.** k is checked in every view with the strongest receiver, which is what its acquisition executes (a Consuming
  acquisition moves the closure in every case). Where an admitted case's own receiver is weaker the verdict can differ from SPEC's
  per-case reading in either direction; it is PD13's recorded limit (PLAN G13 with per-case acquisition), no program known.

### 3.7 Value calls and the result contract (B8, B9)

- **Formation (Binding, after classification).** `ResolveLocalResultOrigins` as today, then the receiver resolution (B4) over the
  result. A source Finite in k (a local, a parameter's own storage, a temporary, `env_i` under `owner/Self`) is left out of its
  position: it is CallLocalOrigin's record on the default pipeline (unchanged code and span) and a Refuted row on the MIR (below); a
  position left with no source is the closure's `CallResult` atom (row 5, Fixed in k). `CallLocalOrigin` records without failing the
  Closure, which keeps its Type (F2: no `Never` result, no derived `NotCallable_Kd`, no validator finding).
- **Result rows.** For an inferred result, Binding forms one `ResultFit` row per returned source (the result context's sources,
  captured before its reset) to the published result Origin of each position, at the source. The default pipeline does not judge
  them (CallLocalOrigin's twin, 2a0-3a's rule); MirOrigins does: a body-local source is Refuted at the returned value (G7), an
  environment Borrow under a borrowed receiver is `ρ -> ρ`, and rule W (R4-M122, 2a-2) derives the storage-end conflict of the same
  Loan (G8). An annotated result keeps its ordinary fit row (f7e1's `invalid`: Unknown, judged at classification by B4).
- **Calls.** `BindValueCall`: a result naming ρ substitutes the receiver acquisition's Origin (the callee Borrow's `PlaceOrigin`, a
  Reborrow's `AdaptedType` Origin, a payload projection's); `CallResult` becomes `Unbound` (the closure's own error; R4-M118: the
  call records nothing on it). `dependent`, R1's two `Unsupported_Kd`, `ResultFixedForCall`'s capture scan and `HasEnvironmentOrigin`
  are deleted; `ResultFixedForCall` keeps its non-Closure branch (`FixedInBody`).
- **Retention at the call** then follows B5: the result names ρ, the receiver input's outer Origin, so it carries the receiver Loan
  (f7e2's `let moved = get@move` is a Loan conflict, t1b–t1d, t3a, t3c).
- **Erasure and Callable proofs (§7.7.4 as R4-M99 rewords it).** "Its result contract does not depend on its receiver" reads "the
  published result names no ρ". The reserved-slot predicates read one role test (`ProjectionRole`, the view's `RoleOf`) instead of
  `Slot <= EnvironmentSlot(0)` (L12); the Origin displays stay "closure call receiver".

### 3.8 OQ4's gate (D5)

R1 made Binding incomplete, so the old Analysis never ran on such a program. The gate keeps exactly that: in
`OwnershipAnalysis.Analyze`, after the completeness check, if any value call's declared Closure signature names its ρ, the old Analysis
reports `Unsupported` (`Ownership.Unsupported`, located) at each such call and analyzes nothing else, as an incomplete Binding did.
Code and span equal R1's; the partition becomes Ownership. About 15 lines, a pre-visit over the bodies' call plans, deleted at
R6-check. Rejected: a gate inside `CallValue` (the rest of the body would then be judged by an unverified model, against D5; other
bodies' records would newly appear).

### 3.9 G216, G229, G65, G147 (3), F1–F3, G240

- **G216 (B10).** SPEC 15.4.2: "An omitted Origin of a borrow layer nested in an input is an error". `func g(p: ref/(ref/i32))` gives
  `MissingOriginBinding_Kd` today; `let h = func [] (p: ref/(ref/i32)) -> i32 => 1` is accepted, its inner Origin an `Inference` atom
  of the evaluating body (the local initializer's inference reaching into a nested callable's header). Fix: the header's parameter
  Types take 15.4.2's input rows; the error is Binding's, on both pipelines (OQ7 (a), 15.4.2). Census: no library or milestone
  closure has such a header (grep); the two pinned `OriginRowsTest` rows become the error's tests and G216's row and its located
  `Unsupported_Kd` go. 2x-P7's "A" for h1/h2 read the header as valid; that reading misses 15.4.2.
- **G229 (class S).** s-p3, s-p3d, s-p3e, s-k5, s-r4, s-r6 stay accepted on the MIR (prototype: unchanged), pinned by 2c-3's tests;
  their default rejection stays G65's old-path limit until R6-check.
- **G65.** Its closure part on the MIR: the environment-borrow code family closes in 2c-3B on both pipelines (§3.3); the rest
  (non-Place callable receivers, R4-M112's v3/w3/w4) stays old path, R6-check.
- **G147 (3).** 2c-1 as planned: one exclusive-use predicate over the Place reached from a capture.
- **F3 (b).** No implementation (BU-28's Type is the source's). t4a and t4e become valid by SPEC; G12 closes as conforming. t4g's
  error is §15.6.3's receiver containment (G6), not §3.5.1, and stays (§9 Q-A).
- **F1** (Owned proof lacks a Closure's fixed signature Origins) is BU's; C2's `Signature` and `EnvironmentType` are its inputs.
- **F2** is §3.7's "keeps its Type".
- **G240** applies to C2's retention unchanged: a Limit resting on a Binding failure in an environment Type is dependent, published
  by BU's G240 unit for every CallRetention.

### 3.10 `ValueCalls.cs:541`'s owner and the S1015 sections

The plan's `ValueCalls.cs:531-543`/`:541` is today `:562-575`, R1's result block in `BindValueCall`: Binding's value-call binding
owns it; R4 replaces it in 2c-5 with BU's review (PD6). S1015 (`72fb4b79`): review item 1's "the same store through a Closure
capture" (a mutable-static Borrow stored through a capture is Refuted, §15.6.4/§15.6.5) is 2c-3's retention plus the C1-F4 rows; its
MIR test waits for P42's static Borrows (2b-6 (b)), as 2a0-D records for item 1. PLAN §3's S1015 audit of §7.7.4 is 2c-5's erasure
predicate.

## 4. Options

| Option | Content | Assessment |
| --- | --- | --- |
| **A (B's choice): the receiver rules in their owners** | B1–B10 | One form per rule; the prototype gives SPEC's verdicts with about 185 changed lines; no closure-only component; default verdict changes are SPEC-conformance rejections and codes (§7) |
| B: a C2 view `TyCtxt.Closure(k)` (the plan's 2c-2), `ClosureContract` record | a closure retention and result computation beside `TyCtxt.Retains`, consumed by 2c-3 and 2c-5 | A second retention rule for one callee kind and a second result rule; R4-M99 §1 lists it as what the receiver formulation removes; +170 lines planned |
| C: S3's result formals | one Input-slot formal per inferred Origin position, contract bindings as premises of the body | Keeps the published result distinct from boundary expressions; needed only if a position must name an Origin no boundary expression gives; with B4 every admissible source is already a fixed Origin of the body, so the formal is needed only for a position with no admissible source (the CallResult atom) |
| D: the receiver Origin on the MIR only | `MirBuilder` types local 1 `R/Env during ρ`; D14's walk adds `ρ -> Ref` | A Reborrow through a captured `uniq` is named `o` (fixed): the edge `ρ -> o` is a false Unknown on valid t3b; fixing it in MirOrigins is BU-27's rejected STOP (B); Binding's own judge stays wrong |
| E: rebind a Closure body once its receiver is known | receiver-dependent names | Doubles closure body binding; stale obligations from the first binding; rejected for B4's pending atom |
| F: per-case acquisition | the builder emits a per-case Ref kind | PD13's rejected builder vocabulary change |

Options inside the owners considered and rejected: the receiver as a sentinel input `Retention.Receiver` (MirBorrows would index it
everywhere; position 0 is the ABI's environment position already, R5-M102); ρ always fixed in Binding (Consuming bodies fail at
binding time: prototype round 3); pending relations left to the old reporter (an incomplete Binding loses them: round 9); resolving
`env_i` to ρ in the published result only (the judge and view would keep row 4's interim).

## 5. Deletions

| Deleted | By | Size (H) |
| --- | --- | --- |
| R1's receiver-dependent `Unsupported_Kd` (`ValueCalls.cs:464-472`, `:563-574`), `HasEnvironmentOrigin` + `EnvironmentAtom` | 2c-5 | −45 |
| `ResultFixedForCall`'s Closure branch and capture scan (`FunctionTypes.cs:276-355`, its Closure branch) | 2c-5 | −70 |
| The MIR's temporary rule (`MirBorrows.Calls.cs:40-41`) | 2c-3 | −4 |
| Row 4's interim and its pinned test as an interim; G216's pinned `Unsupported_Kd` row | 2c-3B, 2c-H | test −2 rows |
| The plan's `TyCtxt.Closures.cs`, `TyCtxt.Closure(k)`, the MIR-builder receiver Origin, a W1 slot | (not written) | −170 planned |
| `CallLocalOrigin`'s failure of the Closure (the record stays until H11) | 2c-5 | −3 |
| `Slot <= EnvironmentSlot(0)` tests (four) | 2c-3B | ±0 |

## 6. Units, in order

Lane B (Binding, R4 implements with a BU reviewer in parallel, R4-M119) and lane C (the A5 chain). Q9 replaces the count-triggered
2x-R4 and the token-limit reason of 2c-0: `Binding.Closures.cs` is 4,420 tokens, `Binding.FunctionTypes.cs` 2,771 and
`Binding.ValueCalls.cs` 3,748 (counted with a Roslyn-equivalent lexer that reproduces the plan's 5,911 and 7,522 on `7ecef035`), so
2x-R11 is unnecessary.

| Unit | Lane | Change | Depends | Tests and checks | Done when | Size (H) | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2c-0 | B | bp: C2's producer file `Binding.ClosureContracts.cs` with its contract (receiver classification §7.7.3, `CallLocalOrigin`, `LocalBorrowFinder`, `CallResultOrigin`); by responsibility (Q9): 2c-1, 2c-2, 2c-3B and 2c-5 add to it | — | bp triple; `ClosureReceiverClassificationTest`, `CaptureDiagnosticTest`, `ArchitectureRulesTest` | snapshots byte-equal; CODEMAP and ARCH name the file | move ±1,700 tokens | R (BU) | BU |
| 2c-1 | B | G147 (3): one predicate (plan row unchanged) | 2c-0 | plan's: reproducer `24028725b625` errs at `let q = n` (fails before); snap; +cen; OQ7 (a) | 13972.1438 pairs | +10/−15, t+40 | R (BU) | BU |
| 2c-2 | B | C2's facts: receiver case conditions, `Classified`, ρ accessor; the Signature after classification; publication only | 2c-1 | classification rows per case (f7l1/f7l2, a pair capture in both cases), `Classified` across rebinds; bp on every other output; warm Bind 0 B | the per-case receiver is published for every admitted case | +45, t+60 | D+R (BU, R5) | BU |
| 2c-H | B | G216: 15.4.2's input rows for a Closure header (B10) | — | `OriginRowsTest`'s two G216 rows → `MissingOriginBinding_Kd`; a named-function twin; a fixed-signature closure stays valid; snap; +cen | no Closure header row lies in an evaluating body | +5/−10, t±10 | D+R (BU) | BU |
| 2c-3B | B | (BU implements: BU-27's and BU-28's acceptance rules, PD6; R4 reviews the view part) B3 + B4: containment meets ρ; the receiver resolution; pending atoms, their fits and their judgment at classification; one reserved-slot role test; the old Emission's element-borrow Origin match (`BodyLowering.Sequences.cs:137, :164`) as D5's mechanical follow-up | 2c-2 | g6a-g6e, r1a-r1f, r1g, t4a-t4g, g65a, f7e1 on both pipelines, each also beside an unrelated Binding error (the record stays); `OriginRowsTest` row 4 now Fixed/ClosureValue; spec-cases' §15.8.2 Old records (the (a) difference closes); `ClosureEnvironmentOriginTest` (its ElementBorrow fixture is the Emission follow-up's regression); snap classified (OQ7 (a), §15.6.3/§15.6.5); +cen; sel; closure native families (TupleCapture, StructCapture, FixedArrayCapture, FunctionCapture, ContextualClosure, ClosureEnvironment) both generators; warm Bind 0 B and a pinned warm A/B | the MIR and the default give §1's verdicts and codes on G5/G6's forms | +75/−10, t+90 | D+R (R4 reviews; BU reviews if R4 implements) | BU, D5 |
| 2c-3 | C | B5 + B6 + B7 | 2c-2; PD1's chain position (after A5-F4's units on `MirBorrows*`) | spec-closure's `15.8.2-environment-retention` (R4-M123: `copyOut` valid, `z@follow = 43` an error) on the MIR; `MirBorrowsTest.Closures` (new partial): f7a-f7d, c-f7c2, c-f7d2, f7o4 error; f7a2, f7o, s-p3*, s-k5, s-r4, s-r6, 130a-130g, f7f, f7g accepted; f7n1 held (P13); an arity row; v1-v3; `RetentionTest`'s two closure rows re-numbered (receiver at 0) and closure rows for Exclusive/Consuming/Shared and D1; PD13 caller-side row; +V16 "F7 captures" (N captures × M calls); +warm with a closure; +cen; +led | the four ledger rows 2x-P7 names pair and no temporary rule remains | +45/−10, t+180 | G:A5-F7 (Binding part BU in parallel) | adv (`TyCtxt.Retention.Calls.cs`), BU (`Binding.Retention.Atoms.cs`), sh (`MirTypes.Holding.cs`) |
| 2c-4 | C | BU's 0c from 2c-3's branch (plan row unchanged, AB-2) | 2c-3 | plan's | the 16 (8i) rows are gone | +10/−20 | BU's R + G:A5-F7 | BU |
| 2c-5 | B | B8 + B9's Binding half + OQ4's gate | 2c-3B, 2c-3, OQ4 | default: `ClosureResultOriginTest`, `ClosureEnvironmentOriginTest`, `BlockClosureInferenceTest` keep code and span (gate); spec-closure's `15.8.2-bare-entry-origin` (b) difference closes on the MIR; mir: t1a-t1d, t3a-t3d, t4a-t4c, t5, t5b, f7e1, f7e2 by SPEC; F2's t7a, t7b, e2 build without validator findings; erasure rows (a ρ-dependent result is not erasable); snap; +cen; +led | no Binding `Unsupported_Kd` at a Closure call; `Binding.FunctionTypes.cs` shrinks | +70/−125, gate +15, t+90 | G:A5-F7 (BU in parallel) | BU, D5 |
| 2c-6 | C | MirOrigins judges the result rows; view rows 4/5 by the resolution (no new class) | 2a-1b (G:A5-F4 merged), 2c-5 | `ClosureResultOriginTest`'s refutation theory on mir; f7e1, t7a-t7d, e1-e3 at the returned value; g65a, r1g Unpr; one problem per failure with rule W (t7d, e3) | no Origin row on a Closure is unclassified | +20, t+60 | G:A5-F7 | — |
| 2c-7 | — | records (plan row) plus §8's | 2c-3-2c-6 | Doc + Evid +led | the plan's | doc | G:A5-F7 | — |

Order: 2c-0 → 2c-1 → 2c-2 → {2c-3B (lane B) ∥ 2c-3 → 2c-4 (lane C, after A5-F4's units on `MirBorrows*`, PD1)} → 2c-5 → 2c-6 →
2c-7; 2c-H any time. 2c-3B and 2c-3 touch disjoint files (R4-M119's two lanes); 2c-5 needs both, since the published result must
name ρ for every receiver-dependent source and the call's retention must read it. G6's MIR unsoundness (g6a) is on dev already and
closes with 2c-3B, so 2c-3B should not wait for lane C. PD29: 2c-2, 2c-3B and 2c-H hold BU files and merge
alone (behavior-preserving or SPEC-conformance on their own); 2c-3 holds adv/BU/sh files inside the group, as AB-2 requires 0c to
follow it, so the planning session records its holds in PLAN §3 and BU's G240 unit is ordered against it (both touch Binding's
retention files).

## 7. Size, speed and default-pipeline effects

- **Size (H).** Production about +295/−190 over the units (Binding about +200/−170, Mir about +65/−10, old Analysis +15, Emission
  +5, BU's 0c +10/−20); tests about +530. The prototype's diff is about +165/−20 (2c-2, 2c-3B and 2c-3's parts, without 2c-5).
- **Speed (H, to measure).** Bind: one meet per Borrow through a captured reference (interned, warm 0 B); one ancestor test per
  resolution step (OriginAtUse is hot: warm Bind A/B over Milestones 1, 20, 33 in 2c-3B); a Closure callee's retention gets one
  input, memoized as before. A5: one operand per Closure call; closures that capture no borrow stop being holders (B6), fewer W walks.
  V16 "F7 captures": rows grow with captures × parameters per signature (memoized), W per call is the existing walk.
- **Default pipeline.** 2c-1 (G147 (3)) and 2c-3B reject SPEC-invalid forms it accepted (r1e, r1f, t4g); G6's stores (g6a-g6e) get
  `UnprovenOriginRelation_Kd` in place of the old analysis's conservative Loan record (Binding is now incomplete there); G65's codes
  change (`Unsatisfied…` → `Unproven…`: g65a, r1g, §15.8.2's `invalid`); OQ7 (a) with §15.6.3/§15.6.5 named. 2c-H rejects G216's
  header (§15.4.2). 2c-5 changes no record (the gate) and removes derived `NotCallable_Kd` beside CallLocalOrigin's record (F2). No library or milestone program changes (`MirCensusTest` on the prototype; the default census
  is each unit's).

## 8. Records

- DECISIONS: R4-M1xx (the 2c-D package as approved, naming G:A5-F7's units 2c-3, 2c-4, 2c-5, 2c-6, 2c-7 and the D+R units 2c-2,
  2c-3B, 2c-H; supersedes the plan's 2c-2 `TyCtxt.Closure` row, 2c-0's token reason, 2x-R11, R4-M99 §6's "D14's walk" sentence,
  2a0-D §3 row 4's interim and D19's "Environment atoms are Finite until 2c-6", the S3 plan's C2 formals except CallResult);
  each unit's own entry (C2's facts; ρ and the receiver resolution with BU; the retention row; A5-F7's rule and the temporary rule's
  deletion; the D5 gate; G216's rule). ER-closure-stores (A3's erratum for capture stores) with 2c-7: W under rule G, P13's hold on
  them, and the self-store row's second record in f7c/f7d (same verdict).
- PLAN: G6/G5 (p7) under G65 and G226's C3 closed with 2c-3B; G216 closed by 2c-H; G147 (3) by 2c-1; G75's E7 and "maximal rather than
  per-case Closure receivers" checked on the MIR by 2c-7; G13 (PD13's per-case limit) stays; F1 and the C8 note below for BU.
- STATUS: closure results and calls (R1 row) at 2c-5; f7o's default rejection (spec-closure's); G229's default limit unchanged.
- CODEMAP: `Binding.ClosureContracts.cs`; C2 row ("Callable values and closures"); A5-F7 in the MIR analyses row.

## 9. Owner and planning questions

| ID | Question | Options | Recommendation |
| --- | --- | --- | --- |
| Q-A (planning, to the SPEC session) | The brief's "t4e/t4g valid" under F3 (b): t4g's shared Borrow through the captured `uniq` in a Shared call is contained in ρ by §15.6.3, so `-> ref/i32 during u` is Unknown whatever the binding's Origin | (a) t4g stays an error (SPEC as written; only t4a and t4e change with F3); (b) the SPEC session words an exception (a shared Borrow through a captured `uniq` not contained in a shared receiver) — a SPEC change beyond R4-M99 | (a) |
| Q-B (planning) | h1/h2 (G216) are §15.4.2 errors, not valid as 2x-P7 read them | (a) 2c-H reports `MissingOriginBinding_Kd` as for named functions; (b) keep the located `Unsupported_Kd` | (a) |
| OQ-B1 (owner, with the package) | 2c-3B and 2c-H change default-pipeline acceptance as SPEC-conformance fixes (OQ7 (a)'s list gains them) | (a) yes, census per unit; (b) hold the default (the judge would need a second class of ρ until H11) | (a) |
| OQ-B2 (owner) | Design B drops the plan's `TyCtxt.Closure` view and MIR-builder receiver Origin: C2 is `BoundClosure` + `TyCtxt.Retains` + the receiver resolution | (a) B as written; (b) design A's shape, if it keeps a view | (a) |
| OQ-B3 (owner, after 2c-3's ledger count) | Rule G's self-store row rejects `[x@uniq]` closures that store into `x`'s referent and are followed by a read of `x` (v4; SPEC's letter, as for functions) | (a) record them as rule G's verdict, GXS stays unscheduled (G148 (2)); (b) schedule GXS (full process, a SPEC 15.6.4 sentence) before R4's exit if the count is large | (a), revisited with the count |
| OQ4 | already (a); the gate is program-level (§3.8) | — | — |

## 10. What was probed

All on `a30f83887` plus the prototype, with 2x-P7's `ScratchProbeP7Test` (copied into the worktree, never committed): each program bound
as an Application under the mir pipeline; default = Binding's and the old analyses' records, mir = Binding's and the MIR's.
Results per round in `probes-b\results-*.txt`; class runs `proto*-classes.log/.xml`.

| Round | Change | Result |
| --- | --- | --- |
| base | none | 109 programs (2x-P7's cases, cases-x, param-slot, failed-closure, and g6a-g6e) |
| G6 probe | — | **g6a: the MIR accepts a use-after-free**: `[p@move, q@move]` where `p: uniq/Array<i32>`; the first call stores `p[0]@uniq` into `*q`, the second appends to `p` and writes through `*q`. The default rejects it (`ComparisonLoanConflict_Kd` at the store, conservative: it also rejects g6c, valid by SPEC). G6 is reachable without a receiver-dependent result, so it is not hidden by R1 |
| 1 | B5 + B7 (receiver input 0, D1 row, temporary rule deleted) | f7a, f7b, f7c, f7d, c-f7c2, c-f7d2, f7o4 to SPEC's error; f7o stays valid (D1); f7c and f7d add a second record at `holder@follow` (rule G's self-store row, same verdict); f7a2 **wrongly rejected** |
| 2 | + B6 (guard reads the environment) | f7a2 and v1-v3 valid; everything else as round 1; `MirCensusTest` passes; 560 tests of 10 classes pass except `RetentionTest`'s two closure rows (positions shift) |
| 3-6 | + B3 + B4 without the pending fit | Consuming bodies fail at binding time (t4a, t4e, t4f, g6c); traced to `BindNodeCore → FitsTypeAt → RecordOriginFitFailure` with ρ judged fixed through `EnclosingAtom` (k's literal value is an evaluating-body root) |
| 7-8 | + the pending fit for ρ, the `EnclosingAtom` exclusion (k's literal value is an evaluating-body root, so ρ was fixed through row 6), the old Emission's Origin match | t4a, t4e, t4f, g6c unchanged (valid); g6a, g6b, g6d, g6e, r1a-r1c, r1e, r1f, t4g `UnprovenOriginRelation_Kd` from the old reporter (default only); t3a-t3d, t4b, t4c keep R1's `Unsupported_Kd`; g65a still the old analysis's `Unsatisfied…` (its `env_i` was judged at binding time); 1,335 tests of 36 classes pass except `OriginRowsTest`'s row-4 pin and `RetentionTest`'s two rows |
| 9 | + `env_i` pending too | §15.8.2's `invalid` loses its record (R1 keeps Binding incomplete, so the old reporter never runs): `VerifiedSpecExampleTest`'s two 15.8.2 variants fail. A deferred judgment must not wait for a complete Binding |
| 10 | + the judgment at classification | g65a, r1g, §15.8.2's `invalid` `UnprovenOriginRelation_Kd` and g6a-g6e, r1a-r1f, t4g likewise, all Binding's records on both pipelines; the Consuming forms unchanged; 1,419 tests of 44 classes pass except five rows: `OriginRowsTest`'s row-4 pin, `RetentionTest`'s two closure rows, and the two 15.8.2 variants whose recorded old-path code `Unsatisfied…` is now SPEC's `Unproven…` |

Also: v4 (`[holder@uniq]` storing into `holder`'s referent, then a read of `holder`) is rejected on the MIR by round 10 and accepted
by the default (§3.6); token counts (§6); `func g(p: ref/(ref/i32))` → `MissingOriginBinding_Kd`, the closure header accepted (B10);
a grep of the library and milestones for nested omitted borrow Origins in closure headers (none) and for `HoldsLoans` readers (one).

## 11. Not verified

- MirOrigins was not run with B4 (dev has no Origin stratum; 2a-1b/2a-1c are not merged): A5's own Origin verdicts (2c-6's rows, G7)
  are by reading; the prototype shows Binding's names, its records on both pipelines and the old reporter's verdicts.
- The prototype's classification-time judgment scans every obligation (quadratic over closures); the unit takes the index range k's
  binding added. Nested closures (an inner k judged at its own classification, inside the outer's range) were not probed.
- 2c-5 (substitution, gate, result rows, F2) and per-case receivers (PD13) were not prototyped; PD13's located `Unsupported_Kd` has no
  program.
- The default census beyond the 36 classes (a full Session, default and mir), the native closure families, sel, warm Bind A/B and V16.
- Whether rule G's second record in f7c/f7d is what the ledger classes as a derived duplicate (W, 2a-2) or an independent one.
- C8's erased `uniq/F` receivers: an erased signature has no receiver input; a Callable signature names no written Origin, so no
  argument can be retained into its hidden environment (H), noted for C8's owner.
- The view's renumbering of C2's case conditions to a body's binders (2c-2's test).
