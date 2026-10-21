# G160: Raw Storage Kept by Declarations

Date: 2026-10-21. Scope: the design and implementation plan for PLAN G160; no implementation. Baseline: dev `59b78023`
(code and SPEC unchanged in this area since `08145aa2`, where the designs were read); probes ran on dev `a1677549`.

Status: 完了 (closed without integration, 2026-10-21). The owner rejected its declaration statement and adopted
[G160 and G184: Raw Reach and Raw Origins](../Changes/2026-10-21%20G160%20Raw%20Reach%20and%20Raw%20Origins.md) (DECISIONS
R4-M95), which answers its Q1–Q5 and takes over its shapes, probes and call-side analysis. Only this status paragraph changed.

## 0. Summary

**Scope.** G160 covers a call's store through raw storage: PLAN's shapes and the holders that share their cause. The in-body read
side shares part of the cause but needs a MIR form decision shared with R5. Review rejected two rounds of designs for it (§11). It
moves to PLAN G184 as a separate issue for the owner's choice (Q5; §4.6), as AGENTS requires after two rounds on one family. The
call-side fix does not widen that hole, except for one class: a `via self` keeper whose pointee holds a Loan-capable part other
than through its Type's parameters. Its interim until G184 is the owner's choice (Q5, §4.6), because three review rounds found
none the plan could settle alone.

**Meaning.**

- SPEC 15.6 sends a value stored in raw storage to *storage the body does not own* (E), where it "may be used after every point".
- SPEC 5.2.1 asks that "the Type owning the storage must keep" a stored value's dependencies "through a Type argument or a
  `Loan<T>` Field". Its `Buffer<T>` example ties an inserted value's Loans to the Buffer's lifetime.
