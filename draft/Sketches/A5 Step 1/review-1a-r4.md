# Review round 4: A5 unit 1a (`8da4a8a9`; (a) `efb2dd1d` and (b) `e3ec10d9` unchanged)

## Verdict: accept

- The probes were the round-3 file plus round 1's carrier, pair and associated-Type cases, saved as `r4-probes.txt`. The
  temporary test was deleted and the project rebuilt; the worktree is clean.
- These test classes pass: RetentionTest (85), OriginPremiseClosureTest (12), OriginProofScalingTest (26),
  OriginPremiseCatalogTest (36) and OriginPremiseLifecycleTest (7).
- The evidence is in `20261017-082251-446`: 918 tests, diagnostic snapshot 0 differences, and `commit-match` names
  `8da4a8a9`.

## B3 is closed

- `mk(View(q@follow@ref))` gives `0 Value result`, and `use` gives `1 Value result`.
- `map` gives `1 Value result` declared, through the inner `g(x)`, and from a generic caller that passes its own Type
  parameters (`map in use: 1 Value result`).
- The old path accepts `carrier-out-err`, so the ledger will now show A5 rejecting a program the old path accepts. That is
  the SPEC verdict, not a regression.

## The representation is sound for named callees too

- **Identity is kept.** All occurrences of a Type parameter (or pair target) share one unnamed node. A result with an
  abstract Type marks every input that names that Type, exactly as `MarkCarrier` did.
- **Reaching through a layer is kept.** Each occurrence adds the edge `N(T) ⊒ layer`. A closure that reaches the layer
  reaches `N(T)`, as the old layer nodes did.
- **No spurious paths.** The edges point out of `N(T)` (stored in the layers' incoming lists), so `N(T)` cannot link two
  layers. Only the erased relations add edges into `N(T)`, as R4-M52 intends.
- **Untied and the source links behave as before.** The -1 node is gone, and every input atom is a source.
- **Round 1's rows are unchanged.** `pick`, `hidden`, `two`, `f<T>(ref/T, T)`, the store `uniq/T`, `un`, the three pair
  cases, the associated `Item`, and the concrete and generic `next` all give the same rows.
- **No regression elsewhere.** B1's reproducers and `nextPair` are intact.
- **`OriginPremiseClosure`:**
  - `Node()` is a pure extraction for the existing callers: the same fields are initialized.
  - The unnamed nodes are neither top nor meet.
  - `Origin(int)` now returns a nullable value. Its only caller, R5 in `ExtractOriginPremises`, pattern-matches it and so
    handles null.

## Non-blocking (unchanged, recorded in R4-M51 Open)

- A C8 call of a closure or item still reads its bounds as declared. This is latent, since Binding rejects such an
  instance as a value.
