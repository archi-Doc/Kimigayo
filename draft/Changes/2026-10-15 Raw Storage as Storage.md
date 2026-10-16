# Raw Storage as Storage

**Status:** proposal, final revision of 2026-10-16. Not integrated and not implemented; the owner decides adoption.
Two independently written designs were compared; section 3.3 keeps their rejected choices.

`NN:line` is a line of SPEC or IMPL chapter NN (chapters 20–21 are IMPL), and `file:line` a line of another file, both
at `8ffc296b`. § numbers are SPEC or IMPL sections; this proposal's own sections are written "section 3.3". **N** marks
a normative change, **D** a reproducer, **E** an example, **RZ** a follow-up and **(H)** a hypothesis.

## 1. Summary

Distinct zero-sized values may share an address (SPEC §4.1), but SPEC 5 keeps raw-storage state per address, so
initialization, Take, borrowing, `Raw.slice` and release cannot say which value they act on. The SPEC's own
`Buffer<T>` is undefined for a zero-sized Non-Copy `T` (D1), and D2–D6 show further cases.

The proposal makes raw storage ordinary Storage:

- **Places.** A value placed in raw storage occupies a Place, and each component occupies a part of it (N1). The
  per-Place rules of initialization, completeness, capability, Loans and destruction then apply to raw storage.
- **Designation.** A raw Place designates one Place within the pointer's provenance, not an address (N5). Behavior
  stays defined while some single choice of designated Places lets every operation meet its rules (N8).
- **Placement and completeness.** One placement rule covers `Raw.initialize`, relocation and contract construction:
  the Place being filled is written under the raw access conditions and holds no value, and no Initialized Place other
  than its ancestors holds its bytes. Whole-value raw access needs a complete value (N9).
- **Bytes.** Type-punned access (N14) and relocation by byte copy (N15) become explicit rules.
- **Overlap.** One definition, in §3.4 (N2).

D1–D5 become valid, and D6 does after RZ4–RZ6; section 2.3 also gives the verdicts of related programs.

**Owner decisions:**

- Adoption of section 4, including the SETTLED entry and the PLAN row RZ.
- Five deliberate choices:
  - Placement creates new Places only in raw storage, so reusing a moved-out local for another Type becomes undefined.
  - Integer-derived pointers need the implementation's guarantee also for zero-sized access, which is stricter than
    Rust.
  - A byte copy whose contract states no construction relocates values and never duplicates them.
  - The release precondition applies to zero-byte results too; a zero-byte result may still be released more than
    once.
  - Release needs no destruction of a Non-Copy value whose destruction would only destroy Copy components, such as the
    library's `Links`; a placed `Array`, `string`, handle or closure still needs it.

## 2. Problem

### 2.1. Required meaning

Every value of any size has its own initialization state, construction completion, components, Loans and destruction
responsibility (SPEC §3.4, §15.1, §16). IMPL §21.1.1 states it for zero size: "Zero-sized values keep their evaluation,
initialization, ownership, Loans and destruction." Stride may be zero (§4.1), and `p[n]` is exactly `*(p + n)` (§5.3).

The family governed: every raw operation (formation, read, Copy, replacement, Take, in-place destruction, shared and
exclusive borrow, `Raw.initialize`, `Raw.slice`, `Raw.release`), every safe operation on the same values, overlap,
data races (§22.3.7) and relocation.

### 2.2. Cause

§3.4 lists the kinds of Storage (03:243) without raw storage, so a value placed through a raw pointer has no Place.
SPEC 5 therefore keeps raw state per "storage" at an address, and per-address state cannot tell zero-sized values
apart. A byte copy creates no value either (05:36), so moving values with a byte copy has no meaning at any size.

### 2.3. Programs

The examples use one shared declaration:

```kimi
struct Token                                // Zero-sized and Non-Copy; its drop is observable.
    drop => Console.writeLine("drop")
```

**Reproducers:**

| | Program | Current SPEC | Proposed |
| --- | --- | --- | --- |
| D1 | §5.6 `Buffer<Token>`, one push, then drop | Undefined: `_ = data[0]@move` dereferences one past the end (05:34) of zero-length provenance (05:126) | Valid: one drop per pushed value |
| D2 | `Buffer<()>` pushed twice | Undefined: the storage already holds a value (05:128) | Valid: two Places at one address (E1) |
| D3 | Owning iteration of `Array<Token>`: `takeFirst` (Storage.kimi:107), then the remainder's drop (:42) | Undefined: the first Take leaves the storage Uninitialized (05:67) | Valid: each Take designates another Place; RZ6 still has to state that :43 releases an `allocate` result |
| D4 | `Storage.tryGetPairUniq` over zero-sized elements (Storage.kimi:182) | Undefined: the second `uniq` accesses the first one's storage (05:63) | Valid: two distinct Places |
| D5 | `Raw.allocate<(i64, Token)>(1)`, initialize, `_ = (*p).1@move`, release | Undefined: the element is neither destroyed nor taken, and SPEC 5 keeps no component state (05:127) | Valid: component Places (E3) |
| D6 | `Dictionary<K, V>` growth (DictionaryStorage.kimi:91) and compaction (:135), any `K` and `V` | Undefined: a byte copy places nothing (05:36), so new slots hold no value and the released buffer still does | Valid once RZ4 places the `Links` values and RZ5 and RZ6 give the pointer read from the Handle's `buffer` Field and the `tryAllocateBytes` result their provenance: relocation moves the values of each copied range (E5) |

D6's layout (BodyLowering.Dictionaries.cs:21-26): a slot holds 16 bytes of `Links`, then the key and the value, each
aligned, with the stride rounded up to the slot alignment (at least 8). In `Dictionary<i64, ()>` the `()` therefore lies
at `slot + 24 = slot + stride`, the next slot's start.

**Related programs:**

