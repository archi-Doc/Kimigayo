# Audit of MIR analyses A1–A4 (origin/dev `1ca50db2`, worktree a5-step1)

Read-only audit: no build, no tests run. (H) marks a claim not confirmed by running code. Paths are relative to
`src/Kimi/Compiler/`. The known items the caller listed (G91, G92a, G93, G100, G103, G105, G106, G123, HoldsRaw's depth-only
bound, A4 `Process` restarting at depth 0, the catalog `None = default` fix) are not reported again. Where a finding has a
broader cause than a known item, the finding says so.

## Verdict

A1 (MirInit/MirMoveData), A3 (MirLints) and the shared dataflow (MirDataflow/MirReach) look sound. Each of these was traced
against the SPEC and DECISIONS: the phases, chains and chain-local numbering, the pre-order path numbering, the five lanes, the
delivery and check-only rules, and A3's accumulated walk. No SPEC violation and no fault was found in them.
A2 (MirDrops) gives correct verdicts for every producer that exists today.
A4 (MirEffects) has two unsound acceptances. One (finding 1) also exists on the old path. The other (finding 2) needs a
type with at least 65 parts.
One V16 hazard (finding 3) is broader than G91: G91's fix as decided does not remove it.
The other findings are representation or contract issues and missing tests.

## Blocking

### 1. Reading an immutable static is classified by a raw/rc walk, not by Transferable (unsound `confined`)
- Where: `Mir/MirTypes.Holding.cs:15-16` (`HoldsRaw`), `Mir/Build/MirBuilder.Places.cs:161` (`MirStatic.RawHolding`),
  `Mir/MirDescriptors.cs:376` (`RawStaticRead`), `Mir/Analysis/MirEffects.cs:340`. The old path has a second, narrower copy that
  leaves out `rc`: `Binding/Binding.EffectBounds.cs:241,478` (`ContainsRawPointer`).
- Rule: SPEC 8.11.2. Reading or borrowing an immutable static is an environment effect unless the static's Type is proven
  Transferable. SPEC 15.2.4: Transferable is refuted by any reachable `rc`, Weak or raw pointer, and also by common Function
  Types, runtime-Contract views and base-object views. An abstract Type needs a Transferable premise.
- What goes wrong: `HoldsRaw` answers yes only for `raw`, `rc` and their components. A static whose Type contains `obj/Base`
  (a base-object view), a runtime-Contract view, a common Function Type, or a bare `T` of a generic group reads as no effect.
- Reproducer (H on exact syntax):
  ```kimi
  contract Probe
      func probe(self: ref/Self) -> i32
          effect confined
  open struct Animal
      public var age: i32 = 1
  struct Dog : Animal
  group Shared
      public let pet: obj/Animal = Kimi.Intrinsics.makeObj(Dog.init())   // a base-object view
      public let twice: (i32) -> i32 = func [] (n: i32) => n * 2           // a common Function Type
  struct P
      Self is Probe
      public func probe(self: ref/Self) -> i32
          let a = Shared.pet@ref      // SPEC: environment effect. A4 and the old path accept it.
          let b = Shared.twice@ref    // the same
          return 0
  ```
- Severity: blocking. This is unsound acceptance in shadow. STATUS says Transferable is not implemented, but AGENTS
  ("never accept a narrow subset") requires `Unsupported_Kd` here, not acceptance. It is also a second copy of one rule.
- Proposed fix (owner: Binding): publish one Transferable refutation fact per static Type in TyCtxt, and have MirBuilder store
  it in place of `HoldsRaw`. Until that exists, give a static whose Type has a Function, view or abstract part a located
  `Unsupported_Kd` (A4 Limit). Delete `ContainsRawPointer` with R6.

### 2. A4 `Destroyed` drops part 63's destruction when a child row's argument is 63 or more (unsound `confined`)
- Where: `Mir/Analysis/MirEffects.cs:247` (`covered |= ... 1UL << Math.Min(child.Arg, 63)`) and `:268`
  (`if (i > 63 || (covered & (1UL << i)) == 0)`).
- Rule: R4-M22/R4-M6a. Under Open, "each part without [a row] follows the own bit", so A4 must destroy every uncovered part.
  SPEC 16.3 and 8.11.2 then apply to those parts.
- What goes wrong: a child row with `Arg >= 64` sets bit 63, so part 63 is treated as covered and is never destroyed. Parts
  above 63 are always destroyed even when a row covers them (sound, but imprecise). Fields (struct and Tuple) and Downcasts
  (enum) are affected. ConstIndex is not, because its elements share one Type.
