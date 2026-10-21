# G160 design B: raw storage a declaration keeps, and raw storage nobody keeps

Designer B, 2026-10-21, against origin/dev 08145aa2 (worktree g160-plan, read-only). Probes: `scratchpad/g160/b/p1..p5.kimi` with
their `.out` files (A5 problems on the built g160-probe binary; each line `function: first line of the access`).

## 0. Summary

- **Reading.** SPEC 15.6 says that a raw pointer read from raw storage, from a parameter or from an untied call result reaches
  *storage the body does not own*, where a stored value "may be used after every point" (E). SPEC 5.2.1 makes one exception: raw
  storage that a Type *keeps through a Type argument* holds the Loans of its values in the Type's holder. The `Buffer` example says
  "keeps its Loans while the Buffer ... live". The distinction that decides G160 is therefore **kept by its declaration** against
  **kept by no declaration**. The `Loan<>` marker is not that distinction (§1.4).
- **Root cause.** MirTypes' holding walk reads instantiated Field Types. There a raw layer no longer shows which declaration wrote it
  or whether its pointee is that declaration's Type parameter. So `MirWrites` has only `Raw`, "a value that holds no Loan has such a
  raw pointer", and the walk drops every raw layer it cannot classify (§2).
- **Representation.** A per-declaration fact in MirTypes, `Keeping(D)`. It records whether D's declared storage writes a raw layer it
  does not keep whose pointee may hold a Loan (`Bare`), and in which routes (own, past an exclusive or a shared layer, in bare raw
  storage) each Type parameter occurs. MirWrites becomes flags, where **`Unowned`** replaces `Raw` and is independent of the guard.
  W gets E from the Type of the operand a call stores through, and in P13's second pass from the holders it reaches. The raw clause
  is deleted (§4, §5).
- **Owner questions.** Q1: does an Origin slot that the declaration keeps through a stored reference or a `Loan<T>` Field also keep
  a raw pointee? The default here is no, which makes (a) a defect. Q2: a raw write past a shared layer into raw storage that a
  nominal Type hides has no retention row in Binding (a new shape (d)).

## 1. Required meaning

### 1.1 The rules

- **R1. Storage the body does not own** (15-ownership:454). A reference reaches it "when it comes from a parameter or capture, from
  static or raw storage, or from a call result that its contract ties to no argument, or derives from such a reference". A value
  derives when it is "a value read from a part of a holder that holds one" (:453, R4-M76 S1-a). "A value stored there may be used
  after every point." S3:164: *reference* includes `raw`.
- **R2. A call writes like a reference** (:454, 15.6.4). "A call writes the storage its contract lets it write the same way". This
  is rule G (R4-M73): the store reaches every holder its argument reaches.
- **R3. Raw access past a shared layer** (15.6.2, :522). A Place is written "only through a direct path or an exclusive reference,
  or by raw access within its authority (§5.2.1)". Past a shared layer only reads and shared borrows are possible. Binding's
  `RetentionWalk` follows this for a raw layer that is visible in the Type (Binding.Retention.cs:161-208, the comment at :161-169,
  `raw` at :203-204).
- **R4. Kept raw storage** (05-raw:69, :191). "A value moved into raw storage keeps its Origins and Loan requirements; the Type
  owning the storage must keep them through a Type argument or a `Loan<T>` Field (§15.3.5)". `Buffer<T>`: "Because `T` appears in
  `Buffer<T>`, an inserted value keeps its Loans while the `Buffer` and every value taken or borrowed from it live". The element
  storage of a dynamic collection is raw storage too (S3:243). A5 already counts it as part of its holder: HoldsLoans walks a Type
  argument "which its storage may hold behind a raw pointer" (MirTypes.Holding.cs:56-60).
- **R5. Per declaration** (15.3.5:321). Slot properties are computed "once per declaration, in terms of its own Type parameters and
  slots, never by expanding instantiations".
- **R6.** `@raw` "leaves no Loan" (5.4:101). A raw pointer supplies no Loan proof (5.2:38). Loan liveness follows safe values alone
  (5.2.1, condition 4).

