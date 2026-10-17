# Review: A5 unit 1a (`14f4b93f`, branch `claude/a5-1a`)

Reviewer: independent (wrote neither design nor code). Base: `5c42b577`.
Probes: `ReviewRetentionProbe1aTest.cs.txt` beside this file. It ran as an uncommitted test in the worktree, which was
then deleted and rebuilt, so `git status` is clean. Outputs are `probes*.txt`. RetentionTest (77) and
`PublishedRowsAreFixed` pass.

## Verdict: redo

B1 is an unsound omission in the premise set. It is also a second, narrower copy of the premise catalog's selection
rule, so it must be redone, not patched. The redo is also the cheap moment to split the commit (N1). Everything else
can stay: the atom walk, Kind, stores, carriers, Untied, the memo, the catalog `InPlace` and the `None` fix.

## Problem and general rule (as the reviewer states them)

A call's output may retain input i exactly when the callee's body can move a value of i into that output. The body is
judged under its premises. So the sound view is reachability over the instantiated atoms under **the same premises
Binding judged the body under**: the instantiated own signature (R1–R3), plus every enclosing declaration's premises as
`ExtractOriginPremises` collects them, plus carriers. Any premise the body may use but the view lacks gives an unsound
omission. Any extra premise only adds rows.

## Blocking

**B1. Enclosing-scope premises are missing, so the view omits a retention that the body proves.**
`Binding.RetentionPremises` adds R2/R3 only for the callee's own instantiated signature. It adds R1 only for
declarations whose Origins an atom names (`AddRetentionClauses`). Binding's body premises (`ExtractOriginPremises`) also
add **R2 inputs and R3 results of every enclosing `FunctionKoto`/`FunctionTypeKoto`/`PropertyAccessorKoto`** (and R4/R5).
A nested function or closure whose body relies on them gets no row.

```kimi
func outer(y: ref/(ref/i32 during c) during o, q: uniq/i32 during c) -> i32
    func pass(value: ref/i32 during c) -> ref/i32 during o => value   // proven only by y's well-formedness (c outlives o)
    let x = q@follow@ref
    let r = pass(x)
    q@follow = 5          // must be rejected: r aliases *q and is used below
    return r@follow
```

- Binding accepts the program (complete). Without `y`, `pass` fails with `UnprovenOriginRelation_Kd`, so the body uses
  the enclosing premise.
- `Retains(pass, null)` and `Retains(pass, call Subst)` both return `[untied]`; the row `0 Value result` is missing.
- Under design A3 §5, `out = {Φ_E}`, so r carries no Loan on `q.*`. The write finds no live Loan and is accepted
  (unsound).
- The same omission occurs for:
  - a closure called through C8: `let k = func (value: ref/i32 during c) -> ref/i32 during o => value; let r = k(x)`
    gives `[untied]`;
  - an enclosing **result** premise: `outer(q: uniq/i32 during c) -> Option<ref/(ref/i32 during c) during o>` gives
    `[untied]`.
- An enclosing explicit clause (`origin c outlives o`) works (`0 Value result`), because R1 is collected.
- The old path accepts all three programs too (a pre-existing hole), so the MIR difference ledger will not flag it.

Fix direction (shorter and more general):
- Take the enclosing part from the catalog's own walk. Share `ExtractOriginPremises`' per-node step from the binder's
  parent outward: the callee's, or for C8 the signature's own binder (closure, item, Function Type).
- Keep only the substituted own part (instantiated R2/R3, `SubstituteClauseOrigin` clauses, the declaring Type)
  bespoke.
- This deletes `AddRetentionClauses`' binder list and the `RetentionUse` first-atom heuristic (with its `use!` null
  path), and gives `environment.Use` its canonical value.
- Add the three reproducers as RetentionTest rows (expected `0 Value result`), and update R4-M51's premise sentence.

## Item 2: Content vs Value for a requirement on an abstract Self (R4-M51 open)

The row is sound but imprecise, and it is not a departure from design A3, which fixes no premise scope. It is not
acceptable as a silent limit for 1b:

