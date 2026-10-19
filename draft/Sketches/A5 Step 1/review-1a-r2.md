# Review round 2: A5 unit 1a redo (`efb2dd1d`, `e3ec10d9`, `47e9eaf5` on `claude/a5-1a`)

Reviewer: independent; same rules as round 1.

The probes are in `ReviewRetentionProbe1aR2Test.cs.txt`, with outputs in `r2-probes.txt` and `r2-probes.txt.memo.txt`.
They ran as an uncommitted test in the worktree, which was then deleted and rebuilt, so `git status` is clean.
RetentionTest (80) and PublishedRowsAreFixed pass.

## Verdict: not acceptable yet — one blocking soundness gap (B2) in the callable rule

What the redo fixes, verified:
- B1;
- item 2;
- the commit split.

B2 is a gap I wrongly cleared in round 1. There I listed "Function Type with fixed `a → b`" as unreachable; a value
stored under one function's premise and called elsewhere reaches it. Fixing it is a design decision for the planning
session; see the options below.

## Resolved from round 1

**B1 is fixed.** The premises are now the instantiated own part plus `ExtractOriginPremises` from the binder's parent
outward, which is the catalog's own walk, so there is no second copy. All three reproducers (nested function, closure
through C8, enclosing result) now give `0 Value result`. They are pinned in `EnclosingPremisesAreTheBodysPremises`, and
`RetentionUse` and the binder list are gone.

**Item 2 is fixed.**
- For `nextPair`, each `next` is exactly `0 Content result, 0 Content store 0`.
- With `definition = null`, the row is still `0 Value result`, and a test pins it.
- Normalization uses `CallableContractType` under `EffectPremises(definition)`. It rewrites only by Type equalities that
  hold in the caller, so it is sound.
- The calling definition's scope is in the key only when the Subst has an abstract part, which is minimal for C1.

**Commit split.**
- (a) is behaviour-preserving by inspection. The G74 message is kept. Evidence: diagnostic snapshot 0 differences
  (`20261017-064431-911`) and a semantic HIR/MIR snapshot equal to `5c42b577`'s.
- (b) is the defect commit with its regression, which fails before the fix.
- (c) has the snapshot at 0 differences.
- Nit: the semantic-snapshot evidence lives in the planning session's scratchpad (`semantic-base`, `semantic-a`), not in
  `artifacts/verify/`.

**N2–N10 are addressed.**
- The nested conditional is split, and `Mark` is now `void`.
- The scaling test is output-sensitive: n chained written inputs give n(n+1)/2 rows, and work stays within rows plus
  closures.
- Raw pointees are writable past a shared layer, with a test.
- The callee precondition is asserted, and the corpus harness uses `direct`.
- The rows list is reused.
- Pair-layer Value and the warm cost are recorded as Open in R4-M51.
- The ARCH row lists `Binding.Retention.cs`.

**New parameters.**
- C1 `(callee, Subst, definition)` is sound, and the key is minimal.
- `CallRetention` is still immutable.
- Memo growth is bounded by calls per pass (measured): library 242 calls / 220 keys (187 scoped), milestones ≤ 99 /
  56. Sharing is poor in generic library bodies, which is acceptable.

## Blocking

**B2. An erased callable's retention ignores the premises its implementation was compared under, so A5 would accept
three programs the old path rejects.**

A Function Type value's implementation is fitted at its conversion site. That site's premises may relate any of the
signature's fixed (non-per-call) Origins. The view reads other premises instead, in three ways:

1. **Binder set: the writing site's chain is read.** `Binding.CalleeBinder` returns the `FunctionTypeKoto` where the
   Function Type was written, for example a struct field. That chain knows H's declared slots `a, b`, not the instance
   Origins `s.a, s.b`, so even the calling definition's own clause is lost.
2. **Binder null: only the calling definition is read.** This is R4-M51 Open. It misses the conversion site.
3. **C1 with a callable input:** the callee body may apply the callable, so it maps the callable's fixed input Origins to
   its fixed result Origins. No premise represents that mapping.

