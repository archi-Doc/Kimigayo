# Raw Pointer Rules Left Open by G160

Status: 取り込み済み (closed 2026-10-22). The owner chose OQ-R1 (a), OQ-R2 (a), OQ-R3 (b) and OQ-R4 (a) (verbatim 「以下で決定します」;
DECISIONS R4-M105). Integrated into SPEC §5.7: 2.1 A and 2.2 A. Transferred: 2.3 B to G160-4's design, 2.4 A to BU's implementing
unit (DIAGNOSTICS). Not implemented. Date: 2026-10-22. Base: `dev` `127b6f0e`.
Written by the R4 planning session on the owner's instruction (2026-10-21): 「この勢いで、先ほどの新しく挙げる候補 2、4、5、6の提案書も書いてください。」
(candidate 4). Round 1 was not accepted by its independent review (`../Sketches/R4 Proposals 2026-10-21/review-3.md`, RP-B1 …
RP-B4); this round adopts that review's shorter formulations, and its own review (`review-rp2.md`: ACCEPT WITH FIXES, RP2-B1 …
RP2-B3) is applied. BU's planning session checked round 1's facts against its G160-1/2
design B+ and has since chosen B′+ (raw Origins in a separate `BoundType.RawOrigin` member). §2.1 and §2.2 are BU's G160-1 C1
work, best decided before C1; §2.3 belongs to G160-4 (R4). "(H)" marks a claim no run has checked.

## 1. Problem

