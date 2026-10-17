# A5 step 1, design 1b: MIR facts (designer B)

Base: `bin/worktrees/a5-step1` at `5c42b577` (origin/dev). Read-only; nothing built or run. `M/` = `src/Kimi/Compiler/Mir/`,
`MB.x` = `M/Build/MirBuilder.x.cs`. (H) marks an unverified claim. Serialized shared files are named **in bold**.

Summary of choices:

| Item | Chosen design | Shared MIR files | Size (src) | Owner decision |
| --- | --- | --- | --- | --- |
| G92a | Rule AV "assumed completion": every node's own operation is built after a diverging operand; only a Never form has no value; an operation whose every input is that Never value is not built | **MirBuilder**, **MirValidator** (+1 line Binding) | ≈ −20 | Planning session (supersedes R4-M37's `Set` rule); SPEC gap reported |
| G92b | Fold a written exclusive borrow on a preparation path into the path: one reservation at the lending point | **MirBuilder** | ≈ +5 | **Yes**: (b2) fold vs (b1) parent chain in A5 |
| G94 | `Locate`: a built-in selection fixes its receiver where located: a Copy handle copied, any other storage lent shared (the range backing, or a guard ended by `EndBorrow` once the index is evaluated) | **MirBuilder** | ≈ +14 | Planning session (amends R4-M40) |
| G91 | The Destruction walk enters each reported part once per query (`IMirDestruction.Enter`); visitors drop their own cuts | none (`MirTypes`, A4) | ≈ +4 | none |

## 1. G92 (a): which values exist on assumed paths

**Required meaning.** SPEC 14.10.3: on an assumed path a transfer, `$abort` and a Never call "continue after their operands are
evaluated"; all checks apply there (uninitialized or Moved reads, second Moves, Loan conflicts). SPEC 3.8 (Never fitting): a Never
expression fits any expected Type without a value conversion; Never has no values (3.1.3). SPEC 15.6.7: a call whose operand does not
complete enters no callee at run time. So on the assumed path every enclosing node runs its own operation (call, construction,
update write, drop at `=`); only the Never form itself yields nothing.

**What the representation wrongly allows.** The builder ties "has a value" to the node's HIR Completion, not to the Never form:

- `Set` (`MB.Values:398`) assigns nothing for any node that does not complete, so a construction with a diverging element
  (`(p@ref, (return 1))`, `S(a: box@move, b: stop())`) is never built: its operands' Moves and Loans vanish (A1 misses
  `let t = (box@move, (return 1)); consume(box@move)`, A5 misses the tuple's Loan at a continued call's activation).
- `Reached` (`MB.Calls:161`) replaces any non-completing operand by a fresh unwritten Never temp, so a continued call drops the value an
  inner continued call wrote: `both(h(p@uniq, (return 1)), q@uniq)` passes `both` a Never instead of `h`'s result.