- Reproducer (H): `struct Wide` with `f0…f62: i32`, `f63: Noisy` and `f64: (string, i32)`, where `Noisy` declares
  `drop => Metrics.n += 1` and `Metrics.n` is a mutable static. A `confined` witness does `var w = makeWide()` and then
  `let s = w.f64.0@move`. `w`'s Drop is Open with a child row f64 (Arg 64), so Noisy's destructor, an environment effect, is
  never composed and the witness is accepted. SPEC rejects it (IncompatibleContractImplementation_Kd).
- Proposed fix (owner: A4): test coverage by scanning the node's direct child rows (they are contiguous), or use a pooled
  `bool[]` sized by `count`. Add the reproducer as a regression.

## Improvements, ranked

### 3. The destruction walk is exponential on DAG-shaped Types (V16). The cause is broader than G91.
- Where: `Mir/MirTypes.Destruction.cs:68-122`. The walk has no seen set for the composite Types it walks through.
  `MayNeedDrop` (`:30-34`) runs it on every builder local (`Mir/Build/MirBuilder.Drops.cs:76`) and on every A2 tree node
  (`MirDrops.cs:289`). A4's `Glue` (`MirEffects.Solve.cs:298-301,425`) never stops the walk; `glued` only deduplicates
  `Destroy`, not the descent.
- Reproducer (H on timing): `struct P0 { a: i32, b: i32 }`, `struct P1 { a: P0, b: P0 }`, … , `struct P26 { a: P25, b: P25 }`.
  Then `func f()` with body `var x: P26` makes `MayNeedDrop(P26)` visit about 2^27 leaves. The depth bound of 32 stops only
  deeper Types. `Pair<Pair<…<i32>>>` behaves the same way.
- Why G91 does not cover it: G91's decided fix ("the walk enters each reported part once per query") cuts only reported parts.
  A struct of scalars reports nothing, so the walk through it is still exponential.
- Fix (owner: MirTypes): cut every walked Type once per query (a seen set in all visitors, including `AnyPart`), or publish a
  cached MayNeedDrop fact per interned Type. Add a scaling test with nested pairs.

### 4. MirReport merges views from different bodies under one key
- Where: `Mir/Analysis/MirReport.cs:38` keys by (Code, Subject) and ORs `Views`. A view number only means something within one
  body (`MirCases.Select`).