[G160](../Changes/2026-10-21%20G160%20Raw%20Reach%20and%20Raw%20Origins.md) (DECISIONS R4-M95) put raw Origins into the SPEC
([§5.7](../../docs/spec/05-raw-pointers-and-unsafe-memory.md#57-raw-pointer-origins),
[§5.2.1](../../docs/spec/05-raw-pointers-and-unsafe-memory.md#521-access-conditions)). Four questions stayed open:

| # | Form | State |
| --- | --- | --- |
| 1 | Writing, borrowing exclusively or constructing a `during self` layer through no dereference outside a constructor or `drop` (a Field of an owned local, an enum Case payload; an owned `self: Self` receiver is immutable, §7.2, so it is not open) | The owner's Q-O4 (B+ `design-final.md` §0): a located `Unsupported_Kd` "until SPEC states them" |
| 2 | A raw layer whose formed Origin would be an intersection: a typed path `P@raw` or a `during self` Field read through a borrow whose outer Origin is an intersection, and a substitution that binds a raw layer's slot or input to a meet (`Lens.init(choose(x@ref, y@ref))` then `lens.at`; `dataOf(choose(x@ref, y@ref))` with `dataOf<T>(b: ref/Buffer<T>) -> raw/T during b`) | The owner's Q-O5 for the first two; B+'s `Formed`, applied at every substitution (G1), makes the third the same located `Unsupported_Kd` (U2) |
| 3 | A pointer formed through an own step used after the owning value leaves its holder | §5.2.1 condition 3: undefined behavior, no diagnostic; G160 D7 "obligation, not checked", and its §7 "a static check of D7: later precision candidate" |
| 4 | A raw pointer hidden in a Field of a nominal Type | §5.7 "Calls" and condition 5: the body cannot trace it; G160 §6 plans, as Advice, "a Field a `drop` releases without `during self`" |

B+'s U1 (a pair input whose admitted cases mix modes) is an implementation limit of `RawStep`, not a SPEC gap.

## 2. Proposals

### 2.1 Writing or constructing a `during self` layer through no dereference (Q-O4)

**Rule (an allowlist).** Through no dereference, a `during self` layer's Place admits reads, shared borrows and writes of `null`
only. Any other write (a source with any Origin, `static` included, or with none; a compound assignment), an exclusive borrow of
it (also as an argument of `exchange`, `swap` or `replace`) and a Case construction with any other payload are Origin relation
errors (Language). The repair goes through an exclusive borrow of the holder, whose Origin a claim can name.

The rule decides by the Place, not by fits: through no dereference the destination's raw Origin is the holder's own storage, which
no Origin expression names, so an ordinary fit by `outlives` (a `static` source would fit, §15.2.2) would put a pointer the holder
does not own into storage §5.7 counts as owned, and an exclusive borrow of the Place would let a body store any pointer there
without a claim (against D5). A constructor and `drop` keep their own route (`self` is the value constructed or destroyed, §5.7).
A Case whose payload has a `during self` layer is constructed with `null` only, until a revision adds Place Origins (Appendix D.4's
"static-Place Origins" could generalize).

```kimi
// Sketch: struct Block has `var data: raw/u8 during self`, filled by its constructor with a claim.
func reset(fresh: raw/u8, shared: raw/u8 during static)
    var b = Block.init()
    b.data = null                                   // Valid: a write of null.
    // b.data = fresh                               // Error: through no dereference only null is written.
    // b.data = shared                              // Error: also a source that would fit by outlives.
    // let w = b.data@uniq                          // Error: no exclusive borrow through no dereference.
    let r = b@uniq
    unsafe => r.data = fresh@(raw/u8 during r)      // A claim through the borrow.
```

That a write through a dereference takes the target's Origin is B+'s reading; §3 adds it to §5.7.

| Option | Content | Assessment |
| --- | --- | --- |
| **A (recommended)** | The allowlist above | Decidable; `null` stays valid; B+'s U3 becomes this Language error. Narrowings, all of forms that are a located `Unsupported_Kd` today (U3): a non-null write, a `static` source, an exclusive borrow (and `exchange`, `swap`, `replace`) and a compound assignment through no dereference, and a Case construction with a non-null payload. A grep finds no library or milestone program among them: the library's one `var` raw Field (`DictionaryStorage.Handle.buffer`) is written only through `(*handle)`, and the milestones contain no `raw/` |
| B | A Place Origin: in a claim, `during b` names the storage an owned Place `b` owns | Expressive, one claim form; a new Origin kind close to Appendix D.4's deferred line, and a Case expression has no receiving Place until it is stored |
| C | Keep B+'s located `Unsupported_Kd` (no SPEC statement) | Leaves the gap the owner asked SPEC to close |

### 2.2 Formed intersections (Q-O5 and the substitution route)

**Rule.** "A formed raw Origin is the outer Origin of the borrow it is formed through, whatever its form; `during` writes one
atom." It covers typed paths, `self` reads and substitution alike.

- **Supersedes G160 D18 for formed Origins** ("No intersection is a raw Origin", recorded in R4-M95; the adopting DECISIONS entry
  supersedes R4-M95's "no intersection" for formed Origins). G160 records no reason for D18; this reconstruction keeps it for
  written Origins: an intersection written with `during` may belong to no single borrow. It does not hold for a formed one, which
  belongs to the borrow it is formed through, and B+'s `Formed` carries that borrow's mode, so a formation through a shared borrow
  is `Shared(L)` and §5.7's Authority error stays (`*dataOf(choose(x@ref, y@ref)) = v` is refused).
- **B+ amendment:** `RawStep` gains an explicit intersection row: Exclusive, because `Formed` wraps every shared formation as
  `Shared(L)` (BU's reading made explicit).
- **Cost:** a formed `raw/T during (x and y)` in a local's inferred Type is displayed but cannot be written back (§15.3.1). A named
  local or projection already denotes such a region as one atom (`during r` for `let r = choose(…)`, `x.source`), so only temporaries
  strictly need a formed intersection.

| Option | Content | Assessment |
| --- | --- | --- |
| **A (recommended)** | The rule above | One reading for three routes; keeps the Authority error; supersedes D18 for formed Origins only |
| B | The pointer states no Origin on all three routes | Total; but loses §5.7's Authority error for shared intersections on each route, and dropping it at substitution makes `*dataOf(choose(x@ref, y@ref)) = v` writable in Binding |
| C | A Language error | Rejects forms §5.4 and §5.7 otherwise admit, only because the Origin cannot be written |

### 2.3 Own-step pointers after the owner leaves its holder (D7)

**Meaning today.** §5.2.1 condition 3: a pointer formed through an own step "has authority only while the owning value stays in
the holder it was reached from". Authority also ends when the value leaves by Take, `exchange` or `swap`, and own storage ends at
replacement or destruction (condition 2).

**What a check needs.** A5's address (R4-M95, G160 §9.2) is a non-conflicting element naming the root holder, and `b.data`
carries the same element as `b@raw`; §5.2.1's Local-variables example (a pointer to a local used after the local's value is moved
and replaced) requires that element not to conflict with a Move. A check therefore needs an own-step marker on the address,
carrying the owning value's Place and compared by §15.6.2's overlap rules (with `p` read from `b.inner.data`,
`consume(b.other@move)` must not affect it). That changes the owner-approved address representation and G160 D1 ("an address
conflicts with nothing").

| Option | Content | Assessment |
| --- | --- | --- |
| A | An error: an operation that ends the owning value's stay in its holder while a value carrying its own-step address is live | Rejects defined programs: after the Move, `p == null`, `p == b2.data`, `p@usize` and passing `p` to a call that never dereferences it are defined (§5, §5.1); restricting the error to uses that access through `p` needs the second liveness kind G160 §7 rejected |
| **B (recommended)** | A Warning with the same condition, over Move, Take, `exchange`, `swap`, replacement and destruction of the owning value's Place or an ancestor of it (not any overlapping Place), and a call that takes the owning value by `uniq` (it may replace it) | Rejects nothing, so its over-approximation is harmless; integer round trips and pointers hidden in nominal Fields stay undefined behavior, not warnings (the check is defined over reach) |
| C | No change (G160's obligation) | — |

B is decided with G160-4: its design (R4, full process) adds the own-step marker. Adopting B asks the owner to approve a change to
the owner-approved address representation (R4-M95, G160 §9.2 and D1), and a closed-vocabulary entry if one is added.

### 2.4 Hidden raw pointers (condition 5)

Unannotated `raw/T` is G160's canonical spelling of "unstated" (D3), which G160 calls sound; a warning on it would leave no
warning-free spelling for a legitimately unstated Field, and repairs such as `during self` or `during static` would be false claims
there. G160 §6 already plans a meaning-based diagnostic.

| Option | Content | Assessment |
| --- | --- | --- |
| **A (recommended)** | G160 §6's diagnostic: a raw Field in a struct's or enum's inline storage that the Type's `drop` releases without `during self`, as an Information diagnostic (§23.1.1 has no Advice severity), with the repair "state `during self` and add the claim at each constructor fill" | Meaning-based: a `drop` that releases the Field shows ownership, and `drop` already makes the Type non-Copy (§16.3), so the repair is never false. The constructor trigger ("a constructor fills it from `Raw.allocate`") is not proposed: the storage may be handed to another owner, and a `Self is Copy` Type would become invalid under `during self` (§5.7), so its repair could be false (§23.1.3). A new DIAGNOSTICS code (vocabulary: the owner) |
| B | A broader Warning: a raw Field without an Origin whose pointee Type may carry a Loan (condition 5's actual concern) | Closer to condition 5; still warns legitimately unstated Fields of Loan-carrying pointees (H) |
| C | No diagnostic | — |

## 3. SPEC text (planned mapping)

| Item | Sections |
| --- | --- |
| 2.1 A | §5.7: a new "Writing a Field" bullet (through a dereference the target's Origin; through no dereference only `null`; the repair) and one sentence on Case payloads |
| 2.2 A | §5.7 "Origins": "`during` writes one atom; a formed raw Origin is the outer Origin of the borrow it is formed through, whatever its form"; "Typed paths", "Reading a Field" and "Calls" cite it |
| 2.3 B | §5.2.1 condition 3: the Warning (required warnings are stated in their chapters, as §17.5 and §14.3 do) |
| 2.4 A | §5.7 "Slots and ownership": the Information diagnostic |

A shorter variant for 2.3 and 2.4: both as implementation diagnostics only (DIAGNOSTICS), with no SPEC text.

## 4. Consequences for the implementation (H)

- **BU (G160-1 C1, with B′+):** 2.1 A turns U3 into the Language error with its repair candidate. BU's check against B′+
  (2026-10-22): implementable exactly at U3's sites (assignment and compound-assignment targets, the Case construction payload, the
  argument adaptation of `exchange`, `swap` and `replace`, `@uniq`/`@objuniq` of a Place), each one `RawOrigins` check (the target
  layer's stored raw Origin is `Holder` and its receiver's `PathOrigin` is none), admitting a `null` source and otherwise recording an
  Origin relation failure fact directly, with no relation, premise or row; no new representation. `null` is read from a published
  fact, not from syntax. `b.data@uniq` has no faithful Type (`uniq/(raw/u8)` would admit any unstated pointer through it, and
  `Holder` never appears in a body Type), which is why the allowlist refuses it. Reads give the unstated pointer (`b.data`:
  `raw/u8`), and a write through a dereference takes the target's Origin (`r.data` reads `r`'s outer Origin with `r`'s mode). 2.2 A lets `Formed` accept a meet
  on every route and adds `RawStep`'s intersection row, deleting U2's intersection half (its pair-follow half stays) and the
  substitution case BU listed as item 11.
- **R4 (G160-4):** 2.3 B's own-step marker and the Warning, in G160-4's full-process design.
- **2.4 A:** a declaration-time Information diagnostic in Binding (BU), with its repair candidate in DIAGNOSTICS' catalog.
- **Records:** the adopting DECISIONS entry supersedes R4-M95's "no intersection" for formed Origins (2.2) and, under 2.3 B, records
  the owner's approval of the address change.

## 5. Principles

1. **One concept, one canonical form.** One reading of a formed raw Origin on every route; claims keep one spelling; the canonical
   "unstated" form is not warned.
2. **Local reasoning.** A write through no dereference is decided at the write; an own-step pointer's lost authority is flagged at
   the operation that ends it.
3. **Explicit semantics.** Q-O4 and Q-O5 get verdicts instead of `Unsupported_Kd`; D18's scope is stated.
4. **Compiler Server Protocol.** Errors and warnings with repairs that are true (a write through a borrow; `during self` for an
   owned buffer).

## 6. Owner questions

| ID | Question | Options | Recommendation |
| --- | --- | --- | --- |
| OQ-R1 | Q-O4: a `during self` layer written, borrowed exclusively or constructed through no dereference? | (a) the allowlist: reads, shared borrows and `null` writes only; every other write (a `static` source included), an exclusive borrow (also in `exchange`, `swap`, `replace`) and a non-null Case payload are Origin relation errors, repaired through a borrow; (b) a Place Origin; (c) keep the located `Unsupported_Kd` | (a) |
| OQ-R2 | Q-O5 and substitution: formed intersections? | (a) a formed raw Origin is the borrow's outer Origin, whatever its form (supersedes D18 for formed Origins); (b) no Origin; (c) an error | (a) |
| OQ-R3 | D7: own-step pointers after the owner leaves its holder? | (a) an error (rejects defined programs); (b) a Warning, which approves a change to the owner-approved address representation; (c) unchanged | (b), designed in G160-4 |
| OQ-R4 | Hidden raw pointers? | (a) G160 §6's diagnostic, the `drop` trigger only, as Information; (b) a broader Warning; (c) none | (a) |

## 7. Unverified

- That no library or milestone program writes a non-null `during self` layer through no dereference (2.1) or forms a raw
  intersection on any route (2.2).
- 2.3's census and the own-step marker's cost (G160-4).
- The library's raw Fields that stay unannotated after G160-3 (G160 §4: `dispatch` and `pending`, "unannotated if unknown").
