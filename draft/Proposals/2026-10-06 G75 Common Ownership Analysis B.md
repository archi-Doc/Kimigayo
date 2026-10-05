# G75: Common Ownership Analysis B — the pair layer's outer Origin

Date: 2026-10-06. Status: implementation plan; not integrated, implemented or verified.
Planning baseline: `dev` at `1f4fe73e`. Supersedes the approach of `2026-10-06 G75 Common Ownership Analysis.md` for this issue.

## 1. Objective and constraints

Resolve G75 by making Semantics-generic code use the ownership analysis that concrete code already uses, instead of a second propagation mechanism. The admitted cases of a pair layer `s/T` are checked at definition, each under its own rules, before any concrete use exists (SPEC §8.9–§8.10).

- Implement from scratch. Do not merge, cherry-pick, copy or port code or tests from `held/n11-conditional-capture` (`fc5ab7cd`). Its review history may inform coverage only; write reproducers and expectations independently from the SPEC.
- Fix the cause, not individual routes. No operation whitelist, no per-route propagation table, no strongest-case shortcut.
- SPEC may be revised where this plan says so, under the AGENTS.md rules (rationale, Principles check, examples, INTEGRATED record in the same commit).

## 2. Diagnosis (verified in code unless marked)

**How concrete code keeps a Loan.** Ownership derives a value's Loans primarily from the Origins of its Binding Type: `OwnershipBody.VerifyBorrows` calls `AddType` for every Place (`OwnershipBody.Borrows.cs:110–123`), and `AddOrigin` maps Origin atoms to roots — a `Projection` atom to the Place it names, an `Input` atom to the body's own parameter, an exclusive parameter with a declared Origin to itself (`IsExclusiveBorrowInput`, `:1298`). `RetainBorrowAuthority` then carries authority along operations, and stores into exclusive parameter referents are recorded where the destination Type names the root's Origin (`NamedOriginRequirement`, `:932`, `:1392–1428`). A Reborrow keeps its parent's Origin (`PathReborrowOrigin`, `Binding.ArgumentOperations.cs:538, 601`), so for `v: uniq/i32`, `let local = v` has the same Origin as `v`; the child relation is value ancestry (`IsBorrowAncestor`). Every operation that moves, stores, captures or returns `local` moves a Type that still names that Origin, so the Loan follows without any per-operation rule.

**Why generic code loses it.** The original pair `s/T` is the `WholeType` W of the target symbol, created without an Origin (`Binding.cs:1589`). `AddType` skips the Origin of a pair-layer parameter (`OwnershipBody.Borrows.cs:472`), and `PlaceOrigin` skips the stored Origin of a pair layer (`Binding.ArgumentOperations.cs:739, 748`). A value of Type `s/T` therefore carries no Origin atom, ownership finds no root, and nothing propagates. dev compensates with a definition-only side channel: `OwnershipAnalysis.Use` emits an exclusive Borrow into a temporary and registers `ConditionalReborrows` (`OwnershipAnalysis.cs:806–813`), consumed by `AddExplicitDependencies`, `IsPairInput` and an ancestry special case (`OwnershipBody.Borrows.cs:659–782, 1288, 2105`). That channel propagates only along the operations it lists, which is the G75 soundness gap; the held branch extended the list operation by operation and never converged.

**Second cause.** The template treats a pair layer as the owner case ("universal verification treats the layer as the owner case", STATUS): `v@follow = x` is lowered as a plain Write of `v` (`SelectedPlace`, `OwnershipAnalysis.Instances.cs:274`), not as a store through the referent, so the borrow-case conflict check never runs.

**Instances do not compensate (inferred, unverified).** Instance analysis runs on substituted template Types (`Concrete`, `OwnershipAnalysis.Instances.cs:285–299`); the substituted outer Origins are the caller's and likely match no Place of the instance body, and universal verification is the acceptance proof anyway.

**Related representation defects found.** An explicit `s/T during a` becomes a `Parameter` Type with an Origin that `TryPairLayer` no longer recognizes as a pair layer (`Binding.TypeOrigins.cs:158`, `Binding.PairLayers.cs:21`). A `SemanticsApplication` compares its target covariantly even when its Semantics admits `uniq` (`Binding.TypeRelations.cs:140`; possible soundness gap, unverified).

## 3. Core idea

