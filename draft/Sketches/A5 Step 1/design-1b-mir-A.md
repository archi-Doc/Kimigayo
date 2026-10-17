# A5 step 1, the 1b design diff: MIR facts (designer A)

Base: `bin/worktrees/a5-step1` at `5c42b577` (origin/dev). Read-only; nothing was built or run. `B/` = `src/Kimi/Compiler/Mir/Build/`,
`M/` = `src/Kimi/Compiler/Mir/`. (H) marks an unverified claim. Kimi spellings follow `tests/mir/*.kimi` and `MirBuildTest`.

## 0. Summary

| Item | Chosen design | Shared MIR files touched | Size (code) | Decision needed |
| --- | --- | --- | --- | --- |
| G92a | **V**: on an assumed path every node completes into its destination with the value it forms; only a Never form has none (SPEC 14.10.3, 3.8). Delete the builder's non-completion guards; the validator reads a Never value's Type as unknown. Prerequisite: Binding fits a literal's Never element (α). | MirBuilder (Values, Calls, Control, Expansions, Formatting), MirValidator (Types), MirDescriptors (MirUses, one line) | ≈ −30 MIR, +5 Binding (H) | Supersedes R4-M37 G6's `Set` clause (owner-approved contract): planning session, owner if it treats G6 as fixed |
| G92b | **B2**: a written exclusive borrow that an input's Place path continues through is transparent: the path's Place is the Place it lends (SPEC 15.6.7: "the same call"). One reservation per input; A3's mode rule stays. | MirBuilder (Places, Values) | ≈ +7 | Supersedes R4-M37 G5's path clause: owner (A3 §9 item 8). Alternative B1 = A5 reservation chains |
| G94 | **L**: a selection takes what it reads from its receiver where it locates it: an array is lent shared there (a range's backing, or an element's guard ended by `EndBorrow` once the index is evaluated); a Slice handle or a raw pointer is copied there. | MirBuilder (Places, Expansions), MirVocabulary (EndBorrow doc) | ≈ +15 | None beyond a DECISIONS entry (closes R4-M40's Open) |
| G91 | **C**: the Destruction walk enters each Type once per query (`IMirDestruction.First`), so a repeat ends where it repeats; growth still meets the bound of 32. | none (MirTypes.Destruction.cs, Analysis/MirEffects.Solve.cs) | ≈ +2 | None |

Unit order (separate commits): G91; G94; B2; then α + V (V needs α). Each passes `MirBuildTest`, `MirValidatorTest`, the goldens
`tests/mir/*.mir`, `MirInitTest`/`MirDropTest`/`MirEffectsTest`, `BodiesScaleLinearly`, and the difference ledger (A1 changes, below).

## 1. G92a: which values exist on assumed paths

**Required meaning.** SPEC 14.10.3: on an assumed path a transfer, `$abort` and a call of Type Never "continue after their operands are
evaluated"; every check (Moves, initialization, Loan conflicts) applies there. So every node around a Never form completes too, and
forms the value it forms on a real path from its operands; the Never form itself has no value (SPEC 3.1.3), and SPEC 3.8 lets a Never
expression fit any expected Type without a conversion.

**What the representation lacks or wrongly allows.** The builder decides "has a value" by runtime completion (`HirCompletion`), not
by the assumed path:
- `MirBuilder.Set` (B/MirBuilder.Values.cs:398) assigns nothing where its node does not complete. An update whose target or right side
  diverges reserves `$w` (Writeback, B/MirBuilder.Expansions.cs:250) but the write through `(*$w)` (Update's `Set`/`Overwrite`) is never
  built, so the activation SPEC 13.7.2 requires is never checked; an aggregate or operator around a Never form leaves its destination
  unwritten.
- `Reached` (B/MirBuilder.Calls.cs:158) replaces every non-completing operand by an unwritten Never temp. For a compound operand the
  inner call was built and wrote its destination, so `f(h(p@uniq, (return 1)), q@uniq)` passes Never to `f` and drops `h`'s result
  and the Loans it retains: A5 sees no holder live into `f`'s Call.
- `Overwrite` (Values.cs:354) drops the old value only where the update completes.
- Part gates with the same cause skip a node's later parts: `Slice` (Expansions.cs:290, no `RawSlice`, so no backing Loan),
  `Boundary` (357), `Collection` (416, no appends, so the elements' Loans never reach the Array), `Observe` (B/MirBuilder.Control.cs:244),
  Formatting's per-write discriminant (B/MirBuilder.Formatting.cs:88) and `FormatFinish` (104), `TypeTest` (Calls.cs:212).
- A diverging Call writes no destination even on its check edge (`MirUses.Walk`, M/MirDescriptors.cs:270), so a recursive default
  template whose expression diverges (`Defaults`, `Invoke(..., !completes)`) leaves its pending slot without the retention P9 gives
  it, and `let y = fail()` leaves `y` uninitialized in check-only code (H: A1 then reports `use(y)`).

**Owning component.** MIR construction (the builder decides values; R4-M37 G6) and its validator (Type agreement of values).

**Rule V (assumed values).** On the check-only continuation every node is built as on a real path and completes into its
destination: a call writes its destination (a diverging one too, on its check edge), an aggregate, operator, conversion or `Use`
assigns its value, an assignment drops the old value and places the new, an update writes through its reservation. A Never form
(transfer, `$abort`, a Never call, a diverging intrinsic) has no value: where it is an operand its consumer takes its unwritten Never
temp, which carries no Loan and is no holder (`HoldsLoans(Never)` is false). A part that needs a Never form's value as a Place (a
borrow of the writer of `$tryWrite`, of a `$expect` message) is not built: there is nothing to borrow. Control-flow structure (which
edge is a runtime edge, which a CheckTarget, `Transfer`'s early return, `Branch`, `Join`, a body's end) stays the HIR Completion's.

**A5's check sets, decided together.** A5 needs no assumed-path rule of its own: (1) every Loan SPEC 14.10.3 needs on an assumed path
is issued by a statement of the check-only continuation and carried by a holder that a later check-only use keeps live; (2) A3's
carrying and liveness already run on P1+P2 chains (F10), so `CS`/`CS_call` at a check-only Call see `h`'s result; (3) a check-only
Call and writeback write activate as runtime ones do; (4) a Never operand contributes `val = ∅`; (5) R4-M37's `EndBorrow` on a
transfer's runtime edge ends abandoned reservations only there, so the continued call activates them. The diverging Call's check edge
applies A3's call rule (`out` into D), which gives a template slot its P9 retention.

**Never-part Types (prerequisite α).** Binding builds a tuple literal's Type from its elements' own Types (`BindTuple`,
`B/../Binding/Binding.Expressions.cs:1406`), so `pair((1, (return 1)))` has `(i32, Never)` against the parameter `(i32, i32)`; R4-M37
routed it through `Reached`. Under V the aggregate is formed, and the validator rejects `(i32, Never)` into `(i32, i32)` (rightly: SPEC
3.8 says Never fitting is not subtyping). α: Binding gives an element that is a Never expression under an expected element Type the
fitted Type (SPEC 3.8), for tuple, fixed-array and enum-payload literals (H: the three sites; ≈ +5 lines). The owning component of the
Type is Binding; MIR then never sees a Never part where its consumer expects another Type.

**Options.** (1) *Builder rule V + α (chosen)*: one rule at every site, deletions, A5 unchanged. (2) *An A5 rule* taking a Never
operand as the Loans of the call nested in it: rejected, A5 would re-derive values from syntax order, and updates would still have no
write. (3) *Keep `Reached` for Never-part operands, with a located `Unsupported_Kd` where `HoldsLoans`*: a gate for one form; only a
temporary if α is refused (removal condition α). (4) *The validator accepts Types with a Never part*: rejected again (R4-M37; SPEC 3.8).

**Changes.**
- B/MirBuilder.Values.cs: `Set` loses its `Completes` guard, so `Make` is the same function and goes (9 callers renamed);
  `Overwrite`'s drop loses `Completes(node) &&`. B/MirBuilder.Calls.cs: `Reached` and its three uses (75, 96, 111-114) go;
  `TypeTest`'s gate goes (H: Binding rejects a Never handle; else it is restated on Type Never).
- Gates deleted: `Observe` (Control.cs:244), `Slice`'s `if (!Completes(index)) return`, `Boundary`'s `Completes(node) &&`,
  `Collection`'s `&& Completes(array)`, Formatting 88 and 104. Restated on "has Type Never" (no value to borrow): `Verification`'s
  message (Control.cs:209), `$tryWrite`'s writer (Formatting.cs:44).
- M/MirDescriptors.cs `MirUses.Walk`: a Call writes its destination (the `Target >= 0` condition goes; a diverging Call's write
  reaches only its check edge); the `Call`/`Unwritten` docs follow.
- M/MirValidator.Types.cs: `TypeOf(in MirOperand)` returns null (unknown, agrees with any) for a Never value; the Call-only
  `continue` (213-216) goes; `Signature` checks a diverging Call's destination too (a Never result is unknown by the same rule).
- **Deletions**: `Reached`, `Make`, the validator's argument exception, eight gates. DECISIONS: an entry superseding R4-M37 G6's
  "By the builder's rule that a node which does not complete assigns no value (`Set`) ..." and "The validator lets a `Never` argument
  agree with any parameter".

**Dump/golden impact.** Check-only blocks gain assignments and drops: `updates.mir` (`noncompletingWrite`: `drop _3`, `_3 = move _5`
in bb10), `control.mir` (`short`: the `and` result), `values.mir` (`[1: flag, 2: (return)]`: the entry placement calls), `matches.mir`
(guard `or (return 2)`), `calls.mir`/`continuations.mir` where a diverging call's destination is a Var (H: none today). Runtime blocks
are unchanged, so R5's code is unchanged (H: R5 emits no check-only block).

**Validator impact.** No new rule; one generalized (a Never value's Type is unknown), one exception deleted. **A1 ledger effect (H).** Values now written in check-only code remove false `UninitializedPlace_Kd` there (`let t = (1, (return 2))`
then `use(t)`; `let y = fail()` then `use(y)`); the old analysis's verdicts decide whether the ledger moves.

**Reproducers** (SPEC 14.10.3; errors are A5's, step 1b):
```kimi
func keep(a: uniq/i32, b: i32) -> uniq/i32 during a => a
func both(x: uniq/i32, y: uniq/i32) => ()
func inspect(v: ref/i32) => ()
func f() -> i32
    var p: i32 = 1
    both(keep(p@uniq, (return 1)), p@uniq)   // Error on the assumed path: p's Loan in keep's result meets p@uniq
    return 0
func g() -> i32
    var x: i32 = 1
    let r = x@ref
    x += (return 1)                          // Error on the assumed path: the write activates x's reservation while r lives
    inspect(r)
    return 0
```
**Tests (MIR layer, fail before, pass after).** `MirBuildTest.ADivergingArgumentContinuesItsCallForTheChecks` rewritten: in `f` above,
`both`'s first operand is `move` of `keep`'s Call destination (before: a Never temp); `pair((1, (return 1)))` passes the formed
tuple (needs α). New `MirBuildTest.ADivergingOperandStillWritesItsUpdate`: in `g`, a check-only block holds `(*$w) = Add(...)` through
the TwoPhaseUniq temp (before: none). `MirValidatorTest`: a Never operand of a `Use`/`BinaryOp` validates. A5 rule rows (1b): `f`, `g`
rejected; `let t = (1, (return 2))` accepted.

**Size and speed.** ≈ −30 lines in Mir, +5 in Binding (H); tests ≈ +40. Only check-only code grows; build and analysis cost is
unchanged on runtime code.

## 2. G92b: a child reservation keeps its parent's authority

**Required meaning.** SPEC 15.6.7: the path from the root through standard Field and element projections to a receiver or argument
"is one preparation ... even when it contains written exclusive borrows; no exclusive Loan on that path is live before the invocation.
So `holder@uniq.items.append(x)`, `holder.items@uniq.append(x)` and `holder.items.append(x)` are the same call." Shared reads coexist
with the reservation; writes, Moves, destructions and exclusive operations on it are errors.

**What the representation lacks or wrongly allows.** G5 builds a written `@uniq` on an input path as its own reservation:
`_1 = &uniq two-phase (*h); _2 = &uniq two-phase (*_1).c; bump(move _2, 1)`. Under A3's mode rule `L1` is `ref` only in `_1`, which is
dead after `_2` forms; in `_2` (R2 carries `L1` as a parent) it is `uniq`, so `h@follow@uniq.c.bump(h.c.count)` and
`holder@uniq.items.append(holder.count)` are rejected. `L1` has no Call of its own, so it is never activated; on a path that ends in a
value read (`f(holder@uniq.items[0])`) nothing activates it at all. The MIR has two reservations where SPEC has one call.

**Owning component.** MIR construction (G5: which borrows on an input path are reservations, R4-M37). A3 decides modes from them.

**Options.**
- **B2, inside the owner (chosen).** `MirBuilder.Place(node, input)`: a written exclusive borrow (`HirOp.Borrow`, exclusive Type) on an
  input path whose PlaceShape follows its value (`Layers > 0`) is the Place it lends, followed by `Layers - 1`; `Ref` is split into
  `Lent(place, reference)` (the follow and base-layer selection it already does) and the rvalue. The input then has one reservation,
  `&uniq two-phase (*h).c`, exactly `h.c.bump(1)`'s. A written borrow that is the input itself (`take(p@uniq)`, `c@uniq` as a receiver)
  stays its reservation; off input paths (a `let`, an operator operand) a written `@uniq` stays a live `Uniq` borrow.
- **B1, shared structure (rejected).** An A5 pre-pass relation `resv(H)`: the TwoPhaseUniq Loans a Temp continues (its own issue
  plus `resv(T)` of each reservation holder its one definition reads); `mode(L, H) = ref` iff `L ∈ resv(H)`; a Call activates `resv(t)`
  of each operand root; a validator rule makes TwoPhaseUniq destinations single-definition Temps. It keeps the parent's whole Place, so
  it rejects `holder@uniq.items.append(take(holder.other@uniq))`, which SPEC calls the same call as the valid unwritten form, and it
  costs a relation, a validator rule and an A3 amendment. Refining A3's mode rule alone needs the same relation.

**Reserved interval, mode, activation under B2.** One reservation per input: formed where the input is located (after any index on
its path, which G94's guard protects while evaluated), `ref` in its reservation holder, activated at its Call. Its R2 parents are the
Loans of the references the path went through (`σ(h)`), as for the unwritten form. The route protection (a write to `h` during the
reservation) is G119's, unchanged by B2.

**Forms that stay errors** (A5 1b rule rows):
1. `holder@uniq.items.append(holder.items.remove(0))` (SPEC's `tasks.append(tasks.remove(0))`): `remove`'s activation (uborrow) meets
   the reservation (`ref`).
2. `holder@uniq.items.append(reset(holder@uniq))`, `func reset(h: uniq/Holder) -> i32`: the nested activation of `holder` overlaps.
3. `holder@uniq.items.append(consume(holder@move))`: a Move of the owner during the reservation.
4. `let r = holder.items@ref` live across `holder@uniq.items.append(1)`: activation meets a live Loan.
5. `h@follow@uniq.c.merge(h.c@ref)`: shared and exclusive inputs of one call overlap.
6. A retaining result: `let n = holder@uniq.store.newestItem()`, then `holder.store.newest = ...` while `n` lives.
7. A write to `h` during `h@follow@uniq.c.bump(...)`: G119 (open; B2 neither fixes nor worsens it).

**Becoming valid** (as their unwritten forms): `h@follow@uniq.c.bump(h.c.count)`, `holder@uniq.items.append(holder.count)`,
`holder@uniq.items.append(take(holder.other@uniq))`, `f(holder@uniq.items[0], holder@ref)`. **Files.** B/MirBuilder.Places.cs (`Place`: one arm), B/MirBuilder.Values.cs (`Ref` → `Lent` + rvalue). No validator, vocabulary or dump
code change. DECISIONS: an entry superseding R4-M37 G5's "a written `@uniq` ... on the Place path of a receiver or an argument down to
a value (`h@follow@uniq.c.bump(1)`)" and closing its Open sentence on the mode rule; A3 §5 unchanged.

**Golden/test impact.** No golden uses the form (grep: 0 in `tests/mir`, library and milestones). `MirBuildTest.ReservedCallInputsAreTwoPhase`:
`g`'s expectation becomes two TwoPhaseUniq Refs rooted at `h` (before: four, two rooted at temps); a new row
`holder@uniq.items.append(holder.count)` gives one TwoPhaseUniq of `holder.items` (fails before: two, the second through a temp).

**Size and speed.** ≈ +7 lines; one Ref and one temp fewer per written borrow on a path.

## 3. G94: the receiver's Loan where it is located

**Required meaning.** SPEC 4.7.4: the receiver is located once, before the index; "while the index is evaluated, the receiver's
storage may be read but not modified, moved, destroyed or reallocated"; "a Slice receiver's handle is copied first". SPEC 4.8.1: an
array-derived view Loans the whole array Place. SPEC 12.2: the receiver is evaluated before the index.

**What the representation lacks.** `Element` (B/MirBuilder.Places.cs:211) locates `sequence` with no Loan, evaluates the position
(or `Boundary`/`Resolve`), then projects; `Slice` (Expansions.cs:263) forms its backing `Borrow(Ref, sequence)` after the boundaries
and the bounds check (R4-M40's Open). A write inside the index meets nothing. Element reads of a Slice and of a raw pointer read the
handle variable after the index (`Element` copies `p` after `position`; a Slice element is `s[i]` on the variable), against SPEC 12.2
and 4.7.4 (H: a wrong value when the index writes the handle, MIR only).

**Owning component.** MIR construction, the one place selections are built (`Element`, `Slice`), shared by element and range
selections, reads, borrows, assignments and compound updates (all reach `Place` → `Element`).

**Rule L.** A built-in selection takes, where it locates its receiver, what it reads from it: an array (fixed or `Array`) is lent
shared there; a Slice handle or a raw pointer is copied there. A range selection's shared borrow is its backing (R4-M40), moved into
`RawSlice` and retained. An element selection's is a guard: `EndBorrow` once the index or key is evaluated, before the element's
Place is used; the element Place is projected from the receiver itself, so its consumer (read, borrow, write, writeback) is checked
as today. A constant index (`ConstElement`) evaluates nothing and needs no guard. User `index`/`indexUniq`, Dictionary and UniqSlice
selections are calls, whose receiver input is already borrowed or reserved where located (G5).

**Interactions.** G5: the receiver of a built-in selection is not a reservation; the element is the input, reserved after the index
(`xs[g()].bump(h())`: guard over `g()`, `EndBorrow`, `&uniq two-phase xs[i]`, then `h()`). With B2 no reservation temp lies on such a
path. A1: the guard is a use of the receiver (must be initialized), with the element access's subject, so `MirReport` merges a repeat
by (code, subject); a Move of the receiver inside the index is A5's conflict at the Move and A1's `MovedPlace_Kd` at the element read,
two independent problems (SPEC 23). (H) A write `xs[i] = v` into an uninitialized or partly moved array now needs `xs` initialized at
the guard; the ledger shows whether any program had it. G92a: a diverging index leaves the `EndBorrow` and the access on the
check-only continuation.

**Options.** (1) *Rule L in the builder (chosen)*: existing kinds (`Ref` Shared, `EndBorrow`), no vocabulary. (2) *Route the element
through the guard* (`(*g)[i]`, TwoPhaseUniq for exclusive consumers): rejected, the consumer's mode would decide the guard's kind and
writes would pass a reservation. (3) *An A5 interval rule* ("receiver located at ℓ0"): rejected, MIR keeps no location fact for it.
(4) *A guard only where the index can write*: rejected, a syntactic gate; revisit only if measurement asks.

**Changes.** B/MirBuilder.Places.cs: one helper `Receiver(IndexKoto, bool input)` used by `Element` and `Slice`: locates, then copies
a Slice or raw receiver into a temp, or makes the shared borrow of an array (`Borrow(Ref, sequence)`); `Element` pushes
`EndBorrow(guard)` after the position. B/MirBuilder.Expansions.cs: `Slice` takes its handle copy and its backing from the helper
(its own copy code goes; its `Borrow` at the end goes). M/MirVocabulary.cs: `EndBorrow`'s comment adds "a selection's receiver guard
once its index is evaluated". DECISIONS: an entry closing R4-M40's Open.

**Dump/golden impact.** `places.mir` (`a[i]`, `a[n]`: a Ref and an EndBorrow around the position; `s[i]`: a handle copy first),
`expansions.mir` (`values[key]`, `values[^1]`, `back`: guard; `middle`, `head`: the backing Ref moves before the boundaries),
`updates.mir` (`values[i] -= after`), `raw.mir` (`p[1]`: `p` copied before the position). **Validator**: none (EndBorrow of a
reference local is already its rule).

**Reproducers** (each body starts `var values: [3 of i32] = [1, 2, 3]`; errors are A5's, step 1b), with
`func reset(values: uniq/[3 of i32]) -> isize => 1`: `element`: `return values[reset(values@uniq)]` (Error: the receiver is modified
while the index is evaluated); `range`: `let view = values[0..reset(values@uniq)]` (Error); `update`: `values[reset(values@uniq)] += 1`
(Error); `reads`: `return values[values[0]]` (Valid: a read during the index).
**Tests.** New `MirBuildTest.SelectionsLendTheirReceiverWhereLocated`: in `element`, a Shared Ref of `values` precedes `reset`'s Call
and its `EndBorrow` lies between that Call and the element read (fails before: no Ref); in `range`, `RawSlice`'s backing Ref precedes
`reset`'s Call (before: after it). `RangeSelectionsPassTheirBacking` keeps its assertions. A5 rows (1b): the three errors and the
valid read.

**Size and speed.** ≈ +15 lines. Per runtime-index array selection +2 statements, +1 temp, +1 Loan for A5; the library has no
`x[...]` selection, the milestones many. Measure MIR build and A1 on the O2 corpus (expected < 1%, H). Native: the guard Ref is dead
code at O2 and `EndBorrow` emits nothing (H: R5).

## 4. G91: the cycle cut inside the one walk

**Required meaning.** SPEC 16.3: destroying a value destroys its parts once; a repeating recursive Type ends exactly; SPEC 15.3.5
allows growing (non-regular) Types, which the implementation bounds (R4-M36: depth 32, `Unsupported_Kd` where it matters).

**What the representation lacks.** `MirTypes.Destruction` (M/MirTypes.Destruction.cs:68) walks a sequence's elements with no
repeat check; only visitors cut repeats, each its own way (Observer `seen.Add(payload)` below a handle; A4's `Destroy` `glued.Add(type)`,
which dedups reports but cannot stop the walk). `struct Tree {a}` (`ref` Field, `children: Array<Tree{a}>`) walks Tree → Array → Tree
to depth 33: A4 adds Limit, `Observes` reports deep (MirTypesTest encodes `("tree", true, true)`). `HoldsLoans` has its own walk and
already cuts every repeat (`!seen.Add(type)`), so it is not a site; `MayNeedDrop`'s `AnyPart` stops at the first reported part, and
every cycle passes one (a sequence or a handle), so it is not a site either.

**Owning component.** The Destruction walk (`MirTypes`, R4-M36: "the one Destruction walk").

**Rule C.** The walk enters each Type once per query: after the null check and before the depth check it asks
`visitor.First(type)` ("whether the query meets this Type first"), and returns where not. A repeat ends where it repeats, through a
sequence or a handle alike; a growing Type never repeats and meets the bound as before (`Deep`), so the bound stays exact. Every
visitor reports a Type's parts once anyway, so the cut changes no verdict but the spurious deep.

**Options.** (1) *First at the walk's entry (chosen)*: one rule for sequences and payloads, as `HoldsLoans` has. (2) *A
sequence-only cut*: keeps two dedups (Observer's payload set, `glued` in `Destroy`). (3) *Visitors walk sequences as payloads*: three
copies of the element walk. Identity alone, without the bound, stays rejected (89d19fa4: growth never repeats).

**Changes.** M/MirTypes.Destruction.cs: `IMirDestruction.First(BoundType)` (+4 with doc); `Destruction`: one condition; `AnyPart.First
=> true`; `Observer.First => seen.Add(type)`, and its `Visit` drops `seen.Add(payload) &&`; doc of `Observes`/`Destruction` ("each
Type once per query"). M/Analysis/MirEffects.Solve.cs: `Glue.First => owner.glued.Add(type)`; `Destroy` drops its
`if (!this.glued.Add(type)) return;`. DECISIONS: R4-M36's G91 sentence superseded. Dump, golden, validator: none.

**Tests.** `MirTypesTest.DestructionObservesUserDropsAndUnknownParts`: `("tree", false, false)` (fails before: `(true, true)`); add
`struct Grow<T>` with `var next: Array<Grow<(T, T)>>` as `grow: Grow<i32>`, `(true, true)` before and after (growth still deep).
`MirEffectsTest`: a `confined` witness whose body destroys a `Tree` (the `Sink`/`Witness` helpers of
`AGrowingRecursiveDestructionEndsAtTheBound`) reports nothing (fails before: `Unsupported_Kd`, Limit).

**Growth through a composed body's atoms (PLAN's question).** Rule C does **not** cover it: growth never repeats, and `Process`
(M/Analysis/MirEffects.Solve.cs:288) restarts `Destruction` at depth 0 for each atom `Compose` substitutes, so a destructor's or a
witness's abstract atom re-enters the walk below the bound. (H) predicted mechanism: `release(nest: Nest<i32>)` with
`var next: Option<Box<Nest<(T, T)>>>` and a user `Box<T>` whose `drop` destroys its `T` → `Destroy(Box<…>)` → `Enter(drop)` →
`Compose` → atom `T` → `Nest<(i32, i32)>` → … each level new, so `glued` never repeats; once `drop`'s node has its atom and the reader
is re-queued, the evaluation recurses without end. 89d19fa4's two probes finished; why is unexplained. G91's unit adds that probe
(plain and under `confined`) as a `MirEffectsTest` case; if it hangs, `Destroy` passes its depth to `Enter`/`Compose`/`Process`
(and `Require` for a witness), which start the composed atoms' walks at `depth + 1`: the same bound (≈ +6 lines, A4 only), not a
second one. If it finishes, the unit records why.

**Size and speed.** ≈ +2 lines net. One set lookup per Type walked instead of per payload or reported part (Observer, Glue); sets are
reused (no warm allocation). Check `MirEffectsTest` V16 scaling and A4's allocation test (cold may rise by a set growth, H).

## 5. Shared files, DECISIONS and owner decisions

| Unit | MirProgram | MirBuilder | MirVocabulary | MirDescriptors | MirValidator | MirDump |
| --- | --- | --- | --- | --- | --- | --- |
| G91 | – | – | – | – | – | – |
| G94 | – | Places, Expansions | EndBorrow doc | – | – | goldens only |
| B2 | – | Places, Values | – | – | – | – |
| V (+α) | – | Values, Calls, Control, Expansions, Formatting | Call doc | MirUses (1 line) | Types | goldens only |

Owner decisions (recommendation first):
1. **G5 refinement B2** (written exclusive borrows on an input path are transparent; SPEC 15.6.7 "the same call") vs **B1** (A3 mode
   rule over reservation chains plus a validator rule). Recommend B2: shorter, matches the SPEC sentence, keeps A3 §5. Both depart
   from R4-M37 G5, approved with A3 §9 item 8.
2. **G6 refinement V** (values exist on assumed paths; Never fits as unknown) with Binding α, vs keeping `Reached` for Never-part
   operands with a located `Unsupported_Kd` where the value holds Loans (temporary until α). Recommend V + α.
G91 and G94 need DECISIONS entries only.

## 6. Unverified (H)

α's sites, and that Binding's argument fit accepts `(i32, Never)` for `(i32, i32)` today; `TypeTest`'s Never handle; A1's check-only
verdict changes under V; R5 emitting nothing for check-only blocks, `EndBorrow` and a dead shared Ref; G94's raw/Slice read order and
the guard's A1 requirement; the composed-atom recursion (§4); reference identity as Type identity in the walk's sets; the spellings
`during a` and a user `Box<T>` drop.