| Rule | Program | Current SPEC | Proposed |
| --- | --- | --- | --- |
| Designation (N5, N8) | Read before any placement; forging a Non-Copy, Loan-bearing or `Never` zero-sized value | Undefined (05:43) | Undefined: no Place holds a value |
| | `x[0]@raw` reaching a zero-sized `x[1]` at the same address | Unclear at zero size (05:42) | Undefined: the provenance is the storage of `x[0]` |
| | Two unordered threads each take the only zero-sized value | Not expressible (15:214) | Undefined from both Takes |
| Placement (N9) | `Raw.initialize` over bytes that another Initialized Place holds, including re-initializing a moved-out Place after another value was placed over its bytes | Undefined (05:128) | Undefined (E2) |
| | Placing a value of another Type in a declared local whose value was moved | Valid (05:128) | Undefined: placement creates new Places only in raw storage, and a later safe initialization would overlap the value |
| | `Raw.initialize` through one zero-byte result, then a Take through another result at the same address | Not stated | Undefined: each result has its own provenance (N16) |
| | Taking and re-placing a value through a pointer taken from a live `uniq` path | Valid | Valid: the Loan of the pointer's own path is exempt (condition 4) |
| Completeness (N9) | Whole-value raw read, replacement or Take of a partially taken value | Not stated (15:86) | Undefined (E3) |
| | Taking every component of a placed positive-sized value, then a whole `Raw.initialize` | Not stated | Undefined: the incomplete value still holds the bytes; place each component again. At zero size the call places a new value |
| Allocation and release (N16, N17) | More zero-sized placements than `count` | Undefined (05:128), as D2 | Valid: `count` bounds bytes, not values (E6) |
| | Release while a placed `Token` remains | Not stated | Undefined (E6); valid for `()` or for a value whose destruction would only destroy Copy components, such as `Links`; undefined for a placed `Array<Token>` |
| | Placement through a released zero-byte result | Not stated | Undefined: release ends its provenance |
| | A second release of a zero-byte result | Valid (05:127) | Valid |
| Type punning (N14) | `raw/u8` reads of an integer's bytes; two `uniq/u8` borrows of different bytes of one `u64` | Reads valid; the borrows access one storage (05:63) | Valid: punned Places that share no byte (E4) |
| | A punned read of padding, or of bytes that an enum's active Case does not use | Not stated | Undefined: such bytes represent no value; a punned write of padding is valid (E4) |
| | A punned `u8` read of the bytes of a taken component, such as `pair.1` of a `(u64, string)` after `pair.1@move` | Undefined: the storage is Uninitialized (05:43, 05:47) | Undefined: punning excludes parts that hold no value |
| | A punned write that leaves a `bool` holding 2 when it is next read | Not stated | Undefined (E4) |
| | A punned write that changes `Empty` to `Full(Token)` in a Non-Copy enum with those two Cases | Not stated | Undefined: punning constructs no payload |
| | A generic `T is Copy` read with zero-sized `T` at an address inside placed values, where no `T` is placed | Not stated | Undefined: punning needs a positive size |
| Relocation (N15) | A foreign copy of `i64` values whose contract states construction, then reads of both copies | Unclear (05:36 against 22:448) | Valid: contract construction |
| | The same copy whose contract states only a byte copy, then reads of both copies | Destination undefined (05:36) | Undefined: the copy relocates or it does not, so one side holds no value |
| | A byte copy whose choice relocates a `(i64, Token)` and, separately, its Token component | Undefined (05:36) | Undefined: a component moves only with its value |
| | Relocating a value out and back while a `ref` of it, of an ancestor or of a punned Place over its bytes stays live | Undefined (05:36) | Undefined: relocation and placement meet condition 4 |
| | A user loop that places each byte of a range | Valid | Not a relocation: it places `u8` values, and the source values stay |

## 3. Design

### 3.1. Representation and owners

- **§3.4.** Owns Storage, Place, capability and overlap (N1, N2). Raw storage gets its Places only from placement;
  other Storage keeps its declared Places, and punned Places (N14) are transient views over either.
- **§5.2.** Owns the raw-specific rules: provenance, designation, placement, type punning and relocation.
- **Other owners.** Loans (§15.6), completeness (§15.1.2), destruction (§16) and data races (§22.3.7) keep their
  owners and apply to the Places of raw storage. N13 adds contract construction, punned Places and relocation to
  completeness, and N21 states conflicts over Places.

For positive size, the address and Type determine the designated Place, apart from punned Places over the same bytes.
For zero size, designation is a choice among the Places at one address.

### 3.2. Why the choice cannot change execution (argument, no formal proof)

- **Positive size.** For a given positive-sized Type, at most one Initialized placed or declared Place of that Type
  holds given bytes, because a Type cannot contain itself inline; a placement may fill any Place of that Type there
  that holds no value, and all of them occupy the same bytes. Places punned over those bytes read and borrow the same
  bytes.
- **Zero size.**
  - Candidate Places store nothing beyond their Type, so every operation on them touches no bytes, and destruction
    runs the Type's code at the same address.
  - Whole-value access needs completeness, which removes the one case where a choice could pick different code: a
    partial against a complete value.
- **Results.** The result of a raw read or borrow takes its Origins from the pointer Type and its dependencies from
  the retained-dependency rule (05:59).

N8 therefore states, as a rule, that the choice is not part of execution.

### 3.3. Rejected options