- Probe `nextpair` (SPEC §22.2.3's valid `nextPair` under `I is Iterator`): each `iterator.next()` gives
  `0 Value result`.
- Under §5, `ret(0, Value)` puts `R_c` (the receiver Loan) into `first`, so the second `next` conflicts. 1b would
  report a conservative Language problem for a SPEC-valid form (AGENTS Diagnostics).
- In 2f, `out = ret-rows ∪ desc(c)` re-adds the `R_c` that `desc(c)` removes, so `takeTwo`/`nextPair` cannot pass.
- The fix is local, as probe `Premised` shows: `TyCtxt.Premised(nextPair, instantiated result)` already gives
  `Option<I.Item>` (no `step`). Normalize the instantiated contract under the calling definition's premise scope, and add
  that scope to the memo key when the Subst names the caller's abstract Types (MIR knows the body).
- Recommendation: fix it in the redo. If the planning session defers it, then before 1b:
  - pin the current row in a test; `SubstitutedCalleesRetainByTheirInstance` hides it with `Assert.Contains`;
  - record a PLAN open issue owned by a unit before 2f, with `nextPair` as its acceptance case;
  - keep such calls out of 1b's Language problems.
- No library or milestone body calls `next` twice on an abstract I (checked `Iteration.kimi` and the milestones), so 1b's
  library/milestone condition is not affected.

## Item 3: the catalog `None` defect

- The fix is right. A record struct `default` ignores parameter defaults, so `None` published `On 0, Destroys 0`.
- `KimiLibraryShapeTest.PublishedRowsAreFixed` fails before the fix: it expects `-1 -1` for initCapacity,
  initRepeating, reserve and shrinkToFit. It asserts at the lowest public layer (`TyCtxt.Published`), which satisfies
  AGENTS.
- No MirEffects test shows the A4 consequence (a spurious destruction effect). That is optional.
- It is an independent defect, so it belongs in its own commit with that test (N1).

## Item 4: commit structure and behaviour preservation

By inspection, the extractions preserve behaviour:
- `ComponentVariance` is the same rule as `Match`'s inline expression.
- `SubstituteClauseOrigin` uses the same binder. Its new `Math.Min` clamp changes only a case where `AsSpan` threw.
- `RentPremises`/`ReturnPremises` are the same, except that the G74 exception lost its `{longer} outlives {shorter}` text
  (a small regression in a debugging message).
- `Entails` is `Close(...) || reachedStamp[longer] == stamp`, as before.

## Non-blocking (open issues or fixes in the redo)

- **N1 Commit split** (AGENTS "Separate commits", VERIFICATION "Behavior-preserving commits"). Use three commits:
  1. the restructuring (shared helpers), with diagnostic and semantic (HIR/MIR) snapshot evidence against `5c42b577`;
     the commit itself says "no diagnostic snapshot comparison";
  2. the `None` defect with its regression;
  3. `Retains` and `InPlace`.

  The commit is unpushed, so this is cheap with the redo.
- **N2 Density rule:** a nested conditional expression at `Binding.Retention.cs:200` (`(… ? … : …) is { } o ? … : -1`).
  `Mark` always returns `true` and runs as a side effect inside `&&` chains (`MarkCarrier`, `MarkClosure`), which
  reads poorly.
- **N3 Scaling test:**
  - Total work is O(output atoms × closure reach), which is quadratic for a signature with many exclusive inputs whose
    Origins form a chain.
  - The test (8 vs 32) runs 2 closures, so `Closures <= 4×+4` is vacuous.
  - Add a shape with many outputs, or state the bound in R4-M51 and the test comment.
- **N4 Memo sharing:**
  - Subst `Declaring`/`Types` carry caller Origins (for example `View<i32>{q}`), so method calls on different receivers
    do not share.
  - The test comment "calls differing in their Origins only share it" holds only for Origin-free Types.
  - Caller clauses (`AddRetentionClauses` on caller binders) can add rows that depend on caller Origins. This is sound
    and consistent with "instantiated contract", but say so in R4-M51.
- **N5 Raw consistency:** a top-level `raw/T` input is a store, but `ref/(raw/(ref/i32 during q))` gives none.
  SPEC 15.6.2 makes raw pointees an unsafe obligation either way. State one rule in R4-M51.
- **N6 Pair-layer inputs:** they give Value through their target even in borrow cases. Step 4 would give Content. This
  is sound but imprecise; record it if 2f/2c needs it.
- **N7 Test harness:**
  - `EveryCorpusCallHasARetention` calls `Retains(call.Target, SubstOf(call))`. For `VirtualIsDirect`, `SubstOf`
    targets the implementation, while MIR passes `direct`, so the test exercises a pair MIR never asks for.
  - Consider asserting `ReferenceEquals(subst.Target, callee)` in `Retains` as its precondition.
- **N8 Allocation:**
  - `RetentionPremises` allocates a `List<Koto>` per computation; the other scratch is reused.
  - The view is recomputed each pass once 1b reads it. 1b should measure it and tag an allocation test if warm rebuilds
    are affected.
- **N9 Size:** Binding grows by +419 lines, against design A3 §8's +460 for *all* Binding views (C1-F4, C2, C5,
  E-keys, boundary). The planning session should re-estimate.
- **N10 Docs:** list `Binding/Binding.Retention.cs` in the ARCH row; update R4-M51 for B1 and the item-2 decision.

## Checked and found sound

- Equality clauses (`identity`): `0 Value result`. Projections `v.source`: Value by value, Content borrowed.
- Getter `peek.Get`: Content. `View<i32>.init` under its Subst: Value. `read` on `View<i32>{q}`: Content.
- `Box` get/take/put: Value, Value, store 0. A closure instance through `pick<T>`: Value. `two<T,U>`: no cross-row.
- Stores through `uniq/Option<…>`, a tuple, `Array.append`, `s/(…)` with `owner or uniq`, `Cell` by value, and a
  well-formedness source (Content). Static input: Value, not untied. A result that outlives its input: untied.
- The SPEC 15.3.4 nested-function example (clause): `0 Value result`.
- The Function Type with fixed `a → b` and Callable binding-set forms: no instance can relate the fresh regions (Binding
  reports `UnprovenConstraint`/`NoApplicableOverload`, as §8.10/§10.7 require), so the omitted row is unreachable.
- Contract header, parameter/nesting/statement/token limits, no new long lines, the commit-message size, and the
  catalog `InPlace` and `Replace` rows match design §4.1.
