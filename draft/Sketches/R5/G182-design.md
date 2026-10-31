# G182 design: O2 growth under MIR code generation

Status: design phase of PLAN G182 for the R5 planning session (2026-10-31). Working record, not authority: a choice becomes
authority only as the DECISIONS entry or amendment a landing unit adds. No source change; every probe below was throwaway and is
outside the tree.

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

## Summary

| Part | Today | Cause (owner) | Recommendation | Class, units |
| --- | --- | --- | --- | --- |
| (1) u64/usize position | 16 vs 15 O2 instructions per selection (1 `smax`; 17 vs 14 machine instructions); O0 58 vs 40; 0 of 6,137 fixtures | LLVM does not fold `icmp ugt (smax x, -1), C`; the join is R5-M84's (MirBuilder, R4-held) | **Keep**; record the measurement in PLAN; no DECISIONS entry | none (keep); supersede = Major, R4's release |
| (2) metric | `HelloLines` bodies 72, 21 of them the string glue holding the inlined helper; 1,406 fixtures carry 35,484 such instructions | the metric's premise "runtime helpers are the same text in both runs" holds at O0, not O2 (inlining, IPSCCP) | **Rank and total by whole-module instructions**; runtime/bodies stay as information | Minor, U2 (≤ 30 min) |
| (3a) dead adapter entry | 49 fixtures, 60 uncalled `__kimi_erase<N>` (415 instructions) | the drop entry receives the Function value's address, so the value's alloca escapes and its table store stays (Backend codegen, R5-M104) | **Drop entry takes the environment word by value** | Major (R5-M104 amendment), U1 (≤ 1 h) |
| (3b) self-recursive adapter | `__kimi_erase7` 15 instructions = the direct-call program's `ping` (15) | none: LLVM keeps the recursion where it is; the adapter costs nothing | **No action**; the row's +45 does not reproduce | none |
| (3c) general `__kimi_abort` | 45 instructions in 4,213 modules, 13 in 426 | IPSCCP runs before inlining removes the other callers | **Keep** (cold, once per module) | none |
| (3d) E13 inputs | 1,308 of 6,137 fixtures fold to `__kimi_start` alone | test programs with constant inputs | guidance in VERIFICATION's `-O2Metrics` paragraph (with U2) | with U2 |

Order by benefit: U1, then U2 (U2 is independent; U1's evidence can use the O2 line's existing `all` totals).

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
  between helpers and bodies.
- R: classify the string glue as runtime. Rejected: the script would need the O0 text to recognize a one-call wrapper (a shape
  allowlist), and abort specialization and alloc inlining still move code.
- C: make the string's drop call `__kimi_destroy_string` directly (no glue item). Rejected: `DropGlue.Row`'s Release row keeps one
  drop path per Type (one glue item each, `DropGlue.cs`'s contract); O2 already removes the wrapper, and the change would serve a metric.

## (3a) The dead adapter entry kept by its table store

Measurement: probe `let inner: () -> i32 = func () => 7` / `let outer: () -> i32 = func [inner@move] () => inner() + 1`: both
calls of `outer` fold, yet `__kimi_f0` still stores `@__kimi_erase4_table` into its Function value and calls
`@__kimi_erase4_drop(%l3, ...)`, so `__kimi_erase4` (15 instructions, never called) stays. Census: 49 fixtures with 60 uncalled
`__kimi_erase<N>` defines, 415 instructions (`ErasureRows`, `HeapClosure*`, `FunctionCapture*`, `StructCapture*`, `Callback*`,
`ObjectCallablePayload*`, ...).

Cause chain: R5-M104's drop entry takes a glue function's parameters and receives the Function value's address
(`FunctionCodegen.DestroyErased`) -> once O2 devirtualizes it the call is direct but not inlined, so the value's alloca escapes ->
the store of the table at offset 8 cannot be removed -> the private table stays referenced -> its entry stays. Owner: Backend codegen
(`FunctionCodegen.Erasures`, `ModuleCodegen.Erasures`), not R4-held.

Options and experiments (the 49 fixtures; O2 instructions, whole module):

| Option | Total 12,626 -> | Down | Up | Notes |
| --- | --- | --- | --- | --- |
| A: the Function glue passes a copy of the word (entry-block slot) | 12,445 (-181) | 26 (-289) | 12 (+108) | +5 per site whose table is unknown (alloca, lifetime, load, store) |
| B: the drop entry takes the word by value (i64) | 12,197 (-429) | 35 (-446) | 9 (+17) | +1 to +3 where the table is unknown |
| C: specialize the drop at a constant table | - | - | - | rejected: codegen does not know a Function local's table at its drop (a fact MIR does not publish); a consumer deciding what its owner should publish |
| D: keep | 12,626 | - | - | dead code only, no speed effect measured |

All 432 fixtures with an adapter table under B: 37,742 -> 37,313 (-429): the same 35 down and 9 up, the other 388 unchanged:
B changes no O2 module outside the 49. The probes under B: the nested capture above 154 -> 137
(`__kimi_erase4` gone; `__kimi_erase3`, 1 instruction, stays: its table is stored in the heap environment), a moved string capture
called twice 189 -> 171 (`__kimi_erase2` gone).

Recommendation: B. Design:

- The drop entry's signature: `(i64 environment, ptr location, i64 length)`, existing `AbiParameterKind`s (Environment, Location,
  LocationLength); a static `FunctionAbi` beside `DropEntry` in `FunctionCodegen.Erasures` replaces `DropGlue.Parameters` for drop entries.
