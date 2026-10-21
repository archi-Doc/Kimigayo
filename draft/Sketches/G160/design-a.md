# G160 design A: a raw pointer reaches what it carries

Designer A, independent design for PLAN G160 (full process). Repository read: `bin/worktrees/g160-plan` at 08145aa2. Probes:
`scratchpad/g160/a/p1–p7.kimi` (+ `.out`), run through `probe.sh` on dev a1677549 (Loan codes only; `Unsupported_Kd` is not printed).

## 0. Summary

A5 decides where the storage behind a raw pointer lies in two places with two answers. In-body Read, Issued and Store send
every raw Deref to E (`MirBorrows.Values.cs:72`, `:95-97`, `MirBorrows.Flow.cs:132-143`). A call's W sends a store past a
holder's raw pointer to the holder alone. Only a raw-only local's first raw layer reaches E: `MirWrites.Raw`, the raw clause
(`MirBorrows.Values.cs:227`). The holding walk does not treat a raw pointer as a reference. A raw pointer holds no Loan, so a
raw local is not a holder and the provenance of a pointer stored into a struct is lost (`MirTypes.Holding.cs:56-60`). RawPointee
mode yields 0 (`:155`), and nothing past a shared layer is walked (`:174-175`). The PLAN's shapes (a)–(c) follow from this. The
probes also show the read side of the same cause, which the PLAN does not list (§2).