The SPEC already names the missing fact. §13.5.5.1 defines `o`, "the conditional outer Origin of the followed occurrence: `W`'s outer Origin for the original `s/T`", and §8.1.2 already gives a direct-input `s/U` an outer-Origin slot "active only when `s` is a safe borrow" (implemented as an `Input` atom with `BorrowCondition`, `Binding.TypeOrigins.cs:136–143`). The implementation never materializes `o` for the original `s/T`.

**Make W's outer Origin an explicit conditional Origin atom `o_s` of the pair binder, carried by every occurrence of the original `s/T`.** Then, for each borrow case, a generic body is analyzed exactly as its concrete counterpart in which W is `uniq/X during a` (or `ref/X during a`) with a declared universal Origin `a`:

```kimi
func poke<s/T>(value: s/T, slot: uniq/(s/T), x: T) -> ()     // value: W, slot: uniq/W; W carries o_s
    s is owner or uniq
    T is Copy
    var v = value          // owner: Copy; uniq: Reborrow, keeps o_s (as concrete keeps a)
    let local = v          // the same; local descends from v
    put(slot, local@move)  // T := W; the slot's referent names o_s, so local's Loan is retained in it
    v@follow = x           // owner: write v; uniq: store through v's referent -> conflict while slot
                           // holds local's Loan (case s = uniq)

// Counterpart of the uniq case, analyzed today by the concrete rules:
func poke<X, origin a>(value: uniq/X during a, slot: uniq/(uniq/X during a), x: X) -> ()
    X is Copy
```

No new propagation exists: every operation moves Types that name `o_s`, and the existing `AddType`/`RetainBorrowAuthority` machinery keeps the Loan. The only new concept is a **condition** on Origin-derived roots and dependencies: active for a subset of the binder's admitted Semantics. This is the "conditional dependency" that §8.9 and §13.5.5.1 already require.

## 4. Specification changes (integrated in U0)

| ID | Target | Change |
| --- | --- | --- |
| S1 | §8.1.2 | State that the original `s/T` has exactly one conditional outer-Origin slot, `W`'s outer Origin, shared by every occurrence of the original pair, active only for the admitted borrow bindings of `s` and dropped by value bindings. It cannot be named in source. This makes the existing sentence "keeps `W`'s Origins and receives no new Origin" precise; it adds no binding and no omission permission. An explicit `s/T during a` remains a pair layer whose slot is `a` instead. |
| S2 | §8.9, §13.5.5.1 | A conditional dependency is active for a stated set of Semantics of one binder. A conflict, Loan requirement or destruction obligation exists in a definition when its facts can be active in the same admitted case; facts active only in disjoint cases never combine. Replace "definition checking uses every dependency that can be active" with this wording, which §8.9's "a case that Copies receives no Loan that only a Reborrow case creates" already implies. The Reborrow case of a bare pair Place keeps the parent's Origin, as §13.5.5.2 does for concrete references. |
| S3 | §15.3.5 (if U0 confirms the gap) | The target of a pair layer is invariant when an admitted case is an exclusive borrow, as for `uniq/V`. |
| S4 | §23.3.6 | No schema change: a case-dependent record states the case in its Note ("when s = uniq"), as existing Notes do. Example only. |

Principles: (1) one ownership model and one Loan rule for concrete and generic code; (2) legality of a generic body follows from its declared premises and the finite admitted set, judged locally per case; (3) Copy, Reborrow and the per-case dependency stay explicit in the plan, with no hidden Constraint; (4) the case and the conflicting operation are reported as structured facts. `SETTLED.md` has no entry on pair layers or conditional plans.

## 5. Design

### 5.1 Binding

- **B1. `o_s` atom.** One interned Origin atom per pair binder, kind `Input`-like with a Semantics condition (generalize `BorrowCondition` to a binder plus `SemanticsMask`). W is created carrying it, keeping W's object identity so `TryPairLayer` still recognizes it. Explicit `s/T during a` becomes a pair layer with Origin `a` (fixes the representation defect in §2).
- **B2. Substitution.** A borrow instance replaces `o_s` by the bound W's actual outer Origin; a value instance drops it (existing conditional-slot path of `SubstituteType`, `Binding.TypeRelations.cs:410–431`; `CallType`'s `BorrowCondition` handling, `Binding.ContractCalls.cs:233`).
- **B3. Fits and inference.** `o_s` behaves as a declared universal Origin: equal to itself, related to others only by premises. Call inference binds `T := W` with its Origin, as for a concrete `uniq/X during a`. Keep the existing outlives proof; add no special case.
- **B4. Acquisition plan.** The bare acquisition of a pair Place with a conditional plan (`HasConditionalReborrowPlan`, kept as the legality proof) produces a value of Type W (the Reborrow case keeps `o_s`, the Copy case has no layer). Binding records the plan (per-case Copy/Reborrow) on the node for ownership, captures and lowering; no other component re-derives it.
- **B5. Pair-follow Origins.** A follow, borrow or Reborrow through a pair layer carries `o_s` for the borrow cases and the Place's own Projection for the owner case, as conditional atoms (SPEC §13.5.5.1 table, Dependencies row). Replace `PairOrigin`'s single fallback (`Binding.PairLayers.cs:170`).
- **B6. Variance (S3).** If U0 confirms S3, compare the target of an exclusive-admitting pair layer invariantly.