- `FunctionCodegen.DestroyErased` loads the word at the value's address and passes it (the call writer's one path).
- Every adapter whose environment may need a drop gets its own `__kimi_erase<N>_drop` (`ModuleCodegen.Release`): a heap environment's
  `inttoptr`s the word, runs the environment's glue and the heap release (as today, minus the load); an inline one spills the word
  to an entry slot and runs the environment's glue on it. The table's drop field is always the adapter's own release or null, which
  removes today's case "an inline environment's glue is the drop entry": one row instead of two.
- R5-M104's Representation clause gets an amendment (the drop entry receives the environment word, not the Function value's
  address; reason: the address made every Function value escape). No vocabulary entry, IMPL unchanged (table layout and runtime
  metadata are implementation-defined, IMPL 21.1.3).
- Risks: O0 adds one small function per adapter with an inline environment that needs a drop (spill and call) and one load per
  Function glue; compile time and compiler allocation unchanged in kind (one more `FunctionAbi` per such adapter, cached by
  `Helper` as today). Behavior: none (the word is moved, not copied: the value is dead after its drop; an inline environment of at
  most 8 bytes has no address identity).
- Expectation change required by the amendment (named in the commit): `FunctionCodegenTest.Erasures` asserts that an inline
  environment's drop entry is the environment's glue (`StartsWith("__kimi_drop")`, line 69); under B it is the adapter's
  `_drop`. `FunctionCodegenTest.Shims` and `.Virtuals` read tables too and are re-run.
- Checks: `FunctionCodegenTest`, the erasure native fixture families at O0 and O2 (`-Fixtures 'Erasure*.ll','HeapClosure*.ll',
  'FunctionCapture*.ll','StructCapture*.ll','ObjectCallablePayload*.ll','Callback*.ll','FunctionDefault*.ll'`), an O2 regression
  asserting that the probe above leaves no `__kimi_erase<N>` define, and `compare-fixtures -O2Metrics` totals against the parent.

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
measure the runtime's specialization (3c) and not generated code. With U2, VERIFICATION's `-O2Metrics` paragraph says so and points
code-quality evidence to the O2 corpus kernels (R5-M131/R5-M132's A/B) and to external functions (`a[b[i]]`-style probes) for a
form's O2 IR. No new mechanism.

## Units

| Unit | Content | Class | Size | Benefit |
| --- | --- | --- | --- | --- |
| U1 | (3a) option B, R5-M104 amendment, O2 regression, natives O0/O2 | Major (this design; no design review needed: no open design issue) | ≤ 1 h | -429 O2 instructions on the 49 fixtures (all 432 table fixtures: the same -429, 388 unchanged); removes one drop-entry case |
| U2 | (2) option T and (3d)'s paragraph: `compare-fixtures.ps1`, `FixtureComparisonTest.ps1`, VERIFICATION | Minor (tooling; affected workflow checks) | ≤ 30 min | correct evidence for every later O2 change |
| - | (1) keep, (3b) close, (3c) keep: PLAN only | documentation | in G182's closing PLAN edit | - |

Open questions for the planning session: none blocking. (1) S is the only option that needs R4's release; no owner question
arises unless the planning session prefers S (then it is the planning session's decision, as R5-M84 was).