R1 and R2 make every raw pointee that a call may write E. R4 carves out one exception: raw storage whose declaration keeps it.
Whether a raw layer is kept is a property of **the declaration that wrote it** (R5). Raw layers that the body itself writes (a
local's Type, a Tuple, a Type argument the body supplies) and raw pointers read out of raw storage are kept by no declaration.

### 1.2 Verdict per shape

Default reading R-T (§1.3). "Today" is A5 on the probe binary.

| Shape | Rule | Required | Today |
| --- | --- | --- | --- |
| (a) `storeRaw(b@uniq, x@ref)`, `RawBox{a}.p: raw/(ref/i32 during a)`, then `x = 5` | R1, R2: `p`'s pointee names slot `a`, not a Type parameter, so it is not kept | error | accepted |
| (a) direct twin `q.p[0] = x@ref` | R1, an in-body raw store (Flow.cs:132,143) | error | error |
| (a) raw-only twin `putRaw(c@uniq, …)` | R1 | error | error |
| (b) `c: raw/(raw/(ref/i32))`, `putRR` | R1: a raw pointer read from raw storage | error | accepted |
| (c) `r = c@ref`, `putRS(r@uniq, …)` | R3, R1 | error | accepted |
| Tuple holder `t: (raw/(ref/i32), isize)` or `(raw/…, ref/i32)` (p2 `tupleHolder`) | R1: no declaration | error | accepted |
| By-value holder `byValue(b@move, x@ref)`, `RawBox` (p2 `valueBox`) | R1, R2 | error | accepted |
| Raw pointer by value `putRV(cell, …)` | R1 | error | error |
| `MarkBox{a}`: `raw/(ref/i32 during a)` beside `Loan<ref/i32 during a>` (p1) | R-T: not kept; R-S: kept | R-T error | accepted |
| `for v in views`, then `a.value = 3` (Array, ArrayIterator, RefRemainder `raw/E`) | R4: kept by the Type argument | valid | valid |
| `for v in views@uniq` (UniqRemainder, `Loan<uniq/E>`) | R4; writes go to the source (rule G) | valid; P13's hold unchanged | valid |
| Owning iteration (`OwnedRemainder<E>`, `raw/E`, no marker) | R4 | valid | valid |
| `Buffer<ref/i32>.push`, then `x = 5` with the buffer dead (kept.kimi) | R4, the SPEC example | valid | valid |
| the same with the buffer used later | R4 | error | error |
| `KeptBox<T>{p: raw/T}` built from a pointer, then the buffer dead (kept.kimi, p1 `keptAlias`) | R4. A later aliased read is the unsafe reader's breach of R4's obligation | valid | valid |
| `TupBox<T>{p: (raw/T, isize)}`, `Outer<V>{w: Wrap<raw/V>}` (p5) | R4: written by the declaration, pointee its parameter | dead: valid; live: error | the same |
| (d) `putSB(b@ref, x@ref)` writing `b.p[0]` past `ref/RawBox` (p1), `pushShared(b@ref, …)` into `KeptBox`'s storage (p4) | R3: within authority, so a row is needed | error (Q2) | accepted, because Binding gives no row |

### 1.3 Two readings of R4 (owner question Q1)

- **R-T (default).** "Through a Type argument": a raw layer is kept when its declaration wrote it in its own storage and its pointee
  holds a Loan only through that declaration's Type parameters (or their projections). On this reading 5.2.1's `Loan<T>` Field keeps
  the Loan of the storage's *owner*, which is how `BufferView{source}` and the remainders use it. (a) and `MarkBox` are then defects.
  - Principle 1 holds: kept raw storage has one canonical form, a Type parameter pointee, which every library collection already
    uses.
  - Principle 2 holds: whether a Field keeps its storage is read from that Field alone.
  - It matches A5's approved holder model (R4-M36: a Type argument's storage behind a raw pointer is a part of the holder; a raw
    pointer holds none).