- Problem: one subject can be analyzed in several bodies, such as an inlined default template's syntax in two callers with
  different binders. Their unrelated view bits are then ORed together (H, until A6's Inlined rule from R4-M10 lands).
- Fix (owner: A6): apply R4-M10's Inlined rule before merging, or make the body part of the fact.

### 5. "Every view" has two encodings
- A3 and A4 use `ulong.MaxValue` (`MirLints.cs:38`, `MirEffects.Solve.cs:480,484,504`). The MirProblem doc
  (`MirReport.cs:21`) also says "every bit".
- A1 uses `all`, which is only `Count` bits, for view-independent bodies and for `OwnershipStorageLimit_Kd`
  (`MirInit.cs:54,58,62`).
- Fix: one canonical constant. Use `MaxValue` for problems no view decides, or always `Count` bits.

### 6. A2's "one group per view class" is not canonical
- Where: `MirDrops.Merge` (`MirDrops.cs:310-333`) compares only with the Drop's previous group.
- Example: two binders of 2 cases each and a Type that depends only on the second binder give views Keep, Elide, Keep, Elide.
  That is 4 groups for 2 classes.
- Lookups are still correct. The header (`:6`, "one verdict group per view class") and CODEMAP overstate the behavior.
  R4-M6a's own wording ("merged into the previous group") matches the code.
- Fix: merge against every earlier group of the block, or reword the contract.

### 7. A4's read dependencies are duplicated
- Where: `MirEffects.Solve.cs:266-272`. `readers[node]` deduplicates only consecutive reads. When readers interleave, each
  re-evaluation appends another (reader, node) entry.
- Effect: `dependents` grows with evaluations × degree instead of with edges, and `Spread` (R4-M38's Θ((nodes + deps) × words))
  repeats its unions for every duplicate. This adds to G93's storage concern.
- Fix: record a dependency only on a node's first evaluation (read sets only grow), or stamp each entry by reader.

### 8. A4's per-version reset is inferred from `body.Ordinal == 0 && view == 0`
- Where: `MirEffects.cs:61-66`.
- Risk: a version with no bodies would solve the previous version's nodes, and `Evaluate` would index `program.Bodies` with
  stale owners and throw (H: the library may always contribute bodies).
- Fix: have the driver call an explicit begin.

### 9. Fragile invariants that comments should state or the validator should check
- A2's Flag verdict assumes a descendant can be Uninit under an Init own bit only together with a Partial bit. A constructing
  `_0` breaks that assumption: at entry its own bit is set, its Fields are Uninit and there is no Partial bit, and its flag
  has no set point. A Drop of `_0` would then be Flag with a flag that never sets. This is harmless today only because
  SPEC 6.3 lets no constructor fail and the builder never registers `_0`. State it in the MirDrops contract, or have the
  validator reject a Drop of a constructing `_0`.
- `MirInit.Entry` (`MirInit.cs:92`) records A2's entry events without the `Checks` guard. This is correct only while `Forward`
  calls `Entry` exactly once per run. `Backward`'s `Unwind` calls `Entry` once per chain, so a future backward solve with this
  transfer would duplicate the events.

### 10. Contract drift
- `MirInit.cs:4-5`: the Output list omits `OwnershipStorageLimit_Kd`, which `Run` records (`:58`).
- `MirReport.cs:6-7`: says `Add` is "the one home of the rules that drop derived problems", but no rule exists yet (G105/U11).
  Word it as pending.

### 11. A dead vocabulary row with an unsafe default
- Where: `Mir/MirDescriptors.cs:157`. `IntrinsicId.DestroyValues` has a row with no effects, and no producer or library
  declaration maps to it.
- Risk: if a producer appears, A4 would count no destruction for the values it destroys.
- Fix: remove it, or give its row a destruction meaning before its first use.

### 12. HoldsRaw over-approximates the SPEC 15.2.4 exception for standard iterators
- An immutable static holding a library iterator (a struct with raw Fields) reads as an environment effect, but SPEC exempts
  the standard iterators.
- Minor. Finding 1's fact would fix it as well.

### 13. Minor V16 notes
- `AddAtom` (`MirEffects.Solve.cs:445-451`) scans linearly, so it is quadratic in the atoms of one node.
- `MirMoveData.Number` keeps never-used locals as flow roots (`chains == -1`), which widens the stored words.

### 14. Missing negative tests for rules that are implemented
- A1:
  - a loop-carried Move: `var x = make()` followed by `while c => consume(x@move)` should give MovedPlace.
  - a `let` declared outside a loop and assigned inside it should give ReassignedLet.
  - a Discriminant or match on a partly moved enum.
- A4:
  - an immutable static of a Function Type, view or abstract Type (finding 1).
  - a part beyond 63 (finding 2).
  - Destruction scaling on nested pairs (finding 3).
- MirDataflow: `MirDataflowTest` checks only the backward solve. Add a forward P2 join test: a P2 head reached from a P1
  CheckTarget and from a P2 runtime edge, with a P2 runtime edge into a P1 head ignored.
- Existing tests read public layers (Problems, `MirProgram.Drops` and the dump, `Keys`). The exceptions are
  `MirInitTest.AnElementMoveAddsOnlyItsOwnPath` and the scaling tests, which read MirMoveData or MirDataflow internals. Those
  are justified as input-size and allocation checks.

## Checked and found consistent

- **Dataflow (R4-M20, R4-M34).** Chain heads come first in RPO (a single-predecessor successor cannot be a back-edge target),
  and each chain has one phase. Forward: P2 never feeds a P1 head, and frozen P1 heads pass state only along their CheckTarget.
  Backward: P1 chains join only their runtime successors. The ring queue has the capacity it needs; `Mask` and `Apply` are
  correct at word edges. Chain-local paths: stale scratch bits, including those sharing the last stored word, are never read
  before the root's first event, its StorageLive.
- **MirMoveData (R4-M21).** Pre-order ranges and the cursor aliasing in `Number` are correct; `Once` is inherited before use;
  PairLayer is transparent. Every prefix of a resolved Place is resolved, so `Find(Layer(place))` cannot miss.
- **A1 (SPEC 15.1.1–15.1.5, 14.10.3).** Construction facts for strict ancestors; whole-subtree uses; ReassignedLet through
  Ever, per StorageLive lifetime; Moves without a Move Path, including through a reference-case PairLayer; untracked raw Takes;
  delivery only on runtime paths; temporaries not reported.
- **A2 (R4-M6a, R4-M6b).** Verdict lanes; Type elision per view, with descendants skipped; the MovedPlace rule for an Open layer
  with its own drop; flag numbering and point ranges; the stable counting sort; the binary search in `Group`.
- **A3 (R4-M7a).** Seeding from the last row to the first is exact: a failure body is single-entry and its End is reachable
  only through it, so accumulated reach equals reach from each Entry.
- **A4 (R4-M22, R4-M28, R4-M38).** The fixed point is monotone, so comparing counts is valid. Tarjan's walk: lows propagate
  before the component end overwrites them, members stay split between the open stack and finished components, and callees'
  components come before their readers'. `Keys`: the bit decode is correct, read and write bits share one word, and the
  site's inputs are discarded afterwards.
- **Structure limits (COMPILER_ARCHITECTURE).** Largest file about 3,846 tokens (`MirEffects.Solve.cs`, under 5,000); no line
  over 160 characters; nesting at most 3 in the files read; every entry file has its contract header.
