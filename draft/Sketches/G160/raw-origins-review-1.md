# Review 1: G160/G184 Raw Reach and Raw Origins (SPEC revision and records)

- **Reviewer:** independent reviewer (wrote none of the reviewed text), 2026-10-21.
- **Scope:** worktree `bin/worktrees/g160-raw-reach`, branch `claude/g160-raw-reach`, uncommitted changes on `origin/dev`
  `c0f370bd`: the design `draft/Changes/2026-10-21 G160 Raw Reach and Raw Origins.md`; `git diff -- docs/spec` (chapters 3, 5,
  8, 13, 15, Appendix E); DECISIONS R4-M95; PLAN G147 (1), G160, G184; STATUS "Loans and returned references"; COMPILER_ARCHITECTURE
  R4 row; INTEGRATED (edited G160 entry and the new section); the moved old plan in `draft/Obsolete/`.
- **Read:** the whole design; every changed SPEC paragraph in context (ch. 5 in full; §3.3.2, §3.5.2, §3.8; §8.1.1-§8.1.2, §8.11.4;
  §13.5.1, §13.5.3, §13.5.6; §15.2.1-§15.2.4, §15.3.1-§15.3.7, §15.4.2, §15.6-§15.6.5; §16.3.1; §9.4.1; §22.6; Appendix E; Appendix F
  §F.5, §F.9.4); AGENTS; old plan §10 (Q1-Q5); `draft/Sketches/G160/` (design A/B, grep of reviews 1-7); DECISIONS R4-M36, R4-M73,
  R4-M76; the cited A5 anchors (`MirBorrows.Values.cs:30,72,95,227`, `MirBorrows.Flow.cs:138`, `MirTypes.Holding.cs:34-40`,
  `Parser.Origins.cs:167`); library `Storage.kimi`, `StorageOperations.kimi`, `DictionaryStorage.kimi`, `Dictionary.kimi`,
  `Array.kimi`, `UniqSlice.kimi`, `Formatting.kimi`, `Text.kimi`; LIBRARY `Raw` table.
- **Probes and searches:** anchor check of every link added in `docs/spec` (all 49 resolve; `#57-raw-pointer-origins` matches
  `## 5.7. Raw pointer Origins`); grep of `docs/spec` for `raw`, `storage the body does not own`, `no upper bound`, `fresh anchor`,
  `unsafe context`, `@raw`; grep of the library for every raw write, Take and `borrowStorage(…@ref)`; the parser's adaptation-target
  path (`Parser.cs:3575-3597`, `CheckAdaptationOrigins`); PLAN line count (222). No builds or tests (documentation only).

## Blocking findings

### B1. The claim form `E@raw/U during o` is ill-formed under unchanged §13.5.1 and Appendix F

- **Location:** §5.4 new row; §5.6 `Buffer.init`; §5.7 `adopt`; Appendix E "Claim"; §13.5.3 row (refers to the claim); design D5.
- **Problem:** the only way to add an Origin (D5) has no valid spelling. §13.5.1 is unchanged: "Origins written on its outer
  Semantics chain are an error, even through grouping; they are inferred" (`13-operators-and-assignment.md`, §13.5.1 para 1), with the
  examples `// let w = value@(ref/T during a)    // Error: Origin written on the outer borrow.` and
  `// let d = saved@ref/T? during a     // Error: group the annotated target.`. Appendix F §F.9.4 (`F-syntax-summary.md:701`): "an
  ungrouped Type head consumes no OriginApplication and no BorrowOrigin". So `Raw.allocate<T>(capacity)@raw/T during self`
  (`05-…:155`) and `pointer@raw/i32 during static` (`05-…:233`) are syntax errors, and the grouped `E@(raw/U during o)` is the
  §13.5.1 error. The implementation agrees (`Parser.cs:3584-3597`: `AdaptationOrigin`, `AdaptationDuring`).
