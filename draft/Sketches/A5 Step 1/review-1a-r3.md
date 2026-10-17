# Review round 3: A5 unit 1a (`ccaee620`; (a) `efb2dd1d` and (b) `e3ec10d9` unchanged)

Reviewer: independent, same rules as before.

Probes: `ReviewRetentionProbe1aR3Test.cs.txt` beside this file, with output in `r3-probes.txt`. The probe ran as an
uncommitted test in the worktree; it was then deleted and the build rebuilt, so `git status` is clean. RetentionTest (84)
and `PublishedRowsAreFixed` pass.

## Verdict: not yet. One blocking gap in R4-M52's erased-callable rule (C1), for abstract Types; the rest is accepted

- **(a) and (b) are unchanged**: `ccaee620`'s parents are `e3ec10d9` and then `efb2dd1d`, the same hashes as in round 2.
- **Evidence**:
  - (a)'s semantic snapshot is now under
    `artifacts/verify/20261017-064431-911-unit-a5-1a-restructure-semantic/`.
  - (c) is verified by `20261017-080137-472`: 917 tests, diagnostic snapshot 0 differences, and `commit-match.json`
    names `ccaee620`.

## Accepted

- **Round 2's three B2 rows** are exact:
  - `h.g(w, x)`: `1 Value result`;
  - `h.k(x)`: `0 Value result`;
  - `apply`: `0 Content result, 1 Value result`.
- **The three kinds** are covered for Origins:
  - a Function Type value: `RetainsErased`, no premise read;
  - a Callable-bound value: erased, keyed by the calling definition's scope;
  - each callable Type inside a contract and each Callable bound of the callee's premise scope (`AddErasedSignature`).
    It also covers:
    - nested callables, because the list grows;
    - writable outputs: `h(g: (uniq/(ref/i32 during q), ref/i32 during p) -> (), d, v)` gives `2 Value store 1`;
    - the bounds as a call instantiates them (`map(fv@ref, n@ref)` gives `1 Value result`, and the old path rejects the
      write).
- **Per-call Origins stay entailment-only.** `IsFixed` excludes per-call (`FunctionTypeKoto`) and `static` Origins, and
  `static` reaches everything through the top nodes. This is sound: compatibility must hold for every per-call Origin.
- **The C8 key is canonical**:
  - a Closure or item is keyed by its declaration;
  - an erased call is keyed by `ConstraintScope(calling definition)`. The computation reads its use only through
    `AdmittedSemantics(ConstraintScope(use))`. The case bits from `PairBinders(use)` do not matter, since the closure runs
    with `ulong.MaxValue`.
  - Round 2's binder/definition collision is gone: the two kinds of key are distinct object types.
- **The superset Open** (every Callable bound in the callee's premise scope) only adds edges, so it is sound and costs
  precision only.
- **No regressions**: B1's nested-function and closure reproducers give `0 Value result`, and `nextPair` gives
  `0 Content result, 0 Content store 0`.

## Blocking

**B3. An erased callable relates only Origin atoms, so a Type parameter at its input or output positions is never
related. Its instance's hidden Origins are fixed Origins (R4-M36) that the implementation may map.**

`AddErasedRelations` skips carrier atoms, because `IsFixed` requires `atom.Origin`. An output carrier is marked only by
identity (`MarkCarrier`).

```kimi
struct View<T> {source}  // public let data: ref/T during source; init(data:)
func use<M, A>(mk: ref/M, q: uniq/i32 during c) -> A
    M is Callable<(View<i32>{v}) -> A>
    origin v.source == c
    let a = mk(View<i32>.init(data: q@follow@ref))   // with A := View<i32>{c} and an identity mk, a views *q
    q@follow = 5                                     // must be rejected: a is used below
    return a@move
```

- Binding completes this program. The view gives `mk(...)`: `[untied]`, with no row for the View argument. Under design
  §5, a carries only `Φ_E`, so A5 accepts the write: unsound.
- The old path accepts it too, so the ledger will not show it.
- The same gap affects `map<F, T, U>(g: ref/F, x: T) -> U` with `F is Callable<(T) -> U>`:
  - the declared contract gives `untied` and x is not retained, and `CallableBoundsRelateTheirFixedOrigins` pins that
    row;
  - the inner `g(x@move)` gives `untied`;
  - a generic caller that passes its own carriers gives `map in use: [untied]`.
- Concrete instances are right, because the Subst turns the carriers into Origins.

Fix direction: treat a carrier at an erased signature's input position as a fixed source and one at an output position as
a fixed target. A shorter and more general form:
- give each carrier one hidden-Origin node per computation, with an edge `hidden(T) → layer` (R4-M36: T's hidden Origins
  outlive each layer above an occurrence);
- then carriers are ordinary atoms: identity becomes the shared node, and the `Layer` field, `MarkCarrier`'s scan and the
  carrier branch in `MarkClosure` go away;
- `AddErasedRelations` then relates them like any fixed atom.

Also add `carrier-out-err` and the declared and generic `map` as RetentionTest rows. The owner's wording ("fixed Origin")
read with R4-M36 ("an abstract Type counts") covers this; if the planning session reads it otherwise, it needs an owner
decision.

## Non-blocking

- **The C8 owner path reads the declared bounds** (`CallableBoundSignatures(owner, …)`), not instantiated by an item
  instance's Type arguments, whereas C1 applies the Subst. This is latent: Binding rejects
  `map<(ref/i32) -> ref/i32, ref/i32, ref/i32>` as a value (`UnprovenConstraint_Kd`). Instantiate the bounds as C1 does,
  or record it.
- **Over-approximation is inherent to edge premises.** For example, `mk2(x: a, y: b, k: ref/((a) -> b)) -> b`, whose body
  returns y, still retains x, because the body could call `k`. This is correct by the rule.