### 5.2 Ownership

- **O1. Conditional roots.** `AddType`/`AddOrigin` stop skipping pair Origins. A conditional atom maps to roots exactly as its counterpart atom would, and each root carries the condition. A parameter of Type W is its own root in the exclusive cases (the counterpart of `IsExclusiveBorrowInput`).
- **O2. Per-case modes.** A conditional root's Loan mode is that of the active case (`uniq` → exclusive, `ref` → shared, owner → the Place's own path dependency). Store the condition on the root and cap cell modes per case; do not duplicate the dependency table per case. Conflicts are judged per case of the root's binder; roots of different binders are independent, so no product of cases is formed.
- **O3. Conditional operations.** A pair-follow write, read or borrow in the template is one operation whose effect is selected per case: owner = the Place itself; ref/uniq = read the reference and access the referent (`StorePointer` for a write). This replaces the owner-case view of the template (§2 second cause).
- **O4. Removal.** Delete `ConditionalReborrows`, the conditional branch of `AddExplicitDependencies`, `IsPairInput`, the pair special case of `IsBorrowAncestor` and the definition-only Borrow in `Use`. Keep the `RequirementResults` consumers of shared helpers.
- **O5. Diagnostics.** A conflict of a conditional root names the case(s) in which it occurs and keeps the existing primary location, `loan` and `borrow` roles (DIAGNOSTICS §10).

### 5.3 Captures (N11)

- **C1.** A bare entry `[v]` of a pair binding with a conditional plan initializes the environment slot like `let v = v` (§7.6.2): its Type is W with `o_s`, its plan is B4's. The Closure Type's components then carry `o_s`, so `keep<F>`, stores and calls keep the Loan through the existing paths.
- **C2.** The call receiver is derived per case from the same plans (§7.6.3): Exclusive exactly in the cases where a body acquisition Reborrows a captured exclusive reference. Binding stores the per-case receiver; every call, Callable fit and erasure is checked per case; instances use their case's receiver.
- **C3.** Lowering reads the recorded plan and receiver; it never reclassifies acquisitions.

### 5.4 Instances and lowering

- **L1.** The verified per-case plan is substituted at instantiation (§8.9). Instance ownership must not report a failure its template's case did not; such a difference is an internal invariant failure, not a user diagnostic.
- **L2.** Generation reuses the existing per-instance follow (`FollowsReference`, `PairLayerExists`). Remove owner-case-only template paths (`CopyPairTarget`, `CopyThroughPair`) only after their consumers use O3.

### 5.5 Performance

`o_s` is one interned atom per binder. Conditions are a binder index plus a `SemanticsMask` on roots; per-case judgment iterates at most the admitted Semantics of one binder. Keep warm rebinding and warm ownership analysis at zero allocation (existing `Purpose=Allocation` tests plus new ones for generic bodies); measure Binding and ownership of a generic-heavy workload before and after U1 and U2.

## 6. Oracle: the case counterpart

For a generic definition G and each admitted case `c` of each pair binder, the **counterpart** `C_c` replaces every original `s/T` by `uniq/X during a` (c = uniq), `ref/X during a` (c = ref) or `X` (c = owner), and each `s/U during b` likewise, where `X` is a fresh Type parameter with T's Constraints and `a` a fresh declared Origin. Expected result: G is accepted iff every `C_c` is accepted, and each rejection of G is at the location of the corresponding `C_c` rejection with the case named. A disagreement where `C_c` itself contradicts the SPEC (for example the known concrete capture gap below) is triaged against the SPEC and recorded; it is never copied into G.

A generator produces G and its counterparts from small templates (acquisition × carrier × operation × order), covering the families of §8. The counterpart rewrite is mechanical, so expectations do not depend on either implementation.

## 7. Units