- **R-S.** 15.3.5: "A `Loan<T>` Field counts as storing a `T` for all slot properties". On this reading a pointee whose borrows name
  only slots the declaration keeps is kept too. A slot is kept when a stored safe reference or a `Loan<T>` in the declaration's own
  storage names it, that is, when the slot is non-Phantom.
  - `RawBox` (kept through `r`) and `MarkBox` are then kept, so (a) is no defect.
  - (a)'s direct twin still errs, by A5's conservative in-body raw store, which is a precision gap and not a soundness gap.
  - The meaning of `p` then depends on another Field.
- **Decision needed.** R-T and R-S differ only for a raw pointee that names an Origin slot. In the design this is one predicate row
  (`Kept`, §4.2). The owner chooses before U2 lands, so A5 never reports a Language error on a form the chosen reading makes valid
  (AGENTS: Diagnostics). On either reading, adding a sentence to 5.2.1 would remove the ambiguity. That sentence is reported here as
  a gap; this design does not write it.

### 1.4 The PLAN's framing ("a `Loan<>` marker against a bare raw pointer")

The marker is neither necessary nor sufficient for keeping:

- `Buffer<T>`, `OwnedRemainder<E>`, `KeptBox<T>`, `TupBox<T>` and `Outer<V>` keep their storage with no marker. A marker rule would
  send them to E: owning iteration and a dead Buffer would get false errors.
- A marker beside a pointee that names an Origin slot (`MarkBox`) is exactly the open question Q1.
- What a marker does state is **authority**: `Loan<ref/E during source>` grants no write past it, and `Loan<uniq/E …>` grants one
  into the source's storage. HoldsLoans already reads that, because a `Loan<uniq/…>` is walked as an exclusive reference and gives
  the guard. Authority matters where Binding must decide whether a raw write past a shared layer is part of a contract (Q2), not
  where A5 decides between E and a holder.

### 1.5 Shape (d) (owner question Q2)

`RetentionWalk` marks writable storage past a shared layer only "unless a raw pointer is followed again" (Binding.Retention.cs:167-168). It never reads
Fields (TyCtxt.Retention.cs:39-41), so a raw Field inside a nominal Type reached past `ref` is invisible.

- p1 `sharedBoxLive` and p4 `keptShared` are accepted with no row at all. In `keptShared` a safe caller observes the stale
  reference through a safe accessor.
- 15.6.2 allows the write. 8.11.4 rule 1 ("only shared past a shared layer", 08:789) forbids it for abstract callees.

**Q2.** Either Binding gives rows for slots and parameters under a raw layer that a nominal Type reaches past a shared layer, with
the authority a `Loan<ref/…>` marker states excluding them. Or SPEC states that a public contract never lets a callee write a
nominal Type's raw storage past a shared layer, which makes that write the unsafe author's breach.

Either way the owner is Binding's retention: a different phase with a different contract. It is a separate PLAN issue and unit
(U3), not this design's representation.

## 2. Root cause

- A5's E-or-holder choice for a raw pointee rests on a fact the representation lacks: **which declaration wrote a raw layer, and
  whether it keeps the pointee**. HoldsLoans walks instance Types (`AdtDef.FieldType` substitutes; Holding.cs:189-199), where
  `raw/T` with `T = ref/i32` looks like `raw/(ref/i32 during a)`.
- The walk therefore produces `RawLoan` only in Own and Exclusive mode (:153-156). It returns 0 for a raw pointer met in a raw
  pointee (b) and never walks a shared referent (:174-179, shape c). Past an exclusive layer it turns a raw pointer into the guard
  (`Write`) rather than E.
- `HoldsLoans` keeps `RawLoan` only where the value holds no Loan (`(false,_,_,true) => Raw`, :75-81). It must, because for a holder
  it cannot tell an iterator's kept `raw/T` from `RawBox.p`.
- W's raw clause (Values.cs:227) consumes `Raw` for a local that holds no Loan and that a direct Loan names.
- Every symptom is that one lack: (a), the Tuple holder and the by-value holder (a holder's own raw), (b) (chains), (c) (past
  shared), and the naive rule's three false errors (kept storage taken as bare). Shape (d) is the same lack in Binding's contract
  walk (§1.5).

## 3. Owning component