| Option | Reason |
| --- | --- |
| A positive stride, a logical position in every raw pointer, `Raw.write`, per-address counting, a Rust-style provenance exemption or a producer list | Recorded with reasons in SETTLED (N27) |
| Designation by a protocol that the storage's owning Type declares | Validity would depend on a stated protocol, not on the program (Principle 2) |
| Bounding `P@raw` by `P` and its parts | Excludes punned Places over the bytes of `P`; the storage of `P` already excludes a zero-sized `x[1]` |
| Overlap defined in §15.6.2 | §3.4 owns Place; two owners would split one definition |
| Completeness only for raw reads, replacements and Takes | A raw borrow gives safe code the whole value, which 15:41 forbids while it is incomplete |
| Type punning for every Type | Forges Non-Copy and `Never` values; forging Copy values with invariants, Loans or provenance stays RZ5 |
| Punned writes only over Places of Copy Types | `DictionaryStorage`'s `Handle` writes pun over a `Dictionary` value, which declares no Fields; forging stays RZ5 |
| A release obligation for Copy values, or for values whose destruction would only destroy Copy components | Such values carry no destruction work |
| Relocating only entry records wholly inside the range | No entry record exists: `appendSlot` writes `Links` (DictionaryStorage.kimi:176-184), then the helper stores key and value separately (LlvmModuleWriter.DictionaryHelpers.cs:104-112), so the `()` at `slot + stride` would stay |
| Typed per-entry moves in `Dictionary` instead of relocation | Needs a walk of the live slots; growth would likely regress at O2 (H) |
| Byte copies that also duplicate Copy values | A third way to construct values (Principle 1); a contract can already state construction |
| Keeping "overlapping Storage" for data races | Two overlap notions, one undefined at zero size |
| Relocation as Take followed by `Raw.initialize` | Take needs completeness, so partial values could not move |

## 4. Normative changes

Each item names the target, the current text where one exists, and the proposed text; "…" in either stands for
unchanged text. Links in proposed text are written as they will appear in the target file. E1–E6 illustrate the
rules; they are not inserted into the target files.

### 4.1. Storage, Places and overlap

**N1** §3.4 (03:243). "…, an object payload or a materialized temporary." becomes:

> …, an object payload, a materialized temporary or **raw storage** such as an allocation, which declares no Place for
> the values placed in it ([§5.2](05-raw-pointers-and-unsafe-memory.md#52-raw-places)). A value placed in raw storage
> occupies a Place of its Type there, and each component of the value occupies a Place that is part of that Place.

**N2** §3.4 (03:253). "Place overlap follows [§15.6.2](…)." becomes:

> Two Places **overlap** when they are identical, one is an inline part of the other, or they share a byte; distinct
> zero-sized Places at one address overlap only when one is an inline part of the other.
> [§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts) states what proves that two Places
> do not overlap.

**N3** §3.4 (03:257). "leaves remaining storage Uninitialized" becomes "leaves its Place Uninitialized".

**N4** §15.6.2 (15:487) and §15.6.3 (15:515).

- "Two Places overlap when an operation on one may affect the other. Non-overlap is proven only by these rules:"
  becomes "Places overlap as [§3.4](03-types-and-values.md#34-places-and-storage) defines; non-overlap is proven only
  by these rules:".
- "and never lending a child twice is the unsafe obligation" becomes "and never lending overlapping Places to two live
  exclusive children is the unsafe obligation".

### 4.2. Provenance and designation

**N5** §5.2 (05:34). The paragraph becomes two. The one-past-the-end ban is dropped, because provenance bounds every
access and every placement:

> **Provenance** is the storage a pointer derives from and the range of it that the pointer's accesses may cover
> (condition 1 of [§5.2.1](#521-access-conditions)). A Place lies within a range when its bytes do or, if it is
> zero-sized, when its address is in the range or at its end. A Place lies within the pointer's provenance when it lies
> within the provenance's range and belongs to the provenance's storage; a punned Place belongs to the storage whose
> bytes it covers.
>
> `*p` is a **raw Place** of Type `T`, as are `p[n]` and the Fields and elements of a raw Place; a null pointer
> cannot be dereferenced. A raw Place that an operation acts on designates one Place at the raw Place's address within
> the pointer's provenance, whose Type is the raw Place's Type under normalized Type identity with Origins treated as
> equal ([§3.8](03-types-and-values.md#38-type-relations-and-operations)); the base of a Field or element projection
> designates none. Distinct zero-sized Places of one Type may share an address
> ([§4.1](04-arrays-indexing-and-slices.md#41-fixed-arrays)); a raw Place may designate any of them, and no address,
> index or Field name selects one.

**N6** §5.2.1 conditions 1–3 (05:42-44).

- "1. **Range.** The accessed range lies within the pointer's provenance: for `P@raw`, the storage of `P` (`x[0]@raw`
  cannot reach `x[1]`); …" becomes "1. **Provenance.** What the access acts on lies within the pointer's provenance
  ([§5.2](#52-raw-places)): for `P@raw`, the storage of `P` (`x[0]@raw` cannot reach `x[1]`, even at the same
  address); …". The rest of the condition is unchanged.
- "The storage is live and aligned for `T`;" becomes "The storage is live and aligned for `T`, also when `T` is
  zero-sized;".
- "has for that Place ([§13.5.5](…) object-payload table, [§11.8](…))." becomes "has for that Place
  ([§13.5.5](13-operators-and-assignment.md#1355-follow) object-payload table,
  [§11.8](11-properties.md#118-static-storage)) or, for a punned Place ([§5.2](#52-raw-places)), instead for each
  Place that holds one of its bytes."

**N7** Other references to provenance and to the raw-Place rules.

- §3.4.1 (03:275): "follow the raw-Place rules ([§5.2.2](…), [§5.2.3](…), [§5.2.1](…))" becomes "follow the raw-Place
  rules ([§5.2](05-raw-pointers-and-unsafe-memory.md#52-raw-places),
  [§5.2.1](05-raw-pointers-and-unsafe-memory.md#521-access-conditions),
  [§5.2.2](05-raw-pointers-and-unsafe-memory.md#522-borrowing-raw-places),
  [§5.2.3](05-raw-pointers-and-unsafe-memory.md#523-taking-from-raw-places))".
- §5.3 (05:73): "so a generic `raw/T` needs no size Constraint." becomes "so a generic `raw/T` needs no size
  Constraint; pointer equality then cannot find the end of a sequence of such elements, which is traversed by count."
- §5.3 (05:75): "within the pointer's provenance range (condition 1 of [§5.2.1](#521-access-conditions)) or one past
  its end" becomes "within the range of the pointer's provenance ([§5.2](#52-raw-places)) or one past its end".
- §5.4 (05:93): "access as `U` still needs [§5.2.1](#521-access-conditions)" becomes "access as `U` still needs
  [§5.2](#52-raw-places) and [§5.2.1](#521-access-conditions)".
- §5.5 (05:110): "needs a provenance guarantee documented by the implementation, which gives its range and authority"
  becomes "needs a guarantee documented by the implementation that gives the pointer's provenance
  ([§5.2](#52-raw-places)), its authority and the values placed within that provenance".
- §15.1.5 (15:97): "A raw Place keeps the obligations of [§5.2.1](…)." becomes "A raw Place keeps the obligations of
  [§5.2](05-raw-pointers-and-unsafe-memory.md#52-raw-places) and
  [§5.2.1](05-raw-pointers-and-unsafe-memory.md#521-access-conditions)."

**N8** §5.2, new paragraph after the Placement paragraph (N9):

> **Designation.** A set of operations is **closed** when it contains every operation that happens before one of its
> members ([§22.3.7](22-core-execution-and-foreign-functions.md#2237-data-races)). A closed set is **consistent** when
> some single choice of the Place each raw Place designates, the Place each placement fills and the values each
> relocation moves lets its members, safe or unsafe, meet their rules with no data race among them on the chosen
> Places. Behavior is undefined from an operation X when a closed set that contains X, but no operation that X happens
> before, is inconsistent while the set without X is consistent. The choice is not part of execution: an operation acts at
> its address as its Type directs, whatever the choice assigns to it; the choice only decides whether behavior is
> defined.

In sequential code a closed set is a prefix of the execution, so behavior is undefined from the first operation for
which no choice works.

**E1** Designation (D2–D4):

```kimi
let p = Raw.allocate<Token>(2)              // Zero bytes, so p + 1 == p.
unsafe
    Raw.initialize(p, Token.init())
    Raw.initialize(p + 1, Token.init())     // Valid: a second Place at the same address.
    let both = (p[0]@uniq, p[1]@uniq)       // Valid: p[0] and p[1] designate different Places.
    _ = p[0]@move                           // Prints "drop".
    _ = p[0]@move                           // Valid: the index selects no Place. Prints "drop".
    // _ = p[1]@move                        // Undefined behavior: no Token at p holds a value.
    Raw.release(p)                          // Valid: no value remains.
```

### 4.3. Placement, completeness and Take

**N9** §5.2 (05:36). The paragraph becomes two:

> Reads and writes follow the ordinary Copy and assignment rules. A whole-value read, replacement, Take or borrow of a
> raw Place needs a complete value
> ([§15.1.2](15-ownership-and-lifetime-analysis.md#1512-construction-and-completeness)). A raw pointer supplies no Loan,
> exclusivity or completeness proof.
>
> **Placement.** Only `Raw.initialize` ([§5.6](#56-raw-storage-operations)), relocation (below) and an operation whose
> contract states construction, such as a foreign function
> ([§22.6](22-core-execution-and-foreign-functions.md#226-foreign-function-imports)), initialize Places through a raw
> pointer. A Place **holds no value** when it is Uninitialized or Moved
> ([§15.1.1](15-ownership-and-lifetime-analysis.md#1511-initialization-state)). Initializing a Place is a write of it
> that meets the conditions of [§5.2.1](#521-access-conditions) for the pointer through which it is initialized. That
> Place must be an existing Place or, in raw storage, a new one; it must hold no value, and no Initialized Place other
> than its ancestors may hold any of its bytes. A `noinit` array ([§4.3.4](04-arrays-indexing-and-slices.md#434-noinit))
> is already Initialized.

**N10** §5.2.1 Local variables (05:47) and its example (05:54).

- "the storage stays live and Uninitialized, and a later initialization (…) fills the same storage." becomes "the
  storage stays live, the Places of those contents hold no value, and a later initialization (…) fills the same
  Places."
- "// Valid: same storage, initialized again." becomes "// Valid: the same Place, initialized again."

**N11** §5.2.3 (05:67). "`(*p)@move` takes the value and leaves the storage Uninitialized. The storage stays live;
`Raw.initialize` may fill it again." becomes "`(*p)@move` takes the value and leaves the designated Place holding no
value; the storage stays live, and `Raw.initialize` may place a value there again."

**N12** §5.6 `initialize` (05:128) becomes:

> - **`initialize`** moves `value` into a Place of `T` at `storage` under the placement rule of
>   [§5.2](#52-raw-places).

**N13** §15.1.2 (15:38) and §15.1.5 (15:86).

- "Tuple and array construction, and `noinit` ([§4.3.4](…))." becomes "Tuple and array construction, `noinit`
  ([§4.3.4](04-arrays-indexing-and-slices.md#434-noinit)) and construction that a contract states; a punned Place holds
  a complete value, and relocation keeps a value's completion
  ([§5.2](05-raw-pointers-and-unsafe-memory.md#52-raw-places))."
- "offer Take without state tracking or a completeness check ([§5.2.3](…))" becomes "offer Take without state
  tracking; completeness is the author's obligation ([§5.2](05-raw-pointers-and-unsafe-memory.md#52-raw-places),
  [§5.2.3](05-raw-pointers-and-unsafe-memory.md#523-taking-from-raw-places))".

**E2** Placement:

```kimi
var value = Token.init()
let q = value@raw
let r = Raw.allocate<i64>(1)
unsafe
    // Raw.initialize(q, Token.init())      // Undefined behavior: value's Place holds a value.
    Raw.initialize(r, 1)
    // Raw.initialize(r, 2)                 // Undefined behavior: an Initialized Place holds these bytes.
    _ = (*r)@move                           // The i64 Place now holds no value.
    Raw.initialize(r@raw/u8, 7)             // Valid: a new u8 Place over a byte that no value holds.
    // Raw.initialize(r, 2)                 // Undefined behavior: the u8 Place holds one of the bytes.
    Raw.release(r)                          // Valid: u8 is Copy.
```

**E3** Component Places and completeness (D5):

```kimi
let p = Raw.allocate<(i64, Token)>(1)
unsafe
    Raw.initialize(p, (7, Token.init()))    // Places the tuple and its two components.
    _ = (*p).1@move                         // Valid: destroys only the Token component. Prints "drop".
    let n = (*p).0                          // Valid: a Copy read of the remaining component.
    // let whole = (*p)@move                // Undefined behavior: the tuple is incomplete.
    Raw.release(p)                          // Valid: no Non-Copy component remains.
```

### 4.4. Type-punned access

**N14** §5.2, new paragraph after Designation (N8):

> **Type-punned access.** A raw Place whose Type is Copy and positive-sized may, when a read, write or borrow acts on
> it or `Raw.slice` takes it as an element, instead designate a **punned Place**: a Place of that Type at the raw
> Place's address within the pointer's provenance, over bytes that Initialized Places hold, none of those bytes in a
> part that holds no value. A punned Place exists only for the access that acts on it or the Loan that borrows it.
> Placement, relocation and release disregard its bytes and value, but its Loans count under condition 4 of
> [§5.2.1](#521-access-conditions). It holds a complete value. A read of it, also through its borrow or as a
> `Raw.slice` element, needs its bytes to represent a valid value of its Type; padding
> ([§21.1.1](../impl/21-layout-runtime-and-code-generation.md#2111-common-rules)) and bytes that an enum's active Case
> does not use represent none. A write through a punned Place, also through its borrow, must leave each Initialized
> Place that holds a changed byte with a valid value of that Place's Type, and with its active Case unchanged when it is
> an enum Place whose Type is not Copy, by the time that Place is next accessed other than through a punned Place:
> punning constructs no payload.

**E4** Type-punned access:

```kimi
#Layout("C")
struct Header
    var tag: u8 = 1
    var size: u32 = 2

var word: u64 = 0x0102
var header = Header.init()
var flag = true
let bytes = word@raw@raw/u8                 // Same address and provenance as word@raw.
let headerBytes = header@raw@raw/u8
let flagBytes = flag@raw@raw/u8
unsafe
    let low = bytes[0]                      // Valid: a u8 Place punned over a byte of word.
    let both = (bytes[0]@uniq, bytes[1]@uniq)  // Valid: the punned Places share no byte.
    headerBytes[1] = 0                      // Valid: padding may be written.
    // let pad = headerBytes[1]             // Undefined behavior: a padding byte represents no value.
    // flagBytes[0] = 2                     // Undefined behavior once flag is read: 2 is not a valid bool.
```

### 4.5. Relocation, allocation and release

**N15** §5.2, new paragraph after Type-punned access (N14):

> **Relocation.** An operation whose contract states that it copies the bytes of a source range into raw storage, such
> as a foreign function, may relocate, when it completes, values whose Places lie within the source range and the
> source pointer's provenance; a component of a relocated value moves with it and is not relocated on its own. Each
> relocated value, with its components and their state, then occupies the Place at the same offset in the destination,
> and its source Place holds no value. Each destination Place meets the placement rule, counting the relocated source
> Places as holding no value, and conditions 3 and 4 of [§5.2.1](#521-access-conditions) apply to each relocated
> source Place as to a Take.

**N16** §5.6 `allocate` (05:126).

- "allocates uninitialized storage for `count` elements." becomes "allocates raw storage for `count` elements; it
  holds no value."
- "with zero-length provenance;" becomes "with zero-length provenance of its own;".

**N17** §5.6 `release` (05:127) becomes:

> - **`release`** takes null, a zero-byte result or the start of an unreleased `allocate` result, possibly converted
>   to another raw pointer Type; it ignores null and frees nothing for a zero-byte result, which may be released again.
>   Every Non-Copy value placed in the storage must already be destroyed, taken or relocated, whole or, if destroying
>   it whole would only destroy its components
>   ([§16.3.2](16-scope-exit-and-destruction.md#1632-component-destruction)), by its remaining Non-Copy components
>   under the same rule. Afterwards every pointer into the storage is invalid.

**E5** Relocation (D6 reduced to one value pair). A zero-sized value at the end of a copied range moves with it:

```kimi
group Native
    /// Copies `count` bytes from `source` to `target`.
    ///
    /// - safety: Both ranges lie within their provenance and do not overlap.
    #LibraryImport("copier", "copy_bytes")
    public unsafe func copyBytes(target: raw/u8, source: raw/u8, count: u64) -> raw/u8

let source = Raw.allocate<i64>(1)
let target = Raw.allocate<i64>(1)
unsafe
    Raw.initialize(source, 7)
    Raw.initialize((source@raw/u8 + 8)@raw/Token, Token.init())  // A Token at the end of the range.
    _ = Native.copyBytes(target@raw/u8, source@raw/u8, 8)         // May relocate both values (N15).
    Raw.release(source)                                          // Valid: the choice relocates the Token (N8).
    _ = (*((target@raw/u8 + 8)@raw/Token))@move                  // Prints "drop".
    Raw.release(target)
```

**E6** Release:

```kimi
let tokens = Raw.allocate<Token>(1)         // Zero bytes; count bounds bytes, not values.
let numbers = Raw.allocate<i64>(1)
unsafe
    Raw.initialize(tokens, Token.init())
    Raw.initialize(tokens, Token.init())    // Valid: a second zero-sized Place.
    Raw.initialize(numbers, 5)
    Raw.release(numbers)                    // Valid: an i64 needs no destruction.
    _ = tokens[0]@move                      // Prints "drop"; one Token remains.
    // Raw.release(tokens)                  // Undefined behavior: a placed Token remains.
```

### 4.6. Loans, slices and data races

**N18** §5.2.1 condition 4 (05:45). "conflicts with no live Loan of the storage, except" becomes "conflicts with no live
Loan of a Place overlapping what it acts on, except".

**N19** §5.2.2 (05:63). "including another raw pointer, accesses the storage." becomes "including another raw pointer,
accesses a Place overlapping the borrowed one."

**N20** §5.6 `slice` (05:129). "and the `length` elements lie contiguously within `storage`'s provenance, each
holding an initialized value of `T` (for example written by `Raw.initialize`) or belonging to a `noinit` array (…)."
becomes "and for each `i` below `length`, `storage[i]` designates a distinct Place within `storage`'s provenance
holding a complete value of `T` (for example placed by `Raw.initialize`) or belonging to a `noinit` array (…); element
`i` of the result is that Place."

**N21** §22.3.7 (22:320). "Two accesses **conflict** when they touch overlapping Storage, at least one of them writes,
and …" becomes:

> Two accesses **conflict** when they touch overlapping Places ([§3.4](03-types-and-values.md#34-places-and-storage)) or
> the reference counts of one object
> ([§13.6.2](13-operators-and-assignment.md#1362-object-creation-clone-and-lifecycle)), at least one of them writes, and
> they are not both atomic accesses made by the implementation (such as `arc` count updates). Takes, destructions and
> placements count as writes; a relocation also writes its source Places, and a release writes every Place in its
> storage.

### 4.7. Appendix E, IMPL, LIBRARY and library comments

**N22** Appendix E (E-terminology.md:32, :91 and new rows).

- Conflicting access: "Two accesses to overlapping Storage, at least one a write, not both atomic implementation
  accesses." becomes "Two accesses to overlapping Places or to one object's reference counts, at least one a write,
  not both atomic implementation accesses."
- Raw pointer / raw Place: "`*p`, `p[n]` or a part of one." becomes "`*p`, `p[n]` or a part of one, designating a Place
  at its address."
- New row before Override: "| Overlap | Identical Places, a Place and an inline part of it, or Places that share a
  byte. | [§3.4](../03-types-and-values.md#34-places-and-storage) |".
- New row before Raw pointer: "| Punned Place | A transient Place of a Copy, positive-sized Type over bytes that
  Initialized Places hold. | [§5.2](../05-raw-pointers-and-unsafe-memory.md#52-raw-places) |".
- New row after Raw pointer: "| Raw storage | Storage, such as an allocation, that declares no Place for the values
  placed in it. | [§3.4](../03-types-and-values.md#34-places-and-storage) |".

**N23** IMPL §21.1.1 (21:15). "Distinct zero-sized Places may share an address." becomes "Distinct zero-sized Places
may share an address; a raw Place designates a Place, not an address
([§5.2](../spec/05-raw-pointers-and-unsafe-memory.md#52-raw-places))."

**N24** LIBRARY §5.4 Raw table (LIBRARY.md:509-512), the guarantee column:

| Function | Guarantee |
| --- | --- |
| `allocate` | Safe. Allocates raw storage for `count` elements; it holds no value. A negative count, an overflow of `count * stride(T)` or an allocation failure Aborts. Zero bytes allocate nothing and return a nonnull address aligned for `T`, with provenance of its own. An element alignment above 16 is unsupported at compile time. |
| `release` | Releases an `allocate` result at its start address without destroying any value. Null is ignored, and nothing is freed for a zero-byte result, which may be released again. Every Non-Copy value placed in the storage must already be destroyed, taken or relocated, whole or, if destroying it whole would only destroy its components, by its remaining Non-Copy components under the same rule (SPEC §5.6). |
| `initialize` | Moves `value` into a Place of `T` at `storage`, an existing one or, in raw storage, a new one, destroying nothing. The write meets the raw access conditions: `storage` is live, aligned and writable, the Place lies within its provenance, and no live Loan conflicts. The Place holds no value, and no Initialized Place other than its ancestors holds any of its bytes (SPEC §5.2, §5.2.1). |
| `slice` | Forms a shared Slice of `length` elements at `storage` without reading them; `s` is fixed by the caller's expected Type. A positive length requires a nonnull, aligned `storage` with `length * stride(T)` fitting in `isize`, such that each `storage[i]` below `length` designates a distinct Place that holds a complete, valid value or belongs to a `noinit` array, and no element is written for `s`; a zero length accepts any storage. |

**N25** `src/Kimi/Library/RawOperations.kimi:2-13`, comments only, STYLE §6.2 form, within 120 columns:

```kimi
/// Allocates raw storage for `count` elements; it holds no value.
///
/// A zero-byte request allocates nothing and returns a nonnull address aligned for `T`, with provenance of its own.
///
/// - abort: `count` is negative, `count * stride(T)` overflows, or the allocation fails.
public func allocate<T>(count: isize) -> raw/T
/// Releases storage that `allocate` returned, at its start address, without destroying any value.
///
/// Null is ignored, and nothing is freed for a zero-byte result, which may be released again. Afterwards every
/// pointer into the storage is invalid.
///
/// - safety: Every Non-Copy value placed in the storage is already destroyed, taken or relocated, whole or, if
///   destroying it whole would only destroy its components, by its remaining Non-Copy components under the same rule
///   (SPEC 5.6).
public unsafe func release<T>(storage: raw/T)
/// Moves `value` into a Place of `T` at `storage`, an existing one or, in raw storage, a new one, destroying nothing.
///
/// - safety: The write meets the raw access conditions (SPEC 5.2.1): `storage` is live, aligned for `T` and writable,
///   the Place lies within its provenance, and no live Loan conflicts. The Place holds no value, and no Initialized
///   Place other than its ancestors holds any of its bytes (SPEC 5.2).
public unsafe func initialize<T>(storage: raw/T, value: T)
/// Forms a shared Slice of `length` elements starting at `storage` without reading them; the caller fixes the
/// result Origin `s`.
///
/// - safety: `length >= 0`. A positive `length` needs a nonnull, aligned `storage` with `length * stride(T)` fitting
///   in `isize`, each `storage[i]` below `length` designating a distinct Place that holds a complete, valid value or
///   belongs to a noinit array, and no element is written for `s` (SPEC 5.6). The programmer writes valid values
///   before reading noinit elements through this view.
public unsafe func slice<T>(storage: raw/T, length: isize) -> Slice<T> during s
```

**N26** Other library comments.

- `StorageOperations.kimi:33-34` (`transferBytes`): "…whatever their initialization state; no responsibility moves and
  no user code runs." becomes "…whatever their initialization state; it may relocate the values in the source range
  (SPEC 5.2) and runs no user code."; rewrap within 120 columns.
- `StorageOperations.kimi:36-37` (`placeEntry`): "and transfers `key` and `value` into it." becomes "and places `key`
  and `value` in it (SPEC 5.2)."; rewrap within 120 columns (the line is 121 today).
- `src/Kimi/Library/README.md:107`: "transfers the acquired pair" becomes "places the acquired pair".
- `Storage.kimi:202`: "// Logical positions, not pointer equality, prove disjointness even when E has zero size."
  becomes "// Distinct positions let the raw Places designate distinct Places, also when E is zero-sized (SPEC 5.2)."

### 4.8. SETTLED and PLAN

No DECISIONS entry is needed: DECISIONS records compiler decisions only, and this is a language rule. No existing
SETTLED entry is reopened; "A no-leak guarantee" stays, since leaking raw storage stays valid. STATUS does not change:
no verified boundary moves.

**N27** SETTLED, new entry after the last one:

````markdown
## Zero-sized raw values told apart by stride, pointer position, `Raw.write`, count, a provenance exemption or a producer list

- **Problem:** Distinct zero-sized values may share an address (§4.1), so raw-storage state kept per address cannot tell them apart: initialization, Take, borrowing, `Raw.slice` and release cannot say which value they act on.
- **Example:**

  ```kimi
  let p = Raw.allocate<()>(2)
  unsafe
      Raw.initialize(p, ())
      Raw.initialize(p + 1, ())   // p + 1 == p: per-address state already holds a value here.
  ```

- **Proposed fix:** Give zero-sized Types a positive stride; carry a logical position in every raw pointer; replace `Raw.initialize` with a `Raw.write` that overwrites without destroying; count zero-sized values per address; exempt zero-byte accesses from provenance, as Rust does; or name "raw elements" by a list of storage producers.
- **Why not applied:** A positive stride changes §4.1 and the cost of zero-byte allocation. Pointer positions add state to arithmetic, casts, projection and foreign contracts. `Raw.write` still leaves Take, borrowing and `Raw.slice` without a target and adds a second way to abandon a value (Principle 1). Counting restates the capability and Loan rules for each counted group of zero-sized values. Rust can exempt zero-byte accesses because its abstract machine keeps no memory state for zero-byte ranges; Kimigayo keeps initialization, Loans and destruction per zero-sized Place, so an exemption would need a second, size-dependent bound on the Places a pointer reaches, and without it equal zero-byte results would share Places of different owners. A producer list misses storage that contracts provide and values that relocation moves. Raw storage is Storage instead: a placed value occupies a Place, and a raw Place designates one (§3.4, §5.2).
````

**N28** PLAN §7, one row after G87 (199 lines in total):

> | RZ | [Raw storage as Storage](../../draft/Changes/2026-10-15%20Raw%20Storage%20as%20Storage.md) (SPEC §3.4, §5.2)
> leaves eight follow-ups (its section 7): RZ1 borrow authority for raw Places; RZ2 native tests that use
> `VirtualAlloc` pages as values; RZ3 `_ = (*p)@move` against §14.2.4; RZ4 `Links` placement in `DictionaryStorage`;
> RZ5 forging by type-punned access (H); RZ6 storage-boundary contracts; RZ7 Origins of values placed in declared
> Places; RZ8 the stale `placeValue` in the Library README. | Specification and Library, unscheduled |

## 5. Consequences

- **Removed text.**
  - The one-past-the-end dereference ban (05:34).
  - The per-storage state sentences of §5.2, §5.2.2, §5.2.3 and §5.6 (D1–D4).
  - §15.6.2's own overlap definition (15:487), and "overlapping Storage" in §22.3.7 and Appendix E. The §3.4
    definition replaces them.
- **Code.** No compiler, IR, runtime or API code changes, and this change adds no library code (RZ4 will); only the
  comments of N25 and N26 change. Native O2 code, allocation counts and compile time are unchanged.
- **Size.** About +1,000 SPEC and IMPL words and +550 words in SETTLED, LIBRARY, PLAN and library comments (rough
  counts of the proposed texts); measure at intake.
- **Integration.**
  - SPEC, IMPL, LIBRARY, SETTLED and PLAN are documentation, checked by diff and reference review.
  - The `.kimi` comment edits fall under the Kimi library row: selected checks with `KimiLibraryShapeTest` and
    `ArchitectureRulesTest`, then a Session.
  - Record the intake in `draft/INTEGRATED.md` and close the proposal into `draft/Changes` as 取り込み済み (RZ1–RZ8
    transferred to PLAN RZ), which the PLAN link assumes.
  - Refresh the Japanese editions, which keep the replaced rules: SPECJ.md:249, 489–498, 524–527, 1302, 1323,
    1422–1424, 1432, 1776, 2045, 2405 and 2469, plus Appendix E rows for Overlap, Punned Place and Raw storage;
    SPECJ2.md two lines later in each case.
- **Principles.**
  - One Place concept and one placement rule (Principle 1).
  - An access is judged by the state, capability and Loans of the Place its raw Place designates (Principle 2).
  - Completeness, placement, punning and relocation are stated rules, not inferred from addresses (Principle 3).

## 6. Sites

**Searched** in `docs/spec`, `docs/impl`, `docs/*.md`, `docs/examples` and `src/Kimi/Library`: "raw storage", `Raw\.`,
"the storage", "storage holding", "one-past", "overlapping Storage", "Uninitialized", "zero-siz", "zero-byte",
"transferBytes", "completeness check", "lending a child", "logical position", "overlap", "relocat", "contiguous" and
links to §5.2.1. Changed sites: N1–N28.

**Checked and unchanged:**

- **SPEC §4.**
  - §4.1, §4.3.4 (04:91 already covers reads through raw pointers) and §4.8.5.
  - §4.8.1 (04:352) and §4.8.4 (04:424): N5 and N20 give element identity; UniqSlice reaches its elements through raw
    Places.
  - §4.9.7 (04:575): collection Relocation stays under the collection contracts (RZ6); Dictionary's `transferBytes` is
    a §5.2 relocation (N26).
- **SPEC §5.**
  - The §5.6 `Buffer` example, now valid for every `T`.
  - 05:59: "raw storage" now has the N1 meaning; the obligation keeps its scope, the Types that own allocations.
- **SPEC §11 to §22.**
  - §11.2.2: no per-storage wording.
  - §13.7.1 (13:569): partial-cleanup replacement stays safe-code only; raw whole replacement needs a complete value.
  - §14.2.4 (RZ3), and 16:205 and §16.3.2, which state no raw rule.
  - §22.6 (22:448): its construction is a contract placement.
- **IMPL.** 21:14 (padding, which N14 now cites) and §21.4.3 (N16 states zero-byte provenance in the SPEC).
- **Other documents.** STATUS:279, GUIDE and STYLE §4.5 state no replaced rule.
- **Library.**
  - The Library README :61 (positions, not pointer equality, stays true).
  - `Array.kimi` (:195: the source Loan still excludes overlapping Places) and `Dictionary.kimi` (:58-61: the old value
    is taken before the new one is placed).
  - `DictionaryStorage.kimi` (:71 relies on RZ4; :123-124 stays true).
  - `Storage.kimi:3-4`: "initialization" understates the obligations but contradicts no rule.
  - `Raw.kimi` (:1-2 already exceed 120 columns).

No test asserts the replaced comments.

## 7. Open issues and unverified points

**Follow-ups** (PLAN RZ):

- **RZ1** Borrow authority for raw Places: §5.2.1 condition 3 names no borrow.
- **RZ2** Native tests use `VirtualAlloc` pages as values without placement or a construction contract:
  - `GenericPointerEmissionTest.ConcreteReadsPreserveCopyMoveAndCleanup`;
  - `RawPlaceAccessTest.AnExplicitTakeMovesAndDestroys`;
  - the `ForeignEmissionTest` pointer read and write cases.

  Place the values with `Raw.initialize`, or state construction in each import's `- safety:` contract.
- **RZ3** `_ = (*p)@move` destroys in place (§5.2.3), while §14.2.4 transfers and then cleans up.
- **RZ4** `DictionaryStorage` writes `Links` Fields where no `Links` value was placed: `appendSlot`
  (DictionaryStorage.kimi:176-184) writes them, and they are read at :133, :172, :206-219 and Storage.kimi:87, :132,
  :141 and :150. This predates the proposal.
  - Each slot needs one `Links` value, placed when the slot is first used.
  - `clearLinks` leaves the slots below the old `used` populated, so the `free == 0` branch does not identify fresh
    slots. Keep a high-water mark, or take or destroy the `Links` values in `clearLinks`.
  - `compact` then acts on the relocated `Links`. `Links` declares no `drop` and has only Copy Fields, so destroying
    it would only destroy Copy components, and under N17 the releases need no prior destruction of it.
- **RZ5** Type-punned access may forge values (H):
  - Copy Types with constructors (invariants);
  - references and Slices (Loans);
  - raw pointers (provenance);
  - writes over Types with constructors, and over the bytes of other Non-Copy values, such as an Array's length
    metadata (§4.7.3).

  The N14 sentence that a punned Place holds a complete value, and its §15.1.2 counterpart (N13), are the ones to
  restrict.
- **RZ6** The storage-boundary contracts (`borrowStorage`, `borrowStorageUniq`, `ownStorage`, `tryAllocateBytes`;
  StorageOperations.kimi) do not state:
  - the write authority that Dictionary.kimi:59-61 and DictionaryStorage.kimi:35-37 use;
  - the capacity range that Array.kimi:179 and :197 write;
  - that the Array buffer that Storage.kimi:43 releases is an `allocate` result;
  - the provenance of `tryAllocateBytes`;
  - that the element storage of `Array` and `Dictionary` that these contracts expose is raw storage (SPEC §3.4).

  This predates the proposal.
- **RZ7** Origins of values placed in declared Places: no rule states that a value placed through a pointer converted
  only in Origins must fit the declared Place's Origins (05:59 covers raw storage only).
- **RZ8** Outside the cause: `src/Kimi/Library/README.md:108` still names `Storage.placeValue`, which no longer exists.

**Unverified points:**

- The section 3.2 argument has no formal proof.
- Closed-set designation and data races across workers are not tested. User raw pointers refute Transferable, and
  Appendix D defers raw pointers in concurrent execution. The collection raw pointers that 15:214 exempts still reach
  workers, for example the `uniq` elements of an `Array<Token>` passed by `offloadEach`. N21's write rules keep §22.3.7
  defined for them when designation is a choice.
- The Dictionary layout and lowering were read from source, not executed.
- windows-x64 documents no §5.5 guarantee, so every access through an integer-derived pointer, zero-sized ones
  included, is undefined there.