- **Correction:** amend §13.5.1 (an outer `raw` layer of a target may state its Origin: keep, drop, or a claim under §5.7) and
  §F.9.4 (OpenTarget takes a BorrowOrigin when its outermost Semantics is `raw`, so the owner's spelling D5 parses), and list both in
  INTEGRATED. G160-0's located `Unsupported_Kd` must also replace the two syntax errors above, not only `BorrowOriginSemantics_Kd`.

### B2. C1 and the planned annotations make two library bodies undefined; C1's third reason is false

- **Location:** design §1 C1 third bullet ("No library body or SPEC example writes through raw storage past a shared layer"),
  design §8 and §9.1 G160-3; §5.2.1 condition 3 limits 1-2; §15.6.2.
- **Problem:** `Dictionary.insertOrReplace` (`Dictionary.kimi:53-61`) and `DictionaryStorage.removeEntry`
  (`DictionaryStorage.kimi:48-53`) form `var state = Storage.borrowStorage(self@follow@ref)` (shared remainder), then
  `let slot = state.storage + (link - 1) * state.stride` and write or Take through it:
  `let previous = (*Storage.valueAt<K, V>(slot))@move`, `Raw.initialize(Storage.valueAt<K, V>(slot), value@move)`,
  `(*Storage.keyAt<K, V>(slot))@move`. `state.storage` is the Handle's `buffer` read by `borrowStorage` past the `ref` layer, so
  limit 1 ("A raw pointer read from a Place past a shared layer … has no write authority", `05-…:55`) removes write authority; after
  G160-3's `DictionaryRefRemainder.storage: raw/u8 during source` (design §4) limit 2 does too (`source` keeps only a shared Loan).
  `keyAt`/`valueAt` pass "the provenance and authority of `slot`" (`StorageOperations.kimi:31-37`) and drop the Origin, so D11's
  error never fires: the library becomes silently undefined, and the owner is asked to confirm C1 on a false premise.
- **Correction:** correct C1's third reason (and DECISIONS R4-M95's C1 sentence) before the owner confirms; add both bodies to
  G160-3's scope (derive `slot` from the exclusive handle, e.g. `(*(layout.0@raw/Handle)).buffer + (link - 1) * layout.1`, or search
  with the Uniq remainder), and add them to §9.4's checks.

### B3. Typed paths and addresses ignore raw dereferences

- **Location:** §5.4 `P@raw` row; §5.7 "Typed paths" and "Reading a Field"; §15.6 "Addresses"; design §3.1, §3.2 (D1, D13).
- **Problem:** both rules are stated over "the last borrow that `P`'s path dereferences" / "when `P`'s path has no dereference",
  but a dereference is only a step through `ref`, `uniq`, `objref`, `objuniq` (§15.6.2) and raw Places have no access path (§3.4.1,
  Appendix E "Access path"). For `P` through a raw dereference:
  - Origin: `(*h.p)@raw` with `h: uniq/Holder`, `p: raw/T` unstated, states `during h`; `(state.storage[0])@raw` in
    `splitFirst(state: uniq/RefRemainder<E>)` states `during state` (exclusive) instead of `state.source` (shared). This is an implicit
    Origin change with no unsafe promise (against D5 and the rejected "implicit fit") and bypasses D11's error.
  - Reach: for `(*q).x@raw` with `q` a local raw pointer, "P's path has no dereference" selects the first branch, so the result
    addresses `q`'s holder instead of what `q` reaches: G184's miss again, and a later read of a Loan-carrying value through it
    becomes the client's undefined behavior under condition 5. "Reading a Field" has the same gap for a `during self` Field reached
    through a raw dereference (`(*bp).data` with `bp: raw/Buffer<T>` is classed "owning").
- **Correction (one general rule):** count raw dereferences as dereferences in these three places: `P@raw` (and a Field read)
  states the Origin of the last reference the path dereferences, a borrow's outer Origin or the raw pointer's stated Origin (none when
  unstated); when the last dereference is raw, the pointer carries what that raw pointer reaches (§5.2.2), otherwise as now.