- An update whose right side diverges (`x += (return 1)`) builds `$w = TwoPhaseUniq(x)` but not the write through `$w` (`Set` at
  `MB.Expansions:217`, `Overwrite`'s guard at `MB.Values:354`), so its activation is never checked; one whose target index diverges
  (`a[(return 0)] = v`) skips the write too.
- Eleven more `Completes(…)` guards decide, each locally, whether a node's own operation exists in check-only code (table below).

**Owner.** The MIR builder decides values (one rule, at `Set`); the validator owns Never fitting; Binding owns the published Types.
A5 needs no clause of its own: assumed values are ordinary values on P2 chains (A3 F10), and A3's `val`, `CS` and `CS_call` apply.

**Rule AV (assumed completion).** Every node is built where its building reaches, also after an operand that does not complete, and
makes its own value and performs its own accesses as at run time. A Never form (a transfer, `$abort`, a call or intrinsic whose
result Type is Never) makes no value: it writes no destination, and where a consumer takes it as an operand it is its unwritten temp of
Type Never, the **Never value**, which holds no Loan, moves nothing and fits any operand position (SPEC 3.8). An operation whose every
input is the Never value is the Never value too and is not built (a placement `x = (return 1)`, a borrow, a Discriminant, a Cast of
it; the old value at `=` is then not dropped either). Any operation with another input is built, its Never inputs fitting.

Consequences: H1 (continued call results), H2 (update writes and activations; a diverging target index still writes `a[_k]` with
`_k` unwritten), H3 (constructions keep their operands' Moves and Loans) are closed. A1's C3 holds unchanged:
`let x: i32 = stop()` and `x = stop()` still leave x unwritten (the placement's only input is the Never value). It changes only
operations with a real input: `let x: i32 = (return 1) + 1` now initializes x in check-only code (one A1 problem fewer).

**SPEC gap (report, no rule change):** 14.10.3 does not say whether a Never form's assumed completion initializes what it is placed
into. AV keeps today's answer (no); the opposite reading would make `let x: i32 = stop(); return x` valid and turn
`x = stop()` into a checked write.

**Sites** (all `Completes` reads in the builder):

| Site | Today | AV |
| --- | --- | --- |
| `Set` (`MB.Values:398`) | assigns only where the node completes | `if (!this.Absent(rvalue)) Make(…)`; `Absent`: the rvalue has an input and each input (operand, or its Place for Ref/Len/Discriminant/Adapt) is of Type Never (+8) |
| `Reached` (`MB.Calls:161`, 3 callers) | Never temp for any non-completing operand | **deleted**; a call takes each operand as built |
| `Overwrite` (`MB.Values:354`) | drop only where the assignment completes | drop and placement only where the value is not the Never value (same predicate) |
| `Replace` (`MB.Values:345`) | value temp of the target's Type only if the target completes | unchanged (Type choice, not a value rule) |
| `TypeTest` (`MB.Calls:212`), `Verification` message (`MB.Control:209`), `$tryWrite` operand (`MB.Formatting:44`) | skip where the operand does not complete | skip only where the handle, text or storage is the Never value (a Never Place has nothing to borrow) |
| `Observe` (`MB.Control:244`) | skip `testObserve` | **guard deleted** (its operand is the bool temp) |
| `Slice` (`MB.Expansions:290`), `Boundary` (`:357`), `Collection` (`:416`) | stop the selection / check / appends | **guards deleted**: bounds checks, `RawSlice` and appends are built |
| `Format` (`MB.Formatting:88, 104`) | no discriminant test, no `FormatFinish` | **guards deleted** (the continued write calls give results; G6) |
| `Transfer` (`MB.Control:128`), `Branch` (`:277`), `Join` (`:302`), `Loop` (`:54`), template end (`MB.cs:236`) | structure | **kept**: they decide edges, and SPEC 14.10.3 adds no Runtime Reachability edge |

**Tuple Types with a Never element.** Binding types `(1, (return 1))` as `(i32, Never)` even where `(i32, i32)` is expected
(`Binding.Expressions.cs:1414`), which is why `Reached` substitutes. AV needs the fitted Type: in `BindTuple` an element whose Type is
Never takes the expected component (SPEC 3.8: the element fits it). +1 line in Binding (H: no other Never-part Type reaches a typed
consumer; any left is a validator `Type` finding, i.e. a visible `InternalInvariant_Kd`, not a silent loss).

**Options.**

| Option | Verdict |
| --- | --- |
| (A) AV in the builder (fix inside the owner) | **Chosen**: one predicate at `Set`, deletes `Reached` and 6 guards |
| (B) A5-side "assumed contributions" (A5 unions the operands of non-built nodes) | Rejected: the MIR keeps no record of the operands of an rvalue it did not build |
| (C) New rvalue kind `Assumed` carrying the operands (shared structure, O1 vocabulary) | Rejected: a second form of Aggregate/Use; AV builds the real rvalue |
| (D) Only fix `Reached` (pass a continued call's result) | Rejected: H2 and H3 stay; one more case of the open family |
| (E) Keep `Reached` for Types that disagree | Rejected: a fallback that silently drops the tuple's Loans |

**Validator.** One rule replaces the call-argument special case (`MirValidator.Types.cs:213`, −4): `Fits(expected, actual)` =
`actual is Never || Agrees(expected, actual)` at operand positions (Use, aggregate components, call arguments, operator operands; an
operator with a Never operand is admitted). Places keep `Agrees` (a Ref of a Never Place stays a finding). Net ≈ +3.

**Deletions.** `Reached` (−6 with callers), 6 guards (≈ −18), the validator's Never `continue`. Net builder ≈ −20 lines.

**MIR dump/goldens.** Check-only blocks gain the operations AV builds: `tests/mir/continuations.mir` (`divergingArgument`,
`abandoned`), `interpolations.mir` (`do => return` inside an interpolation), any golden with a diverging operand inside a
construction, update, range selection or `$tryWrite` (H: a handful). Runtime blocks do not change.

**Reproducers and MIR-layer tests** (fail before, pass after):

1. `MirBuildTest.ADivergingArgumentContinuesItsCallForTheChecks` (extend): `func h(a: uniq/i32, b: i32) -> uniq/i32 during a => a`,
   `func both(x: uniq/i32, y: uniq/i32) => ()`, body `var p: i32 = 1`, `var q: i32 = 2`, `both(h(p@uniq, (return 1)), q@uniq)`:
   `both`'s first operand is the destination of the `h` Call (before: a fresh Never temp). `pair((1, (return 1)))`: the operand is an
   Aggregate temp of Type `(i32, i32)` (before: the assertion `Same(Never, …)`, which this test inverts with its reason).
2. `MirBuildTest` (new fact in the same class): `var x: i32 = 1`, `x += (return 1)`: a check-only statement writes `(*$w)`.
3. `MirInitTest.CheckOnlyCodeKeepsItsOwnState` (extend): `let t = (box@move, (return 1))`, then `consume(box@move)`:
   `MovedPlace_Kd box` (before: none). C3 rows unchanged.
4. `MirValidatorTest`: an Aggregate and a BinaryOp with a Never operand give no finding; a `ref/Never` operand for `ref/i32` does.
5. A5 rule tests (unit 1b, prototype cases): `both(h(p@uniq, (return 1)), p@uniq)` error (activation against `h`'s result);
   `let r = x@ref; x += (return 1); inspect(r)` error; `f((p@ref, (return 1)), p@uniq)` with `f(t: (ref/i32, i32), u: uniq/i32)` error.

**Cost.** Only check-only code grows (its nodes' operations); runtime paths unchanged; no new allocation.

## 2. G92 (b): a child reservation and its parent's authority

**Required meaning.** SPEC 15.6.7: the path from the root through standard Field and element projections up to a receiver or
argument "is one preparation …, even when it contains written exclusive borrows; no exclusive Loan on that path is live before the
invocation. So `holder@uniq.items.append(x)`, `holder.items@uniq.append(x)` and `holder.items.append(x)` are the same call."
During the reservation shared reads coexist; writes, Moves and destructions of the target, its owner and its route are errors.
"A child reservation keeps its parent's authority."

**What the representation wrongly allows.** R4-M37's G5 builds each written borrow on the path as its own `TwoPhaseUniq`:
`$1 = TwoPhaseUniq(holder)`, `$2 = TwoPhaseUniq((*$1).items)` (`MB.Places:164` → `Value` → `Into(Borrow)`). Under A3's `mode`,
`L1` is `ref` only in `$1`; `$2` carries it (R2) as `uniq`, so `holder@uniq.items.append(holder.count)` is rejected before the call
(too strict). At the call only `L2` (Place `(*$1).items`, root `$1`) is activated; distinct roots are disjoint, and nothing ever
activates `L1`, so `let v = holder.items@ref; holder@uniq.items.append(1); inspect(v)` is accepted (unsound, H: by A3 §5's rows).

**Options.**

| Option | Content | Verdict |
| --- | --- | --- |
| (b2) Fold (shared structure: the builder's path representation) | On a preparation path (`input`), a written exclusive borrow whose value the path follows (`HirPlaceKind.Value`, `Layers > 0`, parentheses transparent) is not formed: `Place` returns the Place the borrow would lend (`Lent`, factored out of `Ref`, base views and payloads included) and follows `Layers − 1`. The path then has one reservation, at its lending point, exactly as `holder.items.append(x)`. A3's `mode` rule needs no change: no reservation is formed through a reservation holder on a G5 path. | **Recommended** |
| (b1) Parent chain in A5 (fix inside A5) | A5's pre-pass derives `parent(R) = L` when R's Place starts with a Deref of L's reservation holder; `mode(L, H) = ref` for H in L's holder or a descendant's; `err(activate)` also activates every ancestor of R at R's Call or writeback write. No MIR change; ≈ +25 lines in `MirBorrows`. | Alternative |
| (b3) A parent link on the Ref (MIR field or `MirRefKind` member) | Explicit, but an O1 vocabulary entry for a fact the Place already shows | Rejected |

**Where (b1) and (b2) differ** (the owner's SPEC reading): `let r = holder.count@ref; holder@uniq.items.append(1); inspect(r)`.
(b1) activates `L1` on all of `holder` at the call: error (Rust E0502 for an explicit `&mut holder`). (b2) reserves only
`holder.items`: valid, as the "same call" `holder.items.append(1)` is. (b1) contradicts SPEC's equivalence sentence; (b2) reads
"keeps its parent's authority" as: the child's reservation carries the exclusive capability (and, through R2, the Loans) the written
borrow's operand grants. Recommendation (b2): it follows the SPEC sentence literally, gives the three spellings one MIR (Principle 1),
is smaller, and keeps A3's mode rule. If the owner reads the written borrow as acquiring its whole operand, choose (b1) and revise
the equivalence sentence (SPEC gap either way, reported).

**Forms that stay errors** (both options): a write, Move or destruction of the target or its owner during the reservation
(`holder@uniq.items.append(do-block writing holder)`, `…append(holder@move)`); a second exclusive input on an overlapping Place
(`holder@uniq.items.append(holder.items@uniq)`, activation); a live shared Loan on the target across the call
(`let v = holder.items@ref; holder@uniq.items.append(1); inspect(v)`, activation); a getter on the path (activates at its own call,
its result live and exclusive). Not covered here: a write to a reference the reservation was reached through
(`h@follow@uniq.c.bump(do-block writing h)`), which is G119's route rule.

**Files (b2).** `MB.Places.Place` (+4: the fold row), `MB.Values.Ref` → `Lent(place, reference)` + `Ref` (±0). Deletions: the
intermediate reservation temps on such paths. R4-M37's G5 sentence ("a written `@uniq` … on the Place path of a receiver") gets a
superseding DECISIONS entry. A4 keys unchanged (the folded reservation is still rooted at the static). (H) A5's Origin stratum (F4)
must not expect a Ref at the folded borrow's site; its Origin is the child's.

**Dump/goldens.** No `tests/mir/*.kimi` has a written `@uniq.` path (grep). **Validator:** none.
**Tests.** `MirBuildTest.ReservedCallInputsAreTwoPhase`: `g` has 2 `TwoPhaseUniq` (was 4), each on `(*h).c`; add
`holder@uniq.items.append(holder.count)` with exactly one `TwoPhaseUniq`, Place `holder.items` (before: 2, the first on `holder`).
A5 (1b): `holder@uniq.items.append(holder.count)` and `h@follow@uniq.c.bump(h.c.count)` valid; the error forms above.

**Cost.** Fewer temps and statements on written-borrow paths; A5 sees one Loan instead of a chain.
## 3. G94: the receiver's Loan where it is located

**Required meaning.** SPEC 4.7.4: the receiver is located once before the index (a range's boundaries); while the index is
evaluated its storage may be read but not modified, moved, destroyed or reallocated; a Slice receiver's handle is copied first and a
UniqSlice handle is borrowed. SPEC 4.8.1: an array-derived view Loans the whole array. SPEC 5.3: `p[n]` is `*(p + n)`.

**What the representation lacks.** Built-in selections (`HirPlaceKind.Index`: fixed arrays, Array, Slice, UniqSlice, raw pointers;
`Binding.Hir.cs:358`) form nothing at locate:

- `Element` (`MB.Places:211`) projects `sequence[_i]` after the index; no Loan covers the index's evaluation, so
  `values[reset(values@uniq)]` meets nothing. Updates and assignments through an index (`MB.Values.Replace`, `MB.Expansions.Update`
  → `Writeback`) share it.
- `Slice` (`MB.Expansions:263`) forms the array backing (`Borrow`, R4-M40) after the boundaries.
- A Slice receiver of an element selection is read at the access, after the index (range selections copy it first); a raw pointer
  receiver is read by the `Add` after the index (wrong code, no Loan).
- User `Indexable` selections (`CallResult` → `Referent`) already form their receiver first (call receiver before arguments).

**Owner.** The builder's selection lowering (`Places`), one helper shared by element and range selections.

**Design: `Locate(sequence, node)`.** A built-in selection fixes its receiver where it is located: a receiver whose value the
selection reads (a Slice handle, a raw pointer) is copied into a temp there and the selection reads the copy; any other receiver's
storage is lent there by a shared borrow (`Borrow(Ref, …)`). A range selection passes that borrow (or the copy) as `RawSlice`'s
backing, as R4-M40 has it. An element selection ends it with `EndBorrow` once its index (or key resolution, or `^x`) is evaluated,
before the element's own access (read, write, borrow, reservation or writeback) is formed. `EndBorrow` keeps its R4-M37 meaning ("a
use of the local after which every Loan issued into it must be dead; never an activation"): the guard is live exactly through the
index, so a write, Move, destruction or exclusive borrow of the receiver there conflicts with a shared Loan (A3 `conflicts`), and
reads coexist. No vocabulary entry; `ConstIndex` evaluates nothing and forms none.

**G5 interaction.** An index receiver of an exclusive access is not a reservation: the preparation path's reservation is formed
on `a[_i]` once the target is located (after the index), and SPEC 15.6.7 lets no reservation pass through a user index call (that
call activates at its own call). The shared guard is exactly the reservation's protection for the earlier interval and ends before
the reservation forms, so "no exclusive Loan on that path is live before the invocation" holds.

**A1 interaction.** The guard reads the receiver, which every element access already requires whole (an Index ends the Move path,
R4-M21). A Move of the receiver inside the index now conflicts with the guard (A5) and the access stays `MovedPlace` (A1). (H) A write
through an index into an uninitialized or partly moved array is reported twice by A1 (at `a[i]`, the guard's subject, and at
`a[i] = v`), one more problem for an invalid program; reads share the subject and merge (`MirReport` keys by code and subject).

**G92 interaction.** The `EndBorrow` and the range's tail are built in check-only code under AV; a transfer inside the index leaves
the guard dead on its runtime edge (no use there), so no runtime `EndBorrow` is needed.

**Options.**

| Option | Verdict |
| --- | --- |
| (a) `Locate` + guard/backing (inside the owner) | **Chosen** |
| (b) Route every selection through a reference formed at locate, `(*$r)[_i]`, its mode by the access (shared, or `TwoPhaseUniq` activated by the write) — the writeback pattern generalized (shared structure) | Rejected: needs the consumer's capability inside `Element`, rebases every element Loan on a temp (S1 base/kill changes), more statements |
| (c) A5 tracks "located" intervals | Rejected: the MIR has no locate point |
| (d) A `Locate` statement kind | Rejected: O1 entry for what a Ref and `EndBorrow` already express |

**Files.** `MB.Places`: `Locate` (+10) used by `Element` (+4: copy or guard, `EndBorrow`) and by the raw branch (the pointer copy);
`MB.Expansions.Slice`: the handle copy and the end-of-tail `Borrow` replaced by `Locate` (−4). Updates and assignments need no change
(they locate through `Element`). R4-M40's "a shared borrow made with the call" gets a superseding sentence (made where located).

**Dump/goldens.** Every runtime-index selection on storage gains one temp, `_g = &a` and `EndBorrow(_g)`; Slice receivers gain a copy;
range backings move before the boundaries: `places.mir`, `expansions.mir`, `collections.mir`, `iterations.mir`, `every-form.mir`
(H: the exact list from a golden run). **Validator:** none.

**Tests.** `MirBuildTest` (new fact beside `RangeSelectionsPassTheirBacking`): `func reset(v: uniq/[3 of i32]) -> isize => 0`,
`var values: [3 of i32] = [1, 2, 3]`: (1) `let x = values[reset(values@uniq)]`: a shared Ref of `values` precedes `reset`'s Call
and its `EndBorrow` follows it, before the element read; (2) `let s = values[reset(values@uniq)..]`: the backing Ref precedes the
Call; (3) `func pick(view: Slice<i32>, i: isize) -> i32 => view[i]`: the element is read from a copy made before `i`; (4) a raw
`p[n]` adds to a copy of `p` made before `n`. Each fails before. A5 (1b): `values[reset(values@uniq)]`,
`values[reset(values@uniq)] = 9`, `values[reset(values@uniq)..]` errors; `values[peek(values@ref)]`, `values[i] += values[j]` valid;
a Slice receiver reassigned inside its index valid (copy first).

**Cost.** One temp and two statements per runtime-index storage selection; measure `MirBuildTest.BodiesScaleLinearly` and the
Benchmark `BuildMir` stage (expected < 1%, H).

**Related findings (H, not designed here, for open issues):** (i) a call's `Copy` operand is read at its Call terminator, after
later arguments (`MB.Calls:91-114`; pending slots secure it, plain calls do not), so `f(x, do-block writing x)` passes the new `x`
(SPEC 12.2; Rust's MIR copies to a temp). (ii) A3 classifies Index as an inline step, but a Slice or UniqSlice element is the
referent of the handle (SPEC 4.8.1, `ref/E during s.source`); an element Loan on `s[_i]` carries no parents (R2 counts borrow
Derefs), so reassigning or ending `s` (or the `Locate` copy) ends it and the element reference loses the backing Loans. The Index of
a Slice or UniqSlice receiver should count as a shared or exclusive borrow Deref in A3's step classes.

## 4. G91: the cycle cut in the Destruction walk

**Required meaning.** SPEC 16.3: destroying an Array or Dictionary destroys its elements; SPEC 15.6.6 asks whether destruction may
observe. SPEC 15.3.5: recursive Types, also non-regular growing ones, are valid; analyses compute per declaration and terminate.
R4-M36: a repeating recursive Type ends exactly; a growing one at the bound of 32 (Observes deep, A4 Limit).

**What the walk lacks.** `MirTypes.Destruction` (`M/MirTypes.Destruction.cs:68`) goes into a sequence's components with no memory,
so `struct Tree {a}` with `let r: ref/i32 during a` and `var children: Array<Tree{a}>` recurses to depth 33: `Observes` reports deep
(A5's located `Unsupported_Kd` at its Drops) and A4's `Glue` a Limit. Handles are cut, but twice, per visitor: `Observer` by
`seen.Add(payload)` (`:151`), A4 by `glued.Add` in `Destroy` (`MirEffects.Solve.cs:407`), which dedups the report but not the walk.

**Owner.** `MirTypes.Destruction`, the one walk (R4-M22, R4-M36); visitors only supply a query's memory.

**Design.** A recursive Type repeats only through a part the walk reports and goes into: a sequence's elements or a Sealed handle's
payload (inline storage cannot contain itself). So the walk enters each reported part once per query:

- `IMirDestruction` gains `bool Enter(BoundType type)`: "whether this query meets the reported part for the first time".
- In `Destruction`, each reported part (abstract, Function, owning handle, Array, Dictionary, string, struct with a drop) calls
  `if (!visitor.Enter(type)) return false;` before `Visit`; a part met again was reported and walked already.
- `AnyPart.Enter => true` (it stops at its first reported part, so it never repeats); `Observer.Enter => seen.Add(type)` and its
  payload walk drops `seen.Add(payload) &&`; `Glue.Enter => owner.glued.Add(type)` and `Destroy` drops its `glued.Add` check.

A growing Type makes a new Type at each level, so it still meets the bound exactly (Nest stays deep). The cut is by identity of
interned Types (`ReferenceEqualityComparer`, as today).

**Options.** (a) `Enter` on reported parts (**chosen**: one cut for sequences and handles, same set contents as today, no new
allocation); (b) a `HashSet` parameter on `Destruction` (rejected: a nullable mode parameter, since `MayNeedDrop` has no scratch);
(c) a seen set over every Type, as `Holds` has (rejected: larger sets in A4's per-evaluation `glued`, for DAG blow-up that is
pre-existing and not G91); (d) an ancestor stack (rejected: a second cut beside the visitors'). A depth bound alone is the status quo.

**Site sharing the cause.** `MirTypes.HoldsRaw` (`MirTypes.Holding.cs:175`) walks with a depth bound only, so `Tree` reads as
raw-holding at depth 33 and its immutable statics as environment authority (SPEC 8.11.2; H: an A4 false Environment). Fix in the same
unit: a caller's `seen` like `HoldsLoans` (+4; its one caller `MB.Places.StaticRoot` passes a builder scratch: **MirBuilder** +1 field).
`HoldsLoans` already cuts by `seen` and is unchanged; after this unit both Type walks state one rule: each Type (part) once per query.

**Growth through a composed body's atoms (PLAN's question): not covered.** A4's `Process` restarts the walk at depth 0 for each
substituted atom of a composed summary (`MirEffects.Solve.cs:280, 300`), and a growing chain makes new Types, so neither the identity
cut nor the bound applies across restarts. A substituted parameter atom is a subterm of the destroyed Type, but walking it instantiates
Field Types, which may grow. Predicted hang (H, untried; the probes of 89d19fa4 had no rc payload with a destructor):
`struct D<T>` with `var value: T` and `drop => ()`; `struct G<U>` with `var next: Option<rc/D<G<(U, U)>>>`;
`func release(g: G<i32>) => ()`. Fix if it hangs (A4-owned, ≈ +5, separate unit): `Destroy` composes a destructor at the walk's
depth (`Enter` → `Compose` → `Process` start at the destroyed part's depth + 1, a saved field restored after), so the one bound covers
the chain (Limit), as 89d19fa4 did for payloads.

**Files.** `M/MirTypes.Destruction.cs` (+6 −4), `M/Analysis/MirEffects.Solve.cs` (+1 −4), `M/MirTypes.Holding.cs` (+3),
`MB.Places` (+1). DECISIONS R4-M36's "cuts no cycle through a sequence (PLAN G91)" gets a superseding sentence.
**Dump/goldens:** none (`MayNeedDrop` unchanged, A2 verdicts unchanged). **Validator:** none.

**Tests.** `MirTypesTest.DestructionObservesUserDropsAndUnknownParts`: `tree` becomes `(false, false)` (it is `(true, true)` today,
the G91 limit recorded); add `Forest {a}` (`ref` Field, `Array<Forest{a}>`): not observing, not deep, `HoldsLoans` true; a
`Dictionary<i32, Table>` recursion; an `Array<Logged>` with a drop: observing, not deep; `nest` stays `(true, true)` (bound exact).
`MirEffectsTest` (beside `AGrowingRecursiveDestructionEndsAtTheBound`): a confined witness body that drops a `Tree`: no problem
(before: `Unsupported_Kd Self is Sink 1`). `MirTypesTest`: `HoldsRaw(Tree)` false (new assertion).

**Cost.** Shorter walks (a `Tree` ends at its first repeat instead of 32 levels); the same hash inserts as today. A4's
`SummariesScaleLinearly` allocation is unchanged (glued holds the same reported parts).

## 5. Order, shared files and decisions

- Units, each its own commit: G91 (no serialized file but `MirBuilder` for `HoldsRaw`), G92b, G92a (restructuring `Set`/`Reached`
  first, behavior unchanged where possible), G94 (after G92a: `Slice` and `Element` change in both). G92a and G94 regenerate goldens.
- Serialized files: **MirBuilder** (G92a, G92b, G94, G91's one field), **MirValidator** (G92a). None touches `MirProgram`,
  `MirVocabulary`, `MirDescriptors` or `MirDump`.
- Decisions: G92b's option (owner: (b2) recommended); G92a's AV superseding R4-M37's `Set` sentence and its Binding line (planning
  session); G94's `Locate` amending R4-M40 (planning session); two SPEC gaps to report (14.10.3 placement of a Never form; 15.6.7
  "same call" vs a parent's authority); two findings for open issues (§3 related findings).

Unverified (H): A3's miss through G5 chains (by its rows); Binding's tuple typing as the only Never-part Type at a typed consumer;
golden lists; G94's double A1 report; the restart hang; `HoldsRaw`'s effect on A4; compile-time figures; §3's related findings.
