# G182 design: O2 growth under MIR code generation

Status: design phase of PLAN G182 for the R5 planning session (2026-10-31), revision 1 after review 1 (REVISE). Working record, not
authority: a choice becomes authority only as the DECISIONS entry or amendment a landing unit adds. No source change; every probe
below was throwaway and is outside the tree.

Base: `origin/dev` `d459bc5b8`. Measurements are deterministic O2 IR instruction counts (LLVM 22.1.8 `opt -O2`, the counting rule of
`compare-fixtures.ps1 -O2Metrics`: an instruction is a line indented by two spaces inside a `define`; "runtime" are the defines of
`WindowsRuntime.ll.in`, "bodies" every other define):

- Corpus census: the 6,137 `*.O2.ll` of the default Session `20261031-130140-620-session-batch78` (`r5-merge`, compiler sources
  identical to `d459bc5b8`: `git diff a9e5e34b5 d459bc5b8 -- src/Kimi` is empty). `opt -O2` on a fixture's `.ll` reproduces its
  `.O2.ll` count exactly (49 of 49 checked).
- Probes: CLI builds of small programs with the worktree's `Kimi.dll`, user functions made external where noted (memory note
  "O2 IR inspection").
- Experiments: the fixtures' `.ll` rewritten by a script to emulate an option, then `opt -O2` (scripts `count.py`, `census.py`,
  `wordcopy.py`, `byvalue.py` in `C:\Users\bwff1\AppData\Local\Temp\claude\r5\g182\`).

Census totals: 848,907 instructions, runtime 432,933, bodies 415,974.

The old generator is gone (G4); "legacy" figures in the row are history and are not re-measured.

## Revision 1 (review 1 and the planning session's question)

| Item | Revision 0 | Revision 1 | Source |
| --- | --- | --- | --- |
| (3a) choice | B, by value, as a Representation amendment | A, copy: one destruction signature, no R5-M104 rejection reversed; B compared and rejected on consistency (B's extra -248 is per-site slot traffic off the hot path) | planning question, B1 |
| (3a) B's record | "an amendment", "one row instead of two" | if B is chosen, a superseding entry naming both reversed rejections; `_drop` gets a heap and an inline form | B1 |
| (3a) tests | an O2 regression test | the rule pinned at O0 in `FunctionCodegenTest`; O2 totals as commit evidence (R5-M129) | B2 |
| (3a) expectations | line 69 | A's list: `FunctionCodegenTest.Erasures.cs` :133-134, :142-147; unchanged ones named; behavior change with equal native outcomes | B3 |
| (3a) census | "60 dead entries" | 60 entries without a direct call: 30 dead (238 instructions; A and B remove the same), 30 reached through collections or objects | N1 |
| (3a) measurement | A on the 49 only | A on all 432 table fixtures; rewrites load the word inside the guard; re-measured at landing | N3 |
| (3a) documents | ARCH 145 not named | A changes no ARCH text; B would change ARCH "Drop glue" | N5 |
| U1 / U2 class | U2 Minor; "no design review needed" | U1 and U2 Major; this design and its review serve as their design review (WORKFLOW Design step 4) | B4, N4 |
| (2) / (3d) | ranking change only; (3d) as guidance | the break in `growth`'s meaning noted; (3d) stated as a fact about the metric | N6 |

## Summary

| Part | Today | Cause (owner) | Recommendation | Class, units |
| --- | --- | --- | --- | --- |
| (1) u64/usize position | 16 vs 15 O2 instructions per selection (1 `smax`; 17 vs 14 machine instructions); O0 58 vs 40; 0 of 6,137 fixtures | LLVM does not fold `icmp ugt (smax x, -1), C`; the join is R5-M84's (MirBuilder, R4-held) | **Keep**; record the measurement in PLAN; no DECISIONS entry | none (keep); supersede = Major, R4's release |
| (2) metric | `HelloLines` bodies 72, 21 of them the string glue holding the inlined helper; 1,406 fixtures carry 35,484 such instructions | the metric's premise "runtime helpers are the same text in both runs" holds at O0, not O2 (inlining, IPSCCP) | **Rank and total by whole-module instructions**; runtime/bodies stay as information | Major (documented ranking contract), U2 (≤ 30 min) |
| (3a) dead adapter entry | 30 dead `__kimi_erase<N>` (238 instructions) in a census of 60 without a direct call | the drop entry receives the Function value's address, so the value's alloca escapes and its table store stays (Backend codegen, R5-M104) | **The Function glue passes a copy of the word (A)**: -181 on the 432 table fixtures | Major (R5-M104 amendment), U1 (≤ 1 h) |
| (3b) self-recursive adapter | `__kimi_erase7` 15 instructions = the direct-call program's `ping` (15) | none: LLVM keeps the recursion where it is; the adapter costs nothing | **No action**; the row's +45 does not reproduce | none |
| (3c) general `__kimi_abort` | 45 instructions in 4,213 modules, 13 in 426 | IPSCCP runs before inlining removes the other callers | **Keep** (cold, once per module) | none |
| (3d) E13 inputs | 1,308 of 6,137 fixtures fold to `__kimi_start` alone | test programs with constant inputs | a sentence in VERIFICATION's `-O2Metrics` paragraph stating what the metric measures for them (with U2) | with U2 |

Order by benefit: U1, then U2 (independent; U1's evidence uses the O2 line's existing `all` totals).

## (1) F19's join for a u64 or usize position

Measurement (probe `a[b[i]]`, `a: ref/[4 of i32]`, `b: ref/[4 of T]`, `i: isize`, functions made external):

| `b`'s element Type | O2 instructions | machine instructions (llc -O2) | O0 instructions |
| --- | --- | --- | --- |
| `u64` | 16 (`%l7.0 = llvm.smax.i64(%t8, -1)` before `icmp ugt %l7.0, 3`) | 17 (+`test`, `mov $-1`, `cmovns`) | 58 |
| `i64`, `isize` | 15 | 14 | 40 |

Census: no fixture's O2 IR has `llvm.smax.i64(x, -1)` (254 modules use `smax` with other operands, e.g. capacity growth); the join
survives only when a non-constant u64/usize value indexes, which no fixture or corpus kernel does.

Cause chain: `MirBuilder.Position` (`Mir/Build/MirBuilder.Selections.cs`, R5-M84) converts a u64 position by `x <= isize.MaxValue`
in u64, else -1 (`Unresolved`, a diamond); LLVM makes the diamond `smax(x, -1)` and, lacking a fold for
`icmp ugt (smax x, -1), C` with `C >= 0`, keeps it before the Index's unsigned check. Owner: the MIR builder (R4 holds
`MirBuilder*`); the missing fold is LLVM's.

What R5-M84 rejected: "keeping Wrap for a same-width unsigned position (a second encoding of an unresolved position)". Does the
reason still hold?

- Behavior would not change: every consumer of a position rejects every negative isize, not only -1. The Index's check is unsigned
  (`icmp ugt x, len - 1`); a range's chain checks `0 <= start <= end <= length`; `^x` (`MirBuilder.Boundary`) tests `0 <= x` on the
  converted value before `L - x`. A wrapped u64 above isize's maximum is negative, so each fails at the same check with
  KIMI_E_INDEX_BOUNDS at the selection. R5-M154 already relies on "every selection rejects any negative position".
- The reason itself still holds: under Wrap an unresolved position is "any negative value" for u64/usize and exactly -1 for i128,
  u128 and a Type parameter, so the contract every consumer must keep widens from "-1" to "negative", and two conversion shapes
  remain. The gain is one ALU instruction per selection by a non-isize unsigned value, which no fixture has.

Options:

- K (recommended): keep R5-M84. Record in PLAN (closing G182 (1)) that the cost is 1 O2 / 3 machine instructions, O0 +18, zero
  fixtures, and that an LLVM fold would remove it. No DECISIONS entry (reconsidered, not changed).
- S: supersede R5-M84's rejected clause: a position whose Type is unsigned of isize's width converts by `MirCastKind.Wrap` (no
  block), the selection contract stated as "rejects every negative position". Major; edits `MirBuilder.Position` (R4's release
  needed), MIR goldens (`tests/mir`, held), a superseding DECISIONS entry naming the reason that no longer holds; one unit ≤ 1 h after
  the release. Not recommended: it trades one canonical encoding for one instruction on a rare form.

## (2) The `-O2Metrics` body figure

Measurement: `HelloLines` today: all 164, runtime 92, bodies 72, of which `__kimi_drop1` (the string glue) is 21: its O0 text is one
call of `__kimi_destroy_string` (`DropGlueRow.Release`), its only caller, so O2 inlines the helper into the glue and deletes the helper.
Census: 1,406 fixtures carry a string glue holding the inlined helper, 35,484 instructions counted as bodies. Runtime helpers are not
stable either: `__kimi_abort` is 45 instructions in 4,213 modules, 13 in 426, 18 in 23, 6 and 34 in one each (IPSCCP specializes it
when its remaining callers agree), and `__kimi_alloc`/`__kimi_free` inline into bodies.

Cause: the script's ranking rests on "the runtime helpers are the same text in both runs" (its comment). That holds for the O0
template; at O2 inlining and interprocedural constant propagation move code between helpers and bodies per module, so a change that
adds or removes one caller of a helper shifts instructions across the split. Owner: `scripts/compare-fixtures.ps1` (E lane, E13).
With the old generator gone, both runs of a comparison come from one generator, so the row's ×1.169 cross-generator artefact cannot
recur; the instability inside one generator remains.

Options:

- T (recommended): rank and compute growth by the module's whole instruction count (`instructions`), keep `runtime` and `bodies` as
  information in `o2-metrics.json`, and correct the comment and VERIFICATION's `-O2Metrics` paragraph. Totals are invariant to inlining
  between helpers and bodies. This changes the documented ranking contract (VERIFICATION:134-138, the script's header,
  `FixtureComparisonTest.ps1`:133 and :142, which expect the body ranking), so U2 is Major. The break: a `growth` written before U2 is
  body growth, after it module growth; earlier `o2-metrics.json` files keep each fixture's `instructions`, so module growth can be
  computed from them; the VERIFICATION paragraph states the date of the change.
- R: classify the string glue as runtime. Rejected: the script would need the O0 text to recognize a one-call wrapper (a shape
  allowlist), and abort specialization and alloc inlining still move code.
- C: make the string's drop call `__kimi_destroy_string` directly (no glue item). Rejected: `DropGlue.Row`'s Release row keeps one
  drop path per Type (one glue item each, `DropGlue.cs`'s contract); O2 already removes the wrapper, and the change would serve a metric.

## (3a) The dead adapter entry kept by its table store

Measurement: probe `let inner: () -> i32 = func () => 7` / `let outer: () -> i32 = func [inner@move] () => inner() + 1`: both
calls of `outer` fold, yet `__kimi_f0` still stores `@__kimi_erase4_table` into its Function value and calls
`@__kimi_erase4_drop(%l3, ...)`, so `__kimi_erase4` (15 instructions, never called) stays. Census: 49 fixtures have an
`__kimi_erase<N>` define with no direct call, 60 defines and 415 instructions. That test is "no direct call", not "dead": 30 of them
(238 instructions) are dead and kept only by a table store into a stack Function value; the other 30 (177 instructions) are reached
through Function values held in Arrays, Dictionaries or objects (review 1's reading; e.g. `CallbackFunctionArrayIteration`, whose table
has a null drop entry) and stay under both A and B (counted on the rewritten modules).

Cause chain: R5-M104's drop entry takes a glue function's parameters and receives the Function value's address
(`FunctionCodegen.DestroyErased`) -> once O2 devirtualizes it the call is direct but not inlined, so the value's alloca escapes ->
the store of the table at offset 8 cannot be removed -> the private table stays referenced -> its entry stays. Owner: Backend codegen
(`FunctionCodegen.Erasures`), not R4-held.

Options (O2 instructions, whole module; the 432 fixtures whose `.ll` has an adapter table, base 37,742; the rewrites load the word
inside the glue's guard, as a landing would):

| Option | 432 fixtures | Down | Up | Unchanged | Dead entries removed |
| --- | --- | --- | --- | --- | --- |
| A, copy: the Function glue passes the address of a copy of the word, in a slot of its own | 37,561 (-181) | 26 (-289) | 12 (+108) | 394 | 30 (238) |
| B, by value: the drop entry takes the word (i64); an inline environment's drop entry becomes a spilling `_drop` | 37,313 (-429) | 35 (-446) | 9 (+17) | 388 | the same 30 (238) |
| C: specialize the drop at a constant table | - | - | - | - | rejected: codegen does not know a Function local's table at its drop (a fact MIR does not publish); a consumer deciding what its owner should publish |
| D: keep | 37,742 | - | - | - | 0 |

A and B remove the same dead entries. B's further -248 is the copy's per-site cost where the drop entry is not inlined (an
`alloca`, two lifetime markers, a load and a store: about +5 to +8 IR instructions; at machine level one store and one load of a
stack slot), largest in the callback-in-collection and object-payload shapes (`CallbackFunctionDictionary` A +15 / B -3,
`ObjectCallablePayloadObjrefParameter` A +15 / B +3). Under B those 9 fixtures pay one `load i64` per drop through an unknown table.

Consistency first (AGENTS "Implementation Design"; the planning session's question):

| | A, copy | B, by value |
| --- | --- | --- |
| What changes | `DestroyErased` only: the Function glue writes an i64 slot in its entry block and, in the guarded block, loads the word at the value's address, stores it in the slot and calls the drop entry with the slot, its location and length | the drop entry's signature `(i64, ptr, i64)`; `DestroyErased` passes the loaded word; `ModuleCodegen.Release` becomes a heap form (`inttoptr`, glue, free) and an inline form (spill, glue) per adapter; the table's drop is always the adapter's `_drop` |
| Destruction signatures | one: every drop entry keeps `DropGlue.Parameters` (R5-M104, ARCH "Drop glue": `DropGlue.Parameters`, a glue function's parameters) | two: the glue signature and the drop entry's `(i64, ptr, i64)` |
| R5-M104's rejections | none reversed; an inline environment's glue stays its drop entry | reverses two: "the old generator's drop entry `(i64 word, context, location)` (a second destruction signature; an inline environment destroyed in a spill)" and "a drop-entry function for an inline environment (its glue is one)" |
| Shape precedent | the adapter entry already spills an inline word to a slot of its own and passes it (`FunctionCodegen.Environment`, `FunctionCodegen.Erasures.cs:192-206`) | the same spill, moved into a new `_drop` per inline-environment adapter |
| O0 | +3 instructions per Function glue (one per Function Type) | +1 load per Function glue; +1 function (about 4 instructions) per adapter with an inline environment needing a drop |
| Documents | R5-M104 amendment (the address the drop entry receives); comments in `FunctionCodegen.Erasures` (header lines 11 and 26, `DestroyErased`) | a superseding entry for the two rejections, R5-M104's Representation clause, ARCH "Drop glue" (`DropGlue.Parameters` no longer covers drop entries), comments in both Erasures files |
| Code (estimated) | about +8 lines in one method | about +25 lines over four methods and two files |

Recommendation: **A**. It keeps R5-M104's one destruction signature and both its rejections, reuses the entry's spill shape, and is
the shorter change; B's extra -248 is per-site slot traffic around a non-inlined destruction of a Function value, off every measured
hot path: the O2 corpus's erased kernel `ErasedCalls` (2^27 calls through Function values held in an Array) is 594 O2 instructions,
599 under A and 595 under B, the difference only in the Array's one-time element drop loop, its call loop identical. It does not
outweigh a second signature. Record (3a)'s choice in U1's DECISIONS amendment of R5-M104 with option B rejected for that reason.

If the planning session chooses B instead, its superseding entry must name each reversed rejection and why it no longer holds or is
outweighed: the spill is the shape every inline call already uses (`FunctionCodegen.Erasures.cs:192-206`), and the second signature
buys -248 O2 instructions on the 432 fixtures (no runtime evidence); "one row instead of two" does not hold (`_drop` gets a heap and an
inline form).

Design of A (U1):

- `FunctionCodegen.DestroyErased` (the Function glue's row, written at the glue's entry): an i64 slot first (`WriteSlot`, the entry's
  shape), then the table's drop entry loaded and tested as today; in the guarded block the word loaded at the value's address, stored in
  the slot, and the drop entry called through the one destruction-call writer with the slot, the location and the length. The value is
  dead after its drop, so destroying the moved word in place in the slot is the value's destruction (an inline environment of at most 8
  bytes has no address identity).
- R5-M104 amendment: "the drop entry receives the address of the environment word, a copy in the Function glue's own slot (G182: the
  value's own address made every dropped Function value escape and kept its table and dead entry); rejected: B (a second destruction
  signature for -248 O2 instructions)". No vocabulary entry, no ARCH change (`DropGlue.Parameters` unchanged), IMPL unchanged (table
  layout and runtime metadata are implementation-defined, IMPL 21.1.3). Comments: `FunctionCodegen.Erasures` header (lines 11, 26) and
  `DestroyErased`'s.
- Risks: O0 +3 instructions per Function glue; compile time and compiler allocation: none (text only, no new item or `FunctionAbi`).
  Behavior: none; many `.ll` files differ with equal native outcomes, so the landing is a behavior change (not behavior-preserving)
  verified by native outcomes and O2 totals.
- Tests (R5-M129: no managed test asserts O2 code): the rule is pinned at O0 in
  `FunctionCodegenTest.AFunctionValueIsDestroyedByItsTablesDropEntry` (`FunctionCodegenTest.Erasures.cs:133-155`). Changed
  expectations, each replaced by an equally exact one and named in the commit: :133-134, the comment ("with its own parameters"
  becomes the word's copy and its own location and length); :142-145, the glue's entry block, gains the slot (`alloca i64, align 8`)
  before today's six shapes; :146-147, the drop call's operands (`glue.Instructions[3]`, an index the slot shifts, and the glue's
  parameters), become the loaded drop entry, the slot, the location and the length, with new assertions that the guarded block is
  exactly `load i64, ptr %, align 8` of the glue's first parameter, `store i64 %, ptr %, align 8` of that load into the slot, the call,
  and the branch. Unchanged: :50-52's comment (the drop
  entry still receives the word's address), :69-70 (an inline environment's drop entry is its glue, `(ptr, ptr, i64)`), :80-81 (the
  release loads the pointer at its first parameter), :148-154 (the owner call), `FunctionCodegenTest.Shims`/`.Virtuals` (tables only),
  native expectations, milestone baselines and golden IR. The O2 effect is commit evidence: `compare-fixtures -O2Metrics` module totals
  against the parent (expected 37,561 (-181) on the 432; re-measured at landing), not a test.
- Checks: `FunctionCodegenTest`, the erasure native families at O0 and O2 (`-Fixtures 'Erasure*.ll','HeapClosure*.ll',
  'FunctionCapture*.ll','StructCapture*.ll','ObjectCallablePayload*.ll','Callback*.ll','FunctionDefault*.ll','ContextualClosure*.ll',
  'AggregateDefault*.ll','GenericCallback*.ll','GenericClosureErasure*.ll'`), a Session at the end.

## (3b) Two default Closures' self-recursive adapter

Measurement: `FunctionDefaultMutualRecursion` today: all 151, runtime 111, bodies 40 (`__kimi_f2` 22 with the harness's
`Console.writeLine("done")`, `__kimi_erase7` 15, `__kimi_start` 3). The same program written with two direct functions
(`ping`/`pong` without default Closures) compiles to the same bodies: its `ping` is 15 instructions, the caller 12, `__kimi_start` 3,
exactly the Closure probe's `__kimi_erase7` 15, caller 12 and start 3.

Cause: the recursion `ping -> f -> pong -> g -> ping` is real; LLVM inlines the bodies into one adapter and keeps the recursion there.
The unused environment, context and location parameters are `readnone` and cost no instruction. The row's +45 was measured against
the old generator, which no longer exists; against the direct-call program the overhead is 0. Recommendation: no action; close (3b).

## (3c) `__kimi_abort` stays general

Measurement: `StringGuardAbort` today: all 96, runtime 92, bodies 4; the program folds to `__kimi_abort(6, ...)` (its overflow is a
constant), and `__kimi_abort` stays the general 45-instruction routine (reason table lookup, OS-error decimal), where a module whose
callers all agree gets the 13-instruction specialization.

Cause: IPSCCP runs before inlining removes the string guard's and `Console.writeLine`'s other abort callers, so it sees disagreeing
constants (the O2 define keeps IPSCCP's `range(i32 0, 7)` on `%reason` and `range(i64 13, 15)` on the length although one call remains);
nothing later re-specializes. Owner: LLVM's pass order; the helper is the runtime template's.

Options: keep (recommended): a cold, noreturn routine emitted once per module; any program with two abort reasons needs the general
form anyway (4,213 of 6,137 modules have it). Rejected: per-reason abort entries (duplicates the routine whenever two reasons occur,
the common case); a second IPSCCP or function specialization pass (pipeline policy, LLVM_OPTIMIZATION's, for 32 instructions in
programs that fold to one abort).

## (3d) E13's inputs

1,308 of the 6,137 fixtures' O2 modules have `__kimi_start` as their only body: their programs fold to constants, so their O2 counts
measure the runtime's specialization (3c) and not generated code. With U2, VERIFICATION's `-O2Metrics` paragraph states that fact
about the metric (a fixture folded to constants contributes runtime helpers only); where O2 evidence is required stays as VERIFICATION
and R5-M131/R5-M132 have it. No new mechanism, no policy change.

## Units

| Unit | Content | Class | Size | Benefit |
| --- | --- | --- | --- | --- |
| U1 | (3a) option A in `DestroyErased`, R5-M104 amendment, O0 test in `FunctionCodegenTest`, natives O0/O2, O2 totals as evidence | Major; this design and its review are its design review | ≤ 1 h | -181 O2 instructions on the 432 table fixtures (37,742 -> 37,561); the 30 dead entries go |
| U2 | (2) option T and (3d)'s sentence: `compare-fixtures.ps1`, `FixtureComparisonTest.ps1` (:133, :142), VERIFICATION:134-138 | Major (the documented ranking contract); this design and its review are its design review; checks: the affected workflow checks | ≤ 30 min | stable evidence for every later O2 change, U1's included |
| - | (1) keep, (3b) close, (3c) keep: PLAN only | documentation | in G182's closing PLAN edit | - |

Open questions for the planning session: (3a) A or B (recommended A; B needs the superseding entry above). (1) S is the only option
that needs R4's release; no owner question arises unless the planning session prefers S (its decision, as R5-M84 was).