- **What a value's storage may reach past it is a Type fact.** It belongs to `MirTypes` (COMPILER_ARCHITECTURE:110: "Type facts in
  MIR"). R4-M73 put the guard there and R4-M76 places the per-part guard there ("reads a generic Type's Fields").
- `MirBorrows.Writes` consumes the fact. It never decides by Type syntax.
- Binding's retention decides which outputs a contract writes (15.6.4) and stays unchanged here, except for (d) after Q2.
- One decision, one owner: the keeping judgment of a raw layer is made once, per declaration, in MirTypes.

## 4. Representation

### 4.1 Vocabulary

`MirWrites` (Holding.cs:10-25) becomes a flags byte. No new vocabulary family is added.

| Member | Meaning | Change |
| --- | --- | --- |
| `None` | nothing past it | same |
| `Through` | the guard, passed concretely | same |
| `ThroughArgument` | the guard, passed only through a Type argument, a pair layer or an abstract part (P13) | same; never set together with `Through` |
| `Unowned` | a call writing it may write, by some route, into a raw pointee that no declaration keeps and that may hold a Loan (E) | replaces `Raw`; independent of the Loan fact and of the guard |

Walk facts (Holding.cs:39-42): `Loan`, `Unowned` (was `RawLoan`), `Write`, `WriteByArgument`.