Shared setup:

```kimi
func second2(y: ref/i32, z: ref/i32 during p) -> ref/i32 during q    // origin p outlives q; return z
func first2(z: ref/i32 during p) -> ref/i32 during q                 // origin p outlives q; return z
struct H {a, b}
    public var g: (ref/i32, ref/i32 during a) -> ref/i32 during b
    public var k: (ref/i32 during a) -> ref/i32 during b
func make(h: uniq/H{s})                                              // origin s.a outlives s.b
    h.g = second2
    h.k = first2
func apply(f: ref/((ref/i32 during a) -> ref/i32 during b), x: ref/i32 during a) -> ref/i32 during b => f(x)
```

Every `use` below has the shape `use(h: ref/H{s}, q: uniq/i32 during s.a, …) -> i32`. Its body is
`let x = q@follow@ref; let r = <call>; q@follow = 5; return r@follow`. Each case also has a variant without the write.

| Case | `<call>` | Binding | Old path without the write / with it | `Retains` |
| --- | --- | --- | --- | --- |
| e-g-clause (`use` declares `origin s.a outlives s.b`) | `h.g(w, x)` (binder: H's field `FunctionTypeKoto`) | complete | accepts / `ComparisonLoanConflict_Kd` | `[untied]` |
| e-k-noclause | `h.k(x)` (binder null) | complete | accepts / `ComparisonLoanConflict_Kd` | `[untied]` |
| f-apply (C1) | `apply(h.k@ref, x)` | complete | accepts / `ComparisonLoanConflict_Kd` | `apply`: `[0 Content result]`, x missing |

- In every case r is x, because `second2`/`first2` return z. Without the row, design §5 gives r no Loan on `q.*`, so A5
  accepts the write. That is unsound, and the MIR ledger will show a regression against the old path.
- As a control, e-k with the clause (binder null, so the calling definition's premises are read) gives `0 Value result`.
  The g/k pair differs only by g's extra per-call input, yet the fixed `s.a → s.b` flow disappears.

**General rule.**
- An erased callable is a mapping whose relations among its fixed Origins were proven where it was converted. No premise
  set at the call site or at the writing site bounds them.
- Per-call Origins cannot carry a flow into or out of fixed ones: compatibility must hold for every per-call Origin, and
  `static` is covered by the top nodes.

**Options for the planning session:**
- **(1) Treat fixed atoms of one erased callable as related.**
  - For a C8 erased signature (a Function Type, or a Callable bound with no concrete body), any non-static fixed input
    atom reaches every fixed result or writable atom.
  - In a C1/C8 contract, inside each callable Type (and each Callable bound of a carrier the contract names), add edges
    from the fixed input-position atoms to the fixed output-position atoms.
  - Closures and items keep the binder chain, since their bodies are known.
  - This deletes `DefinitionUse` from C8 and the writing-site premise read for Function Types. The cost is precision
    only for fixed-to-fixed flows, which the old path also treats as retained.
- **(2) A located `Unsupported_Kd` in A5** for calls whose retention depends on an erased callable with fixed Origins,
  until (1) is designed.
- In either case, add e-g-clause, e-k-noclause and f-apply as RetentionTest rows, and the Callable-bound-with-sets form
  from round 1's `callable-sets` probe.
- Answer to the coordinator's question: no. A Function Type value without per-call Origins, read under the calling
  definition's premises, is not sound (e-k-noclause). The R4-M51 Open entry should become this decision.

## Non-blocking

- **C8 key.** `(signature, null, use)` uses `use` both as a closure/item binder (premises from `use.Parent`) and as the
  definition node of a binder-null call (premises from `use`). Inside closure K, an erased call whose interned signature
  equals K's own signature shares K's key while reading a different `from`, so whichever is computed first wins. Key on
  `from`, or on (binder, definition). This may disappear under option (1).
- **`Retains(BoundType callee, …)`** takes `callee` only to find the binder. That is fine, but document that the Type
  must be the callee operand's Type (`CallPlan.CalleeType`), which MIR must reproduce.