### B4. D15 is not stated; G147 (1)'s planned closure has no SPEC basis

- **Location:** §5.7 "Calls" ("a call may store in the storage it addresses what that borrow's Referent Type admits"); §15.6.4
  added sentence; §5.7 table row `during o` ("what the Loans of `o` protect"); "Authority"; PLAN G147 (1); design §4 (`placeEntry`).
- **Problem:** "that borrow" reads as "a borrow with that Origin", i.e. the pointee: `raw/u8 during d` admits `u8`. D15 says "the
  Referent Type of the borrow its Origin belongs to", but SPEC never defines that borrow. For a signature Origin carried by no
  borrow in the signature, which is exactly the planned `placeEntry<K, V>(handle: raw/u8 during d, key: K, value: V)`, the
  callee's contract gives no Referent Type, no mode (for D11 and for G160-4's "a dereference with `o`'s mode") and no protected
  Loans. Under the literal reading the call retains neither `key` nor `value`, so `let entries = [1: value@ref]; value = 99` stays
  accepted (or `placeEntry` breaks the Retained-dependencies sentence). A caller-side reading (the caller's `self` borrow) would
  break Local Reasoning.
- **Correction:** define it in §5.7: for a slot, the stored references and `Loan<T>` Fields that keep it (the union of their
  Referent Types); in a signature, the parameter or receiver borrows `during o`; for `static`, static storage. Require a raw layer's
  Origin in a signature to be one of these. Give `placeEntry` a Type that states K and V, for example
  `handle: raw/Dictionary<K, V> during d` with `dictionaryStorage` returning `raw/Dictionary<K, V> during value` (it already
  returns "the address of the Dictionary"), and record that in PLAN G147 (1) and G160-3.

### B5. The §5.7 `Window` example reads a private Field from outside the Type

- **Location:** `05-…:217-233`.
- **Problem:** `let at: raw/T during source` is private by default (§9.4.1: "`private` (default) | `Body(C)`"; §5.6's prose
  relies on this), but the top-level `read` and `clear` read `window.at`. `// Valid` is wrong, and `clear`'s first error is access,
  not authority.
- **Correction:** declare `internal let at` (and `internal let loan`, `internal init`), or make `read` and `clear` members.

## Non-blocking findings

- **N1** §5.2.1 "Local variables" (`05-…:61`): "under conditions 2-4" omits condition 5 and the new limits; write "2-5".
- **N2** §5.2.2: "no owner or existing Loan is recovered" now sits beside "the result also carries what the pointer reaches"
  (which includes parent Loans for `r@follow@raw`). Say "its anchor recovers no owner or Loan".
- **N3** §5.7 "Any other change of a raw Origin is a claim" and §5.4 "unless it only keeps or drops an Origin" turn a covariant
  narrowing (`during a` to `during b`, `a outlives b`; a subtype by §3.8) into a claim when written as a conversion, and leave
  `null@(raw/T during o)` unclear against "`null` takes any Origin". Use "a change that is not a fit (§3.8)". Also say whether a
  fit that needs a claim is a Type, Origin-relation or separate error (§15.6.1 gives a Refuted relation).
- **N4** §5.7 says "a raw result without an Origin"; §15.6.4 says "a result's [raw layer]". Decide whether D6 covers raw layers
  inside a Tuple (`dictionaryStorage -> (raw/u8, isize)`), an Option payload or a Type argument, and say private Fields are not
  covered (C2). Same for parameters.
- **N5** C1's "shared layer" (`ref`, `objref`, `rc`, `arc`; §15.6.2) omits a raw layer whose Origin keeps only shared Loans. An
  inner raw pointer read through `uniq/Window<raw/(ref/i32)>`'s shared `at` keeps write authority, while §15.6.4 and G160-4 ("no
  write when shared") assume nothing is written past it. Not unsound (condition 4 and Retained dependencies make misuse undefined),
  but the authority rule and the checker's W should agree: count such raw layers as shared layers.
- **N6** D11's checked error lists write, Take and in-place destruction but not `@uniq`/`@objuniq` of a raw Place, which §15.6.2
  groups with writes. Undefined behavior covers it (§5.2.2), but `(*window.at)@uniq` bypasses the error. Owner's call (beyond D11).
- **N7** §5.7 "keep a Loan through a stored safe reference or a `Loan<T>` Field" is narrower than §15.3.5's Phantom test, which also
  counts slots of nested Types (a `Slice<T> during source` Field). Use "must not be Phantom".
- **N8** §5.7 names `self` for receivers and constructors but not the destruction receiver (§16.3.1: "not an ordinary borrow value
  or an owner"), whose `self.data` reads occur in §5.6 `Buffer.drop` and four library `drop`s. Both possible readings are
  exclusive, so nothing breaks; state it.
- **N9** §8.1.1 "The original `s/T` still denotes `W`" versus the raw row's "none in a pair": say that a `raw/T during o` argument
  fits a pair parameter by dropping its Origin.
- **N10** §5.7 opening: "constrains no region" overstates. Fits and well-formedness still give relations on raw Origins. Say "its
  uses add no point to its region (§15.6.1)".
- **N11** Appendix E "Address": `@raw` through a dereference, and a `during self` read through a borrow, carry Loans, not an
  address (§15.6). Restrict the row to "a path without a dereference".
- **N12** Condition 5's second clause says "must not move such values", where the condition says "reads or writes". DECISIONS
  R4-M95 says "an access that moves a Loan-carrying value". Use "read or write" everywhere.
- **N13** LIBRARY `Raw.slice` row (`docs/LIBRARY.md:512`, "`s` is fixed by the caller's expected Type") misses §5.6's new "within
  `storage`'s Origin when it states one". This is a changed public guarantee, so it is an Auto update. The §5.6 rule also refers to the
  argument's Origin before its fit to `raw/T`; say so.
- **N14** INTEGRATED: "written in `Proposals`, reviewed and moved to `Changes` with only its status sentence replaced" will be
  false once §11 "Pending" is filled; the mapping omits condition 3's second limit (design §0 point 3, §3.2 Authority).
  DECISIONS "Q5 addresses carried by values, with no interim" reads against "(the G160 plan's Q5 (iii))" two bullets earlier.
  Say the widening-class interim is moot.
- **N15** PLAN §3: BU's "Next:" omits G160-0 to G160-3, and R4's position omits the new exit condition. Both are next-action changes,
  and only the G160 row and COMPILER_ARCHITECTURE carry them. Design §9.5 says R4 waits for "G160-4 and G160-5"; DECISIONS and
  COMPILER_ARCHITECTURE say G160-0 to G160-5. These are equivalent by the unit order, but the wording should match.
- **N16** Typed paths put raw Origins into inferred Type arguments (`T` from `r@follow@raw` is `raw/i32 during r`). Invariant
  containers (`Array<raw/i32 during r>` against `Array<raw/i32>`) and `Owned` can now fail where they passed. Add this to §9.4's
  unverified census items.
- **N17** Two SPEC examples declare `struct Window<T> {source}` with different storage and variance (§15.3.5, covariant in `T`;
  §5.7, invariant). Rename the new one.
- **N18** §22.6 allows "Origins introduced by borrow annotations" in an import signature. State whether a raw layer's `during o`
  counts.

## Checks with no finding

- **Soundness: shapes of design §5.** (a), (b), the Tuple and by-value variants, `KeptBox`, `Cursor`+`setC` and
  `Raw.initialize(Raw.allocate…, x@ref)` reach E or the holder and are rejected. (c) and (d) are undefined by C1. G184's two in-body
  reads reach the addressed holder (modulo B3 for paths through raw dereferences). D2's `p == null` row keeps the holder used.
- **Adversarial cases.** An address stored into E keeps its holder used forever. A pointer read back from E reaches only E, so
  writes or reads of Loan-carrying values into the body's holders through it are condition 5's undefined behavior (conservative or
  author's breach). Copies of `during self` pointers used after the owner moves are D7's obligation. Safe code needs an unsafe
  access to observe a violation, and the library keepers (`BufferView`, `UniqSlice`, remainders) hold a Loan or re-read their Field.
  Enum payloads, nested Fields and `obj` payloads with `during self` follow own steps. `during self` inside `Option<…>` is rejected
  (D17). Generic `Box<T>{p: raw/T during self}` with `T` a borrow or raw Type counts the pointee as stored content and keeps its
  Origins. Closures carry addresses, and an erased callee storing an argument through a captured pointer breaks the Retained-dependencies
  sentence. `null`, zero-sized Types, `Raw.slice` and `during static` are consistent (`static` needs a claim, and the pointee Type's
  Origins carry the relations). Pair binders drop raw Origins (sound; N9).
- **Claims, subtyping and liveness.** Covariance of raw Origins matches borrows, and retention "as a borrow would" includes the
  read-through Loans. Invariant positions forbid dropping. A raw Origin adds no liveness, while the retained Loans stay live through
  the carrying value. A covariant fit to a weaker Origin only lowers authority.
- **C1 and C2.** C1 is justified (the safe analogue; it avoids per-declaration Field facts) and sufficient for (d), but see B2 and
  N5. C2 is the minimal general rule (one condition covers integer round trips, pointers hidden in nominal results and
  callee-stored pointers) and sufficient. Its strength falls on clients: a pointer laundered through `Cursor<raw/X>` and read back
  makes the client's access undefined, with no diagnostic. This is acceptable as an explicit unsafe obligation, and the owner should
  confirm knowing it. I found no shorter general design. The design's own rule ("a raw pointer is a reference whose reach is what it
  carries and whose stated Origin is the borrow Origin it would have had") is the general one once B3 extends it to raw
  dereferences.
- **Other SPEC examples.** `Buffer`/`BufferView` (apart from B1's spelling) are valid: the `init` claim is in `unsafe`. `push`,
  `get` and `items` read `self.data` with the receiver's Origin. `BufferView.init` gets `during source` without a claim, and
  `BufferView.get` stays within `self.source`. §5.4's `number`/`r` example keeps its verdicts.
- **Decision coverage.** D1-D18 each appear in SPEC at the places INTEGRATED names (D15 see B4; D5 see B1). D9, D19 and D20 are
  in STATUS, PLAN, DECISIONS and COMPILER_ARCHITECTURE. Nothing in SPEC goes beyond the design's §3 items, which the owner confirmed
  ("残りの判断事項はすべて推奨どおり").
- **Terminology and anchors.** "Origin", "claim", "holder", "address" and "storage the body does not own" are used consistently.
  The Appendix E rows are in alphabetical order. All added links resolve.
- **Records.** PLAN has 222 lines (≤ 225) and records current facts only. STATUS edits its existing row. COMPILER_ARCHITECTURE's
  R4 exit matches DECISIONS. Unit names and owners match across PLAN, DECISIONS and the design. INTEGRATED's lifecycle holds: the
  design is 取り込み済み with every section mapped, transferred or recorded, and the old plan is 完了 in `Obsolete` with only its
  status paragraph changed (diff checked) and its earlier register entry updated, as the register allows. The old plan is linked
  nowhere else by its `Proposals` path, and the relative links resolve. The STATUS gap (`BorrowOriginSemantics_Kd` until G160-0)
  follows AGENTS Diagnostics.

## Verdict

**Accept with the listed corrections.** B1-B5 must be fixed before integration. B1 (§13.5.1 and §F.9.4 amendments), B3 (raw
dereferences in typed paths and addresses) and B5 are text corrections within the owner's decisions. B2 corrects C1's stated
premise and adds two library bodies to G160-3; it should reach the owner with the C1/C2 confirmation. B4 needs the planning
session to fix D15's wording and `placeEntry`'s Type, also worth relaying with C1/C2. The corrected B1-B4 text needs a short
re-check, not a new round. The N items may become open issues.