Design A makes a raw pointer an ordinary reference for carrying and for W, as SPEC S3:164 ("`reference` is every Semantics except
`owner`, raw included") and 15.6 say. Only the raw-specific rules stay separate: it forms no Loan (5.4), its borrows get fresh
anchors (5.2.2), and it writes within its own authority, also past a shared layer (15.6.2, 5.2.1 condition 3). One fact is new:
a `Loan<…>` Field can state the authority of a raw Field. The representation lives in `MirTypes`' one holding walk. MirBorrows
uses one raw reach for Read, Issued, Store and W, and rule G's existing step does the rest. `MirWrites.Raw`, the raw clause and the
RawPointee mode are deleted.

The recommended SPEC reading is the letter (U). Bare raw storage reaches E exactly where its holder carries Φ_E (S1-a). Under U,
`Buffer` and `KeptBox` pushes with the holder dead become errors, as their direct twins already are. Reading K (Type-kept storage
belongs to its keeper) is one row; it is owner question Q1.

## 1. Required meaning

### 1.1 Rules

- **R-reach** (15.6, 15-…:453-454): a reference reaches the holders named by the direct Loans it carries. It also reaches storage
  the body does not own when it comes from a parameter, capture, static or raw storage, or an untied call result, or derives
  from such a reference. A value stored there may be used after every point. A write through a reference adds the value's Loans
  to every holder it may reach. A call writes the storage its contract lets it write "the same way".
- **R-holder** (15.6:453): a value read, Copied or Moved from a part carries every Loan the holder carries. Storage the body does
  not own follows the holder as Loans do (S1-a, owner R4-M76; holder granularity, Φ_E per part rejected).
- **R-raw-ref** (S3:164): a raw pointer is a reference. **R-noloan** (5.4, 05-…:101): `@raw` "reads nothing and leaves no Loan";
  a raw Place borrow recovers "no owner or existing Loan" (5.2.2, 05-…:73).
- **R-authority** (15.6.2, 15-…:522; 5.2.1 cond. 3, 05-…:54): a Place is written only through a direct path, an exclusive
  reference, or raw access within its authority, which is that of the path the address was taken from. "Past a shared layer
  only reads and shared borrows are possible", except by raw access.
- **R-kept** (5.2.1 Retained dependencies, 05-…:69): the Type owning raw storage must keep the stored values' Origins and Loan
  requirements through a Type argument or a `Loan<T>` Field. The Buffer prose (5.6) says an inserted value "keeps its Loans while
  the Buffer and every value taken or borrowed from it live". 15.3.5 (15-…:319-325): a `Loan<T>` Field counts as storing a `T` for
  slot properties, "names the caller-side Loan to keep and grants nothing".
- **R-contract** (15.6.4, 15-…:594): retention into writable storage is decided per argument by the instantiated public contract.
  "An unproven absence of retention never permits access."
- **S3:243**: raw storage includes a dynamic collection's element storage. Its Loans are the collection's (4.9.6, 04-…:556: added
  inputs' dependencies "join the collection's set"), because Array and Dictionary elements are index steps of the holder.

### 1.2 Verdicts per shape (U is recommended; K is owner question Q1)

| Case (probe) | Rule | Today | U (design A) | K row |
| --- | --- | --- | --- | --- |
| (a) `storeRaw(b@uniq, x@ref)` with `RawBox{a}`, then `x = 5`; `cell[0]` read later (shapes `aCallTwin`) | `b.p` comes from `cell`, a `Raw.allocate` result (untied, R-reach), so σ(b) carries Φ_E; the write reaches E | accepted | error | error (`ref/i32 during a` is not a Type parameter, so not kept) |
| (a) direct twin `q.p[0] = x@ref`; raw-only twin `putRaw(c@uniq,…)` | the same | error | error | error |
| (a′) RawBox passed by value: `storeRawV(b@move, x@ref)` (p1 `rawBoxByValue`) | val(b) = σ(b) ∋ Φ_E | accepted | error | error |
| (b) `putRR(c@uniq, …)`, `c: raw/(raw/(ref/i32))` | raw past raw: both layers are references, σ(c) = {Φ_E} | accepted | error | error |
| (c) `r = c@ref; putRS(r@uniq, …)` | R-authority: a raw pointer read past a shared layer writes with its own authority; the step r → c → Φ_E reaches E | accepted | error | error |
| raw pointer by value `putRV(cell, x@ref)` | refs({Φ_E}) = E | error | error | error |
| `KeptBox<ref/i32>`, `storeKept`, holder dead (`keptDead`) | U: p comes from `Raw.allocate` (Φ_E). K: kept through T | accepted | **error** | valid |
| the same with `cell` aliasing `p` (p1 `keptAlias`) | U as above | accepted | error | accepted (K needs an alias obligation, Q1) |
| Buffer push, buffer dead (`bufferDead`) | U: `Buffer.init(capacity:)` is untied, so σ(buf) ∋ Φ_E (verified in p2 `untiedInit`) | accepted | **error** | valid |
| call store then in-body read `b.p[0]` / `buf.data[0]`, holder replaced, `x = 5`, read used (p3 `keptReadKill`, `bufferReadKill`) | the reads must see what the call stored | accepted | error | error |
| tied raw view `V2{s}` (`data: raw/T`, `r: uniq/T`): `setV2` writes `v.data[0]`, in-body `q = v.data[0]`, `x = 5`, `inspect(q)` (p5 `viewRead2`) | R-reach for reads: q reaches w, which the call stored into | accepted | error | error |
| shared iteration `for v in views`, then `a.value = 3` or `views.append(b@ref); inspect(views)` (kept `views`, p6 `viewsAppend`) | R-authority: `RefRemainder`'s pointer was taken through `ref`, as its `Loan<ref/E>` states | valid | valid | valid |
| exclusive iteration (`viewsUniq`) | `UniqRemainder`'s pointer is stated by its `Loan<uniq/E>` marker | valid (held) | unchanged | unchanged |
| owning iteration over a literal (`viewsOwned`, p6 `ownedAppend`) | `OwnedRemainder` is bare, σ(it) = σ(views) has no Φ_E | valid | valid | valid |
| owning iteration over `Array.init(capacity:)` (p7 `capOwned`) | U: the untied init gives Φ_E at holder granularity (S1-a), so next's self-row reaches E | valid | **error** | valid |
| `Raw.initialize(p, x@ref)` tests (`AnIntrinsicRetainsAsItsDeclarationSays`) | refs({Φ_E}) = E | error | unchanged | unchanged |

### 1.3 Readings and gaps (owner questions; §10)

- **U, the letter.** R-reach, R-holder and R-contract decide it with no new sentence. A raw pointer is a reference that reaches
  what it carries. It carries Φ_E when it comes from raw storage, `Raw.allocate`, an untied call, a parameter, or a holder that
  carries Φ_E (S1-a, holder granularity). The Buffer prose is still true under U, because E keeps the Loans at least that long.
  Cost: `bufferDead`, `keptDead` and `capOwned` become errors. The first two match their in-body twins, which A5 already rejects.
- **K.** R-kept and the Buffer prose read as a carrying rule: values stored in raw storage that a Type keeps through its own Type
  parameter are carried by the keeping value's holder only, as Array elements are. K is a SPEC change, because 15.6 and S1-a
  route these pointers to E. K also needs an obligation sentence. A raw pointer copied out of the keeper, or an alias such as
  `cell` in `keptAlias`, does not see the keeper's Loans, and A5 cannot see them either. Without the sentence, K is unsound
  under copies (§6).
- **Gap G1, storage the pointer's holder owns** (`OwnedRemainder` after `ownStorage(views@move)`; a Buffer under K). The letter
  gives a call's stored values to the holder through R-contract. A read through the raw pointer reaches only what its Loans name,
  which is not the holder itself. Design A closes this conservatively: the raw reach includes the holder (the carrier) that the
  pointer is read from. Q2 asks for the sentence that makes this the letter.
- **Gap G2, authority stated by a marker.** SPEC never says that a `Loan<ref/Y>` Field bounds a raw Field's authority. Design A
  reads it so (R-authority plus R-kept: the Type keeps only a shared Loan of the storage, so a write would exceed the authority it
  keeps). Q3 asks for the sentence. Without this reading, every shared iteration would write its own Loan back into the
  collection, the false-error family the PLAN found.
- **Gap G3, abstract callees** (8.11.4 rule 1, 08-…:789: "only shared past a shared layer"; versus R-authority). Design A adds
  no reach for an abstract part past a shared layer, because generic code cannot dereference an abstract Type as raw. Q4 asks
  the owner to confirm.
- **`@raw` and `IntToPointer`**: these values carry no Loan (R-noloan), and the letter gives them no reach. A5 keeps its present
  reading (A3 E14: "a raw pointer's referent is {Φ_E}") and gives them {Φ_E}. Recorded, not asked.

## 2. Root cause

- **Two reaches for one pointer.** The in-body reach is E, always (`Values.cs:72` Read, `:95-97` Issued, `Flow.cs:132,138`
  Store). W's reach is the holder itself. E is added only for a Loan-free local's first raw layer (`MirTypes.Holding.cs:75-81`:
  `Raw` only for `(false,_,_,true)`; `Values.cs:227`). So a Loan stored by one path is invisible to the other.
  `aCallTwin` (the call) versus `aDirectTwin` (in-body) shows the write side. `viewRead2` shows the read side: W reaches `w`
  through the uniq Field, while the in-body raw read sees only E. It is accepted on dev today and is not in the PLAN. So is
  `keptReadKill`.
- **The walk does not treat a raw pointer as a reference.** A raw pointer "holds none" (`:60`), so a raw local is no holder, and
  `RawBox.init(p: cell, …)` stores Root(cell) = 0 (`Values.cs:52-60`): b loses Φ_E. RawLoan is found only in Own or Exclusive mode
  (`:155`); RawPointee mode returns 0, which is shape (b). Shared references are never walked (`:174-175`, `own &&`), which is
  shape (c), against R-authority. Past an exclusive reference, a raw pointer gives Write, but deeper layers are not walked.
- **Authority is not represented.** No fact says that a raw pointer is read-only (a `Loan<ref/…>` marker), so the only safe
  choice left to W was not to step past raw pointers at all. That is why the naive rule ("E for every holder holding a raw
  pointer") gives the iterator's false errors.
- The same W serves every store path, so captures (2c) and desc(c) (2f), which will call `Writes`, inherit the miss.

## 3. Owner of the decision

- **`MirTypes` (`MirTypes.Holding.cs`)** decides what a value carries and what a call may write past it. This is R4-M73's one
  walk, and every raw-pointer fact belongs there: Loan-capable part, write-capable reference, and authority stated by a marker.
  The walk reads only Types: the case Type and the declaring struct's own Field Types.
- **`MirBorrows`** applies the reach. `Value` gives raw-making rvalues their value. `Read`, `Issued` and `Store` give the reach
  at a Raw step: one rule, computed in one helper. `Writes`/`Write` (rule G) is unchanged except for one deletion.
- **Not Binding.** `RetentionWalk` already emits the rows for all three shapes: raw is writable "unless a raw pointer is
  followed again" (`Binding.Retention.cs:167`, `:203-204`). It does not read Fields (nominal Types, R4-M73 "Why rule G"), so
  which holder lies behind a raw Field is A5's decision. Binding's untied flag already supplies the Φ_E of `Raw.allocate`,
  `Buffer.init` and similar calls (`Binding.Retention.cs:344-386`; probe p2 `untiedInit` versus `tiedInit`).

## 4. Representation

### 4.1 The holding walk (`MirTypes.Holding.cs`)

Places (where a part lies): **Own** (the value's storage), **Exclusive** (storage written past an exclusive reference or a raw
pointer), **Shared** (new: storage past a shared layer, read-only except by raw authority), each optionally with the
**Argument** bit as today. RawPointee is deleted. The walk now uses modes 0, 1, 2, 4, 5, 6, which still fit the `byte` of
`seen`. Facts: **Loan**, **Write**, **WriteByArgument**. RawLoan is deleted. `met` is Loan in Own, Write/WriteByArgument in
Exclusive, and nothing in Shared. As today, `done` is `Loan|exclusive` in Own and the write fact elsewhere.

| Part | Own | Exclusive | Shared |
| --- | --- | --- | --- |
| exclusive borrow, UniqSlice view | met, referent in Exclusive (today) | met, referent in Exclusive (new: deeper layers) | referent in Shared |
| shared borrow, Slice | met, referent in Shared (new) | met, referent in Shared (new) | referent in Shared |
| raw pointer, unstated (not a struct Field, or a bare Field) | pointee in Exclusive without the Argument bit; if that finds a fact, Loan plus that fact | the same, without Loan | the same, without Loan (R-authority) |
| raw Field stated **shared** (§4.2) | pointee in Shared | pointee in Shared | pointee in Shared |
| raw Field stated **exclusive** | nothing; its marker Field is its reference | nothing | nothing |
| pair layer | met, below in Exclusive\|Argument (today) | met, below in Exclusive\|Argument | below in Shared\|Argument |
| abstract part | met \| WriteByArgument (today) | met | nothing (G3, Q4) |
| Function, Closure, part past depth 32 | met \| Write (today) | met | nothing |
| struct / enum / Tuple / array | Fields, payloads and base in the place; a struct's or enum's Type arguments in the place \| Argument (today) | the same | the same |

Results: `Through` = Loan and Write. `ThroughArgument` = Loan and WriteByArgument without Write. Otherwise `None`. A Loan-free
value can no longer have a write fact, because every write path starts at an Own-place reference that is itself Loan-capable.
So **`MirWrites` loses `Raw`** and keeps three members. `HoldsLoans` returns Loan as before. Raw pointers inside Type arguments
stay concrete, as today's RawLoan was: the pointee walk drops the Argument bit, so `Array<raw/(ref/i32)>` is `Through` and not
held. Termination is unchanged: each (Type, mode) is walked once per query, and the answer past depth 32 is yes.

Example rows: `raw/(ref/i32)` gives Loan and Through (today: no Loan, `Raw`); `raw/(raw/(ref/i32))` gives Through;
`ref/(raw/(ref/i32))` gives Through (today None); `ArrayIterator<ref/i32>` gives None; `ArrayOwningIterator<ref/i32>` gives
Through (today None); `ArrayUniqIterator<ref/i32>` stays ThroughArgument; `Utf8Writer` and `raw/u8` stay None.

### 4.2 Authority stated by a marker (G2)

A raw Field f of struct S is **stated** when S declares a Field `Loan<B/Y during o>` whose target Y contains f's declared pointee
P (Y = P, or P is a component or Type argument of Y at any depth). Both are read in S's own terms, from S's own instance
(`Binding.SelfType` Fields; one `AdtDef` accessor), so an instantiation can never make them coincide. The mode is the strongest
such B: `ref`/`objref` are shared, `uniq`/`objuniq` are exclusive. Library rows: `RefRemainder`, `DictionaryRefRemainder` and
`BufferView` are shared; `UniqRemainder`, `DictionaryUniqRemainder` and `UniqSlice` are exclusive; `OwnedRemainder`, `Buffer`,
`KeptBox` and `RawBox` are bare (Storage.kimi:7-43, UniqSlice.kimi:6-8, SPEC 5.6). Pointees of `raw/u8` hold no Loan and are
not concerned. Cost: one scan of S's declared Fields at each raw Field met.

### 4.3 MirBorrows

1. **Holders.** Locals whose Type now holds a Loan through a raw pointer become holders. There is no code change: `Holders()`
   (`MirBorrows.Solve.cs:70-106`) asks `HoldsLoans`. A copy `let p = b.p` therefore carries σ(b) (R-holder), and
   `RawBox.init(p: cell, …)` carries σ(cell) into b.
2. **Raw-making rvalues** (`MirBorrows.Values.cs:22-45`): a raw Ref (`@raw`) and an `IntToPointer` Cast give {Φ_E} instead of 0.
   `Raw.allocate` and other untied results already give Φ_E (`MirBorrows.Calls.cs:66`).
3. **One raw reach** at a Raw step whose input layer is L: the referents are refs(L), meaning the holders L's direct Loans name
   and E where L carries Φ_E or no direct Loan (`Refs`, `Values.cs:133-155`). The pointer's carriers (C) are the holders whose
   state L is: the root for an inline prefix, else refs of the layer before the last borrow step (G1, Q2).
   - Read (`:72`): value := L ∪ Content(L). Today it is {Φ_E}. For a raw local L is {Φ_E}, so the result is {Φ_E} ∪ σ(E): the
     same reach. For `b.p[0]` it now includes σ(b) and what σ(b) names.
   - Issued (`:95-97`): (parents, layer) := (parents ∪ L, L ∪ Content(L)). Today it is (parents ∪ {Φ_E}, {Φ_E}). The Loan
     stays non-direct (`Loans.cs:135`), a fresh anchor (5.2.2).
   - Store (`Flow.cs:132-147`): targets := refs(L) ∪ C. Today it is {E}.
   Loans and paths keep `StepClass.Raw` (`Loans.cs:135`, `Checks.cs:148`), as do `Through` (`Places.cs:80-92`) and Endings.
4. **W** (`Values.cs:164-245`): the algorithm is unchanged. A raw-carrying local is a holder with a kind, so rule G's step from
   it reaches the holders its Loans name and E for Φ_E. **The raw clause is deleted**: the `else if` at `:227-230` and the
   comment at `:164-171` and `:210-211`. Shape (c) needs no new code: the targets loop grows in pass 0, so r → c → E.

### 4.4 Composition

- **R4-M76's per-part guard** (not yet designed). Both read declared Fields. §4.2's accessor is the one R4-M76's walk will use,
  so both use one owning reader. Exclusive-stated raw Fields defer to their marker, so R4-M76's `Loan<uniq/…>` decision (15.3.5:
  it counts as storing a T) covers them. Raw Type arguments are concrete already, in line with "takes no Type argument as an
  exclusive reference".
- **P13's body hold** (R4-M73). The mechanism is unchanged. A concrete raw Write steps in pass 0. A holder that passes the guard
  concretely only through a bare raw Field is no longer held for that store, and its conflicts become Language verdicts of the
  letter. The candidates are generic `ArrayOwningIterator<T>`; the census in §7 lists them. Stated-exclusive pointers keep today's
  held set (`iterated`, `exclusiveElements`, `bumpRefs` unchanged).
- **2e-D's raw-pointer row for defaults** (prototype.md finding 7). Its condition changes from "MirTypes' Raw fact" to "the slot
  Type's `MirWrites` is not None", the same walk. Once F9 gives template calls contract rows, W reaches E for `dc-raw-hid` (a
  RawBox holder) and `dc-raw-hid-x` by itself. The row then remains only for the unknown-callee interim and is deleted with it,
  as finding 7's "(H)" anticipates.
- **Captures (2c) and desc(c) stores (2f)** call `Writes` and inherit everything here.

## 5. Deletions and the sites sharing the cause

Search: `grep -rn "HoldsLoans\|MirWrites\|StepClass.Raw\|escapedSet" src/Kimi/Compiler/Mir`. `MirWrites` and `HoldsLoans` are
consumed only in `Values.cs:14-16,161,225-227`. `StepClass.Raw` is consumed in `Values.cs:72,95`, `Flow.cs:118,132,138`,
`Loans.cs:135`, `Checks.cs:148` and `Places.cs:32,85`.

| Site | Same cause? | Change |
| --- | --- | --- |
| `MirTypes.Holding.cs:8-25` `MirWrites.Raw` | yes | deleted |
| `:30-42` RawPointee mode, RawLoan fact; `:75-81` switch arm; `:153-156` raw case; `:174-175` `own &&` | yes | §4.1 table |
| `Values.cs:227-230` raw clause | yes | deleted |
| `Values.cs:72`, `:95-97`; `Flow.cs:132-143` | yes (read and write side) | one raw reach (§4.3.3) |
| `Values.cs:30` raw Ref value; IntToPointer default | yes (lost provenance) | {Φ_E} |
| `Loans.cs:135`, `Checks.cs:148`, `Places.cs:85`, `Endings.cs:80` | no: paths and fresh anchors (5.2.2) | unchanged |
| `Calls.cs:55` | consumer of W | unchanged |
| `Binding.Retention.cs:150-205` | no: rows already exist | unchanged |
| A3 E16 raw clause, R4-M73's `Raw` bullet, STATUS:49, ARCH:110,120 | records | superseded or updated (§9) |

Net: one enum member, one mode, one fact and one W branch are deleted. The three Raw→E shortcuts become one helper.

## 6. Options

**(i) Inside the owning component: a W-only fact.** Add "Escape" to `MirWrites`: E whenever W's walk passes a raw pointer whose
pointee may hold a Loan, exempting stated-shared Fields. In-body handling stays E.
- Cost: about +20 lines. It fixes (a)–(c) for calls.
- Rejected:
  - `viewsOwned`: `OwnedRemainder` is bare, so next's self-row would put the elements' Loans into E. That is a false error,
    which the letter (tied provenance) does not give and AGENTS forbids. Avoiding it needs a "kept" exemption, which is reading K.
  - With that exemption, reads still miss what calls put in the keeper: `keptReadKill`, `viewRead2` and copies.
  - It keeps two reaches, which is the root cause itself.

**(ii) A change of shared structure (chosen): raw pointers are references.** Facts in the walk, holders, one reach at a Raw
step, and rule G unchanged.
- Cost: about +45/−30 compiler lines; more holders (raw locals with Loan-capable pointees); deeper walks.
- Risk: verdict changes under U (§1.2) and held-view changes (§4.4). Both are measured by the census before landing.
- It removes four special cases and needs no new vocabulary.

**The PLAN's framing, "a raw pointer whose authority a `Loan<>` marker states, against a bare one".** It is half right:
- A marker states authority. Shared means nothing is written past the pointer; exclusive means the marker's Loan is the reach.
  §4.2 keeps this.
- But "bare means E" is wrong. `OwnedRemainder` is bare, and E would give owning iteration false errors.
- A bare pointer reaches what it carries: E exactly where its provenance (Φ_E at holder granularity) says so, the owner's
  holder, or the holders its Loans name.
- Under K, the question becomes kept versus unkept (a Type-parameter pointee), not marker versus bare.

**Also rejected:**
- The naive E for every holder holding a raw pointer: the PLAN's iterator false errors.
- Visibility: kept and inaccessible storage would belong to the keeper. It is more precise than the letter (it accepts
  `bufferDead` from another module, which the letter rejects) and makes verdicts depend on module boundaries.
- A new Loan-set element "Φ_R" for raw provenance: a vocabulary entry, and finer than S1-a's holder granularity, which the
  owner fixed (R4-M76 rejected Φ_E per part).
- Per-part provenance: the same objection as Φ_R.
- K now, by fiat: a SPEC change, and unsound without an alias/copy sentence (`keptAlias`, and copies taken before a store).

## 7. Tests

New `MirBorrowsTest` fact, `ARawPointerReachesWhatItCarries` (SPEC 15.6, 15.6.2, 5.2.1, 5.4; DECISIONS R4-M8x). It reuses the
Prelude and avoids `Raw.initialize(cell, y@ref)` noise.
- **Errors** (each fails before the change):
  - aCallTwin, bRawRaw and cRawShared: `x = 5`.
  - rawBoxByValue.
  - keptAlias, keptReadKill, bufferReadKill and viewRead2.
  - keptDead, bufferDead and capOwned (U).
- **Valid counterparts:**
  - `views`, `viewsLater` and `viewsAppend` (the marker's authority; fails under (i) without §4.2).
  - `viewsUniq` (held, unchanged) and `viewsOwned`.
  - `ownedAppend`.
  - A tied raw view written in-body with its source dead (`v.data[0] = x@ref; x = 5`): now valid by the letter (refs(L) = {w}),
    an error today.
- **Kept errors:** rawValue, aDirectTwin and rawInit.
- **Invalid counterpart per rule:** cRawShared versus `cRawSharedLive`; Buffer with `inspect(buf)`.

`MirTypesTest`:
- `HoldsLoans`: row `raw` changes from false to true.
- `TheGuardSaysWhatACallMayWritePastAValue`: `raw` changes from `Raw` to `Through`. New rows: `rawRaw`, `sharedRaw`
  (`ref/(raw/…)`), `iterRef: ArrayIterator<ref/i32>` (None), `iterOwned` (Through), `iterUniq` (ThroughArgument), and a
  bare `Box<T>{p: raw/T}` (Through).
- These changes are not weakening. Each turns a false negative into the letter's answer: a raw pointer read from a part carries
  its holder's Loans (15.6) and writes within its authority (15.6.2).

Existing `MirBorrowsTest` expectations: `rawPointee` (b = 3) and `AnIntrinsicRetainsAsItsDeclarationSays` are unchanged, as
traced in §1.2. Any held view that changes from `Unsupported_Kd` to a Language problem is listed in its commit with the letter
verdict.

Scaling and allocation:
- Add a `TheWorkScalesWithTheProgram` row: N raw holders in a chain `pN = p(N-1)@ref`, followed by `putRS`-style stores
  (dense = false).
- `AWarmRunAllocatesNothing`: add a body with a raw holder and a stated iterator. The walk allocates nothing once `walked`
  has grown.
- `MirCensusTest`: library, 44 milestones and 31 goldens with no A5 problem (R4-M12). The A5 classes: `MirBorrowsTest`,
  `MemberThroughLayersTest`, `BlockEscapeLoanTest`, and the pair-cases and IntegerRange runners that pin held views.

## 8. Size, speed, unverified

**Size** (estimated):

| File | Change | Lines |
| --- | --- | --- |
| `MirTypes.Holding.cs` | walk rows and §4.2 | +30/−22 |
| `AdtDef.cs` | declared Field accessor | +3 |
| `MirBorrows.Values.cs` | Value +2; reach helper +12; Read and Issued arms −4; raw clause −4 | net +6 |
| `MirBorrows.Flow.cs` | Store reach | +6/−3 |
| tests | | +100/−4 |
| docs | DECISIONS +20; ARCH, STATUS and PLAN rows | ±8 |

**Speed:**
- Per local: the walk now also visits Types behind shared layers and deeper exclusive layers. Each (Type, place) is still
  visited once: at most 6 visits against today's 8 modes, with the same early exit.
- Per W: unchanged; the targets come from holders' existing sets.
- Per Raw step: one Refs or Content, memoized per location (E13 (5)).
- Holder count grows by the raw locals with Loan-capable pointees: rare in user code; in the library, `OwnedRemainder.drop`
  and `FixedOwnedRemainder.drop`, and Dictionary's `keyAt`/`valueAt` results.
- Measure: A5 work and bytes on MirBorrowsTest's shapes, corpus work against dev at the census, and the
  `--compiler-pipeline` mir stage (pinned, interleaved A/B).

**Unverified:**
- The census result.
- The held-view delta (§4.4).
- Speed.
- The owner's answers to Q1–Q4.
- Whether any milestone contains a Buffer-like or untied-collection owning iteration that U turns into an error (search
  `Array<…>.init(capacity:` followed by `@move` iteration over Loan-holding elements).
- The default pipeline is out of scope: STATUS keeps its limit for that path.

## 9. Units, records

**Units:**
- **G160-0 (behavior-preserving, optional).** Put the walk's layer handling into the §4.1 table form with today's rows (Shared
  maps to "not walked", raw as today) and add the declared-Field accessor. Checks: `MirTypesTest`, `MirBorrowsTest` and
  `MirCensusTest` unchanged. This lets G160-1 change rows, not add cases.
- **G160-1 (behavior, full process).** §4.1–4.3 together, because a W that steps past raw pointers without the read reach would
  widen the read-side gap. Checks:
  - the unit verification of `MirTypesTest`, `MirBorrowsTest`, `MirCensusTest` and the A5 classes;
  - the measurements above and the allocation regression;
  - a Session at the end.
  It records DECISIONS **R4-M8x** (next free):
  - the representation: raw pointers are references for carrying and W, authority stated by a `Loan` marker, and one raw reach;
  - the reading U, the gaps G1–G3 and the owner's answers;
  - deletions: `MirWrites.Raw`, RawPointee, RawLoan and the raw clause;
  - rejected options (§6);
  - supersedes the raw clause of A3 E16 and R4-M73's `Raw` bullet. A3 §12 gets erratum E17 naming it.
- **G160-K (only if the owner chooses K).** A `kept` classification in §4.2: a bare Field `raw/P` with P a Type parameter of its
  struct. Its pointee is walked in the parent place as own storage (no write fact), and its raw Deref reaches only its carriers.
  It needs the owner's SPEC sentences (Q1).

**Documents:**
- PLAN: remove the G160 row.
- STATUS:49: A5 now checks Loans stored through raw pointers; the default pipeline's limit stays.
- COMPILER_ARCHITECTURE:110,120: `HoldsLoans` counts raw pointers; W's raw reach; no raw clause.
- CODEMAP: unchanged.

## 10. Owner questions

- **Q1. K or U** for raw storage a struct keeps through its own Type parameter (Buffer, KeptBox, OwnedRemainder).
  - Recommended: U, the 15.6 letter, sound without a new sentence. Under U, `bufferDead`, `keptDead` and `capOwned` are errors.
  - K makes them valid. It needs two SPEC sentences: 5.2.1 (kept raw storage carries its values' Loans in the keeper's
    holders) and an alias obligation (a raw pointer not read through the keeper sees none of them). `keptAlias` stays accepted
    under K.
- **Q2. Storage a pointer's holder owns** (G1). Proposed 15.6 sentence: "A read or write through a raw pointer also reaches the
  holder its pointer is read from." A5 implements it as the carrier term.
- **Q3. Authority stated by a `Loan` marker** (G2). Proposed 5.2.1 sentence: "A Type that keeps only a shared Loan of the storage
  a raw Field addresses grants no write through it."
- **Q4. Abstract callees** (G3). Confirm that 8.11.4 rule 1's "only shared past a shared layer" holds for raw pointers hidden in
  an abstract part, so that A5 adds no reach there.
