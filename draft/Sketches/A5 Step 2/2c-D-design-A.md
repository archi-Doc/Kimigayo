# 2c-D design A: C2 and A5-F7 under the receiver formulation (R4-M99)

Designer slot A, 2026-10-25. Design only: no repository edit, commit or push. Base: dev `a30f83887`. Probes and a throwaway prototype ran
in `bin/worktrees/2c-d-a` (removed at the end); everything they produced is in `probes-a\` next to this file (§14). Design B was not read.
"(H)" marks a claim no run checked. Inputs: brief-design.md, s2-common.md, R4-M99 and its proposal, 2x-P7 (`..\s2-2x-p7\p7-round.md`),
the A5 step 2 plan (2c rows, PD13, PD29, OQ4), 2a0-D §3 and §6, BU-27, BU-28, BU-30, R4-M117, R4-M122, R5-M102, the S3 plan, and the
owner's answers of 2026-10-25 (D1 (a), D2 (a), F3 (b) as relayed: a bare-entry capture's binding has its source's Origin).

## 0. The package in brief

R4-M99 makes a Closure call a receiver call. Design A therefore adds **no closure contract record and no closure retention view**: each
part of C2 goes to the component that already owns its meaning, and the closure-specific remainder is one row in each.

| C2 part | Owner (existing) | What changes |
| --- | --- | --- |
| Receiver per admitted case (PD13) | `Binding.ClosureReceivers` (split out of `Binding.Closures.cs`) and `BoundClosure` | `Receiver` stays the maximal requirement (acquisition, ABI, the MIR body's receiver local, R5); each capture records whether its bare acquisitions make the call Exclusive only in its pair cases; `TyCtxt.ClosureReceiverIn(case callee Type)` answers a view |
| The receiver Type and its Origin ρ | `BoundClosure.ReceiverType` | `ref/Env during ρ`, `uniq/Env during ρ` or `Env`; ρ is 2a0-D row 5's ClosureValue atom of the Closure (already "B's own call receiver, Fixed") |
| Retention over the receiver and the parameters | `TyCtxt.Retains` (C1+C8, one owner of SPEC 15.6.4) | A Closure callee's contract gets its receiver as one more input (after the parameters); the receiver's referent, the environment, is walked by its bindings with D1's one rule: a `let` binding's own storage is not writable, everything past an exclusive layer is |
| Stores into the environment (ER-closure-stores) | `MirBorrows` (W, rule G, P13's hold) | The Value callee operand is that input; W from it (layer 1 the Closure holder, deeper the captured referents, a Consuming call's moved value) needs no new rule; the temporary environment-to-result rule goes |
| What a Closure value may hold | `MirTypes.HoldsLoans` | A concrete Closure is walked by its captures (IMPL 21.1.2: a Tuple of them); only a common Function value stays opaque |
| Containment through the receiver (G5, G6) | `Binding.PlaceOrigins` (BU-27's walk), the MIR builder, the C1-F4 view | The walk meets ρ when it reaches an environment binding's root; the builder types the body's receiver local by `ReceiverType`; view row 4 is per receiver (Inferred under a borrowed receiver, Finite under `owner/Self`) |
| Classes of ρ | `Binding.OriginClasses`, the premise catalog | ρ and the call result are fixed in their own body (row 5, as the view says); the receiver Type's well-formedness is a premise of the body; under `owner/Self` ρ equals `static` |
| The inferred result contract | `Binding.ClosureResults` (split out) | After receiver classification, each result position names the boundary of its sources (ρ for an environment borrow under a borrowed receiver; capture, input and `static` Origins as they are); a finite source is left out and its relation to the call result is a row at the returned value, Refuted; the Closure keeps its Type. `CallLocalOrigin` goes |
| The value call | `Binding.ValueCalls` | ρ is substituted by the call's receiver Origin, as a method call substitutes `self`'s (`static` for a Consuming call); R1's `Unsupported_Kd` goes; OQ4 (a)'s gate in the old Analysis reads the old condition, which Binding publishes until R6-check |
| Header rows (G216) | Binding's header binding | A Closure header's well-formedness forms no row of the evaluating body: the body assumes it (R2), each call proves it (existing) |

Units (§6): 2c-0 (split), 2c-1 (G147 (3)), 2c-2/3 (C2 retention and the store rule, one unit), 2c-4 (BU's 0c), 2c-8 (ρ in Binding: C3,
classes, premises; BU), 2c-6a (the inferred result contract), 2c-6b (containment on MIR), 2c-5 (the value call and OQ4's gate), 2c-9
(G216), 2c-7 (records). Production about +250/−170 lines, tests about +600 (H). Deleted: the temporary rule, A3 P4's Closure clause, R1's
`Unsupported_Kd`, `CallLocalOrigin` with `LocalBorrowFinder`, row 4's interim, G216's pinned limit, the S3 plan's result formals as a
planned mechanism. Kept to R6-check: `ResultFixedForCall`, as OQ4's gate predicate.

Probed: the closure contract's retention with the receiver input (the D1 walk) on 61 programs, and a prototype of C2's MirBorrows rule.
It turns every capture-store gap of 2x-P7 (G1) into the SPEC verdict, keeps G229's class S accepted, removes a false conflict P4's
clause causes (`x3`), and passes `MirCensusTest` (library, milestones, goldens, SPEC variants: no MIR problem); 13 selected classes
(719 tests) pass but for one warm-allocation test that the prototype's environment-variable read trips (§14). It also showed two things for the planning session (§13): `HoldsLoans` must walk a Closure's captures, and
rule G's per-argument self-row rejects the read-after twins of the capture stores (c-f7c-ok, f7h2), exactly as dev's MIR already rejects
the method analog; and t4g stays invalid under F3 (b).

## 1. Required meaning

SPEC 7.7.2, 7.7.3, 15.6.3–15.6.5 and 15.8.2 as the spec-closure session integrates R4-M99 with D1 (a), D2 (a) and F3 (b). `self` is the
call receiver, ρ its Origin, `Env` the Closure Type (its captures, SPEC 3.2.1), each environment binding a Field of `Env` (15.6.5).

| | Shared (`ref/Self`) | Exclusive (`uniq/Self`) | Consuming (`owner/Self`) |
| --- | --- | --- | --- |
| Acquisition (7.7.3) | `&c` / Reborrow / Copy of `ref/F` | `&uniq c` (two-phase) / Reborrow | Move, or a Copy of a Copy Closure |
| Writable environment storage (15.6.4, D1) | none | each `var` binding, and every Place past an exclusive layer of any binding (captured exclusive referents) | captured exclusive referents only: the bindings die with the call |
| Retention (15.6.4, per argument) | a result naming ρ carries the receiver's Loan and every Loan a read through it carries; a result naming an Origin inside `Env` (a capture's, a fixed signature Origin, 15.2.3) carries every Loan a read through it carries | the same, and the writable storage may retain each argument whose Origins can reach a writable position (per argument), the receiver's own content included | a result naming any Origin of the owned receiver carries every Loan the Closure value carries (t5b) |
| A Borrow of a binding in the body (15.6.5) | an inferred region contained in ρ | the same | body-local (finite) |
| A Borrow through a captured `uniq/T during o` (15.6.3) | contained in `o` and ρ (ρ the first shared layer) | contained in `o` and ρ | contained in `o` only; the end of the binding's storage ends the child, its parent Loans stay |
| Through a captured `ref/T during o` | contained in `o` only | the same | the same |
| Inferred result (15.8.2 ¶2, D2) | each Origin a per-call Origin outlived by every source; a finite source (a Borrow anchored in the body, a binding of a consumed environment) is Refuted at the returned value; a static source is valid | | |
| Annotated result (15.8.2 ¶3) | ordinary elision without the receiver (t6b); no name resolves to a binding | | |

Per admitted Semantics case (7.7.3) the requirement may differ only between Shared and Exclusive: a bare acquisition of a pair-layer
capture Reborrows exactly in its exclusive cases (a Move is Consuming on every path, H). F3 (b): `[u]` initializes a binding of `u`'s
Type, so `t4a` and `t4e` are valid; `t4g` (`[u] () -> ref/i32 during u => u@follow@ref`, Shared) stays invalid, since the shared Borrow is
contained in ρ by 15.6.3 whatever F3 decides (as `r1f`, a Move capture, is): an `UnprovenOriginRelation_Kd` in the body (§13).

## 2. What the representation lacks (dev `a30f83887`)

| # | Site | Lack | Effect (2x-P7 gap or new) |
| --- | --- | --- | --- |
| L1 | `TyCtxt.Retains(BoundType callee, BoundType signature, …)` (`TyCtxt.Retention.Calls.cs`) | A Closure's contract has its parameters only; no receiver input, so nothing stores into the environment, and a result over the environment is `Untied` | G1: f7a–f7d, c-f7c2, c-f7d2, f7o4 accepted on MIR (probe: rows `[0 Value R]` for f7a) |
| L2 | `MirBorrows.Calls.Call` (the temporary rule) | Every Value callee's content reaches the result: A3 P4's Closure clause | `x3` (`[r] (b: ref/i32) => b`, a later write to `r`'s source) a false MIR conflict; the result never carries the receiver's own Loan |
| L3 | `MirTypes.HoldsLoans` (`MirTypes.Holding.cs`: `Function or Closure`) | A Closure value passes the guard whatever its captures | Once stores exist, W steps past every Closure holder into all it holds, `Φ_E` included: f7a2 and `c-put-then-write` become false conflicts (probe) |
| L4 | `BoundClosure` | One maximal `Receiver`; no receiver Type, no receiver Origin, no per-case fact | PD13 has nothing to read; G13 |
| L5 | `MirBuilder.cs:219-222` | The receiver local is `Reference(Receiver, EnvironmentType)` without an Origin | No containment edge through the receiver (G5, G6 on MIR) |
| L6 | `Binding.PlaceOrigins.MeetStep` (`PathStep.Root`) | A walk reaching an environment binding ends without meeting the receiver (BU-27's open C3) | G6: t4g, r1e, r1f accepted on both pipelines; t3a's result would not depend on the receiver |
| L7 | `Binding.OriginClasses` (`FixedIn`, `BodyLocalIn`) | B's own ClosureValue and CallResult atoms are body-local to Binding's judge, fixed to the view (row 5) | Nothing can be stated over ρ in the body |
| L8 | `Binding.Closures.BindConcreteClosure` | The signature is formed before receiver classification; the inferred result names environment slot atoms; `CallLocalOrigin` fails the whole Closure | G7, F2 (`Never` result, validator findings as `InternalInvariant_Kd`) |
| L9 | `Binding.ValueCalls.BindValueCall` (`dependent`, `HasEnvironmentOrigin`) | R1: a result over the environment is `Unsupported_Kd`; `ResultFixedForCall` decides it by capture carriers | G4 |
| L10 | C1-F4 view row 4 (`Binding.OriginRows.Atoms.ClassifyAtom`, on the G:A5-F4 branches) | Interim Finite for an environment binding's atom | G5: Unsat for Unpr (g65a, f7e1's invalid, r1g) |
| L11 | `Binding.ClosureEffects` | Outer arms and `UsesReferentExclusively`, two copies of one predicate | G3: f7k3 Shared (G147 (3)) |
| L12 | Closure header binding (`BeginOriginDeclaration` with the evaluating body forming) | The header's well-formedness is a row of the evaluating body | G216: h1, h2 a located `Unsupported_Kd` on 2a-1b |

## 3. Owners and the representation (design A)

### 3.1 The receiver per case (PD13)

- `Binding.ClosureReceivers` (2c-0's file; SPEC 7.7.3) classifies as today; 2c-1 merges the two exclusive-use copies into one predicate
  over the Place reached from a capture (the plan's 2c-1).
- `BoundCapture` gains one bit: whether a bare acquisition of this binding makes the call Exclusive only in the binding's pair cases (the
  `BareReborrows` arm). `Receiver` stays the maximum over all cases: Binding's acquisition (`AcquireValueCallee`), the MIR body's receiver
  local, ABI and R5 read only it, unchanged.
- `TyCtxt.ClosureReceiverIn(BoundType callee)`: for a Closure Type in a view's case form (`MirCases.Type` of the callee operand), the
  unconditional requirement, raised to Exclusive where a marked capture's case Type is an exclusive layer. A pure TyCtxt function, so A5
  reads no Binding plan.
- MirBorrows (2c-2/3): at a Value call whose acquired receiver is stronger than `ClosureReceiverIn` of its view, the view is **held**
  (R4-M73's existing hold: every Loan problem of the view a located `Unsupported_Kd` at its access). This replaces PD13's "a conflict whose
  only witness is a receiver Loan stronger than its case admits" by the coarser hold that already exists (no new per-Loan fact; §4 O4);
  library 0, milestones 0 (H). Removal: per-case callee acquisition, recorded in PLAN (PD13).
- Retention uses the maximal receiver too (§3.3): in a view whose case admits less, the extra rows are covered by the same hold.

### 3.2 The receiver Type and ρ

`BoundClosure.ReceiverType` = `Reference(Receiver, EnvironmentType, ρ)` for Shared and Exclusive, `EnvironmentType` for Consuming, ρ =
`OriginAtom(closure, Projection, ClosureValueSlot)`. 2a0-D row 5 already classes this atom Fixed in the Closure's own body and Finite (the
literal's temporary) in the evaluating body, so no new atom or slot. Readers: the MIR builder (2c-6b), the retention contract (2c-2), the
premise catalog (2c-8), the result contract (2c-6a), the value call (2c-5). Instances apply the Closure's context as `ClosureSignature`
does.

### 3.3 Retention: `TyCtxt.Retains` with the receiver (2c-2)

- For a Value callee whose `SignatureOwner` is a Closure, the contract's inputs are its parameters, then `ReceiverType` at position n
  (the parameter count); the result is the published signature's (after 2c-6a it names ρ; before it, the unit reads its environment slot
  atoms under a borrowed receiver as ρ, the prototype's way, deleted by 2c-6a). The memo key is `(closure, context, null)`: the premises
  are the Closure body's, whatever the call site.
- The environment walk, `Binding.Retention.Atoms`: the receiver input is a borrowed input (`RetentionInput`), so its referent `Env` is
  walked as any exclusive referent, with one rule: a `let` binding's component restarts at a covariant position, so its own storage is
  not writable and every Place past an exclusive layer inside it is (D1, SPEC 7.7.2: no Closure body can replace its environment). Under
  Consuming, `Env` is a by-value input, already writable only past exclusive layers. The referent's atoms are OwnedOrigins of the Closure
  Type: its captures' Types and its fixed signature Origins (SPEC 15.2.3), read through one Binding function that BU's F1 fix (the Owned
  proof) reads too.
- Rows the probe printed (`probes-a\c3-contract-rows.txt`; receiver = last input): f7a `0 Value R, 1 Content R, 0 Value 1, 1 Content 1`;
  f7c `0 Value 1, 1 Content 1`; f7o none (D1); f7o4 `0 Value 1`; s-p3 `0 Value 1`; t1a, t1c, t1d `0 Value R`; t2a, t3a–t3c `0 Content R`
  (t3a's is `Value` once 2c-8 names ρ); t4a, t4b, t5b (Consuming) `0 Value R`; t6, x3 `0 Value R` (the parameter only). Every row is SPEC
  15.6.4's per-argument retention of the table in §1.
- Function-typed and Callable-bound callees keep C8's contract: SPEC 8.10, no result depends on their hidden receiver.

### 3.4 The store rule in MirBorrows (2c-2/3; ER-closure-stores)

- D1 decides which rows exist (an argument is retained only where a writable position can hold it, so f7o has none); W decides which
  holders a row's store reaches (rule G, as for a method receiver: layer 1 and every holder past a guard-passing one).
- `MirBorrows.CallRows`: a Value callee's position n is the callee operand (`MirCallee.Position` already maps slots to positions for
  Value callees). `MirBorrows.Calls.Call`: the operand's value is input n (read before the operand's holder is cleared, as now); a row
  into n stores through W from it: layer 1 the Closure holder for a borrowed receiver, deeper the captured referents, for a moved receiver
  what the moved value names; rule G (R4-M73, no exclusion) and P13's hold unchanged. The temporary rule is deleted.
- G136's Value-callee arity: the token's inputs equal the operand count.
- `MirTypes.HoldsLoans`: a concrete Closure is walked by its components (its captures, IMPL 21.1.2), so the guard decides W past a
  Closure holder by what it captured; a common Function value stays opaque (its environment is erased and Owned).
- ER-closure-stores (A3 erratum, numbered at adoption, PD4): "A5-F7's capture stores are W's stores into a Closure call's receiver input,
  whose rows are C2's (TyCtxt.Retains with the receiver); A3 §5's F7 line (`TyCtxt.Closure`) and P4's Closure clause are replaced".

### 3.5 ρ in Binding (2c-8)

- `Binding.PlaceOrigins.MeetStep`: at `PathStep.Root` whose symbol is an environment binding of the Closure being bound, meet ρ and
  end (the binding is reached through the receiver). A Borrow through a captured `uniq/T during o` is then named `meet(o, ρ)` (t3a–t3d,
  t4g, r1e, r1f); one through a captured `ref/T` stops at its shared layer as now (t2a, t2b); a direct Borrow of a binding keeps its root's
  atom (bu18), classed by the receiver (§3.7).
- `Binding.OriginClasses`: `FixedIn` includes B's own ClosureValue and CallResult atoms (row 5), `BodyLocalIn` excludes them, so the
  judge and the view class them alike (BU-28's one rule).
- The premise catalog (`Binding.OriginPremises`): R2 adds the receiver input's well-formedness for a Closure body (OwnedOrigins of
  `Env` outlive ρ, per case, SPEC 15.6.1, as a method's receiver Type is assumed well formed); under `owner/Self`, `ρ == static` (no
  receiver borrow exists; the call substitutes `static`, §3.8). Read after classification by every judge (BodyOrigins deadline,
  UniversalRelations).
- `HasEnvironmentOrigin` (value calls, erasure, Callable proofs) treats ρ as the environment, so a result naming `meet(o, ρ)` stays R1's
  `Unsupported_Kd` until 2c-5 and SPEC 7.7.4's erasure condition reads "names ρ".
- Default pipeline: t4g, r1e, r1f-like uncalled Closures become `UnprovenOriginRelation_Kd` (SPEC-conformance, OQ7 (a); census §9).

### 3.6 The inferred result contract (2c-6a)

`BindConcreteClosure` classifies the receiver before it forms the signature. For an inferred result, `Binding.ClosureResults` forms each
Origin position from the returned sources (`ResolveLocalResultOrigins` keeps resolving local regions to their sources):

- a source fixed in the body (an Input, a capture's or enclosing body's atom, `static`, ρ or a meet of them) stays;
- an environment binding's atom becomes ρ under a borrowed receiver;
- a finite source (a Borrow anchored in the body; an environment binding's atom under `owner/Self`; a mutable static Borrow) is left out,
  and Binding forms a `ResultFit` row from it to the Closure's CallResult atom at the returned value: Binding's judge refutes it there
  (its record, OQ11 (b)'s twin) and MirOrigins judges the same row (Refuted); with 2a-2's rule W the storage-end conflict of the same
  Loan is derived (G8);
- a position left with no source is `static` (the meet of none).

The Closure keeps its Type (F2 closes: no `Never` result, no derived `NotCallable_Kd`, no validator finding); the record moves from the
Closure to the returned value (G7, SPEC 15.8.2). `CallLocalOrigin`, `LocalBorrow` and `LocalBorrowFinder` are deleted; `CallResultOrigin`
stays as the row's shorter end ("the call result" display). Rows for kept sources are not formed: the result is their meet by
construction. S3's result formals are not needed (§4 O3).

### 3.7 Containment on MIR (2c-6b)

- The MIR builder types the Closure body's receiver local by `BoundClosure.ReceiverType` (one source of that Type; W1 file, R5 review).
  MirOrigins' D14 walk then adds `ρ → n` for every Ref through the receiver; the validator compares Types modulo Origins and code
  generation reads none (R5-M102).
- View row 4: an environment binding's atom is Inferred under a borrowed receiver (bounded by ρ through those edges) and Finite under
  `owner/Self`, from the Closure's published `Receiver` (borrowed or owned never differs by case, H). Row 4's interim and its comment go.
- Result: g65a, f7e1's `invalid`, r1g give `UnprovenOriginRelation_Kd` on MIR (G5); Binding keeps `UnsatisfiedOriginRelation_Kd` where it
  judges such a relation during body binding, before classification: PLAN G65's recorded old-path code difference, unchanged.

### 3.8 The value call and the gate (2c-5)

- `BindValueCall` substitutes ρ by the call's receiver Origin, named as a method call's implicit receiver is: the callee Place's Origin
  for a Shared or Exclusive call on an owned Closure (`PlaceOrigin`), the Reborrow's (`ReferentOrigin`, already the receiver operation's
  AdaptedType) for `uniq/F`, the reference's own Origin for `ref/F`, `static` for a Consuming call. The `dependent` branch and the
  `HasEnvironmentOrigin` failure go: no `Unsupported_Kd` at these calls.
- OQ4 (a): the old Analysis keeps its verdict. Binding publishes on the value call's plan, `[Transitional("R6-check")]`, the condition the
  deleted branch used (`HasEnvironmentOrigin` or neither per-call nor `ResultFixedForCall`); `OwnershipAnalysis.CallValue` reports
  `Ownership.Unsupported` at the call when it is set (a D5 gate: R1's code and span; the requirement becomes the old Analysis's, a snapshot row classified). `ResultFixedForCall` stays as that
  predicate until R6-check (H11's list), not deleted here: a predicate read from C2's rows would also gate Copy-carried results the old
  path accepts (`ConcreteResultsKeepFixedExternalOrigins`).
- The default pipeline's verdicts are unchanged; the MIR gives SPEC's (t1a–t1d, t3a–t3d, t4a–t4c, t5, t5b, f7e1, f7e2, r1a–r1c).
- Owner of the old `ValueCalls.cs:541` site (now `:563-573`): Binding's value-call binding, R4's unit 2c-5 under PD6 with a BU reviewer;
  MIR reads nothing new there (the bound result Type and the Retains contract).

### 3.9 G216 (2c-9)

A Closure header's well-formedness rows (an omitted inner Origin is an evaluating-body inference variable, SPEC 15.3.4, fixed in the
Closure, row 6) are not formed in the evaluating body: the Closure body assumes them (R2 already supplies its inputs' well-formedness) and
each call proves them (`RequireInputPremises` already forms the rows, `h2`'s "the call's rows do"). The pinned limit
`AClosureHeadersOmittedInnerOriginIsAPinnedLimit` flips to that.

## 4. Options

| # | Option | Assessment | Choice |
| --- | --- | --- | --- |
| O1 | **A (this design): the receiver formulation in the existing owners** | One form for calls (R4-M99): retention is `TyCtxt.Retains`, stores are W, containment is BU-27's walk and D14, the result is the ordinary relation at the returned value; closure-specific: receiver selection, D1's `let` rule, the result formation | **chosen** |
| O2 | B (shared structure): the plan's `TyCtxt.Closure(k)` view (`B/TyCtxt.Closures.cs`), a `ClosureContract` record with its own rows, memo and receiver per case, read by MirBorrows beside `Retains` | A second retention component with the same meaning and contract (15.6.4 per argument); a closure branch in every consumer; its only reason, closure-specific rows, R4-M99 removed | rejected |
| O3 | S3 §4.1's result formals: an Input-slot formal per inferred position, source rows and contract bindings | Needed only if a kept source were not already a fixed atom of the body; by 15.6.3's walk every kept source is (§3.6), so the boundary meet is the per-call Origin's caller image. Adds an allocator extension and a second result Type | rejected (S3-0's gate R is moot; §12) |
| O4 | PD13's letter: a per-Loan "stronger than its case" fact and a witness test at the conflict | Exact, about +40 lines and a new fact; the hold (O1) is 3 lines and the same `Unsupported_Kd` | hold chosen (departure listed) |
| O5 | Per-case acquisition: a MIR acquisition whose Ref kind is the view's | A new Adapt kind (MIR vocabulary, O1 owner); PD13 records it in PLAN | not now |
| O6 | Per-case retention (the receiver per view in the contract key) | More memo entries for no verdict the hold does not already cover | rejected |
| O7 | Binding classing an environment binding's atom by the receiver in every judgment | The receiver is unknown while the body binds; classes would depend on timing | rejected: classes read after classification only (§3.5, §3.7) |
| O8 | Keep the temporary rule (P4's Closure clause) | False conflicts (x3) and still no receiver Loan in the result | rejected |
| O9 | Delete `ResultFixedForCall` now, the gate reading C2's rows | Changes default-pipeline verdicts (Copy-carried results become `Unsupported_Kd`) | rejected |
| O10 | The structural Closure Type with the call as an instance function | R4-M99's rejected round 1 (two verdict changes) | rejected |
| O11 | ρ as an Input atom of the Closure, solved by `SolveValueCallOrigins` with the callee as one more fitted input | The fit would relate the definition's capture atoms to the value's local regions (already related at creation) and change `InputOriginCount` for every reader (S3 §4.4's warning); the receiver's Origin is known at the call, so a direct substitution is the whole rule | rejected |

## 5. Deletions

The temporary rule (`MirBorrows.Calls.cs:40-41`), A3 P4's Closure clause (with it), R1's `Unsupported_Kd` at value calls
(`Binding.ValueCalls.cs:464-471, 563-573`: `dependent` and `HasEnvironmentOrigin`'s failure), `CallLocalOrigin`, `LocalBorrow`, `LocalBorrowFinder`
(`Binding.Closures.cs:38-97, 605-643`), the builder's own receiver Type construction (`MirBuilder.cs:219-222`), view row 4's interim, the
G216 pinned limit and its evaluating-body row, the `HoldsLoans` opacity of concrete Closures (`MirTypes.Holding.cs:159`), the S3 plan's result formals and its
`ResolveLocalResultOrigins` "closure meet rewrite" deletion as planned items. Deferred to R6-check: `ResultFixedForCall` (the gate's
predicate) and the gate.

## 6. Units

Hold codes as the step 2 plan's. Every unit holds a BU, adv or W1 file, so each takes its own review (PD29; D+R under the approved
package); A5-F7 ends with 2c-7 and the family's final review (S3-6's role). Verification letters as the plan's (+cen, +led, +V16, +warm,
+cold, snap, bp, both).

| ID (plan) | Unit | Change | Prereq | Verification and done | Size (H) | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 2c-0 | Split by responsibility (bp) | `Binding.ClosureReceivers.cs` (receiver classification, SPEC 7.7.3) and `Binding.ClosureResults.cs` (the inferred result's check, SPEC 15.8.2) out of `Binding.Closures.cs` (4,420 tokens), each with its contract | — | Comp: ClosureReceiverClassificationTest, CaptureDiagnosticTest, ClosureResultOriginTest, ClosureEnvironmentOriginTest, BlockClosureInferenceTest, ArchitectureRulesTest; bp. CODEMAP, ARCH | move +260/−250 | R (BU) | BU |
| 2c-1 | G147 (3) | One exclusive-use predicate (the plan's row) | 2c-0 | As the plan's: 13972.1438 errs at `let q = n`; f7k4 valid; snap; +cen; OQ7. Unblocks R5's W1 validator rule 1 (AR-8) | +10/−15, t+40 | R (BU) | BU |
| 2c-8 | ρ in Binding | §3.5: the root step meets ρ; row-5 atoms fixed in their body; the catalog's receiver well-formedness and Consuming `ρ == static`; `HasEnvironmentOrigin` reads ρ | 2c-0 | Comp: ClosureEnvironmentOriginTest, OriginRelationDiagnosticTest, ClosureResultOriginTest (rows stay `Unsupported_Kd`), OriginRowsTest; t4g, r1e, r1f give `UnprovenOriginRelation_Kd` (both); t4e, t4f, t3b-like bodies stay valid; snap (new rejections classified (a) SPEC 15.6.3); census lib/ms; +cen | +30/−3, t+80 | R (BU implements, R4 reviews; or the reverse) | BU |
| 2c-6a | The inferred result contract | §3.6 | 2c-8, G:A5-F4's 2a-2 (`ResultFit`, rule W) for the MIR half | Comp: ClosureEnvironmentOriginTest, BlockClosureInferenceTest (`UnsatisfiedOriginRelation_Kd` at the returned value), t7a, t7b, e2 without NotCallable/UnresolvedBinding and, on mir, without `InternalInvariant_Kd` (F2); t7d, e3 one record (W); MirOriginsTest rows; snap (locations move, derived records go); +cen; +cold | +60/−95, t+80 | R (BU) | BU |
| 2c-2/3 | C2 retention and A5-F7's store rule (one unit, two commits: Binding's contract, then MirBorrows) | §3.1–§3.4: `BoundCapture`'s case bit, `ReceiverType`, `ClosureReceiverIn`, `Retains` with the receiver and the D1 walk; MirBorrows' input n, W, the PD13 hold, the arity check, the temporary rule deleted; `HoldsLoans` by captures | 2c-1, G:A5-F4 merged (chain, PD1) | Comp: RetentionTest closure rows (§3.3's table, a fixed-signature-Origin row); MirBorrowsTest.Closures (new partial, PD19): f7a/622, f7b/1379, f7c/575, f7d/576, c-f7c2, c-f7d2, f7o4 error; f7a2, f7o, x3, f7f, f7g, f7k1/2, f7m, f7j1, cc1, rc1–rc4 as SPEC; G229 class S (s-p3, s-p3d, s-p3e, s-k5, s-r4, s-r6) accepted; f7n1 held; a PD13 hold row; c-f7c-ok rule G's self-route (§13); +V16 "F7 captures" (N captures × M calls); +warm with a Closure call; +cold; +cen (Milestone34); +led (the 4 rows pair; the A5-only rows classified) | +100/−12, t+220 | D+R; BU (Binding), R4 (A5), R5 (AR-2) | adv (`TyCtxt.Retention.Calls.cs`), BU, sh (`MirTypes.Holding.cs`) |
| 2c-4 | 0c (BU) | As planned (owner-or-uniq pair carrier self-store rows) | 2c-2/3 merged | As planned, its rows re-checked under C2 (f7i1, f7h2: §13) | +10/−20, t±20 | R (BU) + R4 | BU |
| 2c-6b | Containment through the receiver on MIR | §3.7: the builder's receiver Type; row 4 per receiver | 2c-8, 2c-2/3, G:A5-F4 merged | Comp: MirOriginsTest (g65a, f7e1 invalid, r1g Unpr; t1a, t3b bodies clean; an edge per Ref through the receiver), OriginRowsTest (row 4), MirBuildTest (dump), MirValidator rows; sel pending check (R5-M8); +cen | +5/−5, t+60 | R (R5 for the builder, AR-2) | W1 (`MirBuilder.cs`) |
| 2c-5 | The value call and OQ4's gate | §3.8 | 2c-2/3, 2c-6a, 2c-6b | Comp: ClosureResultOriginTest (`AResultOverTheEnvironmentIsUnsupported`: default `Unsupported_Kd` from the gate, mir by SPEC; 15.8.2's `get@move` a Loan conflict), ClosureEnvironmentOriginTest, VerifiedSpecExampleTest (15.8.2 lands F7); t1a–t1d, t3a–t3d, t4a–t4c, t5, t5b, f7e1, f7e2 by SPEC on mir; snap (default unchanged); +cen; +led. Done when no Binding `Unsupported_Kd` remains at Closure calls | +30/−15, gate +10, t+100 | D+R; BU | BU, D5 |
| 2c-9 | G216 | §3.9 | — | Comp: OriginRowsTest (the pinned limit flips), MirOriginsTest h1, h2 clean on G:A5-F4's branch; a body that needs the premise (`func [] (p: ref/(ref/i32)) -> ref/i32 => p@follow`) valid | +5/−8, t±20 | R (BU) | BU |
| 2c-7 | Records | ER-closure-stores; PLAN (G65's MIR part, G75 E7, G147 (3)/(8i), G216, G226 C3, G229, F1/F2); STATUS closure rows; CODEMAP; `classification.tsv`; pair-case target sides; X-2 registration of the PD13 hold and the remaining closure `Unsupported_Kd`s; `Landed += F7`; ARCH's R4 row | all | Doc + Evid: +led. Done when the 5 rows 2x-T2 assigned are gone and no closure signature is unclassified | doc | R4 | — |

Order. Lane B (Binding, now): 2c-0 → 2c-1; 2c-0 → 2c-8 → 2c-6a; 2c-9 any time. Lane C (after G:A5-F4's merge, PD1): 2c-2/3 → 2c-4
(BU) → 2c-6b → 2c-5 → 2c-7. At most one unit changing a shared representation at a time (R4-M119): 2c-8, 2c-6a and 2c-2/3 in that
order. 2c-2 and 2c-3 are one unit because the Closure retention entry has one consumer: published alone it would change MirBorrows'
input or need a temporary skip.

## 7. The gate (OQ4 (a))

The owner's (a) is implemented as §3.8: Binding stops failing these calls; the old Analysis reports the same `Unsupported_Kd` (code
`Unsupported_Kd`, requirement `Ownership.Unsupported`, span the call) from a Transitional fact Binding publishes; no feature is added to
the old path (D5). Removal: R6-check with the old Analysis (H11 gains the fact and `ResultFixedForCall`).

## 8. Placement of the related items

| Item | Placement |
| --- | --- |
| G229 (class S) | MIR keeps accepting (2c-2/3's census rows; probe: accepted); the old path's rejection stays G65 (R6-check) |
| G216 | 2c-9 |
| G65 | MIR part closes with 2c-5 and 2c-6b; the old path's Unsat-for-Unpr and class S stay to R6-check |
| G147 (3) | 2c-1; (8i) 2c-4 (BU) |
| F3 | Closed by the owner's (b); no change. t4a, t4e valid; t4g, r1e, r1f invalid by 15.6.3 (2c-8) |
| F1 (fixed signature Origins in Owned) | BU's PLAN row; C2's walk reads the same OwnedOrigins function (§3.3) |
| F2 | Closes with 2c-6a |
| G6 / BU-27's C3 / G226 C3 | 2c-8 |
| G75 E7 | MIR closes with 2c-2/3 (c-f7c2, c-f7d2); default to R6-check |
| G148 (2) GXS | Unscheduled (owner); it would recover the self-route rejections of §13 |
| G240 | Unchanged: a Closure contract the hold walk cannot complete is `CallRetention.Limit`, A5's located `Unsupported_Kd` |
| S1015 | §7.7.4 (the erasure condition reads "names ρ", 2c-8); §15.6.4 item 1's capture form (a mutable static Borrow stored through a capture: Refuted by rows 6 and 7 once P42 lifts 2b-6 (b)'s `Unsupported_Kd`; X-2 list); §15.6.1 item 25 (the receiver Type's well-formedness per case, 2c-8) |

## 9. Size and speed

- Production about +250/−170 lines (Binding +150/−130 with the 260-line move; MIR +25/−20; old Analysis +10), tests about +600 (H).
  Files: `Binding.Closures.cs` shrinks below 3,000 tokens after 2c-0; no file crosses 5,000 tokens (`Binding.ValueCalls.cs` 3,748,
  `Binding.Retention.Atoms.cs` 1,526, `TyCtxt.Retention.Calls.cs` 826, `MirBorrows.Calls.cs` 772; counted with `probes-a\tools\tokens.py`,
  within 0.4% of the Roslyn counts commit 5c961060d states).
- Speed: retention once per (Closure, context), memoized until an edit; `ClosureReceiverIn` O(captures) only at Closures with a marked
  capture; one more input per Closure call; `HoldsLoans` walks captures (fewer Closure holders tracked when nothing captured holds a Loan);
  the result formation replaces `CallLocalOrigin`'s scan. Warm Bind and warm MIR: 0 B (memo, interning, pooled rows; the prototype's 288 B
  in `ATemplateCallsWarmRunAllocatesNothing` is its environment-variable read, §14). V16 "F7 captures": Work ≤ c·M·(N + rows) at 2× and 4×.
- Census (H until +cen): the library has no capture list; the milestones' 15 capture lists capture `i32`, owned values or `@ref`
  entries, return no environment borrow and store nothing that holds a Loan; `MirCensusTest` passes under the prototype (§14).

## 10. Records

- DECISIONS: the package's approval (R4-M1xx, next free: the owner's words, the units, the departures of OQ-F7-1); per unit: 2c-2/3 (the
  C2 contract as `Retains` with the receiver, D1's walk, ER-closure-stores, P4's Closure clause retired, `HoldsLoans` by captures, the
  PD13 hold), 2c-8 (ρ, BU-27 C3 closed), 2c-6a (`CallLocalOrigin` deleted, the location move), 2c-5 (the D5 gate with its removal
  stage), 2c-9 (G216's ruling). A3's erratum list gains ER-closure-stores.
- DIAGNOSTICS: an inferred Closure result's Refuted source is at the returned value; ρ's display is the existing "closure: call
  receiver". CODEMAP: the two new files; `Binding.ClosureResults`. ARCH: the `TyCtxt` row (a Closure callee's contract has its receiver),
  the desugared-forms row (`BoundClosure.ReceiverType`), the MirTypes row (`HoldsLoans` by captures), the transitional gate.
- STATUS: closure rows (G75 E7 and R1 on MIR; the default's limits to R6-check); PLAN: the rows in §8.
- spec-cases: 15.8.2's example lands F7 at 2c-5; f7o's default rejection is spec-closure's (D1).

## 11. Reviewers

BU for every Binding part (2c-0, 2c-1, 2c-8, 2c-6a, 2c-2's Binding commit, 2c-5, 2c-9), in parallel with R4's A5 reviewer (R4-M119).
R5 (AR-2) for 2c-2/3 (the contract shape the erasure and Value-call code generation share: positions, the receiver, no Origin read) and
2c-6b (the builder's receiver Type in W1). 2c-4 is BU's with R4's review.

## 12. Owner questions

| ID | Question | Options | Recommendation |
| --- | --- | --- | --- |
| OQ-F7-1 | Approve the package (OQ2) with its departures from owner-approved texts: (i) no `TyCtxt.Closure` view, C2's retention being `TyCtxt.Retains` with the receiver (A3 §4.3's C2 row, the plan's 2c-2 row); (ii) A3 P4's Closure clause retired (results retain the receiver per argument); (iii) PD13's located `Unsupported_Kd` as the view hold; (iv) `CallLocalOrigin` deleted in 2c-6a (PD13 kept it to H11): the default pipeline's record moves to the returned value and F2's derived records go; (v) `ResultFixedForCall` kept to R6-check as the gate's predicate; (vi) the S3 plan's result formals not built (S3-0's gate R moot), the plan to be closed in INTEGRATED with its other items mapped to these units | (a) approve all; (b) approve with changes per item | (a) |
| OQ-F7-2 | Rule G's per-argument self-row rejects the read-after twins of the capture stores (c-f7c-ok; f7h2's exclusive case, which 2x-P7 listed as valid): the Closure's own content may be retained by the captured referent, as dev's MIR already rejects the method analog. Keep rule G's letter for Closures as for methods? | (a) yes, one rule with methods; GXS (G148 (2)) stays the recovery, unscheduled; (b) schedule GXS before R4's exit (full process, a SPEC 15.6.4 sentence); (c) a located `Unsupported_Kd` for these forms until GXS | (a): no library or milestone form (H); the old path accepts them, a recorded (d) row |

Decided elsewhere, not asked: D1, D2, F3 (owner, 2026-10-25); OQ4 (a); t4g, r1e, r1f's rejection under OQ7 (a) with its census.

## 13. Findings for the planning session

1. **`HoldsLoans` must walk a Closure's captures.** With stores into the receiver, the guard's "a Closure value may hold an exclusive
   reference" lets W step past every Closure holder into all its Loans and `Φ_E`: f7a2 and `c-put-then-write` became false conflicts
   in the prototype until it walked captures (`probes-a\d2c`). Part of 2c-2/3.
2. **Rule G's self-route reaches Closures.** `c-f7c-ok` (`[holder@uniq]` storing `x`, then reading `holder`) and f7h2's exclusive case
   are rejected by the receiver's `1 Content 1` row, as dev's MIR rejects the method analog `m-f7c-method-ok` (`probes-a\d3`); f7c gains
   a second record at its read (an independent error by the letter). 2x-P7's "SPEC A" for f7h2 (and the self-route part of f7n2) is the
   GXS reading. OQ-F7-2.
3. **t4g stays invalid under F3 (b).** Its shared Borrow through `u` is contained in the receiver (SPEC 15.6.3), as `r1f`'s is; F3 (b)
   makes t4a and t4e valid only. The spec-closure wording should not imply t4g valid.
4. **P4's Closure clause is visible on dev's MIR**: `x3` is a false conflict today; 2c-2/3 removes it.
5. **The S3 plan** loses its remaining mechanism (formals, gate R) to this design; its stated intent (one body-relative publication) is
   kept.

## 14. Probes (`probes-a\`)

- `ScratchProbe2cdATest.cs` (copied into the worktree's tests, never committed): per program, each Closure's receiver, signature,
  environment Type and contract rows with raw Origins, each Borrow and return in a Closure body with Binding's Origin, each value call's
  bound result and `Retains` rows, and the default and MIR records (`MirDifferenceLedger.Sides`); `KIMI_2CDA_DUMP` adds problems with
  facts and the MIR.
- `prototype.diff`: the throwaway prototype, gated by `KIMI_2CDA_C2`: the Closure contract with its receiver and D1's walk
  (`Binding.Retention.cs`, `.Atoms.cs`, `TyCtxt.Retention.Calls.cs`), MirBorrows' input n without the temporary rule
  (`MirBorrows.Calls.cs`) and `HoldsLoans` by captures (`MirTypes.Holding.cs`).
- Runs: `c1\`, `c2\` (dev facts, 22 and 36 programs), `c3-base\` and `c3\` (61 programs from 2x-P7 plus x2, x3; dev vs prototype),
  `c3-compare.tsv`, `c3-contract-rows.txt`; `d1\`–`d3c\` (f7a2's cause, the method analogs, c-f7c-ok); `classes-c2.log`: MirCensusTest,
  MirBorrowsTest, ClosureResultOriginTest, ClosureEnvironmentOriginTest, RetentionTest, MirAnalysisTest under the prototype, 391 tests,
  one failure (`ATemplateCallsWarmRunAllocatesNothing`, 288 B: the prototype's `Environment.GetEnvironmentVariable` in `HoldsLoans`; 0
  without it); `classes2-c2.log`: GenericOwnershipCaseTest, ContextualCaptureTest, MirDifferenceTest, ConcreteClosureTest,
  FixedOriginRetentionTest, InputDependentValueCallTest, ValueCallActivationTest, 328 tests, all pass.
- Prototype verdicts on MIR (`c3-compare.tsv`): to SPEC E: f7a, f7b, f7c, f7d, c-f7c2, c-f7d2, f7o4; to A: x3; unchanged A: f7a2, f7o,
  f7f, f7j1, f7k2, f7m, cc1, rc1–rc4, t2a, t2b, t6, G229's class S; unchanged E: f7g, f7k1, x2; f7h1, f7h2, f7n1, f7n2: P13's hold (owner
  view) and rule G's self-route (exclusive view, §13). Receiver-dependent calls stay R1's `Unsupported_Kd` (no 2c-5 in the prototype).

## 15. Not verified

- 2c-8, 2c-6a, 2c-6b, 2c-5 and 2c-9 were designed by reading, not prototyped; their test rows are 2x-P7's verdicts with §13's
  corrections.
- That Consuming is never case-dependent; the PD13 hold's reachability (no program built); per-case behavior beyond f7h/f7l/f7n.
- The full ledger (+led), the diagnostic snapshot and cold Bind under the prototype; R5's sel ledger for the calls 2c-5 binds.
- Binding's use-time judgments that touch ρ before classification (expected none after 2c-8's premise; H).
- The F1 helper's exact shape (BU's unit).