Places (2 bits): Own, RawPointee, Exclusive and **Shared**. Shared takes value 3, which today means "raw pointee past exclusive".
Mode bits: `Argument` (as today) and **`InDeclaration`** (inside a declaration's stored parts). That makes 16 modes, so the `seen`
values widen from byte to ushort. Domination holds: a concrete mode dominates its Argument twin (as today), and a mode outside a
declaration dominates its InDeclaration twin.

### 4.2 `Keeping(D)`, per declaration (new file `MirTypes.Keeping.cs`)

For a struct or enum declaration D, read from its self Type's declared parts (`AdtDef.FieldType`, `CaseTypes` and `Base` on
`Binding.SelfType`, so in D's own parameters):

- `Bare`: D's declared storage, by any route, writes a raw layer that D does not keep and whose pointee holds a concrete
  Loan-capable part. A concrete part is a borrow or pair layer, a Slice, a callable, an abstract part that is not D's, or a nested
  instance whose `Concrete` is set.
- `Routes[k]` for each Type parameter `T_k`: the routes where it occurs, a subset of {Own, Exclusive, Shared, Bare}. The blanket
  Own rule of R4 stays: every parameter also occurs Own|Argument, as the component walk does today.
- `Concrete`: D's storage holds a Loan-capable part other than through its parameters. `Kept` reads it.
- `Projections`: (part, component path, route) for each projection of D's parameters. The list is empty for every library
  declaration.

**DFS.** The DFS starts in route Own. A reference or handle changes the route: `uniq` or `objuniq` gives Exclusive; `ref`,
`objref`, Slice, `rc`, `arc` and `Weak` give Shared; an `obj` payload stays Own. Then:

| Met | Action |
| --- | --- |
| a parameter `T_k` | `Routes[k] |= route` |
| a projection of D's parameters | record it in `Projections` with its path and route |
| `raw/P` | if `route == Own && Kept(P)`: walk `P` in Own (kept storage is D's own); else walk `P` in Bare |
| in Bare, a concrete part | `Bare = true` |
| a nested instance `E<X>` | `Concrete |= E.Concrete`; walk each `X_j` in `compose(route, r)` for every `r` in `E.Routes[j]` |

`compose` works as follows:

- Own∘r = r.
- Exclusive∘Own = Exclusive, and Exclusive∘Exclusive = Exclusive.
- Shared∘(Own, Exclusive, Shared) = Shared.
- Anything followed by Bare is Bare.
- Bare∘Shared = Shared. A shared reference stored in bare storage still grants no write, and a raw pointer past it is Bare again.

E's own `Bare` is not copied into D, because the instance walk applies E's `Bare` where it meets E.

`Kept(P)`: P holds a Loan only through D's parameters or their projections. P has no concrete part by any route, and each nested
instance in P has `!Concrete` and arguments that are themselves `Kept`.

- R-S adds a row: or a borrow layer whose Origins are slots D keeps. That needs a slot mask, the slots that a safe borrow or a
  `Loan<T>` in D's own route names, mapped through nested instances' Origin arguments.

Examples:

- `RefRemainder<E>`, `UniqRemainder<E>`, `OwnedRemainder<E>` and `UniqSlice<T>`: `raw/E` is kept and `Bare` is false. The
  `raw/u8` Fields of the Dictionary remainders, the writers and `FixedBuffer` hold no Loan.
- `RawBox{a}`: `Bare` is true.
- `TupBox<T>` and `Outer<V>`: `Bare` is false.
- `S<T>{u: uniq/(raw/T)}`: route Exclusive, so not kept; `T` gets the Bare route. That pointer lies in another holder's storage,
  which matches today's E through the raw clause.

**Fixed point and cache.**

- The summaries of mutually recursive declarations start empty and are recomputed until none changes, then cached. Each summary is
  a few bits per parameter, so this takes at most a few rounds.
- A growing instance such as `Nest<(T, T)>` maps parameters to parameters, so it adds no nodes (R5).
- The cache is part of the walk's scratch object, which MirBorrows owns beside `walked` (Values.cs:12). It is cleared when `Run`
  meets a new program version (MirAnalysis keeps one MirBorrows per compilation), so warm runs allocate nothing.

### 4.3 HoldsLoans' instance walk (Holding.cs:131-200)

Every Type the walk starts from is written by the body: a local's Type, an operand's Type, or a Type argument the body supplies.

- **A raw layer outside a declaration** walks its pointee in RawPointee from any place. That covers chains (b), past exclusive
  (`Unowned` where `Write` was), and past shared (c).
- **A raw layer inside a declaration** (`InDeclaration`) gives 0. Its author's `Bare` decides it. A substituted Type argument
  reaches the body-level walk through the blanket component walk. Because InDeclaration is part of the memo key, the order in which
  the walk meets a Type cannot hide a body-level raw layer.
- **RawPointee**: a Loan-capable part gives `Unowned` (met). A raw pointer continues in RawPointee.
- **A shared reference or a Slice** walks its referent in **Shared**. In Shared only raw pointers (to RawPointee), `Bare` instances
  and projections count. Loan-capable parts give nothing there, as do exclusive references, whose referents are walked in Shared.
  The walk stops once it has `Unowned`.
- **An instance `D<args>` in mode m**:
  - `Bare` gives `Unowned`;
  - each `k` with Bare in `Routes[k]` walks `args[k]` in RawPointee;
  - each projection is resolved along its path in `AdtDef.FieldType(instance, part)` and walked at body level in `compose(m,
    route)`;
  - the components are walked as today, in m|Argument when own;
  - Fields, payloads and the base are walked as today, in m|InDeclaration.
- **The switch at :75-81** becomes guard (from Loan, Write, WriteByArgument, unchanged) `| (Unowned ? MirWrites.Unowned : 0)`.
- **Unchanged**: the Loan fact (a raw pointer still holds none, and no new parts are added), the guard for every non-raw part, and
  the depth bound for instances.

### 4.4 W (Values.cs:172-245)

`Writes(state, set, through)`, where `through` is the MirWrites of the operand that a call stores through into output j:
`writable[local]` for a whole-local operand (the usual MIR temp), otherwise HoldsLoans of the operand's Type.

1. refs(S) and rule G's two passes, unchanged.
2. **E when `through` has `Unowned`.**
   - The Type walk follows every route a call may write by: own parts, exclusive referents by their Types, raw pointees, and raw
     pointers past shared layers.
   - It is exact per route, so E does not depend on which Loans the holders carry. A reborrow of `b.r` passed alone does not add E;
     `b@uniq` does.
3. **E when a holder or root local reached in the second pass has `Unowned`.** A Type argument, pair layer or abstract part hides
   that route from the operand's Type. That E lies past `clean`, so a non-empty value holds the view (P13), never silently.
4. **Deleted**: the raw clause in `Write` (:227-230) and its doc text (:164-171, :210-211). `Written` and the passes compare only the
   guard bits.

Every store path passes `through`: a Call's rows (Calls.cs:53-56, which passes the operand of `row.Output`); A5-F7's capture stores
(2c), with the capture's Type; and A5-F6's desc(c) stores (2f), with the receiver operand.

### 4.5 Composition

- **R4-M76's per-part guard.** `Keeping` is the per-declaration reading of a generic Type's Fields that R4-M76 asks for:
  `Routes[k]` already tells whether `T_k` lies past an exclusive layer in D. The per-part guard adds its own fact to the same
  summary and cache rather than adding a second reader. G160 does not decide that fact.
- **P13's body hold.** It is unchanged: rule 3's E is a second-pass target, and rule 2's E is concrete (pass 0).
- **2e-D's raw-pointer row** (prototype.md:1764-1774). Its condition becomes `Unowned` of the slot's Type, the same fact. Where the
  unknown-callee rows store into an exclusive input, rule 2 already gives E. A5-F9, which gives template calls their contracts, gives
  a by-value raw slot a store row and deletes the special row.
- **In-body accesses.** `Flow.Store` (:113-148) and `Read` and `Issued` (Values.cs:63-102) still send every raw dereference to E or
  {Φ_E}. That is sound under both readings, and is now consistent with calls under R-T.

## 5. Deletions and sites sharing the cause

The code search covered `HoldsLoans`, `MirWrites`, `RawLoan`, `StepClass.Raw`, `Writes(`, `RetentionWalk` and `raw` in
MirBorrows, MirTypes and Binding.

| Site | Same cause? | Action |
| --- | --- | --- |
| Values.cs:227 raw clause; Holding.cs:15-16 `Raw` and :79 | yes | deleted; replaced by rule 2 and `Unowned` |
| Holding.cs:155 (`place is Own or Exclusive`), place 3's raw-past-exclusive `Write` | yes (b) | replaced by the RawPointee and Shared rules |
| Holding.cs:176-178 (shared referents not walked) | yes (c) | Shared place |
| Holding.cs:189-191 (raw Fields judged by their instance Types) | yes (a), naive-rule false errors | InDeclaration and `Keeping` |
| Calls.cs:53-63 (W per output, by-value operand) | yes (valueBox) | passes `through` |
| 2c capture stores and 2f desc(c) stores (planned) | yes (same W) | pass `through` |
| 2e-D raw-pointer row (planned) | yes | condition = `Unowned` |
| Binding.Retention.cs:170-208 past a shared layer at a nominal Type | yes, in another phase (d) | Q2, unit U3 |
| Flow.cs:132,143 and Values.cs:72,95 (raw step to E) | no (conservative, sound) | unchanged |
| `HoldsRaw` (Holding.cs:50-123; builder's `confined`) | no (authority for effects) | unchanged |
| Default pipeline (old Analysis; STATUS:49) | yes (keeps no Loan for a raw store) | frozen until R6-check, when A5 decides |

What else becomes unnecessary: E16's sentence on a raw-only local (design-A3.md:759-763), and the rule that "a store row into a
raw-pointer input reaches E because its value has no direct Loan" (E14). That rule now follows from `Unowned` of the input's Type,
and refs(∅) = E remains only for Φ_E.

## 6. Options

| Option | Content | Cost | Risk | Verdict |
| --- | --- | --- | --- | --- |
| (i) Inside the owner, instance walk | Keep instance Field walks. Treat a Field whose declared Type is `raw/P` with `Kept(P)` as kept, and every other raw layer as bare; add chains and the Shared place. | +50/−20 | `TupBox` and `Outer` (valid and accepted today, p5) get false E, or a hold; an instance Type met inside and outside a declaration needs authorship in the memo; R4-M76 later adds a second reader of generic Fields | rejected: a clause for one form (AGENTS: Symptom fixes) |
| **(ii) Per-declaration `Keeping` in MirTypes plus `Unowned`** | §4 | +~170/−~40 code | the summary's fixed point; HoldsLoans' signature changes (test scratch) | **chosen**: R5's per-declaration principle; exact for every shape in §1.2; the substrate R4-M76 needs |
| (iii) Shared structure across phases: Binding publishes the keeping fact (AdtDef/TyCtxt) for A5 and for (d) | one fact, two phases | +~200 | Binding would need A5's Loan-capability vocabulary (callables, pair cases per view, abstract parts); (d) needs another contract (slots under raw past shared, with marker authority) | rejected: a similar shape under another contract stays separate. (d) gets its own Binding fact (U3) |
| (iv) Value flow: raw values carry Φ_E; E iff σ(h) carries Φ_E | uses S1-a | raw-only locals become holders | `x@raw` carries nothing (R6), so this is unsound unless every raw value carries Φ_E. It then errs on iteration over an Array holding a parameter-derived reference (σ(it) ∋ Φ_E), because a holder's Φ_E does not say which part carries it | rejected |
| (v) PLAN's marker rule | E unless a `Loan<>` marker sits beside the raw Field | small | false errors for Buffer, owning iteration, `KeptBox`, `TupBox` and `Outer` (§1.4) | rejected; the marker matters for Q2 |
| (vi) Naive rule: E for any W holder whose Type has a Loan-capable raw pointee | small | iterator false errors (brief) | | rejected |
| (vii) Every raw pointee E (15.6's letter without R4) | smallest | contradicts the Buffer example and the library | | rejected |

## 7. Tests

**Regressions** (MirBorrowsTest, new `ARawStoreReachesEUnlessItsDeclarationKeepsIt`, Prelude plus RawBox, MarkBox, KeptBox, TupBox,
Wrap/Outer and Buffer; no `Raw.initialize` of a second local, so E's own entries add no `var y` lines).

- Each of these fails today and passes after the change: `aCall: x = 5`, `rawChain: x = 5`, `rawShared: x = 5`,
  `tupleHolder: x = 5`, `byValue: x = 5` and, under R-T, `marker: x = 5`.
- Unchanged errors: `rawOnly: x = 5`, `rawValue: x = 5`, `bufferLive`, `keptLive`, `tupKeptLive` and `outerLive` (`x = 5`).
- Valid and expected empty: `views`, `viewsUniq`, `viewsOwned`, `bufferDead`, `keptDead`, `tupKeptDead`, `outerDead` and
  `reborrowedPart` (`put(b.r@uniq, x@ref)` with b dead), which shows rule 2's per-route exactness.

**MirTypesTest.** New `RawStorageNoDeclarationKeepsIsUnowned`, pinning MirWrites:

| Unowned | Not Unowned |
| --- | --- |
| `raw/(ref/i32)`, `raw/(raw/(ref/i32))`, `ref/(raw/(ref/i32))`, `(raw/(ref/i32), isize)`, `RawBox`, `KeptBox<raw/(ref/i32)>`, `S<ref/i32>` (raw past a uniq Field), `Slice<raw/(ref/i32)>`, `Option<raw/(ref/i32)>` | `KeptBox<ref/i32>`, `TupBox<ref/i32>`, `Outer<ref/i32>`, `ArrayIterator<ref/i32>`, `ArrayOwningIterator<ref/i32>`, `Buffer<ref/i32>`, `raw/i32` |

`ArrayUniqIterator<ref/i32>` stays `ThroughArgument` and `UniqSlice<ref/i32>` stays `Through`.

**Expectations that change**

- `TheGuardSaysWhatACallMayWritePastAValue` (MirTypesTest.cs:46-72): `raw` goes from `Raw` to `Unowned` (the same fact, renamed and
  generalized). `toRaw` goes from `Through` to `Unowned`: E is now reached directly, where it was reached through the raw-only local
  it names. W's targets are unchanged, as `ACallStoresIntoEveryHolderItsArgumentReaches`' `rawPointee: b = 3` (MirBorrowsTest.cs:604)
  shows, so this is not a weakening.
- Unchanged expectations: `AnIntrinsicRetainsAsItsDeclarationSays` (`Raw.initialize` to E), `ACallStoreFollowsSpecsPerArgumentRule`
  (the iterator holds) and `LoansAreHeldBy…` (the Loan fact, including `nest` true).

**Allocation, scaling and census**

- `AWarmRunAllocatesNothing` (:2588) gains `aCall`, so its count goes from 7 to 8 and the run still allocates nothing. The cache is
  filled during the warm-up.
- A scaling case: N nested generic declarations wrapping a kept `raw/T`, and a recursive family. The summary's DFS steps are added
  to `Work` (A3 6.2) and must grow linearly. The four-layer chain shape (`121596, 204840, 513216`) stays within ±1 %.
- Census (MirCensusTest; library, 44 milestones, 31 goldens): no new A5 problem. Library raw Fields are all `raw/E`, `raw/T` or
  `raw/u8` written by their declarations, and library stores through raw pointers store parameter values.

## 8. Size, speed, unverified

**Size**

| File | Lines |
| --- | --- |
| `MirTypes.Keeping.cs` (new: summary, `Kept`, fixed point, cache) | +130 |
| `MirTypes.Holding.cs` | +30/−25 |
| `MirBorrows.Values.cs` | +12/−14 |
| `MirBorrows.Calls.cs` | +2/−1 |
| `MirBorrows.cs` (scratch, reset) | +5 |
| tests | +110/−2 |
| DECISIONS | +25 |
| PLAN, STATUS, CODEMAP and ARCH rows | ±6 |

The net code is about +140.

**Speed**

- Each declaration's summary costs one DFS per program version, at most a few rounds.
- HoldsLoans per local keeps today's order, plus Bare-route argument walks and Shared walks, each Type once per mode, stopping at
  `Unowned`.
- W adds one MirWrites read per store output, and one HoldsLoans walk where the operand is projected (rare).

**Unverified**

- The census and ledger effect, which is expected to be none.
- Projections of a parameter that resolve to a raw pointer. No probe exists, because the program Binding needs for it is
  unverified.
- That a generic body cannot store a local Loan into an abstract Type's hidden raw storage. This is argued only: that Type's Origins
  are fixed by the caller.
- That the fixed point needs at most a few rounds.
- That a Copy Type with kept raw storage, whose copies alias it, stays an R4 author obligation that A5 cannot see.
- R-S's slot mask, which is only sketched.
- The default pipeline stays unsound until R6-check.

## 9. Units, records

Units:

- **U0 (owner).** Q1 (R-T or R-S) and Q2.
- **U1 (behavior-preserving; VERIFICATION#behavior-preserving-commits).** `MirWrites` becomes flags with `Unowned` taking exactly
  today's `Raw` condition. HoldsLoans takes the scratch object that MirBorrows owns and resets per program. The `seen` modes become
  ushort.
  - Checks: MirTypesTest, MirBorrowsTest and the census, with results identical to before.
- **U2 (behavior; full process).** `MirTypes.Keeping`, the walk rules (§4.3), W's rules 2 and 3, and deleting the raw clause.
  - Checks: the regressions, the MirTypesTest pins, the census, allocation, scaling, and a Session.
  - DECISIONS **R4-M8x**, "Raw storage a declaration keeps (PLAN G160)". It records the reading (R-T or R-S per Q1); the
    `Keeping` representation and the cache owner; `MirWrites.Unowned`, superseding R4-M73's raw clause; W's E rules; the
    composition with R4-M76, P13 and 2e-D; and the rejected options of §6. Because it is a new representation, it needs owner
    approval.
  - A3 E16's text gets an erratum line.
- **U3 (Binding, after Q2; its own design and review).** Rows for raw storage in a nominal Type past a shared layer, with marker
  authority. It is not part of G160's group.

Records:

- **PLAN.** G160 closes at U2's merge. A new G row covers shape (d), owner Binding retention, R4 before its exit or R5 as the owner
  places it. The (a) sentence follows Q1.
- **STATUS:49.** The default pipeline's sentence stays. It points at R6-check and at the new G row for (d), and states that A5
  rejects the shapes of §1.2.
- **CODEMAP and COMPILER_ARCHITECTURE:110.** `MirTypes.Keeping` is added to the Type facts row.
