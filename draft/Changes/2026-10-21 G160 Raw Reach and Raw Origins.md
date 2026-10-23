# G160 and G184: Raw Reach and Raw Origins

Date: 2026-10-21. Scope: the SPEC revision and the implementation plan for PLAN G160 and G184, and for G147 (1), which shares
their cause. Baseline: dev `c0f370bd`; the code anchors below were read on dev `0eac4690` and are unchanged at the baseline.

Status: 取り込み済み (2026-10-21). The owner's design, worked out with the planning session in one conversation (§1), reviewed
independently and integrated into SPEC (INTEGRATED; review in §11). It supersedes the plan
[G160 Raw Storage Kept by Declarations](../Obsolete/2026-10-21%20G160%20Raw%20Storage%20Kept%20by%20Declarations.md), whose
probes and review record stay in `Sketches/G160/`. Adoption implies no implementation support (STATUS).

## 0. Summary

**Cause.** SPEC defines what a reference reaches only through the Loans it carries (§15.6), and a raw pointer forms no Loan
(§5.4). By the letter a raw pointer taken from a Place reaches nothing, and every other raw pointer reaches storage the body does
not own (E). A call's effect on raw storage is decided by its public contract (§15.6.4), but `raw/T` states neither whose storage
it addresses nor what authority it has. Hence:

- G184: in a body, a read through a raw pointer misses the Loans of the holder it actually reads.
- G160: at a call, the checker must either send every raw pointee to E (false errors on owned storage and on shared iteration) or
  to the holder (unsound for a pointer into someone else's storage).
- G147 (1): a byte handle (`raw/u8`) drops the Types a call places through it.

**Design.** One reach rule for every reference, raw included, and one optional Origin on raw layers:

1. **Reach** (§15.6). Besides its Loans, a value carries the holders it **addresses**. `P@raw` addresses the root holder of `P`
   when `P`'s path dereferences no borrow and no raw pointer, and otherwise carries what a Borrow of `P` would carry,
   without the Loan the Borrow would form. Copies, displacements and conversions between raw pointer Types keep addresses. Reads and writes through any reference
   use what it reaches, and a holder that an address names is used wherever the address is. An address conflicts with nothing and
   grants no authority.
2. **Raw Origins** (§5.7). `raw/T during o` states that the pointer addresses storage that `o` covers. `raw/T during self`, in the
   storage of a struct or enum, states storage the value owns: an own step, like an object payload. `raw/T` states nothing.
   Typed paths give Origins. Adding an Origin is an explicit claim in an unsafe context (`E@(raw/U during o)`); dropping one is
   a fit.
3. **Authority and calls.** A raw pointer's Origin bounds its authority (a write through a shared Origin is an error), past a
   shared layer raw access writes nothing, and a call retains through an annotated raw layer as through a borrow of its Origin
   and through an unannotated raw result as through every argument plus E.

Every class maps to an existing step: `during self` to an own step, `during o` to a dereference with `o`'s mode, an unannotated
layer to a reference whose reach is what it carries. There is no declaration statement, no per-declaration keeping fact and no
MIR builder change.

## 1. The owner's decisions

The owner's request of 2026-10-21 on PLAN G160 and G184 (excerpts): 「とにかく、場合分けを増やすと実装の難易度が極度に上がる。
共通の規則で対処したい。」, 「objrefやobjuniqも同等に扱えるかどうか、検討してください。」, 「実装の簡素化、効率化のためなら、rawの
SPEC変更も許容します。」. The owner rejected the G160 plan's declaration statement (「unsafe storage p of T via loan なんて、
私の言語的許容範囲をオールト越えしている。」) and asked for designs without new declaration syntax, judged by the Kimigayo
Principles. The planning session compared four families (§7); the owner adopted the Origin design and answered every open
question with the recommendation:

- 「OKです。」 (the spelling `during self`);
- 「推奨の仕様で結構です。」 (an explicit claim to add an Origin; unannotated raw results reach every argument and E);
- 「残りの判断事項はすべて推奨どおりで確定します。」 (all other items below);
- 「案aで決定します。」 (R4's exit waits for G160 and G184);
- 「推奨の進め方の通り、文書の作成、レビュー、反映処理を行ってください。文書が完成したら、マージ・プッシュして、R4 Agentに
  通知してください。」 (this document, its review and its integration).

| # | Decision | SPEC |
| --- | --- | --- |
| D1 | Reach: a value carries addresses beside Loans; reads and writes through every reference, raw included, use one rule | §15.6, §5.2.1 |
| D2 | Liveness makes no distinction between address and content uses: a holder an address names is used wherever the address is | §15.6, §5.2.1 condition 4 |
| D3 | `raw/T during o` (storage `o` covers), `raw/T during self` (own storage), `raw/T` (unstated) | §5.7, §3.3.2, §15.3.1 |
| D4 | The spelling `during self` | §5.7 |
| D5 | Adding or replacing an Origin is a claim, only by `E@(raw/U during o)` in an unsafe context (the grouped spelling: review B1); dropping one is a fit | §5.4, §5.7, §3.8 |
| D6 | A raw layer without an Origin in a result reaches every argument's reach and storage the body does not own | §15.6.4, §5.7 |
| D7 | Authority through an own step (object payload or `during self` storage) lasts while the owning value stays in its holder; obligation, not checked | §5.2.1 condition 3 |
| D8 | A pointer restored from an integer reaches only storage the body does not own; the implementation's guarantee carries the rest | §5.5 |
| D9 | The default pipeline keeps its verdicts until R6-check (STATUS) | — |
| D10 | §8.11.4 rule 1 ("only shared past a shared layer") holds for raw layers, also inside abstract parts | §8.11.4 |
| D11 | A write, Take or in-place destruction through a raw pointer whose Origin keeps only shared Loans is an error (with C3) | §5.7 |
| D12 | G160's shape (d): settled by a SPEC rule on raw writes past a shared layer, with no Binding rows from Fields (wording: C1 below) | §5.2.1, §15.6.2 |
| D13 | Typed paths give Origins (raw dereferences counted: review B3): `P@raw` and a `during self` Field read take the Origin of the last borrow or raw pointer the path dereferences (a borrow's, or the Origin a raw pointer states); displacement and conversions keep it | §5.3, §5.4, §5.7 |
| D14 | A borrow of a raw Place, and `Raw.slice`'s result, lie within the pointer's Origin | §5.2.2, §5.6 |
| D15 | Retention through an annotated raw layer follows the Referent Type of the borrow its Origin belongs to (defined after review B4 and confirmed by the owner: 「承認 (Recommended)」): a slot's keeping Fields, an input's borrow, the holding value for `self`; a raw Origin in a signature must belong to an input's borrow, a slot or `static`. G147 (1) closes through G160-3's typed `placeEntry` handle (§4) | §15.6.4, §5.7 |
| D16 | Contract comparison follows subtyping: an annotated raw layer is a subtype of the unannotated one | §3.8, §15.3.7 |
| D17 | `during self` appears only on raw layers in inline storage (Fields, Tuple and fixed-array elements, Case payloads), never inside a Type argument | §5.7 |
| D18 | No intersection is a raw Origin | §5.7, §15.3.1 |
| D19 | Process: this proposal, an independent review, the SPEC revision, then the units of §9; the G160 plan closes; the owner approves the two vocabulary entries of §9.2; until the units land, G160 and G184 stay recorded limits (G184's Q5 (iii)) | — |
| D20 | R4's exit waits for G160 and G184 | PLAN |

**C1, the planning session's correction of D12's wording, confirmed by the owner (「承認 (Recommended)」).** The recommendation the owner
confirmed read 「共有層の先の raw は、Origin が uniq のときだけ書ける」. The SPEC text says instead that a raw pointer read past a
shared layer has no write authority, whatever its Origin. Reasons:

- The safe analogue: through a shared reference, an inner exclusive capability is never used (§3.3.2: shared access cannot use
  an inner `uniq` exclusively or write the slot). An exception for exclusive Origins would give raw access more through a shared
  layer than the safe form it stands for.
- The exception defeats D12's purpose: to know which nominal Types reached past a shared layer may be written through a raw
  Field, Binding would need a per-declaration fact read from Fields, the fact D12 was chosen to avoid.
- A raw pointer whose Origin keeps only shared Loans counts as a shared layer too, so the authority rule and the checker's W
  agree (review N5).

Cost under either wording (review B2): two library bodies write through a raw pointer read past a shared layer.
`Dictionary.insertOrReplace` and `DictionaryStorage.removeEntry` find the slot through `Storage.borrowStorage(self@follow@ref)`
and then Take or `Raw.initialize` through it. Their pointer states no exclusive Origin, so they become undefined under the
confirmed wording as well. No other library body or SPEC example writes through raw storage past a shared layer. A related
change follows from D6 and D15 (review R2-B1): `Dictionary.tryInsert`, `insertOrReplace` and `DictionaryStorage.removeEntry`
take the exclusive handle `layout = Storage.dictionaryStorage(self)` before borrowing `self@follow@ref`, and once the handle
retains the exclusive Loan, A5 rejects that order. G160-3 rewrites all three to search first through the shared remainder,
then take `dictionaryStorage` and derive the slot from its handle.

**C2, the planning session's addition for soundness, confirmed by the owner (「承認 (Recommended)」).** D8 is stated as one instance of a general
obligation, condition 5 of §5.2.1: a raw access that reads or writes a value carrying a Loan acts within what the pointer reaches
or in storage the body does not own. Without it, an untracked path (a pointer hidden in a nominal call result whose Type states
no Origin, such as `Cursor<T>.init(p: w@raw)`, a pointer a callee stored and another returned, or an integer round trip) could
move Loan-carrying values through a holder the checker does not see. D6 keeps the common case defined: a raw result states what
it may address. The obligation falls on the client and has no diagnostic: a pointer passed through such a `Cursor` and read back
makes the client's access undefined.

**C3, the planning session's extension of D11, confirmed by the owner (「エラーにする (Recommended)」).** D11's error also covers an `@uniq` or `@objuniq`
Borrow of a raw Place through a shared Origin, since condition 3 already requires write capability for an exclusive borrow's
accesses (review N6).

## 2. Problem and root cause

SPEC (baseline):

- §15.6: "A reference reaches the holders named by those Loans it carries … It also reaches storage the body does not own when it
  comes from a parameter or capture, from static or raw storage, or from a call result that its contract ties to no argument".
  A raw pointer carries no Loan (§5.4: `@raw` "leaves no Loan"), so `w@raw` reaches nothing.
- §5.2.1 condition 4: "Loan liveness follows the uses of safe values alone", so a raw read can recover a Loan that nothing keeps
  live.
- §5.2.1, Retained dependencies: "the Type owning the storage must keep them through a Type argument or a `Loan<T>` Field", but
  SPEC never says which raw storage a Type owns.
- §15.4.2: "`raw/T` has no borrow Origin"; §8.1.2's table gives raw no outer Origin.

A5 (dev): a raw Ref's value is empty (`MirBorrows.Values.cs:30`); Read, Issued and Store send every raw Deref to E
(`MirBorrows.Values.cs:72`, `:95`; `MirBorrows.Flow.cs:138`); a raw local is no holder (DECISIONS R4-M36); a call's W treats a
raw pointee in a holder as the holder's storage, except the raw clause (`MirBorrows.Values.cs:227`) and `MirWrites.Raw`,
`RawPointee` and `RawLoan` (`MirTypes.Holding.cs:34-40`, `:75-79`, `:150-155`); `p[n]` is lowered through a pointer temporary whose
`Add` has an empty value (`MirBuilder.Selections.cs:36-42`). Binding: the parser reports `BorrowOriginSemantics_Kd` for `during` on
a raw layer (`Parser.Origins.cs:167`), and `RetentionWalk` reads no Fields, so shape (d) has no row.

The known shapes are the G160 plan's §1.2 (probes in `Sketches/G160/probes/`), G184's two and G147 (1); §5 gives their verdicts.

## 3. Rules

SPEC holds the normative text; this section records the content and its locations.

### 3.1 Reach and addresses (§15.6, §5.2.1, §5.2.2)

- A raw pointer formed by `P@raw` **addresses** the holder at the root of `P` when `P`'s path dereferences no borrow and no raw
  pointer; otherwise it carries what a Borrow of `P` carries (§15.6.3, and past a raw pointer §5.2.2: what that pointer
  reaches), without the Loan the Borrow would form. A raw pointer read from a `during self` Field reaches what `@raw` of the
  Field's storage would.
- An address is carried like a Loan: by Copies, displacements, conversions between raw pointer Types and every value or holder
  that holds one. It conflicts with nothing and grants no authority.
- A reference reaches the holders named by its Loans and the holders it addresses. A raw pointer that reaches no holder this
  way, such as a `Raw.allocate` result, reaches storage the body does not own.
- A value is also used wherever a value that carries an address of its holder is used (D2). §5.2.1 condition 4 follows: Loan
  liveness follows the uses of values, raw pointers included. A raw Place borrow carries what the pointer reaches; its anchor
  recovers no owner or Loan.

### 3.2 Raw Origins (§5.7, new)

| Form | The pointer addresses |
| --- | --- |
| `raw/T` | Unstated storage: what the value carries (§15.6); at a call, what the callee may reach |
| `raw/T during o` | Storage that `o` covers: what the Loans of the borrow `o` belongs to protect, or static storage |
| `raw/T during self`, in a struct's or enum's inline storage | Storage the value holding the Field owns: an own step of that value, like an object payload |

- **Origins (D15, D17, D18).** An Origin atom other than an intersection, which belongs to a borrow: a slot, also projected
  from a value (`x.source`), to the stored safe references and `Loan<T>` Fields that keep it (together they admit what their
  Referent Types admit); the outer Origin of a borrow value, an input's or in a body a local's, to that borrow; `static` to
  static storage (review R2-N2). In a signature a raw layer's Origin must belong to an input's borrow, a slot or `static`.
  `self` names the holding value and never appears inside a Type argument. In the Type's own bodies `self` is the receiver's
  Origin (§15.2.1); in a constructor the value being constructed and in `drop` the destruction receiver, both exclusive (review
  N8).
- **Reading a Field (D13).** Through a path that dereferences a borrow or a raw pointer, `self` becomes the Origin of the last
  one the path dereferences: a borrow's outer Origin, the Origin a raw pointer states, or none (review B3, R2-N6). Through no such
  dereference the read pointer has no Origin and reaches what `@raw` of the Field's storage reaches. A slot of the Field's Type
  is substituted by the value's binding.
- **Typed paths (D13).** `P@raw` takes its Origin the same way. Displacement and conversions between raw pointer Types keep the
  Origin. None of these needs an unsafe context.
- **Fits and claims (D5, D16).** A raw layer without `during` counts as stating an Origin that every Origin outlives, so
  `raw/T during o <: raw/T` (§3.8): dropping an Origin, or narrowing it to one it outlives, is a fit, and `E@(raw/U during o)` is
  a fit when the raw Origin of `E`, or `null`, fits `o` (review R2-N3). A change that is not a fit is a claim, formed only by
  `E@(raw/U during o)` in an unsafe context (§13.5.1 admits the grouped target's Origin on an outer `raw` layer, review B1);
  elsewhere it is an Origin relation error (review R2-N4). `null` takes any Origin. A claim promises that every access through
  the pointer and every pointer derived from it lies in storage the Origin covers.
- **No region.** A raw Origin states which storage the pointer addresses, not how long it is valid: the pointer's uses add no
  point to its region (§15.6.1), and it keeps no Loan live.
- **Authority (D11, C3).** A write, Take, in-place destruction or `@uniq`/`@objuniq` Borrow of a raw Place is an error when the
  pointer's Origin keeps only shared Loans: the outer Origin of a `ref` or `objref` borrow, or a slot whose Loan requirement is
  `ref`, judged per admitted Semantics case.
- **Slots.** A raw layer's Origin is a covariant position for variance but no use of a slot for Loan requirements or Phantom
  status; a slot a raw layer states must not be Phantom. A `during self` pointee counts as stored content of its Type for slot
  properties, OwnedOrigins and holder eligibility, and a Type with a `during self` layer is not Copy.
- **OwnedOrigins.** A raw layer's Origin other than `self` is in OwnedOrigins (§15.2.3).

### 3.3 Authority past a shared layer (§5.2.1 condition 3, §15.6.2; D12 with C1)

A raw pointer read from a Place past a shared layer, or past a raw pointer whose Origin keeps only shared Loans, has no write
authority. The rule replaces Binding rows for shape (d): a call writes through no raw layer reached past a shared layer.

### 3.4 Own steps and Move (§5.2.1 condition 3; D7)

A pointer formed through an own step (an object payload, or storage a raw Field declares `during self`) has authority only while
the owning value stays in the holder it was reached from. A pointer read again from a `during self` Field takes the current
holder, so a value may keep pointers into its own storage across Moves only in `during self` Fields. The rule is an obligation;
a static check (a Move of the owner while such a pointer is live) is a later precision candidate.

### 3.5 Calls (§15.6.4, §5.2.1; D6, D15, C2)

- Through a raw layer with an Origin a call may store what the Referent Type of the borrow that Origin belongs to admits (for
  `self` the holding value's Type, for `static` the pointee Type), and that storage retains as the borrow would, without forming
  a Loan.
- Through a raw parameter without an Origin a call may store what its pointee Type admits (today's rule). A raw layer without an
  Origin in a result Type, also as a Tuple element, an Option payload or a Type argument, may address what the callee could reach
  through any argument, and storage the body does not own (review N4).
- A raw pointer in a Field of a nominal result is stated by that Type's Fields alone; one hidden there falls under condition 5.
- A function stores through raw storage only values its contract lets the call retain there (§5.2.1, Retained dependencies).
- A raw access that reads or writes a value carrying a Loan acts within what the pointer reaches or in storage the body does not
  own (condition 5, C2).

### 3.6 Integer addresses (§5.5; D8)

A pointer restored from an integer has no Origin and reaches storage the body does not own, so condition 5 applies to it.

### 3.7 Types and syntax (§3.3.2, §3.8, §8.1.2, §13.5.1, §15.2.1, §15.2.3, §15.3.1, §15.3.2, §15.3.5, §15.4.2, §22.6)

`during` is accepted on `raw`; `raw/ref/T during a` now annotates the raw layer (the inner layer needs
`raw/(ref/T during a)`); a grouped adaptation target may state an outer `raw` layer's Origin (§13.5.1); a raw layer's Origin is
never completed; §8.1.2's raw row gains an optional Origin that a pair binder's raw case never has, so a `raw/T during o`
argument fits a pair by dropping its Origin; storage names may use `self` on raw layers; a foreign import may state raw Origins
as part of its safety contract (§22.6); variance and OwnedOrigins as in §3.2. LIBRARY's `Raw.slice` row states the new bound.

## 4. Examples

```kimi
public struct Buffer<T>
    internal let data: raw/T during self         // Buffer owns the storage.
    internal var length: isize
    let capacity: isize

    public init(capacity: isize)
        unsafe => self.data = Raw.allocate<T>(capacity)@(raw/T during self)   // The claim.
        self.length = 0
        self.capacity = capacity

public struct BufferView<T> {source}
    let loan: Loan<ref/Buffer<T> during source>  // The mode: shared.
    let data: raw/T during source                // The relation: storage source covers.
    let length: isize

    internal init(buffer: ref/Buffer<T> during source)
        self.data = buffer.data                  // raw/T during source: self becomes source; no claim.
        self.length = buffer.length
        self.loan = Loan.init(buffer)
```

The Type checks the link: the storage Buffer owns, seen through `source`, is the storage the view addresses.

| Type | Raw Fields after unit G160-3 |
| --- | --- |
| `Storage.RefRemainder<E>` | `storage: raw/E during source` beside `Loan<ref/E during source>` |
| `Storage.UniqRemainder<E>`, `UniqSlice<T>` | `raw/… during source` beside `Loan<uniq/… during source>` |
| `Storage.OwnedRemainder<E>`, `Text.HeapBuffer` | `raw/… during self` |
| `Storage.DictionaryRefRemainder`, `DictionaryUniqRemainder` | `storage: raw/u8 during source` (punned content, relation by Origin) |
| `Storage.DictionaryOwnedRemainder` | `storage: raw/u8 during self` |
| `Storage.dictionaryStorage`, `Storage.placeEntry` | `dictionaryStorage<K, V>(value: uniq/Dictionary<K, V>) -> (raw/Dictionary<K, V> during value, isize)`; `placeEntry<K, V>(handle: raw/Dictionary<K, V>, key: K, value: V)`, whose pointee admits `K` and `V` (G147 (1)) |
| `Dictionary.tryInsert`, `insertOrReplace`, `DictionaryStorage.removeEntry` | Search first through the shared remainder of `borrowStorage(…@ref)`, then take `dictionaryStorage` and derive the slot from its exclusive handle (C1's cost, review R2-B1) |
| `WriteWindow`, `Text.FixedBuffer` | `raw/u8 during source` |
| `Utf8Writer` | `destination: raw/u8 during target`; `dispatch` and `pending` as their producers state, unannotated if unknown |
| A user `Cursor<T>{p: raw/T}` | Unannotated: unstated and sound; precise as a view with `{source}`, a `Loan<…>` and `during source` |
| A user `MarkBox {a}` | Each raw Field states its own relation; an unrelated marker changes nothing |

Signatures may state the relation: `func dataOf<T>(b: ref/Buffer<T>) -> raw/T during b` retains `b` only, while the unannotated
`-> raw/T` reaches every argument and E.

## 5. Verdicts on the known shapes

| Shape | A5 today | After the units |
| --- | --- | --- |
| G160 (a) `storeRaw(b@uniq, x@ref)` into an unannotated `RawBox {a}`, then `x = 5`; by value, two `uniq` layers, a Tuple holder | accepted | error: the call writes what `b` carries (E for a `Raw.allocate` cell, the holder for `y@raw`) |
| G160 (b) a raw chain `raw/(raw/(ref/i32))` | accepted | error |
| G160 (c) `r = c@ref; putRS(r@uniq, x@ref)` and (d) `putSB(b@ref, x@ref)` past `ref/RawBox` | accepted | the callee's write through a raw pointer read past a shared layer is undefined behavior (§3.3); the call writes nothing there |
| SPEC's `Buffer<T>.push(x@ref)`, buffer dead / live (`during self`) | valid / error | valid / error |
| Shared iteration (`RefRemainder` `during source`, `ref` marker), then `a.value = 3` or `append` | valid | valid: no write through a shared Origin |
| Exclusive iteration | valid (P13 hold) | unchanged until R4-M76's per-part guard |
| Owning iteration, also over `Array.init(capacity:)` (`OwnedRemainder` `during self`) | valid | valid |
| An unannotated user keeper `KeptBox<T>{p: raw/T}`, holder dead | valid | error (unstated: annotate `during self` to own) |
| `Cursor<T>` built from `w@raw`, `setC(c@uniq, x@ref)`, `x = 5; inspect(w)` | accepted | error |
| G184: `buf.data[0]` after `push(x@ref)`, buffer replaced, `x = 5`, the read value used | accepted | error: the read addresses `buf` |
| G184: `q = *p` with `p = w@raw`, then `y = 5; inspect(q)` | accepted | error |
| G147 (1): `let entries = [1: value@ref]; value = 99; inspect(entries)` | accepted | error once `placeEntry`'s handle admits `K` and `V` and reaches the Dictionary (G160-3) |
| `Raw.initialize(p, x@ref)`, `p = Raw.allocate(…)`, then `x = 5` | error | error |
| `p = w@raw` with `w` holding `y@ref`, then `y = 5`, then only `p == null` | accepted | error (D2) |

## 6. Principles

- **One Concept, One Canonical Form.** The relation between a pointer and the Loans that protect its storage is an Origin, as
  for every borrow, written with the existing `during`; the Loan's mode stays the `Loan<T>` marker's. Each concept has one form:
  the dependency (marker), the relation (Origin), ownership (`during self`), the claim (`@(raw/U during o)`).
- **Local Reasoning.** A raw Field's class is in its own Type; a call's effect on raw storage is in its signature. No inference
  from bodies, construction history or other Fields.
- **Explicit Semantics.** Ownership of raw storage and its dependency are written; omission means "unstated", which is sound;
  every claim is a visible unsafe operation.
- **Compiler Server Protocol.** The forms give located diagnostics with repair candidates: a write through a shared Origin, a
  slot a raw layer names that keeps no Loan (add `Loan<ref/…>` or `Loan<uniq/…>`), a missing claim (`@(raw/U during o)` in an
  unsafe context), and, as Advice, a Field a `drop` releases without `during self`; claims are enumerable for review.

## 7. Options considered

| Option | Verdict |
| --- | --- |
| The G160 plan's `unsafe storage p of T via self / via loan` (X-decl) | Rejected by the owner (new declaration syntax); a Field-name relation the Types do not carry |
| K′: keeping inferred from a `raw/T` Field over a Type parameter | Rejected: unsound for a non-owning pointer (`Cursor`) unless every existing raw Field becomes an implicit promise |
| U: every raw pointee is E | Rejected: false errors on owned storage (`Buffer`, owning iteration) |
| Marker presence: a raw Field is owned unless the Type has a `Loan<…>` Field | Rejected: a Field's meaning depends on other Fields; several raw Fields and markers are ambiguous (`Utf8Writer`, `MarkBox`); an implicit promise |
| A generic marker (`Own<T>` beside `Loan<T>`) | Rejected: the Field-to-marker correspondence stays implicit and fails for punned storage |
| Integrated storage Types (`Lent<B/T during o>`, `Owned<T>`) | Rejected: new Types overlapping `Slice`/`UniqSlice`, a larger library migration, and punning needs a second Type parameter |
| Implicit fit of an unannotated pointer to an Origin (safe, or in an unsafe context) | Rejected: a safe body could publish a false claim, and the claim would not be visible |
| An unannotated raw result reaches E only | Rejected: `return b.data` would drop the relation in safe code |
| Liveness that ignores address-only uses | Rejected: a second liveness kind; the false errors it avoids are rare |
| Exposure rule for integer round trips | Not adopted now: needs an escape rule across calls; condition 5 covers it |
| Binding rows from nominal Types' Fields for shape (d) | Rejected: a per-declaration Field fact; §3.3 makes such writes undefined |
| A static check of D7 | Later precision candidate |

## 8. Costs and consequences

- **Language.** Raw access no longer writes past a shared layer (§3.3), so raw-based mutation through shared access is undefined;
  raw Origins add claims, which need an unsafe context; a pointer kept across a Move of its owner loses its authority unless it
  is re-read from a `during self` Field; condition 5 makes untracked aliasing of the body's own Loan-carrying storage undefined.
- **Library.** `Dictionary.insertOrReplace` and `DictionaryStorage.removeEntry` write through a raw pointer read past a shared
  layer (C1), and with `Dictionary.tryInsert` they hold the exclusive handle across a shared Borrow of the same Dictionary, which
  A5 rejects once the handle retains its Loan (R2-B1). G160-3 rewrites all three before G160-4 lands.
- **Verdicts.** Unannotated user keepers become E (errors where a dead holder used to be enough); address-only uses keep Loans
  live (D2); typed paths put raw Origins into inferred Type arguments (`r@follow@raw` gives `raw/i32 during r`), so an invariant
  container or an `Owned` premise can now fail where it passed (review N16). Apart from the three Dictionary bodies, which
  G160-3 rewrites, no library or milestone verdict is expected to change once the library is annotated (unverified, §9.4).
- **Implementation gap until G160-0.** The parser reports a Language error for `during` on a raw layer
  (`BorrowOriginSemantics_Kd`) and for an Origin on a grouped `raw` adaptation target (`AdaptationOrigin`), and Binding rejects
  `self` in storage; these forms are valid now, so G160-0 makes them the located `Unsupported_Kd` (AGENTS: never a Language error
  for a valid form). The ungrouped `E@raw/U during o` stays a syntax error (`AdaptationDuring`, §F.9.4). STATUS records the gap
  meanwhile.

## 9. Implementation plan

### 9.1 Units

| Unit | Owner | Content | Process |
| --- | --- | --- | --- |
| G160-0 | BU | The located `Unsupported_Kd` for the new forms: `during` on a raw layer, `self` on a raw layer in storage, `E@(raw/U during o)` (today `BorrowOriginSemantics_Kd` and `AdaptationOrigin`; the ungrouped `E@raw/U during o` keeps `AdaptationDuring`) | one review |
| G160-1 | BU | Binding: raw Origins in Types (§3.2, §3.7): the representation, `self` and its substitution on reads, typed paths, fits and claims, the authority error, the slot and Copy checks, OwnedOrigins and variance, raw Place borrows and `Raw.slice` within the Origin, contract comparison | full process |
| G160-2 | BU | Binding: retention for raw (§3.5): annotated layers as borrows of their Origin; unannotated results to every argument plus untied; nothing written past a shared layer (closes (d)) | full process, one design with G160-1 |
| G160-3 | BU, with the library's owners | Library: the annotations of §4, `dictionaryStorage` and `placeEntry`'s handle (G147 (1)), the three Dictionary bodies (`tryInsert`, `insertOrReplace`, `removeEntry`: search first, then take the handle), the compiler-supplied constructions' contracts, LIBRARY; `KimiLibraryShapeTest` | one review; lands with or before G160-4 |
| G160-4 | R4 | A5: addresses as a non-conflicting set element with a named holder; raw locals and temporaries become holders; `@raw` gives parents and an address; a displacement keeps its pointer's value; the raw arms of Read, Issued and Store join the borrow arms (Content and refs, E where nothing is reached); W classifies a raw layer by its Origin (`self`: the holder's own storage; `o`: a dereference with `o`'s mode, no write when shared; unstated: a reference whose reach is what it carries); nothing written past a shared layer | full process |
| G160-5 | R4 | Records and checks: the §5 matrix, census, ledger, allocation and scaling; PLAN and STATUS closure | with G160-4 |

Order: G160-0, then G160-1 and G160-2, then G160-3 and G160-4 (G160-4 before G160-3 would turn the library's unannotated
keepers into E and fail the census). G184 needs no R5 unit: values carry their addresses through MIR's pointer temporaries.

### 9.2 Representations and vocabulary (owner-approved, D19)

- A5's address: a set element that names a holder like a direct Loan and conflicts with nothing (one row of the conflicts
  table, not a case).
- Binding's raw Origin: an optional Origin on raw layers, with `self` on raw layers in storage.

A new `BindingFailure` entry or diagnostic code needed by G160-0 to G160-2 is decided in that unit's design; an entry beyond
the existing limits needs the owner's approval there.

### 9.3 Deletions

`MirWrites.Raw`, the raw clause of W (`MirBorrows.Values.cs:227-230`), `RawPointee` and `RawLoan` (`MirTypes.Holding.cs`), the raw
arms that return `escapedSet`, R4-M36's "a raw pointer holds none", A3 E16's raw clause and R4-M73's `Raw` bullet (superseded
in G160-4's DECISIONS entry), the parser's raw case of `BorrowOriginSemantics_Kd`. Not needed: the G160 plan's `MirTypes.Keeping`
fixed point, its Unowned walk, its declaration grammar and G184's separate representation or hold.

### 9.4 Tests, measurements, unverified

- MirBorrowsTest: the §5 matrix as failing-before rows (sources from `Sketches/G160/probes/` and G184's two), with valid
  counterparts (owning, shared and exclusive iteration; annotated keepers); MirTypesTest rows for the raw classes.
- Binding: each new error and its valid twin (a write through a shared Origin, a Phantom slot named by a raw layer, a claim
  outside an unsafe context, `self` inside a Type argument, an intersection on a raw layer, a `during self` Copy Type).
- Census (library, 44 milestones, 31 goldens, accepting spec-case variants): no new problem after G160-3; allocation
  (`AWarmRunAllocatesNothing`) and scaling (raw chains, nested generic keepers) rows.
- Library: `tryInsert`, `insertOrReplace` and `removeEntry` after the rewrite (their Dictionary tests and fixtures unchanged), a Dictionary
  literal keeping its elements' Loans (G147 (1)).
- Unverified: the census effect, raw Origins in inferred Type arguments (invariant containers, `Owned`), the cost of raw holders
  (formatting and Dictionary bodies), and the library's `dispatch` and `pending` relations.

### 9.5 Gate and interim

R4's exit waits for G160-0 to G160-5 (D20). Until then G160 and G184 stay recorded unsound acceptances (PLAN, STATUS), the
default pipeline keeps its verdicts until R6-check (D9), and G160-0's gap is a recorded limit.

## 10. Records

- SPEC: the sections of §3 (INTEGRATED gives the mapping).
- DECISIONS: one entry with the owner's decisions D1–D20, C1–C3 as confirmed, and the vocabulary approvals.
- PLAN: G147 (1), G160 and G184 point here, with the units and D20; COMPILER_ARCHITECTURE's R4 row; STATUS: the Loans row
  records the SPEC change and the gaps; LIBRARY: `Raw.slice`'s bound.
- The G160 plan moves to `Obsolete` as 完了.

## 11. Review record

Round 1 (independent reviewer, `Sketches/G160/raw-origins-review-1.md`): accept with corrections. Blocking findings and their
fixes:

- B1, the claim had no valid spelling (§13.5.1 forbids an Origin on a target's outer Semantics; §F.9.4 forbids an ungrouped
  BorrowOrigin): the claim is the grouped `E@(raw/U during o)`, and §13.5.1 admits an outer `raw` layer's Origin; G160-0 also
  replaces the parser's `AdaptationOrigin` for it.
- B2, C1's third reason was false: `Dictionary.insertOrReplace` and `DictionaryStorage.removeEntry` write through a pointer read
  past a shared layer; C1 now states the cost, and G160-3 rewrites both bodies.
- B3, typed paths and addresses ignored raw dereferences: the rules now count a raw pointer as a reference the path dereferences.
- B4, D15 was not stated: §5.7 defines the borrow an Origin belongs to and restricts raw Origins in signatures; `placeEntry` takes
  a handle whose pointee admits `K` and `V`.
- B5, the example read a private Field: it is `Lens` with `internal` members.

Non-blocking findings adopted: N1–N5 and N7–N18 (N5 and N6 as parts of C1 and C3); none became an open issue.

The owner then confirmed C1, C2, C3 and D15's definition with `placeEntry`'s typed handle (「承認 (Recommended)」 and
「エラーにする (Recommended)」 for C3).

Round 2 (the same reviewer's re-check, `Sketches/G160/raw-origins-review-2.md`): B1–B5 resolved; accept with two corrections,
both in records and the plan, folded:

- R2-B1: `Dictionary.tryInsert`, `insertOrReplace` and `DictionaryStorage.removeEntry` take the exclusive handle before borrowing
  `self@follow@ref`, which A5 rejects once the handle retains its Loan; G160-3 searches first and takes the handle afterwards, and
  §8 states the change.
- R2-B2: `AdaptationDuring` fires only for the ungrouped spelling, which stays invalid; it is no longer listed as a gap.

Non-blocking R2-N2 to R2-N6 (a local borrow's Origin and slot projections in §5.7 Origins; fits compare Origins only, `null`
included; an Origin relation error rather than Refuted; §15.6.2 counts a shared-Origin raw pointer; "the last borrow or raw
pointer") are folded into SPEC. R2-N1 (review folds shown as owner decisions): the rows D5, D13 and D15 name the review finding
that refined them, and D15's refinement was put to the owner. No further round is needed.