- G160 needs one distinction that SPEC does not yet state precisely: **raw storage a declaration keeps** (its keeper's holder, or
  for a view the owner's, carries the stored Loans) against **raw storage nobody keeps** (E). It is not the PLAN's "`Loan<>`
  marker against a bare pointer" (§1.4).

**Recommended reading (Q1): explicit keeping.** A struct states keeping by a declaration, the core of plan X's §5:

- `via self` for storage it owns;
- `via <loan Field>` for its owner's storage under that owner's Loan.

Every other raw layer is bare. Inferring keeping from a `raw/T` Field's shape (K′) is unsound for a non-owning, marker-less keeper
(`review-r3:cursorWrite`) unless SPEC adds three obligation sentences and amends 5.2.1. Even then, every existing `raw/T` Field
would become an implicit promise (Principles 2 and 3).

**Root cause (call side).** A5 has no fact that says which declaration wrote a raw layer and whether it keeps the pointee.
`MirTypes.HoldsLoans` walks instantiated Field Types, where that has been lost, so a call's W (rule G) approximates:

- it treats every raw pointee inside a holder as that holder's storage;
- it adds E only for the first raw layer of a local that holds no Loan (`MirWrites.Raw`, the raw clause).

Calls therefore miss E in (a), (b), (c), and in Tuple, by-value and nested-`uniq` holders.

**Design.** It keeps B's MIR representation; plan X's Binding storage graph and legacy adapter are not needed.

- `MirTypes.Keeping(D)`, per declaration, records the kept raw Fields with their form, `Concrete`, `Bare` and the bare-routed Type
  parameters.
- `MirWrites` becomes flags: the guard plus an independent `Unowned` bit. A walk of the body-level Type structure, which reads no
  Fields, computes `Unowned`.
- The guard walk counts a `via self` pointee as the keeper's own storage and drops every other raw fact.
- W adds E from the Type of the operand a call stores through.
- In-body raw accesses are unchanged: every raw Deref reaches E, and the contract states that in-body raw access to declared
  storage follows R1.
- Deleted: the raw clause, `RawLoan`/`RawPointee`, and the raw facts in the guard.

**Owner questions.**

- Q1: the keeping source, with a SPEC revision.
- Q2: shape (d), a raw write past a shared layer into a nominal Type's raw storage, for which Binding gives no row; a separate unit.
- Q3: the default pipeline stays a STATUS limit until R6-check.
- Q4: abstract callees.
- Q5: G184's representation, whitelist or hold, and the interim for the class that would widen it.

## 1. Required meaning

### 1.1 Rules (anchors under `docs/spec/`)

- **R1. Storage the body does not own** (15-ownership:454). A reference "reaches storage the body does not own when it comes from a
  parameter or capture, from static or raw storage, or from a call result that its contract ties to no argument, or derives from such
  a reference; a value stored there may be used after every point". "Reference" includes raw (03-types:164). Raw storage covers a
  `Raw.allocate` result and "the element storage of a dynamic collection" (03-types:243).
- **R2. Calls write like references** (15-ownership:454, 15.6.4). "A call writes the storage its contract lets it write the same
  way". This is rule G (DECISIONS R4-M73).
- **R3. Holders** (15-ownership:453). "A holder carries the Loans of the values stored in every part of it". "A value read, Copied
  or Moved from a part carries every Loan the holder carries". Storage the body does not own "follows the holder as Loans do"
  (S1-a, R4-M76).
- **R4. Raw authority past a shared layer** (15.6.2, 15-ownership:522; 5.2.1 condition 3). A Place is written "only through a direct
  path or an exclusive reference, or by raw access within its authority". The authority is that of the path the address was taken
  from. Binding follows this for a raw layer visible in the Type (`Binding.Retention.cs`, `RetentionWalk`: "unless a raw pointer is
  followed again").
- **R5. Retained dependencies** (05-raw:69). "A value moved into raw storage keeps its Origins and Loan requirements; the Type
  owning the storage must keep them through a Type argument or a `Loan<T>` Field (§15.3.5)".
  - The `Buffer` example (05-raw:150-195): "Because `T` appears in `Buffer<T>`, an inserted value keeps its Loans while the
    `Buffer` and every value taken or borrowed from it live".
  - `BufferView<T> {source}` keeps `Loan<ref/Buffer<T> during source>` beside `data: raw/T`: a non-owning view keeps its owner's
    Loan.
- **R6. Per declaration, by analogy** (15.3.5, 15-ownership:321). Variance, Loan requirements, OwnedOrigins and `Transferable` are
  computed "once per declaration, in terms of its own Type parameters and slots, never by expanding instantiations". Keeping is the
  same kind of fact.
- **R7. Raw borrows form no Loan.** `@raw` "reads nothing and leaves no Loan" (05-raw:101). A borrow of a raw Place has a fresh
  anchor and recovers no owner (05-raw:73).
- **R8. Markers.** A `Loan<T>` Field "counts as storing a `T` for all slot properties … and grants no access" (15-ownership:325).
  `Loan.init` "creates no authority" (`Core.kimi:64-70`).

R1 and R2 send every raw pointee a call may write to E. R5 asks owning Types to keep their stored dependencies, and its example
treats the Buffer's storage as carried by the Buffer. SPEC says neither how the checker tells kept raw storage from storage a Type
merely points into, nor whether the keeper is the value's holder. G160 needs that sentence: §1.3 gives the candidates, and Q1 asks
for it.

### 1.2 Verdicts

Reproducers are in `draft/Sketches/G160/probes/` as sources with `.out` files, run through the harness `MirBorrowsTest.Problems`.

- Each output line is `function: first line of the reported access`.
- `var y = 2` lines are E's own `Raw.initialize(cell, y@ref)` noise.

Columns:

- **Today:** A5 on dev.
- **X-decl:** the recommended explicit reading. `RawBox`, `KeptBox`, `Cursor`, `TupBox`, `Outer`, `V2`, `MarkBox` and `OwnRefs`
  carry no declaration unless the row says so.
- **K′:** the implicit reading with its three sentences (§1.3).
- **G184** marks a row this plan leaves to that issue.

| Shape (probe) | Today | X-decl | K′ |
| --- | --- | --- | --- |
| (a) `storeRaw(b@uniq, x@ref)` into `RawBox {a}`'s `p: raw/(ref/i32 during a)`, then `x = 5` (`shapes:aCallTwin`) | accepted | error | error |
| (a) direct and raw-only twins (`aDirectTwin`, `aRawOnlyTwin`) | error | error | error |
| (a′) `RawBox` by value (`b-p2:valueBox`, `a-p1:rawBoxByValue`) | accepted | error | error |
| (a″) `RawBox` past two `uniq` layers (`a-p1:rawBoxUU`) | accepted | error | error |
| Tuple holder `(raw/(ref/i32), ref/i32)` (`b-p2:tupleHolder`) | accepted | error | error |
| (b) `putRR(c@uniq, …)`, `c: raw/(raw/(ref/i32))` (`bRawRaw`) | accepted | error | error |
| (c) `r = c@ref`, `putRS(r@uniq, …)` (`cRawShared`); live twin (`cRawSharedLive`) | accepted; error | error | error |
| Raw pointer by value, `Raw.initialize(p, x@ref)` (`rawValue`, `rawInit`) | error | error | error |
| A raw pointer to a non-generic holder, by `uniq`, by value, or in a `BoxH` Field (`review2-s1:rawHolder`, `rawHolderV`, `boxHolder`) | error | error | error |
| SPEC's `Buffer<T>.push(x@ref)` (`via self` under X-decl), buffer dead / live (`kept:bufferDead`, `bufferLive`) | valid; error | valid; error | valid; error |
| Library iteration over a local `Array<ref/Data>`, then `a.value = 3`: shared, exclusive, owning, `init(capacity:)` (`kept:views*`, `a-p6`, `a-p7`) | valid (exclusive: P13 hold) | unchanged (library declared) | unchanged |
| `KeptBox<T>{p: raw/T}`, `storeKept(b@uniq, x@ref)`, b dead / live (`kept:keptDead`, `keptLive`) | valid; error | error; error (valid; error with `via self`) | valid; error |
| `OwnRefs {a}{data: raw/(ref/i32 during a)}`, its own allocation, `storeO(o@uniq, x@ref)`, o dead / `inspect(o.get())` (`review2-s3:ownRefs`) | error; error (raw clause) | error; error (with `via self`: Q5's interim, §4.6) | error; error (not a parameter pointee) |
| `TupBox<T>{p: (raw/T, isize)}` dead (`b-p5`) | valid | error (not a declarable raw Field) | valid |
| `Outer<V>{w: Wrap<raw/V>}` dead (`b-p5`) | valid | error | error (the raw layer is in `Wrap`'s storage) |
| Non-owning `Cursor<T>{p: raw/T}` built from `w@raw`, `setC(c@uniq, x@ref)`, `x = 5; inspect(w)` (`review-r3:cursorWrite`) | accepted | error | accepted; `setC`'s write breaches sentence 2 (undefined behavior) |
| `MarkBox {a}`: `raw/(ref/i32 during a)` beside `Loan<ref/i32 during a>` (`b-p1:markAlias`) | accepted | error (with `via self`: Q5's interim, an over-catch of the class, §1.3) | error (valid under the R-L variant) |
| `Array<raw/(ref/Data)>` iterated, then `arr.append(…)` (`review-r2:rawArrayIter`) | accepted | error (letter; R4-M73's per-argument rows) | error |
| In-body raw store through a Field, keeper dead (`review-r1:keptDirectDead`; `keptInitDead`) | error | error (in-body raw access follows R1, minimal core item 5) | error (sentence 3) |
| Call store into a keeper's storage, then in-body raw read through it (`a-p3:keptReadKill`, `bufferReadKill`; `review-r1:keptReadRef`, `keptReadUniq`; `review3-t1:derefKill`; `a-p5:viewRead2`) | accepted | error for undeclared keepers (the store reached E); accepted for a declared one (`bufferReadKill`): G184 | accepted: G184 |
| Raw read of a safe holder's storage through `w@raw` (`review3-t1:rawReadOfHolder`, `…Idx`; safe twin rejected) | accepted | accepted: G184 | accepted: G184 |
| `OwnRefs` store, then in-body raw read `q = o.data[0]`, o replaced, `x = 5; inspect(q)` (`review4-u2:ownRefsRead`) | error (raw clause) | error undeclared; with `via self`, Q5's interim (without one it joins G184's hole) | error |
| `var o = OwnRefs.init(seed: x@ref)`, then `q = o.data[0]`, `x = 5; inspect(q)` (`review6-w1:initThenRead`) | accepted (undeclared: the author's breach of 5.2.1) | with `via self`, valid code that A5 must check: Q5's interim | accepted (breach) |
| `PD<S>{data: raw/(S.Src.Item)}` with `RefSrc {a}`'s `Src.Item is ref/i32 during a`, `d.put(x@ref); x = 5` (`review5-v1:projOwner`) | error (raw clause) | error undeclared; with `via self`, Q5's interim (the pointee is `Concrete` through a projection) | error |
| (d) `putSB(b@ref, x@ref)` past `ref/RawBox` (`b-p1:sharedBox*`); `pushShared(b@ref, …)` into kept storage (`b-p4:keptShared`) | accepted (no Binding row) | Q2, separate unit | Q2 |

### 1.3 Readings (owner question Q1)

**X-decl, explicit keeping (recommended).** A nominal Type states each kept raw Field:

```text
unsafe storage p of T via self     // the Type owns p's storage; its holder carries the contents' Loans
unsafe storage p of T via loan     // p addresses its owner's storage under the Loan<…> Field `loan`
```

Every raw layer without such a declaration is bare, which gives E (R1). The contract source is plan X's §5
(`2026-10-21 G160 Stored Loan Destinations.md`, branch `codex/g160-storage-loan-plan`, `e76e03ae`). The owner adopts either the
whole of X §5 or this **minimal core**:

1. **The two forms.** A declaration names one raw Field of its own Type, and a `via loan` names one `Loan<…>` Field of it. Both
   Fields are non-public `let` Fields, private or internal to their module (X §5.1). In-body raw access to declared storage is then
   confined to the declaring module, which narrows G184 for declared Types.
2. **Stored content.** A `via self` pointee counts as the Type's stored content for the slot properties of 15.3.5 (variance, Loan
   requirements, OwnedOrigins, Phantom) and for holder eligibility. A `via self` Type is not Copy.
3. **The persistent invariant.** Every access to declared storage, through any alias, obeys the declared owner or the marker's
   Loan, and that owner keeps the stored values' Loans. A violation is undefined behavior; the compiler does not prove aliases.
4. **Establishment.** Establishing or restoring a declared Field happens only inside an Unsafe Block.
5. **Raw access follows R1.** Only calls use a declaration. Every in-body raw access to declared storage follows R1: a Deref,
   indexing, pointer arithmetic, and a pointer read out of the keeper. This matches X ("Direct raw Store continues to target E";
   extracting the raw Field "intentionally loses that static abstraction").

An implementation boundary, not a SPEC restriction, applies until G184 lands. It concerns a `via self` kept Field whose pointee
is `Concrete` (§4.1): the pointee holds a Loan-capable part other than through the Type's parameters, and an associated projection
counts.

- **Why.** This is the class the call-side fix would widen. Unless such a Type is a holder through another Field (the over-catch
  below), only through the declaration would it become one, so its in-body raw reads would join G184's hole (§4.6).
- **What happens.** The interim is the owner's choice (Q5, §4.6).
  - A P13-style view hold was found insufficient in review (round 6). It only reclassifies problems A5 finds, and this class's
    conflict is never found: the in-body raw read reads E only, and a constructor's result row bypasses W.
  - The options are therefore an unconditional located `Unsupported_Kd`, a recorded limit, or a reader obligation.
- **Owner.** `MirTypes.Keeping` marks the class from the `Concrete` fact §4.1 already computes, so the boundary has one owner and
  no second copy of the Loan-capability rule.
- **Over-catch.** The class also contains a keeper that is a holder today through another Field (`MarkBox` with `via self`), which
  widens nothing. Under an `Unsupported_Kd` interim that costs an `Unsupported_Kd`, not soundness.
- **Not affected.** `Buffer`, `OwnedRemainder` and the library's other keepers, whose pointees are Type parameters.

The scope per option:

- **Minimal core:**
  - the grammar;
  - Binding resolution and validation of the named Fields;
  - publication on `AdtDef`;
  - the slot-property change in TyCtxt;
  - the A5 representation below.
- **X §5 whole adds:**
  - Binding's typed backing-route proof ("prove a typed storage route from U to T in the published graph", which needs X's storage
    graph);
  - the lifecycle checks of X §5.1 (partial Moves, restoration, cleanup);
  - typed declarations for `raw/u8` Dictionary content.
- **Library annotations.** `RefRemainder`, `UniqRemainder` and `UniqSlice` take `via loan`, and `OwnedRemainder` takes `via self`.
  SPEC's examples take them too: `Buffer` `via self`, `BufferView` `via loan`.
- `raw/u8` and `raw/()` Fields hold no Loan, so G160 needs no declaration for them.

Why X-decl is recommended:

- **Sound by default.** Undeclared `Cursor`, `KeptBox`, `MarkBox` and `OwnRefs` are E.
- **Explicit (Principle 3).** The declaration states owning against borrowing, which no Field shape can: `Cursor` and `Buffer`
  have the same shape.
- **One canonical form (Principle 1).**
- **Local (Principle 2).** `Keeping` reads declarations, not unsafe conventions.

What X-decl costs:

- new grammar, and the integration of SPEC 3.4, 5.2.1, 6, 15.3.5 and 15.6 (X §8);
- library annotations, and LIBRARY's description of the public `UniqSlice` descriptor;
- every user raw box without a declaration becomes bare: `keptDead`, `TupBox` and `Outer` are errors, the letter of R1. The corpus
  has no such Type (§7).

**K′, implicit keeping.** A raw layer is kept when its declaration D writes it in its own inline Field structure (a Field, a Tuple
or fixed-array element, or a Case payload) at route Own, and its pointee holds a Loan-capable part only through D's Type parameters
(or their projections).

- This reads R5's "through a Type argument" as the keeping rule. It reads R5's "`Loan<T>` Field" as a non-owning view keeping its
  owner's Loan, which is what `BufferView` does (design B §1.3).
- It **amends 05:69**: R5 also lets an owning Type keep a slot by a `Loan<T>` Field, which K′ calls bare (`MarkBox`).
- K′ needs three sentences, or it is unsound:
  1. Kept raw storage carries its values' Loans in the keeper's holder.
  2. A raw layer K′ treats as kept addresses only storage its value owns, or storage whose owner's Loan it keeps in a `Loan<T>`
     Field (`uniq` for writes). Any other write through it breaks the writer's unsafe obligation. This closes `cursorWrite`, which
     is valid under today's SPEC and unsound under K′ without the sentence. A K′-kept view with an exclusive marker is sound: a
     call reaches the owner through the marker's guard (P13's hold), and an in-body store reaches E (sentence 3).
  3. Kept storage is accessed only through its keeper's calls. Every in-body raw access to it, a pointer extracted from it, and a
     Copy of a keeper that aliases it follow R1. The reader keeps a value's Loans live itself.
- K′ needs no grammar and no slot-property change, because its kept pointees are Type arguments, which already count. But it makes
  every existing `raw/T` Field an implicit owning promise.

**U, the letter of 15.6 (design A).** There is no keeping: every raw pointee reaches what the pointer's provenance says, at holder
granularity.

- R5's Buffer sentence becomes a lower bound: E keeps the Loans at least that long.
- Owning iteration over an `Array.init(capacity:)` holding borrows becomes an error (`capOwned`), while the same iteration over a
  literal stays valid, because the untied `init` gives Φ_E.
- Verdicts would then depend on construction history, against Principle 2. Not recommended.

**R-L and R-S, variants of K′.**

- R-L: a slot kept by a `Loan<T>` Field also keeps a raw pointee, which is R5's second way read for owners (`MarkBox` valid).
- R-S: a slot kept by a stored safe reference also keeps one (`RawBox` valid, so (a) is no defect).
- Either is one row of K′'s kept test.

### 1.4 The PLAN's framing

The PLAN's framing, "a raw pointer whose authority a `Loan<>` marker states against a bare one", does not decide G160.

- **Not necessary.** `Buffer`, `OwnedRemainder` and owning iteration keep owned storage with no marker. A marker rule sends them to
  E, which gives false errors.
- **Not sufficient.** A marker beside an owned pointee (`MarkBox`) is R-L's question, not a borrowed view.
- **A marker grants nothing (R8).** It keeps the owner's Loan, under whose liveness the raw pointer's authority holds (5.2.1
  condition 3).
- **Writes through a `via loan` Field.** A call reaches the owner through the marker's Loan with rule G's existing guard. A
  `Loan<uniq/…>` is walked as storing a `uniq` reference (R8). Today that gives P13's hold, until R4-M76's per-part guard.
- **Shared iteration needs no marker rule.** `RefRemainder`'s storage is kept, so `next`'s self-row stores into the iterator alone.

## 2. Root cause and sites

- **The missing fact.** Which declaration wrote a raw layer, and does it keep the pointee? `HoldsLoans` walks instance Types
  (`AdtDef.FieldType` substitutes; `MirTypes.Holding.cs:189-199`), so a raw layer has lost its author.
- **Effects in the holding walk.**
  - It reports `RawLoan` only in Own and Exclusive modes (`:153-156`).
  - It returns 0 for a raw pointer inside a raw pointee: shape (b).
  - It descends referents only from Own (`:174`, `own && …`). So a second `uniq` layer (a″) and a shared referent (c) are not
    walked.
  - It folds a raw pointer past an exclusive layer into the guard (`Write`).
  - It keeps the raw fact only for a value that holds no Loan (`(false,_,_,true) => Raw`, `:75-81`): shape (a). It cannot do better,
    because for a holder it cannot tell kept storage from `RawBox.p`. That is also why the naive rule gives false errors.
- **Effects in W.**
  - W's raw clause (`MirBorrows.Values.cs:227-230`) applies `Raw` only to a direct Loan naming a non-holder local.
  - By-value and Tuple operands never ask (`MirBorrows.Calls.cs:53-63`).
  - Today's catch for a raw pointer to a non-generic holder (`review2-s1`) is that clause plus `RawLoan`, both of which this plan
    replaces.
- **In Binding's contract walk.** `RetentionWalk` reads no Fields, so a raw layer inside a nominal Type reached past a shared layer
  gives no row: shape (d).
  - The subjects differ: Binding decides writable Origin positions, while A5 decides holder or E.
  - (d) is therefore a separate unit that shares one sub-fact with `Keeping` (§4.5).
- **The read side (G184, not this plan's fix).**
  - In-body `Read`, `Issued` and `Store` send every raw Deref to E (`Values.cs:72`, `:95-97`; `MirBorrows.Flow.cs:132-147`). A raw
    pointer value carries nothing, because a raw local or temporary is no holder (R4-M36).
  - The MIR builder lowers `p[n]` through a pointer temporary (`MirBuilder.Selections.cs:36-42`), so the Field a pointer came from
    is not visible at the Deref.
  - A Loan a call stored into a declared keeper's storage is therefore invisible to an in-body raw read of it (`bufferReadKill`),
    and so is a safe holder's content read through `w@raw` (`review3-t1`). Both are accepted today.
- **The default pipeline.** It keeps no Loan for any raw store (`OwnershipBody.Borrows.cs`, `PrepareRetentions`; STATUS:49). It is
  frozen until R6-check deletes it (COMPILER_ARCHITECTURE D5; DECISIONS R4-M50). The erased declarations need no code there.

## 3. Owners

- **`MirTypes`** owns the per-declaration fact and what a call may write past a value (COMPILER_ARCHITECTURE "Type facts in MIR";
  R4-M73's walk; R4-M76 places the per-part guard there).
- **Binding** owns the declarations under X-decl:
  - it parses, binds and validates them;
  - it publishes them on the declaration's stored shape (`AdtDef`, which "owns the stored shape of one struct or enum declaration");
  - it counts `via self` content in the slot properties (TyCtxt, 15.3.5).

  `Keeping` reads the publication; no consumer re-reads syntax.
- **`MirBorrows`** consumes the facts in W (`Values.cs`, `Calls.cs`).
- **Binding's retention** decides which outputs a contract writes (15.6.4). G160's units leave it unchanged.

## 4. Representation

### 4.1 `MirTypes.Keeping(D)` (new file `MirTypes.Keeping.cs`, entry contract first)

`Keeping(D)` is computed for each struct or enum declaration D from its declared parts (Fields, Case payloads and base), in D's own
parameters. It holds four facts:

- **Kept Fields.** D's raw layers that D keeps, each with its form, self or loan. A `via self` Field whose pointee is `Concrete` is
  marked: it is the class Q5's interim covers (§1.3).
  - Under X-decl, they are exactly D's declarations.
  - Under K′, they are §1.3's kept layers, all with form self.
- **`Concrete`.** D's storage, by any route, holds a Loan-capable part other than through D's parameters; `via self` pointees count.
  A Loan-capable part is a borrow or pair layer, a Slice, a callable, or an abstract part that is not D's parameter (an associated
  projection counts).
- **`Bare`.** D's storage, by any route, writes a raw layer that is not kept and whose pointee holds a concrete Loan-capable part.
- **`BareParams`.** D's Type parameters that lie, by any route, under such a raw layer.

The DFS over D's declared Type structure crosses every reference layer (`H {u: uniq/(RawBox during a)}`). It has two states: in
**before-raw** it looks for raw layers, and in **past-raw** it is inside the pointee of a raw layer that is not kept.

- **A kept Field's pointee.** It is walked as D's own storage, in before-raw. Under X-decl, a `via loan` pointee is not walked: the
  owner holds it, and the marker reaches the owner.
- **Any other raw layer** switches to past-raw.
- **In before-raw:**
  - a concrete Loan-capable part sets `Concrete`;
  - a nested instance `E<X>` sets `Bare` when `E.Bare` and `Concrete` when `E.Concrete`. It walks each `X_j` with
    `j ∈ E.BareParams` in past-raw, and the other arguments in before-raw.
- **In past-raw:**
  - a concrete Loan-capable part sets `Bare` (and `Concrete`);
  - a parameter `T_k` joins `BareParams`;
  - a nested instance `E<X>` sets `Bare` when `E.Concrete`, and each of its arguments is walked in past-raw.

**Fixed point, cache and projections.**

- **Fixed point.** Mutually recursive declarations iterate from empty summaries (`Concrete` and `Bare` false, `BareParams` empty).
  All three only grow, so this is a least fixed point.
  - Under K′, a kept layer depends on D's own "only through parameters" test. That test is `Concrete` restricted to the pointee,
    solved in the same fixed point before kept layers are read.
  - A growing instance such as `Nest<(T, T)>` maps parameters to parameters, so it adds no states (R6).
- **Cache.** The cache belongs to the scratch object that `MirBorrows` owns, beside `walked`. It is reset when `Run` meets a new
  program version, so a warm run allocates nothing.
- **Projections.** Projections are classified at the declaration, where they are abstract. A bare raw layer over a projection is
  therefore `Bare`, and instances never re-decide keeping. Binding also does not resolve an instance's projection-typed Field for
  indexing (`review2-s2`: `NotIndexable_Kd` at `h.item[0]`), so no callee writes through one.

| Declaration | Kept Fields | `Concrete` / `Bare` / `BareParams` |
| --- | --- | --- |
| `RefRemainder<E>`, `UniqRemainder<E>`, `UniqSlice<T>` (X-decl: `via loan`) | `storage` (loan) | no / no / ∅ |
| `OwnedRemainder<E>`, SPEC's `Buffer<T>` (X-decl: `via self`) | `storage`, `data` (self) | no / no / ∅ |
| `OwnRefs {a}` with `via self` (marked: Q5's interim) | `data` (self) | yes / no / ∅ |
| `RawBox {a}`, `MarkBox {a}`, `OwnRefs {a}` without a declaration | none | yes / yes / ∅ |
| `HolderR {a}` (no raw) | none | yes / no / ∅ |
| `BoxH {a}{p: raw/(HolderR during a)}` | none | yes / yes / ∅ |
| `Wrap<T>{v: T}`; `BoxW<T>{p: raw/(Wrap<T>)}` | none; none | no / no / ∅; no / no / {T} |
| `KeptBox<T>`, `Cursor<T>` without a declaration (X-decl) | none | no / no / {T} |
| `S<T>{u: uniq/(raw/T)}` | none | no / no / {T} |
| Dictionary remainders, writers, `FixedBuffer` (`raw/u8`) | none | no / no / ∅ (the pointee holds no Loan) |

### 4.2 `MirWrites` and the two walks (`MirTypes.Holding.cs`)

`MirWrites` becomes a flags byte. No new vocabulary family is added.

- The guard keeps its two members, `Through` and `ThroughArgument`. They are never both set.
- **`Unowned`** replaces `Raw`. It means that a call writing the value may write raw storage that no declaration keeps and that may
  hold a Loan. It is independent of the Loan fact and of the guard.

`Holding(local)` computes both in one query, by two traversals.

**The guard walk** is `HoldsLoans`' walk as today, with two changes:

- It walks a `via self` kept Field's pointee as the keeper's own storage, in the current place: the Loan fact and the guard. It
  decides this per Field from `Keeping`'s kept Fields, so there is no memo-order hazard.
  - This makes `OwnRefs` with `via self` a holder.
  - It gives the guard for a `via self` pointee that holds `uniq/…`.
- Every other raw layer gives nothing: no `RawLoan`, and no `Write` past an exclusive layer, because a raw pointer holds no Loan
  (R4-M36). The `RawPointee` place and the `RawLoan` fact are deleted. The memo stays a `byte`.
- Under K′ the first change is empty, because kept pointees are Type arguments and are already walked as components.

**The Unowned walk** is new and short. It covers only the body-level Type structure: the local's or operand's Type, its components
and Type arguments. It never descends into a nominal Type's Fields; `Keeping` decides those. Its states are before-raw and past-raw,
memoized per (Type, state). At each node:

- Every reference layer is walked through in the current state: `uniq`, `objuniq`, `ref`, `objref`, Slice, `rc`, `arc`, `Weak` and
  pair layers. For shared layers this is R4.
- A raw layer switches to past-raw.
- In past-raw, a Loan-capable part gives `Unowned`, and so does an instance `D<args>` with `D.Concrete`. Its arguments are walked in
  past-raw.
- In before-raw, an instance `D<args>` gives `Unowned` when `D.Bare`. An argument `args[k]` with `k ∈ D.BareParams` is walked in
  past-raw, and the other arguments in before-raw.
- The walk stops at the first `Unowned`, and each (Type, state) is walked once.

Because the Unowned walk reads no Fields, an instance met in a Field context and in a component context cannot hide a raw layer
through memo order. That is the hazard an InDeclaration mode would otherwise have to prevent.

### 4.3 W (`MirBorrows.Values.cs`, `MirBorrows.Calls.cs`)

`Writes(state, set, through)` takes `through`, the `MirWrites` of the operand the call stores through into output j. For a
whole-local operand, the usual MIR temporary, that is `writable[local]`; otherwise it is the Unowned walk of the operand's case
Type.

1. refs(S) and rule G's two passes are unchanged (R4-M73).
2. **E when `through` has `Unowned`.**
   - The Unowned walk follows every route a call may write by, so E is exact per argument route: `put(b.r@uniq, x@ref)` adds no E,
     while `b@uniq` does.
   - The rule covers by-value, Tuple, nested-`uniq` and raw-only operands, and raw pointers to non-generic holders (`review2-s1`), so
     the raw clause is no longer needed.
3. **E when a holder or root local reached in the second pass has `Unowned`.**
   - A Type argument, a pair layer or an abstract part hides that route from the operand's Type.
   - That E lies past `clean`, so a non-empty value holds the view (P13's interim, R4-M73), never silently.
4. **Deleted.** The raw clause in `Write` (`Values.cs:227-230`) and its comment text. `Written` and the passes compare only the guard
   bits.

Every store path passes `through`:

- a Call's rows, with the operand of `row.Output`;
- A5-F7's capture stores (unit 2c), with the capture's Type;
- A5-F6's desc(c) stores (2f), with the receiver operand.

In-body raw accesses (`Read`, `Issued`, `Store`) are unchanged: every raw Deref reaches E, which minimal-core item 5 and K′ sentence
3 make the contract.

### 4.4 Composition

- **R4-M76's per-part guard** (not yet designed) reads a generic Type's Fields for the guard, and `Keeping` reads the same
  declarations for the raw facts. The per-part guard's bits join `Keeping`'s summary and cache as its own entries, rather than
  adding a second per-declaration reader. G160 adds no bits for it.
- **P13's body hold** is unchanged. Rule 2's E is concrete (pass 0); rule 3's E lies in the second pass.
- **2e-D's raw-pointer row for defaults** (prototype.md finding 7; 2e-D's design is in flight on dev):
  - Its condition becomes "the slot Type has `Unowned`", the same walk.
  - Once A5-F9 gives template calls their contract rows, rule 2 covers `dc-raw-hid` and `dc-raw-hid-x`, and the row is deleted
    with the unknown-callee interim.
  - If 2e-D lands first, G160-3 changes only the condition's spelling.
- **Captures (2c) and desc(c) stores (2f)** pass `through` and inherit everything here.

### 4.5 Shape (d): Binding rows (separate unit, after Q2)

`RetentionWalk` marks writable storage past a shared layer only "unless a raw pointer is followed again". It never reads a nominal
Type's Fields, so a raw Field inside `RawBox` or `KeptBox` reached past `ref` gives no row. `sharedBoxLive` and `keptShared` are
accepted with no row, and `keptShared` observes the stale reference through a safe accessor.

- **Recommended.** Binding gives rows for the parameter and slot positions that a nominal Type's raw layers make writable past a
  shared layer, the rule Binding already applies to a visible raw layer (c). A5 maps those rows with §4.3: E for bare storage, and
  the keeper for kept storage, where a shared `via loan` grants no write.
- **Alternative.** A SPEC sentence makes such writes the unsafe author's breach. That is the reading 8.11.4 rule 1 suggests for
  abstract callees ("only shared past a shared layer", 08-generics:789).
- **Shared sub-fact.** "The raw layers a declaration's storage writes" is common to `Keeping` and (d)'s per-declaration row fact.
  (d)'s design decides its single owner. If Binding needs it, it moves to `AdtDef` or `TyCtxt` beside the declarations, and
  `Keeping` reads it there.
- **Process.** (d) gets its own PLAN row, design and review under Binding's contract. G160 can close without it.

### 4.6 The read side: G184 (separate issue, owner's choice Q5)

**The problem.** An in-body raw read reaches E only. Two kinds of value are missed:

- a Loan a call stored into a declared keeper's storage (`bufferReadKill`; under K′ also `keptReadKill`, `keptReadRef`, `viewRead2`
  and `derefKill`);
- a safe holder's content read through `w@raw` (`review3-t1`).

Both are accepted today, while their safe twins are rejected. SPEC 15.6 says that "a read through a reference carries the Loans of
what it reaches", and the unsafe reads meet 5.2.1, so these are unsound acceptances in safe code.

**Why it is separate.**

- Review rejected two rounds of designs for it (round 1: B2, B4; round 3: R3-B1, R3-B2, R3-B3).
- A declaration-classed step cannot see `p[n]`, which MIR lowers through a pointer temporary.
- A kept step must also keep {Φ_E}.
- A classed step for marker-kept storage needs the view's mode.

**What the call-side fix does to the hole.**

- An undeclared store reaches E, which every read sees.
- A declared store into a keeper that is a holder today (`Buffer`, `OwnedRemainder`, the iterators: their pointees are Type
  parameters) reaches that keeper, exactly as today.
- One class would widen the hole: a `via self` keeper whose only Loan-capable content is its raw pointee (`OwnRefs`). The
  declaration makes it a holder, so its stores would move from E to the keeper, and an in-body raw read would miss them
  (`review4-u2:ownRefsRead`, an error today through the raw clause).
- The class is §4.1's `Concrete` on the pointee, so a Loan through an associated projection is caught too
  (`review5-v1:projOwner`). A Loan also enters through a constructor's result (`review6-w1:initThenRead`).
- Three review rounds rejected interims for this class (R4-B1, R5-B1, R6-B1). The last was a P13-style view hold, which only
  reclassifies problems A5 finds; this class's conflict is never found. As AGENTS requires, the interim is the owner's choice
  (Q5):
  - **(a)** Record the class in G184's limit.
  - **(b)** A complete, unconditional located `Unsupported_Kd`, owned by A5 with `Keeping`'s mark and with G184 as its removal
    condition. It can be placed in one of three ways:
    - (1) at every Call whose store or result row puts a non-empty value into a holder whose Type has a marked Field;
    - (2) at each local, parameters and temporaries included, whose case Type has one;
    - (3) at the declaration, where `Keeping` decides it.
  - **(c)** Make in-body raw reads of declared storage the reader's obligation (K′ sentence 3's wording). This would also move
    G184's declared-keeper half to the unsafe reader.
- These follow AGENTS: a valid form that no rule covers yet is `Unsupported_Kd`, never a narrowed SPEC or a conservative Language
  error.
- Minimal-core item 1 (non-public `let` Fields) confines what remains to the declaring module.

**Candidates for the owner** (AGENTS: after two rounds on one family, the owner chooses a representation, a whitelist or a hold):

- **(i) Representation: a raw pointer value carries what it addresses.** This is design A's direction, with R3 for pointers read
  from a part.
  - A raw local or temporary whose pointee may hold a Loan becomes a holder.
  - A read through a raw Deref takes the pointer's own set ∪ {Φ_E}.
  - A borrow through it needs a content route to the addressed storage (Named-for-content, or the MIR builder publishing a raw
    element as an `Index` projection of the pointer's Place).
  - `w@raw` carries σ(w).
  - It is shared with R5, which owns `MirBuilder` and `MirValidator`, and takes the full process.
- **(ii) A body-level hold.** It uses facts A5 already has, without (i)'s carried set. A view is marked when it
  contains an in-body raw read and also either takes `@raw` of a holder's Place or puts a value into a keeper with kept storage
  through a call's store or result row (`review6-w1:initThenRead`). Every in-body raw read in such a view is then a located
  `Unsupported_Kd`, reported at the read itself and unconditionally. A P13-style reclassification would report nothing, because these conflicts are never found.
  - Cost: library bodies that take `@raw` of a parameter's Place (for example `FixedOwnedRemainder.drop`) and their independent
    problems. The census would show them.
  - A whitelist is the narrow form of the same: in-body raw reads admitted only through raw-only locals that never derive from a
    holder's Place.
- **(iii) A recorded limit.** The issue stays open in PLAN and STATUS until (i) lands, as the default pipeline's raw stores do today.

## 5. Deletions and sites sharing the cause

The code search covered `HoldsLoans`, `MirWrites`, `RawLoan`, `StepClass.Raw`, `escapedSet`, `Writes(` and `RetentionWalk` over
`src/Kimi/Compiler/Mir` and `Binding`.

| Site | Same cause? | Action |
| --- | --- | --- |
| `Values.cs:227-230` raw clause; `MirWrites.Raw` (`Holding.cs:15-16`, `:79`) | yes | deleted; rule 2 and `Unowned` |
| `Holding.cs:153-156`: raw facts, `RawPointee`, `RawLoan`, raw past exclusive as `Write` | yes: (b), (a″) | deleted from the guard walk; the Unowned walk |
| `Holding.cs:174-179`: referents walked only from Own | yes: (a″), (c) | the Unowned walk crosses every reference layer |
| `Holding.cs:189-199`: raw Fields judged by their instance Types | yes: (a) and the naive rule's false errors | `Keeping`; the Unowned walk reads no Fields; `via self` in the guard walk |
| `Calls.cs:53-63`: W per output | yes: by-value and Tuple operands | passes `through` |
| 2c capture stores, 2f desc(c) stores, 2e-D's raw row (planned or in flight) | yes (same W) | pass `through`; condition `Unowned` |
| `Binding.Retention.cs` `RetentionWalk` past a shared layer at a nominal Type | yes, in Binding's contract | (d), separate unit |
| `Values.cs:72`, `:95-97`; `Flow.cs:132-147`; `MirBuilder.Selections.cs:36-42` (in-body raw reach; `p[n]` through a temporary) | partly (the read side) | G184 (§4.6) |
| G147 (1): `placeEntry(handle: raw/u8, …)` stores into typed content behind `raw/u8` with no row (2d-3) | no (no contract row; another cause) | unchanged |
| `HoldsRaw` (builder's `confined`) | no (environment authority) | unchanged |
| Default pipeline (`OwnershipBody.Borrows.cs`, `PrepareRetentions`) | yes | frozen until R6-check (Q3) |

The design text this makes unnecessary:

- A3 E16's sentences on a raw-only local (`design-A3.md:759-763`), superseded by an erratum rather than rewritten.
- E14's "the refs of a value with no direct Loan are E" as the reason a raw input reaches E. It now follows from `Unowned`.

## 6. Options

Three designs were written independently and then compared: A and B by this session's designers
(`draft/Sketches/G160/design-a.md`, `design-b.md`), and X by another agent.

| Option | Content | Verdict |
| --- | --- | --- |
| **This plan** | B's `Keeping` and `Unowned`, with `Concrete` and the shorter walk that reads no Fields; `via self` in the guard walk; X's explicit contract as the keeping source; the read side split off to G184 | **Chosen.** One per-declaration fact decides kept against bare for every call store. It is exact on every call-side shape in §1.2 and composes with R4-M76, P13 and 2e-D. About +150 net code lines in A5 (§8), plus the declaration's work under X-decl. |
| X's representation: a Binding-owned storage graph, retention outputs as (input, storage node), and a legacy adapter | | Not adopted. Destination-identity rows refine rule G's per-argument W, which R4-M73 keeps coarse; they add precision but are not needed for soundness. The adapter needs a D5 freeze exception for code R6-check deletes (Q3). X ran no probes. Its contract (§5) is Q1's recommended keeping source, whole or as the minimal core. |
| A: raw pointers are references carrying provenance; raw locals become holders; a marker states authority; reading U | | Its store side is not adopted: under U, verdicts depend on construction history (`capOwned`) and owned storage reaches E. Its read-side finding is G184, and its carried-provenance idea is G184's candidate (i). |
| B alone (K′; in-body raw unchanged) | | Not adopted: K′ is unsound for `Cursor` without sentence 2. |
| A declaration-classed in-body step, or a kept-step helper at the three raw-to-E sites (rounds 1–3) | | Withdrawn to G184. Neither sees `p[n]`, MIR's lowering of raw indexing; the step needs {Φ_E} and the view's mode. |
| Naive rule: E for any W holder whose Type has a Loan-capable raw pointee | | Rejected: three false errors on shared iteration (PLAN). |
| PLAN's marker rule: E unless a `Loan<>` marker sits beside the raw Field | | Rejected (§1.4): false errors for owned storage (`Buffer`, owning iteration). |
| Instance-walk exemption: treat a Field `raw/P` with P a parameter as kept inside `HoldsLoans` | | Rejected: a clause for one form, memo-order hazards, and R4-M76 would add a second reader. |
| Visibility: storage hidden behind module privacy belongs to its keeper | | Rejected: verdicts would depend on module boundaries. |
| Raw provenance element Φ_R, or Φ_E per part | | Rejected: a vocabulary entry finer than S1-a's holder granularity (R4-M76 rejected Φ_E per part). |

## 7. Tests

Use the owning classes. Do not add native fixtures for analysis-only outcomes.

**New fact in `MirBorrowsTest`, `ARawStoreReachesEUnlessItsDeclarationKeepsIt`** (SPEC 15.6, 15.6.2, 5.2.1). It uses the Prelude
plus the §1.2 Types, with the declarations the chosen reading needs. It does not `Raw.initialize` a second local, so E adds no
noise.

- **Fail before, pass after:** `x = 5` in `aCall`, `rawChain`, `rawShared`, `tupleHolder`, `byValue`, `twoUniq`, `cursorWrite`
  (X-decl) and `marker`.
- **Unchanged errors:**
  - `rawOnly`, `rawValue`, `rawInit`;
  - `rawHolder`, `rawHolderV`, `boxHolder`;
  - the direct twin, `keptDirectDead`, `keptInitDead`;
  - the live twins, `cRawSharedLive`, and the undeclared `OwnRefs` used after the store.
- **Valid, expected empty:**
  - the iterations: `views`, `viewsLater`, `viewsAppend`, `viewsOwned`, `initOwned`, `capOwned`, `ownedAppend`;
  - `bufferDead` with `via self`;
  - `reborrowedPart` (`put(b.r@uniq, x@ref)` with b dead), which shows that rule 2 is exact per route.
- **Verdict changes the reading makes,** each listed with its rule in the commit:
  - under X-decl: the undeclared `keptDead`, `OwnRefs` dead, `TupBox` and `Outer` dead, and `rawArrayIter`;
  - under K′: `Outer` dead and `rawArrayIter`;
  - under X-decl, a keeper over an exclusive-reference argument, `ArrayOwningIterator<uniq/…>` (through `OwnedRemainder`) or
    `Buffer<uniq/…>`. Walking a `via self` pointee as own storage gives it the guard concretely, so a store past it is no longer
    P13-held and its conflicts become Language problems. This is R4-M76's per-part direction; the census checks it.
- **Unchanged:** `viewsUniq` keeps P13's hold.
- **The widening class (X-decl):** `OwnRefs`, `ownRefsRead`, `projOwner`, `initThenRead` and `MarkBox`, each with `via self`.
  They get the verdict of the interim Q5 picks, and G160-3 adds them with it. `MarkBox` is an over-catch.
- **The read-side shapes:** G184's unit adds them, apart from `ownRefsRead`, whose declared form is in the widening-class list
  above.

**Declaration tests (X-decl), in Binding's and the diagnostics' classes:**

- a missing or ambiguous Field, a non-raw Field, and a non-`Loan` backing Field;
- two declarations for one Field;
- a `via self` Copy Type;
- establishment outside an Unsafe Block;
- `via self` content in the slot properties: a non-generic owner's slot is no longer Phantom.

**`MirTypesTest`:**

- A new test, `RawStorageNoDeclarationKeepsIsUnowned`.
  - These Types are `Unowned`:
    - raw layers and chains: `raw/(ref/i32)`, `raw/(raw/(ref/i32))`, `ref/(raw/(ref/i32))`, `(raw/(ref/i32), isize)`;
    - `RawBox` and its nestings: `RawBox`, `uniq/(uniq/RawBox)`, `H {a}{u: uniq/(RawBox during a) during a}`;
    - holders behind raw: `raw/HolderR`, `uniq/(raw/HolderR)`, `BoxH`, `BoxW<ref/i32>`;
    - raw in arguments: `S<ref/i32>`, `Slice<raw/(ref/i32)>`, `Option<raw/(ref/i32)>`;
    - `Cursor<ref/i32>` and `KeptBox<ref/i32>` without a declaration.
  - These are not `Unowned`:
    - `ArrayIterator<ref/i32>`, `ArrayOwningIterator<ref/i32>`, `Buffer<ref/i32>`;
    - `KeptBox<ref/i32>` and `OwnRefs` with `via self`;
    - `BoxW<i32>`, `raw/i32`, `(ref/i32, isize)`.
- `HoldsLoans`:
  - `KeptBox<ref/i32>` with `via self` holds a Loan;
  - `KeptBox<uniq/(ref/i32)>` with `via self` passes the guard concretely (`Through`; today `ThroughArgument`);
  - `Keeping` marks `OwnRefs` and `PD<RefSrc>` with `via self`.
- A `Keeping` test pins §4.1's table, including a mutually recursive pair and a growing instance.

**Expectations that change, with reasons:**

- In `TheGuardSaysWhatACallMayWritePastAValue` (`MirTypesTest.cs:46-72`), `raw` moves from `Raw` to `Unowned`: the same fact,
  generalized.
- `toRaw` (`uniq/(raw/(ref/i32))`) moves from `Through` to `Unowned`.
  - A raw pointer past an exclusive layer gives the raw fact, not the guard, because a raw pointer holds no Loan and is never a
    holder.
  - E is reached by rule 2 instead of through the raw-only local the Loan names. W's targets are unchanged
    (`ACallStoresIntoEveryHolderItsArgumentReaches`' `rawPointee: b = 3`).
- Neither change is a weakening: no expected problem disappears.
- `AnIntrinsicRetainsAsItsDeclarationSays`, `ACallStoreFollowsSpecsPerArgumentRule` and `LoansAreHeldBy…` are unchanged.

**Allocation, scaling and census:**

- **Allocation.** `AWarmRunAllocatesNothing` gains the `aCall` body, so its Loan-problem count goes from 7 to 8. The `Keeping` cache
  fills during the warm-up.
- **Scaling.** Add N nested generic declarations wrapping a kept `raw/T`, a raw chain of length N, and a recursive family.
  `Keeping`'s DFS steps and the Unowned walk count in `Work` (A3 6.2) and must grow linearly. Compare the existing scaling rows
  against the unit's own base, measured in the unit.
- **Census** (`MirCensusTest`: library, 44 milestones, 31 goldens). No new A5 problem is expected:
  - under X-decl, every library raw Field whose pointee may hold a Loan is declared (§1.3);
  - a search of `tests/` finds no corpus raw Field or local whose pointee may hold a Loan outside the A5 test classes;
    `tests/mir/raw.kimi` and `adaptations.kimi` use `raw/string` and `raw/i32` parameters.

## 8. Size, speed, unverified

| File | Lines (estimate) |
| --- | --- |
| `MirTypes.Keeping.cs` (new: summary, fixed point, cache; entry contract) | +130 |
| `MirTypes.Holding.cs` (guard walk: raw facts out, `via self` in; Unowned walk) | +50/−30 |
| `MirBorrows.Values.cs` (W `through`, rule 3; raw clause deleted) | +18/−14 |
| `MirBorrows.Calls.cs`, `MirBorrows.cs` (scratch, reset) | +7/−1 |
| X-decl minimal core: Parsing, Binding (declaration, validation, `AdtDef` publication, `via self` slot properties), diagnostics, library annotations | about +200, designed and measured in G160-2; X §5 whole adds its storage graph and lifecycle checks |
| tests | +150/−4 |
| DECISIONS, A3 erratum, PLAN, STATUS, LIBRARY, ARCH, CODEMAP rows | +40 |

Net A5 code is about +160 lines.

**Speed:**

- One DFS per declaration per program version.
- The guard walk gets shorter, because its raw facts are gone. It adds one `Keeping` lookup per raw Field.
- The Unowned walk visits only the body-level Type structure, each (Type, state) once, and stops at `Unowned`. For a Type without
  `Unowned`, which is the common case, it is exhaustive, but bounded by the Type's component and reference structure, not its
  Fields.
- W adds one flag read per store output.
- To measure: A5 work, bytes and time on `MirBorrowsTest`'s shapes and on the corpus, against the unit's base, as pinned
  interleaved A/B runs.

**Unverified** (each is a check in its unit, not an assumption):

- the census, ledger and held-view effects;
- the fixed point's round count on the library;
- that a generic body cannot store a local Loan into an abstract Type's hidden raw storage (argued; Q4);
- the speed;
- the X-decl Binding cost (G160-2).

## 9. Units and records

The units belong to R4 (A5) before R4's exit and are serialized in one worktree. Each behavior unit adds regressions that fail
before and pass after.

**G160-0 (owner).**

- **Work:**
  - answers to Q1–Q5;
  - under X-decl, the owner adopts the declaration contract (X §5 whole, or §1.3's minimal core) as an explicit SPEC revision,
    with SPEC's `Buffer` and `BufferView` examples;
  - under K′, the amendment of 05:69 and the three sentences;
  - approval of `MirTypes.Keeping` and of the `MirWrites` flags with `Unowned`.
- **Completion:** the SPEC revision is integrated with its INTEGRATED record. No A5 behavior changes before Q1.

**G160-1 (behavior-preserving; VERIFICATION#behavior-preserving-commits).**

- **Work:** `MirWrites` becomes flags with today's members; `Raw` keeps its name and condition. `Holding` takes the scratch object
  that `MirBorrows` owns.
- **Completion:** `MirTypesTest`, `MirBorrowsTest` and `MirCensusTest` are identical.

**G160-2 (X-decl only).**

- **Area and process:** Parsing, Binding and TyCtxt. It takes the full process, because it adds new grammar, a new `AdtDef`
  publication and diagnostic codes.
- **Work:** its own design (two independent designs if the owner adopts X §5 whole) and review, covering:
  - the `unsafe storage` declaration's syntax, binding, validation, publication on `AdtDef`, and diagnostics;
  - `via self` content in the slot properties;
  - the library annotations and LIBRARY's descriptor text.

  No A5 change.
- **Completion:** declaration, slot-property and diagnostics tests; `KimiLibraryShapeTest`; Session.

**G160-3 (behavior).**

- **Process:** this plan's design, once G160-0 records the owner's approval. It needs one review before completion (DECISIONS Q6's
  first form).
- **Work:**
  - `MirTypes.Keeping`;
  - the two walks (§4.2);
  - `Raw` becomes `Unowned` with its new meaning;
  - W rules 2 and 3;
  - the interim Q5 picks for the widening class (§4.6), with G184 as the removal condition;
  - the raw clause and the raw guard facts are deleted.
- **Completion:** the §7 matrix; census, allocation and scaling; Session.
- **Records:**
  - DECISIONS R4-M (next free), "Raw storage a declaration keeps (PLAN G160)": the reading, the representation and its owner, the
    deletions, composition with R4-M76, P13 and 2e-D, and the rejected options (§6);
  - A3 §12 erratum E17 for E16's raw clause.

**(d) (Binding, after Q2).** Its own design and review under Binding's contract (§4.5). It has its own PLAN row and lies outside
G160's group.

**G184 (after Q5).** Its own design and review under the owner's choice (§4.6): (i) is shared with R5; (ii) uses facts A5
already has and reports each marked read unconditionally. It lies outside G160's group.

**Records at G160-3's merge:**

- **PLAN:** G160 closes. (d)'s row gets the next free G number at push time.
- **STATUS:49:** A5 rejects §1.2's call-side shapes. The default pipeline's sentence stays, pointing at R6-check, at (d)'s row and at
  G184.
- **ARCH and CODEMAP:** `MirTypes.Keeping` joins the "Type facts in MIR" row.

## 10. Owner questions

- **Q1. How does a Type keep raw storage?**
  - Recommended: **X-decl**, explicit `unsafe storage … via self | via <loan Field>` declarations, with every other raw layer bare.
    The contract is plan X's §5 whole, or §1.3's minimal core, adopted as a SPEC revision. §1.3 gives each option's Binding scope.
  - Alternatives:
    - **K′:** implicit keeping by a Type-parameter pointee. It amends 05:69 and needs three sentences (§1.3).
    - **U:** no keeping, the 15.6 letter; `capOwned` errors.
    - **R-L / R-S:** variants of K′ that also keep slots kept by a `Loan<T>` Field or by a stored reference.
- **Q2. Shape (d).**
  - Recommended: Binding rows for raw-writable positions of nominal Types past a shared layer, as for a visible raw layer. This is
    a separate unit.
  - Alternative: a SPEC sentence makes such writes the unsafe author's breach.
- **Q3. Default pipeline.**
  - Recommended: it stays a STATUS limit until R6-check deletes it (D5 freeze, R4-M50).
  - Alternative: a D5 exception for an adapter (X's U5).
- **Q4. Abstract callees.** Confirm that 8.11.4 rule 1 ("only shared past a shared layer") holds for raw storage hidden in an
  abstract part. Then A5 adds no reach there beyond rule 3's held E.
- **Q5. G184, the read side** (§4.6).
  - The candidates are:
    - (i) a representation shared with R5;
    - (ii) a body-level hold that reports each marked in-body raw read unconditionally, or its whitelist form;
    - (iii) a recorded limit until (i).
  - Recommended: (iii) now, and (i) as G184's design, scheduled with R5.
  - The widening class (a `via self` pointee that is `Concrete`, such as `OwnRefs`) needs an interim until G184 (§4.6). The
    options are:
    - (a) record it in G184's limit;
    - (b) a complete located `Unsupported_Kd`, owned by A5: (1) at Calls, (2) at locals, or (3) at the declaration;
    - (c) a reader obligation for in-body raw reads of declared storage.
  - A P13-style hold is not an option: it reports nothing here (review round 6). The planning session's suggestion is (b)(2). It
    is complete by construction and A5-owned, and it costs only programs that declare such a Type. The choice is the owner's.

## 11. Review record

**Process.** Designs A and B were written independently from one brief and compared with plan X. Reviews and probes are in
`draft/Sketches/G160/`.

**Round 1** (independent reviewer; `review-1.md`): rejected. The blocking findings were:

- **B1:** walk transitions;
- **B2:** reads past a reference;
- **B3:** K is unsound for non-owning keepers;
- **B4:** direct stores;
- **B5:** R5 misread.

What changed: X-decl became the recommended answer, K′ gained its sentences, R-L and R-S were split, and the Unowned walk now crosses
every reference layer. N1–N13 were adopted.

**Round 2** (same reviewer; `review-2.md`): rejected. B1–B5 were resolved. The new blocking findings, and their fixes:

- **R2-B1:** `Concrete` was missing. It is restored.
- **R2-B2:** `via self` content was not counted. It now counts in the guard walk, and the minimal core includes holder eligibility
  and slot properties.

The round's non-blocking findings were adopted.

**Round 3** (same reviewer; `review-3.md`): rejected. R2-B1 and R2-B2 were resolved. Every new blocking finding concerned the
in-body read side:

- **R3-B1:** the declaration-classed step misses `p[n]`, which MIR lowers through a temporary.
- **R3-B2:** a kept step must keep {Φ_E}.
- **R3-B3:** K′'s Own class makes a user view with a marker unsound.

With round 1's B2 and B4, that is the second rejected round on one family. As AGENTS requires, that family left the plan and became
G184, with the owner's choice Q5 (§4.6).

- K′ sentence 2 now excludes marker-kept views.
- Minimal-core item 5 and K′ sentence 3 state the in-body rule (R1).
- The round's N1 (`review3-t1`) joined G184. Its N3 and N4 concerned the withdrawn step classification.

**Round 4** (same reviewer; `review-4.md`): rejected. The split was confirmed legitimate under AGENTS, and the call-side design
was confirmed sound and independent of the withdrawn step. The blocking findings, and their fixes:

- **R4-B1:** the claim that the call-side fix does not widen G184 was false for a `via self` keeper whose only Loan-capable content
  is its raw pointee (`review4-u2`). The class is stated, and such a declaration is a located `Unsupported_Kd` until G184
  (§1.3, §4.6), with Q5's alternatives. Minimal-core item 1 now requires non-public `let` Fields.
- **R4-B2:** K′ sentence 2's marker alternative was restored; library views are K′-kept and sound.

N1 (a body-level hold without a carried set, and the whitelist form) is in §4.6. N2 and N3 are in §1.2, §7 and G184's row.

**Round 5** (same reviewer; `review-5.md`): rejected.

- **Resolved:** R4-B2. Reporting the interim at the declaration was found acceptable.
- **Blocking, R5-B1:** the "slot made non-Phantom" predicate missed a Loan that reaches the pointee through an associated
  projection (`review5-v1:projOwner`).
- **The fix:**
  - the boundary is §4.1's `Concrete` on the pointee;
  - `MirTypes.Keeping` alone decides it, so no second copy of the Loan-capability rule moves into Binding;
  - the declaration-located `Unsupported_Kd` became R4-M73's body hold on stores into such keepers (then §4.3 rule 4, withdrawn
    in round 6), and the former became Q5's alternative (b).
- **Non-blocking, adopted:**
  - N1: the `MarkBox` over-catch is stated;
  - N2: the guard test uses `KeptBox<uniq/(ref/i32)>`;
  - N3: the change from P13-held to Language for keepers over exclusive arguments is listed.

**Round 6** (same reviewer; `review-6.md`): rejected.

- **Blocking, R6-B1:** a P13-style hold cannot cover the widening class. It only reclassifies problems A5 finds, and this class's
  conflict is never found. A constructor's result row also bypasses W (`review6-w1`).
- **Escalated.** This is the third rejected round on that family, so, as AGENTS requires, the interim went to the owner as Q5's
  options (§4.6):
  - the `Held` bit and W rule 4 were withdrawn;
  - the same insufficiency was corrected in G184's hold candidate (ii), which now reports each read unconditionally.
- **The rest of the plan.** The reviewer found the rest ready: the call side (`Keeping`, the Unowned walk, `via self` in the guard
  walk, W rules 2 and 3), the G184 split, and the PLAN rows.

**Round 7** (same reviewer; `review-7.md`): a records check after the escalation. It found the plan ready for commit with Q1–Q5
open. Its four optional notes were adopted: G184's (ii) trigger includes result rows; the round-5 entry names the withdrawn rule;
§1.3's "Why" is qualified for `MarkBox`; and INTEGRATED cites this review.

The read-side issue was registered as G182 during the reviews. At push it took dev's next free number, G184, because another
session had used G182 and G183; the review files keep the old number.

**State.** The plan is complete apart from the owner's answers. No further design round is open.