| Unit | Work | Completion gate |
| --- | --- | --- |
| U0 | S1–S4 into SPEC and INTEGRATED; reproducers of §8 written from the SPEC; the counterpart generator; baseline of current outcomes and measurements. Confirm S3 and the explicit-annotation defect with probes. | Every family has an expected outcome from the SPEC or its counterpart. Baseline failures stay evidence, not expectations. |
| U1 | B1–B3, B5, B6: `o_s`, explicit annotations as pair layers, substitution, fits, inference, display. Ownership keeps skipping pair Origins in this unit. | Existing functional, snapshot and allocation tests unchanged except intended B6/annotation fixes with their own regressions. |
| U2 | B4, O1–O5: conditional roots and modes, conditional pair-follow operations, removal of the `ConditionalReborrows` channel. | dev's local, call, store, aggregate and Move reproducers reject at definition (also without callers) with the case named; their valid orders run at O0/O2; the counterpart sweep agrees for non-capture families. |
| U3 | C1–C3: bare entries and per-case receivers. | Bare entries run per case; invalid exclusive calls, erasure and Callable fits fail with the case; capture families agree with their counterparts with no capture-specific propagation. |
| U4 | L1–L2, remaining removals, full sweep, diagnostic review (CLI text/JSON, LSP), measurements, Session with milestone harnesses (Programs 26, 39 and the completed set). STATUS, PLAN, CODEMAP, DIAGNOSTICS. | §10 holds. |

Order: U0 → U1 → U2 → U3 → U4. Each unit is verified with `scripts/verify.ps1` and committed separately. Review findings carry a `blocking` flag; the counterpart sweep is the first review criterion. If the same family yields blocking findings in two consecutive rounds, stop and report instead of adding routes.

## 8. Families (each with an invalid program and a valid counterpart)

| Family | Contents |
| --- | --- |
| Local acquisition | owner Copy vs uniq Reborrow; read, write and second Reborrow of the source while the child lives; explicit Move; no-use definition; owner without Copy |
| Calls | `keep`, `first`, `second`, `put`, two `uniq` arguments, nested generic calls, results holding the carrier |
| Stores | local assignment, out-parameter, store through a reference, Field, Tuple, fixed-array element, Origin-slot Field, setter Closure, chained stores |
| Components | Move of a Tuple, struct or fixed-array component holding the carrier; siblings stay independent |
| Control flow | `if`, `match`, `for`, loops, branch overwrite, early exit |
| Cases | `{owner, uniq}`, `{ref, uniq}`, `{uniq}`, two binders, nested `s/(t/U)`, explicit `s/T during a` |
| Captures | bare and explicit entries, `var` entries, nested entries, `let`/`var` Closure calls, erasure, Callable arguments |
| Publication | case Note, `loan`/`borrow` roles, CLI text/JSON, LSP, rebind invalidation |

## 9. Expected behavior changes

| Form | dev | After |
| --- | --- | --- |
| `let local = value` then write or second Reborrow of the source, then use `local` (uniq admitted) | accepted for routes outside `ConditionalReborrows` | `ComparisonLoanConflict_Kd` when `s = uniq` |
| `put(slot, local@move)` then write through the source | accepted, unsound | conflict, as the counterpart |
| bare `[value]` entry with a conditional plan | `TransferRequired_Kd` | accepted; per-case receiver |
| valid orders of the above | mostly accepted | accepted; a form whose counterpart is a located limit gets the same limit |

## 10. Completion criteria

1. Every family of §8 agrees with its counterparts or with a recorded SPEC-based triage; generic definitions are rejected at definition, with no callers.
2. No `ConditionalReborrows`-style channel remains; pair layers carry `o_s` and ownership uses the concrete rules with conditional roots.
3. Bare capture entries work per case; receivers are per case.
4. No accepted generic body fails in ownership only at instantiation or generation.
5. Focused, allocation, Session and milestone verification pass; measurements are recorded; STATUS states the new boundary.

## 11. Open points for U0

- Whether the counterpart's declared `a` and W's `o_s` produce identical Loan roots for several W-typed parameters (concrete makes each exclusive parameter its own root).
- Whether S3 changes any accepted program in the milestone set.
- The concrete gap `var v = n@uniq; let h = func [v] () => v@follow; v@follow = 9; h()` (accepted on dev) is outside G75; record it in PLAN §7 and exclude it from counterpart expectations until fixed.
- Receiver-dependent closure results of Type `s/T` (G65) stay a located limit.
