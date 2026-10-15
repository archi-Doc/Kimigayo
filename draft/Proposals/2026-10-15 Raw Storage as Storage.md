# Raw Storage as Storage: Zero-Size Values in Raw Memory

**Status:** proposal. The owner requested this design on 2026-10-15 and decides its adoption.

- It is not integrated into the specification and not implemented; no compiler change is needed (§11).
- Two independent designs (P1 and P2), written from the same owner decision, are compared in §8 by the AGENTS
  priorities; P2 is selected with five changes.
- An independent review (two reviewers) accepted the result with required changes. This revision applies them (§9).
  Three sentences added while applying them have not been re-reviewed (§12).

Line numbers refer to `b32f93ef`. **(H)** marks a hypothesis.

## 1. Summary and owner decisions

**Proposal.** Raw storage joins Storage ([SPEC §3.4](../../docs/spec/03-types-and-values.md#34-places-and-storage)):

- A value placed in raw storage occupies a Place of its Type there, and each of its components a Place that is part of
  it. Every existing per-Place rule then applies to raw memory: initialization state, completion and completeness,
  capability, Loans, destruction.
- A raw Place designates one Place of its Type at its address within the pointer's provenance. Distinct zero-size Places
  may share an address; no address, offset or spelling `p[n]` selects among them.
- One choice of designated Places, fixed for the operations that happen before each operation, must let every operation,
  safe or unsafe, meet its rules. The choice never changes what executes.
- Whole-value raw access needs a complete value; one placement rule covers `Raw.initialize`, relocation and contract
  construction; type-punned access and relocation become explicit rules; overlap is defined once in §3.4.

**Owner decisions this proposal needs.**

- **Adoption** of the normative changes in §4, the SETTLED entry and the PLAN row RZ.
- **Follow-ups** recorded in RZ (§12): U3 raw borrow authority, O1 a test fixture, O2 discard in place, O3 Dictionary
  link writes, O4 type-punned forging of Copy Types with constructors.
- **Japanese editions.** `docs/SPECJ.md` and `docs/SPECJ2.md` keep the replaced rules (for example SPECJ.md:489, :2043,
  :2467; SPECJ2.md:491); refresh them with the intake or note them.

**Not needed.** No DECISIONS entry: the change is a language rule (SPEC) with rejected options in SETTLED
([DECISIONS](../../docs/dev/DECISIONS.md) records compiler decisions only, lines 3-5). No STATUS change: no verified
boundary moves. No vocabulary, cap, budget or freeze change. No existing SETTLED entry is reopened; "A no-leak
guarantee" stays.

## 2. Problem and cause

**Required meaning.** Every value of any size has its own initialization state, construction completion, components,
Loans and destruction responsibility (SPEC §3.4, §3.5, §15.1, §16;
[IMPL §21.1.1](../../docs/impl/21-layout-runtime-and-code-generation.md#2111-common-rules): "Zero-sized values keep
their evaluation, initialization, ownership, Loans and destruction. Distinct zero-sized Places may share an address").
Stride may be zero (§4.1) and `p[n]` is exactly `*(p+n)` (§5.3).

**Rule family.** Every raw operation (formation, read, Copy, replacement, Take, in-place destruction, shared and
exclusive borrow, `Raw.initialize`, `Raw.slice`, `Raw.release`), every safe operation on the same values, overlap, data
races (§22.3.7) and relocation of raw-storage contents.

**Cause.** §3.4 lists the kinds of Storage (03:243) without raw storage, so a value placed through a raw pointer has no
Place, and SPEC 5 keeps raw state per "storage" at an address. Specification reproducers (not compiler faults; STATUS
records zero-size raw execution):

| | Reproducer | Failing rule |
| --- | --- | --- |
| D1 | SPEC 5.6's `Buffer<T>` with a zero-field Non-Copy `T`, one push: drop's `_ = data[0]@move` | 05:34 one-past-the-end ban with 05:126 zero-length provenance |
| D2 | `Buffer<()>` pushed twice | 05:128 "storage holding no initialized value" |
| D3 | One raw Take among zero-size elements at one address | 05:67 "leaves the storage Uninitialized" |
| D4 | `Storage.tryGetPairUniq` over zero-size elements (Storage.kimi:182) | 05:63 "no other path ... accesses the storage" |
| D5 | `Raw.allocate<(i64, Token)>(1)`, initialize, `_ = (*p).1@move` | the component is no Place |
| D6 | `Dictionary<i64, ()>` growth and compaction | zero-size values do not move with `Storage.transferBytes`; release then fails |

D6's layout is verified in source (BodyLowering.Dictionaries.cs:21-26): the value offset is `align(key offset + key
size, value alignment)` and the stride rounds that end up, so a zero-size value of `Dictionary<i64, ()>` lies at
`slot + 24 = slot + stride`, the next slot's start; zero-size keys and values of `Dictionary<(), ()>` both lie at
`slot + 16 = slot + stride`. Growth copies one range (DictionaryStorage.kimi:91) and compaction one slot at a time
(:135), then the old buffer is released.

## 3. Representation and owner

**Owner:** §3.4 (Storage, Place, overlap). **Consumer:** §5.2 (designation, placement, type punning, relocation).

**Representation.** Raw storage is Storage that declares no Place for the values placed in it, such as an allocation.
Its Places come from placement; other Storage keeps its declared Places. A raw Place designates one of the Places its
provenance reaches. Positive-size Places that hold values and are not parts of one another never share bytes, so
address and Type determine the Place; zero-size Places may share an address, so the designation is a choice.

**Behavior independence (argument, not proof).** For a positive size the designation is forced: at most one Place of a
positive-size Type holds given bytes, since a Type cannot contain itself inline. For a zero size, the candidate Places
hold values that store nothing beyond their complete Type (Fields, Origins and dependencies are Type-level, §4.1);
every operation on them touches no bytes, and destruction runs the Type's code at the same address. Raw operations are
untracked, so their code depends only on the Type and static state. Whole-value completeness (§4, N5) removes the one
case where a choice could select different code: a partial against a complete value at one address. A choice therefore
decides only whether the conditions hold, never what executes.

**Checking (implementation note).** Inside one raw storage the zero-size Places of one Type are interchangeable, so a
checker decides designation there by counting; for other Storage the candidates are the finite Places the provenance
reaches.

## 4. Normative changes

Each item gives the current text (shortened with …) and the proposed text. Links inside proposed text are written as
they will appear in the target file (relative to `docs/spec`, `../spec/` in IMPL), not relative to this proposal.

### §3.4 Places and storage (03-types-and-values.md)

**N1** (03:243). Current: "**Storage** holds values: a local, a static slot (…), a part of an aggregate, an object
payload or a materialized temporary." Proposed:

> **Storage** holds values: a local, a static slot (…), a part of an aggregate, an object payload, a materialized
> temporary or **raw storage**, which declares no Place for the values placed in it, such as an allocation
> ([§5.6](05-raw-pointers-and-unsafe-memory.md#56-raw-storage-operations)). A value placed in raw storage occupies a
> Place of its Type there, and each of its components a Place that is part of it.

**N2** (03:253). Current: "Place overlap follows [§15.6.2](…)." Proposed:

> Two Places **overlap** when they are identical, one is part of the other or they share a byte, so distinct zero-size
> Places at one address do not; [§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)
> states what proves that two Places do not overlap.

**N3** (03:257). "and leaves remaining storage Uninitialized;" becomes "and leaves its Place Uninitialized;".

### §5.2 Raw Places (05-raw-pointers-and-unsafe-memory.md)

**N4** (05:34) replaces the whole paragraph, including "Null and one-past-the-end pointers cannot be dereferenced.":

> **Provenance** is the storage a pointer derives from and the range its accesses may cover; a zero-size Place lies
> within a range when its address is in the range or at its end. `*p` is a **raw Place** of Type `T`, as are `p[n]`
> and the Fields and elements of a raw Place. A raw Place designates one Place of its Type at its address within the
> pointer's provenance. A null pointer cannot be dereferenced. Distinct zero-size Places of one Type may share an
> address ([§4.1](04-arrays-indexing-and-slices.md#41-fixed-arrays)), and a raw Place may designate any of them; no
> address, offset or spelling `p[n]` selects one.

**N5** (05:36). Current: "Reads and writes follow the ordinary Copy and assignment rules. Only `Raw.initialize` (…)
initializes Uninitialized raw storage; a `noinit` array (…) is already Initialized. …" Proposed (the last two clauses
unchanged):

> Reads and writes follow the ordinary Copy and assignment rules; a whole-value read, replacement, Take or borrow needs
> a complete value ([§15.1.2](15-ownership-and-lifetime-analysis.md#1512-construction-and-completeness)), because no
> state records which parts to skip. Only `Raw.initialize` ([§5.6](#56-raw-storage-operations)), relocation and a
> function whose contract states construction, such as a foreign function
> ([§22.6](22-core-execution-and-foreign-functions.md#226-foreign-function-imports)), initialize Places through a raw
> pointer, each a Place that holds no value and none of whose bytes an Initialized Place other than its ancestors
> holds; a `noinit` array (…) is already Initialized. A raw pointer supplies no Loan, exclusivity or completeness proof.

**N6** (after 05:36), three new paragraphs:

> **Designation.** Behavior is undefined from each operation for which no single choice of the Places that raw Places
> designate, and of the relocations below, lets that operation and every operation that happens before it
> ([§22.3.7](22-core-execution-and-foreign-functions.md#2237-data-races)), safe or unsafe, meet its rules without a
> data race among them on the Places so chosen. The choice never changes what executes; it only decides whether the
> conditions hold.
>
> **Type-punned access.** A read, write or borrow of a Copy Type of positive size may instead designate a Place of its
> Type formed over bytes that Initialized Places hold, none of them in a part that holds no value; a read needs those
> bytes to represent a valid value of its Type.
>
> **Relocation.** A completed copy of every byte of a range into raw storage, also by a foreign function, may relocate
> values whose Places lie within the source range and the source pointer's provenance: each, with its components, then
> occupies the Place at the same offset in the destination, and its source Place holds no value.

### §5.2.1–§5.2.3

**N7** (05:42, condition 1). "The accessed range lies within the pointer's provenance: for `P@raw`, the storage of `P`
(…)" becomes "What the access acts on lies within the pointer's provenance: for `P@raw`, `P` and its parts (…)".
Condition 2 is unchanged.

**N8** (05:45, condition 4). "conflicts with no live Loan of the storage, except" becomes "conflicts with no live Loan
of a Place overlapping what it acts on, except".

**N9** (05:47, Local variables). "the storage stays live and Uninitialized, and a later initialization (…) fills the
same storage." becomes "the storage stays live, the Places of those contents hold no value, and a later initialization
(…) fills the same Places."

**N10** (05:63). "including another raw pointer, accesses the storage." becomes "including another raw pointer, accesses
a Place overlapping the borrowed one."

**N11** (05:67). "`(*p)@move` takes the value and leaves the storage Uninitialized. The storage stays live;
`Raw.initialize` may fill it again." becomes "`(*p)@move` takes the value and leaves the designated Place holding no
value; the storage stays live, and `Raw.initialize` may fill it again."

### §5.6 Raw storage operations

**N12** (05:126). "allocates uninitialized storage for `count` elements." becomes "allocates raw storage for `count`
elements, holding no value."; "with zero-length provenance;" becomes "with zero-length provenance of its own;".

**N13** (05:127). "Its elements must already be destroyed or taken." becomes:

> Every value placed in it must already be destroyed, taken or relocated, whole or, where
> [§16.3.2](16-scope-exit-and-destruction.md#1632-component-destruction) permits, by its remaining components; Copy
> values and components need neither.

**N14** (05:128). Proposed:

> **`initialize`** moves `value` into the Place of `T` that `*storage` designates or, in raw storage, into a new Place
> of `T` there ([§5.2](#52-raw-places)); the storage is live, aligned and writable.

**N15** (05:129). "and the `length` elements lie contiguously within `storage`'s provenance, each holding" becomes "and
`storage[i]` designates, for each `i` below `length`, a distinct Place within `storage`'s provenance holding".

### Other chapters

**N16** (15:86, §15.1.5). "offer Take without state tracking or a completeness check ([§5.2.3](…))" becomes "offer Take
without state tracking; completeness is the author's obligation
([§5.2](05-raw-pointers-and-unsafe-memory.md#52-raw-places), [§5.2.3](…))".

**N17** (15:487, §15.6.2). "Two Places overlap when an operation on one may affect the other. Non-overlap is proven only
by these rules:" becomes "Places overlap as [§3.4](03-types-and-values.md#34-places-and-storage) defines; non-overlap
is proven only by these rules:".

**N18** (15:515, §15.6.3). "and never lending a child twice is the unsafe obligation" becomes "and never lending
overlapping Places to two live children is the unsafe obligation".

**N19** (22:318, §22.3.7). "when they touch overlapping Storage," becomes "when they touch overlapping Places
([§3.4](03-types-and-values.md#34-places-and-storage)) or the same implementation state,".

**N20** (Appendix E). "Conflicting access": "overlapping Storage" becomes "overlapping Places or the same implementation
state"; "Place / Storage": "a region that holds values." becomes "a region that holds values; raw storage declares no
Place for them."; "Raw pointer / raw Place": "`*p`, `p[n]` or a part of one." becomes "`*p`, `p[n]` or a part of one,
designating a Place at its address."; new row before "Override":
"| Overlap | Places that are identical, of which one is part of the other, or that share a byte. | [§3.4](…) |".

### IMPL

**N21** (IMPL 21:15, §21.1.1). "Distinct zero-sized Places may share an address." becomes "Distinct zero-sized Places
may share an address; a raw Place designates a Place, not an address ([§5.2](…))."

**N22** (IMPL 21:159, §21.4.3). "A zero-byte `Raw.allocate` does not allocate." becomes "A zero-byte `Raw.allocate` does
not allocate; its result, like the element storage of an `Array` with zero-size elements, is distinct raw storage with
its own provenance even where addresses coincide ([§5.2](…))."

### LIBRARY and library comments

**N23** ([LIBRARY](../../docs/LIBRARY.md) Raw table, 509-512). `allocate`: "Allocates raw storage for `count` elements,
holding no value"; `release`: append "Every non-Copy value placed in it must already be destroyed, taken or
relocated."; `initialize`: "Moves `value` into the Place of `T` that `storage` designates or, in raw storage, a new
Place there; the Place holds no value and shares no byte with an Initialized Place other than its ancestors (SPEC §5.2,
§5.6). Destroys nothing."; `slice`: "A positive length requires a nonnull, aligned `storage` whose `storage[i]`
designate `length` distinct initialized, valid elements …".

**N24** (`src/Kimi/Library/RawOperations.kimi` doc comments, within 120 columns): the same four statements; the
release precondition moves before its postcondition, and the slice comment, 125 columns today, is rewrapped.
`src/Kimi/Library/StorageOperations.kimi:33-34` (`transferBytes`): "no responsibility moves and no user code runs."
becomes "it may relocate the values within the source range (SPEC 5.2) and runs no user code." No code changes.

### SETTLED and PLAN

**N25** ([SETTLED](../../docs/SETTLED.md)), new entry:

> ## Zero-size raw values told apart by stride, pointer position, `Raw.write`, count or a producer list
>
> - **Problem:** Distinct zero-size values share an address (§4.1), so raw-storage state kept per address cannot tell
>   them apart: initialization, Take, borrowing, `Raw.slice` and release cannot say which value they act on.
> - **Example:** `let p = Raw.allocate<()>(2)`, `Raw.initialize(p, ())`, `Raw.initialize(p + 1, ())`; `p + 1 == p`,
>   so per-address state already sees a value.
> - **Proposed fix:** Give zero-size Types a positive stride; carry a logical position in every raw pointer; replace
>   `Raw.initialize` with a `Raw.write` that overwrites without destroying; count zero-size values per address; or name
>   "raw elements" by a list of storage producers.
> - **Why not applied:** A positive stride changes §4.1 and the cost of zero-byte allocation. Pointer positions add
>   state to arithmetic, casts, projection and foreign contracts. `Raw.write` still leaves Take, borrowing and
>   `Raw.slice` without a target and adds a second way to abandon a value (Principle 1). Counting restates the
>   capability and Loan rules per class and exempts zero size from provenance. A producer list misses storage that
>   contracts provide and values that relocation moves. Raw storage is Storage instead: a placed value occupies a Place,
>   and a raw Place designates one (§3.4, §5.2).

(The entry's example is a `kimi` code block in the integrated text.)

**N26** ([PLAN](../../docs/dev/PLAN.md) §7), one table line after G87 (198 lines in total), wrapped here:

> | RZ | Raw storage as Storage (SPEC §3.4, §5.2, 2026-10-15) leaves five follow-ups: U3, borrow authority for raw
> Places (§5.2.1 condition 3 names no borrow); O1, `GenericPointerEmissionTest.ConcreteReadsPreserveCopyMoveAndCleanup`
> takes unconstructed values from zeroed memory (§15.1.2, §22.6); O2, `_ = (*p)@move` destroys in place (§5.2.3) while
> §14.2.4 transfers and then cleans up; O3, `DictionaryStorage.appendSlot` assigns `Links` Fields in fresh slots where
> no `Links` value was placed (§5.2), so it must place a `Links` value in each fresh slot, and `compact` then acts on
> the relocated `Links`; O4, type-punned whole reads of Copy Types that have constructors may forge values
> (§5.2, §15.1.2). Evidence: `artifacts/handoff/raw-zst-20261015/` (ignored by Git). |
> Specification and Library, unscheduled |

## 5. What becomes unnecessary

The one-past-the-end ban and the per-storage state sentences of SPEC 5 (D1-D4); a separate completeness unit for raw
whole-value access (folded into N5); producer-named raw storage; separate notions of storage for overlap, data races and
relocation. No runtime state, pointer component, IR form, API or library code is added.

## 6. Case verdicts

| Program | Verdict |
| --- | --- |
| `Buffer<T>` of SPEC 5.6 for every `T`, including zero-size Non-Copy `T` with an observable drop | Valid; one drop per pushed value |
| Two pushes of `Buffer<()>` | Valid: two Places at one address |
| `tryGetPairUniq` over zero-size elements | Valid: two distinct Places |
| `(*p).1@move` on a raw `(i64, Token)` (D5) | Valid: the component Place |
| `Dictionary<K, V>` growth and compaction with zero-size `K` or `V` (D6) | Valid: relocation moves the Places at the range end |
| `raw/u8` reads of initialized integer bytes; two `uniq/u8` borrows of different bytes of one `u64` | Valid: formed punned Places that do not overlap |
| A read of bytes moved out of a partially taken value | Undefined: no formed Place over a part that holds no value |
| `Raw.initialize` over bytes that another Initialized Place holds, including a stale Place refilled under a new value | Undefined: the placement rule |
| Read before any placement; forging a Non-Copy, Loan-bearing or `Never` zero-size value | Undefined: no Place holds a value |
| `x[0]@raw` reaching `x[1]` at one address | Undefined: provenance covers `x[0]` and its parts |
| A second zero-size placement in an allocation, then release with it still held | Undefined at release (N13) |
| Whole-value raw read, replacement or Take of a partially moved value | Undefined (N5) |

## 7. Sites and search

Search terms over `docs/spec`, `docs/impl`, `docs/*.md`, `docs/examples` and `src/Kimi/Library`: "raw storage", `Raw\.`,
"the storage", "storage holding", "one-past", "overlapping Storage", "Uninitialized", "zero-siz", "zero-byte",
"transferBytes", "completeness check", "lending a child", "logical element", "overlap". Sites: N1-N26. Checked and
unchanged: §4.1, §4.3.4 (already covers raw pointers), §4.8.5 (already logical), §5.3-§5.5 (integer addresses keep the
implementation provenance guarantee), the §5.6 Buffer example (now valid for every `T`), §11.2.2, §13.7.1 (N5 states the
raw boundary), §14.2.4 (O2), §16.3.2, §22.6 (its construction is a placement), IMPL §21.4.3 apart from N22, STATUS:277,
GUIDE, STYLE, the Library README, and the comments in Array.kimi, Storage.kimi (:202 already says "logical positions,
not pointer equality"), Raw.kimi and Dictionary.kimi. No test asserts the replaced comments.

## 8. Designs compared

**History.** The first detailed design (owner designation of logical elements) failed review: validity depended on a
stated protocol, zero-size provenance was inconsistent, and unrelated changes were bundled. A count model was rejected:
it restates the Loan and capability rules per class and exempts zero size from provenance. A revision with "raw
elements" kept eight blocking findings, among them missing components, a producer allowlist and unmoved values under
relocation. After four revisions of one rule, the owner chose this representation.

**P1 and P2.** P1 was written by the planning session; P2 by a designer who did not read P1. Both reach the same
representation and rules (§1). Their differences:

| Point | P1 | P2 | Chosen |
| --- | --- | --- | --- |
| Raw storage | by source: `Raw.allocate` or a producing contract | by property: declares no Place | P2 (no producer list) |
| Overlap owner | a sentence in §15.6.2 | §3.4, the owner of Place | P2 |
| Positive-size access without a Place | any Type may designate bare bytes | Copy Types act on Initialized Places | P2, then reformed (M2, §9) |
| Relocation | internal contract: positive-size Places wholly in range; `placeEntry` places an entry record | SPEC rule, zero-size Places at the range end included | P2 |
| Release | non-Copy values taken or destroyed | every placed value | P1 (M1) |
| Completeness | read, replacement, Take | adds borrow | P2 |

**Decisive point.** P1's relocation rests on `placeEntry` placing one entry record whose parts are the links, key and
value. `DictionaryStorage.appendSlot` (:162-185) instead assigns the `Links` Fields of a fresh slot before `placeEntry`
places the key and the value separately (BodyLowering.Dictionaries.cs:156), so no such record exists, and P1's rule
would leave the zero-size value at `slot + stride` unmoved. P2's rule relocates it by the same choice as designation.
SPEC correctness, a constraint, selects P2's relocation; coherence selects P2 elsewhere.

**Changes to P2.** M1 release exempts Copy values and components; M2 type punning covers borrows (then reformed in
§9); M3 overlap includes identical Places and states the zero-size case; M4 designation states the single choice; M5
"a raw Place designates one Place of its Type" without the undefined term "declared Place".

## 9. Review

Two reviewers who wrote neither design nor the patch (adversarial semantics; coverage and process) both returned
**approve with fixes**. They confirmed behavior independence on partial values, components against standalone values,
two zero-size Fields and compaction boundaries; the consistency of §15.6.2's static table; that user byte loops cannot
relocate; that all added links resolve; and the comparison facts against the code.

Blocking findings, all applied: placement lacked byte exclusion (a stale Place could be refilled under a new value;
now one placement rule in N5); type punning acted on whole Places, defining reads of moved-out bytes and making two
`u8` borrows of one `u64` overlap (now formed Places, N6); release made relocated values undefined (N13); "the first
operation in happens-before order" was ill-defined for a partial order (N6); "place values through a raw pointer"
contradicted replacement (N5 says "initialize Places"); `Raw.slice` ignored punning (resolved by formed Places); LIBRARY
and the library comment dropped `initialize`'s new-Place branch (N23, N24); library comments exceeded 120 columns
(N24).

Non-blocking findings applied: Local variables and Take wording without "Uninitialized" for Moved Places (N9, N11);
§15.6.3 overlapping Places (N18); zero-byte results with their own provenance (N12, N22); relocation bounded by
provenance and including foreign copies (N6); "such as a byte transfer" removed for SPEC self-containment; contract
construction named (N5); reference counts in §22.3.7 (N19, N20); `storage[i]` and "a raw Place designates" wording
(N15, N21); PLAN O3 and O4 (N26); SETTLED title and example (N25). Not applied: the Japanese editions (§1).

## 10. Size

Measured with `git diff --no-index --word-diff=porcelain` on scratch copies, links counted:

| Scope | Added | Removed | Net |
| --- | --- | --- | --- |
| SPEC and IMPL | 594 | 82 | +512 words |
| SETTLED, LIBRARY, PLAN, library comments | 468 | 37 | +431 words |

P1 measured +333 net SPEC words; the selected change is larger for the relocation, type-punning and placement
sentences that P1 lacked or stated incorrectly. The first detailed design estimated 500-1000 words of SPEC text.

## 11. Performance and verification on integration

No compiler, IR, runtime or library code change: native O2 code, allocation counts and compile time are unchanged, and
nothing needs measuring. On integration, SPEC, IMPL, LIBRARY, SETTLED, PLAN and Appendix E are documentation (diff and
reference review); the comment edits in `RawOperations.kimi` and `StorageOperations.kimi` fall under the Kimi library
row (selected checks with `KimiLibraryShapeTest` and `ArchitectureRulesTest`, then a Session). Record the intake in
`draft/INTEGRATED.md`. The commit states the root cause (§2), the concept changed (§3), the sites (§7), the deleted text
(§5), the rejected options (N25) and the unverified points (§12), and how the Principles are preserved: one canonical
form (the safe Place concept and one placement rule, no second write operation); local reasoning (an access is judged
by the state, capability and Loans of the Place it designates, and every valid choice behaves alike); explicit
semantics (completeness, placement, relocation and type punning are stated rules, not inferred from addresses).

## 12. Open issues and unverified points

- **U3** borrow authority for raw Places; **O1** `GenericPointerEmissionTest` takes unconstructed values; **O2** discard
  in place against §14.2.4; **O3** Dictionary link writes without placement (a library defect under the current SPEC
  too); **O4** type-punned whole reads of Copy Types that have constructors (H). All in PLAN RZ (N26).
- The behavior-independence argument (§3) has no formal proof.
- Data races among raw accesses across workers are tied to the chosen Places (N6) (H); Appendix D defers raw pointers in
  concurrent execution.
- Whether a user-written byte copy is a relocation is settled by the wording, not by an example: its destination Places
  are Initialized, so it is not.
- The Dictionary layout was read from source, not executed.
- Not re-reviewed: N22 (IMPL §21.4.3), the "same implementation state" clause of N19 and N20, and the N23 wording of
  `initialize`.
- StorageOperations.kimi:36 (`placeEntry`, 121 columns) exceeds the style limit already and is not touched.

**Evidence** (ignored by Git): `artifacts/handoff/raw-zst-20261015/`: the earlier designs and reviews, `design-p1.txt`,
`design-p2.txt`, `comparison-p.txt`, `review-p.txt`, and `proposal.patch`, which applies the normative changes of §4 to
`b32f93ef`.
